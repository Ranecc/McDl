using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>一个待落盘文件：从哪来、到哪去、怎么校验。Sha1 为空表示不校验。</summary>
    public sealed class FileTask
    {
        public string Label { get; set; }
        public string Url { get; set; }
        public string TargetPath { get; set; }
        public string Sha1 { get; set; }
        public long Size { get; set; }
        public SourceKind Kind { get; set; }
        public bool Optional { get; set; }
    }

    /// <summary>一次传输的快照，进度回调和最终结果共用。</summary>
    public sealed class TransferStats
    {
        public int TotalFiles { get; set; }
        public int DoneFiles { get; set; }
        public int SkippedFiles { get; set; }
        public int FailedFiles { get; set; }
        public long TotalBytes { get; set; }
        public long DoneBytes { get; set; }
        public double BytesPerSecond { get; set; }
        public string CurrentFile { get; set; }
        public string LastError { get; set; }

        /// <summary>没有字节数（老版本或空计划）时退回按文件数算。</summary>
        public double Percent
        {
            get
            {
                if (TotalBytes <= 0) return TotalFiles == 0 ? 100 : (DoneFiles + SkippedFiles) * 100.0 / TotalFiles;
                var pct = DoneBytes * 100.0 / TotalBytes;
                return pct > 100 ? 100 : pct;
            }
        }
    }

    /// <summary>
    /// 并发下载器：固定线程数、.part 断点续传、SHA1 校验、镜像回退。
    /// 单个文件失败不会中断其它文件，最后统一抛 IOException；调用方再看 Failures。
    /// </summary>
    public sealed class DownloadEngine : IDisposable
    {
        public const string DefaultUserAgent = "mcdl/0.1 (+https://github.com/)";

        private readonly HttpClient _http;
        private readonly MirrorRouter _router;
        private readonly int _parallelism;
        private readonly List<string> _failures = new List<string>();

        private long _doneBytes;
        private long _doneFiles;
        private long _skippedFiles;
        private long _failedFiles;
        private long _totalBytes;
        private int _totalFiles;
        private string _current;
        private string _lastError;
        private long _lastReportTick;
        private IProgress<TransferStats> _progress;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public DownloadEngine(MirrorRouter router, int parallelism = 8, string userAgent = null)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = Math.Max(64, parallelism * 4);

            _router = router ?? MirrorRouter.CreateDefault();
            _parallelism = Math.Max(1, parallelism);

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };

            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent ?? DefaultUserAgent);
        }

        /// <summary>本次运行失败的文件及错误，重跑前不会自动清空。</summary>
        public IReadOnlyList<string> Failures => _failures;

        /// <summary>下载一段文本（元数据）；按候选镜像依次重试，全部失败才抛。</summary>
        public async Task<string> GetTextAsync(string url, SourceKind kind, CancellationToken ct)
        {
            var errors = new List<string>();
            foreach (var candidate in _router.Candidates(url, kind))
            {
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using (var response = await _http.GetAsync(candidate.Url, HttpCompletionOption.ResponseContentRead, ct)
                                   .ConfigureAwait(false))
                        {
                            response.EnsureSuccessStatusCode();
                            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            _router.NoteSuccess(candidate.Profile, kind);
                            return text;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _router.NoteFailure(candidate.Profile, kind);
                        errors.Add(candidate.Url + " -> " + ex.Message);
                        await BackoffAsync(attempt, ct).ConfigureAwait(false);
                    }
                }
            }

            throw new HttpRequestException("all sources failed for " + url + " :: " + string.Join(" | ", errors));
        }

        /// <summary>跑完整个文件列表。TargetPath 相同只下一次，方便来源把重复项直接塞进来。</summary>
        public async Task<TransferStats> RunAsync(IEnumerable<FileTask> tasks, IProgress<TransferStats> progress,
            CancellationToken ct)
        {
            _progress = progress;

            var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new ConcurrentQueue<FileTask>();
            foreach (var task in tasks)
            {
                if (task == null || string.IsNullOrEmpty(task.TargetPath) || string.IsNullOrEmpty(task.Url)) continue;
                if (!dedup.Add(task.TargetPath)) continue;
                queue.Enqueue(task);
                Interlocked.Increment(ref _totalFiles);
                Interlocked.Add(ref _totalBytes, task.Size);
            }

            var workers = new Task[_parallelism];
            for (var i = 0; i < workers.Length; i++)
                workers[i] = Task.Run(() => WorkerAsync(queue, ct), ct);

            await Task.WhenAll(workers).ConfigureAwait(false);
            Report(true);

            var stats = Snapshot();
            if (_failures.Count > 0)
                throw new IOException(_failures.Count + " file(s) failed, first: " + _failures[0]);
            return stats;
        }

        private async Task WorkerAsync(ConcurrentQueue<FileTask> queue, CancellationToken ct)
        {
            FileTask task;
            while (queue.TryDequeue(out task))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await FetchOneAsync(task, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _failedFiles);
                    lock (_failures) _failures.Add(task.TargetPath + " :: " + ex.Message);
                }
            }
        }

        private async Task FetchOneAsync(FileTask task, CancellationToken ct)
        {
            _current = task.Label ?? Path.GetFileName(task.TargetPath);

            // 已存在且哈希对得上就算完成；这是“重复点安装会自动跳过”的实现。
            if (File.Exists(task.TargetPath) && HashMatches(task.TargetPath, task.Sha1))
            {
                Interlocked.Increment(ref _skippedFiles);
                if (task.Size > 0) Interlocked.Add(ref _doneBytes, task.Size);
                Report(false);
                return;
            }

            var dir = Path.GetDirectoryName(task.TargetPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var partPath = task.TargetPath + ".part";
            var errors = new List<string>();

            foreach (var candidate in _router.Candidates(task.Url, task.Kind))
            {
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        await DownloadToFileAsync(candidate.Url, partPath, task, ct).ConfigureAwait(false);

                        if (!HashMatches(partPath, task.Sha1))
                            throw new InvalidDataException("sha1 mismatch after transfer");

                        if (File.Exists(task.TargetPath)) File.Delete(task.TargetPath);
                        File.Move(partPath, task.TargetPath);

                        _router.NoteSuccess(candidate.Profile, task.Kind);
                        Interlocked.Increment(ref _doneFiles);
                        Report(false);
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _router.NoteFailure(candidate.Profile, task.Kind);
                        _lastError = ex.Message;
                        errors.Add(candidate.Url + " -> " + ex.Message);
                        await BackoffAsync(attempt, ct).ConfigureAwait(false);
                    }
                }
            }

            throw new IOException(string.Join(" | ", errors));
        }

        private async Task DownloadToFileAsync(string url, string partPath, FileTask task, CancellationToken ct)
        {
            // .part 比目标还大说明上次写坏了（或换过版本），丢掉重下。
            long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            if (task.Size > 0 && existing > task.Size)
            {
                TryDelete(partPath);
                existing = 0;
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);

                using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                           .ConfigureAwait(false))
                {
                    // 服务器不支持 Range 会回 200，此时只能从头写。
                    if (existing > 0 && response.StatusCode != HttpStatusCode.PartialContent) existing = 0;
                    response.EnsureSuccessStatusCode();

                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(partPath, existing > 0 ? FileMode.Append : FileMode.Create,
                               FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                            Interlocked.Add(ref _doneBytes, read);
                            Report(false);
                        }
                    }
                }
            }

            if (task.Size > 0)
            {
                var actual = new FileInfo(partPath).Length;
                if (actual != task.Size)
                    throw new InvalidDataException("size mismatch: expected " + task.Size + " got " + actual);
            }
        }

        /// <summary>SHA1 为空时只判断文件存在；用于没有哈希的上游。</summary>
        public static bool HashMatches(string path, string sha1)
        {
            if (string.IsNullOrEmpty(sha1)) return File.Exists(path);
            if (!File.Exists(path)) return false;

            using (var stream = File.OpenRead(path))
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(stream);
                var sb = new System.Text.StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return string.Equals(sb.ToString(), sha1, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void Report(bool force)
        {
            var handler = _progress;
            if (handler == null) return;

            // 每秒最多 8 次；UI 重绘比网络回调慢，没必要每个 buffer 都 Report。
            var now = Stopwatch.GetTimestamp();
            var last = Interlocked.Read(ref _lastReportTick);
            if (!force && now - last < Stopwatch.Frequency / 8) return;
            if (Interlocked.CompareExchange(ref _lastReportTick, now, last) != last) return;

            handler.Report(Snapshot());
        }

        private TransferStats Snapshot()
        {
            var elapsed = _clock.Elapsed.TotalSeconds;
            var done = Interlocked.Read(ref _doneBytes);

            return new TransferStats
            {
                TotalFiles = Volatile.Read(ref _totalFiles),
                DoneFiles = (int)Interlocked.Read(ref _doneFiles),
                SkippedFiles = (int)Interlocked.Read(ref _skippedFiles),
                FailedFiles = (int)Interlocked.Read(ref _failedFiles),
                TotalBytes = Interlocked.Read(ref _totalBytes),
                DoneBytes = done,
                BytesPerSecond = elapsed > 0.5 ? done / elapsed : 0,
                CurrentFile = _current,
                LastError = _lastError
            };
        }

        private static async Task BackoffAsync(int attempt, CancellationToken ct)
        {
            var delay = TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt - 1));
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // 删不掉就留给下次覆盖；这里不值得让整个下载失败。
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
