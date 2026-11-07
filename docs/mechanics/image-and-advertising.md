# Image, advertising and passenger moods

Game-mechanics reference for the free game of Airline Tycoon Deluxe, written for OpenAT. Sources:
our behaviour specs (`docs/spec/findings/economy.md` §8–10, `contracts-and-routes.md` §3.7–3.9,
`planes-and-flights.md` §12.8–13, `airport-and-people.md` §6.5–6.7, `rooms-and-ui-7` §7,
`rooms-and-ui-3b` §10), with every number re-checked against the reference source. Formulas are
restated in our own words. All arithmetic is integer unless stated (÷ truncates toward zero).

Units: money in game currency; "image" is a plain integer; percentages are integer percent.
`RefPrice(route)` is the reference ticket price defined in `routes-and-orders.md` §2.5
(≈ 1/30 of a notional fuel bill, so it follows the daily kerosene price).

---

## 1. The two image values

| Value | Range | Start | Where shown |
|---|---|---|---|
| Company image `I` | −1000 … +1000 (clamped after every change) | 0 (300 in one add-on mission) | Statistics "awareness" = I/10 %; ad agency report |
| Route image `R` (per airline, per **directed** route) | 0 … 100 (a byte) | 0 when the licence is taken | not shown directly; drives demand |

* There is **no passive decay** of either value. Nothing lowers image just because time passes; it
  only moves through the events in §3 and the campaigns in §4.
* Route image belongs to the licence. Losing or dropping a route and renting it again starts again
  at 0.
* Branch offices carry an image field too, but it is never read by anything (dead data).

### Implementation in OpenAT
* Implemented: company image and clamp `Assets/OpenAT/Sim/Player.Finance.cs:39,58,105-110`;
  route image field `Assets/OpenAT/Sim/Routes/Route.cs:54`; start values
  `Assets/OpenAT/Sim/Finance/FinanceSetup.cs:51`; awareness statistic
  `Assets/OpenAT/Sim/World.Finance.cs:116`; unused branch image `Assets/OpenAT/Sim/Airport/Gates.cs:69`.
* No decay system exists, which matches the original.

---

## 2. How image feeds into demand

Image only matters for **route** passengers. Contract (order) passengers are a fixed head count and
are not affected by image at all.

For every route flight entry, while it is still more than about an hour before departure, the
passenger estimate is multiplied by an image factor (full formula in `routes-and-orders.md` §2.3,
step 7):

```
T      = clamp(4·R + I + 200, 0, 1000)
factor = (400 + T) / 1100            → between 0.364 and 1.273
```

Both economy and first class use the same factor.

Consequences:
* One point of route image is worth **four** points of company image.
* With a new route (R = 0) and a neutral airline (I = 0), T = 200 and the factor is 0.545. Most
  of the potential market is lost until image is built.
* The cap is reached at 4R + I = 800, e.g. R = 100 with I ≥ 400, or R = 50 with I ≥ 600.
  Beyond that, more image adds nothing to demand.
* Very negative image (I ≤ −600 with R = 100, or I ≤ −200 with R = 0) bottoms out at 0.364.

**Worked example.** A route with R = 20, airline I = 100: T = 80 + 100 + 200 = 380, factor =
780/1100 = 0.709. A top-tier company campaign (+50, §4) raises T to 430 and the factor to 0.755,
about 6.4 % more passengers on every route. A top-tier route campaign (+46 route image) on that one
route raises T to 564 and the factor to 0.876, about 24 % more on that route.

### Implementation in OpenAT
* Implemented: `Assets/OpenAT/Sim/Fleet/RoutePassengers.cs:128-139` (image and noise step,
  16-bit truncation kept), called for economy (line 44) and first class (line 68).

---

## 3. What changes image

### 3.1 Every route or contract flight (at take-off)

When a passenger flight departs (route or contract; ferry and freight flights never touch image),
the game computes a score `A` and applies it.

**Image receiver.** Normally the flying airline. If a rival has run the "foreign brochures" prank on
it, all company-image changes of this step go to that rival for the day (the route-image changes
still go to the flier's own route).

**Human-only extras** (computer airlines skip these):
* Crew: if the plane's crew quality (average talent of its assigned crew, 60 if none) is below 50
  **and** the flight's object id is a multiple of 10, company image −1; above 90 with the same id
  test, +1. The id test is a cheap deterministic "1 in 10".
* Route flights: route image −1 (if above 0). Together with the +1 every route flight gets anyway
  (§3.2) a human's route image is unchanged by an average flight, while a computer airline's rises
  by 1.

**Score `A`** (all airlines):

| Input | Points |
|---|---|
| Plane condition < 20 % | −50 |
| Plane condition < 60 % (adds to the line above) | −10 |
| Plane condition > 98 % | +10 |
| Each of seats, food, trays, décor at level 0 (cheapest) | −1 each |
| Each of seats, food, trays, décor at level 2 (best) | +2 each |
| Humans only: `3·assigned attendants ÷ required attendants` | +3 when exactly staffed, up to +6 with double staffing |
| Route flights only, ticket price `P` vs `C = 3·RefPrice` (C × 0.9 for computer airlines) | P < C/2: +10; P > C: −10; P > 1.5·C: another −10; P > 2·C: another −10 |

Then company image `+= A ÷ 10` (truncated toward zero, so −9…+9 does nothing), clamped ±1000.
On route flights the route image also gets `A ÷ 10`, but only if `R + A÷10` stays above 0 (a
penalty that would reach 0 or below is skipped entirely), then clamped 0…100.

Not part of the score: engines, tyres, electronics and safety equipment. They matter for
first-class demand (luxury sum) and seasonal incidents, not for image.

**Worked examples (human, route flight).**

| Plane | A | Image per flight |
|---|---|---|
| Condition 75 %, all cabin items level 1, exact crew, price 2·RefPrice (the default) | 3 | 0 |
| Same, but all four cabin items level 2 | 11 | +1 company, +1 route |
| Same as row 2, price ½·RefPrice (cheap preset) | 21 | +2 / +2 |
| Condition 99 %, cabin level 2, double crew, price ≤ 1.4·RefPrice | 34 | +3 / +3 |
| Condition 50 %, cabin level 0, exact crew, price 4·RefPrice | −10 −4 +3 −10 = −21 | −2 company; route −2 if it stays above 0 |
| Condition 15 %, cabin level 1, exact crew, price 7·RefPrice | −60 +3 −30 = −87 | −8 company; route −8 if it stays above 0 |

So the cheap way to gain image is a well-equipped cabin plus prices below 1.5·RefPrice. Prices
above 3·RefPrice cost image on every flight.

### 3.2 Other route-flight effects
* Every route flight: route image +1 (up to 100) in the booking step.
* Route image on the first flight starts from 0, so a new human route climbs only through A ≥ 10
  and campaigns; a new computer route climbs by 1 per flight even with A = 0.

### 3.3 Incidents and operations

| Event | Company image | Route image | Notes |
|---|---|---|---|
| Breakdown in the air from poor condition | −2 | × 0.8 (that route) | Rolled every 4 game minutes per airborne plane carrying passengers: if condition < 80, chance `(79 − condition)/5000` per roll. Also share price × 0.7, dividend trust −1 if above 4, and the boss notices "image got worse" next morning. |
| Departure from home with no gate | −2 | — | Route and contract flights only. |
| Landing at home with no free gate | −2 | −2 (floor 0) only when the new company image is a multiple of 10 | The odd "multiple of 10" test is original. All arriving passengers count as unhappy. |
| A passenger walks into stink-bomb smoke | −1 | — | Only for one in four passenger clans. |
| Seasonal incident (missing season upgrade) | 0 | 0 | Costs 75 000, no image effect. |
| Delays, strikes, missed passengers | 0 | 0 | No direct image effect. |
| Plane crash | — | — | There are no crashes in the free game. |

### 3.4 Sabotage (rival pranks) on the victim

| Prank (flight-time family) | Company image | Route image of the flight |
|---|---|---|
| 1 (mildest) | 0 | × 0.99 |
| 2 | −2 | × 0.95 |
| 3 (flat tyre, also delays 1 h) | −3 | × 0.90 |
| 4 | −8 | × 0.50 |
| 5 (most expensive prank) | 0 | unchanged (money and share trust only) |

Next-day pranks affecting image indirectly:
* **Foreign brochures**: for one day, the victim's flight image changes (§3.1) go to the saboteur.
* **Press statement**: for one day, every route flight that is recalculated carries only 1–3
  passengers (economy and first class), whatever the image.

### 3.5 Scripted and AI effects
* Computer airlines with the "image bonus" behaviour (one add-on mission only, not the free game)
  gain `Date mod 3` image every night.
* Flight Security mission 10, day 55: −200 (mission specific).

### 3.6 What image changes elsewhere
* Staff mood: humans' employees lose happiness every night when I < 500 (1 step; 2 if I < 0;
  3 if I < −500; each step −1, or −21 for staff paid below their original salary).
* Morning boss comment tiers (worse today / < −100 / > 100 / > 500); I < −990 means game over.
* Newspaper "most popular airline" headline when the leader has I > 100.
* AI behaviour: robots advertise more when their image is low (§4.4).
* Not affected by image: contract offers, contract pay, share price, bank terms.

### Implementation in OpenAT
* Implemented: flight image effects `Assets/OpenAT/Sim/Fleet/FlightOps.cs:233-279` (brochure
  redirect, crew ±1, score, route image rule); route image +1 `FlightOps.cs:180`; no-gate departure
  `FlightOps.cs:450-456`; no-gate landing `FlightOps.cs:539-548`; breakdown `FlightOps.cs:588-596`
  with `Assets/OpenAT/Sim/Player.Finance.Events.cs:34-39` (via
  `Assets/OpenAT/Sim/Fleet/FinanceIntegration.cs:96`); sabotage `Player.Finance.Events.cs:13-28`
  and route factor `Assets/OpenAT/Sim/Items/SabotageEngine.cs:236`; staff mood
  `Assets/OpenAT/Sim/Staff/WorkerPool.cs:72-74`; boss tiers and game over
  `Assets/OpenAT/Sim/Missions/MorningReviewSystem.cs:75-87`; AI image bonus
  `Assets/OpenAT/Sim/Finance/FinanceSystems.cs:86-92`; ATFS 10 `FinanceSystems.cs:215`; newspaper
  `Assets/OpenAT/Sim/Finance/Newspapers.cs:223-226`.
* Implemented (stink bomb): `Assets/OpenAT/Sim/Hall/HallCustomers.cs` applies `ChangeImage(-1)` to
  the passenger's airline, then publishes `HallImagePenalty` for observers.
* Verified: sabotage type 1 multiplies the flight's route image by 99/100 (integer, as the original;
  `Player.SabotageRouteImagePercent`, test `RoutesOrdersFixesTests.Sabotage1_RouteImageTimes099`).

---

## 4. Advertising agency

### 4.1 Access
* Opens at 12:00. Closed on weekday codes 5 and 6 in the free game. (In missions below Normal
  difficulty the room is a closed, empty variant.)
* The agent can also be reached by phone.
* Talking to him offers: "how is my image" (report, §4.5), a company campaign, a route campaign
  (pick one of your licensed routes from a list), or a campaign for "everything".

### 4.2 Campaigns, prices and effects

Six sizes per type, from a joke campaign to a huge one. Price `p`, size index `k = 0…5`.

| Type | Prices k = 0…5 | Effect formula |
|---|---|---|
| Company | 1 000 · 10 000 · 50 000 · 100 000 · 250 000 · 2 500 000 | I += ⌊⌊p/10 000⌋·(k+6)/55⌋ |
| One route (both directions) | 2 000 · 8 000 · 22 000 · 56 000 · 120 000 · 1 400 000 | R += ⌊p/30 000⌋ on A→B **and** B→A |
| Everything | 5 000 · 93 000 · 290 000 · 870 000 · 4 925 000 · 60 000 000 | I += ⌊⌊p/15 000⌋·(k+6)/55⌋, and every licensed route R += ⌊p·(k+6)/6/120 000⌋ (as a byte) |

Resulting gains:

| k | 0 | 1 | 2 | 3 | 4 | 5 |
|---|---:|---:|---:|---:|---:|---:|
| Company: I | 0 | 0 | 0 | +1 | +4 | +50 |
| Route: R (each direction) | 0 | 0 | 0 | +1 | +4 | +46 |
| Everything: I | 0 | 0 | +2 | +9 | +59 | +800 |
| Everything: R on every route | 0 | 0 | +3 | +10 | +68 | 916 → wraps to 148 as a byte, then capped → 100 |

Rules:
* **Instant and permanent.** No duration, no fading, no delayed effect. The campaign's image is
  added on the spot; nothing ever takes it away again except the events in §3.
* Clamp: company ±1000, route 0…100.
* Money check: refused if it would push cash below the debt limit (−1 000 000 overdraft floor).
* Cost: booked as "other" expense with its own ledger line per size; the bodyguard (security
  advisor) rebate of talent/10 % applies afterwards.
* The smallest size (k = 0) does nothing except make every human airline receive a mocking letter
  about the advertiser (a joke).
* With integer maths, sizes 0–2 of company and route campaigns are pure money sinks. Value for
  money: route k = 4 gives 4 route points (= 16 demand points) for 120 000; company k = 4 gives 4
  points for 250 000; the "everything" k = 4 at 4.9 M gives +59 company and +68 on every route, which
  for an airline with many routes is by far the strongest per currency unit.

**Worked example.** An airline with I = 100 and five routes at R = 10 buys "everything" k = 3
(870 000): I → 109, every R → 20. On each route T rises from 340 to 389 (factor 0.673 → 0.717,
+6.6 % passengers).

### 4.3 Image report
Asking about image clamps it to ±1000 and answers with a text band plus the number:
* I = 0: one neutral line.
* I < 0: band `2503 + I/251` (four bands, 2500–2503).
* I > 0: band `2504 + I/167` (six bands, 2504–2509).

### 4.4 Computer airlines
* Same company formula and prices, decided during their "advertising" errand (only when they are
  not hoarding cash). Size choice: in route mode (or with the hard-advertising behaviour) the joke
  size on every fourth day, otherwise by cash (≤ 1.8 M → 1, ≤ 2.5 M → 2, ≤ 3 M → 3, ≤ 4 M → 4, else
  5). Otherwise the joke size if cash ≤ 1.8 M or on every third day, else by image (> −20 → 1,
  > −40 → 2, > −60 → 3, else 4); then, with more than five planes and not on a third day, one size
  more (max 4).
* Trigger: (I < 0, or route-minded with I < 300) with > 1.5 M cash and not saving; or the
  "much advertising" behaviour with > 150 000 cash and trailing a rival by more than 10; or
  I < 1000 with more than 4 M net cash and not saving.
* **Route ads are cheaper for robots**: they pay the 56 000 size but gain `56 000/2000 = +28`
  route image (instead of +1), on one direction only, picking the first licensed route below 70
  (or below 80 / 100 depending on cash and saving).

### Implementation in OpenAT
* Implemented: prices and formulas `Assets/OpenAT/Sim/Finance/Advertising.cs:25-50`, campaign
  `Advertising.cs:64-97`, report bands `Advertising.cs:117`, command with debt check
  `Assets/OpenAT/Sim/Finance/FinanceCommands.cs:231-244`, robot campaigns
  `Assets/OpenAT/Sim/AI/RobotExecutor.cs:486-526`, robot route ad
  `Assets/OpenAT/Sim/AI/RobotShortcuts.cs:273-290`, opening hour constant
  `Assets/OpenAT/Sim/Hall/HallConstants.cs:136-137`. Room/dialog in
  `Assets/OpenAT/View/Rooms/Werbung/WerbungRoom.cs`.
* Implemented (robot size choice): `RobotExecutor.CampaignSize` applies the "+1 size with more than
  five planes, not on a third day" bump after the whole choice, so it also lifts the joke size when
  cash is ≤ 1.8 M, as the original.
* Minor: robot company campaigns go through `AdCampaignCommand`, which adds the debt-limit check,
  the "other expenses" statistic and the bodyguard rebate; the original robot path charges the
  money only. No practical effect (robots have no bodyguard and plenty of cash when they qualify).

---

## 5. Passenger satisfaction and thought bubbles

### 5.1 Satisfaction statistic (no effect on image)

On each arrival of a route or contract flight the passengers are judged in groups of 15 (plus one
group with the remainder). Each group gets the first rule that fires:

1. `Rand(70) > condition` → unhappy (needs repairs).
2. Cabin sum `Q > 4` and `Rand(100) < 40` → happy.
3. `Q > 8` and `Rand(100) < 55` → happy.
4. `Rand(65) > crew quality` → unhappy (bad service).
5. Route flights priced above `3·RefPrice` (using the route's current price) and `Rand(130) < 100`
   → unhappy (too expensive).
6. otherwise happy.

`Q = seats + food + trays + décor levels + (attendants on board − attendants required)`. The random
generator is seeded from airline, date, hour, Q and departure city, so it is repeatable.

This only feeds the "satisfied / unsatisfied passengers" statistics. **It does not change image.**
Image is driven directly by the same inputs (condition, cabin, crew, price) through §3.1.

Practical reading: condition is checked first. A plane at 50 % condition has a 19/70 = 27 % chance
per group of failing rule 1 alone. Cabin quality rules 2–3 can rescue a group before the crew and
price checks are even reached, which is why good equipment "hides" high fares.

### 5.2 Thought bubbles in the hall

Bubbles are cosmetic, but the arrival bubbles mirror the satisfaction rules:

| Group | Shown when |
|---|---|
| Rival sympathy (5 faces, very friendly … hostile) | Computer rivals near you or each other; thresholds sympathy > 50 / > 10 / > −10 / > −50 / else |
| Arriving passengers' verdict | Rules of §5.1 per spawned figure: repairs needed, two stars, three stars, bad service/sick, too expensive, satisfied. First class uses its own variants (`Rand(90)` for condition, cabin thresholds 8 and 10, `Rand(100)` for crew; the price check never fires for first class). The verdict bubble carries the flight's airline logo (or the brochure saboteur's). |
| Departing passengers | ticket in hand, flying, missed the plane |
| Shoppers | where they are heading (bank, café, chocolates, travel, home, museum, toilet, screen) |
| Events | stink (shoppers), sick (passengers in stink smoke), stuck in a crowd, the dog |

* A bubble lives about 60–74 ticks (3–3.7 s), fades in and out, and is shown only with the
  "Thoughts" option on.
* Hovering the mouse over a person keeps the bubble up.
* One hall figure stands for 15 passengers (45 once 100 have arrived).

### Implementation in OpenAT
* Implemented: satisfaction `Assets/OpenAT/Sim/Fleet/FlightOps.cs:287-324` (group size 15 is
  correct; `docs/spec/findings/planes-and-flights.md` §13 wrongly says groups of 5); arrival bubbles
  `Assets/OpenAT/Sim/Hall/HallPlanes.cs:265-307`; mood ids and logo rule
  `Assets/OpenAT/Sim/Hall/HallConstants.cs:114-130`; sympathy bubbles
  `Assets/OpenAT/Sim/Hall/HallTick.cs:234-243`; stink/sick `Assets/OpenAT/Sim/Hall/HallCustomers.cs:76-86`
  (image part not applied, see §3).

---

## 6. Mission switches (brief)
* Ad agency closed variant below Normal difficulty (missions only).
* One add-on mission starts everyone at I = 300 and gives robots the nightly image bonus.
* The "image" mission is won at I ≥ 750 (progress = I·100/750).
* Flight Security 10 has a scripted −200 on day 55.

---

## Guide claims

Claims from the third-party strategy guide (`docs/spec/local/faq/gamefaqs-11142-claims.md`) that touch
this topic.

| ID | Claim (paraphrase) | Verdict for Deluxe | OpenAT status |
|---|---|---|---|
| F8 | The ad agency sells airline and route ads that improve image. | **Confirmed, incomplete.** There is a third type, "everything" (company + all licensed routes). Effects are instant and permanent; small sizes do nothing. | Implemented (`Advertising.cs`). |
| E1 | Better equipment allows higher fares without complaints, fewer breakdowns; happy passengers raise image. | **Differs.** Cabin quality makes "happy" fire before the price check, so fewer price complaints: true for the statistic and bubbles. But satisfaction never changes image. Image comes directly from condition, cabin levels, crew and price (§3.1). Breakdowns depend on condition (and season upgrades), not cabin equipment. | Implemented as the original. |
| E2 | Technical items: cheap safety gives a bad image; cockpit calms passengers. | **Wrong for image.** Engines, tyres, electronics and safety are not in the image score. They count in the first-class luxury sum and for seasonal incidents (summer: safety; other seasons: electronics). | Implemented as the original. |
| E3 | Cabin items are one-off costs except food, which is charged per passenger per flight. | **Confirmed.** Food costs 10 / 20 / 50 per economy passenger by level (first class not charged). Food level counts in the image score like the other cabin items. | Implemented (`Assets/OpenAT/Sim/Fleet/FlightOps.cs:33,101-105`). |
| T4 (second half) | Route passengers depend on image, unlike orders with fixed counts, so advertise. | **Confirmed.** Factor 0.36–1.27 on route demand; route image counts 4× company image; no effect on contracts. | Implemented. |
