using System;

namespace McDl.Core
{
    /// <summary>解析请求时交给 <see cref="IInstallSource"/> 的共享服务。</summary>
    public sealed class InstallContext
    {
        public InstallContext(DownloadEngine engine, PlatformInfo platform)
            : this(engine, platform, null)
        {
        }

        public InstallContext(DownloadEngine engine, PlatformInfo platform, Action<string> log)
        {
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
            Platform = platform ?? PlatformInfo.Current();
            Log = log;
        }

        /// <summary>网络访问，带镜像回退；来源不要自己造。</summary>
        public DownloadEngine Engine { get; }

        /// <summary>平台规则，用来筛库。</summary>
        public PlatformInfo Platform { get; }

        /// <summary>给用户看的进度文字，可以为 null。</summary>
        public Action<string> Log { get; }
    }
}
