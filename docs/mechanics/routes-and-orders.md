# Routes, orders, gates and branches

Game-mechanics reference for the free game of Airline Tycoon Deluxe, written for OpenAT. Sources:
our behaviour specs (`docs/spec/findings/contracts-and-routes.md`, `economy.md` §13,
`planes-and-flights.md` §10–12, `ai-robots.md` §5–6, `rooms-and-ui-6-airport-services.md`
§3.8–8, `sim-core.md` §3.2), with the numbers re-checked against the reference source. Formulas are
restated in our own words. All arithmetic is integer, evaluated left to right, unless stated.

Units: money in game currency; distances in metres in formulas (km in prose); `K` = today's global
kerosene price (starts at 500, moves daily within 300…700); "day" = game day index.

---

## 1. The two price yardsticks

Every price in this topic is derived from one fuel-cost helper. For a notional plane burning `b`
litres/hour at `v` km/h over distance `d` (metres):

```
barrels(b, v) = ⌊⌊⌊d/1000⌋·b / 160⌋ / v⌋
fuelcost(b,v) = max(1000, barrels · K)
```

| Yardstick | Definition | Used for |
|---|---|---|
| **RefPrice** (route ticket reference) | `⌊⌊fuelcost(800, 800)·3/180⌋·2⌋` ≈ fuelcost/30 | ticket presets and clamps, overpricing cut-offs, image price check, satisfaction |
| **Base(f)** (order price base) | `⌈fuelcost(8000, 700)/100⌉ · f` | every order's payment, the order analyst |

Both follow distance **and** the daily kerosene price, so the same route or order is worth more on
expensive-fuel days.

**Worked example** (1000 km, K = 500): fuelcost(800,800) = max(1000, 6·500) = 3000, so RefPrice =
100. fuelcost(8000,700) = 71·500 = 35 500, so Base(f) = 355·f. Very short routes hit the 1000
minimum: RefPrice is never below 32.

### Implementation in OpenAT
* Implemented: `Assets/OpenAT/Sim/World.Operations.cs:142` (RefPrice), `:145` (Base).

---

## 2. Routes

### 2.1 The route table and licences

* A fixed table of city pairs; each pair exists as two **directed** routes (A→B and B→A) with the
  same level (1 regional, 2 long-haul), monthly fee and popularity factor. Fees in the shipped data
  run from 50 000 to 1 600 000 per month.
* Up to **3 airlines** may hold a licence on a direction. Each holder has a **rank** 1–3 = order of
  licensing; when a higher-ranked holder leaves, everyone below moves up one.
* Licences are taken at the **route office** (pin board next to the airport manager). Deluxe never
  puts routes on the manager's auction board (the code path exists but is unused).

**Eligibility** (computed when you enter the route office), for a direction you do not hold:
1. it touches the home airport, **or**
2. it shares an endpoint with any route on which your share of demand (§2.4) is ≥ 20 %;
3. **and** it is not in cooldown: after a licence was **taken away** for low use, that direction is
   blocked for 7 days. A voluntary drop has no cooldown.

The rank you get is 1 + the highest rank any active airline holds there; above 3 it is refused.

**Licensing effect:**
* **No up-front payment.**
* Both directions get your rank, the table fee, route image 0, seat load 0, and default prices:
  economy = 2·RefPrice rounded down to a multiple of 10, first class = 2 × economy.

**Rent:**
* Every night: `fee/30` (integer) for **each licensed direction**. Because licensing always takes
  both directions, a pair costs `2·fee/30` per day, twice what the route office quotes (it shows
  fee/30 per day and fee per month). Example: a 300 000 pair costs 20 000 per day, 600 000 per
  30 days.
* Booked as route rent (ledger 2052) with gate and branch rents under the "rents" statistic.

**Dropping** a route (route office, notebook, filofax): refused while any flight-plan entry uses
either direction. Both directions are released at once, with no refund and no cooldown.

**Quirk:** the share value (§2.4) is not reset by dropping or re-licensing, and it is only updated
while licensed. A dropped route that had ≥ 20 % share keeps unlocking its neighbours.

### Implementation in OpenAT
* Implemented: route and licence state `Assets/OpenAT/Sim/Routes/Route.cs:10-64`; rent helper
  `Assets/OpenAT/Sim/Routes/RouteOps.cs:21-40`; eligibility `RouteOps.cs:46-61`; rank
  `RouteOps.cs:64-70`; licensing `RouteOps.cs:86-100`; drop `RouteOps.cs:103-116`; commands with
  refusal reasons `Assets/OpenAT/Sim/Routes/RouteCommands.cs:76-110`; nightly rents
  `Assets/OpenAT/Sim/Player.Finance.Upkeep.cs:79-90` fed by
  `Assets/OpenAT/Sim/Fleet/FinanceIntegration.cs:217-224`. The two-direction charge is kept.
* Doc fix needed: `docs/spec/findings/rooms-and-ui-6-airport-services.md` §8.4 says a voluntary drop
  resets the counter to 0 (7-day block). The source sets 99 (no block); the revocation path sets 0.
  OpenAT follows the source (`RouteOps.cs:113-114` vs `:146`).

### 2.2 Demand per route

Potential daily passengers of a direction (floating point, then truncated):

```
DayF      = 1000 (day < 3), 900 (day < 9), 750 (day < 29), else 650
Potential = ⌊ √(pop_from · pop_to) · factor / DayF / 2 · 3/4 ⌋
```

Demand therefore rises by about 54 % over the first month as DayF falls.

The **demand pool** (people waiting) is shared by all airlines on that direction:
* day 0: `pool = ⌊Potential · 4.27⌋`
* every later night: `pool = (6·pool + ⌊Potential·4.27⌋) / 7`, an exponential pull toward 4.27 ×
  the potential (about 1/7 of the gap closes per night).
* Every route flight removes its **economy** passengers from the pool of the flown direction
  (floor 0). First-class passengers are not removed.

The route office shows the potential as "daily demand" and the pool as "current demand".

### Implementation in OpenAT
* Implemented: `Assets/OpenAT/Sim/Fleet/OperationsRules.cs:343-363` (tunable factor 4.27 and a
  potential % tunable for mods), nightly update `RouteOps.cs:266-270`, pool reduction
  `Assets/OpenAT/Sim/Fleet/FlightOps.cs:177-179`.

### 2.3 Passengers on one route flight

Recalculated every time the entry is created, moved or repriced, and every hour, but only while the
departure is more than about an hour away. After that the number is frozen and used at take-off.
`H` = hours of notice between the last change and departure (capped; auto-extended weekly entries
count as 48).

**Economy**, for the planning airline `i` with the entry's price `P`:

1. **Share weights** for each of the four airlines `c` holding the direction:
   `w = 10000/P` (100 000 if P = 0); ×0.9 if P > 3·RefPrice, ×0.9 again if P > 5·RefPrice, ×0.8
   if P > 6·RefPrice; at least 1. Rank 1 ×1.5, rank 2 ×1.2. Then ×½ in the original (a "rarely flown" factor that
   is the same for everyone, because the underlying counter is never updated; OpenAT updates it nightly by
   default, `FixRouteAvgFlown`, so the factor is 10/(10 + min(avg − 3, 10)) only for a holder whose average
   days between flights is above 4). Computer airlines
   +25 %. Non-holders weigh 0 (the planner itself at least 1).
   **Quirk:** every holder's weight is computed with the *planning flight's* price, not that
   holder's own price. Relative price therefore does **not** move market share. Only rank and
   computer status do.
2. `n = pool · w_i / Σw`.
3. Cap at 1.5 × economy seats.
4. Short notice: if H < 48, `n = n·(H+48)/96` (as low as ½ for a flight booked at the last moment).
5. Overpricing: if P > 3·RefPrice, `n = n·(3·RefPrice − 10)/P`.
6. Night: ×5/6 if the departure hour is before 05 or after 22, and again for the landing hour.
7. Image: `× (400 + clamp(4·routeImage + companyImage + 200, 0, 1000)) / 1100` (see
   `image-and-advertising.md` §2).
8. Noise: ×10/11 for each threshold the plane's noise exceeds (60, 80, 100, 110).
9. Final: min(n, pool·w_i/Σw, economy seats).
10. Under a rival's "press statement" prank: 1 + (n mod 3).

**First class:** same chain with weight thresholds 9×/15×/18× RefPrice, rank 1 ×1.7, cap 1.5 ×
first-class seats, short notice `(H+48)/120` when H < 72, overpricing only above 9·RefPrice
(factor `(3·RefPrice − 10)/P`), noise thresholds 40/60/80/100, and a **luxury gate**: L = sum of the
plane's eight equipment levels (0–2 each) + two top gate-security upgrades (0–18); L < 6 gives 0
first-class passengers, otherwise ×(L−6)/8 (up to 1.5×).
Original quirks: the first-class chain uses the **economy** price; and because step 4 of the economy
chain already capped H at 48, first class always gets at most (48+48)/120 = 0.8 from its notice
step.

**Income at take-off:** economy price × economy passengers + first-class price × first-class
passengers. Costs: fuel (pump or tank) plus catering 10/20/50 per economy passenger by food level.
Computer airlines also receive a hidden bonus top-up per flight (§2.7 of `sim-core.md`).

**Worked example.** A pair where you (human) hold rank 1 and one computer airline rank 2, your entry
priced P = 200 with RefPrice = 100: your weight 50 → 75 → 37; the robot's 50 → 60 → 30 → 37. Shares
are equal. With pool 3000 the raw estimate is 1500, capped at 225 for a 150-seat plane. Booked two
days ahead (no notice cut), daytime, image factor 0.709 (route image 20, company 100) → 159, quiet
plane → 159, seat cap → **150**. The same flight booked the evening before (H = 12) gets
225·60/96 = 140, then image → 99.

### Implementation in OpenAT
* Implemented: `Assets/OpenAT/Sim/Fleet/RoutePassengers.cs:13-82` (weights `:88-119`; the first-class
  price bug is fixed by default (`FixFirstClassDemand` on), the other quirks
  are kept, including the H-cap carry-over into first class at `:39-40`/`:57-58`).
  Switches: `GameOptions.FixFirstClassDemand` (first-class price) and
  `GameOptions.FairRouteCompetition` (each holder's own price), `Assets/OpenAT/Sim/GameOptions.cs:20,65`.
  Income `Assets/OpenAT/Sim/Fleet/FlightOps.cs:206-216`.

### 2.4 The utilisation rule ("use it or lose it")

Every night, for each licensed **direction**:

```
carriedToday  = economy pax of flights on this direction
              + economy AND first-class pax of flights on the reverse direction
share         = min(100, (4·share + carriedToday·100 / Potential) / 5)
```

So the share is a moving average (weight 1/5 per day) of **passengers carried as % of the daily
potential demand**, not % of seats and not % of the pool. Both directions are credited by every
flight, so one round trip a day feeds both.

* If share < 10: a low-use day counter +1; otherwise it resets to 0.
* Humans get a warning letter on low-use day 10 and day 15 (one letter per city pair), and a
  loss letter on day 20.
* On **day 20 in a row** the licence of that direction is **taken away**: rank 0, higher ranks
  move up, all its future flight-plan entries are deleted, and a **7-day cooldown** starts. After
  that it can be licensed again like any other route (if still eligible).
* Robots in route mode rescue a leg from low-use day 15 on by throwing up to four planes at it.

**Worked example.** Potential 400/day. To stay above 10 % you need to carry on average about 40
passengers a day on that direction (counting the reverse direction's flights). Starting from share
0, one 150-passenger flight per day in each direction credits the direction with about 150 + 150 =
300 a day = 75 %, and the share reaches 15, 27, 36 … within days. A route never flown decays from
its last value by a factor 4/5 per night (truncated): from 50 % it goes 40, 32, 25, 20, 16, 12, 9,
so the 7th idle night is the first low night, and the licence is lost on the 20th low night in a
row (26 idle nights in total).

### Implementation in OpenAT
* Implemented: crediting `Assets/OpenAT/Sim/Fleet/FlightOps.cs:107-120`, nightly rule and letters
  `Assets/OpenAT/Sim/Routes/RouteOps.cs:174-204`, revocation `RouteOps.cs:123-167`, cooldown check
  `RouteOps.cs:51` and `RouteCommands.cs:86-87`. Adds a guard against Potential = 0 (modded data)
  at `RouteOps.cs:185`.

### 2.5 Ticket prices

* Presets ½, 1, 2, 4 × RefPrice (each rounded down to ×10), first class = 2 × the economy preset;
  ±10 buttons; economy clamped to [0, 16·RefPrice], first class to [0, 48·RefPrice].
* The economy price is copied to the reverse direction; first class is not.
* Future plan entries take the new price; when a price is raised, their passengers are recalculated.
* With an informer you can copy a competitor's prices.
* What price does (because of the share quirk in §2.3): above 3·RefPrice it cuts passengers
  (`(3·RefPrice − 10)/P`), costs image every flight, and makes arriving passengers unhappy; below
  1.5·RefPrice it gains image every flight. Between those it only changes revenue.
* Robot pricing: 2·RefPrice (rounded), capped at 2× each other holder's price, undercutting
  strongly disliked rivals by 10, with floors ½·RefPrice / RefPrice (cash < 100 000) / 2·RefPrice
  (cash < 0).

### Implementation in OpenAT
* Implemented: `Assets/OpenAT/Sim/Routes/RouteOps.cs:211-263`, command
  `Assets/OpenAT/Sim/Routes/RouteCommands.cs:113-133`.

### 2.6 Route advisor (route office tooltip)

With any route advisor employed (talent does not matter, only presence), hovering a route gives:
* If the direction is unheld and in its 7-day cooldown: a "blocked for now" line only.
* Otherwise a city-size verdict: both endpoints above 2 000 000 inhabitants → good; one endpoint
  above 2 M → fair (names that city); neither → modest.
* Plus a demand remark from the popularity factor: factor > level + 0.5 → popular;
  factor < level − 0.3 → unpopular; otherwise nothing.

Examples from the shipped table: a level-2 pair with factor 3.5 is "popular"; a level-1 pair with
factor 2 is "popular"; a level-1 pair with factor 0.7 is "unpopular".

### Implementation in OpenAT
* Implemented: verdict `Assets/OpenAT/Sim/Routes/RouteOps.cs` (`Advice`: blocked only for unheld +
  cooldown, size verdict 8000/8001/8002, remark 8010/8011 from factor vs level), shown by
  `Assets/OpenAT/View/Rooms/RouteBox/RouteBoxRoom.cs` (`AdviseHover`).
* Implemented: the paper clips on the board (hotspots as the original) give the paper clip item
  (`PickUpItemCommand`); once taken that day the info sheet and airline toggles are gone and the
  "no paper" overlay shows (`RouteBoxRoom.cs`); the hall pin board brick shows its second frame
  (`Assets/OpenAT/View/Airport/AirportScreen.cs`, brick 421).

---

## 3. Flight orders (contracts)

### 3.1 Where they come from

| Source | Pool | Opening | Price base | Cities |
|---|---|---|---|---|
| Travel agency (home airport) | 6 shared slots | until 17:00 | Base(120) | two random cities, one replaced by home |
| Last Minute (home airport) | 6 shared slots | until 16:00, closed Saturday | Base(130) × 5/4 | slots 0–1: both cities in one random region, home **excluded**; 2–3: home region incl. home; 4–5: anywhere |
| Branch office phone call (city X) | 6 per city | any time (phone charges) | Base(120) | as travel agency, with X instead of home |
| Freight office (add-on) | 6 shared crates | no closing time found | Base(105) | as Last Minute's picking |
| Branch freight (city X) | 6 per city | by phone | Base(105) | as travel agency |
| Start of game | 2 per airline | — | Base(100) | from home, ≤ 5000 km, 90 pax |

* Pools are **shared by all four airlines**: first come, first served.
* **Daily full refill** overnight (seeded per pool from the date), plus a **trickle**: a counter per
  pool rises by 1 every 5 game minutes; when anyone enters or leaves the counter room (or opens a
  branch list), up to that many empty slots are regenerated. About one new offer per 5 minutes
  since the last visit, capped at a full board.
* Early-game help: on days 0–4 (slots 0–4) and days 5–9 (slots 0–2) the travel agency and branch
  lists only draw city pairs within 10 000 km and without the area surcharges.

### 3.2 The generator (common template)

1. Cities as above (repeat until different).
2. 180 passengers; departure day = today + 1…3; last day = first day, with a 1/5 chance of a window
   of up to 4 extra days.
3. Pay `Praemie = Base(f)`; penalty `Strafe = Praemie/2`.
4. Type roll (0–99): normal; relaxed (window opens today, lasts 4–6 days); tight (pay ×2, penalty =
   2 × new pay, tomorrow only); swindle (pay ÷2, penalty unchanged so penalty = pay); windfall (pay
   ×2, no penalty). Ranges: travel agency 0–49 / 50–64 / 65–79 / 80–94 / 95–99; others use a gap
   (60–69 = normal) and tight = 70–79.
5. Size roll: 2 pax (×4 pay, penalty = 4 × pay; 1 pax at Last Minute), 90 (×3/4), 180 (×1),
   280 (×5/4), 340 (×6/4), 430 (×7/4). Small sizes are more likely in the first week.
6. Area surcharge: slots 2–3 ×3/2, slots 4–5 ×8/5.
7. Window discount: if the window spans more than one day, it opens today and pay ×4/5.
8. Retry (up to 100 times) if no human plane in the game could carry it (seats or range).

**Last Minute differences:** departs today (relaxed type: tomorrow), no windows, tight type penalty
= 4 × pay. Its penalty is set **before** the ×5/4 surcharge, so a normal Last Minute order's penalty
is 40 % of its pay, not 50 %.

**Freight differences:** tonnage 1/15/30/40/60/100 t instead of seats, then a tonnage bonus (pay
×3 above 30 t, ×4 above 60 t, ×5 above 80 t). A crate with pay 0 is relief cargo (no income).

**Worked example** (1000 km, K = 500, Base = 355·f). A normal travel-agency order, 280 passengers,
slot 3: 42 600 → ×5/4 = 53 250 → ×3/2 = **79 875**, penalty 21 300. A normal Last Minute order, 180
passengers, slot 0: 46 150 → ×5/4 = **57 687**, penalty 23 075.

### 3.3 Accepting, flying, payment

* Clicking an offer accepts it **at once**, with no cost and no limit on how many you hold. There is
  no undo except a cancel, which costs the full penalty immediately.
* Put it in a plane's plan. It is valid if a departure falls in [first day, last day], the plane
  has enough seats (economy + first class together, one plane only) and the trip is < 24 h and
  within range.
* **Payment at take-off:** the full pay if the entry is valid. All passengers count in statistics;
  image effects apply (no price check).
* Flying it on a wrong day uses the order up **without pay and without penalty**. An undersized
  plane leaves it open (and shows a warning).
* Freight: each flight carries type seats/10 tonnes; pay is credited only on the flight that brings
  the remaining tonnage to 0. Switching a plane between seats and cargo costs 15 000 each time.

### 3.4 Deadlines and penalties

* On the morning after the last allowed day: if the order is neither flown nor validly planned, a
  human pays the **penalty** (letter, counts against the profit mission); with penalty 0 a polite
  letter only.
* **Computer airlines never pay order penalties.**
* Two days after the last day the order disappears from the album and from future plans.

### 3.5 Order analyst (order advisor)

With any order advisor employed (talent does not matter), hovering an offer gives, in order:
1. an existing future ferry flight on the same pair and window with enough seats can be replaced
   by this order;
2. no plane has the range; 3. no plane has the seats; 4. no plane has both;
5. a profit rating against a **notional** cost `Base(100)` (not your real plane): no penalty →
   neutral note; cost ≤ ½ pay → excellent; ≤ 0.9 pay → good; ≤ 1.4 pay → so-so; else poor.
   Freight: cost × ⌊tonnes/22⌋ (at least 1 trip), relief cargo has its own note.

Since the cost reference is Base(100) and a normal 180-passenger order pays Base(120) (travel
agency) or about Base(162) (Last Minute), such orders rate at least "good"; 90-passenger and swindle
orders drop to "so-so". The rating ignores positioning flights and your real plane's fuel burn.

**Worked example:** the travel-agency order above (pay 79 875, cost 35 500): 35 500 ≤ 39 937 →
excellent.

### 3.6 Computer airlines and orders
Robots in "order mode" take offers from all counters and their (and cooperating robots') branches:
a plane's next free point, the nearest departure city, distance caps, cost of positioning ≤ pay.
They do **not** check seats or the date window, and their orders are never validated, so every robot
order flight is paid.

### Implementation in OpenAT
* Implemented: generators `Assets/OpenAT/Sim/Contracts/ContractGenerator.cs:17-320` (Last Minute
  penalty order kept at `:99-101`); pools, seeds and trickle
  `Assets/OpenAT/Sim/Contracts/ContractBoards.cs:14-192`; opening hours and accept/cancel
  `Assets/OpenAT/Sim/Contracts/ContractCommands.cs:11-190`; validation and penalties
  `Assets/OpenAT/Sim/Contracts/ContractUsage.cs:16-200`; payment and done-marking
  `Assets/OpenAT/Sim/Fleet/FlightOps.cs:77-99,183-186`; analyst
  `ContractCommands.cs:198-237`; robot order taking `Assets/OpenAT/Sim/AI/RobotExecutor.Orders.cs`.
* Implemented (cooperation): `CounterHours.HasBranch` (`ContractCommands.cs`) accepts an own branch
  or one of a cooperating airline still in the game, so humans can take orders and freight from a
  partner's branch listed in `Assets/OpenAT/View/Rooms/World/WorldRoom.cs`. (Robots still use their
  own shortcut `Assets/OpenAT/Sim/AI/RobotShortcuts.cs` `AcceptForeignContract`.)
* Implemented: the analyst compares seats with the configured economy + first-class seats (ferry
  test and the 7011/7012 checks); freight rating has the ferry (7113) and range (7110) checks in the
  original order (`ContractAdvisor.RateFreight`); 7013/7113 name the plane
  (`Assets/OpenAT/View/Rooms/Counters/OrderCounterRoom.cs`).

---

## 4. Gates at the home airport

* Each airline starts with **one gate** (number = airline index) at 1000/month.
* More gates **only** through the auction board (§5). There is no way to give a gate back.
* Rent `rent/30` per gate per night (always booked, ledger 2050).
* After every auction night all rented gates are renumbered 0, 1, 2 … in airline order.
* **Gate planning** (rerun after every plan change, 7 days × 24 hours): a home departure (route or
  order flight) needs a free gate in its departure hour and the hour before; an arrival occupies the
  hour before landing. Ferry and freight flights never need gates.
* No gate: the plane boards from the apron and image −2 on departure; landing with no gate −2 and
  all its passengers count as unhappy. Robots automatically push gate-less flights back up to 6
  hours; humans must fix their plans themselves.

### 4.1 Airport expansion (airport manager)

Asking the manager for more capacity is decided in this order:
1. Gate numbers exist that nobody rents → refused (bid for those first).
2. Check-in level ≥ 5 (or ≥ 2 in missions up to Normal) → maximum reached.
3. Day < 8 → too early.
4. Fewer than 3 days since the last expansion → too soon.
5. One already ordered today → already ordered.
6. Otherwise offered: **1 000 000**, paid at once by the asking airline.

The next night the hall gets one more check-in level and one more waiting zone (with baggage),
which adds gate numbers that then appear on the auction board. Expansion buys capacity for
**everyone**; the gates themselves still have to be won at auction.

### Implementation in OpenAT
* Implemented: gate records `Assets/OpenAT/Sim/Airport/Gates.cs:16-59`; start gate
  `Assets/OpenAT/Sim/Fleet/OperationsSetup.cs:149`; planning and renumbering
  `Assets/OpenAT/Sim/Airport/GatePlanner.cs:19,113`; no-gate effects
  `Assets/OpenAT/Sim/Fleet/FlightOps.cs:450-456,539-548`; expansion
  `Assets/OpenAT/Sim/Hall/AirportExpansion.cs:49-111`.

---

## 5. Auction board (airport manager's office)

### 5.1 Postings
* Every night `n = min(day + 2, 7)` draws (2 at game start), seeded by date + start time.
* **Branches** (cities): per draw a 30 % chance, while fewer than 3 are posted, to post a random city
  that fewer than 3 airlines hold and that is not already posted. Start price = the city's monthly
  office rent; the slip's rank = current holders + 1. Unbid branch slips from earlier days can stay.
* **Gates:** every gate number of the hall (4 on day 0) that nobody rents is posted, up to 7, at
  start price `3000 − 100·number`.

### 5.2 Bidding
* Clicking a slip you do not lead raises its price by **10 %** (integer) and makes you the leader.
* You cannot bid on a branch city where you already have a branch.
* Within one visit a slip can be raised at most once by you (a second click restores the state on
  entry and bids again). Leave and re-enter to bid again after being outbid.
* With an informer you get a warning when a robot outbids you.

### 5.3 Resolution (overnight)
* The leader of each slip wins. **Branch:** rank as posted, monthly rent = final bid. **Gate:** first
  free gate record, monthly rent = final bid.
* The winner pays **3 × the final bid** once (ledgers 2040 / 2042), then the bid every month
  (charged /30 nightly).
* The local player receives one summary letter listing won and lost items they bid on.

### 5.4 Computer bidders
* **Gates:** robots bid only when their "ran out of gates" counter exceeds their plane count. The
  original never increments that counter, so **robots never bid on gates**. They live with apron
  departures and the automatic delay helper.
* **Branches:** when not saving for a plane (or early, or by chance), for each slip they do not lead
  and have no branch in: if led by someone, bid when sympathy to the leader < 40, or by chance
  (1 in 10, or 1 in 5 after day 10), or always for slot 0's airline; if unled, 1 in 3. Further
  conditions: yesterday's result > 10 × price, credit·2 < cash·3, cash > 0, and a condition on the
  (always empty) route slip that in practice passes when the robot dislikes nobody, for robot 0, or
  with the "abroad" behaviour.
* Robots visit the board on their errands; in the free game, after 16:00 a scheduling quirk sends
  them back to the board on almost every planning step until 18:00.

### Implementation in OpenAT
* Implemented: `Assets/OpenAT/Sim/Airport/AuctionBoard.cs:55-117` (postings), `:148-161` (bid),
  `:168-215` (resolution); command `Assets/OpenAT/Sim/Airport/AirportCommands.cs:13-38`; robots
  `Assets/OpenAT/Sim/AI/RobotExecutor.cs:380-416`; nightly order
  `Assets/OpenAT/Sim/Fleet/OperationsSystems.cs:24-26`. `GameOptions.FixOutOfGatesCounter`
  (`GameOptions.cs:44`, used at `FlightOps.cs:457`) lets robots bid on gates.

---

## 6. Branch offices and cooperation

* Every airline starts with its home city as a branch (rank 1, rent = the city's office rent,
  charged nightly like any branch).
* More branches **only** by auction (§5). A branch can be closed from the filofax (no refund); it can
  then only be regained by auction.
* **What a branch gives:** the phone order and freight lists of that city (§3.1) and the city pages
  in the laptop. It is **not** needed to license or fly routes and does **not** raise demand: the
  branch image field is never read.
* **Cooperation:** two airlines can agree to cooperate (human ↔ robot by talking in the hall; a robot
  accepts if its sympathy to you is > 0, and agreeing adds +30 sympathy; a robot may also phone you
  to propose it when its sympathy is > 50, or to end it when its sympathy falls below −20). Robots
  cooperate with each other from the start. While cooperating, each airline's
  branch list also shows the partner's branch cities, and orders can be taken there. **You pay no
  rent** for the partner's branches. There is no other cooperation effect (no shared routes, gates
  or passengers).

### Implementation in OpenAT
* Implemented: branch records `Assets/OpenAT/Sim/Airport/Gates.cs:62-70`; drop
  `Assets/OpenAT/Sim/Airport/AirportCommands.cs:41-55`; cooperation flags
  `Assets/OpenAT/Sim/Player.AI.cs:10-11`, robot defaults `Assets/OpenAT/Sim/AI/RobotBrain.cs:36`,
  talk result `Assets/OpenAT/Sim/Rivals/RivalTalkCommand.cs:37-40`, robot use of partner branches
  `Assets/OpenAT/Sim/AI/RobotExecutor.Orders.cs:54-66`.
* Implemented: humans take orders through a partner's branch (§3.6).

---

## 7. Home airport

* Chosen on the new-game screen; all four airlines share it. Default by language: Berlin (German
  and fallback), London (English), Paris (French), Rome (Italian), Madrid (Spanish/Portuguese).
* Effects:
  * The starting set of licensable routes is exactly the routes touching home (§2.1); everything
    else is unlocked outward through ≥ 20 % shares.
  * Travel-agency orders always involve home; Last Minute slots 2–3 involve the home region; start
    orders leave from home within 5000 km.
  * Gates, the hall and boarding only exist at home; no-gate penalties only apply there.
  * Demand of every home route scales with √(population of home), so a big home city lifts all
    home routes. There is no other per-city bonus (no time zones, no city-specific prices).
* Branch rent at home is the home city's office rent.

### Implementation in OpenAT
* Implemented: `Assets/OpenAT/Sim/World.Core.cs:149`; default by language `World.Core.cs`
  (`DefaultHome`: London, Paris, Rome, Madrid, Berlin for Dutch/German/others, as the original).

---

## 8. Mission switches (brief)
* Route mission (Normal): 6 mission cities, synthetic home→city routes (fee 6000) if missing, extra
  branch postings.
* Harder start modes (missions above Normal, except add-on 2): each airline starts with one home
  route and branches. Not in the free game.
* Add-on 5: no travel agency. Add-on 9: 5 manager orders per morning. Add-on 3: relief cargo to a
  disaster city. Flight Security 10: counters emptied on certain days.

---

## Guide claims

Claims from the third-party strategy guide (`docs/spec/local/faq/gamefaqs-11142-claims.md`) that touch
this topic.

| ID | Claim (paraphrase) | Verdict for Deluxe | OpenAT status |
|---|---|---|---|
| F7 | Freight office sells cargo orders. | **Confirmed.** Six shared crates, Base(105), tonnage bonus, pay on the last trip. | Implemented. |
| F10 | Last Minute orders are mostly same-day; travel agency orders are not. | **Confirmed.** Last Minute departs today (relaxed type: tomorrow), closes 16:00 and on Saturday; travel agency 1–3 days ahead or a window, closes 17:00. | Implemented (`ContractCommands.cs:11-17`). |
| F15 | Airport manager: expansion costs about 1 million; bid for branches and gates on the board. | **Confirmed**, exactly 1 000 000, with the refusal order of §4.1. Routes are not on the board in Deluxe. Winning costs 3 × bid once plus the bid monthly. | Implemented. |
| A5 | Route advisor says whether a route is good or bad. | **Confirmed**; presence only, talent irrelevant (§2.6). | Implemented (`RouteOps.Advice`). |
| A6 | Order analyst says which orders are worth taking. | **Confirmed**; presence only; rates against a notional Base(100) cost (§3.5). | Implemented. |
| T1 | Good home airports: Berlin, Cairo, London, Paris, Singapore, Sydney. | **Opinion.** No city bonus exists; big populations raise home-route demand via √pop. | n/a |
| T4 | A route must use ≥ 10 % of its capacity or the manager revokes it, and it can't be rented again. | **Differs.** The rule is ≥ 10 % of the route's daily **potential demand** carried (moving average, both directions credited). Revocation comes after **20 consecutive** low nights, with warnings on nights 10 and 15, per direction. It **can** be rented again after a **7-day** cooldown. | Implemented (`RouteOps.cs:174-204`). |
| T5 | Cooperating with a rival lets you use their branches without paying rent. | **Confirmed** for phone order/freight lists; no other benefit. | Implemented (`CounterHours.HasBranch`). |
| T6 | Bid for branches late (after 17:30). | **Partly true.** The leader at the night change wins, so late bids leave less time to be outbid. But robots keep visiting the board until 18:00 in the free game, and each re-entry allows a new +10 % bid. Robots never bid on gates. | Implemented as the original. |
| Routes | Rents $150k–$1.6M; advisor verdict by city size plus a low / very high demand remark. | **Mostly confirmed.** Shipped fees are 50 000–1 600 000 per month (the guide's sample lacks the cheapest), and the real charge is that fee for **each direction**. The guide's sample fees (e.g. 200 000, 300 000, 800 000, 1 000 000, 1 600 000) match our table. Verdict thresholds: population > 2 M; remark from factor vs level (§2.6). | Data loaded; advisor implemented. |
