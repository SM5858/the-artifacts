using System.Collections.Generic;
using System.Linq;

namespace TheArtifacts.Core.Guardian
{
    /// <summary>
    /// The only player information the Guardian model may see (Configuration Document 2.2):
    /// stats, skills, equipped artifacts, and attempts so far on this Guardian.
    /// Never add GPS / location history, private player data, reward rolls, or unequipped inventory here;
    /// a test fails if any other property appears.
    /// </summary>
    public sealed class ChallengerSnapshot
    {
        public IReadOnlyDictionary<string, int> Stats { get; }
        public IReadOnlyList<string> SkillIds { get; }
        public IReadOnlyList<string> EquippedArtifactIds { get; }
        public int AttemptsOnThisGuardian { get; }

        public ChallengerSnapshot(
            IDictionary<string, int> stats,
            IEnumerable<string> skillIds,
            IEnumerable<string> equippedArtifactIds,
            int attemptsOnThisGuardian)
        {
            Stats = new Dictionary<string, int>(stats ?? new Dictionary<string, int>());
            SkillIds = (skillIds ?? Enumerable.Empty<string>()).ToArray();
            EquippedArtifactIds = (equippedArtifactIds ?? Enumerable.Empty<string>()).ToArray();
            AttemptsOnThisGuardian = attemptsOnThisGuardian;
        }
    }
}
