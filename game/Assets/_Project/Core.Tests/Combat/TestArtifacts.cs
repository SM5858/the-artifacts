using TheArtifacts.Core.Combat;

namespace TheArtifacts.Core.Tests.Combat
{
    /// <summary>
    /// Test-only artifacts that exercise trait parts the Columbia set does not use
    /// (cooldown override, heal on ability, reset on ability, Guard duration, reflect).
    /// </summary>
    static class TestArtifacts
    {
        public static readonly ArtifactDef KeenEdge = new ArtifactDef
        {
            Id = "keen_edge", Name = "Keen Edge", Slot = ArtifactSlot.RightHand, Rarity = Rarity.Treasure, Strength = 20f,
            Trait = new[] { new TraitPart { Mode = TraitMode.Passive, Effects = new[] { new TraitEffect { Kind = TraitEffectKind.SetCooldown, Ability = Ability.HeavyStrike, Value = 4.5f } } } },
        };

        public static readonly ArtifactDef WardensPlate = new ArtifactDef
        {
            Id = "wardens_plate", Name = "Warden's Plate", Slot = ArtifactSlot.Armor, Rarity = Rarity.Treasure, Hp = 150f, Def = 12f,
            Trait = new[] { new TraitPart { Mode = TraitMode.Triggered, Trigger = TraitTrigger.AbilityUsed, Ability = Ability.Guard,
                Effects = new[] { new TraitEffect { Kind = TraitEffectKind.HealPercentMaxHp, Value = 0.05f } } } },
        };

        public static readonly ArtifactDef EchoBlade = new ArtifactDef
        {
            Id = "echo_blade", Name = "Echo Blade", Slot = ArtifactSlot.RightHand, Rarity = Rarity.Mythic, Strength = 25f,
            Trait = new[] { new TraitPart { Mode = TraitMode.Triggered, Trigger = TraitTrigger.AbilityUsed, Ability = Ability.Overpower, RequiresGuardianRecovery = true,
                Effects = new[] { new TraitEffect { Kind = TraitEffectKind.ResetCooldown, Ability = Ability.Lunge } } } },
        };

        public static readonly ArtifactDef Stillstone = new ArtifactDef
        {
            Id = "stillstone", Name = "Stillstone", Slot = ArtifactSlot.Trinket, Rarity = Rarity.Mythic, Hp = 150f,
            Trait = new[]
            {
                new TraitPart { Mode = TraitMode.Passive, Effects = new[] { new TraitEffect { Kind = TraitEffectKind.SetGuardDuration, Value = 3f } } },
                new TraitPart { Mode = TraitMode.Triggered, Trigger = TraitTrigger.DamageBlocked,
                    Effects = new[] { new TraitEffect { Kind = TraitEffectKind.ReflectPercent, Value = 0.3f } } },
            },
        };

        public static PlayerBuild With(params ArtifactDef[] artifacts) => new PlayerBuild(PrototypeContent.Base, artifacts);
    }
}
