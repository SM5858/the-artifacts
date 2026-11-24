using System.Collections.Generic;
using System.Linq;
using TheArtifacts.Core.Account;
using TheArtifacts.Core.Combat;
using TheArtifacts.Core.Guardian;
using TheArtifacts.PC.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInput = TheArtifacts.Core.Combat.PlayerInput;

namespace TheArtifacts.PC.Combat
{
    public enum TestBuild { Starter, Geared }

    /// <summary>
    /// Who picks the Guardian's setup: the scripted default (Combat Spec 4.4), the custom fields below, or the
    /// adaptive Guardian on the server (Configuration Document 2), which falls back to the default without it.
    /// </summary>
    public enum GuardianMode { Default, Custom, Adaptive }

    /// <summary>
    /// Graybox PC fight: drives a Core <see cref="CombatSim"/> at a fixed step, builds primitive visuals,
    /// follows the player with a lock-on camera, and draws a minimal HUD (tells, HP, cooldowns, outcome).
    /// Before each attempt the Guardian's setup comes from <see cref="GuardianMode"/>; in Adaptive mode the server is
    /// asked once, and the fight waits for it at most <see cref="GuardianClient.ClientTimeout"/>.
    /// </summary>
    public sealed class CombatSandbox : MonoBehaviour
    {
        [Header("Game")]
        [Tooltip("On: the game loop (sign in, quest list, equipment; the account's build and unlocks). " +
                 "Off: test fights with the build and Guardian below.")]
        [SerializeField] bool accountLoop = true;

        [Header("Fight")]
        [SerializeField] TestBuild build = TestBuild.Starter;
        [SerializeField] int seed = 1;
        [Tooltip("alma_mater (Undead lich), columbia_lion (Beast), or book_keeper (Construct golem).")]
        [SerializeField] string guardianId = "columbia_lion";
        [Tooltip("Default: the scripted Guardian (Combat Spec 4.4). Custom: the setup below. Adaptive: ask the Guardian server " +
                 "(falls back to Default when it is not set up). M on the result screen cycles the mode.")]
        [SerializeField] GuardianMode guardianMode = GuardianMode.Adaptive;
        [SerializeField] string customTier = "T2";
        [SerializeField] string[] customSkills = { "cleave", "slam", "charge", "embers" };
        [SerializeField] string customStrategy = "aggressive";

        [Header("Art")]
        [Tooltip("CC0 materials from The Artifacts → Build Sandbox Materials. Empty: flat graybox colors.")]
        [SerializeField] SandboxArt art;

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
        static readonly Color EyeColor = new Color(0.35f, 1f, 0.9f);
        // The lich's material slots (concept: blue-grey gown, verdigris mantle, bone, gilded bronze).
        static readonly Color RobeTint = new Color(0.62f, 0.72f, 0.95f);
        static readonly Color HairTint = new Color(0.6f, 0.65f, 0.6f);
        static readonly Color BoneColor = new Color(0.78f, 0.74f, 0.64f);
        static readonly Color BronzeColor = new Color(0.72f, 0.56f, 0.3f);
        // The Book Keeper's slots (concept: cream marble, leather books, page edges, pale blue ribbons).
        static readonly Color MarbleColor = new Color(0.86f, 0.81f, 0.7f);
        static readonly Color LeatherColor = new Color(0.45f, 0.28f, 0.16f);
        static readonly Color PagesColor = new Color(0.92f, 0.86f, 0.72f);
        static readonly Color RibbonColor = new Color(0.62f, 0.76f, 0.8f);
        static readonly Color TelegraphStart = new Color(0.55f, 0.25f, 0.05f);
        static readonly Color TelegraphHit = new Color(1f, 0.1f, 0.05f);

        CombatSim _sim;
        /// <summary>Test fights (account loop off) record into this throwaway profile, with every dungeon open.</summary>
        PlayerProfile _testProfile;
        PcFrontEnd _frontEnd;
        /// <summary>The quest list or another menu is open; no fight is running.</summary>
        bool _inMenu;
        /// <summary>The current attempt's setup and where it came from; null while the server is being asked.</summary>
        FightSetup _fight;
        /// <summary>Bumped on every restart so a late answer for an older attempt is ignored.</summary>
        int _attemptToken;
        bool _outcomeRecorded;
        /// <summary>The Guardian's opening line stays on screen until this time.</summary>
        float _announcementUntil;
        const float AnnouncementSeconds = 4f;
        float _accumulator;
        Ability _pendingAbility;
        SkillBarSlot _pendingArtifactSkill;
        bool _pendingAutoToggle;
        /// <summary>The player's skill bar order, kept across retries; reapplied when it still fits the build.</summary>
        string[] _preferredBar;
        /// <summary>The first slot clicked when rearranging the skill bar, or None.</summary>
        SkillBarSlot _swapFrom;
        /// <summary>Screen areas of the HUD (GUI coordinates); clicks there do not aim at the floor.</summary>
        readonly List<Rect> _hudRects = new List<Rect>();
        string _reward;
        Vec2? _moveTarget;
        /// <summary>
        /// Picked with left-click: a lore target (spells 5 and 6 go to it) or a weak point on the Guardian (every attack
        /// aims at it). Null is the Guardian's body.
        /// </summary>
        string _spellTarget;
        int _eventCursor;
        readonly List<string> _log = new List<string>();

        Camera _camera;
        Vector3 _cameraBack = Vector3.back;
        Transform _player, _guardian, _moveMarker;
        Material _playerMaterial;
        /// <summary>The player flashes red until this time after taking damage.</summary>
        float _hurtUntil;
        Material _guardianMaterial, _telegraphTemplate;
        /// <summary>The model's materials, one per slot kind; all of them glow with the tell.</summary>
        readonly List<Material> _guardianSlots = new List<Material>();
        readonly Dictionary<Telegraph, (GameObject go, Material mat)> _telegraphViews =
            new Dictionary<Telegraph, (GameObject, Material)>();
        readonly Dictionary<string, (GameObject go, Material mat)> _loreViews = new Dictionary<string, (GameObject, Material)>();
        Shader _lit;
        /// <summary>The weak point shown while exposed: the model's "Owl" child, or the capsule's sphere.</summary>
        Transform _weakPoint;
        Transform _nose, _owlSphere;
        GameObject _guardianModel;
        /// <summary>Procedural motion for the model; null for the capsule.</summary>
        GuardianMotion _motion;
        Material _weakPointMaterial;
        /// <summary>Textured materials show state through emission or a tint instead of a flat color.</summary>
        bool _guardianTextured, _loreTextured;

        static readonly Color LoreIdle = new Color(0.45f, 0.3f, 0.15f);
        static readonly Color LoreTargeted = new Color(1f, 0.85f, 0.3f);
        static readonly Color LoreSpent = new Color(0.08f, 0.06f, 0.05f);

        PlayerProfile Profile => accountLoop ? GameSession.Profile : _testProfile;

        PlayerBuild CurrentBuild => accountLoop ? Progress.Build(GameSession.Profile)
            : build == TestBuild.Geared ? PrototypeContent.GearedBuild : PrototypeContent.StarterBuild;

        void Start()
        {
            _testProfile = new PlayerProfile();
            foreach (var g in PrototypeContent.Guardians) Progress.CheckIn(_testProfile, g.Id, "sandbox", System.DateTime.UtcNow);
            _frontEnd = new PcFrontEnd(StartQuest);
            BuildWorld();
            if (accountLoop) _inMenu = true;
            else Restart();
        }

        /// <summary>The player picked a quest in the list: fight that landmark's Guardian with the account's build.</summary>
        void StartQuest(string id)
        {
            _inMenu = false;
            if (id != guardianId)
            {
                guardianId = id;
                _preferredBar = null;
                ApplyGuardianLook();
            }
            Restart();
        }

        void BackToQuests()
        {
            _attemptToken++;  // drop an answer still on its way for the attempt being left
            _inMenu = true;
            _frontEnd.ShowQuests();
        }

        async void Restart()
        {
            var token = ++_attemptToken;
            _fight = null;
            _announcementUntil = 0f;
            _outcomeRecorded = false;
            FightSetup fight;
            if (guardianMode == GuardianMode.Custom)
                fight = new FightSetup(new GuardianSetup(customTier, customSkills, customStrategy), SetupSource.Scripted, null, null, false, 0);
            else
                fight = await GameSession.Guardian.ConfigureAsync(new GuardianCall(guardianId, CurrentBuild.Artifacts.Select(a => a.Id),
                    Progress.FailedAttempts(Profile, guardianId), adaptive: guardianMode == GuardianMode.Adaptive));
            if (token != _attemptToken || this == null) return;  // restarted again, or the scene closed, while waiting

            _fight = fight;
            if (fight.Announcement != null) _announcementUntil = Time.time + AnnouncementSeconds;

            foreach (var view in _telegraphViews.Values) Destroy(view.go);
            _telegraphViews.Clear();
            _log.Clear();
            _eventCursor = 0;
            _accumulator = 0f;
            _pendingAbility = Ability.None;
            _pendingArtifactSkill = SkillBarSlot.None;
            _pendingAutoToggle = false;
            _swapFrom = SkillBarSlot.None;
            _reward = null;
            _moveTarget = null;
            _spellTarget = null;
            _sim = new CombatSim(CurrentBuild, fight.Setup, seed, PrototypeContent.GuardianById(guardianId));
            _motion?.Reset();
            if (_preferredBar != null)
            {
                try { _sim.SetArtifactSkillBar(_preferredBar); }
                catch (System.ArgumentException) { _preferredBar = null; }  // the build changed; use its default bar
            }
            BuildLoreViews();
            SyncViews();
        }

        // ---------------- Loop ----------------

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || _inMenu || _sim == null || _fight == null) return;

            if (_sim.Outcome != CombatOutcome.Ongoing)
            {
                if (!_outcomeRecorded)
                {
                    _outcomeRecorded = true;
                    RecordOutcome();
                }
                if (kb.rKey.wasPressedThisFrame) Restart();
                if (accountLoop && (kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
                {
                    BackToQuests();
                    return;
                }
                if (kb.mKey.wasPressedThisFrame)
                {
                    guardianMode = (GuardianMode)(((int)guardianMode + 1) % 3);
                    Restart();
                }
                if (!accountLoop && kb.bKey.wasPressedThisFrame)
                {
                    build = build == TestBuild.Starter ? TestBuild.Geared : TestBuild.Starter;
                    Restart();
                }
                return;
            }

            // Skills on Q W E R T F, auto-attack toggle on A, artifact skill bar on 1 2 3 4 C V (Combat Spec 3.2).
            if (_pendingAbility == Ability.None)
            {
                if (kb.qKey.wasPressedThisFrame) _pendingAbility = Ability.HeavyStrike;
                else if (kb.wKey.wasPressedThisFrame) _pendingAbility = Ability.Guard;
                else if (kb.eKey.wasPressedThisFrame) _pendingAbility = Ability.Lunge;
                else if (kb.rKey.wasPressedThisFrame) _pendingAbility = Ability.Overpower;
                else if (kb.tKey.wasPressedThisFrame) _pendingAbility = Ability.LightningBolt;
                else if (kb.fKey.wasPressedThisFrame) _pendingAbility = Ability.FireBolt;
            }
            if (kb.aKey.wasPressedThisFrame) _pendingAutoToggle = !_pendingAutoToggle;
            if (kb.escapeKey.wasPressedThisFrame) _spellTarget = null;
            if (_pendingArtifactSkill == SkillBarSlot.None) _pendingArtifactSkill = ReadSkillBarKey(kb);

            ReadMoveTarget();
            ReadSpellTarget();
            _accumulator = Mathf.Min(_accumulator + Time.deltaTime, 0.25f);
            while (_accumulator >= SimStep && _sim.Outcome == CombatOutcome.Ongoing)
            {
                _sim.Step(SimStep, new PlayerInput(MoveTowardTarget(), _pendingAbility, _pendingArtifactSkill, _spellTarget, _pendingAutoToggle));
                if (_spellTarget != null && _sim.IsLoreSpent(_spellTarget)) _spellTarget = null;
                _pendingAbility = Ability.None;
                _pendingArtifactSkill = SkillBarSlot.None;
                _pendingAutoToggle = false;
                _accumulator -= SimStep;
            }

            SyncViews();
            CollectEvents();
        }

        static readonly string[] SkillBarKeyNames = { "1", "2", "3", "4", "C", "V" };

        static SkillBarSlot ReadSkillBarKey(Keyboard kb)
        {
            if (kb.digit1Key.wasPressedThisFrame) return SkillBarSlot.Slot1;
            if (kb.digit2Key.wasPressedThisFrame) return SkillBarSlot.Slot2;
            if (kb.digit3Key.wasPressedThisFrame) return SkillBarSlot.Slot3;
            if (kb.digit4Key.wasPressedThisFrame) return SkillBarSlot.Slot4;
            if (kb.cKey.wasPressedThisFrame) return SkillBarSlot.Slot5;
            if (kb.vKey.wasPressedThisFrame) return SkillBarSlot.Slot6;
            return SkillBarSlot.None;
        }

        /// <summary>
        /// A win rolls this landmark's reward onto the profile (Combat Spec 5.1; the first win on a Guardian is the
        /// better roll); a loss counts toward easing. The account's profile is then saved.
        /// </summary>
        void RecordOutcome()
        {
            var profile = Profile;
            if (_sim.Outcome == CombatOutcome.Win)
            {
                var artifact = Progress.RecordWin(profile, guardianId, new System.Random(seed + Progress.Record(profile, guardianId).Wins));
                _reward = $"{artifact.Name} ({artifact.Rarity})  -  {artifact.Description}";
            }
            else Progress.RecordLoss(profile, guardianId);
            if (accountLoop) _ = GameSession.SaveAsync();
        }

        /// <summary>
        /// Control + left button counts as a right-click, as on a Mac trackpad without two-finger click
        /// (macOS turns Control-click into a right-click, but the Input System reports the raw left button).
        /// </summary>
        static bool ControlHeld => Keyboard.current != null && Keyboard.current.ctrlKey.isPressed;

        /// <summary>
        /// Right-click (or hold) sets the move target on the arena floor; Control-click does the same.
        /// QWE/ASD/XC are equipment-skill keys.
        /// </summary>
        void ReadMoveTarget()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (!mouse.rightButton.isPressed && !(ControlHeld && mouse.leftButton.isPressed)) return;

            var ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var enter))
            {
                var hit = ray.GetPoint(enter);
                _moveTarget = new Vec2(hit.x, hit.z);
            }
        }

        /// <summary>Left-click on a lore target (a bookshelf) to aim spells at it; left-click elsewhere aims at the Guardian.</summary>
        void ReadSpellTarget()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || ControlHeld) return;
            var screen = mouse.position.ReadValue();
            var gui = new Vector2(screen.x, Screen.height - screen.y) / _guiScale;
            if (_hudRects.Any(r => r.Contains(gui))) return;  // a click on the HUD, not the floor

            var ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var enter)) return;
            var hit = ray.GetPoint(enter);
            var point = new Vec2(hit.x, hit.z);
            // Clicking the Guardian while its weak point is out aims at the weak point; it stays aimed until Esc.
            var weakPoint = _sim.Guardian.WeakPoints.FirstOrDefault(wp => _sim.IsWeakPointExposed(wp.Id));
            if (weakPoint != null && Vec2.Distance(_sim.GuardianPosition, point) <= 2.5f)
            {
                _spellTarget = weakPoint.Id;
                return;
            }
            _spellTarget = _sim.Guardian.LoreTargets
                .Where(t => !_sim.IsLoreSpent(t.Id) && Vec2.Distance(t.Position, point) <= 2f)
                .OrderBy(t => Vec2.Distance(t.Position, point))
                .Select(t => t.Id)
                .FirstOrDefault();
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
            if (!_inMenu) _motion?.Update(_sim, Time.deltaTime);

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
            _lit = lit;
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");

            // A flat disc with planar UVs (0–1 across the arena), so a floor material tiles evenly.
            var floor = new GameObject("Arena");
            floor.transform.SetParent(transform, false);
            floor.AddComponent<MeshFilter>().sharedMesh = Disc(GuardianRules.ArenaRadius, 96);
            floor.AddComponent<MeshRenderer>().sharedMaterial =
                art != null && art.Arena != null ? art.Arena : Lit(lit, new Color(0.35f, 0.35f, 0.38f));

            _playerMaterial = Lit(lit, PlayerColor);
            _player = Primitive(PrimitiveType.Capsule, "Player", _playerMaterial);
            // A textured Guardian (stone or bronze) shows its phase by glowing instead of changing color.
            _guardian = Primitive(PrimitiveType.Capsule, "Guardian", null);
            _guardian.localScale = Vector3.one * 2f;

            // A nose so the capsule's facing is readable.
            _nose = Primitive(PrimitiveType.Cube, "Facing", Lit(lit, Color.black));
            _nose.SetParent(_guardian, false);
            _nose.localPosition = new Vector3(0f, 0.3f, 0.5f);
            _nose.localScale = new Vector3(0.3f, 0.2f, 0.4f);

            // Alma Mater's owl on the capsule: a small sphere, shown only while it is out. A model brings its own.
            _weakPointMaterial = Lit(lit, LoreIdle);
            _owlSphere = Primitive(PrimitiveType.Sphere, "Owl", _weakPointMaterial);
            _owlSphere.SetParent(_guardian, false);
            _owlSphere.localPosition = new Vector3(0.35f, 0.1f, -0.45f);
            _owlSphere.localScale = Vector3.one * 0.3f;
            ApplyGuardianLook();

            _moveMarker = Primitive(PrimitiveType.Cylinder, "Move Target", Lit(lit, PlayerColor));
            _moveMarker.localScale = new Vector3(0.5f, 0.01f, 0.5f);

            _telegraphTemplate = new Material(unlit != null ? unlit : lit);
        }

        /// <summary>
        /// The Guardian's look for <see cref="guardianId"/>: its blockout model if one is built, otherwise the capsule,
        /// in stone or bronze when the CC0 art is built.
        /// </summary>
        void ApplyGuardianLook()
        {
            var guardianArt = art != null ? art.ForGuardian(guardianId) : null;
            _guardianTextured = guardianArt != null;
            _guardianMaterial = _guardianTextured ? new Material(guardianArt) : Lit(_lit, GuardianIdle);
            if (_guardianTextured) _guardianMaterial.EnableKeyword("_EMISSION");
            _guardian.GetComponent<Renderer>().sharedMaterial = _guardianMaterial;

            if (_guardianModel != null) Destroy(_guardianModel);
            _motion = null;
            var model = art != null ? art.ModelFor(guardianId) : null;
            _guardianModel = model != null ? Instantiate(model, _guardian, false) : null;
            _guardian.GetComponent<Renderer>().enabled = _guardianModel == null;
            _nose.gameObject.SetActive(_guardianModel == null);
            _owlSphere.gameObject.SetActive(false);
            _weakPoint = _owlSphere;
            if (_guardianModel == null) return;

            // The capsule is scaled ×2 around its centre; undo that so the model stands at its own size on the floor.
            _guardianModel.transform.localScale = Vector3.one * ModelScale(guardianId) / _guardian.localScale.y;
            _guardianModel.transform.localPosition = new Vector3(0f, -1f, 0f);
            // The FBX models face -Z in Unity (Blender's -Y front comes through backwards); the Guardian faces +Z.
            _guardianModel.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // Skinned models bend away from their rest bounds (a lunge, a raised sceptre): never cull them early.
            foreach (var skin in _guardianModel.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
            _guardianSlots.Clear();
            var bySlot = new Dictionary<string, Material>();
            foreach (var r in _guardianModel.GetComponentsInChildren<Renderer>(true))
            {
                var isOwl = r.name == "Owl";
                var special = isOwl ? _weakPointMaterial : r.name == "Eyes" ? EyeGlow() : null;
                // Each slot is coloured by its Blender material's name (Robe, Mantle, Bone, Metal, Hair); others
                // take the Guardian material as it is.
                r.sharedMaterials = r.sharedMaterials
                    .Select(m => special ?? SlotMaterial(m != null ? m.name : "", bySlot))
                    .ToArray();
                if (isOwl) _weakPoint = r.transform;
            }
            if (_weakPoint != _owlSphere) _weakPoint.gameObject.SetActive(false);
            _motion = new GuardianMotion(_guardianModel.transform, _guardian, guardianId);
        }

        /// <summary>
        /// How much larger than its FBX a Guardian is drawn. The golem is 1.3× (5.5 m) so its short legs take steps
        /// long enough for its walking speed; its BodyRadius matches (Content.cs).
        /// </summary>
        static float ModelScale(string guardianId) => guardianId == "book_keeper" ? 1.3f : 1f;

        Material SlotMaterial(string slot, Dictionary<string, Material> bySlot)
        {
            var kinds = new[] { "Robe", "Bone", "Metal", "Hair", "Marble", "Leather", "Pages", "Ribbon" };
            var kind = kinds.FirstOrDefault(slot.StartsWith) ?? "Main";
            if (bySlot.TryGetValue(kind, out var existing)) return existing;
            Material m;
            switch (kind)
            {
                case "Robe":
                case "Hair":
                    m = new Material(_guardianMaterial);
                    m.SetColor("_BaseColor", _guardianMaterial.GetColor("_BaseColor") * (kind == "Robe" ? RobeTint : HairTint));
                    break;
                case "Bone":
                    m = Lit(_lit, BoneColor);
                    m.SetFloat("_Smoothness", 0.25f);
                    break;
                case "Metal":
                    m = Lit(_lit, BronzeColor);
                    m.SetFloat("_Metallic", 0.85f);
                    m.SetFloat("_Smoothness", 0.55f);
                    break;
                case "Marble":
                    // The stone's relief (normal map) in marble's colour: the stone texture itself is too dark.
                    m = new Material(_guardianMaterial);
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
                    m.SetColor("_BaseColor", MarbleColor);
                    m.SetFloat("_Smoothness", 0.45f);
                    break;
                case "Leather":
                case "Pages":
                case "Ribbon":
                    m = Lit(_lit, kind == "Leather" ? LeatherColor : kind == "Pages" ? PagesColor : RibbonColor);
                    m.SetFloat("_Smoothness", kind == "Leather" ? 0.35f : 0.15f);
                    break;
                default:
                    m = _guardianMaterial;
                    break;
            }
            m.EnableKeyword("_EMISSION");
            bySlot[kind] = m;
            _guardianSlots.Add(m);
            return m;
        }

        /// <summary>The lich's glowing eyes (concept: verdigris lich scholar): unlit, so they read in any light.</summary>
        Material EyeGlow()
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(unlit != null ? unlit : _lit);
            mat.SetColor("_BaseColor", EyeColor);
            return mat;
        }

        /// <summary>One box per lore target of this fight's Guardian (the Book Keeper's bookshelves).</summary>
        void BuildLoreViews()
        {
            foreach (var view in _loreViews.Values) Destroy(view.go);
            _loreViews.Clear();
            foreach (var lore in _sim.Guardian.LoreTargets)
            {
                _loreTextured = art != null && art.Bookshelf != null;
                var mat = _loreTextured ? new Material(art.Bookshelf) : Lit(_lit, LoreIdle);
                var shelf = Primitive(PrimitiveType.Cube, lore.Name, mat);
                shelf.localScale = new Vector3(3f, 3f, 1f);
                shelf.position = TelegraphMeshes.ToWorld(lore.Position, 1.5f);
                shelf.rotation = Quaternion.LookRotation(new Vector3(-lore.Position.X, 0f, -lore.Position.Y));
                _loreViews[lore.Id] = (shelf.gameObject, mat);
            }
        }

        /// <summary>A flat disc on the floor (y = 0) facing up, with UVs mapping its square bounds to 0–1.</summary>
        static Mesh Disc(float radius, int segments)
        {
            var verts = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            var tris = new int[segments * 3];
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (var i = 0; i < segments; i++)
            {
                var a = 2f * Mathf.PI * i / segments;
                var x = Mathf.Cos(a) * radius;
                var z = Mathf.Sin(a) * radius;
                verts[i + 1] = new Vector3(x, 0f, z);
                uvs[i + 1] = new Vector2(x / (2f * radius) + 0.5f, z / (2f * radius) + 0.5f);
                // Clockwise seen from above, so the face points up.
                tris[i * 3] = 0;
                tris[i * 3 + 1] = (i + 1) % segments + 1;
                tris[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "Arena Disc", vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
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
            _playerMaterial.SetColor("_BaseColor", Time.time < _hurtUntil ? new Color(1f, 0.15f, 0.1f) : PlayerColor);
            _guardian.position = TelegraphMeshes.ToWorld(_sim.GuardianPosition, 2f);
            _moveMarker.gameObject.SetActive(_moveTarget.HasValue);
            if (_moveTarget is Vec2 target) _moveMarker.position = TelegraphMeshes.ToWorld(target, TelegraphY);

            var f = _sim.GuardianFacing;
            if (f.LengthSquared > 1e-6f) _guardian.rotation = Quaternion.LookRotation(new Vector3(f.X, 0f, f.Y));

            if (_guardianTextured)
            {
                var glow = _sim.Phase switch
                {
                    GuardianPhase.Tell => GuardianTell * 1.5f,
                    GuardianPhase.Recovery => GuardianRecovery,
                    _ => _sim.IsFuryActive ? GuardianFury : Color.black,
                };
                _guardianMaterial.SetColor("_EmissionColor", glow);
                foreach (var m in _guardianSlots) m.SetColor("_EmissionColor", glow);
            }
            else
            {
                var color = _sim.Phase switch
                {
                    GuardianPhase.Tell => GuardianTell,
                    GuardianPhase.Recovery => GuardianRecovery,
                    _ => _sim.IsFuryActive ? GuardianFury : GuardianIdle,
                };
                _guardianMaterial.SetColor("_BaseColor", color);
            }

            var exposed = _sim.Guardian.WeakPoints.FirstOrDefault(wp => _sim.IsWeakPointExposed(wp.Id));
            _weakPoint.gameObject.SetActive(exposed != null);
            if (exposed != null) _weakPointMaterial.SetColor("_BaseColor", exposed.Id == _spellTarget ? LoreTargeted : LoreIdle);

            var shelfIdle = _loreTextured ? Color.white : LoreIdle;  // a textured shelf keeps its wood color
            foreach (var pair in _loreViews)
                pair.Value.mat.SetColor("_BaseColor",
                    _sim.IsLoreSpent(pair.Key) ? LoreSpent : pair.Key == _spellTarget ? LoreTargeted : shelfIdle);

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

                var placed = t.HitTime - t.Tell;
                var progress = Mathf.Clamp01((_sim.Time - placed) / t.Tell);
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
                if (e.Type == CombatEventType.PlayerDamaged && e.Amount > 0f) _hurtUntil = Time.time + 0.15f;
                // The model lunges when a skill lands (hit or dodged) and swings lighter for an auto-attack.
                if (e.Type == CombatEventType.SkillHit || e.Type == CombatEventType.SkillMissed) _motion?.Strike();
                if (e.Type == CombatEventType.PlayerDamaged && e.Id == "guardian_auto") _motion?.Strike(0.5f);
                if (e.Type == CombatEventType.GuardianDamaged && e.Amount > 0f) _motion?.Flinch();
                string line = e.Type switch
                {
                    CombatEventType.PlayerDamaged when e.Id == "guardian_auto" => $"Guardian strikes you {e.Amount:0}",
                    CombatEventType.TellStarted => $"Guardian readies {Name(e.Id)}",
                    CombatEventType.SkillHit => $"{Name(e.Id)} hits you",
                    CombatEventType.SkillMissed => $"You avoid {Name(e.Id)}",
                    CombatEventType.BuffStarted => "Guardian is furious",
                    CombatEventType.AbilityRejected => $"Can't: {e.Id.Replace(":", " - ")}",
                    CombatEventType.GuardianDamaged when e.Id != "auto" => $"{e.Id.Replace('_', ' ')} {e.Amount:0}",
                    CombatEventType.CriticalHit => "  critical!",
                    CombatEventType.StatusApplied => $"Guardian {e.Id.ToLowerInvariant()}",
                    CombatEventType.StatusResisted => $"Guardian resists {e.Id.ToLowerInvariant()}",
                    CombatEventType.WeakPointHit => $"  {_sim.FindWeakPoint(e.Id)?.Name.ToLowerInvariant()} struck!",
                    CombatEventType.LoreTriggered => $"The {_sim.FindLoreTarget(e.Id)?.Name.ToLowerInvariant()} burns!",
                    CombatEventType.LoreNoEffect => $"Nothing happens to the {_sim.FindLoreTarget(e.Id.Split(':')[0])?.Name.ToLowerInvariant()}",
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

        GUIStyle _label, _center, _big, _quote;
        const float ReferenceWidth = 1280f, ReferenceHeight = 720f;
        float _guiScale = 1f;

        void OnGUI()
        {
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            _center ??= new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _big ??= new GUIStyle(_center) { fontSize = 40 };
            _quote ??= new GUIStyle(_center) { fontSize = 22, fontStyle = FontStyle.BoldAndItalic, wordWrap = true };

            // Lay the HUD out at 1280×720 and scale it to the Game view, so it fits a small window.
            _guiScale = Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight);
            GUI.matrix = Matrix4x4.Scale(new Vector3(_guiScale, _guiScale, 1f));
            float w = Screen.width / _guiScale, h = Screen.height / _guiScale;

            if (_inMenu)
            {
                _frontEnd.Draw(w, h, _label);
                return;
            }
            if (_fight == null)
            {
                Bar(new Rect(0f, h * 0.4f, w, 60f), 1f, new Color(0f, 0f, 0f, 0.7f));
                GUI.Label(new Rect(0f, h * 0.4f, w, 60f), "The Guardian is sizing you up...", _center);
                return;
            }
            if (_sim == null) return;

            // Guardian HP and cast bar
            var top = new Rect(w * 0.5f - 250f, 20f, 500f, 22f);
            Bar(top, _sim.GuardianHp / _sim.GuardianMaxHp, new Color(0.8f, 0.15f, 0.15f));
            GUI.Label(top, $"{_sim.Guardian.Name} ({_sim.Guardian.Type}) {_sim.Tier.Id}   {_sim.GuardianHp:0} / {_sim.GuardianMaxHp:0}", _center);

            var cast = new Rect(w * 0.5f - 160f, 48f, 320f, 20f);
            if (_sim.Phase == GuardianPhase.Tell && _sim.ActiveTelegraphs.Count > 0)
            {
                var next = _sim.ActiveTelegraphs.Min(t => t.HitTime) - _sim.Time;
                var tell = _sim.TellFor(_sim.CurrentSkill);
                Bar(cast, 1f - Mathf.Clamp01(next / tell), GuardianTell);
                GUI.Label(cast, $"{_sim.CurrentSkill.Name}  {next:0.0}s", _center);
            }
            else if (_sim.Phase == GuardianPhase.Recovery)
            {
                Bar(cast, 1f, GuardianRecovery);
                GUI.Label(cast, "OPENING  -  crit chance +50%", _center);
            }
            else if (_sim.Phase == GuardianPhase.Incapacitated)
            {
                Bar(cast, 1f, new Color(0.4f, 0.4f, 0.75f));
                GUI.Label(cast, $"{_sim.ActiveStatus.ToString().ToUpperInvariant()}  -  it cannot attack" +
                    (_sim.IsOpening ? ", crit chance +50%" : ""), _center);
            }

            // Player HP and hotbar
            var hp = new Rect(w * 0.5f - 250f, h - 132f, 500f, 20f);
            Bar(hp, _sim.PlayerHp / _sim.PlayerMaxHp, new Color(0.2f, 0.75f, 0.3f));
            GUI.Label(hp, $"{_sim.PlayerHp:0} / {_sim.PlayerMaxHp:0}" + (_sim.GuardRemaining > 0f ? "   GUARD" : "") +
                (_sim.AutoAttacking ? "   AUTO-ATTACK ON" : "   auto-attack off (A)"), _center);
            var mp = new Rect(w * 0.5f - 250f, h - 108f, 500f, 16f);
            Bar(mp, _sim.PlayerMp / _sim.PlayerMaxMp, new Color(0.25f, 0.4f, 0.95f));
            GUI.Label(mp, $"MP {_sim.PlayerMp:0} / {_sim.PlayerMaxMp:0}", _center);

            var abilities = new[]
            {
                (Ability.HeavyStrike, "Q Heavy"), (Ability.Guard, "W Guard"), (Ability.Lunge, "E Lunge"), (Ability.Overpower, "R Overpower"),
                (Ability.LightningBolt, "T Lightning"), (Ability.FireBolt, "F Fire"),
            };
            for (var i = 0; i < abilities.Length; i++)
            {
                var (ability, label) = abilities[i];
                var slot = new Rect(w * 0.5f - 306f + i * 102f, h - 80f, 94f, 50f);
                var cd = _sim.CooldownRemaining(ability);
                var spell = ability == Ability.LightningBolt || ability == Ability.FireBolt;
                var blocked = _sim.BlockReason(ability, spell ? _spellTarget : null);
                Bar(slot, 1f, blocked == null ? new Color(0.2f, 0.25f, 0.35f) : new Color(0.12f, 0.12f, 0.14f));
                GUI.Label(slot, cd > 0f ? $"{label}\n{cd:0.0}" : blocked is "range" or "mana" or "target" ? $"{label}\n{blocked}" : label, _center);
            }
            if (_sim.GcdRemaining > 0f)
                Bar(new Rect(w * 0.5f - 250f, h - 28f, 500f * _sim.GcdRemaining / PlayerRules.GlobalCooldown, 4f), 1f, Color.white);

            ArtifactSkillBar(w, h);

            // Info and log
            GUI.Label(new Rect(16f, 16f, 800f, 130f),
                $"{(accountLoop ? GameSession.Account?.Username : build.ToString())} (gear {_sim.Build.GearScore})   {_sim.Time:0.0}s\n" +
                $"Guardian: {_sim.Setup}   [{SetupLine()}]\n" +
                "Right-click (or Ctrl+click) move   Q Heavy  W Guard  E Lunge  R Overpower  T Lightning Bolt  F Fire Bolt  A auto-attack on/off\n" +
                "Left-click a bookshelf or something on the Guardian to aim at it (Esc: back to the Guardian)" +
                (_spellTarget != null ? $"   Target: {_sim.FindLoreTarget(_spellTarget)?.Name ?? _sim.FindWeakPoint(_spellTarget)?.Name} ({_spellTarget})" : "") + "\n" +
                "Artifact skills: 1 2 3 4 C V (click two slots on the right to swap them)", _label);
            GUI.Label(new Rect(16f, h - 190f, 360f, 170f), string.Join("\n", _log), _label);

            // The adaptive Guardian's opening line (GDD 6.4): shown at fight start, then fades.
            var left = _announcementUntil - Time.time;
            if (left > 0f && _fight.Announcement != null)
            {
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(left));
                Bar(new Rect(w * 0.5f - 360f, h * 0.2f, 720f, 70f), 1f, new Color(0.08f, 0.05f, 0.02f, 0.75f));
                GUI.Label(new Rect(w * 0.5f - 350f, h * 0.2f, 700f, 70f), $"{_sim.Guardian.Name}: \"{_fight.Announcement}\"", _quote);
                GUI.color = old;
            }

            if (_sim.Outcome != CombatOutcome.Ongoing)
            {
                Bar(new Rect(0f, h * 0.35f, w, 150f), 1f, new Color(0f, 0f, 0f, 0.7f));
                GUI.Label(new Rect(0f, h * 0.35f, w, 90f), _sim.Outcome == CombatOutcome.Win ? "VICTORY" : "DEFEAT", _big);
                GUI.Label(new Rect(0f, h * 0.35f + 90f, w, 40f),
                    (_reward != null ? $"Reward: {_reward}\n" : "") +
                    $"{_sim.Time:0.0}s    R retry    " +
                    (accountLoop ? "Enter quest list" : $"B switch build (now {build})") + $"    M Guardian mode (now {guardianMode})" +
                    (accountLoop && GameSession.SaveError != null ? $"\nNot saved: {GameSession.SaveError}" : ""), _center);
            }
        }

        /// <summary>Where this attempt's setup came from, for the info line.</summary>
        string SetupLine()
        {
            var failed = Progress.FailedAttempts(Profile, guardianId);
            var where = _fight.Source switch
            {
                SetupSource.Server when _fight.FallbackReason == nameof(FallbackReason.None) => $"adaptive, server {_fight.LatencyMs} ms",
                SetupSource.Server => $"server fell back: {_fight.FallbackReason}",
                SetupSource.LocalFallback => $"default, server not used: {_fight.FallbackReason}" +
                    (GameSession.Guardian.SessionFlagged ? " (session flagged)" : ""),
                _ => guardianMode == GuardianMode.Custom ? "custom" : "scripted default",
            };
            return $"{guardianMode}: {where}" + (_fight.Eased ? ", eased" : "") + $"   failed attempts {failed}";
        }

        /// <summary>
        /// The six artifact skill slots, stacked at the right edge. Click one slot, then another, to swap them;
        /// the order is kept for later retries.
        /// </summary>
        void ArtifactSkillBar(float w, float h)
        {
            _hudRects.Clear();
            for (var i = 0; i < PlayerRules.SkillBarSize; i++)
            {
                var slot = (SkillBarSlot)(i + 1);
                var rect = new Rect(w - 196f, h * 0.5f - 165f + i * 56f, 180f, 50f);
                _hudRects.Add(rect);
                var artifact = _sim.ArtifactAt(slot);
                var cd = _sim.ArtifactSkillCooldownRemaining(slot);
                var color = slot == _swapFrom ? new Color(0.55f, 0.45f, 0.1f)
                    : artifact == null ? new Color(0.1f, 0.1f, 0.12f)
                    : cd > 0f ? new Color(0.12f, 0.12f, 0.14f) : new Color(0.2f, 0.25f, 0.35f);
                Bar(rect, 1f, color);
                var label = $"{SkillBarKeyNames[i]}  {(artifact != null ? artifact.Name : "(empty)")}" + (cd > 0f ? $"\n{cd:0.0}s" : "");
                if (GUI.Button(rect, label, _center)) ClickSkillBarSlot(slot);
            }
        }

        void ClickSkillBarSlot(SkillBarSlot slot)
        {
            if (_swapFrom == SkillBarSlot.None) { _swapFrom = slot; return; }
            if (_swapFrom != slot) _sim.SwapArtifactSkills(_swapFrom, slot);
            _swapFrom = SkillBarSlot.None;
            _preferredBar = _sim.ArtifactSkillBar.ToArray();
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
