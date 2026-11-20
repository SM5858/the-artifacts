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

        /// <summary>Overpower, the ultimate on key 4: a big melee hit on a long cooldown.</summary>
        public const float OverpowerCooldown = 45f;
        public const float OverpowerCoefficient = 8.0f;

        /// <summary>Opening: while the Guardian recovers from a skill, player attacks gain this much crit chance.</summary>
        public const float OpeningCritBonus = 0.5f;

        public const float LightningBoltCooldown = 8f;
        /// <summary>Lightning Bolt damage = base + coefficient × Intelligence, so it is useful at any Intelligence.</summary>
        public const float LightningBoltBaseDamage = 100f;
        public const float LightningBoltCoefficient = 1.5f;
        public const float LightningBoltManaCost = 30f;
        public const float LightningBoltRange = 15f;

        /// <summary>
        /// Fire Bolt, key 6: a weaker, cheaper spell whose value is fire. It can target the Guardian or a lore
        /// target such as the Book Keeper's bookshelves (GDD 6.7).
        /// </summary>
        public const float FireBoltCooldown = 6f;
        public const float FireBoltBaseDamage = 60f;
        public const float FireBoltCoefficient = 1.0f;
        public const float FireBoltManaCost = 20f;
        public const float FireBoltRange = 25f;

        /// <summary>Diminishing returns: 0 at 0, 50% at <see cref="StatK"/>, never 100%.</summary>
        public static float Rating(float value) => value <= 0f ? 0f : value / (value + StatK);
    }

    public enum DamageType { Physical, Magic }

    /// <summary>A spell's element. Damage uses <see cref="DamageType"/>; the element only matters to lore targets (GDD 6.7).</summary>
    public enum Element { None, Fire, Lightning }

    /// <summary>General (non-artifact) item grades, from shops up to crafted Masterpieces (GDD 6.6).</summary>
    public enum GeneralGrade { Standard, Fine, Superior, Masterpiece }

    /// <summary>
    /// Stat budget for general items (GDD 6.6, Q12). One point = 1 Strength, Intelligence, Agility, Def, Res, or
    /// Precision, or 10 HP or 10 MP; Speed (crafted items only) costs 2 points per Speed.
    /// The budget keeps the strongest hit under <see cref="MaxBurstShareOfLowestTier"/> of a T1 Guardian's HP.
    /// </summary>
    public static class ItemBudget
    {
        public const int EquipmentSlots = 8;
        public const float HpPerPoint = 10f;
        public const float MpPerPoint = 10f;
        public const float PointsPerSpeed = 2f;

        /// <summary>The strongest hit (a critical Overpower) may take at most this share of T1 HP.</summary>
        public const float MaxBurstShareOfLowestTier = 0.5f;

        /// <summary>Points per slot; a Masterpiece's points rise with crafting skill and materials up to the max.</summary>
        public static int Points(GeneralGrade grade) => grade switch
        {
            GeneralGrade.Standard => 5,
            GeneralGrade.Fine => 10,
            GeneralGrade.Superior => 15,
            _ => MasterpieceMinPoints,
        };

        public const int MasterpieceMinPoints = 20;
        public const int MasterpieceMaxPoints = 23;

        /// <summary>The strongest single hit for this Strength: a critical Overpower (Combat Spec 2, 3.2).</summary>
        public static float StrongestHit(float strength) =>
            PlayerRules.OverpowerCoefficient * PlayerRules.CritMultiplier * strength;

        /// <summary>An artifact's flat stats in budget points, for comparing grades (traits not counted).</summary>
        public static float Points(ArtifactDef a) =>
            a.Strength + a.Intelligence + a.Agility + a.Def + a.Res + a.Precision
            + a.Hp / HpPerPoint + a.Mp / MpPerPoint + a.Speed * PointsPerSpeed;
    }

    /// <summary>
    /// Guardian rules from Combat Spec 4.1; arena and movement from 4.8 (approved 2026-11-20).
    /// </summary>
    public static class GuardianRules
    {
        public const float AutoBaseDamage = 20f;
        public const float AutoSwing = 2.5f;
        public const float MeleeRange = 3f;

        /// <summary>Combat Spec 4.8: slower than the player's 7 m/s so evading is always possible.</summary>
        public const float MoveSpeed = 5f;
        /// <summary>Combat Spec 4.8: where the Guardian stops when chasing.</summary>
        public const float ChaseStopDistance = 2.5f;
        /// <summary>Combat Spec 4.8: circular arena radius.</summary>
        public const float ArenaRadius = 20f;
        /// <summary>Combat Spec 4.8: player starts this far from the Guardian.</summary>
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
        /// <summary>Base tell at T3; tiers scale it (Combat Spec 4.1), never below <see cref="MinTell"/>.</summary>
        public float Tell;
        /// <summary>Shortest fair tell: worst-case escape from melee range at run speed plus 0.4 s to react.</summary>
        public float MinTell;
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
        /// <summary>Scales every skill's tell: long at low tiers (easy to dodge), short at high tiers.</summary>
        public float TellMultiplier;
        /// <summary>Added to the Guardian's own Def and Res (Combat Spec 4.1), so higher tiers resist more.</summary>
        public float Def, Res;
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

        /// <summary>The artifact cast by an equipment-skill key (Combat Spec 3.2), or null. D and C are the first and second trinket.</summary>
        public ArtifactDef EquippedAt(EquipmentKey key)
        {
            switch (key)
            {
                case EquipmentKey.Q: return Artifacts.FirstOrDefault(a => a.Slot == ArtifactSlot.LeftHand);
                case EquipmentKey.W: return Artifacts.FirstOrDefault(a => a.Slot == ArtifactSlot.Helm);
                case EquipmentKey.E: return Artifacts.FirstOrDefault(a => a.Slot == ArtifactSlot.RightHand);
                case EquipmentKey.A: return Artifacts.FirstOrDefault(a => a.Slot == ArtifactSlot.Gloves);
                case EquipmentKey.S: return Artifacts.FirstOrDefault(a => a.Slot == ArtifactSlot.Armor);
                case EquipmentKey.X: return Artifacts.FirstOrDefault(a => a.Slot == ArtifactSlot.Boots);
                case EquipmentKey.D: return Artifacts.Where(a => a.Slot == ArtifactSlot.Trinket).ElementAtOrDefault(0);
                case EquipmentKey.C: return Artifacts.Where(a => a.Slot == ArtifactSlot.Trinket).ElementAtOrDefault(1);
                default: return null;
            }
        }
    }

    /// <summary>All prototype content from the Combat Spec, in one place.</summary>
    public static class PrototypeContent
    {
        /// <summary>Base stats of both test builds (Combat Spec 3.3).</summary>
        public static BaseStats Base => new BaseStats { Hp = 1000f, Mp = 100f, Strength = 40f, Intelligence = 40f };

        public static readonly IReadOnlyList<TierDef> Tiers = new[]
        {
            new TierDef { Id = "T1", MaxHp = 5500f, TellMultiplier = 1.40f, Def = 0f, Res = 0f },
            new TierDef { Id = "T2", MaxHp = 6400f, TellMultiplier = 1.20f, Def = 5f, Res = 5f },
            new TierDef { Id = "T3", MaxHp = 7500f, TellMultiplier = 1.00f, Def = 10f, Res = 10f },
            new TierDef { Id = "T4", MaxHp = 8700f, TellMultiplier = 0.90f, Def = 20f, Res = 20f },
            new TierDef { Id = "T5", MaxHp = 9800f, TellMultiplier = 0.80f, Def = 30f, Res = 30f },
        };

        public static readonly IReadOnlyList<GuardianSkillDef> Skills = new[]
        {
            new GuardianSkillDef { Id = "cleave", Name = "Cleave", Shape = AreaShape.Cone, AngleDegrees = 90f, Range = 6f, Tell = 1.2f, MinTell = 0.7f, BaseDamage = 200f, Recovery = 1.0f, DamageType = DamageType.Physical },
            new GuardianSkillDef { Id = "slam", Name = "Slam", Shape = AreaShape.Circle, Range = 5f, Tell = 1.6f, MinTell = 0.8f, BaseDamage = 250f, Recovery = 1.5f, DamageType = DamageType.Physical },
            new GuardianSkillDef { Id = "charge", Name = "Charge", Shape = AreaShape.Line, Range = 15f, Width = 3f, Tell = 1.4f, MinTell = 0.7f, BaseDamage = 220f, Recovery = 2.0f, DamageType = DamageType.Physical },
            new GuardianSkillDef { Id = "rings", Name = "Shock Rings", Shape = AreaShape.Ring, InnerRadius = 4f, Range = 12f, Tell = 1.8f, MinTell = 1.0f, BaseDamage = 180f, Recovery = 1.2f, DamageType = DamageType.Magic },
            new GuardianSkillDef { Id = "embers", Name = "Embers", Shape = AreaShape.CirclesOnPlayer, Range = 3f, Tell = 1.0f, MinTell = 0.9f, BaseDamage = 120f, Recovery = 0.8f, DamageType = DamageType.Magic, Pulses = 3, PulseInterval = 0.6f },
            new GuardianSkillDef { Id = "fury", Name = "Fury", Shape = AreaShape.None, Tell = 1.0f, MinTell = 0.5f, Recovery = 0f, BuffDuration = 8f, BuffAutoMultiplier = 1.5f },
        };

        public static readonly IReadOnlyList<string> StrategyIds = new[] { "steady", "aggressive", "combo", "reactive" };

        /// <summary>The six Columbia landmark artifacts (Combat Spec 5.3), grouped by the landmark that drops them.</summary>
        public static readonly IReadOnlyList<ArtifactDef> Artifacts = new[]
        {
            // Alma Mater statue
            new ArtifactDef { Id = "wreath_of_alma_mater", Name = "Wreath of Alma Mater", Description = "Max MP +30%",
                Slot = ArtifactSlot.Helm, Rarity = Rarity.Relic, Hp = 50f, Res = 5f,
                Trait = new[] { Passive(new TraitEffect { Kind = TraitEffectKind.MultiplyStat, Stat = StatKind.Mp, Value = 1.3f }) } },
            new ArtifactDef { Id = "staff_of_alma_mater", Name = "Staff of Alma Mater", Description = "Intelligence +30%",
                Slot = ArtifactSlot.RightHand, Rarity = Rarity.Relic, Intelligence = 10f,
                Trait = new[] { Passive(new TraitEffect { Kind = TraitEffectKind.MultiplyStat, Stat = StatKind.Intelligence, Value = 1.3f }) } },

            // Columbia Lion statue
            new ArtifactDef { Id = "columbia_lions_fang", Name = "Columbia Lion's Fang", Description = "20% more damage to Beasts",
                Slot = ArtifactSlot.RightHand, Rarity = Rarity.Heirloom, Strength = 15f, Precision = 10f,
                Trait = new[] { Passive(new TraitEffect { Kind = TraitEffectKind.DamageDealtVsType, Creature = CreatureType.Beast, Value = 0.2f }) } },
            new ArtifactDef { Id = "columbia_lions_pelt", Name = "Columbia Lion's Pelt", Description = "Once per fight, survive a killing blow at 1 HP",
                Slot = ArtifactSlot.Helm, Rarity = Rarity.Mythic, Hp = 150f, Def = 12f, Speed = 10f,
                Trait = new[]
                {
                    new TraitPart { Mode = TraitMode.Triggered, Trigger = TraitTrigger.LethalDamage, OncePerFight = true,
                        Effects = new[] { new TraitEffect { Kind = TraitEffectKind.SurviveAtOneHp } } },
                } },

            // Columbia bookstore
            new ArtifactDef { Id = "columbia_blue_mortarboard", Name = "Columbia Blue Mortarboard", Description = "20% more damage to Undead",
                Slot = ArtifactSlot.Helm, Rarity = Rarity.Relic, Precision = 20f,
                Trait = new[] { Passive(new TraitEffect { Kind = TraitEffectKind.DamageDealtVsType, Creature = CreatureType.Undead, Value = 0.2f }) } },
            new ArtifactDef { Id = "columbia_blue_robe", Name = "Columbia Blue Robe", Description = "S: take 30% less damage for 4 s (25 s cooldown)",
                Slot = ArtifactSlot.Armor, Rarity = Rarity.Treasure, Hp = 150f, Def = 6f, Res = 12f,
                Trait = new[]
                {
                    new TraitPart { Mode = TraitMode.EquipmentSkill, Cooldown = 25f,
                        Effects = new[] { new TraitEffect { Kind = TraitEffectKind.DamageTakenBuff, Value = -0.3f, Duration = 4f } } },
                } },
        };

        static TraitPart Passive(TraitEffect effect) =>
            new TraitPart { Mode = TraitMode.Passive, Effects = new[] { effect } };

        /// <summary>The three prototype Guardians (GDD 6.4, Combat Spec 4.5). Def and Res are per Guardian, not per tier.</summary>
        public static readonly IReadOnlyList<GuardianProfile> Guardians = new[]
        {
            new GuardianProfile { Id = "alma_mater", Name = "Alma Mater", Type = CreatureType.Undead, Res = 400f,
                Landmark = "Alma Mater statue", LootPool = new[] { "wreath_of_alma_mater", "staff_of_alma_mater" },
                // Lore (GDD 6.7): a small owl hides in the folds of the statue's robe. When it peeks out, strike it.
                WeakPoints = new[]
                {
                    new WeakPoint { Id = "owl", Name = "Owl", FirstAppearance = 12f, Interval = 15f, Duration = 3f, DamageMultiplier = 2f },
                } },
            new GuardianProfile { Id = "columbia_lion", Name = "Columbia Lion", Type = CreatureType.Beast,
                Landmark = "Columbia Lion statue", LootPool = new[] { "columbia_lions_fang", "columbia_lions_pelt" } },
            new GuardianProfile { Id = "book_keeper", Name = "Book Keeper", Type = CreatureType.Construct,
                Landmark = "Columbia bookstore", LootPool = new[] { "columbia_blue_mortarboard", "columbia_blue_robe" },
                // Lore (GDD 6.7): the keeper fears for its books. Burning a shelf behind it makes it flee in fear.
                LoreTargets = new[]
                {
                    Bookshelf("bookshelf_left", -8f, 14f),
                    Bookshelf("bookshelf_center", 0f, 16f),
                    Bookshelf("bookshelf_right", 8f, 14f),
                } },
        };

        /// <summary>Fear from a burning bookshelf, in seconds. The panicked keeper is also open to crits.</summary>
        public const float BookshelfFearDuration = 6f;

        static LoreTarget Bookshelf(string id, float x, float y) => new LoreTarget
        {
            Id = id, Name = "Bookshelf", Position = new Vec2(x, y), Element = Element.Fire,
            Status = GuardianStatus.Fear, Duration = BookshelfFearDuration, GrantsOpening = true,
        };

        /// <summary>The Guardian a fight uses when none is given: the Columbia Lion, which hosts the comparison (GDD Q16).</summary>
        public static GuardianProfile Guardian => GuardianById("columbia_lion");

        public static IReadOnlyList<ArtifactDef> LootPool(string guardianId) =>
            GuardianById(guardianId).LootPool.Select(Artifact).ToArray();

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

        /// <summary>Geared test build: one artifact from each landmark (Combat Spec 3.3, 5.3).</summary>
        public static PlayerBuild GearedBuild => Build("wreath_of_alma_mater", "columbia_lions_fang", "columbia_blue_robe");

        /// <summary>Scripted baseline and fallback (Combat Spec 4.4).</summary>
        public static GuardianSetup DefaultSetup => new GuardianSetup("T3", new[] { "cleave", "slam", "charge" }, "steady");

        public const int MinSkills = 3;
        public const int MaxSkills = 4;

        /// <summary>
        /// Open Guardian tuning range by gear score (Combat Spec 4.6). When easing is unlocked (<see cref="IsEased"/>),
        /// the range gains the one tier below it, if there is one: easing is a single step.
        /// </summary>
        public static AllowedSetupSpace AllowedSpaceFor(int gearScore, bool eased = false)
        {
            string[] tiers = gearScore <= 2 ? new[] { "T1", "T2", "T3" }
                : gearScore <= 5 ? new[] { "T2", "T3", "T4" }
                : new[] { "T3", "T4", "T5" };
            if (eased)
            {
                var lowest = Tiers.ToList().FindIndex(t => t.Id == tiers[0]);
                if (lowest > 0) tiers = new[] { Tiers[lowest - 1].Id }.Concat(tiers).ToArray();
            }
            return new AllowedSetupSpace(tiers, Skills.Select(s => s.Id), StrategyIds, MinSkills, MaxSkills);
        }

        /// <summary>Failed attempts before easing unlocks on a Guardian whose best reward is Heirloom or lower.</summary>
        public const int EasingThresholdUpToHeirloom = 10;
        /// <summary>Failed attempts before easing unlocks on a Guardian whose pool has a Treasure or Mythic.</summary>
        public const int EasingThresholdAboveHeirloom = 50;

        /// <summary>Failed attempts on this Guardian that unlock its single easing step (GDD 6.4).</summary>
        public static int EasingThreshold(string guardianId) =>
            LootPool(guardianId).Max(a => a.Rarity) <= Rarity.Heirloom ? EasingThresholdUpToHeirloom : EasingThresholdAboveHeirloom;

        public static bool IsEased(string guardianId, int failedAttempts) => failedAttempts >= EasingThreshold(guardianId);

        /// <summary>Gear score at which the Guardian greets the challenger as strong (the top tuning band).</summary>
        public const int StrongChallengerGearScore = 6;

        /// <summary>
        /// What the adaptive Guardian says at the start of a fight, so the player sees that it adapted (GDD 6.4):
        /// a strong challenger is greeted, and an unlocked easing step is announced. Null when there is nothing to say.
        /// </summary>
        public static string Announcement(int gearScore, bool eased)
        {
            if (gearScore >= StrongChallengerGearScore) return "At last, a worthy challenger. I will not hold back.";
            if (eased) return "You keep coming back... Very well, I will ease my guard.";
            return null;
        }

        /// <summary>Gated variant (stretch): T4–T5 for every gear score.</summary>
        public static AllowedSetupSpace GatedSpace() =>
            new AllowedSetupSpace(new[] { "T4", "T5" }, Skills.Select(s => s.Id), StrategyIds, MinSkills, MaxSkills);
    }
}
