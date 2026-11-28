using System.Linq;
using NUnit.Framework;
using TheArtifacts.Core.Combat;
using TheArtifacts.Core.Guardian;

namespace TheArtifacts.Core.Tests.Combat
{
    /// <summary>Design guardrails for the general-item stat budget (GDD 6.6, Q12).</summary>
    public class ItemBudgetTests
    {
        [Test]
        public void FullMasterpieceStrengthSet_CannotTakeMoreThanHalfOfT1InOneHit()
        {
            // Worst case: every slot a top Masterpiece with all points in Strength, and a critical Overpower.
            var strength = PrototypeContent.Base.Strength + ItemBudget.EquipmentSlots * ItemBudget.MasterpieceMaxPoints;
            var t1 = PrototypeContent.Tier("T1");
            var hit = ItemBudget.StrongestHit(strength) * (1f - PlayerRules.Rating(t1.Def));

            Assert.That(hit, Is.LessThanOrEqualTo(ItemBudget.MaxBurstShareOfLowestTier * t1.MaxHp),
                $"strongest hit {hit:0} vs T1 HP {t1.MaxHp:0}");
        }

        [Test]
        public void Grades_RiseInOrder()
        {
            var points = new[] { GeneralGrade.Standard, GeneralGrade.Fine, GeneralGrade.Superior, GeneralGrade.Masterpiece }
                .Select(ItemBudget.Points).ToArray();
            Assert.That(points, Is.Ordered.Ascending);
            Assert.That(ItemBudget.MasterpieceMinPoints, Is.GreaterThan(ItemBudget.Points(GeneralGrade.Superior)));
            Assert.That(ItemBudget.MasterpieceMaxPoints, Is.GreaterThanOrEqualTo(ItemBudget.MasterpieceMinPoints));
        }

        [Test]
        public void EveryArtifact_MeetsItsGradeBudget_AboveTheBestMasterpiece()
        {
            // GDD 6.6 (2026-11-28): every artifact, Relic included, out-points the best Masterpiece, which never has a trait.
            foreach (var a in PrototypeContent.Artifacts)
            {
                Assert.That(ItemBudget.Points(a), Is.GreaterThanOrEqualTo(ItemBudget.ArtifactMinPoints(a.Rarity)), a.Id);
                Assert.That(ItemBudget.Points(a), Is.GreaterThan(ItemBudget.MasterpieceMaxPoints), a.Id);
            }
            var grades = new[] { Rarity.Relic, Rarity.Heirloom, Rarity.Treasure, Rarity.Mythic }.Select(ItemBudget.ArtifactMinPoints);
            Assert.That(grades, Is.Ordered.Ascending);
        }

        [Test]
        public void ALightPlayerWhoTravelsOnce_EdgesOutAHeavyPlayer_InPoints()
        {
            // GDD 6.6 monthly rule, worst case for the traveler: the trip yields three Relics. Their three slots would
            // otherwise hold the light player's general items (an even share of what 60 hours gives).
            var perSlot = ItemBudget.LightMonthPoints / ItemBudget.EquipmentSlots;
            var relic = ItemBudget.ArtifactMinPoints(Rarity.Relic) + ItemBudget.TraitPoints;
            var traveler = ItemBudget.LightMonthPoints + ItemBudget.LandmarksPerTrip * (relic - perSlot);

            Assert.That(traveler, Is.GreaterThan(ItemBudget.HeavyMonthPoints), $"traveler {traveler:0} vs heavy {ItemBudget.HeavyMonthPoints:0}");
            Assert.That(ItemBudget.HeavyMonthPoints, Is.LessThanOrEqualTo(ItemBudget.EquipmentSlots * ItemBudget.MasterpieceMaxPoints),
                "the heavy month stays under the general ceiling");
        }

        [Test]
        public void ALightPlayerWhoTravelsOnce_BeatsAHeavyPlayer_InAFight()
        {
            // The heavy player spreads general points about half to Strength, the rest to HP, Def, and Res. The traveler wears
            // the geared build's three artifacts (Relic, Heirloom, Treasure), mostly defensive, so puts the 60 hours' general
            // points, less the three slots the artifacts fill, into Strength. A bot with human-like slips (0.6 s reaction,
            // 30% of telegraphs missed) plays both, so staying alive counts as well as damage.
            var heavy = new PlayerBuild(General(ItemBudget.HeavyMonthPoints, 0.5f), new ArtifactDef[0]);
            var lightGeneral = ItemBudget.LightMonthPoints * (ItemBudget.EquipmentSlots - ItemBudget.LandmarksPerTrip) / ItemBudget.EquipmentSlots;
            var traveler = new PlayerBuild(General(lightGeneral, 1f), PrototypeContent.GearedBuild.Artifacts);

            float heavyTime = 0f, travelerTime = 0f;
            int heavyWins = 0, travelerWins = 0;
            for (var seed = 1; seed <= 5; seed++)
            foreach (var guardian in PrototypeContent.Guardians)
            {
                var setup = new GuardianSetup("T4", new[] { "cleave", "slam", "charge" }, "steady");
                var h = TestBot.Play(heavy, setup, seed, 0.6f, guardian: guardian, missChance: 0.3f);
                var t = TestBot.Play(traveler, setup, seed, 0.6f, guardian: guardian, missChance: 0.3f);
                if (h.Outcome == CombatOutcome.Win) heavyWins++;
                if (t.Outcome == CombatOutcome.Win) travelerWins++;
                heavyTime += h.Outcome == CombatOutcome.Win ? h.Time : 600f;
                travelerTime += t.Outcome == CombatOutcome.Win ? t.Time : 600f;
            }
            TestContext.WriteLine($"wins heavy {heavyWins}, traveler {travelerWins}; total time (a loss counts 600 s): heavy {heavyTime:0}s, traveler {travelerTime:0}s");
            Assert.That(travelerWins, Is.GreaterThanOrEqualTo(heavyWins));
            Assert.That(travelerTime, Is.LessThan(heavyTime));
        }

        static BaseStats General(float points, float strengthShare)
        {
            var b = PrototypeContent.Base;
            var rest = points * (1f - strengthShare);
            b.Strength += points * strengthShare;
            b.Hp += rest * 0.6f * ItemBudget.HpPerPoint;
            b.Def += rest * 0.2f;
            b.Res += rest * 0.2f;
            return b;
        }
    }
}
