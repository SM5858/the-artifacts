# The Artifacts — Configuration Document

**Status: Initial (G1).** Next revision: Update at G2/G3. Final at G4.
**Owner:** the student, in the Tech Lead role. **Gate Approver:** instructor.

This document records how agents are configured: the **in-game agent** (the Guardian) and the **production agents** that perform delegated development work. Everything here is **planned**; no configuration exists in the repository yet. Paths marked *(planned)* are where each executable artifact will live. Open questions are marked **[OPEN]** and collected in Section 6.

**Authority.** I remain Executive Producer and Creative Director. Agents recommend; I decide what to propose; the instructor decides gates.

## 1. Two kinds of agent

| | In-game agent | Production agents |
|---|---|---|
| Who | The Guardian's adaptive policy | Role agents run in Claude Code |
| Sees | A filtered combat snapshot of one play session | The repository and docs, per role |
| May do | Return one legal move ID | Read, write, and test per role permissions |
| Never | Sees GPS, private data, or reward rolls; changes state directly | Merges, changes scope, or submits a gate without my approval |

## 2. In-game agent: the Guardian

### 2.1 Purpose

Select the Guardian's next move in a real-time fight so that the fight responds to the current player's build and recent actions, within fixed rules and stats.

### 2.2 World state versus agent knowledge

The engine owns the authoritative world state. The Guardian receives only the allowed combat snapshot.

| Authoritative world state (engine only) | Allowed combat snapshot (sent to the Guardian) | Blocked from agent context |
|---|---|---|
| Quest unlocks, inventory, HP, cooldowns, reward rolls, account and visit records | Visible HP, cooldowns, equipped artifacts and abilities relevant to the fight, a short history of recent actions, the list of legal move IDs | GPS and location history, private player data, hidden reward rolls, the player's not-yet-committed action |

### 2.3 Interface (executable extension artifact)

The Guardian ships as a **function-calling interface**: the model must answer by calling one tool.

- **Tool:** `select_move`
- **Input:** `{ "move_id": <one of legal_move_ids> }`
- **Context per call:** system instructions + the allowed combat snapshot + `legal_move_ids`.
- **Location:** `agents/guardian/` *(planned)* — `manifest.json`, `system_prompt.md`, `select_move.schema.json`.
- **Manifest:** will carry the fields the syllabus requires for the common manifest. **[OPEN]** C1 — confirm the required field list against the syllabus.

### 2.4 Permissions and prohibited actions

- **Permitted:** propose exactly one move ID from the legal list for the Guardian's next move.
- **Prohibited:** proposing moves outside the list; changing stats, damage, cooldowns, HP, or rewards; free-form text shown to the player; any tool other than `select_move`.

### 2.5 Validation and fallback

0. Combat is real-time and never pauses. The engine requests the next move while the Guardian's current move (tell, attack, recovery) is playing.
1. The engine checks the returned `move_id` against the legal list and the Guardian's cooldowns at the moment the move would start.
2. Valid → the engine resolves it with fixed stats.
3. Invalid, malformed, or not back when the current move ends (**3 s** at most) → the engine plays a **legal fallback move** (the scripted policy's next move) without pausing the fight, and records the fallback in the trace.
4. Service unavailable → the fight continues on the scripted policy and the session is flagged.

### 2.6 Memory policy

- Within a fight: the short action history in the snapshot. No other memory.
- Across fights and players: none. **[OPEN]** C2 — whether the Guardian should carry anything across a player's retries.

### 2.7 Model, settings, and limits

| Item | Value | Basis |
|---|---|---|
| Model | **[OPEN]** C3 — not selected | Budget uses planning rates |
| Deadline per decision | 3 s, then fallback | Planning length of one Guardian move cycle (tell, attack, recovery), during which the next move is chosen; the player never waits. To verify against measured latency. **[OPEN]** C8 |
| Retries | 0 (fallback instead) | A retry would break the deadline |
| Input cap | 1,500 tokens per call (target) | Needed to reach the cost target |
| Output cap | 100 tokens per call | One tool call |
| Cost per session | $0.075 estimate / **$0.06 target** | 10 calls/battle × 3 battles = 30 calls. 2,000 in × $1/M + 100 out × $5/M = $0.0025/call = $0.075/session. At 1,500 in: $0.002/call = $0.06/session. |
| Spending limit | **[OPEN]** C4 — per-session and total evaluation cap | Set before live evaluation |

### 2.8 Failure modes

| Failure | Detection | Response |
|---|---|---|
| Illegal or malformed move | Validator | Legal fallback; trace entry |
| Late response (> 3 s) | Deadline timer | Legal fallback; trace entry |
| Service unavailable | Call error | Scripted policy for the fight; session flagged |
| Adaptive play feels unfair | Fairness and winnability ratings, clear rate vs. scripted | Retune move selection instructions; tells stay mandatory |
| Guardian is uniformly harder, not more adaptive | Clear-rate gap by mode | Retune before reading strategy-reuse results |

### 2.9 Evaluation and logging

- **Deterministic tests** (rules and invariants): every resolved move is in the legal list; cooldowns respected; stats never change; fallback fires on forced timeout and on a forced illegal move; snapshot contains no blocked fields.
- **Bounded live trials:** a fixed number of fights per build per policy, recording legality rate, fallback rate, end-to-end decision latency, and actual tokens and cost. Trial count: **[OPEN]** C5.
- **Trace per decision:** fight timestamp, snapshot hash, legal list, returned move, valid / fallback, latency, tokens.
- **Test command and CI workflow:** *(planned)* a single test command and `.github/workflows/ci.yml`. **[OPEN]** C6 — depends on the stack (GDD Q1).

### 2.10 Human override

I can switch any session to the scripted policy with a configuration flag, and I approve every change to the system instructions, the snapshot contents, or the legal-move rules.

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

> Design Lead to Tech Lead: "Implement the Guardian's next-move request so the adaptive policy receives only the allowed combat snapshot and the legal move list, and returns one move ID. The engine validates it. Include a forced-timeout test, a forced-illegal-move test, and a test that the snapshot contains no blocked fields."

## 5. Configuration versions

| Version | Date | Change | Approved by |
|---|---|---|---|
| 0.1 (Initial) | 2026-11-15 | First planned configuration for G1 | Pending G1 decision |

## 6. Open questions

| # | Question | Needed by |
|---|---|---|
| C1 | Required fields of the common manifest for the extension artifact (check syllabus) | Before the Guardian interface task |
| C2 | Does the Guardian carry any memory across a player's retries? | Before adaptive policy task |
| C3 | Runtime model and settings for the Guardian | Before adaptive policy task |
| C4 | Per-session and total evaluation spending limits | Before live evaluation |
| C5 | Number of live trials per build per policy | Before live evaluation |
| C6 | Test command and CI workflow path (depends on stack, GDD Q1) | With the first prototype task |
| C7 | Which model each production role uses, and whether roles share one | When the subagent files are written |
| C8 | Decision cadence in real time (every move or every few seconds) and whether 3 s fits inside one move cycle at measured latency (GDD Q11) | Before adaptive policy task |
