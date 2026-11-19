# The Artifacts — Combat Spec

**Status: Decided (2026-11-16).** Answers GDD Q3, Q4, Q5, Q6, Q7 and Q11 for the prototype. Nothing here is implemented or playtested yet; numbers are expected to change after the first playtest.
Labels as in the GDD: **[DECIDED]** chosen by me as Design Lead; **[OPEN]** unresolved. All numbers here were approved on 2026-11-16 and will be retuned from playtest data.

## 1. Framing [DECIDED 2026-11-16]

| Choice | Decision | Why |
|---|---|---|
| Control scheme | **Tab-target + click-to-move evasion** (changed 2026-11-17 from WASD). The Guardian is auto-targeted; skills are on a hotbar; the player avoids telegraphed attacks by right-clicking out of their marked area. No dodge roll. | Simplest to build; puts the weight on build choices and reading tells (Pillars 2, 3). |
| Player character | **One melee class.** | Fighting at the Guardian's feet makes tell → move → punish the clearest. |
| Fight length target | **90–120 s** per attempt. | Two or three passes through the Guardian's pattern; 10 attempts fit one 20–30 min playtest session. |
| Rarity | **Power and variety.** Higher rarity gives larger stats and adds a unique effect. | Keeps "gear felt worth it" measurable. |

## 2. Rules [DECIDED 2026-11-16]

- **Stat ratings [DECIDED 2026-11-19]:** Def, Res, Agility, and Precision turn into a percent as *value / (value + 100)*: 25 → 20%, 100 → 50%, 400 → 80%, never 100%. This holds for players and Guardians.
- **Damage types [DECIDED 2026-11-19]:** every skill, the player's and the Guardian's, is **physical** or **magic**. Def reduces physical damage, Res reduces magic damage.
- **Damage dealt by the player** = skill coefficient × Strength (physical) or Intelligence (magic) × (1 − the Guardian's Def or Res rating). This applies to all player damage, reflected damage included (reflects count as physical). The Guardian has no evasion stat.
- **Critical hits [DECIDED 2026-11-19]:** a player attack crits with chance = Precision rating and deals **×1.5**. Reflects never crit. Guardians do not crit.
- **Damage taken by the player** = base damage × Guardian tier multiplier × (1 − the player's Def or Res rating).
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
| 4 | Punish | 15 s | 4.0 × Strength; **×2 if the Guardian is in a recovery window** | commit (rewards reading tells) |
| 5 | Lightning Bolt **[PROPOSED 2026-11-19]** | 8 s | Magic: 100 + 1.5 × Intelligence (useful at any Intelligence), range 15 m, costs 30 MP. Strong against most Guardians, nearly useless against Constructs (high Res) | commit at range |
| Right-click | Move | — | Run to the clicked point (hold to steer); walk out of telegraphed areas | evade |

**Equipment-skill keys [DECIDED 2026-11-17].** A skill granted by an equipped artifact (GDD Section 6.6) is cast with the key of the slot it is worn in. The keys follow the body: top row head and hands, middle row torso, bottom row feet. WASD is no longer movement.

| Key | Slot | | Key | Slot | | Key | Slot |
|---|---|---|---|---|---|---|---|
| Q | Left hand | | W | Helm | | E | Right hand |
| A | Gloves | | S | Armor | | D | Trinket 1 |
| — | — | | X | Boots | | C | Trinket 2 |

A slot with no granted skill leaves its key unused.

### 3.3 Test builds

| | Starter | Geared |
|---|---|---|
| HP | 1000 | 1300 |
| MP | 100 | 100 |
| Strength | 40 | 60 |
| Intelligence | 40 | 40 |
| Def | 0 (0%) | 18 (15.3%) |
| Res, Agility, Precision, Speed | 0 | 0 |
| Artifacts | none | Treasure right-hand weapon, Heirloom armor, Heirloom trinket (Section 5.3) |
| Gear score | 0 | 7 |
| Expected DPS (ideal / realistic 70% uptime) | 54 / 38 | 89 / 63 |

## 4. Guardian

### 4.1 Stat tiers (Q11)

| Tier | HP | Damage multiplier |
|---|---|---|
| T1 | 3000 | 0.80 |
| T2 | 3600 | 0.90 |
| **T3 (default)** | 4300 | 1.00 |
| T4 | 5200 | 1.15 |
| T5 | 6200 | 1.30 |

A Guardian's Def and Res belong to the Guardian, not the tier (Section 4.5).

The Guardian auto-attacks the player for 20 base damage every 2.5 s when in range.

### 4.2 Skill pool (Q11)

Each skill: **tell** (telegraph shown, player reacts) → **hit** → **recovery** (Guardian idle, Punish deals double).

| ID | Skill | Area | Tell | Base damage | Type | Recovery | How to avoid |
|---|---|---|---|---|---|---|---|
| `cleave` | Cleave | 90° cone in front, 6 m | 1.2 s | 200 | physical | 1.0 s | Step to the side or behind |
| `slam` | Slam | Circle around the Guardian, 5 m | 1.6 s | 250 | physical | 1.5 s | Run out (0.7 s at run speed) |
| `charge` | Charge | Line toward the player, 15 m × 3 m | 1.4 s | 220 | physical | 2.0 s | Sidestep |
| `rings` | Shock Rings | Ring 4–12 m from the Guardian (safe up close) | 1.8 s | 180 | magic | 1.2 s | Move **in** to the Guardian |
| `embers` | Embers | Three 3 m circles placed on the player, 0.6 s apart | 1.0 s each | 120 each | magic | 0.8 s | Keep moving |
| `fury` | Fury | Self-buff: auto-attacks +50% for 8 s | 1.0 s | — | — | 0 s | Use Guard; back off |

Guardian auto-attacks are physical.

A setup picks **3 or 4** skills from the pool.

### 4.3 Strategies (Q11)

A strategy decides only **when** and **which** skill is used. Tells, damage, and recovery belong to the skill and never change.

| ID | Strategy | Behavior |
|---|---|---|
| `steady` | Steady | One skill every 8 s, in a fixed rotation. |
| `aggressive` | Aggressive | One skill every 5.5 s, favors `cleave` and `charge`. |
| `combo` | Combo | Every 12 s, two skills back to back (0.5 s gap between the first recovery and the second tell). |
| `reactive` | Reactive | Every 7 s, picks by distance: `charge` or `embers` if the player is beyond 8 m, `slam` or `rings` if close. |

### 4.4 Default (scripted) Guardian

**T3, skills `cleave` `slam` `charge`, strategy `steady`.** Used for every attempt in scripted mode and as the fallback in adaptive mode.

### 4.5 Enrage [DECIDED 2026-11-16]

Enrage is a **per-Guardian property, off by default.** The prototype's open Guardian has no enrage. Special Guardians in the full game may set an enrage timer as part of their identity; it belongs to that Guardian's configuration, never to the adaptive setup, so the agent cannot turn it on or change it.

**Creature type [DECIDED 2026-11-19]:** every Guardian (and, in the full game, every field monster) has a creature type that conditional traits target. Types so far: Construct, Beast, Elemental, Undead; the full list is open. **The prototype Guardian is a Construct.**

**The three prototype Guardians** (GDD Section 6.4). Def and Res are per Guardian; values marked [PROPOSED] are for the Design Lead to confirm:

| Guardian | Type | Def | Res | Immunities |
|---|---|---|---|---|
| Alma Mater | Construct | 0 | 400 (80%) **[PROPOSED]** | none |
| Columbia Lion | Beast | 0 | 0 | none |
| Book Keeper | Undead | 0 | 0 | none |

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

### 4.7 Expected outcomes (realistic DPS, no Guard used)

| | T1 | T2 | **T3 (default)** | T4 | T5 |
|---|---|---|---|---|---|
| Starter: time to kill | 79 s | 95 s | 114 s | 138 s | 164 s |
| Starter: telegraphed hits it can take | 2.8 | 1.6 | **0.4** | dies to auto-attacks | dies to auto-attacks |
| Geared: time to kill | 48 s | 58 s | 69 s | 83 s | 99 s |
| Geared: telegraphed hits it can take | 6.9 | 5.6 | 4.5 | 3.0 | 1.7 |

> **Note (2026-11-19):** this table predates traits, the nine-stat model, and Lightning Bolt. Lightning Bolt adds ranged magic damage against the Columbia Lion and the Book Keeper, so those fights run shorter than shown; against Alma Mater (Res 400) it adds little. Against the Construct prototype Guardian, the geared build's Iron Charm and Scaled Coat cut damage taken to 0.9 × 0.85 = 0.765, so its "telegraphed hits it can take" row is about 31% higher. The rebalance in GDD Q15 recomputes the table.

Reading: the scripted default is very hard for a starter and easy for a geared player (the gap the hypothesis targets). Inside their bands, a starter at T1–T2 and a geared player at T4–T5 both land near the 90–120 s target, and the geared player still has more margin, so gear keeps its value.

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

Eight slots **[DECIDED 2026-11-17]**: **helm, armor, left hand, right hand, gloves, boots,** and **two trinket slots.** One item per slot; a trinket fits either trinket slot. Each slot has an equipment-skill key (Section 3.2). The eight prototype artifacts below cover only right hand, armor, and trinket; content for the other slots and the rebalance it needs are open (GDD Q15).

Every artifact has a **trait**, which general items never have (GDD Section 6.6). The Effect column is that trait.

| Rarity | Stats | Effect |
|---|---|---|
| Relic | one small stat bonus | one passive trait (Section 5.4) |
| Heirloom | two stat bonuses, or one large | one passive trait (Section 5.4) |
| Treasure | stat bonus | one minor effect (tunes an ability) |
| Mythic | stat bonus | one signature effect (changes how an ability is used) |

### 5.3 Prototype artifact list (8 artifacts)

> **Being replaced (2026-11-19):** the prototype rewards become six Columbia landmark artifacts in three loot pools (GDD Section 6.5). Their stats and traits are pending approval; this list and the test builds stay in use until then.

| Artifact | Slot | Rarity | Stats | Effect |
|---|---|---|---|---|
| Worn Band | trinket | Relic | +5 Strength | Strength +30% (×1.3) |
| Padded Vest | armor | Relic | +100 HP | Max HP +30% (×1.3) |
| Iron Charm | trinket | Heirloom | +100 HP, +6 Def | 10% less damage taken from Constructs |
| Scaled Coat | armor | Heirloom | +200 HP, +12 Def | 15% less damage taken from Constructs |
| Keen Edge | right hand | Treasure | +20 Strength | Heavy Strike cooldown 6 → 4.5 s |
| Warden's Plate | armor | Treasure | +150 HP, +12 Def | Guard also heals 5% max HP |
| Echo Blade | right hand | Mythic | +25 Strength | Punish in a recovery window resets Lunge |
| Stillstone | trinket | Mythic | +150 HP | Guard lasts 3 s and reflects 30% of blocked damage |

The geared test build wears Keen Edge, Scaled Coat, and Iron Charm.

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
