using System.Collections.Generic;
using TheArtifacts.Core.Combat;
using UnityEngine;

namespace TheArtifacts.PC.Combat
{
    /// <summary>
    /// Procedural motion for a Guardian blockout until rigged models arrive. The model has no skeleton; its moving
    /// parts (Head, ArmL, ArmR, LegFL, LegFR, LegBL, LegBR, LegL, LegR, Tail; see tools/blender/guardian_blockouts.py)
    /// turn at their joints, and the whole body bobs, leans, and lunges. Every pose follows the fight, so the motion
    /// also reads as a tell: it winds up during a skill's tell, strikes when the skill lands, slumps during the
    /// opening, and trembles under Fear.
    /// </summary>
    public sealed class GuardianMotion
    {
        /// <summary>How one Guardian moves (degrees and metres).</summary>
        sealed class Style
        {
            public float Breath = 0.03f, WalkBob = 0.06f, Stride = 2.2f, LegSwing = 22f, ArmSwing = 8f;
            public float WindupLean = -10f, WindupDrop = 0.1f, StrikeLean = 16f, StrikeReach = 0.5f;
            public float ArmRaiseR = -40f, ArmRaiseL = -40f, ArmSlam = 30f, HeadWindup = -12f;
            public bool Trot;  // four legs: diagonal pairs move together
        }

        static Style StyleFor(string guardianId) => guardianId switch
        {
            // The lich glides: no legs; it lifts the sceptre and its casting hand.
            "alma_mater" => new Style { WalkBob = 0.03f, ArmRaiseR = -35f, ArmRaiseL = -70f, ArmSlam = 22f, WindupDrop = -0.1f },
            // The lion trots, crouches before it springs, and lashes its tail.
            "columbia_lion" => new Style { LegSwing = 26f, Trot = true, WindupLean = 6f, WindupDrop = 0.25f, StrikeLean = 10f, StrikeReach = 0.9f, HeadWindup = -18f },
            // The golem stomps and raises both fists over its head before it slams.
            "book_keeper" => new Style { WalkBob = 0.1f, Stride = 1.6f, LegSwing = 18f, ArmSwing = 14f, ArmRaiseR = -120f, ArmRaiseL = -120f, ArmSlam = 45f },
            _ => new Style(),
        };

        readonly Transform _root, _frame;
        readonly Vector3 _rootPosition;
        readonly Quaternion _rootRotation;
        readonly Dictionary<string, (Transform T, Quaternion RestRotation)> _parts = new Dictionary<string, (Transform, Quaternion)>();
        readonly Style _style;

        float _time, _walkPhase, _moving, _windup, _slump, _tremble, _strike, _flinch, _death;
        Vec2? _lastPosition;

        /// <param name="model">The model's root, a child of <paramref name="frame"/>.</param>
        /// <param name="frame">The Guardian's transform: +Z is its facing, +Y up.</param>
        public GuardianMotion(Transform model, Transform frame, string guardianId)
        {
            _root = model;
            _frame = frame;
            _rootPosition = model.localPosition;
            _rootRotation = model.localRotation;
            _style = StyleFor(guardianId);
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                // Blender adds ".001" to names it has already used in another model.
                var name = t.name.Split('.')[0];
                if (t != model && !_parts.ContainsKey(name)) _parts[name] = (t, t.localRotation);
            }
        }

        /// <summary>A skill landed or an auto-attack swung: lunge and slam (weight 0–1).</summary>
        public void Strike(float weight = 1f) => _strike = Mathf.Max(_strike, weight);

        /// <summary>The player hit it: a short jolt back.</summary>
        public void Flinch() => _flinch = 1f;

        public void Reset()
        {
            _time = _walkPhase = _moving = _windup = _slump = _tremble = _strike = _flinch = _death = 0f;
            _lastPosition = null;
        }

        public void Update(CombatSim sim, float dt)
        {
            if (dt <= 0f) return;
            _time += dt;
            var s = _style;

            // Speed from the simulation, so a charge or a chase both move the legs.
            var speed = _lastPosition is Vec2 last ? Vec2.Distance(last, sim.GuardianPosition) / dt : 0f;
            _lastPosition = sim.GuardianPosition;
            var ongoing = sim.Outcome == CombatOutcome.Ongoing;
            _moving = Damp(_moving, ongoing && speed > 0.3f ? 1f : 0f, 8f, dt);
            _walkPhase += dt * Mathf.Min(speed, 12f) * s.Stride;

            var windup = 0f;
            if (ongoing && sim.Phase == GuardianPhase.Tell && sim.CurrentSkill != null && sim.ActiveTelegraphs.Count > 0)
            {
                var left = float.MaxValue;
                foreach (var t in sim.ActiveTelegraphs) left = Mathf.Min(left, t.HitTime - sim.Time);
                windup = 1f - Mathf.Clamp01(left / sim.TellFor(sim.CurrentSkill));
            }
            _windup = Damp(_windup, windup, 12f, dt);
            _slump = Damp(_slump, ongoing && sim.Phase == GuardianPhase.Recovery ? 1f : 0f, 6f, dt);
            _tremble = Damp(_tremble, ongoing && sim.Phase == GuardianPhase.Incapacitated ? 1f : 0f, 8f, dt);
            _strike = Mathf.Max(0f, _strike - dt / 0.4f);
            _flinch = Mathf.Max(0f, _flinch - dt / 0.25f);
            _death = sim.Outcome == CombatOutcome.Win ? Mathf.Min(1f, _death + dt / 1.6f) : 0f;

            var fury = sim.IsFuryActive ? 1.8f : 1f;
            var breath = Mathf.Sin(_time * Mathf.PI * 2f * 0.35f * fury);
            var step = Mathf.Sin(_walkPhase);
            var strike = Mathf.Sin(_strike * Mathf.PI * 0.5f);  // snaps out, eases back
            var shake = _tremble * (Mathf.PerlinNoise(_time * 18f, 0.3f) - 0.5f) * 8f;
            var death = Smooth(_death);

            // Whole body: bob, lean into the walk, wind up, lunge, slump, flinch, and sink when beaten.
            var rise = breath * s.Breath + Mathf.Abs(step) * s.WalkBob * _moving - _windup * s.WindupDrop - death * 0.9f;
            var lean = 5f * _moving + s.WindupLean * _windup + s.StrikeLean * strike + 8f * _slump - 5f * _flinch
                       - 8f * _tremble + 28f * death;
            var roll = step * 3f * _moving + shake + 12f * death;
            var metres = 1f / Mathf.Max(0.01f, _frame.localScale.y);  // the model sits under the scaled capsule
            _root.localPosition = _rootPosition + new Vector3(0f, rise, s.StrikeReach * strike) * metres;
            _root.localRotation = Quaternion.AngleAxis(lean, Vector3.right) * Quaternion.AngleAxis(roll, Vector3.forward) * _rootRotation;

            // Head: looks up as it winds up, nods into the strike, droops in the opening, shakes in fear.
            Turn("Head", s.HeadWindup * _windup + 14f * strike + 12f * _slump + breath * 2f + 10f * death, shake * 1.5f);

            // Arms: raised in the windup, slammed down on the strike, swinging against the legs while walking;
            // under Fear the golem shields its face.
            var swing = step * s.ArmSwing * _moving;
            var cower = _tremble * -60f;
            Turn("ArmR", s.ArmRaiseR * _windup + s.ArmSlam * strike + swing + cower + 6f * _slump);
            Turn("ArmL", s.ArmRaiseL * _windup + s.ArmSlam * strike - swing + cower + 6f * _slump);

            // Legs: two-legged walkers alternate; the lion trots in diagonal pairs.
            var legs = s.LegSwing * _moving;
            Turn("LegL", step * legs);
            Turn("LegR", -step * legs);
            Turn("LegFL", step * legs);
            Turn("LegBR", (s.Trot ? step : -step) * legs);
            Turn("LegFR", -step * legs);
            Turn("LegBL", (s.Trot ? -step : step) * legs);

            // The lion's tail swishes, and lashes while it winds up.
            Turn("Tail", -15f * _windup + 10f * death, Mathf.Sin(_time * 2.1f) * 14f + Mathf.Sin(_time * 9f) * 22f * _windup);
        }

        /// <summary>Turns a part at its joint: pitch about the Guardian's right axis (positive tips forward), yaw about up.</summary>
        void Turn(string part, float pitch, float yaw = 0f)
        {
            if (!_parts.TryGetValue(part, out var p)) return;
            var parent = p.T.parent;
            var right = parent.InverseTransformDirection(_frame.right);
            var up = parent.InverseTransformDirection(_frame.up);
            p.T.localRotation = Quaternion.AngleAxis(pitch, right) * Quaternion.AngleAxis(yaw, up) * p.RestRotation;
        }

        static float Damp(float current, float target, float rate, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
