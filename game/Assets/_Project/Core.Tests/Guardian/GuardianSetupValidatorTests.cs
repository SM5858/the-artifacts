using System;
using NUnit.Framework;
using TheArtifacts.Core.Guardian;

namespace TheArtifacts.Core.Tests.Guardian
{
    // Fixture IDs are placeholders; real tiers, skills, and strategies come from GDD Q3/Q11.
    public class GuardianSetupValidatorTests
    {
        static readonly AllowedSetupSpace Space = new AllowedSetupSpace(
            statTiers: new[] { "tier-1", "tier-2" },
            skillPool: new[] { "skill-a", "skill-b", "skill-c" },
            strategies: new[] { "aggressive", "patient" },
            minSkills: 1,
            maxSkills: 2);

        static readonly GuardianSetup Default = new GuardianSetup("tier-1", new[] { "skill-a" }, "patient");
        static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(500);

        static SetupDecision Decide(GuardianSetup proposal, TimeSpan? latency = null) =>
            GuardianSetupValidator.Decide(proposal, latency ?? Fast, Space, Default);

        [Test]
        public void ValidProposal_IsUsed()
        {
            var proposal = new GuardianSetup("tier-2", new[] { "skill-b", "skill-c" }, "aggressive");

            var decision = Decide(proposal);

            Assert.That(decision.UsedFallback, Is.False);
            Assert.That(decision.Setup, Is.SameAs(proposal));
        }

        [Test]
        public void LatencyExactlyAtDeadline_IsAccepted()
        {
            var proposal = new GuardianSetup("tier-2", new[] { "skill-b" }, "aggressive");

            Assert.That(Decide(proposal, GuardianSetupValidator.Deadline).UsedFallback, Is.False);
        }

        [Test]
        public void ForcedTimeout_UsesDefaultGuardian()
        {
            var proposal = new GuardianSetup("tier-2", new[] { "skill-b" }, "aggressive");

            var decision = Decide(proposal, GuardianSetupValidator.Deadline + TimeSpan.FromMilliseconds(1));

            Assert.That(decision.Reason, Is.EqualTo(FallbackReason.Timeout));
            Assert.That(decision.Setup, Is.SameAs(Default));
        }

        [Test]
        public void NoProposal_UsesDefaultGuardian()
        {
            var decision = Decide(null);

            Assert.That(decision.Reason, Is.EqualTo(FallbackReason.ServiceUnavailable));
            Assert.That(decision.Setup, Is.SameAs(Default));
        }

        static readonly object[] InvalidProposals =
        {
            new object[] { new GuardianSetup("tier-9", new[] { "skill-a" }, "patient"), FallbackReason.TierOutOfRange },
            new object[] { new GuardianSetup("tier-1", new[] { "skill-z" }, "patient"), FallbackReason.SkillOutOfRange },
            new object[] { new GuardianSetup("tier-1", new[] { "skill-a", "skill-a" }, "patient"), FallbackReason.DuplicateSkill },
            new object[] { new GuardianSetup("tier-1", new string[0], "patient"), FallbackReason.SkillCountOutOfRange },
            new object[] { new GuardianSetup("tier-1", new[] { "skill-a", "skill-b", "skill-c" }, "patient"), FallbackReason.SkillCountOutOfRange },
            new object[] { new GuardianSetup("tier-1", new[] { "skill-a" }, "berserk"), FallbackReason.StrategyOutOfRange },
            new object[] { new GuardianSetup(null, new[] { "skill-a" }, "patient"), FallbackReason.Malformed },
            new object[] { new GuardianSetup("tier-1", null, "patient"), FallbackReason.Malformed },
            new object[] { new GuardianSetup("tier-1", new[] { "skill-a" }, ""), FallbackReason.Malformed },
            new object[] { new GuardianSetup("tier-1", new string[] { null }, "patient"), FallbackReason.Malformed },
        };

        [TestCaseSource(nameof(InvalidProposals))]
        public void ForcedOutOfRangeOrMalformed_UsesDefaultGuardian(GuardianSetup proposal, FallbackReason expected)
        {
            var decision = Decide(proposal);

            Assert.That(decision.Reason, Is.EqualTo(expected));
            Assert.That(decision.Setup, Is.SameAs(Default));
        }
    }
}
