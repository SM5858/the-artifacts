# The Artifacts — Combat Spec

**Status: Decided (2026-11-16).** Answers GDD Q3, Q4, Q5, Q6, Q7 and Q11 for the prototype. Nothing here is implemented or playtested yet; numbers are expected to change after the first playtest.
Labels as in the GDD: **[DECIDED]** chosen by me as Design Lead; **[OPEN]** unresolved. All numbers here were approved on 2026-11-16 and will be retuned from playtest data.

## 1. Framing [DECIDED 2026-11-16]

| Choice | Decision | Why |
|---|---|---|
| Control scheme | **Tab-target + click-to-move evasion** (changed 2026-11-17 from WASD). The Guardian is auto-targeted; skills are on a hotbar; the player avoids telegraphed attacks by right-clicking out of their marked area. No dodge roll. | Simplest to build; puts the weight on build choices and reading tells (Pillars 2, 3). |
| Player character | **One melee class.** | Fighting at the Guardian's feet makes tell → move → strike in the opening the clearest. |
| Fight length target | **90–120 s** per attempt. | Two or three passes through the Guardian's pattern; 10 attempts fit one 20–30 min playtest session. |
| Rarity | **Power and variety.** Higher rarity gives larger stats and adds a unique effect. | Keeps "gear felt worth it" measurable. |

## 2. Rules [DECIDED 2026-11-16]

- **Stat ratings [DECIDED 2026-11-19]:** Def, Res, Agility, and Precision turn into a percent as *value / (value + 100)*: 25 → 20%, 100 → 50%, 400 → 80%, never 100%. This holds for players and Guardians.
- **Damage types [DECIDED 2026-11-19]:** every skill, the player's and the Guardian's, is **physical** or **magic**. Def reduces physical damage, Res reduces magic damage.
- **Damage dealt by the player** = skill coefficient × Strength (physical) or Intelligence (magic) × (1 − the Guardian's Def or Res rating). This applies to all player damage, reflected damage included (reflects count as physical). The Guardian has no evasion stat.
- **Critical hits [DECIDED 2026-11-19]:** a player attack crits with chance = Precision rating and deals **×1.5**. Reflects never crit. Guardians do not crit.
- **Opening [DECIDED 2026-11-19]:** while the Guardian recovers after a skill (its recovery window, 1.0–2.0 s), player attacks get **+50 percentage points** of crit chance (capped at 100%). This replaced Punish: every attack can use the opening, so no skill slot sits unused.
- **Damage taken by the player** = base damage × (1 − the player's Def or Res rating). Tiers do not scale damage; they scale tell times (Section 4.1) **[DECIDED 2026-11-19]**.
- **Cooldowns:** every ability cooldown is shortened by the Agility rating.
- **Global cooldown (GCD):** 1.0 s after any hotbar skill.
- **Movement:** run speed 7 m/s × (1 + Speed / 100). Turning and moving never stop auto-attack, but attacking requires melee range (≤ 3 m).
- **Telegraphs:** every Guardian skill shows its area on the ground for its full tell time; the area is fixed when the tell starts.
- **No fight timer.** A fight ends only when either side's HP reaches zero. Guardian auto-attacks already make a long fight a losing one for the player.

## 3. Player

### 3.1 Stats

The nine stats are defined in GDD Section 6.2. In combat:

| Stat | Meaning |
|---|---|
| HP | Health. No regeneration in a fight. |
| MP | Mana for learned magic skills; regenerates 2 MP/s. Artifact skills cost no mana. |
| Strength | Scales physical damage. |
| Intelligence | Scales magic damage. |
| Agility | Cooldown reduction (rating). |
| Def | Physical damage reduction (rating). |
| Res | Magic damage reduction (rating). |
| Precision | Critical-hit chance (rating). |
| Speed | Run speed +Speed%. Raised only by artifacts and crafted items. |

### 3.2 Abilities (same for both builds)

Both builds use the same abilities, so a tester learns one control scheme. The builds differ only in stats and artifacts. Keys 1–4 are physical; key 5 is the one learned magic skill in the prototype.

| Key | Ability | Cooldown | Effect | Intent |
|---|---|---|---|---|
| auto | Auto-attack | 2.0 s swing | 1.0 × Strength while in range | commit |
| 1 | Heavy Strike | 6 s | 2.5 × Strength | commit |
| 2 | Guard | 12 s | 60% less damage taken for 2 s | defend |
| 3 | Lunge | 10 s | Dash up to 8 m to the Guardian, 1.0 × Strength | switch tactics (re-engage after evading) |
| 4 | Overpower (ultimate) **[PROPOSED 2026-11-19]** | 45 s | 8.0 × Strength, melee. Best saved for an opening, where it is likely to crit | commit (rewards reading tells) |
| 5 | Lightning Bolt **[DECIDED 2026-11-19]** | 8 s | Magic: 100 + 1.5 × Intelligence (useful at any Intelligence), range 15 m, costs 30 MP. Strong against most Guardians, nearly useless against Constructs (high Res) | commit at range |
| 6 | Fire Bolt **[DECIDED 2026-11-19; numbers PROPOSED]** | 6 s | Magic: 60 + 1.0 × Intelligence, range 15 m, costs 20 MP. Weaker than Lightning Bolt; its value is fire, which sets lore targets alight (4.5) | commit at range, or aim at a lore target |
| Right-click | Move | — | Run to the clicked point (hold to steer); walk out of telegraphed areas | evade |

**Equipment-skill keys [DECIDED 2026-11-17].** A skill granted by an equipped artifact (GDD Section 6.6) is cast with the key of the slot it is worn in. The keys follow the body: top row head and hands, middle row torso, bottom row feet. WASD is no longer movement.

| Key | Slot | | Key | Slot | | Key | Slot |
|---|---|---|---|---|---|---|---|
| Q | Left hand | | W | Helm | | E | Right hand |
| A | Gloves | | S | Armor | | D | Trinket 1 |
| — | — | | X | Boots | | C | Trinket 2 |

A slot with no granted skill leaves its key unused. Equipment skills cost no MP and neither need nor trigger the GCD **[DECIDED 2026-11-19]**, so a defensive one can be used in reaction to a tell.

### 3.3 Test builds

| | Starter | Geared |
|---|---|---|
| HP | 1000 | 1200 |
| MP | 100 | 130 |
| Strength | 40 | 55 |
| Intelligence | 40 | 40 |
| Def | 0 (0%) | 6 (5.7%) |
| Res | 0 (0%) | 17 (14.5%) |
| Precision | 0 (0%) | 10 (9.1% crit) |
| Agility, Speed | 0 | 0 |
| Artifacts | none | Wreath of Alma Mater, Columbia Lion's Fang, Columbia Blue Robe (Section 5.3) |
| Gear score | 0 | 6 |
| Expected DPS (ideal / realistic 70% uptime) | 54 / 38 | recompute in Q15 |

## 4. Guardian

### 4.1 Stat tiers (Q11)

Tiers scale HP, defense, and **tell time** **[DECIDED 2026-11-19]**: damage stays the same at every tier, but low tiers give long tells (easy to dodge) and high tiers short ones (fast reactions needed). A tell never drops below the skill's fair minimum (Section 4.2). Values tuned by bot simulation and softened on 2026-11-19 so a 0.6 s reaction can still dodge at T5; shorten them if playtesters find the high tiers too easy:

| Tier | HP | Tell multiplier | Def / Res added |
|---|---|---|---|
| T1 | 5500 | ×1.40 | 0 |
| T2 | 6400 | ×1.20 | 5 |
| **T3 (default)** | 7500 | ×1.00 | 10 |
| T4 | 8700 | ×0.90 | 20 |
| T5 | 9800 | ×0.80 | 30 |

The tier's Def and Res add to the Guardian's own (Section 4.5); for example, Alma Mater at T3 has Res 410.

The Guardian auto-attacks the player for 20 base damage every 2.5 s when in range.

### 4.2 Skill pool (Q11)

Each skill: **tell** (telegraph shown, player reacts) → **hit** → **recovery** (Guardian idle; the opening raises the player's crit chance, Section 2).

Tell is the T3 value; tiers scale it (4.1). Minimum tell = worst-case escape from melee range at run speed + 0.4 s to react.

| ID | Skill | Area | Tell (T3) | Min tell | Base damage | Type | Recovery | How to avoid |
|---|---|---|---|---|---|---|---|---|
| `cleave` | Cleave | 90° cone in front, 6 m | 1.2 s | 0.7 s | 200 | physical | 1.0 s | Step to the side or behind |
| `slam` | Slam | Circle around the Guardian, 5 m | 1.6 s | 0.8 s | 250 | physical | 1.5 s | Run out (0.7 s at run speed) |
| `charge` | Charge | Line toward the player, 15 m × 3 m | 1.4 s | 0.7 s | 220 | physical | 2.0 s | Sidestep |
| `rings` | Shock Rings | Ring 4–12 m from the Guardian (safe up close) | 1.8 s | 1.0 s | 180 | magic | 1.2 s | Move **in** to the Guardian |
| `embers` | Embers | Three 3 m circles placed on the player, 0.6 s apart | 1.0 s each | 0.9 s | 120 each | magic | 0.8 s | Keep moving |
| `fury` | Fury | Self-buff: auto-attacks +50% for 8 s | 1.0 s | 0.5 s | — | — | 0 s | Use Guard; back off |

Guardian auto-attacks are physical.

A setup picks **3 or 4** skills from the pool.

### 4.3 Strategies (Q11)

A strategy decides only **when** and **which** skill is used. Damage and recovery belong to the skill and never change; tells change only with the tier (4.1), the same way in scripted and adaptive mode.

| ID | Strategy | Behavior |
|---|---|---|
| `steady` | Steady | One skill every 8 s, in a fixed rotation. |
| `aggressive` | Aggressive | One skill every 5.5 s, favors `cleave` and `charge`. |
| `combo` | Combo | Every 12 s, two skills back to back (0.5 s gap between the first recovery and the second tell). |
| `reactive` | Reactive | Every 7 s, picks by distance: `charge` or `embers` if the player is beyond 8 m, `slam` or `rings` if close. |

### 4.4 Default (scripted) Guardian

**T3, skills `cleave` `slam` `charge`, strategy `steady`.** Used for every attempt in scripted mode and as the fallback in adaptive mode. The comparison runs on the **Columbia Lion** (Beast, Def 0, Res 0; GDD Q16), so this setup is the Lion's scripted baseline.

### 4.5 Enrage [DECIDED 2026-11-16]

Enrage is a **per-Guardian property, off by default.** The prototype's open Guardian has no enrage. Special Guardians in the full game may set an enrage timer as part of their identity; it belongs to that Guardian's configuration, never to the adaptive setup, so the agent cannot turn it on or change it.

**Creature type [DECIDED 2026-11-19]:** every Guardian (and, in the full game, every field monster) has a creature type that conditional traits target. Types so far: Construct, Beast, Elemental, Undead; the full list is open. **The prototype Guardian is a Construct.**

**The three prototype Guardians** (GDD Section 6.4). Def and Res are per Guardian:

| Guardian | Type | Def | Res | Immunities |
|---|---|---|---|---|
| Alma Mater | Construct | 0 | 400 (80%) **[DECIDED 2026-11-19]** | none |
| Columbia Lion | Beast | 0 | 0 | none |
| Book Keeper | Undead | 0 | 0 | none |

**Lore targets [DECIDED 2026-11-19] (GDD 6.7):** arena objects fixed to a Guardian, like its immunities, never part of the adaptive setup. A spell (5 or 6) can aim at one instead of the Guardian; a spell of the target's element spends it (once per fight) and puts its status on the Guardian, and any other element does nothing. In the sandbox, left-click a target to aim at it and Esc to aim back at the Guardian.

| Guardian | Lore targets | Element | Effect |
|---|---|---|---|
| Book Keeper | 3 bookshelves behind its start, at (−8, 14), (0, 16), (8, 14) | Fire | Fear 4 s: cancels the current skill, no attacks |

**Status immunities [DECIDED 2026-11-18]** work the same way: a per-Guardian property that fits its identity (for example, a Lich immune to fear, a Golem immune to petrify), never part of the adaptive setup. The prototype Guardian has none.

### 4.6 Tuning range by challenger strength (open Guardian)

Challenger strength is the **gear score**: the sum of equipped artifact rarity points (Relic 1, Heirloom 2, Treasure 3, Mythic 4).

| Gear score | Allowed tiers | Allowed skills | Skill count | Allowed strategies |
|---|---|---|---|---|
| 0–2 | T1–T3 | all | 3–4 | all |
| 3–5 | T2–T4 | all | 3–4 | all |
| 6+ | T3–T5 | all | 3–4 | all |

Bands overlap at their edges, and a geared player can never get a weaker tier than a starter's best case (T3).
**Gated variant (stretch):** allowed tiers T4–T5 for every gear score.

### 4.7 Expected outcomes (bot simulation, 2026-11-19)

From the balance report in CI (`Report_BotFightsAcrossGuardiansTiersAndBuilds`) with the tell curve in 4.1: a bot that reacts in 0.6 s and fails to notice a share of telegraphs, standing in for human mistakes. Its miss rate does not drop when tells get longer, so low tiers play easier for people than shown here. Steady strategy, physical skill set (the scripted default's skills), **Columbia Lion** (the comparison Guardian). Cells are *time to kill / HP left*, or L for a loss.

| Build | Missed telegraphs | T1 | T2 | **T3 (default)** | T4 | T5 |
|---|---|---|---|---|---|---|
| Starter | 0% | 81 s / 50% | 106 s / 36% | 133 s / 18% | 163 s / 1% | L |
| Starter | 30% | 81 s / 25% | L | L | L | L |
| Starter | 60% | L | L | L | L | L |
| Geared | 0% | 51 s / 75% | 58 s / 72% | 72 s / 64% | 102 s / 51% | L |
| Geared | 30% | 51 s / 55% | 58 s / 52% | 75 s / 44% | 102 s / 13% | L |
| Geared | 60% | 51 s / 20% | 58 s / 17% | 75 s / 9% | L | L |

Measured after Punish was replaced by the opening and Overpower (fights run about 5–15% longer than with Punish).

Reading: the scripted default (T3) is hard for a starter and comfortable for the geared build, the gap the hypothesis targets. T5 is out of reach for a 0.6 s reaction even with no misses (its shortest tells leave about 0.35 s to move), so it is for fast players; the playtest decides whether to keep it. The Book Keeper plays the same for a starter and about 15–20% slower for the geared build (no Lion's Fang bonus); Alma Mater runs about 25–50% longer (Lightning Bolt barely hurts it). With the mixed skill set (Slam, Shock Rings, Embers) a starter loses from T3 even without misses, because Embers lands on the player three times in a row.

## 5. Rewards (Q5)

### 5.1 Drop on a win

| Rarity | Chance | Rarity points |
|---|---|---|
| Relic | 50% | 1 |
| Heirloom | 30% | 2 |
| Treasure | 15% | 3 |
| Mythic | 5% | 4 |

**Per landmark [DECIDED 2026-11-19]:** a win rolls the grade from this table restricted to the grades the landmark's pool offers (chances renormalized), then picks one of that landmark's artifacts of that grade at random.

The **first win** on a Guardian gives Heirloom or better when the landmark's pool has one; otherwise the pool's highest grade **[DECIDED 2026-11-19]**. Rolls are made by the engine and hidden from the Guardian agent.

### 5.2 Slots and what rarity changes

Eight slots **[DECIDED 2026-11-17]**: **helm, armor, left hand, right hand, gloves, boots,** and **two trinket slots.** One item per slot; a trinket fits either trinket slot. Each slot has an equipment-skill key (Section 3.2). The prototype artifacts (5.3) cover helm, right hand, and armor; content for the other slots is open (GDD Q15).

Every artifact has a **trait**, which general items never have (GDD Section 6.6). The Effect column is that trait.

| Rarity | Stats | Effect |
|---|---|---|
| Relic | one small stat bonus | one passive trait (Section 5.4) |
| Heirloom | two stat bonuses, or one large | one passive trait (Section 5.4) |
| Treasure | stat bonus | one minor effect (tunes an ability) |
| Mythic | stat bonus | one signature effect (changes how an ability is used) |

### 5.3 Prototype artifacts: Columbia landmark pools [DECIDED 2026-11-19]

| Landmark (Guardian) | Artifact | Slot | Grade | Stats | Trait (tooltip) |
|---|---|---|---|---|---|
| Alma Mater statue (Alma Mater) | Wreath of Alma Mater | helm | Relic | +50 HP, +5 Res | Max MP +30% |
| | Staff of Alma Mater | right hand | Relic | +10 Intelligence | Intelligence +30% |
| Columbia Lion statue (Columbia Lion) | Columbia Lion's Fang | right hand (dagger) | Heirloom | +15 Strength, +10 Precision | 20% more damage to Beasts |
| | Columbia Lion's Pelt | helm | Mythic | +150 HP, +12 Def, +10 Speed | Once per fight, survive a killing blow at 1 HP |
| Columbia bookstore (Book Keeper) | Columbia Blue Mortarboard | helm | Relic | +20 Precision | 20% more damage to Undead |
| | Columbia Blue Robe | armor | Treasure | +150 HP, +6 Def, +12 Res | S: take 30% less damage for 4 s (25 s cooldown) |

With the per-landmark roll (5.1), a win gives: Alma Mater, always a Relic (either artifact); Columbia Lion, Fang 86% / Pelt 14%; Book Keeper, Mortarboard 77% / Robe 23%. A first win gives the Fang or Pelt at the Lion (unchanged), the Robe at the bookstore, and a Relic at Alma Mater.

The geared test build wears one artifact from each landmark: **Wreath of Alma Mater, Columbia Lion's Fang, Columbia Blue Robe.** The eight earlier placeholder artifacts were removed; the trait mechanics they used stay covered by test-only fixtures.

The item tooltip shows the Effect column as written, with multipliers as percentages (×1.3 → "+30%").

> **Note (2026-11-17):** the full game adds general items graded Standard, Fine, Superior, Masterpiece. Artifacts are a separate category with the four grades Relic, Heirloom, Treasure, Mythic. A Masterpiece's stats can reach Heirloom, but it never has a trait; every artifact does ([GDD](gdd.md) Section 6.6). The prototype gives the test builds a fixed general-gear loadout (Q13); its stats and each grade's budget are open (Q12). The numbers above stand until then.

### 5.4 Trait model [DECIDED 2026-11-17]

Traits are meant to be very varied and themed on their place (for example, an Athena's Shield from a Greco-Roman dungeon that petrifies an attacker). So a trait is **data built from shared parts**, not code written per artifact:

- **Mode:** *passive* (always on), *equipment skill* (cast with its slot key, Section 3.2, with a cooldown), or *triggered* (fires on an event).
- **Trigger** (triggered only): on hit taken, on hit dealt, on lethal damage, on skill cast, at fight start, and more as needed.
- **Conditions:** chance, HP threshold, internal cooldown, once per fight.
- **Effects:** add or multiply a stat, reflect damage, ignore Guardian Def or Res, always hit, heal, shield, apply a status (petrify, stun, slow), teleport, survive at 1 HP, change or reset a cooldown.

A new artifact usually only combines existing parts; a truly new effect adds one part.

**Always hit [DECIDED 2026-11-17]:** an ability with "always hit" has no range limit; its range becomes the whole arena. There is no hit-chance roll, because the Guardian has no evasion.

**What each grade may use [DECIDED 2026-11-18]:**

| Grade | Trait range |
|---|---|
| Relic, Heirloom | Passive numeric effects (multiplicative stats, conditional bonuses) |
| Treasure | Triggered effects or an equipment skill |
| Mythic | Rule-breaking effects (for example, 100% reflect, survive at 1 HP, always hit) |

**Passive numeric traits [DECIDED 2026-11-19]:**

- *Multiplicative stat:* multiplies any stat (GDD 6.2) after flat bonuses. One artifact may multiply a stat by at most **×2**. This is a ceiling for designing future artifacts, so new content cannot break balance, not a target: prototype values stay well below it (Relic ×1.3). CI checks every artifact against the cap. Multipliers from different artifacts multiply together.
- *Conditional by creature type:* more damage dealt to, or less damage taken from, one creature type (Guardians and field monsters). Effects of this kind from different artifacts multiply together.

**Statuses on a Guardian [DECIDED 2026-11-18]:** a status (stun, petrify, fear) interrupts the Guardian's current skill: its telegraphs are removed, the rest of the skill (and any queued combo skill) is cancelled, and there is no recovery window. While the status lasts the Guardian does nothing, then returns to its normal rhythm. A Guardian immune to a status ignores it (Section 4.5). No diminishing returns for now; revisit if a playtest shows a status locking a Guardian down. In the prototype all three statuses behave the same; distinct behavior (such as fear making the Guardian flee) is later work.

Still open (GDD Q14): a power budget per grade.

## 6. Session rules (Q4, Q6, Q7)

| Question | Proposal |
|---|---|
| Q4 Attempt cap | 10 attempts per playtest session per mode. |
| Q6 Mid-fight disconnect | **Forfeit**, and the attempt is not counted. Simple and cannot be exploited for rewards. |
| Q7 "A few attempts" | Median attempts to first win **≤ 5** for both builds in adaptive mode. |
| Q7 Fairness thresholds | Mean ≥ 4 / 5 on "the fight was fair" and "I earned this"; "gear felt worth it" for geared testers no lower than in scripted mode. |

## 7. Still open

- **Implementation defaults (in `Core/Combat/Content.cs`, not yet approved):** Guardian move speed 5 m/s (slower than the player's 7 m/s); Guardian stops chasing at 2.5 m; circular arena radius 20 m; player starts 12 m away; Charge leaves the Guardian at the end of its line; a strategy's first skill comes one interval into the fight.
- **Bot result (first engine build, 2026-11-16):** a bot that dodges every telegraph wins every tier with both builds (starter T3 in 75 s, geared T5 in 70 s), even with a 0.6 s reaction delay, because the default skills' tells are long enough to escape. The "scripted default is very hard for a starter" reading in 4.7 depends on human mistakes; measure telegraph hits per attempt in the first playtest before retuning.

- Exact animation timings will shift tells by ±0.2 s once the Guardian model is animated; recheck readability in a build.
- Whether Guard should be usable during the GCD.
- Theme and names of the first dungeon and Guardian (Narrative).
