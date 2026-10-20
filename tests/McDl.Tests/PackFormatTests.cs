using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using McDl.Core;
using Xunit;

namespace McDl.Tests
{
    /// <summary>整合包格式读取：现场造 zip，不依赖网上下载的包。</summary>
    public sealed class PackFormatTests : IDisposable
    {
        private readonly string _root;

        public PackFormatTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mcdl-pack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            // 临时目录可能还被 zip 句柄占着，删不掉就算了，别让清理把测试搞红。
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private string CreatePack(string fileName, string indexEntryName, string indexJson)
        {
            var path = Path.Combine(_root, fileName);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry(indexEntryName);
                using (var stream = entry.Open())
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    writer.Write(indexJson);
            }

            return path;
        }

        private const string CurseForgeJson =
            "{\"manifestType\":\"minecraftModpack\",\"manifestVersion\":1," +
            "\"name\":\"微光生存\",\"version\":\"1.2.3\"," +
            "\"minecraft\":{\"version\":\"1.20.1\",\"modLoaders\":[{\"id\":\"forge-47.2.0\",\"primary\":true}]}," +
            "\"files\":[{\"projectID\":123,\"fileID\":456,\"required\":true}],\"overrides\":\"overrides\"}";

        private const string McbbsJson =
            "{\"name\":\"工业时代\",\"version\":\"2.0\"," +
            "\"minecraft\":{\"version\":\"1.16.5\",\"modLoaders\":[{\"id\":\"forge-36.2.34\",\"primary\":true}]}," +
            "\"files\":[{\"path\":\"mods/ic2.jar\",\"hash\":\"abc123\"," +
            "\"downloads\":[\"https://example.com/ic2.jar\"],\"size\":1024}]}";

        private const string HmclJson =
            "{\"manifest\":{\"type\":\"hmcl\",\"name\":\"Fabric 世界\",\"version\":\"1.0\"}," +
            "\"minecraft\":{\"version\":\"1.20.1\",\"modLoaders\":[{\"id\":\"fabric-0.15.11\",\"primary\":true}]}}";

        [Fact]
        public void CurseForgePackIsReadWithForgeLoader()
        {
            var path = CreatePack("cf.zip", CurseForgePackFormat.IndexEntryName, CurseForgeJson);
            var pack = PackFormats.Read(path);

            Assert.Equal("curseforge", pack.FormatId);
            Assert.Equal("微光生存", pack.Name);
            Assert.Equal("1.2.3", pack.VersionId);
            Assert.Equal("1.20.1", pack.MinecraftVersion);
            Assert.Equal(LoaderKind.Forge, pack.Loader.Kind);
            Assert.Equal("47.2.0", pack.Loader.Version);
            Assert.True(pack.Loader.IsSupported);
            Assert.Equal(path, pack.SourcePath);

            Assert.Single(pack.Files);
            var file = pack.Files[0];
            Assert.Equal("mods/project-123-file-456.jar", file.Path);
            Assert.Equal(0L, file.Size);
            Assert.Empty(file.Downloads);
        }

        [Fact]
        public void CurseForgePackWithoutModLoaderIsVanilla()
        {
            var path = CreatePack("cf-vanilla.zip", CurseForgePackFormat.IndexEntryName,
                "{\"name\":\"原版包\",\"version\":\"1\",\"minecraft\":{\"version\":\"1.19.2\"},\"files\":[]}");

            var pack = PackFormats.Read(path);

            Assert.Equal(LoaderKind.None, pack.Loader.Kind);
            Assert.Same(LoaderSpec.None, pack.Loader);
            Assert.Empty(pack.Files);
        }

        [Fact]
        public void CurseForgePackWithoutMinecraftVersionIsRejected()
        {
            var path = CreatePack("cf-no-version.zip", CurseForgePackFormat.IndexEntryName,
                "{\"name\":\"坏包\",\"version\":\"1\",\"minecraft\":{},\"files\":[]}");

            var error = Assert.Throws<InvalidDataException>(() => new CurseForgePackFormat().Read(path));
            Assert.Contains("minecraft.version", error.Message);
        }

        [Fact]
        public void McbbsPackIsReadWithForgeLoader()
        {
            var path = CreatePack("mcbbs.zip", McbbsPackFormat.IndexEntryName, McbbsJson);
            var pack = new McbbsPackFormat().Read(path);

            Assert.Equal("mcbbs", pack.FormatId);
            Assert.Equal("工业时代", pack.Name);
            Assert.Equal("2.0", pack.VersionId);
            Assert.Equal("1.16.5", pack.MinecraftVersion);
            Assert.Equal(LoaderKind.Forge, pack.Loader.Kind);
            Assert.Equal("36.2.34", pack.Loader.Version);

            Assert.Single(pack.Files);
            var file = pack.Files[0];
            Assert.Equal("mods/ic2.jar", file.Path);
            Assert.Equal("abc123", file.Sha1);
            Assert.Equal(1024L, file.Size);
            Assert.Equal("https://example.com/ic2.jar", Assert.Single(file.Downloads));
        }

        [Fact]
        public void McbbsPackAcceptsLooseFieldLayout()
        {
            // 有的导出工具：minecraft 直接写版本字符串、加载器写顶层、哈希包在 hashes 对象里。
            var path = CreatePack("mcbbs-loose.zip", McbbsPackFormat.IndexEntryName,
                "{\"name\":\"老包\",\"version\":\"3\",\"minecraft\":\"1.12.2\"," +
                "\"modLoaders\":[{\"id\":\"forge-14.23.5.2859\",\"primary\":true}]," +
                "\"files\":[{\"path\":\"config/a.cfg\",\"hashes\":{\"sha1\":\"deadbeef\"}}]}");

            var pack = new McbbsPackFormat().Read(path);

            Assert.Equal("1.12.2", pack.MinecraftVersion);
            Assert.Equal(LoaderKind.Forge, pack.Loader.Kind);
            Assert.Equal("14.23.5.2859", pack.Loader.Version);
            Assert.Equal("deadbeef", pack.Files[0].Sha1);
            Assert.Equal(0L, pack.Files[0].Size);
        }

        [Fact]
        public void HmclPackIsReadWithFabricLoader()
        {
            var path = CreatePack("hmcl.zip", HmclPackFormat.IndexEntryName, HmclJson);
            var pack = PackFormats.Read(path);

            Assert.Equal("hmcl", pack.FormatId);
            Assert.Equal("Fabric 世界", pack.Name);
            Assert.Equal("1.0", pack.VersionId);
            Assert.Equal("1.20.1", pack.MinecraftVersion);
            Assert.Equal(LoaderKind.Fabric, pack.Loader.Kind);
            Assert.Equal("0.15.11", pack.Loader.Version);
        }

        [Fact]
        public void HmclPackWithForeignManifestTypeIsRejected()
        {
            var path = CreatePack("hmcl-wrong-type.zip", HmclPackFormat.IndexEntryName,
                "{\"manifest\":{\"type\":\"curseforge\",\"name\":\"x\",\"version\":\"1\"}," +
                "\"minecraft\":{\"version\":\"1.20.1\"}}");

            var format = new HmclPackFormat();
            Assert.False(format.CanRead(path));
            Assert.Throws<InvalidDataException>(() => format.Read(path));
            Assert.Throws<InvalidDataException>(() => PackFormats.Read(path));
        }

        [Fact]
        public void HmclPackWithoutMinecraftVersionIsRejected()
        {
            var path = CreatePack("hmcl-no-version.zip", HmclPackFormat.IndexEntryName,
                "{\"manifest\":{\"type\":\"hmcl\",\"name\":\"坏包\",\"version\":\"1\"},\"minecraft\":{}}");

            var error = Assert.Throws<InvalidDataException>(() => new HmclPackFormat().Read(path));
            Assert.Contains("minecraft.version", error.Message);
        }

        [Fact]
        public void PlainTextFileIsNotAnyPackFormat()
        {
            var path = Path.Combine(_root, "not-a-pack.zip");
            File.WriteAllText(path, "这不是压缩包", new UTF8Encoding(false));

            foreach (var format in PackFormats.All) Assert.False(format.CanRead(path));

            Assert.Throws<InvalidDataException>(() => PackFormats.Read(path));
        }

        [Fact]
        public void PackFormatsRoutesEachIndexToItsReader()
        {
            var curseforge = CreatePack("route-cf.zip", CurseForgePackFormat.IndexEntryName, CurseForgeJson);
            var mcbbs = CreatePack("route-mcbbs.zip", McbbsPackFormat.IndexEntryName, McbbsJson);
            var hmcl = CreatePack("route-hmcl.zip", HmclPackFormat.IndexEntryName, HmclJson);

            Assert.Equal("curseforge", PackFormats.Read(curseforge).FormatId);
            Assert.Equal("mcbbs", PackFormats.Read(mcbbs).FormatId);
            Assert.Equal("hmcl", PackFormats.Read(hmcl).FormatId);
        }

        [Theory]
        [InlineData("forge-47.2.0", LoaderKind.Forge, "47.2.0")]
        [InlineData("fabric-0.15.11", LoaderKind.Fabric, "0.15.11")]
        [InlineData("neoforge-21.1.0", LoaderKind.NeoForge, "21.1.0")]
        [InlineData("quilt-0.20.0", LoaderKind.Quilt, "0.20.0")]
        [InlineData("forge", LoaderKind.Forge, null)]
        public void LoaderIdsAreSplitIntoKindAndVersion(string id, LoaderKind kind, string version)
        {
            var loader = PackLoaderIds.Parse(id);

            Assert.Equal(kind, loader.Kind);
            Assert.Equal(version, loader.Version);
        }

        [Fact]
        public void PrimaryLoaderWinsOverTheFirstOne()
        {
            var loaders = new List<PackModLoader>
            {
                new PackModLoader { Id = "fabric-0.15.11" },
                new PackModLoader { Id = "neoforge-21.1.0", Primary = true }
            };

            var loader = PackLoaderIds.Pick(loaders);

            Assert.Equal(LoaderKind.NeoForge, loader.Kind);
            Assert.Equal("21.1.0", loader.Version);
        }
    }
}
