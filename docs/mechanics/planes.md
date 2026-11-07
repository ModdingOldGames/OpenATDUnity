# Planes: types, condition, equipment, repairs, used planes, designer

Part of the OpenAT game-mechanics reference. It covers the **free game** of Airline Tycoon Deluxe
(difficulty −1). Campaign-only rules (mission availability, scripted plane damage) are mentioned
at most in passing.

Sources: our clean-room specs `docs/spec/findings/planes-and-flights.md`,
`rooms-and-ui-7-shops-and-planes.md`, `sim-core.md` and `economy.md`. Facts were spot-checked
against the original source (reference only; no code is reproduced here). Everything is in our own
words.

**No per-type data here.** The plane type table (seats, speed, range, burn, price, crew, availability
day ...) and the designer part catalogue are game data. They are read from the install at run time.
A local, gitignored dump for reference is generated into `docs/spec/local/mechanics/plane-types.md`.
Worked examples below use **illustrative** numbers unless they are engine constants.

Conventions:
- Money is in whole currency units. Integer maths, evaluated **left to right**, truncating toward
  zero, unless marked "float". So `a·b/c/d` means ((a·b)/c)/d.
- `Z` = plane condition 0..100 (stored as a byte). `Y` = build year. `Rand(n)` = uniform integer
  0..n−1 from the original's seeded generator (`TeakRand`). `Date` = day index, 0 = first day.
- Status tags: **Implemented** / **Partial** / **Missing**. File references are relative to
  `Assets/OpenAT/Sim/` unless the path says otherwise.

---

## 1. Plane type fields and what they drive

Each plane copies its type's values when it is created and the game only ever reads the plane's own
copy. Designer planes fill the same fields from their design (§11), so both kinds behave alike.

| Field | Unit | Used for |
|---|---|---|
| Seat units `S` | seats | cabin size; a first-class seat takes 2 units. Default split: economy `S·6/8`, first `S·1/8`. Freight capacity `S/10` tonnes. Per-seat equipment prices multiply by `S`. |
| Speed `v` | km/h | flight duration; fuel per flight |
| Range `R` | km | the planner refuses a leg longer than `R·1000` m |
| Burn `B` | l/h | fuel per flight |
| Pilots / attendants | persons | required crew; a plane without its full crew does not depart |
| Price `P` | money | broker price, market value, repair cost |
| Maintenance factor `f` | float | second (small) repair cost term (§9.3) |
| First year built | year | used-plane build years (§10) |
| Noise `N` | points | route passenger demand; **0 for every catalogue type**, only designer planes are noisy |
| Tank | litres | **display only**. The sim never uses it. |
| Span, length, height, take-off weight, engines, thrust, photos, comment | – | display only |

There is **no per-type reliability or wear field**. Wear depends on build year, distance and fuel
quality (§4.1). Breakdown chances depend on condition only (§5).

### 1.1 Flight duration

`d` = great-circle distance in metres (capped at 16,440,000).

    hours = ceil( trunc(d / v) / 1000 ) + 1,   minimum 2

That is airborne time rounded up to whole hours plus one hour of ground handling. A leg with
`hours ≥ 24` cannot be planned.

Example (illustrative type, `v` = 800 km/h): 1,000 km → trunc(1,000,000/800) = 1250 → ceil(1.25) = 2
→ **3 h**. 5,000 km → 6250 → 7 → **8 h**. Doubling the speed to 1600 km/h gives 2 h and 5 h.

### 1.2 Fuel per flight and fuel cost

    F = d/1000 · B / 160 / v            (fuel units, nominally barrels; integer, left to right)
    cost = F · K · q                     q = 2 premium, 1 normal, 1/2 cheap

`K` is the day's kerosene price per unit (economy.md). Units already in the airline's open central
tank are taken first and cost nothing at departure (they were paid on purchase). The estimate shown
in offers and the planner has a floor of 1,000; the real booking has none.

Example (illustrative: `B` = 3000 l/h, `v` = 800, 5,000 km, `K` = 500, normal):
F = 5000·3000/160/800 = 117 units → **58,500**. Fuel per km scales with `B/v`, so a fast plane with
the same burn is cheaper per trip.

### 1.3 Capacity, crew, noise

- Economy and first-class seats can be shifted in 10 % steps of the seat units at no cost (§7.5).
- Freight flights carry `S/10` tonnes. Switching a plane between passenger and freight use (seats
  out / seats in) costs **15,000** each time.
- Crew: pilots and attendants are assigned automatically from the staff pool. A plane short of
  either is delayed hour by hour (§6). Assigned crew salaries go to the plane's daily balance.
- Noise cuts route demand: each threshold exceeded multiplies the passenger count by 1000/1100
  (−9.1 %). Economy thresholds: noise > 60, > 80, > 100, > 110. First class: > 40, > 60, > 80, > 100.

**Implementation in OpenAT**
- Type table and loader: **Implemented**, `Data/PlaneType.cs:6`, `Data/GameData.cs:93`
  (`LoadPlaneTypes`, with the `atoi`/`atof` quirks); freight `PlaneType.CargoTons`
  `Data/PlaneType.cs:43`.
- Per-plane copies: **Implemented**, `Fleet/Plane.cs:74-89`, `Plane.FromType` `Fleet/Plane.cs:166`,
  default seat split `Fleet/Plane.cs:203-204`.
- Duration: **Implemented**, `Data/CitiesGeo.cs:55` (`FlightHours`); range and 24 h checks
  `Fleet/FleetCommands.cs:397-398`.
- Fuel units and costs: **Implemented**, `Fleet/OperationsRules.cs:60` (`FuelUnits`), estimate
  `:75`, booking `:105`.
- Seat conversion fee: **Implemented**, `Fleet/FlightOps.cs:140-142`.
- Crew delay: **Implemented**, `Fleet/FlightOps.cs:357`, `CrewShort` `:376`.
- Noise factors: **Implemented**, `Fleet/RoutePassengers.cs:128` (`ImageAndNoise`), thresholds at
  `:44` and `:68`.
- Tank is stored but unused: matches the original.

---

## 2. New planes: availability, broker, prices

### 2.1 When a type becomes available

Every type has a "first mission" and a "first day" column. In the **free game only the first day
counts**: the type is offered from day index `FirstDay` on (day 0 = the first day). In campaign
missions a type is available if its first mission is earlier than the current one, or equal and its
first day has been reached. One consequence: the fantasy supersonic type, which campaigns hide until
a late mission, is for sale from day 0 of a free game.

The same rule feeds the broker's list and the random type pick for museum offers (§10).

Rick (the bar) announces a new type on the day it debuts and three days before. The debut-day line
is one of his first topics. The three-days-ahead line only comes up if several random tips of his
do not fire first. Only the first matching type is named.

### 2.2 Buying at the broker

- Pick a type, then a quantity of **1, 2, 3, 5 or 10**. A quantity is only offered if
  `money − price·n ≥ −1,000,000` (the debt limit). If even one plane is unaffordable the broker
  declines.
- Each plane costs the type's list price. It is created with condition 100, build year
  `2002 + Date/365`, a free random name (§3), at the home airport, **delivered at once** (there is no
  delivery delay, whatever the broker says).
- Desired attendants are set to the type value times the fleet's current average ratio of desired to
  required attendants, then clamped to 0..2·(type value).
- Security advisor rebate: with a security advisor of skill `q > 20` the airline gets back
  `price_total/100 · (q/10)` (q/10 percent of the bill, at most 10 %). This applies to broker,
  museum and designer purchases alike.
- Planes cannot be leased, and the broker does not buy planes back. Selling happens only at the
  museum (§10.3).

### 2.3 Starting fleet

A free game starts with two planes per airline (the same two types for everyone). They cost nothing
and are flagged **sponsored**: their market value and hence their resale price are divided by 10.

**Implementation in OpenAT**
- Availability rule: **Implemented**, `Fleet/PlaneMarket.cs:19`; broker list `:34`; random type
  `:42`.
- Rick's announcements: **Implemented** (view), `Assets/OpenAT/View/Rooms/Ricks/RicksRoom.cs:154-163`.
- Broker purchase: **Implemented**, `Fleet/FleetCommands.cs:34` (`BuyPlaneCommand`, quantities `:27`,
  debt limit via `Player.CanAfford` `Player.Finance.cs:113`), `Fleet/PlaneMarket.cs:103` (`BuyNew`),
  attendant ratio `:90`, build year `:87`.
- Rebate: **Implemented**, `Fleet/FinanceIntegration.cs:84`.
- Starting fleet and sponsored flag: **Implemented**, `Fleet/OperationsSetup.cs:130` (`StartingTypes`),
  `:160-165`.

---

## 3. Plane names

- Names come from the install's name list, which has two tiers. A new plane gets a random unused
  tier-1 name (up to as many random tries as there are names), then the first unused tier-1 name in
  order, then the same for tier 2, then a fixed fallback. The first entry of each tier is never used.
  "Unused" means no plane of any of the four airlines has that name (museum offers do not count).
- One generator, seeded with the current `Date`, serves a whole purchase batch, so a batch gets
  distinct names. The starting fleet uses seed 1.
- The player can rename a plane (at most 20 characters).
- The fleet is kept sorted by name after every purchase.

**Implementation in OpenAT**
- Name search: **Implemented**, `Fleet/PlaneMarket.cs:61` (`UnusedName`); list loading
  `Data/GameData.cs:167`. The name check is case-sensitive like the original
  (`Fleet/PlaneMarket.cs:53`). Deviation: an over-long name list is silently
  truncated at 200/100 entries where the original stops with an error. That does not matter with the
  shipped list.
- Rename: **Implemented**, `Fleet/FleetCommands.cs:176`. Sorting: `Fleet/PlaneMarket.cs:130`.

---

## 4. Condition (wear) model

Each plane has three condition values:
- `Z`: current condition, 0..100;
- target condition: the level the workshop repairs up to (set by the player, §9.2);
- worst condition: a low-water mark that decides what a repair costs (§9.3).

### 4.1 Wear per flight

Every departure (any flight kind, including empty transfer flights) costs

    wear = (1 + d·10/40,040,174) · (2015 − Y) · k / 15

- `d·10/40,040,174` counts whole 4,004 km steps (a tenth of the earth's circumference), so legs under
  4,004 km count 1, 4,004..8,007 km count 2, and so on.
- `k` = 3 with bad fuel (cheap fuel bought for the flight, or an open tank that is more than half
  cheap fuel), else 1.
- The result is subtracted as a byte; a wrap below 0 becomes 0.

| Build year | short leg, good fuel | 4,004+ km leg | short leg, bad fuel |
|---|---|---|---|
| 2002 (new broker plane) | **0** | 1 | 2 |
| 1990 | 1 | 3 | 5 |
| 1975 | 2 | 5 | 8 |
| 1965 | 3 | 6 | 10 |

So new planes barely wear at all. Old museum planes lose several points a day and depend on the
workshop. Later new planes (`Y` = 2003, 2004 ...) wear even less.

There is no wear per day on the ground. Condition only changes overnight through the workshop
(§9.1).

**Implementation in OpenAT**
- Wear: **Implemented**, `Fleet/OperationsRules.cs:288` (`OriginalPlaneWear`), called at booking
  `Fleet/FlightOps.cs:193`; bad-fuel factor `Fleet/OperationsRules.cs:113`. Adds a modding tunable
  (`PlaneWearPercent`, 100 = original).

---

## 5. Breakdowns and incidents

There are **no crashes and no plane losses** in normal (free game) play.

### 5.1 Technical breakdown ("problem")

Checked once at every full hour from 06:00 to 16:00, not after the player ended the day, **only for
the local human player**, only if none of his planes already has a problem and he owns at least two
planes. Candidates are his airborne planes that land in this hour, scanned last to first. The first
candidate that passes

    Rand(10) = 0  and  Rand(100) > Z  and  Z < 90

breaks down (one per hour at most). Chance per landing ≈ `(99 − Z)/1000` for `Z < 90`, else 0:
Z 80 → 1.9 %, Z 50 → 4.9 %, Z 20 → 7.9 %.

Duration in hours:

    h = 4 + Rand(101 − Z)/3 + (15 if Rand(4) = 0)

- Landing at home: `h = max(4, h − 15)`.
- Landing abroad with `h > 20`: a **spare-parts order** appears: from home to the stranded plane's
  city, no passengers, no pay, no penalty, due by `Date + (h − (24 − hour))/24`. Any contract flight
  with 0 passengers that lands where the plane stands cuts its problem by 15 h (minimum 1).
- While `h > 0` every departure of the plane is delayed by one hour, re-checked hourly. `h` drops
  by 1 every hour.

### 5.2 In-flight incidents

Every 4th game minute, for every airline's airborne plane whose flight started at least an hour ago:

1. **Quality incident**, only if `Z < 80`: if `Rand(5000) > Z + 4920` and the flight carries
   passengers (route or contract): airline image −2, route image ×0.8, the airline's share price
   ×0.7, the trusted dividend −1 (if above 4), and an advisor report. Chance per roll
   `(79 − Z)/5000`; per flight hour (15 rolls) about **8.7 % at Z 50**, 4.2 % at Z 65, 0.3 % at Z 78.
2. **Seasonal incident**, any flight kind: if `Rand(5000) > 4980` (19 in 5000; ≈ 5.7 % per flight
   hour) and the plane is not protected, the airline pays **75,000**. Protection: in summer
   **safety level 2**; in spring, autumn and winter **electronics (cockpit) level 2**. Seasons use
   30-day months (`month·30 + day`): spring 5 Mar–20 May, summer 21 May–11 Aug, autumn 12 Aug–19 Dec,
   winter otherwise.

All planes rolled in the same minute share the seed, so incidents tend to hit several planes at once.

Example: an unprotected plane flying 12 airborne hours a day expects about 12 · 0.057 · 75,000 ≈
**51,000 a day** in seasonal costs.

**Implementation in OpenAT**
- Breakdowns: **Implemented**, `Fleet/FlightOps.cs:647` (`Breakdowns`, roll `:662-667`, spare-parts
  order `:668-677`), run at minute 0 (`Fleet/OperationsSystems.cs:37`); relief by 0-passenger
  contract `Fleet/FlightOps.cs:396-399`; hourly countdown and delay `Fleet/FlightOps.cs:342`, `:360`.
- Incidents: **Implemented**, `Fleet/FlightOps.cs:575` (quality `:588`, seasonal `:604-607`), every
  4th minute (`Fleet/OperationsSystems.cs:38`); costs via `Fleet/FinanceIntegration.cs:94`; season
  `Core/GameClock.cs:61`.
- Timing verified to match: the original rolls only on minutes it actually lands on that are a
  multiple of 4, but its step is a constant 30 time units (1/33 of a minute) at every speed; the speed
  setting only runs more steps per frame. So every minute is visited, as in OpenAT (same 30-unit tick).

---

## 6. Effects of condition

| Condition | Effect |
|---|---|
| `Z < 15` | grounded: every departure is delayed by one hour, re-checked hourly |
| `Z < 20` | per-flight image term −50 (plus the −10 below) |
| `Z < 60` | per-flight image term −10 |
| `Z > 98` | per-flight image term +10 |
| `Z < 80` | quality incidents possible (§5.2) |
| `Z < 90` | breakdowns possible (§5.1) |
| `Z < 69` | passengers may be unhappy because of condition: each group is unhappy if `Rand(70) > Z` |
| any | market value grows with `Z²` (§10.4) |

Condition has **no effect on ticket demand** (route passenger counts ignore it).

Per-flight image: for every route or contract flight the game sums an `add` term (condition terms
above; cabin items §7.3; attendants; ticket price vs. reference) and changes the airline image by
`add/10` and the route image by `add/10` (clamped 0..100). See economy/routes docs for the price and
crew-quality terms.

Example (human player): condition 100 (+10), all four cabin items at level 2 (+8), crew exactly as
required (+3) → add 21 → **+2 image per flight**. A price above 3× the reference (−10) brings it to
11 → +1. A plane at condition 50 with a bare cabin: −10 − 4 + 3 = −11 → −1 per flight.

**Implementation in OpenAT**
- Grounding at `Z < 15`: **Implemented**, `Fleet/FlightOps.cs:358`.
- Image terms: **Implemented**, `Fleet/FlightOps.cs:233` (`ImageEffects`), condition `:253-255`,
  cabin `:256-260`, attendants `:261`.
- Satisfaction: **Implemented**, `Fleet/FlightOps.cs:287` (see §7.4).

---

## 7. Equipment (plane properties room)

Eight items, each with levels 0..2 (shown as 1..3 stars). New and used planes start with **all
items at 0**.

| Item | Level 0 | Level 1 | Level 2 | Priced per |
|---|---|---|---|---|
| Seats | 300 | 1,200 | 3,000 | seat unit |
| Trays / entertainment | 100 | 800 | 3,400 | seat unit |
| Decoration | 500 | 1,500 | 6,000 | seat unit |
| Food | 10 | 20 | 50 | economy passenger **per flight** (running cost) |
| Engines | 100,000 | 750,000 | 3,500,000 | plane |
| Tyres | 50,000 | 300,000 | 900,000 | plane |
| Electronics (cockpit) | 600,000 | 1,500,000 | 8,000,000 | plane |
| Safety | 0 | 2,000,000 | 6,000,000 | plane |

(These are engine constants, not install data.)

### 7.1 Cost of a change

    per-seat item:  S · (price[new] − price[old]/2)
    per-plane item:       price[new] − price[old]/2
    food:           no change cost

A negative result is a refund: a downgrade recovers half the old level's price minus the new price.

Example (illustrative plane, `S` = 160 seat units):
- seats 0→2: 160 · (3000 − 150) = **456,000**; trays 0→2: 160 · 3350 = 536,000; deco 0→2:
  160 · 5750 = 920,000. Full cabin at level 2: 1,912,000.
- electronics 0→2: 8,000,000 − 300,000 = 7,700,000; safety 0→2: 6,000,000; engines 0→2: 3,450,000;
  tyres 0→2: 875,000. All four technical items at level 2: **18,025,000**, the same for every plane
  size.
- electronics 2→0: 600,000 − 4,000,000 = **−3,400,000** (refund).

### 7.2 When changes happen

- In the room the player only sets target levels. A change is refused if the pending costs of the
  **whole fleet** would exceed the current cash. Food changes are never refused.
- The refit happens when the plane is **on the ground in a city** and the owner is not in the room
  (or has ended the day). The cost is charged then (human airlines only; computer airlines refit for
  free in this step and use their own budget logic).
- Copy and paste of a configuration: paste applies **only the four technical items** (a literal
  quirk of the original).

### 7.3 What each item does

| Item | Satisfaction quality sum | Per-flight image | First-class luxury sum | Other |
|---|---|---|---|---|
| Seats, trays, deco, food | +level | −1 at level 0, +2 at level 2 (tenths) | +level | food is a per-passenger cost |
| Engines | – | – | +level | none: **no** effect on speed, burn, noise or breakdowns |
| Tyres | – | – | +level | none |
| Electronics | – | – | +level | level 2 blocks seasonal incidents in spring, autumn, winter |
| Safety | – | – | +level | level 2 blocks seasonal incidents in summer |

**First-class luxury.** Route first-class demand is multiplied by `(L − 6)/8` with
`L` = sum of all eight levels + 1 for each of two security-office subscriptions (0..18). `L < 6` →
**no first-class passengers at all** on routes. `L = 14` → normal demand; `L = 18` → 1.5× (still capped
by the share and the seats). So a new plane with bare equipment flies its first-class seats empty on
routes until `L ≥ 7`. Contract passengers are not affected.

No equipment item changes breakdown chances (only condition does, §5.1) or economy demand.

### 7.4 Passenger satisfaction (statistic)

On each route or contract arrival the passengers are judged in groups of **15** (plus one group with
the remainder). Rules for each group, first match wins:
1. `Rand(70) > Z` → unhappy;
2. quality sum `Q > 4` and `Rand(100) < 40` → happy;
3. `Q > 8` and `Rand(100) < 55` → happy;
4. `Rand(65) > crew quality` (average talent of the assigned crew, 60 without crew) → unhappy;
5. route ticket above 3× the reference fare and `Rand(130) < 100` → unhappy;
6. else happy.

`Q` = seats + food + trays + deco levels + (assigned attendants − required attendants).

The result only feeds the satisfied/unsatisfied passenger statistics. It changes neither image nor
demand.

Example: overpriced route, `Z` = 100, crew quality 60. Bare cabin: P(unhappy) ≈ 0.06 + 0.94·0.77 ≈
**78 %**. Full cabin plus one extra attendant (`Q` = 9): only 0.6·0.45 = 27 % of groups get past
rules 2–3, so P(unhappy) ≈ 0.27·0.78 ≈ **21 %**.

### 7.5 First-class split and extra attendants

- The FC share moves in ±10 % steps of the seat units; no cost; refused if it would no longer fit an
  already planned contract or freight job.
- Desired attendants can be set between the required value and twice that. Extras raise `Q` by one
  each and raise the image term `3·assigned/required`.

### 7.6 Food cost

Charged per route or contract flight: `economy passengers · food price[level]` (10 / 20 / 50).
First-class passengers (and contract passengers placed in FC seats) eat for free. Food sits in the
kerosene line of the accounts. Example: 120 economy passengers at level 2 → 6,000 per flight.

**Implementation in OpenAT**
- Price table and change cost: **Implemented**, `Fleet/Plane.cs:21-48` (`Equip`).
- Target check: **Implemented**, `Fleet/FleetCommands.cs:196` (`SetEquipmentCommand`), fleet sum
  `Fleet/FlightOps.cs:721`.
- Refit on the ground: **Implemented**, `Fleet/FlightOps.cs:695` (`ApplyRefits`).
- Copy/paste quirk: **Implemented**, `Fleet/FleetCommands.cs:289`, with option
  `GameOptions.FixPasteCabinEquipment` to also paste the cabin.
- Seasonal protection: **Implemented**, `Fleet/FlightOps.cs:607`.
- FC luxury factor: **Implemented**, `Fleet/RoutePassengers.cs:63-67`. Note: OpenAT defaults to the
  GOG behaviour (FC demand computed with the *economy* ticket price), with
  `GameOptions.FixFirstClassDemand` for the FC price (`Fleet/RoutePassengers.cs:53`). That follows
  the "original by default" rule, while the findings doc (§19) had recommended the fixed variant.
- Satisfaction: **Implemented**, `Fleet/FlightOps.cs:287`, group size `Fleet/FlightOps.cs:31` (15).
  Spec error, not a code bug: `planes-and-flights.md` §13 says groups of 5. The original and OpenAT
  both use 15.
- FC split and attendants: **Implemented**, `Fleet/FleetCommands.cs:221`, `:264`.
- Food: **Implemented**, `Fleet/FlightOps.cs:33`, `:102-106`.

---

## 8. Crew requirement (summary)

A plane needs its required pilots and attendants assigned, or it is delayed hourly. Spare attendants
are assigned up to each plane's desired count. Crew talent sets the "crew quality" used in
satisfaction (§7.4) and in a small image rule (economy/staff docs). Staff mechanics are documented
with the staff domain.

**Implementation in OpenAT**: crew mapping `Fleet/OperationsRules.cs:165` (default) and the staff
domain's mapping; delay check `Fleet/FlightOps.cs:357`.

---

## 9. Workshop: maintenance and repairs

### 9.1 Mechanic modes and nightly repair

The airline picks one mechanic mode. Its fee is per plane and month, charged daily as fee/30 for
**every** plane:

| Mode | Monthly fee per plane | Daily fee | Nightly condition change |
|---|---|---|---|
| 0 cleaning crew (hidden, needs full mechanic trust) | 1,200 | 40 | −2 on alternate nights (when plane slot + Z + Date is even), never up |
| 1 apprentice | 18,000 | 600 | +5, but −2 on every 8th combination (slot + Z + Date divisible by 8) |
| 2 mechanic (default) | 48,000 | 1,600 | `Z < 60`: +2..+6; else +2..+9 (shared generator per airline, seeded with Date) |
| 3 engineer | 150,000 | 5,000 | `Z < 60`: +18; else +15 |

Increases stop at 100 and at the plane's target. Mode 0 is offered only once the mechanic trusts
the player fully. Beer gives the first trust level; a later dialog choice raises it to full.

### 9.2 Target condition

The player sets a target per plane, or for all planes, in steps of 5 (0..100). A plane whose
condition is **2 or more points above its target** is skipped entirely that night: no repair, no
mode-0 decline, and its per-plane balance history does not move. The flat fee is still paid for it.

### 9.3 Repair cost

Nightly, per processed plane (planes from last to first):

1. `old = Z`; if `Z < worst` then `worst = Z`.
2. Apply the mode change, cap at 100 and at target.
3. If `Z > worst + 20`: `improvement = Z − (worst + 20)`, then `worst = Z − 20` (0 if Z < 20).
4. If `Z > old`, add to the bill:

       improvement · P / 110                                 (32-bit product, can overflow)
     + (Z − old) · 10 · f · (2100 − Y)/100 · (200 − Z)/100     (float, truncated)

   and raise `worst` to at least `Z − 20`.

Total booked overnight: Σ bills + monthly fee · planes / 30.

What it means: `worst` follows about 20 points under the best condition reached so far. **Repairing
wear within that 20-point band costs only the second term (usually a few hundred).** Every point
above the band costs `P/110` ≈ 0.91 % of the list price. A used plane is bought with
`worst = Z − 20` (§10.2), so every point you raise it above its purchase condition is billed.

Example (illustrative used plane: list price 44,000,000, built 1980, `f` = 1, bought at Z 60, target 100,
engineer mode):
- night 1: 60 → 75, improvement 15 → 15 · 400,000 = 6,000,000 + (15·10·120/100·125/100 = 225) →
  **6,000,225** + 5,000 fee;
- night 2: 75 → 90: 6,000,000 + 198; night 3: 90 → 100 (cap): 10 points → 4,000,000 + 120.
- Total ≈ 16.0 M to go from 60 to 100. Afterwards, flying ~6 points of wear a day and repairing it
  every night costs only fee + ≈ 70.

Overflow bug of the original: `improvement · P` is a 32-bit product. With `P` = 200 M and an improvement of 11 or
more in one night the product wraps negative and the night's repair **pays the player**. This only
affects very expensive types or designs. OpenAT computes it in 64 bits by default (`GameOptions.FixRepairCostOverflow`;
off restores the overflow).

### 9.4 Repair speed per night (from Z 40 to 100, no flights)

| Mode | Nights |
|---|---|
| 0 | never (only declines) |
| 1 | about 14 (+5, but one night in eight −2) |
| 2 | about 12 (+4 average below 60, +5.5 above) |
| 3 | 4 (40 → 58 → 76 → 91 → 100) |

**Implementation in OpenAT**
- Nightly upkeep: **Implemented**, `Fleet/Maintenance.cs:18` (`NightlyUpkeep`), fee table `:16`,
  skip rule `:31`, cost terms `:51-54` (64-bit unless `FixRepairCostOverflow` is off), booking
  `Player.Finance.Upkeep.cs:103`; run as day system `Fleet/OperationsSystems.cs:33`.
- Mode changes: **Implemented**, `Fleet/OperationsRules.cs:306` (`OriginalMaintenance`).
- Mode choice: **Implemented**, `Finance/FinanceCommands.cs:252` (`SetMechModeCommand`, mode 0 gated on
  `MechTrust == 2`); default mode 2 `Player.Finance.Upkeep.cs:22`.
- Target: **Implemented**, `Fleet/FleetCommands.cs:328` (`SetRepairTargetCommand`).

---

## 10. Used planes (museum) and selling

### 10.1 Daily offers

Three offer slots, regenerated at every day change. Slot `i` uses a generator seeded with
`Date + i`:

- a throw-away draw `Rand(80) + 11` (it ends up as the offer's internal worst value);
- type: a random **currently available** type (same rule as the broker, §2.1);
- name: a free name (§3);
- build year:
  - first year < 1990: `Y = 1990 − Rand(1990 − first year)` → first year + 1 .. 1990;
  - otherwise: `Y = 1996 − (draw mod (1996 − first year))`. For types first built in 1998 the
    modulus is negative and the result is 1995 or 1996, before the type's own first year (a quirk);
- condition: `Z = (Y − 1950) + 25 + Rand(40) − 20`, then below 20 → 20, above 100 → 100. Range:
  `(Y − 1950) + 5 .. (Y − 1950) + 44`, so 1990 planes come at 45..84 and 1965 planes at 20..59. The
  target is set to `Z`.

The draws come in the order throw-away, type, name. This matches MSVC's right-to-left argument
evaluation and is still an open question in the findings.

**Refill:** a counter counts 5-minute ticks (reset to 6 each morning, to 0 on each museum purchase).
Entering the museum regenerates `min(3, counter/20)` empty slots, i.e. one about 100 game minutes after
the last purchase. A regenerated slot uses the same seed, so the **same plane reappears**.

The museum is closed on Saturdays and Sundays.

### 10.2 Buying a used plane

Price = the plane's market value (§10.4), with the same debt-limit check and rebate as the broker.
The plane joins the fleet as offered (equipment 0, default seat split) with `worst = Z − 20`.

Example (illustrative type, list price 30,000,000): built 1975, Z 55 →
30,000,000·55/10000 = 165,000; ·55 = 9,075,000; ·75/120 = **5,671,875** (19 % of list). Repairing
to 100 then costs about 45 · 272,727 ≈ 12.3 M, about 18 M in all, versus 30 M new. But it keeps
wearing 2 points per short flight against 0 for a new plane, and resells for 62.5 % of list at Z 100
against 85 %.

### 10.3 Selling

- Only at the museum. A plane can be sold only if no route, contract or freight flight in its plan
  lands later than two hours ago. Automatic transfer flights do not block. Because route flights repeat
  weekly, a plane with a route schedule must have its plan cleared first.
- Sale price = market value · 9/10.

### 10.4 Market value

    value = P · Z / 10000 · Z · (Y − 1900) / 120          (64-bit, left to right)
    sponsored planes: value / 10

- Value is quadratic in condition and linear in build year. It does **not** drop with game time
  (the build year is fixed). It only drops through lost condition.
- New broker plane (Z 100, Y 2002): **85 %** of list; sold at once: 76.5 %. A 2010-built plane:
  91.7 %.
- Z 80 → 64 % of the Z 100 value; Z 50 → 25 %.
- Fleet value (Σ market values) also enters the company value statistic.

**Implementation in OpenAT**
- Offers: **Implemented**, `Fleet/PlaneMarket.cs:146` (`CreateUsedPlane`), daily `:168` via
  `Fleet/OperationsSystems.cs:22`; refill `Fleet/PlaneMarket.cs:179`, ticks
  `Contracts/ContractBoards.cs:172`, `:190`.
- Opening days: **Implemented**, `Fleet/FleetCommands.cs:109` (`MuseumHours`).
- Purchase: **Implemented**, `Fleet/FleetCommands.cs:115` (`BuyUsedPlaneCommand`).
- Sale: **Implemented**, `Fleet/FleetCommands.cs:146`, block rule `Fleet/FlightPlanner.cs:311`,
  price `Fleet/PlaneMarket.cs:194`.
- Market value: **Implemented**, `Fleet/Plane.cs:144`; company value `Fleet/FinanceIntegration.cs:204`.

---

## 11. Custom planes (designer and editor)

### 11.1 Flow

In the editor the player assembles a design from parts and saves it as a design file (the files are
shared between games, not saved game state). At the designer desk the player buys copies of any
**buildable** design: quantity 1/2/3/5/10, same debt-limit and rebate rules as the broker. Each copy
costs the design's cost, gets condition 100 and build year `2002 + Date/365`, and is **delivered at
once**. There is no development fee and no build time.

### 11.2 Parts and limits

Part categories: body, cockpit, tail, right wing, left wing, engine. Each part has cost, weight,
power, noise, maintenance, seat units and burn values (from the install's part table). Parts attach
through a fixed attachment graph. Each attachment ("relation") needs a slot that no already-placed
attachment blocks, may block other slots, adds extra noise, and carries up to three **notes**:
- "breaks often" / "breaks very often": +10 / +20 maintenance **and** +2,500 / +5,000 l/h burn (one
  code is used for both meanings, so maintenance notes always raise consumption too);
- speed caps at 300..800 km/h;
- +1..+4 pilots, +4/+6/+8/+10 attendants.

A design is **buildable** when it has at least one body, cockpit, tail, right wing and engine, and:
1. every engine on a right wing has its mirror engine on the left wing, and vice versa;
2. `power · 4 ≥ weight`.

### 11.3 Derived stats

| Stat | Rule |
|---|---|
| Cost | Σ part cost |
| Seat units | Σ part seats |
| Weight, power | Σ part values (bodies, wings and some tails have negative power) |
| Speed | 0 if power = 0, else `(power − 6000)·730/44000 + 270`; then for every speed-cap note `c` in part order: if speed > c, speed = (2·speed + c)/3 |
| Burn | Σ part burn + 2,500 / 5,000 per breaks note |
| Tank | 7 · Σ weight of wing parts; capped at 22 · burn (the editor's own display shows the uncapped value) |
| Range | `tank / burn · speed` (whole hours first), 0 if burn ≤ 0. So range ≤ 22 h · speed. |
| Pilots / attendants | Σ notes, minimum 1 each |
| Noise | Σ part noise + Σ attachment noise |
| Maintenance | Σ part maintenance + 10 / 20 per breaks note; plane factor `f = (maint + 100)/100` |

The resulting plane uses these values exactly like a catalogue type: speed and burn drive duration
and fuel, noise cuts demand (§1.3), and `f` scales only the small repair term. **Maintenance and the
"breaks" notes do not change breakdown chances.** Breakdowns depend on condition only.

Example (illustrative parts): power 15,200 → (9,200·730/44000) + 270 = 152 + 270 = 422 km/h; a
cockpit with a 400 km/h cap → (844 + 400)/3 = **414 km/h**. Wings weighing 2·2,500 → tank 35,000;
burn 2,200 → 35,000/2,200 = 15 h → range 15·414 = **6,210 km**. (A real shipped-part example is in
the local file.)

**Implementation in OpenAT**
- Catalogue and graph: **Implemented**, `Data/PlaneDesign.cs:88` (`PlaneDesignCatalog`, install
  overrides `:137`), compiled relation data `Data/PlaneDesignData.cs`.
- Stats: **Implemented**, `Data/PlaneDesign.cs:265` (`Stats`); errors `:317`; buildable `:350`.
- Purchase: **Implemented**, `Fleet/FleetCommands.cs:68`, `Fleet/PlaneMarket.cs:117`,
  `Fleet/Plane.cs:182` (`FromDesign`).
- Editor: `Data/PlaneDesignEditor.cs`, design library `Data/PlaneDesignLibrary.cs`.

---

## 12. Plane advisor

Hired like other advisors (staff domain). Its comments:

- **Museum hover:** a comment on the offer under the mouse. Built before 1960 gets its own "too old"
  comment. Otherwise the condition picks one of five grades: < 20, < 40, < 60, < 80, or better. The
  wording is read from the game texts. It does not judge the price against the value.
- **Random fleet warning:** during the working day (from 09:00 until the day ends) the game tries an
  advisor message about every 1,024 sim ticks (≈ once per game minute). The advisor topic is picked at
  random (non-deterministic `rand() % 3`), and one value in three is the plane advisor. If the
  mechanic mode is 0, 1 or 2, it lists the planes with `Z < 30` (urgent); if there are none, those
  with `Z < 55`. In modes 0/1 a hint about the poor maintenance mode is appended. In mode 3 it stays
  silent.

**Implementation in OpenAT**
- Museum hover: **Implemented** (view), `Assets/OpenAT/View/Rooms/Museum/MuseumRoom.cs:122-126`.
- Random fleet warning: **Implemented**, `AdvisorChatter` in `Assets/OpenAT/Sim/Staff/StaffModule.cs` (checked every 512 ticks on the game clock, see staff-and-advisors.md §9).

---

## 13. Guide claims (GameFAQs 11142, older edition)

| ID | Claim (paraphrase) | Verdict for Deluxe | OpenAT |
|---|---|---|---|
| E1 | Better equipment lets you charge more without complaints, means fewer breakdowns, and happy passengers raise the image | **Partly.** Cabin quality only lowers the share of *unhappy* passengers when you overcharge (statistic only, §7.4). It does not change economy demand. **No** item reduces breakdowns (condition only). Satisfaction does **not** feed the image; the cabin items and condition change the image directly per flight (§6). Electronics/safety level 2 prevent the 75,000 seasonal incidents. | Implemented |
| E2 | Tyres matter by season; engines are quiet/safe and pricey; cheap safety gives a bad image; cockpit calms passengers | **Mostly false.** Tyres and engines only add to the first-class luxury sum. Engines change neither noise, speed nor burn. Safety has no image effect; level 2 blocks *summer* incidents. Cockpit (electronics) level 2 blocks spring/autumn/winter incidents. | Implemented |
| E3 | Comfort: seats, luxury (1–3 stars), ambience, food; all one-off except food, paid per passenger per flight | **True.** Levels 0..2 shown as 1..3 stars. Food 10/20/50 per *economy* passenger per flight; FC eats free. Downgrades refund half the old price minus the new one. | Implemented |
| F5 | Broker sells new planes, expensive; new types are added over time and Rick announces them | **True.** Free game: per-type day offset. Rick tells on the day and 3 days ahead (low priority). Delivery is immediate; broker never buys back. | Implemented |
| F11 | Museum sells old planes (varying condition, often need the workshop) and buys planes | **True.** 3 offers a day, condition 20..90, price = market value. It buys **any** plane (not just old ones) at 90 % of value, if no flight is pending. Closed on weekends. | Implemented |
| F16 | Workshop: order repairs, choose who maintains the planes | **True.** 4 mechanic modes (one hidden), per-plane/all target 0..100 in steps of 5. Cost model §9.3. | Implemented |
| F17 | Designer (Deluxe): custom planes, expensive | **True, but not always expensive.** Price = sum of parts; small designs cost well below comparable catalogue planes. Instant delivery, no build time. | Implemented |
| T3 | Museum plane + repair is cheaper than the broker; avoid planes over ~9,000 l/h | **Partly.** Purchase plus repair to 100 usually costs well under list price (§10.2), but old planes keep wearing 2–3 points per flight, resell lower, and every repaired point above the 20-point band costs ~0.9 % of list. Fuel cost scales with burn/speed per seat, not burn alone, so the 9,000 l/h line is only a rough heuristic. | Implemented |
| Plane list | 26 types with passengers/freight/range/speed/tank/burn/price/crew | **Matches** the Deluxe install on every listed field except one range value (the L-1011; Deluxe gives it more range). Freight = seats/10 is exact. Tank is display-only in Deluxe. Details: local `docs/spec/local/mechanics/plane-types.md`. | Read from install |
