using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace McDl.Core
{
    /// <summary>Describes the host we are resolving files for; also the input to rule evaluation.</summary>
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

        /// <summary>Classic launchers use 32/64 in native classifier names.</summary>
        public string ArchBits => Arch == "x86" ? "32" : "64";
    }

    /// <summary>Standard allow/disallow rule evaluation used by version manifests.</summary>
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
                        // A malformed version regex must not break resolution.
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
