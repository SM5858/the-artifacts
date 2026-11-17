using System;
using System.Collections.Generic;
using System.Linq;

namespace TheArtifacts.Core.Combat
{
    /// <summary>What a strategy may look at when choosing the next skill.</summary>
    public readonly struct StrategyContext
    {
        public readonly IReadOnlyList<string> SkillIds;
        public readonly float DistanceToPlayer;
        public readonly Random Rng;

        public StrategyContext(IReadOnlyList<string> skillIds, float distanceToPlayer, Random rng)
        {
            SkillIds = skillIds;
            DistanceToPlayer = distanceToPlayer;
            Rng = rng;
        }
    }

    /// <summary>
    /// Decides only when and which skill the Guardian uses (Combat Spec 4.3).
    /// Tells, damage, and recovery belong to the skill and never change.
    /// </summary>
    public abstract class GuardianStrategy
    {
        /// <summary>Seconds from one skill start to the next.</summary>
        public abstract float Interval { get; }

        /// <summary>Skills to use now, in order (two for Combo, otherwise one).</summary>
        public abstract IReadOnlyList<string> Pick(StrategyContext ctx);

        /// <summary>Gap between the first skill's recovery and the second skill's tell (Combo).</summary>
        public const float ComboGap = 0.5f;

        public static GuardianStrategy Create(string id)
        {
            switch (id)
            {
                case "steady": return new Steady();
                case "aggressive": return new Aggressive();
                case "combo": return new Combo();
                case "reactive": return new Reactive();
                default: throw new ArgumentException($"Unknown strategy '{id}'.");
            }
        }

        sealed class Steady : GuardianStrategy
        {
            int _next;
            public override float Interval => 8f;

            public override IReadOnlyList<string> Pick(StrategyContext ctx)
            {
                var id = ctx.SkillIds[_next % ctx.SkillIds.Count];
                _next++;
                return new[] { id };
            }
        }

        sealed class Aggressive : GuardianStrategy
        {
            const int FavoredWeight = 3;
            public override float Interval => 5.5f;

            public override IReadOnlyList<string> Pick(StrategyContext ctx)
            {
                var weighted = ctx.SkillIds
                    .SelectMany(id => Enumerable.Repeat(id, id == "cleave" || id == "charge" ? FavoredWeight : 1))
                    .ToArray();
                return new[] { weighted[ctx.Rng.Next(weighted.Length)] };
            }
        }

        sealed class Combo : GuardianStrategy
        {
            int _next;
            public override float Interval => 12f;

            public override IReadOnlyList<string> Pick(StrategyContext ctx)
            {
                var first = ctx.SkillIds[_next % ctx.SkillIds.Count];
                var second = ctx.SkillIds[(_next + 1) % ctx.SkillIds.Count];
                _next += 2;
                return new[] { first, second };
            }
        }

        sealed class Reactive : GuardianStrategy
        {
            const float FarDistance = 8f;
            int _fallback;
            public override float Interval => 7f;

            public override IReadOnlyList<string> Pick(StrategyContext ctx)
            {
                var wanted = ctx.DistanceToPlayer > FarDistance
                    ? new[] { "charge", "embers" }
                    : new[] { "slam", "rings" };
                var available = wanted.Where(ctx.SkillIds.Contains).ToArray();
                if (available.Length > 0)
                    return new[] { available[ctx.Rng.Next(available.Length)] };

                // None of the preferred skills are in this setup: rotate through what it has.
                var id = ctx.SkillIds[_fallback % ctx.SkillIds.Count];
                _fallback++;
                return new[] { id };
            }
        }
    }
}
