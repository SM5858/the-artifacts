# The Artifacts — Game Design Document

**Status: Initial (G1).** Next revision: Update at G2/G3. Final at G4.
**Owner:** the student, as Creative Director and in the Design Lead role. **Gate Approver:** instructor.

Labels used in this document:

- **[DECIDED]** a choice I have made as Creative Director / Executive Producer.
- **[PROPOSED]** intended behavior, not yet implemented.
- **[OPEN]** an unresolved question. All open questions are collected in Section 13.

Nothing in this document is implemented or verified yet. Implemented and verified labels are added from G2 on.

## 1. The X

> *Visit real-world landmarks to unlock PC dungeons, then use your build to outplay Guardians and earn powerful artifacts.*

## 2. Player and motivation

- **Player:** campus explorers who enjoy RPG buildcraft and learning boss fights. For the prototype, Columbia students who walk past the chosen landmarks anyway.
- **Intended experience:** the pleasure of mastering a readable opponent with a build that feels like your own, with a real place as the reason the fight exists.
- **Observable hypothesis:** after earning a first artifact, players voluntarily retry a Guardian or seek another artifact, without being prompted.
- **Secondary signal:** the share of testers who open the PC dungeon after checking in on mobile (the handoff is the main friction point).

## 3. Platform and genre

- **Genre:** location-based RPG with real-time boss combat built on skill cooldowns.
- **Platform:** mobile for the visit check-in; PC for dungeon and combat. One account links the two.
- **Engine / stack:** **[OPEN]** not selected. See Q1.

## 4. Core loop

| Step | Player / game | Detail |
|---|---|---|
| Act | **Visit** | Check in on mobile at a Columbia-area landmark. |
| Respond | **Unlock** | A verified visit adds a dungeon quest to the account. |
| Interpret | **Fight** | On PC, read the Guardian's tells and use the build to earn an artifact. |
| Next action | **Equip and return** | Equip the reward, then retry the fight or seek another unlock. |

Inside a fight, the moment-to-moment loop runs in real time: the Guardian telegraphs a move (tell) → the player reacts with a basic attack, a skill off cooldown, or an artifact effect → the engine resolves hits and starts cooldowns → feedback shows what happened → the player reads the next tell.

## 5. Pillars

1. **Place unlocks play.** A brief real-world visit opens a quest on PC. The visit stays short; the play happens on PC.
2. **Combat earns the artifact.** Players win by making effective choices with their build. Rewards are never bought or granted for the visit alone.
3. **The Guardian responds.** Readable counters reward players who adjust their tactics.

## 6. Mechanics [PROPOSED]

### 6.1 Visit and unlock

- The mobile check-in confirms the player is within a radius of a registered landmark and records the visit against the account.
- A verified visit adds that landmark's dungeon quest to the account. The unlock persists.
- Radius, cooldown between check-ins, and anti-spoofing level: **[OPEN]** Q2.
- **First cut [DECIDED]:** if live GPS verification slips, visits are simulated (a button or code) while account persistence and the PC unlock are kept.

### 6.2 Build

- A build is the character's abilities plus equipped artifacts.
- The prototype tests at least two builds with different winning tactics: a **shield** build (bait a counter, punish after block) and a **mage** build (evade, punish the recovery window).
- Ability lists, stats, and artifact effects: **[OPEN]** Q3.

### 6.3 Combat

- **Real-time [DECIDED]**, one player versus one Guardian. The fight never pauses.
- Both sides act through **basic attacks**, **skills with cooldowns**, and (for the player) **artifact effects**. The Guardian's skills have cooldowns too.
- Each Guardian move has a **tell** shown before the player commits, and a **recovery window** after it resolves.
- Player choices fall into three intents: commit (attack or spend a skill), defend or evade, switch tactics. Exact skill list, cooldown lengths, and input scheme: **[OPEN]** Q3.
- All damage, cooldowns, and stats are fixed and resolved by the engine. The Guardian never changes numbers; it only chooses which legal move to use.
- Attempt cap per quest and fight length target: **[OPEN]** Q4. Planning assumption for budgets: about 10 Guardian move decisions per battle.

### 6.4 Guardian (in-game agent)

- One Guardian, two policies with **identical stats, move list, tells, and reward rules**:
  - **Scripted baseline:** a fixed move sequence.
  - **Adaptive Guardian:** selects one move ID from the engine's legal-move list, based on a filtered combat snapshot.
- Because combat is real-time, the adaptive Guardian chooses its **next** move while its current move (tell, attack, recovery) is playing. The engine validates the selected move against the current fight state and the Guardian's cooldowns. If the answer is invalid, or has not arrived when the current move ends (3 s at most), the engine plays a legal fallback move. The player never waits on the model.
- How often the Guardian decides (every move, or every few seconds): **[OPEN]** Q11.
- What the Guardian may and may not know is specified in the Configuration Document, Section 2.

### 6.5 Rewards and progression

- Winning a fight grants an artifact from one of **four rarity tiers**.
- Reward rolls are made by the engine and are hidden from the Guardian.
- Drop table and whether rarity affects power or only variety: **[OPEN]** Q5.
- Progression in the prototype is: unlock → win → equip → retry or seek another unlock. No levels or currencies.

## 7. Win, failure, and edge cases [PROPOSED]

- **Win:** Guardian HP reaches zero. The artifact is granted and stored on the account.
- **Failure:** player HP reaches zero or the attempt cap is reached. The quest stays unlocked; the player may retry.
- **Edge cases to specify before G2:**
  - Model timeout or illegal move → legal fallback, fight continues without a pause, fallback recorded in the trace.
  - Model service unavailable for the whole fight → the scripted policy runs and the session is flagged.
  - Player closes the PC client mid-fight → **[OPEN]** Q6 (resume or forfeit).
  - Check-in succeeds on mobile but the PC client does not show the quest → handoff failure; must be detectable in logs.
  - Duplicate check-in at the same landmark → no duplicate quest.

## 8. Dynamics and intended aesthetics

- **Intended dynamic:** shared guides can teach tells and counters, but victory still depends on how the player responds in this fight. Different builds should win in different ways.
- **Risk to the dynamic:** adaptive counters may feel unfair. Mitigated by always telegraphing moves, keeping stats fixed, and measuring perceived fairness and clear rate.
- **Aesthetic targets:** challenge (mastery of a readable opponent), discovery (a place has something behind it), expression (my build, my tactic).

## 9. Controls and feedback [PROPOSED]

- **Mobile:** one check-in action; confirmation of the unlocked quest.
- **PC:** quest list; in combat, real-time inputs for movement or evasion, basic attack, skills, and artifact effects. Input scheme: **[OPEN]** Q3.
- **Feedback the player must be able to read:** quest status, the Guardian's tell for the coming move, HP changes, their own skill cooldowns, and the reward.
- Presentation scope is the minimum needed for a fair test: readable tells come before visual polish.

## 10. Content scope

| | |
|---|---|
| **Included [DECIDED]** | 2–3 Columbia-area locations, mobile visit unlock, one PC dungeon, four artifact rarity tiers, one real-time Guardian in two modes (scripted, adaptive). |
| **Excluded [DECIDED]** | Multiplayer / PvP, open world, paid unlocks, trading, additional dungeon environments. |
| **First cut [DECIDED]** | Simulate GPS visits; keep the unlock-to-combat loop. |
| **Hold fallback** | One simulated location, one playable Guardian encounter, both policies running under the same rules. |

## 11. Agent hypothesis and playtest question

- **Hypothesis:** adaptive move selection increases the variety of winning tactics and makes a shared, copied sequence less reliable, while fights stay fair and winnable.
- **Scripted baseline:** fixed move sequence with the same stats, moves, tells, and reward rules.
- **What the player loses with only the scripted baseline:** with a fixed script, a player can win by memorized timing or a copied sequence, and has fewer reasons to adapt the build or tactics. The fight stops responding to *this* player.
- **Playtest question:** will adaptation diversify winning strategies and make a shared sequence less reliable while keeping fights fair and winnable?
- **Measures:**
  - *Strategy diversity:* distinct winning tactics by build, from combat logs.
  - *Strategy reuse:* clear rate with a shared sequence versus free adaptation, in each mode.
  - *Fairness and achievability:* fairness and perceived-winnability ratings, clear rate, attempts to first win.
- **Design of the comparison:** counterbalance which Guardian a player sees first; at least two builds; same encounter settings in both modes; same build-compatible guide within each build; fixed attempt cap. Record the clear-rate gap by mode so a uniformly harder boss does not count as evidence that strategy reuse declined.
- **Thresholds:** acceptable fairness and clear-rate thresholds are set before testing. **[OPEN]** Q7.

## 12. References and attribution

| Reference | Specific moment or mechanic | Borrowed | Changed | Terms |
|---|---|---|---|---|
| *Pokémon GO* | PokéStop spin: a brief check-in at a real landmark | The short landmark interaction | The main encounter moves to PC; the visit only unlocks | Proprietary. Design reference only; no assets or code used. |
| *Dark Souls* | A boss telegraphs an attack and leaves a recovery opening | The readable tell and punish window | Stays real-time, but the fight is paced by basic attacks, skill cooldowns, and artifact effects in my own combat rules | Proprietary. Design reference only; no assets or code used. |

- Cover illustration in the pitch deck: AI-generated concept art created for the pitch.
- Prototype assets and code will be original or used under documented licenses, with terms recorded in this section before use.
- **Concepts considered before this one:**
  - *AI Mafia* — simpler to prototype; set aside because The Artifacts connects real-world discovery with RPG progression.
  - *Conducting game* — the player conducts with a trackpad or mouse, shaping each part's dynamics (forte / piano) and nudging the rhythm, with an AI agent evaluating the performance. Set aside for two reasons: music licensing, and, even with rights cleared, the cost of obtaining a separate line for every instrument part.

## 13. Open questions

| # | Question | Needed by | Deciding role |
|---|---|---|---|
| Q1 | Engine / stack for the PC client, the mobile check-in, and the shared account store | Before first prototype task | Tech Lead recommends; I decide |
| Q2 | Check-in radius, cooldown, and how much anti-spoofing the prototype needs | Before mobile check-in task | Tech Lead / Design Lead |
| Q3 | Skill lists, cooldown lengths, stats, artifact effects, and the input scheme for the two builds | Before combat implementation | Design Lead |
| Q4 | Attempt cap and target fight length in seconds (budget assumes ~10 Guardian decisions) | Before combat implementation | Design Lead |
| Q5 | Drop table for four rarity tiers; whether rarity changes power or only variety | Before G2 playtest | Design Lead |
| Q6 | Mid-fight disconnect: resume or forfeit | Before G2 playtest | Design Lead / Tech Lead |
| Q7 | Fairness and clear-rate thresholds that count as "fair and winnable" | Before the first comparison playtest | Design Lead / BI |
| Q9 | Runtime model for the Guardian (budget uses planning rates) | Before adaptive policy task | Tech Lead recommends; I decide |
| Q10 | Which 2–3 landmarks, and whether any are indoors (GPS accuracy) | Before mobile check-in task | Design Lead |
| Q11 | Guardian decision cadence in real time: every move or every few seconds, and how long a tell must last to cover model latency | Before adaptive policy task | Design Lead / Tech Lead |
