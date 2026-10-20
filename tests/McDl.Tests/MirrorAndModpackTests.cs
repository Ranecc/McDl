using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using McDl.Core;
using Xunit;

namespace McDl.Tests
{
    public sealed class MirrorRouterTests
    {
        [Theory]
        [InlineData("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", SourceKind.Meta)]
        [InlineData("https://launchermeta.mojang.com/v1/packages/x/y.json", SourceKind.Meta)]
        [InlineData("https://libraries.minecraft.net/org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1.jar", SourceKind.Maven)]
        [InlineData("https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.15.0/x.jar", SourceKind.Maven)]
        [InlineData("https://resources.download.minecraft.net/ab/abcdef", SourceKind.Assets)]
        [InlineData("https://cdn.modrinth.com/data/x/versions/y/file.jar", SourceKind.Other)]
        [InlineData("", SourceKind.Other)]
        public void UrlsAreClassifiedByUpstream(string url, SourceKind expected)
        {
            Assert.Equal(expected, MirrorRouter.Classify(url));
        }

        [Fact]
        public void OfficialProfileComesFirstAndMirrorFollows()
        {
            var router = MirrorRouter.CreateDefault();
            var candidates = router.Candidates("https://piston-meta.mojang.com/mc/game/x.json", SourceKind.Meta);

            Assert.Equal(2, candidates.Count);
            Assert.Equal("official", candidates[0].Profile);
            Assert.Equal("https://piston-meta.mojang.com/mc/game/x.json", candidates[0].Url);
            Assert.Equal("bmclapi", candidates[1].Profile);
            Assert.Equal("https://bmclapi2.bangbang93.com/mc/game/x.json", candidates[1].Url);
        }

        [Fact]
        public void FailingProfileSinksToTheBack()
        {
            var router = MirrorRouter.CreateDefault();

            router.NoteFailure("official", SourceKind.Maven);
            var candidates = router.Candidates("https://libraries.minecraft.net/a/b.jar", SourceKind.Maven);

            Assert.Equal("bmclapi", candidates[0].Profile);
            Assert.Equal("official", candidates[1].Profile);

            router.NoteSuccess("official", SourceKind.Maven);
            Assert.Equal("official", router.Candidates("https://libraries.minecraft.net/a/b.jar", SourceKind.Maven)[0].Profile);
        }

        [Fact]
        public void UnknownUpstreamIsNotRewritten()
        {
            var router = MirrorRouter.CreateDefault();
            var candidates = router.Candidates("https://cdn.modrinth.com/data/x/file.jar", SourceKind.Other);

            Assert.Single(candidates);
            Assert.Equal("https://cdn.modrinth.com/data/x/file.jar", candidates[0].Url);
        }
    }

    public sealed class ModpackTests
    {
        private static string WritePack(string indexJson)
        {
            var path = Path.Combine(Path.GetTempPath(), "mcdl-test-" + System.Guid.NewGuid().ToString("N") + ".mrpack");

            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry(ModpackArchive.IndexEntryName);
                using (var stream = entry.Open())
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    writer.Write(indexJson);
            }

            return path;
        }

        [Fact]
        public void PackIndexIsReadFromTheArchive()
        {
            var path = WritePack("{\"formatVersion\":1,\"game\":\"minecraft\",\"versionId\":\"1.0\"," +
                                 "\"name\":\"Test Pack\",\"dependencies\":{\"minecraft\":\"1.20.1\",\"fabric-loader\":\"0.15.0\"}}");
            try
            {
                var pack = ModpackArchive.ReadIndex(path);
                Assert.Equal("Test Pack", pack.Name);
                Assert.Equal("1.20.1", pack.MinecraftVersion);
                Assert.Equal("0.15.0", pack.FabricLoaderVersion);
                Assert.Null(pack.UnsupportedLoader);
                Assert.Equal("Test Pack 1.0", pack.DisplayName);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ArchiveWithoutIndexIsRejected()
        {
            var path = Path.Combine(Path.GetTempPath(), "mcdl-test-" + System.Guid.NewGuid().ToString("N") + ".mrpack");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                zip.CreateEntry("readme.txt");

            try
            {
                Assert.Throws<InvalidDataException>(() => ModpackArchive.ReadIndex(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void MissingArchiveIsRejected()
        {
            Assert.Throws<FileNotFoundException>(() => ModpackArchive.ReadIndex(Path.Combine(Path.GetTempPath(), "nope.mrpack")));
        }

        [Theory]
        [InlineData("forge", "forge 47.1.0")]
        [InlineData("neoforge", "neoforge 20.4.0")]
        [InlineData("quilt-loader", "quilt 0.20.0")]
        public void UnsupportedLoadersAreReported(string key, string expectedPrefix)
        {
            var pack = new ModpackManifest
            {
                Dependencies = new Dictionary<string, string> { { key, expectedPrefix.Split(' ')[1] } }
            };

            Assert.Equal(expectedPrefix, pack.UnsupportedLoader);
        }

        [Theory]
        [InlineData("My Pack", "1.0", "My-Pack-1.0")]
        [InlineData("  ", "1.0", "modpack-1.0")]
        [InlineData("a/b", "1", "a-b-1")]
        public void ProfileIdIsFolderSafe(string name, string versionId, string expected)
        {
            var pack = new ModpackManifest { Name = name, VersionId = versionId };
            Assert.Equal(expected, pack.BuildProfileId());
        }

        [Fact]
        public void RequestFallsBackToMinecraftFolderUnderCurrentDirectory()
        {
            var root = new InstallRequest().ResolveRoot();
            Assert.Equal(Path.Combine(System.Environment.CurrentDirectory, ".minecraft"), root);
        }
    }
}
