using McDl.Core;
using Xunit;

namespace McDl.Tests
{
    public sealed class VanillaResolverTests
    {
        private static PlatformInfo Platform(string arch)
        {
            return new PlatformInfo { OsName = "windows", OsVersion = "10.0", Arch = arch };
        }

        private static PlatformInfo OtherOs(string osName)
        {
            return new PlatformInfo { OsName = osName, OsVersion = "10.0", Arch = "x64" };
        }

        [Theory]
        [InlineData("org.lwjgl:lwjgl:3.3.1", null)]
        [InlineData("org.lwjgl:lwjgl:3.3.1:natives-windows", "natives-windows")]
        [InlineData("a:b:1:", null)]
        public void ClassifierIsTheFourthSegment(string coordinate, string expected)
        {
            Assert.Equal(expected, VanillaResolver.ClassifierOf(coordinate));
        }

        [Theory]
        [InlineData("natives-windows", true)]
        [InlineData("Natives-Linux", true)]
        [InlineData("sources", false)]
        [InlineData(null, false)]
        public void NativeClassifierIsDetected(string classifier, bool expected)
        {
            Assert.Equal(expected, VanillaResolver.IsNativeClassifier(classifier));
        }

        [Theory]
        [InlineData("natives-windows", "x64", true)]
        [InlineData("natives-windows-arm64", "x64", false)]
        [InlineData("natives-windows-arm64", "arm64", true)]
        [InlineData("natives-windows-x86", "x64", false)]
        [InlineData("natives-windows-x86", "x86", true)]
        [InlineData("natives-windows-x64", "x64", true)]
        [InlineData(null, "x64", true)]
        public void ArchitectureTaggedClassifiersOnlyMatchTheirArchitecture(string classifier, string arch, bool expected)
        {
            Assert.Equal(expected, VanillaResolver.MatchesArchitecture(classifier, Platform(arch)));
        }

        [Theory]
        [InlineData("org.lwjgl:lwjgl:3.3.1", "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1.jar")]
        [InlineData("org.lwjgl:lwjgl:3.3.1:natives-windows", "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar")]
        [InlineData("com.mojang:brigadier:1.0.18", "com/mojang/brigadier/1.0.18/brigadier-1.0.18.jar")]
        public void MavenCoordinateBecomesRepositoryPath(string coordinate, string expected)
        {
            Assert.Equal(expected, VanillaResolver.MavenRelativePath(coordinate));
        }

        [Fact]
        public void BadMavenCoordinateIsRejected()
        {
            Assert.Throws<System.FormatException>(() => VanillaResolver.MavenRelativePath("only:two"));
        }

        [Fact]
        public void LegacyNativesMapSubstitutesTheArchPlaceholder()
        {
            var library = new LibraryEntry
            {
                Name = "org.lwjgl.lwjgl:lwjgl-platform:2.9.4",
                Natives = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "windows", "natives-windows${arch}" }
                }
            };

            Assert.Equal("natives-windows64", VanillaResolver.NativeClassifier(library, Platform("x64")));
            Assert.Equal("natives-windows32", VanillaResolver.NativeClassifier(library, Platform("x86")));
            Assert.Null(VanillaResolver.NativeClassifier(library, OtherOs("linux")));
        }

        [Fact]
        public void TotalBytesAddsUpOptionalFiles()
        {
            var files = new System.Collections.Generic.List<FileTask>
            {
                new FileTask { Size = 10 },
                new FileTask { Size = 32 }
            };

            Assert.Equal(42, VanillaResolver.TotalBytes(files));
        }
    }
}
