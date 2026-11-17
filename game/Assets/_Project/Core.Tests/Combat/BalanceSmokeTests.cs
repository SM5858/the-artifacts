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

        [TestCase(0f)]
        [TestCase(0.6f)]
        public void Report_BotFightsAcrossTiersAndStrategies(float reactionTime)
        {
            TestContext.WriteLine($"--- bot reaction time {reactionTime:0.0}s ---");
            var builds = new[] { ("starter", PrototypeContent.StarterBuild), ("geared", PrototypeContent.GearedBuild) };
            foreach (var strategy in PrototypeContent.StrategyIds)
            foreach (var (name, build) in builds)
            foreach (var tier in PrototypeContent.Tiers)
            {
                var sim = TestBot.Play(build, new GuardianSetup(tier.Id, DefaultSkills, strategy), seed: 1, reactionTime);
                var hits = sim.Events.Count(e => e.Type == CombatEventType.SkillHit);
                TestContext.WriteLine(
                    $"{strategy,-10} {name,-7} {tier.Id}: {sim.Outcome,-4} {sim.Time,6:0}s  " +
                    $"player HP {sim.PlayerHp,5:0}/{sim.PlayerMaxHp:0}  Guardian HP {sim.GuardianHp,5:0}  telegraph hits {hits}");
                Assert.That(sim.Outcome, Is.Not.EqualTo(CombatOutcome.Ongoing), "every fight must end");
            }
        }
    }
}
