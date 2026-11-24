using System.Collections.Generic;
using TheArtifacts.Core.Combat;
using UnityEngine;

namespace TheArtifacts.PC.Combat
{
    /// <summary>
    /// Procedural animation for the rigged player model (Art/Models/Player, bones from tools/blender/rig_guardians.py),
    /// driven by the fight like <see cref="GuardianMotion"/>: she turns to run where she moves and to face the Guardian
    /// when she stops, runs with counter-swinging arms, slashes with her right hand, the weapon hand (auto-attack, Heavy
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
        /// <summary>Radians of run cycle per metre: about 3.1 m per cycle, 2.2 strides a second at her 7 m/s run.</summary>
        const float Stride = 2.0f;

        float _time, _runPhase, _moving, _guard, _flinch, _fall, _dt;
        Action _action;
        float _actionTime, _actionLength;
        Vec2? _lastPosition;
        float _lastSimTime;
        Vec2 _velocity;
        /// <summary>The axis, in the right hand's space, that curls its fingers toward the palm (null without Fingers_R).</summary>
        readonly Vector3? _curlAxis;
        float _grip;

        /// <summary>A weapon is in her right hand: the fingers close around its grip (CombatSandbox.WearArtifacts).</summary>
        public bool Gripping { get; set; }

        // A worn cape's bones (tools/blender/make_cape.py: Cape_R1-4, Cape_C1-4, Cape_L1-4): each row swings on a spring
        // toward a trail set by how she moves, so it streams back as she runs, swings out as she turns or sidesteps,
        // ripples at speed, and settles with a sway when she stops.
        readonly List<(Transform T, Quaternion RestRotation, int Row, int Column)> _cape = new List<(Transform, Quaternion, int, int)>();
        const int CapeRows = 4;
        readonly Vector2[] _capeAngle = new Vector2[CapeRows], _capeRate = new Vector2[CapeRows];
        Vector3 _capeVelocity;
        float _lastYaw, _yawRate;
        // Its empty hanging sleeves (Sleeve_R1-2, Sleeve_L1-2): two rows each, on their own springs, trailing a little less
        // than the cape and lifting outward as she runs.
        readonly List<(Transform T, Quaternion RestRotation, int Row, int Side)> _sleeves = new List<(Transform, Quaternion, int, int)>();
        readonly Vector2[] _sleeveAngle = new Vector2[4], _sleeveRate = new Vector2[4];

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
            // At rest the fingers hang from the knuckles (the wrist-to-knuckle line runs the same way) with the palm
            // turned to her left; curling turns them from that line toward her left, about an axis fixed in the hand.
            if (_bones.TryGetValue("Fingers_R", out var fingers) && _bones.TryGetValue("Hand_R", out var hand))
            {
                var along = (fingers.T.position - hand.T.position).normalized;
                _curlAxis = hand.T.InverseTransformDirection(Vector3.Cross(along, -frame.right).normalized);
            }
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

        /// <summary>Takes in a worn artifact's own bones (a cape's), at rest.</summary>
        public void Wear(Transform worn)
        {
            foreach (var t in worn.GetComponentsInChildren<Transform>(true))
            {
                // Cape_<R|C|L><row>, Sleeve_<R|L><row>
                if (t.name.Length == 7 && t.name.StartsWith("Cape_"))
                {
                    var column = "RCL".IndexOf(t.name[5]);
                    var row = t.name[6] - '1';
                    if (column >= 0 && row >= 0 && row < CapeRows) _cape.Add((t, t.localRotation, row, column));
                }
                else if (t.name.Length == 9 && t.name.StartsWith("Sleeve_"))
                {
                    var side = "RL".IndexOf(t.name[7]);
                    var row = t.name[8] - '1';
                    if (side >= 0 && row >= 0 && row < 2) _sleeves.Add((t, t.localRotation, row, side));
                }
            }
        }

        /// <summary>Lets go of the worn artifacts' bones (they are being destroyed).</summary>
        public void Unwear()
        {
            _cape.Clear();
            _sleeves.Clear();
            ResetCape();
        }

        void ResetCape()
        {
            for (var i = 0; i < CapeRows; i++) _capeAngle[i] = _capeRate[i] = Vector2.zero;
            for (var i = 0; i < _sleeveAngle.Length; i++) _sleeveAngle[i] = _sleeveRate[i] = Vector2.zero;
            foreach (var c in _sleeves) c.T.localRotation = c.RestRotation;
            _capeVelocity = Vector3.zero;
            _yawRate = 0f;
            _lastYaw = _frame.eulerAngles.y;
            foreach (var c in _cape) c.T.localRotation = c.RestRotation;
        }

        public void Reset()
        {
            ResetCape();
            _time = _runPhase = _moving = _guard = _flinch = _fall = _grip = 0f;
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
            Cape(velocity, dt);
        }

        void Cape(Vec2 velocity, float dt)
        {
            if (_cape.Count == 0 && _sleeves.Count == 0) return;
            var yaw = _frame.eulerAngles.y;
            _yawRate = Mathf.Lerp(_yawRate, Mathf.DeltaAngle(_lastYaw, yaw) / dt, 1f - Mathf.Exp(-10f * dt));
            _lastYaw = yaw;
            _capeVelocity = Vector3.Lerp(_capeVelocity, _frame.InverseTransformDirection(new Vector3(velocity.X, 0f, velocity.Y)),
                1f - Mathf.Exp(-6f * dt));
            var forward = Mathf.Clamp(_capeVelocity.z, -3f, 8f);
            var side = Mathf.Clamp(_capeVelocity.x, -6f, 6f);
            var pace = Mathf.Clamp01(new Vector2(_capeVelocity.x, _capeVelocity.z).magnitude / 7f);
            for (var row = 0; row < CapeRows; row++)
            {
                // The lower rows trail further: about 15°, 17°, 19°, 21° each (72° at the hem) at her full run.
                var trail = (1.8f + 0.3f * row) * forward;
                // Sidestepping or turning swings it out the other way; a turn to her right (positive yaw) flings it left.
                var swing = -(0.9f + 0.4f * row) * side - (0.02f + 0.012f * row) * _yawRate;
                var target = new Vector2(trail, Mathf.Clamp(swing, -35f, 35f));
                // A slightly underdamped spring: it overshoots and sways when she stops.
                var accel = 70f * (target - _capeAngle[row]) - 8f * _capeRate[row];
                _capeRate[row] += accel * dt;
                _capeAngle[row] += _capeRate[row] * dt;
            }
            foreach (var (t, rest, row, column) in _cape)
            {
                // A ripple that runs down the cape and differs across it, growing with speed; a slow sway at rest.
                var ripple = pace * (2f + 2.5f * row) * Mathf.Sin(_time * 9f - row * 1.1f + column * 1.7f)
                             + 1.2f * Mathf.Sin(_time * 1.3f + row * 0.6f + column);
                var a = _capeAngle[row];
                t.localRotation = FrameTurn(t, a.x + ripple, 0f, a.y + 0.4f * ripple * (column - 1)) * rest;
            }

            for (var i = 0; i < _sleeveAngle.Length; i++)
            {
                int which = i / 2, row = i % 2;
                var outward = which == 0 ? 1f : -1f;  // positive roll swings toward her right: out for the right sleeve
                var trail = (1.4f + 0.5f * row) * forward;
                var swing = -(0.8f + 0.5f * row) * side - (0.02f + 0.015f * row) * _yawRate + outward * (6f + 4f * row) * pace;
                var target = new Vector2(trail, Mathf.Clamp(swing, -40f, 40f));
                var accel = 90f * (target - _sleeveAngle[i]) - 7f * _sleeveRate[i];
                _sleeveRate[i] += accel * dt;
                _sleeveAngle[i] += _sleeveRate[i] * dt;
            }
            foreach (var (t, rest, row, sideIndex) in _sleeves)
            {
                var flap = pace * (3f + 4f * row) * Mathf.Sin(_time * 10f - row * 1.3f + sideIndex * 2.1f)
                           + 1.5f * Mathf.Sin(_time * 1.5f + row + sideIndex * 1.4f);
                var a = _sleeveAngle[sideIndex * 2 + row];
                t.localRotation = FrameTurn(t, a.x + flap, 0f, a.y + 0.5f * flap * (sideIndex == 0 ? 1f : -1f)) * rest;
            }
        }

        /// <summary>A turn about her own axes (pitch about her right, yaw about up, roll about forward) in the bone's parent space.</summary>
        Quaternion FrameTurn(Transform bone, float pitch, float yaw, float roll)
        {
            var parent = bone.parent;
            return Quaternion.AngleAxis(pitch, parent.InverseTransformDirection(_frame.right))
                   * Quaternion.AngleAxis(yaw, parent.InverseTransformDirection(_frame.up))
                   * Quaternion.AngleAxis(roll, parent.InverseTransformDirection(_frame.forward));
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
            // A runner's forward lean (about 25° through spine and chest), kept while she swings or casts on the move;
            // the neck and head tip back so she still looks ahead.
            Turn("Spine", 16f * legs + breath * 1.5f + 15f * lunge * s + 12f * heavy * s - 10f * heavy * k - 10f * fl,
                -6f * twist + 20f * slash * k - 30f * slash * s + 15f * heavy * k - 10f * cast * s);
            Turn("Chest", 9f * legs + breath * 2f + 8f * heavy * s - 6f * fl,
                -8f * twist + 15f * slash * k - 25f * slash * s - 15f * cast * s);
            Turn("Neck", -8f * legs + 4f * g);
            Turn("Head", -10f * legs - 8f * fl + 4f * g, -10f * slash * k + 15f * slash * s, 3f * Mathf.Sin(t * 0.4f));
            // The braid trails as she runs and swings when she turns.
            Turn("Braid1", -20f * run - 8f * Mathf.Abs(Mathf.Sin(a)) * run - 25f * spin * s, 10f * twist);
            Turn("Braid2", -10f * run - 6f * Mathf.Sin(a * 2f) * run, 8f * twist);

            // The weapon arm (empty until equipment is attached): swings with the run; a high wind-up and a cross-body slash; overhead for Heavy Strike;
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
            // A closed fist around a held weapon (the handle crosses the palm; tools/blender/prepare_artifact.py).
            _grip = Damp(_grip, Gripping ? 1f : 0f, 12f, _dt);
            if (_curlAxis is Vector3 curl && _bones.TryGetValue("Fingers_R", out var fingers))
                fingers.T.localRotation = Quaternion.AngleAxis(80f * _grip, curl) * fingers.RestRotation;

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
            var target = FrameTurn(b.T, pitch, yaw, roll) * b.RestRotation;
            b.T.localRotation = Quaternion.Slerp(b.T.localRotation, target, 1f - Mathf.Exp(-Follow * _dt));
        }

        static float Damp(float current, float target, float rate, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
