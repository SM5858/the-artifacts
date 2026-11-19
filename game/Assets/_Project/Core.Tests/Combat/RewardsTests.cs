using System.Linq;
using NUnit.Framework;
using TheArtifacts.Core.Combat;

namespace TheArtifacts.Core.Tests.Combat
{
    public class RewardsTests
    {
        static ArtifactDef A(string id, Rarity rarity) => new ArtifactDef { Id = id, Name = id, Rarity = rarity };

        // Pools shaped like the three prototype landmarks (GDD 6.5).
        static readonly ArtifactDef[] LionLike = { A("fang", Rarity.Heirloom), A("pelt", Rarity.Mythic) };
        static readonly ArtifactDef[] BookstoreLike = { A("mortarboard", Rarity.Relic), A("robe", Rarity.Treasure) };
        static readonly ArtifactDef[] AlmaMaterLike = { A("wreath", Rarity.Relic), A("staff", Rarity.Relic) };

        [Test]
        public void GradeChances_AreTheDropTableRenormalizedOverThePool()
        {
            var lion = Rewards.GradeChances(LionLike, firstWin: false);
            Assert.That(lion[Rarity.Heirloom], Is.EqualTo(0.30 / 0.35).Within(1e-9));
            Assert.That(lion[Rarity.Mythic], Is.EqualTo(0.05 / 0.35).Within(1e-9));

            var store = Rewards.GradeChances(BookstoreLike, firstWin: false);
            Assert.That(store[Rarity.Relic], Is.EqualTo(0.50 / 0.65).Within(1e-9));
        }

        [Test]
        public void FirstWin_GivesHeirloomOrBetter_WhenThePoolHasOne()
        {
            Assert.That(Rewards.EligibleGrades(BookstoreLike, firstWin: true), Is.EqualTo(new[] { Rarity.Treasure }));
            Assert.That(Rewards.EligibleGrades(LionLike, firstWin: true), Is.EqualTo(new[] { Rarity.Heirloom, Rarity.Mythic }));
        }

        [Test]
        public void FirstWin_GivesThePoolsHighestGrade_WhenNothingIsHeirloomOrBetter()
        {
            Assert.That(Rewards.EligibleGrades(AlmaMaterLike, firstWin: true), Is.EqualTo(new[] { Rarity.Relic }));
        }

        [Test]
        public void Roll_FollowsTheChances_AndPicksWithinTheGrade()
        {
            var rng = new System.Random(7);
            var lion = Enumerable.Range(0, 20000).Select(_ => Rewards.Roll(LionLike, false, rng).Id).ToArray();
            Assert.That(lion.Count(id => id == "pelt") / 20000.0, Is.EqualTo(0.05 / 0.35).Within(0.01));

            var alma = Enumerable.Range(0, 2000).Select(_ => Rewards.Roll(AlmaMaterLike, false, rng).Id).ToArray();
            Assert.That(alma.Distinct().OrderBy(x => x), Is.EqualTo(new[] { "staff", "wreath" }));

            var firstStore = Enumerable.Range(0, 200).Select(_ => Rewards.Roll(BookstoreLike, true, rng).Id).Distinct();
            Assert.That(firstStore, Is.EqualTo(new[] { "robe" }));
        }
    }
}
