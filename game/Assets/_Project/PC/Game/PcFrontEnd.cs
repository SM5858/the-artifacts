using System;
using System.Linq;
using TheArtifacts.Core.Account;
using TheArtifacts.Core.Combat;
using TheArtifacts.Shared;
using UnityEngine;

namespace TheArtifacts.PC.Game
{
    /// <summary>
    /// The PC screens around a fight (GDD 9): sign in, the quest list with each landmark's story card, and equipment.
    /// IMGUI, drawn by the combat scene at the 1280×720 reference size.
    /// </summary>
    public sealed class PcFrontEnd
    {
        enum Page { SignIn, Quests, Equipment }

        readonly Action<string> _startFight;
        Page _page;
        string _username = "", _password = "", _status;
        bool _online, _busy;
        /// <summary>The Guardian whose story card is open after an unlock, or null.</summary>
        string _card;
        Vector2 _scroll;
        GUIStyle _label, _title, _wrap, _small;

        /// <param name="startFight">Called with a Guardian id when the player picks a quest.</param>
        public PcFrontEnd(Action<string> startFight)
        {
            _startFight = startFight;
            _online = Accounts.Online != null;
            _page = GameSession.SignedIn ? Page.Quests : Page.SignIn;
        }

        public void ShowQuests() => _page = GameSession.SignedIn ? Page.Quests : Page.SignIn;

        public void Draw(float w, float h, GUIStyle label)
        {
            _label ??= new GUIStyle(label) { alignment = TextAnchor.MiddleLeft };
            _title ??= new GUIStyle(label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _wrap ??= new GUIStyle(label) { wordWrap = true, alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Italic };
            _small ??= new GUIStyle(label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };

            Fill(new Rect(0f, 0f, w, h), new Color(0.03f, 0.03f, 0.05f, 0.82f));
            var panel = new Rect(w * 0.5f - 420f, 40f, 840f, h - 80f);
            GUI.enabled = !_busy;
            switch (_page)
            {
                case Page.SignIn: SignIn(panel); break;
                case Page.Quests: Quests(panel); break;
                case Page.Equipment: Equipment(panel); break;
            }
            GUI.enabled = true;
            if (_card != null) StoryCard(w, h);

            var status = _busy ? "Working..." : GameSession.SaveError != null ? $"Not saved: {GameSession.SaveError}" : _status;
            if (status != null) GUI.Label(new Rect(panel.x, panel.yMax - 28f, panel.width, 24f), status, _label);
        }

        // ---------------- Sign in ----------------

        void SignIn(Rect panel)
        {
            GUILayout.BeginArea(new Rect(panel.x + 220f, panel.y + 80f, 400f, 360f));
            GUILayout.Label("The Artifacts", _title, GUILayout.Height(40f));
            GUILayout.Label("Sign in with the same account you use in the mobile game.", _small);
            GUILayout.Space(16f);
            GUILayout.Label("Username", _label);
            _username = GUILayout.TextField(_username, AccountRules.MaxUsername, GUILayout.Height(26f));
            GUILayout.Label("Password", _label);
            _password = GUILayout.PasswordField(_password, '*', AccountRules.MaxPassword, GUILayout.Height(26f));
            GUILayout.Space(8f);
            if (Accounts.Online != null)
                _online = GUILayout.Toggle(_online, _online ? " Online account (Unity)" : " Offline account (this computer only)");
            else
                GUILayout.Label("Offline accounts only: the Authentication and Cloud Save packages are not installed.", _small);
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Sign in", GUILayout.Height(32f))) SubmitAsync(create: false);
            if (GUILayout.Button("Create account", GUILayout.Height(32f))) SubmitAsync(create: true);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        async void SubmitAsync(bool create)
        {
            var account = _online && Accounts.Online != null ? Accounts.Online : Accounts.Offline;
            _busy = true;
            _status = null;
            try
            {
                await GameSession.SignInAsync(account, _username.Trim(), _password, create);
                _password = "";
                _page = Page.Quests;
                if (create) await GameSession.SaveAsync();
            }
            catch (AccountException e) { _status = e.Message; }
            catch (Exception e) { _status = $"Sign-in failed: {e.Message}"; }
            finally { _busy = false; }
        }

        // ---------------- Quests ----------------

        void Quests(Rect panel)
        {
            var profile = GameSession.Profile;
            var build = Progress.Build(profile);
            GUI.Label(new Rect(panel.x, panel.y, 500f, 36f), "Quests", _title);
            GUI.Label(new Rect(panel.x, panel.y + 36f, 600f, 22f),
                $"{GameSession.Account.Username} ({(GameSession.Account.IsOnline ? "online" : "offline")})   gear score {build.GearScore}   " +
                $"{profile.UnlockedGuardians.Count}/{PrototypeContent.Guardians.Count} dungeons open", _small);
            if (GUI.Button(new Rect(panel.xMax - 250f, panel.y + 4f, 120f, 30f), "Equipment")) _page = Page.Equipment;
            if (GUI.Button(new Rect(panel.xMax - 120f, panel.y + 4f, 120f, 30f), "Sign out"))
            {
                GameSession.SignOut();
                _page = Page.SignIn;
                return;
            }

            var y = panel.y + 72f;
            foreach (var g in PrototypeContent.Guardians)
            {
                var box = new Rect(panel.x, y, panel.width, 150f);
                var state = Progress.State(profile, g.Id, DateTime.UtcNow);
                var unlocked = state == DungeonState.Open;
                Fill(box, unlocked ? new Color(0.14f, 0.12f, 0.08f, 0.95f) : new Color(0.08f, 0.08f, 0.09f, 0.95f));
                GUI.Label(new Rect(box.x + 14f, box.y + 8f, 600f, 28f), $"{g.Landmark}: {g.Name}", _title);
                if (unlocked)
                {
                    var record = Progress.Record(profile, g.Id);
                    GUI.Label(new Rect(box.x + 14f, box.y + 38f, 600f, 20f),
                        $"Unlocked   wins {record.Wins}   failed attempts since last win {record.FailedSinceWin}   " +
                        $"drops: {string.Join(", ", g.LootPool.Select(id => PrototypeContent.Artifact(id).Name))}", _small);
                    GUI.Label(new Rect(box.x + 14f, box.y + 62f, box.width - 180f, 80f), g.StoryCard, _wrap);
                    if (GUI.Button(new Rect(box.xMax - 140f, box.y + 60f, 120f, 44f), "Fight")) _startFight(g.Id);
                }
                else if (state == DungeonState.Resting)
                {
                    var wait = Progress.ReopensAt(profile, g.Id).Value - DateTime.UtcNow;
                    GUI.Label(new Rect(box.x + 14f, box.y + 44f, box.width - 200f, 40f),
                        $"Beaten. The Guardian rests; visit the {g.Landmark} again in {wait.Days} d {wait.Hours} h to challenge it again.", _label);
                }
                else
                {
                    GUI.Label(new Rect(box.x + 14f, box.y + 44f, box.width - 200f, 40f),
                        state == DungeonState.Revisit
                            ? $"The Guardian has returned. Visit the {g.Landmark} in the mobile game to challenge it again."
                            : $"Locked. Visit the {g.Landmark} in the mobile game to open this dungeon.", _label);
                    // Stands in for the mobile game's check-in until it exists (GDD 6.1 first cut).
                    if (GUI.Button(new Rect(box.xMax - 180f, box.y + 60f, 160f, 44f), "Simulate visit (dev)")) SimulateVisit(g.Id);
                }
                y += 160f;
            }
        }

        async void SimulateVisit(string guardianId)
        {
            Progress.CheckIn(GameSession.Profile, guardianId, "simulated", DateTime.UtcNow);
            Debug.Log($"check_in user={GameSession.Account.Username} guardian={guardianId} source=simulated");
            _card = guardianId;
            _busy = true;
            try { await GameSession.SaveAsync(); }
            finally { _busy = false; }
        }

        /// <summary>The card shown on an unlock (GDD 6.7): the place's story, which carries the hint to its mechanic.</summary>
        void StoryCard(float w, float h)
        {
            var g = PrototypeContent.GuardianById(_card);
            var rect = new Rect(w * 0.5f - 300f, h * 0.5f - 150f, 600f, 300f);
            Fill(rect, new Color(0.2f, 0.16f, 0.09f, 0.98f));
            GUI.Label(new Rect(rect.x + 24f, rect.y + 16f, 552f, 34f), "Dungeon unlocked", _title);
            GUI.Label(new Rect(rect.x + 24f, rect.y + 54f, 552f, 24f), $"{g.Landmark}: {g.Name}", _label);
            GUI.Label(new Rect(rect.x + 24f, rect.y + 88f, 552f, 150f), g.StoryCard, _wrap);
            if (GUI.Button(new Rect(rect.xMax - 144f, rect.yMax - 52f, 120f, 36f), "OK")) _card = null;
        }

        // ---------------- Equipment ----------------

        void Equipment(Rect panel)
        {
            var profile = GameSession.Profile;
            var build = Progress.Build(profile);
            GUI.Label(new Rect(panel.x, panel.y, 500f, 36f), "Equipment", _title);
            if (GUI.Button(new Rect(panel.xMax - 120f, panel.y + 4f, 120f, 30f), "Back")) _page = Page.Quests;
            GUI.Label(new Rect(panel.x, panel.y + 40f, panel.width, 22f),
                $"Gear score {build.GearScore}   HP {build.MaxHp:0}   MP {build.MaxMp:0}   Str {build.Strength:0}   Int {build.Intelligence:0}   " +
                $"Def {build[StatKind.Def]:0}   Res {build[StatKind.Res]:0}   Crit {build.CritChance:P0}", _small);

            if (profile.Artifacts.Count == 0)
            {
                GUI.Label(new Rect(panel.x, panel.y + 80f, panel.width, 40f), "No artifacts yet. Beat a Guardian to earn one.", _label);
                return;
            }

            var list = new Rect(panel.x, panel.y + 72f, panel.width, panel.height - 110f);
            var content = new Rect(0f, 0f, panel.width - 20f, profile.Artifacts.Count * 70f);
            _scroll = GUI.BeginScrollView(list, _scroll, content);
            var y = 0f;
            foreach (var owned in profile.Artifacts.ToList())
            {
                var a = PrototypeContent.Artifact(owned.Id);
                var on = profile.Equipped.Contains(a.Id);
                var row = new Rect(0f, y, content.width, 62f);
                Fill(row, on ? new Color(0.16f, 0.22f, 0.3f, 0.95f) : new Color(0.1f, 0.1f, 0.12f, 0.95f));
                GUI.Label(new Rect(12f, y + 6f, 560f, 24f), $"{a.Name}   {a.Rarity} {a.Slot}" + (owned.Count > 1 ? $"   x{owned.Count}" : ""), _label);
                GUI.Label(new Rect(12f, y + 32f, 560f, 22f), a.Description, _small);
                if (GUI.Button(new Rect(row.xMax - 130f, y + 12f, 116f, 38f), on ? "Unequip" : "Equip")) ToggleAsync(a.Id);
                y += 70f;
            }
            GUI.EndScrollView();
        }

        async void ToggleAsync(string artifactId)
        {
            _status = Progress.ToggleEquip(GameSession.Profile, artifactId);
            if (_status != null) return;
            _busy = true;
            try { await GameSession.SaveAsync(); }
            finally { _busy = false; }
        }

        static void Fill(Rect r, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
