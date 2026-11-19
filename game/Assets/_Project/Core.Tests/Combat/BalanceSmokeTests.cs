using System.Linq;
using NUnit.Framework;
using TheArtifacts.Core.Combat;
using TheArtifacts.Core.Guardian;

namespace TheArtifacts.Core.Tests.Combat
{
    /// <summary>
    /// Whole fights played by <see cref="TestBot"/>. These check that fights finish and that the
    /// starter-versus-geared ordering holds; they are not a substitute for playtests.
    /// </summary>
    public class BalanceSmokeTests
    {
        static readonly string[] DefaultSkills = { "cleave", "slam", "charge" };

        [Test]
        public void Starter_BeatsT1()
        {
            var sim = TestBot.Play(PrototypeContent.StarterBuild, new GuardianSetup("T1", DefaultSkills, "steady"), seed: 1);
            Assert.That(sim.Outcome, Is.EqualTo(CombatOutcome.Win));
        }

        [Test]
        public void Geared_BeatsT5()
        {
            var sim = TestBot.Play(PrototypeContent.GearedBuild, new GuardianSetup("T5", DefaultSkills, "steady"), seed: 1);
            Assert.That(sim.Outcome, Is.EqualTo(CombatOutcome.Win));
        }

        [Test]
        public void Geared_IsFasterThanStarter_OnTheSameGuardian()
        {
            var setup = PrototypeContent.DefaultSetup;
            var starter = TestBot.Play(PrototypeContent.StarterBuild, setup, seed: 1);
            var geared = TestBot.Play(PrototypeContent.GearedBuild, setup, seed: 1);

            Assert.That(geared.Outcome, Is.EqualTo(CombatOutcome.Win));
            if (starter.Outcome == CombatOutcome.Win)
                Assert.That(geared.Time, Is.LessThan(starter.Time));
        }

        /// <summary>
        /// Balance report for the Q15 rebalance: every Guardian, build, tier, and skill set, with a bot that reacts
        /// in 0.6 s and misses a share of telegraphs entirely (0%, 30%, 60%) as a stand-in for human mistakes.
        /// Printed to the test log as REPORT lines (steady strategy). Not a playtest.
        /// </summary>
        [TestCase(0f)]
        [TestCase(0.3f)]
        [TestCase(0.6f)]
        public void Report_BotFightsAcrossGuardiansTiersAndBuilds(float missChance)
        {
            const float reactionTime = 0.6f;
            var skillSets = new[] { ("physical", DefaultSkills), ("mixed", new[] { "slam", "rings", "embers" }) };
            var builds = new[] { ("starter", PrototypeContent.StarterBuild), ("geared", PrototypeContent.GearedBuild) };
            TestContext.WriteLine("REPORT|miss|guardian|skills|build|tier|outcome|seconds|hp_left|max_hp|telegraph_hits|bolt_share|robe_uses|crits");
            foreach (var guardian in PrototypeContent.Guardians)
            foreach (var (setName, skills) in skillSets)
            foreach (var (name, build) in builds)
            foreach (var tier in PrototypeContent.Tiers)
            {
                var sim = TestBot.Play(build, new GuardianSetup(tier.Id, skills, "steady"), seed: 1, reactionTime, guardian: guardian, missChance: missChance);
                var dealt = sim.Events.Where(e => e.Type == CombatEventType.GuardianDamaged).Sum(e => e.Amount);
                var bolt = sim.Events.Where(e => e.Type == CombatEventType.GuardianDamaged && e.Id == "lightning_bolt").Sum(e => e.Amount);
                TestContext.WriteLine(string.Join("|", "REPORT", missChance.ToString("0.0"), guardian.Id, setName, name, tier.Id,
                    sim.Outcome, sim.Time.ToString("0"), sim.PlayerHp.ToString("0"), sim.PlayerMaxHp.ToString("0"),
                    sim.Events.Count(e => e.Type == CombatEventType.SkillHit),
                    dealt > 0f ? (bolt / dealt).ToString("0.00") : "0",
                    sim.Events.Count(e => e.Type == CombatEventType.AbilityUsed && e.Id == "columbia_blue_robe"),
                    sim.Events.Count(e => e.Type == CombatEventType.CriticalHit)));
                Assert.That(sim.Outcome, Is.Not.EqualTo(CombatOutcome.Ongoing), "every fight must end");
            }
        }
    }
}
