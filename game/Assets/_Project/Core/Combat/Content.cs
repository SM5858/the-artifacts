using System;
using System.Collections.Generic;
using System.Linq;
using TheArtifacts.Core.Guardian;

namespace TheArtifacts.Core.Combat
{
    /// <summary>Player rules from Combat Spec 2 and 3.2.</summary>
    public static class PlayerRules
    {
        public const float GlobalCooldown = 1.0f;
        public const float RunSpeed = 7f;
        public const float MeleeRange = 3f;
        public const float ArmorCap = 0.5f;

        public const float AutoSwing = 2.0f;
        public const float AutoCoefficient = 1.0f;

        public const float HeavyStrikeCooldown = 6f;
        public const float HeavyStrikeCoefficient = 2.5f;

        public const float GuardCooldown = 12f;
        public const float GuardDuration = 2f;
        public const float GuardReduction = 0.6f;

        public const float LungeCooldown = 10f;
        public const float LungeDistance = 8f;
        public const float LungeCoefficient = 1.0f;

        public const float PunishCooldown = 15f;
        public const float PunishCoefficient = 4.0f;
        public const float PunishRecoveryMultiplier = 2f;
    }

    /// <summary>
    /// Guardian rules from Combat Spec 4.1. Values marked "implementation default" are not in the spec
    /// and are for the Design Lead to confirm.
    /// </summary>
    public static class GuardianRules
    {
        public const float AutoBaseDamage = 20f;
        public const float AutoSwing = 2.5f;
        public const float MeleeRange = 3f;

        /// <summary>Implementation default: slower than the player's 7 m/s so evading is always possible.</summary>
        public const float MoveSpeed = 5f;
        /// <summary>Implementation default: where the Guardian stops when chasing.</summary>
        public const float ChaseStopDistance = 2.5f;
        /// <summary>Implementation default: circular arena radius.</summary>
        public const float ArenaRadius = 20f;
        /// <summary>Implementation default: player starts this far from the Guardian.</summary>
        public const float StartDistance = 12f;
    }

    public enum AreaShape { None, Cone, Circle, Line, Ring, CirclesOnPlayer }

    /// <summary>One Guardian skill: tell → hit → recovery (Combat Spec 4.2).</summary>
    public sealed class GuardianSkillDef
    {
        public string Id;
        public string Name;
        public AreaShape Shape;
        public float Range;          // cone range, circle/ring outer radius, line length, ember radius
        public float InnerRadius;    // ring only
        public float Width;          // line only
        public float AngleDegrees;   // cone only
        public float Tell;
        public float BaseDamage;
        public float Recovery;
        public int Pulses = 1;          // embers: number of circles
        public float PulseInterval;     // embers: seconds between placements
        public float BuffDuration;      // fury
        public float BuffAutoMultiplier = 1f;
    }

    public sealed class TierDef
    {
        public string Id;
        public float MaxHp;
        public float DamageMultiplier;
    }

    /// <summary>Equipment slots (Combat Spec 5.2). Each has an equipment-skill key; Trinket has two slots (D, C).</summary>
    public enum ArtifactSlot { Helm, Armor, LeftHand, RightHand, Gloves, Trinket, Boots }

    public enum Rarity { Relic = 1, Heirloom = 2, Treasure = 3, Wonder = 4 }

    public enum ArtifactEffect
    {
        None,
        /// <summary>Keen Edge: Heavy Strike cooldown 6 → 4.5 s.</summary>
        HeavyStrikeCooldown45,
        /// <summary>Warden's Plate: Guard also heals 5% max HP.</summary>
        GuardHeals5Percent,
        /// <summary>Echo Blade: Punish in a recovery window resets Lunge.</summary>
        PunishInRecoveryResetsLunge,
        /// <summary>Stillstone: Guard lasts 3 s and reflects 30% of blocked damage.</summary>
        GuardLongerAndReflects,
    }

    public sealed class ArtifactDef
    {
        public string Id;
        public string Name;
        public ArtifactSlot Slot;
        public Rarity Rarity;
        public float Hp;
        public float Power;
        public float Armor;
        public ArtifactEffect Effect;
    }

    /// <summary>A player's final numbers after artifacts (Combat Spec 3.3).</summary>
    public sealed class PlayerBuild
    {
        public float MaxHp { get; }
        public float Power { get; }
        public float Armor { get; }
        public IReadOnlyList<ArtifactDef> Artifacts { get; }
        public int GearScore => Artifacts.Sum(a => (int)a.Rarity);

        public PlayerBuild(float baseHp, float basePower, float baseArmor, IEnumerable<ArtifactDef> artifacts)
        {
            Artifacts = (artifacts ?? Enumerable.Empty<ArtifactDef>()).ToArray();
            if (Artifacts.GroupBy(a => a.Slot).Any(g => g.Count() > SlotCapacity(g.Key)))
                throw new ArgumentException("At most one artifact per slot (two trinkets).");

            MaxHp = baseHp + Artifacts.Sum(a => a.Hp);
            Power = basePower + Artifacts.Sum(a => a.Power);
            Armor = Math.Min(PlayerRules.ArmorCap, baseArmor + Artifacts.Sum(a => a.Armor));
        }

        public bool Has(ArtifactEffect effect) => Artifacts.Any(a => a.Effect == effect);

        public static int SlotCapacity(ArtifactSlot slot) => slot == ArtifactSlot.Trinket ? 2 : 1;
    }

    /// <summary>All prototype content from the Combat Spec, in one place.</summary>
    public static class PrototypeContent
    {
        public const float BaseHp = 1000f;
        public const float BasePower = 40f;
        public const float BaseArmor = 0f;

        public static readonly IReadOnlyList<TierDef> Tiers = new[]
        {
            new TierDef { Id = "T1", MaxHp = 3000f, DamageMultiplier = 0.80f },
            new TierDef { Id = "T2", MaxHp = 3600f, DamageMultiplier = 0.90f },
            new TierDef { Id = "T3", MaxHp = 4300f, DamageMultiplier = 1.00f },
            new TierDef { Id = "T4", MaxHp = 5200f, DamageMultiplier = 1.15f },
            new TierDef { Id = "T5", MaxHp = 6200f, DamageMultiplier = 1.30f },
        };

        public static readonly IReadOnlyList<GuardianSkillDef> Skills = new[]
        {
            new GuardianSkillDef { Id = "cleave", Name = "Cleave", Shape = AreaShape.Cone, AngleDegrees = 90f, Range = 6f, Tell = 1.2f, BaseDamage = 200f, Recovery = 1.0f },
            new GuardianSkillDef { Id = "slam", Name = "Slam", Shape = AreaShape.Circle, Range = 5f, Tell = 1.6f, BaseDamage = 250f, Recovery = 1.5f },
            new GuardianSkillDef { Id = "charge", Name = "Charge", Shape = AreaShape.Line, Range = 15f, Width = 3f, Tell = 1.4f, BaseDamage = 220f, Recovery = 2.0f },
            new GuardianSkillDef { Id = "rings", Name = "Shock Rings", Shape = AreaShape.Ring, InnerRadius = 4f, Range = 12f, Tell = 1.8f, BaseDamage = 180f, Recovery = 1.2f },
            new GuardianSkillDef { Id = "embers", Name = "Embers", Shape = AreaShape.CirclesOnPlayer, Range = 3f, Tell = 1.0f, BaseDamage = 120f, Recovery = 0.8f, Pulses = 3, PulseInterval = 0.6f },
            new GuardianSkillDef { Id = "fury", Name = "Fury", Shape = AreaShape.None, Tell = 1.0f, Recovery = 0f, BuffDuration = 8f, BuffAutoMultiplier = 1.5f },
        };

        public static readonly IReadOnlyList<string> StrategyIds = new[] { "steady", "aggressive", "combo", "reactive" };

        public static readonly IReadOnlyList<ArtifactDef> Artifacts = new[]
        {
            new ArtifactDef { Id = "worn_band", Name = "Worn Band", Slot = ArtifactSlot.Trinket, Rarity = Rarity.Relic, Power = 5f },
            new ArtifactDef { Id = "padded_vest", Name = "Padded Vest", Slot = ArtifactSlot.Armor, Rarity = Rarity.Relic, Hp = 100f },
            new ArtifactDef { Id = "iron_charm", Name = "Iron Charm", Slot = ArtifactSlot.Trinket, Rarity = Rarity.Heirloom, Hp = 100f, Armor = 0.05f },
            new ArtifactDef { Id = "scaled_coat", Name = "Scaled Coat", Slot = ArtifactSlot.Armor, Rarity = Rarity.Heirloom, Hp = 200f, Armor = 0.10f },
            new ArtifactDef { Id = "keen_edge", Name = "Keen Edge", Slot = ArtifactSlot.RightHand, Rarity = Rarity.Treasure, Power = 20f, Effect = ArtifactEffect.HeavyStrikeCooldown45 },
            new ArtifactDef { Id = "wardens_plate", Name = "Warden's Plate", Slot = ArtifactSlot.Armor, Rarity = Rarity.Treasure, Hp = 150f, Armor = 0.10f, Effect = ArtifactEffect.GuardHeals5Percent },
            new ArtifactDef { Id = "echo_blade", Name = "Echo Blade", Slot = ArtifactSlot.RightHand, Rarity = Rarity.Wonder, Power = 25f, Effect = ArtifactEffect.PunishInRecoveryResetsLunge },
            new ArtifactDef { Id = "stillstone", Name = "Stillstone", Slot = ArtifactSlot.Trinket, Rarity = Rarity.Wonder, Hp = 150f, Effect = ArtifactEffect.GuardLongerAndReflects },
        };

        public static TierDef Tier(string id) => Tiers.FirstOrDefault(t => t.Id == id);
        public static GuardianSkillDef Skill(string id) => Skills.FirstOrDefault(s => s.Id == id);
        public static ArtifactDef Artifact(string id) => Artifacts.FirstOrDefault(a => a.Id == id)
            ?? throw new ArgumentException($"Unknown artifact '{id}'.");

        public static PlayerBuild Build(params string[] artifactIds) =>
            new PlayerBuild(BaseHp, BasePower, BaseArmor, artifactIds.Select(Artifact));

        /// <summary>Starter test build: no artifacts (Combat Spec 3.3).</summary>
        public static PlayerBuild StarterBuild => Build();

        /// <summary>Geared test build: Keen Edge, Scaled Coat, Iron Charm (Combat Spec 3.3, 5.3).</summary>
        public static PlayerBuild GearedBuild => Build("keen_edge", "scaled_coat", "iron_charm");

        /// <summary>Scripted baseline and fallback (Combat Spec 4.4).</summary>
        public static GuardianSetup DefaultSetup => new GuardianSetup("T3", new[] { "cleave", "slam", "charge" }, "steady");

        public const int MinSkills = 3;
        public const int MaxSkills = 4;

        /// <summary>Open Guardian tuning range by gear score (Combat Spec 4.6).</summary>
        public static AllowedSetupSpace AllowedSpaceFor(int gearScore)
        {
            string[] tiers = gearScore <= 2 ? new[] { "T1", "T2", "T3" }
                : gearScore <= 5 ? new[] { "T2", "T3", "T4" }
                : new[] { "T3", "T4", "T5" };
            return new AllowedSetupSpace(tiers, Skills.Select(s => s.Id), StrategyIds, MinSkills, MaxSkills);
        }

        /// <summary>Gated variant (stretch): T4–T5 for every gear score.</summary>
        public static AllowedSetupSpace GatedSpace() =>
            new AllowedSetupSpace(new[] { "T4", "T5" }, Skills.Select(s => s.Id), StrategyIds, MinSkills, MaxSkills);
    }
}
