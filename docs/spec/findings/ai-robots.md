# Computer-controlled rival airlines ("robots") — behaviour spec

Clean-room behaviour description of the AI airlines. Written in our own words from
reading the reference source; no code is reproduced. Citations are `File.cpp: Function`
against the GOG source (`docs/spec/local/gog_src`); every quirk listed in §10 was also
checked against the SDL port (`docs/spec/local/ref_AirlineTycoon/src`) and is identical there.

Confidence markers: **[H]** high (read directly, unambiguous), **[M]** medium
(read, but how it combines with other systems is inferred), **[L]** low (guess / not traced).

Related specs (mechanics referenced here but specified elsewhere): sabotage / rival
interference, bank & shares, advertising, airport board auctions, security office,
mechanic & plane condition, orders / last-minute / travel agency / cargo generation,
route demand & passenger distribution.

Terminology used below:

* **slot** = player index 0..3. Several decision rules are hard-wired per slot (see §7).
  The character/airline in each slot is whatever the new-game setup assigns.
* **hour unit** = the simulation clock runs 60 000 time units per game hour
  (a day is 24 × 60 000). "3 h" below means 180 000 units. [H] (`Sim.cpp: DoTimeStep`)
* **tick** = one call of the per-player robot pump (one simulation step while the robot
  pump is active). The network code treats 20 ticks as ~2 s of real time, i.e. ~20 ticks
  per real second at normal speed. [M]
* **feature(X)** = the per-mission robot feature lookup (§8).

---

## 1. Overview

A robot is a normal player record whose owner type is "computer". It has the same money,
planes, routes, offices, shares, items etc. as a human, and its figure physically walks
through the airport. Its brain is a small **action queue** of 5 slots plus a planner:

1. Each morning the queue is pre-filled with a fixed opening sequence (§2).
2. While the queue has work and the robot is idle on the concourse, it pops the next
   action and **walks** to the room that action belongs to (§3).
3. When the figure enters the room, the action is **executed instantly** (all logic runs
   in one go, no UI), then a **work countdown** keeps the robot in the room for a while (§5).
4. When the queue runs dry, the **planner** (§4) appends one (occasionally two) new actions,
   chosen by a fixed priority chain and, failing that, by a weighted random pick.
5. Once per night, robots that have switched to "route mode" re-plan all route flights
   and ticket prices (§6).

Robots do not use the planner UI, laptop UI, phone menus or room dialogs. Everything they
"buy" in a room is applied directly to their data, and several costs that humans pay in
those dialogs are simply never charged (§9).

---

## 2. Initialisation

### 2.1 New game (`Sim.cpp: SIM::ChooseStartup`) [H]

Applies per player; robot-relevant differences:

* **Starting money**: every player starts with the *human* amount of the mission. The
  difference between the robot amount and the human amount (absolute value; ATFS missions:
  signed, but both columns are equal there) is put into the robot's hidden **bonus pool**
  (§9.1). Humans' pool is 0.

  | Mission | Human money | Robot table value | Robot bonus pool at start |
  |---|---|---|---|
  | Free game | 1 500 000 | 2 000 000 | 500 000 |
  | Tutorial | 5 000 000 | 200 000 | 4 800 000 (abs of the negative difference) |
  | First mission | 5 000 000 | 500 000 | 4 500 000 |
  | Easy | 3 000 000 | 1 000 000 | 2 000 000 |
  | Normal | 2 000 000 | 3 000 000 | 1 000 000 |
  | Hard | 1 800 000 | 4 000 000 | 2 200 000 |
  | Final | 1 500 000 | 6 000 000 | 4 500 000 |
  | Add-on 01..10 | 3 000 000 | 1 000 000 | 2 000 000 |
  | ATFS 01..10 | 3 000 000 | 3 000 000 | 0 |

  Starting credit is 0 everywhere except Add-on 01, where both humans and robots start with
  10 000 000 credit. The "absolute value" step means that in missions where the designers
  gave robots *less* money than humans (tutorial, first, easy, add-on), robots in fact get
  an equally large hidden pool — see quirk Q12.
* **Staff**: humans are given pilots and attendants for their starting planes; robots get
  **no staff at all** and never hire any. [H]
* **Cooperation flags**: a robot starts "cooperating" with every other robot and not with
  any human. Humans start cooperating with nobody. [H]
* **Sympathy**: every player starts with sympathy 50 towards everyone, +10 towards the
  next slot ((slot+1) mod 4). [H]
* **Game speed**: robots are set to speed 3 (they never block the "everyone ready" logic). [M]
* ATFS 09 / 10 only: robots get a 5 000-unit kerosene tank, already opened. [H]
* All robot planning state (timestamps, route-mode flags, saving flags, queue) is cleared.

### 2.2 Every morning (`Player.cpp: PLAYER::NewDay` → `PLAYER::RobotInit`) [H]

`NewDay` runs for every player; robot-specific parts:

* If feature(IMAGEBONUS): image += (day mod 3), clamped to ±1000.
* Bonus pool refill (rubber band, §9.1) when the pool is empty.
* A laptop virus on a robot is cured overnight (humans: only the "repairing" state advances).
* The per-player random generator used for planning is reseeded with (day + slot), so the
  random choices are deterministic per day and slot.
* Rent, interest, security costs, maintenance, dividends are booked like for humans;
  **salaries and order penalties are not** (§9).
* If the robot is in route mode, `RobotPlanRoutes` runs (§6).

`RobotInit` then:

* sets an initial idle countdown of 10 + 10 × slot ticks (staggers the four robots);
* clears the queue and the "last visit" timestamps (office, personnel, board, agents);
* queue slot 1 = go to own office (the "start day" action), slot 2 = go to own personnel office;
* if **not** in route mode:
  * if feature(TRAVELHOLDING) (i.e. travel agencies exist):
    * on days where (slot + day) is odd: slots 3/4 = last-minute desk, travel agency;
      slot 2 is replaced by the cargo office if feature(MUCH_FRACHT), or if
      (slot + day) mod 4 == 0 and feature(FRACHT);
    * otherwise: slots 3/4 = travel agency, last-minute desk; slot **1** becomes the cargo
      office if feature(RUN_FRACHT) (robot runs to cargo first thing), else the same
      "slot 2 → cargo" rule as above;
  * else if feature(FRACHT): slot 3 = cargo office.
* calls the planner once (it does nothing because the queue is not empty).

Note slot 0 of the queue is the "current" action; the morning list starts at slot 1.

---

## 3. Timing loop and walking

### 3.1 When robots think (`Takeoff.cpp` main loop, `Player.cpp: PLAYERS::RobotPump`) [H]

* The robot pump runs only between 09:00 and 18:00 game time, not while time is frozen,
  and not during the tutorial before its scripted step 1100.
* Every tick, for each player that is not bankrupt and not in a face-to-face talk,
  `PLAYER::RobotPump` runs. It returns immediately for non-robots after the toilet/stun
  handling below.
* Side job of the global pump: every 10 game minutes, if no player is inside a room / in a
  dialog / entering a room, all room "busy" locks and talker locks are force-released
  (anti-deadlock hack).

### 3.2 Per-robot pump (`Player.cpp: PLAYER::RobotPump`) [H]

In order, each tick:

1. **Sick (stomach) state** (applies to any player): between 09:00 and 18:00, on ticks
   divisible by 127, 1 in 4 chance to run to the toilet (stun 20 ticks, running). On arrival
   the robot is stunned for 70 ticks (`Sim.cpp: ComputerOnToilet`, plays a sound if the
   human sees it).
2. **Stunned**: count down; while stunned do nothing else. When the stun ends and the
   robot is not running to the toilet, the current action is dropped (re-plan if empty).
3. Only robots, only on the network host (clients just mirror).
4. **Stuck detection**: if the robot is on the concourse, not walking and not talking
   (or, after the human ended the day, its fast-forward counter is 0), a "standing still"
   counter increases (only while the work countdown is 0 and it is not in a door). After
   more than 10 such ticks the current action is cleared and the planner is called.
5. If the current action is set and the robot is on the concourse → still walking, return.
6. If the work countdown > 0 → decrement, return (the robot is "busy in the room").
7. If the current slot is empty but slot 1 has an action: **shift the queue** left by one,
   leave the current room if inside one, remember the previous action id, and **walk** to the
   room of the new current action:

   | Action | Destination |
   |---|---|
   | start day / office | own office |
   | personnel | own personnel office |
   | bank, emit shares | bank |
   | petrol (Arab Air) | kerosene dealer |
   | sabotage | saboteur's room |
   | kiosk / mechanic / museum / duty free / board (Aufsicht) / NASA / broker / Rick's / route box / designer | that room |
   | security, security-2 | security office |
   | buy used plane | museum |
   | advertising | advertising agency |
   | agent 1 / 2 / 3 | last-minute desk / travel agency / cargo office |
   | telescope | island room in the Final mission, otherwise the Rushmore room |

   **Running**: slot 2 always runs during even 5-minute windows of each hour (minute/5 even,
   minute > 0), and every robot always runs if feature(ALLRUN); never when heading to the
   route box. Otherwise walks.

   If the walk is refused (room closed, see 3.3), the action is discarded, the previous
   action id is restored (so the "no immediate repeat" rule still refers to the real last
   visit) and the planner is called.
8. If both current and next slots are empty → planner.

### 3.3 Walking (`Player.cpp: PLAYER::WalkToRoom`, `Person.cpp`) [H]

* A walk request fails (returns "refused") for closed rooms: duty free before 10:00 or
  Sat/Sun after 16:00; kerosene dealer before 11:00 or on Sunday; last-minute after 16:00 or
  Saturday; museum before 11:00 or on the weekend; travel agency after 17:00; broker after
  16:00; advertising before 12:00 or weekends (only for Normal and harder, and free game);
  security office while its power is cut. (Weekday 5/6 = Sat/Sun.) The human gets a
  "closed" popup; robots silently re-plan.
* Target is a random door-tile of the room; the figure follows the shared waypoint walker.
  Robots walk at base speed 3 (speed is only raised by a fitness adviser among staff, which
  robots never have). Running performs a second step per tick (except on the escalator row);
  caffeine gives extra steps for anyone who has it.
* **Room busy on arrival** (`Person.cpp`, `Takeoff.cpp`): if the room is occupied the
  robot postpones: the queue becomes [—, next action, the blocked action, …]; if there was no
  next action it inserts "office" (or "personnel" if its office is destroyed). The blocked
  action is thus retried right after the next one. [H]
* **Arrival**: on entering the room `RobotExecuteAction` is called (§5); the figure is
  turned to face the counter in some rooms (cosmetic).
* **After the human ends the day** ("call it a day" fast-forward): robots no longer walk.
  A walk instead sets a countdown equal to the Manhattan distance (pixels) between figure
  and target door (minus the floor offset if changing floor); `Person.cpp:
  PERSONS::DoOneStep` decrements it once per step and executes the action at 0. [H]

---

## 4. The planner (`Player.cpp: PLAYER::RobotPlan`)

Runs only for robots that are not bankrupt, and only when both the current and the next
queue slot are empty. It fills queue slot 1 (sometimes slot 2).

### 4.1 Priority chain (first match wins) [H]

| # | Action | Condition |
|---|---|---|
| 1 | buy used plane | money > 5 000 000, image > −100, not saving for the rocket, time after 11:00, not Sat/Sun, not yet in the museum today |
| 2 | mechanic | feature(GOODPLANES), day mod 4 == slot, hour 9, minute ≤ 35 |
| 3 | advertising | "wants to do routes", image < 150, 1-in-4 |
| 4 | museum | "needs a bigger plane" (§6.2), money > 100 000, 1-in-4, not feature(GROSSESKONTO) |
| 5 | emit shares | day + 1 ≥ slot, at least 25 000 shares still issuable, and (money < 3 000 000 or credit > 1 000 000 or feature(EMITMUCHSHARES)) |
| 6 | personnel | more than 3 h since the last personnel visit |
| 7 | board (Aufsicht) | more than 3 h since the last board visit, **or** the late-day clause (see Q2): in practice *always* after 16:00 when feature(AUFSICHT) |
| 8 | last-minute + travel agency (two actions) | more than 1.5 h since the last agent visit and not in route mode |
| 9 | office | the office-timer clause (see Q1): in practice *always*, unless the office is destroyed |
| 10 | random pick | otherwise (4.2) |

Rows 7 and 9 are the comparisons flagged by the earlier review. Because of how they
evaluate in the original (unsigned clock), they are almost always true rather than never
(§10, Q1/Q2). Consequence: a passenger-order robot effectively alternates **office →
other action → office → …**, and after 16:00 in feature(AUFSICHT) missions
**board → other → board → …** (the "no immediate repeat" re-roll in 4.3 supplies the
"other").

### 4.2 Random default pick [H]

Weighted table (31 entries; weight = number of entries):

| Action | Weight |
|---|---|
| sabotage | 2 |
| office | 2 |
| bank | 2 |
| personnel | 2 |
| mechanic | 2 |
| cargo office (agent 3) | 2 |
| advertising | 2 |
| security office | 3 |
| designer | 2 |
| petrol / Arab Air | 1 |
| broker | 1 |
| kiosk | 1 |
| museum | 1 |
| last-minute (agent 1) | 1 |
| travel agency (agent 2) | 1 |
| duty free | 1 |
| board (Aufsicht) | 1 |
| telescope | 1 |
| Rick's | 1 |
| route box | 1 |
| NASA | 1 |

Draw repeatedly until a candidate survives these filters (in this order; "reject" = draw again):

1. NASA without feature(NASA) → reject.
2. "Chit-chat" rooms (telescope, Rick's, kiosk): replaced by mechanic if feature(OFTENMECH);
   replaced by sabotage if feature(EXTREME_SABOTAGE); rejected if feature(NOCHITCHAT).
3. After day 1: slot 0 overrides the pick with **sabotage** 1-in-4; slot 1 overrides with
   **bank** 1-in-3.
4. Museum rejected when the plane cap of feature(MAX4/5/10PLANES) is reached.
5. Broker rejected without feature(MAKLER); petrol rejected without feature(PETROLAIR).
6. Loop until different from the last executed action.
7. Office rejected if laptop quality > 2.
8. Agents 1/2 rejected in route mode once the robot has flown routes, except on 1 in 16
   time values; rejected without feature(TRAVELHOLDING).
9. Cargo rejected without feature(FRACHT); advertising without feature(WERBUNG); NASA/telescope
   rejected if that room does not exist on the current airport map.

### 4.3 Post-filters (apply to the chain result too) [H]

* If the result equals the last action: re-roll with a slightly smaller filter set
  (filters 1, 2, 8-agent-holding, 9 only — note it does **not** apply the slot overrides,
  plane caps, broker/petrol, or laptop filters) until different.
* Designer without feature(DESIGNER), security without feature(SECURTY_OFFICE) → back to 4.2.
* feature(MUCHWERBUNG): bank becomes advertising in even hours.
* Petrol not on day 0, nor on day 1 except for slot 2. Sabotage not before day (1 + slot).
* Office rejected if the office is destroyed; office after 10:00 with a working laptop:
  60 % chance to skip it, marking the office timer as "just visited", → back to 4.2.
* Still equal to last action → back to 4.2.
* Without feature(SABOTAGE): petrol and sabotage rejected. Without feature(ROUTEBOX): route box rejected.
* **Follow-ups** (only after day 1, if slot 2 is free, 50 %): slot 0 (with feature(SABOTAGE))
  queues sabotage after kiosk / duty free / bank; slot 1 queues bank after kiosk / duty free /
  sabotage.
* feature(MUCH_FRACHT): an agent-1/2 pick is pushed to slot 2 and cargo goes first.
* feature(FREE_FRACHT): an agent-1 or museum pick is pushed to slot 2 and cargo goes first.

---

## 5. Executing actions (`Player.cpp: PLAYER::RobotExecuteAction`)

### 5.1 Common prologue [H]

* Network: on the host the action is not run immediately but broadcast with a target
  tick ~40 ticks (about 2 s) ahead, so all machines run it at the same simulated time; nothing
  is started after 17:58. Clients replay. After "call it a day" the host broadcasts at once.
  Single player seeds the action's local random generator from the wall clock, so these rolls
  are **not** reproducible across runs in single player.
* Duplicate guard: an action with the same id at the exact same time as the previous
  execution is dropped.
* **Disliked rival**: among all non-bankrupt players towards whom this robot's sympathy is
  negative (humans or robots), one is picked uniformly at random (or none).
* **Phone call to a human**: pick a random slot 0..3. If it is a human, not out, callable
  (09:00 to 17:59, has a mobile phone, is in a room window, not stuck, phone not sabotaged, no
  dialog/menu open, not passing a door), and either (they cooperate and sympathy < -20) or
  (they don't cooperate, sympathy > 50 and the robot hasn't called recently), and the robot is
  doing its office action or owns a mobile phone: if the target is the local human, ring their
  phone, pause their game and open the rival-call dialog (dialog content: other spec). The
  "recently called" counter is set to 2 and decays by 1 every odd day.
* Action-specific logic (5.2), then:
  * slot 2 halves its work countdown (if > 2);
  * feature(WORKVERYQUICK) divides it by 4 (if > 4), else feature(WORKQUICK) halves it (if > 2);
  * all flight plans are re-validated; the current action is cleared.

Work countdowns are in ticks; 100 is roughly 5 real seconds at normal speed [M].

### 5.2 Action catalogue [H unless marked]

**Office / start day** (own office; countdown 100; office timer := now)

1. *Route-mode switch* (only if not yet in route mode). Triggers if feature(SUGGESTROUTES), or
   (day > slot + 30 and more than 6 planes), or (day > slot + 15 and the previous slot's
   player, (slot+3) mod 4, has a route counter of exactly 1 or above 20). For a human that
   counter goes up once per route flight booked (`Schedule.cpp: BookFlight`), so a robot copies
   a neighbouring human who starts flying routes. When triggered:
   * image < 150: set "wants to do routes" (buys routes and ads but still flies orders);
     additionally switch to full route mode if the number of cities the local human has
     connected (mission bitmask popcount) exceeds the slot number;
   * image >= 150: switch to route mode only if it already holds more than 2 route legs.
   feature(FORCEROUTES) always switches to route mode.
2. *Saving goals* (if image > -100 and not in route mode, or image > 100, or in route mode):
   count rivals that own at least as many planes as the robot. Set "saving for a plane" if
   not saving for the rocket and that count >= 3; for slot 2 if >= 2; for slot 0 if >= 1 (the
   slot 2 / slot 0 cases ignore the rocket check, operator precedence). Also set it if the
   disliked rival has at least 2 more planes (and not saving for rocket). If the disliked rival
   has at least as many rocket parts, the robot has more than 3 planes and feature(NASA): stop
   saving for a plane, start saving for the rocket.
3. *Abroad orders by phone* (not in route mode, feature(ABROAD), and not before 10:00 under
   feature(MUCH_FRACHT)): for every city where the robot, or a cooperating player (in practice
   another robot), has a branch office, except the home airport and except a city the local
   human is phoning right now: refill that city's foreign order and cargo lists, run the
   order-taking routine (5.3) for passenger orders and then the cargo routine (5.4; skipped in
   route mode and for a 4-plane fleet under feature(SHORTFLIGHTS)), then top the lists up
   again. No call charges (§9).
4. *Strike*: if the robot's staff is on strike, 1-in-6 chance the strike simply ends.
5. *Luxury upgrades* (feature(LUXERY), has planes, money > 200 000): probability 1/5;
   1/3 if money > 2.5 M; 1/2 if > 15 M; certain if > 25 M. Number of upgrades = 1, plus
   money / 10 M if money > 10 M. Each upgrade: up to 3 attempts on a random plane, up to 4
   random rolls among the 7 cabin/technical components; the first rolled component not
   already at level 2 is raised one level and 170 000 is charged.

**Personnel office** (countdown 100): only records the visit time. Robots have no staff.

**Broker** (countdown 100): if the robot has planes and travel agencies do **not** exist
(only the add-on mission without them), 1-in-8 chance to raise one random component target of
one random plane by one level (max 2), free of charge (§9).

**Petrol / kerosene dealer** (countdown 100): only with feature(TANKS) (ATFS 09/10). If the
kerosene price < 500 and money > 1 M: buy (money - reserve) / price units, reserve 2.5 M
(price 400..499), 2 M (350..399), 1.5 M (< 350), capped by free tank space; paid. Robots never
change their kerosene quality setting (always normal).

**Sabotage** (saboteur room; countdown 200 if a job was placed, else 100). This decides
*whether* and *against whom*; the job itself is the shared sabotage mechanic (other spec),
written into the same three pending-job fields a human order fills (plane job, office/personal
job, special job).

* Target: the disliked rival from 5.1. If none, then with feature(EXTREME_SABOTAGE), or
  1-in-3, or always for slot 0: a random slot; under EXTREME, if that is itself or bankrupt,
  the player with the best mission rating instead. Self or bankrupt means no target.
* Preconditions: suspicion level ("hints") < 100, image > -500, no job of any kind pending.
* Go/no-go: ((day + slot) mod 3 != 0, or EXTREME, or 1-in-4, or slot 0 with 1-in-2) **and** a
  valid target **and** (suspicion < 80, or slot 1 with suspicion < 90, credit < 1 M and
  money > 3 M on every 4th day) **and** (target is a robot, or feature(MUCH_SABOTAGE)).
  So without MUCH_SABOTAGE (tutorial, first, easy) robots only sabotage each other.
* On go: "trust" with the saboteur +1 (max 4). Up to 2 passes (5 with EXTREME); in each pass
  the **hour of day mod 3** selects the job family:
  * 0: plane job against a random plane of the target (target must own planes);
    severity = trust (max 4);
  * 1: office/personal job; severity = trust (max 4); a severity 1 has a 50 % chance to become
    2 (this roll uses the unseeded C runtime random, a network-desync risk); severity 2 drops to
    1 if the target has no laptop;
  * 2: special job (target must own planes), severity = trust (max 5); severity 5 also picks a
    target plane.
  * From the 3rd pass on, severity is re-rolled uniformly below its value (can become 0).
  * Slot 0: 2/3 chance to cap the plane job at 2, and 2/3 chance each to force the office and
    special jobs to severity 1. Slot 2: 2/3 chance to cap plane and office jobs at 3.
  * Security-office check: if the target owns the matching protection, the job is cancelled
    and an "annoyance" counter goes up. Matching (job severity: protection bit): plane job
    1,2: bit 6; 3,4: bit 7. Office job 1,3: bit 0; 2: bit 1; 4: bit 2. Special job 1,5: bit 8;
    2,3: bit 5; 4: bit 3.
  * Each job that was attempted sets the same bit in the target's "security needed" mask (a
    robot victim later buys exactly those protections, see Security below).
  * Stop at the first pass that placed a job.
* If everything was blocked (annoyance >= 2), the bolt-cutter item is still available, the day
  is even, and the airport has a security office: take the bolt cutter and queue "security-2"
  next.
* The robot pays nothing for the job (§9).

**Bank** (countdown 100). All money moves are direct edits of money / credit / share counts,
bypassing the bank dialog:

1. Dividend: slot 1 and feature(HIGHSHAREPRICE) fix it at 25. Others: 1-in-10 chance per visit
   to raise it (+1 with 1/5, another +1 with 1/10), clamped to 5..25. It never goes down except
   through the clamp.
2. feature(PAYBACK_CREDIT) (add-on 01), money > 750 000, after day 1, not MAXKREDIT: repay
   min(credit, money - 250 000).
3. Credit, first matching rule:
   * (slot 1 or feature(MAXKREDIT)) and credit < 1 000 000 + 50 000 x day and not PAYBACK:
     borrow up to exactly that target;
   * money > 1.5 M, credit > 0, not slot 1, not MAXKREDIT: repay min(credit, money - 1.5 M);
   * money < 1 M, not slot 1, not PAYBACK: borrow (1.4 M - money).
   No credit-limit check (§9, Q8).
4. feature(MAX20PERCENT), own stake > 20 % and own price >= 200: sell the excess down to 20 %;
   proceeds capped at 20 000 in total (Q10).
5. Fire sale: if credit > 3 M (or slot 2 with feature(SELLSHARES) and credit > 1 M), and (not
   slot 1, or money < -100 000): go through holdings in every company (own shares only after day
   10, and up to day 20 only while the robot's remaining own stake stays above half of all
   issued shares); sell up
   to 20 000 per company **at the robot's own share price** (Q10); stop once money > 2 M. The
   other company's price is not touched.
6. Buy the disliked rival's shares: not saving for plane/rocket, not DONTBUYANYSHARES, and
   (money > 3 M with credit < 1 M, or money > 2 M with own price < dividend/2). Buy the free
   float (shares held by no player), limited to (money - 3 M) / their price (money - 2 M in the
   "cheap" case, where cheapness is judged on the robot's *own* price), rounded down to 100.
   Their price rises to price x total / (total - bought/2). The human gets an adviser note if
   they are the target.
7. Buy back own shares: only if not PAYBACK, not DONTBUYANYSHARES and not MAX20PERCENT.
   MAX20PERCENT is on in every mission except ATFS 06, so this branch is practically dead.
   Conditions and amounts analogous to 6 (money > 3 M with feature(BUYOWNSHARES), or > 6 M;
   credit < 1 M unless slot 1; or the cheap case).

**Emit shares** (bank; countdown 120): new shares = issuable remainder rounded down to 100; 80 %
go to market at (price - 5); a 10 % issue fee on the full issue is charged; the price is
re-averaged over old and new market shares; existing outside holders are compensated for the
price drop (robot pays the total, other players receive their part); the robot keeps the other
20 % and, unless slot 3 without feature(REBUYSHARES), immediately buys half of the new issue
at the issue price (direct money edit). MAX20PERCENT excess selling then applies, at full price
this time. Human adviser note.

**Mechanic** (no countdown is set, see Q11):

* feature(REPAIRPLANES) (add-on 07): per plane, target condition := min(worst seen + 19, 100),
  then raise using a notional budget equal to current money: +15 per 3 M while budget > 3 M and
  target < 92, otherwise +3 per 300 000, while target < 100 and budget > 300 000. The budget is
  not spent here (repairs are billed nightly). Mechanic quality set to the best level.
* feature(GOODPLANES): every plane's target +3 (max 100); mechanic quality set to best.
* otherwise: every plane's target +2, max 100 (slot 3: max **70**); if the mechanic quality is
  the default level it is upgraded to the best one.

**NASA** (countdown 100): Final mission: buy the lowest-numbered missing rocket part if its
price < money; add-on 10: same with space-station parts. At most one part per visit, paid.
Clears "saving for rocket" after a purchase.

**Telescope / Rick's / kiosk**: just wait (countdown 100).

**Museum, big plane** (countdown 200 if eligible, else 100): only if "needs bigger plane" > 0
and not GROSSESKONTO. Scans the 3 used-plane offers up to 3 times; buys an offer built after
1960, condition > 40, price < money + 1 M and range >= the needed range. Buying resets the
needed range to 0, so later scans in the same visit accept any range (Q13). Plane set-up on
purchase: worst condition = condition - 20; attendant maximum = type attendants x fleet
average; all cabin/technical targets level 1, except slot 0 puts seats/trays/decor at 2 and
slot 1 puts engines/tyres/electronics/safety at 2; food target 0. Price charged normally.

**Buy used plane** (museum; countdown 140; marks "museum visited today"): only below the plane
cap (GROSSESKONTO: fewer than 3 planes; MAX4/5/10 respectively). Buys at most one offer with
year > 1950, condition > 65, price < money - 1 M. Same set-up as above; clears "saving for
plane". Robots never buy new planes from the manufacturer (an action id exists but is unused).

**Duty free** (countdown 100): if nobody has bought the laptop today, own laptop quality < 4,
day > 7 and money > 100 000: laptop quality +1 and the laptop item if not owned, **free** (and
this blocks the human from buying a laptop that day). If no mobile phone, day > slot and money
> 100 000: mobile phone, **free**.

**Last-minute desk / travel agency / cargo office** (countdown 140; agent timestamp := now):
skipped entirely in route mode; cargo also skipped for a 4-plane fleet under
feature(SHORTFLIGHTS). Refill the offers, run 5.3 / 5.4 over the list, refill again.

**Board / Aufsicht** (countdown 140; board timestamp := now):

* Gate bidding only when the "out of gates" counter exceeds the fleet size. That counter is
  reset daily and never increased anywhere, so robots **never bid on gates** (Q9).
* Branch offices: if not saving for plane/rocket, or day < 5, or 1-in-25, or slot 0 with 1-in-3:
  for each of the 7 city notices not already led by this robot, where it has no office yet:
  bid if ((another player leads and (sympathy to them < 40, or slot 0, or 1-in-10, or after day
  10 with 1-in-5)) or (nobody leads and 1-in-3)) and yesterday's balance > 10 x price and
  2 x credit < 3 x money and money > 0 and (the *route* notice in the same row is led by the
  disliked rival, or slot 0, or feature(ABROAD)). A bid raises the price by 10 % and makes the
  robot the leader; payment happens at the nightly auction exactly as for humans. Human adviser
  note when the human is outbid.

**Route box** (countdown 140): does nothing while any owned route leg has had more than one
"low utilisation" day. Otherwise, if wanting/doing routes, fleet size > (owned legs/2) x 3/2,
and not every route is owned:

* Candidates: unowned legs touching the home airport, plus unowned legs sharing a city with any
  leg on which the robot's own utilisation is >= 20 %; minus unowned legs with fewer than 7
  "low days" since the robot last lost or gave them up (cool-down).
* Choice: with feature(ROUTEMISSION) the first candidate (in list order) connecting home to one
  of the 6 mission cities, with a 50 % chance to stop at each such hit and otherwise keep
  scanning; without it, the candidate with the lowest rent per passenger.
* Rent it (rank = one more than the best existing rank of any player) and give the reverse leg
  the same rank. Only the daily rent is paid, as for humans.

**Security office** (countdown 140): two rolls; each picks a random protection bit 0..8. If
money > 1 M and the bit is in the "security needed" mask (bits set by sabotage attempts against
this robot), that protection is switched on; if money <= 1 M the rolled protection is switched
**off**. Daily security fees are billed by `NewDay` as for humans.

**Security-2 / bolt cutter** (countdown 20): drop the bolt cutter, cut the security office power
for 3 days and clear every player's protections (shared mechanic).

**Designer** (countdown 140): with feature(DESIGNER_BUY), load the mission's predefined custom
plane design file; while money - design cost > 6 M, buy one copy (paid).

**Advertising** (countdown 140): nothing if feature(GROSSESKONTO). Otherwise:

1. Route ad (wanting/doing routes, money > 500 000): first owned leg whose route image is < 70,
   or < 80 when not saving, or < 100 when not saving and money > 2 M: route image +28 (route-ad
   tier 3 price / 2000), clamp 0..100, pay that tier's price (56 000). One leg per visit.
2. Airline campaign if ((image < 0, or wanting/doing routes and image < 300) and money > 1.5 M
   and not saving) or (money > 150 000 and feature(MUCHWERBUNG) and image + 10 < image of slot
   (slot+1) mod 3 or of the disliked rival) or (image < 1000 and money - credit > 4 M and not
   saving). Tier n:
   * route mode or feature(HARDWERBUNG): n = 0 on every 4th day; else by money: <= 1.8 M: 1,
     <= 2.5 M: 2, <= 3 M: 3, <= 4 M: 4, else 5;
   * otherwise: n = 0 if money <= 1.8 M or on every 3rd day; else by image: > -20: 1, > -40: 2,
     > -60: 3, else 4; with more than 5 planes and not a 3rd day, n + 1 (max 4).
   Image += (price_n / 10 000, integer) x (n + 6) / 55 (integer). With the campaign price row
   (1 000; 10 000; 50 000; 100 000; 250 000; 2 500 000) that is +0, +0, +0, +1, +4, +50 (Q14).
   Tier 0 additionally sends every human a letter (letter text group 9900..9902) about the
   rival's cheap campaign. The price is paid.

**Raise money**: placeholder, countdown 140, nothing else (never queued).

### 5.3 Order-taking routine (abroad phone orders, last-minute, travel agency) [H]

For every plane, three passes (0, 1, 2; passes 0 and 1 are identical, so the "same city" rule
can take two orders per plane):

* **Free point** of the plane: destination and landing time + 1 h of its last scheduled entry
  (any type), in hours from today 00:00; if the plan is empty, its start city at now + 2 h.
  Never earlier than now + 2 h. On day 0 at 09:00 the default is additionally offset by slot/2
  hours.
* Only if the free point is before 23:00 tomorrow (hour 47; travel agency: hour 71).
* **Pass 0/1**: take the first open offer (scanning the list from the end) that departs from the
  plane's free city and whose positioning cost (same city, so the 1 000 minimum) is <= 80 % of
  the premium.
* **Pass 2**: among open offers whose departure city can be reached before hour 47, pick the
  one with the nearest departure city; accept if distance < 15 000 km (abroad 9 000), and if
  >= 6 000 km (abroad 5 000) the free point is before hour 34, and if >= 4 000 km (last-minute)
  or 3 000 km (travel agency, abroad) the free point is still today (before hour 24); and the
  positioning cost (at a reference consumption) <= the full premium.
* feature(SHORTFLIGHTS) with exactly 4 planes: ignore offers longer than 3 000 km or with <= 90
  passengers.
* feature(TUTORIALLIMIT) (tutorial): only while orders taken today < 2 x day + 3 + (1 for odd
  slots). (Applies to last-minute and travel agency, not abroad.)
* On accept: copy the order into the robot's order list (order statistic +1; add-on 09 counts
  only from the 5th order of the day; last-minute also counts a last-minute statistic), mark
  the offer taken, write a flight-plan entry at the free point, let the plane's plan validator
  insert positioning flights, and run the robot gate delayer (6.4).
* **Not checked**: whether the plane has enough seats for the order, and whether the free point
  lies inside the order's allowed date window (Q7).

### 5.4 Cargo routine (abroad cargo, cargo office) [H]

As 5.3 with these changes; free point limit is hour 71 for both passes.

* **Pass 0/1**: first open cargo job from the free city with estimated cost
  2 x trip cost x (tons / (seats / 10) + 1) <= 80 % of premium + bonus, or the job pays no
  premium and feature(FREE_FRACHT). Bonus = 150 000 for slot 2 under feature(MUCH_FRACHT),
  else 0.
* **Pass 2**: visit the open jobs in order of increasing distance of their departure city (each
  once). Requires that the departure city is reachable before hour 47, distance < 15 000 km,
  (< 6 000 km or free point before hour 58), (< 3 000 km or free point before hour 48); cost =
  trip cost x (trips) + positioning cost <= premium + bonus (or the free-cargo rule). Several
  jobs can be accepted in one pass; the free point is **not** recomputed between them (Q15).
* On accept: one flight entry per trip, each covering (seats / 10) tons, until the open tonnage
  is covered. Cargo validity checks are human-only (`Player.cpp: UpdateFrachtauftragsUsage`
  returns early for robots).

Note on how entries are written (5.3 and 5.4): the new flight is written into the **last**
entry of the plane's fixed-size flight-plan array, and the plan validator
(`CPlane::CheckFlugplaene`, other spec) is relied on to sort it into place and add positioning
flights. [M] If the last entry was already in use it is overwritten; not observed to matter in
practice, but a reimplementation should append into a free entry and treat "plan full" as
"reject".

---

## 6. Flight schedules and routes

### 6.1 Order mode vs. route mode [H]

* **Order mode** (default): the robot only flies passenger orders and cargo jobs collected by
  §5.3/5.4. There is no long-term schedule; each agent/office visit appends work at each plane's
  free point. Orders past their deadline are removed from the plan two days later like for
  humans (`Player.cpp: UpdateAuftraege`), with the robot gate delayer re-run if not in route
  mode. Since order validation is human-only, every robot order entry counts as valid.
* **"Wants to do routes"**: still order mode, but the robot buys route legs (route box), runs
  route ads, and the planner can queue advertising (priority 3).
* **Route mode** (`DoRoutes`): agent visits are skipped (or almost never chosen), and every
  night `RobotPlanRoutes` builds the next schedule. Missions: in Normal (feature SUGGESTROUTES) the
  switch test passes from the first office visit, so robots "want routes" at once and enter
  full route mode once the human has connected more mission cities than the robot's slot
  number (or, with image >= 150, once they own more than 2 legs); add-on 05 forces route mode
  (FORCEROUTES); elsewhere the switch rules in 5.2 apply. Nothing ever switches a robot back
  to order mode.

### 6.2 Nightly route planning (`Player.cpp: PLAYER::RobotPlanRoutes`) [H]

Called from `NewDay` for robots in route mode, after maintenance.

1. **Wipe future routes**: every route entry starting after today is cleared. For each plane,
   remember the destination of its last remaining route entry ("last city"; 0 = unknown/empty),
   and count planes per route leg. (A second branch meant to do the same for order entries can
   never run: it repeats the route condition, Q16.)
2. **Rescue endangered legs**: for every owned leg with 15 or more consecutive low-utilisation
   days (20 means the leg is confiscated, see `NewDay`), give up to 4 planes one flight on it
   each: written into the first free entry, starting today at now + 2 h with 50 h "booking
   lead", current ticket prices, no gate; passengers computed; plan validated (gates later).
   If the validator immediately removes it, that plane is marked unusable for the rest of the
   night. (Index slip: the day-overflow fix-up after setting the start hour is applied to the
   wrong entry, Q17.)
3. Assign gates.
4. **Range check**: "needs bigger plane" := the longest owned leg in km, reset to 0 if any
   plane's range covers it. Drives planner priority 4 / the museum action.
5. **Fill loop** (repeat while something was added):
   * Pick the owned leg with the highest demand. The intended rule is demand / (planes on leg +
     1), but the comparison divides both sides by the *candidate's* plane count and the count is
     never updated in the loop, so it is effectively "highest raw demand" (Q18). Legs marked
     "full" are skipped.
   * Pick a plane with sufficient range that is not marked unusable: a plane with unknown last
     city wins immediately; otherwise the one whose last city is nearest to the leg's origin.
     (Ties/iteration order: later planes with unknown last city overwrite earlier picks.)
   * Write one flight on that leg into the plane's first free entry (same parameters as step 2),
     compute passengers, validate. Last city := leg destination; if the validator removed it,
     mark the plane unusable. If the plane had no free entry, mark the **leg** "full".
   * If the plane now has any entry landing 5 or more days ahead, mark it unusable.
   Effect: the top-demand leg absorbs flights from all planes until their plans are filled 5
   days ahead or no entry is free; then the next leg; the validator inserts the positioning
   (empty) flights needed between legs.
6. Assign gates.
7. **Convert positioning flights**: any empty positioning flight whose city pair matches an
   owned leg becomes a route flight on that leg (current prices, passengers recomputed; not
   re-validated). This is how the return direction gets flown.
8. **Ticket prices** for every owned leg:
   * Count players (including itself) owning the leg, using the robot's *own* "days since last
     flown < 5" for everyone (Q19).
   * Base cost C = reference flight cost of the leg x 3 / 180. Economy price = 6 x C rounded down
     to 10; first class = 2 x economy.
   * If more than one owner: cap at 2 x each owner's price (rounded to 10) — this includes
     itself, so no effect from that term; undercut every owner the robot dislikes strongly
     (sympathy < -40) by 10 below their price (rounded); never below C/2 (rounded; first class
     2x); never below C if money < 100 000; never below 2C if money < 0.
   * Loss tracking (only if more than one owner): the "is this leg losing?" test is broken and
     always true (Q3), so the leg's loss-day counter grows by one every night. After more than
     10 nights the robot gives the leg up (unless feature(NEVERGIVEROUTEUP), i.e. Hard only):
     human adviser note, other owners ranked below move up, own rank := 0, low-days := 99
     (i.e. no re-buy cool-down, see route box). Only this direction of the leg is dropped (the
     reverse leg stays owned); flights already planned on it are not removed here, but the
     next nightly wipe clears its future entries and it is no longer chosen. [M on the last
     point]

### 6.3 Route passenger share (`Schedule.cpp: CFlugplanEintrag::CalcPassengers`) [H]

When the route demand of a leg is split among owners, each owner's weight is computed from
ticket price, rank, flight frequency etc. (other spec); a robot's weight is then **+25 %**
(both economy and first class weights). Every robot owner gets it, so between two robots it
cancels out; it only shifts passengers away from humans.

### 6.4 Gate delayer (`Player.cpp: PLAYER::DelayFlightsIfNecessary`) [H]

Robot-only helper called after every order/cargo insertion (and after removals in order mode):
assign gates; then for each plane, any route or order flight touching the home airport that has
no gate is pushed back one hour (re-validating and re-assigning gates each time), at most 6
pushes per plane per call. Humans get no such automatic fixing; they must plan gates themselves.

---

## 7. Per-slot ("per airline") differences [H]

The code distinguishes robots only by slot number:

| Slot | Personality as coded |
|---|---|
| 0 | **Saboteur / show-off.** Random pick overridden to sabotage 1-in-4 after day 1; queues sabotage after kiosk/duty free/bank; picks a random victim if it dislikes nobody; sabotage go/no-go has an extra 1-in-2 pass; but keeps jobs mild (2/3 chance: plane job <= 2, office/special job = 1). Saves for a plane as soon as one rival has as many planes. Bids on every branch-office notice it is eligible for regardless of sympathy, the disliked-rival route condition, or saving (1-in-3 extra). Buys used planes with luxury cabin (seats, trays, decor level 2). |
| 1 | **Banker.** Random pick overridden to bank 1-in-3 after day 1; queues bank after kiosk/duty free/sabotage. Dividend fixed at 25. Keeps credit topped up to 1 M + 50 000 x day and never repays; only fire-sells shares when money < -100 000; may buy own shares while indebted. Extra sabotage allowance when rich and lightly indebted. Buys used planes with technical upgrades (engines, tyres, electronics, safety level 2). |
| 2 | **Hustler.** Runs during even 5-minute windows; all work countdowns halved (works twice as fast). Saves for a plane when two rivals have as many planes. May go to the kerosene dealer on day 1. Gets the cargo valuation bonus (150 000) in the cargo mission and may sell shares early (feature SELLSHARES). Sabotage capped at 3 (2/3 chance). |
| 3 | **Penny-pincher.** Maintains planes only up to 70 % target condition (default mechanic rule). Does not buy back half of a share issue (except with feature REBUYSHARES). Starts latest in the morning (largest initial countdown); first share issue one day later than others. |

Common to all: sabotage not before day 1 + slot; initial idle 10 + 10 x slot ticks; the
"previous slot" whose route behaviour is copied is (slot + 3) mod 4.

Cosmetic only: in an all-robot game, slot 3's planes show more unhappy passengers in the
airport view (`Planetyp.cpp`) — not a gameplay effect.

---

## 8. Mission feature table (`Player.cpp: RobotUse`) [H]

A feature is looked up by level index: base missions 0..5 (tutorial, first, easy, normal, hard,
final), free game = 6, add-on 01..10 = 7..16, ATFS 01..10 = 17..26. Below, "X" = on, "-" = off;
"F" column is the free game (features the source marks as "added after 1.0" are on).

| Feature | Base 0-5 | F | Add-on 01-10 | ATFS 01-10 | Meaning |
|---|---|---|---|---|---|
| SABOTAGE | -XXXXX | X | XXXXXXXXXX | -XXXXXXXXX | may use saboteur / petrol rooms |
| FRACHT | ------ | X | -XXXXXXXXX | XXXXXXXXXX | takes cargo jobs |
| WERBUNG | ---XXX | X | XXXXXXXXXX | -XX-X-XXXX | uses advertising |
| AUFSICHT | ----XX | X | ---------- | ---------- | late-day board visits |
| NASA | -----X | - | ---------X | ---------- | visits NASA / rocket logic |
| NEVERGIVEROUTEUP | ----X- | - | ---------- | ---------- | never drops a losing route |
| ROUTES | ------ | - | ---------- | ---------- | (unused, always off) |
| SUGGESTROUTES | ---X-- | - | ---------- | ---------- | switch to route mode at once |
| FORCEROUTES | ------ | - | -----X---- | ---------- | forced route mode |
| HARDWERBUNG | ----XX | - | ------XXXX | ---------- | money-based ad tiers |
| SELLSHARES | XXXX-- | - | XXX------- | X--------- | slot 2 may sell shares early |
| BUYOWNSHARES | ----XX | - | -------XXX | ---------- | buy back own shares |
| REBUYSHARES | -----X | - | --------XX | ---------- | slot 3 rebuys after issue |
| TUTORIALLIMIT | X----- | - | ---------- | ---------- | daily order cap |
| ROUTEMISSION | ---X-- | - | ---------- | ---------- | prefer mission routes |
| MUCHWERBUNG | ----X- | - | ---------- | ---------- | image race ads |
| BONUS_X2 | --XXX- | - | XXX------- | ---------- | bonus refill x2 |
| BONUS_X4 | -----X | - | ---X--X--- | ---------- | bonus refill x4 |
| BONUS_X8 | ------ | X | ----XX-XXX | ---------- | bonus refill x8 |
| ROUTEBOX | -XXXXX | X | XXXXXXXXXX | XXX-X-XXXX | route box exists |
| ABROAD | --XXXX | X | XXXXXXXXXX | XXXXXXXXXX | phones abroad offices |
| MUCH_SABOTAGE | ---XXX | X | XXXXXXXXXX | XXXXXXXXXX | may sabotage humans |
| PAYBACK_CREDIT | ------ | - | X--------- | ---------- | repays credit |
| MUCH_FRACHT | ------ | - | -X-------- | ---------- | cargo first |
| FREE_FRACHT | ------ | - | --X------- | ---------- | takes free (relief) cargo |
| RUN_FRACHT | ------ | - | --X------- | ---------- | runs to cargo in the morning |
| LUXERY | ------ | - | ----X----- | -X-------- | buys cabin luxury |
| TRAVELHOLDING | XXXXXX | X | XXXXX-XXXX | XXXXXXXXXX | travel agencies exist |
| IMAGEBONUS | ------ | - | -----X---- | ---------- | daily free image |
| GOODPLANES | ------ | - | -----X---- | -X-------- | always repairs |
| REPAIRPLANES | ------ | - | ------X--- | ---------- | careful repair mission |
| HIGHSHAREPRICE | ------ | - | -------X-- | XXXXXXXXXX | dividend 25 |
| WORKQUICK | ------ | - | ---XXXXXXX | XXXXXXXXXX | half room time |
| GROSSESKONTO | ------ | - | ---------- | X--------- | hoards money |
| EMITMUCHSHARES | ------ | - | ---------- | X---X--X-- | issues shares whenever possible |
| WORKVERYQUICK | ------ | - | ---------- | -----XXXXX | quarter room time |
| ALLRUN | ------ | - | ---------- | ---------X | always runs |
| DONTBUYANYSHARES | ------ | - | ---------- | X--------- | never buys shares |
| MAXKREDIT | ------ | - | ---------- | XXX-----XX | maximum credit |
| MAX4PLANES | ------ | - | ---------- | --X-X--X-- | plane cap 4 |
| MAX5PLANES | ------ | - | ---------- | ---X-X---- | plane cap 5 |
| MAX10PLANES | ------ | - | ---------- | -X-------- | plane cap 10 |
| NOCHITCHAT | ------ | - | ---------- | --X----XXX | no kiosk/Rick/telescope |
| OFTENMECH | ------ | - | ---------- | -X--X--XXX | mechanic instead of chit-chat |
| SHORTFLIGHTS | ------ | - | ---------- | --X------- | short flights with 4 planes |
| EXTREME_SABOTAGE | ------ | - | ---------- | ---X-X---- | heavy sabotage |
| SECURTY_OFFICE | ------ | X | ---------- | ---X-X-X-X | uses security office |
| MAKLER | XXXXXX | X | XXXXXXXXXX | XXX-X-XXXX | uses broker |
| PETROLAIR | XXXXXX | X | XXXXXXXXXX | XXXXX-XX-- | uses kerosene dealer |
| MAX20PERCENT | XXXXXX | X | XXXXXXXXXX | XXXXX-XXXX | keep own stake <= 20 % |
| TANKS | ------ | - | ---------- | --------XX | hoards kerosene |
| DESIGNER | ------ | X | ---------- | ---XX-XX-- | visits designer |
| DESIGNER_BUY | ------ | - | ---------- | ----X--X-- | buys designer planes |

Other mission dependencies outside the table: starting planes (2 per player; types depend on
Easy / Normal / others), starting route + branch offices for Hard and above (except add-on 02),
special events in ATFS 10, mission 9's order counting. These apply to all players. [H]

---

## 9. Robot advantages and handicaps

### 9.1 Hidden bonus pool [H]

`Schedule.cpp: BookFlight`: after each flight's result (income - costs) is computed, up to
10 % of |result| is added from the pool and removed from it. Because the amount removed is
recomputed from the *already increased* result, on loss-making flights slightly less is removed
than was added, on profitable flights slightly more. Humans' pool is always 0.

Refill (`Player.cpp: NewDay`), only when the pool is exactly 0: +100 000 if the local human's
mission rating beats the robot's, another +100 000 if it beats it by 3:2, another +100 000 if by
2:1 (for add-on 01 lower is better, comparison inverted); then x2 / x4 / x8 by feature
BONUS_X2/4/8 (they can stack where several are on, none do). A robot that is ahead of the human
gets nothing. Rubber-band: worse robots catch up with free money.

### 9.2 Costs robots never pay [H]

| Item | Humans | Robots |
|---|---|---|
| Sabotage / rival-interference jobs | pay the job price in the saboteur dialog | free (§5.2 Sabotage) |
| Laptop, mobile phone | pay in duty free | free; laptop also upgraded one level per purchase |
| Phone calls (abroad orders) | call charges | none |
| Staff salaries | daily | none (no staff at all; salary booking is human-only) |
| Penalties for unfulfilled orders and cargo | charged on deadline | never charged |
| Cabin/technical upgrades applied at landing (`Planetyp.cpp`) | charged | free (only luxury-feature upgrades pay a flat 170 000) |

Costs they do pay like humans: plane purchases, rent (gates, offices, routes), board auctions,
advertising, NASA parts, maintenance, interest, dividends, security fees, kerosene, tank
purchases, share issue fees.

### 9.3 Rules skipped for robots [H]

* **Pre-departure delay checks** (`Sim.cpp: DoTimeStep`): an hour before departure, human
  flights are delayed by sabotage-induced pseudo-problems, strikes, missing pilots/attendants,
  plane condition < 15, an invalid order, or a plane problem. Robots' flights skip this check
  completely (they also have no crew to miss).
* **Order validation**: seat capacity and date window are not checked (§5.3); robot order
  entries are always valid, so they are always paid.
* **Credit**: borrowing/repaying edits money and credit directly; no credit limit, no debt
  limit, no history entry or bank statistic.
* **Image effects** (`Schedule.cpp: BookFlight`): humans lose/gain image from cabin staff
  quality and the route image decays by 1 per flight; robots skip both, and get no attendant
  count bonus either.
* **Strikes** may end at random on any office visit (1-in-6).
* **Gate planning** is automated (§6.4).
* **Laptop virus** cured overnight.

### 9.4 Handicaps [H]

* Image pricing check: for route flights the "fair price" a robot is judged against is 10 %
  lower (code comment: robots' kerosene discount is applied here instead of at the dealer), so
  robots are slightly more likely to be judged too expensive.
* No staff means no service points from talented staff in service-based missions; a fixed
  simulated staff count is used instead (`Player.cpp: GetMissionRating`).
* Never bid on gates (Q9), never buy new planes, never change kerosene quality.

### 9.5 Per-difficulty summary [H]

| Mission | Notable robot settings |
|---|---|
| Tutorial | no sabotage, no cargo, no ads; daily order cap; pool 4.8 M |
| First | sabotage of other robots only; route box; pool 4.5 M |
| Easy | + abroad offices; bonus refill x2; pool 2.0 M |
| Normal | wants routes from day 0 (route mode once the human connects enough cities), mission routes preferred; may sabotage humans; ads; x2; pool 1.0 M |
| Hard | + late-day board visits, money-based ads, never gives up routes, image race ads (own-share buyback flag set but dead, see bank step 7); x2; pool 2.2 M |
| Final | + NASA rocket; rebuys shares; x4; pool 4.5 M |
| Free game | sabotage incl. humans, cargo, ads, late-day board, abroad, security office and designer visits; no NASA, no route suggestion; x8; pool 0.5 M |
| Add-on 01..10 / ATFS 01..10 | see §8 |

---

## 10. Original behaviour quirks

All verified in the GOG source and identical in the SDL port. Suggestion for OpenAT: replicate
by default for fidelity, ideally behind a "fix original AI bugs" switch.

| # | Quirk | Where | Confidence |
|---|---|---|---|
| Q1 | **Office timer compares in the wrong order.** The planner's office rule subtracts "now" from "last office visit" instead of the reverse. The previous reviewer concluded it never fires. **Correction:** the simulation clock is an *unsigned* 32-bit value (robot timestamps are signed), so the subtraction is done unsigned, wraps to a huge number, and the rule is true on essentially every call (false only in the exact tick of the visit). Net effect: office is the default choice whenever priorities 1-8 do not apply, giving an office / other / office / other rhythm (the "no repeat" re-roll provides the "other"); with a working laptop after 10:00, 60 % of those office picks are turned into a random pick. | `Player.cpp: RobotPlan` (priority 9); clock type in `class.h` (SIM) | [H] types and arithmetic; [M] resulting rhythm (not observed in a running game) |
| Q2 | **Late-day board clause, same inversion.** Intended "one more board visit in the afternoon"; written as "last visit minus now > 1 h". The unsigned wrap makes it *always* true, so in feature(AUFSICHT) missions (Hard, Final, free game) every planner call after 16:00 that reaches priority 7 picks the board, alternating board / other until 18:00. Same correction to the earlier "never fires" claim. | `Player.cpp: RobotPlan` (priority 7) | [H] arithmetic, [M] rhythm |
| Q3 | **Route-loss typo.** The "price below cost" test has a comma where a second operand was intended, so it evaluates to the cost value itself (non-zero), i.e. always "losing". For any leg shared with at least one other owner (as counted in Q19) the loss counter rises every night; after 11 nights the robot drops the leg unless feature(NEVERGIVEROUTEUP), which is on only in Hard. Confirms the earlier review: outside Hard, robots abandon every shared route after about 11 days (planning runs once per night). | `Player.cpp: RobotPlanRoutes` (ticket price section) | [H] |
| Q4 | **Robots don't pay** for: sabotage / rival-interference jobs, laptop (and its upgrades) and mobile phone, abroad phone calls, staff salaries (they have no staff), penalties for missed orders and cargo, cabin/technical upgrades installed at landing (broker and other target raises; "luxury" raises pay a flat 170 000 instead). | `Player.cpp: RobotExecuteAction`, `BuyItem`, `BookSalary`, `UpdateAuftraege`; `Planetyp.cpp` (upgrade billing limited to human owners); `Dialog.cpp` / `DutyFree.cpp` for the human prices | [H] |
| Q5 | **Human-only pre-departure delay checks**: robots' flights are never delayed by strikes, missing crew, low condition (< 15), invalid orders, or sabotage-caused plane problems at the one-hour-before check. | `Sim.cpp: DoTimeStep` | [H]; [M] whether sabotage delays reach robots by another path |
| Q6 | **+25 % route passenger weight** for robot owners in the demand split (economy and first class). | `Schedule.cpp: CalcPassengers` | [H] |
| Q7 | **No order validation for robots**: seat capacity and date window are not checked when taking orders, and the usage check marks every robot order entry valid, so the premium is always earned. | `Player.cpp: RobotExecuteAction` (agent cases), `UpdateAuftragsUsage`, `UpdateFrachtauftragsUsage` | [H] |
| Q8 | **Credit edited directly**: borrowing ignores the bank's credit limit and the debt limit; no history line. | `Player.cpp: RobotExecuteAction` (bank) vs. `CalcCreditLimit` | [H] |
| Q9 | **Gate bidding dead**: the "out of gates" counter that triggers gate bids is only ever reset, never increased. Robots live with their starting gates plus the gate delayer. | `Player.cpp: NewDay`, `RobotExecuteAction` (board) | [H] |
| Q10 | **Share sale pricing**: in the bank fire sale, shares of *any* company are sold at the robot's own share price; the 20 % rule's excess sale at the bank pays at most 20 000 in total (the same rule after a share issue pays full value); the "rival shares are cheap" test uses the robot's own price. | `Player.cpp: RobotExecuteAction` (bank, emit shares) | [H] |
| Q11 | **Mechanic visit has no room time**: every mechanic branch leaves the action before the work countdown is set, so the robot walks out at once. | `Player.cpp: RobotExecuteAction` (mechanic) | [H] code path; [M] countdown is 0 at that moment |
| Q12 | **Bonus pool uses the absolute difference**: where robots were meant to start poorer (tutorial, first, easy, add-ons), the negative difference is made positive and becomes a large hidden pool (4.8 M in the tutorial). | `Sim.cpp: ChooseStartup` | [H] |
| Q13 | **Museum multi-buy**: in the big-plane museum visit the range requirement is reset after the first purchase, so up to 3 used planes can be bought in one visit, also bypassing the plane caps. | `Player.cpp: RobotExecuteAction` (museum) | [H] |
| Q14 | **Cheap ad tiers give no image**: integer arithmetic makes campaign tiers 0-2 yield +0 image while still costing money. | `Player.cpp: RobotExecuteAction` (advertising) | [H] |
| Q15 | **Stale free point in cargo pass 2**: several cargo jobs can be accepted in one pass for the same plane without moving its free point / city on. | `Player.cpp: RobotExecuteAction` (cargo) | [H] code; [M] consequences depend on the plan validator |
| Q16 | **Dead branch** in the route wipe: the second test repeats the route condition, so order entries never set the "last city" (and would have read the wrong table anyway). | `Player.cpp: RobotPlanRoutes` | [H] |
| Q17 | **Index slip** in "rescue endangered legs": the midnight fix-up after setting the start hour uses the plane index instead of the entry index, so the new entry can keep an hour >= 24. | `Player.cpp: RobotPlanRoutes` | [H] code; [M] what the validator does with it |
| Q18 | **Demand comparison** divides both sides by the candidate's plane count, and plane counts are not updated during the fill, so the busiest leg always wins. | `Player.cpp: RobotPlanRoutes` | [H] |
| Q19 | **Competitor count** for ticket pricing tests the robot's own "recently flown" value for every owner instead of each owner's. If the robot itself has not flown the leg for 5+ days it sees no competition at all (no undercutting, no loss tracking). | `Player.cpp: RobotPlanRoutes` | [H] |
| Q20 | **Unseeded randomness**: one sabotage roll uses the C runtime random; single-player action rolls are seeded from the wall clock. Robot behaviour is not replay-deterministic in single player. | `Player.cpp: RobotExecuteAction` | [H] |
| Q21 | **Credit interest** (all players, matters most for credit-hungry robots): the daily interest on an outstanding credit is computed from cash, not from the credit amount (so with negative cash the player *receives* money). | `Player.cpp: NewDay` | [H]; general economy issue, cross-reference the bank spec |
| Q22 | **Saving-for-plane precedence**: the "not saving for rocket" guard only covers the 3-rivals case, not the slot 2 / slot 0 cases. | `Player.cpp: RobotExecuteAction` (office) | [H] |
| Q23 | **Starting route filter**: the "first-tier route" filter for the free starting route in Hard and above is always bypassed (its loop counter is incremented before being tested). Affects all players. | `Sim.cpp: ChooseStartup` | [H] |

---

## 11. Open questions

1. Q1/Q2 reverse the earlier reviewer's "never fires" conclusion. Confirm in a running build
   (e.g. instrument the SDL port and log robot action ids) that robots really alternate
   office / other, and board / other after 16:00, before OpenAT locks this in.
2. Exact robot-pump tick rate in real time and per speed setting (to convert work countdowns
   into game minutes). The network code implies about 20 ticks per second.
3. What the plane plan validator (`CPlane::CheckFlugplaene`) does with entries written into the
   last array slot, with an entry at hour >= 24 (Q17), and with several cargo trips sharing one
   start time (Q15). Belongs to the schedule spec.
4. Whether sabotage-caused plane problems delay robot flights through some path other than the
   pre-departure check (Q5).
5. The route box sets the robot's rank to "best existing rank + 1" right after the generic rent
   helper computed one; can these differ (e.g. if the reverse leg is ranked differently)?
6. Robot phone calls and face-to-face approaches to the human (`Person.cpp`, walking to a
   player): dialog topics and the effect of accepted cooperation are not covered here.
7. Same-city order acceptance relies on the positioning cost being the 1 000 minimum; verify the
   80 % rule is effectively always true for those offers (i.e. any premium >= 1 250).
8. Network games: only the synchronisation envelope was read; clients replay host decisions and
   do not plan.

---

## 12. Corrections found while implementing (OpenAT, checked against the reference source)

* §6.2 step 1: the wipe tests object type **2 (contract)**, not 1 (route) — the code comment says
  "delete old route plans". Robots in route mode therefore lose future contract entries each night,
  and their route flights persist through the weekly rollover; the fill loop inserts new flights at
  now + 2 h in front of them. New quirk **Q24** (OpenAT switch `AiOptions.FixRouteWipe`).
* §6.2 step 5: when the top leg has no usable plane, or the chosen plane's plan is full, nothing is
  added and the loop ends for the night (the "continue" re-tests the flag) — it does not move on to
  the next leg.
* §8: FORCEROUTES is add-on **6** (difficulty 16, the matrix column), not add-on 05 as §6.1 says;
  IMAGEBONUS and the missing TRAVELHOLDING are the same mission.
* §5.2 "Sabotage": the "security needed" bits are set only for jobs that were *not* cancelled by the
  victim's protection; the annoyance counter is local to the visit.
* §5.2 "Board": the route-row condition compares the route slip's leader (always −1, routes are never
  posted) with the disliked rival (−1 when none), so it holds whenever the robot dislikes nobody.
* §5.2 "Route box": "rent per passenger" = the airline's own licence record's rent ÷ the leg's potential;
  for unowned legs that record holds 0 (or a stale fee), so the first eligible leg usually wins.
* §5.2 "Office" step 1: the neighbour's route counter is the same field that holds a robot's route
  mode, so a robot also copies a neighbouring *robot* in route mode (value 1).
* §5.1: the duplicate guard, the disliked-rival roll and the phone-call roll all draw from the action's
  local generator before the action runs (one roll even when nobody is disliked).
