using System.Collections.Generic;
using TheArtifacts.Core.Combat;
using UnityEngine;

namespace TheArtifacts.PC.Combat
{
    /// <summary>
    /// Procedural animation for the rigged Guardian models (bones from tools/blender/rig_guardians.py), driven by the
    /// fight: every pose is also a tell. It winds up during a skill's tell, strikes when the skill lands, slumps in the
    /// opening, trembles under Fear, flinches when hit, and falls when beaten.
    /// <list type="bullet">
    /// <item>Alma Mater hovers upright above the floor like a ghost: she moves like a person from the waist up (breathing,
    /// looking about, raising the sceptre and casting with her left hand), and her robe trails as she glides.</item>
    /// <item>The Columbia Lion moves like a big cat: a four-beat walk that lengthens as it speeds up, a supple spine, a low
    /// stalking head, a swaying tail, a crouch before it pounces, and a roar.</item>
    /// <item>The Book Keeper moves like a heavy humanoid machine: stiff, slightly late, stomping steps, a head that
    /// turns in steps and holds, and both fists raised before it slams.</item>
    /// </list>
    /// Bones are turned about the Guardian's own axes (pitch about its right, positive tips forward; yaw about up;
    /// roll about forward), so bone orientation in the FBX does not matter. Missing bones are skipped.
    /// </summary>
    public sealed class GuardianMotion
    {
        enum Body { Lich, Lion, Golem, Other }

        readonly Transform _root, _frame;
        readonly Vector3 _rootPosition;
        readonly Quaternion _rootRotation;
        readonly Dictionary<string, (Transform T, Quaternion RestRotation)> _bones = new Dictionary<string, (Transform, Quaternion)>();
        readonly Body _body;
        /// <summary>How fast bones follow their target pose (1/s): the golem is slow, the lion quick.</summary>
        readonly float _follow;
        /// <summary>Radians of gait cycle per metre travelled.</summary>
        readonly float _stride;

        float _time, _walkPhase, _speed, _moving, _windup, _slump, _tremble, _strike, _flinch, _death, _dt;
        Vec2? _lastPosition;
        /// <summary>The Guardian's velocity in its own frame (x right, z forward), m/s.</summary>
        Vector3 _localVelocity;
        /// <summary>The lich's robe joints, top to bottom: each lags the one above it on a loose spring (x roll, y pitch).</summary>
        readonly Vector2[] _drag = new Vector2[3], _dragRate = new Vector2[3];

        /// <param name="model">The model's root, a child of <paramref name="frame"/>.</param>
        /// <param name="frame">The Guardian's transform: +Z is its facing, +Y up.</param>
        public GuardianMotion(Transform model, Transform frame, string guardianId)
        {
            _root = model;
            _frame = frame;
            _rootPosition = model.localPosition;
            _rootRotation = model.localRotation;
            (_body, _follow, _stride) = guardianId switch
            {
                "alma_mater" => (Body.Lich, 12f, 1.2f),
                "columbia_lion" => (Body.Lion, 22f, 1.5f),
                "book_keeper" => (Body.Golem, 7f, 1.7f),
                _ => (Body.Other, 10f, 2f),
            };
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t != model && !_bones.ContainsKey(t.name)) _bones[t.name] = (t, t.localRotation);
        }

        /// <summary>A skill landed or an auto-attack swung: lunge and slam (weight 0–1).</summary>
        public void Strike(float weight = 1f) => _strike = Mathf.Max(_strike, weight);

        /// <summary>The player hit it: a short jolt back.</summary>
        public void Flinch() => _flinch = 1f;

        public void Reset()
        {
            _time = _walkPhase = _speed = _moving = _windup = _slump = _tremble = _strike = _flinch = _death = 0f;
            _lastPosition = null;
            _localVelocity = Vector3.zero;
            for (var i = 0; i < _drag.Length; i++) _drag[i] = _dragRate[i] = Vector2.zero;
            foreach (var b in _bones.Values) b.T.localRotation = b.RestRotation;
        }

        public void Update(CombatSim sim, float dt)
        {
            if (dt <= 0f) return;
            _dt = dt;
            _time += dt;

            // Speed from the simulation, so a charge or a chase both move the legs.
            var speed = _lastPosition is Vec2 last ? Vec2.Distance(last, sim.GuardianPosition) / dt : 0f;
            if (_lastPosition is Vec2 from)
            {
                var v = (sim.GuardianPosition - from) * (1f / dt);
                _localVelocity = Vector3.Lerp(_localVelocity, _frame.InverseTransformDirection(new Vector3(v.X, 0f, v.Y)),
                    1f - Mathf.Exp(-8f * dt));
            }
            _lastPosition = sim.GuardianPosition;
            _speed = Damp(_speed, speed, 6f, dt);
            var ongoing = sim.Outcome == CombatOutcome.Ongoing;
            _moving = Damp(_moving, ongoing && speed > 0.3f ? 1f : 0f, 6f, dt);
            _walkPhase += dt * Mathf.Min(speed, 14f) * _stride;

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
            _strike = Mathf.Max(0f, _strike - dt / 0.6f);
            _flinch = Mathf.Max(0f, _flinch - dt / 0.25f);
            _death = sim.Outcome == CombatOutcome.Win ? Mathf.Min(1f, _death + dt / 1.6f) : 0f;

            var p = new Pose
            {
                Fury = sim.IsFuryActive ? 1.8f : 1f,
                Strike = Mathf.Sin(_strike * Mathf.PI * 0.5f), // snaps out, eases back
                Shake = _tremble * (Mathf.PerlinNoise(_time * 18f, 0.3f) - 0.5f) * 8f,
                Death = Smooth(_death),
            };
            p.Breath = Mathf.Sin(_time * Mathf.PI * 2f * 0.3f * p.Fury);

            switch (_body)
            {
                case Body.Lich: Lich(p); break;
                case Body.Lion: Lion(p); break;
                case Body.Golem: Golem(p); break;
                default: Root(p, Mathf.Abs(Mathf.Sin(_walkPhase)) * 0.06f * _moving, 0f, 0f); break;
            }
        }

        struct Pose
        {
            public float Fury, Strike, Shake, Death, Breath;
        }

        /// <summary>
        /// The ghost scholar: hovers upright and still below the waist, moves like a person above it (breathing,
        /// looking about, raising the sceptre and casting with her left hand); her robe trails as she glides.
        /// </summary>
        void Lich(Pose p)
        {
            float w = _windup, s = p.Strike, sl = _slump, tr = _tremble, d = p.Death, m = _moving, t = _time;
            // Upright: she rises and sinks but does not lean or roll as she flies.
            var hover = 0.5f + 0.06f * Mathf.Sin(t * 1.2f) - 0.15f * sl - 0.5f * d + 0.2f * w;
            Root(p, hover, 6f * s + 6f * sl, 0f);

            var idle = (1f - w) * (1f - s) * (1f - tr);
            var look = (Mathf.PerlinNoise(t * 0.35f, 1.7f) - 0.5f) * 70f * (1f - w);
            var conjure = Mathf.Clamp01(Mathf.PerlinNoise(t * 0.4f, 6.3f) * 2.2f - 0.9f) * idle;
            var drift = Mathf.Sin(t * 1.1f * p.Fury);
            Turn("Hips", 4f * sl);
            // The upper body bends through three joints, so it arches and twists smoothly.
            Spread(new[] { "Spine", "Spine2", "Chest" }, p.Breath * 6f - 30f * w + 45f * s + 20f * sl - 25f * _flinch + 25f * d,
                30f * w - 20f * s + look * 0.35f, p.Shake);
            Turn("Neck", 5f * sl - 6f * w + 6f * s, look * 0.3f);
            Turn("Head", -14f * w + 12f * s + 15f * sl + 20f * d, look * 0.35f + p.Shake * 1.5f, 8f * Mathf.Sin(t * 0.45f));

            // The sceptre arm raises it high in the tell and drives it down when the spell lands.
            Turn("Shoulder_R", -8f * w + 8f * s);
            Turn("UpperArm_R", -60f * w * (1f - s) + 30f * s + 10f * drift * idle + 8f * sl - 30f * tr, 0f, -10f * w);
            Turn("LowerArm_R", -40f * w + 15f * s - 8f * drift * idle - 25f * tr);
            Turn("Hand_R", 10f * drift * idle - 15f * w + 20f * s);
            // The casting hand: conjures while idle, reaches out during the tell, thrusts when it lands, shields under Fear.
            var cast = Mathf.Max(w, conjure * 0.6f);
            Turn("Shoulder_L", -10f * cast - 12f * s);
            Turn("UpperArm_L", -60f * cast * (1f - s) - 80f * s - 55f * tr + 6f * sl, 20f * conjure * Mathf.Sin(t * 1.3f));
            Turn("LowerArm_L", -45f * cast - 25f * s - 60f * tr + 8f * drift * idle);
            Turn("Hand_L", -35f * cast + 12f * Mathf.Sin(t * 10f) * cast);

            // The robe swings like cloth through three joints: it trails when she glides or lunges, and sways gently.
            // Her legs under the robe drag behind as she glides: the robe trails against her motion, each joint a
            // little later than the one above, and swings past and settles when she stops or turns.
            var lag = Mathf.Min(_dt, 0.05f);
            var pull = new Vector2(-_localVelocity.x * 3f, _localVelocity.z * 3.5f);
            pull = Vector2.ClampMagnitude(pull, 10f); // per joint: about 30° at the hem at full speed
            for (var i = 0; i < _drag.Length; i++)
            {
                var target = i == 0 ? pull : _drag[i - 1];
                _dragRate[i] += (40f * (target - _drag[i]) - 5f * _dragRate[i]) * lag;
                _drag[i] += _dragRate[i] * lag;
                Turn($"Robe{i + 1}", _drag[i].y + 6f * s - 3f * w + 2f * Mathf.Sin(t * 1.3f - i * 0.6f), 0f,
                    _drag[i].x + 2f * Mathf.Sin(t * 1.0f - i * 0.6f), 20f);
            }
        }

        /// <summary>
        /// The big cat: a four-beat walk (hind, fore, hind, fore; it lengthens its stride as it speeds up), shoulder
        /// blades that roll with the front legs, a low stalking head, a tail that sways and lashes, a crouch before the
        /// pounce, a roar.
        /// </summary>
        void Lion(Pose p)
        {
            float w = _windup, s = p.Strike, sl = _slump, tr = _tremble, d = p.Death, m = _moving, t = _time;
            var a = _walkPhase;
            var run = Mathf.Clamp01((_speed - 4f) / 3f);
            var bob = Mathf.Abs(Mathf.Sin(a * 2f)) * (0.1f + 0.08f * run) * m;
            // Standing, it shifts its weight from paw to paw and breathes hard.
            var shift = Mathf.Sin(t * 0.9f) * (1f - m) * (1f - w);
            Root(p, bob - 0.6f * w + 0.4f * s - 0.25f * sl - 0.2f * tr + 0.04f * p.Breath,
                8f * w + 12f * s + 5f * sl, 3f * shift);

            // Every leg keeps its own beat (hind L, fore L, hind R, fore R), so the two front legs always alternate.
            var reach = Mathf.Lerp(32f, 48f, run) * m;
            void Leg(string blade, string upper, string lower, string paw, string toe, float offset, bool fore, float side)
            {
                var phase = a + offset * Mathf.PI * 2f;
                var lift = Mathf.Max(0f, Mathf.Cos(phase)) * m;
                var swing = -reach * Mathf.Sin(phase);
                var stand = 6f * shift * side;
                if (fore)
                {
                    Turn(blade, 0.35f * swing + 8f * w - 20f * s);
                    Turn(upper, 0.65f * swing + 15f * w - 60f * s + 12f * sl + 25f * d + 12f * tr + stand);
                    Turn(lower, 65f * lift - 20f * w - 25f * s + 30f * d);
                    Turn(paw, 40f * lift + 15f * w - 30f * s);
                    Turn(toe, 30f * lift - 30f * s + 10f * w); // the claws spread as it pounces
                }
                else
                {
                    Turn(upper, swing - 30f * w + 40f * s - 22f * sl - 30f * d - 12f * tr - stand);
                    Turn(lower, -45f * lift - 30f * w + 30f * s - 25f * sl - 30f * d);
                    Turn(paw, 45f * lift + 40f * w - 20f * s + 20f * sl + 30f * d);
                    Turn(toe, 25f * lift - 20f * w + 15f * s);
                }
            }
            Leg(null, "RearUpper_L", "RearLower_L", "RearFoot_L", "RearToe_L", 0f, false, 1f);
            Leg("Scapula_L", "FrontUpper_L", "FrontLower_L", "FrontPaw_L", "FrontToe_L", 0.25f, true, 1f);
            Leg(null, "RearUpper_R", "RearLower_R", "RearFoot_R", "RearToe_R", 0.5f, false, -1f);
            Leg("Scapula_R", "FrontUpper_R", "FrontLower_R", "FrontPaw_R", "FrontToe_R", 0.75f, true, -1f);

            // A supple spine: it bends side to side through three joints with the walk.
            var sway = Mathf.Sin(a) * m;
            Turn("Hips", 0f, 9f * sway, 5f * sway + 3f * shift);
            Spread(new[] { "Spine", "Spine2", "Chest" }, p.Breath * 4.5f - 8f * w + 4f * s + 8f * run * Mathf.Sin(a * 2f) * m,
                -18f * sway, p.Shake - 3f * shift);

            // The head: low and level while it stalks, scanning when it stands; it roars in the tell and bites.
            var look = (Mathf.PerlinNoise(t * 0.45f, 4.2f) - 0.5f) * 80f * (1f - m) * (1f - w);
            var roar = Mathf.Max(s, Mathf.Clamp01((w - 0.4f) * 2f));
            var snarl = Mathf.Clamp01(Mathf.PerlinNoise(t * 0.5f, 8.8f) * 2.5f - 1.3f) * (1f - m) * (1f - w);
            Spread(new[] { "Neck", "Neck2" }, 18f * m + 20f * w - 28f * s + 25f * sl + 20f * tr + 25f * d - 10f * snarl,
                look * 0.6f + 6f * sway);
            Turn("Head", -12f * m - 18f * w - 20f * roar + 10f * sl + p.Breath * 3f - 8f * snarl,
                look * 0.4f + p.Shake * 1.5f, 8f * Mathf.Sin(t * 0.6f) * (1f - m));
            Turn("Jaw", 4f + 3f * Mathf.Sin(t * 0.9f) + 40f * roar + 22f * snarl + 12f * d + 4f * Mathf.Sin(t * 30f) * roar);

            // The tail: a slow S-curve through six joints, flicking now and then, lashing in the tell, tucked under Fear.
            var lash = 1f + 2.5f * w * p.Fury + 1.2f * m;
            var flick = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 0.7f)), 8f);
            for (var i = 1; i <= 6; i++)
            {
                var yaw = (5f + 3.5f * i) * lash * Mathf.Sin(t * (1.8f + 6f * w) - i * 0.55f);
                var pitch = i == 1 ? 20f * w - 40f * tr - 20f * d + 10f * s
                    : 5f * Mathf.Sin(t * 1.3f - i * 0.7f) - 7f * tr - 18f * flick * (i - 1) / 5f;
                Turn($"Tail{i}", pitch, yaw);
            }
        }

        /// <summary>
        /// The stone machine: stiff, a little late (bones follow slowly), stomping steps with a weight shift, a head
        /// that turns in steps and holds, fists that clench as both arms rise overhead before the slam.
        /// </summary>
        void Golem(Pose p)
        {
            float w = _windup, s = p.Strike, sl = _slump, tr = _tremble, d = p.Death, m = _moving, t = _time;
            var a = _walkPhase;
            var step = Mathf.Sin(a) * m;
            var stomp = Mathf.Pow(1f - Mathf.Abs(Mathf.Cos(a)), 4f) * m; // a jolt as each foot lands
            // Standing, it sways its weight from leg to leg like a machine idling.
            var idle = Mathf.Sin(t * 0.8f) * (1f - m) * (1f - w);
            Root(p, 0.15f * Mathf.Abs(Mathf.Sin(a)) * m - 0.12f * stomp - 0.25f * w - 0.4f * s - 0.15f * sl - 1.0f * d,
                6f * m - 14f * w + 25f * s + 10f * sl + 30f * d, 2.5f * step + 2f * idle);

            // Legs: alternate, knees fold only while a foot is in the air, the toe points as it lifts; a wide braced
            // stance in the tell; kneel when it falls.
            void Leg(string upper, string lower, string foot, string toe, float phase, float side)
            {
                var lift = Mathf.Max(0f, Mathf.Cos(phase)) * m;
                // The legs keep up with the gait so the feet plant instead of sliding; the slowness is in the body.
                Turn(upper, -30f * Mathf.Sin(phase) * m - 20f * w + 10f * s - 70f * d, 0f, side * (8f * w + 3f * idle), 16f);
                Turn(lower, 55f * lift + 30f * w + 15f * s + 100f * d, 0f, 0f, 16f);
                Turn(foot, -20f * lift - 10f * w - 30f * d, 0f, 0f, 16f);
                Turn(toe, 20f * lift + 15f * d, 0f, 0f, 16f);
            }
            Leg("UpperLeg_L", "LowerLeg_L", "Foot_L", "Toe_L", a, 1f);
            Leg("UpperLeg_R", "LowerLeg_R", "Foot_R", "Toe_R", a + Mathf.PI, -1f);

            Turn("Hips", 0f, 10f * step, 3f * step + 3f * idle);
            Turn("Spine", p.Breath * 2f - 10f * w + 20f * s + 8f * sl, -10f * step + 15f * w - 10f * s);
            Turn("Chest", -18f * w + 35f * s + 15f * sl - 15f * _flinch + 25f * d, -6f * step, p.Shake - 3f * idle);

            // The head scans in steps and holds, like a machine (neck first, then head); it looks up at its fists in the tell.
            var scan = Mathf.Round((Mathf.PerlinNoise(t * 0.3f, 9.1f) - 0.5f) * 4f) * 20f * (1f - w);
            Turn("Neck", -8f * w + 6f * s + 8f * sl, scan * 0.6f);
            Turn("Head", -14f * w + 10f * s + 10f * sl + 15f * d, scan * 0.4f + p.Shake * 1.5f);

            // Arms: a heavy swing against the legs with a set elbow; overhead in the tell with fists clenched; the slam;
            // a guard under Fear.
            void Arm(string shoulder, string upper, string lower, string hand, string fingers, float swing, float side)
            {
                Turn(shoulder, 5f * swing - 15f * w + 10f * s - 15f * tr);
                Turn(upper, swing * 24f - 135f * w * (1f - s) + 35f * s - 65f * tr + 6f * sl + 10f * d - 4f * idle * side,
                    0f, side * 6f * Mathf.Abs(swing));
                Turn(lower, -20f - 40f * w - 15f * s - 90f * tr + 10f * sl - 10f * Mathf.Abs(swing));
                Turn(hand, -15f * w + 20f * s);
                Turn(fingers, 20f + 40f * Mathf.Max(w, s) + 30f * tr);
            }
            Arm("Shoulder_L", "UpperArm_L", "LowerArm_L", "Hand_L", "Fingers_L", step, 1f);
            Arm("Shoulder_R", "UpperArm_R", "LowerArm_R", "Hand_R", "Fingers_R", -step, -1f);
        }

        /// <summary>The whole body: rises (metres), leans (degrees, forward positive), rolls, lunges on a strike.</summary>
        void Root(Pose p, float rise, float lean, float roll)
        {
            var reach = _body == Body.Lion ? 1.4f : 0.8f;
            var metres = 1f / Mathf.Max(0.01f, _frame.localScale.y); // the model sits under the scaled capsule
            _root.localPosition = _rootPosition + new Vector3(0f, rise, reach * p.Strike - 0.2f * _flinch) * metres;
            var bodyLean = (_body == Body.Lion ? 0.3f : 0.7f) * lean + 28f * p.Death * (_body == Body.Golem ? 0.3f : 1f) - 5f * _flinch;
            _root.localRotation = Quaternion.AngleAxis(bodyLean, Vector3.right)
                                  * Quaternion.AngleAxis(roll + p.Shake + 10f * p.Death, Vector3.forward) * _rootRotation;
        }

        /// <summary>Splits a bend evenly over a chain of bones, so it curves instead of kinking at one joint.</summary>
        void Spread(string[] bones, float pitch, float yaw = 0f, float roll = 0f)
        {
            var n = bones.Length;
            foreach (var b in bones) Turn(b, pitch / n, yaw / n, roll / n);
        }

        /// <summary>Eases a bone toward a pose: pitch about the Guardian's right (positive tips forward), yaw about up, roll about forward.</summary>
        void Turn(string bone, float pitch, float yaw = 0f, float roll = 0f, float follow = 0f)
        {
            if (bone == null || !_bones.TryGetValue(bone, out var b)) return;
            var parent = b.T.parent;
            var right = parent.InverseTransformDirection(_frame.right);
            var up = parent.InverseTransformDirection(_frame.up);
            var forward = parent.InverseTransformDirection(_frame.forward);
            var target = Quaternion.AngleAxis(pitch, right) * Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(roll, forward) * b.RestRotation;
            b.T.localRotation = Quaternion.Slerp(b.T.localRotation, target, 1f - Mathf.Exp(-(follow > 0f ? follow : _follow) * _dt));
        }

        static float Damp(float current, float target, float rate, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
