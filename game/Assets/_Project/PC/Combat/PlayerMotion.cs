using System.Collections.Generic;
using TheArtifacts.Core.Combat;
using UnityEngine;

namespace TheArtifacts.PC.Combat
{
    /// <summary>
    /// Procedural animation for the rigged player model (Art/Models/Player, bones from tools/blender/rig_guardians.py),
    /// driven by the fight like <see cref="GuardianMotion"/>: she turns to run where she moves and to face the Guardian
    /// when she stops, runs with counter-swinging arms, casts with her left hand (bolts and artifact skills), raises a
    /// guard, flinches when hit, and falls when she loses. Her attacks are keyframed whole-body clips (see Clips): the
    /// hips lead, the trunk whips round after them, the feet step and plant, and the weapon arm is aimed in the chest's
    /// frame: a horizontal slash for auto-attack, a crouched run for a dash-in, two quick cuts for Double Strike, a leap
    /// and a downward cut on landing for Lunge, and a gathered aura and a great diagonal cut for Overpower. Each clip also
    /// says when the blade leaves a trail and how strong the aura is (<see cref="Trail"/>, <see cref="Aura"/>).
    /// Bones are turned about her own axes (pitch about her right, positive tips an upright bone forward and swings a
    /// hanging one back; yaw about up; roll about forward, positive swings a hanging bone out to her right).
    /// </summary>
    public sealed class PlayerMotion
    {
        public enum Action { None, Slash, Dash, Double, Lunge, Overpower, Cast }

        readonly Transform _root, _frame;
        readonly Vector3 _rootPosition;
        readonly Quaternion _rootRotation;
        readonly Dictionary<string, (Transform T, Quaternion RestRotation)> _bones = new Dictionary<string, (Transform, Quaternion)>();
        const float Follow = 20f;
        /// <summary>Radians of run cycle per metre: about 3.1 m per cycle, 2.2 strides a second at her 7 m/s run.</summary>
        const float Stride = 2.0f;

        float _time, _runPhase, _moving, _guard, _flinch, _fall, _dt, _leap = -1f;
        Action _action;
        /// <summary>The pose a new clip fades in from, and whether there is one.</summary>
        Key _from, _current;
        float _fromWeight;
        const float CrossFade = 0.07f;
        /// <summary>A Lunge leap's peak height, metres (its arc follows <see cref="CombatSim.LeapProgress"/>).</summary>
        const float LeapHeight = 0.75f;
        /// <summary>Each aimed arm bone's head-to-tail direction in its own space.</summary>
        readonly Dictionary<string, Vector3> _along = new Dictionary<string, Vector3>();
        /// <summary>The held blade's direction (pommel to tip) in the forearm's space, the wrist at rest.</summary>
        readonly Vector3? _bladeAxis;
        /// <summary>The chest's rest rotation relative to her frame.</summary>
        readonly Quaternion _chestRest = Quaternion.identity;

        /// <summary>The move being played.</summary>
        public Action Current => _action;
        /// <summary>How strongly the blade leaves a trail now (0 for none), from the clip.</summary>
        public float Trail { get; private set; }
        /// <summary>How strong Overpower's aura is now (0 to 1), from the clip.</summary>
        public float Aura { get; private set; }
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
            foreach (var (bone, child) in new[] { ("UpperArm_R", "LowerArm_R"), ("LowerArm_R", "Hand_R"), ("UpperArm_L", "LowerArm_L"), ("LowerArm_L", "Hand_L") })
                if (_bones.TryGetValue(bone, out var b) && _bones.TryGetValue(child, out var c))
                    _along[bone] = b.T.InverseTransformDirection((c.T.position - b.T.position).normalized);
            // The Lion's Fang's hold: blade forward and 25° down from the hanging fist (tools/blender/prepare_artifact.py HOLDS).
            if (_bones.TryGetValue("LowerArm_R", out var fore))
                _bladeAxis = fore.T.InverseTransformDirection(frame.TransformDirection(new Vector3(0f, -0.43f, 0.9f).normalized));
            if (_bones.TryGetValue("Chest", out var chest)) _chestRest = Quaternion.Inverse(frame.rotation) * chest.T.rotation;
        }

        /// <summary>Starts the move for a combat event about the player (an ability used, an auto-attack landing, a hit taken).</summary>
        public void OnEvent(CombatEvent e)
        {
            switch (e.Type)
            {
                case CombatEventType.GuardianDamaged when e.Id == "auto": Play(Action.Slash); break;
                case CombatEventType.PlayerDamaged when e.Amount > 0f: _flinch = 1f; break;
                case CombatEventType.DashStarted: Play(Action.Dash); break;
                case CombatEventType.AbilityUsed:
                    switch (e.Id)
                    {
                        case "DoubleStrike": Play(Action.Double); break;
                        case "Lunge": Play(Action.Lunge); break;
                        case "Overpower": Play(Action.Overpower); break;
                        case "Guard": case "auto_attack_on": case "auto_attack_off": break;
                        default: Play(Action.Cast, 0.6f); break; // the bolts and artifact skills
                    }
                    break;
            }
        }

        void Play(Action action, float seconds = 0f)
        {
            // The new clip fades in from the pose she is in, so a dash flows into its strike.
            _from = _current;
            _fromWeight = 1f;
            _action = action;
            _actionTime = 0f;
            _actionLength = Clips.TryGetValue(action, out var keys) ? keys[keys.Length - 1].T : seconds;
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
            _time = _runPhase = _moving = _guard = _flinch = _fall = _grip = _fromWeight = 0f;
            _leap = -1f;
            _action = Action.None;
            _from = _current = Key.Zero;
            Trail = Aura = 0f;
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
            _leap = alive ? sim.LeapProgress : -1f;
            if (_action != Action.None && (_actionTime += dt) >= _actionLength)
            {
                _action = Action.None;
                _fromWeight = 0f;
            }

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

            // Casting keeps its simple wind-up (k) and thrust (s) of the left hand.
            var u = _action == Action.None ? 1f : _actionTime / _actionLength;
            float Bell(float from, float peak, float to) =>
                u < from || u > to ? 0f : u < peak ? Smooth((u - from) / (peak - from)) : 1f - Smooth((u - peak) / (to - peak));
            var cast = _action == Action.Cast ? 1f : 0f;
            var k = cast * Bell(0f, 0.35f, 0.6f);
            var s = cast * Bell(0.3f, 0.5f, 1f);

            // The attack clip's pose at this moment, faded in from whatever pose she was in (CrossFade).
            var clip = _action != Action.None && Clips.TryGetValue(_action, out var keys) ? Sample(keys, _actionTime) : Key.Zero;
            var fade = _fromWeight > 0f ? Smooth(Mathf.Clamp01(_actionTime / CrossFade)) : 1f;
            var p = fade >= 1f ? clip : Key.Lerp(_from, clip, fade);
            _current = p;
            Trail = p.Trail;
            Aura = p.Aura;
            // Feet planted: the stance and steps of a clip give way to the run while she moves.
            var plant = 1f - m;
            var arms = Mathf.Max(p.ArmR, p.ArmL);
            var run = m * (1f - arms) * (1f - g);

            // The body: a light bob as she runs, the clip's steps and crouches, a leap's arc, a fall backwards.
            var bob = m * 0.05f * Mathf.Abs(Mathf.Cos(a));
            var crouch = 0.06f * g;
            var leap = _leap >= 0f ? LeapHeight * 4f * _leap * (1f - _leap) : 0f;
            var root = new Vector3(p.Root.x * plant, p.Root.y + leap, p.Root.z * plant);
            _root.localPosition = _rootPosition + new Vector3(0f, bob - crouch - 0.05f * f, 0f) + root;
            _root.localRotation = Quaternion.AngleAxis(-80f * f - 6f * fl, Vector3.right) * _rootRotation;

            // Legs: a run with folding knees and swinging thighs; the clip's stance (thigh pitch, knee bend) when planted,
            // the thighs turned back against the hips so the feet stay put; bent knees on guard.
            var stand = 1f - m;
            void Leg(string upper, string lower, string foot, float phase, Vector2 clipLeg)
            {
                var swing = Mathf.Sin(phase);
                var fold = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(phase)), 1.5f);
                float thigh = clipLeg.x * plant, knee = clipLeg.y * (_leap >= 0f ? 1f : plant);
                if (_leap >= 0f) thigh = clipLeg.x;
                Turn(upper, m * (-40f * swing - 15f * fold) - 12f * g * stand + thigh, -p.HipYaw * plant);
                Turn(lower, m * (15f + 85f * fold) + 22f * g * stand + knee + 30f * f);
                Turn(foot, m * (-10f + 25f * fold) - 10f * g * stand - 0.7f * (thigh + knee));
            }
            Leg("UpperLeg_L", "LowerLeg_L", "Foot_L", a, p.LegL);
            Leg("UpperLeg_R", "LowerLeg_R", "Foot_R", a + Mathf.PI, p.LegR);

            // The trunk leans into the run and twists against the legs; in an attack the hips lead, then the spine and
            // chest whip round after them, and the chest pitches into the blow.
            var twist = m * Mathf.Sin(a);
            Turn("Hips", 0f, 8f * twist + p.HipYaw);
            // A runner's forward lean (about 25° through spine and chest), kept while she swings or casts on the move;
            // the neck and head tip back so she still looks ahead.
            Turn("Spine", 16f * m + breath * 1.5f - 10f * fl + p.SpinePitch, -6f * twist - 10f * s + p.SpineYaw);
            Turn("Chest", 9f * m + breath * 2f - 6f * fl + p.ChestPitch, -8f * twist - 15f * s + p.ChestYaw, p.ChestRoll);
            // The head keeps its eyes on the foe: it turns back against the trunk's twist and lean.
            var trunkYaw = p.HipYaw + p.SpineYaw + p.ChestYaw;
            Turn("Neck", -8f * m + 4f * g - 0.4f * (p.ChestPitch + p.SpinePitch), -0.4f * trunkYaw);
            Turn("Head", -10f * m - 8f * fl + 4f * g - 0.3f * (p.ChestPitch + p.SpinePitch), -0.4f * trunkYaw, 3f * Mathf.Sin(t * 0.4f));
            // The braid trails as she runs and swings when she turns or leaps.
            Turn("Braid1", -20f * run - 8f * Mathf.Abs(Mathf.Sin(a)) * run - 30f * (_leap >= 0f ? 1f : 0f), 10f * twist - 0.3f * trunkYaw);
            Turn("Braid2", -10f * run - 6f * Mathf.Sin(a * 2f) * run, 8f * twist - 0.2f * trunkYaw);

            // The weapon arm (the Lion's Fang when worn): swings with the run and rises across the face on guard; in an
            // attack the clip aims the upper arm and forearm (in the chest's frame, so they ride the trunk's turn) and
            // turns the forearm so the blade faces where the clip wants it.
            Turn("Shoulder_R", -6f * g);
            Turn("UpperArm_R", -run * 45f * Mathf.Sin(a) - 70f * g + 10f * fl, -20f * g, -20f * g);
            Aim("UpperArm_R", p.UpperR, null, p.ArmR);
            Turn("LowerArm_R", -70f * run - 100f * g - 15f * stand * (1f - p.ArmR));
            Aim("LowerArm_R", p.ForeR, p.BladeR, p.ArmR);
            Turn("Hand_R", p.WristR);
            // A closed fist around a held weapon (the handle crosses the palm; tools/blender/prepare_artifact.py).
            _grip = Damp(_grip, Gripping ? 1f : 0f, 12f, _dt);
            if (_curlAxis is Vector3 curl && _bones.TryGetValue("Fingers_R", out var fingers))
                fingers.T.localRotation = Quaternion.AngleAxis(80f * _grip, curl) * fingers.RestRotation;

            // The spell hand: swings with the run; thrusts forward, palm out, to cast; across the body on guard; in an
            // attack it balances the blow (the clip aims it).
            Turn("Shoulder_L", -8f * s - 6f * g);
            Turn("UpperArm_L", run * 45f * Mathf.Sin(a) + 15f * k - 75f * s - 70f * g + 10f * fl, 0f, -12f * k + 20f * g);
            Aim("UpperArm_L", p.UpperL, null, p.ArmL);
            Turn("LowerArm_L", -50f * run + 55f * s - 25f * k - 100f * g);
            Aim("LowerArm_L", p.ForeL, null, p.ArmL);
            Turn("Hand_L", -35f * s + 6f * Mathf.Sin(t * 12f) * k);
        }

        /// <summary>
        /// Turns an arm bone so it points along <paramref name="dir"/> (her right, up, forward, in the chest's frame) and,
        /// for the forearm, so the held blade faces <paramref name="blade"/>; blended over the procedural pose by weight.
        /// </summary>
        void Aim(string bone, Vector3 dir, Vector3? blade, float weight)
        {
            if (weight <= 1e-3f || dir.sqrMagnitude < 1e-6f || !_bones.TryGetValue(bone, out var b) || !_along.TryGetValue(bone, out var along)) return;
            var parent = b.T.parent;
            var chest = _bones.TryGetValue("Chest", out var c) ? c.T.rotation * Quaternion.Inverse(_frame.rotation * _chestRest) : Quaternion.identity;
            var toWorld = chest * _frame.rotation;
            var want = toWorld * dir.normalized;
            var restWorld = parent.rotation * b.RestRotation;
            var target = Quaternion.FromToRotation(restWorld * along, want) * restWorld;
            if (blade is Vector3 hint && hint.sqrMagnitude > 1e-6f && _bladeAxis is Vector3 axis)
            {
                // Twist about the forearm so the blade (fixed in the forearm's frame, the wrist at rest) lies toward the hint.
                var have = target * axis;
                var wantBlade = toWorld * hint.normalized;
                have -= Vector3.Dot(have, want) * want;
                wantBlade -= Vector3.Dot(wantBlade, want) * want;
                if (have.sqrMagnitude > 1e-6f && wantBlade.sqrMagnitude > 1e-6f)
                    target = Quaternion.AngleAxis(Vector3.SignedAngle(have, wantBlade, want), want) * target;
            }
            b.T.localRotation = Quaternion.Slerp(b.T.localRotation, Quaternion.Inverse(parent.rotation) * target, Mathf.Clamp01(weight));
        }

        /// <summary>Eases a bone toward a pose: pitch about her right, yaw about up, roll about forward.</summary>
        void Turn(string bone, float pitch, float yaw = 0f, float roll = 0f)
        {
            if (!_bones.TryGetValue(bone, out var b)) return;
            var target = FrameTurn(b.T, pitch, yaw, roll) * b.RestRotation;
            b.T.localRotation = Quaternion.Slerp(b.T.localRotation, target, 1f - Mathf.Exp(-Follow * _dt));
        }

        // ---------------- Attack clips ----------------

        /// <summary>
        /// One keyframe of an attack, added over the procedural pose. Angles in degrees about her own axes (pitch +
        /// forward, yaw + to her right); the root offset in metres (right, up, forward); arm directions are her right, up,
        /// forward in the chest's frame, so they ride the trunk's turn. Legs are (thigh pitch, - forward; knee bend).
        /// </summary>
        struct Key
        {
            public float T;
            public Vector3 Root;
            public float HipYaw, SpineYaw, ChestYaw, SpinePitch, ChestPitch, ChestRoll;
            public Vector2 LegL, LegR;
            public float ArmR, ArmL;  // how much the clip aims each arm (0 leaves the procedural pose)
            public Vector3 UpperR, ForeR, BladeR, UpperL, ForeL;
            public float WristR;
            public float Trail, Aura;

            public static readonly Key Zero = default;

            static Vector3 Dir(Vector3 a, Vector3 b, float f) =>
                a == Vector3.zero ? b : b == Vector3.zero ? a : Vector3.Slerp(a.normalized, b.normalized, f);

            public static Key Lerp(Key a, Key b, float f) => new Key
            {
                T = Mathf.Lerp(a.T, b.T, f),
                Root = Vector3.Lerp(a.Root, b.Root, f),
                HipYaw = Mathf.Lerp(a.HipYaw, b.HipYaw, f),
                SpineYaw = Mathf.Lerp(a.SpineYaw, b.SpineYaw, f),
                ChestYaw = Mathf.Lerp(a.ChestYaw, b.ChestYaw, f),
                SpinePitch = Mathf.Lerp(a.SpinePitch, b.SpinePitch, f),
                ChestPitch = Mathf.Lerp(a.ChestPitch, b.ChestPitch, f),
                ChestRoll = Mathf.Lerp(a.ChestRoll, b.ChestRoll, f),
                LegL = Vector2.Lerp(a.LegL, b.LegL, f),
                LegR = Vector2.Lerp(a.LegR, b.LegR, f),
                ArmR = Mathf.Lerp(a.ArmR, b.ArmR, f),
                ArmL = Mathf.Lerp(a.ArmL, b.ArmL, f),
                UpperR = Dir(a.UpperR, b.UpperR, f),
                ForeR = Dir(a.ForeR, b.ForeR, f),
                BladeR = Dir(a.BladeR, b.BladeR, f),
                UpperL = Dir(a.UpperL, b.UpperL, f),
                ForeL = Dir(a.ForeL, b.ForeL, f),
                WristR = Mathf.Lerp(a.WristR, b.WristR, f),
                Trail = Mathf.Lerp(a.Trail, b.Trail, f),
                Aura = Mathf.Lerp(a.Aura, b.Aura, f),
            };
        }

        /// <summary>The clip's pose at time t: eased between the keys on either side.</summary>
        static Key Sample(Key[] keys, float t)
        {
            if (t <= keys[0].T) return keys[0];
            for (var i = 1; i < keys.Length; i++)
            {
                if (t > keys[i].T) continue;
                var f = (t - keys[i - 1].T) / Mathf.Max(1e-4f, keys[i].T - keys[i - 1].T);
                return Key.Lerp(keys[i - 1], keys[i], Smooth(f));
            }
            return keys[keys.Length - 1];
        }

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        // A guarding left hand, low and forward; drawn back behind her when the right arm cuts across.
        static readonly Vector3 GuardL = V(-0.25f, -0.55f, 0.8f), GuardLFore = V(-0.15f, -0.1f, 1f);
        static readonly Vector3 BackL = V(-0.35f, -0.7f, -0.6f), BackLFore = V(-0.25f, -0.9f, -0.35f);

        static readonly Dictionary<Action, Key[]> Clips = new Dictionary<Action, Key[]>
        {
            // Auto-attack: a quick horizontal forehand. The hips turn first, the chest whips round, the left foot steps in,
            // and the blade sweeps from her right across her front, its tip leading.
            [Action.Slash] = new[]
            {
                new Key { T = 0f },
                new Key { T = 0.07f, Root = V(0f, -0.03f, 0f), HipYaw = 15f, SpineYaw = 10f, ChestYaw = 20f, ChestPitch = 4f,
                    LegL = new Vector2(-10f, 15f), LegR = new Vector2(10f, 20f),
                    ArmR = 1f, UpperR = V(0.85f, 0.05f, -0.2f), ForeR = V(0.75f, 0.3f, 0.3f), BladeR = V(-0.2f, 0.2f, 1f),
                    ArmL = 1f, UpperL = GuardL, ForeL = GuardLFore },
                new Key { T = 0.16f, Root = V(0f, -0.06f, 0.1f), HipYaw = -15f, SpineYaw = -10f, ChestYaw = -20f, ChestPitch = 12f,
                    LegL = new Vector2(-30f, 35f), LegR = new Vector2(20f, 25f),
                    ArmR = 1f, UpperR = V(-0.15f, -0.05f, 1f), ForeR = V(-0.55f, -0.05f, 0.8f), BladeR = V(-0.9f, 0f, -0.3f),
                    ArmL = 1f, UpperL = BackL, ForeL = BackLFore, Trail = 1f },
                new Key { T = 0.27f, Root = V(0f, -0.06f, 0.1f), HipYaw = -22f, SpineYaw = -12f, ChestYaw = -25f, ChestPitch = 12f,
                    LegL = new Vector2(-30f, 35f), LegR = new Vector2(20f, 25f),
                    ArmR = 1f, UpperR = V(-0.6f, -0.25f, 0.7f), ForeR = V(-0.85f, -0.3f, 0.3f), BladeR = V(-0.2f, 0f, -1f),
                    ArmL = 1f, UpperL = BackL, ForeL = BackLFore, Trail = 0.4f },
                new Key { T = 0.5f },
            },

            // A dash-in: low and leaning, the blade drawn back by her right hip, ready to strike on arrival.
            [Action.Dash] = new[]
            {
                new Key { T = 0f },
                new Key { T = 0.1f, SpinePitch = 12f, ChestPitch = 18f, HipYaw = 10f, ChestYaw = 15f,
                    ArmR = 1f, UpperR = V(0.45f, -0.55f, -0.7f), ForeR = V(0.4f, -0.35f, -0.85f), BladeR = V(0f, 1f, 0f),
                    ArmL = 1f, UpperL = V(-0.35f, -0.35f, 0.85f), ForeL = V(-0.2f, -0.1f, 1f) },
                new Key { T = 0.55f, SpinePitch = 12f, ChestPitch = 18f, HipYaw = 10f, ChestYaw = 15f,
                    ArmR = 1f, UpperR = V(0.45f, -0.55f, -0.7f), ForeR = V(0.4f, -0.35f, -0.85f), BladeR = V(0f, 1f, 0f),
                    ArmL = 1f, UpperL = V(-0.35f, -0.35f, 0.85f), ForeL = V(-0.2f, -0.1f, 1f) },
                new Key { T = 0.7f },
            },

            // Double Strike: a downward forehand cut from high on her right to low on her left (the first hit, at once), the
            // hips reverse, and a rising backhand cut back up to her right as the right foot steps in (the second hit,
            // 0.25 s later, CombatSim.DoubleStrikeInterval).
            [Action.Double] = new[]
            {
                new Key { T = 0f },
                new Key { T = 0.03f, Root = V(0f, -0.03f, 0f), HipYaw = 18f, SpineYaw = 10f, ChestYaw = 22f, ChestPitch = -4f,
                    LegL = new Vector2(-15f, 20f), LegR = new Vector2(10f, 20f),
                    ArmR = 1f, UpperR = V(0.55f, 0.75f, 0.1f), ForeR = V(0.3f, 0.9f, 0.3f), BladeR = V(0f, 0f, 1f),
                    ArmL = 1f, UpperL = GuardL, ForeL = GuardLFore },
                new Key { T = 0.11f, Root = V(0f, -0.08f, 0.12f), HipYaw = -18f, SpineYaw = -14f, ChestYaw = -24f, ChestPitch = 20f,
                    LegL = new Vector2(-35f, 45f), LegR = new Vector2(20f, 30f),
                    ArmR = 1f, UpperR = V(-0.35f, -0.45f, 0.8f), ForeR = V(-0.6f, -0.65f, 0.45f), BladeR = V(0f, 0f, 1f),
                    ArmL = 1f, UpperL = BackL, ForeL = BackLFore, Trail = 1f },
                new Key { T = 0.2f, Root = V(0f, -0.08f, 0.14f), HipYaw = -24f, SpineYaw = -14f, ChestYaw = -28f, ChestPitch = 16f,
                    LegL = new Vector2(-30f, 40f), LegR = new Vector2(15f, 30f),
                    ArmR = 1f, UpperR = V(-0.6f, -0.5f, 0.6f), ForeR = V(-0.85f, -0.35f, 0.35f), BladeR = V(0f, 0.2f, 1f),
                    ArmL = 1f, UpperL = BackL, ForeL = BackLFore, Trail = 0.2f },
                new Key { T = 0.33f, Root = V(0f, -0.05f, 0.22f), HipYaw = 18f, SpineYaw = 14f, ChestYaw = 28f, ChestPitch = 6f,
                    LegL = new Vector2(15f, 25f), LegR = new Vector2(-28f, 38f),
                    ArmR = 1f, UpperR = V(0.8f, 0.25f, 0.55f), ForeR = V(0.85f, 0.45f, 0.25f), BladeR = V(0f, 0f, 1f),
                    ArmL = 1f, UpperL = V(-0.45f, -0.75f, 0.45f), ForeL = V(-0.1f, -0.25f, 0.95f), Trail = 1f },
                new Key { T = 0.45f, Root = V(0f, -0.04f, 0.22f), HipYaw = 24f, SpineYaw = 14f, ChestYaw = 32f, ChestPitch = 2f,
                    LegL = new Vector2(15f, 25f), LegR = new Vector2(-28f, 38f),
                    ArmR = 1f, UpperR = V(0.8f, 0.5f, 0.2f), ForeR = V(0.55f, 0.8f, 0f), BladeR = V(0f, 0f, 1f),
                    ArmL = 1f, UpperL = V(-0.45f, -0.75f, 0.45f), ForeL = V(-0.1f, -0.25f, 0.95f), Trail = 0.3f },
                new Key { T = 0.75f },
            },

            // Lunge: a crouch, a leap (CombatSim.LeapProgress lifts her; the legs tuck), the blade raised high behind her
            // head, and a downward cut as she lands in a deep, wide crouch (the hit lands with her, 0.45 s in).
            [Action.Lunge] = new[]
            {
                new Key { T = 0f },
                new Key { T = 0.05f, Root = V(0f, -0.12f, 0f), SpinePitch = 10f, ChestPitch = 12f,
                    LegL = new Vector2(-25f, 55f), LegR = new Vector2(-25f, 55f),
                    ArmR = 1f, UpperR = V(0.3f, -0.6f, -0.7f), ForeR = V(0.3f, -0.7f, -0.6f), BladeR = V(0f, 1f, 0f),
                    ArmL = 1f, UpperL = V(-0.3f, -0.6f, -0.7f), ForeL = V(-0.3f, -0.7f, -0.6f) },
                new Key { T = 0.18f, SpinePitch = -6f, ChestPitch = -14f,
                    LegL = new Vector2(-50f, 85f), LegR = new Vector2(-35f, 95f),
                    ArmR = 1f, UpperR = V(0.25f, 0.95f, 0.05f), ForeR = V(0.15f, 0.7f, -0.7f), BladeR = V(0f, 0.6f, 0.8f),
                    ArmL = 1f, UpperL = V(-0.7f, 0.4f, 0.4f), ForeL = V(-0.7f, 0.5f, 0.5f) },
                new Key { T = 0.33f, SpinePitch = -8f, ChestPitch = -18f,
                    LegL = new Vector2(-55f, 95f), LegR = new Vector2(-40f, 100f),
                    ArmR = 1f, UpperR = V(0.2f, 1f, -0.1f), ForeR = V(0.1f, 0.55f, -0.85f), BladeR = V(0f, 0.85f, 0.5f),
                    ArmL = 1f, UpperL = V(-0.75f, 0.35f, 0.4f), ForeL = V(-0.7f, 0.5f, 0.5f) },
                new Key { T = 0.4f, SpinePitch = 6f, ChestPitch = 16f,
                    LegL = new Vector2(-50f, 80f), LegR = new Vector2(-20f, 80f),
                    ArmR = 1f, UpperR = V(0.15f, 0.55f, 0.8f), ForeR = V(0.08f, 0.15f, 1f), BladeR = V(0f, 1f, 0.2f),
                    ArmL = 1f, UpperL = V(-0.7f, 0f, -0.5f), ForeL = V(-0.7f, -0.2f, -0.5f), Trail = 1f },
                new Key { T = 0.46f, Root = V(0f, -0.3f, 0.1f), SpinePitch = 18f, ChestPitch = 32f,
                    LegL = new Vector2(-55f, 100f), LegR = new Vector2(30f, 95f),
                    ArmR = 1f, UpperR = V(0.15f, -0.55f, 0.8f), ForeR = V(0.08f, -0.8f, 0.6f), BladeR = V(0f, 0.5f, 0.9f),
                    ArmL = 1f, UpperL = V(-0.6f, -0.3f, -0.7f), ForeL = V(-0.5f, -0.5f, -0.7f), Trail = 1f },
                new Key { T = 0.62f, Root = V(0f, -0.28f, 0.1f), SpinePitch = 16f, ChestPitch = 28f,
                    LegL = new Vector2(-55f, 100f), LegR = new Vector2(30f, 95f),
                    ArmR = 1f, UpperR = V(0.15f, -0.6f, 0.75f), ForeR = V(0.08f, -0.85f, 0.5f), BladeR = V(0f, 0.5f, 0.9f),
                    ArmL = 1f, UpperL = V(-0.6f, -0.3f, -0.7f), ForeL = V(-0.5f, -0.5f, -0.7f) },
                new Key { T = 0.9f },
            },

            // Overpower: she gathers herself (the aura rises) with the blade cocked high over her right shoulder and the left
            // hand pointing at the foe, then cuts diagonally down across to her lower left, stepping in hard (the blow lands
            // 0.3 s in, CombatSim.OverpowerWindup), and holds the follow-through while the trail and aura fade.
            [Action.Overpower] = new[]
            {
                new Key { T = 0f },
                new Key { T = 0.12f, Root = V(0f, -0.07f, -0.04f), HipYaw = 25f, SpineYaw = 10f, ChestYaw = 30f, ChestPitch = -8f,
                    LegL = new Vector2(-25f, 30f), LegR = new Vector2(20f, 30f),
                    ArmR = 1f, UpperR = V(0.55f, 0.8f, -0.25f), ForeR = V(0.3f, 0.85f, -0.45f), BladeR = V(0f, 0.4f, 0.9f),
                    ArmL = 1f, UpperL = V(-0.25f, -0.15f, 0.95f), ForeL = V(-0.15f, 0.05f, 1f), Aura = 1f },
                new Key { T = 0.26f, Root = V(0f, -0.1f, -0.06f), HipYaw = 35f, SpineYaw = 12f, ChestYaw = 40f, ChestPitch = -12f,
                    LegL = new Vector2(-28f, 35f), LegR = new Vector2(22f, 35f),
                    ArmR = 1f, UpperR = V(0.6f, 0.8f, -0.35f), ForeR = V(0.45f, 0.75f, -0.5f), BladeR = V(0f, 0.4f, 0.9f),
                    ArmL = 1f, UpperL = V(-0.25f, -0.1f, 0.95f), ForeL = V(-0.15f, 0.1f, 1f), Aura = 1f },
                new Key { T = 0.37f, Root = V(0f, -0.18f, 0.3f), HipYaw = -30f, SpineYaw = -15f, ChestYaw = -35f, ChestPitch = 30f,
                    LegL = new Vector2(-48f, 62f), LegR = new Vector2(35f, 40f),
                    ArmR = 1f, UpperR = V(-0.45f, -0.55f, 0.7f), ForeR = V(-0.7f, -0.6f, 0.35f), BladeR = V(0f, 0.3f, 1f),
                    ArmL = 1f, UpperL = V(0.1f, -0.6f, -0.8f), ForeL = V(0.1f, -0.8f, -0.6f), Trail = 1f, Aura = 1f },
                new Key { T = 0.65f, Root = V(0f, -0.17f, 0.3f), HipYaw = -32f, SpineYaw = -15f, ChestYaw = -37f, ChestPitch = 28f,
                    LegL = new Vector2(-48f, 62f), LegR = new Vector2(35f, 40f),
                    ArmR = 1f, UpperR = V(-0.5f, -0.55f, 0.65f), ForeR = V(-0.72f, -0.6f, 0.3f), BladeR = V(0f, 0.3f, 1f),
                    ArmL = 1f, UpperL = V(0.1f, -0.6f, -0.8f), ForeL = V(0.1f, -0.8f, -0.6f), Aura = 0.5f },
                new Key { T = 1.1f },
            },
        };

        static float Damp(float current, float target, float rate, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
