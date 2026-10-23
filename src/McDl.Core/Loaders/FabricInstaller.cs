using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>Fabric：官方 meta 直接给出启动所需库列表，不需要跑任何本地程序。</summary>
    public sealed class FabricInstaller : ILoaderInstaller
    {
        public LoaderKind Kind => LoaderKind.Fabric;

        public string DisplayName => "Fabric";

        public async Task ApplyAsync(InstallContext context, InstallPlan plan, string gameVersion, LoaderSpec loader,
            string profileId, CancellationToken ct)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var meta = await FabricLoader.FetchAsync(context.Engine, gameVersion, loader?.Version, ct)
                .ConfigureAwait(false);

            await FabricLoader.ApplyAsync(context, plan, meta, profileId, ct).ConfigureAwait(false);
            context.Log?.Invoke("Fabric " + (loader?.Version ?? meta.Loader?.Version) + " 已加入启动配置");
        }
    }
}
