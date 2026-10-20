using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace McDl.Core
{
    /// <summary>HMCL 自己的整合包格式：索引是 modpack.json。</summary>
    public sealed class HmclPackFormat : IPackFormat
    {
        public const string IndexEntryName = "modpack.json";

        public string Id => "hmcl";

        public string DisplayName => "HMCL (modpack.json)";

        public bool CanRead(string archivePath)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(archivePath))
                {
                    // HMCL 的索引里必须带 manifest.type = hmcl，光有同名文件不算。
                    var entry = PackArchive.FindEntry(zip, IndexEntryName);
                    if (entry == null) return false;

                    var text = PackArchive.ReadText(entry);
                    return text.IndexOf("\"hmcl\"", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        public PackManifest Read(string archivePath)
        {
            var index = PackJson.ReadIndex<HmclIndex>(archivePath, IndexEntryName, "HMCL");

            // CanRead 只是粗筛（文本里得有 "hmcl"），真正认领前按声明的类型再确认一次。
            if (index.Manifest == null)
                throw new InvalidDataException(IndexEntryName + " 里没有 manifest 节点，不像 HMCL 的整合包");

            var type = index.Manifest.Type;
            if (!string.Equals(type, "hmcl", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(IndexEntryName + " 的 manifest.type 是 \"" + (type ?? "") + "\"，不是 hmcl，这不是 HMCL 整合包");

            var minecraftVersion = PackJson.MinecraftVersion(index.Minecraft);
            if (string.IsNullOrWhiteSpace(minecraftVersion))
                throw new InvalidDataException(IndexEntryName + " 里没写 minecraft.version，认不出这个包是哪个游戏版本");

            var manifest = new PackManifest
            {
                FormatId = Id,
                Name = index.Manifest.Name,
                VersionId = index.Manifest.Version,
                MinecraftVersion = minecraftVersion,
                SourcePath = archivePath,
                Loader = PackLoaderIds.Pick(PackJson.ModLoaders(index.Minecraft, "modLoaders"))
            };

            if (index.Files != null)
            {
                foreach (var file in index.Files)
                {
                    if (file == null) continue;
                    manifest.Files.Add(file.ToEntry(IndexEntryName));
                }
            }

            return manifest;
        }
    }

    internal sealed class HmclIndex
    {
        [JsonProperty("manifest")] public HmclHeader Manifest { get; set; }
        [JsonProperty("minecraft")] public JToken Minecraft { get; set; }
        [JsonProperty("files")] public List<PackIndexFile> Files { get; set; }
    }

    internal sealed class HmclHeader
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("version")] public string Version { get; set; }
    }
}
