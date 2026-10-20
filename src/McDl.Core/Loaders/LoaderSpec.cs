using System;

namespace McDl.Core
{
    /// <summary>本项目认识的加载器。Quilt 故意留在“识别但不安装”的位置。</summary>
    public enum LoaderKind
    {
        None,
        Fabric,
        Forge,
        NeoForge,
        Quilt,
        Other
    }

    /// <summary>整合包声明的加载器及版本。</summary>
    public sealed class LoaderSpec
    {
        public static readonly LoaderSpec None = new LoaderSpec(LoaderKind.None, null);

        public LoaderSpec(LoaderKind kind, string version)
        {
            Kind = kind;
            Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }

        public LoaderKind Kind { get; }

        /// <summary>包声明的版本；为空表示“用最新稳定版”。</summary>
        public string Version { get; }

        /// <summary>能不能由 McDl 自己装。Quilt 与未知加载器返回 false。</summary>
        public bool IsSupported
        {
            get { return Kind == LoaderKind.None || Kind == LoaderKind.Fabric || Kind == LoaderKind.Forge || Kind == LoaderKind.NeoForge; }
        }

        public string DisplayName
        {
            get
            {
                switch (Kind)
                {
                    case LoaderKind.Fabric: return "Fabric";
                    case LoaderKind.Forge: return "Forge";
                    case LoaderKind.NeoForge: return "NeoForge";
                    case LoaderKind.Quilt: return "Quilt";
                    case LoaderKind.Other: return "未知加载器";
                    default: return "原版";
                }
            }
        }

        /// <summary>装不了的时候给用户看的一句话。</summary>
        public string UnsupportedMessage
        {
            get
            {
                if (Kind == LoaderKind.Quilt)
                    return "Quilt 加载器不在支持范围内，可以先单独安装原版，再用别的工具补加载器。";
                return null;
            }
        }

        /// <summary>把各格式的依赖键（modrinth / curseforge 的写法）翻成本枚举。</summary>
        public static LoaderSpec FromKey(string key, string version)
        {
            if (string.IsNullOrWhiteSpace(key)) return None;

            switch (key.Trim().ToLowerInvariant())
            {
                case "fabric-loader":
                case "fabric": return new LoaderSpec(LoaderKind.Fabric, version);
                case "forge": return new LoaderSpec(LoaderKind.Forge, version);
                case "neoforge": return new LoaderSpec(LoaderKind.NeoForge, version);
                case "quilt-loader":
                case "quilt": return new LoaderSpec(LoaderKind.Quilt, version);
                case "minecraft": return None;
                default: return new LoaderSpec(LoaderKind.Other, version);
            }
        }
    }
}
