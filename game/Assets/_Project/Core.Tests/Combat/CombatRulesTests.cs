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

            Assert.That((starter.MaxHp, starter.Power, starter.Armor, starter.GearScore), Is.EqualTo((1000f, 40f, 0f, 0)));
            Assert.That(geared.MaxHp, Is.EqualTo(1300f));
            Assert.That(geared.Power, Is.EqualTo(60f));
            Assert.That(geared.Armor, Is.EqualTo(0.15f).Within(1e-6));
            Assert.That(geared.GearScore, Is.EqualTo(7));
        }

        [Test]
        public void Armor_IsCappedAtHalf()
        {
            Assert.That(new PlayerBuild(1000, 40, 0.9f, null).Armor, Is.EqualTo(0.5f));
        }

        [Test]
        public void RelicMultipliers_ApplyAfterFlatBonuses()
        {
            Assert.That(PrototypeContent.Build("worn_band").Power, Is.EqualTo(45f * 1.3f).Within(1e-3));
            Assert.That(PrototypeContent.Build("padded_vest").MaxHp, Is.EqualTo(1100f * 1.3f).Within(1e-3));
        }

        /// <summary>Design guardrail: every authored artifact stays within the ×2 per-artifact multiplier cap.</summary>
        [Test]
        public void EveryArtifact_RespectsTheMultiplierCap()
        {
            foreach (var artifact in PrototypeContent.Artifacts)
                Assert.DoesNotThrow(() => new PlayerBuild(1000f, 40f, 0f, new[] { artifact }), artifact.Id);
        }

        [Test]
        public void OneArtifact_CannotMultiplyAStatPastTwo()
        {
            var greedy = new ArtifactDef
            {
                Id = "test_greedy", Slot = ArtifactSlot.Helm, Rarity = Rarity.Wonder,
                Trait = new[]
                {
                    new TraitPart { Mode = TraitMode.Passive, Effects = new[]
                    {
                        new TraitEffect { Kind = TraitEffectKind.MultiplyStat, Stat = StatKind.Power, Value = 1.5f },
                        new TraitEffect { Kind = TraitEffectKind.MultiplyStat, Stat = StatKind.Power, Value = 1.5f },
                    } },
                },
            };
            Assert.Throws<System.ArgumentException>(() => new PlayerBuild(1000f, 40f, 0f, new[] { greedy }));
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
        public void HeavyStrike_DealsCoefficientTimesPower_AndGoesOnCooldown()
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
        public void GuardianArmor_ReducesPlayerDamage()
        {
            var tier = PrototypeContent.Tier("T3");
            tier.Armor = 0.25f;
            try
            {
                var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "cleave", "slam", "charge");
                Engage(sim);

                Use(sim, Ability.HeavyStrike);
                Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "heavy_strike"), Is.EqualTo(75f));
            }
            finally
            {
                tier.Armor = 0f;
            }
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

        // Geared: 15% armor; Iron Charm −10% and Scaled Coat −15% against Constructs. Guard: 60% less.
        // Against the Construct prototype Guardian: 200 × 0.85 × 0.9 × 0.85 × 0.4 = 52.02; against a Beast: 68.
        [TestCase(CreatureType.Construct, 52.02f)]
        [TestCase(CreatureType.Beast, 68f)]
        public void ArmorGuardAndTypeTraits_ReduceDamage(CreatureType guardianType, float expected)
        {
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
                Id = "test_aegis", Name = "Test Aegis", Slot = ArtifactSlot.LeftHand, Rarity = Rarity.Wonder,
                Trait = new[]
                {
                    new TraitPart
                    {
                        Mode = TraitMode.Triggered, Trigger = TraitTrigger.HitTaken,
                        Effects = new[] { new TraitEffect { Kind = TraitEffectKind.ApplyStatus, Status = GuardianStatus.Petrify, Value = 1f } },
                    },
                },
            };
            var sim = Sim(new PlayerBuild(1000f, 40f, 0f, new[] { aegis }), "T3", "steady", "embers", "slam", "cleave");

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
