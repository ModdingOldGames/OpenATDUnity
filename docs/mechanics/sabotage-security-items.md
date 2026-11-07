# Sabotage, security office, carried items and rival relations

Game-mechanics reference for the **free game** of Airline Tycoon Deluxe as reimplemented by OpenAT.
Sources: our behaviour specs (`docs/spec/findings/items-security-special.md`, `ai-robots.md`,
`economy.md`, `sim-core.md`, `rooms-and-ui-3b-dialog-trees.md`, `airport-and-people.md`), every number
re-checked against the reference source. Formulas are restated in our own words; no game text, no
decoded data tables. Money is in whole currency units, time in game minutes unless "ticks" is
stated (one sim step).

Conventions: *saboteur* = the airline ordering a prank, *victim* = the target airline, *robot* =
computer airline. "Slot" = airline index 0..3. `[M]` marks a fact read but not fully traced.

---------------------------------------------------------------------------------------------------

## 1. Access to the saboteur

* The saboteur's room sits behind a secret door in the airport (a wall section that rotates when
  someone uses it). In the free game it is part of the shop wing (the same segment as the route box
  and the kerosene counter). The door is always usable; what is locked is the saboteur himself.
* Each airline has a **saboteur trust** `T` (0..5), starting at 0. With `T = 0` the saboteur only
  brushes the player off.
* The only way to `T ≥ 1`: buy the **gun in a violin case** at the duty-free shop and use it while
  standing at the **kerosene counter** (the kerosene dealer). The case is consumed, `T := max(T, 1)`,
  and the dealer's dialog opens.
  * The case is offered from day 1 on (not on day 0), only while `T = 0` and the player does not
    already carry one. Price 80 000 (read from the install's text resource).
* After that the dialog runs: pick the victim (one of the three other airlines; an eliminated one is
  refused) → pick the category (plane / personal / special) → pick the act.

### 1.1 Unlocking further acts ("levels")

Offered acts per category: acts `1 .. min(cap, T)` with cap = 5 (plane), 4 (personal), 6 (special).
Trust growth when a human buys an act:

| bought | new trust |
|---|---|
| plane act (any) | `T := min(5, T + 1)` |
| personal act k | `T := max(T, k + 1)` |
| special act k, k = 1..4 | `T := max(T, k + 1)` |
| special act 5 or 6 | original: unchanged (bug: the code takes the max with a large negative number); OpenAT default: `T := max(T, k + 1)` |

In the original the ceiling is therefore `T = 5`. Consequences (original):

* Special act 6 (route theft) needs `T = 6` and is **unreachable** in normal play.
* Fastest path to everything reachable: buy personal act 4 or special act 4 once (both need `T ≥ 4`),
  or climb by single steps. E.g. from `T = 1`: personal 1 → `T = 2`, personal 2 → 3, personal 3 → 4,
  personal 4 → 5.

The trust step for special acts 5/6 uses the wrong dialog-id base (a slip); with the intended value
the impound gives `T = 6` and unlocks route theft. OpenAT fixes this by default (below).

### 1.2 Order rules (in this order)

1. `T = 0` → brush-off.
2. Victim eliminated → refused.
3. **Only one pending act** at a time over all three categories → refused while anything is pending.
4. Victim has the matching protection (§3) → refused with a victim-specific line, nothing charged.
5. Affordability: refused if `money − price < −1 000 000` (the overdraft limit). **Original bug:** special
   acts 5 and 6 skip this check.
6. Personal act 2 (laptop virus) needs the victim to carry a laptop; otherwise refused, no charge.
7. Payment is immediate (plane acts: once the plane is picked). The bodyguard rebate (§1.6) applies.
8. On acceptance the victim's "security needed" bit for that act is set (§3.3) — not for special 5/6
   bought by a human.

#### Implementation in OpenAT
- Implemented: trust gate and options `Assets/OpenAT/Sim/Items/SabotageEngine.cs:41-46`; order checks in
  the original order `SabotageEngine.cs:57-78` (5/6 debt-checked by default,
  `GameOptions.FixSabotageSpecialAffordability`; off = unchecked as in the original); trust growth and
  payment `SabotageEngine.cs:92-128`; gun-case use `Sim/Items/ItemEngine.cs:237-240`; duty-free offer rule
  `ItemEngine.cs:55`; dialog `Assets/OpenAT/View/Rooms/Sabotage/SabotageRoom.cs:146-239`.
- Fixed by default: special 5/6 raise trust to `k + 1` (`GameOptions.FixSpecialPrankTrust`; a plane act
  then no longer lowers `T = 6` back to 5), so route theft is reachable after the impound; a human's
  special 5/6 order sets the victim's "needed" bit (`FixSpecialPrankNeeded`). Off restores the original.
- Option `GameOptions.RouteTheftReachable` offers act 6 already from `T = 5` (addition, off by default).

### 1.3 Prices and evidence

Prices are fixed (no inflation, no difficulty scaling). "Evidence" `H` is the saboteur's hint
counter used for exposure (§1.5).

| category | act | price | evidence added | needs T |
|---|---|---|---|---|
| plane | 1 salt in the tank / minor | 1 000 | 2 | 1 |
| plane | 2 skeleton in the cabin | 5 000 | 4 | 2 |
| plane | 3 flat tyre | 10 000 | 10 | 3 |
| plane | 4 fire | 50 000 | 20 | 4 |
| plane | 5 "superhero" stunt | 100 000 | 100 | 5 |
| personal | 1 bacteria in the coffee | 10 000 | 8 | 1 |
| personal | 2 laptop virus | 25 000 | **0** | 2 |
| personal | 3 office bomb | 50 000 | 25 | 3 |
| personal | 4 provoke a strike | 250 000 | 40 | 4 |
| special | 1 fake brochures | 100 000 | 8 | 1 |
| special | 2 phones dead | 500 000 | 15 | 2 |
| special | 3 press release | 1 000 000 | 25 | 3 |
| special | 4 bank hack | 2 000 000 | 30 | 4 |
| special | 5 impound a plane | 5 000 000 | 50 | 5 |
| special | 6 steal a route | 8 000 000 | 70 | 6 (unreachable) |

(The short labels are our own descriptions of what the newspaper picture shows.)

Evidence is added **when the act fires**, not when it is ordered.

### 1.4 Effects on the victim

**Plane acts** — fire on the hour. The order is first *armed* once the target plane stands on the
ground; after that it fires on the first hourly tick on which the plane is **airborne** (acts 1, 2, 4,
5), or — flat tyre — on the hourly tick equal to the plane's next scheduled departure while it is
still on the ground. If the plane is sold first, the order lapses (no refund).

Let `S` = victim's share price, `D` = victim's dividend trust (investor confidence, ≥ 0), `I` = image
(−1000..1000), `R` = image of the route the plane is flying next (only if that flight is a route
flight), `Y` = victim's sympathy toward the saboteur.

| act | money | S | D | I | R | Y | extra |
|---|---|---|---|---|---|---|---|
| 1 | — | ×0.95 | −1 | — | ×99/100 | −5 | |
| 2 | −2 000 | ×0.90 | −2 | −2 | ×95/100 | −15 | |
| 3 | −8 000 | ×0.85 | −3 | −3 | ×90/100 | −35 | next departure **+1 h**, advisor message |
| 4 | −40 000 | ×0.75 | −4 | −8 | ×50/100 | −80 | |
| 5 | −70 000 | — | −5 | — | — | −200 | |

* No act changes plane condition. The hourly share recalculation pulls the price toward 10 × `D`,
  so the `D` loss keeps depressing the price after the instant multiplier.
* Bookkeeping: victim "accidents" statistic +1, saboteur "sabotage" statistic +1, a newspaper
  headline, a letter/fax/advisor note to a human victim, a phone call from the saboteur to a human
  saboteur.
* Original bug: the flat tyre's delay statistic is booked to the **saboteur** (OpenAT books it to the victim by default).
* Original bug: if the plane has no next flight when the act fires, only the two statistics are counted and
  the order stays pending — it re-counts every hour until a flight exists.

**Personal and special acts** execute the next morning when everyone leaves the briefing, and last
that day (until the next day change):

| act | effect |
|---|---|
| P1 bacteria in the coffee | victim is "sick": between 09:00 and 18:00, every 127th tick (not at minute 0) a 1-in-4 roll sends the boss running to the toilet. `Y −10`. Cured by the tablets (§4.4). |
| P2 laptop virus | victim's laptop infected if it is carried and clean (§5). No evidence, no sympathy change. |
| P3 office bomb | the victim walks to his office; at the door it explodes: office unusable for the rest of the day, way to it blocked between 08:00 and 18:00. `Y −50`. A human trying to enter the ruin gets the glowing coal (§4.4). |
| P4 strike | sets "strike planned"; between 07:00 and 17:00, once the victim has a plane on the ground or landing within 2 h, a strike starts: 2 h if > 10 strike-free days, 72 h if the local player's average staff mood < −10, else 4..23 h. Human departures are delayed while it lasts. |
| S1 fake brochures | all image the victim's flights earn today goes to the **saboteur**; arriving passengers carry the saboteur's logo. |
| S2 phones dead | victim cannot use the mobile phone and cannot be phoned today. |
| S3 press release | the victim's route flights book only `1 + (n mod 3)` passengers per class today; all are recalculated at once. Letter to the local player. |
| S4 bank hack | victim −1 000 000, saboteur +1 000 000 (net cost of the act 1 000 000). Can push the victim into bankruptcy. |
| S5 impound | chosen plane gets a 15-hour "pseudo problem": each hour, a departure due within the next hour is pushed back 1 h; counts down 1 per hour. **Original bug: only human airlines' departures are checked, so it does nothing to robots** (fixed by default, `FixImpoundComputerPlanes`). |
| S6 route theft | the route and its reverse are removed from the victim (lower-ranked holders move up). **Original bug:** the hand-over to the saboteur never happens (fixed by default, `FixRouteTheftTransfer`). |

Note for robots as victims: robots have no staff and their flights are never delayed by strikes, so
P4 against a robot only produces a headline. S5 has no effect either in the original (above; OpenAT fixes that by default).

#### Implementation in OpenAT
- Implemented: price/evidence/sympathy tables `Sim/Items/Sabotage.cs:100-112`; hourly arming and
  firing `SabotageEngine.cs:189-268`; victim economics `Sim/Player.Finance.Events.cs:11-28`; route image
  `SabotageEngine.cs:232-237`; flat tyre delay `SabotageEngine.cs:239-258`; morning execution
  `SabotageEngine.cs:321-420`; nightly clear of day effects and evidence decay
  `SabotageEngine.cs:464-471`; toilet runs `Sim/Items/ItemSystems.cs:170-198`; bomb
  `ItemSystems.cs:201-206` and office block `ItemSystems.cs:92-98`; strike start
  `Sim/Finance/FinanceSystems.cs:343-357`; brochures `Sim/Fleet/FlightOps.cs:236-240` and
  `Sim/Hall/HallPlanes.cs:280-281`; press release `Sim/Fleet/RoutePassengers.cs:31`; impound delay
  `Sim/Fleet/FlightOps.cs:343-355` (human-only guard `FlightOps.cs:351`); route theft
  `SabotageEngine.cs:440-459`.
- Original bugs fixed by default (switch off = original): `FixSabotageDelayStatistic`, `FixSabotageRetrigger`,
  `FixImpoundComputerPlanes` (`ItemSystems.cs:115-137`), `FixRouteTheftTransfer`
  (`Sim/GameOptions.cs:50-58`).

### 1.5 Detection, fine and bankruptcy

There is **no random detection roll**. Exposure is deterministic from the evidence counter:

```
on firing:        H := H + h(act)                       (table §1.3)
every night:      H := max(0, H − 3)
at the briefing:  for every act listed (yesterday's acts):
                    H <  15  → "no idea who did it"
                    H <  40  → "suspicions"
                    H < 100  → "strong suspicions"
                    H ≥ 100  → culprit named, fine F = 10 000 × H paid to the victim
```

* `H` is the culprit's **current** value at the briefing (after the night's −3).
* `H` is not reset by the fine: while `H ≥ 100` every further listed act is fined again.
* Minimum fine is 1 000 000. The fine is mandatory (booked even into deep debt), and the briefing
  cannot be skipped while a fine is pending.
* The bankruptcy check follows in the same briefing: cash below **−5 000 000** ends the game for a
  human (a robot is removed). So a fine can directly cause game over.
* Robots accumulate evidence and are fined exactly like humans.
* Glue, stink bomb, stink perfume, pliers and spiked chocolates are **not** acts: no evidence, no
  trust, no fine.

**Timing subtlety.** A plane act fires during the day, so it suffers one night of decay before its
briefing. Plane act 5 alone gives `H = 100` → `97` at the briefing → not exposed. If the plane only
takes off after midnight (night flights, the sim keeps running), it reaches the briefing at 100 and
is exposed.

**Worked example (all one human, no previous evidence).**

| day | action | H after the act | H at next briefing | verdict / fine |
|---|---|---|---|---|
| 5 | buys personal 4 (strike), 250 000; trust 4 → 5 | — | — | |
| 6 | strike executes when leaving the briefing | 40 | 37 (day 7) | "suspicions" (37 < 40) |
| 7 | buys special 4 (bank hack), 2 000 000 | — | 34 (day 8, nothing listed) | |
| 8 | hack executes after the briefing; +1 000 000 back | 64 | 61 (day 9) | "strong suspicions" |
| 9 | buys plane 5 (100 000); fires at 14:00 | 161 | 158 (day 10) | **fine 1 580 000** |
| 10 | buys personal 1 (10 000), executes day 11 | 155 + 8 = 163 | 160 (day 12) | **fine 1 600 000** |

Getting back under 100 from 160 takes `⌈(160 − 99) / 3⌉ = 21` quiet days.

#### Implementation in OpenAT
- Implemented: report and fine `SabotageEngine.cs:284-314` (runs at 09:00 before the boss's review,
  `ItemSystems.cs:72`); nightly decay `SabotageEngine.cs:470`; briefing lines and fine display
  `View/Rooms/Items/BossSabotageReport.cs:50-54`.
- Matches the original, including the "hints at briefing time" rule. The repeated fine while `H ≥ 100`
  is kept: it is the designed rule (no code path consumes the evidence; the same counter drives the
  warnings and the naming, and the nightly decay is the way back), see `docs/QUIRKS.md`.

### 1.6 Bodyguard rebate

With a security advisor of skill `q > 20` on staff, each sabotage payment (and most shop purchases:
kerosene, planes, ads, duty-free goods) refunds

```
rebate = ⌊price / 100⌋ × ⌊q / 10⌋
```

Example: skill 75, special 3 (1 000 000) → 10 000 × 7 = 70 000 back.

#### Implementation in OpenAT
- Implemented: `Sim/Player.Finance.Upkeep.cs:41-49`, called from `SabotageEngine.cs:117`,
  `ItemEngine.cs:72`, `ItemEngine.cs:104`.

---------------------------------------------------------------------------------------------------

## 2. How computer players sabotage

Free game: robots may sabotage **humans and robots** (the "may sabotage humans" feature is on), use
the security office, and never pay for acts.

### 2.1 Who they dislike

Each airline holds a sympathy value `Y[a→b]` toward every other airline.

* Start: 50 toward everyone, 60 toward the next slot `(a + 1) mod 4`. Robots start "cooperating"
  with the other robots, nobody cooperates with a human.
* Changes:
  * being sabotaged by `b` (plane acts −5/−15/−35/−80/−200, coffee −10, bomb −50) — applied
    **whether or not** the culprit is ever exposed: the robot always "knows";
  * daily envy drift (only while `Y < 25` and the other airline's day balance `B_b > 0`, only toward
    non-robots):
    * if `Y < −50` and `B_b > B_a`: −10 if `B_b > B_a + 1 000 000`, and −2 if `B_b > B_a + 100 000`;
    * else if `B_b > 2·B_a`: −5 if the lead > 1 000 000, and −1 if > 100 000;
  * then, every day, if `|Y| > 70`: `Y := ⌊Y × 69 / 70⌋` (pulls extremes back toward ±70 but never
    across zero);
  * talks and gifts (§6).
* The **disliked rival** is drawn uniformly from all active airlines with `Y < 0`. Only one draw per
  action.

### 2.2 The decision (each time the robot's action is "visit the saboteur")

```
target := disliked rival
if none: with chance 1/3 (always for slot 0): a random slot (self or eliminated → none)

skip if  H ≥ 100  or  image ≤ −500  or  an act is pending
go   if  ((day + slot) mod 3 ≠ 0  or  1-in-4  or  (slot 0 and 1-in-2))
         and a target exists
         and (H < 80  or  (slot 1 and H < 90 and credit < 1 000 000 and money > 3 000 000 and day mod 4 = 0))
```

Never before day `1 + slot`.

On "go": robot trust `T := min(4, T + 1)`. The **game hour mod 3** picks the category
(0 plane, 1 personal, 2 special); up to 2 passes:

* plane: severity `min(4, T)` against a random plane of the target (target must own planes);
* personal: severity `min(4, T)`; severity 1 has a 50 % chance to become the laptop virus; the virus
  falls back to coffee if the target has no laptop;
* special: severity `min(5, T)` but `T ≤ 4`, so at most the bank hack (target must own planes);
* personality caps: slot 0 — 2/3 chance plane ≤ 2 and 2/3 chance personal = 1 and special = 1;
  slot 2 — 2/3 chance plane ≤ 3 and personal ≤ 3;
* if the target's protection blocks the choice it is dropped and an "annoyance" counted; a second
  pass re-tries the same category (same hour).
* If everything was blocked, there were ≥ 2 annoyances, the day is even and the pliers lie in the
  saboteur's room, the robot takes them and goes to cut the security office's power (§3.4).

So robots never use plane 5 or special 5/6, and early on (low trust) only mild acts.

**What this means for the player.** A rival you never hurt still sabotages you: slot 0 picks a random
victim when it dislikes nobody, the others with 1/3; and envy makes robots dislike a human who
earns much more. Sabotaging a robot makes it dislike you (most acts), which makes you its preferred
target — retaliation. Robots stop when their own evidence reaches 80 (90 for the slot-1 rich case),
so they rarely get fined.

#### Implementation in OpenAT
- Implemented: sympathy init `Sim/AI/RobotBrain.cs:35-36`; envy drift `Sim/AI/AiSystems.cs:119-146`;
  disliked rival `Sim/AI/RobotExecutor.cs:111-115`; decision and passes
  `Sim/AI/RobotExecutor.Rivals.cs:28-118`; order placement `Sim/Items/RivalActions.cs:17-24` →
  `SabotageEngine.cs:126-139`; sabotage sympathy goes to the AI's ledger (`Sim/World.AI.cs:21,30-34`).
- Deviation (deliberate): the 50 % coffee→virus roll uses the seeded robot generator instead of the
  unseeded C runtime random (`RobotExecutor.Rivals.cs:84`), keeping the sim deterministic.
- Implemented: the "no laptop → coffee" fallback tests whether the victim carries a laptop
  (`RobotExecutor.PersonalActFor`, `Sim/AI/RobotExecutor.Rivals.cs:122`), like the original.

---------------------------------------------------------------------------------------------------

## 3. Security office

In the free game the office is in the main-entrance segment of the airport (hidden entrance,
hotkey Y). A dialog with the officer opens a menu with 9 rows. **Clicking a row toggles it**; there is
no up-front fee. Fees are billed **every night**:

```
daily cost = Σ flat rows + (Σ per-plane rows) × planes owned
```

| row | protection | daily price | billed | blocks |
|---|---|---|---|---|
| 0 | office | 25 000 | flat | P1 coffee, P3 bomb |
| 1 | laptop (needs a laptop to select) | 20 000 | flat | P2 virus |
| 2 | personnel office | 25 000 | flat | P4 strike |
| 3 | bank | 30 000 | flat | S4 bank hack |
| 4 | route board | 30 000 | flat | **nothing** (only a padlock picture in the planner) |
| 5 | administration | 30 000 | flat | S2 phones, S3 press release, S6 route theft |
| 6 | plane interior | 30 000 | per plane | plane 1, 2, 5 |
| 7 | plane exterior | 30 000 | per plane | plane 3, 4 |
| 8 | gate scanner L1 / L2 / L3 | 40 000 / 90 000 / 140 000 | per plane | S1 brochures (quirk), S5 impound |

* The gate row cycles off → L1 → L2 → L3 → off. Levels 2/3 cost more but block nothing extra;
  L1 rejects 1 in 30 boarding passengers at the scanner (they leave), L2/L3 instead hold each one
  for 240 ticks (animation only).
* Protection is checked **only when an act is ordered**; buying it later does not cancel a pending
  act.
* Full protection with 10 planes: 160 000 flat + (30 000 + 30 000 + 40 000) × 10 = 1 160 000 per
  day (gate L1).

### 3.1 Robots buying protection

Robot visit: two rolls of a random row 0..8. If money > 1 000 000 the rolled row is switched **on**
only if that act was already ordered against the robot ("needed" bit). If money ≤ 1 000 000 the
rolled row is switched **off**.

### 3.2 Power outage (pliers)

The pliers lie in the saboteur's room once per day. Using them next to the security office entrance
consumes them, switches **every** airline's protection off and closes the office for 3 days.
Walking into a rival's office with them instead cuts that office's power for the day (dark
overlay; no gameplay lockout found `[M]`).

#### Implementation in OpenAT
- Implemented: bits and gate cycle `Sim/Items/Security.cs:7-36`; fees
  `Sim/Player.Finance.Upkeep.cs:18-19,54-73` billed from `Sim/Finance/FinanceSystems.cs:149`; block map
  `Sim/Items/Sabotage.cs:130-148`; toggle command and outage closure
  `Sim/Items/ItemCommands.cs:282-299`, `Sim/Hall/HallPlayers.cs:31`; robot visit
  `Sim/Items/ItemSystems.cs:234-246`; outage `Sim/Items/ItemEngine.cs:434-440`, counter
  `ItemSystems.cs:46`; gate scanner `Sim/Hall/HallCustomers.cs:690-708`; room/menu
  `View/Rooms/Security/SecurityRoom.cs:20,147-215`; cosmetic badges/signs
  `View/Rooms/Office/OfficeRoom.cs:135`, `View/Rooms/Personal/PersonalRoom.cs:119`,
  `View/Rooms/PlaneProps/PlanePropsRoom.cs:279`, `View/Rooms/Planner/LaptopRoom.cs:234,451`,
  `View/Airport/AirportScreen.cs:610-613`.
- Robot pliers: `RobotExecutor.Rivals.cs:112-115,155-159`.
- Implemented: cutting a rival's office power with the pliers — the hall's "wrong room" step at a rival's
  intact office door (not the personnel office) uses the pliers up (`Sim/Hall/HallPlayers.cs:563-566` →
  `ItemEngine.CutOfficePower`); dark overlay `View/Rooms/Office/OfficeRoom.cs:139`, `GlobeRoom.cs:261`.

---------------------------------------------------------------------------------------------------

## 4. Carried items

### 4.1 Inventory

* 6 slots; picking something up fails silently when full. Slots are re-sorted after each change.
* Using an item = clicking its slot (not while the clock is stopped). If the item has a special
  effect here it applies; else, if the room has a person, they react ("what's that for?") and nothing
  happens. In the newspaper kiosk items do nothing unless the headline is "interesting".
* **World items** (glove, paper clip, glue, glowing coal, perfume, pliers, postcard) exist **once
  per day for all players**: the first to take one removes it for everyone; restored each night.

### 4.2 Duty-free goods (bought)

Prices are read from the install's text resource at run time; the values below are those of the
English install.

| item | price | when offered | use |
|---|---|---|---|
| laptop (4 grades) | 10 000 / 30 000 / 50 000 / 100 000 | from day 8; **one laptop sale per day for the whole airport**; each buy upgrades one grade (old one replaced) | portable planner, usable anywhere; battery §5 |
| gun in a violin case | 80 000 | day ≥ 1, saboteur trust 0, not carried | §1: unlocks the saboteur |
| address book (filofax) | 5 000 | not carried | list of rooms already visited; pick one and the boss walks there |
| mobile phone | 300 000 | not carried | phone book from anywhere; needed to be called by rivals; dead under S2 |
| beer | 5 000 | not carried | workshop: calms the mechanic, mechanic trust ≥ 1 (unlocks the oil can). Elsewhere without a partner: drink it → drunk (+400 units of cursor wobble, −1 per tick) |
| chocolates / spiked chocolates | 10 000 | via the saleswoman, only if neither is carried | §6.2; eaten: nothing / drunk +400 |

There is no overdraft check on duty-free goods. Robots get laptop upgrades (same daily lock,
money > 100 000, day > 7) and a phone (money > 100 000, day > slot) **free**.

### 4.3 Laptop battery and virus — see §5.

### 4.4 Trade-chain and found items

Trust flags below are per player and permanent for the game (kiosk trust is used up by one stink
bomb). Once a flag is set, the reward is handed out again on later talks whenever a slot is free
`[M]`.

| item | how to get it | use → effect |
|---|---|---|
| bra | plane broker's room: click the storage hatch (not while carrying it or the horseshoe, not after the drinker trust) | give to the duty-free saleswoman → next talk gives the **horseshoe** |
| horseshoe | duty free, after the bra | give to the drinker at Rick's → drinker trust. Afterwards, **during** one of your staff strikes, talking to the drinker ends it after a 10-minute countdown (4 if you ask again). It does not prevent strikes. |
| beer → oil can | beer to the mechanic (trust ≥ 1), then the oil can from the workshop | use in the office globe room → the globe stops squeaking (cosmetic) |
| spider | catch it in the travel agency while its animation is in catchable frames | in the saboteur's room (saboteur trust ≥ 1) → spider trust; then the dartboard gives the **dart** |
| dart | saboteur's dartboard (after the spider) | advertising agency → ad trust; next talk gives the **anti-virus diskette** |
| diskette | advertising agency (after the dart) | anywhere, while your laptop is infected → cured at once, diskette consumed. It is a cure, not a shield. |
| postcard | bulletin board in the supervisor's office (world item) | own personnel office → personnel trust; the personnel manager then gives the **tablets** |
| tablets | personnel office (after the postcard) | anywhere while sick from P1 → cured, consumed |
| glove | kerosene counter (world item) | at the drinks machine on the airport floor it turns into an **energy drink**. Without a glove the machine shocks the boss (cosmetic smoke; also sobers him up). |
| energy drink | drinks machine (with glove) | give to the kiosk man → kiosk trust; next kiosk talk gives the **stink bomb**. Drunk with no partner: caffeine 2400 ticks → the boss walks about 3× as fast on the airport floor. |
| stink bomb | kiosk (after the energy drink) | **lower** airport level only: a stink cloud at your feet for 3960 ticks; people keep away |
| paper clip | route box (world item) | freight counter: unlocks the glue (clip consumed) |
| glue | freight counter after the clip (world item) | **upper** level only, standing still: glues the floor tile in front; any boss stepping on it is stuck 600 ticks (and cannot be phoned) |
| glowing coal | trying to enter an office destroyed by P3 (humans only, world item) | last-minute counter or travel agency: **all remaining offers there are burnt** for everyone. Cools to useless coal overnight. |
| perfume | ladies' toilet, players 1–3 (player 0 uses the gents' and finds none) (world item) | in the workshop while the slime event is active (resets every 6th day) → stink perfume |
| stink perfume | workshop | airport: you stink for 2000 units; people keep away |
| pliers | saboteur's room (world item) | §3.2 |
| money in the bank bin | at game start, chance 24 % (`roll(100) > 75`) | first player to click it gets +100 000; once per game |

#### Implementation in OpenAT
- Implemented (sim): ids and prices `Sim/Items/ItemIds.cs:11-122`; inventory
  `Sim/Items/Inventory.cs:15-90`; world items `Sim/Items/WorldItems.cs:15,39-43`; purchases
  `Sim/Items/ItemEngine.cs:51-110`; pick-ups and rewards `ItemEngine.cs:137-210`; all item uses
  `ItemEngine.cs:217-364`; glue `ItemEngine.cs:394-415` with the stuck effect
  `Sim/Hall/HallPlayers.cs:274-278`; stink bomb `ItemEngine.cs:418-431`; caffeine/drunk/stench decay
  `ItemSystems.cs:140-166`; drinker strike end `Sim/Items/ItemCommands.cs:260-270`,
  `Sim/Items/RoomItemCommands.cs`; bank bin `Sim/Finance/FinanceCommands.cs:163-173`,
  `Sim/Finance/FinanceSetup.cs:86`; robot laptop/phone `Sim/AI/RobotExecutor.cs:305-306`.
- Fixed by default: a first laptop is refused with a full inventory instead of being charged and not
  handed over (`GameOptions.FixLaptopFullInventory`, `Sim/Items/ItemCommands.cs` `BuyLaptopCommand`);
  robots pay the human rocket-part prices (`FixRobotRocketPrices`, `Sim/Items/SpaceParts.cs`). The
  coffee prank's toilet run takes a robot out of its room and stuns it 20 ticks as the original does,
  and can no longer leave a figure stuck in the "running to the toilet" state (`ItemSystems.cs`
  SickStep; `Sim/Hall/HallPlayers.cs` EnterRoom, `Sim/Hall/HallState.cs` NewDay).
- Implemented (view): duty free `View/Rooms/DutyFree/DutyFreeRoom.cs:56-181`; item clicks
  `View/Rooms/Items/ItemUseView.cs:27-34`; pills `View/Rooms/Personal/PersonalDialogs.cs:46`;
  diskette `View/Rooms/Werbung/WerbungRoom.cs:189`; stink bomb `View/Rooms/Kiosk/KioskRoom.cs:264`;
  postcard `View/Rooms/Tafel/TafelRoom.cs:123`; dart and pliers `View/Rooms/Sabotage/SabotageRoom.cs:110-112,239`;
  perfume `View/Rooms/Items/SabotageNotifications.cs:232`; drinker `View/Rooms/Ricks/RicksRoom.cs:193-200`;
  bank bin `View/Rooms/Bank/BankRoom.cs:121`; globe squeak `View/Rooms/Planner/GlobeRoom.cs:223`.
- Implemented (view hook-ups of the chains): horseshoe reward at the duty free
  (`ReceiveRewardCommand`, `View/Rooms/DutyFree/DutyFreeRoom.cs:179`, bra line 801); glove on the kerosene
  counter (`View/Rooms/ArabAir/ArabAirRoom.cs:66`, the violin case's line 1010); bra behind the broker's wall
  flap, clickable once the flap is open (frame 3; `View/Rooms/Makler/MaklerRoom.cs:107`, ONCE animations now
  hold their last frame, `Rooms/Framework/FrameAnimation.cs`); spider caught while it runs, until the snake
  strikes at frame 11 (`View/Rooms/Counters/TravelAgencyRoom.cs:96-117`); oil can from the mechanic with trust
  (`View/Rooms/Werkstatt/WerkstattRoom.cs:122-140`, beer line 10000) and the workshop slime that the stink
  perfume needs (`WerkstattRoom.cs:69-90` → `SetWorkshopSlimedCommand`); glue pot at the freight counter
  (`View/Rooms/Counters/FreightRoom.cs:121-133`); glowing coal and the "destroyed" notice when walking to a
  bombed office (`Sim/Hall/HallPlayers.cs:47-54`, `View/Rooms/Items/SabotageNotifications.cs:119`); the trade
  dialogs are opened with the talker the original names (the energy drink reaches the sleeping kiosk man,
  `View/Rooms/Items/ItemUseView.cs` `AppliedTalker`); the spider is refused (line 1021) while the saboteur
  has no trust (`View/Rooms/Sabotage/SabotageRoom.cs:188-190`). The paper clip at the route box is hooked
  up (`View/Rooms/RouteBox/RouteBoxRoom.cs`). The drinks machine is hooked up too: glove → Red Bull with
  the vending sound, otherwise the shock (animation only then) with the fuse sound, both at effects × global
  ambient volume (`HallRequest.ColaMachine` in `View/Rooms/Framework/GameFlow.cs`, `Sim/Hall/HallPlayers.cs`).
  Glue and stink bomb are used from the hall's status bar (`View/Airport/AirportScreen.cs` `UseItem`).
  Tests: `tests/OpenAT.Tests/ItemsChainTests.cs`.

---------------------------------------------------------------------------------------------------

## 5. Laptop battery and virus

**Battery** (game minutes per day, refilled at the day change):

| grade | 1 | 2 | 3 | 4 |
|---|---|---|---|---|
| minutes | 40 | 80 | 200 | 1440 |

Drains 1 per game minute while the laptop view is open (only while the day is running). Warning
beep at 2, planner inert at 0 (you can still leave).

**Virus lifecycle** (state 0 clean, 1 infected, 2 at the repair shop, 3 repaired and waiting):

* P2 sets 1 if the victim carries a clean laptop. In state 1 the laptop shows broken buttons; planes
  cannot be dragged, planner buttons do nothing.
* Cures: the diskette (instant, any place), or give the laptop to the duty-free saleswoman
  (1 → 2, laptop leaves the inventory), next night 2 → 3, next visit she returns it free (→ 0).
* Robots' laptops are cleaned automatically every night.
* Protection: security row 1 (needs the laptop to subscribe).

#### Implementation in OpenAT
- Implemented: battery table `Sim/Items/ItemIds.cs:59`; nightly refill `ItemSystems.cs:60`; drain
  `ItemSystems.cs:100-109`; virus night step `ItemSystems.cs:54-58`; repair `ItemEngine.cs:113-129`;
  diskette `ItemEngine.cs:272-280`; laptop view virus and dead battery
  `View/Rooms/Planner/LaptopRoom.cs:28-31,130-142`; duty-free virus dialogs
  `View/Rooms/DutyFree/DutyFreeRoom.cs:84,96`.

---------------------------------------------------------------------------------------------------

## 6. Rival relations and cooperation

### 6.1 Talking to a rival (phone or face to face)

All values are the partner's sympathy toward you, clamped to ±1000.

| your move | effect |
|---|---|
| propose cooperation | a robot accepts if its sympathy > 0 |
| accept a proposal | both cooperate, sympathy +30 |
| decline a proposal | −10 (a robot sulks for a further −20) |
| cancel cooperation bluntly | off, −200 |
| cancel cooperation politely | off, no sympathy change |
| flatter | +10 |
| end the call politely | −10; cooperation ends if sympathy drops below −100 |
| hang up rudely | −100, cooperation ends |

A robot phones a human (09:00–18:00, the human has a working phone, robot in its office or owns a
phone) to **propose** cooperation when it does not cooperate and sympathy > 50 (once until the
"called" counter decays), or to **cancel** it when it cooperates and sympathy < −20. Rick's café
hints when a rival with positive sympathy is not cooperating with you.

### 6.2 Gifts

Chocolates offered face to face to a robot: +10; spiked ones −10. To a human rival: spiked ones make
them drunk.

### 6.3 What cooperation gives

Only access to the partner's **branch offices** in your phone's branch list: you can phone those
cities and take their foreign orders and freight as if you had a branch there. No shared rent, no
route sharing, no sabotage immunity (robot targeting reads sympathy only). Robots extend the same
branch access to the robots they cooperate with.

#### Implementation in OpenAT
- Implemented: talk effects `Sim/Rivals/RivalTalkCommand.cs:10-40`, dialog
  `View/Rooms/Rivals/RivalDialog.cs`; robot calls `Sim/AI/RobotExecutor.cs:124-125`; branch list with
  partners `View/Rooms/World/WorldRoom.cs:298`; robot abroad orders with partners
  `Sim/AI/RobotExecutor.Orders.cs:64`; Rick's hint `View/Rooms/Ricks/RicksRoom.cs:167`; chocolates rule
  `Sim/Items/ItemEngine.cs:460-466`.
- Implemented: offering chocolates face to face — the main list becomes 2010..2013 while a box is carried
  (`View/Rooms/Rivals/RivalDialog.cs`, case 2012 → `OfferChocolatesCommand`); the normal box is given
  first and the robot's sympathy is clamped to ±1000 (`Sim/Items/ItemEngine.cs:460-475`). Network
  partners are not covered.

---------------------------------------------------------------------------------------------------

## 7. Deluxe extras

Compared with the older editions described by the third-party guide: the **special** category of
acts (brochures, phones, press, bank hack, impound, route theft), the **security office** and the
pliers, and the airport pranks (glue, stink bomb, stink perfume, glowing coal) are present in Deluxe;
the guide only knows plane and personal acts `[M: edition attribution taken from the guide's
coverage, not from version history]`.

---------------------------------------------------------------------------------------------------

## Guide claims

| ID | claim (paraphrase) | verdict for Deluxe | OpenAT status |
|---|---|---|---|
| F4 | Duty free sells gifts (liquor, chocolates, violin case) and tools (laptop, filofax, phone) | **True.** The violin case is the saboteur key; laptop from day 8, one sale per day airport-wide | Implemented (`ItemEngine.cs:51-110`, `DutyFreeRoom.cs`) |
| F18 | Hidden security office upstairs beside the manager; protects laptop, office, each plane; very expensive | **Mostly true.** Hidden, 9 protections, 20 000–140 000 per day, plane rows per plane. Location: in the free game it is in the main-entrance segment, not the supervisor's shop segment `[M]` | Implemented |
| F19 | Hidden sabotage shop, needs clearance; plane or personal acts; more unlock after sabotaging | **True**, plus a third "special" category in Deluxe. Clearance = gun case used at the kerosene counter; unlocking by trust (§1.1) | Implemented |
| T2 | Filofax lists rooms already visited; laptop works like the planner and can get a virus | **True** | Implemented (laptop, virus, filofax screen) |
| T7 | Unsabotaged rivals rarely sabotage you; sabotage makes them dislike you and retaliate; more sabotage = higher chance to be caught; fine big and unavoidable; no money → game over; glue/stink bomb don't count | **Partly.** Retaliation and dislike true; but random targeting (slot 0 always, others 1/3) and envy also make non-victims sabotage you. Being caught is **not a chance** — deterministic at evidence ≥ 100, decays 3/day. Fine = 10 000 × evidence (≥ 1 000 000), mandatory; cash < −5 000 000 at the briefing = game over. Glue and stink bomb add nothing | Implemented; robot laptop fallback bug (§2) |
| T8 | Give the violin case to the kerosene dealer and he reveals the entrance | **Partly.** Using the case at the kerosene counter raises saboteur trust to 1; the secret door itself is always there, the saboteur just won't deal before | Implemented |
| T9 | Postcard from the manager's board → personnel lady → tablets; cure bacteria-in-coffee toilet runs | **True** (bulletin board in the supervisor's office; own personnel office) | Implemented, playable |
| T10 | Broker's storage → bra → duty-free girl → horseshoe → Rick; prevents strikes | **Partly.** Chain correct (horseshoe goes to the drinker at Rick's), but it **ends** a running strike after a short countdown, it does not prevent strikes | Implemented, playable |
| T11 | Glove from the kerosene counter → drinks machine (shocks without glove) → energy drink → kiosk man → stink bomb; usable anywhere; no penalty | **Mostly.** Chain and "no penalty, no relationship loss" true; the bomb works only on the **lower** airport level | Implemented, playable |
| T12 | Liquor → mechanic → oil can → globe stops squeaking | **True** (beer gives mechanic trust; cosmetic) | Implemented, playable |
| T13 | Paper clips from the route board → freight man → glue; puddle sticks rivals | **True**, the clip comes from the route box; glue works only on the **upper** level, sticks any boss 600 ticks | Implemented, playable |
| T14 | Spider at travel agency → saboteur; dart → ad agency; then the disk protects the laptop from viruses | **Partly.** Chain true (needs saboteur trust ≥ 1). The diskette **cures** an infected laptop once; it does not protect (protection is the security office's laptop row) | Implemented, playable |
| T15 | Bank waste bin sometimes holds money | **True:** 24 % chance per game, +100 000, once | Implemented |
