using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace McDl.Core
{
    /// <summary>Modrinth 的 .mrpack：索引是 modrinth.index.json。</summary>
    public sealed class ModrinthPackFormat : IPackFormat
    {
        public const string IndexEntryName = "modrinth.index.json";

        public string Id => "modrinth";

        public string DisplayName => "Modrinth (.mrpack)";

        public bool CanRead(string archivePath)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(archivePath))
                    return PackArchive.FindEntry(zip, IndexEntryName) != null;
            }
            catch (InvalidDataException)
            {
                return false; // 不是 zip
            }
        }

        public PackManifest Read(string archivePath)
        {
            ModpackManifest index;
            using (var zip = ZipFile.OpenRead(archivePath))
            {
                var entry = PackArchive.FindEntry(zip, IndexEntryName);
                if (entry == null)
                    throw new InvalidDataException("不是 Modrinth 整合包（缺少 " + IndexEntryName + "）");

                index = PackArchive.ReadJson<ModpackManifest>(entry);
                if (index == null)
                    throw new InvalidDataException("Modrinth 索引读不出来：" + archivePath);
            }

            var manifest = new PackManifest
            {
                FormatId = Id,
                Name = index.Name,
                VersionId = index.VersionId,
                MinecraftVersion = index.MinecraftVersion,
                SourcePath = archivePath,
                Loader = PickLoader(index.Dependencies)
            };

            if (index.Files != null)
            {
                foreach (var file in index.Files)
                {
                    if (file == null) continue;
                    manifest.Files.Add(new PackFileEntry
                    {
                        Path = file.Path,
                        Sha1 = file.Sha1,
                        Size = file.FileSize,
                        Downloads = file.Downloads ?? new List<string>()
                    });
                }
            }

            return manifest;
        }

        /// <summary>一个包只会声明一个加载器；按大家都用的优先级挑一个。</summary>
        internal static LoaderSpec PickLoader(IDictionary<string, string> dependencies)
        {
            if (dependencies == null) return LoaderSpec.None;

            var order = new[] { "fabric-loader", "forge", "neoforge", "quilt-loader" };
            foreach (var key in order)
            {
                string version;
                if (dependencies.TryGetValue(key, out version))
                    return LoaderSpec.FromKey(key, version);
            }

            return LoaderSpec.None;
        }
    }
}
