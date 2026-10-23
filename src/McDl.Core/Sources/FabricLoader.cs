using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace McDl.Core
{
    /// <summary>Fabric loader metadata as published by meta.fabricmc.net.</summary>
    public sealed class FabricLoaderMeta
    {
        [JsonProperty("loader")] public FabricLoaderVersion Loader { get; set; }
        [JsonProperty("launcherMeta")] public FabricLauncherMeta LauncherMeta { get; set; }

        public string MainClassClient => LauncherMeta?.MainClass?.Client;

        /// <summary>Loader libraries that have to sit in libraries/ for the client to start.</summary>
        public List<FabricLibrary> ClientLibraries()
        {
            var result = new List<FabricLibrary>();
            var libraries = LauncherMeta?.Libraries;
            if (libraries == null) return result;

            if (libraries.Common != null) result.AddRange(libraries.Common);
            if (libraries.Client != null) result.AddRange(libraries.Client);

            // The same coordinate can appear in both lists.
            return result
                .Where(l => l != null && !string.IsNullOrEmpty(l.Name) && !string.IsNullOrEmpty(l.Url))
                .GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }
    }

    public sealed class FabricLoaderVersion
    {
        [JsonProperty("version")] public string Version { get; set; }
        [JsonProperty("stable")] public bool Stable { get; set; }
    }

    public sealed class FabricLauncherMeta
    {
        [JsonProperty("libraries")] public FabricLibraries Libraries { get; set; }
        [JsonProperty("mainClass")] public FabricMainClass MainClass { get; set; }
    }

    public sealed class FabricLibraries
    {
        [JsonProperty("common")] public List<FabricLibrary> Common { get; set; }
        [JsonProperty("client")] public List<FabricLibrary> Client { get; set; }
        [JsonProperty("server")] public List<FabricLibrary> Server { get; set; }
    }

    public sealed class FabricLibrary
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
    }

    public sealed class FabricMainClass
    {
        [JsonProperty("client")] public string Client { get; set; }
        [JsonProperty("server")] public string Server { get; set; }
    }

    /// <summary>Fetches Fabric loader metadata and folds it into an existing vanilla plan.</summary>
    public static class FabricLoader
    {
        public const string MetaBase = "https://meta.fabricmc.net";

        public static async Task<FabricLoaderMeta> FetchAsync(DownloadEngine engine, string gameVersion,
            string loaderVersion, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(gameVersion))
                throw new ArgumentException("game version is required to install the fabric loader");

            var loader = loaderVersion;
            if (string.IsNullOrWhiteSpace(loader)) loader = await PickLatestStableAsync(engine, gameVersion, ct)
                .ConfigureAwait(false);

            var url = MetaBase + "/v2/versions/loader/" + Uri.EscapeDataString(gameVersion) + "/" +
                      Uri.EscapeDataString(loader);
            var text = await engine.GetTextAsync(url, SourceKind.Other, ct).ConfigureAwait(false);
            var meta = Json.Parse<FabricLoaderMeta>(text);

            if (meta?.Loader == null || meta.LauncherMeta?.Libraries == null)
                throw new InvalidDataException("fabric loader metadata is unreadable: " + url);

            return meta;
        }

        private static async Task<string> PickLatestStableAsync(DownloadEngine engine, string gameVersion,
            CancellationToken ct)
        {
            var url = MetaBase + "/v2/versions/loader/" + Uri.EscapeDataString(gameVersion);
            var text = await engine.GetTextAsync(url, SourceKind.Other, ct).ConfigureAwait(false);
            var list = Json.Parse<List<FabricLoaderMeta>>(text);

            var pick = list?.FirstOrDefault(m => m.Loader != null && m.Loader.Stable)
                       ?? list?.FirstOrDefault(m => m.Loader != null);
            if (pick?.Loader?.Version == null)
                throw new InvalidDataException("no fabric loader found for " + gameVersion);

            return pick.Loader.Version;
        }

        /// <summary>
        /// Adds the loader jars to the plan and writes a merged version profile next to the vanilla one,
        /// so launchers that understand "inheritsFrom" can start the modpack directly.
        /// Fabric 的 meta 只给库的名字和地址，没有哈希；这里顺手去 maven 取每个库的 .sha1，
        /// 取不到就按“不校验”走（记日志），不影响安装。细节见 docs/network-and-files.md。
        /// </summary>
        public static async Task ApplyAsync(InstallContext context, InstallPlan plan, FabricLoaderMeta meta,
            string profileId, CancellationToken ct)
        {
            var librariesRoot = Path.Combine(plan.Root, "libraries");
            var libraries = meta.ClientLibraries().ToList();

            var hashes = await Task.WhenAll(libraries.Select(lib => FetchSha1Async(context, lib, ct)))
                .ConfigureAwait(false);

            var verified = 0;
            for (var i = 0; i < libraries.Count; i++)
            {
                var library = libraries[i];
                var relative = VanillaResolver.MavenRelativePath(library.Name);

                if (hashes[i] != null) verified++;
                plan.Files.Add(new FileTask
                {
                    Label = library.Name,
                    Url = library.Url,
                    TargetPath = Path.Combine(librariesRoot, relative.Replace('/', Path.DirectorySeparatorChar)),
                    Sha1 = hashes[i],
                    Kind = SourceKind.Other
                });
            }

            if (libraries.Count > 0)
                context?.Log?.Invoke("Fabric 库 " + libraries.Count + " 个，其中 " + verified + " 个能取到上游 .sha1 并校验");

            var profile = new
            {
                id = profileId,
                inheritsFrom = plan.Detail != null ? plan.Detail.Id : null,
                type = "release",
                mainClass = meta.MainClassClient,
                libraries = meta.ClientLibraries()
            };

            var path = Path.Combine(plan.Root, "versions", profileId, profileId + ".json");
            Json.Save(path, profile);
        }

        private static async Task<string> FetchSha1Async(InstallContext context, FabricLibrary library,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(library?.Url)) return null;

            try
            {
                var listing = await context.Engine.GetTextAsync(library.Url + ".sha1", SourceKind.Other, ct)
                    .ConfigureAwait(false);
                return Hashes.ParseSha1Listing(listing);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 单个库拿不到哈希不该让整个安装失败，最后那条汇总日志已经说明有几个没校验。
                return null;
            }
        }
    }
}
