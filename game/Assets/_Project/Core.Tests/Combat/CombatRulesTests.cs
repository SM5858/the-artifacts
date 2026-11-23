using System.Linq;
using NUnit.Framework;
using TheArtifacts.Core.Combat;
using TheArtifacts.Core.Guardian;
using static TheArtifacts.Core.Tests.Combat.CombatTestUtil;

namespace TheArtifacts.Core.Tests.Combat
{
    public class CombatRulesTests
    {
        /// <summary>Player damage after the T3 tier's Def (plus any Guardian Def), as most rules tests fight at T3.</summary>
        static float AtT3(float damage, float guardianDef = 0f) =>
            damage * (1f - PlayerRules.Rating(PrototypeContent.Tier("T3").Def + guardianDef));

        // ---------- Content ----------

        [Test]
        public void TestBuilds_MatchCombatSpec()
        {
            var starter = PrototypeContent.StarterBuild;
            var geared = PrototypeContent.GearedBuild;

            Assert.That((starter.MaxHp, starter.MaxMp, starter.Strength, starter.Intelligence, starter.GearScore), Is.EqualTo((1000f, 100f, 40f, 40f, 0)));
            Assert.That((starter.PhysicalReduction, starter.CooldownReduction, starter.CritChance), Is.EqualTo((0f, 0f, 0f)));
            // Wreath of Alma Mater + Columbia Lion's Fang + Columbia Blue Robe.
            Assert.That((geared.MaxHp, geared.Strength, geared.Intelligence), Is.EqualTo((1200f, 55f, 40f)));
            Assert.That(geared.MaxMp, Is.EqualTo(130f).Within(1e-3));
            Assert.That((geared[StatKind.Def], geared[StatKind.Res], geared[StatKind.Precision]), Is.EqualTo((6f, 17f, 10f)));
            Assert.That(geared.GearScore, Is.EqualTo(6));
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
            Assert.That(PrototypeContent.Build("staff_of_alma_mater").Intelligence, Is.EqualTo(50f * 1.3f).Within(1e-3));
            Assert.That(PrototypeContent.Build("wreath_of_alma_mater").MaxMp, Is.EqualTo(100f * 1.3f).Within(1e-3));
        }

        [Test]
        public void EachGuardian_DropsItsLandmarksArtifacts()
        {
            Assert.That(PrototypeContent.LootPool("alma_mater").Select(a => a.Id), Is.EqualTo(new[] { "wreath_of_alma_mater", "staff_of_alma_mater" }));
            Assert.That(PrototypeContent.LootPool("columbia_lion").Select(a => a.Rarity), Is.EqualTo(new[] { Rarity.Heirloom, Rarity.Mythic }));
            Assert.That(PrototypeContent.LootPool("book_keeper").Select(a => a.Rarity), Is.EqualTo(new[] { Rarity.Relic, Rarity.Treasure }));
            Assert.That(PrototypeContent.Guardians.SelectMany(g => g.LootPool), Is.EquivalentTo(PrototypeContent.Artifacts.Select(a => a.Id)));
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
            Assert.Throws<System.ArgumentException>(() => PrototypeContent.Build("staff_of_alma_mater", "columbia_lions_fang"));
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

        [TestCase(0, "T1")]  // already at the lowest tier: nothing below to add
        [TestCase(3, "T1")]
        [TestCase(6, "T2")]
        public void Easing_AddsOnlyOneTierBelowTheRange(int gearScore, string lowest)
        {
            var normal = PrototypeContent.AllowedSpaceFor(gearScore).StatTiers;
            var eased = PrototypeContent.AllowedSpaceFor(gearScore, eased: true).StatTiers;
            Assert.That(eased.OrderBy(t => t).First(), Is.EqualTo(lowest));
            Assert.That(eased.Count - normal.Count, Is.LessThanOrEqualTo(1));
            Assert.That(normal, Is.SubsetOf(eased));
        }

        [TestCase("alma_mater", 10)]     // Relic only
        [TestCase("book_keeper", 50)]    // has a Treasure
        [TestCase("columbia_lion", 50)]  // has a Mythic
        public void EasingThreshold_FollowsThePoolsBestGrade(string guardianId, int threshold)
        {
            Assert.That(PrototypeContent.EasingThreshold(guardianId), Is.EqualTo(threshold));
            Assert.That(PrototypeContent.IsEased(guardianId, threshold - 1), Is.False);
            Assert.That(PrototypeContent.IsEased(guardianId, threshold), Is.True);
        }

        // ---------- Player abilities ----------

        [Test]
        public void HeavyStrike_DealsCoefficientTimesStrength_AndGoesOnCooldown()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "cleave", "slam", "charge");
            Engage(sim);

            Use(sim, Ability.HeavyStrike);
            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "heavy_strike"), Is.EqualTo(AtT3(100f)).Within(1e-3));

            RunFor(sim, 1.1f);
            Use(sim, Ability.HeavyStrike);
            Assert.That(sim.Events.Last(e => e.Type == CombatEventType.AbilityRejected).Id, Is.EqualTo("HeavyStrike:cooldown"));

            RunFor(sim, 5f);
            Use(sim, Ability.HeavyStrike);
            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "heavy_strike"), Is.EqualTo(AtT3(200f)).Within(1e-3));
        }

        [Test]
        public void GuardianDef_ReducesPhysicalDamage()
        {
            var sim = new CombatSim(PrototypeContent.StarterBuild, PrototypeContent.DefaultSetup, seed: 1,
                new GuardianProfile { Id = "test", Type = CreatureType.Beast, Def = 100f });
            Engage(sim);

            Use(sim, Ability.HeavyStrike);
            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "heavy_strike"), Is.EqualTo(AtT3(100f, guardianDef: 100f)).Within(1e-3));
        }

        // ---------- Lightning Bolt, Agility, Precision, Speed (Combat Spec 2, 3.2) ----------

        [TestCase("columbia_lion")]  // 100 + 1.5 × Intelligence 40 = 160, only the tier's Res
        [TestCase("alma_mater")]     // the lich: Res 400 on top → about 80% less
        public void LightningBolt_IsRangedMagic_ResistedByHighRes(string guardianId)
        {
            var res = PrototypeContent.GuardianById(guardianId).Res + PrototypeContent.Tier("T3").Res;
            var expected = 160f * (1f - PlayerRules.Rating(res));
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

        // ---------- Fire Bolt and lore targets (Combat Spec 3.2, GDD 6.7) ----------

        static CombatSim BookKeeper() => new CombatSim(PrototypeContent.StarterBuild, PrototypeContent.DefaultSetup, seed: 1,
            PrototypeContent.GuardianById("book_keeper"));

        static PlayerInput Cast(Ability ability, string target) => new PlayerInput(Vec2.Zero, ability, target: target);

        /// <summary>Walks toward a bookshelf until both spells can reach it.</summary>
        static LoreTarget WalkIntoRangeOf(CombatSim sim, string id)
        {
            var shelf = sim.FindLoreTarget(id);
            var range = System.Math.Min(PlayerRules.FireBoltRange, PlayerRules.LightningBoltRange) - 0.5f;
            // Veer a little to the side: the Guardian's body stands between the start and the shelf (Combat Spec 4.8).
            RunUntil(sim, s => Vec2.Distance(s.PlayerPosition, shelf.Position) <= range, 8f,
                s => new PlayerInput(((shelf.Position - s.PlayerPosition).Normalized() + new Vec2(0.3f, 0f)).Normalized()));
            return shelf;
        }

        [Test]
        public void FireBolt_DamagesTheGuardian()  // 60 + 1.0 × Intelligence 40 = 100, only the tier's Res
        {
            var sim = BookKeeper();
            Use(sim, Ability.FireBolt);

            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "fire_bolt"),
                Is.EqualTo(100f * (1f - PlayerRules.Rating(PrototypeContent.Tier("T3").Res))).Within(1e-3));
            Assert.That(sim.PlayerMp, Is.EqualTo(80f).Within(0.1f));
        }

        [Test]
        public void FireBolt_OnABookshelf_FearsTheBookKeeper_AndInterruptsItsTell()
        {
            var sim = BookKeeper();
            WalkIntoRangeOf(sim, "bookshelf_center");
            RunUntil(sim, s => s.Phase == GuardianPhase.Tell, 15f);
            Assert.That(sim.Phase, Is.EqualTo(GuardianPhase.Tell));
            var hp = sim.GuardianHp;
            var autos = DamageTo(sim, CombatEventType.GuardianDamaged, "auto");

            sim.Step(Dt, Cast(Ability.FireBolt, "bookshelf_center"));

            Assert.That(sim.Events.Any(e => e.Type == CombatEventType.TellInterrupted));
            Assert.That(sim.Events.Any(e => e.Type == CombatEventType.LoreTriggered && e.Id == "bookshelf_center"));
            Assert.That(sim.Phase, Is.EqualTo(GuardianPhase.Incapacitated));
            Assert.That(sim.ActiveStatus, Is.EqualTo(GuardianStatus.Fear));
            Assert.That(sim.ActiveTelegraphs, Is.Empty);
            Assert.That(sim.IsLoreSpent("bookshelf_center"));
            Assert.That(sim.IsOpening, "the panicked keeper is open to crits");
            Assert.That(sim.CritChance, Is.EqualTo(sim.Build.CritChance + PlayerRules.OpeningCritBonus).Within(1e-6));
            Assert.That(sim.GuardianHp, Is.EqualTo(hp - (DamageTo(sim, CombatEventType.GuardianDamaged, "auto") - autos)).Within(1e-3),
                "the bolt hits the shelf, not the Guardian");

            // Fear lasts its duration, then the Guardian fights again; a burned shelf cannot be used twice.
            RunFor(sim, PrototypeContent.BookshelfFearDuration + 0.1f);
            Assert.That(sim.Phase, Is.Not.EqualTo(GuardianPhase.Incapacitated));
            Assert.That(sim.Phase == GuardianPhase.Recovery || !sim.IsOpening, "the fear opening ends with the fear");
            RunFor(sim, PlayerRules.FireBoltCooldown);
            sim.Step(Dt, Cast(Ability.FireBolt, "bookshelf_center"));
            Assert.That(sim.Events.Last(e => e.Type == CombatEventType.AbilityRejected).Id, Is.EqualTo("FireBolt:target"));
        }

        [Test]
        public void LightningBolt_OnABookshelf_HasNoEffect()
        {
            var sim = BookKeeper();
            WalkIntoRangeOf(sim, "bookshelf_center");

            sim.Step(Dt, Cast(Ability.LightningBolt, "bookshelf_center"));

            Assert.That(sim.Events.Any(e => e.Type == CombatEventType.LoreNoEffect && e.Id == "bookshelf_center:LightningBolt"));
            Assert.That(sim.Events.Any(e => e.Type == CombatEventType.StatusApplied), Is.False);
            Assert.That(sim.IsLoreSpent("bookshelf_center"), Is.False);
        }

        [TestCase("book_keeper", "bookshelf_center", "range")]  // 28 m from the start, beyond the 25 m range
        [TestCase("columbia_lion", "bookshelf_center", "target")]  // only the Book Keeper has bookshelves
        public void FireBolt_AtALoreTarget_NeedsRangeAndAKnownTarget(string guardianId, string target, string reason)
        {
            var sim = new CombatSim(PrototypeContent.StarterBuild, PrototypeContent.DefaultSetup, seed: 1,
                PrototypeContent.GuardianById(guardianId));

            sim.Step(Dt, Cast(Ability.FireBolt, target));

            Assert.That(sim.Events.Last(e => e.Type == CombatEventType.AbilityRejected).Id, Is.EqualTo($"FireBolt:{reason}"));
        }

        // ---------- Weak points: Alma Mater's owl (GDD 6.7) ----------

        static CombatSim AlmaMater() => new CombatSim(PrototypeContent.StarterBuild, PrototypeContent.DefaultSetup, seed: 1,
            PrototypeContent.GuardianById("alma_mater"));

        [Test]
        public void Owl_PeeksOutOnItsSchedule()
        {
            var sim = AlmaMater();
            var owl = sim.FindWeakPoint("owl");
            bool At(float t) { RunUntil(sim, s => s.Time >= t, 60f); return sim.IsWeakPointExposed("owl"); }

            Assert.That(At(owl.FirstAppearance - 0.5f), Is.False);
            Assert.That(At(owl.FirstAppearance + 0.5f), Is.True);
            Assert.That(At(owl.FirstAppearance + owl.Duration + 0.5f), Is.False);
            Assert.That(At(owl.FirstAppearance + owl.Interval + 0.5f), Is.True);
        }

        /// <summary>Two identical fights up to <paramref name="time"/>; then Heavy Strike, aimed at the body and at the owl.</summary>
        static (float Body, float Owl, CombatSim OwlSim) HeavyStrikeAt(float time)
        {
            float Strike(string target, out CombatSim sim)
            {
                sim = AlmaMater();
                Engage(sim);
                RunUntil(sim, s => s.Time >= time, 60f, s => new PlayerInput((s.GuardianPosition - s.PlayerPosition).Normalized() * 0.01f));
                var before = DamageTo(sim, CombatEventType.GuardianDamaged, "heavy_strike");
                sim.Step(Dt, new PlayerInput(Vec2.Zero, Ability.HeavyStrike, target: target));
                return DamageTo(sim, CombatEventType.GuardianDamaged, "heavy_strike") - before;
            }
            var body = Strike(null, out _);
            var owl = Strike("owl", out var owlSim);
            return (body, owl, owlSim);
        }

        [Test]
        public void Owl_WhileOut_AttacksAimedAtItDealDoubleDamage()
        {
            var (body, owl, sim) = HeavyStrikeAt(12.5f);
            Assert.That(body, Is.GreaterThan(0f));
            Assert.That(owl, Is.EqualTo(2f * body).Within(1e-3));
            Assert.That(sim.Events.Any(e => e.Type == CombatEventType.WeakPointHit && e.Id == "owl"));
        }

        [Test]
        public void Owl_WhileHidden_AttacksAimedAtItHitTheBody()
        {
            var (body, owl, sim) = HeavyStrikeAt(16f);
            Assert.That(body, Is.GreaterThan(0f));
            Assert.That(owl, Is.EqualTo(body).Within(1e-3));
            Assert.That(sim.Events.Any(e => e.Type == CombatEventType.WeakPointHit), Is.False);
        }

        [Test]
        public void Owl_SpellsCanAimAtIt()
        {
            var sim = AlmaMater();
            Assert.That(sim.BlockReason(Ability.LightningBolt, "owl"), Is.Null);
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

            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "heavy_strike"), Is.EqualTo(AtT3(150f)).Within(1e-3));
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
            Use(sim, Ability.Overpower);

            Assert.That(sim.Events.Last(e => e.Type == CombatEventType.AbilityRejected).Id, Is.EqualTo("Overpower:gcd"));
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
            var sim = Sim(TestArtifacts.With(TestArtifacts.KeenEdge), "T3", "steady", "cleave", "slam", "charge");
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

        // ---------- Body radius (Combat Spec 4.8) ----------

        static CombatSim Lion(params string[] skills) => new CombatSim(PrototypeContent.StarterBuild,
            skills.Length == 0 ? PrototypeContent.DefaultSetup : new GuardianSetup("T3", skills, "steady"), seed: 1,
            PrototypeContent.GuardianById("columbia_lion"));

        [Test]
        public void BodyRadius_TheGuardianStopsItsChaseAtItsBody_NotItsCentre()
        {
            var sim = Lion();
            var radius = sim.Guardian.BodyRadius;
            Assert.That(radius, Is.GreaterThan(1f), "the lion is long; the player should not stand under its chin");

            RunUntil(sim, s => s.Phase != GuardianPhase.Idle, 4f);  // stand still while it closes in
            var centre = Vec2.Distance(sim.PlayerPosition, sim.GuardianPosition);

            Assert.That(centre, Is.EqualTo(GuardianRules.ChaseStopDistance + radius).Within(0.2));
            Assert.That(sim.DistanceToGuardian, Is.EqualTo(GuardianRules.ChaseStopDistance).Within(0.2));
            Assert.That(sim.InMeleeRange, Is.True);
        }

        [Test]
        public void BodyRadius_GrowsAreasCentredOnTheGuardian()
        {
            var sim = Lion("slam");
            RunUntil(sim, s => s.ActiveTelegraphs.Count > 0, 20f);

            var slam = (CircleArea)sim.ActiveTelegraphs[0].Area;
            Assert.That(slam.Radius, Is.EqualTo(PrototypeContent.Skill("slam").Range + sim.Guardian.BodyRadius).Within(1e-4));
        }

        [Test]
        public void BodyRadius_ThePlayerCannotWalkIntoTheBody()
        {
            var sim = Lion();
            RunFor(sim, 3f, s => new PlayerInput((s.GuardianPosition - s.PlayerPosition).Normalized()));

            Assert.That(Vec2.Distance(sim.PlayerPosition, sim.GuardianPosition), Is.GreaterThanOrEqualTo(sim.Guardian.BodyRadius - 1e-3));
            Assert.That(sim.DistanceToGuardian, Is.EqualTo(0f).Within(1e-3));
        }

        // ---------- Guardian skills, tells, recovery ----------

        [Test]
        public void StandingInCleave_TakesFullDamage_ThenTheOpeningRaisesCritChance()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "cleave", "slam", "charge");
            Engage(sim);
            Assert.That(sim.CritChance, Is.Zero);

            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 15f);

            Assert.That(DamageTo(sim, CombatEventType.PlayerDamaged, "cleave"), Is.EqualTo(200f));
            Assert.That(sim.CritChance, Is.EqualTo(PlayerRules.OpeningCritBonus));
        }

        [Test]
        public void Overpower_IsAnEightTimesStrengthHit_OnALongCooldown()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "cleave", "slam", "charge");
            Engage(sim);

            Use(sim, Ability.Overpower);   // outside an opening, so no crit for a starter

            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "overpower"), Is.EqualTo(AtT3(320f)).Within(1e-3));
            Assert.That(sim.CooldownRemaining(Ability.Overpower), Is.EqualTo(PlayerRules.OverpowerCooldown).Within(Dt));
        }

        [Test]
        public void Opening_CritsAboutHalfTheTime()
        {
            var crits = 0;
            const int fights = 200;
            for (var seed = 1; seed <= fights; seed++)
            {
                var sim = new CombatSim(PrototypeContent.StarterBuild, PrototypeContent.DefaultSetup, seed);
                Engage(sim);
                RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 15f);
                Use(sim, Ability.Overpower);
                if (sim.Events.Any(e => e.Type == CombatEventType.CriticalHit && e.Id == "overpower")) crits++;
            }
            Assert.That(crits / (double)fights, Is.EqualTo(PlayerRules.OpeningCritBonus).Within(0.1));
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
        public void StandingInSlam_TakesBaseDamage_AtAnyTier()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T5", "steady", "slam", "cleave", "charge");
            Engage(sim);

            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 15f);

            Assert.That(DamageTo(sim, CombatEventType.PlayerDamaged, "slam"), Is.EqualTo(250f).Within(1e-3));
        }

        // Tells scale by tier (Combat Spec 4.1) but never drop below the skill's fair minimum.
        [TestCase("T1", "slam", 1.6f * 1.4f)]
        [TestCase("T3", "slam", 1.6f)]
        [TestCase("T5", "slam", 1.6f * 0.8f)]
        [TestCase("T5", "embers", 0.9f)]   // 1.0 × 0.8 = 0.8 is below Embers' minimum of 0.9
        public void Tells_ScaleByTier_WithAFloor(string tier, string skill, float expected)
        {
            var sim = Sim(PrototypeContent.StarterBuild, tier, "steady", skill, "cleave", "charge");

            RunUntil(sim, s => s.ActiveTelegraphs.Count > 0, 15f);

            var t = sim.ActiveTelegraphs[0];
            Assert.That(t.Tell, Is.EqualTo(expected).Within(1e-4));
            Assert.That(t.HitTime - sim.Time, Is.EqualTo(expected).Within(Dt * 1.5f));
        }

        [Test]
        public void ShockRings_AreSafeUpClose()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "rings", "slam", "cleave");
            Engage(sim);

            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 15f);

            Assert.That(sim.Events.Any(e => e.Type == CombatEventType.SkillMissed && e.Id == "rings"), Is.True);
        }

        // Def 18 (18/118 ≈ 15.3% less); Guard: 60% less. Cleave is physical.
        [Test]
        public void DefAndGuard_ReducePhysicalDamage()
        {
            var expected = 200f * (1f - 18f / 118f) * 0.4f;
            var tough = new BaseStats { Hp = 1000f, Mp = 100f, Strength = 40f, Intelligence = 40f, Def = 18f };
            var sim = Sim(new PlayerBuild(tough, null), "T3", "steady", "cleave", "slam", "charge");
            Engage(sim);
            RunUntil(sim, s => s.Phase == GuardianPhase.Tell, 15f);

            Use(sim, Ability.Guard);
            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 3f);

            Assert.That(DamageTo(sim, CombatEventType.PlayerDamaged, "cleave"), Is.EqualTo(expected).Within(1e-3));
        }

        [Test]
        public void TypeTraits_OnlyApplyToTheirCreatureType()
        {
            var fang = PrototypeContent.Build("columbia_lions_fang");
            Assert.That(fang.VersusType(TraitEffectKind.DamageDealtVsType, CreatureType.Beast), Is.EqualTo(1.2f).Within(1e-6));
            Assert.That(fang.VersusType(TraitEffectKind.DamageDealtVsType, CreatureType.Undead), Is.EqualTo(1f));
        }

        // ---------- Columbia artifact traits ----------

        [Test]
        public void BlueRobe_ArtifactSkill_CutsDamageTaken()
        {
            // Slam 250 at T3; Robe Def 6 → 6/106 less; the S skill takes 30% off for 4 s.
            var sim = Sim(PrototypeContent.Build("columbia_blue_robe"), "T3", "steady", "slam", "cleave", "charge");
            Engage(sim);
            RunUntil(sim, s => s.Phase == GuardianPhase.Tell, 15f);

            sim.Step(Dt, new PlayerInput(Vec2.Zero, Ability.None, SkillBarSlot.Slot1));
            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 3f);

            Assert.That(DamageTo(sim, CombatEventType.PlayerDamaged, "slam"), Is.EqualTo(250f * (1f - 6f / 106f) * 0.7f).Within(1e-2));
            Assert.That(sim.ArtifactSkillCooldownRemaining(SkillBarSlot.Slot1), Is.GreaterThan(20f));
            Assert.That(sim.GcdRemaining, Is.Zero);
        }

        [Test]
        public void SkillBarSlot_WithNoSkill_IsRejected()
        {
            var sim = Sim(PrototypeContent.Build("wreath_of_alma_mater"), "T3", "steady", "slam", "cleave", "charge");
            sim.Step(Dt, new PlayerInput(Vec2.Zero, Ability.None, SkillBarSlot.Slot1));
            Assert.That(sim.Events.Last(e => e.Type == CombatEventType.AbilityRejected).Id, Is.EqualTo("Slot1:none"));
        }

        // ---------- Skill bar and auto-attack toggle (Combat Spec 3.2) ----------

        [Test]
        public void SkillBar_DefaultsToArtifactsWithSkills_AndCanBeRearranged()
        {
            var sim = Sim(PrototypeContent.GearedBuild, "T3", "steady", "slam", "cleave", "charge");
            Assert.That(sim.ArtifactSkillBar, Is.EqualTo(new[] { "columbia_blue_robe", null, null, null, null, null }),
                "only the Robe has an artifact skill; the Wreath and Fang are passive");

            sim.SwapArtifactSkills(SkillBarSlot.Slot1, SkillBarSlot.Slot6);
            Assert.That(sim.ArtifactAt(SkillBarSlot.Slot6).Id, Is.EqualTo("columbia_blue_robe"));
            Assert.That(sim.ArtifactAt(SkillBarSlot.Slot1), Is.Null);

            sim.SetArtifactSkillBar(new[] { null, "columbia_blue_robe" });
            Assert.That(sim.ArtifactAt(SkillBarSlot.Slot2).Id, Is.EqualTo("columbia_blue_robe"));
            Assert.Throws<System.ArgumentException>(() => sim.SetArtifactSkillBar(new[] { "wreath_of_alma_mater" }), "no skill");
            Assert.Throws<System.ArgumentException>(() => sim.SetArtifactSkillBar(new[] { "columbia_blue_robe", "columbia_blue_robe" }));
            Assert.Throws<System.ArgumentException>(() => sim.SetArtifactSkillBar(new string[7]));
        }

        [Test]
        public void AutoAttack_IsOffUntilToggled()
        {
            var sim = Sim(PrototypeContent.StarterBuild, "T3", "steady", "slam", "cleave", "charge");
            Engage(sim);
            RunFor(sim, 3f);
            Assert.That(sim.AutoAttacking, Is.False);
            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "auto"), Is.Zero);

            sim.Step(Dt, PlayerInput.None.WithToggleAutoAttack());
            RunFor(sim, 3f);
            Assert.That(sim.AutoAttacking, Is.True);
            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "auto"), Is.GreaterThan(0f));

            sim.Step(Dt, PlayerInput.None.WithToggleAutoAttack());
            Assert.That(sim.AutoAttacking, Is.False);
        }

        [Test]
        public void LionsPelt_SurvivesOneKillingBlowPerFight()
        {
            // 200 HP total; Slam hits for 250 × (1 − 12/112) ≈ 223, so each Slam would kill.
            var frail = new BaseStats { Hp = 50f, Mp = 100f, Strength = 40f, Intelligence = 40f };
            var sim = Sim(new PlayerBuild(frail, new[] { PrototypeContent.Artifact("columbia_lions_pelt") }), "T5", "steady", "slam", "cleave", "charge");
            Engage(sim);

            RunUntil(sim, s => s.Events.Any(e => e.Type == CombatEventType.SurvivedLethal), 15f);
            Assert.That(sim.PlayerHp, Is.LessThanOrEqualTo(1f));
            Assert.That(sim.Outcome, Is.EqualTo(CombatOutcome.Ongoing));

            RunFor(sim, 20f);
            Assert.That(sim.Outcome, Is.EqualTo(CombatOutcome.Loss));
            Assert.That(sim.Events.Count(e => e.Type == CombatEventType.SurvivedLethal), Is.EqualTo(1));
        }

        [Test]
        public void Stillstone_ReflectsBlockedDamage()
        {
            // Cleave 200, no armor; Guard blocks 120; Stillstone reflects 30% of that = 36.
            var sim = Sim(TestArtifacts.With(TestArtifacts.Stillstone), "T3", "steady", "cleave", "slam", "charge");
            Engage(sim);
            RunUntil(sim, s => s.Phase == GuardianPhase.Tell, 15f);

            Use(sim, Ability.Guard);
            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 3f);

            Assert.That(DamageTo(sim, CombatEventType.GuardianDamaged, "stillstone_reflect"), Is.EqualTo(AtT3(36f)).Within(1e-3));
        }

        [Test]
        public void EchoBlade_OverpowerInAnOpeningResetsLunge()
        {
            var sim = Sim(TestArtifacts.With(TestArtifacts.EchoBlade), "T3", "steady", "cleave", "slam", "charge");
            Use(sim, Ability.Lunge);
            Assert.That(sim.CooldownRemaining(Ability.Lunge), Is.GreaterThan(0f));
            Engage(sim);

            RunUntil(sim, s => s.Phase == GuardianPhase.Recovery, 15f);
            Use(sim, Ability.Overpower);

            Assert.That(sim.CooldownRemaining(Ability.Lunge), Is.Zero);
        }

        [Test]
        public void WardensPlate_GuardHealsFivePercentOfMaxHp()
        {
            // Max HP 1150 after the plate; standing in Slam leaves the player well below full.
            var sim = Sim(TestArtifacts.With(TestArtifacts.WardensPlate), "T3", "steady", "slam", "cleave", "charge");
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
            // cleave: 1.2 s tell × the T1 multiplier + 1.0 s recovery, then 0.5 s gap.
            var cleaveTell = 1.2f * PrototypeContent.Tier("T1").TellMultiplier;
            Assert.That(tells[1].Time - tells[0].Time, Is.EqualTo(cleaveTell + 1.5f).Within(Dt * 2));
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
