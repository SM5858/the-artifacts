using System;
using System.Collections.Generic;
using System.Linq;
using TheArtifacts.Core.Guardian;

namespace TheArtifacts.Core.Combat
{
    public enum Ability { None, HeavyStrike, Guard, Lunge, Punish, LightningBolt }

    /// <summary>Equipment-skill keys, laid out like the body (Combat Spec 3.2).</summary>
    public enum EquipmentKey { None, Q, W, E, A, S, D, X, C }

    /// <summary>One tick of player input. Move is a direction on the floor; length above 1 is clamped.</summary>
    public readonly struct PlayerInput
    {
        public readonly Vec2 Move;
        public readonly Ability Ability;
        public readonly EquipmentKey Equipment;

        public PlayerInput(Vec2 move, Ability ability = Ability.None, EquipmentKey equipment = EquipmentKey.None)
        {
            Move = move;
            Ability = ability;
            Equipment = equipment;
        }

        public static readonly PlayerInput None = new PlayerInput(Vec2.Zero);
    }

    public enum GuardianPhase
    {
        /// <summary>Chasing and auto-attacking.</summary>
        Idle,
        /// <summary>Telegraph shown; the player can react.</summary>
        Tell,
        /// <summary>Opening after a skill; Punish deals double.</summary>
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

        public Telegraph(GuardianSkillDef skill, Area area, float hitTime)
        {
            Skill = skill;
            Area = area;
            HitTime = hitTime;
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

        public IReadOnlyList<Telegraph> ActiveTelegraphs => _telegraphs;
        public IReadOnlyList<CombatEvent> Events => _events;

        // ---- Private state ----
        readonly Dictionary<Ability, float> _cooldowns = new Dictionary<Ability, float>
        {
            { Ability.HeavyStrike, 0f }, { Ability.Guard, 0f }, { Ability.Lunge, 0f }, { Ability.Punish, 0f }, { Ability.LightningBolt, 0f },
        };
        float _playerAutoTimer;
        readonly Dictionary<string, float> _equipmentCooldowns = new Dictionary<string, float>();
        readonly List<(float Multiplier, float End)> _damageTakenBuffs = new List<(float, float)>();
        readonly HashSet<TraitPart> _usedOncePerFight = new HashSet<TraitPart>();

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
        }

        public float DistanceToGuardian => Vec2.Distance(PlayerPosition, GuardianPosition);
        public bool InMeleeRange => DistanceToGuardian <= PlayerRules.MeleeRange;
        public float CooldownRemaining(Ability ability) => _cooldowns.TryGetValue(ability, out var t) ? t : 0f;

        /// <summary>Advances the fight by <paramref name="dt"/> seconds with this tick's input.</summary>
        public void Step(float dt, PlayerInput input)
        {
            if (Outcome != CombatOutcome.Ongoing) return;
            if (dt <= 0f) throw new ArgumentOutOfRangeException(nameof(dt));

            Time += dt;
            TickPlayerTimers(dt);
            MovePlayer(input.Move, dt);
            if (input.Ability != Ability.None) TryUse(input.Ability);
            if (input.Equipment != EquipmentKey.None) TryUseEquipment(input.Equipment);
            PlayerAutoAttack();
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

        public float EquipmentCooldownRemaining(EquipmentKey key)
        {
            var artifact = Build.EquippedAt(key);
            return artifact != null && _equipmentCooldowns.TryGetValue(artifact.Id, out var t) ? t : 0f;
        }

        /// <summary>Why this key's equipment skill cannot be cast now ("none" if the slot has no skill), or null.</summary>
        public string EquipmentBlockReason(EquipmentKey key)
        {
            if (EquipmentSkill(Build.EquippedAt(key)) == null) return "none";
            if (EquipmentCooldownRemaining(key) > 0f) return "cooldown";
            return null;
        }

        /// <summary>Equipment skills cost no MP and neither need nor trigger the GCD.</summary>
        void TryUseEquipment(EquipmentKey key)
        {
            var reason = EquipmentBlockReason(key);
            if (reason != null)
            {
                Emit(CombatEventType.AbilityRejected, $"{key}:{reason}", 0f);
                return;
            }

            var artifact = Build.EquippedAt(key);
            var part = EquipmentSkill(artifact);
            foreach (var effect in part.Effects) ApplyTraitEffect(artifact, effect, 0f);
            _equipmentCooldowns[artifact.Id] = part.Cooldown * (1f - Build.CooldownReduction);
            Emit(CombatEventType.AbilityUsed, artifact.Id, 0f);
        }

        void MovePlayer(Vec2 move, float dt)
        {
            if (move.LengthSquared < 1e-8f) return;
            var dir = move.LengthSquared > 1f ? move.Normalized() : move;
            PlayerPosition = ClampToArena(PlayerPosition + dir * (Build.RunSpeed * dt));
        }

        /// <summary>Why an ability cannot be used right now, or null if it can.</summary>
        public string BlockReason(Ability ability)
        {
            if (CooldownRemaining(ability) > 0f) return "cooldown";
            if (GcdRemaining > 0f) return "gcd";
            if ((ability == Ability.HeavyStrike || ability == Ability.Punish) && !InMeleeRange) return "range";
            if (ability == Ability.LightningBolt)
            {
                if (PlayerMp < PlayerRules.LightningBoltManaCost) return "mana";
                if (DistanceToGuardian > PlayerRules.LightningBoltRange) return "range";
            }
            return null;
        }

        void TryUse(Ability ability)
        {
            var reason = BlockReason(ability);
            if (reason != null)
            {
                Emit(CombatEventType.AbilityRejected, $"{ability}:{reason}", 0f);
                return;
            }

            var inRecovery = Phase == GuardianPhase.Recovery;
            switch (ability)
            {
                case Ability.HeavyStrike:
                    DamageGuardian(PlayerRules.HeavyStrikeCoefficient * Build.Strength, "heavy_strike", DamageType.Physical, canCrit: true);
                    _cooldowns[ability] = Cooldown(ability, PlayerRules.HeavyStrikeCooldown);
                    break;

                case Ability.Guard:
                    GuardRemaining = Build.Passive(TraitEffectKind.SetGuardDuration)?.Value ?? PlayerRules.GuardDuration;
                    _cooldowns[ability] = Cooldown(ability, PlayerRules.GuardCooldown);
                    break;

                case Ability.Lunge:
                {
                    var toGuardian = GuardianPosition - PlayerPosition;
                    var travel = Math.Max(0f, Math.Min(PlayerRules.LungeDistance, toGuardian.Length - 2f));
                    PlayerPosition = ClampToArena(PlayerPosition + toGuardian.Normalized() * travel);
                    if (InMeleeRange) DamageGuardian(PlayerRules.LungeCoefficient * Build.Strength, "lunge", DamageType.Physical, canCrit: true);
                    _cooldowns[ability] = Cooldown(ability, PlayerRules.LungeCooldown);
                    break;
                }

                case Ability.Punish:
                {
                    var mult = inRecovery ? PlayerRules.PunishRecoveryMultiplier : 1f;
                    DamageGuardian(PlayerRules.PunishCoefficient * Build.Strength * mult, inRecovery ? "punish_recovery" : "punish", DamageType.Physical, canCrit: true);
                    _cooldowns[ability] = Cooldown(ability, PlayerRules.PunishCooldown);
                    break;
                }

                case Ability.LightningBolt:
                    PlayerMp -= PlayerRules.LightningBoltManaCost;
                    DamageGuardian(PlayerRules.LightningBoltBaseDamage + PlayerRules.LightningBoltCoefficient * Build.Intelligence, "lightning_bolt", DamageType.Magic, canCrit: true);
                    _cooldowns[ability] = Cooldown(ability, PlayerRules.LightningBoltCooldown);
                    break;
            }

            GcdRemaining = PlayerRules.GlobalCooldown;
            Emit(CombatEventType.AbilityUsed, ability.ToString(), 0f);
            FireTraits(TraitTrigger.AbilityUsed, ability, 0f, inRecovery);
        }

        /// <summary>The ability's cooldown (a trait may replace the base), shortened by Agility.</summary>
        float Cooldown(Ability ability, float baseCooldown) =>
            (Build.Passive(TraitEffectKind.SetCooldown, ability)?.Value ?? baseCooldown) * (1f - Build.CooldownReduction);

        void PlayerAutoAttack()
        {
            if (_playerAutoTimer > 0f || !InMeleeRange) return;
            DamageGuardian(PlayerRules.AutoCoefficient * Build.Strength, "auto", DamageType.Physical, canCrit: true);
            _playerAutoTimer = PlayerRules.AutoSwing;
        }

        /// <summary>
        /// All player damage, reflects included, is scaled by type traits and reduced by the Guardian's Def or Res
        /// (Combat Spec 2). Attacks may crit; the roll is skipped at 0 Precision so fights stay deterministic.
        /// </summary>
        void DamageGuardian(float amount, string source, DamageType type, bool canCrit = false)
        {
            var crit = canCrit && Build.CritChance > 0f && _rng.NextDouble() < Build.CritChance;
            if (crit) amount *= PlayerRules.CritMultiplier;
            var resist = type == DamageType.Physical ? Guardian.Def : Guardian.Res;
            amount *= _dealtVsType * (1f - PlayerRules.Rating(resist));
            GuardianHp = Math.Max(0f, GuardianHp - amount);
            Emit(CombatEventType.GuardianDamaged, source, amount);
            if (crit) Emit(CombatEventType.CriticalHit, source, amount);
        }

        void HealPlayer(float amount, string source)
        {
            var healed = Math.Min(amount, Build.MaxHp - PlayerHp);
            PlayerHp += healed;
            Emit(CombatEventType.PlayerHealed, source, healed);
        }

        /// <summary>Damage taken = base × tier multiplier × (1 − Def or Res rating) × type traits, reduced by Guard (Combat Spec 2).</summary>
        void DamagePlayer(float baseDamage, string source, DamageType type)
        {
            var reduction = type == DamageType.Physical ? Build.PhysicalReduction : Build.MagicReduction;
            var amount = baseDamage * Tier.DamageMultiplier * (1f - reduction) * _takenVsType;
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

            var end = Time + duration;
            _phaseEnd = Phase == GuardianPhase.Incapacitated ? Math.Max(_phaseEnd, end) : end;
            Phase = GuardianPhase.Incapacitated;
            ActiveStatus = status;
            Emit(CombatEventType.StatusApplied, status.ToString(), duration);
        }

        // ---------------- Guardian ----------------

        void UpdateGuardian(float dt)
        {
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
            if (toPlayer.Length <= GuardianRules.ChaseStopDistance) return;
            var target = PlayerPosition - GuardianFacing * GuardianRules.ChaseStopDistance;
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
            Emit(CombatEventType.TellStarted, skill.Id, skill.Tell);

            _pulsesLeft = 0;
            switch (skill.Shape)
            {
                case AreaShape.Cone:
                    Place(skill, new ConeArea(GuardianPosition, GuardianFacing, skill.AngleDegrees, skill.Range), Time);
                    break;
                case AreaShape.Circle:
                    Place(skill, new CircleArea(GuardianPosition, skill.Range), Time);
                    break;
                case AreaShape.Ring:
                    Place(skill, new RingArea(GuardianPosition, skill.InnerRadius, skill.Range), Time);
                    break;
                case AreaShape.Line:
                    Place(skill, new LineArea(GuardianPosition, GuardianFacing, skill.Range, skill.Width), Time);
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
            _telegraphs.Add(new Telegraph(skill, area, placedAt + skill.Tell));
            if (area != null) Emit(CombatEventType.TelegraphPlaced, skill.Id, skill.Tell);
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
