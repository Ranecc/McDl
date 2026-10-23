using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>单个文件的校验结论。</summary>
    public enum FileCheckStatus
    {
        /// <summary>文件在，大小和哈希（如果计划里给了）都对得上。</summary>
        Ok,

        /// <summary>文件不在盘上。</summary>
        Missing,

        /// <summary>文件在，但内容与计划不符；也可能是读不出来。</summary>
        Mismatch
    }

    /// <summary>一个文件的实际校验结果，下载引擎没如实落盘时用来给用户看。</summary>
    public sealed class FileCheckResult
    {
        internal FileCheckResult(string label, string path, FileCheckStatus status, long expectedSize, long actualSize)
        {
            Label = label;
            Path = path;
            Status = status;
            ExpectedSize = expectedSize;
            ActualSize = actualSize;
        }

        /// <summary>与下载进度里显示的名字一致，便于对上号。</summary>
        public string Label { get; }

        public string Path { get; }

        public FileCheckStatus Status { get; }

        /// <summary>计划里声明的大小；0 表示计划没给，不做大小判定。</summary>
        public long ExpectedSize { get; }

        public long ActualSize { get; }
    }

    /// <summary>一次全量校验的结果。只描述事实，删不删文件由用户决定。</summary>
    public sealed class VerificationReport
    {
        internal VerificationReport(int checkedCount, int ok, int missing, int mismatched,
            IReadOnlyList<FileCheckResult> problems)
        {
            Checked = checkedCount;
            Ok = ok;
            Missing = missing;
            Mismatched = mismatched;
            Problems = problems;
        }

        /// <summary>实际检查过的文件数（按路径去重后）。</summary>
        public int Checked { get; }

        public int Ok { get; }

        public int Missing { get; }

        public int Mismatched { get; }

        /// <summary>只含缺失与不符的条目；干净时为空列表而不是 null。</summary>
        public IReadOnlyList<FileCheckResult> Problems { get; }

        public bool Clean => Missing == 0 && Mismatched == 0;

        /// <summary>一句中文摘要，供日志和界面直接显示。</summary>
        public string Describe()
        {
            if (Clean) return "校验 " + Checked + " 个文件：全部一致";

            var parts = new List<string>(2);
            if (Missing > 0) parts.Add("缺失 " + Missing + " 个");
            if (Mismatched > 0) parts.Add("哈希不符 " + Mismatched + " 个");
            return "校验 " + Checked + " 个文件：" + string.Join("，", parts);
        }
    }

    /// <summary>
    /// 安装完成后的全量校验：按 <see cref="FileTask"/> 里记录的路径、大小、SHA1 复核磁盘。
    /// 这里只报告，不动文件——删除用户磁盘上的东西必须是用户明确点的动作。
    /// </summary>
    public static class InstallVerifier
    {
        /// <summary>哈希是 CPU 密集的，同时数到网络请求数没意义；4 个线程足够跑满磁盘顺序读。</summary>
        private const int Workers = 4;

        /// <summary>
        /// 校验 <paramref name="files"/> 里的每一个文件，<paramref name="progress"/> 报告已检查的文件数。
        /// 单个文件读失败按 Mismatch 报告，不中断整体校验；只有取消才抛 OperationCanceledException。
        /// </summary>
        public static async Task<VerificationReport> VerifyAsync(IEnumerable<FileTask> files, IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            // 先响应取消：空列表也要让调用方看到取消，而不是拿到一份"全部一致"。
            cancellationToken.ThrowIfCancellationRequested();

            // 与下载引擎一致，按路径去重：来源可能把同一个文件登记两次，重复校验只会重复报告同一个问题。
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new ConcurrentQueue<FileTask>();
            if (files != null)
            {
                foreach (var file in files)
                {
                    if (file == null || string.IsNullOrEmpty(file.TargetPath)) continue;
                    if (!seen.Add(file.TargetPath)) continue;
                    queue.Enqueue(file);
                }
            }

            var problems = new ConcurrentBag<FileCheckResult>();
            var checkedFiles = 0;
            var okFiles = 0;
            var missingFiles = 0;
            var mismatchedFiles = 0;

            var workers = new Task[Workers];
            for (var i = 0; i < workers.Length; i++)
            {
                workers[i] = Task.Run(() =>
                {
                    FileTask file;
                    while (queue.TryDequeue(out file))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var result = Check(file, cancellationToken);

                        switch (result.Status)
                        {
                            case FileCheckStatus.Missing:
                                Interlocked.Increment(ref missingFiles);
                                problems.Add(result);
                                break;
                            case FileCheckStatus.Mismatch:
                                Interlocked.Increment(ref mismatchedFiles);
                                problems.Add(result);
                                break;
                            default:
                                Interlocked.Increment(ref okFiles);
                                break;
                        }

                        var done = Interlocked.Increment(ref checkedFiles);
                        progress?.Report(done);
                    }
                }, cancellationToken);
            }

            await Task.WhenAll(workers).ConfigureAwait(false);

            // 并行完成顺序不稳定，排序后同一份计划总是给出同样的输出，日志和测试才好比对。
            var ordered = new List<FileCheckResult>(problems);
            ordered.Sort((left, right) =>
            {
                var byLabel = string.Compare(left.Label, right.Label, StringComparison.OrdinalIgnoreCase);
                return byLabel != 0
                    ? byLabel
                    : string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
            });

            return new VerificationReport(checkedFiles, okFiles, missingFiles, mismatchedFiles, ordered);
        }

        private static FileCheckResult Check(FileTask file, CancellationToken ct)
        {
            var label = string.IsNullOrEmpty(file.Label) ? Path.GetFileName(file.TargetPath) : file.Label;

            // File.Exists 在路径非法或没有权限时也返回 false，这里一律当成"不在"。
            if (!File.Exists(file.TargetPath))
                return new FileCheckResult(label, file.TargetPath, FileCheckStatus.Missing, file.Size, 0);

            long actualSize;
            try
            {
                actualSize = new FileInfo(file.TargetPath).Length;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new FileCheckResult(label, file.TargetPath, FileCheckStatus.Mismatch, file.Size, 0);
            }

            // 大小不同就没有算哈希的必要：省一次整文件读，也避免在 40MB 的 jar 上白费时间。
            if (file.Size > 0 && actualSize != file.Size)
                return new FileCheckResult(label, file.TargetPath, FileCheckStatus.Mismatch, file.Size, actualSize);

            // 计划没有哈希（部分上游不给），能比的就只有存在性和大小。
            if (string.IsNullOrEmpty(file.Sha1))
                return new FileCheckResult(label, file.TargetPath, FileCheckStatus.Ok, file.Size, actualSize);

            try
            {
                var actualHash = ComputeSha1(file.TargetPath, ct);
                var status = string.Equals(actualHash, file.Sha1, StringComparison.OrdinalIgnoreCase)
                    ? FileCheckStatus.Ok
                    : FileCheckStatus.Mismatch;
                return new FileCheckResult(label, file.TargetPath, status, file.Size, actualSize);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 杀毒软件或独占句柄会让文件读不出来。校验不出来就按不符告警，不能当作通过。
                return new FileCheckResult(label, file.TargetPath, FileCheckStatus.Mismatch, file.Size, actualSize);
            }
        }

        /// <summary>
        /// 流式算 SHA1：client.jar 有 40MB+，整读进内存会让界面卡顿且吃掉大块内存。
        /// 分块算还能在取消时几毫秒内退出，而不是等整个文件读完。
        /// </summary>
        private static string ComputeSha1(string path, CancellationToken ct)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                       FileOptions.SequentialScan))
            using (var sha = SHA1.Create())
            {
                var buffer = new byte[81920];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    sha.TransformBlock(buffer, 0, read, null, 0);
                }

                sha.TransformFinalBlock(buffer, 0, 0);

                var hash = sha.Hash;
                var text = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) text.Append(b.ToString("x2"));
                return text.ToString();
            }
        }
    }
}
