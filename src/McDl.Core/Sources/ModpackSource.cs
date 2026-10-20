using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>
    /// 整合包安装：先按格式读出包信息，再装对应的原版本体，最后把加载器挂上去。
    /// 只做游戏本体，包里的模组与配置文件不下载。
    /// </summary>
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
            if (!File.Exists(archivePath))
                throw new FileNotFoundException("modpack not found: " + archivePath, archivePath);

            var pack = PackFormats.Read(archivePath);
            context.Log?.Invoke("识别为 " + pack.FormatId + " 整合包：" + pack.DisplayName);

            var gameVersion = pack.MinecraftVersion;
            if (string.IsNullOrWhiteSpace(gameVersion))
                throw new InvalidDataException("整合包没有写 Minecraft 版本：" + archivePath);

            if (!pack.Loader.IsSupported)
                throw new NotSupportedException(pack.Loader.UnsupportedMessage
                                                ?? ("暂时不支持 " + pack.Loader.DisplayName + " 加载器"));

            var root = request.ResolveRoot();
            var manifest = await VanillaSource.FetchManifestAsync(context.Engine, ct).ConfigureAwait(false);
            var entry = VanillaSource.PickVersion(manifest, gameVersion);
            if (entry == null)
                throw new InvalidOperationException("整合包要求的 " + gameVersion + " 不在官方版本列表里");

            var vanilla = new VanillaSource();
            var plan = await vanilla.ResolveAsync(context, root, entry, request.IncludeAssets, ct).ConfigureAwait(false);

            var profileId = pack.BuildProfileId();
            plan.Label = pack.DisplayName;

            if (pack.Loader.Kind == LoaderKind.None)
            {
                WriteVanillaProfile(plan, root, profileId, gameVersion);
                return plan;
            }

            var installer = LoaderInstallers.For(pack.Loader.Kind);
            context.Log?.Invoke("准备安装 " + installer.DisplayName +
                                (pack.Loader.Version == null ? "" : " " + pack.Loader.Version));
            await installer.ApplyAsync(context, plan, gameVersion, pack.Loader, profileId, ct).ConfigureAwait(false);

            return plan;
        }

        /// <summary>纯原版包也写一个 profile，启动器里能看到包名。</summary>
        private static void WriteVanillaProfile(InstallPlan plan, string root, string profileId, string gameVersion)
        {
            var path = Path.Combine(root, "versions", profileId, profileId + ".json");
            Json.Save(path, new
            {
                id = profileId,
                inheritsFrom = plan.Detail != null ? plan.Detail.Id : gameVersion,
                type = "release"
            });
        }
    }
}
