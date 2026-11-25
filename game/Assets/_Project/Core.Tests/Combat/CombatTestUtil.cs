using System;
using System.Collections.Generic;
using System.Linq;
using TheArtifacts.Core.Combat;
using TheArtifacts.Core.Guardian;

namespace TheArtifacts.Core.Tests.Combat
{
    static class CombatTestUtil
    {
        public const float Dt = 1f / 30f;

        public static CombatSim Sim(PlayerBuild build, string tier, string strategy, params string[] skills) =>
            new CombatSim(build, new GuardianSetup(tier, skills, strategy), seed: 1);

        /// <summary>Steps until the condition holds or the time limit passes.</summary>
        public static void RunUntil(CombatSim sim, Func<CombatSim, bool> done, float maxSeconds,
            Func<CombatSim, PlayerInput> input = null)
        {
            var end = sim.Time + maxSeconds;
            while (!done(sim) && sim.Time < end && sim.Outcome == CombatOutcome.Ongoing)
                sim.Step(Dt, input?.Invoke(sim) ?? PlayerInput.None);
        }

        public static void RunFor(CombatSim sim, float seconds, Func<CombatSim, PlayerInput> input = null) =>
            RunUntil(sim, _ => false, seconds, input);

        /// <summary>Walks into melee range.</summary>
        public static void Engage(CombatSim sim) =>
            RunUntil(sim, s => s.DistanceToGuardian <= 2.6f, 5f,
                s => new PlayerInput((s.GuardianPosition - s.PlayerPosition).Normalized()));

        public static void Use(CombatSim sim, Ability ability) => sim.Step(Dt, new PlayerInput(Vec2.Zero, ability));

        /// <summary>Uses the ability and stands until every blow of it has landed (Double Strike's second, Overpower's wind-up, Lunge's leap).</summary>
        public static void Finish(CombatSim sim, Ability ability)
        {
            Use(sim, ability);
            RunUntil(sim, s => !s.IsCommitted, 2f);
        }

        public static float DamageTo(CombatSim sim, CombatEventType type, string id) =>
            sim.Events.Where(e => e.Type == type && e.Id == id).Sum(e => e.Amount);
    }

    /// <summary>
    /// A simple competent player for balance smoke tests: steps out of telegraphs by the quickest
    /// direction, turns auto-attack on, guards (or uses a ready artifact skill) for hits it cannot escape, punishes recovery
    /// windows, keeps melee uptime, and casts Lightning Bolt when out of melee or when melee skills are down.
    /// A bot that knows the place's lore keeps the fight within reach of an unburned lore target and answers
    /// a noticed tell by setting it alight, and aims at an exposed weak point (GDD 6.7).
    /// </summary>
    static class TestBot
    {
        static readonly Vec2[] Directions = Enumerable.Range(0, 16)
            .Select(i => new Vec2((float)Math.Cos(i * Math.PI / 8), (float)Math.Sin(i * Math.PI / 8)))
            .ToArray();

        public static PlayerInput Decide(CombatSim sim) => Decide(sim, 0f, null);

        /// <param name="knowsLore">Whether the bot uses lore targets: Fire Bolt on a bookshelf when it notices a tell.</param>
        public static PlayerInput Decide(CombatSim sim, float reactionTime, ISet<Telegraph> missed, bool knowsLore)
        {
            var input = Decide(sim, reactionTime, missed);
            if (!knowsLore) return input;
            var exposed = sim.Guardian.WeakPoints.FirstOrDefault(w => sim.IsWeakPointExposed(w.Id));
            if (exposed != null) input = new PlayerInput(input.Move, input.Ability, input.ArtifactSkill, exposed.Id);
            if (sim.Guardian.LoreTargets.Count == 0) return input;
            if (sim.Phase != GuardianPhase.Tell)
            {
                // Fight within reach of an unburned shelf; the Guardian follows the player there.
                var shelf = sim.Guardian.LoreTargets.Where(t => !sim.IsLoreSpent(t.Id))
                    .OrderBy(t => Vec2.Distance(t.Position, sim.PlayerPosition)).FirstOrDefault();
                if (shelf == null || sim.ActiveTelegraphs.Any() || Vec2.Distance(shelf.Position, sim.PlayerPosition) <= PlayerRules.FireBoltRange - 3f)
                    return input;
                return new PlayerInput((shelf.Position - sim.PlayerPosition).Normalized(), input.Ability, input.ArtifactSkill);
            }
            var noticed = sim.ActiveTelegraphs.Any(t => sim.Time - (t.HitTime - t.Tell) >= reactionTime && (missed == null || !missed.Contains(t)));
            var lore = sim.Guardian.LoreTargets.FirstOrDefault(t => sim.BlockReason(Ability.FireBolt, t.Id) == null);
            return noticed && lore != null ? new PlayerInput(input.Move, Ability.FireBolt, input.ArtifactSkill, lore.Id) : input;
        }

        /// <param name="reactionTime">Seconds after a telegraph appears before the bot notices it.</param>
        /// <param name="missed">Telegraphs the bot fails to notice at all.</param>
        public static PlayerInput Decide(CombatSim sim, float reactionTime, ISet<Telegraph> missed)
        {
            var pos = sim.PlayerPosition;
            var threats = sim.ActiveTelegraphs
                .Where(t => t.Area != null && sim.Time - (t.HitTime - t.Tell) >= reactionTime)
                .Where(t => missed == null || !missed.Contains(t))
                .ToArray();
            bool Danger(Vec2 p) => threats.Any(t => t.Area.Contains(p));

            if (Danger(pos))
            {
                // Pick the direction that leaves every telegraph soonest; prefer staying near the Guardian.
                Vec2 best = Vec2.Zero;
                var bestTime = float.MaxValue;
                var bestDist = float.MaxValue;
                foreach (var dir in Directions)
                {
                    for (var t = 0.05f; t <= 2f; t += 0.05f)
                    {
                        var p = pos + dir * (PlayerRules.RunSpeed * t);
                        if (Danger(p)) continue;
                        var d = Vec2.Distance(p, sim.GuardianPosition);
                        if (t < bestTime - 1e-4f || (Math.Abs(t - bestTime) < 1e-4f && d < bestDist))
                        {
                            best = dir; bestTime = t; bestDist = d;
                        }
                        break;
                    }
                }

                var nextHit = threats.Where(t => t.Area.Contains(pos)).Min(t => t.HitTime) - sim.Time;
                var cannotEscape = bestTime > nextHit;
                var ability = cannotEscape && sim.BlockReason(Ability.Guard) == null ? Ability.Guard : Ability.None;
                // Otherwise a ready artifact skill (the Robe's damage cut in the geared build).
                var equipment = cannotEscape && ability == Ability.None
                    ? Enumerable.Range(1, PlayerRules.SkillBarSize).Select(i => (SkillBarSlot)i)
                        .FirstOrDefault(s => sim.ArtifactSkillBlockReason(s) == null)
                    : SkillBarSlot.None;
                return new PlayerInput(best, ability, equipment);
            }

            Ability act = Ability.None;
            if (sim.InMeleeRange)
            {
                // Save Overpower for an opening, where its crit chance is highest.
                if (sim.IsOpening && sim.BlockReason(Ability.Overpower) == null) act = Ability.Overpower;
                else if (sim.BlockReason(Ability.DoubleStrike) == null) act = Ability.DoubleStrike;
            }
            else if (sim.DistanceToGuardian > 6f && !threats.Any() && sim.BlockReason(Ability.Lunge) == null)
            {
                act = Ability.Lunge;
            }
            if (act == Ability.None && sim.BlockReason(Ability.LightningBolt) == null) act = Ability.LightningBolt;

            var move = Vec2.Zero;
            if (sim.DistanceToGuardian > 2.6f)
            {
                var toward = (sim.GuardianPosition - pos).Normalized();
                if (!Danger(pos + toward * (PlayerRules.RunSpeed * CombatTestUtil.Dt))) move = toward;
            }
            return new PlayerInput(move, act);
        }

        /// <param name="missChance">Chance the bot never notices a given telegraph, standing in for human mistakes.</param>
        public static CombatSim Play(PlayerBuild build, GuardianSetup setup, int seed,
            float reactionTime = 0f, float maxSeconds = 600f, GuardianProfile guardian = null, float missChance = 0f,
            bool knowsLore = false)
        {
            var sim = new CombatSim(build, setup, seed, guardian);
            var rng = new Random(seed * 7919 + 17);
            var seen = new HashSet<Telegraph>();
            var missed = new HashSet<Telegraph>();
            CombatTestUtil.RunFor(sim, maxSeconds, s =>
            {
                foreach (var t in s.ActiveTelegraphs)
                    if (seen.Add(t) && rng.NextDouble() < missChance) missed.Add(t);
                var input = Decide(s, reactionTime, missed, knowsLore);
                return s.AutoAttacking ? input : input.WithToggleAutoAttack();
            });
            return sim;
        }
    }
}
