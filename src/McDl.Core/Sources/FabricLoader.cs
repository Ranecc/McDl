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
        /// </summary>
        public static void Apply(InstallPlan plan, FabricLoaderMeta meta, string profileId)
        {
            var librariesRoot = Path.Combine(plan.Root, "libraries");

            foreach (var library in meta.ClientLibraries())
            {
                var relative = VanillaResolver.MavenRelativePath(library.Name);
                plan.Files.Add(new FileTask
                {
                    Label = library.Name,
                    Url = library.Url,
                    TargetPath = Path.Combine(librariesRoot, relative.Replace('/', Path.DirectorySeparatorChar)),
                    Kind = SourceKind.Other
                });
            }

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
    }
}
