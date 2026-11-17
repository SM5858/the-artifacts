using System;
using System.Collections.Generic;
using System.Linq;
using TheArtifacts.Core.Guardian;

namespace TheArtifacts.Core.Combat
{
    public enum Ability { None, HeavyStrike, Guard, Lunge, Punish }

    /// <summary>One tick of player input. Move is a direction on the floor; length above 1 is clamped.</summary>
    public readonly struct PlayerInput
    {
        public readonly Vec2 Move;
        public readonly Ability Ability;

        public PlayerInput(Vec2 move, Ability ability = Ability.None)
        {
            Move = move;
            Ability = ability;
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

        public IReadOnlyList<Telegraph> ActiveTelegraphs => _telegraphs;
        public IReadOnlyList<CombatEvent> Events => _events;

        // ---- Private state ----
        readonly Dictionary<Ability, float> _cooldowns = new Dictionary<Ability, float>
        {
            { Ability.HeavyStrike, 0f }, { Ability.Guard, 0f }, { Ability.Lunge, 0f }, { Ability.Punish, 0f },
        };
        float _playerAutoTimer;

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

        public CombatSim(PlayerBuild build, GuardianSetup setup, int seed)
        {
            Build = build ?? throw new ArgumentNullException(nameof(build));
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            Tier = PrototypeContent.Tier(setup.StatTier) ?? throw new ArgumentException($"Unknown tier '{setup.StatTier}'.");
            if (setup.SkillIds == null || setup.SkillIds.Count == 0)
                throw new ArgumentException("A setup needs at least one skill.");
            foreach (var id in setup.SkillIds)
                if (PrototypeContent.Skill(id) == null) throw new ArgumentException($"Unknown skill '{id}'.");

            _strategy = GuardianStrategy.Create(setup.StrategyId);
            _rng = new Random(seed);

            PlayerHp = build.MaxHp;
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
        }

        void MovePlayer(Vec2 move, float dt)
        {
            if (move.LengthSquared < 1e-8f) return;
            var dir = move.LengthSquared > 1f ? move.Normalized() : move;
            PlayerPosition = ClampToArena(PlayerPosition + dir * (PlayerRules.RunSpeed * dt));
        }

        /// <summary>Why an ability cannot be used right now, or null if it can.</summary>
        public string BlockReason(Ability ability)
        {
            if (CooldownRemaining(ability) > 0f) return "cooldown";
            if (GcdRemaining > 0f) return "gcd";
            if ((ability == Ability.HeavyStrike || ability == Ability.Punish) && !InMeleeRange) return "range";
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

            switch (ability)
            {
                case Ability.HeavyStrike:
                    DamageGuardian(PlayerRules.HeavyStrikeCoefficient * Build.Power, "heavy_strike");
                    _cooldowns[ability] = Build.Has(ArtifactEffect.HeavyStrikeCooldown45) ? 4.5f : PlayerRules.HeavyStrikeCooldown;
                    break;

                case Ability.Guard:
                    GuardRemaining = Build.Has(ArtifactEffect.GuardLongerAndReflects) ? 3f : PlayerRules.GuardDuration;
                    if (Build.Has(ArtifactEffect.GuardHeals5Percent)) HealPlayer(0.05f * Build.MaxHp, "wardens_plate");
                    _cooldowns[ability] = PlayerRules.GuardCooldown;
                    break;

                case Ability.Lunge:
                {
                    var toGuardian = GuardianPosition - PlayerPosition;
                    var travel = Math.Max(0f, Math.Min(PlayerRules.LungeDistance, toGuardian.Length - 2f));
                    PlayerPosition = ClampToArena(PlayerPosition + toGuardian.Normalized() * travel);
                    if (InMeleeRange) DamageGuardian(PlayerRules.LungeCoefficient * Build.Power, "lunge");
                    _cooldowns[ability] = PlayerRules.LungeCooldown;
                    break;
                }

                case Ability.Punish:
                {
                    var inRecovery = Phase == GuardianPhase.Recovery;
                    var mult = inRecovery ? PlayerRules.PunishRecoveryMultiplier : 1f;
                    DamageGuardian(PlayerRules.PunishCoefficient * Build.Power * mult, inRecovery ? "punish_recovery" : "punish");
                    _cooldowns[ability] = PlayerRules.PunishCooldown;
                    if (inRecovery && Build.Has(ArtifactEffect.PunishInRecoveryResetsLunge)) _cooldowns[Ability.Lunge] = 0f;
                    break;
                }
            }

            GcdRemaining = PlayerRules.GlobalCooldown;
            Emit(CombatEventType.AbilityUsed, ability.ToString(), 0f);
        }

        void PlayerAutoAttack()
        {
            if (_playerAutoTimer > 0f || !InMeleeRange) return;
            DamageGuardian(PlayerRules.AutoCoefficient * Build.Power, "auto");
            _playerAutoTimer = PlayerRules.AutoSwing;
        }

        void DamageGuardian(float amount, string source)
        {
            GuardianHp = Math.Max(0f, GuardianHp - amount);
            Emit(CombatEventType.GuardianDamaged, source, amount);
        }

        void HealPlayer(float amount, string source)
        {
            var healed = Math.Min(amount, Build.MaxHp - PlayerHp);
            PlayerHp += healed;
            Emit(CombatEventType.PlayerHealed, source, healed);
        }

        /// <summary>Damage taken = base × tier multiplier × (1 − Armor), reduced by Guard (Combat Spec 2).</summary>
        void DamagePlayer(float baseDamage, string source)
        {
            var amount = baseDamage * Tier.DamageMultiplier * (1f - Build.Armor);
            if (GuardRemaining > 0f)
            {
                var blocked = amount * PlayerRules.GuardReduction;
                amount -= blocked;
                if (Build.Has(ArtifactEffect.GuardLongerAndReflects)) DamageGuardian(blocked * 0.3f, "stillstone_reflect");
            }
            PlayerHp = Math.Max(0f, PlayerHp - amount);
            Emit(CombatEventType.PlayerDamaged, source, amount);
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
            DamagePlayer(GuardianRules.AutoBaseDamage * (IsFuryActive ? _furyMultiplier : 1f), "guardian_auto");
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
                if (PlayerHp <= 0f) return;
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
                DamagePlayer(skill.BaseDamage, skill.Id);
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
