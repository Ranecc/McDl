using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace McDl.Core
{
    /// <summary>
    /// CurseForge 与 HMCL 导出的整合包：索引是 manifest.json。
    /// 两者结构一致，差别只在少数可选项，所以共用一个读取器。
    /// </summary>
    public sealed class CurseForgePackFormat : IPackFormat
    {
        public const string IndexEntryName = "manifest.json";

        public string Id => "curseforge";

        public string DisplayName => "CurseForge / HMCL 导出 (manifest.json)";

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
            var index = PackJson.ReadIndex<CurseForgeIndex>(archivePath, IndexEntryName, "CurseForge / HMCL 导出的");

            // 没写游戏版本这个包就没法装，直接说清楚缺什么。
            var minecraftVersion = PackJson.MinecraftVersion(index.Minecraft);
            if (string.IsNullOrWhiteSpace(minecraftVersion))
                throw new InvalidDataException(IndexEntryName + " 里没写 minecraft.version，认不出这个包是哪个游戏版本");

            var manifest = new PackManifest
            {
                FormatId = Id,
                Name = index.Name,
                VersionId = index.Version,
                MinecraftVersion = minecraftVersion,
                SourcePath = archivePath,
                Loader = PackLoaderIds.Pick(PackJson.ModLoaders(index.Minecraft, "modLoaders"))
            };

            if (index.Files != null)
            {
                foreach (var file in index.Files)
                {
                    if (file == null) continue;
                    if (file.ProjectId <= 0 || file.FileId <= 0)
                        throw new InvalidDataException(IndexEntryName + " 里有一条 files 记录缺 projectID/fileID，不知道它对应哪个文件");

                    manifest.Files.Add(new PackFileEntry
                    {
                        // CurseForge 清单只给项目/文件的数字 id，真实文件名要走 API（要 key），
                        // 这版不联网查，所以用能看出出处的占位名 —— 编个像真的文件名反而更误导。
                        Path = "mods/project-" + file.ProjectId + "-file-" + file.FileId + ".jar",
                        Downloads = new List<string>()
                    });
                    // 文件大小清单里没有，留 0；下载链接要 API key，这版空着不下载。
                }
            }

            return manifest;
        }
    }

    internal sealed class CurseForgeIndex
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("version")] public string Version { get; set; }
        [JsonProperty("minecraft")] public JToken Minecraft { get; set; }
        [JsonProperty("files")] public List<CurseForgeFile> Files { get; set; }
    }

    internal sealed class CurseForgeFile
    {
        [JsonProperty("projectID")] public long ProjectId { get; set; }
        [JsonProperty("fileID")] public long FileId { get; set; }
        [JsonProperty("required")] public bool Required { get; set; }
    }
}
