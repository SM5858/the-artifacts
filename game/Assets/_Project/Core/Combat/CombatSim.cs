using System;
using System.Collections.Generic;
using System.Linq;
using TheArtifacts.Core.Guardian;

namespace TheArtifacts.Core.Combat
{
    public enum Ability { None, DoubleStrike, Guard, Lunge, Overpower, LightningBolt, FireBolt }

    /// <summary>
    /// The six artifact skill bar slots, on keys 1, 2, 3, 4, C, V (Combat Spec 3.2). The player chooses which
    /// equipped artifact's skill sits in each slot; artifact skills that do not fit cannot be used.
    /// </summary>
    public enum SkillBarSlot { None, Slot1, Slot2, Slot3, Slot4, Slot5, Slot6 }

    /// <summary>
    /// One tick of player input. Move is a direction on the floor; length above 1 is clamped.
    /// Target is a lore target id for a spell, or a weak point id for any attack (GDD 6.7); null means the Guardian.
    /// </summary>
    public readonly struct PlayerInput
    {
        public readonly Vec2 Move;
        public readonly Ability Ability;
        public readonly SkillBarSlot ArtifactSkill;
        public readonly string Target;
        /// <summary>Turns auto-attack on or off (key A).</summary>
        public readonly bool ToggleAutoAttack;

        public PlayerInput(Vec2 move, Ability ability = Ability.None, SkillBarSlot artifactSkill = SkillBarSlot.None,
            string target = null, bool toggleAutoAttack = false)
        {
            Move = move;
            Ability = ability;
            ArtifactSkill = artifactSkill;
            Target = target;
            ToggleAutoAttack = toggleAutoAttack;
        }

        public PlayerInput WithToggleAutoAttack() => new PlayerInput(Move, Ability, ArtifactSkill, Target, true);

        public static readonly PlayerInput None = new PlayerInput(Vec2.Zero);
    }

    public enum GuardianPhase
    {
        /// <summary>Chasing and auto-attacking.</summary>
        Idle,
        /// <summary>Telegraph shown; the player can react.</summary>
        Tell,
        /// <summary>Opening after a skill; player attacks gain crit chance (Combat Spec 2).</summary>
        Recovery,
        /// <summary>Combo: short pause before the second skill's tell.</summary>
        ComboGap,
        /// <summary>Under a player-applied status (stun, petrify, fear): no skills, chase, or auto-attacks.</summary>
        Incapacitated,
    }

    public enum CombatOutcome { Ongoing, Win, Loss }

    public enum CombatEventType
    {
        AbilityUsed,
        AbilityRejected,
        PlayerDamaged,
        PlayerHealed,
        GuardianDamaged,
        TellStarted,
        TelegraphPlaced,
        SkillHit,
        SkillMissed,
        BuffStarted,
        RecoveryStarted,
        Win,
        Loss,
        StatusApplied,
        StatusResisted,
        TellInterrupted,
        /// <summary>A player hit was critical; Id is its source, Amount the damage after armor.</summary>
        CriticalHit,
        /// <summary>A trait left the player at 1 HP instead of dying; Id is the artifact.</summary>
        SurvivedLethal,
        /// <summary>A spell of the right element hit a lore target; Id is the target. Its status follows.</summary>
        LoreTriggered,
        /// <summary>A spell of another element hit a lore target and did nothing; Id is "target:ability".</summary>
        LoreNoEffect,
        /// <summary>A player attack struck an exposed weak point; Id is the weak point, Amount the damage dealt.</summary>
        WeakPointHit,
        /// <summary>The player dashes in for a melee attack; Id is the ability ("auto" for an auto-attack swing).</summary>
        DashStarted,
        /// <summary>A hit staggered the Guardian; Id is the hit's source, Amount the stagger in seconds.</summary>
        GuardianStaggered,
    }

    /// <summary>Feedback for the UI and the per-attempt trace.</summary>
    public readonly struct CombatEvent
    {
        public readonly float Time;
        public readonly CombatEventType Type;
        public readonly string Id;
        public readonly float Amount;

        public CombatEvent(float time, CombatEventType type, string id, float amount)
        {
            Time = time;
            Type = type;
            Id = id;
            Amount = amount;
        }

        public override string ToString() => $"{Time,7:0.00}s {Type} {Id} {Amount:0.#}";
    }

    /// <summary>A telegraphed area waiting to hit. The area is fixed when it is placed.</summary>
    public sealed class Telegraph
    {
        public GuardianSkillDef Skill { get; }
        public Area Area { get; }
        public float HitTime { get; }
        /// <summary>This telegraph's tell after tier scaling (Combat Spec 4.1).</summary>
        public float Tell { get; }

        public Telegraph(GuardianSkillDef skill, Area area, float hitTime, float tell)
        {
            Skill = skill;
            Area = area;
            HitTime = hitTime;
            Tell = tell;
        }
    }

    /// <summary>
    /// One real-time fight: one player versus one Guardian. Engine-free and deterministic for a given
    /// seed and input sequence. The Guardian's setup is fixed for the whole fight and no model is called.
    /// </summary>
    public sealed class CombatSim
    {
        // ---- Public state (read by the UI and tests) ----
        public float Time { get; private set; }
        public CombatOutcome Outcome { get; private set; } = CombatOutcome.Ongoing;

        public PlayerBuild Build { get; }
        public Vec2 PlayerPosition { get; private set; }
        public float PlayerHp { get; private set; }
        public float PlayerMaxHp => Build.MaxHp;
        public float PlayerMp { get; private set; }
        public float PlayerMaxMp => Build.MaxMp;
        public float GcdRemaining { get; private set; }
        public float GuardRemaining { get; private set; }
        /// <summary>Auto-attack swings while this is on and the Guardian is in melee range. Off at the start.</summary>
        public bool AutoAttacking { get; private set; }
        /// <summary>Artifact ids in skill bar slots 1–6 (null for an empty slot).</summary>
        public IReadOnlyList<string> ArtifactSkillBar => _skillBar;

        public GuardianSetup Setup { get; }
        public TierDef Tier { get; }
        public Vec2 GuardianPosition { get; private set; }
        public Vec2 GuardianFacing { get; private set; }
        public float GuardianHp { get; private set; }
        public float GuardianMaxHp => Tier.MaxHp;
        public GuardianPhase Phase { get; private set; } = GuardianPhase.Idle;
        public GuardianSkillDef CurrentSkill { get; private set; }
        public bool IsFuryActive => Time < _furyEnd;
        public GuardianStatus? ActiveStatus { get; private set; }
        public GuardianProfile Guardian { get; }
        /// <summary>A hit has the Guardian reeling: it does not chase, auto-attack, or start a skill.</summary>
        public float StaggerRemaining { get; private set; }
        public bool IsStaggered => StaggerRemaining > 0f;
        /// <summary>The player is dashing in for a melee attack or leaping (movement input is ignored meanwhile).</summary>
        public bool IsDashing => _dash.HasValue;
        /// <summary>Through a Lunge leap, 0 at take-off to 1 on landing; -1 when not leaping.</summary>
        public float LeapProgress => _dash == Ability.Lunge ? Math.Min(1f, _dashTime / PlayerRules.LungeLeapTime) : -1f;
        /// <summary>The player is committed to an attack (dashing, leaping, or with a blow still to land): she cannot move.</summary>
        public bool IsCommitted => _dash.HasValue || _strikes.Count > 0;

        public IReadOnlyList<Telegraph> ActiveTelegraphs => _telegraphs;
        public IReadOnlyList<CombatEvent> Events => _events;

        // ---- Private state ----
        readonly Dictionary<Ability, float> _cooldowns = new Dictionary<Ability, float>
        {
            { Ability.DoubleStrike, 0f }, { Ability.Guard, 0f }, { Ability.Lunge, 0f }, { Ability.Overpower, 0f }, { Ability.LightningBolt, 0f },
            { Ability.FireBolt, 0f },
        };
        float _playerAutoTimer;
        readonly Dictionary<string, float> _equipmentCooldowns = new Dictionary<string, float>();
        readonly string[] _skillBar;
        readonly List<(float Multiplier, float End)> _damageTakenBuffs = new List<(float, float)>();
        readonly HashSet<TraitPart> _usedOncePerFight = new HashSet<TraitPart>();
        readonly HashSet<string> _spentLore = new HashSet<string>();
        float _loreOpeningEnd = float.NegativeInfinity;
        /// <summary>This tick's aimed weak point, or null.</summary>
        WeakPoint _aimedWeakPoint;
        /// <summary>The melee attack the player is dashing in to make (<see cref="Ability.None"/> for an auto-attack swing).</summary>
        Ability? _dash;
        float _dashTime, _leapTravelled;
        /// <summary>Blows still to land: Double Strike's second hit, Overpower after its wind-up; each keeps its aim.</summary>
        readonly List<(float Time, Ability Ability, WeakPoint Aim)> _strikes = new List<(float, Ability, WeakPoint)>();

        readonly GuardianStrategy _strategy;
        readonly Random _rng;
        readonly Queue<GuardianSkillDef> _queued = new Queue<GuardianSkillDef>();
        readonly List<Telegraph> _telegraphs = new List<Telegraph>();
        readonly List<CombatEvent> _events = new List<CombatEvent>();
        float _nextSkillTime;
        float _phaseEnd;
        int _pulsesLeft;
        float _nextPulseTime;
        float _guardianAutoTimer;
        float _furyEnd = float.NegativeInfinity;
        float _furyMultiplier = 1f;
        readonly float _dealtVsType;
        readonly float _takenVsType;

        /// <param name="guardian">The Guardian's type and immunities; defaults to the prototype Guardian.</param>
        public CombatSim(PlayerBuild build, GuardianSetup setup, int seed, GuardianProfile guardian = null)
        {
            Guardian = guardian ?? PrototypeContent.Guardian;
            Build = build ?? throw new ArgumentNullException(nameof(build));
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            Tier = PrototypeContent.Tier(setup.StatTier) ?? throw new ArgumentException($"Unknown tier '{setup.StatTier}'.");
            if (setup.SkillIds == null || setup.SkillIds.Count == 0)
                throw new ArgumentException("A setup needs at least one skill.");
            foreach (var id in setup.SkillIds)
                if (PrototypeContent.Skill(id) == null) throw new ArgumentException($"Unknown skill '{id}'.");

            _strategy = GuardianStrategy.Create(setup.StrategyId);
            _rng = new Random(seed);
            _dealtVsType = build.VersusType(TraitEffectKind.DamageDealtVsType, Guardian.Type);
            _takenVsType = build.VersusType(TraitEffectKind.DamageTakenVsType, Guardian.Type);

            PlayerHp = build.MaxHp;
            PlayerMp = build.MaxMp;
            GuardianHp = Tier.MaxHp;
            GuardianPosition = Vec2.Zero;
            PlayerPosition = new Vec2(0f, -GuardianRules.StartDistance);
            GuardianFacing = new Vec2(0f, -1f);
            _nextSkillTime = _strategy.Interval;
            _skillBar = build.DefaultSkillBar.ToArray();
        }

        /// <summary>From the player to the Guardian's body (its centre less <see cref="GuardianProfile.BodyRadius"/>).</summary>
        public float DistanceToGuardian => Math.Max(0f, Vec2.Distance(PlayerPosition, GuardianPosition) - Guardian.BodyRadius);
        public bool InMeleeRange => DistanceToGuardian <= PlayerRules.MeleeRange;
        /// <summary>A skill's tell at this fight's tier: base × tier multiplier, never below the skill's fair minimum.</summary>
        public float TellFor(GuardianSkillDef skill) => Math.Max(skill.MinTell, skill.Tell * Tier.TellMultiplier);

        /// <summary>
        /// An opening: the Guardian is recovering from a skill, or under a lore status that grants one
        /// (the Book Keeper's fear, GDD 6.7).
        /// </summary>
        public bool IsOpening =>
            Phase == GuardianPhase.Recovery || (Phase == GuardianPhase.Incapacitated && Time < _loreOpeningEnd);

        /// <summary>Crit chance right now: the Precision rating, plus the opening bonus during an opening.</summary>
        public float CritChance =>
            Math.Min(1f, Build.CritChance + (IsOpening ? PlayerRules.OpeningCritBonus : 0f));

        /// <summary>This Guardian's lore target with the given id, or null.</summary>
        public LoreTarget FindLoreTarget(string id) => Guardian.LoreTargets.FirstOrDefault(t => t.Id == id);

        /// <summary>This Guardian's weak point with the given id, or null.</summary>
        public WeakPoint FindWeakPoint(string id) => Guardian.WeakPoints.FirstOrDefault(w => w.Id == id);

        /// <summary>Whether the weak point is out right now (its fixed schedule).</summary>
        public bool IsWeakPointExposed(string id)
        {
            var w = FindWeakPoint(id);
            if (w == null) return false;
            var t = Time - w.FirstAppearance;
            return t >= 0f && t % w.Interval < w.Duration;
        }

        /// <summary>A lore target works once per fight.</summary>
        public bool IsLoreSpent(string id) => _spentLore.Contains(id);

        public float CooldownRemaining(Ability ability) => _cooldowns.TryGetValue(ability, out var t) ? t : 0f;

        /// <summary>Advances the fight by <paramref name="dt"/> seconds with this tick's input.</summary>
        public void Step(float dt, PlayerInput input)
        {
            if (Outcome != CombatOutcome.Ongoing) return;
            if (dt <= 0f) throw new ArgumentOutOfRangeException(nameof(dt));

            Time += dt;
            TickPlayerTimers(dt);
            _aimedWeakPoint = input.Target != null ? FindWeakPoint(input.Target) : null;
            if (_dash.HasValue) Dash(dt);
            else if (_strikes.Count == 0) MovePlayer(input.Move, dt);
            LandStrikes();
            if (CheckEnd()) return;
            if (input.ToggleAutoAttack)
            {
                AutoAttacking = !AutoAttacking;
                Emit(CombatEventType.AbilityUsed, AutoAttacking ? "auto_attack_on" : "auto_attack_off", 0f);
            }
            if (input.Ability != Ability.None) TryUse(input.Ability, input.Target);
            if (input.ArtifactSkill != SkillBarSlot.None) TryUseArtifactSkill(input.ArtifactSkill);
            PlayerAutoAttack(standing: input.Move.LengthSquared < 1e-8f);
            if (CheckEnd()) return;

            UpdateGuardian(dt);
            CheckEnd();
        }

        // ---------------- Player ----------------

        void TickPlayerTimers(float dt)
        {
            foreach (var key in _cooldowns.Keys.ToArray())
                _cooldowns[key] = Math.Max(0f, _cooldowns[key] - dt);
            GcdRemaining = Math.Max(0f, GcdRemaining - dt);
            GuardRemaining = Math.Max(0f, GuardRemaining - dt);
            _playerAutoTimer = Math.Max(0f, _playerAutoTimer - dt);
            PlayerMp = Math.Min(Build.MaxMp, PlayerMp + PlayerRules.MpRegenPerSecond * dt);
            foreach (var key in _equipmentCooldowns.Keys.ToArray())
                _equipmentCooldowns[key] = Math.Max(0f, _equipmentCooldowns[key] - dt);
            _damageTakenBuffs.RemoveAll(b => b.End <= Time);
        }

        // ---------------- Equipment skills (Combat Spec 3.2, 5.4) ----------------

        static TraitPart EquipmentSkill(ArtifactDef artifact) =>
            artifact?.Trait.FirstOrDefault(p => p.Mode == TraitMode.EquipmentSkill);

        /// <summary>The equipped artifact whose skill sits in this bar slot, or null.</summary>
        public ArtifactDef ArtifactAt(SkillBarSlot slot)
        {
            var i = (int)slot - 1;
            if (i < 0 || i >= _skillBar.Length || _skillBar[i] == null) return null;
            return Build.Artifacts.FirstOrDefault(a => a.Id == _skillBar[i]);
        }

        /// <summary>
        /// Rearranges the skill bar (the player's choice, Combat Spec 3.2): up to six ids, null for an empty slot.
        /// Each id must be an equipped artifact with an equipment skill, at most once. Cooldowns follow the artifact.
        /// </summary>
        public void SetArtifactSkillBar(IReadOnlyList<string> ids)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            if (ids.Count > PlayerRules.SkillBarSize) throw new ArgumentException($"At most {PlayerRules.SkillBarSize} slots.");
            var placed = ids.Where(id => id != null).ToArray();
            if (placed.Distinct().Count() != placed.Length) throw new ArgumentException("An artifact can sit in only one slot.");
            foreach (var id in placed)
                if (EquipmentSkill(Build.Artifacts.FirstOrDefault(a => a.Id == id)) == null)
                    throw new ArgumentException($"'{id}' is not an equipped artifact with an equipment skill.");
            for (var i = 0; i < _skillBar.Length; i++) _skillBar[i] = i < ids.Count ? ids[i] : null;
        }

        /// <summary>Swaps two skill bar slots.</summary>
        public void SwapArtifactSkills(SkillBarSlot a, SkillBarSlot b)
        {
            int i = (int)a - 1, j = (int)b - 1;
            if (i < 0 || j < 0 || i >= _skillBar.Length || j >= _skillBar.Length) return;
            (_skillBar[i], _skillBar[j]) = (_skillBar[j], _skillBar[i]);
        }

        public float ArtifactSkillCooldownRemaining(SkillBarSlot slot)
        {
            var artifact = ArtifactAt(slot);
            return artifact != null && _equipmentCooldowns.TryGetValue(artifact.Id, out var t) ? t : 0f;
        }

        /// <summary>Why this slot's artifact skill cannot be cast now ("none" if the slot is empty), or null.</summary>
        public string ArtifactSkillBlockReason(SkillBarSlot slot)
        {
            if (EquipmentSkill(ArtifactAt(slot)) == null) return "none";
            if (ArtifactSkillCooldownRemaining(slot) > 0f) return "cooldown";
            return null;
        }

        /// <summary>Artifact skills cost no MP and neither need nor trigger the GCD.</summary>
        void TryUseArtifactSkill(SkillBarSlot slot)
        {
            var reason = ArtifactSkillBlockReason(slot);
            if (reason != null)
            {
                Emit(CombatEventType.AbilityRejected, $"{slot}:{reason}", 0f);
                return;
            }

            var artifact = ArtifactAt(slot);
            var part = EquipmentSkill(artifact);
            foreach (var effect in part.Effects) ApplyTraitEffect(artifact, effect, 0f);
            _equipmentCooldowns[artifact.Id] = part.Cooldown * (1f - Build.CooldownReduction);
            Emit(CombatEventType.AbilityUsed, artifact.Id, 0f);
        }

        void MovePlayer(Vec2 move, float dt)
        {
            if (move.LengthSquared < 1e-8f) return;
            var dir = move.LengthSquared > 1f ? move.Normalized() : move;
            PlayerPosition = KeepOutOfBody(ClampToArena(PlayerPosition + dir * (Build.RunSpeed * dt)));
        }

        /// <summary>The player slides around the Guardian's body instead of walking into it.</summary>
        Vec2 KeepOutOfBody(Vec2 p)
        {
            var r = Guardian.BodyRadius;
            var away = p - GuardianPosition;
            if (r <= 0f || away.LengthSquared >= r * r) return p;
            return away.LengthSquared > 1e-8f ? GuardianPosition + away.Normalized() * r : GuardianPosition - GuardianFacing * r;
        }

        static bool IsSpell(Ability ability) => ability == Ability.LightningBolt || ability == Ability.FireBolt;

        static (float ManaCost, float Range) SpellCost(Ability ability) => ability == Ability.FireBolt
            ? (PlayerRules.FireBoltManaCost, PlayerRules.FireBoltRange)
            : (PlayerRules.LightningBoltManaCost, PlayerRules.LightningBoltRange);

        /// <summary>
        /// Why an ability cannot be used right now, or null if it can. A spell may target a lore target by id
        /// ("target" if it is unknown or spent); other abilities always hit the Guardian.
        /// </summary>
        public string BlockReason(Ability ability, string target = null)
        {
            if (target != null && FindWeakPoint(target) != null) target = null;  // a weak point is on the Guardian
            if (CooldownRemaining(ability) > 0f) return "cooldown";
            if (GcdRemaining > 0f) return "gcd";
            if (IsDashMelee(ability) && DistanceToGuardian > PlayerRules.DashInRange) return "range";
            if (IsSpell(ability))
            {
                var (manaCost, range) = SpellCost(ability);
                if (PlayerMp < manaCost) return "mana";
                if (target != null)
                {
                    var lore = FindLoreTarget(target);
                    if (lore == null || IsLoreSpent(target)) return "target";
                    if (Vec2.Distance(PlayerPosition, lore.Position) > range) return "range";
                }
                else if (DistanceToGuardian > range) return "range";
            }
            return null;
        }

        void TryUse(Ability ability, string target = null)
        {
            if (!IsSpell(ability) || (target != null && FindWeakPoint(target) != null)) target = null;
            var reason = BlockReason(ability, target);
            if (reason != null)
            {
                Emit(CombatEventType.AbilityRejected, $"{ability}:{reason}", 0f);
                return;
            }

            // Out of melee range but close enough: dash in and strike on arrival. The GCD starts now, so nothing else is used
            // on the way; the cooldown starts when the attack lands.
            if (IsDashMelee(ability) && !InMeleeRange)
            {
                StartDash(ability);
                GcdRemaining = PlayerRules.GlobalCooldown;
                return;
            }
            Perform(ability, target);
        }

        static bool IsDashMelee(Ability ability) => ability == Ability.DoubleStrike || ability == Ability.Overpower;

        void StartDash(Ability ability)
        {
            _dash = ability;
            _dashTime = _leapTravelled = 0f;
            if (ability != Ability.Lunge) Emit(CombatEventType.DashStarted, ability == Ability.None ? "auto" : ability.ToString(), 0f);
        }

        /// <summary>Runs the player at the Guardian; on arrival the attack lands, and a dash too long is called off.</summary>
        void Dash(float dt)
        {
            if (_dash == Ability.Lunge)
            {
                Leap(dt);
                return;
            }
            _dashTime += dt;
            var toGuardian = GuardianPosition - PlayerPosition;
            var gap = DistanceToGuardian - PlayerRules.DashStopDistance;
            if (gap > 0f && toGuardian.LengthSquared > 1e-8f)
                PlayerPosition = KeepOutOfBody(ClampToArena(PlayerPosition + toGuardian.Normalized() * Math.Min(gap, PlayerRules.DashSpeed * dt)));
            var ability = _dash.Value;
            if (DistanceToGuardian <= PlayerRules.DashStopDistance + 0.05f)
            {
                _dash = null;
                if (ability == Ability.None) _playerAutoTimer = 0f;  // the swing follows in PlayerAutoAttack this tick
                else Perform(ability, null);
            }
            else if (_dashTime >= PlayerRules.DashMaxTime)
            {
                _dash = null;
                Emit(CombatEventType.AbilityRejected, $"{(ability == Ability.None ? "auto" : ability.ToString())}:range", 0f);
            }
        }

        /// <summary>
        /// Lunge's leap: toward the Guardian's body (re-aimed each tick, as it may move) at a pace that lands on time, at
        /// most <see cref="PlayerRules.LungeDistance"/> in all; in place when she is already there. It strikes on landing.
        /// </summary>
        void Leap(float dt)
        {
            var left = Math.Max(dt, PlayerRules.LungeLeapTime - _dashTime);
            _dashTime += dt;
            var gap = Math.Min(DistanceToGuardian - PlayerRules.DashStopDistance, PlayerRules.LungeDistance - _leapTravelled);
            var toGuardian = GuardianPosition - PlayerPosition;
            if (gap > 0f && toGuardian.LengthSquared > 1e-8f)
            {
                var step = gap * Math.Min(1f, dt / left);
                PlayerPosition = KeepOutOfBody(ClampToArena(PlayerPosition + toGuardian.Normalized() * step));
                _leapTravelled += step;
            }
            if (_dashTime < PlayerRules.LungeLeapTime - 1e-4f) return;
            _dash = null;
            Land(Ability.Lunge, _aimedWeakPoint);
        }

        /// <summary>Lands the blows that are due.</summary>
        void LandStrikes()
        {
            for (var i = 0; i < _strikes.Count;)
            {
                if (_strikes[i].Time > Time + 1e-4f) { i++; continue; }
                var (_, ability, aim) = _strikes[i];
                _strikes.RemoveAt(i);
                Land(ability, aim);
            }
        }

        /// <summary>A melee blow: it hits if the Guardian is in reach, keeping the aim it was made with; otherwise it whiffs.</summary>
        void Land(Ability ability, WeakPoint aim)
        {
            if (!InMeleeRange)
            {
                Emit(CombatEventType.AbilityRejected, $"{ability}:range", 0f);
                return;
            }
            var aimed = _aimedWeakPoint;
            _aimedWeakPoint = aim;
            switch (ability)
            {
                case Ability.DoubleStrike:
                    DamageGuardian(PlayerRules.DoubleStrikeCoefficient * Build.Strength, "double_strike", DamageType.Physical, canCrit: true,
                        stagger: PlayerRules.DoubleStrikeStagger);
                    break;
                case Ability.Lunge:
                    DamageGuardian(PlayerRules.LungeCoefficient * Build.Strength, "lunge", DamageType.Physical, canCrit: true,
                        stagger: PlayerRules.LungeStagger);
                    break;
                case Ability.Overpower:
                    DamageGuardian(PlayerRules.OverpowerCoefficient * Build.Strength, "overpower", DamageType.Physical, canCrit: true,
                        stagger: PlayerRules.OverpowerStagger);
                    break;
            }
            _aimedWeakPoint = aimed;
        }

        void Perform(Ability ability, string target)
        {
            var inRecovery = Phase == GuardianPhase.Recovery;
            switch (ability)
            {
                case Ability.DoubleStrike:
                    // The first hit now, the second a moment later; she holds her ground between them.
                    Land(ability, _aimedWeakPoint);
                    _strikes.Add((Time + PlayerRules.DoubleStrikeInterval, ability, _aimedWeakPoint));
                    _cooldowns[ability] = Cooldown(ability, PlayerRules.DoubleStrikeCooldown);
                    break;

                case Ability.Guard:
                    GuardRemaining = Build.Passive(TraitEffectKind.SetGuardDuration)?.Value ?? PlayerRules.GuardDuration;
                    _cooldowns[ability] = Cooldown(ability, PlayerRules.GuardCooldown);
                    break;

                case Ability.Lunge:
                    // Leaps up and toward the Guardian, cutting down as she lands (Leap).
                    StartDash(ability);
                    _cooldowns[ability] = Cooldown(ability, PlayerRules.LungeCooldown);
                    break;

                case Ability.Overpower:
                    // Gathers itself, then the blow lands.
                    _strikes.Add((Time + PlayerRules.OverpowerWindup, ability, _aimedWeakPoint));
                    _cooldowns[ability] = Cooldown(ability, PlayerRules.OverpowerCooldown);
                    break;

                case Ability.LightningBolt:
                    PlayerMp -= PlayerRules.LightningBoltManaCost;
                    if (target != null) HitLore(FindLoreTarget(target), Element.Lightning, ability);
                    else DamageGuardian(PlayerRules.LightningBoltBaseDamage + PlayerRules.LightningBoltCoefficient * Build.Intelligence, "lightning_bolt", DamageType.Magic, canCrit: true,
                        stagger: PlayerRules.SpellStagger);
                    _cooldowns[ability] = Cooldown(ability, PlayerRules.LightningBoltCooldown);
                    break;

                case Ability.FireBolt:
                    PlayerMp -= PlayerRules.FireBoltManaCost;
                    if (target != null) HitLore(FindLoreTarget(target), Element.Fire, ability);
                    else DamageGuardian(PlayerRules.FireBoltBaseDamage + PlayerRules.FireBoltCoefficient * Build.Intelligence, "fire_bolt", DamageType.Magic, canCrit: true,
                        stagger: PlayerRules.SpellStagger);
                    _cooldowns[ability] = Cooldown(ability, PlayerRules.FireBoltCooldown);
                    break;
            }

            GcdRemaining = PlayerRules.GlobalCooldown;
            Emit(CombatEventType.AbilityUsed, ability.ToString(), 0f);
            FireTraits(TraitTrigger.AbilityUsed, ability, 0f, inRecovery);
        }

        /// <summary>A spell of the lore target's element spends it and puts its status on the Guardian (GDD 6.7).</summary>
        void HitLore(LoreTarget lore, Element element, Ability ability)
        {
            if (lore.Element != element)
            {
                Emit(CombatEventType.LoreNoEffect, $"{lore.Id}:{ability}", 0f);
                return;
            }
            _spentLore.Add(lore.Id);
            Emit(CombatEventType.LoreTriggered, lore.Id, 0f);
            ApplyGuardianStatus(lore.Status, lore.Duration);
            if (lore.GrantsOpening && Phase == GuardianPhase.Incapacitated)
                _loreOpeningEnd = Math.Max(_loreOpeningEnd, Time + lore.Duration);
        }

        /// <summary>The ability's cooldown (a trait may replace the base), shortened by Agility.</summary>
        float Cooldown(Ability ability, float baseCooldown) =>
            (Build.Passive(TraitEffectKind.SetCooldown, ability)?.Value ?? baseCooldown) * (1f - Build.CooldownReduction);

        /// <param name="standing">No movement input this tick: a swing that is due may dash in. While the player moves
        /// (dodging) or the Guardian is telegraphing a skill, auto-attack never pulls her in.</param>
        void PlayerAutoAttack(bool standing)
        {
            if (!AutoAttacking || _playerAutoTimer > 0f || IsCommitted) return;
            if (!InMeleeRange)
            {
                if (standing && Phase != GuardianPhase.Tell && DistanceToGuardian <= PlayerRules.DashInRange) StartDash(Ability.None);
                return;
            }
            DamageGuardian(PlayerRules.AutoCoefficient * Build.Strength, "auto", DamageType.Physical, canCrit: true,
                stagger: PlayerRules.AutoStagger);
            _playerAutoTimer = PlayerRules.AutoSwing;
        }

        /// <summary>
        /// All player damage, reflects included, is scaled by type traits and reduced by the Guardian's Def or Res
        /// (Combat Spec 2). Attacks may crit; the roll is skipped when the chance is 0 (no Precision, no opening).
        /// </summary>
        /// <param name="stagger">How long the hit staggers the Guardian (0 for reflects and other indirect damage).</param>
        void DamageGuardian(float amount, string source, DamageType type, bool canCrit = false, float stagger = 0f)
        {
            var chance = CritChance;
            var crit = canCrit && chance > 0f && _rng.NextDouble() < chance;
            if (crit) amount *= PlayerRules.CritMultiplier;
            var weakPoint = canCrit && _aimedWeakPoint != null && IsWeakPointExposed(_aimedWeakPoint.Id) ? _aimedWeakPoint : null;
            if (weakPoint != null) amount *= weakPoint.DamageMultiplier;
            var resist = type == DamageType.Physical ? Guardian.Def + Tier.Def : Guardian.Res + Tier.Res;
            amount *= _dealtVsType * (1f - PlayerRules.Rating(resist));
            GuardianHp = Math.Max(0f, GuardianHp - amount);
            Emit(CombatEventType.GuardianDamaged, source, amount);
            if (crit) Emit(CombatEventType.CriticalHit, source, amount);
            if (weakPoint != null) Emit(CombatEventType.WeakPointHit, weakPoint.Id, amount);
            if (stagger > 0f && amount > 0f && CanStagger && stagger > StaggerRemaining)
            {
                StaggerRemaining = stagger;
                Emit(CombatEventType.GuardianStaggered, source, stagger);
            }
        }

        /// <summary>
        /// Whether a hit staggers the Guardian now. Not during a tell: it is committed to a telegraphed skill, so the
        /// player cannot cancel it by hitting (only a status interrupts a tell, Section 4.5). Not while incapacitated, which
        /// already stops it. A Guardian guard stance or damage immunity, when added, belongs here too.
        /// </summary>
        public bool CanStagger => Phase != GuardianPhase.Tell && Phase != GuardianPhase.Incapacitated;

        void HealPlayer(float amount, string source)
        {
            var healed = Math.Min(amount, Build.MaxHp - PlayerHp);
            PlayerHp += healed;
            Emit(CombatEventType.PlayerHealed, source, healed);
        }

        /// <summary>Damage taken = base × (1 − Def or Res rating) × type traits, reduced by Guard (Combat Spec 2). Tiers do not scale damage.</summary>
        void DamagePlayer(float baseDamage, string source, DamageType type)
        {
            var reduction = type == DamageType.Physical ? Build.PhysicalReduction : Build.MagicReduction;
            var amount = baseDamage * (1f - reduction) * _takenVsType;
            foreach (var buff in _damageTakenBuffs) amount *= buff.Multiplier;
            if (GuardRemaining > 0f)
            {
                var blocked = amount * PlayerRules.GuardReduction;
                amount -= blocked;
                FireTraits(TraitTrigger.DamageBlocked, Ability.None, blocked, Phase == GuardianPhase.Recovery);
            }
            if (amount >= PlayerHp && TrySurviveLethal()) amount = PlayerHp - 1f;
            PlayerHp = Math.Max(0f, PlayerHp - amount);
            Emit(CombatEventType.PlayerDamaged, source, amount);
            FireTraits(TraitTrigger.HitTaken, Ability.None, amount, Phase == GuardianPhase.Recovery);
        }

        // ---------------- Traits (Combat Spec 5.4) ----------------

        /// <summary>A lethal-damage trait that is still available leaves the player at 1 HP.</summary>
        bool TrySurviveLethal()
        {
            foreach (var (artifact, part) in Build.TraitParts)
            {
                if (part.Mode != TraitMode.Triggered || part.Trigger != TraitTrigger.LethalDamage) continue;
                if (part.OncePerFight && _usedOncePerFight.Contains(part)) continue;
                if (!part.Effects.Any(e => e.Kind == TraitEffectKind.SurviveAtOneHp)) continue;
                if (part.OncePerFight) _usedOncePerFight.Add(part);
                Emit(CombatEventType.SurvivedLethal, artifact.Id, 0f);
                return true;
            }
            return false;
        }

        /// <param name="amount">The trigger's amount: damage taken or blocked, 0 for an ability.</param>
        void FireTraits(TraitTrigger trigger, Ability ability, float amount, bool guardianInRecovery)
        {
            foreach (var (artifact, part) in Build.TraitParts)
            {
                if (part.Mode != TraitMode.Triggered || part.Trigger != trigger) continue;
                if (trigger == TraitTrigger.AbilityUsed && part.Ability != ability) continue;
                if (part.RequiresGuardianRecovery && !guardianInRecovery) continue;
                if (part.OncePerFight && _usedOncePerFight.Contains(part)) continue;
                if (part.Chance < 1f && _rng.NextDouble() >= part.Chance) continue;
                if (part.OncePerFight) _usedOncePerFight.Add(part);
                foreach (var effect in part.Effects) ApplyTraitEffect(artifact, effect, amount);
            }
        }

        void ApplyTraitEffect(ArtifactDef artifact, TraitEffect effect, float amount)
        {
            switch (effect.Kind)
            {
                case TraitEffectKind.HealPercentMaxHp:
                    HealPlayer(effect.Value * Build.MaxHp, artifact.Id);
                    break;
                case TraitEffectKind.ResetCooldown:
                    _cooldowns[effect.Ability] = 0f;
                    break;
                case TraitEffectKind.ReflectPercent:
                    DamageGuardian(effect.Value * amount, $"{artifact.Id}_reflect", DamageType.Physical);
                    break;
                case TraitEffectKind.ApplyStatus:
                    ApplyGuardianStatus(effect.Status, effect.Value);
                    break;
                case TraitEffectKind.DamageTakenBuff:
                    _damageTakenBuffs.Add((1f + effect.Value, Time + effect.Duration));
                    break;
                // SetCooldown and SetGuardDuration are passive and read where the rule applies.
            }
        }

        /// <summary>A status interrupts the Guardian's current skill unless the Guardian is immune (Combat Spec 4.5).</summary>
        public void ApplyGuardianStatus(GuardianStatus status, float duration)
        {
            if (Guardian.Immunities.Contains(status))
            {
                Emit(CombatEventType.StatusResisted, status.ToString(), 0f);
                return;
            }

            if (Phase == GuardianPhase.Tell) Emit(CombatEventType.TellInterrupted, CurrentSkill.Id, 0f);
            _telegraphs.Clear();
            _queued.Clear();
            _pulsesLeft = 0;
            CurrentSkill = null;
            StaggerRemaining = 0f;

            var end = Time + duration;
            _phaseEnd = Phase == GuardianPhase.Incapacitated ? Math.Max(_phaseEnd, end) : end;
            Phase = GuardianPhase.Incapacitated;
            ActiveStatus = status;
            Emit(CombatEventType.StatusApplied, status.ToString(), duration);
        }

        // ---------------- Guardian ----------------

        void UpdateGuardian(float dt)
        {
            if (StaggerRemaining > 0f)
            {
                StaggerRemaining = Math.Max(0f, StaggerRemaining - dt);
                // Reeling: no chase, no auto-attack (its swing timer waits too, so it cannot strike back the moment it
                // recovers), and no new skill; a skill whose time came starts as soon as the stagger ends.
                if (Phase == GuardianPhase.Idle) return;
                if (Phase == GuardianPhase.ComboGap)
                {
                    _phaseEnd = Math.Max(_phaseEnd, Time + StaggerRemaining);
                    return;
                }
            }
            _guardianAutoTimer = Math.Max(0f, _guardianAutoTimer - dt);

            switch (Phase)
            {
                case GuardianPhase.Idle:
                    if (Time >= _nextSkillTime)
                    {
                        var picks = _strategy.Pick(new StrategyContext(Setup.SkillIds, DistanceToGuardian, _rng));
                        foreach (var id in picks) _queued.Enqueue(PrototypeContent.Skill(id));
                        // Start-to-start cadence on a fixed schedule, so tick size does not drift it.
                        // If a skill ran past its slot, restart the cadence from now.
                        _nextSkillTime += _strategy.Interval;
                        if (_nextSkillTime < Time) _nextSkillTime = Time + _strategy.Interval;
                        StartSkill(_queued.Dequeue());
                    }
                    else
                    {
                        Chase(dt);
                        GuardianAutoAttack();
                    }
                    break;

                case GuardianPhase.Tell:
                    UpdateTell();
                    break;

                case GuardianPhase.Recovery:
                    if (Time >= _phaseEnd)
                    {
                        if (_queued.Count > 0)
                        {
                            Phase = GuardianPhase.ComboGap;
                            _phaseEnd = Time + GuardianStrategy.ComboGap;
                        }
                        else
                        {
                            Phase = GuardianPhase.Idle;
                            CurrentSkill = null;
                        }
                    }
                    break;

                case GuardianPhase.ComboGap:
                    if (Time >= _phaseEnd) StartSkill(_queued.Dequeue());
                    break;

                case GuardianPhase.Incapacitated:
                    if (Time >= _phaseEnd)
                    {
                        Phase = GuardianPhase.Idle;
                        ActiveStatus = null;
                    }
                    break;
            }
        }

        void Chase(float dt)
        {
            var toPlayer = PlayerPosition - GuardianPosition;
            if (toPlayer.LengthSquared > 1e-8f) GuardianFacing = toPlayer.Normalized();
            if (DistanceToGuardian <= GuardianRules.ChaseStopDistance) return;
            var target = PlayerPosition - GuardianFacing * (GuardianRules.ChaseStopDistance + Guardian.BodyRadius);
            GuardianPosition = Vec2.MoveTowards(GuardianPosition, target, GuardianRules.MoveSpeed * dt);
        }

        void GuardianAutoAttack()
        {
            if (_guardianAutoTimer > 0f || DistanceToGuardian > GuardianRules.MeleeRange) return;
            DamagePlayer(GuardianRules.AutoBaseDamage * (IsFuryActive ? _furyMultiplier : 1f), "guardian_auto", DamageType.Physical);
            _guardianAutoTimer = GuardianRules.AutoSwing;
        }

        void StartSkill(GuardianSkillDef skill)
        {
            Phase = GuardianPhase.Tell;
            CurrentSkill = skill;
            var toPlayer = PlayerPosition - GuardianPosition;
            if (toPlayer.LengthSquared > 1e-8f) GuardianFacing = toPlayer.Normalized();
            Emit(CombatEventType.TellStarted, skill.Id, TellFor(skill));

            _pulsesLeft = 0;
            // Areas centred on the Guardian reach the same distance past its body, whatever its size (Combat Spec 4.8).
            var body = Guardian.BodyRadius;
            switch (skill.Shape)
            {
                case AreaShape.Cone:
                    Place(skill, new ConeArea(GuardianPosition, GuardianFacing, skill.AngleDegrees, skill.Range + body), Time);
                    break;
                case AreaShape.Circle:
                    Place(skill, new CircleArea(GuardianPosition, skill.Range + body), Time);
                    break;
                case AreaShape.Ring:
                    Place(skill, new RingArea(GuardianPosition, skill.InnerRadius + body, skill.Range + body), Time);
                    break;
                case AreaShape.Line:
                    Place(skill, new LineArea(GuardianPosition, GuardianFacing, skill.Range + body, skill.Width), Time);
                    break;
                case AreaShape.CirclesOnPlayer:
                    Place(skill, new CircleArea(PlayerPosition, skill.Range), Time);
                    _pulsesLeft = skill.Pulses - 1;
                    _nextPulseTime = Time + skill.PulseInterval;
                    break;
                case AreaShape.None:
                    Place(skill, null, Time);
                    break;
            }
        }

        void Place(GuardianSkillDef skill, Area area, float placedAt)
        {
            var tell = TellFor(skill);
            _telegraphs.Add(new Telegraph(skill, area, placedAt + tell, tell));
            if (area != null) Emit(CombatEventType.TelegraphPlaced, skill.Id, tell);
        }

        void UpdateTell()
        {
            var skill = CurrentSkill;

            while (_pulsesLeft > 0 && Time >= _nextPulseTime)
            {
                Place(skill, new CircleArea(PlayerPosition, skill.Range), _nextPulseTime);
                _pulsesLeft--;
                _nextPulseTime += skill.PulseInterval;
            }

            for (var i = 0; i < _telegraphs.Count;)
            {
                var t = _telegraphs[i];
                if (t.HitTime > Time) { i++; continue; }
                _telegraphs.RemoveAt(i);
                Resolve(t);
                // A hit can kill the player, or a trait can interrupt the tell with a status.
                if (PlayerHp <= 0f || Phase != GuardianPhase.Tell) return;
            }

            if (_telegraphs.Count == 0 && _pulsesLeft == 0)
            {
                Phase = GuardianPhase.Recovery;
                _phaseEnd = Time + skill.Recovery;
                Emit(CombatEventType.RecoveryStarted, skill.Id, skill.Recovery);
            }
        }

        void Resolve(Telegraph t)
        {
            var skill = t.Skill;
            if (t.Area == null)
            {
                _furyEnd = Time + skill.BuffDuration;
                _furyMultiplier = skill.BuffAutoMultiplier;
                Emit(CombatEventType.BuffStarted, skill.Id, skill.BuffDuration);
                return;
            }

            if (t.Area.Contains(PlayerPosition))
            {
                Emit(CombatEventType.SkillHit, skill.Id, 0f);
                DamagePlayer(skill.BaseDamage, skill.Id, skill.DamageType);
            }
            else
            {
                Emit(CombatEventType.SkillMissed, skill.Id, 0f);
            }

            if (t.Area is LineArea line) GuardianPosition = ClampToArena(line.End);
        }

        // ---------------- Shared ----------------

        static Vec2 ClampToArena(Vec2 p)
        {
            var r = GuardianRules.ArenaRadius;
            return p.LengthSquared <= r * r ? p : p.Normalized() * r;
        }

        bool CheckEnd()
        {
            if (GuardianHp <= 0f)
            {
                Outcome = CombatOutcome.Win;
                Emit(CombatEventType.Win, null, Time);
                return true;
            }
            if (PlayerHp <= 0f)
            {
                Outcome = CombatOutcome.Loss;
                Emit(CombatEventType.Loss, null, Time);
                return true;
            }
            return false;
        }

        void Emit(CombatEventType type, string id, float amount) => _events.Add(new CombatEvent(Time, type, id, amount));
    }
}
