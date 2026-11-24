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

- **Player:** travelers who enjoy RPG buildcraft and learning boss fights. They visit landmarks on a trip, and play later on PC at home or at their lodging.
- **Prototype stand-in:** Columbia students and Columbia-area landmarks. This tests the visit-to-PC link and the combat, not the travel context itself.
- **Intended experience:** the pleasure of mastering a readable opponent with a build that feels like your own, with a real place as the reason the fight exists.
- **Observable hypothesis:** after earning a first artifact, players voluntarily retry a Guardian or seek another artifact, without being prompted.
- **Secondary signal:** the share of testers who open the PC dungeon after checking in on mobile (the handoff is the main friction point).
- **Differentiator [DECIDED 2026-11-17, extended 2026-11-19]:** in most RPGs a character grows through skill, play time, or money (in-game purchases, experience boosters). Here, real-world travel is another axis of growth, in two ways: **power**, because the top tier (artifacts) is earned only by visiting places (Section 6.6); and **knowledge**, because each dungeon is built from its place's history, myths, and features, so knowing the place helps beat its Guardian (Section 6.7).

## 3. Platform and genre

- **Genre:** location-based RPG with real-time boss combat built on skill cooldowns.
- **Platform:** mobile for the visit check-in while out; PC for dungeon and combat later, at home or lodging. One account links the two.
- **Account [DECIDED 2026-11-20]:** a username and password (Unity Authentication), the same on both devices; the profile (unlocks, visits, artifacts, equipment, per-Guardian results) is stored in Cloud Save. Offline accounts on one computer exist for development and playtests without the network.
- **Engine / stack [DECIDED 2026-11-16]:** Unity 6.3 LTS (3D URP), one project for both the PC client and the mobile check-in, chosen for the release goal. See [Unity Setup](unity-setup.md). Backend **[DECIDED 2026-11-16]:** Unity Gaming Services (Authentication with account linking for mobile and PC, Cloud Save / Economy for unlocks and artifacts, Cloud Code in C# so server logic can reuse the engine-free Core).

## 4. Core loop

| Step | Player / game | Detail |
|---|---|---|
| Act | **Visit** | Check in on mobile at a landmark visited on a trip (Columbia-area landmarks in the prototype). |
| Respond | **Unlock** | A verified visit adds that landmark's themed dungeon to the account. |
| Interpret | **Fight** | Back home or at the lodging, on PC, read the Guardian's tells and use the build to earn an artifact. |
| Next action | **Equip and return** | Equip the reward, then retry the fight or seek another unlock. |

Inside a fight, the moment-to-moment loop runs in real time: the Guardian telegraphs a move (tell) → the player reacts with a basic attack, a skill off cooldown, or an artifact effect → the engine resolves hits and starts cooldowns → feedback shows what happened → the player reads the next tell.

## 5. Pillars

1. **Place unlocks play.** A brief visit to a real landmark opens a dungeon themed on that place. The visit stays short so the trip stays about the place; the play happens later on PC.
2. **Combat earns the artifact.** Players win by making effective choices with their build. Artifacts are never bought or granted for the visit alone; the visit opens the fight, and the fight earns the artifact.
3. **The Guardian adapts.** It tunes itself to the challenger, so a newcomer who unlocks a dungeon can still win in a few attempts.
4. **The place is the puzzle [DECIDED 2026-11-19].** Many Guardians' mechanics come from their real place. What a traveler learns there, or already knows, is an advantage in the fight. Not every Guardian has one: some are pure combat-sense fights (2026-11-20).

## 6. Mechanics [PROPOSED]

### 6.1 Visit and unlock

- The mobile check-in confirms the player is within a radius of a registered landmark and records the visit against the account.
- A verified visit adds that landmark's themed dungeon quest to the account. The unlock persists, so the player can fight hours or days later from anywhere.
- Radius, cooldown between check-ins, and anti-spoofing level: **[OPEN]** Q2.
- **First cut [DECIDED]:** if live GPS verification slips, visits are simulated (a button or code) while account persistence and the PC unlock are kept.
- **The mobile check-in is its own small game [DECIDED 2026-11-20].** Much lighter than the PC game, with Pokémon GO-level graphics: the player walks a map, and near a landmark meets a dungeon entrance or an NPC that unlocks the dungeon. It writes the same account profile the PC reads. Until it exists, the PC quest list has a **Simulate visit (dev)** button that makes the same check-in.
- **Story card [PROPOSED text, 2026-11-20]:** each unlock shows the landmark's story card, which also appears in the PC quest list. It tells the place's lore and carries the hint to its mechanic (Section 6.7), so a player who does not know the place can still learn it. The cards are in `Content.cs` (`StoryCard`).
- Rules in code (`Core/Account/Progress.cs`, tested): a repeat visit is recorded but adds no second quest; every visit is kept with its time and source, and the PC logs the profile it loads, so a check-in that never reached the PC can be traced (Section 7); fights need an unlocked dungeon; a win stores the rolled artifact (repeat drops stack) and resets the failed attempts; a loss adds one; equipping takes owned artifacts only, one per slot (two trinkets).
### 6.2 Build

- A build is the character's abilities plus equipped artifacts.
- **Character stats [DECIDED 2026-11-19]:**

  | Stat | Effect | Raised by |
  |---|---|---|
  | Strength | Physical attack power | Levels, items |
  | Intelligence | Magic attack power | Levels, items |
  | Agility | Cooldown reduction | Levels, items |
  | Def | Physical defense | Levels, items |
  | Res | Magic resistance | Levels, items |
  | HP | Health | Levels, items |
  | MP | Mana pool | Levels, items |
  | Precision | Critical-hit chance in combat; chance of a higher grade when crafting | Levels, items |
  | Speed | Movement speed | **Artifacts and crafted items only**, never levels |

  The full game has character levels that raise the first eight stats. The prototype has no levels (Section 6.5); test builds use fixed base stats. Formulas and damage types: Combat Spec Sections 2 and 3 **[DECIDED 2026-11-19]**. Players can learn magic skills without a class; learned magic costs MP, artifact skills do not. The prototype has one learned magic skill, Lightning Bolt.
- The prototype tests at least two builds of different strength: a **starter** build (a new account, no artifacts) and a **geared** build (artifacts equipped, stronger skills).
- Ability lists, stats, and artifact effects: **[DECIDED]** in the [Combat Spec](combat-spec.md), Section 3 and 5.

### 6.3 Combat

- **Real-time [DECIDED]**, one player versus one Guardian. The fight never pauses.
- Both sides act through **basic attacks**, **skills with cooldowns**, and (for the player) **artifact effects**. The Guardian's skills have cooldowns too.
- Each Guardian move has a **tell** shown before the player commits, and a **recovery window** after it resolves.
- Player choices fall into three intents: commit (attack or spend a skill), defend or evade, switch tactics. Exact skill list, cooldown lengths, and input scheme: **[DECIDED]** in the [Combat Spec](combat-spec.md).
- All damage and cooldowns are resolved by the engine. The Guardian's stats, skills, and strategy are set once before each attempt (Section 6.4) and do not change during the fight.
- Attempt cap and fight length target **[DECIDED]**: 10 attempts per session per mode; 90–120 s per attempt; no fight timer. See [Combat Spec](combat-spec.md), Sections 1, 2 and 6.

### 6.4 Guardian (in-game agent)

- One Guardian, two modes with the **same skill pool, tells, and reward rules**:
  - **Scripted baseline:** one fixed Guardian for everyone, with the same stats, skills, and strategy in every fight.
  - **Adaptive Guardian:** before each attempt, the agent tunes the Guardian to the challenger. It proposes a **setup**: a stat tier, a skill set, and a strategy, each chosen from options the engine defines.
- The agent sees a filtered challenger snapshot: the challenger's stats, skills, equipped artifacts, and whether easing is unlocked. It does **not** see the attempt count, so it cannot quietly weaken the Guardian after each loss (2026-11-20, from G1 review feedback).
- **Easing is a single step [DECIDED 2026-11-20].** The adaptive Guardian matches the challenger's build, not their losses: the same build meets the same Guardian. So that no one fails forever, after enough failed attempts on a Guardian (since the last win) the engine unlocks **one** easing step: the allowed range gains one tier below it, and the agent picks one tier lower (or, already at the lowest tier, more readable skills). The threshold depends on the best artifact in that Guardian's pool: **10** failures when it is Heirloom or lower, **50** when it has a Treasure or Mythic. Prototype: Alma Mater 10; Book Keeper and Columbia Lion 50. The scripted baseline never eases. With the 10-attempt session cap, easing never unlocks within one playtest session.
- **Adaptation is shown [DECIDED 2026-11-20].** At fight start the adaptive Guardian says why it changed: to a strong challenger (gear score 6+, the top tuning band) it says a line such as "At last, a worthy challenger. I will not hold back." and fights at the higher range; when easing unlocks it says "You keep coming back... Very well, I will ease my guard." [line PROPOSED]. The fixed lines are for the prototype; letting the agent write the line is a later option.
- The engine validates the proposed setup against the allowed ranges for that challenger. A setup that is out of range, malformed, or later than 3 s is replaced by the **default Guardian** (the scripted baseline's setup).
- The model is called once at fight start. During the real-time fight the Guardian follows its chosen strategy with no model calls, so the player never waits on the model in combat.
- **Each Guardian has its own tuning range [DECIDED].** Not every Guardian adjusts all the way down to the challenger:
  - An **open Guardian** has a wide range. A brand-new player can win its artifact after a few attempts.
  - A **gated Guardian** has a floor. With the right base stats and equipment it is manageable; without them it is very hard. Its artifacts are the reason to build up a character.
- **Prototype Guardians [DECIDED 2026-11-19]:** three open Guardians, one per landmark, each a different creature type so type-conditional traits can be tested:

  | Landmark | Guardian | Creature type | Look |
  |---|---|---|---|
  | Alma Mater statue | Alma Mater | Undead | A robed lich (changed 2026-11-20 from Construct); high magic resistance |
  | Columbia Lion statue | Columbia Lion | Beast | A lion |
  | Columbia bookstore | Book Keeper | Construct | A plain golem (changed 2026-11-20 from Undead) |

  All three share the engine's skill pool (Combat Spec 4.2); they differ by creature type, immunities, tier range, skill choice, name, and look, so no new skills are needed. The scripted-versus-adaptive comparison (Section 11) runs on **one** of them, so the test conditions stay the same; the other two are content. The comparison runs on the **Columbia Lion** **[DECIDED 2026-11-19]** (Q16).
- A gated variant is the same Guardian with a higher floor (a configuration change, not new content); it is tested only if time allows.
- Allowed ranges per Guardian, the list of stat tiers, the skill pool, and the strategy list: **[DECIDED]** in the [Combat Spec](combat-spec.md), Section 4. Enrage is a per-Guardian property, off for the prototype Guardian, and the agent cannot change it.
- What the Guardian may and may not know is specified in the Configuration Document, Section 2.

### 6.5 Rewards and progression

- Winning a fight grants an artifact from one of **four artifact grades**: Relic, Heirloom, Treasure, Mythic (lowest to highest).
- Reward rolls are made by the engine and are hidden from the Guardian.
- Drop table and rarity **[DECIDED]**: rarity changes both power and variety; first win on a Guardian is at least Heirloom when the landmark offers one, otherwise its highest grade. See [Combat Spec](combat-spec.md), Section 5.
- **Landmark loot pools [DECIDED 2026-11-19]:** each artifact has a fixed grade and belongs to one landmark. A landmark offers several artifacts of different grades; a win rolls the grade, then gives that landmark's artifact of that grade. So a player who wanted Columbia Lion's Fang (Heirloom) may instead get Columbia Lion's Pelt (Mythic). Prototype pools are in Combat Spec Section 5.3.
- Progression in the prototype is: unlock → win → equip → retry or seek another unlock. No levels or currencies.

### 6.6 Item economy: general items and artifacts (full game) [DECIDED 2026-11-17]

- **General items** come from ordinary play with no travel: field drops, shops, crafting, and event rewards. A player who never travels can still build a strong character through time or money.
- **General-item grades [DECIDED 2026-11-17]**, lowest to highest. Grade names describe quality, not source, because crafting reaches every grade.

  | Grade | Typical non-crafted source |
  |---|---|
  | Standard | Shops |
  | Fine | Shops |
  | Superior | Bosses in quests and other in-game content (no travel); stronger than Fine |
  | Masterpiece | Crafting only: high crafting skill plus rare materials |

- **Crafting, no fixed classes [DECIDED 2026-11-17]:** the full game has no fixed class system. Any player can raise a crafting skill; a highly skilled crafter works as a blacksmith and can make every general grade from Standard to Masterpiece, with the grade set by skill level and material quality. Skill thresholds and materials per grade: not yet designed.
- **Artifacts** are a separate category (Legendary-class power), not a grade on the general ladder. They are earned only by visiting a real landmark (unlock) and beating its Guardian on PC (Pillar 2).
- **Every artifact has a trait [DECIDED 2026-11-17].** A trait is something general items never have, for example a multiplicative stat boost, or a skill the player can use only while the artifact is equipped. Higher grades carry stronger traits; Treasure and Mythic traits change how an ability is used (Combat Spec Section 5.2).
- **Masterpiece–artifact boundary [DECIDED 2026-11-17]:** Masterpiece overlaps the lower artifact grades on raw stats, so a player who never travels still has a reason to raise a crafting skill. A Masterpiece's stats reach just below the Heirloom level, depending on crafting skill and materials (budget below). A Masterpiece never has a trait. Stats can overlap; traits are what travel adds, and the strongest traits (Treasure, Mythic) are out of the crafter's reach.
- **Artifact grades:** Relic → Heirloom → Treasure → Mythic. The top artifact for a slot is a step above the best general item for that slot: better, but not so far above that non-travelers are shut out of content.
- **Artifacts need not be equipment.** The full game may include very strong consumable artifacts. The prototype uses equippable artifacts only.
- **General-item stat budget [DECIDED 2026-11-19] (Q12):** each item has stat points; one point is 1 Strength, Intelligence, Agility, Def, Res, or Precision, or 10 HP or 10 MP, and Speed (crafted items only) costs 2 points per Speed.

  | Grade | Points per item |
  |---|---|
  | Standard | 5 |
  | Fine | 10 |
  | Superior | 15 |
  | Masterpiece | 20–23, rising with crafting skill and materials |

  The budget is set by one rule: **the strongest single hit (a critical Overpower, the ultimate on key R) may take at most half of a T1 Guardian's HP.** Eight top Masterpieces with every point in Strength give 224 Strength, a 2688 hit against T1's 5500 HP (49%). CI checks this rule, and that every Heirloom-or-better artifact has more points than the best Masterpiece. **Artifacts may exceed this cap [DECIDED 2026-11-19]:** it binds general items only; an artifact's stats and traits (for example Columbia Lion's Fang against a Beast) can push the strongest hit past half, as a reward for travel. Artifacts stay bounded by the ×2 per-artifact multiplier cap (Combat Spec 5.4). For comparison, the prototype artifacts are worth 10–20 points (Relic), 25 (Heirloom), 33 (Treasure), and 47 (Mythic), plus their traits.
- **Prototype scope [DECIDED 2026-11-17] (Q13):** the prototype builds no general-item sources (no drops, shop, crafting, or events). General gear appears only as a fixed loadout given to the test builds. Its exact stats wait on the Q12 budgets; until then the Combat Spec builds stand.

### 6.7 Place-based Guardian mechanics [DECIDED 2026-11-19 as direction]

- A Guardian may carry a mechanic taken from its place's history, myth, or physical features. Not every Guardian does: some are fought on combat sense alone (reading tells, positioning, build), so a player who prefers that has fights of their own **[DECIDED 2026-11-20]**. Example: at the site of Troy, the spirit of Achilles appears as a giant Guardian that takes damage **only at the heel**. A player who knows the story goes for the ankle at once; one who does not has to discover it.
- Artifacts follow the same idea (for example, Athena's Shield from a Greco-Roman dungeon petrifying attackers, Section 5.4 of the Combat Spec).
- Building blocks the engine needs: **weak points** (damage only from a direction or body zone, or much more there), **immunities** (exist: status immunities, Def/Res per Guardian), and **lore hints**.
- **Fairness [PROPOSED]:** knowledge is an advantage, never a hard gate. Every lore mechanic must be learnable in the game too, for example a short story card at the mobile check-in and a visual hint in the fight (the heel glows when struck), so a player without the background can still win after a few attempts. The adaptive Guardian cannot change a lore mechanic; it belongs to the Guardian, like enrage and immunities.
- **Prototype mechanics (Q18) [DECIDED 2026-11-20]:** the Book Keeper and Alma Mater have a lore mechanic; the Columbia Lion does not.
  - **Book Keeper [DECIDED 2026-11-19]:** the keeper fears for its books. Three bookshelves stand behind it; a **fire** spell on a shelf burns it and puts the Book Keeper in **Fear** for 6 s, which cancels its current skill and stops all its attacks; the panicked keeper is also open, so player attacks get the opening crit bonus (+50%) for those 6 s. Each shelf burns once per fight, so the player picks when to spend them (best during a dangerous tell). Other spells do nothing to the shelves, so a player who tries can find out. Fire comes from **Fire Bolt**, a new spell (key F) that anyone can learn (Combat Spec 3.2). Hitting the body still works, so the lore is an advantage, not a gate.
  - **Alma Mater [DECIDED 2026-11-20]:** the small owl hidden in the folds of the statue's robe (a campus legend) peeks out now and then. While it is out, attacks aimed at the owl deal **double damage**; aimed at it while it hides, they hit the body as usual. Timing [PROPOSED]: first at 12 s, then every 15 s, out for 3 s each time. A player who knows the legend looks for it; one who does not may notice the owl and try it.
  - **Columbia Lion:** no lore mechanic; a pure combat-sense fight. It also hosts the comparison playtest (Q16), so no lore mechanic affects that comparison.

## 7. Win, failure, and edge cases [PROPOSED]

- **Win:** Guardian HP reaches zero. The artifact is granted and stored on the account.
- **Failure:** player HP reaches zero or the attempt cap is reached. The quest stays unlocked; the player may retry.
- **Edge cases to specify before G2:**
  - Model timeout (over 3 s at fight start) or out-of-range setup → the default Guardian is used, the fight starts, and the fallback is recorded in the trace.
  - Model service unavailable → the default Guardian is used for every attempt and the session is flagged.
  - Player closes the PC client mid-fight → forfeit; the attempt is not counted **[DECIDED]** (Q6).
  - Check-in succeeds on mobile but the PC client does not show the quest → handoff failure; must be detectable in logs.
  - Duplicate check-in at the same landmark → no duplicate quest.

## 8. Dynamics and intended aesthetics

- **Intended dynamic:** public guides assume prerequisites (stats, skill levels, items, equipment). Here, a player at any strength, including a brand-new one, meets a Guardian tuned to them and can win a good artifact after a few attempts.
- **Risk to the dynamic:** if the Guardian always matches the challenger, gear may feel pointless and wins unearned, which conflicts with Pillar 2. Mitigated by giving each Guardian its own tuning range: open Guardians welcome newcomers, gated Guardians keep a floor that only stats and equipment overcome, so the best artifacts still require building a character. Checked with "I earned this" and "gear felt worth it" ratings.
- **Aesthetic targets:** challenge (mastery of a readable opponent), discovery (a place has something behind it), expression (my build, my tactic).

## 9. Controls and feedback [PROPOSED]

- **Mobile:** a light map game (Section 6.1): walk to a landmark, meet its dungeon entrance or NPC, confirm the unlock, and read the story card.
- **PC:** sign in, quest list (unlocked dungeons with their story cards; locked ones name the landmark to visit), equipment screen (owned artifacts, equip or unequip, the build's stats), and after a fight, back to the list; in combat, real-time inputs for movement or evasion, basic attack, skills, and artifact effects. Input scheme **[DECIDED, changed 2026-11-20]**: tab-target; skills on Q W E R (physical) and T F (magic); A toggles auto-attack; artifact skills on a six-slot bar (1, 2, 3, 4, C, V) whose order the player sets; right-click to move out of telegraphs. See [Combat Spec](combat-spec.md), Section 3.2.
- **Feedback the player must be able to read:** quest status, the Guardian's tell for the coming move, HP changes, their own skill cooldowns, and the reward.
- Presentation scope is the minimum needed for a fair test: readable tells come before visual polish.
- **Visual direction [DECIDED 2026-11-20]:**
  - **Equipment changes the character's look.** The player character is built from parts per slot, so every equipped artifact and general item is visible. [PROPOSED] Trinkets are too small to show and get no mesh; the other six slots (helm, armor, left hand, right hand, gloves, boots) do. Prototype need: a base look for each slot plus the six Columbia artifacts.
  - **Guardians are living statues**, finished in stone or bronze: Alma Mater a lich **[concept DECIDED 2026-11-23: "Verdigris Lich Scholar" turnaround]**: a tall skeletal scholar in verdigris bronze, a torn blue-green gown under a mantle draped over both shoulders, a laurel wreath with a small finial over hair gathered in a bun, glowing cyan eyes, a crowned sceptre in the right hand, a book hanging from the belt, and the owl perched on the sash (Section 6.7), the Columbia Lion a lion **[concept DECIDED 2026-11-23: "Ancient bronze lion guardian" sheet]**: a tall, lean, muscular lion in verdigris bronze standing square and roaring, a great mane of long flowing locks over the head, chest and shoulders, a laurel collar with a temple medallion on the chest, glowing cyan eyes, long curved claws, and a long tail ending in a flame-like tuft, the Book Keeper a golem among bookshelves **[concept DECIDED 2026-11-23: "Ancient marble book-keeper golem" sheet]**: a broad cream-marble golem whose chest is a great closed book with laurel reliefs and a keyhole lock, a small stern face under a laurel crest with an open book rising behind the head, temple-emblem pauldrons over stone-leaf tassets, fluted bracers, a belt of small leather books with a temple buckle and ribbon bookmarks, and legs like Ionic columns.
  - **Art style [DECIDED 2026-11-20]: realistic PBR** (Dark Souls-like), in URP. Stone and bronze statues suit it best. It is the costliest style for a modular, gear-showing character and its animations, so readability comes first: tells, telegraphs, and HP stay clear before any polish, and the graybox primitives remain the fallback until each model is in.
  - **Asset sources [DECIDED 2026-11-20]: CC0 first, paid only for gaps.** CC0 files can be committed to this public repository (`Assets/_Project/Art`), for example PBR stone, bronze, and floor materials and HDRIs from Poly Haven or ambientCG. Realistic modular characters and creature models are rarely CC0, so the player character and the three Guardians are paid Asset Store packages (approved 2026-11-20), kept in `Assets/ThirdParty/` (git-ignored, never committed); a fresh clone then lacks them and must still run on the graybox fallback. Every file's source and license is recorded in Section 12 before it is committed.

## 10. Content scope

| | |
|---|---|
| **Included [DECIDED]** | 3 Columbia landmarks (Alma Mater statue, Columbia Lion statue, Columbia bookstore), mobile visit unlock, one dungeon and one real-time **open** Guardian per landmark (changed 2026-11-19 from one), four artifact grades. One Guardian runs in two modes (scripted, adaptive) for the comparison. A gated variant is a stretch test, not a commitment. |
| **Excluded [DECIDED]** | Multiplayer / PvP, open world, paid unlocks, trading, additional dungeon environments. |
| **First cut [DECIDED]** | Simulate GPS visits; keep the unlock-to-combat loop. |
| **Full vision, not prototype scope** | Landmarks anywhere a player travels, each with a dungeon, Guardian, and artifacts themed on that place, collected over many trips. A general-item economy (field drops, shops, crafting, events) with grades Standard to Masterpiece, classless crafting, and artifacts as the travel-only category above it (Section 6.6). Consumable artifacts. |
| **Hold fallback** | One simulated location, one playable Guardian encounter, both policies running under the same rules. |

## 11. Agent hypothesis and playtest question

- **Hypothesis:** tuning the Guardian's stats, skills, and strategy to the challenger lets players of different strength, including a brand-new one, each win within a few attempts, while the fight still feels fair and the artifact still feels earned.
- **Scripted baseline:** one fixed Guardian for everyone, with the same stats, skills, and strategy.
- **What the player loses with only the scripted baseline:** a new player meets a fight built for someone else's stats and gear. Public guides apply to everyone in the same way, and their prerequisites decide who can win.
- **Playtest question:** can players with different builds, including a new one, each win within a few attempts and still feel the win was earned?
- **Measures:**
  - *Attempts to first win:* starter versus geared build, in each mode, from combat logs.
  - *Clear-rate gap:* starter versus geared clear rate under one attempt cap, in each mode.
  - *Fairness and earned reward:* fairness, winnability, and "I earned this" ratings, and whether gear still feels worth it.
- **Design of the comparison:** counterbalance which mode a player sees first; a starter and a geared build; the same attempt cap in both modes; the same skill pool and reward rules.
- **Reading the result:** the adaptive mode should narrow the starter-versus-geared gap without closing it. If the gap closes completely, or "gear felt worth it" drops, the open Guardian's range is too wide and needs retuning. Across the full game, gear keeps its value through gated Guardians, which the prototype does not have to prove.
- **Thresholds [DECIDED]:** median attempts to first win ≤ 5 for both builds in adaptive mode; mean ≥ 4/5 on fairness and "I earned this"; geared testers' "gear felt worth it" no lower than in scripted mode. See [Combat Spec](combat-spec.md), Section 6.

## 12. References and attribution

| Reference | Specific moment or mechanic | Borrowed | Changed | Terms |
|---|---|---|---|---|
| *Pokémon GO* | PokéStop spin: a brief check-in at a real landmark | The short landmark interaction | The main encounter moves to PC; the visit only unlocks | Proprietary. Design reference only; no assets or code used. |
| *Dark Souls* | A boss telegraphs an attack and leaves a recovery opening | The readable tell and the opening after it | Stays real-time, but the fight is paced by basic attacks, skill cooldowns, and artifact effects in my own combat rules | Proprietary. Design reference only; no assets or code used. |

- Cover illustration in the pitch deck: AI-generated concept art created for the pitch.
- Prototype assets and code will be original or used under documented licenses, with terms recorded in this section before use. Asset log (source, license, where it lives):

  | Asset | Source | License | Location |
  |---|---|---|---|
  | StoneFloor (arena): *Monastery Stone Floor* by Amal Kumar, 1K maps | [Poly Haven](https://polyhaven.com/a/monastery_stone_floor) | CC0 | `Art/Materials/StoneFloor` |
  | OakWood (bookshelves): *Oak Wood Planks* by Dimitrios Savva, 1K maps | [Poly Haven](https://polyhaven.com/a/oak_wood_planks) | CC0 | `Art/Materials/OakWood` |
  | GolemStone (Book Keeper): *Rock 030*, 1K maps | [ambientCG](https://ambientcg.com/view?id=Rock030) | CC0 | `Art/Materials/GolemStone` |
  | AgedBronze (Alma Mater until 2026-11-23; kept for later use): *Metal 017*, 1K maps | [ambientCG](https://ambientcg.com/view?id=Metal017) | CC0 | `Art/Materials/AgedBronze` |
  | DarkBronze (Columbia Lion): *Metal 013*, 1K maps | [ambientCG](https://ambientcg.com/view?id=Metal013) | CC0 | `Art/Materials/DarkBronze` |
  | Verdigris (Alma Mater, from 2026-11-23): *Metal 058 C*, 1K maps | [ambientCG](https://ambientcg.com/view?id=Metal058C) | CC0 | `Art/Materials/Verdigris` |
  | Guardian blockouts (lich, lion, golem; procedural reference, no longer exported) | Original, built from primitives by `tools/blender/guardian_blockouts.py` in Blender 5.3 | Original (this project) | `tools/blender` |
  | Guardian models and texture sets (Alma Mater, Columbia Lion, Book Keeper; 2026-11-23) | Generated with Tripo AI image-to-3D from this project's own AI concept sheets, then decimated to 80k triangles, scaled, and exported by `tools/blender/prepare_generated.py`; raw generations kept locally in `art_source/generated` (not in git) | Tripo terms of the generating account (commercial use needs a paid plan; confirm before release) | `Art/Models/Guardians`, `Art/Materials/{AlmaMater,ColumbiaLion,BookKeeper}` |
  | Player model and texture set (the hunter; 2026-11-23) | Generated with Tripo AI image-to-3D from the front view of this project's own AI three-view sheet (the unarmed version: no scarf, weapons or spell orb; the orb was painted out of the front view before generation), decimated to 60k triangles by `tools/blender/prepare_generated.py`, rigged by `tools/blender/rig_guardians.py` (21 bones); raw generation and the sheet kept locally in `art_source/generated` (not in git) | Tripo terms of the generating account (commercial use needs a paid plan; confirm before release) | `Art/Models/Player`, `Art/Materials/Player` |
  | Columbia Lion's Fang model and texture set (2026-11-23) | Generated with Tripo AI image-to-3D from this project's own AI design sheet (the main view, with the title and insets painted out), decimated to 16k triangles and posed in the player's hand by `tools/blender/prepare_artifact.py`; raw generation and the sheet kept locally in `art_source/generated` (not in git) | Tripo terms of the generating account (commercial use needs a paid plan; confirm before release) | `Art/Models/Artifacts`, `Art/Materials/ColumbiaLionsFang` |
- **Columbia names and landmarks** (Alma Mater, the Columbia Lion, the bookstore, Columbia blue) are used for the class prototype only. A public release needs the university's permission or replacement content.
- **Concepts considered before this one:**
  - *AI Mafia* — simpler to prototype; set aside because The Artifacts connects real-world discovery with RPG progression.
  - *Conducting game* — the player conducts with a trackpad or mouse, shaping each part's dynamics (forte / piano) and nudging the rhythm, with an AI agent evaluating the performance. Set aside for two reasons: music licensing, and, even with rights cleared, the cost of obtaining a separate line for every instrument part.

## 13. Open questions

| # | Question | Needed by | Deciding role |
|---|---|---|---|
| Q1 | Engine / stack for the PC client, the mobile check-in, and the shared account store — **Resolved 2026-11-16:** Unity 6.3 LTS; backend Unity Gaming Services | Before first prototype task | Tech Lead recommends; I decide |
| Q2 | Check-in radius, cooldown, and how much anti-spoofing the prototype needs | Before mobile check-in task | Tech Lead / Design Lead |
| Q3 | ~~Skill lists, cooldown lengths, stats, artifact effects, and the input scheme for the two builds~~ **Resolved 2026-11-16** in the [Combat Spec](combat-spec.md) | Before combat implementation | Design Lead |
| Q4 | ~~Attempt cap and target fight length in seconds (budget assumes up to 10 attempts per session)~~ **Resolved 2026-11-16** in the [Combat Spec](combat-spec.md) | Before combat implementation | Design Lead |
| Q5 | ~~Drop table for four rarity tiers; whether rarity changes power or only variety~~ **Resolved 2026-11-16** in the [Combat Spec](combat-spec.md) | Before G2 playtest | Design Lead |
| Q6 | ~~Mid-fight disconnect: resume or forfeit~~ **Resolved 2026-11-16** in the [Combat Spec](combat-spec.md) | Before G2 playtest | Design Lead / Tech Lead |
| Q7 | ~~How many attempts count as "a few", and the fairness and earned-reward thresholds~~ **Resolved 2026-11-16** in the [Combat Spec](combat-spec.md) | Before the first comparison playtest | Design Lead / BI |
| Q9 | ~~Runtime model for the Guardian (budget uses planning rates)~~ **Resolved 2026-11-16:** Claude Haiku 5.5 (Configuration Document 2.7) | Before adaptive policy task | Tech Lead recommends; I decide |
| Q10 | ~~Which 2–3 landmarks, and whether any are indoors (GPS accuracy)~~ **Resolved 2026-11-19:** Alma Mater statue, Columbia Lion statue, Columbia bookstore. The bookstore is indoors, so GPS there is unreliable; the prototype may simulate visits (Section 6.1) | Before mobile check-in task | Design Lead |
| Q11 | ~~Stat tiers, skill pool, strategy list, and the allowed tuning range per challenger strength~~ **Resolved 2026-11-16** in the [Combat Spec](combat-spec.md) | Before adaptive Guardian task | Design Lead |
| Q12 | ~~General-item grade ladder and stat budgets~~ **Resolved 2026-11-19:** Standard 5, Fine 10, Superior 15, Masterpiece 20–23 points per item, capped so the strongest hit takes at most half of T1 HP (Section 6.6) | Before item data is authored beyond the prototype artifacts | Design Lead |
| Q13 | ~~Which general-item sources the prototype builds, and whether the test builds are redefined around general gear~~ **Resolved 2026-11-17:** no sources built; general gear only as a fixed test-build loadout (Section 6.6) | Before G2 playtest | Design Lead / Producer |
| Q14 | A power budget per artifact grade (artifacts are exempt from the general burst cap, 2026-11-19). Decided: traits are data built from shared parts (Combat Spec 5.4), skill keys by slot (3.2), Guardian Def and Res, no evasion, "always hit" means arena-wide range (2026-11-17); trait range per grade, statuses interrupt tells, per-Guardian status immunities (2026-11-18); multiplicative stats capped at ×2 per artifact, creature-type conditional traits, prototype Guardian is a Construct, traits for the four Relic and Heirloom artifacts (2026-11-19) | Before the next combat content pass | Design Lead |
| Q15 | Prototype content for the five new slots (helm, gloves, boots, left hand, second trinket): artifacts, the fixed general-gear loadout, and the rebalance they need (tier HP, damage, and Def/Res were set by bot simulation on 2026-11-19, Combat Spec 4.1 and 4.7; still open: the fixed general-gear loadout, gear-score bands, content for the new slots, the Combat Spec 4.7 outcome table) | Before the next combat content pass | Design Lead |
| Q16 | ~~Which of the three Guardians hosts the scripted-versus-adaptive comparison~~ **Resolved 2026-11-19:** the Columbia Lion | Before the first comparison playtest | Design Lead |
| Q17 | ~~Formulas for the new stats, damage types, mana, and which stats the prototype implements~~ **Resolved 2026-11-19:** ratings as value / (value + 100), physical and magic damage per skill, crits ×1.5, one learned magic skill (Lightning Bolt, key 5); all nine stats implemented (Combat Spec 2, 3) | Before the artifact list is replaced | Design Lead |
| Q18 | ~~One lore-based mechanic for each Columbia Guardian, and how its hint is delivered~~ **Resolved 2026-11-20:** the Book Keeper (burning bookshelves cause Fear) and Alma Mater (the hidden owl takes double damage) have one, Section 6.7; the Columbia Lion is a combat-sense fight. Hint delivery (story card, in-fight cue) is still [PROPOSED] | Before the next combat content pass | Design Lead |
