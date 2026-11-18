using System.Collections.Generic;

namespace TheArtifacts.Core.Combat
{
    /// <summary>How a trait part is active (Combat Spec 5.4).</summary>
    public enum TraitMode
    {
        /// <summary>Always on; read when the rule it changes is applied.</summary>
        Passive,
        /// <summary>Cast with the slot's equipment-skill key (Combat Spec 3.2). Not wired yet.</summary>
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
    }

    /// <summary>Statuses the player can put on a Guardian. Each interrupts a tell; a Guardian may be immune.</summary>
    public enum GuardianStatus { Stun, Petrify, Fear }

    public sealed class TraitEffect
    {
        public TraitEffectKind Kind;
        public Ability Ability;
        public GuardianStatus Status;
        public float Value;
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
        public IReadOnlyList<TraitEffect> Effects = new TraitEffect[0];
    }
}
