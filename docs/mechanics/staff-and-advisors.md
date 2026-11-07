# Staff and advisors

Game-mechanics reference for the free game of Airline Tycoon Deluxe, written for OpenAT. Sources:
our behaviour specs (`docs/spec/findings/economy.md` §9, `sim-core.md` §3.6, §6.1, §6.10,
`planes-and-flights.md` §3.5, §8, §12.8, §13, `rooms-and-ui-6-airport-services.md` §1,
`rooms-and-ui-2-menus.md` (personnel menu), `rooms-and-ui-3b-dialog-trees.md` §4, §13b,
`rooms-and-ui-5b-planner-blocks.md` §4.5, `contracts-and-routes.md` §2.7, §3.9, §5.3,
`rooms-and-ui-7` museum, `rooms-and-ui-8` kerosene, `items-security-special.md` §1.7, §3.3,
`airport-and-people.md` §6.4). Every number was re-checked against the reference source; where the
source disagrees with a spec, the source wins and the spec is flagged in §13. Formulas are restated in
our own words. Integer arithmetic unless stated; ÷ truncates toward zero.

Units: money in game currency; salaries are **monthly** and paid as 1/30 per day; happiness and
talent are plain integers. Text ids are cited as `group id` (`Advi` = advisor lines, `Jobs` =
personnel dialogue); the texts themselves are read from the install at run time.

---

## 1. Job types

Every person in the staff pool has one type:

| Type | Role | Notes |
|---|---|---|
| 0 | Secretary ("assistant") | UI figure only, never in the pool |
| 1 | Personnel advisor | staffing remarks, applicant ratings |
| 2 | Kerosene advisor | fuel price and quality remarks |
| 3 | Route advisor | comments in the route office |
| 4 | Order (contract) advisor | rates passenger and freight orders |
| 5 | Finance advisor | balance sheet, loan-rate perk |
| 6 | Informer | rival information |
| 7 | Plane analyst | used-plane ratings, wear warnings |
| 8 | Fitness trainer | faster player figure |
| 9 | Security advisor (bodyguard / negotiator) | purchase rebate |
| 10 | Pilot | cockpit crew |
| 11 | Flight attendant | cabin crew |

There are **no mechanics** as staff: maintenance is a player-wide workshop contract (four modes,
see the planes reference), not a worker type.

### Implementation in OpenAT
* Implemented: `AdvisorType` and `WorkerType` `Assets/OpenAT/Sim/Staff/Worker.cs:4,19`, worker record
  `Assets/OpenAT/Sim/Staff/Worker.cs:34`.

---

## 2. The staff pool

### 2.1 Worker record
Name, sex (0 female, 1 male), type, current salary, **original salary**, talent ("quality"), age,
free-text comment, employer, assigned plane, happiness, "warned today" flag.

Employer codes: 0..3 = airline, **Reserve** (100, hidden pool), **Jobless** (101, today's applicant).

### 2.2 Where people come from
* At game start the pool is the shipped worker table (one row per person, everyone in Reserve,
  happiness 100, original salary = salary). It holds a few advisors of each type (3–7 each) and a few
  hundred crew. Facts about the shipped table that matter for the thresholds below:
  * best finance advisor talent 90, best security advisor 99, best fitness trainer 90, best
    personnel advisor 95; no shipped advisor reaches talent 100;
  * shipped **pilots** follow `salary ≈ 3000 + 100·talent` almost exactly (talent 21..99); shipped
    attendants are much noisier (salary only loosely tied to talent);
  * more than 100 shipped pilots and about 60 attendants have talent > 60.
* **Shortage top-up** (start of every night, before the market reshuffle). Count unemployed
  (Reserve or Jobless) pilots with talent > 60. If fewer than 40, append 10 generated pilots. Same
  test, separately, for attendants. Generated people:

  | | Pilot | Attendant |
  |---|---|---|
  | salary S | `(30 + R(70))·100` → 3 000 … 9 900 | `(30 + R(50))·100` → 3 000 … 7 900 |
  | talent | `S/200 + R(30) + 20` → 35 … 98 | `S/160 + R(30) + 20` → 38 … 98 |
  | age | `19 + R(50)` → 19 … 68 | `19 + R(40)` → 19 … 58 |
  | sex | male when `R(100) > 20` (79 %) | male when an *unseeded* draw `rand()%100 > 80` (19 %) |

  `R(n)` = seeded draw 0..n−1, seed `Date + StartTime`. Names are random first + last name from the
  name table. The pool only ever grows. Given the shipped table, the top-up only fires after the
  players have hired most of the good crew.

### 2.3 Daily labour market (applicants)
Every night, after the airlines' own night processing:
1. Shortage top-up (above).
2. Daily mood loss of human airlines' staff (§5.2).
3. Every Jobless person goes back to Reserve (yesterday's applicants vanish, even un-hired ones).
4. Pick `10 + R(10)` applicants (10..19) of **any** type: up to 100 attempts, each takes a random
   index and walks forward up to 99 places to the first Reserve person; that person becomes Jobless
   and their salary is reset to the original.
5. Then `5 + R(2)` extra **attendants** (5..6) the same way.
6. At game start only: if no Jobless personnel advisor, order advisor or plane analyst exists, one
   random Reserve person of that type is forced into the applicants (up to 1000 draws each).

Same seed `Date + StartTime` throughout, so a given game always offers the same applicants on the
same day. Expected offer: about 15 applicants of mixed type plus 5–6 attendants. Because the pool is
mostly crew, a day usually offers 0–2 advisors.

**Starting crew.** Each human airline is given exactly the pilots and attendants its starting planes
require, drawn at random from Reserve (seed `StartTime + player`). No advisors at the start.

### Implementation in OpenAT
* Implemented: pool init `Assets/OpenAT/Sim/Staff/WorkerPool.cs:36`, night market
  `Assets/OpenAT/Sim/Staff/WorkerPool.cs:60-121`, top-up `:133-161`, forced advisors `:194` called
  from `Assets/OpenAT/Sim/Finance/FinanceSetup.cs:28-30`, starting crew
  `Assets/OpenAT/Sim/Finance/FinanceSetup.cs:105`, nightly hook
  `Assets/OpenAT/Sim/Finance/FinanceSystems.cs:272`.
* Deviation (deliberate): the unseeded `rand()` used for names and attendant sex is replaced by a
  seeded stream (`NameRandom`, seed + 7919) for determinism (`WorkerPool.cs:30,136,154`).

---

## 3. Personnel office

Each airline has its own personnel office (always open, also reachable by phone). Two clerks:

* **Female clerk** — the job market. Options open the hiring folder for advisors, pilots or
  attendants (applicants only). By phone she only reports how many advisors / pilots / attendants are
  applying today.
* **Male clerk** — the existing staff. Options: raise all salaries, cut all salaries, open the
  own-staff folder for advisors / pilots / attendants (manage, fire, reassign). The folders are
  refused by phone. During a strike he opens with the strike negotiation (§6.4). He also phones the
  player when a strike ends (dialogue 1000 + end type).
* A pills puzzle hotspot sits on the desk (item chain, see the items reference).

The folder shows one person per page (name, age, type, talent shown as a rating, salary, comment)
with a cover page listing the count and the average salary. In the hiring folder each page flip
triggers the personnel advisor's comment (§9.1).

**Hire:** employer = player, no plane; crew auto-assignment runs (§7.2) with advice on; walk speed is
recomputed. **No hiring fee, no limit on staff.**
**Fire:** the person becomes Jobless again (re-hirable the same day). **No severance pay**, no
notice period, no effect on the others' mood.
**Reassign:** pick another plane for a pilot/attendant; surplus crew on the target is freed (secretary
remark `Advi 1260`).

### Implementation in OpenAT
* Implemented: dialogues `Assets/OpenAT/View/Rooms/Personal/PersonalDialogs.cs:56-85`, folder
  `Assets/OpenAT/View/Rooms/Personal/PersonalMenu.cs`, commands
  `Assets/OpenAT/Sim/Staff/StaffCommands.cs:7` (hire), `:27` (fire), `:81` (reassign) with
  `Assets/OpenAT/Sim/Player.Staff.cs:231` (overflow freeing).
* Implemented: the personnel advisor's remark on entering the office (§9.1, tolerance 3):
  `Player.PersonnelAdvisorRemark` (`Assets/OpenAT/Sim/Player.Staff.cs`), shown by
  `Assets/OpenAT/View/Rooms/Personal/PersonalRoom.cs` `OnRoomEnter`.

---

## 4. Salaries

### 4.1 Payment
Each night, **human airlines only**:

  `cash −= (Σ salaries of all employees) / 30`   (booking reason 2070, "staff" in the balance sheet)

and each plane's daily balance is charged `salary/30` for every crew member assigned to it. Computer
airlines pay **nothing** (§10).

Worked example: a plane needing 2 pilots and 3 attendants, staffed with average shipped crew
(pilots ≈ 9 400, attendants ≈ 6 000 per month) costs `2·9400 + 3·6000 = 36 800` per month, i.e.
**1 226 per day**.

### 4.2 Raises and cuts (per person, or for everyone via the male clerk)
* Raise: `S += (S/100)·10` (≈ +10 %, truncated to a multiple of 10 per 100), capped at 1 000 000. If
  the salary actually changed: happiness **+20** (not clamped to 100).
* Cut: `S −= (S/100)·10`, happiness **−25**. If happiness < −100 the person quits at once: back to
  Reserve, salary and happiness reset (original salary, 100), crew re-mapped.
* There is no check of the airline's profit or anything else; a cut is always accepted.
* "Raise all" additionally cancels a planned (not yet started) strike and then pumps morale: while
  `mood < 20` (§6.1, measured on the local player), add +10 to everyone (clamped −100..100). With a
  single employee stuck at −100 and everyone else at 100 this loop can fail to terminate.
* Any applicant who is picked from Reserve again gets the original salary back, so raises do not
  carry over after someone leaves.

Example: S = 9 950 → raise → 9 950 + 990 = 10 940; cut from 9 950 → 8 960.

### Implementation in OpenAT
* Implemented: payment `Assets/OpenAT/Sim/Player.Staff.cs:272` (hook
  `Assets/OpenAT/Sim/Finance/FinanceSystems.cs:166`), raise/cut `Assets/OpenAT/Sim/Player.Staff.cs:284`,
  all `:309`, morale pump `:320` (bounded to 1000 rounds — deliberate fix), commands
  `Assets/OpenAT/Sim/Staff/StaffCommands.cs:47,64`.
* Implemented: the per-plane salary charge — `Player.BookSalaries` charges each plane's
  `Balances[0]` with `Player.CrewSalaryPerDay` (Σ salary/30 of its assigned crew,
  `Assets/OpenAT/Sim/Player.Staff.cs`), before the nightly balance shift in `Fleet/Maintenance.cs`.

---

## 5. Happiness (mood)

### 5.1 Range and start
Nominally −100..+100, start 100 for every pool member. Only some changes clamp (see below).

### 5.2 What lowers it
* **Daily loss** (human airlines only), with company image `I`:

  `steps = 0 if I ≥ 500; else 1, +1 if I < 0, +1 if I < −500`

  Each step: happiness −1, and an additional −20 if the person's current salary is below the
  original salary. Not clamped. If it falls below −100 the person **quits**: back to Reserve (salary
  and happiness are *not* reset here), secretary message `Advi 2000 + type + 100·sex`, crew re-mapped.
* Salary cut −25 (§4.2); threatening strikers −20 to all; waiting a strike out −10 to all (§6).

### 5.3 What raises it
Only salary raises (+20 each) and the "raise all" pump. Profit, image, hiring, equipment or time off
never raise it. With image ≥ 500 it simply stops falling.

### 5.4 Worked examples
* New game, image 0 (< 500): everyone loses 1 per day. From 100 the strike threshold `mood < 20` is
  reached after 81 days without raises.
* Image −100 (< 0): 2 per day → about 40 days.
* Cutting a person's pay: 100 → 75, then each day −21 (image 0..499): quits on the 9th night
  (75 − 9·21 = −114).
* Quirk: someone who quit through the daily loss keeps a happiness below −100. If the market later
  offers them again and you hire them, they quit again on the first night with image < 500.

### Implementation in OpenAT
* Implemented: `Assets/OpenAT/Sim/Staff/WorkerPool.cs:65-88` (daily loss, quit without reset kept),
  mood helpers `:214-250`.

---

## 6. Strikes

### 6.1 Planning (10:00, local human only)
`mood = average happiness − (10 if anyone's happiness < 0)`. At the 10:00 hour change, if no strike
is running or planned:

  plan a strike if `(mood < 20 and DaysWithoutStrike > 7)` or `(mood < 0 and DaysWithoutStrike > 3)`.

`DaysWithoutStrike` starts at 20 and counts up each night. The "provoke a strike" sabotage also plans
one, for any airline.

### 6.2 Start
At any hour change from 07:00 to 17:00 (not after calling it a day), a planned strike starts as soon
as one of the airline's planes stands at a city or lands this hour or within the next 2 hours.

  duration = 2 h if DaysWithoutStrike > 10;
           else 72 h if average happiness (local player's staff) < −10;
           else 4 + R(20) h (4..23, seed Date + hour).

Then `DaysWithoutStrike = 0`, and a strike headline appears in the paper (priority 25, +10 for the
local player). The human player gets a fax popup plus an in-tray letter (`Lett 508`) with the
hours, at the first moment between 09:10 and 18:00 when no dialogue or menu is open.

So the **first** strike of a game (DaysWithoutStrike still ≥ 11) always lasts only 2 hours; repeated
strikes within 10 days last 4–23 h, or 72 h with very unhappy staff.

### 6.3 Effect
While `StrikeHours > 0`, every flight of the airline that is due to start next hour is pushed back
by one hour at each hourly check (secretary remark `Advi 2356` per flight). Strike hours count down
every hour, day and night. Contracts can therefore miss their dates. **Computer airlines' flights
are never delayed** (the delay check skips them), so a provoked strike against a computer airline only
produces the headline.

### 6.4 Ending it
* **Wait:** when the hours run out — all staff −10 happiness; the personnel clerk phones (end type 3).
* **Raise all salaries** (male clerk during a strike): +10 % and +20 for everyone, the morale pump,
  strike ends after 2 game minutes (end type 2).
* **Threaten**: everyone −20, strike ends after 4 game minutes (end type 1).
* **The drinker at Rick's** (needs the horseshoe trust, §6.5): ends it after a short countdown
  (10 game minutes, cut to 4 by his follow-up line), free, no mood change (end type 3).

### 6.5 Horseshoe
The horseshoe (bra from the broker's hatch → duty-free saleswoman → horseshoe) is given to the
drinker at Rick's café. That sets a permanent "drinker trust" flag. It does **not** prevent strikes:
it lets the player end every later strike for free and without the −10/−20 mood penalties by
talking to the drinker.

### Implementation in OpenAT
* Implemented: state `Assets/OpenAT/Sim/Player.Staff.cs:36-49`, planning/start/hourly countdown
  `Assets/OpenAT/Sim/Finance/FinanceSystems.cs:318`, minute countdown `:294`, DaysWithoutStrike
  `:79`, delay check `Assets/OpenAT/Sim/Fleet/FlightOps.cs:356`, provoked strike
  `Assets/OpenAT/Sim/Items/SabotageEngine.cs:364`, negotiation
  `Assets/OpenAT/Sim/Staff/StaffCommands.cs:113` + `Assets/OpenAT/View/Rooms/Personal/PersonalDialogs.cs:63,81-84`,
  drinker `Assets/OpenAT/Sim/Items/ItemCommands.cs:260`, `Assets/OpenAT/Sim/Items/RoomItemCommands.cs:10`,
  `Assets/OpenAT/View/Rooms/Ricks/RicksRoom.cs:185-203`, end call
  `Assets/OpenAT/View/Session/StrikeEndCall.cs`, `Assets/OpenAT/Sim/Staff/StrikeNotice.cs:10`.
* Implemented: the strike-start notice — `StrikeStartNotifiedCommand`
  (`Assets/OpenAT/Sim/Staff/StrikeNotice.cs`, due 09:10–18:00 once per strike) marks it told and sends
  `Lett 508`; `Assets/OpenAT/View/Rooms/Items/SabotageNotifications.cs` runs it at the first quiet moment
  and shows the strike fax (regardless of the fax option, as the original). Not reproduced: the
  original also stops the walking figure at that moment.
* Implemented: `StrikeEndOption.BribeDrunk` (`Assets/OpenAT/Sim/Staff/StaffCommands.cs`) checks the
  drinker trust and is the drinker's start (10 minutes, end type 3; it takes over a running negotiated
  countdown, as the original); `DrinkerEndsStrikeCommand` shares its test.

---

## 7. Crew requirements and assignment

### 7.1 Requirements
Each plane type defines a required pilot count and attendant count (from the plane-type table;
designer planes compute their own). Illustrative: small regional types need 2 pilots + 1 attendant,
large wide-bodies 3 pilots + 5–6 attendants. The player can also set a **desired** attendant count
(`MaxAttendants`) between the required count and twice it, in the plane equipment room ("luxury"
cabin crew).

### 7.2 Automatic assignment (after every hire, fire, quit, plane purchase or sale)
1. Count crew per plane from their assignment; unassigned or orphaned crew counts as surplus.
2. Planes with more attendants than desired release the extras.
3. Each unassigned pilot/attendant goes to the plane with the **smallest remaining shortfall** of its
   kind (among planes still short of the required count).
4. An attendant who finds no short plane is put into luxury slots — quirk: the code does not stop
   after the first match, so the same person is **counted on every plane** that has a free luxury
   slot (and ends up assigned to the last one). Counts stay inflated until the next re-mapping.
5. A second pass meant to move luxury attendants to planes lacking base crew **never runs** (its
   "movable" count compares a value with itself and is always 0).
6. Surplus/shortage: `xPilots = unassigned − Σ missing pilots`; `xAttendants = unassigned −
   Σ (desired − assigned)` (attendant shortage is measured against the *desired* count).
7. Crew quality per plane = average talent of its assigned crew; 60 if none.

### 7.3 Understaffed planes
A plane with fewer pilots or attendants than *required* does not depart: each hourly check pushes
the flight back one hour (secretary remark `Advi 2350`), until the crew is complete. Luxury
attendants above the requirement are optional.

### Implementation in OpenAT
* Implemented: `Assets/OpenAT/Sim/Player.Staff.cs` (`MapWorkers`, `MapFreeCrew`). Both bugs (§7.2
  steps 4 and 5) are fixed by default: a spare attendant takes one luxury slot, and the second pass
  moves luxury attendants to planes short of base crew. `GameOptions.FixCrewMapping = false` restores
  the original (docs/QUIRKS.md). Wired via `Assets/OpenAT/Sim/Fleet/FinanceIntegration.cs:63`; delay check
  `Assets/OpenAT/Sim/Fleet/FlightOps.cs:357,376`.

---

## 8. Effects of crew quality

Let `Q` = plane crew quality (average talent), `A` = assigned attendants, `A₀` = required.

| Where | Rule (human airlines) |
|---|---|
| Company image per route/order flight | if `Q < 50` and the flight's object id is a multiple of 10: −1; if `Q > 90` and same: +1 |
| Image score of the flight | `+3·A/A₀` (integer: 3 when exactly staffed, up to 6 with double staffing), added to the score that is then divided by 10 |
| Passenger satisfaction (per group of 15 passengers, after the condition and cabin rules) | unhappy if `R(65) > Q` → chance `(64 − Q)/65` for Q < 64, **zero for Q ≥ 64** |
| Cabin quality sum | includes `A − A₀` (each luxury attendant +1) |
| Morning briefing | the boss comments on the fleet average Q: < 40, < 60, < 70, < 80 (one of three remarks), ≥ 80 |

So crew talent matters below ~65; above that, only the rare ±1 image rule at > 90 is left.

### Implementation in OpenAT
* Implemented: `Assets/OpenAT/Sim/Fleet/FlightOps.cs:248-249,261,317`, boss comment
  `Assets/OpenAT/Sim/Missions/MorningReviewSystem.cs:83`,
  `Assets/OpenAT/View/Rooms/Aufsicht/BossDialog.cs:182-187`.
* Note: satisfaction and image read the assigned counts from
  `MapWorkers`, which reproduce the inflation — consistent with the original.

---

## 9. Advisors

**Quality** of an advisor kind = the highest talent among the airline's employed advisors of that
kind; 0 = none (a talent-0 advisor would count as absent). Hiring several of one kind only helps
for the fitness trainer (summed). Most advisors are **on/off**: any talent gives the full effect.
Only four kinds read their talent:

| Advisor | Talent used for |
|---|---|
| Personnel | chance that an applicant rating is real (§9.1) |
| Finance | > 75 loan perk; ≥ 80 full balance sheet (§9.5) |
| Fitness | walking speed (§9.8) |
| Security | > 20 rebate, rate = talent/10 % (§9.9) |
| Kerosene, route, order, informer, plane analyst | nothing (binary) |

### 9.1 Personnel advisor
* **Staffing remarks.** After every hire/fire (crew re-mapping with advice): when the pilot surplus
  changed, a remark for "x spare pilots" / "exactly enough" / "one missing" / "n missing"
  (`Advi 1200..1203`); otherwise the same for attendants (`1250..1253`). On entering the personnel
  office (tolerance 3) and after buying or selling planes (tolerance 1): spare pilots > tolerance
  (`1100`), else spare attendants > tolerance (`1101`), else both short (`1104`), pilots short
  (`1102`), attendants short (`1103`).
* **Applicant rating** (each page flip in the hiring folder), first match:
  1. The person has left the market meanwhile → "gone" (`1004 + sex`).
  2. The applicant is an advisor of a kind you already employ: a personnel advisor → "you have one"
     (`1272`); talent ≤ your best of that kind → "not better" (`1270`); else "better" (`1271`).
     No talent gate here.
  3. **Talent gate:** if `(poolIndex · 7777) mod 100 > personnel advisor talent` → non-committal
     "can't judge" (`1000`). The roll is fixed per person, so an advisor of talent T judges about
     T % of all people and always the same ones.
  4. Talent ≤ 25 → bad (`1006`); ≤ 45 → bad (`1007`).
  5. Value ratio `n` = over **all** pool members of the same type with |talent difference| < 15
     (including the applicant): +1 for each who earns more, −1 otherwise.
     `n < −5` → overpaid (`1001`); `n > 5` → good value (`1003`), or excellent if talent ≥ 90
     (`1008`); else fair (`1002`).

  Example: a talent-60 pilot asking 7 000 when most talent-46..74 pilots earn 7 600..10 400 →
  n strongly positive → "good value". A talent-95 pilot asking 13 000 among peers at 11 000..12 000 →
  n negative → "overpaid".
* **Random warnings** (see §9.10): one unhappy employee (happiness < −40, not yet warned today) is
  named (`2200..2212` by kind and sex); if more than 10 are unhappy, a single group warning (`2299`)
  and all of them are marked warned.

### 9.2 Kerosene advisor
* On entering the fuel dealer: world price < 400 **and** `tank contents·2 < capacity·4` **and**
  cash > 20 000 → "buy now" (`3000`); else price > 600 → "don't buy" (`3001`). With no storage tank
  (capacity 0) the first test is false, so tankless players never hear "buy now".
* On choosing a fuel grade: a remark per grade (`3020 + grade`) — the only way fuel quality is
  hinted.
* When a departing flight empties the tank: "tank is empty" with the plane name (`3030`).
* Random chatter: price > 600 with tank closed and fuel in it → "open the tank" (`3011`); price < 400,
  tank more than half full, cash > 20 000 and capacity < 4 000·planes → "buy more tanks" (`3012`);
  price < 470 and the price history fell for several days, bottomed out yesterday and rose today,
  tank under a third full, cash > 20 000 → "buy fuel" (`3010`).

### 9.3 Route advisor
Once per newly hovered route in the route office:
* Route not held by you and lost/given up less than 7 days ago → "blocked" (`8020`) only.
* Otherwise a size remark: both cities over 2 000 000 inhabitants (`8000`), one (`8001`, naming the
  big one), neither (`8002`); plus, appended, a demand remark: `factor > level + 0.5` → popular
  (`8010`), `factor < level − 0.3` → unpopular (`8011`). `level` is the route's 1 (regional) / 2
  (long-haul) class, `factor` its popularity value.

### 9.4 Order (contract) advisor
On hovering an order card (travel agency, last minute, branch list), first match:
1. A future automatic ferry flight of yours (starting after next hour) matches the route and date
   window with enough seats → "this can replace a ferry flight" (`7013`).
2. No plane has the range (and < 24 h flight) → `7010`. No plane has the seats → `7011`. No single
   plane has both → `7012`.
3. Rating with `cost = ⌈fuel-cost estimate(from, to, 8000, 700) / 100⌉ · 100` against premium P:
   no penalty → neutral (`7000`); `cost ≤ 0.5P` → great (`7001`); `≤ 0.9P` → good (`7002`);
   `≤ 1.4P` → so-so (`7003`); else bad (`7004`).

Freight cards: ferry match (`7113`, no seat test), range (`7110`), then free cargo (P = 0) → `7105`,
else the same rating with `cost · max(1, tons/22)` (`7100..7104`).

Example: cost estimate 41 230 → 41 300; premium 60 000 → 41 300 ≤ 54 000 → "good".

### 9.5 Finance advisor
* **Notebook / laptop expert pages:**
  * Daily balance: no finance advisor → "you need an advisor" text; talent < 80 → a vague remark
    without figures; talent ≥ 80 → yesterday's full balance in three pages (income, expenses, total,
    in thousands).
  * Plane week balances: need any finance advisor (otherwise the "need an advisor" text, one page).
* **Loan perk** (on entering the bank): talent > 75 and the loan rate still equals the base rate of
  the Normal (6 %), Hard (10 %) or Final (15 %) mission → rate becomes `(loan rate + deposit rate)/2`
  (`5000`). The test checks the mission id, so in the **free game the perk never applies**.

### 9.6 Informer
Binary. Gives:
* rival toggles on the globe/laptop map and rivals' routes in the route office, other holders' shares
  on the route tooltip, competitor ticket prices (copyable) in the planner route block, the
  three-page competitor dossier in the notebook;
* messages: a rival bought a used plane (`9000`), outbid you on a gate (`9001`) or branch (`9002`),
  issued shares (`9004`), is buying your shares (`9005`), gave up a route (`9006`).

### 9.7 Plane analyst
* Museum: hovering a used plane → built before 1960 (`4001`); else condition < 20 (`4000`), < 40
  (`4002`), < 60 (`4003`), < 80 (`4004`), otherwise (`4005`). The **price is not considered**.
* Random chatter: with workshop mode 0–2, list the planes below condition 30 (`4010/4011`); with
  mode 3, the planes below 55 (`4020/4021`); a hint about the workshop mode is appended for modes
  0 and 1 (`4030/4031`).

### 9.8 Fitness trainer
`walk speed W = 3 + Σ over trainers ⌊(talent − 30)/30⌋` (truncating toward zero), clamped 1..5.
So a trainer adds 0 below talent 60, +1 at 60–89, +2 at 90–100. The player figure moves `2W` px
horizontally and `W` px vertically per step; running (double-click) does two such steps per tick. So
the trainer speeds up **both** walking and running, by up to 5/3 (≈ +67 %). Examples: one trainer
talent 75 → W = 4; one at 90 (best shipped) → 5; two at 60+ → 5.

### 9.9 Security advisor (bodyguard / negotiator)
After most purchases — new and used planes, designer planes, kerosene, storage tanks, ads,
duty-free items, chocolates, laptop, sabotage jobs — if talent q > 20:

  `rebate = (price / 100) · (q / 10)` credited (reason 3130), with a message (`6000`, or `6001` in the
  duty-free shop).

Rate = ⌊q/10⌋ %: 2 % at q 21–29 … 9 % at 90–99, 10 % only at 100. The best shipped advisor has 99, so
**9 % is the practical maximum** (10 % only with the advisor cheat). Example: plane at 30 000 000,
q = 85 → 300 000 · 8 = 2 400 000 back.

### 9.10 When advisors speak
* Messages go into a 15-entry queue per player; they are only shown 09:00–18:00 and only with the
  advisor option on. "Comment" messages are dropped when the mouse leaves the hovered thing.
* **Random chatter:** at certain clock values during the working day (main-loop timing, irregular),
  one of three kinds is picked at random — personnel, kerosene or plane analyst — and speaks only if
  hired and its condition holds (§9.1, 9.2, 9.7). The other kinds never chat.
* **Rick's café:** if an advisor kind you do not employ is among today's applicants, Rick may tip you
  off (1 in 5 per talk, kinds checked in the order informer, fitness, security, order, finance,
  personnel, route; `Rick 2107..2113`).

### Implementation in OpenAT
* Implemented: quality/has `Assets/OpenAT/Sim/Player.Staff.cs:79,86`; personnel staffing remarks
  `:197-214`; applicant rating `Assets/OpenAT/Sim/Player.Staff.cs:332` +
  `Assets/OpenAT/View/Rooms/Personal/PersonalMenu.cs:68-80`; kerosene entry/grade/empty
  `Assets/OpenAT/View/Rooms/ArabAir/ArabAirRoom.cs:50,236`, `Assets/OpenAT/Sim/Player.Finance.Fuel.cs:92`;
  order advisor `Assets/OpenAT/Sim/Contracts/ContractCommands.cs:198` +
  `Assets/OpenAT/View/Rooms/Counters/OrderCounterRoom.cs:94`; finance pages
  `Assets/OpenAT/View/Rooms/Planner/PlannerPanes.cs:702,734-735`; loan perk
  `Assets/OpenAT/Sim/Player.Finance.Bank.cs:50` (optional fix for the free game:
  `GameOptions.FixFinanceAdvisorPerk`, `Assets/OpenAT/Sim/GameOptions.cs:38`); informer UI gates
  `Assets/OpenAT/View/Rooms/Planner/PlannerPanes.cs:627,704,771`,
  `Assets/OpenAT/View/Rooms/Planner/LaptopRoom.cs:96,478`,
  `Assets/OpenAT/View/Rooms/RouteBox/RouteBoxRoom.cs:118`; museum ratings
  `Assets/OpenAT/View/Rooms/Museum/MuseumRoom.cs:115-126`; security rebate
  `Assets/OpenAT/Sim/Player.Finance.Upkeep.cs:41`; Rick's hints
  `Assets/OpenAT/View/Rooms/Ricks/RicksRoom.cs:125,161`.
* Implemented — fitness trainer: `Player.UpdateWalkSpeed` (`Assets/OpenAT/Sim/Player.Staff.cs`) sets
  `HallPlayer.WalkSpeed` after every crew re-mapping (hire, fire, quit) and every night
  (`WalkSpeedSystem`, `Assets/OpenAT/Sim/Staff/StaffModule.cs`); the step code uses it for walking and
  running. Computer airlines stay at 3.
* Implemented — route advisor: `RouteOps.Advice` (`Assets/OpenAT/Sim/Routes/RouteOps.cs`), shown by
  `RouteBoxRoom.cs`: "blocked" only within 7 days of losing the route, size remarks 8000–8002,
  "popular"/"unpopular" from the factor against the route level.
* Implemented — freight rating (`ContractAdvisor.RateFreight`): ferry (7113) and range (7110) checks.
* Implemented — kerosene entry remark with the tank test: `Player.KeroseneEntryRemark`
  (`Assets/OpenAT/Sim/Player.Finance.Fuel.cs`), shown by `ArabAirRoom.cs`. The fuel grade switches when
  it is picked in the dialog (`ChooseKeroseneCommand`, `Assets/OpenAT/Sim/Finance/FinanceCommands.cs`).
* **Deviation — plane week balance** (`PlannerPanes.cs:703,759`): shown without a finance advisor.
* Implemented — personnel remark on entering the office and after buying or selling planes
  (`1100..1104`, `StaffModule.AfterCommand`); random chatter (`2200..2212`, `2299`, `3010..3012`,
  `4010..4031`: `AdvisorChatter` / `AdvisorChatterSystem` in `Assets/OpenAT/Sim/Staff/StaffModule.cs`,
  shown by `View/UI/AdvisorLayer.cs`); informer messages `9000..9006` (computer airlines only:
  museum purchase, gate/branch outbid via `RivalMove` from `AuctionBoard.Bid`, share issue and share
  buying from `RobotShortcuts`, route given up).
* Deviation (deliberate) — chatter timing: the original tests `(time + frame steps left) mod 1024`
  in its main loop, which depends on frame rate and game speed (at normal speed it practically never
  fires). OpenAT checks every 512 ticks (15 360 time units ≈ 15 game minutes) on the game clock, the
  cadence the original reaches when sped up; the advisor kind is drawn with a seeded generator
  (`Date + Time`) instead of `rand()`.

---

## 10. Computer airlines

Computer airlines have **no worker records**. Their "employees" are exactly the required crew of
their planes, crew quality is fixed at 60, they pay no salaries, have no happiness, never strike on
their own (a provoked strike causes no delays, §6.3), have no advisors (so no rebate, no fitness).
Their staff statistics are synthetic: satisfaction `employees·(min(500, image) + 2500)/3000`, salary
total from a slice of the pool's original salaries × 1.25 — display only.

### Implementation in OpenAT
* Implemented: virtual counts `Assets/OpenAT/Sim/Player.Staff.cs:27-29,64`, salaries humans-only
  `:274`, delay check skips computers `Assets/OpenAT/Sim/Fleet/FlightOps.cs:350`.

---

## 11. Takeover

When an airline is taken over, all its staff are released to Reserve.

### Implementation in OpenAT
* Implemented: `Assets/OpenAT/Sim/World.Finance.cs:247`.

---

## 12. Quick formulas

| Quantity | Formula |
|---|---|
| Applicants per day | `10 + R(10)` any type + `5 + R(2)` attendants |
| Generated pilot | S = `(30+R(70))·100`, talent = `S/200 + R(30) + 20` |
| Generated attendant | S = `(30+R(50))·100`, talent = `S/160 + R(30) + 20` |
| Daily salary cost | `Σ S / 30` (humans) |
| Raise / cut | `±(S/100)·10`; happiness +20 / −25 |
| Daily mood loss | 0 / 1 / 2 / 3 steps at image ≥500 / <500 / <0 / <−500; −20 extra per step if underpaid |
| Strike plan | mood < 20 & >7 days, or mood < 0 & >3 days |
| Strike length | 2 h (>10 days since last) / 72 h (avg < −10) / 4..23 h |
| Bad-crew passengers | `(64 − Q)/65` per group, Q < 64 |
| Walk speed | `3 + Σ⌊(t−30)/30⌋`, 1..5 |
| Rebate | `(price/100)·⌊q/10⌋`, q > 20 |
| Order rating | cost vs premium at 0.5 / 0.9 / 1.4 |

---

## 13. Spec corrections found while checking the source

* `economy.md` §9.4 / `planes-and-flights.md` §8: the luxury-to-base second pass never runs, and a
  spare attendant is counted on every plane with a free luxury slot (OpenAT already models both).
* `economy.md` §9.2: generated pilots are 79 % male (draw > 20 of 0..99); the attendant sex draw is
  unseeded.
* `economy.md` §9.7: the strike-ending drinker sits at Rick's café and needs the horseshoe trust;
  his countdown is 10 minutes, shortened to 4 by his follow-up line.
* `rooms-and-ui-8` kerosene entry: the tank test is false for players without a tank.
* `planes-and-flights.md` §13: satisfaction groups are 15 passengers (the "customers per person"
  constant), not 5.

---

## Guide claims

| ID | Claim (paraphrase) | Verdict for Deluxe | OpenAT status |
|---|---|---|---|
| F13 | Personnel office: one clerk for applicants, one for existing staff | **True.** Female clerk = job market; male clerk = salaries, own staff, firing, strikes | Implemented (`PersonalDialogs.cs`) |
| A1 | Personnel advisor rates applicants by skill vs salary; a cheap average one can rate above a pricey gifted one | **Mostly true.** Rating compares pay with peers of similar talent (±14); talent ≤ 45 is always "bad"; only about talent-% of people get a real rating, the rest "can't judge" | Implemented (`Player.Staff.cs`, `Staff/StaffModule.cs`) |
| A2 | Financial advisor: daily balance sheet, per-plane earnings; unskilled → empty sheet | **True with thresholds.** None → "need advisor"; < 80 → text without figures; ≥ 80 → full sheet. Plane balances need any finance advisor. Loan perk (> 75) never fires in the free game | Balance gating implemented; plane-balance gate missing |
| A3 | Kerosene advisor helps buying and warns when fuel runs out | **True.** Buy/don't-buy on entry, grade remarks, empty-tank warning, random tips | Implemented (entry with tank test, grade, empty tank, random tips) |
| A4 | Plane advisor rates museum planes, price vs condition | **Partly.** Rating uses build year (< 1960) and condition bands only; price ignored. Also warns about worn planes | Implemented (museum rating, wear warnings) |
| A5 | Route advisor says if a route is good/bad | **True.** City-size remark plus popularity remark, or "blocked" after losing it | Implemented (`RouteOps.Advice`) |
| A6 | Order analyst tells which orders are worth taking | **True.** Feasibility checks, then cost/premium bands 0.5 / 0.9 / 1.4 | Passenger implemented; freight partial |
| A7 | Informer gives rival info | **True.** Map/route/price/dossier access plus rival-action messages; talent irrelevant | Implemented (UI gates, messages 9000..9006) |
| A8 | Security manager negotiates a discount, max about 10 % | **True in principle:** ⌊talent/10⌋ %, from talent 21. Shipped best is 99 → 9 % in practice | Implemented (`Player.Finance.Upkeep.cs:41`) |
| A9 | Fitness trainer makes you run faster (not walk) | **False in detail:** raises the base step, so walking and running both get faster (up to ×5/3) | Implemented (`Player.UpdateWalkSpeed`) |
| T10 | Horseshoe chain ends at Rick and prevents staff strikes | **Partly.** Chain is right; the horseshoe does not prevent strikes, it lets the drinker at Rick's end each strike quickly and without mood loss | Implemented (`ItemCommands.cs:260`, `RicksRoom.cs:185-203`) |
