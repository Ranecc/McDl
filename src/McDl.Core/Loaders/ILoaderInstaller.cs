using System;
using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>
    /// 一种加载器的安装方式。每种加载器一个实现，互不干扰；见 docs/loaders.md。
    /// </summary>
    public interface ILoaderInstaller
    {
        LoaderKind Kind { get; }

        string DisplayName { get; }

        /// <summary>
        /// 解析阶段调用：把加载器需要的库、版本配置挂到 plan 上。
        /// 需要等游戏文件到位才能做的工作（比如跑官方 installer），
        /// 请注册到 <see cref="InstallPlan.PostDownload"/>。
        /// </summary>
        Task ApplyAsync(InstallContext context, InstallPlan plan, string gameVersion, LoaderSpec loader,
            string profileId, CancellationToken ct);
    }

    /// <summary>加载器实现的分发点。新增加载器只改这里 + 加一个实现类。</summary>
    public static class LoaderInstallers
    {
        private static readonly ILoaderInstaller[] Instances =
        {
            new FabricInstaller(),
            new ForgeInstaller(LoaderKind.Forge),
            new ForgeInstaller(LoaderKind.NeoForge)
        };

        public static ILoaderInstaller For(LoaderKind kind)
        {
            foreach (var installer in Instances)
                if (installer.Kind == kind) return installer;

            throw new NotSupportedException("暂不支持 " + kind + " 加载器");
        }

        public static bool IsSupported(LoaderKind kind)
        {
            foreach (var installer in Instances)
                if (installer.Kind == kind) return true;
            return kind == LoaderKind.None;
        }
    }
}
