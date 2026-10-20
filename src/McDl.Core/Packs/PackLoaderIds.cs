using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace McDl.Core
{
    /// <summary>清单里 modLoaders 数组的一行：CurseForge、MCBBS、HMCL 写法都一样。</summary>
    internal sealed class PackModLoader
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("primary")] public bool Primary { get; set; }
    }

    /// <summary>MCBBS 与 HMCL 的 files[] 条目长得一样，一份模型两处用。</summary>
    internal sealed class PackIndexFile
    {
        [JsonProperty("path")] public string Path { get; set; }

        /// <summary>有的工具写 hash（直接一个 sha1 字符串），有的写 hashes 对象。</summary>
        [JsonProperty("hash")] public string Hash { get; set; }
        [JsonProperty("hashes")] public Dictionary<string, string> Hashes { get; set; }
        [JsonProperty("downloads")] public List<string> Downloads { get; set; }
        [JsonProperty("size")] public long Size { get; set; }

        public PackFileEntry ToEntry(string indexName)
        {
            // 没有 path 就不知道文件该放哪，算清单坏了，不能当无事发生。
            if (string.IsNullOrWhiteSpace(Path))
                throw new InvalidDataException(indexName + " 里有一条 files 记录没写 path，这份清单不完整");

            string sha1 = Hash;
            if (string.IsNullOrWhiteSpace(sha1) && Hashes != null) Hashes.TryGetValue("sha1", out sha1);

            return new PackFileEntry
            {
                Path = Path.Trim(),
                Sha1 = string.IsNullOrWhiteSpace(sha1) ? null : sha1.Trim(),
                Size = Size,
                Downloads = Downloads ?? new List<string>()
            };
        }
    }

    /// <summary>“forge-47.2.0” 这种加载器 id 的拆解，以及从 modLoaders 里挑出主加载器。</summary>
    internal static class PackLoaderIds
    {
        /// <summary>优先 primary:true 的那条，没有就取第一条；一条都没有就是原版包。</summary>
        public static LoaderSpec Pick(IEnumerable<PackModLoader> loaders)
        {
            if (loaders == null) return LoaderSpec.None;

            PackModLoader chosen = null;
            foreach (var loader in loaders)
            {
                if (loader == null || string.IsNullOrWhiteSpace(loader.Id)) continue;
                if (loader.Primary)
                {
                    chosen = loader;
                    break;
                }

                if (chosen == null) chosen = loader;
            }

            return chosen == null ? LoaderSpec.None : Parse(chosen.Id);
        }

        /// <summary>前半截是加载器名、后半截是版本：fabric-0.15.11 → Fabric 0.15.11。</summary>
        public static LoaderSpec Parse(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return LoaderSpec.None;

            var text = id.Trim();
            var dash = text.IndexOf('-');
            if (dash < 0) return LoaderSpec.FromKey(text, null);

            var key = text.Substring(0, dash);
            var version = text.Substring(dash + 1).Trim();
            return LoaderSpec.FromKey(key, version.Length == 0 ? null : version);
        }
    }

    /// <summary>
    /// 这几种格式同一个字段常有多种写法（minecraft 是对象还是字符串、hash 还是 hashes、
    /// 加载器挂在 minecraft 下还是顶层），按候选名字宽松地取，取不到就当没有。
    /// </summary>
    internal static class PackJson
    {
        /// <summary>按顺序试候选字段名，返回第一个非空的文本；写成数字的版本号也当文本读。</summary>
        public static string Text(JToken node, params string[] names)
        {
            if (node == null || node.Type != JTokenType.Object) return null;

            foreach (var name in names)
            {
                var value = Child(node, name);
                if (value == null || value.Type == JTokenType.Null) continue;

                string text = null;
                if (value.Type == JTokenType.String) text = value.Value<string>();
                else if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
                    text = Convert.ToString(value, CultureInfo.InvariantCulture);

                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }

            return null;
        }

        /// <summary>minecraft 节点有的导出工具直接写成 "1.16.5"，有的是个对象，两种都认。</summary>
        public static string MinecraftVersion(JToken node)
        {
            if (node == null) return null;
            if (node.Type == JTokenType.String) return node.ToString().Trim();
            if (node.Type != JTokenType.Object) return null;
            return Text(node, "version", "versionId", "minecraftVersion", "gameVersion");
        }

        /// <summary>找 modLoaders 数组，找不到返回 null。</summary>
        public static List<PackModLoader> ModLoaders(JToken node, params string[] names)
        {
            if (node == null || node.Type != JTokenType.Object) return null;

            foreach (var name in names)
            {
                var value = Child(node, name);
                if (value == null || value.Type != JTokenType.Array) continue;

                try
                {
                    return value.ToObject<List<PackModLoader>>();
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>只读 zip 里那个索引条目，不解压整包。不是本格式、JSON 坏了，都算格式错误。</summary>
        public static T ReadIndex<T>(string archivePath, string entryName, string formatName) where T : class
        {
            using (var zip = ZipFile.OpenRead(archivePath))
            {
                var entry = PackArchive.FindEntry(zip, entryName);
                if (entry == null)
                    throw new InvalidDataException("不是" + formatName + " 整合包：" + Path.GetFileName(archivePath) + " 里没有 " + entryName);

                T index;
                try
                {
                    index = PackArchive.ReadJson<T>(entry);
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException(Path.GetFileName(archivePath) + " 里的 " + entryName + " 不是合法 JSON", ex);
                }

                if (index == null)
                    throw new InvalidDataException(Path.GetFileName(archivePath) + " 里的 " + entryName + " 是空的");
                return index;
            }
        }

        private static JToken Child(JToken node, string name)
        {
            var obj = node as JObject;
            if (obj == null) return null;

            var value = obj[name];
            if (value != null) return value;

            // JObject 的索引器区分大小写，导出工具把字段名大小写写歪了也认。
            var property = obj.Properties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            return property == null ? null : property.Value;
        }
    }
}
