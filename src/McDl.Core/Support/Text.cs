using System;
using System.Collections.Generic;

namespace McDl.Core
{
    /// <summary>
    /// 面向用户的文案出口。现在只有中文，但所有新代码都从这里取字符串，
    /// 将来要接多语言只需替换 <see cref="Catalog"/>，不用再翻一遍代码。
    /// 存量文案还没迁移完，见 docs/architecture.md 的 i18n 一节。
    /// </summary>
    public static class Text
    {
        private static ITextCatalog _catalog = new ChineseCatalog();

        public static ITextCatalog Catalog
        {
            get { return _catalog; }
            set { _catalog = value ?? new ChineseCatalog(); }
        }

        public static string Get(string key, params object[] args)
        {
            return Catalog.Format(key, args);
        }
    }

    public interface ITextCatalog
    {
        /// <summary>取一条文案；没有对应条目时返回 key 本身，方便发现漏翻。</summary>
        string Format(string key, params object[] args);
    }

    public sealed class ChineseCatalog : ITextCatalog
    {
        private readonly Dictionary<string, string> _entries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "error.noJava", "这个版本需要 Java {0}，本机没有找到。装一个 JDK/JRE，或者把 java.exe 放进 PATH 再试。" },
            { "error.unsupportedLoader", "暂时不支持 {0} 加载器。" },
            { "error.unknownPack", "无法识别这个整合包（{0}）。" },
            { "log.packDetected", "识别为 {0} 整合包：{1}" },
            { "log.loaderPreparing", "准备安装 {0}" }
        };

        public string Format(string key, params object[] args)
        {
            string template;
            if (key == null || !_entries.TryGetValue(key, out template)) return key;

            try
            {
                return args == null || args.Length == 0 ? template : string.Format(template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }
    }
}
