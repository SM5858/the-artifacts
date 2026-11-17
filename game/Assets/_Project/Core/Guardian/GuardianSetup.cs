using System;
using System.Collections.Generic;
using System.Linq;

namespace TheArtifacts.Core.Guardian
{
    /// <summary>
    /// One Guardian configuration: a stat tier, a skill set, and a strategy,
    /// each chosen from options the engine defines (Configuration Document 2.3).
    /// Used both for the model's proposal and for the default (scripted) Guardian.
    /// </summary>
    public sealed class GuardianSetup
    {
        public string StatTier { get; }
        public IReadOnlyList<string> SkillIds { get; }
        public string StrategyId { get; }

        public GuardianSetup(string statTier, IEnumerable<string> skillIds, string strategyId)
        {
            StatTier = statTier;
            SkillIds = skillIds?.ToArray();
            StrategyId = strategyId;
        }

        public override string ToString() =>
            $"tier={StatTier}, skills=[{(SkillIds == null ? "null" : string.Join(",", SkillIds))}], strategy={StrategyId}";
    }

    /// <summary>
    /// The options the engine allows for one challenger on one Guardian.
    /// Built by the engine from that Guardian's tuning range (GDD 6.4, Q11).
    /// The model only ever chooses inside this space; it never sees or changes the range itself.
    /// </summary>
    public sealed class AllowedSetupSpace
    {
        public IReadOnlyCollection<string> StatTiers { get; }
        public IReadOnlyCollection<string> SkillPool { get; }
        public IReadOnlyCollection<string> Strategies { get; }
        public int MinSkills { get; }
        public int MaxSkills { get; }

        public AllowedSetupSpace(
            IEnumerable<string> statTiers,
            IEnumerable<string> skillPool,
            IEnumerable<string> strategies,
            int minSkills,
            int maxSkills)
        {
            if (minSkills < 0 || maxSkills < minSkills)
                throw new ArgumentOutOfRangeException(nameof(maxSkills), "Need 0 <= minSkills <= maxSkills.");

            StatTiers = new HashSet<string>(statTiers ?? throw new ArgumentNullException(nameof(statTiers)));
            SkillPool = new HashSet<string>(skillPool ?? throw new ArgumentNullException(nameof(skillPool)));
            Strategies = new HashSet<string>(strategies ?? throw new ArgumentNullException(nameof(strategies)));
            MinSkills = minSkills;
            MaxSkills = maxSkills;
        }
    }
}
