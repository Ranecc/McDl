using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace McDl.Core
{
    /// <summary>当前主机信息，同时作为 rules 判定的输入。只支持 Windows。</summary>
    public sealed class PlatformInfo
    {
        public string OsName { get; set; }
        public string OsVersion { get; set; }
        public string Arch { get; set; }

        public static PlatformInfo Current()
        {
            var arch = RuntimeInformation.OSArchitecture == Architecture.X86
                ? "x86"
                : RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";

            return new PlatformInfo
            {
                OsName = "windows",
                OsVersion = Environment.OSVersion.Version.ToString(),
                Arch = arch
            };
        }

        /// <summary>老版本 natives 名里用 32/64，不是 x86/x64。</summary>
        public string ArchBits => Arch == "x86" ? "32" : "64";
    }

    /// <summary>版本清单里的 allow/disallow 规则；没有规则视为允许，最后一条匹配的规则生效。</summary>
    public static class RuleEvaluator
    {
        public static bool IsAllowed(IList<RuleEntry> rules, PlatformInfo platform, IDictionary<string, bool> features)
        {
            if (rules == null || rules.Count == 0) return true;

            var allowed = false;
            foreach (var rule in rules)
            {
                if (rule == null) continue;
                if (!Matches(rule, platform, features)) continue;
                allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
            }
            return allowed;
        }

        private static bool Matches(RuleEntry rule, PlatformInfo platform, IDictionary<string, bool> features)
        {
            if (rule.Os != null)
            {
                if (!string.IsNullOrEmpty(rule.Os.Name) &&
                    !string.Equals(rule.Os.Name, platform.OsName, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!string.IsNullOrEmpty(rule.Os.Arch) &&
                    !string.Equals(rule.Os.Arch, platform.Arch, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!string.IsNullOrEmpty(rule.Os.Version))
                {
                    try
                    {
                        if (!Regex.IsMatch(platform.OsVersion ?? string.Empty, rule.Os.Version))
                            return false;
                    }
                    catch (ArgumentException)
                    {
                        // 版本号正则是上游给的，写坏了也不能让整个解析失败。
                    }
                }
            }

            if (rule.Features != null)
            {
                foreach (var feature in rule.Features)
                {
                    bool actual;
                    if (features == null || !features.TryGetValue(feature.Key, out actual)) return false;
                    if (actual != feature.Value) return false;
                }
            }

            return true;
        }
    }
}
