# Cities, contracts, freight, routes, auctions, gates and branches

Clean-room behaviour spec, written in our own words from the reference sources
(`docs/spec/local/gog_src`, cross-checked with `docs/spec/local/ref_AirlineTycoon/src`), the decoded
data tables and the manual. Table formats are in `data-tables.md`. Text is referred to by
resource file + group + id. Confidence: **[H]** verified in code and data, **[M]** read in code but
not run, **[L]** inferred.

Glossary of time units:
* `Date` = day index since the game start (day 0 = the first day). Contract dates are day indices.
* `Hour` = 0–23. A game day starts at **09:00** (`Sim.cpp`, initial time).
* Internally `Time` counts 60 000 per game hour (1 000 per game minute).
* `Weekday`: 0 = Monday … 5 = Saturday, 6 = Sunday. It starts from the real-world weekday at
  game start.
* Money is an integer amount in the game currency (shown as $ or DM).
* RNG = the TEAKRAND LCG (data-tables.md §0.7). `Rand(n)` is in `[0,n)`.

---

## 1. Cities

### 1.1 Properties

These come from `city.csv` (data-tables.md §1.1): population, region code (1–4), two
three-letter codes (readable / real IATA, chosen by an option), globe and map coordinates in whole
degrees, monthly branch rent, description text pages, photos, voice sample, and an add-on
generation flag. There are 75 cities. No other per-city state exists globally, apart from:
* `HomeAirportId`: the shared home airport of all four players (chosen at new game).
* `MissionCities[6]`: used by the route mission (§3.1).
* `KrisenCity`: the disaster city of add-on mission 3 (§5.4).

**There are no time zones.** Every departure and arrival time is on the single game clock.
**[H]** (A search for timezone/local-time handling found nothing. Flight landing time is simply
the start time plus the duration.)

### 1.2 Distance (`City.Cpp: CITIES::CalcDistance`) **[H]**

Distance is great-circle on a sphere, computed from the integer globe coordinates:
1. For each city: λ = x°, φ = y°, converted to radians with π = 3.14159. The unit vector is
   (cos λ·cos φ, sin φ, sin λ·cos φ). In the original it is built as (cos λ, sin φ, sin λ), with x
   and z scaled by √(1−sin²φ). This is the same vector.
2. α = arccos(v1·v2 / |v1||v2|) in degrees, again with π = 3.14159.
3. distance_m = trunc(|α| · 40 040 174 / 360), the equatorial circumference in metres.
4. **Clamp the returned value to at most 16 440 000 m.** In the shipped data the longest pair is
   exactly at the cap.
5. Results are cached per (index1, index2) pair. A port can compute on demand. Use single-precision
   float arithmetic if bit-exact agreement matters: the original stores α as a float.

Route lengths in the shipped data run from 222 km to 16 440 km.

### 1.3 Flight duration (`City.Cpp: CITIES::CalcFlugdauer`) **[H]**

`hours = floor((floor(distance_m / speed_kmh) + 999) / 1000) + 1`, with a **minimum of 2**. This is
ceil(distance/speed) + 1 hour, where speed is the plane's cruise speed in km/h. A flight of 24 hours
or more is not allowed: a contract does not "fit" such a plane, and a flight-plan entry that long is
deleted with an advisor message (`std_ger.res` `Advi` 2310). Landing time = (start + hours) mod 24,
and the landing date moves to the next day when it wraps.

### 1.4 Random city choice (`City.Cpp: GetRandomUsedIndex`) **[H]**

To pick one city uniformly (optionally only cities with a given region code), draw
k = `Rand(n)+1` and walk the cities **from the highest index down**, returning the k-th that
matches. Keep this order for reproducible draws.

---

## 2. Passenger contracts ("Aufträge")

### 2.1 Data model (`class.h: CAuftrag`) **[H]**

| Field | Meaning |
|---|---|
| VonCity, NachCity | city ids (from, to) |
| Personen | passengers. All must fit in **one** plane, counting economy and first class. |
| Date, BisDate | first and last allowed **departure** day (inclusive). Date == BisDate means "exactly that day". Otherwise the UI shows "until BisDate". |
| Praemie | payment on successful completion. A value ≤ 0 inside a counter's offer list means "empty slot" (§2.5). |
| Strafe | contract penalty if not fulfilled. 0 means no penalty. |
| InPlan | 0 = not planned, 1 = planned, -1 = **done** (flown). |
| Okay | validity of the plan: 1 = fine, 0 = a planned flight violates the contract. |
| bUhrigFlight | flag for mission-9 orders from the airport manager (§2.9) |

The contracts a player holds are an album that starts with capacity 40. Whenever fewer than 3 slots
are free it grows by 10. **There is no limit on how many contracts a player can hold.** **[H]**

### 2.2 Base price (`Misc.cpp: CalculateFlightCost`, used throughout `Auftrag.cpp`) **[H]**

Every contract price starts from a reference fuel cost for a notional plane (8000 l/h, 700 km/h),
at today's global kerosene price `K` (§10):
```
barrels = ((distance_m / 1000) * 8000) / 160 / 700        (integer, left to right)
fuel    = max(1000, barrels * K)
Base(f) = ceil(fuel / 100) * f                              ( = ((fuel+99)/100)*f )
```
So contract prices follow distance **and the daily kerosene price**. Each source uses its own
factor f (table in §2.4). Example: 1000 km at K = 500 gives 71 barrels and fuel 35 500, so
Base(120) = 355·120 = 42 600.

### 2.3 The common generation template

Every generator below follows the same steps. Differences are listed per source in §2.4.

1. **Pick cities** (source-specific, §2.4). Repeat until From ≠ To. Where "AreaType 4" applies,
   also require distance ≤ 10 000 000 m.
2. **Set dates and size.** Personen = 180 by default. Date = today + 1 + `Rand(3)` (1–3 days ahead).
   BisDate = Date. Then with probability 1/5 (`Rand(5)==4`), BisDate += `Rand(5)`.
3. Praemie = Base(f). Strafe = Praemie / 2.
4. **Type roll** t = `Rand(100)`:

   | Type | Range (default) | Effect |
   |---|---|---|
   | A normal | 0–49 | none |
   | B "relaxed" | 50–59 | Date = today, BisDate = today + 4 + `Rand(3)` |
   | (gap) | 60–69 | none, same as A |
   | C tight | 70–79 | Praemie ×2, then Strafe = Praemie ×2, Date = BisDate = today + 1 |
   | D swindle | 80–94 | Praemie /2 (Strafe unchanged, so it now equals Praemie) |
   | E windfall | 95–99 | Praemie ×2, Strafe = 0 |

5. **Size roll** s = `Rand(100)`, checked in this order:

   | Condition | Personen | Praemie |
   |---|---|---|
   | s == 0 | 2 (1 at Last Minute) | ×4, then Strafe = Praemie ×4 |
   | s < 15, or (today < 4 and s < 30), or (today < 8 and s < 20), or (tutorial and s < 70) | 90 | ×3/4 |
   | s < 40, or tutorial | 180 | ×1 |
   | s < 70 | 280 | ×5/4 |
   | s < 90 | 340 | ×6/4 |
   | else | 430 | ×7/4 |

6. **Area multiplier.** AreaType 1 gives Praemie ×3/2. AreaType 2 gives ×8/5. Types 0 and 4 have
   none.
7. **Window discount.** If Date ≠ BisDate, then Date = today (the window opens immediately) and
   Praemie ×4/5.
8. **Feasibility retry.** If Personen > `PlayerMaxPassagiere` or distance > `PlayerMaxLength`, jump
   back to step 1 and redo everything, at most 100 times. After that the contract is kept even
   though nobody can fly it.

All arithmetic is integer, applied in the order above (×a/b means multiply first, then divide).

`PlayerMaxPassagiere` / `PlayerMaxLength` (`Auftrag.cpp: CalcPlayerMaximums`): these are recomputed
at most once per game hour, or when forced. Starting values are 90 passengers and 2000 km. They are
raised by every plane of every **human** player (local or network). Passengers become the plane
type's seat count, and length becomes the plane's range. Length is then converted to metres. The
original tests "configured economy + first-class seats > current max" but stores the *type* seat
count, a small quirk to keep. There are also `PlayerMinPassagiere` / `PlayerMinLength` (smallest
human plane), used only by the start contracts.

### 2.4 Sources and their variants (`Auftrag.cpp`) **[H]**

Each counter or branch keeps a list of **6 offer slots**. Slot c uses AreaType `c/2`: slots 0–1
use 0, slots 2–3 use 1, slots 4–5 use 2. Two counters replace that early in the game: if
today < 5 and c < 5, or today < 10 and c < 3, the slot uses **AreaType 4** instead. (This applies to
the travel agency and the branches, not to the Last Minute counter.)

| Source | Generator | f | City choice | Differences from the template |
|---|---|---|---|---|
| **Travel agency** (Reisebüro, home airport) | `RefillForAusland` with the home city | 120 | Two uniformly random cities, then one of them (coin flip) is **replaced by the home city**. AreaType affects only the 10 000 km cap (type 4) and the price multipliers. | Type ranges: B = 50–64, C = 65–79 (no gap). City and type draws come from a private RNG seeded from one draw of the list RNG. |
| **Branch office** in city X (phone call) | `RefillForAusland` with X | 120 | as above, with X in place of the home city | as above |
| **Last Minute** counter (home airport) | `RefillForLastMinute` | 130, then ×5/4 | AreaType 0: a random region 1–4, both cities in it (**the home city may not be involved**). AreaType 1: both cities in the home region, then one becomes the home city. AreaType 2: any two cities. | Date = BisDate = **today**. No 1/5 window extension, no window discount. Types: A 0–49 none, B 50–59 Date = BisDate = **tomorrow**, C 60–79 Praemie ×2 and Strafe = Praemie ×**4**, D 80–94, E 95–99. Size s==0 gives 1 passenger. |
| Start-of-game contracts | `RefillForBegin` (AreaType 0) | 100 | From = home. To is random (AreaType 0 picking) until ≠ home and ≤ 5000 km. | Personen fixed at 90. Date = BisDate = today. Types: B 50–59 BisDate = today+6, C **70–94** ×2 / Strafe ×2 / BisDate = today+1, E 95–99. No D, no size roll. Retry: give up on max-fit after 80 tries and on min-fit (smallest human plane) after 60. If still infeasible: Strafe 0, Praemie ×2, window today … today+5. |
| Airport manager orders (add-on mission 9) | `RefillForUhrig` | 115 | same picking as Last Minute | Strafe = Praemie (twice the usual). Otherwise the template with the gap at 60–69. |
| (Dead code) | `RefillForReisebuero` | 105 | — | Not called anywhere. The travel agency uses `RefillForAusland`. Ignore. |

Special rule for Flight Security mission 10 (`DIFF_ATFS10`): between days 20 and 30 every Last
Minute and travel agency offer has Praemie forced to 0, so the counters stay empty.

### 2.5 Offer pools, refilling and seeding **[H]**

Pools are **global and shared by all four players**: first come, first served.
* `LastMinuteAuftraege` (6), `ReisebueroAuftraege` (6).
* `AuslandsAuftraege[city]` (6 per city), for branch phone calls.
* `gFrachten` (6) and `AuslandsFrachten[city]` (6 per city) for freight (§5).

A slot is *empty* when Praemie ≤ 0 (passenger lists) or < 0 (`gFrachten`).

**Daily full refill** (`Sim.cpp: SIM::NewDay`, also at game start): each pool is reseeded and all
6 slots are regenerated.

| Pool | Seed |
|---|---|
| freight | Date |
| Last Minute | Date + 1 |
| travel agency | Date + 2 |
| branch pool of city index c (passenger and freight) | Date + c + 3 |

At game start in single-player, the three home-airport pools are reseeded from the wall clock
instead.

**Trickle refill during the day.** Each pool has a tick counter:
* `TickLastMinuteRefill`, `TickReisebueroRefill`, `TickFrachtRefill`, and per city
  `AuslandsRefill[c]` / `AuslandsFRefill[c]`.
* The counters are set to 6 at each full refill and increase by 1 **every 5 game minutes**.
* A refill happens when a player **enters or leaves** the counter's room, or opens a branch's offer
  list:
  1. Up to `min(6, ticks)` empty slots are regenerated, scanning slot 0 → 5.
  2. Optional "minimum" mode (used by the AI with minimum 3): if the tick budget was not used up,
     count the filled slots, then fill more empty slots until at least `minimum` are filled.
  3. The counter is reset to 0.
* Net effect: about **one new offer per 5 game minutes since the last visit**, capped at a full board.

In network games these refills are broadcast so all machines stay in step (`Sim.NetRefill`).

### 2.6 Opening hours and access **[H]**

* Last Minute counter: closes at **16:00**, and is **closed all day on Saturday** (Weekday 5).
  (`AirportView.cpp` times, `Sim.cpp` condition blocks.)
* Travel agency: closes at **17:00**. A player inside is sent out at closing time. Background
  pedestrians do not go there on Sundays.
* Branch offers: the player phones a city where they **own a branch** (or where a cooperating
  player owns one) from the filofax branch list (`Stdraum.cpp: MENU_BRANCHLIST`). The home city
  is excluded from that list.
  * The first call to a city plays a short greeting dialogue (`dlg_ger.res` group `Welt`,
    1000–4003) and sets a per-player "already called" flag (`CalledCities`). Later calls open the
    offer list directly.
  * Calls are billed per time unit, 1 currency unit per tick, with a tick length that depends on
    mobile vs desk phone and on distance (`Stdraum.cpp`, call-cost block). This is minor; see the
    phone spec.
  * The offer list shows 6 passenger slots and 6 freight slots.
* Add-on mission 5 is the "no travel agency" mission. The counter simply is not in that airport
  layout.

### 2.7 Accepting **[H]**

* Clicking a slip (or a list line) accepts it **immediately and irrevocably**. There is no
  confirmation, cost or limit.
* The contract is copied into the player's album and the slot becomes empty. At the counters the
  slip flies away: Praemie is set to -1000 and animated back up to 0. At the branch list it is set
  to 0 at once.
* A per-day statistic is incremented. At the counters, Last Minute and travel agency are counted
  separately.
* Hovering a slip triggers the **contract advisor** (if the player employs one,
  `Auftrag.cpp: CheckAuftragsBerater`). In order:
  1. If an existing future *ferry* flight (an automatic positioning flight) matches the
     route/date window and has enough seats, the advisor says the order can replace it
     (`Advi` 7013).
  2. Else, if no plane has the range and < 24 h duration, they say so (7010).
  3. Else, if no plane has enough seats, 7011.
  4. Else, if no plane has both, 7012.
  5. Else they rate profit. With cost = Base(100):
     * Strafe = 0 → 7000
     * cost ≤ 0.5·Praemie → 7001 (great)
     * ≤ 0.9·Praemie → 7002
     * ≤ 1.4·Praemie → 7003
     * otherwise → 7004 (bad)

### 2.8 Planning, validation and execution **[H]**

* A contract is put into a plane's flight plan. "Fits in plane" (`CAuftrag::FitsInPlane`) means
  distance ≤ plane range (km·1000) and duration < 24 h.
* Validation runs after every plan change (`Player.cpp: UpdateAuftragsUsage`):
  1. All held contracts are reset to Okay = 1, and to InPlan = 0 unless already done.
  2. Each flight-plan entry of type "contract" is checked:
     * The **first** suitable entry marks the contract InPlan = 1, and that entry becomes "ok".
       Suitable means seats (type seat count) ≥ Personen and departure ≤ BisDate, or the entry is
       still in the future.
     * If seats < Personen, the entry is marked "too small" and the contract Okay = 0.
     * If the departure day is outside [Date, BisDate], the entry is marked "wrong day" and the
       contract Okay = 0.
  3. Only human players' contracts are validated.
* **Execution happens at take-off** (`Schedule.cpp: CFlugplanEintrag::BookFlight`, called when the
  plane departs):
  * Economy load = min(economy seats, Personen). First class takes the rest.
  * If the entry is ok, income = **Praemie**, booked as contract income.
  * If the entry is *not* ok, there is **no income**, but fuel and costs are still paid.
  * Entry state "ok" or "wrong day" marks the contract **done** (InPlan = -1). So flying on a wrong
    day uses up the contract without pay and also avoids the penalty. This quirk follows from the
    code. **[M]**
  * Entry state "too small" leaves it open and shows an advisor message (`Advi` 2313).
  * Passengers are counted in statistics.
  * Image effects apply as for route flights (§3.7, the parts that do not depend on price).

### 2.9 Deadline, penalty and clean-up (`Player.cpp: PLAYER::UpdateAuftraege`, run in each player's new-day step) **[H]**

* On the morning after BisDate (that is, `BisDate == today-1`):
  * If the contract is not done, the player is human (local or network), and it is not
    "planned and valid":
    * If Strafe > 0, the player pays **Strafe**. It is booked as a penalty, it reduces the "profit"
      mission counter, and a letter is sent (`Lett` 1000–1002).
    * If Strafe = 0, a no-penalty letter is sent (1010–1012).
  * **Computer players never pay contract penalties.**
* Two days after BisDate, the contract is removed. Any future flight-plan entries that use it are
  deleted first (the plane's start city is fixed up). If an entry is already in progress (starting
  within the next 2 hours today), removal waits for a later day.
* **Voluntary cancel** (filofax, `Stdraum.cpp: MENU_REQUEST_KILLAUFTRAG`, prompt `Misc` 3017/3018):
  the player pays the full Strafe at once and the contract is removed.

### 2.10 Special contract-like entries **[H]**

* **Start contracts.** At a new game, when the "travel holding" feature is enabled (every mode
  except add-on mission 6) and not in add-on mission 9, **every player** gets 2 contracts from
  `RefillForBegin`.
* **Add-on mission 9.** Each morning the airport manager briefing gives every human player 5 orders
  from `RefillForUhrig` (`Aufsicht.cpp`, `Player.cpp: Add5UhrigFlights`). These orders count toward
  the mission. The `bUhrigFlight` flag is meant to mark them, but the generator never sets it (open
  question §11).
* **Sabotage "spare part" order.** When a sabotaged plane is stranded abroad, a zero-passenger,
  zero-pay, zero-penalty contract from the home airport to that city is added. Its window runs from
  today until roughly the repair delay (`Sim.cpp`, plane-problem block). It exists to let the
  player schedule the ferry.

---

## 3. Routes

### 3.1 Data (`class.h: CRoute`, `CRentRoute`) **[H]**

Global route table, shared by everyone: 350 directed routes from `routen.csv`. Fields: level
(1 short / 2 long), from, to, **monthly licence fee** `Miete`, popularity `Faktor`, and current
**demand pool** `Bedarf` (persons waiting today).

Per player, one `CRentRoute` per directed route:

| Field | Meaning |
|---|---|
| Rang | 0 = not licensed. 1–3 = licence rank (order of licensing; at most 3 airlines per route) |
| Miete | monthly fee this player pays (the table fee, or the winning auction bid) |
| Ticketpreis, TicketpreisFC | ticket prices (economy, first class) |
| Image | 0–100 route image |
| Auslastung, AuslastungFC | moving average of seat load % on this route's flights |
| RoutenAuslastung | moving average of the % of route demand carried ("capacity share") |
| HeuteBefoerdert | passengers carried today (for the daily update) |
| LastFlown | days since last flight (capped 99) |
| AvgFlown | intended "average days between flights". Set to 30 when licensed and **never updated** (§11). |
| TageMitGering | days in a row with share < 10 %. For unlicensed routes: days since the licence was lost, used as a cooldown (99 = none). |
| TageMitVerlust | AI-only "days at a loss" counter |

**Mission routes** (route mission, difficulty 3 "normal"): 6 target cities are chosen from a
default list (looked up by localised name). Any that equal the home city or lie more than
12 000 km away are swapped for the largest-population cities that fit. If a home→target route
does not exist, a synthetic pair is added: level 1, fee 6000, factor 1 (`Sim.cpp: CreateMissionCities`).

### 3.2 Demand model (`Route.Cpp: CRoute::AnzPassagiere`, `CRouten::NewDay`) **[H]**

Potential daily passengers:
```
DayFaktor = 1000 if Date<3; 900 if Date<9; 750 if Date<29; else 650
AnzPassagiere = trunc( sqrt(PopFrom * PopTo) * Faktor / DayFaktor / 2 * 3/4 )   (floating point)
```
Demand therefore grows during the first month, as DayFaktor falls.

Demand pool `Bedarf`, updated once per new day, after the players' own day updates:
* day 0: Bedarf = trunc(AnzPassagiere · 4.27)
* later: Bedarf = (6·Bedarf + trunc(AnzPassagiere · 4.27)) / 7 (integer), an exponential pull toward
  4.27× the potential.

Every route flight removes its **economy** passengers from that direction's pool (floored at 0).
First class is not subtracted. The route office shows AnzPassagiere as "daily demand" and Bedarf as
"current demand" (`std_ger.res` `Rout` 998/999).

### 3.3 Licensing at the route office (RouteBox) (`RouteBox.cpp`, `Stdraum.cpp: MENU_REQUEST_RENTROUTE`, `Player.cpp: PLAYER::RentRoute`) **[H]**

The route office is the pin board next to the airport manager's office.

**Eligibility** ("buyable"), computed for each directed route d that the player has not licensed:
1. d touches the home airport, **or**
2. d shares an endpoint with some route on which this player has RoutenAuslastung ≥ 20 % (in
   practice only the player's own routes, since unlicensed ones have 0), **and**
3. d is **not** in cooldown: unlicensed with TageMitGering < 7 means not eligible. That happens for
   7 days after the licence was taken away (§3.5). A voluntary drop sets the counter to 99, so no
   cooldown follows.

**Rank.** The new rank is 1 + the highest rank any active player holds on that route. If that
exceeds 3, the request is refused (`MENU_REQUEST_NORENTROUTE1`). Other refusals: not eligible
(NORENTROUTE2), and in cooldown (NORENTROUTE4).

**Effect of licensing:**
* **No up-front payment.**
* Both directions A→B and B→A get rank = the computed rank, Miete = the table fee, Image 0,
  load 0, LastFlown = AvgFlown = 30, TageMitGering = 0, TageMitVerlust 0.
* Default ticket price = `RefPrice*2` rounded down to a multiple of 10 (§3.6). First class = 2× that.
* The plain rent helper assigns rank "1 + number of players holding it". The route office overwrites
  it with the max-based rank above. AI licensing uses the same max-based rank.

**Fees:**
* Every new day the player pays **Miete/30 for every licensed directed route**
  (`Player.cpp: BookBuroRent`).
* Licensing sets the fee on **both** directions, so the real daily charge is 2·Miete/30 per city
  pair, while the UI quotes Miete/30 per day and Miete per month (`Misc` 3014). Reproduce this
  (or decide deliberately; §11).

### 3.4 Dropping a route **[H]**

* Done from the filofax or notebook route page, or at the route office.
* Refused while any flight-plan entry of the player still uses either direction
  (`MENU_REQUEST_NORENTROUTE3`).
* On confirm, both directions go to rank 0 with TageMitGering = 99 (so no cooldown). Every other
  player whose rank on that direction is higher than the dropped rank **moves up by one**.

### 3.5 Losing a licence for low use (`Player.cpp: PLAYER::NewDay`, `RouteWegnehmen`) **[H]**

Each new day, for each licensed directed route:
1. LastFlown += 1 (cap 99).
2. RoutenAuslastung = (4·RoutenAuslastung + HeuteBefoerdert·100 / AnzPassagiere) / 5 (integer),
   capped at 100. Then HeuteBefoerdert = 0.
3. If RoutenAuslastung < 10:
   * TageMitGering += 1.
   * For human players, and only for the direction whose from-id < to-id (so one letter per pair):
     a warning letter on day 10 and day 15 (`Lett` 3000–3002, the text includes the current share
     and the days left), and a loss letter on day 20 (3100–3102).
   * On day 20 the licence of **that direction** is removed: rank 0, cooldown counter 0.
     Higher-ranked holders move up, and all future flight-plan entries on it are deleted.
     (Both directions usually cross the threshold on the same day, because HeuteBefoerdert is
     credited to both directions, see §3.7.)
4. Otherwise TageMitGering = 0.

For unlicensed routes, TageMitGering counts up to 99 (the cooldown clock).

The manual summarises this as: carry at least 10 % of demand or lose the route, and carry at least
20 % to expand from its far end.

### 3.6 Ticket prices **[H]**

Reference price per route:
```
RefPrice = ((CalculateFlightCost(from, to, 800 l/h, 800 km/h, no player) * 3) / 180) * 2
```
* The fuel cost uses the same formula as §2.2 with consumption 800 and speed 800: barrels =
  ((dist/1000)·800)/160/800, fuel = max(1000, barrels·K). So RefPrice ≈ fuel/30 and **follows the
  kerosene price**.
* Default on licensing is 2·RefPrice (rounded down to ×10). First class default = 2× economy.
* Player UI (filofax / notebook "route" block, `Planer.cpp`):
  * Presets: ½·, 1·, 2·, 4·RefPrice (each rounded to ×10). First class is set to 2× the economy
    preset.
  * ±10 buttons for economy and for first class.
  * Clamp economy to [0, 16·RefPrice] and first class to [0, 48·RefPrice].
  * The **economy price is copied to the reverse direction**. First class is not copied.
  * With an informer (info advisor), the player can copy a competitor's prices for that route.
* Each flight-plan entry **snapshots** the route's prices when the route is placed in the plan, or
  when the weekly plan is extended. Later price changes reach existing entries through the price
  update helper (`PLAYER::UpdateTicketpreise`). **[M]** (Exactly which entries get re-priced is in
  the flight-plan spec.)

### 3.7 Passengers per route flight (`Schedule.cpp: CFlugplanEintrag::CalcPassengers`) **[H]**

The count is recalculated **whenever the entry is created or changed**, but only while the
departure is more than about 1 hour away (start date in the future, or today with hour+1 < start
hour). After that it is frozen. At take-off (`BookFlight`) the frozen numbers are used.
`HoursBefore` = hours between the last change and the departure, stored in a byte, and set to 48 for
weekly auto-extended entries.

**Economy.** P = the price on *this* flight entry. Let i be the planning player.

1. **Weights** for each of the 4 players c:
   * If c holds the route: w = 10000/P (100 000 if P = 0).
     * Multiply by 0.9 if P > 3·RefPrice, again by 0.9 if P > 5·RefPrice, and by 0.8 if
       P > 6·RefPrice. Minimum 1.
     * Rank 1 gives ×1.5, rank 2 gives ×1.2.
     * If AvgFlown > 4: w = w·10/(10+min(AvgFlown−3, 10)), minimum 1. Because AvgFlown is always 30,
       this halves w for every holder, so it cancels in the share.
     * Computer players: w += w/4.
   * If c does not hold the route: w = 0, except that c = i gets 1.
   * **Quirk [H]:** every holder's weight uses the *planning flight's* price P, not that holder's own
     price. So price **does not shift market share** between airlines. Only rank and AI/human
     status do. Price acts through the absolute penalties below.
2. **Demand share:** n = Bedarf · w_i / Σw.
3. Cap at 1.5 × economy seats.
4. **Short notice:** if HoursBefore < 48, n = n·(HoursBefore+48)/96.
5. **Overpricing:** if P > 3·RefPrice and 3·RefPrice > 10, n = n·(3·RefPrice − 10)/P.
6. **Night penalty:** if start hour < 5 or > 22, n = n·5/6. Apply the same again for the landing hour.
7. **Image:** I = clamp(4·RouteImage + PlayerImage + 200, 0, 1000). Then n = n·(400+I)/1100,
   which ranges from 0.36× to 1.27×. The value is truncated to 16 bits.
8. **Noise:** ×10/11 for each threshold the plane's noise exceeds: 60, 80, 100, 110.
9. Final value: min(n, Bedarf·w_i/Σw, economy seats).
10. If the player is under a "press statement" penalty (sabotage aftermath), the result becomes
    1 + (n mod 3).

**First class.** Same steps with these changes:
* The price thresholds for the weights are 9×, 15× and 18× RefPrice.
* Rank 1 gives ×1.7.
* The cap is 1.5 × first-class seats.
* Short notice: if HoursBefore < 72, n = n·(HoursBefore+48)/120.
* Overpricing only when P > 9·RefPrice, using factor (3·RefPrice−10)/P.
* **Luxury gate:** L = the sum of the plane's 8 equipment levels (seats, food, trays, decor, engine,
  tyres, electronics, safety; each 0–2) + 2 security-upgrade flags (bits 10 and 11 of the player's
  security flags), giving 0–18. If L < 6, n = 0. Otherwise n = n·(L−6)/8, which can reach 1.5× at
  L = 18.
* Noise thresholds 40, 60, 80, 100.
* **Original bug:** the first-class branch uses the **economy** price P throughout. The SDL port
  fixed this to use the first-class price. **Recommendation:** use the first-class price, and offer
  "original" as a compatibility toggle.

**Contract flights:** economy = min(economy seats, Personen), first class = the rest (§2.8).

**Income** at take-off:
* route flight: Ticketpreis·economy + TicketpreisFC·first class
* contract: Praemie
* ferry flight (automatic positioning, type 3): **type seats · distance_km / 40**. This is an odd
  but real income for empty flights. **[H]**
* Costs: every flight pays fuel (using the player's fuel type and tank stock). Route and contract
  flights also pay **food cost per economy passenger** at the plane's catering level
  (`FoodCosts[]`).
* Switching a plane between seats and cargo (§5) costs 15 000.
* Computer players get a hidden bonus. On each flight, up to |profit|/10 is moved from their bonus
  pool into the result.

### 3.8 Per-flight bookkeeping on routes (`BookFlight`) **[H]**

* HeuteBefoerdert: the flown direction gets + economy. The **reverse direction** gets + economy +
  first class. Both directions therefore count each flight. The asymmetry is original; keep it.
* Route seat load: Auslastung = economy·100/seats the first time, then (3·old + new)/4. First class
  works the same way.
* LastFlown = 0.
* Demand: Bedarf of the flown direction −= economy passengers (floored at 0).
* Route image: +1 (max 100).
* The first route flight by any airline triggers a newspaper headline. The text varies with how many
  airlines flew routes before (`Misc` 2010+n).
* When a plane leaves the home airport with no gate, the player's image −2 (§7).

### 3.9 Image effects of every route or contract flight **[H]**

* A "brochure" sabotage can redirect the player-image changes to another airline.
* For humans only:
  * If the cabin crew quality is < 50 and the object id is divisible by 10, player image −1. If the
    quality is > 90 (same id test), +1.
  * Route image −1 if > 0. Together with the +1 above, this makes the per-flight route image change
    0 for humans and +1 for computers.
* Add points:
  * condition < 20 → −50; < 60 → −10 (these add up); > 98 → +10
  * each of seats, food, trays, decor: level 0 → −1, level 2 → +2
  * humans: + 3·crew/required_crew (integer)
  * routes only, with price P and C = 3·RefPrice (×0.9 for computers):
    * P < C/2 → +10
    * P > C → −10
    * P > 1.5·C → another −10
    * P > 2·C → another −10
* Player image += Add/10 (integer division, truncating toward zero), clamped to [−1000, 1000].
* Route image += Add/10, but **only if the result would be > 0**. Then clamp to 0–100.
* Advertising raises route image separately (ads spec). The AI spends ads on routes whose image
  is < 70–100.

### 3.10 Competition summary

* At most 3 airlines per directed route. Rank is the licensing order and moves up when a
  higher-ranked holder leaves.
* All holders draw from the same Bedarf pool. Whoever flies first (and plans earlier) depletes it,
  because each departing flight subtracts its economy passengers.
* The share weights favour rank 1 (×1.5), rank 2 (×1.2) and computer airlines (+25 %).
* Price affects load through the over-pricing cut-offs and the image changes, not through relative
  price (§3.7 quirk).
* AI pricing (`Player.cpp`, end of `RobotPlanRoutes`): base 2·(3/180·fuel)·3 ≈ 2·RefPrice.
  * With more than one active holder: it is capped at 2× each holder's price, and it undercuts
    disliked rivals (sympathy < −40) by 10.
  * Floors: ½·RefPrice; RefPrice when money < 100 000; 2·RefPrice when money < 0.
  * Because of an original comma-operator bug, the AI's loss-days counter rises every day whenever
    it has competition, and after more than 10 days the AI drops the route (unless the mode forbids
    it). This is AI-spec material.

### 3.11 Route office UI facts (`RouteBox.cpp`)

The list shows 22 routes per page.
* Filters: all routes / routes this player can license / this player's routes.
* Up to 4 player overlays on the map.
* The tooltip shows: distance, daily demand (AnzPassagiere), current demand (Bedarf), the fee
  (own Miete if licensed, else the table fee), own share %, and the holders in rank order. Other
  holders' shares are shown only with an informer.
* The route advisor comments:
  * big cities: both endpoints over 2 M population → `Advi` 8000, one endpoint → 8001, neither → 8002
  * Faktor > level + 0.5 → "popular" 8010
  * Faktor < level − 0.3 → "unpopular" 8011
  * cooldown → 8020

---

## 4. Last Minute vs travel agency at a glance

| | Travel agency | Last Minute |
|---|---|---|
| Base factor | 120 | 130 × 5/4 = 162.5 |
| Home city involved | always | only in slots 2–3 |
| Departure | 1–3 days ahead, or a window from today | today (or tomorrow for type B) |
| Penalty for type C | 2× (doubled) prize | 4× (doubled) prize |
| Early-game nearby slots | yes (AreaType 4 for the first days) | no |
| Hours | until 17:00, every day | until 16:00, closed Saturday |

---

## 5. Freight contracts ("Fracht", added by the add-on) (`Fracht.cpp`)

### 5.1 Data **[H]**

These are like passenger contracts, but with Tons (total), TonsLeft (still to fly) and TonsOpen
(neither flown nor planned) instead of Personen. Empty slot marker in `gFrachten`: Praemie = −1.
A Praemie of 0 is a valid free relief cargo.

### 5.2 Generation **[H]**

* **Cargo office** (`gFrachten`), `CFracht::Refill`, f = 105. City choice is the Last Minute
  picking (§2.4: AreaType 0 any region pair, 1 home-region with home, 2 anywhere, 4 like 1 but
  ≤ 10 000 km). The daily full fill uses `c/2`. The trickle refill also uses the early-game
  AreaType 4 rule.
* **Branch freight** (`AuslandsFrachten[city]`), `CFracht::RefillForAusland`, f = 105. City choice
  as the travel agency: two random cities, one replaced by the branch city.
* Template differences from §2.3:
  * Tons default 30.
  * Types: B 50–59, gap 60–69, C 70–79.
  * Size roll:

    | Condition | Tons | Praemie |
    |---|---|---|
    | s == 0 | 1 | ×4, Strafe = ×4 |
    | (same small-size conditions as §2.3) | 15 | ×3/4 |
    | s < 40 or tutorial | 30 | ×1 |
    | s < 70 | 40 | ×5/4 |
    | s < 90 | 60 | ×6/4 |
    | else | 100 | ×7/4 |

  * After the area and window adjustments comes a **tonnage bonus**: Tons > 80 → Praemie ×5,
    > 60 → ×4, > 30 → ×3.
  * Feasibility retry checks **distance only**.
* **Start freight** (`RefillForBegin`): f = 130, 30 t, from home ≤ 5000 km, same rules as the
  passenger start contracts. Not called at new game in the shipped code: only passenger start
  contracts are handed out. **[M]**
* **Add-on mission 3** (relief flights, `DIFF_ADDON03`): in about 74 % of draws
  (`Rand(100) > 25`) the destination is forced to `KrisenCity`. Such cargo pays Praemie = 0
  (humanitarian) and has half the penalty.
* **Flight Security mission 10:** days 25–35, every cargo office slot is emptied (−1).

### 5.3 Accepting and flying **[H]**

* Accepting works as in §2.7. At the cargo office the crate is lifted away by the crane animation.
* A freight flight carries **plane-type seats / 10 tonnes**, using the type value, not the
  configured seats. Planning reduces TonsOpen. An entry that ends up with 0 t gets a warning.
* At take-off: TonsLeft −= payload. **Praemie is paid only on the flight that brings TonsLeft to
  0** (`BookFlight`). Statistics count tons delivered and free (0-prize) tons.
* The first freight flight converts the plane to "no seats". Switching back for a passenger flight
  costs **15 000** each time (any change of the seat/cargo state).
* Freight flights are not validated by `UpdateAuftragsUsage`. They have their own
  `UpdateFrachtauftragsUsage`, which marks a wrong day and reallocates tonnage over the planned
  flights in order.
* Penalty and clean-up (`UpdateAuftraege`): same as passenger contracts, using letters `Lett`
  1005–1007 and 1015–1017. Cargo entries are removed once BisDate ≤ today−2. Computer players do not
  pay.
* The advisor estimates the number of flights as Tons/22 and scales the cost by that, then gives the
  same rating thresholds as §2.7 (texts 7100–7113; 7105 for free cargo).

### 5.4 Cargo office hours

No closing-time condition was found in the airport condition blocks. **Open question:** check in
game.

---

## 6. Auction board (Tafel) in the airport manager's office (`Tafel.cpp`, `Sim.cpp: SIM::NewDay`)

The board has 7 slots each for routes, branches (cities) and gates. **In Deluxe the route slots are
never filled**: the generator posts only cities and gates, and routes are licensed at the route
office. The route-slot code paths still exist.

### 6.1 New postings (`CTafelData::Randomize(n)`) **[H]**

This runs at game start with n = 2, and at every new day with n = min(Date+2, 7). It uses an RNG
seeded with `Date + game start time`. All bidders are cleared.

* **Cities** (only when the travel-holding feature is on). For each of n draws (at most 10): with
  probability 30 %, and while fewer than 3 cities are posted, try up to 5 random cities. Reject a
  city that 3 players already hold or that is already posted. A success posts it with start price =
  the city's **monthly rent** and rank = holders + 1.
  * Route mission only (difficulty 3), while fewer than 4 are posted, a second attempt runs per draw
    with up to 10 tries, which also skips the home and mission cities. **Original bug:** a success
    found on tries 6–10 is discarded, because the success test still checks for "fewer than 5 tries".
* **Gates.** Gate numbers 0…(number of gate runes in the hall − 1), with 4 on day 0, that **no player
  rents** are posted, up to 7, at start price **3000 − 100·gate number**.

### 6.2 Bidding **[H]**

* Clicking a slip that someone else is leading raises its price by 10 % (integer) and makes the
  clicker the leading bidder.
* Branches can be bid on only if the player does not already have a branch in that city.
* Undo: within one visit, clicking a slip whose leader changed since the player entered the room
  first restores it to its state on entry, then applies the bid again. So repeated clicks during one
  visit raise the price at most once.
* Computer players bid during their "visit airport manager" action, using the same +10 % rule.
  * Gates: when they ran out of gates more often than they have planes.
  * Branches: depending on sympathy toward the leader, a random factor, money and yesterday's
    balance (> 10× the price), and similar conditions.
* An informer warns the human when outbid (`Advi` 9001/9002).
* A postcard collectible item may cover part of the board until it is taken. This is cosmetic and
  mission-dependent.

### 6.3 Resolution (each new day, "after 18:00" per the manual) **[H]**

For every slip with a leader:
* **City:** the leader gets a branch there, with rank = the slip's rank, Image 0, and monthly rent =
  the final bid. They pay **3 × the final bid** once (ledger 2040).
* **Gate:** the leader's first free gate record gets this gate number and monthly rent = the final
  bid. They pay **3 × the bid** once (ledger 2042).
* The local player gets one letter listing what they won and what others won, but only for slips the
  local player bid on (`Misc` 2600–2612).
* Slips are then cleared, gate numbers are re-packed (§7), and new postings are made.

---

## 7. Gates at the home airport (`Schedule.cpp: CGates`, `Player.cpp: PlanGates, RentGate`, `Sim.cpp: ReformGates`) **[H]**

* Each player has up to 30 gate records. At new game each player rents one gate (number = player
  index, rent 1000/month).
* More gates come only from the auction board.
* Daily rent: Miete/30 per gate (ledger 2050).
* **Re-packing:** after every auction resolution, all rented gates of all players are renumbered
  0, 1, 2, … in player order.
* **Gate planning** (re-run after any plan change). It uses an hourly occupancy table for the next
  7 days. Only route and contract flights from or to the home airport take part.
  * A departure needs a free gate in its departure hour **and** the hour before. It occupies both.
  * An arrival occupies the hour before its landing hour.
  * A flight that finds no free gate gets "no gate" (−1), and the overlapping flights are flagged with
    a warning.
* Effects of "no gate":
  * the plane boards from the apron
  * the player's image −2 at each such departure from home
  * an advisor message (`Advi` 2353)
* Ferry and cargo flights never use gates.

---

## 8. Branch offices ("Niederlassungen") and expanding to new cities

Per player, one `CRentCity` record per city: rank (0 = none, 1–3), Image (unused; see below) and
monthly rent.

* **Home city:** every player starts with rank 1 there, rent = the city's BuroRent, paid like any
  other branch.
* **Getting a branch:** only through the auction board (§6). In the harder start modes (difficulty
  above "normal", except add-on mission 2), each player also starts with:
  * one route from the home airport: player c takes the (c+1)-th home route, scanning the route
    table from the end. The level-1 preference in this code has no effect, because a loop counter
    is already > 0.
  * With travel holding on, branches at that route's non-home endpoint.
  * One more branch among the 4 nearest cities that are more than 1000 km from home (`Sim.cpp`,
    player init part II).
* **Daily rent:** Miete/30 per branch (ledger 2051).
* **Dropping** a branch (filofax, prompt `Misc` 3013): rank 0. It can only be regained through an
  auction.
* **What a branch gives:**
  1. Access to that city's foreign contract and freight lists by phone (§2.6). Cooperating players'
     branches count too.
  2. City detail pages in the laptop.
  3. The manual says branches raise awareness and so passenger numbers. **No such effect was found
     in the code:** the branch Image field is saved and synchronised but never read by the passenger
     formula. **[M]**
* **Expanding the route network** happens through route eligibility (§3.3): carry ≥ 20 % of
  demand on a route to unlock routes touching its far end. A branch is **not** required to license
  or fly a route.
* Takeover of a bankrupt competitor (`Dialog.cpp`) transfers branches, gates and routes. This
  belongs in the takeover spec.

---

## 9. Special destinations (Rick's, Island, NASA shop, Mount Rushmore, space telescope)

None of these is related to contracts or routes. Rick's is the bar/back room (sabotage). The island,
Rushmore and space views are the telescope screens and rocket-mission rooms (`Insel.cpp`,
`Rushmore.cpp`, `WeltAll.cpp`). NASA is the rocket-part shop. `World.cpp` (not one of the five)
is the branch-office phone screen of §2.6. **[H]**

---

## 10. Dependency: global kerosene price `K` (`Sim.cpp: SIM::NewDay`) **[H]**

`K` starts at 500. Each new day, with an RNG seeded by Date:
* add `Rand(21)−10`
* plus, with probability 3/20, another `Rand(41)−20`
* plus trunc(20·sin((Date+Rand(6))/3))
* plus trunc(20·sin(1.7·Date))
* then clamp to [300, 700]

Two missions add extra shocks: the kerosene-crisis mission, and mission 10 during days 3–10 and
35–55. The last 10 values are kept for charts.

Contract prices (§2.2), reference ticket prices (§3.6) and flight costs all scale with K.
**Draw order:** the values are drawn from left to right in the update above. Keep that order.

---

## 11. Open questions and deliberate-divergence candidates

1. **AvgFlown** is never updated (always 30). The "rarely flown" penalty is constant and cancels.
   Was a moving-average update lost? Recommendation: keep the original behaviour.
2. **Passenger share uses the planning flight's own price for every competitor** (§3.7). This is
   probably unintended. Keep the original behaviour by default, and offer a "fair price competition"
   option.
3. **First class uses the economy price** in the original. The SDL port fixed it. Decide which to
   follow.
4. **The route fee is charged on both directions** (2× the quoted rate). Confirm in game with a
   ledger check.
5. **AnzPassagiere is a divisor** in the daily share update. With the shipped data the smallest
   value (late game, DayFaktor 650) is 37, so it never reaches 0. Modded tables could produce 0, so
   guard against it in the port.
6. `bUhrigFlight` is never set by `RefillForUhrig`. The default constructor leaves it uninitialised.
   Intended behaviour is probably TRUE for manager orders.
7. Flying a contract on the wrong day consumes it with neither pay nor penalty (§2.8). Is that the
   observed behaviour?
8. Opening hours of the cargo office, and whether the travel agency is closed for the player on
   Sundays (only pedestrians avoid it).
9. Multi-line `.res` text joining (data-tables.md §4.1).
10. Network sync messages for refills and auctions are out of scope here.
