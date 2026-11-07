# Original bugs and their switches

Policy: OpenAT is faithful to the original game, but does **not** repeat its bugs. Every genuine bug
of the original (logic slip, overflow, wrong comparison, wrong table, never-incremented counter, code
path made unreachable by a slip) is fixed **by default**. Each fix is a `Fix*` switch that defaults
to **on**; turning it off restores the original behaviour exactly. Deliberate design choices of the
original are kept as they are (no switch). Additions that are not in the original default to **off**.

Switches live in `Sim/GameOptions.cs` (`World.Options`) and, for the computer players, in
`AiOptions` (`World.AI.Options`, `Sim/AI/RobotState.cs`). Both are saved with the game. Tests check
the fixed behaviour with the defaults and the original with the switch off
(`tests/OpenAT.Tests/OriginalOptions.cs` turns every `Fix*` off).

## Game rules (`GameOptions`, on by default)

| Flag | Original behaviour (switch off) | Spec |
|-|-|-|
| `FixLoanInterest` | Loan interest is charged on cash (−Money·SollZins/100/365), not on the loan; with negative cash it is income | economy.md §5.3 |
| `FixOverdraftInterest` | Negative cash is charged at the deposit rate | economy.md §5.3, §19.2 |
| `FixCreditLimit` | Credit limit uses `max(cr, 200 000 − cr)` (large limit with negative cash) | economy.md §5.1, Q-1 |
| `FixLoanRepayCash` | Loan repayment ignores cash (can push into overdraft) | economy.md §5.1, §19.4 |
| `FixShareSaleFee` | Share sales add the flat 100 fee instead of subtracting it | economy.md §6.5, §19.5 |
| `FixDividendStatistics` | Dividend statistics record the yearly amount | economy.md §6.7, §19.6 |
| `FixDilutionFreeFloat` | Share-issue dilution compensation is also paid for free-float shares | economy.md §6.6, §19.7 |
| `FixShareIssueRounding` | A share issue books the unrounded share count instead of the offered (rounded) one | finance-shares-valuation.md §5.2 |
| `FixProfitMissionProgress` | Profit-mission progress statistic divides passengers by the profit target | economy.md §16.3, §19.9 |
| `FixFinanceAdvisorPerk` | Finance-advisor rate perk only for the Normal/Hard/Final base rates | economy.md §5.1, Q-2 |
| `FixCrewMapping` | A spare attendant is counted on every plane with a free luxury slot; the luxury-to-base second pass never runs | economy.md §9.4 |
| `FixFirstClassDemand` | First-class demand uses the economy ticket price | planes-and-flights.md, contracts-and-routes.md §3.7 |
| `FixComputerBonusAbs` | Computers get \|Mc − Mh\| hidden bonus (larger in early missions) | sim-core.md §3.3 |
| `FixHiddenBonusDeduction` | The hidden bonus pool is reduced by a deduction recomputed from the already increased flight result, not by the amount paid | sim-core.md §3.3 |
| `FixFreightTonnageCount` | Freight planning counts a flight toward the open tonnage when its start hour ≥ now regardless of the day | planes-and-flights.md §10.6, §19.2 |
| `FixOutOfGatesCounter` | `OutOfGates` is never incremented, so the AI never bids for gates for that reason | contracts-and-routes.md §6.2 |
| `FixPasteCabinEquipment` | "Paste" in the equipment room applies only the four technical items | planes-and-flights.md §6.6 |
| `FixRepairCostOverflow` | The nightly repair bill multiplies improvement × list price in 32 bits; for very expensive planes it wraps negative and the repair pays the player | planes-and-flights.md §7.3 |
| `FixFlightHourWrap` | An automatic ferry inserted at "now + 2" at 22:00/23:00 keeps start hour 24/25; plan extension wraps hours only above 24 | planes-and-flights.md §10.2, §10.4 |
| `FixMissionBranchTries` | Route mission: a branch city found on tries 6–10 of the second attempt is discarded (the success test checks the 5-try bound) | contracts-and-routes.md §6.1 |
| `FixRouteAvgFlown` | A route's "average days between flights" is set to 30 at licensing and never updated, so the "rarely flown" share factor is the same for every holder. Fixed: nightly moving average (4:1) of the days since the last flight | contracts-and-routes.md §11 |
| `FixStatisticsDayShift` | The overnight statistics shift never clears today's value: additive series (passengers, flights, income, expenses …) are running totals since the game start and their month sums add up totals. Fixed: they start each day at 0; rules the original built on the total (robot tutorial order limit, the boss's "no flights" remark, the ATFS 5-day passengers) read the running total or the 5-day sum | economy.md §16.1 |
| `FixHallWestboundLane` | Westbound customers keeping the outer lane are sent to the *eastbound* outer lane's y (209 instead of 121) | airport-and-people.md (lanes) |
| `FixHallWaitPatience` | The crossing (180) and check-in-exit (198) patience boosts test the walking direction after it was overwritten with "stand", so they never apply | airport-and-people.md (waiting) |
| `FixHallMonitorSpot` | Monitor watchers check spot (d+n) mod 6 but walk to spot (d+n) and 3: the last two monitor spots are never used and a busy spot can be doubled | airport-and-people.md (shop choice) |
| `FixSabotageDelayStatistic` | The flat-tyre prank's delay is counted in the *saboteur's* delay statistic | items-security-special.md §1.3, Q-8 |
| `FixSabotageRetrigger` | A plane prank whose plane has no next flight counts accident/sabotage statistics every hour and stays pending | items-security-special.md §1.3, Q-2 |
| `FixImpoundComputerPlanes` | The impound prank (15-hour hold) never delays computer airlines (the delay check skips them) | items-security-special.md §1.3, sim-core.md §2.4 |
| `FixRouteTheftTransfer` | Route theft only removes the route (and its reverse) from the victim; the saboteur gets nothing (byte rank compared with −1) | items-security-special.md §1.3, Q-1 |
| `FixSabotageSpecialAffordability` | Special pranks 5 (impound, 5 M) and 6 (route theft, 8 M) are charged without the debt-limit check | rooms-and-ui-3b §9 (1085/1086) |
| `FixSpecialPrankTrust` | Special pranks 5 and 6 give no saboteur trust (the step uses the wrong dialog-id base and yields a negative value), so route theft (trust 6) is unreachable. Fixed: trust k + 1, so the impound unlocks route theft; a plane prank no longer lowers trust 6 back to 5 | items-security-special.md §1.2 |
| `FixSpecialPrankNeeded` | A human's special 5/6 order does not set the victim's "security needed" bit (robot orders do), so robots never protect against them | items-security-special.md §2.4 |
| `FixRobotRocketPrices` | Robots pay the rocket part table by bit number: 20 M / 25 M / 50 M for bits 6 / 7 / 8 (humans 25 M / 50 M / 20 M) | items-security-special.md §4.1, Q-7 |
| `FixLaptopFullInventory` | A first laptop is charged even when all six inventory slots are full; the item is then not handed over | items-security-special.md §3.1 |

## Computer players (`AiOptions`, `World.AI.Options`, on by default; ai-robots.md §10)

| Flag | Original behaviour (switch off) | Spec |
|-|-|-|
| `FixOfficeTimer` | Office rule compares "last visit − now" unsigned: due on every call except the visit's own tick | Q1 |
| `FixLateBoardTimer` | Late-day board clause always true after 16:00 in AUFSICHT missions | Q2 |
| `FixRouteLossTest` | "Price below cost" test always true: shared legs are dropped after 11 nights | Q3 |
| `FixMechanicVisitTime` | The mechanic visit leaves before the work countdown is set: the robot walks out at once | Q11 |
| `FixMuseumMultiBuy` | Big-plane museum visit can buy up to 3 planes, ignoring caps | Q13 |
| `FixCargoFreePoint` | Cargo pass 2 accepts several jobs for one plane without moving its free point / city on | Q15 |
| `FixRouteSurveyOrders` | The route survey's order branch repeats the route test (dead code): order entries never set a plane's last city | Q16 |
| `FixRescueHourWrap` | "Rescue endangered legs" applies the midnight fix-up to the entry with the plane's index, so the new flight can keep an hour ≥ 24 | Q17 |
| `FixDemandComparison` | Fill loop divides both demands by the candidate's plane count: the busiest leg always wins | Q18 |
| `FixCompetitorCount` | Competitors counted with the robot's own "recently flown" value | Q19 |
| `FixSavingPrecedence` | "Not saving for the rocket" guard only on the 3-rivals case | Q22 |
| `FixRouteWipe` | The nightly route wipe deletes future *contract* entries, not route entries; route flights persist through the weekly rollover. Fixed: route entries are rebuilt nightly, and a top leg no plane can reach is skipped instead of ending the fill | Q24 |
| `FixBranchBidTable` | Branch bids test the leader of the *route* slip of the same row (never posted, so −1): "led by the disliked rival" holds whenever the robot dislikes nobody | ai-robots.md §5.2 (board) |
| `FixRouteBoxRentTable` | Route box "rent per passenger" reads the robot's own licence record (0 for unowned legs): the first candidate in table order wins | ai-robots.md §5.2 (route box) |

Q20 (wall-clock seeding) is not a switch: OpenAT always uses a deterministic seed (date, time, slot,
game seed).

## Additions (not in the original, default off)

| Flag | Effect | Spec |
|-|-|-|
| `FairRouteCompetition` | Route market-share weights use each licensee's own ticket price instead of the planning flight's price | contracts-and-routes.md §11.2 |
| `RouteTheftReachable` | Special prank 6 (route theft) is offered already from saboteur trust 5. Not needed for reachability while `FixSpecialPrankTrust` is on | items-security-special.md §1.2 |
| `ComputerPhonePose` | A computer airline on the phone with the human stops in the hall and shows the phone pose (the original does so only for the local player's figure) | airport-and-people.md §6.2 |
| `McfFont.BaseLetterFallback` | Accented letters missing from the small fonts draw as their base letter instead of a blank cell | rooms-and-ui-1 / data-tables §0.2 |

## Kept as in the original (design, not bugs)

These look odd but are consistent, deliberate rules of the original; they stay without a switch.

* **Sabotage exposure fine repeated** while evidence stays ≥ 100 (items-security-special.md §1.4).
  No code path consumes the evidence, the same counter drives the three warning levels and the
  naming of the culprit, and the 3-per-night decay is the designed way back. A culprit who is known
  stays known: every further act is fined at 10 000 × current evidence.
* **A single plane prank 5 does not expose** by itself: it adds 100 during the day and the night's
  decay of 3 comes first (timing of the briefing, not a slip).
* **Security mapping**: fake brochures are blocked by the gate scanner, route theft by the
  administration protection, the route-board protection only draws a padlock, gate levels 2/3 block
  nothing extra (they change the scanner's passenger handling). The mapping is the same in the human
  dialog, the robot orders and foreign sabotage, so it is the designed table.
* **Protection is checked only when a prank is ordered** (no re-check when it fires).
* **Cheap ad tiers give no image** (Q14): the image formula on the price row yields +0 for tiers 0–2,
  for humans and robots alike; a balance choice, tier 0 is the joke ad.
* **Robot privileges** (Q4–Q8, Q10): robots do not pay for some services, are not delayed by
  human-only checks, get +25 % route weight, take orders without validation, edit credit and shares
  directly. These are the designed AI handicap/advantages.
* **Unbid branch slips stay on the auction board** and count as posted (only gate slips are cleared
  nightly).
* **In-flight incident rolls every 4th game minute.** Verified to match: the original advances a
  constant 30 time units per sim step (a speed setting only changes how many steps run per frame), so
  every game minute is visited at every speed, exactly as in OpenAT (planes-and-flights.md §7.6).
* **Route give-up and cooldown** verified to match: a voluntary drop sets the low-use counter to 99
  (re-rentable at once, `Routes/RouteOps.cs` `Drop`), only a revocation or theft sets 0 (7-day block)
  (rooms-and-ui-6 §8.4).

## Original bugs still reproduced (owned elsewhere or not yet switchable)

* Contracts (owned by the routes/contracts work): Last Minute penalties are computed before the ×5/4
  surcharge; flying a contract on the wrong day uses it up without pay or penalty; the start contracts use the feasibility maxima of the airlines set up so far
  (32-bit overflow of the "no plane" minimum length); contract maximums cached per game hour only.
* Robot saboteur's "no laptop → coffee" fallback tests the laptop model (planner work).
* Item chains (horseshoe hand-out etc.) are handled by the items work.
* The special-prank network money message uses the wrong price table: not applicable (no network).
