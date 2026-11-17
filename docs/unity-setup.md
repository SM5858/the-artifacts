# Unity Setup (branch `unity-setup`)

**Status:** Project created (Unity 6.3 LTS, 6000.3.28f1, 3D URP). Stack decision for GDD Q1: **Unity 6 LTS**, one project for both the PC client (combat) and the mobile client (check-in), sharing C# code. Reason: release ambition — 3D third-person combat on PC and a map-based check-in app on mobile from one engine and one language.

This branch leaves `main` (the G1 submission) unchanged. It is merged to `main` only after the G1 decision.

## Repository layout

```
the-artifacts/
├── docs/                  GDD, Configuration Document, Project Plan, this file
├── deck/                  G1 pitch deck
├── agents/guardian/       Guardian extension artifact (manifest, prompt, tool schema)
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

Implemented so far: the Guardian setup validator and challenger snapshot (Configuration Document 2.2 and 2.5) — fields from the engine's options, the 3 s deadline, and default-Guardian fallback with a reason. Combat rules wait on GDD Q3/Q4.

## Open items

- Backend for accounts, quest unlocks, and the server-side Guardian call (PlayFab / Nakama / Supabase): **[OPEN]**
- Map SDK for the mobile check-in: **[OPEN]** — check current Unity support status at decision time
- When to split into a private release repository: before any paid asset or release-only code is added
