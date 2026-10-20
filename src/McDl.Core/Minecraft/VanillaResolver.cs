using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace McDl.Core
{
    /// <summary>A natives jar that has to be unpacked into the version directory.</summary>
    public sealed class NativesExtraction
    {
        public FileTask Archive { get; set; }
        public string TargetDir { get; set; }
        public List<string> Excludes { get; set; }
    }

    /// <summary>Turns a vanilla version document into a plain list of files to fetch.</summary>
    public static class VanillaResolver
    {
        public const string MetaBase = "https://piston-meta.mojang.com";
        public const string ManifestUrl = MetaBase + "/mc/game/version_manifest_v2.json";
        public const string AssetObjectBase = "https://resources.download.minecraft.net";

        public static string MavenRelativePath(string coordinate)
        {
            if (string.IsNullOrEmpty(coordinate)) throw new ArgumentException("empty maven coordinate");

            var parts = coordinate.Split(':');
            if (parts.Length < 3) throw new FormatException("bad maven coordinate: " + coordinate);

            var group = parts[0];
            var artifact = parts[1];
            var version = parts[2];
            var classifier = parts.Length > 3 ? parts[3] : null;

            var sb = new StringBuilder();
            sb.Append(group.Replace('.', '/')).Append('/')
                .Append(artifact).Append('/')
                .Append(version).Append('/')
                .Append(artifact).Append('-').Append(version);
            if (!string.IsNullOrEmpty(classifier)) sb.Append('-').Append(classifier);
            sb.Append(".jar");
            return sb.ToString();
        }

        public static string NativeClassifier(LibraryEntry library, PlatformInfo platform)
        {
            if (library?.Natives == null || library.Natives.Count == 0) return null;

            string value;
            if (!library.Natives.TryGetValue(platform.OsName, out value) || string.IsNullOrEmpty(value)) return null;

            return value.Replace("${arch}", platform.ArchBits);
        }

        private static readonly List<string> DefaultNativeExcludes = NativesUnpacker.DefaultExcludes;

        public static List<FileTask> LibraryFiles(VersionDetail detail, string root, PlatformInfo platform,
            List<NativesExtraction> natives)
        {
            var files = new List<FileTask>();
            if (detail?.Libraries == null) return files;

            var librariesRoot = Path.Combine(root, "libraries");
            var nativesDir = Path.Combine(root, "versions", detail.Id, "natives");

            foreach (var library in detail.Libraries)
            {
                if (library == null) continue;
                if (!RuleEvaluator.IsAllowed(library.Rules, platform, null)) continue;

                var nameClassifier = ClassifierOf(library.Name);
                if (IsNativeClassifier(nameClassifier) && !MatchesArchitecture(nameClassifier, platform)) continue;

                var artifactTask = BuildTask(library, library.Downloads?.Artifact, librariesRoot);

                // Modern version documents ship natives as standalone classifier artifacts
                // (":natives-windows"), with the os restriction carried by a rule instead.
                if (artifactTask != null && IsNativeClassifier(nameClassifier))
                {
                    files.Add(artifactTask);
                    natives.Add(new NativesExtraction
                    {
                        Archive = artifactTask,
                        TargetDir = nativesDir,
                        Excludes = library.Extract?.Exclude ?? DefaultNativeExcludes
                    });
                    continue;
                }

                if (artifactTask != null) files.Add(artifactTask);

                // Legacy layout: an os -> classifier map plus a classifiers dictionary.
                var legacyClassifier = NativeClassifier(library, platform);
                if (legacyClassifier == null) continue;

                DownloadEntry nativeEntry = null;
                if (library.Downloads?.Classifiers != null)
                    library.Downloads.Classifiers.TryGetValue(legacyClassifier, out nativeEntry);

                if (nativeEntry?.Url == null) continue;

                var nativeRelative = nativeEntry.Path;
                if (string.IsNullOrEmpty(nativeRelative))
                    nativeRelative = MavenRelativePath(library.Name + ":" + legacyClassifier);

                var nativeTask = new FileTask
                {
                    Label = Path.GetFileName(nativeRelative),
                    Url = nativeEntry.Url,
                    TargetPath = Path.Combine(librariesRoot, nativeRelative.Replace('/', Path.DirectorySeparatorChar)),
                    Sha1 = nativeEntry.Sha1,
                    Size = nativeEntry.Size,
                    Kind = MirrorRouter.Classify(nativeEntry.Url)
                };
                files.Add(nativeTask);

                natives.Add(new NativesExtraction
                {
                    Archive = nativeTask,
                    TargetDir = nativesDir,
                    Excludes = library.Extract?.Exclude ?? DefaultNativeExcludes
                });
            }

            return files;
        }

        /// <summary>The optional fourth maven segment, e.g. "natives-windows" in a:b:1:natives-windows.</summary>
        internal static string ClassifierOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var parts = name.Split(':');
            return parts.Length >= 4 && !string.IsNullOrEmpty(parts[3]) ? parts[3] : null;
        }

        internal static bool IsNativeClassifier(string classifier)
        {
            return classifier != null && classifier.StartsWith("natives-", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Architecture-tagged native classifiers ("natives-windows-arm64") only belong to that
        /// architecture. Mojang ships all variants with a plain "os.name == windows" rule, so the
        /// suffix is the only thing that distinguishes them - installing the wrong one puts a
        /// foreign-architecture dll in the natives folder under the same file name.
        /// </summary>
        internal static bool MatchesArchitecture(string classifier, PlatformInfo platform)
        {
            if (string.IsNullOrEmpty(classifier)) return true;

            var arch = platform != null && !string.IsNullOrEmpty(platform.Arch) ? platform.Arch : "x64";

            if (classifier.EndsWith("-arm64", StringComparison.OrdinalIgnoreCase)) return arch == "arm64";
            if (classifier.EndsWith("-x86", StringComparison.OrdinalIgnoreCase)) return arch == "x86";
            if (classifier.EndsWith("-x64", StringComparison.OrdinalIgnoreCase)) return arch == "x64";
            return true;
        }

        /// <summary>Builds the download task for a library artifact, synthesizing a maven URL when needed.</summary>
        internal static FileTask BuildTask(LibraryEntry library, DownloadEntry entry, string librariesRoot)
        {
            var url = entry?.Url;
            var relative = entry?.Path;

            if (string.IsNullOrEmpty(url))
            {
                if (string.IsNullOrEmpty(library?.RepositoryUrl) || string.IsNullOrEmpty(library?.Name)) return null;

                if (string.IsNullOrEmpty(relative)) relative = MavenRelativePath(library.Name);
                url = library.RepositoryUrl.TrimEnd('/') + "/" + relative;
            }

            if (string.IsNullOrEmpty(relative)) relative = MavenRelativePath(library.Name);

            return new FileTask
            {
                Label = Path.GetFileName(relative),
                Url = url,
                TargetPath = Path.Combine(librariesRoot, relative.Replace('/', Path.DirectorySeparatorChar)),
                Sha1 = entry?.Sha1,
                Size = entry?.Size ?? 0,
                Kind = MirrorRouter.Classify(url)
            };
        }

        public static List<FileTask> ClientFiles(VersionDetail detail, string root)
        {
            var files = new List<FileTask>();
            if (detail?.Downloads == null) return files;

            DownloadEntry client;
            if (!detail.Downloads.TryGetValue("client", out client) || client?.Url == null) return files;

            files.Add(new FileTask
            {
                Label = detail.Id + ".jar",
                Url = client.Url,
                TargetPath = Path.Combine(root, "versions", detail.Id, detail.Id + ".jar"),
                Sha1 = client.Sha1,
                Size = client.Size,
                Kind = MirrorRouter.Classify(client.Url)
            });

            return files;
        }

        public static List<FileTask> AssetFiles(AssetIndex index, string root)
        {
            var files = new List<FileTask>();
            if (index?.Objects == null) return files;

            var objectsRoot = Path.Combine(root, "assets", "objects");

            foreach (var pair in index.Objects)
            {
                var asset = pair.Value;
                if (asset == null || string.IsNullOrEmpty(asset.Hash) || asset.Hash.Length < 4) continue;

                var prefix = asset.Hash.Substring(0, 2);
                files.Add(new FileTask
                {
                    Label = pair.Key,
                    Url = AssetObjectBase + "/" + prefix + "/" + asset.Hash,
                    TargetPath = Path.Combine(objectsRoot, prefix, asset.Hash),
                    Sha1 = asset.Hash,
                    Size = asset.Size,
                    Kind = SourceKind.Assets
                });
            }

            return files;
        }

        public static long TotalBytes(IEnumerable<FileTask> tasks)
        {
            long total = 0;
            foreach (var task in tasks) total += task.Size;
            return total;
        }
    }
}
