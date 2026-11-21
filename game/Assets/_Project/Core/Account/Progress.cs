using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TheArtifacts.Core.Combat;

namespace TheArtifacts.Core.Account
{
    public enum CheckInResult { Unlocked, AlreadyUnlocked }

    /// <summary>The rules that change a <see cref="PlayerProfile"/>: unlock → win → equip → retry (GDD 2, 6.1, 7).</summary>
    public static class Progress
    {
        /// <summary>
        /// A verified visit unlocks the landmark's dungeon. A repeat visit is recorded but adds no second quest
        /// (GDD 7). How the visit is verified (radius, cooldown, spoofing; Q2) is the check-in client's job.
        /// </summary>
        public static CheckInResult CheckIn(PlayerProfile profile, string guardianId, string source, DateTime utcNow)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            PrototypeContent.GuardianById(guardianId);  // throws on an unknown landmark
            profile.Visits.Add(new VisitRecord
            {
                GuardianId = guardianId, Source = source, AtUtc = utcNow.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            });
            if (profile.UnlockedGuardians.Contains(guardianId)) return CheckInResult.AlreadyUnlocked;
            profile.UnlockedGuardians.Add(guardianId);
            return CheckInResult.Unlocked;
        }

        public static bool IsUnlocked(PlayerProfile profile, string guardianId) => profile.UnlockedGuardians.Contains(guardianId);

        public static GuardianRecord Record(PlayerProfile profile, string guardianId) =>
            profile.Guardians.FirstOrDefault(g => g.GuardianId == guardianId) ?? new GuardianRecord { GuardianId = guardianId };

        public static int FailedAttempts(PlayerProfile profile, string guardianId) => Record(profile, guardianId).FailedSinceWin;

        /// <summary>
        /// A loss counts toward easing. A forfeit (closing the client mid-fight) is not reported, so it does not
        /// count (GDD 7, Q6).
        /// </summary>
        public static void RecordLoss(PlayerProfile profile, string guardianId)
        {
            RequireUnlocked(profile, guardianId);
            Upsert(profile, guardianId).FailedSinceWin++;
        }

        /// <summary>
        /// A win rolls this landmark's reward (Combat Spec 5.1; the first win on this Guardian is the better roll),
        /// stores it on the account, and resets the failed attempts. Returns the artifact.
        /// </summary>
        public static ArtifactDef RecordWin(PlayerProfile profile, string guardianId, Random rng)
        {
            RequireUnlocked(profile, guardianId);
            var record = Upsert(profile, guardianId);
            var artifact = Rewards.Roll(PrototypeContent.LootPool(guardianId), firstWin: record.Wins == 0, rng);
            record.Wins++;
            record.FailedSinceWin = 0;

            var owned = profile.Artifacts.FirstOrDefault(a => a.Id == artifact.Id);
            if (owned == null) profile.Artifacts.Add(new OwnedArtifact { Id = artifact.Id, Count = 1 });
            else owned.Count++;
            return artifact;
        }

        public static bool Owns(PlayerProfile profile, string artifactId) => profile.Artifacts.Any(a => a.Id == artifactId);

        /// <summary>Equips exactly these artifacts, in this order. Returns null on success, or why it was refused.</summary>
        public static string TryEquip(PlayerProfile profile, IEnumerable<string> artifactIds)
        {
            var ids = (artifactIds ?? Enumerable.Empty<string>()).ToList();
            if (ids.Distinct().Count() != ids.Count) return "An artifact can be equipped only once.";
            var missing = ids.FirstOrDefault(id => !Owns(profile, id));
            if (missing != null) return $"You do not own '{missing}'.";
            try { PrototypeContent.Build(ids.ToArray()); }
            catch (ArgumentException e) { return e.Message; }
            profile.Equipped = ids;
            return null;
        }

        /// <summary>
        /// Puts an owned artifact on, replacing whatever filled its slot (a trinket replaces the older trinket when
        /// both are taken), or takes it off if it is on. Returns null on success, or why it was refused.
        /// </summary>
        public static string ToggleEquip(PlayerProfile profile, string artifactId)
        {
            if (profile.Equipped.Contains(artifactId))
                return TryEquip(profile, profile.Equipped.Where(id => id != artifactId));

            var slot = PrototypeContent.Artifact(artifactId).Slot;
            var next = profile.Equipped.ToList();
            var sameSlot = next.Where(id => PrototypeContent.Artifact(id).Slot == slot).ToList();
            if (sameSlot.Count >= PlayerBuild.SlotCapacity(slot)) next.Remove(sameSlot[0]);
            next.Add(artifactId);
            return TryEquip(profile, next);
        }

        public static PlayerBuild Build(PlayerProfile profile) => PrototypeContent.Build(profile.Equipped.ToArray());

        static void RequireUnlocked(PlayerProfile profile, string guardianId)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!IsUnlocked(profile, guardianId))
                throw new InvalidOperationException($"'{guardianId}' is not unlocked; visit its landmark first.");
        }

        static GuardianRecord Upsert(PlayerProfile profile, string guardianId)
        {
            var record = profile.Guardians.FirstOrDefault(g => g.GuardianId == guardianId);
            if (record != null) return record;
            record = new GuardianRecord { GuardianId = guardianId };
            profile.Guardians.Add(record);
            return record;
        }
    }
}
