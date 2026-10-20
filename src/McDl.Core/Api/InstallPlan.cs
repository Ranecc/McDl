using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>Everything resolved for one install: what to download, what to unpack, what to write.</summary>
    /// <remarks>
    /// A plan is a value object produced by an <see cref="IInstallSource"/>; it knows how to materialize
    /// itself, but not how it was resolved, which keeps source selection out of the engine.
    /// </remarks>
    public sealed class InstallPlan
    {
        /// <summary>Target .minecraft directory.</summary>
        public string Root { get; internal set; }

        /// <summary>Id of the version folder that receives the downloaded payload.</summary>
        public string VersionId { get; internal set; }

        /// <summary>Human readable name shown by the UI (version id, modpack name, ...).</summary>
        public string Label { get; internal set; }

        /// <summary>Asset index id used on disk (legacy versions use "legacy").</summary>
        public string AssetIndexId { get; internal set; }

        /// <summary>Parsed version document; null for plans that do not start from vanilla metadata.</summary>
        public VersionDetail Detail { get; internal set; }

        /// <summary>Parsed asset index; null when assets were not requested.</summary>
        public AssetIndex Assets { get; internal set; }

        /// <summary>Every file this plan has to place on disk.</summary>
        public List<FileTask> Files { get; internal set; }

        /// <summary>Natives archives to unpack after the download phase.</summary>
        public List<NativesExtraction> Natives { get; internal set; }

        /// <summary>Old versions reference assets by name instead of hash (assets/virtual/&lt;id&gt;).</summary>
        public bool VirtualAssets { get; internal set; }

        /// <summary>Versions before the asset index existed read a flat "resources" folder.</summary>
        public bool MapToResources { get; internal set; }

        public long TotalBytes => VanillaResolver.TotalBytes(Files);

        public InstallPlan()
        {
            Files = new List<FileTask>();
            Natives = new List<NativesExtraction>();
        }

        /// <summary>Downloads every file, then applies the post-steps that depend on the payload.</summary>
        public async Task<TransferStats> MaterializeAsync(DownloadEngine engine, IProgress<TransferStats> progress,
            CancellationToken ct)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (Files == null || Files.Count == 0) return new TransferStats();

            var stats = await engine.RunAsync(Files, progress, ct).ConfigureAwait(false);

            NativesUnpacker.ExtractAll(Natives, ct);
            if (VirtualAssets || MapToResources) LinkVirtualAssets(ct);

            return stats;
        }

        /// <summary>Old versions reference assets by name instead of hash; they need a flat layout.</summary>
        private void LinkVirtualAssets(CancellationToken ct)
        {
            if (Assets?.Objects == null) return;

            var objectsRoot = Path.Combine(Root, "assets", "objects");
            var flatRoot = VirtualAssets
                ? Path.Combine(Root, "assets", "virtual", AssetIndexId)
                : Path.Combine(Root, "resources");

            foreach (var pair in Assets.Objects)
            {
                ct.ThrowIfCancellationRequested();

                var asset = pair.Value;
                if (asset?.Hash == null || asset.Hash.Length < 4) continue;

                var source = Path.Combine(objectsRoot, asset.Hash.Substring(0, 2), asset.Hash);
                if (!File.Exists(source)) continue;

                var relative = pair.Key.Replace('/', Path.DirectorySeparatorChar);
                var destination = Path.Combine(flatRoot, relative);

                var dir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(destination)) continue;

                try
                {
                    File.Copy(source, destination);
                }
                catch (IOException)
                {
                    // A duplicate mapping for the same asset is harmless.
                }
            }
        }
    }
}
