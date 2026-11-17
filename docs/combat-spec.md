# The Artifacts — Combat Spec

**Status: Decided (2026-11-16).** Answers GDD Q3, Q4, Q5, Q6, Q7 and Q11 for the prototype. Nothing here is implemented or playtested yet; numbers are expected to change after the first playtest.
Labels as in the GDD: **[DECIDED]** chosen by me as Design Lead; **[OPEN]** unresolved. All numbers here were approved on 2026-11-16 and will be retuned from playtest data.

## 1. Framing [DECIDED 2026-11-16]

| Choice | Decision | Why |
|---|---|---|
| Control scheme | **Tab-target + movement evasion.** The Guardian is auto-targeted; skills are on a hotbar; the player avoids telegraphed attacks by walking out of their marked area. No dodge roll. | Simplest to build; puts the weight on build choices and reading tells (Pillars 2, 3). |
| Player character | **One melee class.** | Fighting at the Guardian's feet makes tell → move → punish the clearest. |
| Fight length target | **90–120 s** per attempt. | Two or three passes through the Guardian's pattern; 10 attempts fit one 20–30 min playtest session. |
| Rarity | **Power and variety.** Higher rarity gives larger stats and adds a unique effect. | Keeps "gear felt worth it" measurable. |

## 2. Rules [DECIDED 2026-11-16]

- **Damage dealt by the player** = skill coefficient × Power.
- **Damage taken by the player** = base damage × Guardian tier multiplier × (1 − Armor).
- **Global cooldown (GCD):** 1.0 s after any hotbar skill.
- **Movement:** run speed 7 m/s. Turning and moving never stop auto-attack, but attacking requires melee range (≤ 3 m).
- **Telegraphs:** every Guardian skill shows its area on the ground for its full tell time; the area is fixed when the tell starts.
- **No fight timer.** A fight ends only when either side's HP reaches zero. Guardian auto-attacks already make a long fight a losing one for the player.

## 3. Player

### 3.1 Stats

| Stat | Meaning |
|---|---|
| HP | Health. No regeneration in a fight. |
| Power | Scales all player damage. |
| Armor | Percent reduction of damage taken (cap 50%). |

### 3.2 Abilities (same for both builds)

Both builds use the same abilities, so a tester learns one control scheme. The builds differ only in stats and artifacts.

| Key | Ability | Cooldown | Effect | Intent |
|---|---|---|---|---|
| auto | Auto-attack | 2.0 s swing | 1.0 × Power while in range | commit |
| 1 | Heavy Strike | 6 s | 2.5 × Power | commit |
| 2 | Guard | 12 s | 60% less damage taken for 2 s | defend |
| 3 | Lunge | 10 s | Dash up to 8 m to the Guardian, 1.0 × Power | switch tactics (re-engage after evading) |
| 4 | Punish | 15 s | 4.0 × Power; **×2 if the Guardian is in a recovery window** | commit (rewards reading tells) |
| WASD | Move | — | Walk out of telegraphed areas | evade |

### 3.3 Test builds

| | Starter | Geared |
|---|---|---|
| HP | 1000 | 1300 |
| Power | 40 | 60 |
| Armor | 0% | 15% |
| Artifacts | none | Treasure weapon, Heirloom armor, Heirloom trinket (Section 5.3) |
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

The Guardian auto-attacks the player for 20 base damage every 2.5 s when in range.

### 4.2 Skill pool (Q11)

Each skill: **tell** (telegraph shown, player reacts) → **hit** → **recovery** (Guardian idle, Punish deals double).

| ID | Skill | Area | Tell | Base damage | Recovery | How to avoid |
|---|---|---|---|---|---|---|
| `cleave` | Cleave | 90° cone in front, 6 m | 1.2 s | 200 | 1.0 s | Step to the side or behind |
| `slam` | Slam | Circle around the Guardian, 5 m | 1.6 s | 250 | 1.5 s | Run out (0.7 s at run speed) |
| `charge` | Charge | Line toward the player, 15 m × 3 m | 1.4 s | 220 | 2.0 s | Sidestep |
| `rings` | Shock Rings | Ring 4–12 m from the Guardian (safe up close) | 1.8 s | 180 | 1.2 s | Move **in** to the Guardian |
| `embers` | Embers | Three 3 m circles placed on the player, 0.6 s apart | 1.0 s each | 120 each | 0.8 s | Keep moving |
| `fury` | Fury | Self-buff: auto-attacks +50% for 8 s | 1.0 s | — | 0 s | Use Guard; back off |

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

### 4.6 Tuning range by challenger strength (open Guardian)

Challenger strength is the **gear score**: the sum of equipped artifact rarity points (Relic 1, Heirloom 2, Treasure 3, Wonder 4).

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

Reading: the scripted default is very hard for a starter and easy for a geared player (the gap the hypothesis targets). Inside their bands, a starter at T1–T2 and a geared player at T4–T5 both land near the 90–120 s target, and the geared player still has more margin, so gear keeps its value.

## 5. Rewards (Q5)

### 5.1 Drop on a win

| Rarity | Chance | Rarity points |
|---|---|---|
| Relic | 50% | 1 |
| Heirloom | 30% | 2 |
| Treasure | 15% | 3 |
| Wonder | 5% | 4 |

The **first win** on a Guardian is at least Heirloom. Rolls are made by the engine and hidden from the Guardian agent.

### 5.2 Slots and what rarity changes

Three slots: **weapon, armor, trinket.** One artifact per slot.

| Rarity | Stats | Effect |
|---|---|---|
| Relic | one small stat bonus | — |
| Heirloom | two stat bonuses, or one large | — |
| Treasure | stat bonus | one minor effect (tunes an ability) |
| Wonder | stat bonus | one signature effect (changes how an ability is used) |

### 5.3 Prototype artifact list (8 artifacts)

| Artifact | Slot | Rarity | Stats | Effect |
|---|---|---|---|---|
| Worn Band | trinket | Relic | +5 Power | — |
| Padded Vest | armor | Relic | +100 HP | — |
| Iron Charm | trinket | Heirloom | +100 HP, +5% Armor | — |
| Scaled Coat | armor | Heirloom | +200 HP, +10% Armor | — |
| Keen Edge | weapon | Treasure | +20 Power | Heavy Strike cooldown 6 → 4.5 s |
| Warden's Plate | armor | Treasure | +150 HP, +10% Armor | Guard also heals 5% max HP |
| Echo Blade | weapon | Wonder | +25 Power | Punish in a recovery window resets Lunge |
| Stillstone | trinket | Wonder | +150 HP | Guard lasts 3 s and reflects 30% of blocked damage |

The geared test build wears Keen Edge, Scaled Coat, and Iron Charm.

> **Note (2026-11-17):** the full game adds general items up to SSR-equivalent. Artifacts are a separate category above that ladder, with the four grades Relic, Heirloom, Treasure, Wonder ([GDD](gdd.md) Section 6.6). Each grade's stat budget relative to SSR, and whether the test builds change, are open (GDD Q12, Q13). The numbers above stand until then.

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
