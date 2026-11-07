# Economy & player finances (behaviour spec, clean-room)

Scope: player money and debt, the daily balance sheet, the account statement, bank (loans, interest, overdraft), stock market (prices, trading, issuing, dividends, investor trust, takeover and liquidation), company value, image (company and per route), staff (pool, hiring, salary, happiness, strikes, advisor effects), advertising, kerosene price and storage tanks, maintenance fees, rents, security fees, the minor money sinks and sources, the statistics data series, newspapers, the boss's economic evaluation, bankruptcy and game over, the hidden AI money bonus, and taxes.

Not covered here (cross-references only): flight revenue and passenger demand (flights/routes topic), order and freight contracts (orders topic), sabotage (sabotage topic), plane purchase and refits (planes topic), and the room UIs. The UIs are in `rooms-and-ui-6-airport-services.md`, which covers the bank room, personnel office, boss office and bulletin board.

Everything is restated in my own words from the GOG source release (`docs/spec/local/gog_src`). I cross-checked against the community SDL port; I found no behavioural differences in any function cited here (only type-cast fixes). In-game text is never quoted; it is referenced as `TOKEN_x id` (dialog or StandardTexte tables).

Confidence tags: **[H]** read directly and unambiguous · **[M]** inferred from the wiring · **[L]** a guess.
Integer maths: unless stated otherwise all money maths is integer. Division truncates toward zero (C semantics) and is evaluated left to right exactly as written in the formulas, so `a*b/c/d` means `((a*b)/c)/d`. Money is 64-bit. Balance-sheet fields are 32-bit (clamped, see §3).
Time: one game hour = 60000 time units; a day runs 9:00–18:00 for play; "overnight" means the day rollover (`SIM::NewDay`).
`Rand(n)` (TEAKRAND) returns 0..n−1.

---

## 1. Per-player finance state

| Field (orig.) | Meaning | Unit / range |
|---|---|---|
| `Money` | Cash balance. Can go negative (overdraft). | currency, int64 |
| `Credit` | Outstanding bank loan (always ≥ 0). | currency, int64 |
| `Bonus` | Hidden bonus that the AI still has to receive (§18). | currency |
| `MoneyPast[20]` | Rolling history of `Money` at each day rollover (oldest first). | currency |
| `SollZins` / `HabenZins` | Loan interest rate / deposit interest rate, in % per year. | 0..255 |
| `Bilanz`, `BilanzGestern` | Today's and yesterday's balance sheet (§3). | 32-bit fields |
| `History` | Account statement: the last 100 entries plus a carried "historic" opening balance (§4). | — |
| `Image` | Company image. | −1000..+1000 |
| `AnzAktien` | Shares issued. | count |
| `MaxAktien` | Shares that may be issued (ceiling for new issues). | count, ≤ 250,000,000 |
| `OwnsAktien[4]` | How many shares this player holds of each airline 0..3 (including itself). | count |
| `AktienWert[4]` | Purchase cost basis of those holdings. | currency |
| `Kurse[10]` | Share price history. `Kurse[0]` = current (float), `[1..9]` = previous day closes. | currency/share, double |
| `Dividende` | Dividend this player pays, in currency per share **per year**. | 0..25 (human) |
| `TrustedDividende` | "Investor trust": a lagged copy of the dividend that drives the share price. | ≥ 0 |
| `KerosinKind` | Fuel quality bought on the market: 0 = premium, 1 = normal, 2 = cheap. | — |
| `Tank` / `TankInhalt` | Tank capacity / tank contents. | barrels |
| `TankOpen` | Planes refuel from the tank when true. | bool |
| `TankPreis` | Average purchase price per barrel of the tank contents (double). | currency/barrel |
| `BadKerosin` | How many barrels in the tank are cheap fuel. | barrels |
| `MechMode` | Maintenance contract level 0..3 (§12). | — |
| `SecurityFlags` | Subscribed security services (bitmask, §14). | — |
| `Gewinn` | Running profit counter used by the "earn X" mission. | currency |
| `StrikeHours`, `StrikePlanned`, `StrikeEndCountdown`, `StrikeEndType`, `DaysWithoutStrike` | Strike state (§9.7). | — |
| `Statistiken[40]` | Statistics series (§16). | — |

Global: `Sim.Kerosin` (today's market price per barrel, int, 300..700) and `Sim.KerosinPast[10]` (price history, newest at index 9). Traceability: `class.h: PLAYER, CBilanz, SValue, HISTORY`.

---

## 2. Starting conditions — `Sim.cpp: SIM::ChooseStartup`

### 2.1 Starting cash and loan (`InitMoney` table) [H]
Rows are selected by mission. Humans get column A. AI players also start with column A, and their hidden `Bonus` = |B − A| (§18).

| Mission | Human cash (A) | Human loan | AI cash target (B) | AI loan |
|---|---:|---:|---:|---:|
| Free game | 1,500,000 | 0 | 2,000,000 | 0 |
| Tutorial | 5,000,000 | 0 | 200,000 | 0 |
| First (passengers) | 5,000,000 | 0 | 500,000 | 0 |
| Easy (profit) | 3,000,000 | 0 | 1,000,000 | 0 |
| Normal (connect cities) | 2,000,000 | 0 | 3,000,000 | 0 |
| Hard (image) | 1,800,000 | 0 | 4,000,000 | 0 |
| Final (rocket) | 1,500,000 | 0 | 6,000,000 | 0 |
| Add-on 01 (pay off debt) | 3,000,000 | 10,000,000 | 1,000,000 | 10,000,000 |
| Add-on 02..10 | 3,000,000 | 0 | 1,000,000 | 0 |
| ATFS 01..10 | 3,000,000 | 0 | 3,000,000 | 0 |

The "loan" column becomes `Credit`; the cash is in `Money` (the loan is not added on top).
Note: the define comments in `defines.h` label the add-on missions differently from how the code uses them. Trust the code: `GetMissionRating` treats ADDON01 as the debt mission. [H]
Row indexing: rows are indexed by `(difficulty+1)`, with 5 padding rows for difficulty ids 6..10. ATFS uses the row `(diff−ATFS01+22)`. [H]

### 2.2 Interest rates by mission [H]
| Mission | Loan % (`SollZins`) | Deposit % (`HabenZins`) |
|---|---:|---:|
| Tutorial, First, Easy | 5 | 5 |
| Normal | 6 | 4 |
| Free game, Hard | 10 | 3 |
| Final | 15 | 1 |
| Add-on 01..10 | 15 | 1 |
| ATFS 01..10 | 15 | 0 |

### 2.3 Other starting values [H]
- Image = 0 (Add-on 06: 300).
- Shares: `AnzAktien` = 10,000, `MaxAktien` = 20,000, `Dividende` = 10, `TrustedDividende` = 10, all 10 `Kurse` = 100. Each player holds 80 % of its own shares (8,000), cost basis = 8,000 × 100 = 800,000. The other 2,000 are free float ("the market"). Players hold no shares of each other.
- `MoneyPast` = 20 zeros. History empty with `HistoricMoney` = starting cash.
- Fuel: `KerosinKind` = 1 (normal), `Tank` = 100, `TankInhalt` = 0, `TankPreis` = 0, `TankOpen` = false, `BadKerosin` = 0. ATFS 09/10 AI: `Tank` = 5000 and open.
- `MechMode` = 2 (mechanic).
- Home branch: city rank 1 at `Cities[home].BuroRent` per month. Gate: one gate rented at 1000 per month.
- Two starting planes. They are "sponsored", so they are worth 1/10 of the normal price in valuations (§7). Easy: types 100+114; Normal: 100+104; others: 100+105. (Plane details: planes topic.)
- Humans only: enough pilots and stewardesses are hired from the reserve pool to fill the starting planes exactly (random picks, seeded with start time + player number). The AI has virtual staff (§9.9).
- Missions harder than Normal (except Add-on 02): each player also gets one starting route out of the home airport (routes topic).
- `DaysWithoutStrike` = 20, `DaysWithoutSabotage` = 0.
- **Money in the bank bin:** at game start, with probability 24 % (`Rand(100) > 75`), banknotes lie in the bank's waste bin. The first player to click them gets +100,000 (reason 2006), and the bin is emptied for everyone. [H] `Sim.cpp: ChooseStartup`, `Bank.cpp: OnLButtonDown`
- Kerosene: price 500; `KerosinPast[9]` = 508. Entries 8..0 are back-filled by a noisy walk (each previous entry = next entry + Rand(20) − 10 − ⌊sin((Date−c+Rand(6))/3)·20⌋ − ⌊sin((Date−c)·1.7)·20⌋). The walk is not clamped. [H]

---

## 3. Booking money — `Player.cpp: PLAYER::ChangeMoney`

Every normal money change goes through one booking routine with (amount, reason code, text params). Effects, in order: [H]
1. Debt-warning messages, from the secretary (`BERATERTYP_GIRL`). These fire when the balance crosses downward through −500,000 (`TOKEN_ADVICE 2380`), −1,000,000 (2381) or −3,000,000 (2382). The check is "old > limit and new < limit".
2. **Overflow guard:** a positive amount that would overflow the 64-bit balance is silently dropped.
3. `Money += amount`.
4. An account-statement line is appended (§4). Its text is `TOKEN_MONEY <reason>`, formatted with the params.
5. The absolute value of the amount (clamped to 2³¹−1) is added to one balance-sheet field, chosen by reason:

| Reason | Balance-sheet field |
|---|---|
| 2000, 2002 | Loan interest expense (`SollZinsen`) |
| 2001 | Interest income (`HabenZinsen`) |
| 2021 | Kerosene (`Kerosin`) — no caller uses 2021, see below |
| 2030 | Tickets — no caller uses 2030 |
| 2050 / 2051 / 2052 | Gate rent / branch rent / route rent |
| 2060 | Contract penalties (`Vertragsstrafen`) |
| 2061 | Orders income — no caller uses 2061 |
| 2070 | Staff (`Personal`) |
| 3110 | Maintenance (`Wartung`) |
| 3140 | Dividends paid (`SollRendite`) |
| 3141 | Dividends received (`HabenRendite`) |
| others | none |

Because the field always receives the **absolute** amount, a booking with an expense code that actually gains money still counts as an expense (see the interest bug, §5.3).

Flight revenue and flight fuel cost are not booked through these codes. `CFlugplanEintrag::BookFlight` (Schedule.cpp) adds directly to `Bilanz.Tickets` (route flights), `Bilanz.Auftraege` (passenger orders) and `Bilanz.Kerosin` (fuel + catering). It then books the net flight result as one `ChangeMoney` with reason 2099+objectType. Freight-order income is **not** added to the balance sheet (flights topic). [H]

**Money changes that bypass ChangeMoney** (no balance sheet, and for most no history line): phone units (§15.1, history line adjusted separately); AI bank actions and AI share trades (§18); liquidation proceeds credited to the liquidated airline (§6.8); the loan field itself (`Credit +=/−=`). [H]

### 3.1 Reason-code table (meaning inferred from call sites and comments) [H unless marked]
| Code | Meaning | Sign |
|---|---|---|
| 2000 | Loan interest (overnight) | see §5.3 |
| 2001 | Deposit interest | + |
| 2002 | Overdraft interest | − |
| 2003 / 2004 | Loan taken / loan repaid | + / − |
| 2006 | Money found in the bank bin | + |
| 2010 / 2011 | Plane bought / plane sold | − / + |
| 2040 / 2041 / 2042 | Auction won: branch / route / gate (one-off fee) | − |
| 2050 / 2051 / 2052 | Daily gate / branch / route rent | − |
| 2060 / 2065 | Passenger-order penalty / freight-order penalty. The two codes are used swapped at the manual-cancel call sites in `Stdraum.cpp`. 2065 has no balance-sheet mapping. | − |
| 2070 | Daily salaries | − |
| 2080 | Sabotage job paid (sabotage topic) | − |
| 2090 / 2091 | Kerosene bought into tank / storage tank bought | − |
| 2100–2103 | Flight result: route / order / empty (ferry) / freight. The code is 2099 + objectType. | ± |
| 2110 / 2111 | Plane refit / seats removed or installed for a freight switch (15,000) | − |
| 2200 / 2201 | Sabotage fine paid / compensation received (§17.3) | − / + |
| 3110 | Daily maintenance | − |
| 3120–3125 | Ad campaign, tier 0..5 (§10) | − |
| 3130 | Bodyguard rebate (§9.8) | + |
| 3140 / 3141 | Dividends paid / received | − / + |
| 3150 / 3151 | Shares bought / sold | − / + |
| 3160 / 3161 / 3162 | Share issue: bank fee / dilution compensation / proceeds | − / ± / + |
| 3170 | Airport expansion (1,000,000) | − |
| 3180 / 3181 | Takeover: absorbed cash / liquidation payout to shareholders | ± |
| 3200+t | Phone call (history label only; t = mobile bit + 2·long-distance level) | − |
| 3300 | Entering the island/space rooms (−1; flavour) [M] | − |
| 3400 | Rocket / space-station part (prices §15.6) | − |
| 3500 | Sabotage damage to the victim (2,000 / 8,000 / 40,000 / 70,000) | − |
| 3501 | Weather/season incident (75,000, §8.4) | − |
| 3502 | Special sabotage: 1,000,000 moves from victim to saboteur (sabotage topic) | ± |
| 3503 | Daily security fees (§14) | − |
| 3600 | Wealth tax (§15.5) | − |
| 3700 / 3701 | Money gift between players (network chat) | + / − |
| 9999 | Generic shop purchase (duty free etc.; the label is the item name) | − |

---

## 4. Account statement (HISTORY) — `Player.cpp: HISTORY::*`, `Tips.cpp: DrawMoneyTip` [H]
- 100 lines, a FIFO. Each line has an amount, a description and a "by phone" flag. When a new line is appended, the oldest line's amount is added to `HistoricMoney` before that line drops out. `HistoricMoney` therefore always equals the balance before the oldest visible line.
- Every overnight rollover appends a separator line (amount 0, a row of dashes). Separator lines are not drawn but do take a slot.
- Starting a phone call appends a "by phone" line of −1. Each further unit subtracts 1 from the newest "by phone" line (§15.1).
- Display (status-bar $ click, `MENU_KONTOAUSZUG`): 10 pages × 10 lines. Each page shows the loan, the opening balance (HistoricMoney + all lines before the page), the lines, and the closing balance.

---

## 5. Bank — `Bank.cpp`, `Dialog.cpp: TALKER_BANKER1/2`, `Stdraum.cpp: MENU_BANK`

### 5.1 Loans [H]
- **Credit limit:** `cr = (Money − Credit)/2 − Credit`. If `Credit < 200,000`, then `cr = max(cr, 200,000 − cr)`. The result is clamped to 0..2³¹−1.
  - Quirk: the second term uses `cr`, not `Credit`. A player with negative cash and a small loan therefore gets a *large* limit: cash −2M, loan 0 gives cr = −1M, so the limit is 1.2M. The intent was probably `200,000 − Credit`. Decide: replicate or fix (Q-1).
  - Example with no loan: cash 2M → limit 1M; cash 100k → 150k.
- **Take a loan:** the form opens with the limit rounded down to 1000. If the limit is 0, the banker refuses (`TOKEN_BANK 120`). Not possible by phone (150). The amount can be edited with the calculator and is clamped to 0..limit. On OK: if amount ≥ 1000, then `Credit += amount` and `Money += amount` (reason 2003). Otherwise nothing happens and the generic reply runs.
- **Repay:** the form opens with Credit/2; if there is no loan, the banker refuses (140); not possible by phone. The amount is clamped to 0..Credit. **Cash is not checked**: repaying can push the player into overdraft. Then `Credit −= amount`, `Money −= amount` (2004).
- The form shows the current loan, the rate, the monthly interest `SollZins·Credit/1200` and the yearly interest `SollZins·Credit/100`, then the same figures after the change. So the displayed interest is based on **Credit**, while the code actually charges it on **Money** (§5.3).
- **Finance advisor perk** (bank entry): if the finance advisor's talent is > 75 and the loan rate still equals the base rate for Normal (6), Hard (10) or Final (15), the rate becomes `(SollZins + HabenZins)/2`, i.e. 5 / 6 / 8. An advisor message is shown (`TOKEN_ADVICE 5000`). Free game (10 %) and the add-ons/ATFS (15 %) are not covered by the difficulty check, so there is no reduction there (Q-2).

### 5.2 Overdraft and spending limits [H]
- Cash can go negative without an explicit overdraft agreement. There is no fixed overdraft limit on bookings themselves.
- `DEBT_LIMIT` = −1,000,000. Most voluntary purchases refuse when `Money − price < −1,000,000`: share buys, ads, kerosene, tanks, sabotage, plane buys and refits. Mandatory costs (rent, salaries, interest, penalties, maintenance) are always booked.
- Warning thresholds −500k / −1M / −3M are listed in §3. Bankruptcy is at −5,000,000 cash (§17).

### 5.3 Overnight interest — `PLAYER::NewDay` [H]
These three steps run in this order, each using the balance as updated by the previous step:
1. If `Money > 0`: `Money += Money·HabenZins/100/365` (2001). Also added to stat E_SONSTIGES.
2. Else if `Money < 0`: `Money += Money·HabenZins/100/365` (2002, overdraft interest, a negative amount). Note it uses the **deposit** rate. Added to stat A_SONSTIGES.
3. If `Credit > 0`: `Money += −Money·SollZins/100/365` (2000). Added to A_SONSTIGES.
   - **Bug as coded:** step 3 uses the cash balance, not the loan. With positive cash you pay the loan rate on your cash. With negative cash you *receive* money, which the balance sheet still lists as loan-interest expense. The loan amount itself never accrues interest. The bank form (§5.1) and the manual imply interest on the loan. OpenAT should implement `−Credit·SollZins/100/365` behind a "classic bug" flag (Q-3).
- The rates are fixed for the whole game (apart from the advisor perk). There is no credit rating that changes them.

---

## 6. Stock market

### 6.1 Concepts [H]
- Each airline has `AnzAktien` shares. Any player can hold shares of any airline, including its own. Shares not held by any (non-bankrupt) player are the **free float**: `free(a) = AnzAktien(a) − Σ_players OwnsAktien_p[a]`. The free float is an anonymous market; it receives dividends that simply leave the economy.
- `MaxAktien` (the issue ceiling) grows overnight: `MaxAktien = min(MaxAktien·105/100, 250,000,000)`.

### 6.2 Share price — hourly update — `Sim.cpp: SIM::DoTimeStep` (on every game-hour change) [H]
For each airline c (0..3, including bankrupt ones):
1. If `Trusted > Dividende`: `Trusted = (Trusted + Dividende)/2` (integer).
2. `K = (K·29 + 10·Trusted)/30` (double). The price is pulled toward **10 × trusted dividend**: at trust 10 the price drifts to 100, at trust 25 to 250.
3. `K += sin((Date·24 + hour)/50) · K/180` (slow wave, common to all).
4. `K += sin((Date·24 + 100 + hour)/(c + 30)) · K/150` (per-airline wave).
5. `K += sin(R) · K/50`, where R = `Rand(1000)` from a RNG seeded with Date+minute+hour, used as radians (noise ±2 %).
6. Clamp K ≥ 0.

### 6.3 Share price — overnight — `PLAYER::NewDay` [H]
1. The history shifts: `Kurse[9..1] ← Kurse[8..0]`. `Kurse[0]` keeps its value.
2. Company-value penalties (§7). Let V1 = company value at yesterday's evening snapshot and V2 = the value the evening before:
   - V1 < V2: K × 0.97
   - V1 < V2 − 1,000,000: another × 0.97
   - V1 < V2 − 5,000,000: another × 0.95
3. **Investor trust** (after dividends are paid, using today's running balance-sheet result S = income − expenses so far, which already includes the overnight bookings made before this point; see §11):
   - S > 0 and Date odd: if Trusted < Dividende then Trusted += 1.
   - S < 0: if Trusted < Dividende/2 then (Trusted += 1 on odd dates) else Trusted −= 1.
   - S = 0: unchanged.
   - In short, trust rises at most 1 every 2 days toward the dividend while profitable. It falls 1 per day while losing, but not below about half the dividend. Lowering the dividend pulls trust down at once (hourly rule 1).

### 6.4 Events that hit the price [H]
| Event | Price | Trust | Other |
|---|---|---|---|
| Sabotage type 1 | ×0.95 | −1 | route image ×0.99 |
| Sabotage type 2 | ×0.90 | −2 | −2,000; image −2; route image ×0.95 |
| Sabotage type 3 | ×0.85 | −3 | −8,000; image −3; route image ×0.90 |
| Sabotage type 4 | ×0.75 | −4 | −40,000; image −8; route image ×0.50 |
| Sabotage type 5 | — | −5 | −70,000 |
| Breakdown in flight due to poor condition (§8.3) | ×0.70 | −1 if trust > 4 | image −2; route image ×0.8 |
| ATFS 10, day 18 | ×0.20 | — | — |
Trust is clamped ≥ 0 after sabotage. (Sabotage details: sabotage topic. `Sim.cpp: DoTimeStep` ArabMode block.)

### 6.5 Buying and selling (human, at the bank) — `Stdraum.cpp: MENU_AKTIE` [H]
- Not possible by phone. Target airlines that are out are refused.
- **Amount:** buying defaults to free/2, selling to holdings/2. The calculator can set it as a share count, as a % of the target's issued shares (≤ 100 %), or as a money value (÷ current price). It is clamped to 0..free float (buy) or 0..holdings (sell).
- **Buy:** first, if `Money − n·K < −1,000,000`, refuse (`TOKEN_BANK 6000`). Otherwise process in chunks of up to 1000 shares. For each chunk of size m: `holdings += m`, `price += m·K` (at the pre-chunk price), then `K = K·Anz/(Anz − m)`. Total paid = `price + price/10 + 100` (10 % fee + 100 flat) (3150). The total is added to the cost basis `AktienWert` and to stat A_SONSTIGES.
- **Sell:** first scale the cost basis by `(holdings − n)/holdings`. Then in chunks of up to 1000: `holdings −= m`, `proceeds += m·K`, `K = K·(Anz − m)/Anz`. Received = `proceeds − proceeds/10 + 100` (3151). The +100 on a sale looks like a sign slip: the player receives 100 extra. Added to E_SONSTIGES.
- Buying your own shares is allowed and works the same way (it raises your price).
- The price impact is permanent until the hourly drift pulls the price back toward 10 × trust.

### 6.6 Issuing new shares — `Dialog.cpp: TALKER_BANKER1 1003..3050` [H]
- `avail = (MaxAktien − AnzAktien)` rounded down to 100. Refused if avail < 10,000 (`TOKEN_BANK 3000`) or the price is < 10 (3001).
- Choose 10/25/50/75/100 %: `N = ⌊pct·(MaxAktien − AnzAktien)/100⌋`.
- Choose the issue price `E = ⌊K⌋ − 5`, `−3` or `−1`. The market takes M = N (at −5), 0.8·N (at −3) or 0.6·N (at −1). The confirmation shows the fee.
- On confirm (old price K0 = ⌊K⌋):
  1. `Money += M·E` (3162 proceeds).
  2. `Money −= N·E/10/100·100` (3160: 10 % of the nominal value of **all** N new shares, rounded down to 100).
  3. The net (proceeds − fee) goes to E_SONSTIGES.
  4. New price `K = (K·Anz + E·M)/(Anz + M)`, clamped ≥ 0.
  5. **Dilution compensation:** the issuer pays `(Anz − OwnSelf)·(K0 − K)` (3161), i.e. for all outside shares including the free float. Each other player p receives `OwnsAktien_p[issuer]·(K0 − K)` (3161, E_SONSTIGES). The free float's share of the payment vanishes.
  6. `AnzAktien += N`; the issuer's own holding += N − M (unsold shares stay with the issuer).

### 6.7 Dividends — `Stdraum.cpp: MENU_SETRENDITE`, `PLAYER::NewDay` [H]
- Set at the bank (not by phone): 0..25 per share per year. The form shows the % per share, the yearly total `Div·AnzAktien` and the daily total `/365`.
- **Paid overnight:** `Money −= (AnzAktien − OwnSelf)·Div/365` (3140). This pays every outside share including the free float.
- **Received overnight:** `Money += Σ_{p≠self, p not out} OwnsAktien[p]·Div_p/365` (3141). There is no dividend on your own shares.
- Statistics quirk: A_SONSTIGES / E_SONSTIGES get the **yearly** amount (not divided by 365), and the stat is added even when the amount is 0.
- The yield shown in the share tooltip is `Div·100/K` %.
- Effect: the dividend is the long-term anchor of the share price (§6.2–6.3). A high dividend means a high price but costs cash every day.

### 6.8 Takeover / liquidation ("exercise shareholder rights") [H]
**Request (bank, Banker 1, option 1002):**
- Target must not be self (`TOKEN_BANK 2520`), must not be out (2521), and you must hold > 0 of its shares (2525).
- You need ≥ 50 % of the target's issued shares (otherwise 2522, which shows your %).
- The target must hold < 30 % of **your** issued shares (otherwise 2523).
- Then choose "absorb" (2530 → `Overtake` = 1) or "liquidate" (2531 → 2) or cancel. This records the takeover globally; it executes at the next morning briefing. While it is pending the bank greets with 905+medium. Only humans can trigger takeovers; the AI never does.

**Execution (boss briefing, `TALKER_BOSS 5010/5020`):** the target's pending sabotage is cancelled, its passengers in the hall are removed, and a newspaper special is shown.
- **Absorb (1):**
  - All workers move to the acquirer (unassigned).
  - All planes move over with schedules cleared and position set to home. Their pilot/steward needs are counted, and if the target was not a human, that many workers are taken from the reserve pool for the acquirer.
  - Then `Money(acq) += Money(target)` (3180, can be negative), `Credit(acq) += Credit(target)`, `AnzAktien(acq) += AnzAktien(target)`, and the acquirer adds the target's holdings of every airline (and cost bases).
  - Third parties' holdings of the target are converted 1:1 into holdings of the acquirer. All holdings of the target are then zeroed.
  - Gates move over. Routes and branch offices move over when the acquirer doesn't have them, or replace them when the target's rank is better (other players' ranks are adjusted).
- **Liquidate (2):**
  - The target's workers return to the reserve.
  - Planes are sold at full `CalculatePrice` into the target's cash (no ChangeMoney).
  - Gates, routes and branches are freed (ranks of others move up).
  - The target sells all its share holdings, including its own, at the current price.
  - Then `(Money − Credit)` of the target is distributed to every non-bankrupt holder of target shares, pro rata by holdings (3181). The amount **can be negative**: shareholders inherit the debt.
- Afterwards the target is out (or game over if it is the local human). Everyone's holdings of and by the target are zeroed. Statistics are refreshed for both airlines.

---

## 7. Company value — `PLAYER::UpdateStatistics` (STAT_FIRMENWERT) [H]
`value = Σ planes CalculatePrice + Σ_airlines OwnsAktien[a]·K_a + Money − Credit`

Plane price: `ptPreis·Zustand/10000·Zustand·(Baujahr − 1900)/120`, where Zustand is condition 0..100. Sponsored (starting) planes are worth ÷10. The value is a snapshot: it is recomputed whenever statistics are refreshed (§16.2), notably at the 18:00 evening screen. It drives the share-price penalty (§6.3), the high-score table (each human update calls AddHighscore with this value unless the session is flagged as cheated) and the Add-on 06 / ATFS 09–10 missions.

---

## 8. Image (company and per route)

### 8.1 Ranges and meaning [H]
- Company `Image` ranges −1000..+1000 (clamped after most changes). The statistics value "awareness" is shown as Image/10 %. Mission Hard is won at Image ≥ 750.
- Each rented route has its own `Image`, 0..100 (unsigned byte).
- There is **no passive decay** of the company image. The route image only changes through flights, ads and incidents.

### 8.2 Effect on demand (summary; details in the flights topic) [H] `Schedule.cpp: CalcPassengers`
`ImageTotal = clamp(routeImage·4 + Image + 200, 0, 1000)`, then `passengers = passengers·(400 + ImageTotal)/1100`. Both economy and first class use this. So image scales demand between about 36 % and 127 %.

### 8.3 What changes the company image [H]
Per flight that departs (`BookFlight`), for route and passenger-order flights only. If the player's ad-brochure sabotage flag points to another airline, **all of the image changes below go to that airline** (sabotage topic).
- Humans only: if the plane's crew quality < 50 and `ObjectId % 10 == 0`: −1; if > 90 and the same: +1 (deterministic pseudo-random). Also on route flights the route image −1 (if > 0).
- Score `Add`: condition < 20: −50 and < 60: −10 (so −60 total below 20); > 98: +10. For each of seats, food, trays and decor: level 0 gives −1, level 2 gives +2. Humans: `+3·cabinCrew/requiredCabinCrew` (integer; 3 when fully staffed, up to 6 with double staffing).
- Route flights, price check: `ref = CalculateFlightCost(from, to, 800, 800, none)·3/180·2·3` (×0.9 for the AI). Ticket < ref/2: +10. > ref: −10. > 1.5·ref: −10 more. > 2·ref: −10 more.
- Then `Image += Add/10` (truncates toward zero). On route flights the route image also gets Add/10 if the result stays > 0, clamped 0..100.
- Every route flight additionally does route image +1 (up to 100) when booked. So for humans the route image net per flight is roughly Add/10.

Other events:
| Event | Company image | Route image |
|---|---|---|
| Ad campaign | see §10 | see §10 |
| Breakdown in flight from poor condition (checked every 4 game minutes per airborne plane with passengers: when condition < 80 and Rand(5000) > condition + 4920, i.e. chance (79 − condition)/5000) | −2 (sets "image got worse" for the briefing) | ×0.8 |
| Plane departs home with no gate | −2 | — |
| Plane lands home with no free gate | −2 | −2 when `Image % 10 == 0` (sic) |
| Passenger on board smells a stink bomb (one in 4 clans) | −1 | — |
| Plane sabotage types 1/2/3/4 (route image only when the plane's current flight is a route flight) | 0/−2/−3/−8 | ×0.99/×0.95/×0.90/×0.50 (type 1 corrected: an earlier version left it out) |
| ATFS 10, day 55 | −200 | — |
| AI with the "image bonus" behaviour flag | +(Date % 3) daily | — |
Sources: `Sim.cpp: DoTimeStep`, `Planetyp.cpp` (landing), `Person.cpp` (stench), `Player.cpp: NewDay`.

### 8.4 Seasonal incidents (money only) [H]
Every 4 game minutes, each airborne plane rolls `Rand(5000) > 4980` (19/5000). If the season-specific upgrade is missing (spring/autumn/winter: electronics level ≠ 2; summer: safety level ≠ 2), the airline pays 75,000 (3501) and the plane logs a breakdown. There is no image effect.

### 8.5 Image effects elsewhere [H]
- Staff happiness falls daily when Image < 500 (§9.5).
- The boss comments on image tiers (§17.1). Image < −990 means game over.
- The ad agency reports a text tier: negative image → `TOKEN_WERBUNG 2503 + Image/251` (2500..2503), positive → `2504 + Image/167` (2504..2509), zero → 2520.
- Newspaper comparison headline "most popular airline" (§16.4).

---

## 9. Staff and personnel

### 9.1 Worker record [H] `class.h: CWorker`, `Personal.cpp`
Name, sex, type, salary (`Gehalt`, monthly) and original salary, talent (0..100, the "quality"), age, comment, employer (0..3, RESERVE = 100 = hidden pool, JOBLESS = 101 = current applicant), assigned plane, happiness (−100..100, nominal), "warned today".
Types: 0 secretary/"girl" (UI only), 1 personnel advisor, 2 kerosene advisor, 3 route advisor, 4 order advisor, 5 finance advisor, 6 informer, 7 plane analyst, 8 fitness trainer, 9 security (bodyguard), 10 pilot, 11 flight attendant.
The pool is loaded from `data/piloten.csv` (RLE-compressed in the install; columns name, sex, type, salary, talent, age, comment). Names come from `data/Names.csv`. Initial happiness is 100. Salary and talent ranges of the shipped table were not extracted (Q-6).

### 9.2 Daily labour market — `CWorkers::NewDay` (run overnight, after the players' NewDay) [H]
1. **Shortage top-up:** if fewer than 40 pilots with talent > 60 are in RESERVE/JOBLESS, 10 new pilots are generated. Each is 79 % male (seeded draw 0..99, male when > 20), salary `(30 + Rand(70))·100` (3,000..9,900), talent `salary/200 + Rand(30) + 20`, age 19..68. Likewise for flight attendants: 10 new, 19 % male (an **unseeded** C-library draw, male when > 80), salary `(30 + Rand(50))·100` (3,000..7,900), talent `salary·100/80/200 + Rand(30) + 20`, age 19..58. The table only grows.
2. Daily unhappiness for workers of **human** employers (§9.5).
3. All JOBLESS go back to RESERVE.
4. New applicants: `10 + Rand(10)` random RESERVE workers of any type. Each pick tries a random index, then the next 99 indexes, for a reserve worker. Then `5 + Rand(2)` flight attendants. Picked applicants have their salary reset to the original.
5. `EnsureBerater` at game start guarantees a personnel, order and plane advisor among the applicants.
All draws are seeded with Date + StartTime (deterministic per day), except the attendants' sex and the first and last names of new workers, which use the unseeded C-library generator. (Corrected: an earlier version said 80 % male pilots and fully seeded draws.)

### 9.3 Hiring and firing — `Stdraum.cpp: MENU_PERSONAL` [H]
- Folders: advisors (all types < 10), pilots, flight attendants; applicants or own staff. The page header shows the average salary of the listed people.
- **Hire:** employer = player, plane = none. Then the auto-assignment runs (§9.4) and walk speed is updated. There is no hiring fee and no limit.
- **Fire:** the worker becomes JOBLESS again, so they can be re-hired the same day. There is no severance pay.
- **Per-person raise / cut:** see §9.6.
- **Reassign to another plane:** pick a plane from the list.
- Personnel-advisor commentary while browsing applicants (only with a personnel advisor):
  - If `(workerIndex·7777) % 100 > advisor talent`, the comment is a non-committal "can't judge" line (1000). So a poor advisor is often useless, and the outcome is deterministic per worker.
  - Otherwise: talent ≤ 25 → bad (1006); ≤ 45 → bad (1007). Else use the value ratio n: n < −5 → overpaid (1001); n > 5 → good (1003), or excellent if talent ≥ 90 (1008); else neutral (1002).
  - The ratio n counts all workers of the same type with |talent difference| < 15: +1 for each who earns more than the candidate, −1 otherwise.
  - For advisor applicants of a type you already employ: personnel-advisor applicant → 1272; not better than yours → 1270; better → 1271.
  - Someone who left the pool (RESERVE) while you browse → 1004/1005.

### 9.4 Crew assignment — `PLAYER::MapWorkers` [H]
- Pilots and attendants without a plane are assigned to the plane with the smallest remaining shortfall that still needs that type (up to the type's required count).
- Leftover attendants fill planes up to the plane's `MaxBegleiter` (luxury cabin crew: the required count × the fleet's average-cabin-crew factor, max 2× required). Surplus over the maximum is removed first. A leftover attendant that finds no plane short of base crew is counted as crew on **every** plane that still has a free luxury slot (the loop does not stop after the first; the attendant's own plane reference ends at the last of them), and the surplus count drops once per such plane.
- A second pass is meant to move attendants from luxury slots to planes that lack their base crew, but it **never runs**: its "movable attendants" sum subtracts a plane's attendant count from itself and is always 0. (Corrected: an earlier version described the second pass as working.)
- Afterwards it counts the surplus/shortage (`xPiloten`, `xBegleiter`; negative = missing) for advisor messages (`TOKEN_ADVICE 1200..1253`).
- Then it recomputes **crew quality** per plane = average talent of the assigned crew (pilots and attendants), or 60 if none.
- A plane with fewer pilots or attendants than required will not depart (hourly delay check; flights topic). Crew quality drives the ±1 image rule (§8.3) and the boss's staff comment.

### 9.5 Salaries and happiness [H]
- **Salary payment** (overnight, humans only): `Money −= Σ Gehalt/30` (2070). Each plane's daily P&L is also charged `Gehalt/30` of its crew. The AI pays no salaries at all (its statistic shows a virtual figure, §16.3).
- **Daily mood loss** (humans' staff, in `CWorkers::NewDay`): steps = 0 if Image ≥ 500. Otherwise 1, +1 if Image < 0, +1 if Image < −500. Each step: happiness −1, and an extra −20 if the current salary < the original salary. If happiness drops below −100 the worker **quits**: becomes RESERVE, secretary message `TOKEN_ADVICE 2000 + type + sex·100`, and the crew is re-mapped. Daily loss is not clamped.
- Happiness is **not** raised by profit, image or anything else except salary raises and the strike-resolution options (§9.7).

### 9.6 Raises and cuts — `CWorker::Gehaltsaenderung`, Dialog `TALKER_PERSONAL` 600/810 [H]
- **Raise:** `Gehalt += Gehalt/100·10` (≈ +10 %), capped at 1,000,000. If the salary changed: happiness +20 (not clamped to 100).
- **Cut:** `Gehalt −= Gehalt/100·10`, happiness −25. If happiness < −100 the worker quits immediately (salary reset, happiness reset to 100, back to RESERVE).
- Raise all / cut all is offered by the male clerk. "Raise all" (600) also cancels a planned strike. It then adds +10 to everyone (clamped ±100) repeatedly while `avg − (min < 0 ? 10 : 0) < 20`. Note the loop condition could fail to terminate if a single worker is at −100 and everyone else at 100; bound it in OpenAT (Q-7).
- The manual says lowering salaries while profits are high is not accepted; no such check exists in the code [H]. The cut always applies.

### 9.7 Strikes — `Sim.cpp: DoTimeStep` [H]
- **Planning:** checked once per day at the 10:00 hour change, for the **local human only**. Let `mood = avg happiness − (min happiness < 0 ? 10 : 0)`. A strike is planned if (mood < 20 and DaysWithoutStrike > 7) or (mood < 0 and DaysWithoutStrike > 3).
- **Start:** at any hour change between 07:00 and 17:00, once any of the player's planes is on the ground or lands within the next 2 hours. Duration: 2 h if DaysWithoutStrike > 10; else 72 h if avg happiness < −10; else 4 + Rand(20) hours. DaysWithoutStrike resets to 0 (it otherwise counts up overnight). A newspaper headline appears.
- **During:** every scheduled departure is delayed by 1 hour per hour (flights topic). A fax and letter notify the player at the first opportunity.
- **End options** (male clerk, 950):
  - Raise all salaries (end in 2 game minutes, plus the happiness pump of §9.6).
  - Threaten (−20 happiness to all, end in 4 minutes).
  - The drinker at Rick's café (not in the hall), only once his trust was earned with the horseshoe: talking to him during a strike ends it after a 10-minute countdown, shortened to 4 minutes by his follow-up line. (Corrected: an earlier version placed him in the hall with a flat 4 minutes.)
  - Wait it out (−10 happiness to all at the natural end).

### 9.8 Advisor effects (quality = highest talent among employed advisors of that type) [H]
| Advisor | Effect |
|---|---|
| Personnel (1) | Staffing surplus/shortage messages; applicant ratings (§9.3). |
| Kerosene (2) | Fuel-quality comments at Petrol Air (`TOKEN_ADVICE 3020+kind`); warns when the tank runs dry. Fuel quality is otherwise invisible. |
| Route (3), Order (4), Plane analyst (7) | Advice in their rooms (other topics). |
| Finance (5) | Talent > 75: interest perk (§5.1). The notebook's daily-balance page shows nothing without one, a vague text below 80, and the full previous-day balance sheet (in thousands) at ≥ 80. |
| Informer (6) | Messages about rival share buys and issues, rival details in notebook/route office. |
| Fitness (8) | Walk speed = 3 + Σ (talent − 30)/30 over all fitness trainers, clamped 1..5. |
| Security / bodyguard (9) | If talent > 20: a rebate of `price/100·(talent/10)` (i.e. talent/10 %) is credited after most purchases (3130), with a message. It applies to storage tanks, kerosene, sabotage jobs, new/used planes, ads, duty-free items, chocolates and the laptop. |

### 9.9 AI staff [H]
The AI has no real workers. Its employee count = required crew of its planes. Its staff-satisfaction stat = `employees·(min(500, Image) + 2500)/3000`. Its salary stat sums the original salaries of a slice of the pool's pilots/attendants (skipping 2·playerNum entries) ×1.25. None of this is ever charged.

---

## 10. Advertising — `Dialog.cpp: TALKER_WERBUNG` [H]
The agency is open from 12:00. On weekday codes 5 and 6 it is closed in Normal and harder missions and in the free game (`Sim.cpp` condition block 6). See Q-13 on the weekday convention. Choose a campaign type, then one of 6 tiers; the money check uses DEBT_LIMIT. The effect is **instant and permanent**: there is no duration and no decay. Cost goes to reason 3120 + tier and stat A_SONSTIGES, then the bodyguard rebate applies.

| Type | Tier 0 | 1 | 2 | 3 | 4 | 5 |
|---|---:|---:|---:|---:|---:|---:|
| Company image — price | 1,000 | 10,000 | 50,000 | 100,000 | 250,000 | 2,500,000 |
| Image gain `price/10000·(tier+6)/55` | 0 | 0 | 0 | 1 | 4 | 50 |
| Single route — price | 2,000 | 8,000 | 22,000 | 56,000 | 120,000 | 1,400,000 |
| Route image gain (both directions) `price/30000` | 0 | 0 | 0 | 1 | 4 | 46 |
| Everything — price | 5,000 | 93,000 | 290,000 | 870,000 | 4,925,000 | 60,000,000 |
| Company image gain `price/15000·(tier+6)/55` | 0 | 0 | 2 | 9 | 59 | 800 |
| Every rented route's image gain `price·(tier+6)/6/120000` | 0 | 0 | 3 | 10 | 68 | 916 → byte-wrapped to 148, then clamped → 100 |

- Tier 0 of any type sends every human player a letter about the campaigning airline (`TOKEN_LETTER 9900/9910/9920` sets). It is a joke "worst ad" with no effect [M].
- Route image is clamped 0..100. Company image is clamped ±1000.
- With these integer formulas the low tiers are pure money sinks. Only tiers 4–5 matter (Q-8: intended?).
- AI ads (`Player.cpp: ACTION_WERBUNG`): they use the same company formula. AI route ads cost 56,000 but give **+28** route image (price/2000, not /30000) — an AI advantage.

---

## 11. Overnight processing order (economy-relevant) [H]
`SIM::NewDay`:
1. Date++, month/weekday update; airport expansion applied if ordered.
2. **Kerosene price** update (§12.1).
3. For each player not out: `PLAYER::NewDay` (below).
4. Used-plane market, comparison headlines, route demand, newspaper reload.
5. **Auctions settle:** each won branch / route / gate is charged `3 × price` (2040/2041/2042) and rented at `price` per month. Win/lose letter.
6. Bulletin board re-randomised; **labour market** (§9.2).
7. Sympathy updates between players based on balance-sheet results (AI relations; AI topic).
8. ATFS 06 artificial sabotage.
9. Per player: history separator line; `BilanzGestern = Bilanz`; `Bilanz` cleared.
So the overnight bookings of step 3 are counted in the day that just ended.

`PLAYER::NewDay` (economy steps, in order):
1. Daily flags reset; AI image bonus; AI `Bonus` top-up (§18).
2. Statistics month rollover (every 30th day) and day shift (§16.1).
3. Route utilisation averaging; low-use warning letters at 10/15 days and route **revoked** at 20 consecutive days below 10 % (routes topic).
4. `MoneyPast` shift (append current Money).
5. Share price shift and company-value penalties (§6.3); MaxAktien +5 %.
6. Interest (§5.3) and security fees (§14).
7. Rents (§13), salaries (§9.5), expired-order penalties (orders topic).
8. AI daily planning; walk speed; advisor message queue reset.
9. Dividends paid and received (§6.7); investor-trust update (§6.3).
10. Random flavour letters (each loop draws `rand() % 80`; values 0..16 send letter `TOKEN_LETTER 2000+10c`, and the first draw > 16 stops).
11. Plane maintenance and repair (§12.3), ATFS 10 scripted events, maintenance booking.
12. AI route re-planning; wealth tax (§15.5).

Then hourly through the next day: share prices (§6.2), strike checks, delays, incidents.

---

## 12. Kerosene, storage tanks, maintenance

### 12.1 Market price — `SIM::NewDay` [H]
Each night: shift `KerosinPast`, then (RNG seeded with Date):
`price += (Rand(21) − 10) + [Rand(20) < 3]·(Rand(41) − 20) + ⌊sin((Date + Rand(6))/3)·20⌋ + ⌊sin(Date·1.7)·20⌋`, clamped to 300..700, and stored as `KerosinPast[9]`.
The bracket is 1 with 15 % probability. Truncation of the sin terms is toward zero. The original's evaluation order of the four Rand calls inside one expression is compiler-defined; MSVC most likely evaluates left to right (Q-9).
**Fuel-crisis missions** (ATFS 09 always; ATFS 10 on days 3–10 and 35–55):
- With chance 17/20, the price rises by price/4.
- Otherwise, if the three previous days were all > 550, the price is halved.
- Then clamp, then 20 extra noise steps of `(Rand(21) − 10) + [Rand(20) < 3]·(Rand(41) − 20)`, then clamp again.

### 12.2 Fuel quality, buying and tanks — `Dialog.cpp: TALKER_ARAB`, `Stdraum.cpp: MENU_BUYKEROSIN`, `Misc.cpp` [H]
- Petrol Air opens at 11:00 and is closed on weekday code 6 (Q-13).
- Three qualities, priced at 2× / 1× / ½× the market price: premium (0), normal (1), cheap (2). Choosing a quality **sets `KerosinKind` permanently**. It is used both for tank purchases and for all fuel bought at the pump during flights.
- **Buy into tank:** amount in barrels, or % of capacity, or money ÷ price. Clamped to the free tank space and to what you can afford down to −1,000,000.
  - Cost = `price·qty` minus a volume rebate: 5 % at ≥ 10,000 barrels, 10 % at ≥ 50,000.
  - Refused if `Money − cost < −1,000,000`.
  - `TankInhalt += qty`. Cheap fuel adds qty to `BadKerosin`; premium subtracts qty from it (floor 0).
  - `TankPreis = (old contents·TankPreis + cost)/(new contents)`.
  - Booked as 2090, then the bodyguard rebate.
- **Storage tanks** (dialog 900–903, then a multiplier of 1/2/3/5/10):

| Tank | Capacity added per unit | Price per unit |
|---|---:|---:|
| 1 | 10 | 10,000 |
| 2 | 30 | 20,000 |
| 3 | 50 | 40,000 |
| 4 | 100 | 60,000 |
The table stores capacities 10000/30000/50000/100000; the code adds value/1000 to `Tank` (in barrels). The tank-purchase dialog shows the larger number (Q-10 on the intended unit). Starting capacity is 100. Booked as 2091 + bodyguard rebate.
- **Toggle "use tank"** in the office (MENU_KEROSIN): `TankOpen` flips.

### 12.3 Fuel per flight [H] `Misc.cpp: CalculateFlightKerosin / CalculateRealFlightCost`, `Schedule.cpp: BookFlight`
- Barrels needed `B = distance_m/1000·consumption/160/speed`. Here consumption and speed are the plane's values; 160 is "litres per barrel" per the comment.
- If the tank is open: `t = min(TankInhalt, B)` comes from the tank and the rest `B − t` is bought at the pump at the `KerosinKind` price.
- **Real cash cost** = pump part only (plus catering, `passengers·FoodCosts[food level]`, FoodCosts = 10/20/50). It is included in the flight's net booking.
- The tank part is **not** charged again. Instead `t·TankPreis` is added to balance-sheet Kerosene, stat A_KEROSIN and the plane's P&L as an imputed cost. Tank contents −t and BadKerosin −t (floor 0).
- The estimator used for planning has a floor of 1000; the real cost has none.
- **Bad-fuel wear factor** = 3 if (tank open, contents > 0 and cheap share > 50 %) or (tank closed/empty and `KerosinKind` = cheap). Otherwise 1. Plane wear per flight: `Zustand −= (1 + dist·10/40040174)·(2015 − Baujahr)·factor/15`. Premium fuel has **no benefit** beyond diluting the cheap share in the tank.

### 12.4 Maintenance contract and repair cost (overnight) [H] `Player.cpp: NewDay`, `Dialog.cpp: TALKER_MECHANIKER`
- Monthly fee per plane by `MechMode`: 0 cleaner 1,200 · 1 apprentice 18,000 · 2 mechanic 48,000 · 3 certified engineer 150,000. Charged daily as `fee·planes/30`.
- The two top levels are only offered after the mechanic's trust is earned (`MechTrust` = 2).
- Repair effect per plane, only when `Zustand < TargetZustand + 2`:
  - Cleaner: −2 on half of the days (parity of index + condition + date).
  - Apprentice: −2 on 1/8 of the days, else +5.
  - Mechanic: +2..6 if < 60, else +2..9.
  - Engineer: +18 if < 60, else +15.
  - Capped at 100 and at the target. WorstZustand tracking applies (planes topic).
- Extra repair cost when the condition improved:
  - `Improvement·ptPreis/110`, where Improvement = the part of the condition above WorstZustand + 20.
  - Plus `(Δcondition)·10·Wartungsfaktor·(2100 − Baujahr)/100·(200 − Zustand)/100`.
- Total booked as 3110 and stat WARTUNG = −(extra + fees).

---

## 13. Rents and auctions [H] `PLAYER::BookBuroRent`, `SIM::NewDay`
- All rents are monthly prices charged daily at `rent/30` (integer per item): gates (2050), branch offices including home (2051; only if non-zero), routes (2052; only if non-zero). All three go to stat A_MIETEN.
- Auction win (bulletin board, see `rooms-and-ui-6` §4): one-off `3 × final price`, then rent = final price per month.
- Selling or giving up rentals: routes/gates topic.

---

## 14. Security subscriptions (fees) [H] `PLAYER::CalcSecurityCosts`
**Charged every night in full** (not /30), reason 3503, stat A_SONSTIGES. Fixed per-airline items plus per-plane items × number of planes:

| Bit | Item | Cost |
|---|---|---:|
| 0 | Office | 25,000 |
| 1 | Laptop | 20,000 |
| 2 | Personnel office | 25,000 |
| 3 | Bank | 30,000 |
| 4 | Route board | 30,000 |
| 5 | Administration | 30,000 |
| 6 | Planes (inside) — per plane | 30,000 |
| 7 | Planes (outside) — per plane | 30,000 |
| 8 | Gate update — per plane | 40,000 |
| 10 | Gate update II — per plane | 50,000 |
| 11 | Gate update III — per plane | 50,000 |
(Bit 9 has no cost.) Bits 10/11 also raise first-class demand (flights topic). Protection effects: sabotage topic.

---

## 15. Other money sinks and sources

### 15.1 Phone [H] `Stdraum.cpp`
- Starting any paid call costs 1 (history line −1, stat −1; the balance is debited directly).
- Then 1 unit each time `elapsed/50 > (2 − mobile)·10 + (3 − distance)·60` time units. Distance: 0 = airport room, 1 = own home city branch, 2 = foreign city.
- That is one unit every 10 / 7 / 4 game minutes from a landline, and about 9.5 / 6.5 / 3.5 from a mobile. Trivial amounts.

### 15.2 Shops [H/M]
- Duty free (laptop tiers, mobile phone, filofax, beer, etc.) and Rick's chocolates are priced from the text table (`TOKEN_ITEM 2000+id`; chocolates 2801). The numbers must be read from the install's text resource (Q-11).
- They are booked with reason 9999 and stat A_SONSTIGES, plus the bodyguard rebate.
- Laptop: locked for the first 7 days; one buyer per day globally; each purchase upgrades the quality tier 1..4 (battery 40/80/200/1440).
- Rick's Café: free hints only. The hints include economic nudges: the bank-bin money; you own less than half of your shares; ≥ 10,000 shares issuable; dividend < 5; cash > 30M.
- Kiosk: free (§16.4); it sells a stink bomb only via a trust dialog (sabotage topic).

### 15.3 Planes
- Buying new costs the type price (or the designer-plane `CalcCost`), reason 2010.
- Selling yields 90 % of `CalculatePrice` (2011, stat E_SONSTIGES).
- Used planes at the museum cost `CalculatePrice` (2010).
- Refits cost by component (2110; planes topic). An AI emergency repair costs 170,000.
- A seat removal/installation when switching between passenger and freight flights costs 15,000 (2111).

### 15.4 Airport expansion [H]
Requested at the boss: costs 1,000,000 (3170). Conditions are in `rooms-and-ui-6` §3.8.

### 15.5 Wealth tax [H] `PLAYER::NewDay`
At the end of the overnight run, for each threshold T in {5·10¹³, 10¹⁴, 2·10¹⁴, 4·10¹⁴, 8·10¹⁴, 1.2·10¹⁵, 1.6·10¹⁵, 1.7·10¹⁵, 1.8·10¹⁵, 1.9·10¹⁵}: if `Money > T`, then `tax += (Money − T)/10`. Booked as 3600. This is a 64-bit overflow guard and practically never triggers. There are no other taxes, and **no insurance** of any kind exists.

### 15.6 Rocket / space station parts (Final, Add-on 10) [H]
- Rocket parts: 200,000; 400,000; 600,000; 5,000,000; 8,000,000; 10,000,000; 20,000,000; 25,000,000; 50,000,000; 85,000,000.
- Station parts: 1,000,000; 2,000,000; 3,000,000; 2,000,000; 10,000,000; 20,000,000; 35,000,000; 50,000,000; 35,000,000; 80,000,000.
- Reason 3400 (missions topic).

### 15.7 Sabotage prices (cross-reference) [H]
- Arab Air jobs: 1,000 / 5,000 / 10,000 / 50,000 / 100,000.
- Player-related jobs: 10,000 / 25,000 / 50,000 / 250,000.
- Specials: 100,000 / 500,000 / 1,000,000 / 2,000,000 / 5,000,000 / 8,000,000.
- Reason 2080, bodyguard rebate applies. Fines: §17.3. Details: sabotage topic.

### 15.8 Money gifts (network) [H]
In the chat window, typing a pure number < 9 digits that is ≤ your cash transfers that amount to the chat partner (3700/3701).

---

## 16. Statistics

### 16.1 Series storage — `Sim.cpp: SValue` [H]
- Each player has 40 series. Each series holds `Days[31]` (index 0 = today) and `Months[12]`.
- Overnight (`NewDay`): Days[1..30] ← Days[0..29]. **Days[0] is not cleared.** As a result, "additive" series (updated with AddAtPastDay) start each day from yesterday's value and are effectively **running totals since game start**, while "snapshot" series (SetAtPastDay) are overwritten at the next refresh (Q-4: intended daily values?).
- Every 30th day (`Date % 30 == 0`, before the day shift): Months shift. Months[0] = sum of Days[0..min(29, Date)] for these series: passengers, home passengers, flights, orders, last-minute orders, penalty count, satisfied, unsatisfied, staff satisfaction, delays, accidents, maintenance, salary. All other series take Months[0] = Days[0].
- Helper quirks: GetMin returns the maximum and GetMax the minimum (comparisons swapped). GetAvg sums only indexes ≥ Date, so it is wrong after day 0. Neither is used by the statistics screen [H]. Do not replicate unless something reads them.

### 16.2 When snapshots refresh — `PLAYER::UpdateStatistics`
Refresh happens at the 18:00 evening screen (`CAbend`), on opening the statistics screen, on opening the morning briefing menu, after takeovers, in some mission-report dialogs, and on network sync. [H]

### 16.3 Series catalogue [H]
| Id | Name | Kind | Source |
|---|---|---|---|
| 0 | Account | set | Money |
| 1 | Debt | set | Credit if Money > 0, else Credit − Money (loan + overdraft) |
| 2 | Planes | set | count |
| 3 | Passengers | add | economy + first class per route/order flight |
| 4 | Home passengers | add | same, flights touching home |
| 5 | Share price | set | ⌊K⌋ |
| 6 | Flights | add | per route/order flight |
| 7 | Orders taken | add | travel agency / last minute |
| 8 | Last-minute orders | add | — |
| 9 | Penalties (count/amount) | add | orders topic |
| 10 | Company value | set | §7 |
| 11 / 12 | Satisfied / unsatisfied passengers | add | flights topic; also +15 per 15 passengers when landing without a gate |
| 13 | Staff satisfaction | set | humans: Σ((happiness + 100)/2)/100 ("satisfied-employee equivalents"); AI §9.9 |
| 14 | Delays | add | +1 per hourly delay of a flight |
| 15 | Accidents suffered | add | +1 per sabotage hit |
| 16 | Sabotage committed | add | +1 per own sabotage executed |
| 17 | Employees | set | humans: workers; AI: required crew |
| 18 | Mission progress | set | mission-specific; Easy uses passengers/target instead of profit (bug) |
| 19 | Maintenance | add | −(daily repair + fees) |
| 20 | Branch offices | set | count |
| 21 | Routes | set | count |
| 22 | Salaries | set | −Σ monthly salaries (AI: virtual) |
| 23 | Awareness | set | Image/10 |
| 25 | Shares issued | set | AnzAktien |
| 26–29 | Shares held of airline 0..3 | set | OwnsAktien[0..3] |
| 30 | Income: routes | add | route-flight revenue |
| 31 | Income: orders | add | passenger-order premiums (freight not counted) |
| 32 | Income: sales | — | defined but never written |
| 33 | Income: other | add | interest, dividends received (yearly figure!), share sales, issue proceeds, plane sales, refit refunds, compensation received |
| 34 | Expense: rents | add | gate + branch + route rents |
| 35 | Expense: kerosene | add | fuel + catering (pump) and imputed tank fuel |
| 36 | Expense: penalties | add | order/freight penalties, sabotage fines |
| 37 | Expense: purchases | — | defined but never written |
| 38 | Expense: other | add | overdraft and loan interest, security, dividends paid (yearly figure!), share buys, ads, shop items, phone, refits |
| 39 | Freight orders taken | add | freight topic |

### 16.4 Statistics screen data (layout in the UI topic) [H] `Statistk.cpp`
Three groups:
- **0 Finances:** income routes / orders / other, then a sum. Expenses rents / salaries / kerosene / maintenance / penalties / other, then a sum. Then a balance row = income sum + expense sum (expenses are negative).
- **1 Shares & assets:** shares issued, price, held by each of the 4 airlines, then portfolio value. Portfolio value = Σ over the visible airline rows of holding × that airline's price, and only if the price row is visible. Then account, debt, company value, awareness %.
- **2 Operations:** planes, routes, branches, flights, orders, last-minute orders, delays, accidents, sabotage committed. Then passengers and satisfied % (satisfied ÷ passengers). Then employees and staff satisfaction % (series 13 × 100 ÷ employees).
- Rows can be hidden, which removes them from the sums. Players can be masked.
- The view covers the last N days (default 7) or months, and graphs draw Days[d] / Months[d] per player.
- Persistent screen settings live in Sim (`StatplayerMask`, `Statgroup`, `Statdays`, `StatiArray`).

### 16.5 Newspapers / kiosk [H] `Kiosk.cpp`, `Misc.cpp: HEADLINES`
- Three papers with up to 10 headlines each. Each day a random filler headline is drawn from the headline table, or a multi-day story chain is started or continued. Tokens `{city}`, `{rand(a,b)}` and `{n}` (airline n) are filled in.
- **Overrides** queued during the previous day are added: sabotage reports, first route flown, strikes, takeover special, rocket progress, and comparison headlines.
  - Comparison headlines run only when Date > 8 and Date % 4 == 2. One random kind per day:
    - Most planes, if > 10.
    - Highest image, if > 100 (names the runner-up).
    - Most routes, if > 30.
    - Richest, if cash > 25M.
    - Best pilot or stewardess (talent > 90 for the local player; otherwise a rival with a random name).
    - Most passengers.
    - Worst plane, if condition < 50.
- If any override exists, the vendor is "awake" and the page is worth reading; otherwise he sleeps. Reading is free. Headlines are sorted by picture priority.
- The papers carry no hard economic data (no price tables) — purely informational flavour and rival intelligence.

---

## 17. Boss (Aufsicht), bankruptcy, game over

### 17.1 Morning briefing evaluation — `Dialog.cpp: TALKER_BOSS 2000..3002`, `Stdraum.cpp: StartDialog` [H]
- **Order:** a pending takeover (5000…), else sabotage reports (2004…), else a random greeting. Then the mission report (missions topic). Then each non-bankrupt player in turn.
- For each player:
  - Image comment: if "image got worse" was flagged (breakdown accident), a special remark and the flag is cleared. Else Image < −100 → 2021x; > 500 → 2026x; > 100 → 2025x; else 2024x.
  - The < −400 and < −990 branches come after < −100 and are therefore unreachable (dead code).
- Then the **verdict:**
  - **Image < −990** → `TOKEN_BOSS 2023x` → out (local human: game over).
  - Else **cash < −5,000,000** (`DEBT_GAMEOVER`; the loan is not counted) → 3000/3001 → out (local human: game over). For an AI or remote player: IsOut = true, and every holding of and by that airline is zeroed. Its assets are *not* redistributed.
  - Else comments on average plane condition (< 40, < 60, < 80, < 90, else good; "no flights" remark), on average crew quality (< 40, < 60, < 70, < 80, else excellent), or "no planes".
- While any player meets a game-over condition, has won, or a takeover is pending, the briefing cannot be skipped with a right click.
- The briefing summary menu (fast-briefing option) shows per player: mission progress, image %, staff satisfaction %, average condition %. It shows an alert icon for won / image < −990 / cash < −5M / being taken over.

### 17.2 Game-over conditions (summary) [H]
1. Image < −990 at a morning briefing.
2. Cash < −5,000,000 at a morning briefing. There is no gradual bankruptcy process; the −3M warning is the last notice.
3. Being absorbed or liquidated by a rival (§6.8).
4. Mission end (won or not) after the boss's report — missions topic.
Bankrupt AI players simply disappear. Their routes and gates stay rented to nobody [M] (Q-12).

### 17.3 Sabotage fine at the briefing [H]
When a reported sabotage is attributed (the saboteur's evidence counter ≥ 100), the saboteur pays `hints × 10,000` (2200, stat A_STRAFEN) and the victim receives the same (2201).

---

## 18. AI money shortcuts (summary for the AI topic) [H]
- **Hidden bonus:** starts at |AI target cash − human cash| (§2.1). Each night, if Bonus = 0, it is topped up by +100,000 for each of these that holds: the local human's mission rating > the AI's; > 1.5× the AI's; > 2× the AI's. The comparison is inverted for the debt mission. Then ×2/×4/×8 by behaviour flags.
  - It is paid out at flight booking: `saldo += min(Bonus, |saldo|/10)`, and Bonus is reduced by the same amount.
- **AI bank visit:**
  - Sets the dividend to 25 (player 1 or the "high share price" flag), else randomly nudges it within 5..25.
  - Takes or repays loans by directly changing Money/Credit (no interest booking, no history).
  - Sells shares (up to 20,000 per airline) when the loan > 3M. The proceeds use the **AI's own** share price for every airline (bug).
  - Buys the free float of a disliked rival when cash > 3M and the loan < 1M (or cash > 2M and the price < dividend/2). The rival's price is raised by `K·Anz/(Anz − n/2)`, with no fee and no DEBT check.
  - Buys its own free float under similar rules.
- **AI issue:** issues all available shares at price −5. Fee/proceeds/compensation are as for humans. 80 % go to the market, and the AI immediately buys back half of the new shares at the issue price (except player 3 without the flag).
- The AI pays no salaries. It gets a 10 % kerosene allowance only in the image price check (§8.3). It gets +25 % passenger weight (flights topic).

---

## 19. Bugs and quirks (decide per item: replicate for fidelity or fix)
1. Loan interest charged on cash, not on the loan (§5.3) — **major**.
2. Overdraft interest uses the deposit rate (§5.3).
3. Credit-limit formula uses `200,000 − cr` (§5.1).
4. Loan repayment ignores cash (§5.1).
5. Share sale adds +100 instead of subtracting the flat fee (§6.5).
6. Dividend statistics record the yearly amount (§6.7).
7. Dilution compensation is paid for free-float shares too (money destroyed) (§6.6).
8. Statistics Days[0] not reset → additive series are cumulative (§16.1). GetMin/GetMax swapped. GetAvg wrong.
9. Mission-progress stat for Easy uses passengers ÷ profit target.
10. Boss image comments for < −400 / < −990 are unreachable (§17.1).
11. "Everything" ad tier 5 route gain overflows a byte (harmless after the clamp) (§10).
12. Freight penalty code 2065 is not mapped to the balance sheet, and codes 2060/2065 are swapped at manual cancels (§3.1).
13. AI route ads give 28 route image vs 1 for humans at the same price (§10).
14. AI share sales priced at its own share price (§18).
15. Raise-all happiness loop has no iteration bound (§9.6).
16. Finance-advisor rate perk ignores Free game / add-on / ATFS rates (§5.1).
17. Liquidated airline also "sells" its own shares to itself before the payout (§6.8).

## 20. Open questions
- **Q-1** Credit limit: replicate `200,000 − cr`, or use `200,000 − Credit`?
- **Q-2** Should the finance-advisor perk apply to the free game / add-ons? (As coded: no.)
- **Q-3** Interest: ship the fixed formula by default with a "classic" toggle? Balance via playtesting — the original economy may be tuned around the bug (loan interest effectively never accrues while cash is positive and small).
- **Q-4** Were the daily statistics meant to be per-day values? Verify in the running original (open the statistics screen on day 3 and check whether "income routes" grows monotonically).
- **Q-5** Does the hourly share-price update also run during the overnight fast-forward (00:00–09:00)? The trigger is any hour wrap in `DoTimeStep`. Confirm in game whether night hours are simulated tick by tick (affects the number of drift steps per day).
- **Q-6** Extract salary/talent distributions from `data/piloten.csv` (RLE-compressed; formats topic).
- **Q-7** Bound the raise-all happiness pump.
- **Q-8** Low ad tiers have no effect due to integer maths — intended?
- **Q-9** Order of RNG draws inside the kerosene expression (needed for bit-exact replays, not for feel).
- **Q-10** Storage tank unit: is capacity "×1000" a display convention (litres vs barrels)?
- **Q-11** Duty-free / chocolate prices: read from the install's text resource and record them in a data file.
- **Q-13** Weekday convention: at game start `Weekday` is set Monday = 0, but every rollover sets it from the C library, where Sunday = 0. So on day 0 codes 5/6 mean Sat/Sun, and afterwards they mean Fri/Sat. This affects shop closing days (ad agency, Petrol Air, duty free, Last Minute, museum). Check in game.
- **Q-12** What happens to the rentals of an AI eliminated by bankruptcy (not takeover)? The code only zeroes share holdings. Check whether routes/gates stay blocked.

Traceability index: `Player.cpp: ChangeMoney, NewDay, BookBuroRent, BookSalary, CalcCreditLimit, CalcSecurityCosts, UpdateStatistics, MapWorkers, HasBerater, DoBodyguardRabatt, GetMissionRating, RobotExecuteAction (ACTION_VISITBANK, ACTION_EMITSHARES, ACTION_WERBUNG)`; `Sim.cpp: ChooseStartup, DoTimeStep, NewDay, SValue::*`; `Bank.cpp: Bank::Bank, CBilanz::*`; `Dialog.cpp: TALKER_BANKER1/2, TALKER_BOSS, TALKER_PERSONAL*, TALKER_WERBUNG, TALKER_ARAB, TALKER_MECHANIKER, TALKER_RICK`; `Stdraum.cpp: MENU_BANK, MENU_AKTIE, MENU_SETRENDITE, MENU_BUYKEROSIN, MENU_PERSONAL, MENU_BRIEFING, CalcStop, PostPaint (phone)`; `Personal.cpp: CWorkers::*`; `Schedule.cpp: BookFlight, CalcPassengers`; `Misc.cpp: CalculateFlightKerosin/Cost, HEADLINES::*`; `Planetyp.cpp: CalculatePrice, UpdatePersonalQuality`; `Statistk.cpp: CStatistik`; `Tips.cpp: DrawKursTipContents, DrawMoneyTip`; `Block.cpp` (balance-sheet page); `Global.cpp: gRepairPrice, gWerbePrice, TARGET_*`; `defines.h: DEBT_*, STAT_*`.
