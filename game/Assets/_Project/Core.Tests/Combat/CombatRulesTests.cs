using System.Linq;
using NUnit.Framework;
using TheArtifacts.Core.Combat;
using TheArtifacts.Core.Guardian;
using static TheArtifacts.Core.Tests.Combat.CombatTestUtil;

namespace TheArtifacts.Core.Tests.Combat
{
    public class CombatRulesTests
    {
        // ---------- Content ----------

        [Test]
        public void TestBuilds_MatchCombatSpec()
        {
            var starter = PrototypeContent.StarterBuild;
            var geared = PrototypeContent.GearedBuild;

            Assert.That((starter.MaxHp, starter.MaxMp, starter.Strength, starter.Intelligence, starter.GearScore), Is.EqualTo((1000f, 100f, 40f, 40f, 0)));
            Assert.That((starter.PhysicalReduction, starter.CooldownReduction, starter.CritChance), Is.EqualTo((0f, 0f, 0f)));
            Assert.That(geared.MaxHp, Is.EqualTo(1300f));
            Assert.That(geared.Strength, Is.EqualTo(60f));
            Assert.That(geared[StatKind.Def], Is.EqualTo(18f));
            Assert.That(geared.PhysicalReduction, Is.EqualTo(18f / 118f).Within(1e-6));
            Assert.That(geared.GearScore, Is.EqualTo(7));
        }

        [TestCase(0f, 0f)]
        [TestCase(25f, 0.2f)]
        [TestCase(100f, 0.5f)]
        [TestCase(400f, 0.8f)]
        public void StatRating_HasDiminishingReturns(float value, float expected)
        {
            Assert.That(PlayerRules.Rating(value), Is.EqualTo(expected).Within(1e-6));
        }

        [Test]
        public void RelicMultipliers_ApplyAfterFlatBonuses()
        {
            Assert.That(PrototypeContent.Build("worn_band").Strength, Is.EqualTo(45f * 1.3f).Within(1e-3));
            Assert.That(PrototypeContent.Build("padded_vest").MaxHp, Is.EqualTo(1100f * 1.3f).Within(1e-3));
        }

        /// <summary>Design guardrail: every authored artifact stays within the ×2 per-artifact multiplier cap.</summary>
        [Test]
        public void EveryArtifact_RespectsTheMultiplierCap()
        {
            foreach (var artifact in PrototypeContent.Artifacts)
                Assert.DoesNotThrow(() => new PlayerBuild(PrototypeContent.Base, new[] { artifact }), artifact.Id);
        }

        [Test]
        public void OneArtifact_CannotMultiplyAStatPastTwo()
        {
            var greedy = new ArtifactDef
            {
                Id = "test_greedy", Slot = ArtifactSlot.Helm, Rarity = Rarity.Mythic,
                Trait = new[]
                {
                    new TraitPart { Mode = TraitMode.Passive, Effects = new[]
                    {
                        new TraitEffect { Kind = TraitEffectKind.MultiplyStat, Stat = StatKind.Strength, Value = 1.5f },
                        new TraitEffect { Kind = TraitEffectKind.MultiplyStat, Stat = StatKind.Strength, Value = 1.5f },
                    } },
                },
            };
            Assert.Throws<System.ArgumentException>(() => new PlayerBuild(PrototypeContent.Base, new[] { greedy }));
        }

        [Test]
        public void OneArtifactPerSlot()
        {
            Assert.Throws<System.ArgumentException>(() => PrototypeContent.Build("keen_edge", "echo_blade"));
        }

        [Test]
        public void DefaultSetup_IsValidForEveryGearScore()
        {
            foreach (var score in new[] { 0, 3, 7 })
            {
                var reason = GuardianSetupValidator.Check(PrototypeContent.DefaultSetup, System.TimeSpan.Zero,
                    PrototypeContent.AllowedSpaceFor(score));
                Assert.That(reason, Is.EqualTo(FallbackReason.None), $"gear score {score}");
            }
        }

        [TestCase(0, "T1", "T3")]
        [TestCase(2, "T1", "T3")]
        [TestCase(3, "T2", "T4")]
        [TestCase(5, "T2", "T4")]
        [TestCase(6, "T3", "T5")]
        [TestCase(7, "T3", "T5")]
        public void TuningRange_FollowsGearScore(int gearScore, string lowest, string highest)
        {
            var tiers = PrototypeContent.AllowedSpaceFor(gearScore).StatTiers.OrderBy(t => t).ToArray();
            Assert.That(tiers.First(), Is.EqualTo(lowest));
            Assert.That(tiers.Last(), Is.EqualTo(highest));
        }

        // ---------- Player abilities ----------

        [Test]
        public void HeavyStrike_DealsCoefficientTimesStrength_AndGoesOnCooldown()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "cleave", "slam", "charge");
            Engage(sim);

            Use(sim, Ability.HeavyStrike);
            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "heavy_strike"), Is.EqualTo(100f));

            RunFor(sim, 1.1f);
            Use(sim, Ability.HeavyStrike);
            Assert.That(sim.Events.Last(e => e.Type == CombatEventType.AbilityRejected).Id, Is.EqualTo("HeavyStrike:cooldown"));

            RunFor(sim, 5f);
            Use(sim, Ability.HeavyStrike);
            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "heavy_strike"), Is.EqualTo(200f));
        }

        [Test]
        public void GuardianDef_ReducesPhysicalDamage()
        {
            var sim = new CombatSim(PrototypeContent.StarterBuild, PrototypeContent.DefaultSetup, seed: 1,
                new GuardianProfile { Id = "test", Type = CreatureType.Beast, Def = 100f });
            Engage(sim);

            Use(sim, Ability.HeavyStrike);
            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "heavy_strike"), Is.EqualTo(50f).Within(1e-3));
        }

        // ---------- Lightning Bolt, Agility, Precision, Speed (Combat Spec 2, 3.2) ----------

        [TestCase("columbia_lion", 80f)]   // 2.0 × Intelligence 40, no Res
        [TestCase("alma_mater", 16f)]      // Construct: Res 400 → 80% less
        public void LightningBolt_IsRangedMagic_ResistedByConstructs(string guardianId, float expected)
        {
            var sim = new CombatSim(PrototypeContent.StarterBuild, PrototypeContent.DefaultSetup, seed: 1,
                PrototypeContent.GuardianById(guardianId));
            Assert.That(sim.DistanceToGuardian, Is.GreaterThan(PlayerRules.MeleeRange));

            Use(sim, Ability.LightningBolt);

            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "lightning_bolt"), Is.EqualTo(expected).Within(1e-3));
            Assert.That(sim.PlayerMp, Is.EqualTo(70f).Within(0.1f));
        }

        [Test]
        public void LightningBolt_NeedsMana()
        {
            var poor = new BaseStats { Hp = 1000f, Mp = 20f, Strength = 40f, Intelligence = 40f };
            var sim = Sim(new PlayerBuild(poor, null), "T3", "steady", "cleave", "slam", "charge");

            Use(sim, Ability.LightningBolt);

            Assert.That(sim.Events.Last(e => e.Type == CombatEventType.AbilityRejected).Id, Is.EqualTo("LightningBolt:mana"));
        }

        [Test]
        public void Agility_ShortensCooldowns()
        {
            var quick = new BaseStats { Hp = 1000f, Mp = 100f, Strength = 40f, Intelligence = 40f, Agility = 100f };
            var sim = Sim(new PlayerBuild(quick, null), "T3", "steady", "cleave", "slam", "charge");

            Use(sim, Ability.Guard);

            Assert.That(sim.CooldownRemaining(Ability.Guard), Is.EqualTo(PlayerRules.GuardCooldown * 0.5f).Within(Dt));
        }

        [Test]
        public void Precision_CritsForOneAndAHalf()
        {
            // Precision this high makes a crit all but certain.
            var sharp = new BaseStats { Hp = 1000f, Mp = 100f, Strength = 40f, Intelligence = 40f, Precision = 1e9f };
            var sim = Sim(new PlayerBuild(sharp, null), "T3", "steady", "cleave", "slam", "charge");
            Engage(sim);

            Use(sim, Ability.HeavyStrike);

            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "heavy_strike"), Is.EqualTo(150f).Within(1e-3));
            Assert.That(sim.Events.Any(e => e.Type == CombatEventType.CriticalHit && e.Id == "heavy_strike"), Is.True);
        }

        [Test]
        public void Speed_RaisesRunSpeed()
        {
            var fast = new BaseStats { Hp = 1000f, Mp = 100f, Strength = 40f, Intelligence = 40f, Speed = 50f };
            Assert.That(new PlayerBuild(fast, null).RunSpeed, Is.EqualTo(10.5f).Within(1e-4));
        }

        [Test]
        public void MagicGuardianSkills_AreReducedByRes()
        {
            // Embers is magic: 120 at T3, Res 100 halves it; Def does nothing.
            var warded = new BaseStats { Hp = 1000f, Mp = 100f, Strength = 40f, Intelligence = 40f, Def = 1000f, Res = 100f };
            var sim = Sim(new PlayerBuild(warded, null), "T3", "steady", "embers", "slam", "cleave");

            RunUntil(sim, s => s.Events.Any(e => e.Type == CombatEventType.PlayerDamaged && e.Id == "embers"), 15f);

            Assert.That(sim.Events.First(e => e.Type == CombatEventType.PlayerDamaged && e.Id == "embers").Amount, Is.EqualTo(60f).Within(1e-3));
        }

        [Test]
        public void GlobalCooldown_BlocksTheNextSkillForOneSecond()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "cleave", "slam", "charge");
            Engage(sim);

            Use(sim, Ability.HeavyStrike);
            Use(sim, Ability.Punish);

            Assert.That(sim.Events.Last(e => e.Type == CombatEventType.AbilityRejected).Id, Is.EqualTo("Punish:gcd"));
        }

        [Test]
        public void MeleeSkills_NeedRange()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "cleave", "slam", "charge");

            Use(sim, Ability.HeavyStrike);

            Assert.That(sim.Events.Last(e => e.Type == CombatEventType.AbilityRejected).Id, Is.EqualTo("HeavyStrike:range"));
        }

        [Test]
        public void KeenEdge_ShortensHeavyStrikeCooldown()
        {
            var sim = Sim(PrototypeContent.GearedBuild, "T3", "steady", "cleave", "slam", "charge");
            Engage(sim);

            Use(sim, Ability.HeavyStrike);

            Assert.That(sim.CooldownRemaining(Ability.HeavyStrike), Is.EqualTo(4.5f).Within(1e-4));
        }

        [Test]
        public void Lunge_ClosesDistance()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "cleave", "slam", "charge");
            var before = sim.DistanceToGuardian;

            Use(sim, Ability.Lunge);

            Assert.That(sim.DistanceToGuardian, Is.EqualTo(before - PlayerRules.LungeDistance).Within(0.3));
        }

        // ---------- Guardian skills, tells, recovery ----------

        [Test]
        public void StandingInCleave_TakesFullDamage_ThenPunishInRecoveryDoubles()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "cleave", "slam", "charge");
            Engage(sim);

            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 15f);
            Assert.That(DamageTo(sim, CombatEventType.PlayerDamaged, "cleave"), Is.EqualTo(200f));

            Use(sim, Ability.Punish);
            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "punish_recovery"), Is.EqualTo(320f));
        }

        [Test]
        public void PunishOutsideRecovery_IsNotDoubled()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "cleave", "slam", "charge");
            Engage(sim);

            Use(sim, Ability.Punish);

            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "punish"), Is.EqualTo(160f));
        }

        [Test]
        public void WalkingOutOfSlam_DuringTheTell_AvoidsIt()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "slam", "cleave", "charge");
            Engage(sim);
            RunUntil(sim, s => s.Phase == GuardianPhase.Tell, 15f);

            var away = (sim.PlayerPosition - sim.GuardianPosition).Normalized();
            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 3f, _ => new PlayerInput(away));

            Assert.That(sim.Events.Any(e => e.Type == CombatEventType.SkillMissed && e.Id == "slam"), Is.True);
            Assert.That(DamageTo(sim, CombatEventType.PlayerDamaged, "slam"), Is.Zero);
        }

        [Test]
        public void StandingInSlam_TakesTierScaledDamage()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T5", "steady", "slam", "cleave", "charge");
            Engage(sim);

            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 15f);

            Assert.That(DamageTo(sim, CombatEventType.PlayerDamaged, "slam"), Is.EqualTo(250f * 1.3f).Within(1e-3));
        }

        [Test]
        public void ShockRings_AreSafeUpClose()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "rings", "slam", "cleave");
            Engage(sim);

            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 15f);

            Assert.That(sim.Events.Any(e => e.Type == CombatEventType.SkillMissed && e.Id == "rings"), Is.True);
        }

        // Geared: Def 18 (18/118 ≈ 15.3% less); Iron Charm −10% and Scaled Coat −15% against Constructs. Guard: 60% less.
        [TestCase(CreatureType.Construct, 0.9f * 0.85f)]
        [TestCase(CreatureType.Beast, 1f)]
        public void DefGuardAndTypeTraits_ReduceDamage(CreatureType guardianType, float typeFactor)
        {
            var expected = 200f * (1f - 18f / 118f) * typeFactor * 0.4f;
            var sim = new CombatSim(PrototypeContent.GearedBuild, new GuardianSetup("T3", new[] { "cleave", "slam", "charge" }, "steady"),
                seed: 1, new GuardianProfile { Type = guardianType });
            Engage(sim);
            RunUntil(sim, s => s.Phase == GuardianPhase.Tell, 15f);

            Use(sim, Ability.Guard);
            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 3f);

            Assert.That(DamageTo(sim, CombatEventType.PlayerDamaged, "cleave"), Is.EqualTo(expected).Within(1e-3));
        }

        [Test]
        public void Stillstone_ReflectsBlockedDamage()
        {
            // Cleave 200, no armor; Guard blocks 120; Stillstone reflects 30% of that = 36.
            var sim = Sim(PrototypeContent.Build("stillstone"), "T3", "steady", "cleave", "slam", "charge");
            Engage(sim);
            RunUntil(sim, s => s.Phase == GuardianPhase.Tell, 15f);

            Use(sim, Ability.Guard);
            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 3f);

            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "stillstone_reflect"), Is.EqualTo(36f).Within(1e-3));
        }

        [Test]
        public void EchoBlade_PunishInRecoveryResetsLunge()
        {
            var sim = Sim(PrototypeContent.Build("echo_blade"), "T3", "steady", "cleave", "slam", "charge");
            Use(sim, Ability.Lunge);
            Assert.That(sim.CooldownRemaining(Ability.Lunge), Is.GreaterThan(0f));
            Engage(sim);

            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 15f);
            Use(sim, Ability.Punish);

            Assert.That(sim.CooldownRemaining(Ability.Lunge), Is.Zero);
        }

        [Test]
        public void WardensPlate_GuardHealsFivePercentOfMaxHp()
        {
            // Max HP 1150 after the plate; standing in Slam leaves the player well below full.
            var sim = Sim(PrototypeContent.Build("wardens_plate"), "T3", "steady", "slam", "cleave", "charge");
            Engage(sim);
            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 15f);

            Use(sim, Ability.Guard);

            Assert.That(DamageTo(sim, CombatEventType.PlayerHealed, "wardens_plate"), Is.EqualTo(57.5f).Within(1e-3));
        }

        // ---------- Guardian statuses (Combat Spec 4.5, 5.4) ----------

        [Test]
        public void Status_InterruptsTheTell_ThenTheGuardianRecovers()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "cleave", "slam", "charge");
            Engage(sim);
            RunUntil(sim, s => s.Phase == GuardianPhase.Tell, 15f);

            sim.ApplyGuardianStatus(GuardianStatus.Petrify, 1f);

            Assert.That(sim.Phase, Is.EqualTo(GuardianPhase.Incapacitated));
            Assert.That(sim.ActiveTelegraphs, Is.Empty);
            Assert.That(sim.Events.Any(e => e.Type == CombatEventType.TellInterrupted && e.Id == "cleave"), Is.True);

            RunFor(sim, 1.1f);
            Assert.That(sim.ActiveStatus, Is.Null);
            Assert.That(sim.Phase, Is.Not.EqualTo(GuardianPhase.Incapacitated));
            Assert.That(DamageTo(sim, CombatEventType.PlayerDamaged, "cleave"), Is.Zero);
        }

        [Test]
        public void Status_IsResisted_ByAnImmuneGuardian()
        {
            var sim = new CombatSim(PrototypeContent.StarterBuild, PrototypeContent.DefaultSetup, seed: 1,
                new GuardianProfile { Type = CreatureType.Construct, Immunities = new[] { GuardianStatus.Petrify } });
            Engage(sim);
            RunUntil(sim, s => s.Phase == GuardianPhase.Tell, 15f);

            sim.ApplyGuardianStatus(GuardianStatus.Petrify, 1f);

            Assert.That(sim.Phase, Is.EqualTo(GuardianPhase.Tell));
            Assert.That(sim.Events.Any(e => e.Type == CombatEventType.StatusResisted), Is.True);
        }

        [Test]
        public void HitTakenTrait_PetrifiesAndCancelsTheRestOfTheSkill()
        {
            // A test-only artifact: every hit taken petrifies the Guardian for 1 s.
            var aegis = new ArtifactDef
            {
                Id = "test_aegis", Name = "Test Aegis", Slot = ArtifactSlot.LeftHand, Rarity = Rarity.Mythic,
                Trait = new[]
                {
                    new TraitPart
                    {
                        Mode = TraitMode.Triggered, Trigger = TraitTrigger.HitTaken,
                        Effects = new[] { new TraitEffect { Kind = TraitEffectKind.ApplyStatus, Status = GuardianStatus.Petrify, Value = 1f } },
                    },
                },
            };
            var sim = Sim(new PlayerBuild(PrototypeContent.Base, new[] { aegis }), "T3", "steady", "embers", "slam", "cleave");

            // Stand still: the first of three Embers circles lands on the player and interrupts the rest.
            RunUntil(sim, s => s.Events.Any(e => e.Type == CombatEventType.TellInterrupted && e.Id == "embers"), 20f);

            Assert.That(sim.Events.Count(e => e.Type == CombatEventType.PlayerDamaged && e.Id == "embers"), Is.EqualTo(1));
            Assert.That(sim.Events.Any(e => e.Type == CombatEventType.RecoveryStarted && e.Id == "embers"), Is.False);
            Assert.That(sim.Phase, Is.EqualTo(GuardianPhase.Incapacitated));
        }

        [Test]
        public void Fury_RaisesGuardianAutoAttacks()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "fury", "slam", "cleave");
            Engage(sim);

            RunUntil(sim, s => s.IsFuryActive, 15f);
            RunFor(sim, 5f);

            var autos = sim.Events.Where(e => e.Type == CombatEventType.PlayerDamaged && e.Id == "guardian_auto").ToArray();
            Assert.That(autos.First().Amount, Is.EqualTo(20f));
            Assert.That(autos.Last().Amount, Is.EqualTo(30f));
        }

        // ---------- Strategies ----------

        [Test]
        public void Steady_UsesFixedRotationEveryEightSeconds()
        {
            var sim = Sim(PrototypeContent.GearedBuild, "T1", "steady", "cleave", "slam", "charge");
            RunFor(sim, 33f);

            var tells = sim.Events.Where(e => e.Type == CombatEventType.TellStarted).ToArray();
            Assert.That(tells.Select(e => e.Id).Take(4), Is.EqualTo(new[] { "cleave", "slam", "charge", "cleave" }));
            Assert.That(tells.Select(e => e.Time).Take(4), Is.EqualTo(new[] { 8f, 16f, 24f, 32f }).Within(Dt * 1.5f));
        }

        [Test]
        public void Combo_UsesTwoSkillsBackToBack()
        {
            var sim = Sim(PrototypeContent.GearedBuild, "T1", "combo", "cleave", "slam", "charge");
            RunFor(sim, 17f);

            var tells = sim.Events.Where(e => e.Type == CombatEventType.TellStarted).Take(2).ToArray();
            Assert.That(tells.Select(e => e.Id), Is.EqualTo(new[] { "cleave", "slam" }));
            // cleave: 1.2 s tell + 1.0 s recovery, then 0.5 s gap.
            Assert.That(tells[1].Time - tells[0].Time, Is.EqualTo(2.7f).Within(Dt * 2));
        }

        [TestCase(10f, "charge")]
        [TestCase(2f, "slam")]
        public void Reactive_PicksByDistance(float distance, string expected)
        {
            var strategy = GuardianStrategy.Create("reactive");
            var skills = new[] { "charge", "slam", "cleave" };

            var pick = strategy.Pick(new StrategyContext(skills, distance, new System.Random(1)));

            Assert.That(pick, Is.EqualTo(new[] { expected }));
        }

        [Test]
        public void Reactive_FallsBackToRotation_WhenNoPreferredSkill()
        {
            var strategy = GuardianStrategy.Create("reactive");
            var skills = new[] { "cleave", "fury", "rings" };

            var pick = strategy.Pick(new StrategyContext(skills, 10f, new System.Random(1)));

            Assert.That(pick, Is.EqualTo(new[] { "cleave" }));
        }

        [Test]
        public void SameSeedAndInputs_GiveTheSameFight()
        {
            var setup = new GuardianSetup("T2", new[] { "cleave", "slam", "charge", "embers" }, "aggressive");
            var a = TestBot.Play(PrototypeContent.StarterBuild, setup, seed: 42);
            var b = TestBot.Play(PrototypeContent.StarterBuild, setup, seed: 42);

            Assert.That(a.Events.Select(e => e.ToString()), Is.EqualTo(b.Events.Select(e => e.ToString())));
        }

        [Test]
        public void UnknownSetupIds_AreRejected()
        {
            Assert.Throws<System.ArgumentException>(() => Sim(PrototypeContent.StarterBuild, "T9", "steady", "cleave"));
            Assert.Throws<System.ArgumentException>(() => Sim(PrototypeContent.StarterBuild, "T1", "steady", "laser"));
            Assert.Throws<System.ArgumentException>(() => Sim(PrototypeContent.StarterBuild, "T1", "berserk", "cleave"));
        }
    }
}
