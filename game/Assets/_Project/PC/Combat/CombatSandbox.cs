using System.Collections.Generic;
using System.Linq;
using TheArtifacts.Core.Combat;
using TheArtifacts.Core.Guardian;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInput = TheArtifacts.Core.Combat.PlayerInput;

namespace TheArtifacts.PC.Combat
{
    public enum TestBuild { Starter, Geared }

    /// <summary>
    /// Graybox PC fight: drives a Core <see cref="CombatSim"/> at a fixed step, builds primitive visuals,
    /// follows the player with a lock-on camera, and draws a minimal HUD (tells, HP, cooldowns, outcome).
    /// The Guardian setup is fixed for the fight; no model is called here.
    /// </summary>
    public sealed class CombatSandbox : MonoBehaviour
    {
        [Header("Fight")]
        [SerializeField] TestBuild build = TestBuild.Starter;
        [SerializeField] int seed = 1;
        [Tooltip("On: the scripted default Guardian (Combat Spec 4.4). Off: the custom setup below.")]
        [SerializeField] bool useDefaultGuardian = true;
        [SerializeField] string customTier = "T2";
        [SerializeField] string[] customSkills = { "cleave", "slam", "charge", "embers" };
        [SerializeField] string customStrategy = "aggressive";

        [Header("Camera")]
        [SerializeField] float cameraDistance = 8f;
        [SerializeField] float cameraHeight = 5f;

        const float SimStep = 1f / 60f;
        const float TelegraphY = 0.02f;

        static readonly Color PlayerColor = new Color(0.25f, 0.55f, 0.95f);
        static readonly Color GuardianIdle = new Color(0.45f, 0.12f, 0.12f);
        static readonly Color GuardianTell = new Color(1f, 0.55f, 0.1f);
        static readonly Color GuardianRecovery = new Color(0.3f, 0.9f, 0.35f);
        static readonly Color GuardianFury = new Color(0.75f, 0.15f, 0.6f);
        static readonly Color TelegraphStart = new Color(0.55f, 0.25f, 0.05f);
        static readonly Color TelegraphHit = new Color(1f, 0.1f, 0.05f);

        CombatSim _sim;
        float _accumulator;
        Ability _pendingAbility;
        Vec2? _moveTarget;
        int _eventCursor;
        readonly List<string> _log = new List<string>();

        Camera _camera;
        Vector3 _cameraBack = Vector3.back;
        Transform _player, _guardian, _moveMarker;
        Material _guardianMaterial, _telegraphTemplate;
        readonly Dictionary<Telegraph, (GameObject go, Material mat)> _telegraphViews =
            new Dictionary<Telegraph, (GameObject, Material)>();

        PlayerBuild CurrentBuild => build == TestBuild.Geared ? PrototypeContent.GearedBuild : PrototypeContent.StarterBuild;

        GuardianSetup CurrentSetup => useDefaultGuardian
            ? PrototypeContent.DefaultSetup
            : new GuardianSetup(customTier, customSkills, customStrategy);

        void Start()
        {
            BuildWorld();
            Restart();
        }

        void Restart()
        {
            foreach (var view in _telegraphViews.Values) Destroy(view.go);
            _telegraphViews.Clear();
            _log.Clear();
            _eventCursor = 0;
            _accumulator = 0f;
            _pendingAbility = Ability.None;
            _moveTarget = null;
            _sim = new CombatSim(CurrentBuild, CurrentSetup, seed);
            SyncViews();
        }

        // ---------------- Loop ----------------

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (_sim.Outcome != CombatOutcome.Ongoing)
            {
                if (kb.rKey.wasPressedThisFrame) Restart();
                if (kb.bKey.wasPressedThisFrame)
                {
                    build = build == TestBuild.Starter ? TestBuild.Geared : TestBuild.Starter;
                    Restart();
                }
                return;
            }

            if (_pendingAbility == Ability.None)
            {
                if (kb.digit1Key.wasPressedThisFrame) _pendingAbility = Ability.HeavyStrike;
                else if (kb.digit2Key.wasPressedThisFrame) _pendingAbility = Ability.Guard;
                else if (kb.digit3Key.wasPressedThisFrame) _pendingAbility = Ability.Lunge;
                else if (kb.digit4Key.wasPressedThisFrame) _pendingAbility = Ability.Punish;
            }

            ReadMoveTarget();
            _accumulator = Mathf.Min(_accumulator + Time.deltaTime, 0.25f);
            while (_accumulator >= SimStep && _sim.Outcome == CombatOutcome.Ongoing)
            {
                _sim.Step(SimStep, new PlayerInput(MoveTowardTarget(), _pendingAbility));
                _pendingAbility = Ability.None;
                _accumulator -= SimStep;
            }

            SyncViews();
            CollectEvents();
        }

        /// <summary>Right-click (or hold) sets the move target on the arena floor. QWE/ASD/XC are equipment-skill keys.</summary>
        void ReadMoveTarget()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed) return;

            var ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var enter))
            {
                var hit = ray.GetPoint(enter);
                _moveTarget = new Vec2(hit.x, hit.z);
            }
        }

        /// <summary>Unit direction to the target, shortened on the last step so the player stops on it.</summary>
        Vec2 MoveTowardTarget()
        {
            if (_moveTarget is not Vec2 target) return Vec2.Zero;
            var delta = target - _sim.PlayerPosition;
            var step = PlayerRules.RunSpeed * SimStep;
            if (delta.Length <= step)
            {
                _moveTarget = null;
                return delta * (1f / step);
            }
            return delta.Normalized();
        }

        void LateUpdate()
        {
            if (_camera == null || _sim == null) return;

            var away = _player.position - _guardian.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.25f) _cameraBack = Vector3.Slerp(_cameraBack, away.normalized, 1f - Mathf.Exp(-6f * Time.deltaTime));

            var target = _player.position + _cameraBack * cameraDistance + Vector3.up * cameraHeight;
            _camera.transform.position = Vector3.Lerp(_camera.transform.position, target, 1f - Mathf.Exp(-10f * Time.deltaTime));
            var lookAt = Vector3.Lerp(_player.position, _guardian.position, 0.5f) + Vector3.up;
            _camera.transform.rotation = Quaternion.LookRotation(lookAt - _camera.transform.position);
        }

        // ---------------- Visuals ----------------

        void BuildWorld()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                _camera = new GameObject("Main Camera").AddComponent<Camera>();
                _camera.tag = "MainCamera";
            }

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");

            var floor = Primitive(PrimitiveType.Cylinder, "Arena", Lit(lit, new Color(0.35f, 0.35f, 0.38f)));
            var r = GuardianRules.ArenaRadius;
            floor.localScale = new Vector3(r * 2f, 0.05f, r * 2f);
            floor.position = new Vector3(0f, -0.05f, 0f);

            _player = Primitive(PrimitiveType.Capsule, "Player", Lit(lit, PlayerColor));
            _guardianMaterial = Lit(lit, GuardianIdle);
            _guardian = Primitive(PrimitiveType.Capsule, "Guardian", _guardianMaterial);
            _guardian.localScale = Vector3.one * 2f;

            // A nose so the Guardian's facing is readable.
            var nose = Primitive(PrimitiveType.Cube, "Facing", Lit(lit, Color.black));
            nose.SetParent(_guardian, false);
            nose.localPosition = new Vector3(0f, 0.3f, 0.5f);
            nose.localScale = new Vector3(0.3f, 0.2f, 0.4f);

            _moveMarker = Primitive(PrimitiveType.Cylinder, "Move Target", Lit(lit, PlayerColor));
            _moveMarker.localScale = new Vector3(0.5f, 0.01f, 0.5f);

            _telegraphTemplate = new Material(unlit != null ? unlit : lit);
        }

        static Material Lit(Shader shader, Color color)
        {
            var m = new Material(shader);
            m.SetColor("_BaseColor", color);
            return m;
        }

        Transform Primitive(PrimitiveType type, string name, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(transform, false);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        void SyncViews()
        {
            _player.position = TelegraphMeshes.ToWorld(_sim.PlayerPosition, 1f);
            _guardian.position = TelegraphMeshes.ToWorld(_sim.GuardianPosition, 2f);
            _moveMarker.gameObject.SetActive(_moveTarget.HasValue);
            if (_moveTarget is Vec2 target) _moveMarker.position = TelegraphMeshes.ToWorld(target, TelegraphY);

            var f = _sim.GuardianFacing;
            if (f.LengthSquared > 1e-6f) _guardian.rotation = Quaternion.LookRotation(new Vector3(f.X, 0f, f.Y));

            var color = _sim.Phase switch
            {
                GuardianPhase.Tell => GuardianTell,
                GuardianPhase.Recovery => GuardianRecovery,
                _ => _sim.IsFuryActive ? GuardianFury : GuardianIdle,
            };
            _guardianMaterial.SetColor("_BaseColor", color);

            var active = new HashSet<Telegraph>(_sim.ActiveTelegraphs.Where(t => t.Area != null));
            foreach (var gone in _telegraphViews.Keys.Where(t => !active.Contains(t)).ToArray())
            {
                Destroy(_telegraphViews[gone].go);
                _telegraphViews.Remove(gone);
            }

            foreach (var t in active)
            {
                if (!_telegraphViews.TryGetValue(t, out var view))
                {
                    var go = new GameObject($"Telegraph {t.Skill.Id}");
                    go.transform.SetParent(transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = TelegraphMeshes.Build(t.Area, TelegraphY + 0.005f * (_telegraphViews.Count % 4));
                    var mat = new Material(_telegraphTemplate);
                    go.AddComponent<MeshRenderer>().sharedMaterials = new[] { mat, mat };
                    view = (go, mat);
                    _telegraphViews[t] = view;
                }

                var placed = t.HitTime - t.Skill.Tell;
                var progress = Mathf.Clamp01((_sim.Time - placed) / t.Skill.Tell);
                view.mat.SetColor("_BaseColor", Color.Lerp(TelegraphStart, TelegraphHit, progress * progress));
            }
        }

        // ---------------- HUD ----------------

        void CollectEvents()
        {
            var events = _sim.Events;
            for (; _eventCursor < events.Count; _eventCursor++)
            {
                var e = events[_eventCursor];
                string line = e.Type switch
                {
                    CombatEventType.TellStarted => $"Guardian readies {Name(e.Id)}",
                    CombatEventType.SkillHit => $"{Name(e.Id)} hits you",
                    CombatEventType.SkillMissed => $"You avoid {Name(e.Id)}",
                    CombatEventType.BuffStarted => "Guardian is furious",
                    CombatEventType.AbilityRejected => $"Can't: {e.Id.Replace(":", " - ")}",
                    CombatEventType.GuardianDamaged when e.Id != "auto" => $"{e.Id.Replace('_', ' ')} {e.Amount:0}",
                    CombatEventType.Win => "Guardian defeated",
                    CombatEventType.Loss => "You fell",
                    _ => null,
                };
                if (line == null) continue;
                _log.Add($"{e.Time,5:0.0}  {line}");
                if (_log.Count > 7) _log.RemoveAt(0);
            }
        }

        static string Name(string skillId) => PrototypeContent.Skill(skillId)?.Name ?? skillId;

        GUIStyle _label, _center, _big;

        void OnGUI()
        {
            if (_sim == null) return;
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            _center ??= new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _big ??= new GUIStyle(_center) { fontSize = 40 };

            float w = Screen.width, h = Screen.height;

            // Guardian HP and cast bar
            var top = new Rect(w * 0.5f - 250f, 20f, 500f, 22f);
            Bar(top, _sim.GuardianHp / _sim.GuardianMaxHp, new Color(0.8f, 0.15f, 0.15f));
            GUI.Label(top, $"Guardian {_sim.Tier.Id}   {_sim.GuardianHp:0} / {_sim.GuardianMaxHp:0}", _center);

            var cast = new Rect(w * 0.5f - 160f, 48f, 320f, 20f);
            if (_sim.Phase == GuardianPhase.Tell && _sim.ActiveTelegraphs.Count > 0)
            {
                var next = _sim.ActiveTelegraphs.Min(t => t.HitTime) - _sim.Time;
                var tell = _sim.CurrentSkill.Tell;
                Bar(cast, 1f - Mathf.Clamp01(next / tell), GuardianTell);
                GUI.Label(cast, $"{_sim.CurrentSkill.Name}  {next:0.0}s", _center);
            }
            else if (_sim.Phase == GuardianPhase.Recovery)
            {
                Bar(cast, 1f, GuardianRecovery);
                GUI.Label(cast, "OPENING  -  Punish x2", _center);
            }

            // Player HP and hotbar
            var hp = new Rect(w * 0.5f - 200f, h - 110f, 400f, 22f);
            Bar(hp, _sim.PlayerHp / _sim.PlayerMaxHp, new Color(0.2f, 0.75f, 0.3f));
            GUI.Label(hp, $"{_sim.PlayerHp:0} / {_sim.PlayerMaxHp:0}" + (_sim.GuardRemaining > 0f ? "   GUARD" : ""), _center);

            var abilities = new[]
            {
                (Ability.HeavyStrike, "1 Heavy"), (Ability.Guard, "2 Guard"), (Ability.Lunge, "3 Lunge"), (Ability.Punish, "4 Punish"),
            };
            for (var i = 0; i < abilities.Length; i++)
            {
                var (ability, label) = abilities[i];
                var slot = new Rect(w * 0.5f - 200f + i * 102f, h - 80f, 94f, 50f);
                var cd = _sim.CooldownRemaining(ability);
                var blocked = _sim.BlockReason(ability);
                Bar(slot, 1f, blocked == null ? new Color(0.2f, 0.25f, 0.35f) : new Color(0.12f, 0.12f, 0.14f));
                GUI.Label(slot, cd > 0f ? $"{label}\n{cd:0.0}" : blocked == "range" ? $"{label}\nrange" : label, _center);
            }
            if (_sim.GcdRemaining > 0f)
                Bar(new Rect(w * 0.5f - 200f, h - 28f, 400f * _sim.GcdRemaining / PlayerRules.GlobalCooldown, 4f), 1f, Color.white);

            // Info and log
            GUI.Label(new Rect(16f, 16f, 420f, 90f),
                $"{build} (gear {_sim.Build.GearScore})   {_sim.Time:0.0}s\n" +
                $"Guardian: {_sim.Setup}\n" +
                "Right-click move   1 Heavy  2 Guard  3 Lunge  4 Punish", _label);
            GUI.Label(new Rect(16f, h - 190f, 360f, 170f), string.Join("\n", _log), _label);

            if (_sim.Outcome != CombatOutcome.Ongoing)
            {
                Bar(new Rect(0f, h * 0.35f, w, 150f), 1f, new Color(0f, 0f, 0f, 0.7f));
                GUI.Label(new Rect(0f, h * 0.35f, w, 90f), _sim.Outcome == CombatOutcome.Win ? "VICTORY" : "DEFEAT", _big);
                GUI.Label(new Rect(0f, h * 0.35f + 90f, w, 40f),
                    $"{_sim.Time:0.0}s    R retry    B switch build (now {build})", _center);
            }
        }

        static void Bar(Rect r, float fill, Color color)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill), r.height), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
