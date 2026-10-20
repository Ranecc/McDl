using System;

namespace McDl.Core
{
    /// <summary>用户要装的东西的类型。</summary>
    public enum InstallKind
    {
        /// <summary>直接来自官方版本清单的版本。</summary>
        Vanilla,

        /// <summary>整合包；游戏本体按包里的依赖声明安装，模组文件不装。</summary>
        Modpack
    }

    /// <summary>一次安装请求，安装流水线唯一的输入。</summary>
    public sealed class InstallRequest
    {
        public InstallKind Kind { get; set; } = InstallKind.Vanilla;

        /// <summary>目标 .minecraft 目录；相对路径按工作目录算。</summary>
        public string Root { get; set; }

        /// <summary>原版版本号，或 "latest" / "latest-snapshot"。</summary>
        public string Version { get; set; } = "latest";

        /// <summary>整合包路径，格式交给 PackFormats 判断。</summary>
        public string ModpackPath { get; set; }

        /// <summary>是否下载资源索引及 assets 对象。</summary>
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
