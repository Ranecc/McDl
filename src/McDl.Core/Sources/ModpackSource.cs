using System;
using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>
    /// Installs the game body a modpack needs: the vanilla version named by its dependency list,
    /// plus the Fabric loader libraries when the pack asks for one.
    /// </summary>
    /// <remarks>
    /// Deliberately limited to the game itself ("游戏本体"). Mod files inside the pack are described
    /// by <see cref="ModpackManifest.Files"/> and are not downloaded yet.
    /// </remarks>
    public sealed class ModpackSource : IInstallSource
    {
        public string Kind => "modpack";

        public async Task<InstallPlan> ResolveAsync(InstallContext context, InstallRequest request, CancellationToken ct)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (request == null) throw new ArgumentNullException(nameof(request));

            var archivePath = request.ModpackPath;
            if (string.IsNullOrWhiteSpace(archivePath))
                throw new ArgumentException("modpack path is required");
            if (!System.IO.File.Exists(archivePath))
                throw new System.IO.FileNotFoundException("modpack not found: " + archivePath, archivePath);

            var pack = ModpackArchive.ReadIndex(archivePath);

            var gameVersion = pack.MinecraftVersion;
            if (string.IsNullOrWhiteSpace(gameVersion))
                throw new System.IO.InvalidDataException("modpack does not declare a minecraft version: " + archivePath);

            var unsupported = pack.UnsupportedLoader;
            if (unsupported != null)
                throw new NotSupportedException("暂时只支持 Fabric 整合包，检测到 " + unsupported +
                                                 "。可以先安装 " + gameVersion + " 原版本体。");

            var root = request.ResolveRoot();
            var manifest = await VanillaSource.FetchManifestAsync(context.Engine, ct).ConfigureAwait(false);
            var entry = VanillaSource.PickVersion(manifest, gameVersion);
            if (entry == null)
                throw new InvalidOperationException("整合包要求的 " + gameVersion + " 不在官方版本列表里");

            var vanilla = new VanillaSource();
            var plan = await vanilla.ResolveAsync(context, root, entry, request.IncludeAssets, ct).ConfigureAwait(false);

            var profileId = pack.BuildProfileId();
            plan.Label = pack.DisplayName;

            var loaderVersion = pack.FabricLoaderVersion;
            if (!string.IsNullOrWhiteSpace(loaderVersion))
            {
                var meta = await FabricLoader.FetchAsync(context.Engine, gameVersion, loaderVersion, ct)
                    .ConfigureAwait(false);
                FabricLoader.Apply(plan, meta, profileId);
            }
            else
            {
                // Vanilla pack: still expose a profile so launchers see the pack name.
                var path = System.IO.Path.Combine(root, "versions", profileId, profileId + ".json");
                Json.Save(path, new
                {
                    id = profileId,
                    inheritsFrom = plan.Detail != null ? plan.Detail.Id : gameVersion,
                    type = "release"
                });
            }

            return plan;
        }
    }
}
