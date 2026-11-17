# The Artifacts — Configuration Document

**Status: Initial (G1).** Next revision: Update at G2/G3. Final at G4.
**Owner:** the student, in the Tech Lead role. **Gate Approver:** instructor.

This document records how agents are configured: the **in-game agent** (the Guardian) and the **production agents** that perform delegated development work. Everything here is **planned**; no configuration exists in the repository yet. Paths marked *(planned)* are where each executable artifact will live. Open questions are marked **[OPEN]** and collected in Section 6.

**Authority.** I remain Executive Producer and Creative Director. Agents recommend; I decide what to propose; the instructor decides gates.

## 1. Two kinds of agent

| | In-game agent | Production agents |
|---|---|---|
| Who | The Guardian's adaptive tuning | Role agents run in Claude Code |
| Sees | A filtered snapshot of one challenger | The repository and docs, per role |
| May do | Propose one Guardian setup from predefined options | Read, write, and test per role permissions |
| Never | Sees GPS, private data, or reward rolls; changes state directly | Merges, changes scope, or submits a gate without my approval |

## 2. In-game agent: the Guardian

### 2.1 Purpose

Before each attempt, tune the Guardian's stats, skills, and strategy to the challenger, so that players of different strength, including a brand-new one, can each win within a few attempts. Combat itself is real-time and runs without the model.

### 2.2 World state versus agent knowledge

The engine owns the authoritative world state. The Guardian receives only the allowed challenger snapshot.

| Authoritative world state (engine only) | Allowed challenger snapshot (sent to the Guardian) | Blocked from agent context |
|---|---|---|
| Quest unlocks, inventory, HP, cooldowns, reward rolls, account and visit records | Challenger stats, skills, equipped artifacts, attempts so far on this Guardian, and the setup options and ranges the engine allows | GPS and location history, private player data, hidden reward rolls, unequipped inventory |

### 2.3 Interface (executable extension artifact)

The Guardian ships as a **function-calling interface**: the model must answer by calling one tool.

- **Tool:** `configure_guardian`
- **Input:** `{ "stat_tier": <one of allowed_tiers>, "skill_ids": [<from skill_pool>], "strategy_id": <one of strategies> }`
- **Context per call:** system instructions + the allowed challenger snapshot + the allowed options.
- **When:** once at the start of each attempt. No calls during combat.
- **Location:** `agents/guardian/` *(planned)* — `manifest.json`, `system_prompt.md`, `configure_guardian.schema.json`.
- **Manifest:** will carry the fields the syllabus requires for the common manifest. **[OPEN]** C1 — confirm the required field list against the syllabus.

### 2.4 Permissions and prohibited actions

- **Permitted:** propose exactly one setup, with every field chosen from the engine's options.
- **Prohibited:** raw numeric stats outside the predefined tiers; skills outside the pool; changing damage formulas, cooldown rules, HP during a fight, or rewards; free-form text shown to the player; any tool other than `configure_guardian`.

### 2.5 Validation and fallback

1. The engine checks each field of the proposed setup against the options and the allowed range for this challenger's strength.
2. Valid → the engine builds the Guardian from the setup and starts the fight.
3. Out of range, malformed, or later than the **3 s** deadline → the engine uses the **default Guardian** (the scripted baseline's setup), starts the fight, and records the fallback in the trace.
4. Service unavailable → every attempt uses the default Guardian and the session is flagged.

The allowed range belongs to each Guardian and is the guard against the main design risk, that tuning makes gear pointless. An **open** Guardian has a wide range, so a newcomer can win. A **gated** Guardian has a floor the agent cannot tune below, so it stays very hard without the right stats and equipment. The agent never sees or changes the range; it only chooses inside it. The prototype ships one open Guardian; a gated range is a configuration variant.

### 2.6 Memory policy

- Within a fight: none; the model is not called.
- Across a player's attempts: only the attempt count in the snapshot. **[OPEN]** C2 — whether the previous attempt's outcome (for example, how close the fight was) should also be included.
- Across players: none.

### 2.7 Model, settings, and limits

| Item | Value | Basis |
|---|---|---|
| Model | **[OPEN]** C3 — not selected | Budget uses planning rates |
| Deadline per call | 3 s at fight start, then default Guardian | A short loading wait before the fight; combat never waits. To verify against measured latency |
| Retries | 0 (fallback instead) | A retry would break the deadline |
| Input cap | 2,000 tokens per call | Snapshot plus options plus instructions |
| Output cap | 300 tokens per call | One tool call with three fields |
| Cost per session | **$0.035 estimate / $0.04 cap** | 1 call per attempt × up to 10 attempts = 10 calls. 2,000 in × $1/M + 300 out × $5/M = $0.0035/call = $0.035/session. |
| Spending limit | **[OPEN]** C4 — total evaluation cap | Set before live evaluation |

### 2.8 Failure modes

| Failure | Detection | Response |
|---|---|---|
| Out-of-range or malformed setup | Validator | Default Guardian; trace entry |
| Late response (> 3 s) | Deadline timer | Default Guardian; trace entry |
| Service unavailable | Call error | Default Guardian for the session; session flagged |
| Tuned Guardian makes gear feel pointless | "Gear felt worth it" rating; starter-versus-geared gap closes completely | Narrow the allowed ranges |
| Tuned Guardian is too easy or too hard | Attempts to first win outside the target | Retune ranges and instructions |
| Wins feel unearned | "I earned this" rating | Retune; keep the tuning invisible in the fight itself |

### 2.9 Evaluation and logging

- **Deterministic tests** (rules and invariants): every accepted setup is inside the allowed range; the default Guardian is used on forced timeout and on a forced out-of-range setup; the snapshot contains no blocked fields; no model call happens during combat.
- **Bounded live trials:** a fixed number of calls per build strength, recording validity rate, fallback rate, latency, actual tokens and cost, and the setups chosen. Trial count: **[OPEN]** C5.
- **Trace per attempt:** snapshot hash, allowed options, returned setup, valid / fallback, latency, tokens, fight outcome.
- **Test command and CI workflow [DECIDED]:** `dotnet test tools/CoreTests` (Core rules, no Unity license) and Unity Test Runner → EditMode; `.github/workflows/ci.yml` runs the .NET tests on every push and pull request. See [Unity Setup](unity-setup.md).

### 2.10 Human override

I can switch any session to the scripted baseline with a configuration flag, and I approve every change to the system instructions, the snapshot contents, the setup options, or the allowed ranges.

## 3. Production agents: planned role coverage

All production roles are planned as **Claude Code subagents**, one definition file per role under `.claude/agents/` *(planned)*. Roles may share a model. Coverage is established by executable configurations and evidence of use, which do not exist yet at G1.

| Role | Executable artifact *(planned)* | Purpose | Context | Tools / permissions | Outputs | Recipient and handoff | Evaluation | Human approval boundary |
|---|---|---|---|---|---|---|---|---|
| Producer | `.claude/agents/producer.md` | Connect scope, capacity, dependencies, risks | Project plan, Risk Register, gate requirements | Read repo; edit `docs/project-plan.md` and risk docs | Prioritized tasks, weekly PPP draft, scope-cut recommendations | To me; tasks to role agents | Plan matches gate dates; every task has acceptance criteria | I approve scope, priorities, and anything sent to the instructor |
| Design Lead | `.claude/agents/design-lead.md` | Specify rules, builds, Guardian moves and tells, playtest questions | GDD, playtest log | Read repo; edit `docs/gdd.md` | Design spec, balance proposals, playtest protocol | To Tech Lead, Art, QA | Spec is implementable and testable | I approve all design decisions |
| Tech Lead | `.claude/agents/tech-lead.md` | Implement engine, validator, Guardian interface, builds, deployment | GDD, this document, source | Read/write source, run tests, open PRs | Runnable build, technical decisions, test evidence | To QA and to me | Tests pass in CI; traces present | I approve stack choices and merge PRs |
| Art Lead | `.claude/agents/art-lead.md` | Visual direction and readable tells | GDD Section 9, pillars | Read repo; write asset specs and assets | Visual spec, UI and tell assets with provenance | To Tech Lead | Tells readable in a build (reader Section 5.3 checklist) | I approve style and any licensed asset |
| Narrative and Audio | `.claude/agents/narrative-audio.md` | Minimal Guardian and landmark text, small sound vocabulary | GDD, pillars | Read repo; write content files | Bounded text and audio guidance | To Design Lead and Tech Lead | Consistent with pillars; within scope | I approve all player-facing text |
| QA | `.claude/agents/qa.md` | Check behavior independently, reproduce failures, verify fixes | Build, GDD edge cases, this document | Read repo, run tests; **no write access to source** | Reproducible reports, release checks | To Tech Lead and to me | Reports include build, steps, result | I decide severity and release blocking |
| BI | `.claude/agents/bi.md` | Organize combat logs, latency, cost, and playtest measures | Traces, playtest log | Read logs; write analysis | Measurements with stated limits | To Design Lead, Producer, me | Small-sample limits stated | I decide what findings mean |
| Publishing and Release | `.claude/agents/release.md` | Make the build reachable and reproducible | Build, README | Read repo; edit README and release notes | Run instructions, release checklist | To me | Another person can run the build from instructions | I approve every release and tag |

**Independence.** QA does not review its own work: the Tech Lead agent implements, and the QA agent checks in a separate context without write access to source.

## 4. Handoff format

Every handoff states what to use, what to produce, and how acceptance is judged. Example (planned):

> Design Lead to Tech Lead: "Implement `configure_guardian` so the adaptive Guardian receives only the allowed challenger snapshot and the setup options, and returns one setup. The engine validates it against the allowed range. Include a forced-timeout test, a forced-out-of-range test, and a test that the snapshot contains no blocked fields."

## 5. Configuration versions

| Version | Date | Change | Approved by |
|---|---|---|---|
| 0.1 (Initial) | 2026-11-15 | First planned configuration for G1 | Pending G1 decision |

## 6. Open questions

| # | Question | Needed by |
|---|---|---|
| C1 | Required fields of the common manifest for the extension artifact (check syllabus) | Before the Guardian interface task |
| C2 | Should the snapshot include the previous attempt's outcome, beyond the attempt count? | Before adaptive Guardian task |
| C3 | Runtime model and settings for the Guardian | Before adaptive policy task |
| C4 | Per-session and total evaluation spending limits | Before live evaluation |
| C5 | Number of live trials per build strength | Before live evaluation |
| C6 | ~~Test command and CI workflow path (depends on stack, GDD Q1)~~ **Resolved 2026-11-16** (Section 2.9) | With the first prototype task |
| C7 | Which model each production role uses, and whether roles share one | When the subagent files are written |
| C8 | Whether 3 s at fight start is acceptable at measured latency, and the allowed tuning range per challenger strength (GDD Q11) — tuning range resolved in the [Combat Spec](combat-spec.md), Section 4.6; latency still to measure | Before adaptive Guardian task |
