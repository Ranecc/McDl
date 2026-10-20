using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>
    /// The single entry point of the download library: owns one <see cref="DownloadEngine"/>,
    /// routes a request to the matching <see cref="IInstallSource"/> and runs the resulting plan.
    /// Both front ends (CLI and UI) go through this type, so behaviour cannot drift between them.
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

        /// <summary>Files that failed during the last run, with their first error.</summary>
        public IReadOnlyList<string> Failures => _engine.Failures;

        /// <summary>The official version manifest: every published version plus the latest pointers.</summary>
        public Task<VersionManifest> ListVersionsAsync(CancellationToken ct)
        {
            return VanillaSource.FetchManifestAsync(_engine, ct);
        }

        /// <summary>Resolves what an install would download, without downloading it.</summary>
        public Task<InstallPlan> ResolveAsync(InstallRequest request, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            IInstallSource source;
            if (!_sources.TryGetValue(request.Kind, out source))
                throw new NotSupportedException("unsupported install kind: " + request.Kind);

            var context = new InstallContext(_engine, _platform);
            return source.ResolveAsync(context, request, ct);
        }

        /// <summary>Resolves and downloads, reporting progress through <paramref name="progress"/>.</summary>
        /// <param name="onResolved">
        /// Called once the plan is known and before the first byte is downloaded, so a front end can
        /// show file counts while the transfer runs.
        /// </param>
        public async Task<InstallSummary> InstallAsync(InstallRequest request, IProgress<TransferStats> progress,
            CancellationToken ct, Action<InstallPlan> onResolved = null)
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

            var summary = new InstallSummary { Plan = plan };
            try
            {
                summary.Stats = await plan.MaterializeAsync(_engine, progress, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException ex)
            {
                // Individual files failed; the engine keeps the per-file details.
                summary.Error = ex.Message;
            }
            catch (Exception ex)
            {
                summary.Error = ex.Message;
            }

            if (_engine.Failures.Count > 0) summary.Failures = new List<string>(_engine.Failures);
            else summary.Failures = new List<string>();

            return summary;
        }

        public void Dispose()
        {
            _engine.Dispose();
        }
    }
}
