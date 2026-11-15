# The Artifacts — Project Plan

**Status: Initial (G1).** Update at G2; Final baseline at G3 scope lock. This plan moves into Asana after G1, where owners, acceptance criteria, dependencies, and the full Risk Register follow.
**Owner:** the student, in the Producer role. **Gate Approver:** instructor.

## 1. Gate dates

| Gate | Date | Decision |
|---|---|---|
| First pitch round | Mon Nov 9, 2026 | (done) |
| **G1 Concept** — post due | Sun Nov 15, 2026, 11:59 p.m. | Is this idea clear and achievable enough to justify prototype work? |
| G1 review in class | Mon Nov 16, 2026 | Approve / Hold posted by Tue Nov 17 |
| G1 Braintrust replies (two) | Thu Nov 19, 2026, 11:59 p.m. | |
| **G2 Prototype / G3 Production** (combined review) | **TBD — confirm in syllabus** | Does the core experience have support? Can the agreed game be completed? |
| Alpha / Beta (milestones inside Production) | **TBD — confirm in syllabus** | |
| **G4 Final / Pre-Launch Review** | **TBD — confirm in syllabus** | Is this build ready to release? |
| **G5 Retrospective / Closure** | **TBD — confirm in syllabus** | What was learned; is the archive usable? |

## 2. Goal for the work cycle to G2

By the G2/G3 review, a player can check in (live or simulated), see the dungeon unlocked on PC, and fight one Guardian under both the scripted and adaptive policies; and we will know whether adaptation changes how players win without making the fight feel unfair.

## 3. First tasks through G2

Order reflects dependencies. Combat comes before the visit, because the combat comparison is the experiment and the visit can be simulated.

| # | Task / outcome | Delegated role | Depends on | Done when |
|---|---|---|---|---|
| 1 | Create the GitHub repository; commit Initial GDD, Configuration Document, and this plan | Tech Lead | — | Docs are linked from the G1 post |
| 2 | Choose the stack (GDD Q1) and runtime model (Q9) | Tech Lead recommends; I decide | 1 | Decision recorded in GDD and Configuration Document |
| 3 | Write the production role subagent files in `.claude/agents/` | Producer / Tech Lead | 1 | Each role in the coverage table has an executable file |
| 4 | Specify two builds, skills and cooldowns, Guardian moves, tells, and stats (GDD Q3–Q5, Q11) | Design Lead | — | Spec is complete enough to implement one fight |
| 5 | Implement the real-time combat engine (basic attacks, skill cooldowns, artifact effects) and validator with the **scripted** Guardian | Tech Lead | 2, 4 | One fight is playable start to finish on PC; rule tests pass |
| 6 | Implement the **adaptive** Guardian behind `select_move`, with snapshot filter, next-move request during the current move, 3 s deadline, and legal fallback without pausing | Tech Lead | 5 | Forced-timeout, forced-illegal-move, and no-blocked-fields tests pass |
| 7 | Instrument traces: legality, fallback, latency, tokens, cost | Tech Lead / BI | 6 | Every decision writes a trace entry |
| 8 | Account, quest unlock, and **simulated** visit → PC handoff | Tech Lead | 5 | A simulated visit makes the quest appear on PC and persists |
| 9 | Minimum readable presentation: tells, HP, cooldowns, reward | Art Lead / Narrative and Audio | 5 | A new player can name the coming move from its tell |
| 10 | Deterministic checks and a budgeted live evaluation run | QA / BI | 6, 7 | Results recorded, including failed and unavailable runs |
| 11 | Set fairness and clear-rate thresholds (GDD Q7); write the playtest protocol | Design Lead / BI | 4 | Thresholds written down before any playtest |
| 12 | Observe at least one external player on the core loop; start the Playtest Log | Design Lead / QA | 8, 9, 11 | Observations recorded separately from interpretation |
| 13 | Live mobile GPS check-in at 2–3 landmarks | Tech Lead | 8 | **Only if** tasks 5–12 are on track; otherwise stay simulated |
| 14 | Update GDD, Configuration Document, plan, and Risk Register; record the first playable | Producer | 10, 12 | G2/G3 evidence is linked from the Asana gate task |
| 15 | Draft the scope-lock plan and exclusions for G3 | Producer | 12 | Plan fits remaining time with Alpha, Beta, Gold checks defined |

## 4. Scope

- **Included:** 2–3 Columbia-area locations, mobile visit unlock, one PC dungeon, four artifact rarity tiers, one real-time Guardian in two modes.
- **Excluded:** multiplayer / PvP, open world, paid unlocks, trading, additional dungeon environments.
- **First cut:** simulate GPS visits; keep the unlock-to-combat loop (task 13 is dropped).
- **Trigger for the first cut:** the adaptive Guardian (task 6) is not passing its tests one week before the G2/G3 review. *(Adjust once the G2/G3 date is confirmed.)*

## 5. Initial Risk Profile

| Dimension | Rating | Evidence | Mitigation |
|---|---|---|---|
| IP / Concept | Low | References inform mechanics only; no third-party assets or code | Track provenance and terms for every prototype asset |
| Business / Audience | Medium | The visit-to-PC handoff adds friction | Track how many testers open the dungeon after check-in |
| Technology | **High** | Live calls may stall or return illegal moves, and a real-time fight cannot wait for them; stack and model not yet chosen | Choose the next move during the current one; validate move IDs; 3 s deadline with legal fallback; choose stack first |
| Team | Medium | Individual project; role agents are planned, not yet configured or tested; my own review capacity is the limit | Write and exercise the subagent files early (task 3); QA agent independent of Tech Lead |
| Genre / Mechanics | **High** | Adaptive counters may feel unfair; the tell-and-counter loop is untested | Always telegraph moves; test two builds; set thresholds first; watch clear rates by mode |
| Timeline | **High** | Mobile GPS plus PC plus account is a lot of integration for one person, and real-time combat needs more implementation and tuning than a turn-based fight | Combat first; simulate visits if the comparison slips |

**Do the High ratings share a cause?** Technology and Genre / Mechanics share one: both depend on the unproven live adaptive Guardian. The scripted fallback covers both, since the game stays playable and fair without the model. Timeline has a separate cause, the mobile-to-PC integration, and a separate response, simulated visits. The two causes compound only if both are attempted at once, which is why combat is scheduled first.

## 6. Decision requested at G1

- **Approve authorizes:** build the mobile check-in and PC quest handoff; implement one Guardian with scripted and adaptive policies; instrument move legality, latency, cost, and playtest outcomes.
- **A Hold would need:** reduce to one simulated location and one playable Guardian encounter; show that the unlock reaches PC and that both policies run under the same rules before expanding.
