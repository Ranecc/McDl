using System;

namespace McDl.Core
{
    /// <summary>Shared services handed to an <see cref="IInstallSource"/> while it resolves a request.</summary>
    public sealed class InstallContext
    {
        public InstallContext(DownloadEngine engine, PlatformInfo platform)
        {
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
            Platform = platform ?? PlatformInfo.Current();
        }

        /// <summary>Network access with mirror failover; sources must not create their own.</summary>
        public DownloadEngine Engine { get; }

        /// <summary>Platform rules used to filter libraries.</summary>
        public PlatformInfo Platform { get; }
    }
}
