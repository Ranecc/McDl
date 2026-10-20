using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>
    /// Resolves versions published in the official version manifest, including the historical
    /// shapes of the version document (natives map, embedded libraries, legacy assets).
    /// </summary>
    public sealed class VanillaSource : IInstallSource
    {
        public string Kind => "vanilla";

        /// <summary>Downloads and parses the version manifest; shared by the CLI and the UI.</summary>
        public static async Task<VersionManifest> FetchManifestAsync(DownloadEngine engine, CancellationToken ct)
        {
            var text = await engine.GetTextAsync(VanillaResolver.ManifestUrl, SourceKind.Meta, ct).ConfigureAwait(false);
            var manifest = Json.Parse<VersionManifest>(text);
            if (manifest?.Versions == null || manifest.Versions.Count == 0)
                throw new InvalidDataException("version manifest is empty or unreadable");
            return manifest;
        }

        public async Task<InstallPlan> ResolveAsync(InstallContext context, InstallRequest request, CancellationToken ct)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (request == null) throw new ArgumentNullException(nameof(request));

            var root = request.ResolveRoot();
            var spec = string.IsNullOrWhiteSpace(request.Version) ? "latest" : request.Version;

            var manifest = await FetchManifestAsync(context.Engine, ct).ConfigureAwait(false);
            var entry = PickVersion(manifest, spec);
            if (entry == null)
                throw new InvalidOperationException("version not found in manifest: " + spec);

            return await ResolveAsync(context, root, entry, request.IncludeAssets, ct).ConfigureAwait(false);
        }

        /// <summary>Resolves a concrete manifest entry; also used by other sources that embed vanilla.</summary>
        public async Task<InstallPlan> ResolveAsync(InstallContext context, string root, VersionEntry entry,
            bool includeAssets, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("root is required");

            var platform = context.Platform;
            var detailText = await context.Engine.GetTextAsync(entry.Url, SourceKind.Meta, ct).ConfigureAwait(false);
            var detail = Json.Parse<VersionDetail>(detailText);
            if (detail == null) throw new InvalidDataException("version document is unreadable: " + entry.Url);
            if (string.IsNullOrWhiteSpace(detail.Id)) detail.Id = entry.Id;

            var versionDir = Path.Combine(root, "versions", detail.Id);
            Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, detail.Id + ".json"), detailText ?? string.Empty);

            AssetIndex assetIndex = null;
            var assetIndexId = detail.Assets ?? "legacy";
            if (includeAssets && detail.AssetIndex?.Url != null)
            {
                var indexText = await context.Engine.GetTextAsync(detail.AssetIndex.Url, SourceKind.Meta, ct)
                    .ConfigureAwait(false);
                assetIndex = Json.Parse<AssetIndex>(indexText);

                var indexId = detail.AssetIndex.Id ?? assetIndexId;
                var indexPath = Path.Combine(root, "assets", "indexes", indexId + ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(indexPath) ?? root);
                File.WriteAllText(indexPath, indexText ?? string.Empty);
                assetIndexId = indexId;
            }

            var natives = new List<NativesExtraction>();
            var plan = new InstallPlan
            {
                Root = root,
                VersionId = detail.Id,
                Label = detail.Id,
                AssetIndexId = assetIndexId,
                Detail = detail,
                Assets = assetIndex,
                VirtualAssets = assetIndex != null && assetIndex.Virtual,
                MapToResources = assetIndex != null && assetIndex.MapToResources
            };

            plan.Files.AddRange(VanillaResolver.ClientFiles(detail, root));
            plan.Files.AddRange(VanillaResolver.LibraryFiles(detail, root, platform, natives));
            if (assetIndex != null) plan.Files.AddRange(VanillaResolver.AssetFiles(assetIndex, root));
            plan.Natives.AddRange(natives);

            return plan;
        }

        /// <summary>Exact id wins over the "latest" aliases; null when nothing matches.</summary>
        public static VersionEntry PickVersion(VersionManifest manifest, string spec)
        {
            if (manifest?.Versions == null) return null;

            if (string.Equals(spec, "latest", StringComparison.OrdinalIgnoreCase))
                return manifest.Versions.Find(v => v.Id == manifest.Latest?.Release) ?? manifest.Versions[0];

            if (string.Equals(spec, "latest-snapshot", StringComparison.OrdinalIgnoreCase))
                return manifest.Versions.Find(v => v.Id == manifest.Latest?.Snapshot) ?? manifest.Versions[0];

            return manifest.Versions.Find(v => string.Equals(v.Id, spec, StringComparison.OrdinalIgnoreCase));
        }
    }
}
