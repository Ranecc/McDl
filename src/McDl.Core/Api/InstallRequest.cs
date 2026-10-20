using System;

namespace McDl.Core
{
    /// <summary>What kind of thing the user asked to install.</summary>
    public enum InstallKind
    {
        /// <summary>A version straight from the official version manifest.</summary>
        Vanilla,

        /// <summary>A modpack archive; the game body comes from the pack's dependency list.</summary>
        Modpack
    }

    /// <summary>A single user request; the only input the install pipeline needs.</summary>
    public sealed class InstallRequest
    {
        public InstallKind Kind { get; set; } = InstallKind.Vanilla;

        /// <summary>Target .minecraft directory (absolute or relative to the process directory).</summary>
        public string Root { get; set; }

        /// <summary>Vanilla version id, or "latest" / "latest-snapshot".</summary>
        public string Version { get; set; } = "latest";

        /// <summary>Local path of a .mrpack / .zip modpack archive (used when <see cref="Kind"/> is Modpack).</summary>
        public string ModpackPath { get; set; }

        /// <summary>Whether the asset index and its objects are downloaded.</summary>
        public bool IncludeAssets { get; set; } = true;

        public InstallRequest Clone()
        {
            return (InstallRequest)MemberwiseClone();
        }

        public string ResolveRoot()
        {
            if (string.IsNullOrWhiteSpace(Root))
                return System.IO.Path.Combine(Environment.CurrentDirectory, ".minecraft");
            return System.IO.Path.GetFullPath(Root);
        }
    }
}
