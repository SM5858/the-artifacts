using System;
using System.Linq;
using NUnit.Framework;
using TheArtifacts.Core.Account;
using TheArtifacts.Core.Combat;

namespace TheArtifacts.Core.Tests.Account
{
    public class ProgressTests
    {
        const string Lion = "columbia_lion";
        const string Bookstore = "book_keeper";
        static readonly DateTime Now = new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

        static PlayerProfile Unlocked(params string[] guardianIds)
        {
            var p = new PlayerProfile();
            foreach (var id in guardianIds) Progress.CheckIn(p, id, "simulated", Now);
            return p;
        }

        [Test]
        public void CheckIn_UnlocksTheDungeon_AndARepeatAddsNoSecondQuest()
        {
            var p = new PlayerProfile();

            Assert.That(Progress.CheckIn(p, Lion, "simulated", Now), Is.EqualTo(CheckInResult.Unlocked));
            Assert.That(Progress.CheckIn(p, Lion, "simulated", Now.AddHours(1)), Is.EqualTo(CheckInResult.AlreadyUnlocked));

            Assert.That(p.UnlockedGuardians, Is.EqualTo(new[] { Lion }));
            Assert.That(p.Visits.Count, Is.EqualTo(2), "every visit is kept so a lost unlock can be traced");
            Assert.That(p.Visits[0].AtUtc, Does.StartWith("2026-11-20T15:00:00"));
        }

        [Test]
        public void CheckIn_AtAnUnknownLandmark_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => Progress.CheckIn(new PlayerProfile(), "eiffel_tower", "simulated", Now));
        }

        [Test]
        public void Fights_NeedAnUnlockedDungeon()
        {
            var p = Unlocked(Bookstore);

            Assert.Throws<InvalidOperationException>(() => Progress.RecordLoss(p, Lion));
            Assert.Throws<InvalidOperationException>(() => Progress.RecordWin(p, Lion, new Random(1), Now));
        }

        [Test]
        public void Losses_CountUntilAWin_WhichGrantsAnArtifactFromThePool()
        {
            var p = Unlocked(Lion);
            Progress.RecordLoss(p, Lion);
            Progress.RecordLoss(p, Lion);
            Assert.That(Progress.FailedAttempts(p, Lion), Is.EqualTo(2));

            var artifact = Progress.RecordWin(p, Lion, new Random(1), Now);

            Assert.That(PrototypeContent.GuardianById(Lion).LootPool, Does.Contain(artifact.Id));
            Assert.That(Progress.Owns(p, artifact.Id), Is.True);
            Assert.That(Progress.FailedAttempts(p, Lion), Is.EqualTo(0));
            Assert.That(Progress.Record(p, Lion).Wins, Is.EqualTo(1));
        }

        [Test]
        public void FirstWin_AtTheBookstore_GivesTheRobe()
        {
            // Combat Spec 5.1: a first win gives Heirloom or better when the pool has one (the bookstore's is the Robe).
            for (var seed = 0; seed < 20; seed++)
                Assert.That(Progress.RecordWin(Unlocked(Bookstore), Bookstore, new Random(seed), Now).Id, Is.EqualTo("columbia_blue_robe"));
        }

        [Test]
        public void RepeatDrops_StackInsteadOfDuplicating()
        {
            var p = Unlocked(Bookstore);
            for (var i = 0; i < 30; i++)
            {
                var week = Now.AddDays(7 * i);
                Progress.CheckIn(p, Bookstore, "simulated", week);
                Progress.RecordWin(p, Bookstore, new Random(i), week);
            }

            Assert.That(p.Artifacts.Select(a => a.Id), Is.Unique);
            Assert.That(p.Artifacts.Sum(a => a.Count), Is.EqualTo(30));
        }

        [Test]
        public void AWin_ClosesTheDungeon_ForAWeek_ThenAVisitReopensIt()
        {
            var p = Unlocked(Lion);
            Progress.RecordLoss(p, Lion);
            Assert.That(Progress.State(p, Lion, Now), Is.EqualTo(DungeonState.Open), "a loss leaves it open");

            Progress.RecordWin(p, Lion, new Random(1), Now);
            Assert.That(Progress.State(p, Lion, Now.AddDays(6)), Is.EqualTo(DungeonState.Resting));
            Assert.Throws<InvalidOperationException>(() => Progress.RecordWin(p, Lion, new Random(2), Now.AddDays(1)), "no rematch");

            Assert.That(Progress.CheckIn(p, Lion, "gps", Now.AddDays(6.9)), Is.EqualTo(CheckInResult.TooSoon));
            Assert.That(Progress.State(p, Lion, Now.AddDays(6.9)), Is.EqualTo(DungeonState.Resting), "a visit too soon does not open it");

            Assert.That(Progress.State(p, Lion, Now.AddDays(7)), Is.EqualTo(DungeonState.Revisit), "the week alone does not reopen it");
            Assert.That(Progress.CheckIn(p, Lion, "gps", Now.AddDays(7)), Is.EqualTo(CheckInResult.Unlocked));
            Assert.That(Progress.State(p, Lion, Now.AddDays(7)), Is.EqualTo(DungeonState.Open));
            Assert.That(Progress.RecordWin(p, Lion, new Random(3), Now.AddDays(8)), Is.Not.Null);
            Assert.That(p.Visits.Count, Is.EqualTo(3), "every visit is kept, the early one too");
        }

        [Test]
        public void LastWin_SurvivesASaveAndLoad()
        {
            var p = Unlocked(Lion);
            Progress.RecordWin(p, Lion, new Random(1), Now);
            var copy = new PlayerProfile();
            copy.Guardians.Add(new GuardianRecord { GuardianId = Lion, Wins = 1, LastWinUtc = Progress.Record(p, Lion).LastWinUtc });

            Assert.That(Progress.LastWin(copy, Lion), Is.EqualTo(Now));
            Assert.That(Progress.State(copy, Lion, Now.AddDays(3)), Is.EqualTo(DungeonState.Resting));
        }

        [Test]
        public void Equip_OnlyOwnedArtifacts_OnePerSlot()
        {
            var p = new PlayerProfile();
            p.Artifacts.Add(new OwnedArtifact { Id = "columbia_lions_fang", Count = 1 });
            p.Artifacts.Add(new OwnedArtifact { Id = "staff_of_alma_mater", Count = 1 });

            Assert.That(Progress.TryEquip(p, new[] { "columbia_blue_robe" }), Does.Contain("do not own"));
            Assert.That(Progress.TryEquip(p, new[] { "columbia_lions_fang", "staff_of_alma_mater" }), Is.Not.Null, "both are right-hand");
            Assert.That(Progress.TryEquip(p, new[] { "columbia_lions_fang" }), Is.Null);
            Assert.That(Progress.Build(p).Artifacts.Single().Id, Is.EqualTo("columbia_lions_fang"));
        }

        [Test]
        public void ToggleEquip_ReplacesTheSameSlot_AndTakesOff()
        {
            var p = new PlayerProfile();
            p.Artifacts.Add(new OwnedArtifact { Id = "columbia_lions_fang", Count = 1 });
            p.Artifacts.Add(new OwnedArtifact { Id = "staff_of_alma_mater", Count = 1 });
            p.Artifacts.Add(new OwnedArtifact { Id = "columbia_blue_robe", Count = 1 });

            Assert.That(Progress.ToggleEquip(p, "columbia_lions_fang"), Is.Null);
            Assert.That(Progress.ToggleEquip(p, "columbia_blue_robe"), Is.Null);
            Assert.That(Progress.ToggleEquip(p, "staff_of_alma_mater"), Is.Null);
            Assert.That(p.Equipped, Is.EqualTo(new[] { "columbia_blue_robe", "staff_of_alma_mater" }));

            Assert.That(Progress.ToggleEquip(p, "columbia_blue_robe"), Is.Null);
            Assert.That(p.Equipped, Is.EqualTo(new[] { "staff_of_alma_mater" }));
        }

        [Test]
        public void EveryGuardian_HasAStoryCard()
        {
            foreach (var g in PrototypeContent.Guardians)
                Assert.That(g.StoryCard, Is.Not.Null.And.Not.Empty, g.Id);
        }

        [TestCase("ab", "Passw0rd!", "Username")]
        [TestCase("player one", "Passw0rd!", "Username")]
        [TestCase("player1", "short1!", "Password")]
        [TestCase("player1", "password1!", "uppercase")]
        [TestCase("player1", "Password1", "symbol")]
        public void AccountRules_RejectWeakCredentials(string username, string password, string expected)
        {
            Assert.That(AccountRules.Validate(username, password), Does.Contain(expected));
        }

        [Test]
        public void AccountRules_AcceptAValidPair()
        {
            Assert.That(AccountRules.Validate("lion_tamer", "Passw0rd!"), Is.Null);
        }
    }
}
