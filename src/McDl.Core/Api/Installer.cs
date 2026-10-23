using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>
    /// 安装流程的唯一入口：持有 <see cref="DownloadEngine"/>，按 <see cref="InstallKind"/> 选来源并执行计划。
    /// CLI 和 WinForms 都只走这里，避免两边行为分叉。分层和扩展点见 docs/architecture.md。
    /// </summary>
    public sealed class Installer : IDisposable
    {
        private readonly DownloadEngine _engine;
        private readonly PlatformInfo _platform;
        private readonly Dictionary<InstallKind, IInstallSource> _sources;

        public Installer(MirrorRouter router = null, int threads = 8, string userAgent = null)
        {
            _engine = new DownloadEngine(router ?? MirrorRouter.CreateDefault(), threads, userAgent);
            _platform = PlatformInfo.Current();
            _sources = new Dictionary<InstallKind, IInstallSource>
            {
                { InstallKind.Vanilla, new VanillaSource() },
                { InstallKind.Modpack, new ModpackSource() }
            };
        }

        /// <summary>上次安装失败的文件及首个错误。</summary>
        public IReadOnlyList<string> Failures => _engine.Failures;

        /// <summary>前端接日志的地方；null 表示不输出。</summary>
        public Action<string> Log { get; set; }

        /// <summary>官方版本清单（所有版本 + latest 指针）。</summary>
        public Task<VersionManifest> ListVersionsAsync(CancellationToken ct)
        {
            return VanillaSource.FetchManifestAsync(_engine, ct);
        }

        /// <summary>只解析要下载什么，不下载 payload。注意版本 JSON 和 asset index 在解析阶段就会落盘。</summary>
        public Task<InstallPlan> ResolveAsync(InstallRequest request, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            IInstallSource source;
            if (!_sources.TryGetValue(request.Kind, out source))
                throw new NotSupportedException("unsupported install kind: " + request.Kind);

            var context = new InstallContext(_engine, _platform, Log);
            return source.ResolveAsync(context, request, ct);
        }

        /// <summary>解析并下载，进度通过 <paramref name="progress"/> 报告。</summary>
        /// <param name="onResolved">解析完成、下载第一个字节前回调，前端用来提前显示文件数和体积。</param>
        /// <param name="verify">
        /// 落盘后再全量复核一遍，默认开。校验只写日志、不进 <see cref="InstallSummary.Error"/>，
        /// 因为文件被改过应由用户判断怎么处理，不该让安装流程自动删文件或重来。
        /// </param>
        public async Task<InstallSummary> InstallAsync(InstallRequest request, IProgress<TransferStats> progress,
            CancellationToken ct, Action<InstallPlan> onResolved = null, bool verify = true)
        {
            InstallPlan plan;
            try
            {
                plan = await ResolveAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new InstallSummary { Error = ex.Message, Failures = new List<string>() };
            }

            onResolved?.Invoke(plan);

            TransferStats stats = null;
            string error = null;
            try
            {
                stats = await plan.MaterializeAsync(_engine, progress, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单个文件失败；明细在 engine 的 Failures 里。此时磁盘是半成品，校验没有意义，跳过。
                error = ex.Message;
            }

            VerificationReport verification = null;
            if (error == null && verify)
            {
                verification = await InstallVerifier.VerifyAsync(plan.Files, null, ct).ConfigureAwait(false);
                ReportVerification(verification);
            }

            return new InstallSummary
            {
                Plan = plan,
                Stats = stats,
                Error = error,
                Verification = verification,
                Failures = _engine.Failures.Count > 0 ? new List<string>(_engine.Failures) : new List<string>()
            };
        }

        /// <summary>把校验结果写进日志；问题最多列 20 条，避免几千个 assets 刷满界面。</summary>
        private void ReportVerification(VerificationReport report)
        {
            var log = Log;
            if (log == null) return;

            log("校验完成：" + report.Describe());

            const int maxListed = 20;
            var listed = 0;
            foreach (var problem in report.Problems)
            {
                if (listed == maxListed)
                {
                    log("…还有 " + (report.Problems.Count - maxListed) + " 个未列出");
                    break;
                }

                log("校验失败：" + problem.Label + "（" + problem.Path + "）");
                listed++;
            }
        }

        public void Dispose()
        {
            _engine.Dispose();
        }
    }
}
