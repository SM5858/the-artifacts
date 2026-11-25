# Unity Setup (branch `unity-setup`)

**Status:** Project created (Unity 6.3 LTS, 6000.3.28f1, 3D URP). Stack decision for GDD Q1: **Unity 6 LTS**, one project for both the PC client (combat) and the mobile client (check-in), sharing C# code. Reason: release ambition — 3D third-person combat on PC and a map-based check-in app on mobile from one engine and one language.

This branch leaves `main` (the G1 submission) unchanged. It is merged to `main` only after the G1 decision.

## Repository layout

```
the-artifacts/
├── docs/                  GDD, Configuration Document, Project Plan, this file
├── deck/                  G1 pitch deck
├── agents/guardian/       Guardian extension artifact (manifest, prompt, tool schema)
├── server/                UGS Cloud Code module for the adaptive Guardian (see server/README.md)
└── game/                  Unity project (created from Unity Hub, see below)
    └── Assets/
        ├── _Project/
        │   ├── Core/          Pure C# (no UnityEngine): combat sim, stats, cooldowns,
        │   │                  Guardian setup validation, rewards, data models.
        │   │                  Shared by PC, mobile, and the server-side checks.
        │   ├── Core.Tests/    EditMode tests for Core (rule and invariant tests)
        │   ├── Shared/        Unity-side code used by both clients (account, backend API)
        │   ├── PC/            Combat scenes, third-person controller, camera, combat UI
        │   ├── Mobile/        Check-in scenes, map, location, quest confirmation
        │   └── Art/           Original art and assets with documented licenses
        └── ThirdParty/        Asset Store packages (git-ignored: licenses forbid public redistribution)
```

Each `_Project` folder gets its own assembly definition (`.asmdef`). `Core` has **no engine references**, so combat rules and the Guardian validator run in fast EditMode tests without a scene, and the same rules can be checked server-side later.

## One-time setup

1. Install Git LFS, then enable it for this repo:
   ```bash
   brew install git-lfs
   ```
   ```bash
   git lfs install
   ```
2. Install **Unity Hub**, then the latest **Unity 6 LTS** editor with modules:
   - Mac Build Support (Mono / IL2CPP) and Windows Build Support (PC)
   - Android Build Support (with OpenJDK and Android SDK & NDK)
   - iOS Build Support
3. In Unity Hub: **New project → Universal 3D (URP)**, project name `game`, location = the repository root. `game/` must not exist beforehand.
4. In the editor: **Edit → Project Settings → Editor**: Version Control = Visible Meta Files, Asset Serialization = Force Text (Unity defaults).
5. Commit `game/Assets`, `game/Packages`, and `game/ProjectSettings` only. Everything else is ignored by `.gitignore`.

## Tests

Core rule tests live in `game/Assets/_Project/Core.Tests` and run two ways:

- **In Unity:** Window → General → Test Runner → EditMode → Run All.
- **Without Unity (CI):** `tools/CoreTests/CoreTests.csproj` compiles the same Core and Core.Tests sources with .NET 8 at C# 9 (Unity 6's language level), so no Unity license is needed. `.github/workflows/ci.yml` runs it on every push and pull request.
  ```bash
  dotnet test tools/CoreTests
  ```

Core may not use `UnityEngine` (its asmdef sets `noEngineReferences`), and Core.Tests may use only NUnit, so both runs see the same code.

Implemented so far:

- Guardian setup validator and challenger snapshot (Configuration Document 2.2 and 2.5): fields from the engine's options, the 3 s deadline, and default-Guardian fallback with a reason.
- Combat engine (`Core/Combat`, Combat Spec): deterministic real-time simulation of one player versus one Guardian, all prototype content, the four strategies, and tuning ranges by gear score. Played in the graybox scene below.
- PC game loop (`PC/Game`, on by default in the sandbox scene: **Account Loop**): sign in or create an account (online with the Authentication and Cloud Save packages, otherwise offline on this computer under `Application.persistentDataPath/accounts`), quest list with story cards and **Simulate visit (dev)**, equipment screen, fight, then **Enter** back to the list. A win stores the artifact on the account and a loss counts toward easing; both are saved. Turn Account Loop off for quick test fights with the Inspector's build and Guardian.
- Graybox PC fight (`PC/Combat`, scene `PC/Scenes/CombatSandbox.unity`): primitive arena and capsules (the player flashes red on any hit), lock-on camera, right-click (or Ctrl+click) movement, skills on Q W E R T F, A to toggle auto-attack, artifact skill bar on 1 2 3 4 C V (click two bar slots to swap them), floor telegraphs, IMGUI HUD (tells, HP, cooldowns, outcome; R retry, B switch build, M Guardian mode). **Guardian Mode** (Inspector, or M on the result screen): Default (scripted), Custom (the fields below it), or Adaptive (asks the Guardian server before each attempt, waits up to 3.5 s, shows the Guardian's opening line as a banner, and falls back to Default while the server is not set up; the info line says which happened). Failed attempts per Guardian are counted in memory for easing. Recreate the scene with **The Artifacts → Create Combat Sandbox Scene**. Materials are made at runtime from URP shader names, which works in the editor; switch to material assets before making a player build.
- CC0 materials (`Art/Materials`, sources in GDD Section 12): run **The Artifacts → Build Sandbox Materials** with the sandbox scene open. It packs each set's metallic and roughness maps into a URP map, builds the `.mat` files and `Art/SandboxArt.asset`, and assigns it to the sandbox; the arena floor, the Guardian (by `guardianId`: verdigris Alma Mater, bronze Lion, stone Book Keeper), and the bookshelves then use them. A textured Guardian glows to show its phase (orange tell, green opening, purple fury). Without the asset, the flat colors stay.
- Guardian models (`Art/Models/Guardians`, one FBX per Guardian id): generated with Tripo AI from the concept sheets and prepared by `tools/blender/prepare_generated.py` (in Blender: scale to the Guardian's height, feet at the origin, facing -Y in Blender (it imports facing -Z, and the sandbox turns it 180° to the Guardian's +Z), decimated to 80k triangles; the texture set BaseColor / Normal / Roughness / Metallic / AO goes to `Art/Materials/<AlmaMater|ColumbiaLion|BookKeeper>`). **Build Sandbox Materials** builds those materials and assigns model and material; the sandbox then shows the model instead of the capsule. Each is one mesh with one "Main" slot and no "Owl" or "Eyes" child, so the weak point falls back to the capsule's sphere and the eyes glow only in the texture. `tools/blender/rig_guardians.py` then skins each to a skeleton (lich 17 bones with collarbones, a three-joint spine and robe; lion 32 with shoulder blades, toes, a two-joint neck and a six-joint tail; golem 23 with shoulders, neck, fingers and toes; distance-based weights with per-bone regions; imported as a Generic rig with no clips), and `PC/Combat/GuardianMotion.cs` animates the bones procedurally from the fight: Alma Mater hovers upright and moves like a person from the waist up with a trailing robe; the lion walks in a four-beat gait (front legs always alternate; the stride lengthens with speed), stalks with its head low, sways its tail, crouches and pounces, and roars; the golem (drawn 1.3×, 5.5 m, with BodyRadius 1.8 to match, so its short legs can keep pace) walks with planted feet solved as two-bone legs, stomping stiffly and a little late like a heavy machine, scans with its head in steps, and raises both fists before it slams. All three wind up in the tell, strike when the skill lands, slump in the opening, tremble under Fear, flinch when hit, and fall on defeat. The earlier procedural blockouts (`tools/blender/guardian_blockouts.py`, with jointed parts and material slots) are kept as reference and skip the generated ids on export (`GENERATED`).
- Player model (`Art/Models/Player/player.fbx`, 1.75 m, the hunter from the three-view sheet): rigged with 22 bones (spine, neck, head, a two-joint braid, collarbones, arms, legs, and the right hand's fingers, which close into a fist around a held weapon; unarmed, so weapons and artifacts can be attached as separate models later; faces where the generated mesh fused an arm or the braid to the body are cut by `detach`). **Build Sandbox Materials** assigns it and its `Player` material in `SandboxArt`, and the sandbox swaps the player capsule for it. `PC/Combat/PlayerMotion.cs` animates it from the fight: she turns to run where she moves and to face the Guardian when she stops, runs with counter-swinging arms and a trailing braid, attacks with keyframed whole-body clips (the hips lead, the trunk whips round after them, the feet step and plant, and the weapon arm is aimed in the chest's frame so it rides the trunk's turn): a horizontal forehand for auto-attack, a low lean with the blade drawn back for a dash-in, two cuts (down to her left, then rising back to her right with the hips reversing) for Double Strike, a crouch, a leap with the blade raised behind her head and a downward cut into a deep landing crouch for Lunge, and a gathered stance with the blade cocked over her shoulder and a great diagonal cut with a hard step-in for Overpower; she casts with her left hand for the bolts and artifact skills, raises both arms while Guard is up, flinches (and tints red) when hit, and falls on a loss. `PC/Combat/PlayerEffects.cs` adds what the clips time: a trail from the blade's tip (its farthest vertex from the grip; past the fingers when unarmed) while it cuts, thin and quick for the slashes and Lunge, wide, gold and lingering for Overpower, and Overpower's aura (rising sparks, a soft glow, and a light that swell while she gathers herself and fade after the blow). The clips were checked by rendering contact sheets in a batchmode copy (`Camera.Render` there does not skin, so each skinned mesh was baked first).
- Worn artifacts (`Art/Models/Artifacts`, one FBX per artifact id; so far Columbia Lion's Fang): made by `tools/blender/prepare_artifact.py`, which orients a generated model (origin at the grip, blade along -Z), sizes it, and exports it at its pose in the player's rest space with player.fbx's export settings. **Build Sandbox Materials** imports it as a static mesh with the same axis conversion, builds its material, and lists it in `SandboxArt.Artifacts`. At each fight start the sandbox puts the build's artifacts on her (`CombatSandbox.WearArtifacts`): placed under the model at rest, then reparented to the slot's bone (right hand `Hand_R`, left hand `Hand_L`) keeping that pose; a right-hand artifact makes `PlayerMotion` close her fingers around it. The Lion's Fang (0.40 m) lies across her right palm, blade forward and 25° down, the lion pommel behind the fist. The geared test build wears it.
- The Columbia Blue Robe is worn as an open cape (`tools/blender/make_cape.py`): a pleated sheet built in Blender, hung from a standing collar around her back from shoulder to shoulder to mid-calf, with an ivory lining, its outer face textured by projecting the design sheet's back view (laurel yoke, pleats, hem trim and stars). Empty bell sleeves (her arms are not in them) are sewn on at the shoulders over the cape and hang behind her arms, with the sheet's laurel cuff band and the lining inside. It has a root bone, three chains of four bones down the cape (her right, middle, left) and two bones per sleeve, is imported skinned, and rides on `Chest`. `PlayerMotion` swings each row on a spring: it streams back as she runs (about 72° at the hem at full speed), swings out when she turns or sidesteps, ripples at speed, and sways and settles when she stops; the sleeves trail a little less, lift outward as she runs, and flap.

## Open items

- Backend: **decided 2026-11-16 — Unity Gaming Services** (Authentication, Cloud Save / Economy, Cloud Code C# for the Guardian call). To verify at setup: Cloud Code C# module limits (execution time, outbound HTTP to the Claude API) against the 3 s deadline, and free-tier quotas.
- Map SDK for the mobile check-in: **[OPEN]** — check current Unity support status at decision time
- When to split into a private release repository: before any paid asset or release-only code is added
