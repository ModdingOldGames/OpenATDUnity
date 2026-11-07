# Rooms & UI 3b: per-partner dialog trees

This is the companion to [`rooms-and-ui-3-dialog.md`](rooms-and-ui-3-dialog.md), which defines the
machinery. The partner switch is in `Dialog.cpp: CStdRaum::PreLButtonDown` and the openings are in
`StdRaum.cpp: CStdRaum::StartDialog`. Both are **H** unless marked otherwise. Texts are referenced as
`Group/id` in `misc/dlg_<lang>.res`. No original wording is reproduced.

## Notation

| Symbol | Meaning |
|---|---|
| **P** | The partner says a line. Clicking it, or letting its voice end, delivers its id. |
| **C a..b** | The player gets a choice list with ids a..b. Clicking an option delivers that option's id. |
| **S x** | A single player line ("player says x"), which is a list with one option. |
| **V n** | A virtual node: a composed string whose click delivers `n`. |
| **NW x** | A number window (`999xxxx` text) shown together with the line. |
| **M menu (→r)** | Opens a menu. When the menu closes, the partner says re-entry node *r* (see the machinery doc, §3.6). |
| **end** | `StopDialog`. Every id not listed in a partner's "click" table also ends the dialog. |

"Click X → Y" means the handler for clicked id X shows Y. Unless stated otherwise, **money checks**
compare `Money − price` against `DEBT_LIMIT` (−1,000,000); that is the overdraft allowed. The quantity
option lists "1, 2, 3, 5, 10" are truncated to the largest count the player can afford.

---

## 1. Bank, female clerk: loans (`TALKER_BANKER2`, group `Bank`)

**Entry**

| Trigger | par1 | Opening |
|---|---|---|
| Click the clerk (room id 11) | −1 | AIR: **C 101..103**. Phone: **P 109** |
| After the loan menu: borrowed ≥ 1000 | 101 | **P 161** |
| After the loan menu: amount < 1000 | 100 | **P 160** |
| After the repay menu | 102 | **P 162** |
| Item shown | 100xx | **P 100xx** |

**Click**

| Id | Condition | Result |
|---|---|---|
| 109 | – | **C 101..103** |
| 101 (take loan) | Credit limit (`CalcCreditLimit` rounded down to 1000) is 0 | **P 120** |
| | Phone | **P 150** (only possible in person) |
| | Otherwise | **M** loan menu (amount preset = limit, mode 1), no re-entry. The menu adds `Credit` and money, then reopens with par1 101 or 100. |
| 102 (repay) | Credit is 0 | **P 140** |
| | Phone | **P 150** |
| | Otherwise | **M** repay menu (preset = credit/2, mode 2). It reopens with par1 102. |
| 100 (balance) | Credit > 0 | **P 110** + **NW 9990110** (money, credit, interest) |
| | Otherwise | **P 111** + **NW 9990111** |
| 103, 110, 111, 120, 140, 150, 160–162 | – | end |

Note: id 100 is never offered as an option, because the option list is 101..103. The balance node looks
unreachable (**M**).

## 2. Bank, male clerk: shares (`TALKER_BANKER1`, group `Bank`)

**Entry**

| Trigger | par1 | Opening |
|---|---|---|
| Click the clerk (id 10), or phone | −1 | A takeover is pending: **P 905** (AIR) or **P 906** (phone), with the taken-over and taking airline names. Otherwise **P 900** (AIR) or **P 901** (phone). |
| The player finds money in the bank bin (room id 20) | 20 | **P 920** with names. The money is +100,000 before the dialog starts. |

**Click: main**

| Id | Result |
|---|---|
| 900, 901, 3000, 3001, 3015, 3033, 3051, 2014, 2024, 2114, 2124, 2514, 2532 | **C 1000..1005** (main menu) |
| 920 | **C 921..921** (single reply) |
| 1000 (buy shares) | Phone: **P 1200** (only in person). Otherwise **P 2000** |
| 2000, 2016 | **C 2010..2014**: four lines of (airline, free shares). Free shares = issued shares − all shares held by active players. Out players show 0. |
| 2030, 2031 | **C 2020..2024**, same content (the "after a purchase" variant) |
| 2010..2013, 2020..2023 | Target player k is out: **P 2016**. Otherwise **M** shares menu (k, buy) →2030 (→2031 on cancel) |
| 1001 (sell shares) | Phone: **P 1200**. Otherwise **P 2100** |
| 2100, 2116 | **C 2110..2114** (airline, own holding) |
| 2130, 2131 | **C 2120..2124** |
| 2110..2113, 2120..2123 | Target out: **P 2116**. Otherwise **M** shares menu (k, sell) →2130 (→2131) |
| 1002 (use shares / takeover) | **P 2500** |
| 2500, 2520..2523 | **C 2510..2514** (airline, % we hold). Out players show 0. |
| 2510..2513 | Sets `par1 = k`. If k is ourselves: **P 2520**. If k is out: **P 2521**. If we hold 0: **P 2525**. If we hold less than half: **P 2522** + **NW 9992522** (our %). If k holds ≥ 30 % of *our* shares: **P 2523** + **NW 9992523**. Otherwise **P 2524** |
| 2524 | **C 2530..2532** |
| 2530 / 2531 | Sets `Sim.Overtake = 1` (absorb) or `2` (liquidate), with taken = k and taker = us; synced over the network. Then **P 2540**. The takeover itself happens at the next morning briefing (§3). |
| 1003 (issue shares) | `free = (MaxAktien − AnzAktien)` rounded down to 100. If free < 10,000: **P 3000**. If share price < 10: **P 3001**. Otherwise **P 3002** + **NW 9993002** (shares issued, free). |
| 3002 | **C 3010..3015** offering 10 %, 25 %, 50 %, 75 %, 100 % of `free`; 3015 = cancel |
| 3010..3014 | `par1` = that percentage of `free`, rounded down to 100. Then **P 3020** |
| 3020 | **P 3021** |
| 3021 | **P 3022** (current price) + **NW 9993022** |
| 3022 | **C 3030..3033** offering price − 5, price − 3, price − 1; 3033 = cancel |
| 3030..3032 | `par2` = issue price, `par3` = discount (5, 3 or 1). Then **P 3040** + **NW 9993040** (count, issue price, current price, fee) |
| 3040 | **C 3050..3051** (confirm / cancel) |
| 3050 | Executes the share issue (below). Then **P 3060** (count) |
| 1004 (dividend) | Phone: **P 1200**. Otherwise **M** dividend menu, no re-entry |
| 1005, 1200, 2016, 2116, 2520, 2521, 2522, 2523, 2525, 2540, 3060, ... | end. (2522 and 2523 also end, because they are not in the main-menu group.) |

**Share issue (3050)**

1. The market takes part of the new shares: all of them at discount 5, 80 % at discount 3, 60 % at
   discount 1.
2. Money: + market shares × issue price (booking 3162), − fee = count × issue price / 10, rounded down to
   100 (booking 3160).
3. The new price is the weighted average of the old price over the old share count and the issue price
   over the market shares (clamped ≥ 0).
4. Compensation: the issuer pays (shares not held by itself) × (old − new price) (booking 3161). Every
   other player gains their holding of us × (old − new price).
5. Issued count += `par1`. Our own holding += the unsold part.

## 3. Airport boss (`TALKER_BOSS`, group `Boss`)

**Entry**

| Trigger | par1 | Opening |
|---|---|---|
| Morning at 09:00 | 1 | See below. The briefing auto-advances (machinery doc §3.8). |
| Demo ended or protection failed | 30 | **P 30** |
| Click the boss in his room (id 11) | 2 | **P 4000** |
| Phone | −1 | **P 4001** |

Morning opening (par1 = 1):

* **Day 0:**
  * ATFS missions: **P 1800 + (Difficulty − 40) × 10**.
  * Add-on missions: **P 1600 + (Difficulty − 10) × 10**. Add-on 3 inserts the crisis city.
  * Tutorial or free game: **P 700**.
  * Otherwise: **P 1000 + Difficulty × 100**.
* **Later days:** a takeover is pending: **P 5000**. Sabotage acts are recorded: **P 2004**. Otherwise
  **P 2000 + rand(4)**.

**Day-0 introduction chain**

| Id | Result |
|---|---|
| 700 | **P 701** |
| 701 | Mission intro, chosen the same way as the day-0 opening (ATFS / add-on / base) |
| 1000→1001, 1100→1101, 1200→1201→1202, 1300→**V 1301** (text 1301 filled with 6 mission cities + home), 1301→1302→1303, 1400→1401, 1500→1501→1502 | Mission text chains |
| 1610→1611 … 1700→1701 (step 10) | Add-on mission chains (second line) |
| 1810→1811 … 1900→1901 | ATFS chains |
| 1001, 1101, 1202, 1303, 1401, 1502 | **P 1999** |
| 1999 | End. The day starts (`DayState=2`, time runs) and the player leaves the boss room. |
| 1611 … 1901 | End through the default branch. That branch also starts the day, and it leaves the room if the clock is still 09:00. |
| 30 | Game-over menu (demo end) |

**Daily briefing**

| Id | Result |
|---|---|
| 2000..2003, 5041, 2081 | `par1` = first active player, `par2 = par3 = 0`. Free game: jump straight to the player reviews. ATFS: **P 8500 + (Difficulty − 41) × 100**. Otherwise **P 6000 + Difficulty × 100** (the mission report). |

*Mission reports.* Base: R = 6000 + d × 100 for d = 0..5. Add-on: 7100..8000 for difficulty 11..20.
ATFS: 8500..9400 for difficulty 41..50.

| Node | Result |
|---|---|
| R | **V R+1**: a composed line listing every active airline with its metric (formatted), plus **NW** with the same data. `CanCancelEmpty` is set. |
| R+1 | Picks the leader *d* by metric. If the target is not reached: **P R+2** (leader's name). If exactly one player won: **P R+3** (winner). If several won: **P R+4** (list of winners). A local winner unlocks the next mission and saves the options (table below). |
| R+2 | The player reviews (below) |
| R+3, R+4 | Game-over menu (lost = local player has not won) |

Metric and "reached" test per report:

| R | Metric (leader) | Not reached when | Unlock |
|---|---|---|---|
| 6000 | Number of completed orders (max) | Leader has not won | Mission 1 |
| 6100 | Passengers (max) | Leader has not won | 2 |
| 6200 | Profit (max) | Leader has not won | 3 |
| 6300 | Connected mission cities (max) | Leader has not won. If the leader has 0, skip directly to the reviews. | 4 |
| 6400 | Image / 10 % (max) | Leader has not won | 5 |
| 6500 | Rocket parts owned (max) | Leader has not won. If the leader has 0, skip to the reviews. | none |
| 7100 | Mission rating (**min** is the leader) | Leader has not won | Add-on 2 |
| 7200 | Freight tons (max) | Leader has not won | Add-on 3 |
| 7300 | Free freight (max) | Day < `TARGET_DAYS` | Add-on 4 |
| 7400 | Miles (max) | Day < `TARGET_MILESDAYS` | Add-on 5 |
| 7500 | Rating (max) | Leader has not won | Add-on 6 |
| 7600 | Rating (max) | Day < `TARGET_VALUEDAYS` | Add-on 7 |
| 7700, 7800, 7900 | Rating (max) | Leader has not won | Add-on 8, 9, 10 |
| 8000 | Rating (max) | Leader has not won | none |
| 8500 | Rating | Leader has not won | ATFS 2 |
| 8600, 8700, 8800 | Rating | **Nobody** has won. The winner named in R+3 is the last player who won. | ATFS 3, 4, 5 |
| 8900, 9000 | Rating | Leader has not won | ATFS 6, 7 |
| 9100 | Two ratings shown as "a/b" | Nobody has won | ATFS 8 |
| 9200 | Rating | Leader has not won | ATFS 9 |
| 9300 | Rating | Day < `BTARGET_NDAYS9` | ATFS 10 |
| 9400 | Rating | Day < `BTARGET_NDAYS10` | none |

The win test itself is `PLAYER::HasWon()`, which belongs to the missions spec.

*Player reviews.* These cover each active player in turn. `par1` is the player and `par3` is the index
used for the opening phrase.

| Id | Result |
|---|---|
| R+2 (any report), 2010..2013, 20110, 20111 | Builds **V 12040**: an opening phrase `Boss/2010 + par3` (with the airline name) plus an image remark, picked at random from a sub-range (table below). It also makes the boss look toward players 1–2 or 3–4. |
| 12040, 20100..20102 | If image < −990: **P 20230/20231** (random). If money < `DEBT_GAMEOVER` (−5,000,000): **P 3000**. Otherwise **V 2040**: a fleet remark plus a crew remark (tables below). |
| 2040..2044, 3002 | Next active player. Once all are done: **P 2102**, and the boss turns back. |
| 20230, 20231 | Image too bad. Local player: game-over menu. Otherwise that player is marked out, all mutual shareholdings are cleared, and the review continues with the next player. |
| 3000 | **P 3001** (airline) |
| 3001 | Local player: game-over menu. Otherwise the player is out, holdings are cleared, then **P 3002** |
| 2102 | End of briefing. The day starts and the player leaves the room. |

Image remark (checked in this order):

| Condition | Text ids |
|---|---|
| Image got worse since yesterday | 20200..20202 |
| Image < −100 | 20210..20212 |
| Image > 500 | 20260..20264 |
| Image > 100 | 20250..20257 |
| Otherwise | 20240..20249 |

Branches for −400 (20220..20221) and −990 (20230..20231) exist but can never be reached, because the
−100 test comes first.

Fleet remark (average condition):

| Average condition | Text |
|---|---|
| < 40 | 2030 |
| < 60 | 2031 |
| < 80 | 2032 |
| < 90 | 2033 |
| Otherwise | 2034, 2037 or 2038 at random |

`Boss/2036` is added if the player has made no flights. `Boss/2035` is used instead if the player has no
planes.

Crew remark (average `PersonalQuality`):

| Average quality | Text |
|---|---|
| < 40 | 2040 |
| < 60 | 2041 |
| < 70 | 2042 |
| < 80 | 2043, 2046 or 2047 at random |
| Otherwise | 2044 |

With no planes, player 4 gets 2040 or 2041 at random; the other players get 2043, 2046 or 2047.

*Sabotage report.* This runs before the mission report when acts were recorded in `Sim.SabotageActs`
(each act stores perpetrator, mode and victim). `par1` is the act index and `par2` is the
phrase-escalation counter.

| Id | Result |
|---|---|
| 2004, 5040 | Reset `par1 = par2 = 0`, then step 2080 |
| 2080 (internal loop) | Reset the figures' moods. If acts remain: if the same victim was already named, **P 2055** (victim). Otherwise **P 2050 + par2** (victim), with the perpetrator's figure looking innocent unless the perpetrator is "foreign" (−2). `par2` = min(par2 + 1, 3). When there are no more acts: **P 2081** |
| 2050..2053, 2055 | Mode < 100: **P 2060 + mode**. Otherwise **P mode** (raw text id) |
| 2061..2065, 2075..2078, 2091..2096 | The boss raises a finger. Perpetrator foreign or `ArabHints` < 15: **P 2070**. < 40: **P 2071**. < 100: **P 2072**. Otherwise **P 2073** (names the culprit). |
| 2070, 2071, 2072, 2074 | Mark the act handled. Next act. After the last act the boss glares at everyone, then step 2080. |
| 2073 | **P 2074** (victim) + **NW 9992074** (fine, victim). Fine = `ArabHints` × 10,000, moved from the culprit (booking 2200, statistic "penalties") to the victim (booking 2201). |
| 2081 | Same as 2000: continue with the mission report |

*Takeover (decided at the bank).*

| Id | Result |
|---|---|
| 5000 | **P 5001** (taker, taken) |
| 5001 | **P 5010** (absorb) or **P 5020** (liquidate) |
| 5010, 5020 | Executes the takeover (below), then **P 5030** (taken) |
| 5030 | Local player taken over: game-over menu. Otherwise the taken player is marked out and holdings are cleared. `Overtake = 0`. Then **P 5040** if sabotage acts are recorded, otherwise **P 5041** |

Executing a takeover (5010/5020). The taken airline's sabotage modes are cleared, an extra newspaper is
scheduled, and its passengers in flight are removed. Then:

* **Absorb:**
  * Its workers transfer to the taker, unassigned.
  * Its planes transfer: their schedules are cleared and they are placed at the home airport.
  * If the taken airline was not human, the taker gets virtual reserve pilots and attendants equal to
    the planes' crew needs.
  * Money, credit, issued shares, holdings and share values are added to the taker. Third parties'
    holdings of the taken airline become holdings of the taker.
  * Gates move into the taker's free slots.
  * Routes and city slots move to the taker when it has no rank on them, or when its rank is worse. In
    the second case the ranks of third parties are fixed up.
* **Liquidate:**
  * Staff are fired.
  * Planes are sold at their value into the taken airline's money; gates, routes and cities are
    released (ranks are fixed up).
  * Its holdings in other airlines are sold at market price.
  * Its net (money − credit) is paid out to every shareholder in proportion to their stake.
  * The gate mapper and statistics are recomputed.

*Talking to the boss (par1 2, or phone).*

| Id | Result |
|---|---|
| 4000, 4001 | **C 4010..4012**, or **C 4011..4012** in the free game (no mission question) |
| 4010 | **P 4050** |
| 4050 | Mission description. ATFS: **P 4084 + (d − 40)**. Add-on: **P 4070 + (d − 10)**; add-on 3 includes the crisis city; add-on 9 adds **NW 4099** (text 4090 + 9). Normal difficulty: **V 4063** formatted with home + 6 mission cities. Otherwise **P 4060 + d** |
| 4011 (ask for a gate) | Free gates exist: **P 4110**. Expansions already ≥ 5, or ≥ 2 on Normal or easier (not free game): **P 4111**. Day < 8: **P 4114**. Last expansion less than 3 days ago: **P 4115**. Expansion already ordered: **P 4112**. Otherwise **P 4113** |
| 4113 | **C 4120..4121** |
| 4120 | Orders the airport expansion (synced), pays 1,000,000 (booking 3170), then **P 4130** |
| 4121 | **P 4131** |
| 4012, 4130, 4131, 4110..4115, 4060..4064 | Phone: **P 4999**. Otherwise end (the day starts). |
| 4999 | End |
| 10003, 10004 | End (item reactions) |
| any other | End. The default also starts the day, and leaves the room if the clock is at 09:00. |

## 4. Personnel office (group `Jobs`)

### 4a. Hiring clerk (`TALKER_PERSONAL1a+2p`, voice `PE`)

**Entry**

| Trigger | par1 | Opening |
|---|---|---|
| Phone | – | **P 101** + **NW 9990101** (jobless advisors, pilots, attendants) |
| First talk this visit (id 10) | – | **S 99** |
| Later talks this visit | – | **P 102** |
| Click id 20 in the room | 20 | `SeligTrust` set: **P 301**. Otherwise **P 302** |
| Postcard given | 300 | **P 300** |

**Click**

| Id | Result |
|---|---|
| 99 | **P 100** |
| 100, 102 | **C 200..203**, each option formatted with its count (advisors, pilots, attendants, leave) |
| 250 | Same list (string-formatted variant) |
| 200 / 201 / 202 | **M** personnel list for advisors / pilots / attendants, in hire mode (zoom from (291,279)) |
| 300 | `SeligTrust = TRUE`, end |
| 301 | Gives the pills item if there is space, end |
| 101, 203, 302 | End |

### 4b. Staff clerk (`TALKER_PERSONAL1b+2p`)

**Entry**

| Trigger | par1 | Opening |
|---|---|---|
| Strike-ended call | 1000..1010 | **P par1** (1000 + `StrikeEndType`) → end |
| Strike in progress (`StrikeHours` and no end countdown) | – | **P 950** |
| Phone | – | **P 501** |
| Later talks this visit | – | **P 503** |
| First talk | – | **S 499** |

**Click**

| Id | Result |
|---|---|
| 499 | **P 500** |
| 500..503 | **C 600..605** |
| 600 (raise wages) | Wages up (`Gehaltsaenderung(1)`), `StrikePlanned = FALSE`. Happiness is raised in steps of 10 until the average (−10 if any worker is below 0) is ≥ 20. Then **P 700** |
| 700 | **P 820** |
| 601 (lower wages?) | **P 800** |
| 800 | **C 810..811** |
| 810 | Wages down (`Gehaltsaenderung(0)`), then **P 820** |
| 811 | **P 820** |
| 820 | **C 600..605** |
| 602 / 603 / 604 | Phone: **P 900**. Otherwise **M** staff list for advisors / pilots / attendants in mode 2 (manage or fire), zoom from (405,261) |
| 950 | **C 960..962** |
| 960 | Strike ended by a raise: end type 2, countdown 2, wages up, the same happiness top-up as 600. Then **P 970** |
| 961 | Strike ended by a threat: end type 1, countdown 4, happiness −20. Then **P 971** |
| 962 | **C 600..605** |
| 970, 971 | **P 502** → (500..503 group) **C 600..605** |
| 605, 900, 1000.. | End |

Closing either personnel dialog remaps worker overflow.

## 5. Mechanic (`TALKER_MECHANIKER`, group `Mech`)

**Entry**

| Trigger | par1 | Opening |
|---|---|---|
| Click (id 10), phone | 1 | If `MechAngry == 2`: **P 5002** (AIR) or **P 5003** (phone). Otherwise phone **P 1001**, later talks **P 1002**, first talk **P 1000** |
| Beer given | 2 | **P 10000** |
| Click the oil can (id 20) without trust | 20 | **P 1020** |
| Click the oil can with trust | 21 | **P 1021**, gives oil |

**Click** (the main list is **C 2000..2006** in person, **C 2100..2102** on the phone)

| Id | Result |
|---|---|
| 1000, 1001, 1002, 3005, 2500 | Main list |
| 2000, 2100 (what can you do) | Monologue: **P 3000** → 3001 → 3002 → 3003 → 3004 (only if `MechTrust == 2`; otherwise it skips to 3005) → 3005 → main list |
| 2001, 2101 (maintenance mode) | **P 4000 + MechMode** (describes the current mode) |
| 4000..4003, 4020, 4021 | If `MechTrust == 2`: **C 4010..4013** (modes 0..3). Otherwise **C 4011..4014** (modes 1..3 plus a special option) |
| 4010..4013 | `MechMode = id − 4010` (synced), then **P 4060** + **NW 9994060** (daily cost = `gRepairPrice[mode]` × planes / 30, with prices 1200, 18000, 48000, 150000) → end |
| 4014 | If `MechTrust == 1` it becomes 2. Then **P 4021** if trust is 2, otherwise **P 4020** → the mode list again |
| 2002 (costs) | Day 0: **P 2500**. Otherwise **P 2501** |
| 2501 | **M** plane costs →2502 |
| 2003 | **M** plane repairs →2502 |
| 2004 | **M** breakdowns →2502 |
| 2005 (you're smoking) | `MechAngry == 0`: **P 5000**. Otherwise **P 5001** |
| 5000, 2502 | `MechAngry = 1`, main list. Note: returning from *any* of the three menus also sets `MechAngry = 1` (**H**, probably unintended). |
| 5001, 5002, 5003 | `MechAngry = 2`. In person the player is thrown out of the room. End |
| 2006, 2102, 4060, 10000, 1020, 1021 | End |

How trust is gained: beer sets `MechTrust ≥ 1` and resets `MechAngry`; 4014 raises it to 2. The
"best" mode (0) is only offered at trust 2.

## 6. Plane broker (`TALKER_MAKLER`, group `Makl`)

**Entry**

| Trigger | par1 | Opening |
|---|---|---|
| Click (id 10), or phone | −1 | Phone: **P 82**. Later talks this visit: **P 83**. Met before in this game: **P 81**. First ever: **P 80**. Sets the overture flag. |
| After a plane type is picked in the buy menu | 130+rand(4), par2 = type | If the player can't afford one: **P 6000**. Otherwise **P par1** |

**Click**

| Id | Result |
|---|---|
| 80, 81, 83, 110, 150, 170 | Phone: end. Otherwise **C 100..103** |
| 82 | **C 1100..1103** (phone list) |
| 100 (buy) | **P 120 + medium** |
| 120 | **M** buy plane (no re-entry). The picked type reopens the dialog at 130..133 |
| 121, 1200 | End (phone purchases not possible) |
| 1100 | **P 1200** |
| 101, 1101 (sell?) | **P 110** (the broker doesn't buy) |
| 102, 1102 (delivery?) | **P 170** |
| 130..133 | Price = `PlaneTypes[par2].Preis`. Can't afford one: **P 6000**. Otherwise **C 140..141+k** (140 = none; 141.. = buy 1, 2, 3, 5, 10, up to the largest affordable count; each option shows the total price) |
| 140 | **P 160** → end |
| 141..145 | Re-check money (otherwise **P 6000**). Buy N planes, each with a random generator seeded with the date. Network broadcast; bodyguard discount; workers remapped; personnel advisor updated. Then **P 150** → list |
| 103, 1103, 6000 | End |

## 7. Museum, used planes (`TALKER_MUSEUM`, group `Muse`)

**Entry**

| Trigger | par1 | Opening |
|---|---|---|
| Click the attendant (id 11), or phone | −1 | Phone: **P 101**. Met before (flag `MUSEUM`): **P 102**. Otherwise **P 100**. Sets the flag. |
| Click one of the 3 displayed planes (x 125..364 split in thirds, plane exists) | 0..2 | **P 500** (that plane's price) |
| After choosing an own plane in the sell menu | 100/101/102/103, par2 = plane | 100 + 2 × sponsored, or that + 1 if the plane can't be sold (flights booked). Shows **P 710..713** + **NW 9990710..9990713** (offer = 90 % of value) |

**Click**

| Id | Result |
|---|---|
| 100, 101, 102 | **C 201..203**, or **C 211..213** if `MUSEUM2` (visited on an earlier visit) |
| 200 | **P 300** (not offered in any list) |
| 201 / 211 (do you sell planes) | **P 400**/**402** in person, **P 401**/**403** on the phone → end |
| 202 / 212 (buy my plane) | **P 700** / **P 701** |
| 700, 701 | Phone: back to the list. Otherwise **M** sell plane →699 |
| 500 | **C 600..601** |
| 600 | Can't afford: **P 6000**. Otherwise buy the used plane: worst condition = condition − 20, price booked (2010), slot emptied, museum refill timer reset. End |
| 710, 712 | **C 720..721** |
| 720 | Sell for 90 % of value (booking 2011). The plane is removed and the reference plane is fixed. End |
| 699, 711, 713, 721, 203, 213, 6000 | End |

## 8. Arab Air, kerosene (`TALKER_ARAB`, group `Arab`)

**Entry**

| Trigger | par1 | Opening |
|---|---|---|
| Click (id 10), or phone | 0 | Phone: **P 101**. Later talks: **P 102**. Otherwise **P 100** |
| MG given | 1 | `ArabTrust ≥ 1`, then **P 1010** |
| (unused) | 2 | `ArabTrust == 0`: **P 1000**, otherwise **P 1020** |

**Click**

| Id | Result |
|---|---|
| 100, 101, 102, 650 | **C 200..203** |
| 250, 301, 403, 700, 904 | **C 201..203** |
| 200 (general info) | **P 250** |
| 201 (kerosene) | **P 300** (in person) or **P 301** (phone) + **NW 9990300/9990301**. The three prices are quality good = 2 × `Sim.Kerosin`, normal = 1 ×, bad = 0.5 × |
| 300 | **C 400..403** (with the three prices; 403 = back) |
| 400..402 | `KerosinKind = id − 400`. The kerosene advisor comments (`Advi/3020 + kind`). Then **P 500** |
| 500, 620 | **M** buy kerosene →650 |
| 604 | **P 620** |
| 202 (tanks) | **P 800** |
| 800 | **C 900..904** (tank sizes; 904 = back) |
| 900..903 | `par1 = id`, then **P 680** |
| 680 | **C 690..695** (690 = none; 691..695 = 1, 2, 3, 5, 10 tanks with totals) |
| 690 | **P 650** |
| 691..695 | Can't afford: **P 6000**. Otherwise capacity += size / 1000 × N and money −= price × N (booking 2091). Then **P 700** |
| 1010 | **P 1020** |
| 203, 1020, 1000, 6000 | End |

Tank table (size, price): 900 → (10000, 10000), 901 → (30000, 20000), 902 → (50000, 40000),
903 → (100000, 60000).

## 9. Saboteur (`TALKER_SABOTAGE`, group `Sabo`)

**Entry**

| Trigger | par1 | Opening (checked in this order) |
|---|---|---|
| Sabotage-done call / in room | 2000 | **P 2000** → end |
| Menu results | 1070, 1090, 1091 | **P par1** |
| Item | 100xx | **P 100xx** |
| `ArabTrust == 0` (never brought the MG to Arab Air) | – | **P 1021** → end |
| Click the hidden object (id 800) | 800 | `SpiderTrust` set: **P 3002**. Otherwise **P 3001** |
| Spider given | 3000 | **P 3000** |
| No job pending (`ArabMode == 0` and `ArabMode2 == 0`) | 0 | **P 1022** if talked to already, otherwise **P 1020** |
| Otherwise | – | **P 1025** → end (a job is already pending) |

**Click**

| Id | Result |
|---|---|
| 1020, 1022, 1045, 1500 | **C 1030..1033**: the three other players as "airline (name)"; 1033 = cancel |
| 1030..1032 | Victim (`ArabOpfer`, `ArabOpfer2`, `ArabOpfer3`) = that player. Victim out: **P 1045**. Otherwise **P 1040** |
| 1040 | **C 1047..1049** (plane / person / special category) |
| 1047 | Any job pending: **P 1100**. Otherwise **C 1050..1050+min(5, trust)** |
| 1048 | Pending: **P 1100**. Otherwise **C 1250..1250+min(4, trust)** |
| 1049 | Pending: **P 1100**. Otherwise **C 1080..1080+min(6, trust)** |
| 1050, 1250 | **P 1071** → end (cancel) |
| 1051..1055 | Category 1 (blocks below). If not blocked and affordable: the victim's `SecurityNeeded` bit is set (6 for 1051, 1052 and 1055; 7 for 1053 and 1054), `ArabActive = id − 1050`, then **P 1060** |
| 1060 | **M** pick a victim plane. The selection reopens with par1 1070; cancelling gives 1071 |
| 1070 | Commit: `ArabMode = ArabActive`, pay `SabotagePrice[mode − 1]`, `ArabTrust = min(5, trust + 1)`, synced. End |
| 1251..1254 | Category 2 (blocks below). Affordable. 1252 also needs the victim to own a laptop, otherwise **P 1300**. Commit right away: `ArabMode2 = id − 1250`, trust = max(trust, index + 1), pay `SabotagePrice2[i]`. Then **P 1160** |
| 1081..1084 | Category 3 (blocks below). Commit: `ArabMode3 = id − 1080`, trust = max(trust, index + 1), pay `SabotagePrice3[i]`. Then **P 1160** |
| 1085 | Blocked by bit 8: **P 2098**. Otherwise **M** pick a plane →1090 |
| 1090 | `ArabMode3 = 5`, pay 5,000,000, then **P 1160** |
| 1086 | Blocked by bit 5: **P 2095**. Otherwise **M** pick a route →1091 |
| 1091 | `ArabMode3 = 6`, pay 8,000,000, then **P 1160** |
| 3000 | `SpiderTrust = TRUE`, end |
| 3002 | Gives the dart item if there is space, end |
| 1033, 1071, 1100, 1160, 1300, 2090..2098, 3001, 6000, 2000 | End |

Block checks for categories 1–3 (on the victim's `SecurityFlags`):

| Ids | Blocking bit | Refusal |
|---|---|---|
| 1051, 1052, 1055 | 6 | **P 2096** (victim) |
| 1053, 1054 | 7 | **P 2097** |
| 1251, 1253 | 0 | **P 2090** |
| 1252 | 1 | **P 2091** |
| 1254 | 2 | **P 2092** |
| 1081 | 8 | **P 2098** |
| 1082, 1083 | 5 | **P 2095** |
| 1084 | 3 | **P 2093** |

If the player can't afford the price: **P 6000**.

Prices:

| Category | Ids | Prices |
|---|---|---|
| 1 | 1051..1055 | 1000, 5000, 10000, 50000, 100000 |
| 2 | 1251..1254 | 10000, 25000, 50000, 250000 |
| 3 | 1081..1084, 1090, 1091 | 100000, 500000, 1000000, 2000000, 5000000, 8000000 |

Quirks (**H**):

* The network money message for 1081..1084 uses the category-2 table.
* The trust formula for 1085, 1090 and 1091 gives negative values, so trust never changes for those.
* What each `ArabMode` does to the victim belongs to the sabotage spec.

## 10. Advertising agency (`TALKER_WERBUNG`, group `Werb`)

**Entry**

| Trigger | par1 | Opening |
|---|---|---|
| Dart given (Normal or harder, or free game) | 8001 | **P 8001** |
| Click the hidden object (id 800) | 800 | `WerbungTrust` set: **P 8002**. Otherwise **P 8000** |
| After an ad route is picked | 3501, par2 = route | **P 3501** |
| Click (id 10), or phone | 1 | Phone: **P 1001**. Later talks: **P 1003**. Met before: **P 1002**. Otherwise **P 1000**. Sets the flag. |

**Click**

| Id | Result |
|---|---|
| 1000..1003, 3400, 3401 | **C 2000..2004** |
| 2500..2509, 2520 | **C 2001..2004** |
| 2000 (my image?) | Image is clamped to ±1000. Image 0: **P 2520**. Negative: **P 2503 + Image / 251** (2500..2503) + **NW 9992050** (abs). Positive: **P 2504 + Image / 167** (2504..2509) + **NW** |
| 2001 (company ad) | `par1 = 0`, then **P 3000** |
| 2003 (everything) | `par1 = 2`, then **P 3000** |
| 2002 (route ad) | `par1 = 1`, then **P 3500** |
| 3500 | **M** pick route →3400 (cancel). Picking a route reopens with 3501 |
| 3501 | `par1 = 1`, then **P 3001** |
| 3000 | **P 3001** |
| 3001 | **C 4000..4006**: six campaigns with prices `gWerbePrice[par1 × 6 + k]`; 4006 = none |
| 4000..4005 | **P 5000..5005** (describes the campaign) |
| 5000..5005 | Can't afford: **P 6000**. Otherwise apply the effect (below) and pay (booking 3120 + k, statistic "other"), synced. Then **P 3401** |
| 8001 | `WerbungTrust = TRUE`, end |
| 8002 | Gives the diskette item if there is space, end |
| 2004, 4006, 6000, 8000 | End |

Campaign effects (k = option index 0..5, P = price):

| Type | Effect |
|---|---|
| Company | Image += P / 10000 × (k + 6) / 55, clamped to ±1000 |
| Route | Route image += P / 30000, also on the reverse route, clamped 0..100 |
| Everything | Image += P / 15000 × (k + 6) / 55, and every rented route's image += P × (k + 6) / 6 / 120000 |

With the cheapest option (k = 0), every human, non-out player receives a mocking letter (`Lett` texts
9900/9910/9920 + 0..2).

Price table:

| Type | Prices |
|---|---|
| Company | 1000, 10000, 50000, 100000, 250000, 2500000 |
| Route | 2000, 8000, 22000, 56000, 120000, 1400000 |
| Everything | 5000, 93000, 290000, 870000, 4925000, 60000000 |

## 11. Duty free (`TALKER_DUTYFREE`, group `Duty`)

**Entry**

| Trigger | par1 | Opening |
|---|---|---|
| Click the laptop shelf (id 10) or the counter (id 111) with a laptop virus | 2000 / 2010 / 2020 | Virus state 1: **S 2000** (single, parameter-indexed). State 2: **P 2010**. State 3: **P 2020** |
| Laptop shelf, day ≤ 7 (`DAYS_WITHOUT_LAPTOP`) | 3 | Day < 7: **V 100** = text 97 + **NW 9990097** (days left = 8 − day). Day 7: **P 98** |
| Laptop already sold today to another player | 1, par2 = buyer | **V 100** (text 100 with the buyer's airline), or **P 99** if the buyer is us |
| Laptop bought, quality < 4 | 4 | **P 1010** (shown after the purchase) |
| Counter (id 111) without a virus | 2 | **P 1000** |
| Click the hidden object (id 800) | 800 | `DutyTrust` set: **P 802**. Otherwise **P 800** |
| Bra given | 801 | **P 801** |
| Click the pralines (id 801) | 3000 | **P 3000** |

**Click**

| Id | Result |
|---|---|
| 1000 | **S 1001** |
| 1001 | **V 100** = text 1002 (+ 1003 if a laptop was sold today) + 1004 → end |
| 2000 | **P 2001** |
| 2001 | Hands the laptop in for repair: `LaptopVirus = 2`, all laptop items removed. End |
| 2020 | Gets the laptop back: `LaptopVirus = 0`, laptop item added. End |
| 3000 | **C 3001..3003** |
| 3001 | Buy pralines-A |
| 3002 | Buy pralines. Both use price `Item/2801` and are booked as "other". End |
| 801 | `DutyTrust = TRUE`, end |
| 802 | Gives the horseshoe if there is space, end |
| 2010, 3003, 97–100, 1010, 800 | End |

The 2 → 3 step of `LaptopVirus` happens outside the dialog (simulation; **M**).

## 12. NASA (`TALKER_NASA`, group `Nasa`)

**Entry**

| Trigger | par1 | Opening |
|---|---|---|
| Entering the room | 0 | **P 500** → end |
| Click the clerk (id 10), or phone | 1 | Phone: **P 1001**. Later talks: **P 1003**. Met before: **P 1002**. Otherwise **P 1000** |
| First attempt to leave (id 999 or right-click) | 2 | **P 9999**. Clicking it makes the player leave. The player portrait is hidden. |

The option set depends on the mission. Let B = 2000 for the Final mission (rocket) or 2200 otherwise
(space station, add-on 10), plus 100 on the phone.

| Id | Result |
|---|---|
| 1000..1003 | **C B..B+3** |
| 2000, 2100, 2200, 2300 | **P 3000** |
| 3000 | **S 3001** |
| 3001 | **P 3002** |
| 3002 | **C B+1..B+3** |
| 2001, 2101 | **P 4000** → 4001 → 4002 → 4003 → **C (2002..2003) + phone offset** |
| 2201, 2301 | **P 4100** → 4101 → 4102 → 4103 → **C B+2..B+3** |
| 8003, 8105, 8202 | **C B+2..B+3** |
| 4003, 5003, 5107 | **C 2002..2003** (+100 on the phone) |
| 2002, 2102, 7000, 5096..5099, 5196, 5197 (rocket shop) | If part one (base, tower, arm) is incomplete: **C 5000..5003**. Otherwise **C 5100..5107** |
| 5000 | Base, 200,000 |
| 5001 | Tower, 400,000. Needs the base, otherwise **P 5096** |
| 5002 | Arm, 600,000. Needs the tower, otherwise **P 5097**. There is no "already owned" check. |
| 5100 | Airframe, 5,000,000 |
| 5101 | Wings, 8,000,000 |
| 5102 | Capsule, 10,000,000 |
| 5103 | Tail, 20,000,000 |
| 5104 | Propulsion, 25,000,000. Also needs the wings, otherwise **P 5197** |
| 5105 | Main engine, 50,000,000 |
| 5106 | Cockpit, 85,000,000 |
| 2202, 2302, 8300..8302 (station shop) | If station part two is complete: **C 8200..8202**. If part one is complete: **C 8100..8105**. Otherwise **C 8000..8003** |
| 8000..8002, 8100..8104, 8200..8201 | Station part bit = 0..2, 3..7, 8..9. Owned: **P 8301**. Money < price: **P 8300**. Otherwise set the bit, pay (booking 3400), fanfare, then **P 8302**. Prices by bit: 1M, 2M, 3M, 2M, 10M, 20M, 35M, 50M, 35M, 80M |
| 2003 | **P 6000** → end |
| 9999 | Leave the room, end |
| 500, 6000, 2203, 2303 | End |

Rocket-part purchase rules (5000..5106):

* Already owned: **P 5098**.
* Parts 5101..5106 need the airframe, otherwise **P 5196**.
* Money below the price: **P 7000**. Rocket parts require **cash**; the overdraft does not count.
* Success: set the flag, pay (booking 3400), play the fanfare, set the room's tip variable (`KommVarTippNow = 12`), then **P 5099**.

## 13. Rick's Cafe

### 13a. Rick (`TALKER_RICK`, group `Rick`)

**Entry** (click id 10):

* Met before (`RICK`) and already talked today (`RICK_TODAY`): **P 3000** → end.
* Met before, not today: **S 2000** ("anything new?").
* Never met: **P 1000**.

Both `RICK` and `RICK_TODAY` are set at this point.

**Click**

| Id | Result |
|---|---|
| 1000 | **S 1100** |
| 1100 | **V 1200** (text 1200 with our airline) |
| 1200 | **S 1300** |
| 1300 | **P 1400** → 1401 → end |
| 2000 | **Tip** (below) → end |

The tip is the first match in this list. Steps marked "1/5" only fire on a 1-in-5 random roll each:

1. Day 1 of the Final mission: **P 2100**.
2. Day 1 of the Normal mission: **P 2101**.
3. A plane type available in this mission (or the free game) debuts today: **V 9999** (text 2102 with the
   type and maker).
4. Day > 3 and money is in the bank bin (1/5): **P 2104**.
5. Day > 15 and `MechTrust == 0` (1/5): **P 2106**.
6. The first advisor type, in the order info, fitness, security, orders, money, personnel, route, that
   has applied but is not hired (1/5): **P 2107..2113**.
7. A type debuts in 3 days: **V 9999** (text 2103).
8. Some active rival with sympathy > 0 toward us is not cooperating (1/5): **P 2114**.
9. Per plane, each 1/5: target condition − worst condition > 20 → **P 2115**; target condition < 60 →
   **P 2116**; condition ≤ worst + 2 and worst ≤ 80 → **P 2121**.
10. We own less than half of our own shares (1/5): **P 2117**.
11. At least 10,000 shares are still issuable (1/5): **P 2118**.
12. Dividend < 5 (1/5): **P 2119**.
13. Money > 30,000,000 (1/5): **P 2120**.
14. Otherwise: **P 2199** (nothing new).

### 13b. Drinker (`TALKER_TRINKER`, group `Rick`, voice `TR`)

**Entry** (click id 11):

| Trigger | par1 | Opening |
|---|---|---|
| Horseshoe given | 800 | **P 800** → end |
| `TrinkerTrust` and a strike is running, already being ended by him (end type 3) | – | **P 6001** |
| `TrinkerTrust` and a strike is running, not yet being ended by him | – | **P 6000**. At this point (on opening) the end countdown is set to 10, type 3. |
| Otherwise | – | **P 5000 + rand(9)** → end |

**Click:** 6000 sets the countdown to 4, type 3 ("he talks the staff out of the strike"), then end.
Anything else ends.

## 14. Kiosk (`TALKER_KIOSK`, group `Kios`)

**Entry**

| Trigger | par1 | Opening |
|---|---|---|
| Click the owner (id 10) | −1 | **P 1000** → end |
| Click the stink-bomb spot (id 20, not owned) | 1000 | `KioskTrust`: **P 1021**. Otherwise **P 1020** |
| Energy drink given | 1010 | **P 1010** |

**Click:** 1021 gives the stink bomb if there is space and resets `KioskTrust = 0`, then end. Everything
else ends. No partner animation is attached.

## 15. Freight (`TALKER_FRACHT`, group `Frac`)

The only nodes are partner lines; any click ends the dialog.

| Trigger | Line |
|---|---|
| Click the worker (id 11) | **P 1000** |
| Click the glue spot (id 12) while glue is not yet available | **P 1010** |
| Paper clip given | **P 1020** |
| Item shown | **P 100xx** |

## 16. Security (`TALKER_SECURITY`, group `Secu`)

| Id | Result |
|---|---|
| Entry (id 10) | **P 1000** |
| 1000 | **C 2000..2002** |
| 2000 | **P 3000** |
| 3000 | **C 2001..2002** |
| 2001 | **M** security menu (no re-entry) |
| 2002 | **P 4000** → end |

## 17. Designer (`TALKER_DESIGNER`, group `Desi`)

| Id | Result |
|---|---|
| Entry (id 10) | **P 1000** |
| 1000 | **C 2000..2002** |
| 3040 | **C 2001..2002** |
| 2000 | **P 3000** |
| 3000 | **S 3010** |
| 3010 | **P 3020** |
| 3020 | **S 3030** |
| 3030 | **P 3040** |
| 2001 | **P 4000** |
| 4000 | Lists the buildable designs (`*.plane` in the player's plane folder). None: **P 4002**. Some: **P 4001** |
| 4001 | **M** buy designer plane. Picking a design reopens with par1 6000 (filename in the menu parameter); cancelling ends the dialog |
| 6000 | Cost = `CXPlane.CalcCost`. Can't afford one: **P 6020**. Otherwise **C 6010..6011+k** (6010 = none; 1, 2, 3, 5, 10 with totals) |
| 6011..6015 | Re-check money (otherwise **P 6020**). Buy N planes of that design (date-seeded random), sync, discount, remap workers. Then **P 6030** |
| 2002 | **P 5000** → end |
| 4002, 6010, 6020, 6030 | End |

## 18. Branch-office phone (`TALKER_WORLD`, group `Welt`)

This is reached only by phone, through the branch list.

* If the city was not called today (`CalledCities`):
  * If the city has a recorded wave: **P 1000** (owner airline, city).
  * Otherwise: **S 2000** directly.
* If it was already called: the foreign-orders menu opens straight away.

| Id | Result |
|---|---|
| 1000 | **S 2000** (our airline) |
| 2000 | **C 3000..3002** (with the city name) |
| 3000 (small talk) | **P 4000 + (day + city) mod 4** |
| 4000..4003 | **C 3001..3002** |
| 5000 | Marks the city as called, then **C 3001..3002** |
| 3001 | **M** foreign orders for that city. Cancelling before the city is marked gives re-entry 5000; afterwards it returns to the branch list |
| 3002 | End and return to the branch list |

Right-click also returns to the branch list.

## 19. Other players (`TALKER_COMPETITOR`, group `Play`)

`DialogPar2` toggles every exchange: 1 = our turn (a choice list), 0 = the partner's turn. For an AI
partner, the partner's line is computed locally. For a network human, `DIALOG_NEXT` is sent and the
other side picks (machinery doc §8). Let *r* = the partner is an AI.

The main list is offered from 2000, 2010, 2020 or 2030 depending on who the partner is and whether we
have pralines. **Main list variants:**

| Partner | Pralines (and in person) | List |
|---|---|---|
| Human | yes | **C 2030(+1)..2034** |
| Human | no | **C 2020(+1)..2023** |
| AI | yes | **C 2010(+1)..2013** |
| AI | no | **C 2000(+1)..2002** |

The "+1" applies when the list is not reached directly from 1600 (the first offer includes the
"what's up" line). Meaning per offset:

| Offset | Meaning |
|---|---|
| +0 | Small talk |
| +1 | Cooperation talk |
| +2 | Pralines (lists 2010, 2030) or chat (2022) |
| +3 | Chat (2033) or bye |
| last | Bye |

**Click**

| Id | Our turn (par2 becomes 1) | Partner's turn, AI |
|---|---|---|
| Opening | – | **P 1000/1001** (partner, `par2 = 0` at start) or **S 1000/1001** (us, `par2 = 1`) |
| 1000 | **S 1200** | **P 1200** |
| 1200 | **C 1600..1602** | Sympathy toward us < −400: **P 1602**. < −100: **P 1601**. Otherwise **P 1600** |
| 1001, 1600, 2500..2503, 2200..2202 (2200..2202 toggle twice, so the turn doesn't change and we get the main list again) | Main list | **P 2000** or **2001** |
| 2000, 2010, 2020, 2030 (small talk) | **C 2500..2503** | By the AI's yesterday balance: > 10M **P 2500**; > 1M or day 0 **P 2501**; ≥ 0 **P 2502**; otherwise **P 2503** |
| 2001, 2011, 2021, 2031 (cooperation) | Already cooperating: **C 20210..20213**. Otherwise **C 20200..20201** | Cooperating and sympathy < −20: **P 20211** (the AI cancels). Otherwise **P 20200** (the AI proposes) |
| 2012, 2032 (pralines) | Human: give pralines → **P 2200** (sent as text). Pralines-A → **P 2202** and the other player becomes drunk (+400). Item dropped | AI: pralines → **P 2200** (if sympathy > 0) or **P 2201**, sympathy +10. Pralines-A → **P 2202**, sympathy −10. Item dropped |
| 2022, 2033 (chat) | End the dialog, open the chat menu (humans) | – |
| 20200 (proposal made) | **C 20400..20401** | Sympathy > 0: **P 20400** (accept). Otherwise **P 20401** |
| 20400 (accept) | Both sides cooperate, partner sympathy +30, synced. Our turn: main list again | **P 2003** (AI says bye) |
| 20401 (decline) | Sympathy −10. Main list | **P 20402**, sympathy a further −20 |
| 20210 (cancel bluntly) | Cooperation off, sympathy −200, then **S 20402** | **P 20402** |
| 20211 (cancel politely) | Cooperation off, then **S 20402** | **P 20402** |
| 20212 (flatter) | Sympathy +10, then **S 20403** | **P 20403** |
| 20201, 20213 | Back to the main list | – |
| 20403 | Toggle, back to the main list | – |
| 1601 (bye) | If it was our line: sympathy −10, and if < −100 cooperation ends. Partner's line (AI): cooperation ends. End | |
| 1602 (rude bye) | Our line: sympathy −100 and cooperation ends. AI: cooperation ends. End | |
| 2499 (bored) | Shown by the timeout (machinery doc §8). The next click ends the dialog | |
| 20402, anything else | End | |

All sympathy values are clamped to ±1000 and are the partner's sympathy toward the local player. AI
calls to the human (machinery doc §6.3) open this dialog with the AI speaking first.

## 20. Passenger (`TALKER_PASSENGER`)

The opening is **S 100** from group `Pass`, and every click ends the dialog. The group does not exist in
the shipped resource file, so this is treated as a dead feature (Q-1 in the machinery doc).

---

## 21. Global trust/flag summary (state the dialogs read and write)

| Flag (player) | Set by | Read by |
|---|---|---|
| `ArabTrust` (0..5+) | MG at Arab Air (≥ 1). Sabotage commits raise it | Saboteur's entry and list lengths |
| `SpiderTrust` | Spider at the saboteur, or `Sabo/3000` | `Sabo/800` → dart |
| `MechTrust` (0, 1, 2) | Beer (≥ 1), `Mech/4014` (1 → 2) | Mode list, the monologue, Rick's tip 2106, the oil can |
| `MechAngry` (0, 1, 2) | 2005/5000/2502 → 1. 5001..5003 → 2. Beer → 0 | Mechanic's entry |
| `SeligTrust` | Postcard, or `Jobs/300` | `Jobs/20` → pills |
| `WerbungTrust` | Dart, or `Werb/8001` | `Werb/800` → diskette |
| `DutyTrust` | Bra, or `Duty/801` | `Duty/800` → horseshoe |
| `TrinkerTrust` | Horseshoe at Rick's | Drinker ends the strike |
| `KioskTrust` | Energy drink at the kiosk (= 1) | `Kios/1000` → stink bomb (resets to 0) |
| `TalkedToNasa` | First exit attempt from NASA | Farewell 9999 |
| `Kooperation[]`, `Sympathie[]` | Competitor dialog | AI calls, Rick's tip 2114 |
| `Sim.Overtake`, taker, taken | `Bank/2530..2531` | Boss 5000 chain |
| `Sim.ExpandAirport` | `Boss/4120` | `Boss/4011` |

---

## 22. Open questions (trees)

1. **T-1:** `Bank/100` (balance) is not reachable from any list. Was it intended to be in 101..103?
   Check the resource ordering in game.
2. **T-2:** the mechanic's 2502 re-entry sets `MechAngry = 1` after any menu. Confirm that the player can
   then be thrown out on the next smoking remark (5001) without having been warned.
3. **T-3:** boss image-remark tiers −400 and −990 are unreachable (ordering bug). Keep the bug for
   fidelity, or fix it? Recommendation: keep it, behind a "classic" flag.
4. **T-4:** `Arab` par1 2 has no caller. Is this a dead entry?
5. **T-5:** the `Rick` tip uses a 1-in-5 roll per step, so the final "nothing new" line is very common.
   Confirm the frequency in game.
6. **T-6:** the exact mission metrics and the `HasWon` targets (`TARGET_*` constants) belong to the
   missions spec. Cross-link them when it exists.
7. **T-7:** the designer node 6000 reads the design by file name while 6011..6015 read it by index into
   the filtered list. If the two disagree, the wrong design could be bought (**L**).
