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
        /// <summary>Base run speed; Speed raises it by Speed% (Combat Spec 2).</summary>
        public const float RunSpeed = 7f;
        public const float MeleeRange = 3f;
        /// <summary>Def, Res, Agility, and Precision turn into a percent as value / (value + StatK) (Combat Spec 2).</summary>
        public const float StatK = 100f;
        public const float CritMultiplier = 1.5f;
        public const float MpRegenPerSecond = 2f;
        /// <summary>A single artifact may multiply a stat by at most this much (Combat Spec 5.4).</summary>
        public const float MaxStatMultiplierPerArtifact = 2f;

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

        public const float LightningBoltCooldown = 8f;
        public const float LightningBoltCoefficient = 2.0f;
        public const float LightningBoltManaCost = 30f;
        public const float LightningBoltRange = 15f;

        /// <summary>Diminishing returns: 0 at 0, 50% at <see cref="StatK"/>, never 100%.</summary>
        public static float Rating(float value) => value <= 0f ? 0f : value / (value + StatK);
    }

    public enum DamageType { Physical, Magic }

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
        public DamageType DamageType;
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

    public enum Rarity { Relic = 1, Heirloom = 2, Treasure = 3, Mythic = 4 }

    public sealed class ArtifactDef
    {
        public string Id;
        public string Name;
        /// <summary>Player-facing trait text for the item tooltip, e.g. "Power +30%".</summary>
        public string Description;
        public ArtifactSlot Slot;
        public Rarity Rarity;
        public float Hp, Mp, Strength, Intelligence, Agility, Def, Res, Precision, Speed;
        /// <summary>The artifact's trait (Combat Spec 5.4).</summary>
        public IReadOnlyList<TraitPart> Trait = new TraitPart[0];
    }

    /// <summary>A character's stats before items (GDD 6.2). The prototype has no levels, so these are fixed.</summary>
    public sealed class BaseStats
    {
        public float Hp, Mp, Strength, Intelligence, Agility, Def, Res, Precision, Speed;

        public float this[StatKind stat] => stat switch
        {
            StatKind.Hp => Hp, StatKind.Mp => Mp, StatKind.Strength => Strength, StatKind.Intelligence => Intelligence,
            StatKind.Agility => Agility, StatKind.Def => Def, StatKind.Res => Res, StatKind.Precision => Precision,
            _ => Speed,
        };
    }

    /// <summary>A player's final numbers after artifacts (Combat Spec 3.3).</summary>
    public sealed class PlayerBuild
    {
        readonly Dictionary<StatKind, float> _stats = new Dictionary<StatKind, float>();

        public float this[StatKind stat] => _stats[stat];
        public float MaxHp => this[StatKind.Hp];
        public float MaxMp => this[StatKind.Mp];
        public float Strength => this[StatKind.Strength];
        public float Intelligence => this[StatKind.Intelligence];

        public float PhysicalReduction => PlayerRules.Rating(this[StatKind.Def]);
        public float MagicReduction => PlayerRules.Rating(this[StatKind.Res]);
        public float CooldownReduction => PlayerRules.Rating(this[StatKind.Agility]);
        public float CritChance => PlayerRules.Rating(this[StatKind.Precision]);
        public float RunSpeed => PlayerRules.RunSpeed * (1f + this[StatKind.Speed] / 100f);

        public IReadOnlyList<ArtifactDef> Artifacts { get; }
        public int GearScore => Artifacts.Sum(a => (int)a.Rarity);

        public PlayerBuild(BaseStats baseStats, IEnumerable<ArtifactDef> artifacts)
        {
            if (baseStats == null) throw new ArgumentNullException(nameof(baseStats));
            Artifacts = (artifacts ?? Enumerable.Empty<ArtifactDef>()).ToArray();
            if (Artifacts.GroupBy(a => a.Slot).Any(g => g.Count() > SlotCapacity(g.Key)))
                throw new ArgumentException("At most one artifact per slot (two trinkets).");
            foreach (var a in Artifacts)
            foreach (StatKind stat in Enum.GetValues(typeof(StatKind)))
                if (Multiplier(new[] { a }, stat) > PlayerRules.MaxStatMultiplierPerArtifact + 1e-6f)
                    throw new ArgumentException($"{a.Id} multiplies {stat} by more than x{PlayerRules.MaxStatMultiplierPerArtifact}.");

            // Flat bonuses first, then multipliers; multipliers from different artifacts multiply together.
            foreach (StatKind stat in Enum.GetValues(typeof(StatKind)))
                _stats[stat] = (baseStats[stat] + Artifacts.Sum(a => Flat(a, stat))) * Multiplier(Artifacts, stat);
        }

        static float Flat(ArtifactDef a, StatKind stat) => stat switch
        {
            StatKind.Hp => a.Hp, StatKind.Mp => a.Mp, StatKind.Strength => a.Strength, StatKind.Intelligence => a.Intelligence,
            StatKind.Agility => a.Agility, StatKind.Def => a.Def, StatKind.Res => a.Res, StatKind.Precision => a.Precision,
            _ => a.Speed,
        };

        static float Multiplier(IEnumerable<ArtifactDef> artifacts, StatKind stat) =>
            PassiveEffects(artifacts, TraitEffectKind.MultiplyStat).Where(e => e.Stat == stat)
                .Aggregate(1f, (m, e) => m * e.Value);

        static IEnumerable<TraitEffect> PassiveEffects(IEnumerable<ArtifactDef> artifacts, TraitEffectKind kind) =>
            artifacts.SelectMany(a => a.Trait).Where(p => p.Mode == TraitMode.Passive)
                .SelectMany(p => p.Effects).Where(e => e.Kind == kind);

        /// <summary>Product of (1 + Value) for passive effects of this kind that target <paramref name="type"/>.</summary>
        public float VersusType(TraitEffectKind kind, CreatureType type) =>
            PassiveEffects(Artifacts, kind).Where(e => e.Creature == type).Aggregate(1f, (m, e) => m * (1f + e.Value));

        /// <summary>Every trait part from equipped artifacts, with the artifact it came from.</summary>
        public IEnumerable<(ArtifactDef Artifact, TraitPart Part)> TraitParts =>
            Artifacts.SelectMany(a => a.Trait.Select(p => (a, p)));

        /// <summary>The last passive effect of this kind (and ability, if given), or null.</summary>
        public TraitEffect Passive(TraitEffectKind kind, Ability ability = Ability.None) =>
            TraitParts.Where(t => t.Part.Mode == TraitMode.Passive)
                .SelectMany(t => t.Part.Effects)
                .LastOrDefault(e => e.Kind == kind && (ability == Ability.None || e.Ability == ability));

        public static int SlotCapacity(ArtifactSlot slot) => slot == ArtifactSlot.Trinket ? 2 : 1;
    }

    /// <summary>All prototype content from the Combat Spec, in one place.</summary>
    public static class PrototypeContent
    {
        /// <summary>Base stats of both test builds (Combat Spec 3.3).</summary>
        public static BaseStats Base => new BaseStats { Hp = 1000f, Mp = 100f, Strength = 40f, Intelligence = 40f };

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
            new GuardianSkillDef { Id = "cleave", Name = "Cleave", Shape = AreaShape.Cone, AngleDegrees = 90f, Range = 6f, Tell = 1.2f, BaseDamage = 200f, Recovery = 1.0f, DamageType = DamageType.Physical },
            new GuardianSkillDef { Id = "slam", Name = "Slam", Shape = AreaShape.Circle, Range = 5f, Tell = 1.6f, BaseDamage = 250f, Recovery = 1.5f, DamageType = DamageType.Physical },
            new GuardianSkillDef { Id = "charge", Name = "Charge", Shape = AreaShape.Line, Range = 15f, Width = 3f, Tell = 1.4f, BaseDamage = 220f, Recovery = 2.0f, DamageType = DamageType.Physical },
            new GuardianSkillDef { Id = "rings", Name = "Shock Rings", Shape = AreaShape.Ring, InnerRadius = 4f, Range = 12f, Tell = 1.8f, BaseDamage = 180f, Recovery = 1.2f, DamageType = DamageType.Magic },
            new GuardianSkillDef { Id = "embers", Name = "Embers", Shape = AreaShape.CirclesOnPlayer, Range = 3f, Tell = 1.0f, BaseDamage = 120f, Recovery = 0.8f, DamageType = DamageType.Magic, Pulses = 3, PulseInterval = 0.6f },
            new GuardianSkillDef { Id = "fury", Name = "Fury", Shape = AreaShape.None, Tell = 1.0f, Recovery = 0f, BuffDuration = 8f, BuffAutoMultiplier = 1.5f },
        };

        public static readonly IReadOnlyList<string> StrategyIds = new[] { "steady", "aggressive", "combo", "reactive" };

        public static readonly IReadOnlyList<ArtifactDef> Artifacts = new[]
        {
            new ArtifactDef { Id = "worn_band", Name = "Worn Band", Description = "Strength +30%", Slot = ArtifactSlot.Trinket, Rarity = Rarity.Relic, Strength = 5f,
                Trait = new[] { Passive(new TraitEffect { Kind = TraitEffectKind.MultiplyStat, Stat = StatKind.Strength, Value = 1.3f }) } },
            new ArtifactDef { Id = "padded_vest", Name = "Padded Vest", Description = "Max HP +30%", Slot = ArtifactSlot.Armor, Rarity = Rarity.Relic, Hp = 100f,
                Trait = new[] { Passive(new TraitEffect { Kind = TraitEffectKind.MultiplyStat, Stat = StatKind.Hp, Value = 1.3f }) } },
            new ArtifactDef { Id = "iron_charm", Name = "Iron Charm", Description = "10% less damage taken from Constructs", Slot = ArtifactSlot.Trinket, Rarity = Rarity.Heirloom, Hp = 100f, Def = 6f,
                Trait = new[] { Passive(new TraitEffect { Kind = TraitEffectKind.DamageTakenVsType, Creature = CreatureType.Construct, Value = -0.10f }) } },
            new ArtifactDef { Id = "scaled_coat", Name = "Scaled Coat", Description = "15% less damage taken from Constructs", Slot = ArtifactSlot.Armor, Rarity = Rarity.Heirloom, Hp = 200f, Def = 12f,
                Trait = new[] { Passive(new TraitEffect { Kind = TraitEffectKind.DamageTakenVsType, Creature = CreatureType.Construct, Value = -0.15f }) } },
            new ArtifactDef { Id = "keen_edge", Name = "Keen Edge", Description = "Heavy Strike cooldown 6 → 4.5 s", Slot = ArtifactSlot.RightHand, Rarity = Rarity.Treasure, Strength = 20f,
                Trait = new[] { Passive(new TraitEffect { Kind = TraitEffectKind.SetCooldown, Ability = Ability.HeavyStrike, Value = 4.5f }) } },
            new ArtifactDef { Id = "wardens_plate", Name = "Warden's Plate", Description = "Guard also heals 5% of max HP", Slot = ArtifactSlot.Armor, Rarity = Rarity.Treasure, Hp = 150f, Def = 12f,
                Trait = new[] { OnAbility(Ability.Guard, new TraitEffect { Kind = TraitEffectKind.HealPercentMaxHp, Value = 0.05f }) } },
            new ArtifactDef { Id = "echo_blade", Name = "Echo Blade", Description = "Punish in a recovery window resets Lunge", Slot = ArtifactSlot.RightHand, Rarity = Rarity.Mythic, Strength = 25f,
                Trait = new[] { OnAbility(Ability.Punish, new TraitEffect { Kind = TraitEffectKind.ResetCooldown, Ability = Ability.Lunge }, inRecovery: true) } },
            new ArtifactDef { Id = "stillstone", Name = "Stillstone", Description = "Guard lasts 3 s and reflects 30% of blocked damage", Slot = ArtifactSlot.Trinket, Rarity = Rarity.Mythic, Hp = 150f,
                Trait = new[]
                {
                    Passive(new TraitEffect { Kind = TraitEffectKind.SetGuardDuration, Value = 3f }),
                    new TraitPart { Mode = TraitMode.Triggered, Trigger = TraitTrigger.DamageBlocked,
                        Effects = new[] { new TraitEffect { Kind = TraitEffectKind.ReflectPercent, Value = 0.3f } } },
                } },
        };

        static TraitPart Passive(TraitEffect effect) =>
            new TraitPart { Mode = TraitMode.Passive, Effects = new[] { effect } };

        static TraitPart OnAbility(Ability ability, TraitEffect effect, bool inRecovery = false) =>
            new TraitPart { Mode = TraitMode.Triggered, Trigger = TraitTrigger.AbilityUsed, Ability = ability,
                RequiresGuardianRecovery = inRecovery, Effects = new[] { effect } };

        /// <summary>The three prototype Guardians (GDD 6.4, Combat Spec 4.5). Def and Res are per Guardian, not per tier.</summary>
        public static readonly IReadOnlyList<GuardianProfile> Guardians = new[]
        {
            new GuardianProfile { Id = "alma_mater", Name = "Alma Mater", Type = CreatureType.Construct, Res = 400f },
            new GuardianProfile { Id = "columbia_lion", Name = "Columbia Lion", Type = CreatureType.Beast },
            new GuardianProfile { Id = "book_keeper", Name = "Book Keeper", Type = CreatureType.Undead },
        };

        /// <summary>The Guardian a fight uses when none is given. Which one hosts the comparison is open (GDD Q16).</summary>
        public static GuardianProfile Guardian => GuardianById("alma_mater");

        public static GuardianProfile GuardianById(string id) => Guardians.FirstOrDefault(g => g.Id == id)
            ?? throw new ArgumentException($"Unknown Guardian '{id}'.");

        public static TierDef Tier(string id) => Tiers.FirstOrDefault(t => t.Id == id);
        public static GuardianSkillDef Skill(string id) => Skills.FirstOrDefault(s => s.Id == id);
        public static ArtifactDef Artifact(string id) => Artifacts.FirstOrDefault(a => a.Id == id)
            ?? throw new ArgumentException($"Unknown artifact '{id}'.");

        public static PlayerBuild Build(params string[] artifactIds) =>
            new PlayerBuild(Base, artifactIds.Select(Artifact));

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
