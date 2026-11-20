namespace TheArtifacts.Core.Guardian
{
    /// <summary>
    /// The setup a fight uses plus its trace entry (Configuration Document 2.9). The Cloud Code module returns it
    /// and the game client reads it, so both sides share this one type.
    /// </summary>
    public sealed class GuardianDecision
    {
        public string StatTier { get; set; }
        public string[] SkillIds { get; set; }
        public string StrategyId { get; set; }
        public bool Adaptive { get; set; }
        public bool UsedFallback { get; set; }
        public string FallbackReason { get; set; }
        public string ProposedSetup { get; set; }
        public string SnapshotHash { get; set; }
        public int GearScore { get; set; }
        public int AttemptsOnThisGuardian { get; set; }
        public string GuardianId { get; set; }
        /// <summary>The single easing step is unlocked (GDD 6.4): enough failed attempts on this Guardian.</summary>
        public bool Eased { get; set; }
        /// <summary>The Guardian's line at fight start (adaptive mode only), or null.</summary>
        public string Announcement { get; set; }
        public string Model { get; set; }
        public long LatencyMs { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public string StopReason { get; set; }

        public GuardianSetup ToSetup() => new GuardianSetup(StatTier, SkillIds, StrategyId);
    }
}
