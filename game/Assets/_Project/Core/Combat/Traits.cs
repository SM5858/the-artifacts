using System.Collections.Generic;

namespace TheArtifacts.Core.Combat
{
    /// <summary>How a trait part is active (Combat Spec 5.4).</summary>
    public enum TraitMode
    {
        /// <summary>Always on; read when the rule it changes is applied.</summary>
        Passive,
        /// <summary>Cast with the slot's equipment-skill key (Combat Spec 3.2); costs no MP and ignores the GCD.</summary>
        EquipmentSkill,
        /// <summary>Fires when its trigger happens and its conditions hold.</summary>
        Triggered,
    }

    public enum TraitTrigger
    {
        None,
        /// <summary>The player used <see cref="TraitPart.Ability"/>.</summary>
        AbilityUsed,
        /// <summary>A Guardian hit landed on the player; amount is the damage taken.</summary>
        HitTaken,
        /// <summary>Guard blocked damage; amount is the damage blocked.</summary>
        DamageBlocked,
        /// <summary>A hit would bring the player to 0 HP. Checked before the damage lands.</summary>
        LethalDamage,
    }

    public enum TraitEffectKind
    {
        /// <summary>Passive: <see cref="TraitEffect.Ability"/>'s cooldown becomes Value seconds.</summary>
        SetCooldown,
        /// <summary>Passive: Guard lasts Value seconds.</summary>
        SetGuardDuration,
        /// <summary>Heal Value × max HP.</summary>
        HealPercentMaxHp,
        /// <summary><see cref="TraitEffect.Ability"/> comes off cooldown.</summary>
        ResetCooldown,
        /// <summary>Deal Value × the trigger amount to the Guardian.</summary>
        ReflectPercent,
        /// <summary>Apply <see cref="TraitEffect.Status"/> to the Guardian for Value seconds.</summary>
        ApplyStatus,
        /// <summary>Passive: multiply <see cref="TraitEffect.Stat"/> by Value, after flat bonuses. At most ×2 per artifact.</summary>
        MultiplyStat,
        /// <summary>Passive: damage dealt to a Guardian of <see cref="TraitEffect.Creature"/> × (1 + Value).</summary>
        DamageDealtVsType,
        /// <summary>Passive: damage taken from a Guardian of <see cref="TraitEffect.Creature"/> × (1 + Value); negative reduces.</summary>
        DamageTakenVsType,
        /// <summary>For Duration seconds, damage taken × (1 + Value); negative reduces.</summary>
        DamageTakenBuff,
        /// <summary>With <see cref="TraitTrigger.LethalDamage"/>: the player is left at 1 HP instead of dying.</summary>
        SurviveAtOneHp,
    }

    public enum StatKind { Hp, Mp, Strength, Intelligence, Agility, Def, Res, Precision, Speed }

    /// <summary>Guardian and field-monster families that conditional traits target. The full list is open.</summary>
    public enum CreatureType { Construct, Beast, Elemental, Undead }

    /// <summary>Statuses the player can put on a Guardian. Each interrupts a tell; a Guardian may be immune.</summary>
    public enum GuardianStatus { Stun, Petrify, Fear }

    /// <summary>Per-Guardian identity, fixed for the Guardian and never part of the adaptive setup (Combat Spec 4.5).</summary>
    public sealed class GuardianProfile
    {
        public string Id;
        public string Name;
        public CreatureType Type;
        /// <summary>The real-world landmark whose visit unlocks this Guardian's dungeon.</summary>
        public string Landmark;
        /// <summary>Reduce physical / magic damage taken as value / (value + 100), like the player's (Combat Spec 2).</summary>
        public float Def, Res;
        /// <summary>Artifact ids a win here can give (GDD 6.5, Combat Spec 5.3).</summary>
        public IReadOnlyList<string> LootPool = new string[0];
        public IReadOnlyCollection<GuardianStatus> Immunities = new GuardianStatus[0];
    }

    public sealed class TraitEffect
    {
        public TraitEffectKind Kind;
        public Ability Ability;
        public GuardianStatus Status;
        public StatKind Stat;
        public CreatureType Creature;
        public float Value;
        public float Duration;
    }

    /// <summary>
    /// One rule of an artifact's trait: a mode, an optional trigger with conditions, and effects.
    /// A trait is one or more parts (Stillstone: a passive longer Guard plus a triggered reflect).
    /// </summary>
    public sealed class TraitPart
    {
        public TraitMode Mode;
        public TraitTrigger Trigger;
        /// <summary>For <see cref="TraitTrigger.AbilityUsed"/>: which ability.</summary>
        public Ability Ability;
        /// <summary>Condition: only while the Guardian is in a recovery window.</summary>
        public bool RequiresGuardianRecovery;
        /// <summary>Condition: chance to fire, 0–1.</summary>
        public float Chance = 1f;
        /// <summary>Condition: fires at most once per fight.</summary>
        public bool OncePerFight;
        /// <summary>For <see cref="TraitMode.EquipmentSkill"/>: seconds before it can be cast again.</summary>
        public float Cooldown;
        public IReadOnlyList<TraitEffect> Effects = new TraitEffect[0];
    }
}
