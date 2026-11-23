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
    /// <item>Alma Mater hovers above the floor like a ghost: she moves like a person from the waist up (breathing,
    /// looking about, raising the sceptre and casting with her left hand), and her robe trails as she glides.</item>
    /// <item>The Columbia Lion moves like a big cat: a four-beat walk (a gallop when it charges), a supple spine, a low
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
                "alma_mater" => (Body.Lich, 9f, 1.2f),
                "columbia_lion" => (Body.Lion, 18f, 1.5f),
                "book_keeper" => (Body.Golem, 5f, 1.7f),
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
            foreach (var b in _bones.Values) b.T.localRotation = b.RestRotation;
        }

        public void Update(CombatSim sim, float dt)
        {
            if (dt <= 0f) return;
            _dt = dt;
            _time += dt;

            // Speed from the simulation, so a charge or a chase both move the legs.
            var speed = _lastPosition is Vec2 last ? Vec2.Distance(last, sim.GuardianPosition) / dt : 0f;
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
            _strike = Mathf.Max(0f, _strike - dt / 0.45f);
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
        /// The ghost scholar: hovers, glides with her robe trailing, raises the sceptre and casts with her left hand.
        /// </summary>
        void Lich(Pose p)
        {
            float w = _windup, s = p.Strike, sl = _slump, tr = _tremble, d = p.Death, m = _moving, t = _time;
            var hover = 0.45f + 0.07f * Mathf.Sin(t * 1.4f) - 0.12f * sl - 0.45f * d + 0.12f * w;
            Root(p, hover, 7f * m - 6f * w + 10f * s + 8f * sl, 2f * Mathf.Sin(t * 0.7f));

            var look = (Mathf.PerlinNoise(t * 0.25f, 1.7f) - 0.5f) * 40f * (1f - w);
            Turn("Hips", 3f * m + 5f * sl, 5f * Mathf.Sin(t * 0.45f), 2f * Mathf.Sin(t * 0.6f));
            Turn("Spine", p.Breath * 1.5f - 6f * w + 10f * s + 8f * sl - 8f * _flinch + 10f * d, 8f * w);
            Turn("Chest", p.Breath * 2f - 5f * w + 6f * s + 6f * sl, 6f * w, p.Shake);
            Turn("Neck", 4f * sl - 3f * w + 3f * s, look * 0.4f);
            Turn("Head", -8f * w + 8f * s + 12f * sl + 20f * d, look * 0.6f + p.Shake * 1.5f, 6f * Mathf.Sin(t * 0.35f));

            // The sceptre arm lifts it in the tell and drives it down when the spell lands.
            var drift = Mathf.Sin(t * 0.8f);
            Turn("UpperArm_R", -22f * w + 18f * s + 3f * drift + 6f * sl - 25f * tr);
            Turn("LowerArm_R", -15f * w + 8f * s - 20f * tr);
            Turn("Hand_R", 4f * drift * (1f - w));
            // The casting hand: reaches out, palm forward, during the tell; thrusts when it lands; shields under Fear.
            Turn("UpperArm_L", -45f * w * (1f - s) - 60f * s + 4f * drift + 8f * Mathf.Sin(_walkPhase) * m - 45f * tr + 5f * sl);
            Turn("LowerArm_L", -30f * w - 15f * s - 4f * drift - 50f * tr);
            Turn("Hand_L", -25f * w + 6f * Mathf.Sin(t * 9f) * w);

            // The robe swings like cloth: it trails when she glides or lunges, and sways while she hovers.
            Turn("Robe1", 10f * m + 8f * s - 4f * w + 3f * Mathf.Sin(t * 1.1f), 0f, 3f * Mathf.Sin(t * 0.9f + 1f));
            Turn("Robe2", 8f * m + 6f * s + 4f * Mathf.Sin(t * 1.1f - 0.8f), 0f, 3f * Mathf.Sin(t * 0.9f));
        }

        /// <summary>
        /// The big cat: a four-beat walk (hind, fore, hind, fore), a gallop when it charges, a low stalking head, a
        /// tail that sways and lashes, a crouch before the pounce, a roar.
        /// </summary>
        void Lion(Pose p)
        {
            float w = _windup, s = p.Strike, sl = _slump, tr = _tremble, d = p.Death, m = _moving, t = _time;
            var a = _walkPhase;
            var gallop = Mathf.Clamp01((_speed - 5f) / 3f);
            var bob = Mathf.Abs(Mathf.Sin(a * 2f)) * 0.05f * m + gallop * Mathf.Sin(a) * 0.12f * m;
            Root(p, bob - 0.35f * w + 0.2f * s - 0.2f * sl - 0.15f * tr, 4f * w + 6f * s + 4f * sl + gallop * 5f * Mathf.Sin(a) * m, 0f);

            // Leg phase offsets: walk = hind L, fore L, hind R, fore R; gallop = hinds together, then fores.
            float Offset(float walk, float run) => Mathf.Lerp(walk, run, gallop) * Mathf.PI * 2f;
            var reach = Mathf.Lerp(22f, 38f, gallop) * m;
            void Leg(string upper, string lower, string paw, float offset, bool fore)
            {
                var phase = a + offset;
                var lift = Mathf.Max(0f, Mathf.Cos(phase)) * m;
                var swing = -reach * Mathf.Sin(phase);
                if (fore)
                {
                    Turn(upper, swing + 12f * w - 55f * s + 10f * sl + 25f * d + 10f * tr);
                    Turn(lower, 45f * lift - 12f * w - 15f * s + 30f * d);
                    Turn(paw, 35f * lift + 10f * w - 25f * s);
                }
                else
                {
                    Turn(upper, swing - 18f * w + 25f * s - 20f * sl - 30f * d - 10f * tr);
                    Turn(lower, -30f * lift - 20f * w + 20f * s - 25f * sl - 30f * d);
                    Turn(paw, 35f * lift + 30f * w - 10f * s + 20f * sl + 30f * d);
                }
            }
            Leg("RearUpper_L", "RearLower_L", "RearFoot_L", Offset(0f, 0f), false);
            Leg("FrontUpper_L", "FrontLower_L", "FrontPaw_L", Offset(0.25f, 0.5f), true);
            Leg("RearUpper_R", "RearLower_R", "RearFoot_R", Offset(0.5f, 0.1f), false);
            Leg("FrontUpper_R", "FrontLower_R", "FrontPaw_R", Offset(0.75f, 0.6f), true);

            // A supple spine: it bends side to side with the walk and flexes with the gallop.
            var sway = Mathf.Sin(a) * m;
            Turn("Hips", 3f * gallop * Mathf.Cos(a) * m, 4f * sway, 2f * sway);
            Turn("Spine", p.Breath * 1.5f + 6f * gallop * Mathf.Sin(a) * m - 4f * w, -3f * sway);
            Turn("Chest", p.Breath * 1.5f + 6f * w - 8f * s, -3f * sway, p.Shake);

            // The head: low and level while it stalks, scanning when it stands; it roars in the tell and bites.
            var look = (Mathf.PerlinNoise(t * 0.3f, 4.2f) - 0.5f) * 50f * (1f - m) * (1f - w);
            var roar = Mathf.Max(s, Mathf.Clamp01((w - 0.5f) * 2f));
            Turn("Neck", 12f * m + 14f * w - 18f * s + 22f * sl + 20f * tr + 25f * d, look * 0.5f + 3f * sway);
            Turn("Head", -8f * m - 10f * w - 12f * roar + 8f * sl + p.Breath * 2f, look * 0.5f + p.Shake * 1.5f);
            Turn("Jaw", 3f + 2f * Mathf.Sin(t * 0.7f) + 30f * roar + 12f * d);

            // The tail: a slow S-curve sway, lashing during the tell, tucked under Fear.
            var lash = 1f + 2.5f * w * p.Fury;
            for (var i = 1; i <= 4; i++)
            {
                var yaw = (5f + 5f * i) * lash * Mathf.Sin(t * (1.4f + 5f * w) - i * 0.75f);
                var pitch = i == 1 ? 15f * w - 35f * tr - 20f * d : 4f * Mathf.Sin(t * 1.1f - i) - 8f * tr;
                Turn($"Tail{i}", pitch, yaw);
            }
        }

        /// <summary>
        /// The stone machine: stiff, a little late (bones follow slowly), stomping steps with a weight shift, a head
        /// that turns in steps and holds, both fists raised overhead before the slam.
        /// </summary>
        void Golem(Pose p)
        {
            float w = _windup, s = p.Strike, sl = _slump, tr = _tremble, d = p.Death, m = _moving, t = _time;
            var a = _walkPhase;
            var step = Mathf.Sin(a) * m;
            var stomp = Mathf.Pow(1f - Mathf.Abs(Mathf.Cos(a)), 4f) * m; // a jolt as each foot lands
            Root(p, 0.08f * Mathf.Abs(Mathf.Sin(a)) * m - 0.05f * stomp - 0.12f * w - 0.2f * s - 0.15f * sl - 1.0f * d,
                3f * m - 8f * w + 14f * s + 10f * sl + 30f * d, 3f * step);

            // Legs: alternate, knees fold only while a foot is in the air; they kneel when it falls.
            void Leg(string upper, string lower, string foot, float phase)
            {
                var lift = Mathf.Max(0f, Mathf.Cos(phase)) * m;
                Turn(upper, -22f * Mathf.Sin(phase) * m - 10f * w - 70f * d);
                Turn(lower, 40f * lift + 15f * w + 10f * s + 100f * d);
                Turn(foot, -15f * lift - 5f * w - 30f * d);
            }
            Leg("UpperLeg_L", "LowerLeg_L", "Foot_L", a);
            Leg("UpperLeg_R", "LowerLeg_R", "Foot_R", a + Mathf.PI);

            Turn("Hips", 0f, 6f * step, 3f * step);
            Turn("Spine", p.Breath * 1f - 4f * w + 10f * s + 8f * sl, -5f * step + 10f * w);
            Turn("Chest", -10f * w + 22f * s + 15f * sl - 10f * _flinch + 25f * d, 0f, p.Shake);

            // The head scans in steps and holds, like a machine; it looks up at its fists in the tell.
            var scan = Mathf.Round((Mathf.PerlinNoise(t * 0.2f, 9.1f) - 0.5f) * 4f) * 12f * (1f - w);
            Turn("Head", -12f * w + 10f * s + 15f * sl + 15f * d, scan + p.Shake * 1.5f);

            // Arms: stiff swing against the legs with a set elbow; overhead in the tell; the slam; a guard under Fear.
            void Arm(string upper, string lower, string hand, float swing)
            {
                Turn(upper, swing * 14f - 140f * w * (1f - s) + 25f * s - 75f * tr + 6f * sl + 10f * d);
                Turn(lower, -15f - 35f * w - 10f * s - 90f * tr + 10f * sl);
                Turn(hand, -10f * w + 15f * s);
            }
            Arm("UpperArm_L", "LowerArm_L", "Hand_L", step);
            Arm("UpperArm_R", "LowerArm_R", "Hand_R", -step);
        }

        /// <summary>The whole body: rises (metres), leans (degrees, forward positive), rolls, lunges on a strike.</summary>
        void Root(Pose p, float rise, float lean, float roll)
        {
            var reach = _body == Body.Lion ? 0.9f : 0.5f;
            var metres = 1f / Mathf.Max(0.01f, _frame.localScale.y); // the model sits under the scaled capsule
            _root.localPosition = _rootPosition + new Vector3(0f, rise, reach * p.Strike - 0.2f * _flinch) * metres;
            var bodyLean = (_body == Body.Lion ? 0f : 0.4f) * lean + 28f * p.Death * (_body == Body.Golem ? 0.3f : 1f) - 5f * _flinch;
            _root.localRotation = Quaternion.AngleAxis(bodyLean, Vector3.right)
                                  * Quaternion.AngleAxis(roll + p.Shake + 10f * p.Death, Vector3.forward) * _rootRotation;
        }

        /// <summary>Eases a bone toward a pose: pitch about the Guardian's right (positive tips forward), yaw about up, roll about forward.</summary>
        void Turn(string bone, float pitch, float yaw = 0f, float roll = 0f)
        {
            if (!_bones.TryGetValue(bone, out var b)) return;
            var parent = b.T.parent;
            var right = parent.InverseTransformDirection(_frame.right);
            var up = parent.InverseTransformDirection(_frame.up);
            var forward = parent.InverseTransformDirection(_frame.forward);
            var target = Quaternion.AngleAxis(pitch, right) * Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(roll, forward) * b.RestRotation;
            b.T.localRotation = Quaternion.Slerp(b.T.localRotation, target, 1f - Mathf.Exp(-_follow * _dt));
        }

        static float Damp(float current, float target, float rate, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
