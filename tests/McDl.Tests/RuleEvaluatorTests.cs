using System.Collections.Generic;
using McDl.Core;
using Xunit;

namespace McDl.Tests
{
    public sealed class RuleEvaluatorTests
    {
        private static PlatformInfo Windows()
        {
            return new PlatformInfo { OsName = "windows", OsVersion = "10.0", Arch = "x64" };
        }

        [Fact]
        public void NoRulesMeansAllowed()
        {
            Assert.True(RuleEvaluator.IsAllowed(null, Windows(), null));
            Assert.True(RuleEvaluator.IsAllowed(new List<RuleEntry>(), Windows(), null));
        }

        [Fact]
        public void AllowRuleForMatchingOsIsHonoured()
        {
            var rules = new List<RuleEntry>
            {
                new RuleEntry { Action = "allow", Os = new OsRule { Name = "windows" } }
            };
            Assert.True(RuleEvaluator.IsAllowed(rules, Windows(), null));
        }

        [Fact]
        public void AllowRuleForOtherOsIsNot()
        {
            var rules = new List<RuleEntry>
            {
                new RuleEntry { Action = "allow", Os = new OsRule { Name = "osx" } }
            };
            Assert.False(RuleEvaluator.IsAllowed(rules, Windows(), null));
        }

        [Fact]
        public void AllowThenDisallowWins()
        {
            var rules = new List<RuleEntry>
            {
                new RuleEntry { Action = "allow" },
                new RuleEntry { Action = "disallow", Os = new OsRule { Name = "windows" } }
            };
            Assert.False(RuleEvaluator.IsAllowed(rules, Windows(), null));
        }

        [Fact]
        public void ArchMustMatchWhenDeclared()
        {
            var rules = new List<RuleEntry>
            {
                new RuleEntry { Action = "allow", Os = new OsRule { Name = "windows", Arch = "arm64" } }
            };
            Assert.False(RuleEvaluator.IsAllowed(rules, Windows(), null));
        }

        [Fact]
        public void FeaturesGateTheRule()
        {
            var rules = new List<RuleEntry>
            {
                new RuleEntry
                {
                    Action = "allow",
                    Features = new Dictionary<string, bool> { { "has_custom_resolution", true } }
                }
            };

            Assert.False(RuleEvaluator.IsAllowed(rules, Windows(), null));
            Assert.False(RuleEvaluator.IsAllowed(rules, Windows(), new Dictionary<string, bool> { { "has_custom_resolution", false } }));
            Assert.True(RuleEvaluator.IsAllowed(rules, Windows(), new Dictionary<string, bool> { { "has_custom_resolution", true } }));
        }

        [Fact]
        public void MalformedOsVersionRegexIsIgnored()
        {
            var rules = new List<RuleEntry>
            {
                new RuleEntry { Action = "allow", Os = new OsRule { Name = "windows", Version = "^10\\.(" } }
            };
            Assert.True(RuleEvaluator.IsAllowed(rules, Windows(), null));
        }

        [Fact]
        public void OsVersionRegexFilters()
        {
            var rules = new List<RuleEntry>
            {
                new RuleEntry { Action = "allow", Os = new OsRule { Name = "windows", Version = "^6\\." } }
            };
            Assert.False(RuleEvaluator.IsAllowed(rules, Windows(), null));
        }
    }
}
