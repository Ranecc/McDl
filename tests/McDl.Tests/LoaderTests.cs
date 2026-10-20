using System;
using McDl.Core;
using Xunit;

namespace McDl.Tests
{
    /// <summary>Java 版本解析与 Forge/NeoForge installer 坐标拼装，都是纯逻辑，直接测。</summary>
    public sealed class LoaderTests
    {
        [Theory]
        [InlineData("1.8.0_402", 8)]
        [InlineData("1.8", 8)]
        [InlineData("21.0.1", 21)]
        [InlineData("17", 17)]
        [InlineData("25-ea", 25)]
        [InlineData("", 0)]
        [InlineData("   ", 0)]
        [InlineData("not-a-version", 0)]
        [InlineData("1.x", 0)]
        public void ParseMajor_handles_the_usual_shapes(string version, int expected)
        {
            Assert.Equal(expected, JavaLocator.ParseMajor(version));
        }

        [Theory]
        [InlineData("openjdk version \"17.0.12\" 2024-07-16", 17, "17.0.12")]
        [InlineData("java version \"1.8.0_402\"", 8, "1.8.0_402")]
        [InlineData("openjdk version \"21\" 2023-09-19", 21, "21")]
        public void ParseVersionLine_reads_version_and_major(string line, int major, string version)
        {
            var runtime = JavaLocator.ParseVersionLine(@"C:\Java\bin\java.exe", line);

            Assert.NotNull(runtime);
            Assert.Equal(major, runtime.MajorVersion);
            Assert.Equal(version, runtime.Version);
            Assert.Equal(@"C:\Java\bin\java.exe", runtime.Path);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Picked up JAVA_TOOL_OPTIONS: -Dfile.encoding=UTF-8")]
        [InlineData("openjdk version 17.0.12")]
        [InlineData("openjdk version \"first\" second")]
        [InlineData("openjdk version \"garbage\"")]
        public void ParseVersionLine_returns_null_when_the_line_is_not_a_version(string line)
        {
            Assert.Null(JavaLocator.ParseVersionLine(@"C:\Java\bin\java.exe", line));
        }

        [Theory]
        [InlineData(LoaderKind.Forge, "1.20.1", "47.2.0", "net.minecraftforge:forge:1.20.1-47.2.0:installer")]
        [InlineData(LoaderKind.Forge, "1.20.1", "1.20.1-47.2.0", "net.minecraftforge:forge:1.20.1-47.2.0:installer")]
        [InlineData(LoaderKind.Forge, "1.12.2", "14.23.5.2859", "net.minecraftforge:forge:1.12.2-14.23.5.2859:installer")]
        [InlineData(LoaderKind.NeoForge, "1.20.4", "20.4.237", "net.neoforged:neoforge:20.4.237:installer")]
        [InlineData(LoaderKind.NeoForge, "1.20.1", "47.1.106", "net.neoforged:forge:1.20.1-47.1.106:installer")]
        [InlineData(LoaderKind.NeoForge, "1.20.1", "1.20.1-47.1.106", "net.neoforged:forge:1.20.1-47.1.106:installer")]
        public void BuildInstallerCoordinate_follows_the_maven_layout(LoaderKind kind, string gameVersion,
            string loaderVersion, string expected)
        {
            Assert.Equal(expected, ForgeInstaller.BuildInstallerCoordinate(kind, gameVersion, loaderVersion));
        }

        [Theory]
        [InlineData(LoaderKind.Forge, "1.20.1", "47.2.0", "forge-1.20.1-47.2.0-installer.jar")]
        [InlineData(LoaderKind.NeoForge, "1.20.4", "20.4.237", "neoforge-20.4.237-installer.jar")]
        [InlineData(LoaderKind.NeoForge, "1.20.1", "47.1.106", "forge-1.20.1-47.1.106-installer.jar")]
        public void InstallerFileName_uses_the_artifact_stem(LoaderKind kind, string gameVersion, string loaderVersion,
            string expected)
        {
            Assert.Equal(expected, ForgeInstaller.InstallerFileName(kind, gameVersion, loaderVersion));
        }

        [Fact]
        public void ComposeInstallerVersion_rejects_a_missing_loader_version()
        {
            var error = Assert.Throws<ArgumentException>(
                () => ForgeInstaller.ComposeInstallerVersion(LoaderKind.Forge, "1.20.1", null));

            Assert.Contains("版本号", error.Message);
        }

        [Fact]
        public void ComposeInstallerVersion_rejects_a_missing_game_version()
        {
            Assert.Throws<ArgumentException>(
                () => ForgeInstaller.ComposeInstallerVersion(LoaderKind.Forge, " ", "47.2.0"));
        }

        [Theory]
        [InlineData("1.20.2", "1.20.2", true)]
        [InlineData("1.20.10", "1.20.2", true)]
        [InlineData("1.20.1", "1.20.2", false)]
        [InlineData("1.19.4", "1.20.2", false)]
        [InlineData("1.21", "1.20.2", true)]
        [InlineData("23w45a", "1.20.2", false)]
        public void IsVersionAtLeast_compares_numeric_segments(string version, string reference, bool expected)
        {
            Assert.Equal(expected, ForgeInstaller.IsVersionAtLeast(version, reference));
        }

        [Theory]
        [InlineData(0, "1.20.5", 21)]
        [InlineData(0, "1.21.1", 21)]
        [InlineData(0, "1.20.4", 17)]
        [InlineData(0, "1.18", 17)]
        [InlineData(0, "1.17.1", 16)]
        [InlineData(0, "1.16.5", 8)]
        [InlineData(0, "1.12.2", 8)]
        [InlineData(17, "1.12.2", 17)]
        [InlineData(21, "1.20.1", 21)]
        public void RequiredJavaMajor_prefers_metadata_then_falls_back_by_game_version(int declared,
            string gameVersion, int expected)
        {
            Assert.Equal(expected, ForgeInstaller.RequiredJavaMajor(declared, gameVersion));
        }

        [Theory]
        [InlineData(LoaderKind.Forge, "1.20.1-47.2.0", "forge-1.20.1-47.2.0")]
        [InlineData(LoaderKind.NeoForge, "20.4.237", "neoforge-20.4.237")]
        [InlineData(LoaderKind.NeoForge, "1.20.1-47.1.106", "neoforge-1.20.1-47.1.106")]
        public void ExpectedVersionId_uses_the_installer_naming(LoaderKind kind, string installerVersion,
            string expected)
        {
            Assert.Equal(expected, ForgeInstaller.ExpectedVersionId(kind, installerVersion));
        }
    }
}
