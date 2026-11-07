# Economy: income, running costs, kerosene, money events

Part of the OpenAT game-mechanics reference. It covers the **free game** of Airline Tycoon Deluxe.
Values that a campaign mission switches (start money, interest rates, the fuel-crisis missions) are
mentioned only briefly.

Sources: our clean-room specs `docs/spec/findings/economy.md`, `contracts-and-routes.md` and
`planes-and-flights.md`. Facts were spot-checked against the original source (reference only; no code
is reproduced here). Everything is written in our own words. Data tables (per plane type, per route,
per city) are deliberately left out: read them from the install at run time.

Conventions:
- Money is in whole currency units. All formulas use **integer maths, evaluated left to right,
  truncating toward zero** unless marked otherwise. So `a·b/c/d` means ((a·b)/c)/d.
- `K` is today's kerosene market price per barrel. `Rand(n)` is a uniform integer in 0..n−1 from
  the original's seeded generator.
- "Overnight" means the day rollover between 18:00 and the next 09:00.
- Status tags: **Implemented** / **Partial** / **Missing**. File references are relative to
  `Assets/OpenAT/Sim/` unless a path says otherwise.

---

## 1. How money is booked

Every ordinary money change runs through one booking routine (amount, reason code). In order, it:
1. warns via the secretary when the balance crosses **down** through −500,000, −1,000,000 or
   −3,000,000;
2. silently drops a positive amount that would overflow the balance;
3. changes the cash;
4. adds an account-statement line (FIFO of 100 lines, plus a carried opening balance);
5. adds the **absolute** amount to one balance-sheet field, chosen by the reason code (§9).

A few flows bypass it: phone units, AI bank actions and AI share trades, liquidation proceeds, and
the loan balance itself.

**Implementation in OpenAT**
- Booking routine (warnings, overflow guard, statement, balance sheet, event): **Implemented**,
  `Player.Finance.cs:66` (`Book`), warnings `Player.Finance.cs:97`. Minor difference: OpenAT checks
  the warnings after booking, the original before. The result is the same.
- Silent bypass: **Implemented**, `Player.Finance.cs:77` (`ChangeMoneySilently`).
- Reason codes: **Implemented**, `Finance/MoneyReason.cs`.

---

## 2. Flight income

A flight is settled once, **at take-off**. Its result `Saldo = E − A` (income minus fuel and
catering) is booked as a single line with reason 2099 + flight type (2100 route, 2101 passenger
contract, 2102 ferry, 2103 freight). Planes earn nothing on landing.

### 2.1 Route flights (ticket income)

`E = P_eco · T_eco + P_fc · T_fc`. Here P is the number of passengers booked on this flight entry
and T are the ticket prices **frozen on the flight-plan entry**: prices are captured when the
entry is created, or when an existing entry is re-priced after the player changes the route price.

**Reference price** of a route (it moves with kerosene every day):

```
barrels_ref = (distance_km · 800) / 160 / 800            (a notional 800 l/h, 800 km/h plane)
fuel_ref    = max(1000, barrels_ref · K)
RefPrice    = fuel_ref · 3 / 180 · 2                     (≈ fuel_ref / 30)
```

- Default price when a route is licensed: economy = 2·RefPrice, rounded down to a multiple of 10.
  First class = 2 × economy.
- Player presets ½ / 1 / 2 / 4 × RefPrice; ±10 buttons. Economy is clamped to 0..16·RefPrice and
  first class to 0..48·RefPrice. The economy price is copied to the reverse direction; first class
  is not.

*Worked example.* A 1,000 km route has barrels_ref = 6. At K = 500, fuel_ref = 3,000 and
RefPrice = 100, so the default fare is 200 economy and 400 first class. At K = 300, RefPrice = 60;
at K = 700, RefPrice = 140. A fare you leave unchanged therefore becomes "expensive" or "cheap"
relative to the reference as kerosene moves.

### 2.2 How the ticket price acts on demand

The full passenger formula belongs to the routes topic. The price-related parts are:

| Step | Economy | First class |
|---|---|---|
| Share weight per licensee | `10000/T` (100,000 if T = 0), ×0.9 if T > 3·Ref, ×0.9 more if T > 5·Ref, ×0.8 if T > 6·Ref | same with thresholds 9 / 15 / 18 × Ref; rank-1 bonus ×1.7 instead of ×1.5 |
| Over-pricing cut | if T > C (C = 3·Ref, C > 10): `n = n·(C − 10)/T` | only if T > 3·C, same factor |

- **Quirk (original):** every licensee's weight is computed with the price of the flight *being
  planned*, so price never shifts market share between airlines. Price acts only through the
  absolute over-pricing cut, the image change and satisfaction.
- **Original bug:** the first-class pass uses the **economy** price throughout. Our option
  `FixFirstClassDemand` uses the first-class price instead.
- Image scales demand by `(400 + I)/1100` with `I = clamp(4·routeImage + companyImage + 200, 0, 1000)`,
  that is 0.36× to 1.27×.

*Example.* With Ref = 100 (C = 300), a fare of 400 multiplies economy demand by 290/400 = 0.725.
A fare of 300 or less has no over-pricing cut.

### 2.3 Price → image and satisfaction

These rules use C2 = 3·RefPrice (for the AI, C2·9/10).
- **Image score per route flight:** T < C2/2 gives +10; T > C2 gives −10; T > 1.5·C2 another
  −10; T > 2·C2 another −10. The score is added to the equipment/condition/crew score, and the
  company image changes by `score/10` (route image likewise, if the result stays above 0).
- **Satisfaction** (statistics only, evaluated at arrival): passengers are judged in groups of
  15 (the original constant; `planes-and-flights.md` §13 says groups of 5, which is a spec error).
  Each group takes the **first** rule that applies:
  1. a condition check (`Rand(70) > condition` → unhappy);
  2. a cabin-quality chance (quality > 4: 40 % happy; > 8: 55 % happy);
  3. a crew check (`Rand(65) > crewQuality` → unhappy);
  4. a price check: if the route price exceeds C2, `Rand(130) < 100` → unhappy (≈ 77 %);
  5. otherwise happy.

  So good cabin equipment lets a group be happy before the price check is reached.

**Implementation in OpenAT**
- Route income at take-off: **Implemented**, `Fleet/FlightOps.cs:45` (`BookFlight`), income
  `Fleet/FlightOps.cs:210`.
- RefPrice and default fare: **Implemented**, `World.Operations.cs:142`, `Routes/RouteOps.cs:15`.
- Fare clamps, presets and reverse copy: **Implemented**, `Routes/RouteOps.cs:207–240`.
- Price in the passenger formula, including both quirks and the `FixFirstClassDemand` switch (on by
  default: first class uses the first-class price; off = the original's economy price):
  **Implemented**, `Fleet/RoutePassengers.cs:29–75`, weights at `:88–110`. Option at
  `GameOptions.cs:20`.
- Image price check: **Implemented**, `Fleet/FlightOps.cs:262–271`.
- Satisfaction rule chain (groups of 15): **Implemented**, `Fleet/FlightOps.cs:287–320`. It
  matches the original, unlike the spec's "5".

---

## 3. Contract, freight and ferry income; penalties

### 3.1 Passenger contracts
- Pay = the contract's premium, earned **at take-off** when the plan entry is valid. An invalid
  entry pays nothing but still pays fuel and catering. Flying on a wrong day uses the contract up
  with neither pay nor penalty (an original quirk).
- Premium base: `Base(f) = ceil(max(1000, barrels·K)/100)·f`, where barrels is computed for a
  notional 8000 l/h, 700 km/h plane. f = 120 (travel agency and branches), 130 then ×5/4 (Last
  Minute), 100 (start contracts). Then type, size and area multipliers apply (contracts topic).
- Penalty: by default half the premium. Tight contracts pay ×2 with penalty ×2 (at Last Minute,
  ×4). The "windfall" type has no penalty.

*Example.* 1,000 km at K = 500 gives 71 barrels and fuel 35,500. A normal 180-passenger travel
agency contract pays 355·120 = 42,600 and risks a penalty of 21,300.

### 3.2 Freight contracts
- Capacity per flight = **type seats / 10 tonnes** (from the plane type, not the configured seats).
- The premium is paid **only on the flight that delivers the last tonne**. Earlier legs earn
  nothing and only pay fuel. Base factor f = 105, then size and tonnage multipliers apply.
- Freight income is **not** added to the balance sheet or to the income statistics. It reaches
  only the cash.
- Switching a plane between seats and cargo costs **15,000** each time the state flips (reason
  2111). Neither the balance sheet nor the statistics record it.

### 3.3 Ferry (empty positioning) flights
`E = typeSeats · distance_km / 40`, a small positive credit (for example, 150 seats over 1,000 km
earn 3,750). It is not recorded in the balance sheet.

### 3.4 Penalties
- On the morning after the last allowed day, a **human** airline pays the penalty for every
  contract that was neither flown nor validly planned (reason 2060 for passengers, 2065 for
  freight). It gets a letter and the "earn X" mission counter falls by the same amount. **The AI
  never pays penalties.**
- Voluntary cancel (filofax): the full penalty is paid at once. In the original the two reason
  codes are swapped here (passenger → 2065, freight → 2060). The penalty statistic is updated,
  but the mission profit counter is **not** reduced.
- 2065 has no balance-sheet field, so freight penalties (and manually cancelled passenger
  contracts) do not show in the daily balance sheet.

**Implementation in OpenAT**
- Contract/freight/ferry income and the 15,000 seat switch: **Implemented**,
  `Fleet/FlightOps.cs:78–101`, `:143`, `:206–216`.
- Contract base price: **Implemented**, `World.Operations.cs:145`.
- Expiry penalties (humans only, letters, profit counter): **Implemented**,
  `Contracts/ContractUsage.cs:108`, bookings at `:119` and `:160`. Statistic via
  `Fleet/FinanceIntegration.cs:146`.
- Voluntary cancel: **Implemented**, `Contracts/ContractCommands.cs` (`CancelContractCommand`). The
  penalty goes into statistic 36 "expense: penalties" as in the original. Two intentional fixes of
  original bugs: each order is booked with its own reason (passenger 2060, so it shows in the balance
  sheet; freight 2065), and the penalty lowers the "earn X" counter like an expired order's penalty
  (the original forgets it on a cancel, which would let a player dodge the mission loss).

---

## 4. Per-flight costs

### 4.1 Fuel

```
barrels = distance_km · burn_l_per_h / 160 / speed_km_per_h        (integer)
```

Burn and speed are the plane's own values. 160 is the original's litres per barrel. The pump price
per barrel depends on the chosen quality (§6.3): premium 2·K, normal K, cheap K/2.

Three cost variants exist in the original:

| Variant | Use | Tank part | Pump part | Floor |
|---|---|---|---|---|
| Estimate | contract prices, reference fare, advisors | free | pump price | 1000 |
| Planner display | planner cost column | at average tank price | pump price | 1000 |
| Booking | real cash at take-off | **not charged** (already paid) | pump price | none |

When the tank is open, `t = min(tankContents, barrels)` is taken from the tank. The tank part is
then recorded at `t · averageTankPrice` as an **imputed** cost (balance-sheet kerosene, statistic
35, plane P&L), with no cash movement.

*Example.* A plane burning 3,000 l/h at 850 km/h on 1,000 km needs 3,000,000/160/850 = 22
barrels. At K = 500 that costs 11,000 normal, 22,000 premium or 5,500 cheap.

### 4.2 Catering

`food = economyPassengers · FoodCost[level]`, with FoodCost = **10 / 20 / 50** for food levels
0/1/2. It is charged on route and contract flights only. **First-class passengers are not
charged.** The other cabin items are one-off refit costs, not running costs.

*Example* (continuing §4.1): 120 economy passengers at food level 1 cost 2,400. At a fare of 200
the flight earns 24,000 − 11,000 − 2,400 = **10,600**.

### 4.3 Airport fees

There is **no** landing, take-off or per-passenger airport fee in the original (none found). The
only airport-side costs are the gate rents (§5.3). Flying from home without a gate costs image
(−2 per departure), not money.

**Implementation in OpenAT**
- Fuel units and the three cost variants: **Implemented**, `Fleet/OperationsRules.cs:58–111`. Tank
  drain with imputed cost: `Player.Finance.Fuel.cs:84` (`DrawTankFuel`), called from
  `Fleet/FlightOps.cs:152–157`.
- Catering on economy passengers: **Implemented**, `Fleet/FlightOps.cs:102–106`. Prices are in
  `Fleet/Plane.cs:30`.
- The former duplicates in `Player.Finance.Fuel.cs` (`FlightFuelCost`, `BarrelsForFlight`,
  `FlightFuelEstimate`, `PumpCost`) were removed; the planner tooltip uses `World.FuelCost.Estimate`.

---

## 5. Daily running costs (charged overnight)

All monthly prices are charged daily as `monthly/30` (integer).

### 5.1 Salaries
- `Money −= (Σ monthly salaries of all employees)/30` (reason 2070). The sum is taken first,
  then divided once. **Only human airlines pay**; the AI has virtual staff and pays nothing.
- Each plane's P&L is additionally charged `salary/30` for each crew member assigned to it.
- Generated pilots ask 3,000..9,900 a month and flight attendants 3,000..7,900 (pool top-ups). The
  shipped pool table has its own values; read it from the install.

*Example.* 4 pilots at 6,000 and 6 attendants at 4,000 = 48,000 a month → **1,600 a day**.

### 5.2 Maintenance contract and repairs
- Fee per plane per month by mechanic level 0..3: **1,200 / 18,000 / 48,000 / 150,000**. That is
  40 / 600 / 1,600 / 5,000 a day, charged as `fee·planes/30` for **all** planes.
- Repair surcharge, only when the condition improved that night:
  `improvement·typePrice/110 + Δcondition·10·maintFactor·(2100 − buildYear)/100·(200 − condition)/100`.
  Here improvement is the part of the new condition more than 20 points above the plane's recent
  worst condition. Restoring within that 20-point band costs almost nothing extra. Above it, each
  point costs about 0.9 % of the type price.
- The total is booked once as reason 3110 (statistic 19).

*Example.* Two planes on the default mechanic contract (level 2) pay 48,000·2/30 = **3,200 a day**
before any surcharge.

### 5.3 Rents (gates, branches, routes)
- Gate: `rent/30` per rented gate. The starting gate costs 1,000 a month (33 a day). The gate line
  is booked even when it is 0.
- Branch offices, the home city included: `rent/30` each. The home rent is the city's office rent
  from the city table.
- Routes: `rent/30` **per licensed direction**. Licensing sets the fee on both directions, so a
  city pair costs **2 × the quoted monthly fee** (an original quirk; the UI quotes one direction).
- Auction wins (gates, routes, branches from the manager's board) cost a one-off **3 × the final
  bid**, after which the bid is the monthly rent.
- All three rents go to statistic 34.

*Example.* A route quoted at 300,000 a month costs 2 × 10,000 = **20,000 a day**.

### 5.4 Interest (overnight; see §7.2 for the bank)
Three steps run in this order, each on the updated cash balance:
1. cash > 0: `+cash·depositRate/100/365` (reason 2001);
2. cash < 0: `+cash·depositRate/100/365`, a negative amount. **Quirk:** this overdraft interest
   uses the *deposit* rate;
3. loan > 0: `−cash·loanRate/100/365` (reason 2000). **Major original bug:** it is computed on the
   **cash**, not on the loan. With positive cash you pay the loan rate on your cash. With negative
   cash you *receive* money, which the balance sheet still lists as interest expense. The loan
   itself never accrues interest.

Free-game rates are **10 %** (loan) and **3 %** (deposit). Other missions differ: 5/5 on the easy
ones, 6/4 Normal, 15/1 Final and the add-ons, 15/0 for the flight-security missions.

*Example.* Cash 2,500,000 and loan 1,000,000: deposit interest +205, then the "loan" interest is
−2,500,205·10/100/365 = **−684**. With the fix (`FixLoanInterest`) it would be
−1,000,000·10/100/365 = −273.

### 5.5 Other overnight costs
- **Security subscriptions**: charged **in full every night** (not /30), reason 3503. The fixed
  items cost 20,000–30,000 each; the per-plane items cost 30,000–50,000 × planes. They are
  expensive by design.
- **Dividends**: `(issued − own shares)·dividend/365` paid; holdings of rivals × their dividend/365
  received. The start values (2,000 free-float shares at 10) give about 54 a day.
- **Wealth tax**: only above 5·10¹³ (an overflow guard). In practice it never applies.
- There is **no insurance** and no other tax.

**Implementation in OpenAT**
- Salaries (humans only, Σ/30): **Implemented**, `Player.Staff.cs:272` (`BookSalaries`), system
  `Finance/FinanceSystems.cs:162`.
- Crew salary in the plane P&L: **Implemented**, `Player.BookSalaries` charges each plane's
  `Balances[0]` with `CrewSalaryPerDay` (`Player.Staff.cs`).
- Maintenance fees and repair surcharge: **Implemented**, `Fleet/Maintenance.cs:18–62`; booking
  `Player.Finance.Upkeep.cs:103`; fallback `Finance/FinanceSystems.cs:205`.
- Rents: **Implemented**, `Player.Finance.Upkeep.cs:79` (`BookRents`), sourced from
  `Fleet/FinanceIntegration.cs:216`.
- Auction one-off 3×: **Implemented**, `Airport/AuctionBoard.cs:182`, `:195`, `:207`.
- Interest with the original bugs and the fixes `FixLoanInterest` / `FixOverdraftInterest`:
  **Implemented**, `Finance/FinanceRules.cs:43–68`, options at `GameOptions.cs:18`, `:26`.
- Security fees: **Implemented**, `Player.Finance.Upkeep.cs:54`, `:67`.
- Dividends: **Implemented**, `Player.Finance.Shares.cs:152`.
- Wealth tax: **Implemented**, `Player.Finance.Upkeep.cs:119`, `Finance/FinanceSystems.cs:223`.
- Rates and start money per mission: **Implemented**, `Mission.cs:36` (free game 1,500,000, 10 %
  / 3 %).

---

## 6. Kerosene

### 6.1 Market price

Start: K = 500. Before day 1 the 10-day history is back-filled by a noisy walk. Each night, with a
generator seeded by the date:

```
K += (Rand(21) − 10)                               uniform −10..+10
   + [Rand(20) < 3] · (Rand(41) − 20)               15 % chance of an extra −20..+20
   + trunc(20 · sin((Date + Rand(6)) / 3))
   + trunc(20 · sin(1.7 · Date))
K  = clamp(K, 300, 700)
```

What this means for the player (our analysis of the formula):
- The noise part is a random walk with a standard deviation of about 7–8 per day.
- The two sine terms are **added every day**, so they accumulate. Their sum makes a slow cycle with
  a period of 2π·3 ≈ **19 days** and a swing of roughly ±60. A faster ±13 ripple and a few days
  of phase jitter sit on top.
- Net effect: kerosene drifts through troughs and peaks about 9–10 days apart. "Buy at the trough,
  open the tank at the peak" works. The largest single-day move is about ±70.

Fuel-crisis missions (flight-security missions 9 and 10) add a +25 % jump, a halving rule and 20
extra noise steps. This never happens in the free game.

### 6.2 Storage tanks
- Starting capacity: **100 barrels**, empty, valve closed.
- Buying tanks (Petrol Air): four models, each bought ×1/2/3/5/10:

  | Model | Capacity per unit (barrels) | Price per unit |
  |---|---:|---:|
  | 1 | 10 | 10,000 |
  | 2 | 30 | 20,000 |
  | 3 | 50 | 40,000 |
  | 4 | 100 | 60,000 |

  The dialog shows ×1000 the capacity actually added. Whether "barrels" or "litres" was meant is
  open (spec Q-10). As coded, capacity grows by the small number.
- The purchase is refused if `cash − price < −1,000,000`. Reason 2091, then the security rebate
  (§7.4).

### 6.3 Buying fuel; quality
- Three qualities at **2× / 1× / ½× K**: premium, normal, cheap. Choosing one in the dialogue
  **permanently sets** the airline's quality. That quality also prices every barrel bought **at
  the pump** during flights, not only tank purchases.
- Tank purchase cost = `price·qty`, less **5 %** at ≥ 10,000 barrels or **10 %** at ≥ 50,000. It is
  clamped to the free tank space and to what cash allows down to −1,000,000.
- New average tank price: `(oldContents·oldAvg + cost)/(newContents)`.
- Cheap fuel adds qty to a "bad barrels" counter. Premium subtracts qty from it (floor 0). Every
  barrel drawn from the tank also reduces the bad counter first.
- **Effect of cheap fuel:** plane wear per flight is tripled when (tank open, not empty and more
  than 50 % bad barrels) or (tank closed or empty and the chosen quality is cheap):
  `wear = (1 + distance_m·10/40040174)·(2015 − buildYear)·factor/15`.
- **Premium has no benefit** apart from diluting cheap fuel in the tank. Bad fuel has no other
  effect (no extra incidents).

*Example.* A plane built in 1995 loses 20/15 = 1 condition point per short flight on normal fuel
and 60/15 = 4 on cheap. On a 22-barrel flight at K = 500, cheap fuel saves 5,500, but the 3
extra points cost repairs later (§5.2).

### 6.4 Tank vs market

The office kerosene picture opens the tank panel: price, capacity, contents and the **valve**.
When the valve is open, flights draw from the tank first and buy only the rest at the pump. Cash
was already spent when the tank was filled, and the balance sheet shows the imputed tank price.

The choice is economic only. Fill the tank when K is low, then open the valve while K is high. With
the default 100-barrel tank this covers only about 4–5 medium flights. Note that the volume
rebates need a single purchase of 10,000+ barrels, which requires a very large tank investment
(about 100 model-4 units ≈ 6,000,000 at coded capacities).

### 6.5 Kerosene advisor (staff advisor type 2)
- **On entering Petrol Air:** K < 400 and cash > 20,000 → "buy now". K > 600 → "don't buy". (The
  original also tests a tank condition that is always true.)
- **On choosing a quality:** a comment on that quality, which is the only way to judge fuel
  quality.
- **During flights:** a warning naming the plane whose flight just emptied the tank.
- **Random tips** (about every 1,024 time units during the day, one of three advisor kinds picked
  at random):
  - "open the valve" if K > 600, the valve is closed and the tank holds fuel;
  - "buy more tanks" if K < 400, the tank is more than half full, cash > 20,000 and capacity is
    below 4,000 per plane;
  - "buy fuel" if K < 470, the price fell for three days and just turned up, the tank is under 1/3
    full and cash > 20,000.

**Implementation in OpenAT**
- Price process, start history, crisis missions, tunable range: **Implemented**,
  `Finance/FinanceRules.cs:130–166`, `World.Kerosene.cs:24`, `:41`, system
  `Finance/FinanceSystems.cs:237`.
- Tanks, purchase, rebates, average price, bad barrels, clamp: **Implemented**,
  `Player.Finance.Fuel.cs:23`, `:113–158`. Commands with the debt check:
  `Finance/FinanceCommands.cs:189`, `:205`.
- Valve: **Implemented**, `Finance/FinanceCommands.cs:220`, UI
  `View/Rooms/Office/KeroseneMenu.cs:30`.
- Bad-fuel wear factor: **Implemented**, `Fleet/OperationsRules.cs:113` (the duplicate in
  `Player.Finance.Fuel.cs` was removed).
- Quality set on choice: **Implemented**. Picking a grade in the dialog runs `ChooseKeroseneCommand`
  (`Finance/FinanceCommands.cs`, from `View/Rooms/ArabAir/ArabAirRoom.cs`), so the grade switches even
  if no amount is bought, as in the original.
- Advisor on entry (with the tank test, `Player.KeroseneEntryRemark`), quality comment and tank-dry
  warning: **Implemented**, `Player.Finance.Fuel.cs`, `View/Rooms/ArabAir/ArabAirRoom.cs`.
- Random kerosene tips (open valve / buy tanks / buy fuel): **Implemented**, `AdvisorChatter`
  (`Staff/StaffModule.cs`); timing see staff-and-advisors.md §9.

---

## 7. Bank, overdraft and credit limit

### 7.1 Overdraft
- Cash may go negative without any agreement. Mandatory costs (rent, salaries, interest,
  penalties, maintenance, security) are always booked.
- **Voluntary purchases** refuse when `cash − price < −1,000,000`: shares, ads, kerosene, tanks,
  sabotage, planes, refits.

### 7.2 Loans
- **Credit limit:** `cr = (cash − loan)/2 − loan`; if loan < 200,000 then `cr = max(cr, 200,000 − cr)`,
  clamped to 0..2³¹−1. In practice you can borrow up to about half your net cash.
  - **Quirk:** the second term uses `cr`, not the loan. With negative cash and a small loan the
    limit becomes large (cash −2,000,000 and no loan gives 1,200,000).
  - Examples, no loan: cash 1,500,000 → 750,000; cash 100,000 → 150,000. After borrowing the
    full 750,000 the limit is (2,250,000 − 750,000)/2 − 750,000 = 0.
- Take: at least 1,000, not by phone. Repay: up to the loan, **cash is not checked** (you can repay
  into overdraft).
- The bank form shows interest as `rate·loan/1200` a month, but the charge actually follows §5.4.
- Finance-advisor perk (talent > 75): the loan rate becomes the average of the two rates, but
  **only** in the Normal/Hard/Final missions. **Not in the free game.**

**Implementation in OpenAT**
- Debt limit: **Implemented**, `Player.Finance.cs:37`, `:113`, checked in each command
  (`Finance/FinanceCommands.cs`).
- Credit limit with the quirk and `FixCreditLimit`: **Implemented**, `Finance/FinanceRules.cs:18–28`.
- Loans, the unchecked repayment and `FixLoanRepayCash`: **Implemented**,
  `Player.Finance.Bank.cs:14–39`, `Finance/FinanceCommands.cs:32`, `:49`.
- Advisor perk and `FixFinanceAdvisorPerk`: **Implemented**, `Player.Finance.Bank.cs:50`.

### 7.3 Bank bin
At game start, with probability 24 %, the bank's waste bin holds **100,000** for the first player
who clicks it. It never refills. **Implemented**, `Finance/FinanceSetup.cs:86`,
`Finance/FinanceCommands.cs:163`.

### 7.4 Security-guard rebate
If the security advisor's talent is above 20, `price/100·(talent/10)` is credited after most
purchases. That is at most **10 %**, at talent 100. It applies to tanks, kerosene, sabotage, planes,
ads, duty-free items and chocolates. **Implemented**, `Player.Finance.Upkeep.cs:41`.

---

## 8. End of day / week / month

### 8.1 Overnight order (economy-relevant)
1. Date +1; kerosene price update.
2. Per airline: statistics day shift (and the month shift every 30th day); route-use averaging and
   revocation; cash history; share-price history and company-value penalty; interest; security
   fees; rents; salaries; contract penalties; dividends and investor trust; flavour letters;
   plane maintenance and booking; wealth tax.
3. Auctions settle (3× fee); labour market.
4. Account-statement separator; **yesterday's balance sheet = today's**; today's is cleared.

So the overnight bookings count toward the day that just ended.

There are **no weekly money events**: flight plans repeat weekly but cost nothing. There are **no
monthly bills** either: every "monthly" price is charged daily /30. The only true month boundary is
the statistics month roll every 30th day.

### 8.2 Bankruptcy and game over
Checked per airline at the **morning briefing**:
- company image < −990 → out;
- **cash < −5,000,000** → out. The loan is **not** counted. There is no gradual bankruptcy
  process; the −3,000,000 secretary warning is the last notice.
- Being absorbed or liquidated by a rival who holds ≥ 50 % of your shares is also game over.

A bankrupt AI just disappears. Its share holdings are zeroed, but its assets are not redistributed.

**Implementation in OpenAT**
- Overnight pipeline: **Implemented**, `Finance/FinanceSystems.cs:39–67`. Orders follow
  `sim-core.md` §2.6/§2.7.
- Balance-sheet rollover: **Implemented**, `Finance/FinanceSystems.cs:276`.
- Game-over checks: **Implemented**, `Missions/MorningReviewSystem.cs:72–89`;
  `World.Finance.cs:178` (`RemovePlayer`).

---

## 9. Balance sheet and statistics

### 9.1 Daily balance sheet
It holds absolute amounts for the current day and is copied to "yesterday" overnight. Shown in the
notebook's finance page: **nothing without a finance advisor, a vague remark below talent 80,
and the full previous-day sheet (in thousands) at talent ≥ 80.**

| Side | Field | Fed by |
|---|---|---|
| Income | Tickets | route-flight income |
| Income | Contracts | passenger-contract premiums |
| Income | Deposit interest | reason 2001 |
| Income | Dividends received | 3141 |
| Expense | Kerosene | fuel + catering of every flight, plus imputed tank fuel |
| Expense | Loan interest | 2000 and 2002 (overdraft) |
| Expense | Staff | salaries |
| Expense | Penalties | 2060 only |
| Expense | Maintenance | 3110 |
| Expense | Gate / branch / route rent | 2050 / 2051 / 2052 |
| Expense | Dividends paid | 3140 |

Result = income − expenses. It drives the investor-trust update (§6.3 of the finance spec).

**Not in the sheet:** freight premiums, ferry credits, seat switches, plane purchases and sales,
refits, shares, ads, security fees, sabotage costs and fines, freight penalties (2065), phone and
shop purchases. The sheet is therefore an "operations" view, not the full cash flow.

### 9.2 Statistics screen (finance group)
- **Income:** routes (30), contracts (31), other (33: interest, dividends received, share sales
  and issues, plane sales, refit refunds, compensation).
- **Expenses:** rents (34), salaries (22), kerosene (35), maintenance (19), penalties (36),
  other (38: overdraft and loan interest, security, dividends paid, share buys, ads, shop items,
  phone, refits).
- **Balance** = income + expenses (the expense values are negative).
- Original quirks:
  - the day shift does not clear today's slot, so the additive series are **running totals since
    game start** (a bug, fixed by default: `GameOptions.FixStatisticsDayShift`);
  - dividends are recorded at their **yearly** amount (fixed by default, `FixDividendStatistics`);
  - series 32/37 ("sales"/"purchases") are never written;
  - freight income never appears.

**Implementation in OpenAT**
- Balance-sheet fields, mapping and total: **Implemented**, `Finance/BalanceSheet.cs:21–47`.
  Flight parts at `Fleet/FlightOps.cs:124–129`.
- Notebook page with advisor gating: **Implemented**, `View/Rooms/Planner/PlannerPanes.cs:735–769` (per-plane page `:770–789`).
- Series and statistics screen: **Implemented**; additive series start each day at 0 unless
  `FixStatisticsDayShift` is off (then "Days[0] not cleared" as in the original). Rules built on the
  running total read `StatSeries.Total` (robot tutorial order limit, the boss's "no flights" remark);
  the ATFS passengers-per-plane mission sums the last 5 days (`Missions/MissionRules.cs`).
  `Stats/Statistics.cs:87`, `:127`, `Finance/StatisticsScreen.cs:36–60`. The yearly-dividend quirk
  is fixed by default (`FixDividendStatistics`).

---

## Guide claims

Claims from the third-party guide (`docs/spec/local/faq/gamefaqs-11142-claims.md`) that touch this
topic.

| ID | Claim (paraphrase) | Verdict for Deluxe | OpenAT |
|---|---|---|---|
| F1 | Kerosene shop sells fuel and tanks; price changes daily; hoard when cheap. | **Confirmed.** Daily walk clamped 300..700 with a ~19-day cycle (§6.1); tanks per §6.2. | Implemented |
| F2 | Bank: loans and repayment (and shares). | **Confirmed** for loans; limit ≈ half net cash, with a quirk (§7.2). Which clerk does what is not checked here. | Implemented |
| F6 | With advisors the planner shows the balance sheet. | **Confirmed.** Needs a finance advisor; full figures only at talent ≥ 80 (§9.1). | Implemented (View) |
| F12 | Office picture shows the kerosene price and toggles tank vs market refuelling. | **Confirmed.** The tank panel shows price, capacity, contents and the valve (§6.4). | Implemented |
| A2 | Financial advisor gives the daily balance sheet; an unskilled one gives an empty sheet. | **Differs in detail.** No advisor → nothing; talent < 80 → vague text; ≥ 80 → yesterday's sheet. Talent > 75 also cuts the loan rate, but not in the free game. | Implemented (sheet), Implemented (perk) |
| A3 | Kerosene advisor helps buying and warns when fuel runs out. | **Confirmed, and more.** Buy/don't-buy on entry (K < 400 / > 600), quality comment, tank-empty warning, random tips (§6.5). | Implemented |
| A8 | Security manager gives a negotiation discount of up to ~10 %. | **Confirmed** as a rebate after purchase: talent/10 %, only if talent > 20, max 10 % (§7.4). | Implemented |
| E1 | Better equipment allows higher fares without complaints and fewer breakdowns; happy passengers raise image. | **Differs.** Good cabin quality makes groups happy *before* the price check, so there are fewer complaints. A fare above 3·Ref is still penalised in demand and image. Breakdowns depend on condition; only electronics/safety level 2 prevent the seasonal incidents. Image comes from the equipment levels and fare directly, not from happy passengers. | Implemented |
| E3 | Cabin items are one-off except food, which costs per passenger per flight. | **Confirmed:** 10/20/50 per **economy** passenger (first class free). Downgrades refund half the old value. | Implemented |
| T3 | Avoid planes with more than ~9000 l/h consumption. | **Rule of thumb only.** Fuel per km is burn/(160·speed) barrels, so judge burn relative to speed. No hard threshold exists. | n/a |
| T4 | Use ≥ 10 % of a route or lose it, and it can't be rented again; route passengers depend on image. | **Partly wrong.** The share is of route *demand*, as a moving average. Warnings come at days 10 and 15, and revocation after 20 days below 10 %. Re-renting is blocked for 7 days, not forever. The image dependency is **confirmed** (§2.2). | Implemented (`Routes/RouteOps.cs:189–198`, cooldown `:51`) |
| T7 | Sabotage fine is big; not enough money → game over. | **Differs.** Fine = evidence hints × 10,000, paid to the victim. There is no direct game over; you go out only if cash is below −5,000,000 at a morning briefing. | Implemented (`Items/SabotageEngine.cs:304–308`) |
| T15 | Bank waste bin sometimes holds money. | **Narrower:** only at game start, 24 % chance, 100,000, once (§7.3). | Implemented |
| Planes | Freight tonnes ≈ passengers / 10. | **Confirmed:** freight capacity = type seats / 10 tonnes. | Implemented (`Fleet/FlightOps.cs:85`) |
| Routes | Route rents $150k–$1.6M. | **Formula:** the quoted fee is per month **per direction**, charged daily /30 on both directions, so a pair costs 2× the quote (§5.3). Values not checked here (data). | Implemented |
