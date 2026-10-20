using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace McDl.Core
{
    /// <summary>The "modrinth.index.json" document inside a .mrpack archive.</summary>
    public sealed class ModpackManifest
    {
        [JsonProperty("formatVersion")] public int FormatVersion { get; set; }
        [JsonProperty("game")] public string Game { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("versionId")] public string VersionId { get; set; }
        [JsonProperty("summary")] public string Summary { get; set; }
        [JsonProperty("files")] public List<ModpackFile> Files { get; set; }
        [JsonProperty("dependencies")] public Dictionary<string, string> Dependencies { get; set; }

        /// <summary>Minecraft version the pack is built on; also accepts the legacy "minecraft" key only.</summary>
        public string MinecraftVersion
        {
            get
            {
                string value;
                return Dependencies != null && Dependencies.TryGetValue("minecraft", out value) ? value : null;
            }
        }

        public string FabricLoaderVersion
        {
            get
            {
                string value;
                return Dependencies != null && Dependencies.TryGetValue("fabric-loader", out value) ? value : null;
            }
        }

        /// <summary>Name of a loader this project does not install yet, or null.</summary>
        public string UnsupportedLoader
        {
            get
            {
                if (Dependencies == null) return null;

                string value;
                if (Dependencies.TryGetValue("forge", out value) && !string.IsNullOrEmpty(value)) return "forge " + value;
                if (Dependencies.TryGetValue("neoforge", out value) && !string.IsNullOrEmpty(value)) return "neoforge " + value;
                if (Dependencies.TryGetValue("quilt-loader", out value) && !string.IsNullOrEmpty(value)) return "quilt " + value;
                return null;
            }
        }

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name)) return "modpack";
                return string.IsNullOrWhiteSpace(VersionId) ? Name : Name + " " + VersionId;
            }
        }

        /// <summary>Turns the pack name into a folder safe version id ("微光 1.2" -> "微光-1.2").</summary>
        public string BuildProfileId()
        {
            var baseName = string.IsNullOrWhiteSpace(Name) ? "modpack" : Name.Trim();
            var raw = string.IsNullOrWhiteSpace(VersionId) ? baseName : baseName + "-" + VersionId;
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(raw.Length);
            foreach (var ch in raw)
            {
                if (Array.IndexOf(invalid, ch) >= 0 || ch == ' ' || ch == '/' || ch == '\\') builder.Append('-');
                else builder.Append(ch);
            }

            var id = builder.ToString().Trim('-', '.');
            return id.Length == 0 ? "modpack" : id;
        }
    }

    /// <summary>One entry of the pack's file list; only the game body is installed today, the rest is metadata.</summary>
    public sealed class ModpackFile
    {
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("hashes")] public Dictionary<string, string> Hashes { get; set; }
        [JsonProperty("downloads")] public List<string> Downloads { get; set; }
        [JsonProperty("fileSize")] public long FileSize { get; set; }
        [JsonProperty("env")] public ModpackFileEnvironment Environment { get; set; }

        public string Sha1
        {
            get
            {
                string value;
                return Hashes != null && Hashes.TryGetValue("sha1", out value) ? value : null;
            }
        }
    }

    public sealed class ModpackFileEnvironment
    {
        [JsonProperty("client")] public string Client { get; set; }
        [JsonProperty("server")] public string Server { get; set; }
    }

    /// <summary>Reads the pack index out of a .mrpack archive without unpacking the whole file.</summary>
    public static class ModpackArchive
    {
        public const string IndexEntryName = "modrinth.index.json";

        public static ModpackManifest ReadIndex(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("modpack path is required");
            if (!File.Exists(archivePath)) throw new FileNotFoundException("modpack not found: " + archivePath, archivePath);

            using (var zip = ZipFile.OpenRead(archivePath))
            {
                var entry = zip.GetEntry(IndexEntryName)
                            ?? zip.Entries.FirstOrDefault(e =>
                                e.FullName.EndsWith("/" + IndexEntryName, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                    throw new InvalidDataException("not a modrinth pack (missing " + IndexEntryName + "): " + archivePath);

                using (var stream = entry.Open())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var manifest = Json.Parse<ModpackManifest>(reader.ReadToEnd());
                    if (manifest == null) throw new InvalidDataException("modpack index is unreadable: " + archivePath);
                    return manifest;
                }
            }
        }
    }
}
