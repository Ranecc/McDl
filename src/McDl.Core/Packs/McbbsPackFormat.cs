using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace McDl.Core
{
    /// <summary>MCBBS 格式：索引是 mcbbs.packmeta，国内老包常见。</summary>
    public sealed class McbbsPackFormat : IPackFormat
    {
        public const string IndexEntryName = "mcbbs.packmeta";

        public string Id => "mcbbs";

        public string DisplayName => "MCBBS (mcbbs.packmeta)";

        public bool CanRead(string archivePath)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(archivePath))
                    return PackArchive.FindEntry(zip, IndexEntryName) != null;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        public PackManifest Read(string archivePath)
        {
            var index = PackJson.ReadIndex<McbbsIndex>(archivePath, IndexEntryName, "MCBBS");

            var minecraftVersion = PackJson.MinecraftVersion(index.Minecraft);
            if (string.IsNullOrWhiteSpace(minecraftVersion)) minecraftVersion = index.TopLevelMinecraftVersion;
            if (string.IsNullOrWhiteSpace(minecraftVersion))
                throw new InvalidDataException(IndexEntryName + " 里没写 minecraft.version，认不出这个包是哪个游戏版本");

            var manifest = new PackManifest
            {
                FormatId = Id,
                Name = index.Name,
                VersionId = index.Version,
                MinecraftVersion = minecraftVersion.Trim(),
                SourcePath = archivePath,
                // 加载器大多挂在 minecraft 下面，也有工具直接写到顶层的，两处都试。
                Loader = PackLoaderIds.Pick(PackJson.ModLoaders(index.Minecraft, "modLoaders") ?? index.ModLoaders)
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

    internal sealed class McbbsIndex
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("version")] public string Version { get; set; }
        [JsonProperty("minecraft")] public JToken Minecraft { get; set; }

        /// <summary>少数导出工具把版本写在顶层，没有就一直是 null。</summary>
        [JsonProperty("minecraftVersion")] public string TopLevelMinecraftVersion { get; set; }

        /// <summary>加载器偶尔也写在顶层，见 Read 里的注释。</summary>
        [JsonProperty("modLoaders")] public List<PackModLoader> ModLoaders { get; set; }

        [JsonProperty("files")] public List<PackIndexFile> Files { get; set; }
    }
}
