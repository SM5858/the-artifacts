using System.Collections.Generic;
using TheArtifacts.Core.Combat;
using UnityEngine;

namespace TheArtifacts.PC.Combat
{
    /// <summary>
    /// Procedural animation for the rigged player model (Art/Models/Player, bones from tools/blender/rig_guardians.py),
    /// driven by the fight like <see cref="GuardianMotion"/>: she turns to run where she moves and to face the Guardian
    /// when she stops, runs with counter-swinging arms, slashes with the dagger in her right hand (auto-attack, Heavy
    /// Strike, Lunge, Overpower), casts with her left hand (bolts and artifact skills), raises a guard, flinches when hit,
    /// and falls when she loses.
    /// Bones are turned about her own axes (pitch about her right, positive tips an upright bone forward and swings a
    /// hanging one back; yaw about up; roll about forward, positive swings a hanging bone out to her right).
    /// </summary>
    public sealed class PlayerMotion
    {
        enum Action { None, Slash, Heavy, Lunge, Overpower, Cast }

        readonly Transform _root, _frame;
        readonly Vector3 _rootPosition;
        readonly Quaternion _rootRotation;
        readonly Dictionary<string, (Transform T, Quaternion RestRotation)> _bones = new Dictionary<string, (Transform, Quaternion)>();
        const float Follow = 20f;
        /// <summary>Radians of run cycle per metre: about 2.6 m per cycle at her 7 m/s run.</summary>
        const float Stride = 2.4f;

        float _time, _runPhase, _moving, _guard, _flinch, _fall, _dt;
        Action _action;
        float _actionTime, _actionLength;
        Vec2? _lastPosition;
        float _lastSimTime;
        Vec2 _velocity;

        /// <param name="model">The model's root, a child of <paramref name="frame"/>.</param>
        /// <param name="frame">The player's transform; this turns it to face where she runs or the Guardian.</param>
        public PlayerMotion(Transform model, Transform frame)
        {
            _root = model;
            _frame = frame;
            _rootPosition = model.localPosition;
            _rootRotation = model.localRotation;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t != model && !_bones.ContainsKey(t.name)) _bones[t.name] = (t, t.localRotation);
        }

        /// <summary>Starts the move for a combat event about the player (an ability used, an auto-attack landing, a hit taken).</summary>
        public void OnEvent(CombatEvent e)
        {
            switch (e.Type)
            {
                case CombatEventType.GuardianDamaged when e.Id == "auto": Play(Action.Slash, 0.45f); break;
                case CombatEventType.PlayerDamaged when e.Amount > 0f: _flinch = 1f; break;
                case CombatEventType.AbilityUsed:
                    switch (e.Id)
                    {
                        case "HeavyStrike": Play(Action.Heavy, 0.7f); break;
                        case "Lunge": Play(Action.Lunge, 0.5f); break;
                        case "Overpower": Play(Action.Overpower, 0.9f); break;
                        case "Guard": case "auto_attack_on": case "auto_attack_off": break;
                        default: Play(Action.Cast, 0.6f); break; // the bolts and artifact skills
                    }
                    break;
            }
        }

        void Play(Action action, float seconds)
        {
            _action = action;
            _actionTime = 0f;
            _actionLength = seconds;
        }

        public void Reset()
        {
            _time = _runPhase = _moving = _guard = _flinch = _fall = 0f;
            _action = Action.None;
            _lastPosition = null;
            _velocity = Vec2.Zero;
            _root.localPosition = _rootPosition;
            _root.localRotation = _rootRotation;
            foreach (var b in _bones.Values) b.T.localRotation = b.RestRotation;
        }

        public void Update(CombatSim sim, float dt)
        {
            if (dt <= 0f) return;
            _dt = dt;
            _time += dt;

            _velocity = GuardianMotion.SimVelocity(sim, sim.PlayerPosition, ref _lastPosition, ref _lastSimTime, _velocity);
            var velocity = _velocity;
            var speed = velocity.Length;
            var alive = sim.Outcome != CombatOutcome.Loss;
            _moving = Damp(_moving, alive && speed > 0.5f ? 1f : 0f, 8f, dt);
            _runPhase += dt * Mathf.Min(speed, 12f) * Stride;
            _guard = Damp(_guard, sim.GuardRemaining > 0f ? 1f : 0f, 14f, dt);
            _flinch = Mathf.Max(0f, _flinch - dt / 0.25f);
            _fall = alive ? 0f : Mathf.Min(1f, _fall + dt / 0.9f);
            if (_action != Action.None && (_actionTime += dt) >= _actionLength) _action = Action.None;

            // Face where she is going while she moves (even mid-swing or mid-cast); standing, face the Guardian.
            var toGuardian = sim.GuardianPosition - sim.PlayerPosition;
            var face = speed > 0.5f ? velocity : toGuardian;
            if (alive && face.LengthSquared > 1e-6f)
                _frame.rotation = Quaternion.Slerp(_frame.rotation, Quaternion.LookRotation(new Vector3(face.X, 0f, face.Y)),
                    1f - Mathf.Exp(-14f * dt));

            Pose();
        }

        void Pose()
        {
            float m = _moving, g = _guard, fl = _flinch, f = Smooth(_fall), t = _time, a = _runPhase;
            var breath = Mathf.Sin(t * Mathf.PI * 2f * 0.35f);

            // The move in progress: k rises through the wind-up, s is the strike, both fall away in the follow-through.
            var u = _action == Action.None ? 1f : _actionTime / _actionLength;
            float Bell(float from, float peak, float to) =>
                u < from || u > to ? 0f : u < peak ? Smooth((u - from) / (peak - from)) : 1f - Smooth((u - peak) / (to - peak));
            var k = Bell(0f, 0.35f, 0.6f);
            var s = Bell(0.3f, 0.5f, 1f);
            float slash = 0f, heavy = 0f, lunge = 0f, spin = 0f, cast = 0f;
            switch (_action)
            {
                case Action.Slash: slash = 1f; break;
                case Action.Heavy: heavy = 1f; break;
                case Action.Lunge: lunge = 1f; break;
                case Action.Overpower: spin = 1f; break;
                case Action.Cast: cast = 1f; break;
            }
            var busy = Mathf.Max(slash, heavy, lunge, spin, cast) * Mathf.Max(k, s);
            // The legs keep running through a swing or a cast; the arms and trunk give way to the move.
            var legs = m * (1f - lunge * s) * (1f - spin * s);
            var run = m * (1f - busy) * (1f - g);

            // The body: a light bob as she runs, a lean into lunges, a full turn for Overpower, a fall backwards.
            var bob = legs * 0.05f * Mathf.Abs(Mathf.Cos(a));
            var crouch = 0.06f * g + 0.1f * lunge * s + 0.05f * heavy * s;
            _root.localPosition = _rootPosition + new Vector3(0f, bob - crouch - 0.05f * f, 0.6f * lunge * s);
            var turn = spin * 360f * Smooth(Mathf.Clamp01((u - 0.2f) / 0.6f));
            _root.localRotation = Quaternion.AngleAxis(turn, Vector3.up)
                                  * Quaternion.AngleAxis(-80f * f - 6f * fl, Vector3.right) * _rootRotation;

            // Legs: a run with folding knees and swinging thighs; a split stance for the lunge; bent knees on guard.
            var stand = 1f - legs;
            void Leg(string upper, string lower, string foot, float phase, float split)
            {
                var swing = Mathf.Sin(phase);
                var fold = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(phase)), 1.5f);
                Turn(upper, legs * (-40f * swing - 15f * fold) - 12f * g * stand + split * 40f * lunge * s - 8f * heavy * s);
                Turn(lower, legs * (15f + 85f * fold) + 22f * g * stand + 20f * lunge * s + 10f * heavy * s + 30f * f);
                Turn(foot, legs * (-10f + 25f * fold) - 10f * g * stand);
            }
            Leg("UpperLeg_L", "LowerLeg_L", "Foot_L", a, -1f);
            Leg("UpperLeg_R", "LowerLeg_R", "Foot_R", a + Mathf.PI, 0.8f);

            // The trunk leans into the run and twists against the legs; it winds up and turns through each attack.
            var twist = legs * Mathf.Sin(a);
            Turn("Hips", 0f, 8f * twist);
            Turn("Spine", 10f * run + breath * 1.5f + 15f * lunge * s + 12f * heavy * s - 10f * heavy * k - 10f * fl,
                -6f * twist + 20f * slash * k - 30f * slash * s + 15f * heavy * k - 10f * cast * s);
            Turn("Chest", 6f * run + breath * 2f + 8f * heavy * s - 6f * fl,
                -8f * twist + 15f * slash * k - 25f * slash * s - 15f * cast * s);
            Turn("Neck", -4f * run + 4f * g);
            Turn("Head", -6f * run - 8f * fl + 4f * g, -10f * slash * k + 15f * slash * s, 3f * Mathf.Sin(t * 0.4f));
            // The braid trails as she runs and swings when she turns.
            Turn("Braid1", -20f * run - 8f * Mathf.Abs(Mathf.Sin(a)) * run - 25f * spin * s, 10f * twist);
            Turn("Braid2", -10f * run - 6f * Mathf.Sin(a * 2f) * run, 8f * twist);

            // The dagger arm: swings with the run; a high wind-up and a cross-body slash; overhead for Heavy Strike;
            // a straight thrust for Lunge; held out wide through the Overpower spin; up across the face on guard.
            Turn("Shoulder_R", -5f * heavy * k - 6f * g);
            Turn("UpperArm_R",
                -run * 45f * Mathf.Sin(a) - 80f * slash * k - 20f * slash * s - 150f * heavy * k - 30f * heavy * s
                - 85f * lunge * s - 30f * lunge * k - 20f * spin * s - 70f * g + 10f * fl,
                -40f * slash * s - 20f * g,
                30f * slash * k + 75f * spin * Mathf.Max(k, s) - 20f * g);
            Turn("LowerArm_R", -70f * run - 60f * slash * k - 10f * slash * s - 20f * heavy * k - 70f * lunge * k
                + 60f * lunge * s - 100f * g - 15f * stand);
            Turn("Hand_R", -20f * slash * s + 15f * heavy * s - 20f * lunge * s);

            // The spell hand: swings with the run; thrusts forward, palm out, to cast; across the body on guard.
            Turn("Shoulder_L", -8f * cast * s - 6f * g);
            Turn("UpperArm_L",
                run * 45f * Mathf.Sin(a) + 15f * cast * k - 75f * cast * s + 25f * lunge * s + 15f * slash * s - 70f * g + 10f * fl,
                0f, -12f * cast * k + 20f * g - 60f * spin * Mathf.Max(k, s));
            Turn("LowerArm_L", -50f * run + 55f * cast * s - 25f * cast * k - 100f * g);
            Turn("Hand_L", -35f * cast * s + 6f * Mathf.Sin(t * 12f) * cast * k);
        }

        /// <summary>Eases a bone toward a pose: pitch about her right, yaw about up, roll about forward.</summary>
        void Turn(string bone, float pitch, float yaw = 0f, float roll = 0f)
        {
            if (!_bones.TryGetValue(bone, out var b)) return;
            var parent = b.T.parent;
            var right = parent.InverseTransformDirection(_frame.right);
            var up = parent.InverseTransformDirection(_frame.up);
            var forward = parent.InverseTransformDirection(_frame.forward);
            var target = Quaternion.AngleAxis(pitch, right) * Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(roll, forward) * b.RestRotation;
            b.T.localRotation = Quaternion.Slerp(b.T.localRotation, target, 1f - Mathf.Exp(-Follow * _dt));
        }

        static float Damp(float current, float target, float rate, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
