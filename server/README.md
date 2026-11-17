# Server: Guardian Cloud Code module

The adaptive Guardian runs here, in a Unity Gaming Services (UGS) Cloud Code C# module. The game calls it once before each attempt; it never runs during combat.

```
server/
├── GuardianModule/
│   ├── GuardianModule.sln          the solution UGS deploys (main project only)
│   └── GuardianModule/             module "GuardianModule"
│       ├── GuardianFunctions.cs    [CloudCodeFunction("ConfigureGuardian")] + UGS setup
│       ├── GuardianService.cs      one call, 3 s deadline, validate, fall back, trace
│       ├── GuardianRequest.cs      allowed snapshot + allowed options → model input
│       └── ClaudeGuardianModel.cs  Claude Haiku 5.5, thinking off, forced strict tool call
└── GuardianModule.Tests/           fake-model tests; not in the .sln, never deployed
```

The module compiles the engine-free Core from `game/Assets/_Project/Core` and embeds `agents/guardian/system_prompt.md` and `configure_guardian.schema.json`, so the server, the game, and the tests share one copy of the rules and the prompt.

## The function

`ConfigureGuardian(equippedArtifactIds, attemptsOnThisGuardian, adaptive)` returns the setup to fight plus its trace (proposed setup, fallback and reason, latency, tokens, snapshot hash). The client sends only artifact ids and the attempt count; stats are recomputed on the server from content. With `adaptive = false` it returns the scripted default without calling the model (the human override in Configuration Document 2.10).

## Tests

```bash
dotnet test server/GuardianModule.Tests
```

They use a fake model and make no API calls: valid proposal, forced timeout, forced out-of-range, malformed answer, service error, unknown artifact, no blocked fields in the request, enums limited to the challenger's range, and tool-call parsing. CI runs them on every push.

## One-time setup (needs you)

These steps need your Unity account, the Unity Cloud dashboard, and your Anthropic API key, so they can't be done from here.

1. **Create the Unity Cloud project.** In the Unity Cloud dashboard, create a project, then in the Unity Editor link it under **Edit → Project Settings → Services**. Use the `production` environment or create a `dev` environment for testing.
2. **Install packages** in the Unity project (Package Manager → Unity Registry): **Cloud Code** (`com.unity.services.cloudcode`, 3.0.0 or later), **Authentication**, and **Deployment** (`com.unity.services.deployment`).
3. **Set the .NET path** for Cloud Code: **Unity → Settings → Cloud Code → .NET path**. Unity 6.3 has no system `dotnet`; install the .NET 8 SDK (`brew install --cask dotnet-sdk`) and point this at it.
4. **Add the API key as a secret.** In the Unity Cloud dashboard, open **Secret Manager** for the project and environment and add a secret named **`ANTHROPIC_API_KEY`** with your Anthropic API key. Never put the key in the repository or the game.
5. **Link the module.** In the Project window, **Create → Services → Cloud Code C# Module Reference**, save it as `Assets/_Project/Shared/CloudCode/GuardianModule.ccmr`, and in its Inspector set **Solution Path** to `server/GuardianModule/GuardianModule.sln`. (Unity 6.3 uses this module-reference workflow; in-editor module authoring needs Unity 6.7+.)
6. **Deploy.** **Services → Deployment**, select `GuardianModule.ccmr`, **Deploy Selected**. The module is named `GuardianModule` after the main project.

Live calls are billed to your Anthropic account: about $0.00035 per attempt (Configuration Document 2.7). Set a spending limit on the key before playtests (open item C4).

## Still to verify after the first deploy

- Real latency against the 3 s deadline (C8), and the cold start after 15 idle minutes.
- That Cloud Code's 15 s execution limit and outbound HTTP behave as documented with the Claude API.
- Where the `guardian_trace` log lines appear in the dashboard, and whether to also store traces in Cloud Save for BI.

## Not done yet

- The game does not call this module yet. Wiring the Unity client (sign-in, `CallModuleEndpointAsync`, a 3.5 s client-side timeout with a local default fallback, flagging the session after a `ServiceUnavailable`) comes after the module is deployed, so it can be tested against the real service.
- Equipped artifacts come from the client for now. Once artifacts are saved in Cloud Save, the server should read them there instead.
