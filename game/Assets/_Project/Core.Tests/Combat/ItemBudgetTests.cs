using System.Linq;
using NUnit.Framework;
using TheArtifacts.Core.Combat;

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
        public void Masterpiece_StaysBelowEveryHeirloomOrHigherArtifact()
        {
            // GDD 6.6: a Masterpiece reaches just below Heirloom on stats and never has a trait.
            foreach (var a in PrototypeContent.Artifacts.Where(a => a.Rarity >= Rarity.Heirloom))
                Assert.That(ItemBudget.Points(a), Is.GreaterThan(ItemBudget.MasterpieceMaxPoints), a.Id);
        }
    }
}
