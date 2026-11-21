using System;
using System.Collections.Generic;

namespace TheArtifacts.Core.Account
{
    /// <summary>
    /// Everything the account keeps (GDD 6.1, 7): unlocked dungeons, the visits behind them, owned and equipped
    /// artifacts, and per-Guardian results. Plain public fields and lists so both Unity's JsonUtility (local saves) and
    /// Cloud Save can store it. Change it only through <see cref="Progress"/>.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        /// <summary>Guardian ids whose dungeon a verified visit has unlocked; each appears once.</summary>
        public List<string> UnlockedGuardians = new List<string>();
        /// <summary>Every check-in, including repeats, so a lost unlock can be traced (GDD 7, handoff failure).</summary>
        public List<VisitRecord> Visits = new List<VisitRecord>();
        public List<OwnedArtifact> Artifacts = new List<OwnedArtifact>();
        /// <summary>Equipped artifact ids, in equip order (the default skill bar follows it).</summary>
        public List<string> Equipped = new List<string>();
        public List<GuardianRecord> Guardians = new List<GuardianRecord>();
    }

    [Serializable]
    public sealed class VisitRecord
    {
        public string GuardianId;
        /// <summary>How the visit was verified: "gps", "npc", or "simulated" (development only).</summary>
        public string Source;
        /// <summary>UTC, ISO 8601.</summary>
        public string AtUtc;
    }

    [Serializable]
    public sealed class OwnedArtifact
    {
        public string Id;
        /// <summary>How many times it has dropped; only one copy can be equipped.</summary>
        public int Count;
    }

    [Serializable]
    public sealed class GuardianRecord
    {
        public string GuardianId;
        public int Wins;
        /// <summary>Failed attempts since the last win; sets the easing step (GDD 6.4).</summary>
        public int FailedSinceWin;
    }
}
