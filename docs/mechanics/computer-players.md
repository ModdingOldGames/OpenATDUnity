# Computer players (free game)

How the three computer airlines play a free game, and the formulas by which they react to the
human. This is an overview; the full behaviour spec with every condition is
`docs/spec/findings/ai-robots.md` (section numbers below refer to it). Finance details are in
`finance-shares-valuation.md`.

Notation: integer maths, `/` truncates; `Rand(n)` = 0..n−1; "1-in-n" = a `Rand(n) == 0` roll.
A computer is identified only by its **slot** 0..3 (player index); several rules are hard-wired per
slot.

---

## 1. Difficulty

There is **no separate AI difficulty setting**. Computer behaviour depends only on the mission,
through a per-mission feature table (ai-robots.md §8). The free game uses column 6:

| On in the free game | Off in the free game |
|---|---|
| sabotage, including against humans (`SABOTAGE`, `MUCH_SABOTAGE`); cargo (`FRACHT`); ads (`WERBUNG`); late-day board visits (`AUFSICHT`); route box; abroad phone orders; travel agencies; security office; designer visits; broker; kerosene dealer; keep own stake ≤ 20 % (`MAX20PERCENT`); catch-up bonus ×8 (`BONUS_X8`) | NASA/rocket; route suggestion/forcing; money-based ad tiers; share buy-back features; "never give up a route" |

**Implementation in OpenAT**
- Implemented: feature matrix as data `Assets/OpenAT/Sim/AI/RobotFeatures.cs:126-185` (free game =
  level 6); bonus multiplier `Assets/OpenAT/Sim/Missions/MissionRules.cs:89-96`.

---

## 2. Decision loop

1. **Morning** (`RobotInit`): idle countdown `10 + 10·slot` ticks (staggered start); queue =
   office, personnel, then the agents (last-minute desk and travel agency, order depending on
   `(slot + day)` parity; cargo office on some days).
2. **Pump** (09:00–18:00): when idle, pop the next queued action and **walk** to its room (slot 2
   runs during even 5-minute windows). On arrival the action is executed instantly, then a work
   countdown keeps the figure in the room (100–200 ticks; slot 2 halves it).
3. **Planner** when the queue is empty — first match of a priority chain:

   | # | Action | Condition (free game) |
   |---|---|---|
   | 1 | buy used plane | cash > 5,000,000, image > −100, after 11:00, weekday, not yet in the museum today |
   | 3 | advertising | "wants routes", image < 150, 1-in-4 |
   | 4 | museum (bigger plane) | needs more range, cash > 100,000, 1-in-4 |
   | 5 | issue shares | day + 1 ≥ slot, ≥ 25,000 issuable, and (cash < 3 M or loan > 1 M) |
   | 6 | personnel | > 3 h since last visit |
   | 7 | board | > 3 h since last visit, or (quirk) always after 16:00 |
   | 8 | agents | > 1.5 h since last agent visit, not in route mode |
   | 9 | office | (quirk) practically always |
   | 10 | weighted random pick | 31-entry table: security 3; sabotage, office, bank, personnel, mechanic, cargo, ads, designer 2 each; the rest 1 |

   No action may repeat the previous one. After day 1, slot 0 turns a random pick into sabotage
   1-in-4 and slot 1 into a bank visit 1-in-3. Sabotage is not chosen before day `1 + slot`.
   Net effect of the two timer quirks: an office / something / office / … rhythm, and board /
   something / board after 16:00.
4. **Night**: computers in route mode rebuild their route schedule and prices (§4).

**Implementation in OpenAT**
- Implemented: pump `Assets/OpenAT/Sim/AI/AiSystems.cs:51`, morning init `AiSystems.cs:90`; planner
  `Assets/OpenAT/Sim/AI/RobotPlanner.cs:15` (priority chain `:84`, random pick `:116`, post-filters
  `:185`); executor `Assets/OpenAT/Sim/AI/RobotExecutor.cs:44`; replaceable brain
  `Assets/OpenAT/Sim/AI/RobotBrain.cs:12`. Timer bugs Q1/Q2 fixed by default (`AiOptions.FixOfficeTimer` /
  `FixLateBoardTimer`, off = original; `Assets/OpenAT/Sim/AI/RobotState.cs:65-102`).
- Deviation (intended): the original seeds single-player action rolls from the wall clock; OpenAT
  seeds them from world seed, date, time and slot (`RobotExecutor.cs:35`), so runs are replayable.

---

## 3. What they buy

| Item | Rule |
|---|---|
| Planes | **Never new.** Used planes at the museum: one offer with year > 1950, condition > 65, price < cash − 1,000,000; or, when its route network needs more range, an offer after 1960, condition > 40, price < cash + 1,000,000 with enough range (quirk: up to 3 in one visit). |
| Plane upkeep | Mechanic visit raises every plane's target condition +2 (slot 3 caps it at 70) and upgrades the maintenance level to the best one. |
| Kerosene | Free game: no tank purchases (only ATFS fuel missions); quality always normal. |
| Ads | Route ad when a leg's image is low (pays 56,000, gets +28 route image — humans get +1 for that tier). Company campaign when image < 0 (or < 300 while doing routes) and cash > 1.5 M, or when cash − loan > 4 M; tier from cash/image; tiers 0–2 give no image. |
| Security | Two random protection rolls per visit; switches on a protection only for kinds of sabotage that already hit it, if cash > 1 M; switches the rolled one off if cash ≤ 1 M. |
| Laptop, mobile phone | From day 8 if cash > 100,000 — **free**, and it blocks the human's laptop purchase that day. |
| Staff | None. Computers have no workers and pay no salaries. |

**Implementation in OpenAT**
- Implemented: `RobotExecutor.cs:345` (used plane), `:326` (bigger plane, fix
  `AiOptions.FixMuseumMultiBuy`), `:261` (mechanic), `:244` (kerosene), `:486` (ads),
  `:302` (duty free); security `Assets/OpenAT/Sim/AI/RobotExecutor.Rivals.cs:147`; route ad
  `Assets/OpenAT/Sim/AI/RobotShortcuts.cs:280`.

---

## 4. Orders, routes and prices

### 4.1 Order mode (default)

At the agents and by phone through branch offices, for every plane: take an order departing from
the plane's next free city (positioning cost ≤ 80 % of the premium), else the nearest-departure
order within distance limits. Seat capacity and the date window are **not** checked, and order
penalties are never charged to computers.

### 4.2 Switching to routes

At the office, a computer starts "wanting routes" when `day > slot + 30` with more than 6 planes, or
`day > slot + 15` and the airline in slot `(slot + 3) mod 4` has just started flying routes (route
counter exactly 1, or above 20). With image < 150 it then rents legs and runs route ads while still
flying orders; it switches fully to route mode when the human has connected more cities than its
slot number, or (image ≥ 150) once it owns more than 2 legs. It never switches back.

### 4.3 Renting routes and bidding

- **Route box:** only when no owned leg has had more than one low-use day and the fleet is large
  enough (`planes > legs/2·3/2`). Candidates: unowned legs from home, or sharing a city with a leg it
  uses at ≥ 20 %; 7-day cool-down after losing a leg. Picks the lowest rent per passenger, rents it
  and the reverse leg.
- **Board (auctions):** bids only on **branch offices**, never on gates (the trigger counter is never
  increased). A bid is placed when (another player leads and sympathy to them < 40, or slot 0, or
  1-in-10, or after day 10 1-in-5) or (nobody leads, 1-in-3), **and** yesterday's balance-sheet result
  > 10 × price, `2·loan < 3·cash`, cash > 0. Each bid adds 10 %.

### 4.4 Nightly route plan and ticket prices

The highest-demand owned leg gets flights from every plane in range until plans are full 5 days
ahead; ferry flights that match an owned leg become route flights. Prices per owned leg:

```
C        = referenceCost(leg)·3/180
economy  = 6·C rounded down to 10;     first class = 2 × economy
if other owners: price ≤ 2 × each owner's price;
                 price ≤ (owner's price rounded to 10) − 10  for owners it dislikes (sympathy < −40)
floors:  ≥ C/2;  ≥ C if cash < 100,000;  ≥ 2·C if cash < 0      (first class: 2 × floor)
```

Example: C = 1,000 → 6,000 / 12,000. A disliked human charging 5,000 on the same leg is undercut to
4,990 (never below 500). Quirk: on any leg shared with an owner that flew it recently, the "losing
money" test is always true, so after 11 nights the computer gives up that direction of the leg.

Computers also get **+25 %** passenger weight when route demand is split among owners.

**Implementation in OpenAT**
- Implemented: agents/orders `Assets/OpenAT/Sim/AI/RobotExecutor.Orders.cs:26-267`; route switch
  `RobotExecutor.cs:143-153`; route box `RobotExecutor.cs:421`; board `RobotExecutor.cs:380`; nightly
  plan `Assets/OpenAT/Sim/AI/RobotRoutePlanner.cs:22`, prices `:156-215` (fixes
  `FixCompetitorCount`, `FixRouteLossTest`); +25 % weight `Assets/OpenAT/Sim/Fleet/RoutePassengers.cs:113`;
  gate delayer `RobotShortcuts.cs:82`.

---

## 5. How they react to the human

### 5.1 Sympathy

Every computer keeps a sympathy value towards each player: 50 at the start, 60 towards the next
slot. It falls through:

- **Your profit** (each night, per pair, only while its sympathy towards you is < 25 and your
  balance-sheet result `bd > 0`; `bc` = its own result):
  - sympathy < −50 and `bd > bc`: −10 if `bd > bc + 1,000,000`, −2 if `bd > bc + 100,000`;
  - otherwise, if `bd > 2·bc`: −5 if `bd > bc + 1,000,000`, −1 if `bd > bc + 100,000`.
  These penalties apply only towards humans.
- **Your sabotage** against it (victim's sympathy to the saboteur −5 … −200 by severity, applied
  when the job fires, whether or not it is ever traced to you — see the sabotage reference), spiked chocolates −10; normal chocolates +10; dialogue choices.
- Then every value with |s| > 70 is multiplied by 69/70 each night (slow pull towards ±70).

Note the drift only starts once sympathy is already below 25, so a profitable human is not disliked
from profit alone; sabotage or rude dialogue pushes it there first.

A computer **dislikes** a player whose sympathy is negative; at every action one disliked player is
chosen at random.

### 5.2 Consequences of being disliked

| Reaction | Rule |
|---|---|
| Sabotage target | the disliked player; if none: 1-in-3 (slot 0: always) a random slot. Go/no-go needs its suspicion level < 80 (slot 1 rich exception < 90), image > −500, no pending job, and `(day + slot) mod 3 ≠ 0` or 1-in-4 (slot 0: extra 1-in-2). Job family by hour mod 3; severity = trust with the saboteur (+1 per job, max 4/5); slot 0 keeps jobs mild, slot 2 caps at 3. Jobs are **free** for computers. |
| Share raid | at the bank, if not saving and (cash > 3 M with loan < 1 M, or cash > 2 M with its own price < dividend/2): buys your free float, up to (cash − 3 M)/your price (− 2 M in the cheap case), rounded to 100; your price ×Anz/(Anz − n/2). No fee. (Computers never request a takeover.) |
| Price war | undercuts your ticket price by 10 on shared legs when sympathy < −40 (§4.4). |
| Branch auctions | outbids you when its sympathy to you < 40 (§4.3). |
| Phone calls | proposes cooperation when sympathy > 50 and you are not cooperating; cancels it when cooperating and sympathy < −20. |

### 5.3 Catch-up bonus (rubber band)

Each computer has a hidden bonus pool, 500,000 at the start of a free game. Each night, if the pool
is exactly 0, it is refilled by comparing the local human's rating H with the computer's own A
(free game: passengers carried):

```
add 100,000 for each of:  H > A,   H·2/3 > A,   H/2 > A          then × 8 (free game)
```

The pool is paid out at flight booking: each flight's result gets `+min(pool, |result|/10)`.

Example: H = 20,000 passengers, A = 8,000: all three hold → 300,000 × 8 = **2,400,000** added to
the pool, released at up to 10 % of every flight result. A computer ahead of you gets nothing.

**Implementation in OpenAT**
- Implemented: sympathy init `RobotBrain.cs:35`; drift `AiSystems.cs:119-148`; disliked rival
  `RobotExecutor.cs:107`; phone calls `RobotExecutor.cs:118`; sabotage decision
  `RobotExecutor.Rivals.cs:28-145`; share raid `RobotShortcuts.cs:202`; bonus refill
  `Assets/OpenAT/Sim/Finance/FinanceSystems.cs:100-116`; payout `Assets/OpenAT/Sim/Fleet/FlightOps.cs:132-137` (deduction = amount paid unless `FixHiddenBonusDeduction` is off);
  sympathy ledger wired for the sabotage domain `Assets/OpenAT/Sim/World.AI.cs:21`.

---

## 6. Money handling

| Topic | Rule |
|---|---|
| Dividend | slot 1: always 25. Others: 1-in-10 per bank visit to raise it by +1 (1/5) and +1 (1/10), clamped 5..25. Never lowered. |
| Loans | by direct edit, **no credit limit, no debt limit, no statement line**. Slot 1 keeps its loan at `1,000,000 + 50,000·day`. Others: repay `min(loan, cash − 1.5 M)` when cash > 1.5 M; borrow `1.4 M − cash` when cash < 1 M. |
| Own stake | sells own shares down to 20 % when its price ≥ 200 (proceeds capped at 20,000 at the bank). |
| Fire sale | loan > 3 M (slot 1: only when cash < −100,000): sells up to 20,000 shares per company at **its own** price until cash > 2 M; own shares only after day 10. |
| Share issue | the whole issuable remainder at ⌊K⌋ − 5; 80 % to the market; fee and dilution compensation as for humans; then rebuys half the issue at the issue price (not slot 3). |
| Costs never paid | sabotage jobs, laptop/phone, phone calls, salaries, order and freight penalties, cabin/technical upgrades installed at landing. |
| Rules skipped | pre-departure delay checks (strikes, missing crew, condition < 15), order validation, image loss from cabin crew, manual gate planning (automatic gate delayer). |

Slot personalities: **0** saboteur (frequent, mild sabotage; bids on every branch), **1** banker
(dividend 25, permanent loan, bank visits), **2** hustler (runs, works twice as fast, sabotage
capped at 3), **3** penny-pincher (planes kept at ≤ 70 % target condition, no rebuy after issues,
starts latest).

**Implementation in OpenAT**
- Implemented: bank visit `Assets/OpenAT/Sim/AI/RobotExecutor.Bank.cs:15-75`; direct-edit loans and
  share shortcuts `RobotShortcuts.cs:151-266` (issue `:224`).

---

## 7. Known deviations / open points in OpenAT

- None found in the computer-player logic against the spec in the areas checked (bank, issue,
  sympathy drift, bonus, pricing, sabotage decision). The original's AI bugs are **fixed by default**
  (`AiOptions` in `RobotState.cs:65-102`, all `Fix*` on; off restores the original): Q1–Q3, Q11
  (mechanic room time), Q13, Q15 (cargo free point), Q16 (order entries in the route survey), Q17
  (rescue flight hour wrap), Q18, Q19, Q22, Q24 (route wipe; an out-of-range top leg is then skipped
  instead of ending the nightly fill, `RobotRoutePlanner.cs`), branch bids reading the branch slip
  (`FixBranchBidTable`, `RobotExecutor.cs`) and the route box's rent per passenger from the route
  table (`FixRouteBoxRentTable`). Bonus: `GameOptions.FixComputerBonusAbs` and
  `FixHiddenBonusDeduction` (pool reduced by the amount paid). See `docs/QUIRKS.md`.
- Toilet runs (coffee prank) no longer strand a robot: a computer is taken out of its room and
  stunned 20 ticks as in the original, entering another room ends the run, and the run state is
  cleared at day change (`Sim/Items/ItemSystems.cs` SickStep, `Sim/Hall/HallPlayers.cs` EnterRoom,
  `Sim/Hall/HallState.cs` NewDay).
- Interaction gap on the human side: since the human share form is a placeholder
  (finance-shares-valuation.md §5), a human cannot buy back shares a computer raided, nor buy a
  computer's free float.

---

## Guide claims

| Id | Claim (paraphrased) | Verdict |
|---|---|---|
| T7 | Rivals you do not sabotage rarely sabotage you; sabotaging makes them dislike you and raises their chance to strike back | **Mostly confirmed.** Sabotage lowers the victim's sympathy (−5 … −200), and computers target disliked players first. But with nobody disliked a computer still picks a random victim 1-in-3 per sabotage visit (slot 0: always), so a peaceful human is not safe; profit alone only lowers sympathy once it is already below 25. Catch chances and fines: see the sabotage reference. |
| T5 | Cooperation with a rival lets you use their branches without paying rent | Partly relevant: cooperating computers take orders through each other's branch offices by phone (no call charges). The human-side effect is not covered here. |
| F2 | Bank clerks: man = shares, woman = loans | Confirmed (see finance-shares-valuation.md). Computers bypass both clerks and edit money, loans and shares directly. |
