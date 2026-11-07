# Cross-domain integration notes

Each domain lists what it needs from the others under its own heading. The integrator wires them.

## Finance needs from Operations

The finance domain (Sim/Finance, Sim/Stats, Sim/Missions, Sim/Staff) sees planes, rents and routes
only through one rule interface, `OpenAT.Sim.Finance.IOperationsBridge`. The default
`NullOperationsBridge` reports an airline with no operations. Register the real one in
`InitOperations()` (it runs after `InitFinance()`):

```csharp
Rules.Set<OpenAT.Sim.Finance.IOperationsBridge>(new MyOperationsBridge());
```

| Member | Used for | Spec |
|-|-|-|
| `Planes(w, p)` → `PlaneSnapshot` list, **album index order** | company value (`Value` = `CalculatePrice`, already ÷10 for sponsored planes), crew mapping (`Id`, `RequiredPilots`, `RequiredAttendants`, `MaxAttendants`), AI virtual staff, security per-plane fees, maintenance fees, strike start (`StandingAtCity`, `NextLandingHour/Date`), mission ratings (condition, equipment levels, designed planes, seats, fuel use, speed), comparison headlines (`Name`, `Condition`) | economy.md §7, §9.4, §9.9, §12.4, §14; sim-core.md §3.4, §6.1 |
| `Rents(w, p)` | daily rents: one `RentItem` per rented gate (2050), branch (2051) and route (2052) with its monthly rent | economy.md §13 |
| `BranchCount`, `RouteCount` | statistics series 20/21, comparison headline | economy.md §16.3 |
| `RentedRouteIds`, `GetRouteImage`, `SetRouteImage` | ad campaigns (route image 0..100) | economy.md §10 |
| `AbsorbOperations(w, acquirer, target)` | takeover "absorb": planes (plans cleared, at home), gates, routes/branches (rank rules), passengers of the target removed | economy.md §6.8 |
| `LiquidateOperations(w, target)` → plane sale proceeds | takeover "liquidate": sell planes at full value (return the sum, do not book it), free gates/routes/branches | economy.md §6.8 |

### Calls Operations should make into Finance

| Call | When | Spec |
|-|-|-|
| `p.MapWorkers(advice)` | after buying/selling/losing a plane (crew re-mapping) | economy.md §9.4 |
| `p.Crew(planeId)` / `p.CrewQuality(planeId)` | departure check (pilots/attendants below the requirement → delay), ±1 image rule, boss comments | economy.md §9.4, §8.3 |
| `p.CrewSalaryPerDay(planeId)` | plane P&L (salary share) | economy.md §9.5 |
| `p.StrikeRunning` | hourly departure check (delay during a strike) | sim-core.md §2.4, §6.1 |
| `Player.BarrelsForFlight(dist, use, speed)`, `p.FlightFuelCost(barrels)` (cash, pump part only), `p.FlightFuelEstimate(barrels, includeTank)` (planning, floor 1000), `p.DrawTankFuel(barrels, planeName)` at booking (books the imputed tank cost to balance sheet + stat 35 and returns it for the plane P&L), `p.BadFuelWearFactor()` | flight booking / planning / wear | economy.md §12.3 |
| `p.BookMaintenance(extraRepairCost, planeCount)` | once per night after the repair loop (per-airline step 21, order 2100). If not called, the finance fallback (order 2150) books the fees alone | economy.md §12.4 |
| `p.MechMode`, `p.MechanicCostPerPlanePerDay()` | repair behaviour per level and the plane P&L share | economy.md §12.4 |
| `p.ApplyBreakdownEconomics()` | in-flight breakdown with passengers (image −2, price ×0.7, trust) | economy.md §6.4, §8.3 |
| `p.ApplySeasonIncidentCost()` | seasonal incident (−75 000) | economy.md §8.4 |
| `p.Counters.*` (`Passengers`, `Orders`, `Freight`, `ReliefFreight`, `Miles`, `OrderFlights`, `ConnectFlags`, `RocketFlags`) and `p.Profit` | mission counters; `ConnectFlags` must be recomputed at 9:00 before `missions.morning-review` (order 300) | sim-core.md §3.4–3.5 |
| `p.Statistics.Add(StatId.X, v)` | additive series owned by flights/contracts (passengers, flights, orders, penalties, satisfied, delays, income routes/orders, kerosene, freight) | economy.md §16.3 |
| `p.ImageGotWorse = true` | set by breakdown (done by `ApplyBreakdownEconomics`) | economy.md §17.1 |
| `p.Book(..)` reasons 2010/2011/2040–2042/2060/2065/2100–2103/2110/2111 | purchases, auctions, penalties, flight results | economy.md §3.1 |

Note: `Player.FlightCounters` (operations, `OperationsCounters`) duplicates part of
`Player.Counters` (finance, `MissionCounters`). Either have operations write `p.Counters` too, or
change `MissionRuleOriginal` to read `FlightCounters` (Passengers, Miles, Contracts → Orders,
Freight, FreightFree → ReliefFreight, OrderFlights). Mission rules read only `p.Counters` today.

Setup order: operations setup steps (starting planes, gates, branch, routes) must use an
`Order` below **800** (`finance.setup` sets the money after the planes are bought; `finance.starting-crew`
at 850 hires the humans' crew for the planes and maps it).

## Finance needs from Items / Sabotage / Security

| Call | When | Spec |
|-|-|-|
| `p.ApplySabotageEconomics(type)` (+ route image × `Player.SabotageRouteImagePercent[type]`/100 by the caller) | flight-time sabotage 1..5 | economy.md §6.4 |
| `w.SharePriceShock(p, factor)`, `p.ChangeTrust(delta)` | any other price event | economy.md §6.4 |
| `p.StrikePlanned = true` | "provoke a strike" sabotage | sim-core.md §6.1 |
| `p.Counters.DaysWithoutSabotage = 0` | victims of yesterday's sabotage, at 9:00 (finance increments it nightly) | sim-core.md §2.8 |
| `p.SecurityFlags` | subscriptions (finance books the nightly fees; effects are the security domain's) | economy.md §14 |
| `p.BodyguardRebate(price, inDutyFree)` | after every purchase that gets the rebate (sabotage jobs, duty free, chocolates, laptop) | economy.md §9.8 |
| `w.Newspapers.AddOverride(paper, textId, picture, offset, priority, args)` | sabotage headlines etc. (paper 0..2, text group `Misc`) | economy.md §16.5 |
| `PlayerOut` / `TakeoverExecuted` events | cancel pending sabotage of the target | economy.md §6.8 |

## Finance needs from AI

- The AI's own bank/share actions (economy.md §18) bypass the books: change `Money`/`Credit` directly or
  use `ChangeMoneySilently`; share purchases change `SharesOwned` and the target's `SharePrice`.
- AI route ads (+28 route image for 56 000) are the AI's (use `IOperationsBridge.SetRouteImage` and book
  `MoneyReason.AdCampaignBase + 3`). AI image bonus (add-on 6) and the hidden-bonus top-up are done by
  finance (`finance.ai-image-bonus`, `finance.ai-bonus`); the bonus *payout* at flight booking is the
  flights domain's (`p.Bonus`).
- Mission rating for the AI: `w.Rules.Get<IMissionRule>().Rating(w, p)`.

## Finance needs from the integrator (core files)

- `GameData` is not partial: the staff and headline tables live in `FinanceData` (weak side table).
  Call `OpenAT.Sim.Data.FinanceData.Load(data, install)` right after `GameData.Load` (or make GameData
  partial and call it there).
- Highscores: the evening `UpdateStatistics` computes the company value; no highscore table exists yet
  (`Statistics.Today(StatId.CompanyValue)` of humans is the value to submit).

## Operations status (answer to "Finance needs from Operations")

Done in `Assets/OpenAT/Sim/Fleet/FinanceIntegration.cs` (the only operations file that touches
finance APIs), registered from `World.InitOperations()`:

- `FleetOperationsBridge : IOperationsBridge` (planes in album order, rents, branches, routes, route
  image, absorb/liquidate).
- Operations rules implemented over finance: `IFuelAccount` (KeroseneKind, TankOpen/Contents/Price,
  BadKerosene, `DrawTankFuel`), `IMechanicLevel` (`MechMode`), `IStrikeStatus` (`StrikeRunning`),
  `ICrewMapping` (`MapWorkers` + `Crew(planeId)`), `IPurchaseRebate` (`BodyguardRebate`),
  `IMaintenanceBooking` (`BookMaintenance(extra, planes)` at per-airline step 21),
  `IIncidentEconomics` (`ApplyBreakdownEconomics`, `ApplySeasonIncidentCost`), security flags.
- Statistics and mission counters from operations events: `FlightBooked` → passengers, home
  passengers, flights, income routes/orders, kerosene+food, `Counters.Passengers/Orders/Freight/
  ReliefFreight/Miles/OrderFlights`; `PassengersEvaluated` → satisfied/unsatisfied; `FlightDelayed`
  → delays; `ContractAccepted` → orders / last-minute orders / freight orders; `ContractExpired`
  → penalties; refits and plane trades → income/expense other. `Counters.ConnectFlags` is
  recomputed at 9:00 (morning order 200). `Player.FlightCounters` remains as the operations-internal
  copy; missions should read `p.Counters`.
- Rents are booked only by finance (`p.BookRents` via the bridge); the operations system for rents
  is not registered (`OperationsSystems.BookRents` is a stand-alone equivalent).
- Operations setup steps use orders 100–500 (< 800). Starting planes are added without booking.

## Operations needs from Finance / Items / AI / Airport

- Items/sabotage: `IFlightSabotageState` (press statement → passengers 1 + n mod 3; brochure target
  redirects image; default none) — replace with the sabotage state. Flight-time sabotage effects on
  planes (impound = `Plane.PseudoProblem = 15`, flat tyre +1 h delay) are to be applied by the
  sabotage domain on `Plane`/`FlightEntry`.
- Airport/layout: `IAirportGateCount.GateCount(w)` = number of gate runes (brick 2035) in the current
  hall layout (default 4 = waiting zone level 1; levels 2–5 have 5–8).
- AI: uses the same commands (`PlaceFlightCommand`, `RentRouteCommand`, `AcceptContractCommand`
  with `IgnoreOpeningHours`, `VisitOfferBoardCommand` with `Minimum = 3`, `BuyPlaneCommand`,
  `BidCommand`). AI-only original behaviour not implemented here: `DelayFlightsIfNecessary`
  (delay flights without gate by ≤ 6 h), the AI's flat 170 000 per upgrade step, `RobotPlanRoutes`
  pricing, the 5 order flights for computers in add-on 9.
- View: set `Player.InPlanePropRoom` and `Player.PlannerDragging`; animate home-airport states from
  `Plane.Location` + `HomeAirportTiming`; letters arrive as `LetterSent`, advisor lines as
  `AdvisorMessage`; designer values from the install via `PlaneDesignCatalog.Load(install)` into
  `World.DesignCatalog`.
- Missions: day-18 share price ×0.2 and day-55 image −200 of ATFS 10 (plane events of days 35/40 are
  done in `Maintenance.NightlyUpkeep`).

## Hall ↔ operations (status)

Done in `Assets/OpenAT/Sim/Hall/HallFlightLink.cs` and `HallPlanes.cs`, wired from `HallModule`
(`Register` adds the systems and the rule factory; `HallSetup.Create` sets `HallState.Flights` from
`HallFlights.Factory`, replaceable):

- `HallFlightLink : IHallFlightLink` over the operations model: flight lookup of a passenger
  (airline, plane id, plan index → gate, start hour), security flags via `IFlightSabotageState`,
  departing passengers every 32 ticks (airport-and-people.md §6.6: next start from home with a gate
  next hour, 2 tries at 1/6 or always from :20, 15 passengers per figure, 45 after 100, first class
  first; draw order birthplace → luggage → clan), gate owners from the rented gate records, the 2 × 30
  flight displays (§9.3, earliest hour wins; refreshed at every hour change by
  `HallFlightDisplaySystem`, order 400, and on first use). `IHallFlightTooltips` gives the full-name
  variant for the tooltip.
- `HallGateCount : IAirportGateCount` = RUNE_2WAIT runes of the current layout (`HallLayout.GateCount`),
  4 without a hall. It replaces only the operations default `FixedGateCount`.
- Plane animation (`HallState.Planes`, `HallPlaneSystem`, order 340 = per-tick step 3.4): a step every
  tick while a plane is animated, else every 11th. Sequences start when operations changes
  `Plane.Location` (landing at :15 → roll +6 px/step from ((3L+5R)/16 + stagger, 22 + stagger/2);
  gate departure at :30 → from RightEnd + 200 + stagger, taxi at clamp(|dx|/30, 3, 10) to
  RUNE_2WAIT(gate).x + 200; take-off +7 px with the climb). Gameplay milestones stay in operations
  (`HomeAirportTiming`); a visual sequence that outlives its phase finishes on screen (taxi out,
  climb). Reaching the gate calls boarding (waiting/seated passengers of that gate → scanner) or
  unloads arriving passengers at RUNE_WAITPLANE (one per 15, moods from condition, cabin quality,
  crew, price; seed gate + start + hour + date; spot time rounded to 80 ticks).
  `HallLandingAnnouncement` is published at y = 39 of the roll; the view's `HallAnnouncer` voices it (`der<p>flug`, city wave, `gelande2`).
- View: `AirportScreen.Planes.cs` draws runway icons (`999S?`, half-speed parallax) and the
  close-up planes behind the windows (`FLUG*` nose/cabins/tail/engines with airline overlays) between
  the horizon and the bricks. Designer planes use the universal composite (no designer art yet).

Debug flags (`Assets/OpenAT/View/Boot/DebugFlights.cs`, command line only):
`-openat-debug-autoplan [all]` (new game: rent the shortest home route and plan every starting
plane out and back from 11:00, two hours apart; `all` = every airline), `-openat-debug-advance <min>`
(run that many game minutes when the hall is first shown), `-openat-debug-speed <0..3>`,
`-openat-debug-camera <x>` (hall camera left edge, e.g. 950 check-in, 4600 gates). Example:
`tools/build/run-shot.sh 1280 720 "wait 60; shot a.png; quit" -openat-screen hall -openat-debug-autoplan all -openat-debug-advance 95`.

## Items / sabotage / security status (answer to "Finance needs from Items" and "Operations needs from Items")

Done in `Assets/OpenAT/Sim/Items/**`, `Player.Items.cs`, `World.Items.cs` (`InitItems` →
`ItemsModule.Register`). Uses the finance APIs only (`ApplySabotageEconomics`, `ShockSharePrice` via it,
`ChangeTrust` via it, `Book` with 2080/2200/2201/3300/3400/3502/9999, `BodyguardRebate`,
`StrikePlanned`, `Counters.DaysWithoutSabotage`, `Counters.RocketFlags`, `SecurityFlags`,
`Newspapers.AddOverride`) and operations state (`Plane.PseudoProblem`, flight plan entries,
`RentRoute.Image`, `RouteOps.Revoke`, `FlightPlanner.Check`, `ContractUsage.UpdateContracts`,
`IRoutePassengerRule`). No finance/operations file was edited.

- `IFlightSabotageState` is replaced by `ItemsFlightSabotageState` (press release → `Player.PressStatement`,
  brochures → `Player.BrochureFrom`, security flags → `Player.SecurityFlags`).
- `IRivalActions` (Sim/AI) is implemented by `ItemsRivalActions` and registered in `InitItems` (after
  `InitAI`). `IRivalSympathy` (Sim/Items) is the sympathy hook used by pranks and chocolates; the AI
  registers `PlayerSympathyLedger` over `Player.Sympathy`, the items default (`LedgerSympathy`) is used
  only when nobody else registered one.
- Systems (order = step × 100): midnight 200 world items restored, 300 security outage −1, 600 slime
  reset (Date % 6 == 0), 1000 laptop sale lock cleared, 1200 office power-cut flag, 2900 foreign
  sabotage (ATFS 6 = 46); per airline 310 office/stench/caffeine/mechanic anger reset + glowing coal →
  coal, 600 laptop virus 2 → 3 (computers cleaned), 700 sabotage day effects cleared + hints −3,
  1000 laptop battery 40/80/200/1440; minute 900 office condition blocks 20..23 on the hour, 1000
  plane pranks on the hour, 1100 laptop battery drain (while `LaptopOpen` and the day runs); hour 250
  impound for computers (`FixImpoundComputerPlanes` only); tick 400 drunk/stench/caffeine decay
  (caffeine mirrored to `HallPlayer.Koffein`), coffee-prank toilet runs, office bomb at the door,
  overnight-prank execution when the clock starts after the briefing; morning 250 sabotage report
  and fines (before `missions.morning-review` at 300).
- Leaving the briefing: `SabotageEngine.ExecuteMorningPranks` runs on `StartWorkingDayCommand`
  (via `CommandExecuted`) or on the first running tick of the day (idempotent per date).

### View / rooms needs from Items

- Status bar: `Player.Items` is a public field of type `Inventory : IList<int>` (read-only, 0xFF = empty,
  packed and sorted). Use `UseItemCommand(player, slot, ItemUseContext)` and read `ItemUsed`
  (`Outcome` Gift → dialog opening `10000 + item`; Applied with `DialogPar` → start that dialog with the
  room's talker: Arab 1, Mech 2, Jobs 300, Sabo 3000, Werb 8001, Duty 801, Trinker 800, Fracht 1020,
  Kiosk 1010; Refused → `MENU_REQUEST` sub `RequestId` 30/31; OpenScreen → `Screen` "laptop",
  "phonebook", "office-phone", "filofax"; `Sound`). Discard with `DropItemCommand` (the status bar's
  `DiscardItem` sees `IsReadOnly` and should call it).
- Rooms: `PickUpItemCommand` (glove, paper clip, glue, perfume, pliers, postcard, glowing coal, bra,
  spider, oil), `ReceiveRewardCommand` (horseshoe, dart, diskette, pills, stink bomb),
  `UseVendingMachineCommand`, `BuyDutyFreeCommand` (3, 1, 2, 4, 14, 15), `BuyLaptopCommand`
  (refusals "duty-3"/"duty-1"/"virus"), `LaptopRepairCommand`, `OfferChocolatesCommand`,
  `SetLaptopOpenCommand`, `SetWorkshopSlimedCommand` (end of the workshop's slime clip),
  `CutOfficePowerCommand` (pliers at a rival's office door, "wrong room" path), `ExplodeOfficeCommand`
  (only needed without a hall: the tick system explodes when `HallPlayer.Room` is the own office),
  `DrinkerEndsStrikeCommand`, `NasaFarewellCommand`, `ToggleSecurityCommand(row 0..8)`,
  `BuySpacePartCommand(bit)` (rocket menu ids → `SpaceParts.RocketBitOfMenu`), `UseTelescopeCommand`.
- Saboteur dialog: `SabotageEngine.OfferedOptions(w, p, family)` for the list length,
  `SelectPlanePrankCommand` (1051..1055: needed bit set before the plane menu),
  `OrderPrankCommand(player, victim, family, mode, target)`; failure reason "sabo-<id>" is the Sabo line.
  The saboteur's call / letters / faxes come from `PrankExecuted` (+ `LetterSent` "Lett" 500 + mode, 509).
- Briefing: `w.Items.Report` (lines with intro 2050..2053/2055, comment 2070..2073, fine) is built at
  9:00; `Report.HasExposure` = cannot skip. `OfficeBombArmed` → the camera follows the victim.
- Closed security office: `w.Items.SecurityOutageDays > 0` (MENU_CLOSED_NOSECURITY): the hall's
  `ClosedMenu` returns kind 8 for the security office while it lasts.
- View side (done): `View/Rooms/Items/ItemUseView` (status-bar use/discard for rooms and the hall),
  `SabotageNotifications` (fax / advisor for prank letters Lett 501..508, the saboteur's call after a
  prank of the local player, the toilet pause MENU_WC_F/M), `BossSabotageReport` (Boss 2004/2050..2096
  in the briefing via `DialogActions`). Rooms: Sabotage (112), Security (123), NASA (115), Rick's (84),
  telescopes Insel/Rushmore/WeltAll (116/118/122, chosen by mission), World (117, branch calls,
  MENU_AUSLANDSAUFTRAG, MENU_BRANCHLIST, MENU_PLAYERLIST).
- Office state 2/3 (`Player.OfficeState`) for the door art and the dark overlay; `PhonesDown` blocks
  being called; `LaptopVirus`, `LaptopBattery`, `Drunk` (cursor wobble), `Sick`, `Stench`.

### Items API for AI (beyond `IRivalActions`)

`SabotageEngine.Blocked(victim, family, mode)`, `SabotageEngine.RobotOrder(...)` (what
`PlaceSabotage` calls; counts `Sabotage.Annoyances`), `ItemSystems.RobotVisitSecurity(w, p, rand)`
(two passes over bits 0..8 against `Player.SecurityNeeded`), `ItemSystems.RobotBuySpacePart(w, p)`
(lowest missing bit at the robot price), `SpaceParts.RobotPrice`. The "security needed" mask is
`Player.SecurityNeeded` (set by every successful order, human or robot).

## AI needs from Items

The computer players (Sim/AI, ai-robots.md) only *decide* rival interference; the shared mechanics are
the items domain's, reached through one rule interface, `OpenAT.Sim.AI.IRivalActions`
(`Sim/AI/IRivalActions.cs`). The default `NoRivalActions` means "no items domain": nobody is suspicious,
jobs are never placed, no laptop/phone, the security office is open. `InitItems()` (runs after
`InitAI()`) replaces it with `ItemsRivalActions`.

| Member | Used for | Spec |
|-|-|-|
| `SuspicionLevel`, `HasPendingSabotage`, `SaboteurTrust` / `SetSaboteurTrust` | sabotage preconditions and go/no-go; the robot raises its trust by one per "go" | ai-robots.md §5.2 "Sabotage" |
| `PlaceSabotage(attacker, target, family, severity, planeId)` | writes the job into the attacker's pending field of that family, free of charge; the AI has already applied the slot caps and cancelled jobs the target's `SecurityFlags` block | §5.2, §9.2 |
| `SecurityOfficeOpen`, `BoltCutterAvailable`, `TakeBoltCutter`, `CutSecurityPower` | walk refusal while the power is cut; "everything blocked" → bolt cutter → action Security-2 | §3.3, §5.2 |
| `LaptopQuality`, `LaptopWorking`, `LaptopSoldToday`, `GiveLaptop`, `HasMobilePhone`, `GiveMobilePhone` | planner filters (office skipped with laptop quality > 2, 60 % office skip with a working laptop), free duty-free items, phone calls | §4.2, §4.3, §5.2 "Duty free" |
| `OfficeDestroyed` | office rejected / room-busy fallback to personnel | §4.1, §3.3 |
| `CanPhone(human)` | the robot's phone call decision (event `RobotPhoneCall`; the dialog is the rooms domain's) | §5.1 |

Also used directly: `ItemSystems.RobotVisitSecurity` (security office action), `ItemSystems.RobotBuySpacePart`
(NASA action, robot price-by-bit quirk), `Player.SecurityNeeded` (mask set by `RobotOrder`). The
sympathy hook `Items.IRivalSympathy` is registered by the AI as `PlayerSympathyLedger` over
`Player.Sympathy` (no clamp); pranks and chocolates change robots' feelings through it
(victim towards culprit), which is what makes a robot "dislike" a rival.

Events published by the AI: `RobotPhoneCall` (robot, human), `RobotSabotageDecided` (placed / blocked).

## AI status

Done in `Assets/OpenAT/Sim/AI/**`, `Player.AI.cs`, `World.AI.cs` (`InitAI`):

- Rules: `IRobotBrain` (whole AI, default `OriginalRobotBrain`: new game, morning init, pump, arrival,
  room busy, nightly route planning), `IRobotFeatures` (mission feature matrix as data,
  `RobotFeatureTable`, sim-core.md §3.7), `IRivalActions` (above). Tunables in `w.AI.Tuning`
  (`RobotTuning`: weighted pick table, work countdowns, walk fallback); AI bug fixes in `w.AI.Options`
  (`AiOptions`, all off = original; listed in docs/QUIRKS.md).
- Systems: `ai.pump` (tick, order 330, 09:00 < t ≤ 18:00, not gated by call-it-a-day; every 10 min the
  hall's room locks are released when nobody is inside a room), per airline `ai.called-player-decay`
  (900), `ai.robot-init` (1800), `ai.plan-routes` (2200), world `ai.sympathy-drift` (2800),
  `ai.flight-count` (50, reports); setup step `ai` (950, after the hall).
- Walking: with a hall the robots use `WalkToRoomCommand`, enter on `RoomRequested`, leave with
  `ReturnToHallCommand`; `HallRequest` ThrownOut/ThrownOutBoard/Forbidden = room busy. Without a hall
  (or after call-it-a-day) a countdown replaces the walk (`RobotTuning.TeleportWalkTicks`, or the
  Manhattan distance to the door). Rooms closed by opening hours refuse the walk.
- Commands used like a human: `AcceptContractCommand` (`IgnoreOpeningHours`), `AcceptFreightCommand`,
  `VisitOfferBoardCommand` (min 3 after a visit), `BuyUsedPlaneCommand`, `EnterMuseumCommand`,
  `BuyDesignedPlaneCommand`, `RentRouteCommand`, `BidCommand`, `AdCampaignCommand`, `SetDividendCommand`,
  `BuyKeroseneCommand`, `SetMechModeCommand`. Robot-only paths (`RobotShortcuts`): plan writes without the
  range check (`AppendFlight`), the gate delayer, branch offers through a cooperating airline's office,
  credit and share trades by direct edit (`ChangeMoneySilently`), the robots' share issue, the route ad
  (+28), luxury upgrades (170 000 per step), repair targets in steps of 2/3/15, broker upgrades.
- Headless runs: `HeadlessRunner` (Sim/AI) and `openat sim --days N --mission M [--seed S] [--hall]
  [--trace] [--statement P]`.

### AI needs from others (open)

- Operations: nothing blocking. `Player.RouteUsage` is counted for humans only, the AI reads it for the
  "copy the neighbour" route-mode switch.
- Missions/finance: `Counters.ConnectFlags` of the local player drives the route-mode switch in mission
  4 (difficulty 3).
- Rooms/view: done. `GameFlow` never opens rooms for computer players on `RoomRequested`; `RobotPhoneCall`
  to the local player opens the rival-call dialog (`View/Session/SessionServices`, `View/Rooms/Rivals`) when
  the player is in a room without dialog/menu, otherwise the call is dropped (ai-robots.md §5.1). Talk effects go
  through `Sim/Rivals/RivalTalkCommand` (sympathy ±1000, cooperation of both airlines). The robot's "bored of
  player" flag is `Player.BoredOfPlayer` (`RivalBoredCommand` on Play 2499, reset per airline at night by
  `rivals.new-day`; saved): later phone-book calls to it get `noanswer.raw`. Phone-book calls to computer airlines
  (`PlayerListMenu.CallCompetitor`, rooms-and-ui-3 §6.3) and face-to-face talks in the hall (`WalkToPlayerCommand`,
  `EndTalkCommand`, `HallRequest.TalkToPlayer` → `RivalCallScreen` in person) run the same dialog.
- Mission designer planes (DESIGNER_BUY, ATFS 5/8): set `w.AI.MissionDesign` from `misc/pl<difficulty>.pln`.
- Tutorial: done. `Sim/Tutorial` (`World.Tutorial`: running, step, monologue; saved). The pump and figure walking
  wait for step 1100 (`TutorialEngine.FiguresMayMove`); the view's script is `View/Tutorial/TutorialView.cs`.

## Tutorial and open-issue sweep (status)

- Tutorial (mission 0): `Sim/Tutorial/Tutorial.cs` (state, steps the sim sees: first passenger 1600 → 1602,
  check-in 1603, waiting zone 1604, plane at the gate 1605, plane leaves 2000; gates for robots, walking, room
  windows and hall clicks; `TutorialStepCommand`, `CancelTutorialCommand`; midnight end). View:
  `View/Tutorial/TutorialView.cs` (script steps that wait for the assistant / a room, office and planner click
  gates, glow markers `TUTMARK1..5`, status-bar panel `TUT01..03`, Space/button cancel). Spec:
  `docs/spec/findings/tutorial.md`.
- Airport expansion: `Sim/Hall/AirportExpansion.cs` (boss 4011 decision order, 4120 pays 1 000 000 / 3170,
  midnight step 16 raises check-in, waiting zone and belt one level and rebuilds the layout; `World.ExpandAirportPending`,
  `World.LastAirportExpansion` saved).
- Toilet pause: held and timed by the hall (`HallPlayer.OnToilet`, `HallToiletFlush`), the figure walks out through
  the normal leaving-a-room walk; `WcMenu` only blocks input.
- Worst-ad letters: `MailModule` turns `WorstAdLetter` into `Lett` 9900/9910/9920 letters signed with +2
  (`Letter.SenderTextId`/`SenderArgs`).
- Strike end: `StrikeEndNotifiedCommand` + `View/Session/StrikeEndCall.cs` (clerk calls, `Jobs` 1000 + end type).
- Departures monitor: `View/Airport/FluegeMenu.cs` (MENU_FLUEGE). Landing voice: `View/Airport/HallAnnouncer.cs`.
- Half-hourly notices: `Sim/Hall/HallAnnouncements.cs` (when and which sample) voiced by `HallAnnouncer`; the announcer
  keeps its queue across room changes and plays at announcements × global ambient volume².
- Global ambient volume: `GameAudio.SetGlobalAmbientVolume` + the per-room table `View/Audio/RoomAmbience.cs` (applied by
  `RoomScreen.OnEnter`, the hall, the in-game options); the hall's ambient loops are fed by `View/Airport/HallAmbience.cs`,
  the random fly-bys by `GameAudio` (airport-and-people.md §10.1).
- Phone pose: the local figure while a phone dialog runs over the hall; computer figures only with the addition
  `GameOptions.ComputerPhonePose` (`PhoneCallCommand` from `RivalCallScreen`, held still by `HallTick`).
- Room hotkeys: `View/Rooms/Framework/RoomHotkeys.cs` (hall and rooms; off in the tutorial together with Esc/Tab/F3/F4).
- Idle main-menu planes: `View/Screens/KlackerPlanes.cs` (main menu, new-game pages, options).
- Open: network chat of the competitor dialog; Dutch/Portuguese hotkey tables are not selected (no install language
  for them yet).
