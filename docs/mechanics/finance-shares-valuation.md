# Finance, shares and company value (free game)

Game-mechanics reference for the bank, the stock market, company valuation, the free-game ranking
and the end of the game. Scope: **free game only**; mission-specific values are only mentioned
where the free game differs.

Sources: our clean-room specs `docs/spec/findings/economy.md` (§2, §3, §5–§7, §11, §16–§18),
`rooms-and-ui-6-airport-services.md` §2 (bank room), `rooms-and-ui-8-special-rooms.md` §6/§8
(telescope), `ai-robots.md` §5.2 (robot bank visits). Every number below was re-checked against
the reference source; formulas are restated in our own notation. No game text is reproduced; text
ids are given as `group id`.

Notation: all money is whole currency units in 64-bit integers; `/` is integer division truncating
toward zero, evaluated left to right. `K` is a share price (a double). `⌊x⌋` = truncation.

---

## 1. Starting position (free game)

| Value | Human | Computer |
|---|---:|---:|
| Cash | 1,500,000 | 1,500,000 (plus a hidden bonus pool of 500,000, see computer-players.md §6) |
| Loan (`Credit`) | 0 | 0 |
| Loan rate / deposit rate | 10 % / 3 % per year | same |
| Shares issued / issue ceiling | 10,000 / 20,000 | same |
| Own holding | 8,000 shares (80 %), cost basis 800,000 | same |
| Free float (held by nobody) | 2,000 | same |
| Share price (all 10 history slots) | 100 | same |
| Dividend / investor trust | 10 / 10 | same |

Players hold no shares of each other at the start. At game start there is a 24 % chance
(`Rand(100) > 75`, seeded with the start time) that banknotes lie in the bank's waste bin; the
first player to click them gets +100,000 (reason 2006). It is rolled once per game, not daily.

**Implementation in OpenAT**
- Implemented: start values `Assets/OpenAT/Sim/Mission.cs:36`; share setup
  `Assets/OpenAT/Sim/Finance/FinanceSetup.cs:53-60`; bank-bin roll `FinanceSetup.cs:86`; collecting
  the money `Assets/OpenAT/Sim/Finance/FinanceCommands.cs:163` (clicked in
  `Assets/OpenAT/View/Rooms/Bank/BankRoom.cs:110-121`).

---

## 2. Booking, overdraft and spending limits

Every ordinary money change goes through one booking routine: debt warnings, overflow guard,
balance change, account-statement line, balance-sheet field (§9). Cash may go negative at any
time; there is no overdraft agreement.

| Threshold | Effect |
|---|---|
| crossing −500,000 / −1,000,000 / −3,000,000 downward | secretary warning `Advi 2380/2381/2382` |
| `DEBT_LIMIT` = −1,000,000 | voluntary purchases are refused when `Money − price < −1,000,000` (shares, ads, kerosene, tanks, sabotage, planes, refits) |
| `DEBT_GAMEOVER` = −5,000,000 | bankruptcy at the next morning briefing (§8) |

Mandatory costs (rent, salaries, interest, penalties, maintenance, dividends) are always booked,
whatever the balance.

**Implementation in OpenAT**
- Implemented: booking routine `Assets/OpenAT/Sim/Player.Finance.cs:66`; thresholds
  `Player.Finance.cs:37-38`; warnings `Player.Finance.cs:97`; affordability check
  `Player.Finance.cs:113`.

---

## 3. Bank: loans and interest

The bank has two clerks: the **woman** handles the account and loans, the **man** handles shares,
dividends, share issues and takeovers. No bank action works by phone except asking for the balance.

### 3.1 Credit limit

```
cr = (Money − Credit)/2 − Credit
if Credit < 200,000:  cr = max(cr, 200,000 − cr)        (original; intended: 200,000 − Credit)
limit = clamp(cr, 0, 2³¹−1), offered rounded down to a multiple of 1,000
```

Note that `Money` already contains borrowed cash, so a loan reduces the limit twice: once through
`Money − Credit` staying the same and once through `− Credit`.

| Cash | Loan | `cr` | Limit |
|---:|---:|---:|---:|
| 1,500,000 | 0 | 750,000 | 750,000 |
| 100,000 | 0 | 50,000 | 150,000 (the 200,000 floor term) |
| 1,500,000 | 100,000 | 600,000 | 600,000 |
| 1,500,000 | 500,000 | 0 | 0 (refused, `Bank 120`) |
| −2,000,000 | 0 | −1,000,000 | **1,200,000** (quirk: the floor term uses `cr`) |

### 3.2 Taking and repaying

- **Take:** the form is preset to the limit. An amount below 1,000 does nothing. Otherwise
  `Credit += a`, `Money += a` (reason 2003).
- **Repay:** preset to `Credit/2`, clamped to `0..Credit`. **Cash is not checked**: repaying can push
  you into overdraft, or even past the bankruptcy line. `Credit −= a`, `Money −= a` (2004).
- The form shows monthly interest `rate·Credit/1200` and yearly `rate·Credit/100`, i.e. interest on
  the loan (which is not what the game actually charges, §3.3).
- **Finance advisor perk** (on entering the bank, talent > 75): only for the Normal/Hard/Final
  missions' base rates. In the free game (10 %) the check never matches, so there is no perk.

### 3.3 Overnight interest (the "savings" side)

There is no separate savings account: positive cash earns deposit interest automatically. Three
steps run each night, each on the balance left by the previous one:

1. `Money > 0`: `Money += Money·3/100/365` (deposit interest, 2001).
2. `Money < 0`: `Money += Money·3/100/365` (overdraft interest, 2002) — uses the **deposit** rate.
3. `Credit > 0`: `Money += −Money·10/100/365` (loan interest, 2000) — computed on **cash**, not on
   the loan. With negative cash you *receive* money; the balance sheet still lists it as an
   interest expense.

The loan itself never accrues interest. Rates never change during a free game.

**Worked examples (free game, one night)**
- 1,500,000 cash, no loan: +1,500,000·3/100/365 = **+123**.
- 2,000,000 cash, 1,000,000 loan: step 1 +164 → 2,000,164; step 3 −2,000,164·10/100/365 = **−547**.
  With the fix (interest on the loan): −1,000,000·10/100/365 = −273.
- −500,000 cash, 1,000,000 loan: step 2 −41 → −500,041; step 3 +500,041·10/100/365 = **+136**.

Practical consequence of the original: a loan is almost free while cash is small, and a large
overdraft costs only 3 % a year. The real cost of debt is the −5,000,000 bankruptcy line.

**Implementation in OpenAT**
- Implemented: credit limit `Assets/OpenAT/Sim/Finance/FinanceRules.cs:18-27` (fix
  `GameOptions.FixCreditLimit`, `Assets/OpenAT/Sim/GameOptions.cs:24`); take/repay
  `Assets/OpenAT/Sim/Player.Finance.Bank.cs:14-28`, commands `FinanceCommands.cs:32-61`; interest
  `FinanceRules.cs:43-67` (fixes `FixLoanInterest` :18, `FixOverdraftInterest` :26), run overnight by
  `Assets/OpenAT/Sim/Finance/FinanceSystems.cs:142`; perk `Player.Finance.Bank.cs:50`
  (`FixFinanceAdvisorPerk` :38); loan form `Assets/OpenAT/View/Rooms/Bank/BankDialogs.cs:231-305`.
- Fixed: the repay form clamps to `Player.MaxRepayment()` (`BankDialogs.cs:247`; = `Credit`, or
  cash-limited with `FixLoanRepayCash`), and the clerk confirms (`Bank 162`) only a repayment the
  command booked; nothing repaid ends the dialog quietly (`BankDialogs.cs:262-282`).

---

## 4. Shares: concepts and price

### 4.1 Holdings and free float

Each airline has `Anz` issued shares. Any airline can hold shares of any airline, including its
own. `free(a) = Anz(a) − Σ holdings of a by non-bankrupt airlines`. The free float is an anonymous
market: you buy from it and sell to it, and it receives dividends that leave the economy.

The issue ceiling grows every night: `Max = min(Max·105/100, 250,000,000)`.
Day by day from 20,000: 21,000; 22,050; 23,152; 24,309; 25,524; … ≈ 35,900 on day 12.

### 4.2 Hourly price drift

At every change of the game hour, for each airline `c` (0..3, also bankrupt ones), with trust `T`
and dividend `D`:

1. If `T > D`: `T = (T + D)/2`.
2. `K = (29·K + 10·T)/30` — the price is pulled toward the **anchor 10·T**.
3. `K += sin((24·Date + hour)/50) · K/180` — slow wave common to all airlines (±0.56 %).
4. `K += sin((24·Date + 100 + hour)/(c + 30)) · K/150` — per-airline wave (±0.67 %).
5. `K += sin(R) · K/50` with `R = Rand(1000)` read as radians, generator seeded with
   `Date + minute + hour` — noise of ±2 %. The generator is re-created with the same seed for every
   airline, so **all four airlines get the same noise value in a given hour**.
6. `K = max(K, 0)`.

Each hourly step closes 1/30 of the gap to the anchor; over 24 hours `(29/30)²⁴ ≈ 0.44` of the gap
remains, i.e. a half-life of about 20 game hours.

### 4.3 Overnight

1. The 10-day history shifts (`K[9..1] ← K[8..0]`).
2. **Company-value penalties.** With V1 = company value at yesterday evening's snapshot and V2 the
   evening before: V1 < V2 → K×0.97; V1 < V2 − 1,000,000 → another ×0.97; V1 < V2 − 5,000,000 →
   another ×0.95 (worst case ×0.894).
3. **Investor trust**, after dividends are booked, using today's balance-sheet result `S` (§9):
   - `S > 0` and the date is odd: `T += 1` if `T < D`.
   - `S < 0`: if `T < D/2` then `T += 1` on odd dates, else `T −= 1`.
   - `S = 0`: unchanged.

So trust climbs at most 1 every two days toward the dividend while you are profitable, and falls
1 per day while you lose money (settling near `D/2`). A dividend cut drops trust at once (hourly
rule 1).

### 4.4 Shocks

| Event | Price | Trust |
|---|---|---|
| Plane sabotage severity 1 / 2 / 3 / 4 / 5 | ×0.95 / ×0.90 / ×0.85 / ×0.75 / — | −1 / −2 / −3 / −4 / −5 (floor 0) |
| In-flight breakdown due to poor condition | ×0.70 | −1 if trust > 4 |

Buying and selling also move the price (§5.1).

### 4.5 What drives the price (summary)

| Driver | Effect |
|---|---|
| Dividend (via trust) | long-run level ≈ 10 × trust |
| Daily profit (balance-sheet result) | lets trust rise toward the dividend; losses pull it toward half |
| Company value falling day on day | −3 % to −10.6 % per night |
| Trading volume | each 1,000-share chunk ×Anz/(Anz∓1,000) |
| Sabotage, breakdowns | multiplicative hits |
| Waves and noise | ±~3 % per hour, mean-reverting |

Image does **not** enter the price formula directly; it only acts through profit.

**Worked example: dividend changes.** Start K=100, D=T=10. Raise D to 25 while profitable: T
reaches 25 after about 30 days (+1 on odd dates), so the anchor moves 100 → 250 over a month, with
the price lagging about a day behind. Cut D from 25 to 10: within the next four hour changes T goes
25 → 17 → 13 → 11 → 10, and the price slides back to ≈100 over the following days.

**Implementation in OpenAT**
- Implemented: hourly drift `Assets/OpenAT/Sim/Finance/FinanceRules.cs:82-95`, run by
  `FinanceSystems.cs:383-395` (same seed per airline, as in the original); overnight shift,
  penalty and ceiling growth `FinanceSystems.cs:127-139`, `FinanceRules.cs:97-104`; trust
  `FinanceRules.cs:106-116`, run after dividends `FinanceSystems.cs:170-179`; shocks
  `Assets/OpenAT/Sim/Player.Finance.Shares.cs` (`ShockSharePrice`, `ChangeTrust`), free float
  `Assets/OpenAT/Sim/World.Finance.cs:64`.

---

## 5. Trading, issuing and dividends (human, at the bank)

### 5.1 Buying and selling shares

Any active airline, including your own. Not by phone.

- **Buy n:** refused if `Money − n·K < −1,000,000` (`Bank 6000`). Otherwise in chunks of up to
  1,000: holding += m, cost += ⌊m·K⌋, then `K = K·Anz/(Anz − m)`. You pay
  `cost + cost/10 + 100` (reason 3150). The paid total is added to your cost basis.
- **Sell n:** cost basis ×(held − n)/held; chunks: holding −= m, proceeds += ⌊m·K⌋,
  `K = K·(Anz − m)/Anz`. You receive `proceeds − proceeds/10 + 100` (3151; the +100 is a sign slip,
  intended −100).
- The amount can be entered as a share count, as % of the target's issued shares, or as money ÷
  price; it is clamped to the free float (buy) or your holding (sell).

**Worked example.** Rival at K=100, Anz=10,000. Buy 2,000: chunk 1 costs 100,000, K→111.11;
chunk 2 costs 111,111, K→123.46. Paid 211,111 + 21,111 + 100 = **232,322**. Selling the same 2,000
straight back: 123,456 + 111,111 = 234,567 proceeds, received 234,567 − 23,456 + 100 =
**211,211**. A round trip costs about 9 %; the price impact itself is symmetric and then decays
toward 10·trust.

### 5.2 Issuing new shares (emission)

- Available: `avail = (Max − Anz)` rounded down to 100. Refused if `avail < 10,000` (`Bank 3000`)
  or `K < 10` (`Bank 3001`). On day 0 avail is exactly 10,000, so an issue is possible at once.
- Size: 10/25/50/75/100 % → `N = ⌊pct·(Max − Anz)/100⌋`.
- Issue price `E = ⌊K⌋ − 5`, `−3` or `−1`; the market takes `M = N`, `0.8·N` or `0.6·N` shares.
- On confirm (K0 = ⌊K⌋):
  1. proceeds `+M·E` (3162); bank fee `−(N·E/10/100·100)` (3160): 10 % of the nominal value of
     **all N** shares, rounded down to 100;
  2. `K = (K·Anz + E·M)/(Anz + M)`;
  3. dilution compensation: you pay `(Anz − own)·(K0 − K)` (3161) — for every outside share,
     including the free float; each other airline receives `holding·(K0 − K)`;
  4. `Anz += N`; your own holding `+= N − M` (unsold shares stay with you).

**Worked example (Max − Anz = 20,000, K = 100, own 8,000 of 10,000):**

| Choice | N | E | M | Proceeds | Fee | New K | Dilution paid | Net cash | Own stake after |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 100 %, −5 | 20,000 | 95 | 20,000 | 1,900,000 | 190,000 | 96.67 | 6,666 | +1,703,334 | 8,000/30,000 = 27 % |
| 100 %, −1 | 20,000 | 99 | 12,000 | 1,188,000 | 198,000 | 99.45 | 1,090 | +988,910 | 16,000/30,000 = 53 % |

The −5 option raises most cash but hands 22,000 shares to the free float — enough for a rival to
buy a majority (§6). The −1 option keeps control.

### 5.3 Dividends

- Set by the airline itself at the bank (male clerk): 0..25 per share **per year**. Default 10.
- Paid every night: `−(Anz − own)·D/365` (3140), for all outside shares including the free float.
- Received every night: `+Σ_{other active airlines a} holding(a)·D_a/365` (3141). No dividend on
  your own shares.
- Shown yield on the share card: `D·100/⌊K⌋` %.

Example: at the start, 2,000 outside shares × 10 / 365 = **54 per day**; at D=25, 136 per day. After
a 100 % issue at −5 (22,000 outside), D=25 costs 1,506 per day. The dividend is cheap relative to its
effect on the price anchor (§4.2).

**Implementation in OpenAT**
- Implemented (Sim): `Player.BuyShares` / `SellShares` `Assets/OpenAT/Sim/Player.Finance.Shares.cs:43`,
  `:71` (fix `FixShareSaleFee`, `GameOptions.cs:30`); commands `FinanceCommands.cs:77`, `:96`;
  issue `Player.Finance.Shares.cs:97-145`, command `FinanceCommands.cs:115` (fix
  `FixDilutionFreeFloat` :34); dividends `Player.Finance.Shares.cs:152` (fix `FixDividendStatistics`
  :32); dividend command `FinanceCommands.cs:64`; share-issue dialog
  `Assets/OpenAT/View/Rooms/Bank/BankDialogs.cs:165-210`.
- Implemented (View): the share buy/sell form `ShareMenu` (`BankDialogs.cs:315-445`, opened from
  `:219-224`): the original form.gli layout, count / % / money entry with the calculator, clamped to
  the free float or the holding, start value half of that; OK runs `BuySharesCommand` /
  `SellSharesCommand`, `Bank 6000` on the debt limit, re-entry 2030/2130 (2031/2131 after cancel).
- Implemented (View): the dividend form `DividendMenu` (`BankDialogs.cs:453-508`, opened from
  `:212-216`), 0..25 with the calculator, OK runs `SetDividendCommand`. Fix: its payout lines show
  the booked amount, outside shares × D (the original multiplies by all issued shares).
- Fixed: a share issue offers and books the same N = pct·(rounded room)/100, the count the
  original intended (`Player.SharesForPercent`, `Player.Finance.Shares.cs:103`; switch
  `GameOptions.FixShareIssueRounding`, off = the original's unrounded booking).

---

## 6. Takeover and liquidation

### 6.1 Request (male clerk, "shareholder rights")

Checks in order: target is not you (`Bank 2520`), not out (2521), you hold some of its shares
(2525), you hold **≥ 50 %** of its issued shares (2522), the target holds **< 30 %** of *your* issued
shares (2523). Then choose **absorb** or **liquidate**. The order is executed at the next morning
briefing. Only humans can request it; computer players never take over anyone.

### 6.2 Absorb

- The target's staff joins you (unassigned). Its planes join you with empty schedules, parked at
  your home; for a computer target, enough pilots/attendants for them are taken from the reserve
  pool (computers have no staff of their own).
- You take over its cash (may be negative, 3180), its loan, its issued-share count and its holdings
  of every airline. Third parties' holdings of the target become holdings of you, 1:1.
- Gates move over; routes and branches move over when you lack them or the target's rank is better.

### 6.3 Liquidate

- Staff returns to the pool; planes are sold at full market value into the target's cash; gates,
  routes and branches are freed (others move up in rank).
- The target sells all its holdings (including its own shares) at current prices.
- Then `Money − Credit` of the target is shared among all remaining holders of its shares
  pro rata (3181). **This can be negative**: holders inherit the debt.

Either way the target is out. A human target means game over for that player.

### 6.4 How a majority becomes possible

At the start you can buy at most the 2,000-share free float (20 %). A majority needs more float:

- the target issues shares (a computer issues the whole remainder at −5 once ≥ 25,000 are
  available, roughly from day 12; 80 % goes to the market, but it rebuys half the issue at once, so
  the float grows by only 30 % of the issue);
- a computer whose own price is ≥ 200 sells its own stake down to 20 % (on in the free game); the
  airline in slot 1 always pays a dividend of 25, so its price drifts toward 250 and it sells;
- a computer with a loan above 3,000,000 fire-sells own shares after day 10.

**Implementation in OpenAT**
- Implemented: request `Assets/OpenAT/Sim/Finance/FinanceCommands.cs:136-161`, dialog
  `Assets/OpenAT/View/Rooms/Bank/BankDialogs.cs:132-160`; execution
  `Assets/OpenAT/Sim/World.Finance.cs:198-269` (absorb 207-244, liquidate 245-262), run at the
  morning review `Assets/OpenAT/Sim/Missions/MorningReviewSystem.cs:25`.
- Reachable for a human through the share buy form (§5.1); test
  `FinanceTests.BuySharesToMajority_MakesTakeoverRequestable`.
- Fixed: the "target holds ≥ 30 % of you" refusal shows the target's stake in you
  (`BankDialogs.cs:148-153`); the original's number window shows *your* stake in the target there
  (an original slip). `Bank 2524` gets the target airline as in the original (`BankDialogs.cs:155`).

---

## 7. Company value

```
value = Σ_planes planeValue + Σ_airlines a holding(a)·K_a + Money − Credit
planeValue = typePrice·Z/10000·Z·(year − 1900)/120        (Z = condition 0..100)
sponsored (starting) planes: planeValue/10
```

Not included: kerosene in the tank, routes, branches, gates, items. Own shares do count.
The value is a snapshot refreshed at the 18:00 evening screen, the statistics screen and the
morning briefing. It feeds the overnight price penalty (§4.3) and the highscore table (§8.2).

**Worked example.** A plane with type price 20,000,000, condition 90, built 1985:
20,000,000·90/10000 = 180,000; ·90 = 16,200,000; ·85/120 = **11,475,000** (1,147,500 if
sponsored). Start of a free game with two such sponsored planes: 2·1,147,500 + 8,000·100 +
1,500,000 = **4,595,000**. If the value then drops by 1.5 M overnight, the share price is
multiplied by 0.97·0.97 = 0.941.

**Implementation in OpenAT**
- Implemented: `Assets/OpenAT/Sim/Finance/FinanceRules.cs:175-185`; plane value
  `Assets/OpenAT/Sim/Fleet/Plane.cs:144-153`; snapshot `Assets/OpenAT/Sim/World.Finance.cs:82-119`
  (evening refresh `FinanceSystems.cs:401-406`).

---

## 8. Ranking, end of the free game, bankruptcy

### 8.1 Telescope ranking

- Each look through the telescope costs **1** (reason 3300). In the free game it shows the Mount
  Rushmore board.
- Ranking criterion in the free game: **passengers carried** — the running total of economy-class
  passengers on route and passenger-order flights since the start (first-class passengers, freight
  and ferry flights are not counted). Sorted descending, ties by airline index; a bankrupt airline's
  row stays empty.
- The same number drives the computers' hidden catch-up bonus (computer-players.md §6).

### 8.2 Highscores

For a human, every statistics refresh offers the current company value to a 6-entry highscore table
(one entry per session, kept if better; skipped in a cheated session).

### 8.3 End conditions

The free game has **no win condition and no day limit**; there is no mission report at the
briefing. It ends only when the local player is out:

1. **Image < −990** at a morning briefing.
2. **Cash < −5,000,000** at a morning briefing (the loan is not counted; there is no grace period
   after the −3 M warning).
3. **Taken over** (absorbed or liquidated) by another human.

The same checks remove computer players: a bankrupt computer is marked out and every holding of
and by it is zeroed; its assets are not redistributed (its routes and gates probably stay blocked;
open question Q-12 in economy.md).

**Implementation in OpenAT**
- Implemented: telescope fee `Assets/OpenAT/Sim/Items/ItemCommands.cs:342-347`, charged on entry
  `Assets/OpenAT/View/Rooms/Telescope/TelescopeRooms.cs:85`; rating
  `Assets/OpenAT/Sim/Missions/MissionRules.cs:117-126` (economy passengers counted at
  `Assets/OpenAT/Sim/Fleet/FinanceIntegration.cs:121`); board `TelescopeRooms.cs:566-603`; free game
  skips the mission report `MorningReviewSystem.cs:28`; image and bankruptcy checks
  `MorningReviewSystem.cs:87-88`; elimination `World.Finance.cs:178-192`.
- Implemented: highscore table `Assets/OpenAT/Sim/Stats/Highscores.cs` (offer rule, plain-text file
  format); offered from `UpdateStatistics` via `Assets/OpenAT/Sim/World.Highscores.cs` (humans only, not in a
  cheated session; session id and cheat flag are saved with the game, so a loaded game keeps its row and a
  cheated save stays cheated — the original forgot the flag on load); kept in `highscores.txt` in the save
  folder `Assets/OpenAT/View/Session/HighscoreStore.cs`; menu page `Assets/OpenAT/View/Screens/HighscoreScreen.cs`.
  Nothing sets the cheat flag yet (OpenAT has no cheats).

---

## 9. Daily balance sheet ("Bilanz")

A per-day income/expense sheet, separate from the account statement. Each listed booking adds its
**absolute** amount to one field. Today's sheet is copied to "yesterday" at the end of the night,
after the night's bookings, so "yesterday" = the day's flights + the following night's fixed costs.

| Income | Source |
|---|---|
| Deposit interest | §3.3 step 1 |
| Dividends received | §5.3 |
| Tickets | route-flight revenue (added at flight booking) |
| Orders | passenger-order premiums (added at flight booking) |

| Expense | Source |
|---|---|
| Interest | loan and overdraft interest (even when step 3 credits money) |
| Dividends paid | §5.3 |
| Kerosene | pump fuel + catering per flight, plus the imputed value of tank fuel used |
| Staff | salaries |
| Penalties | passenger-order penalties (freight penalties are not mapped) |
| Maintenance | daily maintenance |
| Gate / branch / route rent | daily rents |

`result = Σ income − Σ expenses`. Not in the sheet at all: freight income, share trades and issues,
plane purchases and sales, ads, security fees, sabotage, tank purchases, shop items, phone. The
result is what investor trust reacts to (§4.3) and what computer sympathy compares (computer-players.md
§5).

**Display** (planner / laptop "daily balance" page): no finance advisor → a "no advisor" text;
advisor talent < 80 → a vague text; ≥ 80 → three pages of yesterday's sheet in thousands: incomes and
their sum; the nine expenses (shown as positive numbers) and their sum; total income, total expense
and the result. The per-plane weekly earnings page also needs a finance advisor (any talent) and
starts with an intro page.

**Implementation in OpenAT**
- Implemented: sheet `Assets/OpenAT/Sim/Finance/BalanceSheet.cs:7-50` (reason 2061 goes to Orders like
  the original; income/expense sums `:21-24`); flight fields
  `Assets/OpenAT/Sim/Fleet/FlightOps.cs:124-128`; tank fuel `Assets/OpenAT/Sim/Player.Finance.Fuel.cs:96`;
  rollover `FinanceSystems.cs:276-289`.
- Implemented: planner pane `Assets/OpenAT/View/Rooms/Planner/PlannerPanes.cs:735-769` (three pages,
  13 px rows, sums and result; title "balance of" + weekday). Deliberate fix: the title names yesterday,
  the day the figures cover (the original names today). Per-plane earnings `:770-789` (finance advisor
  gate, intro page, 13 planes a page, losing planes in red); pages `:699-709`. Without the advisor the
  remark is a single page (the original keeps the full page count and repeats the remark).

---

## 10. Bank share cards

Hovering the wall display in the bank (`(378,33)-(436,160)`, cell = column (x−378)/30, row
(y−33)/64, airline = column + 2·row) shows a card for that airline:

- **On the market:** current price (whole units), dividend, yield `D·100/⌊K⌋` %.
- **Your holding:** shares of that airline you own, what you paid for them (cost basis incl. fees),
  their current value `⌊K⌋·owned`.
- **Shareholders:** each of the four players' stake `holding·100/Anz` %.
- A 10-day mini chart scaled to 1.5 × the 10-day high.

The four wall charts show each active airline's last 10 closes on the same 0..1.5×max scale.

**Implementation in OpenAT**
- Implemented: `Assets/OpenAT/View/Rooms/Bank/BankRoom.cs:155-178` (wall charts), `:196-268` (card).
  The card uses the original coordinates; the "your holding" value column moves right of its
  labels when one is wider than x 42..98 (the English market-value label, `BankRoom.cs:233`).

---

## Guide claims

Claims from the third-party guide (`docs/spec/local/faq/gamefaqs-11142-claims.md`), checked against the
source.

| Id | Claim (paraphrased) | Verdict |
|---|---|---|
| F2 | Bank: the man handles shares, the woman handles loans | **Confirmed.** Man = shares, dividend, issues, takeovers; woman = balance, take/repay loan. |
| F14 | Telescope costs $1; the free game ranks by passengers carried | **Confirmed.** 1 per look; free-game rating = running economy-class passenger count of route and order flights (first class and freight excluded). |
| F6 | With advisors the planner also shows rival info and the balance sheet | **Confirmed with detail.** Balance sheet needs a finance advisor and full figures only at talent ≥ 80; per-plane earnings need a finance advisor; rival info needs the informer. |
| A2 | Financial advisor: daily balance sheet, per-plane earnings; unskilled → empty sheet | **Mostly confirmed.** Below talent 80 a vague text replaces the figures (not literally empty). |
| T15 | The bank waste bin sometimes holds money | **Confirmed, but once per game:** 24 % chance at game start, +100,000, first come first served. |
| T1 | Home airport suggestions | Not a finance mechanic; the home choice only sets the home branch rent. Out of scope here. |
