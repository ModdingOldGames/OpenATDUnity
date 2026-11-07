# Simulation core: clock, main loop, game setup, missions, RNG, save state, events, module map

Clean-room behaviour spec for OpenAT. Everything below paraphrases what the original Airline
Tycoon Deluxe (ATD) does. It contains no source code. In-game texts are referred to by resource
file plus group token and id (e.g. `std_*.res` group `NewG` id 2000; `dlg_*.res` group `Boss`).
Group tokens: `Advi` advisor, `Lett` letters, `Misc`, `NewG` new-game, `Tuto` tutorial, `Boss`
(airport manager dialogue), `Sche` schedule/weekday names, `Mony` money history reasons.

Confidence tags: **[H]** read directly from the code; **[M]** inferred from the code with one
assumption; **[L]** a guess. **QUIRK** marks original behaviour that looks like a bug but is what
the game does. Decide per quirk whether OpenAT reproduces it (the default is to reproduce it, behind
a named flag where that is cheap).

Cross references to other topic files use proposed names (`finance.md`, `flights.md`, ...). Rename
them to whatever the other spec writers chose.

---

## 0. Source map (traceability)

| Area | Files / functions |
|---|---|
| Real-time loop, speed multipliers, room windows, evening/morning transitions | `Takeoff.cpp: CTakeOffApp::GameLoop` |
| Per-tick clock advance and all minute/hour/day triggered logic | `Sim.cpp: SIM::DoTimeStep` |
| Midnight processing | `Sim.cpp: SIM::NewDay`, `Player.cpp: PLAYER::NewDay`, `AirportView.cpp: AIRPORT::NewDay` |
| New game setup | `Sim.cpp: SIM::ChooseStartup, CreateMissionCities, CreateEarthquakeCity`; `NewgamePopup.cpp: NewGamePopup::NewGamePopup, OnLButtonDown` |
| Time helpers | `Sim.cpp: SIM::GetHour, GetMinute, GetWeek, GetSeason, UpdateSeason, GetTimeString` |
| Evening screen | `Abend.cpp: CAbend::CAbend, ~CAbend, OnPaint, OnRButtonDown, OnKeyDown` |
| Morning briefing, morning sabotage, route-mission bookkeeping | `Aufsicht.cpp: CAufsicht::CAufsicht, ~CAufsicht, OnRButtonDown, TryLeaveAufsicht` |
| Mission evaluation, bankruptcy and image game-over | `Dialog.cpp: (TALKER_BOSS branch of the dialog handler)`; `Stdraum.cpp: (TALKER_BOSS opening in StartDialog; MENU_GAMEOVER and MENU_BRIEFING handling)` |
| Win test and mission metric | `Player.cpp: PLAYER::HasWon, PLAYER::GetMissionRating` |
| Mission feature matrix for computer players | `Player.cpp: RobotUse` |
| Constants | `defines.h`; `Global.cpp` (mission targets, repair/advert prices); `Sim.cpp` (start-money table, month table); `AirportView.cpp` (opening hours) |
| RNG | `Misc.cpp: TEAKRAND::*` |
| Save / load | `Sim.cpp: operator<< / operator>> (SIM), SIM::SaveGame, SIM::LoadGame`; `Player.cpp: operator<< (PLAYER)` |
| Options and unlocked missions | `Sim.cpp: COptions::ReadOptions, WriteOptions` |
| Highscores | `Sim.cpp: SIM::AddHighscore, SaveHighscores, LoadHighscores`; `Player.cpp: (daily statistics update)` |
| Keys for speed and pause | `Gameframe.cpp: GameFrame::OnKeyDown, OnChar, Pause`; `Stdraum.cpp` (clock click) |

---

## 1. Game clock

### 1.1 Units [H]

| Quantity | Representation |
|---|---|
| `Time` | Unsigned integer, **1000 units = 1 game minute**, 60 000 units = 1 game hour. Runs 0 .. 1 440 000 (24:00) within a day. |
| Hour | `Time / 60000` (integer division) |
| Minute | `(Time / 1000) mod 60` |
| Clock string | hour (width 2) `:` minute (2 digits, zero-padded) |
| `Date` | Integer day counter, 0 on the first day of the game. It is the single authoritative "day" value: contracts, flight plans and statistics are all keyed by it. (Stored obfuscated in memory, which is irrelevant to us.) |
| Tick | One simulation step. Each tick adds `GameSpeed` to `Time`. `GameSpeed` is the constant **30** (it is saved and loaded, but nothing ever changes it). So 1 tick = 30 units = 1.8 game seconds. Day boundaries (9:00 = 540 000, 18:00, 24:00) are multiples of 30, so the clock hits them exactly. |
| Real time | **20 ticks per real second** at normal speed (one tick per 50 ms of wall clock). |
| `TimeSlice` | Counter of ticks played today. It is reset to 0 at day start and when the clock is at 9:00 sharp. It only advances while time actually runs (not paused, not frozen, no options or network wait). Used for spawning cadences and network sync. |
| `TicksPlayed`, `TicksSinceLoad` | Total ticks played (in 1/20 s units). Bookkeeping only. |

Derived figures at speed 0 (×1): 1 game minute = 33.3 ticks = 1.67 s. 1 game hour = 100 s. The
working day 09:00 to 18:00 lasts 15 real minutes. [H]

### 1.2 Weeks, weekdays, months, calendar date, seasons

* **Calendar date shown to the player.** `StartTime` is a real timestamp chosen at game start. The
  displayed date of day `Date` is `StartTime + Date × 86400 s`, converted with the local time zone.
  [H]
  * Option "random start day" (`OptionRandomStartday`, default **on**): `StartTime = random(0..364) ×
    86400`. That is a random day of **1970** counted from the Unix epoch. It uses the C runtime
    `rand()`. [H]
  * Option off: `StartTime` = the current wall-clock time. [H]
  * In a network game the host picks `StartTime` and sends it to the clients. [H]
* **Weekday.** `StartWeekday = (tm_wday(StartTime) + 6) mod 7`, so 0 = Monday … 6 = Sunday. Weekday
  names on screen use `(StartWeekday + Date) mod 7` to index `Sche` 3000+ (short names) or 3010+
  (long names). [H]
  * The `Weekday` field used for opening hours is set to `StartWeekday` on day 0. At every midnight
    it is set to `tm_wday` (Sunday-based) of `StartTime + Date × 86400`, computed **before** `Date`
    is incremented. A Sunday-based weekday of yesterday equals a Monday-based weekday of today, so
    `Weekday` is effectively 0 = Mon … 5 = Sat, 6 = Sun for the current day. [H] (The arithmetic is
    certain. Whether it was intended is not.)
* **`GetWeek()` = `Date mod 4`.** It is unused. [H]
* **Month / MonthDay** start at 1/1 whatever the calendar date, and advance with a private table
  (31, 28, 31, 30, 31, 30, **30**, 31, 30, 31, 30, 31). The rollover test makes every month one day
  shorter than the table says. The values are only saved and loaded and never shown. **Do not
  implement them as game logic.** [H] QUIRK
* **"Month" for statistics** is simply every 30 days: when `Date mod 30 == 0`, each statistic rolls
  its month buffer (see `statistics.md`). [H]
* **Year.** There is no year counter. The build year of a newly bought plane is `2002 + Date / 365`.
  [H]
* **Season** (`GetSeason`). Take the calendar date of the current day (as above). Compute
  `d = month(1..12) × 30 + dayOfMonth` (a simplified day of the year). [H]

  | Season | Index | Range of d | Calendar meaning |
  |---|---|---|---|
  | Spring | 0 | 95 .. 170 | 5 Mar – 20 May |
  | Summer | 1 | 171 .. 251 | 21 May – 11 Aug |
  | Autumn | 2 | 252 .. 379 | 12 Aug – 19 Dec |
  | Winter | 3 | ≤ 94 or ≥ 380 | 20 Dec – 4 Mar |

  The season selects the airport graphics set (`glBrick<season>.gli`) and the evening screen picture.
  It also selects the kind of seasonal flight incident (§6.4). The graphics are reloaded at day change
  if the season changed. A destroyed office also forces a reload. [H]

### 1.3 The working day and the day-state machine [H]

`DayState` values: 1 = morning (briefing), 2 = playing, 3 = evening screen open, 4 = day finished
(night fast-forward). `bNoTime` = clock frozen.

```
          ┌──────────── 09:00, DayState=1, bNoTime=1 (clock frozen at exactly 9:00) ───────────┐
          │  all players are put in the airport and into the airport manager's office            │
          │  local player leaves the briefing room / dialog ends → bNoTime=0, DayState=2         │
          ▼                                                                                       │
   09:00–18:00 playing (DayState 2)                                                               │
          │  (a) clock reaches 18:00            → bNoTime=1 until the evening screen paints        │
          │  (b) local player "calls it a day"  → player.CallItADay=1, DayState=2 (unchanged),     │
          │                                       Sim.CallItADay follows the local player          │
          ▼                                                                                       │
   evening sequence: once no human is in a room any more, the evening screen opens → DayState=3    │
          │  evening screen, first paint at ≥18:00 (or right-click / Esc) → bNoTime=0, DayState=4  │
          ▼                                                                                       │
   night fast-forward (×600). 24:00 → NewDay. Clock runs on to 09:00 ──────────────────────────────┘
          at 09:00..10:00 with DayState=4: evening screen closes, bNoTime=1, DayState=1,
          every non-out player enters the airport, then the manager's office.
```

* **Start of the working day: 09:00.** The clock stops at exactly 9:00 for the briefing. The rule
  for the step: if the clock was below 8:00 or below 9:00 before a tick and reaches it after the
  tick, the current batch of ticks ends. [H]
* **End of the working day: 18:00.** The player gets a warning at 17:30 (sound `DayWarn.raw` plus
  advisor message `Advi` 2390) and another sound at 17:59. [H]
* The evening screen (`Abend.cpp`) runs `Players.UpdateStatistics()` when it opens. That computes
  company value and so on, and writes the highscore (§5.4). It also resets every player's speed
  setting to 0. [H]
* If the player calls it a day before 18:00, the evening screen opens at once. **Simulated time keeps
  running at ×600 until 18:00 and on through the night.** Computer players keep acting during that
  stretch, because their AI pump is not gated by call-it-a-day (see §2.3). The pedestrian queue and
  the player-figure walking are paused. [H]
* "Call it a day" sets `Sim.DayState = 2`. If the autosave option is on, it also writes the autosave
  (single player). In a network game the host waits until every human has called it a day, autosaves,
  and broadcasts a target `TimeSlice` (current + 30) at which everyone switches. [H]
* Manual: the Tab key sends the player home at once (= call it a day). [M] (keyboard topic)

### 1.4 Real time → game time (single player) [H]

Each iteration of the main loop:

1. Count how many whole 50 ms periods have elapsed since the last iteration. That count is `n`, the
   base number of ticks. (After load/save or while the window is inactive the reference time is
   reset, so there is no catch-up.)
2. Some things run `min(n, 400)` times at real-time rate, **before** the speed multiplier. These are
   the per-player message/advisor pumps (`Players.MessagePump`), only during 9–18 with nothing paused.
3. If the cursor of a human player is inside the clock/date area of the status bar (x 102–255,
   y 440–459) and a mouse button is held: left button ×5, right button ×20. (Manual: "hold the
   mouse button on the date/time to accelerate".) Not available in network play or after
   call-it-a-day.
4. Pick the **effective speed level** `d`:
   * `d` = minimum of `GameSpeed` (0..3) over all players that are **not** computer-controlled and
     have not called it a day. If there are none, `d` = 3.
   * If call-it-a-day is active, or the clock is before 9:00: `d = 5`.
   * If the clock is before 8:00 or at/after 18:00, and DayState = 4: `d = 5`.
   * If "warp" is active (the local player is walking in the airport after pressing Space, single
     player only): `d = 4`. Warp ends when the player is no longer in the airport hall or has
     reached the destination. The camera then recenters.
5. Multiply the tick count (and the cap `Faktor`) by:

   | d | Meaning | Multiplier SP | Multiplier network |
   |---|---|---|---|
   | 0 | normal | ×1 | ×1 |
   | 1 | fast | ×2 | ×2 |
   | 2 | faster | ×4 | ×3 |
   | 3 | fastest | ×8 | ×4 |
   | 4 | warp (walking) | ×150 | ×150 |
   | 5 | night / idle fast-forward | ×600 | ×600 |

6. Single player: the tick count is capped at `Faktor` (= multiplier × clock-click factor). The game
   therefore runs **at most one multiplied 50 ms quantum per loop iteration**. A slow frame loses
   time; it does not catch up. Network: the count is adjusted by sync logic instead (§2.5).
7. Run the ticks in a batch. A batch is at most 400 ticks, or 1600 ticks while call-it-a-day is
   active or the clock is outside 9:00–18:00. A batch also ends early at 8:00 / 9:00 crossings.
   Leftover ticks carry into the next iteration.
8. Repaint once after the batch.

Resulting rates (game minutes per real second = 0.6 × multiplier):

| Level | Game min / real s | Real time for 09:00–18:00 |
|---|---|---|
| 0 | 0.6 | 15 min |
| 1 | 1.2 | 7.5 min |
| 2 | 2.4 | 3.75 min |
| 3 | 4.8 | 1 min 52.5 s |
| warp | 90 | – |
| night | 360 | 18:00 → 09:00 (900 min) ≈ 2.5 s |

**Speed controls** [H]:

* `+` key: own `GameSpeed = (GameSpeed + 1) mod 4`. `-` key: `(GameSpeed + 3) mod 4`. Both
  wrap around, so `+` at 3 gives 0.
* Left-click on the status-bar clock symbol: +1 (wraps). Right-click: −1 (wraps).
* Space while walking in the hall (single player): warp. The manual says "20 times", but the code
  uses ×150. Holding a mouse button on the clock: ×5 or ×20 (see step 3).
* Each player has their own `GameSpeed`. Humans start at 0 and computers at 3 (computers do not
  count anyway). Every evening all are reset to 0.
* Several events force the local player's speed back to 0 and cancel warp (fax/phone interruptions:
  strike start, strike end call, plane problem fax, sabotage call/fax). A debug test mode suppresses
  this.

**Pause** [H]: the Pause key toggles pause (the network broadcasts it). Any left click while paused
unpauses. While paused, or while the options screen is open, or while waiting for a network player,
**nothing in the simulation runs**: no persons, no AI, no planes, no clock, no `TimeSlice`. The
paused screen shows a pause overlay.

### 1.5 Opening hours of airport services [H]

Evaluated every game hour (and at 9:00 on day 0) during 8:00–18:00 when not called-it-a-day.
Closed services show a closed door / "closed" menu; computers cannot enter.

| Service | Opens | Closes | Weekday rule (0 = Mon) |
|---|---|---|---|
| Duty free shop | 10:00 | – (16:00 on Sat/Sun) | Sat/Sun close at 16:00 |
| Kerosene seller (back room) | 11:00 | – | closed Sunday |
| Last-minute counter | – | 16:00 | closed Saturday |
| Museum (used planes) | 11:00 | – | closed Sat + Sun |
| Travel agency | – | 17:00 | – (pedestrians avoid it on Sunday; cosmetic only) |
| Plane broker | – | 16:00 | – |
| Advertising agency | 12:00 | – | closed Sat + Sun. These restrictions only apply when Difficulty ≥ 3 (base mission 4) or free game. |
| Security office | – | – | closed while the outage counter > 0 (3 days after the pliers sabotage) |

The last-minute and travel-agency doors only exist when the mission flag `TRAVELHOLDING` is set
(§3.7). Detailed room behaviour: `rooms-and-ui-*.md`.

Also: a player who stays **2 game hours** inside one of the shared rooms (travel agency, last minute,
duty free, bank, museum, kerosene seller, broker, manager's office, route box, café, sabotage room,
NASA, Rushmore, island, workshop, kiosk, freight counter) is pushed out and barred from that room for
2 game hours. [H] (`Player.cpp: PLAYER::CalcRoom`)

---

## 2. Main loop in behavioural terms

### 2.1 Application states [H]

Boot → (intro) → title / main menu → new-game wizard (difficulty, home airport, players) → `INIT
done` → game screen. On entering the game, either the save is loaded or all players enter the
airport and walk to the manager's office: DayState 1, `bNoTime` set, tutorial flag if the tutorial.
The outro states play `outro.smk` (base-game final mission won) or `outro2.smk` (add-on mission 10
won), then return to boot. A "game over" menu returns to boot (main menu). Details:
`rooms-and-ui-4-title-and-meta.md`.

### 2.2 Per loop iteration (outside the tick batch) [H]

* Decide call-it-a-day (single player: mirror the local player's flag; network: host consensus).
* Real-time pumps (advisor messages).
* For each player: if waiting for a busy room that became free, walk there.
* Tutorial script state machine (single steps of `Sim.Tutorial` 999 → 1000 → 1100 → 1230 → 1240 →
  1310 → 1400 → 1401 → 1500 → 1501 → 1502 → … → 1600/1602 → 2000 → 2002 → end). Each step waits for
  silence, room entry and so on, and plays `Tuto` texts with the same ids. See `tutorial.md`.
* Speed computation (§1.4). Network sync gates (§2.5).
* Tick batch (§2.3).
* `TickerTime = realtime_ms / 32`. Drives brick animations only.

### 2.3 Per tick (inside the batch), in this order [H]

1. Drunk cursor wobble (cosmetic).
2. **Room bookkeeping**, if (9:00 ≤ time ≤ 18:00 and not call-it-a-day) or (the local player is not
   in the evening screen):
   * per player: caffeine −1; every time `(Time & 31) == 0` recompute the current room (this
     enforces the 2-hour limit);
   * humans: process leave/enter flags of the room stack. Leaving releases `RoomBusy`. Entering
     opens the room window. Room transitions use a fade.
   * computers: entering a busy room swaps the robot's action queue (it does something else first).
     Entering a free room executes the queued robot action at once. Entering/leaving locks/unlocks
     the room's NPC talker.
3. Unless paused / options open / network wait:
   1. Player figures walk (8:00–18:00, not called-it-a-day): waypoint refresh when `(Time & 255) ==
      0`, steering every tick.
   2. Unless (time < 8:00) or (time ≥ 18:00 and DayState = 4): pump the pedestrian queue (not after
      call-it-a-day), advance all persons one step, pump talkers and airport doors (not after
      call-it-a-day).
   3. **Computer AI pump** (`Players.RobotPump`) every tick while 9:00 < time ≤ 18:00 and the clock
      is not frozen (and the tutorial has progressed far enough). Not gated by call-it-a-day.
   4. **Plane animation/movement** (`Planes.DoOneStep` per non-out player) on **every 11th tick**
      (a skip counter of 10).
   5. Fly-by ambient sound (cosmetic, real-time random).
   6. **Clock advance** (`DoTimeStep`, §2.4) unless the clock is frozen. After it, a random advisor
      chatter line at certain clock values while playing.
   7. Evening/morning transitions (§1.3).
   8. Spawning (8:00–18:00, not called-it-a-day, not frozen): shoppers every 64 `TimeSlice`, flight
      passengers every 32 `TimeSlice` (offset 16), mood bubbles every 8 `TimeSlice` (only while the
      local player is in the hall). Airport announcements at :30 of even hours from 9:00 (cosmetic;
      seeded random).
   9. Unload unused pedestrian gimmick animations (cosmetic).
   10. `TimeSlice`: reset to 0 at exactly 9:00 (and the pedestrian queue is cleared), else +1.
4. Voice scheduler pump (9–18). Tick counters −1 / +1. Network pump.

### 2.4 `DoTimeStep`: what happens on each tick, minute, hour [H]

Notation: "minute change" = the minute value differs from before the tick. "Hour change" = the
minute value wrapped (new minute < old minute).

**Every tick**

* `Time += 30`. `PlayerDidntMove++` (idle counter, in ticks).
* If `Time ≥ 24:00` → `SIM::NewDay` (§2.6), then `AIRPORT::NewDay` (clears all pedestrians,
  respawns the four player figures and waypoint walkers such as the dog).

**On every minute change**

1. Reseed the two pedestrian RNG streams with `minute × 333 + hour × 7` (§4.2).
2. Every 5 minutes (`minute mod 5 == 0`): increment the refill counters of travel agency, last
   minute, freight, museum and of every city's foreign-contract and foreign-freight lists. These
   counters limit how many slips refill during the day; they are reset to 6 at midnight (see
   `contracts.md`, `freight.md`).
3. Network random-state checks (debug).
4. At 18:00 (network, no computers): mark all players ready-for-morning.
5. Network money/image/routes sync at :10/:20/:30.
6. Status bar repaint.
7. **Strike bookkeeping, per non-out player:** if a strike is running and an end countdown is set,
   the countdown −1 per minute. At 0 the strike ends (human: notify later). Notification of strike
   start (fax + letter `Lett` 508, speed → 0) once the player is in a room without menu/dialog
   between 9:10 and 18:00. Notification of strike end by phone call if reachable. Details §6.1.
8. **On hour change:**
   * at **10:00**: strike planning test for the **local human only** (§6.1);
   * strike start/continuation for every player (§6.1);
   * **plane technical problems** for the **local human only**, 6:00–16:59, not after
     call-it-a-day (§6.2).
9. On hour change (and at 9:00 on day 0), between 8:00 and 18:00, not called-it-a-day: update the
   opening-hour blocks (§1.5) and the "office destroyed" blocks.
10. On hour change: **execute pending flight-time sabotage** (`ArabMode`) for every player. See
    `sabotage.md`.
11. Copy-protection check on days where `Date mod 30 == 29` (inert in the shipped code; ignore).

**Every tick (time-of-day triggers)**

* 17:30 warning and 17:59 sound (§1.3).

**Every 4 game minutes** (`minute mod 4 == 0`, on change): for every airborne plane (`Ort = −5`)
of every non-out player whose current flight has been airborne for at least one full hour:

* poor-condition incident roll (§6.3);
* seasonal incident roll (§6.4);
* update the globe position. The fraction is interpolated between departure and arrival city by
  elapsed hours. The longitude takes the short way around the date line. Only computed at 8:00,
  during 9–18 if not called-it-a-day, or at minute 0. The 3D globe is only refreshed when the local
  player is looking at the laptop or globe.

**On hour change (minute 0)**

1. **Share prices**, per player (§6.6).
2. Per plane of every player: `Problem` −1 and `PseudoProblem` −1 (both in hours, floor 0).
   Recompute passenger numbers of **all route flights** in its plan. Refresh next-flight pointers.
3. **Delays**, for each flight of a **non-computer** player whose departure hour is the **next**
   hour. The flight is delayed by one hour if (first match wins, each with an advisor message
   `Advi` 2350–2358):
   * the plane is impounded (`PseudoProblem > 0`, from sabotage) → `Advi` 2358;
   * a strike is running (this is checked independently, and sets delay too) → 2356;
   * fewer pilots or attendants than the type requires → 2350;
   * condition < 15 % → 2351;
   * a contract flight that is flagged not OK → 2352;
   * `Problem > 0` → 2355.

   Delay = departure hour +1 (wrapping to the next day at 24). `STAT_VERSPAETUNG` +1. The plan is
   re-validated and contract usage refreshed. **Computer players' flights are never delayed.** [H]
   QUIRK
4. Repaint text bricks (departure boards).

**At :15 of every hour** — landings: every plane whose current flight lands this hour and today:

* If that flight is a contract flight with 0 passengers (spare-parts ferry), each of the player's
  planes standing at the destination with `Problem > 1` gets `Problem = max(1, Problem − 15)`
  (advisor `Advi` 2320).
* Destination = home airport: the plane enters landing approach (`Ort = −1`), staggered on screen.
* Other destination: passengers' satisfaction is computed (except freight types) and the plane is on
  the ground there (`Ort = city`). See `flights.md`.

**At :30 of every hour** — departures: every plane whose current flight departs this hour today:

* `NumFlights` +1.
* From the home airport with a gate assigned and not a ferry/freight-only type: the plane rolls to
  the gate behind the window (`Ort = −3`), staggered. Boarding and actual take-off are animated by
  the plane step. Revenue is booked there; see `flights.md`.
* Otherwise: airborne at once (`Ort = −5`) and the flight is **booked** (revenue/cost). If it departs
  from home with no gate (gate −1): advisor `Advi` 2353 and **image −2** (clamped −1000..1000).

**At 17:00** (crossing): if the autosave option is on and not called-it-a-day, autosave to slot 11
(name `Misc` 5000) and show advisor `Advi` 500.

Plane `Ort` codes: ≥ 0 = standing at city id; −1 = landing at home; −2 = taking off at home;
−3 = at the gate / behind the window; −5 = in the air. [H]

### 2.5 Network (summary only) [H]

The main loop computes the same tick count on every machine. A host-sent `TIMEPING` gives a
correction `gTimerCorrection` that is folded in gradually (±1 tick every 2nd or 8th iteration).
Mornings wait until all humans are ready (`bReadyForMorning`) and briefing (`bReadyForBriefing`).
Call-it-a-day is decided by the host. Computer players run on the host only. Random streams are
compared every 5 game minutes. A network spec is out of scope here; see `network.md`.

### 2.6 New-day sequence (midnight) [H]

`SIM::NewDay`, in exact order. The topic file that owns the details is in brackets.

1. Reset per-day flags: "persons in airport", `TimeSlice = 0`. Reseed the pedestrian RNGs with
   `Date` (still yesterday's value).
2. Reset the daily one-off item pickups (glove, paper clips, glue, charcoal, perfume, pliers,
   postcard) so they can be picked up again. [`items.md`]
3. `UniqueGameId` +1 (network). Security-office outage counter −1 if > 0. [`security.md`]
4. Clear stink-bomb smoke.
5. Compute `Weekday` (§1.2).
6. Every 6th day (`Date mod 6 == 0`): reset the workshop "slimed" state. [`sabotage.md`]
7. Clear "hints shown today". Debug: a test cheat refills money.
8. End the tutorial flag. Clear "talked to Rick today".
9. Clear room occupancy. Reinitialise talkers.
10. Refresh which pedestrian clans appear today. Reseed the pedestrian RNGs. Clear the person queue.
    Laptop "sold to" = none.
11. Clear floor flags (glue/stench/person markers). Recompute airport seats.
12. If any player's office is destroyed (state 3), force a season-graphics reload.
13. **`Date` += 1, `Time` = 0.** Reload season graphics if needed.
14. **Refill the slip boards** with fresh deterministic seeds: freight seed `Date`, last minute
    `Date+1`, travel agency `Date+2`. Reset the refill counters to 6. For every city: foreign
    contracts and foreign freight, seed `Date + cityIndex + 3`, refill counters 6. [`contracts.md`,
    `freight.md`]
15. Month/MonthDay bookkeeping (no effect).
16. **Airport expansion** if requested: check-in +1, waiting zone +1, suitcase = waiting zone, record
    the expansion date, reload the layout. Or reload the layout if flagged. [`airport.md`]
17. **Kerosene price** update (§6.5). [`kerosene.md`]
18. **`PLAYER::NewDay` for every non-out player**, in player order 0..3 (§2.7).
19. Update the static door images.
20. **Museum**: generate 3 fresh used planes (§6.9). [`planes.md`]
21. **Comparison headlines** (every 4th day after day 8). Rocket-progress headlines in the space
    mission. [`newspapers.md`]
22. **Route demand** update: on day 0 `Bedarf = capacity × 4.27`, afterwards
    `Bedarf = (6 × Bedarf + capacity × 4.27) / 7`. [`routes.md`]
23. **Newspapers**: pick today's random/chain headlines, substitute placeholders, merge queued
    "override" headlines, sort by priority. [`newspapers.md`]
24. **Auction board resolution** (manager's notice board, 7 slots each for branch offices, routes,
    gates): each slot with a bidder goes to that bidder at **3 × bid price** (money reasons
    2040/2041/2042). Branch: rank as bid, image 0, rent = bid. Route: rented at the bid rent. Gate:
    first free gate slot, rent = bid. A summary letter goes to the local player if they bid
    (`Misc` 2600–2603, 2610–2612). [`auctions.md`]
25. Renumber gates contiguously over all non-out players. Rebuild the gate mapper.
26. **New auction slips**: `TafelData.Randomize(min(Date + 2, 7))`. [`auctions.md`]
27. **Workers / job market** daily update (§6.10). [`personnel.md`]
28. **Sympathy drift** between players (computers' feelings towards others), per ordered pair
    (c ≠ d), only while c's sympathy to d < 25 and d's balance today > 0. "Balance" = the sum of the
    day's income/expense sheet `Bilanz`. Penalties only count against non-computer d.
    * if sympathy < −50 and d's balance > c's balance: −10 if d leads by > 1 000 000, −2 if d leads
      by > 100 000 (both can apply);
    * else, if d's balance > 2 × c's balance: −5 if the lead is > 1 000 000, −1 if > 100 000.

    Afterwards, for every pair: if |sympathy| > 70, multiply by 69/70 (integer). [`ai.md`]
29. Mission "crisis management" (ATFS06) only: random forced sabotage against the others (§3.6).
30. Per player: a separator line in the money history. Clear the player's call-it-a-day. **Copy
    today's balance sheet to "yesterday" and clear today's.** The night's interest, rent, salaries and
    maintenance were booked in step 18, so they land in yesterday's sheet.
31. Clear the global call-it-a-day.

Then `AIRPORT::NewDay` (pedestrians cleared, player figures and waypoint walkers respawned).

### 2.7 `PLAYER::NewDay` (per airline, in order) [H]

Owner topics in brackets. Formulas belong to those files; listed here for order only.

1. Reseed `PlayerWalkRandom = Date + PlayerNum` and `PlayerExtraRandom = 0`.
2. (Network sync of planes, workers, meeting.)
3. Reset daily state: mechanic anger, out-of-gates, talking, office state (repaired), smoking,
   stench, alcohol available, caffeine, visited museum. `DaysWithoutStrike` +1,
   `DaysWithoutSabotage` +1. Glowing charcoal → charcoal.
4. Computers in missions with `IMAGEBONUS`: image += `Date mod 3`. [`ai.md`]
5. Computers whose hidden bonus is exhausted (Bonus = 0) get it topped up depending on how the local
   human is doing (§3.3). [`ai.md`]
6. Laptop virus: state 2 → 3 (repaired overnight). Computers' viruses cleared.
7. Clear sabotage-day effects (foreign brochures, telephones blocked, press release), smoke, sickness,
   toilet run, room timers. `ArabHints` −= min(3, ArabHints). [`sabotage.md`]
8. Statistics: if `Date mod 30 == 0` roll months, then roll days (§5.3). [`statistics.md`]
9. On odd days the "called a competitor" cooldown −1.
10. Laptop battery recharge by quality: 1 → 40, 2 → 80, 3 → 200, 4 → 1440. [`items.md`]
11. Rented routes: last-flown +1 (cap 99), utilisation average, low-utilisation warnings and
    revocation after 20 days below 10 % (letters `Lett` 3000–3002, 3100–3102). [`routes.md`]
12. Money history shift. Share price history shift, plus the company-value drop penalty on today's
    price (×0.97 / ×0.97 / ×0.95). `MaxAktien` ×1.05 (cap 250 000 000). [`finance.md`]
13. Clear the room stack.
14. **Interest** (see the QUIRK below), security costs. [`finance.md`]
15. **Rents**: every rented gate, branch and route charges `rent / 30` per day. [`finance.md`]
16. **Salaries** (humans only): monthly salary / 30 per employee per day. [`personnel.md`]
17. **Contracts**: penalties for contracts that expired yesterday unfulfilled, and removal of contracts
    that expired the day before (both passenger contracts and freight). [`contracts.md`]
18. Robot re-initialisation, walk speed, message queue new day. [`ai.md`]
19. **Dividends** paid out and received (§ `finance.md`). Trusted-dividend drift.
20. **Random flavour letters**: repeat { c = rand() mod 80; stop if c > 16; letter `Lett`
    2000+10c / 2001+10c / 2002+10c to humans }. Expected 0.27 letters per day. Uses the
    unseeded C `rand()`. [H]
21. **Plane upkeep**: each plane continues its repeating flight plan into the coming days. If it
    needs repair, the condition improves by mechanic level. Repair/maintenance costs. Mission
    "the tycoon" (ATFS10) day events (§3.6). Booked as money reason 3110. [`planes.md`, `flights.md`]
22. Robots with route planning: replan routes. [`ai.md`]
23. Wealth tax: 10 % of the money above each of 5·10¹³, 1·10¹⁴, 2·10¹⁴, 4·10¹⁴, 8·10¹⁴,
    1.2·10¹⁵, 1.6·10¹⁵, 1.7·10¹⁵, 1.8·10¹⁵, 1.9·10¹⁵ (cumulative brackets; only matters with
    64-bit money and absurd balances). [H]

**QUIRK (interest)** [H]: positive money earns `money × HabenZins / 100 / 365`. **Negative money is
also charged at the *savings* rate** (`money × HabenZins / 100 / 365`, a negative amount). If
`Credit > 0`, an extra amount `−money × SollZins / 100 / 365` is charged. That uses **money**, not
credit: with positive cash it is a cost proportional to cash, and with negative cash it becomes
income. Reproduce exactly, or fix knowingly; flag it for `finance.md`.

### 2.8 Morning (09:00) sequence [H]

When the players enter the manager's office at 9:00 (`Aufsicht.cpp`):

1. (Network: meeting sync.) Record `DaysWithoutSabotage = 0` for every victim of yesterday's
   sabotage acts.
2. **Route mission** (mission 3) only: recompute `ConnectFlags` for every player (§3.5).
3. Briefing:
   * Day 0, or the "short briefing" option off: the manager's dialogue (`Boss` dialogue, start id 1).
     On day 0 that is the mission introduction (`Boss` 1000 + 100 × mission for base missions;
     1600 + 10 × (diff − 10) for add-on; 1800 + 10 × (diff − 40) for ATFS; 700 for tutorial and
     free game).
   * Otherwise the briefing summary menu. If anything important happened (someone met the target,
     bankruptcy, image ≤ −990, takeover), the summary cannot be dismissed with a click. Right-click
     → full dialogue.
   * Full morning dialogue (Date > 0): takeover first if one is pending (`Boss` 5000); else the
     sabotage report if there were acts (2004); else a greeting (2000–2003, chosen with `rand()`).
     Then the **mission report** (§3.4), then **every airline in turn**: comments on image, plane
     condition and staff, and the **bankruptcy / image game-over checks** (§3.4).
   * Computer players (mission 19, "Uhrig" orders): get 5 order flights per day (on day 0, 2 or 3
     flights' worth by player parity). See `contracts.md`.
4. When the local player leaves (exit click or right-click; tutorial gating applies): `bNoTime = 0`,
   `DayState = 2`.
5. On leaving the morning briefing (the room's destructor):
   * clear yesterday's sabotage-act list;
   * place every player figure in front of its office (day 0: different spawn points);
   * all other players leave the office. `NumFlights = 0`. Robots get a fresh work cycle and walk to
     their own office;
   * **execute the "overnight" sabotage types** (`ArabMode2`: bacteria in coffee, laptop virus, bomb
     in office, provoke strike; `ArabMode3`: foreign brochures, phones blocked, press release,
     bank hack ±1 000 000, plane impounded for 15 h, route stolen). See `sabotage.md`;
   * unless a bomb went off, everyone walks to their own office. If a bomb went off, the camera
     follows the bombed player;
   * snap standing planes to their cities on the globe;
   * ATFS10: advisor hints `Advi` 2400–2407 on days 3, 18, 20, 25, 35, 40, 45, 55.

---

## 3. New game setup

### 3.1 Players and airlines [H]

* **Always exactly 4 airlines** (`AnzPlayers = 4`). In single player exactly one is human (owner 0).
  The player picks which of the 4 to play in the "choose player" page; the last choice is remembered
  (`OptionLastPlayer`). The other three are computers (owner 1).
* Network: up to 4 humans (local = 0, remote = 2; 3 = placeholder while loading a network save).
  The rest are computers, simulated on the host.
* Defaults from resources (`std_*.res` group `NewG`): player name id `1000 + 10c`, airline name
  `1001 + 10c`, two-letter abbreviation `1002 + 10c` (c = 0..3). Names can be edited on the
  player page. They are upper-cased in the UI and re-capitalised for display. Only the player names
  are remembered between sessions; airline names and abbreviations reset to the defaults. Duplicate
  names or abbreviations block "Start".
* Logo index = player index. Player colours by index: 0 blue, 1 green, 2 red, 3 yellow (boss-room
  comments). [M]

### 3.2 Home airport [H]

* Chosen on the main menu ("home airport" page) from all cities, 12 per page.
* Default if never chosen, by language: Berlin (German and default), London (English), Paris
  (French), Roma (Italian), Madrid (Spanish, Portuguese), Berlijn (Dutch). It is looked up by the
  city name in the city table.
* All four airlines share the same home airport.

### 3.3 Starting money, credit, bonus, interest rates [H]

Table: money for humans `Mh`, the money intended for computers `Mc`, credit `C` (identical for all),
then rates.

| Mission (Difficulty value) | Mh | Mc | C | SollZins % (loan) | HabenZins % (savings) |
|---|---|---|---|---|---|
| Free game (−1) | 1 500 000 | 2 000 000 | 0 | 10 | 3 |
| Tutorial / mission 1 (0) | 5 000 000 | 200 000 | 0 | 5 | 5 |
| Mission 2 (1) | 5 000 000 | 500 000 | 0 | 5 | 5 |
| Mission 3 (2) | 3 000 000 | 1 000 000 | 0 | 5 | 5 |
| Mission 4 (3) | 2 000 000 | 3 000 000 | 0 | 6 | 4 |
| Mission 5 (4) | 1 800 000 | 4 000 000 | 0 | 10 | 3 |
| Mission 6, space (5) | 1 500 000 | 6 000 000 | 0 | 15 | 1 |
| Add-on 1 (11) | 3 000 000 | 1 000 000 | **10 000 000** | 15 | 1 |
| Add-on 2–10 (12–20) | 3 000 000 | 1 000 000 | 0 | 15 | 1 |
| ATFS 1–10 (41–50) | 3 000 000 | 3 000 000 | 0 | 15 | **0** |

* **Every airline (human and computer) starts with `Money = Mh`.** Computers additionally get a
  hidden `Bonus = |Mc − Mh|`. In ATFS it is `Mc − Mh` = 0, and there is no absolute value there.
  Humans' bonus = 0.
* QUIRK: because of the absolute value, computers in the early missions (where Mc < Mh) get a
  *larger* hidden bonus (e.g. 4 800 000 in the tutorial), not a handicap. [H]
* The bonus is paid out as a secret surcharge on each booked flight: the flight's result gets
  `+min(Bonus, |result| / 10)`, and the bonus is reduced by `min(Bonus, |new result| / 10)`. The
  second term uses the already increased result. QUIRK [H] (`Schedule.cpp: (flight booking)`).
* Once a computer's bonus reaches 0, at each day change it gets +100 000 if the local human's
  mission rating beats its own, +100 000 more if two thirds of the human's rating still beats it,
  and +100 000 more if half of it still beats it. In add-on 1 "lower is better", so the comparison
  is inverted. Then ×2 / ×4 / ×8 according to the mission flags `BONUS_X2/X4/X8` (§3.7). [H]
* The money history starts at the starting money. The 20-day money history is zeroed.

### 3.4 Missions, goals, deadlines, win/lose [H]

**How a mission ends.** Every morning from day 1, the manager's mission report (dialogue
`Boss` 6000 + 100 × Difficulty for base and add-on missions; 8500 + 100 × (Difficulty − 41) for
ATFS) lists each airline's metric. Then:

* It picks the **leader** by the mission metric (highest, or lowest for add-on 1).
* For fixed-duration missions it first checks the day limit; before the limit it only reports.
* If the leader **has met the goal** (`HasWon`), or for ATFS 2/3/4/8 if **anyone** has met it, the
  game ends: the "game over" screen shows a win if the local player also meets the goal, else a loss.
  Meeting the goal unlocks the next mission in that campaign (`MaxDifficulty*`, stored in options).
  For the space missions, a won game plays the outro movie.
* So the goal is checked **once per day at the morning briefing**, never during the day. Ties are
  possible: everyone who meets the goal is named.
* **Free game: no mission report and no win.** The game never ends except by losing.

**Losing (all modes)**, checked per airline during the morning airline review:

* **Image < −990** → game over for the local player. A computer is removed (`IsOut`).
* **Money < −5 000 000** (`DEBT_GAMEOVER`) → bankruptcy: the same consequence. (Image is checked
  first.)
* A removed computer: all cross-shareholdings with it are zeroed. It keeps its slot but is skipped
  everywhere (no figure, no flights, no NewDay).
* Money warnings when crossing −500 000, −1 000 000 and −3 000 000 (`Advi` 2380/2381/2382). The
  overdraft limit −1 000 000 (`DEBT_LIMIT`) is enforced by shops/broker (`finance.md`).
* A hostile share takeover can also remove an airline (`Overtake`, see `finance.md`).

**Mission catalogue.** Names come from `NewG` 2000 + 100 × n (base, n = 0..5), `NewG` 3000 +
Difficulty (add-on 11..20; 3021 is a second line for 20), `NewG` 3041..3050 (ATFS). The labels in
the table are ours.

| Diff | Campaign / # | Our label | Goal (`HasWon`) | Metric shown / leader by | Duration | Special setup |
|---|---|---|---|---|---|---|
| 0 | Base 1 (tutorial) | First job | ≥ **10** contracts completed (`NumAuftraege`). Manual: 5 plus 5 more. | contracts completed | open | scripted tutorial; contracts limited for robots (`TUTORIALLIMIT`) |
| 1 | Base 2 | Passengers | ≥ **2 500** passengers carried | passengers | open | – |
| 2 | Base 3 | Profit | profit counter `Gewinn` ≥ **5 000 000** (contract penalties reduce it) | profit | open | starting planes 737-400 + Tu-154 B |
| 3 | Base 4 | Connect the world | ≥ **10** of the 12 directed connections home↔6 mission cities (§3.5) | connections | open | mission cities; starting planes 737-400 + 747-400 |
| 4 | Base 5 | Image | image ≥ **750** (max 1000) | image / 10 | open | from here on everyone starts with one rented route (§3.6) |
| 5 | Base 6 | Rocket | all 10 rocket parts (`RocketFlags`, 10 bits) | parts owned | open | NASA shop, island; letter `Lett` 1200–1202; headlines days 2, 3, 5, 9; outro on win |
| 11 | Add-on 1 | Debt | credit + overdraft = 0. Rating = credit if money > 0, else credit − money. Lower is better. | rating, lowest leads | open | credit 10 000 000; starting planes at condition **60** |
| 12 | Add-on 2 | Freight | ≥ **1 000** freight units (tons) carried (`NumFracht`) | freight | open | **no starting route** (exception) |
| 13 | Add-on 3 | Relief flights | most relief freight (`NumFrachtFree`) at day **21** | relief freight | **21 days** | earthquake city (§3.5) |
| 14 | Add-on 4 | Miles | most flown miles (`NumMiles`) at day **30** | miles | **30 days** | – |
| 15 | Add-on 5 | Luxury | service points **> 150** | service points | open | – |
| 16 | Add-on 6 | Company value | highest company value at day **21** | company value | **21 days** | all airlines start with image **300**; no travel-agency holding (no travel agency / last minute, no starting contracts); computers get daily image bonus, keep planes perfect, use routes only |
| 17 | Add-on 7 | Scrap fleet | ≥ 2 planes **and** every plane at condition ≥ **90** | average condition | open | starting planes at condition **35** |
| 18 | Add-on 8 | Share price | share price ≥ **220** | share price | open | – |
| 19 | Add-on 9 | Uhrig orders | ≥ **200** manager order flights (`NumOrderFlights`) | order flights | open | no starting contracts; 5 order flights per morning |
| 20 | Add-on 10 | Space station | all 10 station parts | parts | open | outro2 on win |
| 41 | ATFS 1 | Cash | money ≥ **15 000 000** | money | open | – |
| 42 | ATFS 2 | Safe fleet | ≥ 5 planes, **all** with condition ≥ 90 and tyres, engines, safety and electronics all at level 2 | number of such planes | open | – |
| 43 | ATFS 3 | Load factor | ≥ 4 planes and passengers per plane ≥ **500** (passengers of the last 5 days ÷ sum of fleet sizes of the last 5 days) | that average | open | – |
| 44 | ATFS 4 | No sabotage | ≥ 5 planes and ≥ **15** days without being sabotaged | days without sabotage | open | – |
| 45 | ATFS 5 | Big designs | ≥ **3** self-designed planes (designer) with ≥ **600** seats | count | open | – |
| 46 | ATFS 6 | Crisis | as ATFS 4 (≥ 5 planes, ≥ 15 sabotage-free days) | days | open | forced random sabotage among the airlines every night (§3.6) |
| 47 | ATFS 7 | Black Friday | average share price over the last 29 days ≥ **200**, **and** on each of the last 30 days the airline owned ≤ **20 %** of its own shares | avg price / days within 20 % | open | – |
| 48 | ATFS 8 | Fuel saver | ≥ **5** self-designed planes with fuel use × 100 / speed ≤ **500** | count | open | – |
| 49 | ATFS 9 | Fuel crisis | highest company value at day **45** | company value | **45 days** | kerosene crisis formula (§6.5); computers start with a 5 000 tank, open |
| 50 | ATFS 10 | Tycoon | highest company value at day **60** | company value | **60 days** | scripted events (§3.6); computers start with a 5 000 tank, open |

Notes:

* "Day N" = the morning briefing with `Date ≥ N`. The day-limited missions cannot end before that
  day (both in the dialogue and in the "cannot dismiss" logic). [H]
* For the "highest at day N" missions, `HasWon` = no non-out airline has a strictly larger value. So
  ties all win. [H]
* For the rocket missions, if the leader has 0 parts the report is skipped. [H]
* The metric percentage `STAT_MISSIONSZIEL` (statistics screen) is computed separately. In mission 3
  it wrongly divides **passengers** by the profit target. QUIRK [H] (`statistics.md`)
* `defines.h` comments on the add-on ids are shifted by one relative to the actual goals. The table
  above follows the code (`HasWon`, setup) and the resource titles. [H]

### 3.5 Mission-specific world setup [H]

* **Mission cities (Diff 3).** Six default targets, looked up by any of several localised names:
  Rio de Janeiro, New York, Tokyo, Delhi, Johannesburg, Moscow. A target that equals the home airport
  or is farther than **12 000 km** (`CalcDistance > 12 000 000`, distances in metres) is replaced by
  the most populous city that is not home, within 12 000 km, and not already chosen. If no route
  exists from home to a target, a new route pair (both directions) is created with rent 6 000, level
  1, factor 1, demand 0.
* **Connection count (Diff 3)**, recomputed each morning at 9:00 sharp: for every route flight in any
  plan of the player, on a route the player rents with a utilisation (`Auslastung`) > 20, set one bit
  for each of the 12 directed pairs (home → target i, target i → home). `ConnectFlags` = the number
  of set bits.
* **Earthquake city (add-on 3):** the city whose distance to home is nearest to **5 000 km** among
  cities with |globe latitude coordinate| < 20 (near the equator). If the best is more than 2 000 km
  off 5 000 km, the latitude condition is dropped.

### 3.6 Initial state of every airline [H]

Applied to all 4 airlines unless noted.

* **Planes** (seeded RNG 1 for the names): default = Boeing 737-400 (type id 100) + Boeing 757-300
  (105). Mission 3 (Diff 2): 737-400 + Tu-154 B (114). Mission 4 (Diff 3): 737-400 + 747-400 (104).
  New planes: condition 100, build year `2002 + Date/365`, a random unused name, attendants scaled by
  the fleet average. The purchase price is booked, but the starting money is assigned afterwards, so
  it is effectively free. All starting planes are flagged **"sponsored"** (state-subsidised, hard to
  sell; `planes.md`). Add-on 1: condition/worst/target condition = 60. Add-on 7: 35.
* **Crew:** humans (owner ≠ 1) get exactly as many pilots and attendants as the starting planes
  require. They are drawn at random (seed `StartTime + playerIndex`) from the reserve pool of
  `piloten.csv` + `names.csv`. Computers have no worker records (they simulate crew). Advisors
  (personnel, contracts, planes) are guaranteed to exist in the pool. Then workers are mapped to
  planes and personal quality is computed.
* **Gates:** each airline rents one gate (number = player index, rent 1 000 per month) of 30 slots.
* **Branch:** the home city branch at rank 1, rent = the city's office rent.
* **Starting route** (Diff > 3 and not add-on 2; this includes base 5, 6, add-ons except 2, and ATFS;
  **not** the free game, whose value is −1): airline c rents the (c+1)-th route that touches the home
  airport, scanning the route table from the last entry backwards. The intended "level 1 routes
  first" preference is ineffective. QUIRK [H]. Renting a route rents both directions, at the table
  rent. With the travel holding flag, the route's foreign end also becomes a branch (rank = 1 +
  number of other airlines already there, rent = city office rent).
* **Second branch** (same missions, travel holding flag): find for each airline c, in order, the
  nearest city farther than 1 000 km from home that no earlier airline was assigned. That gives four
  distinct cities. Each airline then rents the first of these four, starting from its own index and
  wrapping, where it has no branch yet.
* **Starting contracts:** with the travel holding flag and not add-on 9: 2 contracts generated "for
  the beginning". Single-player seed is wall clock (non-deterministic). [`contracts.md`]
* **Image** 0 (add-on 6: 300). Kerosene tank 100 (empty), normal fuel kind. ATFS 9/10 computers:
  tank 5 000, open.
* **Shares:** 10 000 issued, max 20 000. Own holding 80 % (8 000), price history all 100, dividend
  10 %, trusted dividend 10 %. [`finance.md`]
* **Sympathy** of every airline towards every other: 50. Towards the next player index
  ((c+1) mod 4): 60. Computers "cooperate" with other computers (flag), not with humans.
* **Mechanic level** 2 (normal mechanic; costs `gRepairPrice` = 1 200 / 18 000 / 48 000 / 150 000 per
  month for levels 0–3). [`planes.md`]
* **Strike:** `DaysWithoutStrike = 20`. Everything else 0.
* Inventory 6 empty slots. Welcome letter to humans (`Lett` 1500–1502). Humans get 20 filofax blocks.
* Mission counters (passengers, contracts, profit, freight, miles, service, order flights, rocket
  bits) = 0. Statistics zeroed (40 series).
* Each airline is marked as having "visited" the world-phone and the competitor phone book.
* Laptop: none. Speeds: humans 0, computers 3.

Global initial state:

* `Date = 0`, `Time = 9:00`, clock frozen. Kerosene 500, with a fabricated 10-day history (last =
  508, the earlier ones by the daily formula backwards).
* Airport layout: every hall segment at expansion level 1.
* **Bank waste-bin money** exists with probability 24 % (`Rand(100) > 75`, seed `StartTime`).
* Slime reset, security office available.
* Auction board: `Randomize(2)`. Slip boards and foreign lists filled (seeds as at midnight; single
  player additionally reseeds the three main boards from the wall clock). Newspapers loaded. Museum:
  3 used planes.
* Robot initialisation (`Players.RobotInit`).

**Scripted mission events:**

* **ATFS 6 (crisis management):** every midnight, an RNG seeded `Date + int(localPlayerMoney)` runs
  over each computer/human other than the local player that is not out, has `ArabHints < 90`, and has
  no sabotage pending. It picks a random victim (any of the 4). If the victim is valid (not out, not
  self, has planes), it picks one of the three sabotage families at random and a random kind:
  flight-time 1..4, overnight 1..4, or "next day" 1..5. These are stored as "foreign" sabotage (no
  hints for the culprit) on a random plane of the victim. Kinds the victim's security office
  protects against are cancelled. A laptop virus needs the victim to own a laptop. [`sabotage.md`]
* **ATFS 9 (fuel crisis):** the kerosene formula of §6.5 every day.
* **ATFS 10 (tycoon):** kerosene crisis on days 3–10 and 35–55. Museum offers no planes on days
  40–50. Day 18: share price ×0.2. Days 20 and 45: humans' staff happiness −45. Day 35: every plane's
  condition −40 (floor 0) and target condition ≤ condition + 10. Day 40: one random plane is removed
  from every airline. Day 55: image −200 (floor −1000).

### 3.7 Mission feature matrix (`RobotUse`) [H]

One flag per feature and level. Most flags steer the computer AI (see `ai.md`). Some (marked †)
change the world for everyone. Level index: base missions 0–5, **F** = free game, add-on 1–10,
ATFS 1–10. X = on.

| Feature | Base 0 1 2 3 4 5 | F | Add-on 1..10 | ATFS 1..10 |
|---|---|---|---|---|
| SABOTAGE | - X X X X X | X | X X X X X X X X X X | - X X X X X X X X X |
| FRACHT (freight) | - - - - - - | X | - X X X X X X X X X | X X X X X X X X X X |
| WERBUNG (advertising) | - - - X X X | X* | X X X X X X X X X X | - X X - X - X X X X |
| AUFSICHT (notice board) | - - - - X X | X* | - - - - - - - - - - | - - - - - - - - - - |
| NASA | - - - - - X | - | - - - - - - - - - X | - - - - - - - - - - |
| NEVERGIVEROUTEUP | - - - - X - | - | - - - - - - - - - - | - - - - - - - - - - |
| ROUTES | all off | | | |
| SUGGESTROUTES | - - - X - - | - | - - - - - - - - - - | - - - - - - - - - - |
| FORCEROUTES | - - - - - - | - | - - - - - X - - - - | - - - - - - - - - - |
| HARDWERBUNG | - - - - X X | - | - - - - - - X X X X | - - - - - - - - - - |
| SELLSHARES | X X X X - - | - | X X X - - - - - - - | X - - - - - - - - - |
| BUYOWNSHARES | - - - - X X | - | - - - - - - - X X X | - - - - - - - - - - |
| REBUYSHARES | - - - - - X | - | - - - - - - - - X X | - - - - - - - - - - |
| TUTORIALLIMIT | X - - - - - | - | - - - - - - - - - - | - - - - - - - - - - |
| ROUTEMISSION | - - - X - - | - | - - - - - - - - - - | - - - - - - - - - - |
| MUCHWERBUNG | - - - - X - | - | - - - - - - - - - - | - - - - - - - - - - |
| BONUS_X2 | - - X X X - | - | X X X - - - - - - - | - - - - - - - - - - |
| BONUS_X4 | - - - - - X | - | - - - X - - X - - - | - - - - - - - - - - |
| BONUS_X8 | - - - - - - | X | - - - - X X - X X X | - - - - - - - - - - |
| ROUTEBOX † | - X X X X X | X | X X X X X X X X X X | X X X - X - X X X X |
| ABROAD | - - X X X X | X | X X X X X X X X X X | X X X X X X X X X X |
| MUCH_SABOTAGE | - - - X X X | X* | X X X X X X X X X X | X X X X X X X X X X |
| PAYBACK_CREDIT | - | - | X - - - - - - - - - | - |
| MUCH_FRACHT | - | - | - X - - - - - - - - | - |
| FREE_FRACHT, RUN_FRACHT | - | - | - - X - - - - - - - | - |
| LUXERY | - | - | - - - - X - - - - - | - X - - - - - - - - |
| TRAVELHOLDING † | X X X X X X | X | X X X X X - X X X X | X X X X X X X X X X |
| IMAGEBONUS | - | - | - - - - - X - - - - | - |
| GOODPLANES | - | - | - - - - - X - - - - | - X - - - - - - - - |
| REPAIRPLANES | - | - | - - - - - - X - - - | - |
| HIGHSHAREPRICE | - | - | - - - - - - - X - - | X X X X X X X X X X |
| WORKQUICK | - | - | - - - X X X X X X X | X X X X X X X X X X |
| GROSSESKONTO | - | - | - | X - - - - - - - - - |
| EMITMUCHSHARES | - | - | - | X - - - X - - X - - |
| WORKVERYQUICK | - | - | - | - - - - - X X X X X |
| ALLRUN | - | - | - | - - - - - - - - - X |
| DONTBUYANYSHARES | - | - | - | X - - - - - - - - - |
| MAXKREDIT | - | - | - | X X X - - - - - X X |
| MAX4PLANES | - | - | - | - - X - X - - X - - |
| MAX5PLANES | - | - | - | - - - X - X - - - - |
| MAX10PLANES | - | - | - | - X - - - - - - - - |
| NOCHITCHAT | - | - | - | - - X - - - - X X X |
| OFTENMECH | - | - | - | - X - - X - - X X X |
| SHORTFLIGHTS | - | - | - | - - X - - - - - - - |
| EXTREME_SABOTAGE | - | - | - | - - - X - X - - - - |
| SECURTY_OFFICE | - - - - - - | X | - | - - - X - X - X - X |
| MAKLER (broker) | X X X X X X | X* | X X X X X X X X X X | X X X - X - X X X X |
| PETROLAIR | X X X X X X | X* | X X X X X X X X X X | X X X X X - X X - - |
| MAX20PERCENT | X X X X X X | X* | X X X X X X X X X X | X X X X X - X X X X |
| TANKS | - | - | - | - - - - - - - - X X |
| DESIGNER | - - - - - - | X | - | - - - X X - X X - - |
| DESIGNER_BUY | - | - | - | - - - - X - - X - - |

`X*` = enabled in Deluxe but not in the original 1.0 release. "-" alone in a cell means all off for
that campaign. † TRAVELHOLDING = the travel agency and last-minute counter exist; starting contracts
and branches; travel-agency auctions. ROUTEBOX = the route box on the wall exists. [M for the world
effects; H for the matrix]

---

## 4. Global constants and RNG

### 4.1 Constants (simulation-relevant) [H]

| Name | Value | Meaning |
|---|---|---|
| CUSTOMERS_PER_PERSON | 15 | one on-screen passenger = 15 booked passengers |
| CUSTOMERS_PER_PERSONB | 45 | used once 100 passengers have been shown for a flight |
| DEBT_LIMIT | −1 000 000 | max overdraft allowed by shops |
| DEBT_WARNLIMIT1/2/3 | −500 000 / −1 000 000 / −3 000 000 | advisor warnings |
| DEBT_GAMEOVER | −5 000 000 | bankruptcy at morning review |
| image range | −1000 .. +1000 | clamp everywhere; ≤ −990 → game over (check is `< −990`) |
| MAX_CITIES | 80 | |
| MAX_ROUTES | 370 | |
| MAX_PLANETYPES | 30 | |
| MAX_WORKERS | 350 | |
| MAX_CLANS | 90 | |
| MOODCOUNT_START | 60 | |
| DAYS_WITHOUT_LAPTOP | 7 | |
| STAT_ANZ | 40 | statistic series per airline; each keeps 31 days + 12 "months" |
| gRepairPrice | 1 200, 18 000, 48 000, 150 000 | monthly mechanic cost per level 0..3 (charged per plane per day /30) |
| gWerbePrice | 18 values: 1 000, 10 000, 50 000, 100 000, 250 000, 2 500 000 / 2 000, 8 000, 22 000, 56 000, 120 000, 1 400 000 / 5 000, 93 000, 290 000, 870 000, 4 925 000, 60 000 000 | advertising price table (3 × 6; see `advertising.md`) |
| Mission targets | TARGET_PASSENGERS 2 500; GEWINN 5 000 000; FLAGS 10; IMAGE 750; FRACHT 1 000; DAYS 21; MILESDAYS 30; SERVICE 150; VALUEDAYS 21; SHARES 220; NUM_UHRIG 200; (unused: FRACHT1 2 500, FRACHT2 2 500, FLUGKILOMETER 2 500, DAYSTRAVEL 25, AKTIENKURS 200) | |
| ATFS targets | PASSENGERS 5 000 (unused); KONTO 15 000 000; ZUSTAND 90; PASSAVG 500; DAYSSABO 15; PLANESIZE 600; KURS 200; MEINANTEIL 20 %; DAYS7 30; VERBRAUCH 500; NDAYS9 45; NDAYS10 60 | |
| Opening hours | see §1.5 | |
| Rocket bits | base 1, tower 2, arm 4 (part one = 7); airframe 8, wings 16, capsule 32, side thrusters 64, main engine 128, tail 256, cockpit 512; all = 1023 | `space.md` |
| Station bits | rings A/B/C 1/2/4 (part one 7); oxygen 8, comm 16, main 32, living 64, office 128 (part two 255); power 256, solar 512; all 1023 | `space.md` |
| Room ids | airport 1, options 2, globe 6, laptop 7, evening 8, office A..D 10/20/30/40, personnel A..D 11/21/31/41, shops 80–83, café 84, route box 85, bank 100, kiosk 101, museum 102, last minute 103, kerosene seller 104, broker 105, manager 107, notice board 108, advertising 109, workshop 110, travel agency 111, sabotage 112, WC 113/114, NASA 115, island 116, world phone 117, Rushmore 118, statistics 119, plane props 120, freight 121, space 122, security 123, editor 124, designer 125; flags entering 1024, leaving 2048 | `rooms-and-ui-*.md` |
| Statistic ids | 0 money, 1 credit, 2 planes, 3 passengers, 4 home passengers, 5 share price, 6 flights, 7 contracts, 8 last-minute contracts, 9 penalties, 10 company value, 11/12 satisfied/unsatisfied passengers, 13 satisfied staff, 14 delays, 15 accidents suffered, 16 sabotage ordered, 17 employees, 18 mission %, 19 maintenance, 20 branches, 21 routes, 22 salaries, 23 awareness, 25–29 shares, 30–33 income groups, 34–38 expense groups, 39 freight contracts | `statistics.md` |

### 4.2 Random number generation [H]

**Generator (`TEAKRAND`)**: a 32-bit linear congruential generator.

* State `v` (uint32), plus the original seed (for reset).
* Step: `v = v × 214013 + 2531011 (mod 2³²)`. Output = `v >> 16`, a 16-bit value 0..65535.
* `Rand(n)` = output mod n (n > 0). `Rand(min, max)` = output mod (max − min + 1) + min.
* Seeding sets state = seed.
* The original asm multiplies by **214 013**, even though its header comment says 7381. The SDL port's
  portable C fallback uses 7381 and therefore produces different sequences from the original.
  **OpenAT must use 214 013.** [H] (asm arithmetic checked by hand)
* State (seed and value) is serialised wherever a generator is a member of a saved object.

**Streams and seeding policy** (determinism was designed for network lockstep):

| Stream | Seeding |
|---|---|
| `PersonRandCreate`, `PersonRandMisc` (global; pedestrians) | 0 at setup; `Date` at setup and at midnight; **`minute × 333 + hour × 7` every game minute** |
| `HeadlineRand` | 0 (newspapers mostly use a local generator instead) |
| Last minute / travel agency / freight boards | `Date + 1` / `Date + 2` / `Date` at midnight and at setup (single player: the setup values are overwritten by the wall clock) |
| Foreign contracts / foreign freight per city | `Date + cityIndex + 3` at midnight and setup |
| Per airline `PlayerWalkRandom` | `Date + PlayerNum` each midnight |
| Per airline `PlayerExtraRandom` | 0 each midnight |
| Per airline contract list RNG | wall clock at setup (single player); saved |
| **Local, throw-away generators** created on the spot | strike start `Date + hour`; plane problem `Date + hour + minute`; in-flight incident checks `Date + hour + minute` (**recreated per plane, so every plane in the same check sees the same numbers**, QUIRK); stagger of landing/departure positions `Date + hour`; share price `Date + minute + hour` (recreated per airline); kerosene `Date`; museum plane i `Date + i`; plane repair `Date`; headlines and comparisons `Date + StartTime`; job market `Date + StartTime`; notice board `Date + StartTime`; ATFS 6 sabotage `Date + int(money)`; starting crew `StartTime + c`; bank-bin money `StartTime`; announcements `Date + StartTime + hour`; starting plane names 1 |
| C runtime `rand()` (not reproducible) | random start day, flavour letters, boss dialogue variants, evening sound effects, fly-by sounds, unique game id |

Implementation advice: provide one `TeakRand` value type with exactly this LCG, and keep the same
named streams and the same seeding points. The "every minute reseed" makes pedestrian behaviour a
pure function of the clock. The local generators make most economic events a pure function of
`(Date, hour, minute, StartTime)`. That is a cheap way to get the original outcomes for identical
inputs.

---

## 5. Save game: persisted state (for the data model)

### 5.1 Files [H]

* Single player `Game<n>.dat`, network `Net<n>.dat`, in the savegame folder. Slot **11** is the
  autosave (written at 17:00 and on call-it-a-day if the option is on). Version 1, sub-version 107.
* Header: save name; version; an info string (`Misc` 1150 / 1151 template filled with mission
  number, weekday name, day, month, airline name); unique game id; host flag; number of humans; local
  player index; the 4 owner codes; the "compatible routes" flag; session name; allow-cheating flag;
  number of cities. The load menu reads only the header.
* Highscores and options are **not** in saves (§5.4, §5.5).

### 5.2 Body, in order [H]

1. **SIM**: pedestrians; **the 4 PLAYER records**; used-plane offers (museum); pedestrian queue;
   headlines (current + queued overrides + chain state); kerosene price + 10-day history; local player
   index; camera focus person; sabotage acts awaiting the briefing; ticks played / restore count /
   exe-changed.
   Flags: cheated session, airport expansion pending, game state, difficulty, tutorial running,
   `GameSpeed` (30), paused, clock frozen, dialog-opening flags, `DayState`, call-it-a-day, tutorial
   step, laptop sold-to, bank-bin money, workshop slimed, telescope used, hidden player, mission
   cities, daily item flags (7), freight refill counter, earthquake city, second plane-props use,
   reference-plane editor settings, host flag (ignored on load), security-office outage days.
   Statistics-screen UI state (selected group, days, player mask, row selections). Max difficulty
   (merged with the stored max on load). **Date, Time, Month, MonthDay, quit countdown, TickerTime,
   TimeSlice, StartWeekday, Weekday, StartTime.** Refill counters (travel agency, last minute,
   museum). Airport expansion levels (10 hall segments). Takeover state (victim, taker, mode). **Home
   airport id**, room occupancy.
2. **AIRPORT** (built layout, doors, clip/area markers).
3. People-on-screen count (cosmetic).
4. **Workers** (all pilots/attendants/advisors with employer, salary, happiness, talent, plane).
5. **Talkers** (NPC lock/busy state).
6. **Notice board** (auction slips and bids) and **routes table** (incl. demand).
7. **Last-minute** and **travel-agency** contract lists (with their RNG), unique game id 2,
   **freight board**.
8. Per city: **foreign contracts** + refill counter, **foreign freight** + refill counter.
9. Mouse-wait counter (reset on load).

**PLAYER record** (per airline): identity (index, names, airline, abbreviation, owner, logo, out
flag); money, 20-day money history, credit, image; kerosene (bad-fuel flag, kind, tank size, open,
contents, purchase price); own speed; sabotage state (three pending families with victims/planes/
active flag, hint counter); mechanic level/trust/anger; globe rotation/oiled; reference plane;
sickness/toilet/stun/office state/smoking/stench/laptop virus; brochure/phone/press-release flags;
strike state (hours, notified, end countdown, end type, days without strike, planned); days without
sabotage; hidden bonus; alcohol/caffeine/drunk; freight contracts; kiosk trust; security flags;
mission counters (freight, relief freight, miles, service points, order flights total/today/today2);
stuck flag; robot wait-until times; **its two RNGs**; security needed; NPC trust values (drinker,
personnel lady, spider, advertising, duty free); route/plane display filters; **planes** (with
flight plans), **contracts**, **gates**, items, **branches**, **rented routes**, cursor, money
history lines (100) + historic money, filofax blocks, message queue, letters, laptop battery and
quality, called-cities flags; **balance sheets** today and yesterday; interest rates; shares
(max, issued, dividend, trusted dividend, holdings in each airline and their values, 10 price
history values); flown routes, passengers, contracts done, profit counter, connection count,
rocket bits (current and last); **40 statistic series**; camera/view/window state; the room stack
(10 entries) and room timers; position, buttons, extra pilots/attendants; call-it-a-day; flights
today; route page; stand count; NASA talked; walking targets; visited rooms; walk speed; robot state
(action queue, timers per room, out of gates, cooperation, sympathy, route mode, saving for plane or
rocket, big-plane intent, player-dialog state, competitor call cooldown, boredom).

### 5.3 What is not persisted [H]

Global pedestrian RNGs (reseeded every minute anyway), `HeadlineRand`, airport video overlays,
network ready flags (kept from the current session), `TicksSinceLoad`. On load: if the clock is
before 18:00, everyone's call-it-a-day is cleared. The restore counter +1. Older saves are upgraded
(cities added in Deluxe are inserted; route table extended and rented routes remapped by id; the
check-in level is repaired from the gate count). On load, every player's room-stack top is re-entered.

### 5.4 Highscores [H]

Six entries (name, session id, 64-bit score). The score is the human's **company value**, updated
every evening for each human that is not in a cheated session. The same session updates its own
entry only if the score is higher. A new session is inserted in rank order. The file
`misc/xmlmap.fla` stores the score XOR-obfuscated; that is irrelevant for us (store plainly).

### 5.5 Options / progress (registry in the original) [H]

Sound and music volumes; planes/passengers shown; thought bubbles; transparency; shadows; fades;
short briefing (default on); advisor/girl on; fax on; autosave (default on); round numbers;
**random start day (default on)**; speech/bubbles; home airport; last player; last mission per
campaign; **highest unlocked mission per campaign** (base 0..5, add-on 11..20, ATFS 41..50; the
original obfuscates the values through key tables, so store plainly); player names; key-hint flags.

---

## 6. Events and random processes

There is no generic event queue. "Events" are hard-wired checks in the tick/hour/day code, plus
player-triggered sabotage. Probabilities below are per check.

### 6.1 Strikes [H]

* **Planning** (once per day at 10:00, **local human only**). Let `A` = the average happiness of the
  human's staff, minus 10 if any employee has negative happiness. A strike is planned if
  (`A < 20` and `DaysWithoutStrike > 7`) or (`A < 0` and `DaysWithoutStrike > 3`). Sabotage
  ("provoke a strike") also plans one, for any airline.
* **Start** (any hour 7:00–17:00, not after call-it-a-day): only if the airline has a plane standing
  at a city, or one landing this hour or within the next 2 hours. Duration:
  * 2 hours if `DaysWithoutStrike > 10`;
  * else 72 hours if the average staff happiness < −10;
  * else 4 + Rand(20) hours (4..23), seed `Date + hour`.

  Then `DaysWithoutStrike = 0`. A newspaper override headline (`Misc` 2090, paper 1, picture
  "STREIK", priority 25, +10 if it is the local player).
* **While running**: every hourly departure check delays the flight (§2.4), and `StrikeHours` −1
  every hour (day and night).
* **End by waiting** (counter reaches 0, human): end type 3, **all of that airline's workers'
  happiness −10**, phone notification.
* **End by negotiation**: the personnel dialogue sets an end countdown (in game minutes) and an end
  type. See `personnel.md`.
* Notifications: strike-start fax `MENU_SABOTAGEFAX` type 8 plus letter `Lett` 508 with the hours.
  Strike-end call from the personnel manager (dialogue 1000 + end type).

### 6.2 Plane technical problems (breakdowns) [H]

* Once per game hour 6:00–16:59, not after call-it-a-day, **local human only**, only if no plane
  currently has a problem and the fleet has ≥ 2 planes. A local RNG seeded `Date + hour + minute`
  (minute is 0) is shared across planes.
* Candidate planes: airborne, landing this hour today. The first candidate (scanning from the highest
  index) that passes `Rand(10) == 0` and `Rand(100) > condition` and `condition < 90` gets the
  problem. Probability ≈ 0.1 × (99 − condition)/100.
* `Problem` hours = 4 + Rand(101 − condition)/3 + 15 if Rand(4) == 0.
  * Destination is home: `Problem = max(4, Problem − 15)`. Letter `Lett` 506 (fax type 7).
  * Elsewhere and `Problem > 20`: letter `Lett` 507 (fax type 6), **and a spare-parts contract** is
    created: home → that city, 0 passengers, no premium/penalty, valid from today until
    `Date + (Problem − (24 − hour)) / 24`. Flying it reduces the problem by 15 (§2.4).
* A plane with `Problem > 0` cannot depart (hourly delay). `Problem` −1 every hour.
* Fax popup if the player is in a room without a menu and the fax option is on (`Advi` 2308);
  otherwise an advisor line `Advi` 2309. Speed is forced to 0.

### 6.3 In-flight incidents from poor condition [H]

Every 4 game minutes, per airborne plane past its first flight hour, all airlines:

* If condition < 80 and `Rand(5000) > condition + 4920`: probability **(79 − condition)/5000** per
  check. That is about 15 checks per flight hour, so roughly 1.5 % per flight hour at condition 30.
* Effective only for passenger flights (route or contract) with passengers > 0: image −2 (clamped),
  "image got worse" flag (the boss comments), route image ×0.8, a breakdown record of type
  `Rand(9)` (0..8) is added to the plane (`Advi` 2360 + type), **share price ×0.7**, trusted dividend
  −1 if > 4.
* No plane is lost; there are no crashes. [H]

### 6.4 Seasonal incidents (weather) [H]

Same 4-minute check, after 6.3, same RNG continuing: `Rand(5000) > 4980` → **19/5000** per check
(≈ 5.6 % per flight hour). The plane is exposed only if it lacks the matching upgrade:

| Season | Exposed if |
|---|---|
| Spring, autumn, winter | electronics level ≠ 2 |
| Summer | safety level ≠ 2 |

Effect: **−75 000** money (reason 3501), breakdown record type 10 + season, advisor `Advi` 2370 +
season. Passengers are not required.

### 6.5 Kerosene price [H]

Daily at midnight, with RNG seeded `Date` (the new date):

`price += Rand(21) − 10 + [Rand(20) < 3] × (Rand(41) − 20) + int(sin((Date + Rand(6)) / 3) × 20) +
int(sin(Date × 1.7) × 20)`, then clamp to **300..700**. History: 10 values, newest last. Start 500.

Crisis mode (ATFS 9 always; ATFS 10 on days 3–10 and 35–55), applied before the final clamp: if
`Rand(20) > 2`, price += price/4; else, if the 3 previous history values are all > 550, price −=
price/2. Clamp to 300..700. Then apply 20 extra random steps of `Rand(21) − 10 + [Rand(20) < 3] ×
(Rand(41) − 20)`. Clamp again. The price is "per litre" (`kerosene.md`).

Open: the order in which the original evaluated the several `Rand` calls inside one expression
(C++ leaves it unspecified; MSVC most likely evaluated left to right). [M]

### 6.6 Share prices (hourly drift) [H]

Every game hour, per airline:

1. If trusted dividend > dividend: trusted = (trusted + dividend)/2.
2. `p = (29p + 10 × trustedDividend)/30`. With the default 10 % dividend it converges to 100.
3. `p += sin((Date × 24 + hour)/50) × p/180`.
4. `p += sin((Date × 24 + 100 + hour)/(c + 30)) × p/150` (c = player index).
5. `p += sin(Rand(1000)) × p/50` (seed `Date + minute + hour`, i.e. one random number per hour,
   shared by all airlines).
6. Floor 0.

Plus the event shocks: incidents ×0.7, sabotage ×0.95..×0.75, daily company-value drop ×0.97 /
×0.97 / ×0.95, ATFS 10 day 18 ×0.2. Full model: `finance.md`.

### 6.7 Sabotage (player-ordered) [H]

Ordered at the sabotage room (the kerosene seller's back room). Three families:

* "flight-time" (`ArabMode` 1..5): executed on the hour when the target plane takes off or is in the
  air;
* "overnight" (`ArabMode2` 1..4);
* "next-day" (`ArabMode3` 1..6).

The last two are executed when leaving the morning briefing. All acts are reported in the next
briefing. The culprit's `ArabHints` grows. At ≥ 100 hints the culprit is exposed and fined
`hints × 10 000`, which goes to the victim. Effects per type, protections by the security office,
fax/letter ids: `sabotage.md`. Mission ATFS 6 injects foreign sabotage (§3.6).

### 6.8 Newspapers [H]

Three papers. Each day, a random headline or a continuing "story chain" from `stdpaper.csv` (a new
chain starts with probability 1/20 per eligible row). Placeholders (random city, random number,
airline name). Queued "override" headlines from events (sabotage, strike, comparisons, rocket
progress) with picture and priority. Comparisons on days > 8 with `Date mod 4 == 2`: one of 8
categories chosen at random (most planes > 10, best image > 100, most routes > 30, most money
> 25 000 000, best pilot, best attendant, most passengers, worst plane < 50 %). `newspapers.md`.

### 6.9 Market / world refreshes [H]

* **Museum** (used planes): 3 offers each day, plane i from seed `Date + i`. Random existing type,
  a provisional condition 11..90 from `Rand(80) + 11` (overwritten below), build year 1990 − Rand(1990 − first
  year) for types before 1990, else 1996 − Rand(1996 − first year). Condition =
  `(year − 1950) + 25 + Rand(40) − 20`; values < 20 or > 200 → 20, then cap 100. A sold slot refills
  after `TickMuseumRefill/20` (≈ every 100 game minutes). `planes.md`.
* Slip boards refill during the day by the 5-minute counters (§2.4). `contracts.md`.
* Auction board resolution and new slips nightly (§2.6). `auctions.md`.
* Route demand nightly (§2.6).

### 6.10 Staff (daily) [H]

For every employee of a **human** airline with image < 500: −1 happiness, repeated 2 times if image
< 0 and 3 times if < −500. Each repetition costs an extra −20 if the salary is below the original
salary. Below −100 the employee quits (advisor message). Then the job market: all jobless → reserve.
10 + Rand(10) random reserve workers become applicants at their original salary, plus 5 + Rand(2)
attendants. Seed `Date + StartTime`. `personnel.md`.

### 6.11 Other day/time based happenings [H]

* **Route revocation** after 20 days below 10 % utilisation (warnings at 10 and 15). `routes.md`.
* **Contract penalties** the morning after expiry. `contracts.md`.
* **Random flavour letters** (§2.7 step 20).
* **Workshop slime** (sabotage gag) resets every 6 days. **Security office outage**: 3 days after the
  pliers sabotage.
* **Airport expansion**: requested in the manager dialogue (`Boss` 4120) or by a cheat; executed at
  the next midnight. `airport.md`.
* **Advisor chatter**: random advisor messages at certain clock values while playing.
* **Shoppers** (9:00–12:00 and 14:00–16:00, < 70 shoppers, < 100 persons) and **flight passengers**
  (spawned in the hour before departure from home, 2 attempts every 32 ticks, each with probability
  1/6, or always after :20). `airport-people.md`.

---

## 7. Subsystem map (for other spec writers)

| Subsystem | What it does | Original files |
|---|---|---|
| Simulation core | world object, clock, day change, setup, save/load, options, highscores | `Sim.cpp`, `class.h (SIM)`, `Global.cpp`, `defines.h` |
| App loop and speed | real time → ticks, speed levels, room window lifecycle, evening/morning transitions | `Takeoff.cpp`, `Mainfrm.cpp` |
| Main frame and input | painting, fades, pause, keyboard shortcuts, cheat codes | `Gameframe.cpp`, `Blenden.Cpp`, `Cblenden.cpp`, `Colorfx.cpp` |
| Airline (player) | money, credit, image, mission counters, daily bookkeeping, win test, statistics, save | `Player.cpp`, `class.h (PLAYER, PLAYERS)` |
| Computer AI | robot action queue, decisions per room, route planning | `Player.cpp (Robot*)`, `RobotUse` table |
| Planes | types table, plane instances, condition and repair, used planes, plane animation on the apron | `Planetyp.cpp`, `Museum.cpp`, `Makler.cpp`, `Werkstat.cpp`, `PlanProp.cpp` |
| Flight plans and booking | flight plan entries, passenger calculation, revenue/cost booking, gates | `Schedule.cpp`, `Planer.cpp`, `Laptop.cpp`, `Globe.cpp`, `Block.cpp` |
| Contracts | passenger contracts from travel agency, last minute, abroad, manager order flights | `Auftrag.cpp`, `reise.cpp`, `Lastmin.cpp`, `World.cpp` |
| Freight | freight contracts (add-on) | `Fracht.cpp` |
| Routes | route table, rented routes, demand, utilisation | `Route.Cpp`, `RouteBox.cpp` |
| Cities and branches | city table, distances, branch offices | `City.Cpp` |
| Finance | bank, credit, shares, dividends, balance sheet, money history, takeovers | `Bank.cpp`, `Player.cpp`, `Dialog.cpp (bank)` |
| Personnel | workers, applicants, happiness, salaries, strike negotiation | `Personal.cpp` |
| Kerosene | fuel price, tanks, bad fuel | `ArabAir.cpp`, `Sim.cpp` |
| Sabotage | sabotage orders and execution, hints, fines | `sabotage.cpp`, `Sim.cpp`, `Aufsicht.cpp` |
| Security office | protections against sabotage, costs | `Security.cpp`, `SecurityManager.cpp` |
| Advertising | campaigns, image | `Werbung.cpp` |
| Auctions | manager's notice board (gates, branches, routes) | `Tafel.cpp` |
| Manager / briefing / missions | morning briefing, mission reports, game over | `Aufsicht.cpp`, `Dialog.cpp`, `Stdraum.cpp` |
| Evening | evening screen and statistics trigger | `Abend.cpp` |
| Newspapers | headlines and kiosk | `Misc.cpp (HEADLINES)`, `Kiosk.cpp` |
| Messages and letters | advisor queue, in-tray letters, office | `Misc.cpp (CMessages)`, `Buero.Cpp` |
| Statistics | statistic series and statistics screen | `Statistk.cpp`, `Player.cpp (UpdateStatistik)` |
| Airport and pedestrians | hall layout, bricks, runes, doors, persons, clans, queues, moods | `AirportView.cpp`, `Brick.cpp`, `Person.cpp`, `Talker.cpp` |
| Items and shops | inventory, duty free, café, kiosk trust | `DutyFree.cpp`, `Ricks.cpp`, `Stdraum.cpp` |
| Space missions | NASA shop, rocket island, space station, Rushmore | `Nasa.cpp`, `Insel.cpp`, `WeltAll.cpp`, `Rushmore.cpp` |
| Plane designer | custom plane editor and purchase (Evolution/ATFS) | `Designer.cpp`, `Editor.cpp` |
| Rooms base and UI | base room class, menus, dialogs, status bar, tooltips, tables | `Stdraum.cpp`, `Dialog.cpp`, `Tips.Cpp`, `DataTabl.Cpp`, `Klacker.cpp` |
| Front end | title, new game, options, credits, intro/outro | `Titlepopup.cpp`, `NewgamePopup.cpp`, `Options.cpp`, `Credits.cpp`, `Intro.cpp`, `Outro.cpp` |
| Network | messages, sync, lobby | `AtNet.cpp`, `PNet.cpp`, `NewgamePopup.cpp` |
| Media | sound, voices, speech synthesis, Smacker video, bitmaps | `Sound.cpp`, `Synthese.cpp`, `Smackprs.cpp`, `DeltaVid.cpp`, `SBBM.cpp`, `HLine.cpp`, `Fx2.cpp` |
| Init and protection | paths, data load, copy protection, system checks | `Init.cpp`, `Protect.cpp`, `Checkup.cpp` |
| Editors (dev tools) | airport hall editor | `Askbrick.cpp`, `HalldiskMenu.cpp` |

---

## 8. Open questions

1. **Kerosene / multi-`Rand` expressions:** the order in which the original evaluated multiple
   `Rand()` calls inside one expression (§6.5, §6.2 problem hours). Assume left to right; verify by
   comparing kerosene histories with a real game started at a known random start day. [M]
2. **Computers after call-it-a-day:** robots keep pumping at ×600 until 18:00. Do they physically walk
   (their figures move via `Persons.DoOneStep`, which still runs) or warp? It affects how much they
   get done. Verify in game. [M]
3. **Strike planning only for the local human** and **plane problems only for the local human**, and
   **no delays for computer flights**. Confirmed in code. Decide whether OpenAT keeps this asymmetry
   (it is original balance). [H]
4. **Interest quirk** (§2.7): reproduce or fix? Affects `finance.md`.
5. **Computer starting bonus absolute value** (§3.3): intended or bug? It strongly strengthens
   computers in missions 1–3. Reproduce by default.
6. **Travel agency on Sunday:** the entry rule and the door have no Sunday rule. Only the pedestrian
   target picker avoids it on Sundays. Treat it as open. [H]
7. **Advertising agency availability before mission 4:** the closing rule only applies for Diff ≥ 3
   or free game. Does the room exist at all in earlier missions? (ROBOT_USE_WERBUNG is off there.)
   [L]
8. **`TickMuseumRefill/20` refill rate** and the 5-minute counters: verify the exact refill amounts
   in `contracts.md`.
9. **Season boundaries** use local time of `StartTime + Date × 86400`. With the default random start
   day the calendar is in 1970; time zone and DST could shift the day by one near midnight. OpenAT
   should use UTC dates. [M]
10. **Space bar warp ×150** vs the manual's "20 times". The code says 150. [H] Keep 150.
11. **Network** behaviour is summarised only; a full spec is needed if OpenAT does multiplayer.
