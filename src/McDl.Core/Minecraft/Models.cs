using System.Collections.Generic;
using Newtonsoft.Json;

namespace McDl.Core
{
    public sealed class VersionManifest
    {
        [JsonProperty("latest")] public LatestVersions Latest { get; set; }
        [JsonProperty("versions")] public List<VersionEntry> Versions { get; set; }
    }

    public sealed class LatestVersions
    {
        [JsonProperty("release")] public string Release { get; set; }
        [JsonProperty("snapshot")] public string Snapshot { get; set; }
    }

    public sealed class VersionEntry
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
        [JsonProperty("sha1")] public string Sha1 { get; set; }
        [JsonProperty("releaseTime")] public string ReleaseTime { get; set; }
    }

    public sealed class VersionDetail
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("mainClass")] public string MainClass { get; set; }
        [JsonProperty("assets")] public string Assets { get; set; }
        [JsonProperty("assetIndex")] public AssetIndexRef AssetIndex { get; set; }
        [JsonProperty("downloads")] public Dictionary<string, DownloadEntry> Downloads { get; set; }
        [JsonProperty("libraries")] public List<LibraryEntry> Libraries { get; set; }
        [JsonProperty("javaVersion")] public JavaVersionInfo JavaVersion { get; set; }
    }

    public sealed class JavaVersionInfo
    {
        [JsonProperty("component")] public string Component { get; set; }
        [JsonProperty("majorVersion")] public int MajorVersion { get; set; }
    }

    public sealed class DownloadEntry
    {
        [JsonProperty("url")] public string Url { get; set; }
        [JsonProperty("sha1")] public string Sha1 { get; set; }
        [JsonProperty("size")] public long Size { get; set; }
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("totalSize")] public long TotalSize { get; set; }
    }

    public sealed class AssetIndexRef
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("sha1")] public string Sha1 { get; set; }
        [JsonProperty("size")] public long Size { get; set; }
        [JsonProperty("totalSize")] public long TotalSize { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
    }

    public sealed class AssetIndex
    {
        [JsonProperty("objects")] public Dictionary<string, AssetObject> Objects { get; set; }
        [JsonProperty("virtual")] public bool Virtual { get; set; }
        [JsonProperty("map_to_resources")] public bool MapToResources { get; set; }
    }

    public sealed class AssetObject
    {
        [JsonProperty("hash")] public string Hash { get; set; }
        [JsonProperty("size")] public long Size { get; set; }
    }

    public sealed class LibraryEntry
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("downloads")] public LibraryDownloads Downloads { get; set; }
        [JsonProperty("natives")] public Dictionary<string, string> Natives { get; set; }
        [JsonProperty("rules")] public List<RuleEntry> Rules { get; set; }
        [JsonProperty("extract")] public ExtractRule Extract { get; set; }
        [JsonProperty("url")] public string RepositoryUrl { get; set; }
    }

    public sealed class LibraryDownloads
    {
        [JsonProperty("artifact")] public DownloadEntry Artifact { get; set; }
        [JsonProperty("classifiers")] public Dictionary<string, DownloadEntry> Classifiers { get; set; }
    }

    public sealed class ExtractRule
    {
        [JsonProperty("exclude")] public List<string> Exclude { get; set; }
    }

    public sealed class RuleEntry
    {
        [JsonProperty("action")] public string Action { get; set; }
        [JsonProperty("os")] public OsRule Os { get; set; }
        [JsonProperty("features")] public Dictionary<string, bool> Features { get; set; }
    }

    public sealed class OsRule
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("arch")] public string Arch { get; set; }
        [JsonProperty("version")] public string Version { get; set; }
    }
}
