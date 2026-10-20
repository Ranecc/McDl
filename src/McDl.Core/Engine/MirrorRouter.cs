using System;
using System.Collections.Generic;
using System.Linq;

namespace McDl.Core
{
    /// <summary>Which upstream a URL belongs to. The split exists so every kind can be re-based onto a mirror.</summary>
    public enum SourceKind
    {
        Meta,
        Maven,
        Assets,
        Other
    }

    public sealed class SourceCandidate
    {
        public string Url { get; set; }
        public string Profile { get; set; }
        public SourceKind Kind { get; set; }
    }

    public sealed class MirrorProfile
    {
        public string Name { get; set; }
        public bool Official { get; set; }
        public string MetaBase { get; set; }
        public string MavenBase { get; set; }
        public string AssetsBase { get; set; }
    }

    /// <summary>
    /// Rewrites canonical (official) URLs onto mirror endpoints and remembers which profile
    /// keeps failing so the next request starts from a profile that is actually alive.
    /// </summary>
    public sealed class MirrorRouter
    {
        private static readonly string[] MetaHosts =
        {
            "https://piston-meta.mojang.com",
            "https://launchermeta.mojang.com",
            "https://piston-data.mojang.com"
        };

        private static readonly string[] MavenHosts =
        {
            "https://libraries.minecraft.net",
            "https://maven.minecraftforge.net",
            "https://maven.fabricmc.net",
            "https://maven.neoforged.net",
            "https://maven.quiltmc.org"
        };

        private static readonly string[] AssetHosts =
        {
            "https://resources.download.minecraft.net",
            "https://assets.mojang.com"
        };

        private readonly List<MirrorProfile> _profiles = new List<MirrorProfile>();
        private readonly Dictionary<SourceKind, Dictionary<string, int>> _penalties =
            new Dictionary<SourceKind, Dictionary<string, int>>();

        public MirrorRouter()
        {
            foreach (SourceKind kind in Enum.GetValues(typeof(SourceKind)))
                _penalties[kind] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        public MirrorRouter Add(MirrorProfile profile)
        {
            if (profile == null) return this;
            _profiles.Add(profile);
            return this;
        }

        public IReadOnlyList<MirrorProfile> Profiles => _profiles;

        public static MirrorRouter CreateDefault()
        {
            return new MirrorRouter()
                .Add(new MirrorProfile { Name = "official", Official = true })
                .Add(new MirrorProfile
                {
                    Name = "bmclapi",
                    MetaBase = "https://bmclapi2.bangbang93.com",
                    MavenBase = "https://bmclapi2.bangbang93.com/maven",
                    AssetsBase = "https://bmclapi2.bangbang93.com/assets"
                });
        }

        public static SourceKind Classify(string url)
        {
            if (string.IsNullOrEmpty(url)) return SourceKind.Other;

            if (MetaHosts.Any(h => url.StartsWith(h, StringComparison.OrdinalIgnoreCase))) return SourceKind.Meta;
            if (MavenHosts.Any(h => url.StartsWith(h, StringComparison.OrdinalIgnoreCase))) return SourceKind.Maven;
            if (AssetHosts.Any(h => url.StartsWith(h, StringComparison.OrdinalIgnoreCase))) return SourceKind.Assets;
            return SourceKind.Other;
        }

        public IReadOnlyList<SourceCandidate> Candidates(string url, SourceKind kind)
        {
            var result = new List<SourceCandidate>();
            if (string.IsNullOrEmpty(url)) return result;

            if (kind == SourceKind.Other)
            {
                result.Add(new SourceCandidate { Url = url, Profile = "direct", Kind = kind });
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in _profiles.OrderBy(p => Penalty(kind, p.Name)))
            {
                var candidate = Rewrite(url, kind, profile);
                if (candidate == null || !seen.Add(candidate)) continue;
                result.Add(new SourceCandidate { Url = candidate, Profile = profile.Name, Kind = kind });
            }

            if (result.Count == 0)
                result.Add(new SourceCandidate { Url = url, Profile = "direct", Kind = kind });

            return result;
        }

        public void NoteSuccess(string profile, SourceKind kind)
        {
            if (string.IsNullOrEmpty(profile)) return;
            Dictionary<string, int> map;
            if (_penalties.TryGetValue(kind, out map)) map[profile] = 0;
        }

        public void NoteFailure(string profile, SourceKind kind)
        {
            if (string.IsNullOrEmpty(profile)) return;
            Dictionary<string, int> map;
            if (!_penalties.TryGetValue(kind, out map)) return;
            int count;
            map.TryGetValue(profile, out count);
            map[profile] = count + 1;
        }

        private int Penalty(SourceKind kind, string profile)
        {
            Dictionary<string, int> map;
            int count;
            if (_penalties.TryGetValue(kind, out map) && map.TryGetValue(profile, out count)) return count;
            return 0;
        }

        private static string Rewrite(string url, SourceKind kind, MirrorProfile profile)
        {
            if (profile.Official) return url;

            switch (kind)
            {
                case SourceKind.Meta:
                    return profile.MetaBase == null ? null : SwapAny(url, MetaHosts, profile.MetaBase);
                case SourceKind.Maven:
                    return profile.MavenBase == null ? null : SwapAny(url, MavenHosts, profile.MavenBase);
                case SourceKind.Assets:
                    return profile.AssetsBase == null ? null : SwapAny(url, AssetHosts, profile.AssetsBase);
                default:
                    return null;
            }
        }

        private static string SwapAny(string url, string[] hosts, string newBase)
        {
            foreach (var host in hosts)
            {
                if (!url.StartsWith(host, StringComparison.OrdinalIgnoreCase)) continue;
                return newBase.TrimEnd('/') + url.Substring(host.Length);
            }
            return null;
        }
    }
}
