using System;
using System.Collections.Generic;
using System.Linq;

namespace TheArtifacts.Core.Combat
{
    /// <summary>
    /// Reward roll on a win (Combat Spec 5.1). The grade is rolled from the drop table, restricted to the grades
    /// the landmark's pool offers; then one artifact of that grade is picked. Rolls are hidden from the Guardian.
    /// </summary>
    public static class Rewards
    {
        /// <summary>Drop chance per grade before restricting to a pool (Combat Spec 5.1).</summary>
        public static readonly IReadOnlyDictionary<Rarity, double> DropChance = new Dictionary<Rarity, double>
        {
            { Rarity.Relic, 0.50 }, { Rarity.Heirloom, 0.30 }, { Rarity.Treasure, 0.15 }, { Rarity.Mythic, 0.05 },
        };

        /// <summary>
        /// Grades a win can give from this pool. A first win gives Heirloom or better when the pool has one;
        /// otherwise the pool's highest grade.
        /// </summary>
        public static IReadOnlyList<Rarity> EligibleGrades(IReadOnlyCollection<ArtifactDef> pool, bool firstWin)
        {
            var grades = pool.Select(a => a.Rarity).Distinct().OrderBy(g => g).ToList();
            if (!firstWin) return grades;
            var good = grades.Where(g => g >= Rarity.Heirloom).ToList();
            return good.Count > 0 ? good : new List<Rarity> { grades.Last() };
        }

        /// <summary>Chance of each eligible grade: the drop table renormalized over the eligible grades.</summary>
        public static IReadOnlyDictionary<Rarity, double> GradeChances(IReadOnlyCollection<ArtifactDef> pool, bool firstWin)
        {
            var grades = EligibleGrades(pool, firstWin);
            var total = grades.Sum(g => DropChance[g]);
            return grades.ToDictionary(g => g, g => DropChance[g] / total);
        }

        public static ArtifactDef Roll(IReadOnlyCollection<ArtifactDef> pool, bool firstWin, Random rng)
        {
            if (pool == null || pool.Count == 0) throw new ArgumentException("A loot pool needs at least one artifact.");

            var roll = rng.NextDouble();
            var chosen = Rarity.Relic;
            foreach (var kv in GradeChances(pool, firstWin).OrderBy(kv => kv.Key))
            {
                chosen = kv.Key;
                if (roll < kv.Value) break;
                roll -= kv.Value;
            }

            var candidates = pool.Where(a => a.Rarity == chosen).ToArray();
            return candidates[rng.Next(candidates.Length)];
        }
    }
}
