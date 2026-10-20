using System;
using System.Collections.Generic;
using System.Linq;

namespace McDl.Core
{
    /// <summary>URL 属于哪类上游；分类是为了能分别换到镜像上。</summary>
    public enum SourceKind
    {
        Meta,
        Maven,
        Assets,
        Other
    }

    /// <summary>一个可尝试的下载地址，以及它来自哪个镜像配置。</summary>
    public sealed class SourceCandidate
    {
        public string Url { get; set; }
        public string Profile { get; set; }
        public SourceKind Kind { get; set; }
    }

    /// <summary>一个镜像的基地址；Official 为 true 时不改 URL，直接走官方。</summary>
    public sealed class MirrorProfile
    {
        public string Name { get; set; }
        public bool Official { get; set; }
        public string MetaBase { get; set; }
        public string MavenBase { get; set; }
        public string AssetsBase { get; set; }
    }

    /// <summary>
    /// 把官方 URL 改写到镜像上，并按分类记录失败次数；下次请求优先用还活着的镜像。
    /// 只做 URL 替换，不缓存、不探测。
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

        /// <summary>追加一个镜像；顺序只是初始顺序，实际排序看惩罚分。</summary>
        public MirrorRouter Add(MirrorProfile profile)
        {
            if (profile == null) return this;
            _profiles.Add(profile);
            return this;
        }

        public IReadOnlyList<MirrorProfile> Profiles => _profiles;

        /// <summary>默认配置：官方 + BMCLAPI。BMCLAPI 的三个基地址分开配，避免 maven 路径拼错。</summary>
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

        /// <summary>按 host 判断 URL 类型；不认识的 host（Modrinth、Fabric meta 等）归为 Other，不镜像。</summary>
        public static SourceKind Classify(string url)
        {
            if (string.IsNullOrEmpty(url)) return SourceKind.Other;

            if (MetaHosts.Any(h => url.StartsWith(h, StringComparison.OrdinalIgnoreCase))) return SourceKind.Meta;
            if (MavenHosts.Any(h => url.StartsWith(h, StringComparison.OrdinalIgnoreCase))) return SourceKind.Maven;
            if (AssetHosts.Any(h => url.StartsWith(h, StringComparison.OrdinalIgnoreCase))) return SourceKind.Assets;
            return SourceKind.Other;
        }

        /// <summary>按当前惩罚分列出候选地址；Other 类只有原始地址。</summary>
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

        /// <summary>成功一次就把该镜像的清零，让它重新排到前面。</summary>
        public void NoteSuccess(string profile, SourceKind kind)
        {
            if (string.IsNullOrEmpty(profile)) return;
            Dictionary<string, int> map;
            if (_penalties.TryGetValue(kind, out map)) map[profile] = 0;
        }

        /// <summary>失败一次加一分，只影响同类 URL 的排序。</summary>
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
