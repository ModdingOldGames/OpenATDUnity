# Rooms & UI 9: Room hosting, entering/leaving rooms, airport view, transitions, effects

Clean-room behaviour spec for OpenAT. Everything below is written in our own words. Coordinates, rects,
constants and file names are facts taken from the original sources. In-game text is referenced only by
resource file + group + id (`Std_Ger.res` / group `Misc` = `TOKEN_MISC`, group `Tuto` = `TOKEN_TUTORIUM`,
group `Tooltip` = `TOKEN_TOOLTIP`; the English install ships the same ids in its localised `.res`).

Sources: `gog_src/` (original) cross-checked against `ref_AirlineTycoon/src/` (SDL port). The two match
in every function this document cites unless noted otherwise.

Confidence key: **H** = read directly in code, unambiguous. **M** = read in code but the intent or runtime
effect is inferred. **L** = guess or extrapolation.

> **WIDESCREEN** markers flag every place that assumes a 640x480 frame, a 640x440 play area or a
> 320 px half-width. The full list is collected again in section 11.

---

## 1. Time units (needed for everything below)

- `Sim.Time` counts game time within a day: **60000 units = 1 game hour, 1000 units = 1 game minute**
  (`Sim.cpp: SIM::GetHour`, `SIM::GetMinute`). 9:00 is 540000 and 18:00 is 1080000. (H)
- `Sim.Weekday` is 0 = Monday ... 5 = Saturday, 6 = Sunday. It is derived from the real-world start date
  (`Sim.cpp: SIM::ChooseStartup`, the "+6 mod 7" remap of the C weekday). (H)
- One simulation step advances `Sim.Time` by `GameSpeed` units (`Sim.cpp: SIM::DoTimeStep`). How many steps
  run per rendered frame is decided in the main loop (section 7). (H)

---

## 2. Complete ROOM_* constant list

From `defines.h` ("Der Verwendungszweck der Raumnummern"). Kind:
**Real** = a place you walk to through a door or counter in the airport, with its own full-screen window.
**Sub** = a screen opened from inside another room or from the UI, stacked on top of it.
**Mission** = only reachable in some missions. **Marker** = not a window: a walk-net or area marker id
used by the airport hall data. **Flag** = bit flag OR-ed onto a room id.

| Const | Value | Meaning (own words) | English manual name | Kind |
|---|---|---|---|---|
| ROOM_AIRPORT | 1 | The airport hall view itself (`AirportView`) | "the airport" | Real (base of the stack) |
| ROOM_OPTIONS | 2 | In-game options / load / save menu | Options (keys F1/F2/Esc in manual) | Sub (opened by hotkey, pauses the sim) |
| ROOM_GLOBE | 6 | Globe + filofax planner in your office | "Globe (filofax)" | Sub (from Office) |
| ROOM_LAPTOP | 7 | Notebook planner (inventory item) | "Notebook" | Sub (from inventory, from anywhere) |
| ROOM_ABEND | 8 | Evening / closing-time screen and the start of the next day | (evening at home, ch.1 intro) | Special: shown as **TopWin**, not through the stack (see 6.4) |
| ROOM_BURO_A/B/C/D | 10/20/30/40 | Office of player 1..4 | "Office" | Real |
| ROOM_PERSONAL_A/B/C/D | 11/21/31/41 | Personnel office of player 1..4 | "Personnel Office" | Real (also a phone target) |
| ROOM_STAIRS1UP..STAIRS3DOWN | 50..55 | Stair zones, a pseudo-room (`ROOM_STAIRSVON`=50, `ROOM_STAIRSBIS`=55) | stairs | Marker (walking only) |
| ROOM_SHOP1 | 80 | Duty-free shop | "Duty-Free Shop" | Real |
| ROOM_SHOP2..SHOP4 | 81..83 | Decorative shops for passengers | (shops) | Marker (passengers only, no window) |
| ROOM_RICKS | 84 | Café counter | "Rick's Café" | Real (counter, no door) |
| ROOM_ROUTEBOX | 85 | Route-rental board in the hall | "Route management board" (key R) | Real (counter) |
| ROOM_BANK | 100 | Bank | "Bank" | Real (also a phone target) |
| ROOM_KIOSK | 101 | Newspaper stand | "Newspaper Stand" | Real (counter) |
| ROOM_MUSEUM | 102 | Used-plane museum | "Museum" | Real (also a phone target) |
| ROOM_LAST_MINUTE | 103 | Last Minute order counter | "Last Minute counter" | Real (counter) |
| ROOM_ARAB_AIR | 104 | Kerosene / "ominous airline" counter | "Petrol Air counter" | Real (counter; also a phone target) |
| ROOM_MAKLER | 105 | New-plane dealer | "Plane Dealer" | Real (also a phone target) |
| ROOM_AUFSICHT | 107 | Airport manager's office (morning briefing) | "Mr Uhrig (airport management)" | Real (also a phone target) |
| ROOM_TAFEL | 108 | Pin board inside the airport manager's office (branch/route offers) | "pin board ... in Mr Uhrig's office" | Sub (from Aufsicht) |
| ROOM_WERBUNG | 109 | Advertising agency | "Advertising Agency" (interAd) | Real (also a phone target) |
| ROOM_WERKSTATT | 110 | Maintenance workshop | "Workshop" | Real (also a phone target) |
| ROOM_REISEBUERO | 111 | Travel agency order counter | "AirTravel counter" | Real (counter) |
| ROOM_SABOTAGE | 112 | Back room of the Petrol Air counter (sabotage) | (Petrol Air "other services") | Real (secret door) / phone (incoming calls only) |
| ROOM_WC_F / ROOM_WC_M | 113 / 114 | Ladies' / gents' toilets | "a toilet ... beside the cafe" | Marker: opens a menu (`MENU_WC_F/M`), no window |
| ROOM_NASA | 115 | Space equipment shop | "NASA Shop" | Mission (DIFF_FINAL, DIFF_ADDON10) |
| ROOM_INSEL | 116 | Telescope view of the rocket islands | "Telescope" | Mission (DIFF_FINAL) |
| ROOM_WORLD | 117 | Satellite phone call to a branch city | (branch phone call) | Sub / phone-only |
| ROOM_RUSHMORE | 118 | Telescope view (score) | "Telescope" (key S) | Real (default telescope) |
| ROOM_STATISTICS | 119 | Statistics screen | "Statistics" (ch.13) | Sub (status-bar icon, from anywhere) |
| ROOM_PLANEPROPS | 120 | Plane refit / properties screen | (plane equipment, from Office) | Sub (from Office) |
| ROOM_FRACHT | 121 | Cargo counter | "cargo office" (key H) | Real |
| ROOM_WELTALL | 122 | Telescope view of the space station | "Telescope" | Mission (DIFF_ADDON10) |
| ROOM_SECURITY | 123 | Security company | "Safe & Secure Ltd." | Real (hidden entrance) |
| ROOM_EDITOR | 124 | Plane designer editor | (Design Office editor) | Sub (from Designer) |
| ROOM_DESIGNER | 125 | Room in front of the plane editor | "HiTech Designer-Shop" / "Design Office" | Real (only if the hall has it) |
| ROOM_FLIGHTDISPLAY | 140 | Base id; 140..169 and 170..199 are departure-board tooltips above check-in and departure desks | flight displays | Marker (tooltip only) |
| ROOM_REISE_X1..X4 | 200..203 | Passenger spots at the travel agency | | Marker (passengers) |
| ROOM_LASTMIN_X1..X4 | 210..213 | Passenger spots at Last Minute | | Marker (passengers) |
| ROOM_BELT_X1..X10 | 220..229 | Baggage belts | | Marker (passengers/suitcases) |
| ROOM_MONITOR1..6 | 230..235 | Flight monitors for passengers | | Marker |
| ROOM_MONITOR7..9 | 236..238 | Flight monitors for players | | Marker |
| ROOM_PHONE1..4 | 240..243 | Public phones | public phone | Marker (opens the phone menu) |
| ROOM_ELECTRO | 244 | Drinks vending machine | (cola machine) | Marker (gimmick) |
| ROOM_AIRPORT_EXIT | 250 | Airport exit | "go home" | Marker (asks "call it a day?") |
| ROOM_WALL | 254 | Office wall: stop in front of it | | Marker |
| ROOM_ANTIMARKER | 255 | Cancels a marked area (for example the mailbox) | | Marker |
| ROOM_TITLE | 900 | Title / new-game popup context | | Not a room (UI id) |
| ROOM_ENTERING | 1024 | Flag: "player is entering this room" | | Flag |
| ROOM_LEAVING | 2048 | Flag: "player is leaving this room" | | Flag |

Also defined: `WIN_LO`=1, `WIN_RO`=2, `WIN_LU`=4, `WIN_RU`=8, `WIN_O`=3, `WIN_U`=12, `WIN_BIG`=15. These are
leftover split-screen quadrant masks and are **never used** (see 8.6). (H)

---

## 3. Room factory: ROOM_* id -> class -> GLI -> manual name

The factory is the window-creation switch in the main loop (`Takeoff.cpp: CTakeOffApp::GameLoop`, the
"open window for room" block). Every room class takes `(bHandy, PlayerNum)` and derives from `CStdRaum`
(`Stdraum.cpp: CStdRaum::CStdRaum`). `CStdRaum` loads the GLI named in the constructor from `RoomPath`
(`room\`), `Init.cpp`. All the GLIs listed exist in the GOG install under `room\`. (H)

| ROOM id | Class (file) | GLI file(s) (room\) | Main picture id | Manual name | Phone (bHandy) use |
|---|---|---|---|---|---|
| BURO_A..D | `CBuero` (Buero.Cpp) | `buero_a.gli`..`buero_d.gli` (by player); also `buerodrk.gli` if the office is darkened (OfficeState 3) | GFX_BUERO | Office | - |
| PERSONAL_A..D | `CPersonal` (Personal.cpp) | `personal.gli` | GFX_PERSONAL | Personnel Office | Yes (phone menu, two clerks; strike-end call) |
| AIRPORT | `AirportView` (AirportView.cpp) | none (draws the hall from brick data) | - | airport | - |
| PLANEPROPS | `CPlaneProps` (PlanProp.cpp) | `planprop.gli` | none | (plane fitting) | - |
| EDITOR | `CEditor` (Editor.cpp) | `Editor.gli` | GFX_EDITOR | (designer editor) | - |
| SHOP1 | `CDutyFree` (DutyFree.cpp) | `dutyfree.gli` | GFX_DUTYFREE | Duty-Free Shop | - |
| GLOBE | `CGlobe` (Globe.cpp, a `CPlaner`) | `globe_aa..ad.gli` (by player), plus `globe.gli`, `globe_a.gli`, `buerodrk.gli` when dark | GFX_GLOBE | Globe (filofax) | - |
| LAPTOP | `CLaptop` (Laptop.cpp, a `CPlaner`) | `globe_b.gli` / `globe_c.gli` / `globe_d.gli` (laptop quality variants), plus `globe.gli` | GFX_VR / VR2 / VR3 | Notebook | - |
| ARAB_AIR | `CArabAir` (ArabAir.cpp) | `arab.gli` | GFX_ARAB_AUS | Petrol Air counter | Yes |
| SABOTAGE | `CSabotage` (sabotage.cpp) | `sabotage.gli` | GFX_SABOTAGE | (Petrol Air back room) | Yes (incoming calls only, `Sim.cpp: SIM::DoTimeStep`) |
| AUFSICHT | `CAufsicht` (Aufsicht.cpp) | `aufsicht.gli` | GFX_AUFSICHT | Airport management | Yes |
| ABEND | `CAbend` (Abend.cpp) | `abend<season>.gli` (`abend0..3.gli`) | - | evening | - |
| BANK | `Bank` (Bank.cpp) | `bank.gli` | GFX_BANK | Bank | Yes (two bankers) |
| KIOSK | `CKiosk` (Kiosk.cpp) | `kiosk.gli` (+ `KioskLoc.gli`, `KioskS.gli` from gli\) | GFX_KIOSK | Newspaper Stand | - |
| DESIGNER | `CDesigner` (Designer.cpp) | `Designer.gli` | GFX_DESIGNER | HiTech Designer-Shop | - |
| LAST_MINUTE | `CLastMinute` (Lastmin.cpp) | `lastmin.gli` | GFX_LASTMIN | Last Minute counter | - |
| FRACHT | `CFrachtRaum` (Fracht.cpp) | `Fracht.gli` | GFX_FRACHT | cargo office | - |
| MAKLER | `CMakler` (Makler.cpp) | `makler.gli` | GFX_MAKLER | Plane Dealer | Yes |
| TAFEL | `CTafel` (Tafel.cpp) | `tafel.gli` | GFX_TAFEL | pin board | - |
| OPTIONS | `Options` (Options.cpp) | `startup.gli` | GFX_HAUPTMNU | options | - |
| WERKSTATT | `CWerkstatt` (Werkstat.cpp) | `werkstat.gli` | GFX_WERKSTAT | Workshop | Yes |
| WERBUNG | `CWerbung` (Werbung.cpp) | `werbung.gli`, or `nowerb.gli` placeholder when difficulty < DIFF_NORMAL and not free game | GFX_WERBUNG | Advertising Agency | Yes |
| REISEBUERO | `CReisebuero` (reise.cpp) | `reiseb.gli` | GFX_REISEB | AirTravel counter | - |
| NASA | `CNasa` (Nasa.cpp) | `nasa.gli` | GFX_NASA | NASA Shop | Yes |
| ROUTEBOX | `CRouteBox` (RouteBox.cpp) | `routebox.gli` | GFX_ROUTEBOX | Route management board | - |
| STATISTICS | `CStatistik` (Statistk.cpp) | `Statistk.gli` | GFX_STATISTK | Statistics | - |
| WORLD | `CWorld` (World.cpp) | `World.gli` (+ `EarthAll.lbm` from gli\) | GFX_WORLD | branch phone call | Yes (branch calls, `TALKER_WORLD`) |
| MUSEUM | `CMuseum` (Museum.cpp) | `museum.gli` | GFX_MUSEUM | Museum | Yes |
| RICKS | `CRicks` (Ricks.cpp) | `rick.gli` | GFX_RICK | Rick's Café | - |
| SECURITY | `CSecurity` (Security.cpp) | `security.gli` | GFX_SECURITY | Safe & Secure Ltd. | - |
| INSEL | `CInsel` (Insel.cpp) | `insel.gli`, or `noinsel.gli` when difficulty < DIFF_NORMAL | none / GFX_NOINSEL | Telescope (islands) | - |
| WELTALL | `CWeltAll` (WeltAll.cpp) | `WeltAll.gli` | none (own 640x440 RoomBm) | Telescope (space station) | - |
| RUSHMORE | `CRushmore` (Rushmore.cpp) | `rushmore.gli` | GFX_RUSHMORE | Telescope | - |

Factory details:
- INSEL, WELTALL and RUSHMORE play the sound `coin.raw` on entry (the telescope's coin). Each one also
  charges the player 1 money unit when it opens (`ChangeMoney(-1, 3300, ...)`) (H).
- Any id that is not listed falls to a default branch. It logs "Room %li not implemented" (except for the
  value 2049 = AIRPORT|LEAVING, which is silently ignored) and clears that stack slot (H).
- **ROOM_ABEND is in the factory but no caller ever pushes it.** The evening screen is created directly as
  `TopWin` (section 6.4). (H for "no caller found", M for "factory branch is legacy")
- On a first visit, `SetRoomVisited` (Stdraum.cpp) queues an advisor intro message `TOKEN_TUTORIUM`
  10000+RoomId, or 20000+RoomId for the low-difficulty advertising placeholder. Offices map to 10010 and
  personnel offices to 10011. It does not fire for AIRPORT, LAPTOP, ABEND, WORLD or 254. A per-install
  bitmask (`Options.OptionRoomDescription`, keyed by `RoomTipTable`) makes it fire only once per room group
  (H).

### 3.1 Rooms that open as phone ("Handy") dialogs

A phone dialog is a second room window held in `PLAYER::DialogWin` and created with `bHandy=TRUE`. It does
**not** go on the `Locations[]` stack. The current room (`LocationWin`) stays alive underneath it. (H)

| Creator | Rooms / class | Trigger |
|---|---|---|
| `Stdraum.cpp`: phone menu click (`MENU_FILOFAX` with par 2 = phone list; table `HandyRoomRemapper`) | ARAB_AIR (`CArabAir`), BANK x2 (`Bank`, banker 1 or banker 2), AUFSICHT (`CAufsicht`), MAKLER (`CMakler`), MUSEUM (`CMuseum`), NASA (`CNasa`), PERSONAL x2 (`CPersonal`, clerk a or b), WERBUNG (`CWerbung`), WERKSTATT (`CWerkstatt`); entry 254 = list of other players; ROOM_WORLD = list of branches | The player dials from the office desk phone or with the mobile phone item. Only rooms the player has already visited are listed (`WasInRoom`). |
| `Stdraum.cpp: CStdRaum::StartDialog` case TALKER_WORLD | `CWorld(TRUE, ..., city)` | A call to a branch |
| `Sim.cpp: SIM::DoTimeStep` | `CPersonal(TRUE)` | Personnel calls you when a strike ends (only with a mobile, or when in the office; in the personnel office itself it becomes a normal dialog) |
| `Sim.cpp: SIM::DoTimeStep` | `CSabotage(TRUE)` | Petrol Air calls you back about a sabotage job |

The phone call is refused (sound `busy.raw`) when:
- a player is physically in that room (`Sim.RoomBusy[room]>0`; the personnel office is exempt), or
- the talker is busy or locked, or
- the counterpart is closed: Arab Air before 11:00 or on Sunday; Museum before 11:00 or on Sat/Sun;
  Makler after 16:00; Werbung before 12:00 or on Sat/Sun.

(`Stdraum.cpp`, label `phone_busy`) (H). A successful call plays `dial1.raw` or `dial2.raw`, costs 1 money
unit, and logs the call in the player's history. (H)

Phone panel presentation (H, `Stdraum.cpp: CStdRaum::OnPaint` / `PostPaint`):
- The panel is a 340 px wide vertical slice of the phone room's own picture, taken from x = `HandyOffset`
  (default 160, overridden per call, e.g. 120/280 for the two personnel clerks, 367 for sabotage, 170 for
  world) and full height 440.
- It is drawn at screen x = `TempScreenScroll`. That value starts at 800 (off-screen) and slides left to
  378, moving 1 px per 3 ms of real time (~1.4 s). When the dialog ends it slides back to 800 at the same
  speed, and the phone window is then deleted (`CStdRaum::OnPaint` "old phone screen destroy").
- The left edge is a slanted ragged border: 9 bands of 52 px height, each shifted 13 px. A decoration
  bitmap `gDialogBarBm` sits on each band.
- While the panel is in, the airport renderer clips persons and bricks at `RightAirportClip` = 378 instead
  of 640. The airport camera centres the player in the left half (half-width 160 instead of 320).
- **WIDESCREEN:** 800 / 378 / 340 / 440 / 640 are hard-wired. In widescreen the panel should be
  right-anchored (start = screen width + margin, rest = screen width - 262) and the airport half-width
  should become (rest x)/2.

---

## 4. Player room state (`PLAYER`, Player.cpp)

- `Locations[10]`: a stack of room ids (UWORD), each possibly carrying `ROOM_ENTERING` (1024) or
  `ROOM_LEAVING` (2048). Slot 0 is normally ROOM_AIRPORT. Empty slots are 0. (H)
- `TopLocation` is the highest non-zero slot with its flags stripped, cached by `PLAYER::CalcRoom`.
  `PLAYER::GetRoom` returns it. (H)
- `LocationWin`: the one window for the top room. **Only one room window exists per player at a time.**
  (H)
- `DialogWin`: the optional phone-dialog window (3.1). (H)
- `DirectToRoom`: the room the player is currently walking to. `WaitForRoom`: a room to retry when it
  becomes free. `ThrownOutOfRoom`: the room in the last "occupied" message. `ExRoom`: the room just left,
  used by the walking AI. (H)
- `LocationTime`, `LocationForbidden`, `LocationForbiddenTime`: the max-stay rule (5.5). (H)
- `WasInRoom[256]`: visited flags, used by the phone list and the first-visit tips. (H)

### 4.1 Primitive operations

- **EnterRoom(room)** (`Player.cpp: PLAYER::EnterRoom`): does nothing if the room is already anywhere in the
  stack. Otherwise it stops the player's run, puts `room|ENTERING` into the first empty slot, recalculates
  the top, and broadcasts (`ATNET_ENTERROOM` + position) unless told not to. Special case: entering your own
  office while the "walk to globe" flag is set immediately also enters ROOM_GLOBE on top. If all 10 slots
  are full, it raises an internal exception. (H)
- **LeaveRoom()**: sets `LEAVING` on the topmost non-empty slot only. (H)
- **LeaveAllRooms()**: sets `LEAVING` on every non-empty slot except ROOM_AIRPORT. Hotkeys, the filofax
  walk menu, the office-globe hotkey and network conflict resolution all use it to pop back to the hall.
  (H)
- **WalkToRoom(room)** (`Player.cpp`): checks opening hours first (section 5.2). If the room is closed it
  shows the "closed" box and returns false. Otherwise it targets the room's entrance (`RUNE_2SHOP` rune for
  that room id), clears `WaitForRoom`, sets `DirectToRoom`, plans waypoints, and broadcasts. (H)
- **WalkToMouseClick(world pos)**: asks the hall for the marked area under the click
  (`AIRPORT::IsInMarkedArea`, the smallest rect containing the point wins). A room id becomes
  `WalkToRoom`. ROOM_WALL clamps the target in front of the office wall (y=138 upstairs, y=287
  downstairs). Flight-display ids (140..199) are ignored. Anything else becomes a floor-tile walk. It also
  triggers contextual hints (offices, duty free, etc., depending on game day). (H)

---

## 5. Entering a room from the airport (walk-in protocol)

### 5.1 Doors and room positions: no hard-coded table

Room and door positions are **data-driven by the hall files**, not by a table in code (H):
- The airport is assembled from 10 hall sections (`AIRPORT::LoadAirport`: LeftEnd, CheckIn, Office, Entry,
  Shops, Cafe, Security, Suitcase, WaitZone, RightEnd). Each section is a brick list, and the sections are
  merged left to right by aligning floor plates.
- Hall data carries:
  - **Area markers** (rect + room id): the clickable door/counter zones. `IsInMarkedArea`.
  - **RUNE_2SHOP** (2033): the "approach" point in front of a door or counter, with the room id as
    parameter.
  - **RUNE_SHOP** (2032): the "inside/threshold" point. Reaching it enters the room.
  - **RUNE_LEFTEND / RUNE_RIGHTEND** (2050/2051): scroll limits.
- Doors (`AIRPORT::Doors`) are animated in `AIRPORT::PumpDoors` (an ordinary door opens in 2-unit steps up
  to state 18, then closes; sound `gDoorClose` when within 400 px of the screen centre). `AIRPORT::TryDoor`
  opens doors next to walking persons.
  - The Petrol-Air "fire-extinguisher" secret door rotates in while ROOM_SABOTAGE is busy.
  - The cargo door opens only for a player heading to or leaving ROOM_FRACHT.
- Shop shutters animate with `DoorOpenTab` (`AirportView::OnPaint`). They roll up or down over 2000 time
  units (2 game minutes) around each opening or closing time.
- Closed rooms also get their walk path blocked through "condition blocks" (`Sim.cpp: SIM::DoTimeStep`
  calls `AIRPORT::SetConditionBlock`): ids 0..6 for DutyFree, ArabAir, LastMinute, Museum, Reisebüro,
  Makler, Werbung; ids 20..23 for a destroyed office of player 0..3 (OfficeState 2).

For OpenAT this means positions must come from the hall/brick assets. They should be documented in the
airport-hall format finding, not in this one.

### 5.2 Opening hours / closed rooms (`Player.cpp: PLAYER::WalkToRoom`, constants in `AirportView.cpp`)

| Room | Rule (closed when...) | Closed text (`Misc` id) |
|---|---|---|
| Duty Free (SHOP1) | before 10:00; on Sat/Sun also after 16:00 | 3200 (`MENU_CLOSED_DUTYFREE`=0) |
| Petrol Air (ARAB_AIR) | before 11:00, or on Sunday | 3201 |
| Last Minute | after 16:00, or on Saturday | 3202 |
| Museum | before 11:00, or on Sat/Sun | 3203 |
| AirTravel (REISEBUERO) | after 17:00 | 3204 |
| Plane Dealer (MAKLER) | after 16:00 | 3205 |
| Advertising (WERBUNG) | before 12:00 or on Sat/Sun, enforced only at difficulty >= DIFF_NORMAL or free game | 3206 |
| (copy-protection) | `MENU_CLOSED_PROTECTION`=7; quits the game when closed | 3207 |
| Security | while `Sim.nSecOutDays>0` (power cut after sabotage) | 3208 |

Notes:
- The constants are `timeDutyOpen` 10h, `timeDutyClose` 16h, `timeArabOpen` 11h, `timeLastClose` 16h,
  `timeMuseOpen` 11h, `timeReisClose` 17h, `timeMaklClose` 16h, `timeWerbOpen` 12h. (H)
- The working day itself is 9:00-18:00. All other rooms are always open during it. (H)
- Last Minute and AirTravel shutters stay shut for the whole game in missions without the travel holding
  (`RobotUse(ROBOT_USE_TRAVELHOLDING)`, e.g. DIFF_ADDON05) (H, `AirportView::OnPaint`). How WalkToRoom
  refuses entry in that case is not covered here. (M)
- Small inconsistency: the path block uses ">=" for the Last Minute / Reisebüro / Makler closing time, but
  WalkToRoom uses ">". So the exact closing minute is walk-blocked but not refused. (H, quirk)
- The "closed" box (`MENU_CLOSED`) uses `gli\request.gli` bitmap "REQUEST". It zooms in from (320,220).
  - Body: `Misc` 3200+n, split into lines on a separator character. A line of the form "label:value"
    prints the label left-aligned and the value right-aligned inside x 20..270, starting at y=20 with 21 px
    line spacing.
  - Footer: OK button `Misc` 3002, centred at y=160. It closes on click.
  - It is shown only if no menu or dialog is open. (H, `Stdraum.cpp`)
  - **WIDESCREEN:** zoom origin (320,220) is the centre of the 640x440 area.

### 5.3 Walk-in state machine (Person.cpp: `PERSON::DoOnePlayerStep` region, and `PersonReachedTarget`)

1. A click or hotkey calls `WalkToRoom`. `DirectToRoom` is set, and the person walks waypoints toward the
   room's RUNE_2SHOP point.
2. **The approach rune is stepped on.** While walking (with `StatePar==0`), each step checks for a
   RUNE_2SHOP rune under the person:
   - ROOM_AIRPORT_EXIT: opens the "call it a day?" request (`MENU_REQUEST_CALLITADAY`, text `Misc` 3011,
     buttons 3000/3001) and shows hint HINT_FEIERABEND after day 1.
   - ROOM_PHONE1..4: opens the phone menu, only if it is the `DirectToRoom`.
   - Kiosk, Insel, Security, Rushmore, Weltall, Ricks and RouteBox are entered **only if they are the
     `DirectToRoom`**. Walking past them does nothing. Every other room is entered by just walking onto its
     approach rune.
   - Then the person switches to auto-walk (`StatePar = room`, `ExRoom=-1`), and for real rooms it
     tentatively increments `Sim.RoomBusy[room]` (toilets excluded).
3. **Gatekeeping happens right on the approach rune, before any window opens.** In order:
   a. **Someone else's office or personnel office** (room 10..41 not belonging to this player): the
      increment is undone and the player is turned around. A request box opens:
      `MENU_REQUEST_WRONGROOM` (`Misc` 3015, Yes/No, "do you want to go to your own one?"), or
      `MENU_REQUEST_WRONGROOM2` (`Misc` 3020) if that airline is out of the game. `ThrownOutOfRoom` is set
      to the player's own matching room; "Yes" walks there.
      - Sabotage side effect: if the player carries the pliers item (`ITEM_ZANGE`) and the target office is
        intact (OfficeState 0), the pliers are used up and that office becomes darkened (OfficeState 3,
        synced via `ATNET_SYNC_OFFICEFLAG`). (H)
   b. **A dialog is open or starting** (including walking to talk to a player): the increment is undone and
      the player steps back out. No message.
   c. **The room is occupied** (`RoomBusy[room] > 1`, i.e. someone else is already counted) **or** the room
      is the player's `LocationForbidden`, and time is running (`!bNoTime`), and it is not an office: the
      increment is undone and the player is pushed back out of the door (offsets: NASA/FRACHT +44 x;
      WERKSTATT -44 x; others +22 y).
      - Human player, no menu/dialog open: a request box opens. `ThrownOutOfRoom = room`.
        - Normal case: `MENU_REQUEST_THROWNOUT` (`Misc` 3012), two buttons. Button 1 ("wait") keeps
          `WaitForRoom = room`. Button 2 or a right-click clears it.
        - Room is forbidden: `MENU_REQUEST_FORBIDDEN` (`Misc` 3027), single OK.
        - RouteBox and Security instead show `MENU_REQUEST_THROWNOUT2` (`Misc` 3021: "not a room but an
          open board"), and the player just stands there.
      - Robot player: its current action is demoted to second priority and it does office or personnel
        work instead.
   d. Otherwise the player walks on to the room's RUNE_SHOP point.
4. **The threshold rune is reached** (`RUNE_SHOP` under the person):
   - WC_F / WC_M: no room. The local player gets the toilet menu (`MENU_WC_F/M`); a computer player is
     handled by `SIM::ComputerOnToilet`.
   - Any other room (not passenger markers, not ELECTRO): `PLAYER::EnterRoom(room)` (skipped for the local
     player once the day is called off, and for remote network players, Owner 2). The person stops.
     Counter rooms set a facing direction: Last Minute faces 1, AirTravel faces 3, Arab Air / Kiosk /
     Ricks / RouteBox face 0.
5. **The `ENTERING` flag** is consumed by the main loop on the next simulation step (section 6.1), which
   creates the window.
6. **Waiting for a room** (`Takeoff.cpp: GameLoop`, every frame): if `WaitForRoom != 0` and
   `IsRoomBusy(WaitForRoom, self)` is false, the player automatically calls `WalkToRoom(WaitForRoom)`.
   This is the only queueing for players. **There is no physical queue at counters for players.** The
   passenger queues (check-in, ROOM_REISE_X*, ROOM_LASTMIN_X* spots) are a separate passenger-AI system.
   (H)
7. **Coming out of a room**: back in the airport with `StatePar==0` and standing on a RUNE_SHOP, the person
   auto-walks back out to the approach point plus an offset (NASA/FRACHT +44 x; WELTALL/RUSHMORE/INSEL/
   SECURITY directly to the approach point with `DirectToRoom` cleared; WERKSTATT -44 x; others +22 y).
   `ExRoom` is set, and manual walking is suppressed until the person is out. (H)
   The route box, Rick's and the kiosk-like "target only" rooms are not walked out of; the route box window
   clears `DirectToRoom` and `StatePar` when it closes and turns the figure to look direction 2, so the
   figure on the approach rune does not walk straight back in. (H) OpenAT clears `DirectToRoom` for every
   target-only room when a human leaves it (Rick's would otherwise re-open the same way).

`IsRoomBusy(room, except)` (`Misc.cpp`) is true if any other non-bankrupt player has exactly that id in any
`Locations` slot. Exemptions: ROOM_AIRPORT, and ROOM_AUFSICHT at exactly 9:00 (so all players can attend
the morning briefing together). (H)

### 5.4 Occupancy bookkeeping (`Sim.RoomBusy[256]`)

- It is incremented tentatively by the walking code (5.3) and decremented when a human player's LEAVING
  slot is processed. `SIM::UpdateRoomUsage` (`Sim.cpp`) recomputes it from scratch after every human
  enter/leave and on network messages: it zeroes the counter for every public room, then counts every
  non-bankrupt player's stack slots whose id is in 42..139.
- Personal / private screens can be shared and are not exclusive: Statistics, Globe, Laptop, PlaneProps
  (other players' copies) and the toilets.
  - In a network game, if a remote human enters a public room the local player already occupies, the local
    side sends `ATNET_ENTERROOMBAD` to the remote, which then calls `LeaveAllRooms`. The later arrival
    yields (`AtNet.cpp`). (H)
- Bug: the exclusion list in `UpdateRoomUsage` names `ROOM_WC_F` twice, so ROOM_WC_M is counted. (H, quirk,
  harmless because the walk code never counts toilets)
- Talker locking: entering a room with a talker (Boss, Arab, Sabotage, Banker1+2, Museum, Makler, Mechanic,
  Werbung, Security) locks that talker. That makes phone calls to it come back "busy" while someone is
  physically there (`Takeoff.cpp` robot branch, `AtNet.cpp`, `SIM::UpdateRoomUsage`). (H)

### 5.5 Maximum stay rule ("you were in here too long")

`PLAYER::CalcRoom`. This applies to any player in one of these rooms: REISEBUERO, LAST_MINUTE, SHOP1, BANK,
MUSEUM, ARAB_AIR, MAKLER, AUFSICHT, ROUTEBOX, RICKS, SABOTAGE, NASA, RUSHMORE, INSEL, WERKSTATT, KIOSK,
FRACHT.
- Staying 2 game hours (120000 units) or more in such a room forces you out: that slot and every slot above
  it get LEAVING.
- The room then becomes `LocationForbidden` for 2 game hours. Trying to enter it in that time shows
  `MENU_REQUEST_FORBIDDEN` (`Misc` 3027).

The timer starts when such a room first appears on the stack and resets when none is present. (H in code;
M that it is meant mainly against robots, since nothing restricts it to them.)

### 5.6 Destroyed office

If `WalkToRoom` targets an office whose OfficeState is 2 (bombed), it shows `MENU_REQUEST_DESTROYED`
(`Misc` 3028, single OK), but **it keeps walking** (no early return). The path itself is blocked by
condition blocks 20..23. Picking up the "coal" item event happens here too (`ITEM_GLKOHLE`). (H; the
walk-on behaviour is M)

---

## 6. The room-switch protocol (who creates and destroys windows)

### 6.1 Main-loop processing (`Takeoff.cpp: CTakeOffApp::GameLoop`, inside the per-sim-step loop)

This runs once per simulation step for each player:
- **when**: during 9:00-18:00 while the day is not called off, or whenever the local player is not in
  ROOM_ABEND and no evening screen is open;
- **gated by**: tutorial progress (it does not run while the tutorial is < 1200).

**Human local player (Owner 0), which is the only owner that gets windows:**

1. **Leaving pass**, for slots d = 0..9 that have LEAVING:
   - Decrement `RoomBusy`.
   - If a window exists: mark "just left". If fades are on and the room is not the Laptop and
     (hour >= 10 or day 0), take a fade snapshot (`GameFrame::PrepareFade`, `gBlendState=-1`) and pump one
     frame. Set `bIngnoreNextDoor` (suppresses the next door trigger), then **delete `LocationWin`**.
   - Clear the slot.
   - If the slot below is non-empty, **mark it ENTERING** (this is how you "fall back" into the previous
     room).
   - Recalculate the top, broadcast position and `ATNET_LEAVEROOM`, and run `UpdateRoomUsage`.
2. **Entering pass**, for slots d = 0..9:
   - A slot with both flags is discarded.
   - For a slot with ENTERING:
     - If a window exists: take a fade snapshot unless one was just taken by the leaving pass, or the room
       is Laptop or Globe. **Delete the old `LocationWin`.**
     - Clear ENTERING. Stop edge scrolling (`gMouseScroll=0`).
     - If a snapshot exists and the room is not the Laptop (same hour rule), arm the fade
       (`gBlendState=-2`).
     - For counter rooms (Arab Air, RouteBox, Kiosk, Ricks, Last Minute, Reisebüro): set the person's
       facing and broadcast.
     - **Create the new window** through the factory (section 3).
     - Globe only: take the fade snapshot *after* creation, because the globe constructor pumps messages.
     - Recalculate the top, broadcast position and `ATNET_ENTERROOM`, and run `UpdateRoomUsage`.

**Robot (Owner 1)**: no windows.
- ENTERING: if the room is busy (`IsRoomBusy`), the robot reorders its action list so the blocked action
  moves to second place and office or personnel work comes first. Otherwise the flag is cleared and the
  robot executes the room action right away (`RobotExecuteAction`). Facing and talker locking are applied,
  and the host broadcasts.
- LEAVING: talker unlock, `RoomBusy--`, clear the slot, mark the slot below ENTERING, recalculate, and the
  host broadcasts.

**Remote human (Owner 2)**: its stack is overwritten from `ATNET_ENTERROOM` / `ATNET_LEAVEROOM` messages
(`AtNet.cpp`). (H)

### 6.2 Consequences for OpenAT (H)

- **The previous room's window is destroyed, not hidden**, whenever a sub-screen is entered. Example:
  Office -> Laptop. The CBuero window is deleted and a CLaptop window is created. Leaving the Laptop
  deletes it, marks the Office slot ENTERING, and a **fresh** CBuero is constructed. Any room state that
  must survive a sub-screen round trip has to live in `PLAYER`/`SIM`, not in the room object.
- The `Locations[]` stack is the source of truth. Windows are a projection of its top. Typical stacks:
  - `[AIRPORT]`
  - `[AIRPORT, BURO_x]` -> `+GLOBE`, `+PLANEPROPS`, `+LAPTOP`, `+STATISTICS`, `+OPTIONS`
  - `[AIRPORT, AUFSICHT]` -> `+TAFEL`
  - `[AIRPORT, DESIGNER]` -> `+EDITOR`
  - `[AIRPORT, SABOTAGE]`: when the sabotage window is destroyed with ArabMode 6, ROOM_WORLD is pushed
    (`sabotage.cpp: CSabotage::~CSabotage`).
- The stack is at most 10 deep. Entering a room already on the stack is a no-op. Several entries can be
  pushed in one call (morning: AIRPORT then AUFSICHT), and only the top gets a window. Lower ENTERING slots
  are processed in the same pass in slot order: each one creates and then immediately replaces the window,
  so only the top survives. (M: the intermediate window is constructed and destroyed within one step.)
- How sub-screens are opened:

  | Sub-screen | Opened by |
  |---|---|
  | Globe | clicking the globe in the Office (`Buero.Cpp`, click id 10), or hotkey G (from elsewhere it walks to the office first with `WalkToGlobe`) |
  | PlaneProps | Office click (`Buero.Cpp`) |
  | Laptop | inventory click on the Laptop item, when no dialog or menu is open |
  | Statistics | status-bar icon, lower half of the right icon column; toggles: a second click leaves it |
  | Options | Esc / F2 (toggle), F3 / F4 (shortcut pages 5 and 6) (`Gameframe.cpp`, `Stdraum.cpp`) |
  | Tafel | clicking in the airport manager's office (`Aufsicht.cpp`) |
  | Editor | `Designer.cpp` |

- **Leaving** a room is always `LeaveRoom()` (pop one level). Rooms call it on:
  - a right-click inside the play area (y < 440) when no menu or dialog is open (e.g.
    `Bank.cpp: Bank::OnRButtonDown`);
  - a left-click on the room's exit hotspot: the cursor turns into `CURSOR_EXIT`, and click id 999 calls
    `LeaveRoom` (e.g. Bank rect (62,64)-(173,186), Museum (0,0)-(233,227) and (533,312)-(605,352),
    RouteBox (444,380)-(640,439)).

  Exit hotspot rects are per room (other findings files).
- Hotkeys and the filofax walk list call `WalkToRoom(target)` **and** `LeaveAllRooms()`, so the stack
  collapses to the airport and the player then walks (`Gameframe.cpp: GameFrame::OnChar` /
  `Stdraum.cpp` filofax handler). Pressing the same key twice within 300 ms makes the player run. Hotkeys
  are ignored on stairs, before 9:00, during ROOM_ABEND, while walking to another player, in Options, and
  in the tutorial.

### 6.3 Morning (day start)

The evening screen ends when `DayState==4` and the time is 9:00-10:00 (`Takeoff.cpp: GameLoop`):
- `TopWin` (the CAbend) is deleted, `bNoTime=TRUE`, `DayState=1`, and the mouse wait counter is bumped.
- Every non-bankrupt player gets `EnterRoom(AIRPORT)` then `EnterRoom(AUFSICHT)`. The briefing opens in the
  airport manager's office with time frozen. (H)

On a brand-new game the same happens with walking instead: every player `EnterRoom(AIRPORT, no
broadcast)`, then `WalkToRoom(AUFSICHT)` (`GAMESTATE_INIT|DONE` branch). (H)

Leaving the briefing (clicking the exit, any click in the room after the briefing, or cancelling) sets
`bNoTime=FALSE`, `DayState=2` and calls `TryLeaveAufsicht` (`Aufsicht.cpp`). (H)

`AirportView` re-centres the camera when it is created at exactly 9:00. (H)

Network: a morning ready-handshake (`ATNET_READYFORMORNING`) holds simulation at 9:00 until all humans have
left the briefing (they are shown network bitmap 2). (H)

### 6.4 Evening (day end)

The trigger fires when time >= 18:00, or when the day is called off with `DayState==2`
(`Takeoff.cpp: GameLoop`):
- At 18:00 the local `CallItADay` is reset and `bNoTime=TRUE`.
- With `DayState==2`, the "begin evening" step runs: an open dialog in the local room is stopped (dialogs
  are cut off). Every non-robot player that still has anything on its stack and is not in ROOM_ABEND gets
  `LeaveRoom()`. That pops one level per pass and fades as usual, so a deep stack unwinds over several
  steps, including the airport itself.
- Once no human has anything left, `TopWin = new CAbend(FALSE, localPlayer)` and `DayState=3`. CAbend
  loads `abend<season>.gli` and zeroes all players' GameSpeed.
- Closing CAbend sets `DayState=4` (`Abend.cpp: CAbend::~CAbend`). Its paint also forces `DayState=4` once
  time >= 18:00, then time fast-forwards (x600) to 9:00, and 6.3 runs.
- "Call it a day" comes from the airport exit request, the Tab key (`MENU_REQUEST_CALLITADAY`), or the
  Aufsicht/dialog paths that set `DayState=2`. In single player `Sim.CallItADay` mirrors the local player.
  In a network game, the host ends the day only when all humans have called it off; it then sends
  `ATNET_DAYFINISHALL` with a time slice 30 steps ahead. (H)

`DayState` values: 1 = morning briefing / before work, 2 = working day, 3 = evening screen open,
4 = evening finished, waiting for 9:00. (M, inferred from usage)

### 6.5 Load game

After loading, the stack for each local human is restored as follows: any ROOM_OPTIONS slot is dropped
and the topmost remaining slot is re-flagged ENTERING. The window is then recreated by the normal entering
pass (`Sim.cpp: SIM::LoadGame`). (H)

---

## 7. Time flow while inside a room

The simulation is global. **Being inside a room does not stop the clock.** Planes, robots, passengers and
other players keep running (`Takeoff.cpp: GameLoop`). (H)

What does stop or alter time:
- **Options room** open (`nOptionsOpen>0`, incremented in `Options.cpp: Options::Options`): no simulation
  steps (no persons, planes, time or time slices). In a network game the other machines are paused too and
  show the network bitmap. (H)
- **Pause** (`Sim.bPause`) and **waiting for a network player** (`nWaitingForPlayer`). (H)
- **`bNoTime`** (morning briefing and after 18:00): the clock does not advance, people still animate.
  (H/M)
- **Speed multiplier** per rendered frame: the minimum `GameSpeed` over all non-robot players who have not
  called off the day, 0..3. It maps to step multipliers 1, 2, 4 (3 in network), 8 (4 in network).
  - "Warp" while the player is walking (`bgWarp`, Space bar): x150.
  - Before 9:00, after a called-off day, or overnight: x600.
  - At most 400 sim steps per frame (1600 in fast-forward phases). (H)
- **Clock area of the status bar**:
  - Single player only: holding LMB inside (102,440)-(255,459) multiplies steps x5, RMB x20.
  - Right-click on (78,440)-(102,460) cycles the player's GameSpeed (+3 mod 4) and broadcasts
    `ATNET_SETSPEED`.
  - **WIDESCREEN:** these are absolute 640-frame coordinates.
- People walk only between 8:00 and 18:00. Robots act only between 9:00 and 18:00. (H)
- Mouse wobble when drunk (`IsDrunk`) is applied per sim step. (H)

---

## 8. Airport view (AirportView.cpp), parts relevant to hosting

### 8.1 Viewport and coordinate frame

- Every room window is created with `WindowRect = (0,0,640,480)` and `StatusLineSizeY = 40`. The play area
  is y 0..439 and the status line is y 440..479 (`Stdraum.cpp: CStdRaum::CStdRaum`). **WIDESCREEN.**
- The airport draws into the 640x440 clip rect with world offset `PLAYER::ViewPos` (x scrolls; y is only
  -10 or 0). Two floors: the upstairs floor is world y >= 5000 (walk-net). Screen y < 155 counts as
  upstairs for centring; there the view is nudged to y = -10. Near the right end
  (x > RightEnd-800), y is forced to 0. (H)

### 8.2 Camera and scrolling (`AirportView::FocusCameraOnPos`, `MoveCamera`, `CenterCameraOnPlayer`)

- **Half-width 320** (160 while a phone panel is in) is used to centre the player:
  `ViewPos.x = player.x - 320`. **WIDESCREEN:** this should become `viewWidth/2`.
- Clamp: `ViewPos.x >= Airport.LeftEnd` and `ViewPos.x + 320 <= Airport.RightEnd`. Note the asymmetry: the
  right clamp uses the half-width, not the full width. Either RightEnd marks "rightmost camera centre" or
  the view can show 320 px past it (open question Q3). **WIDESCREEN** affects both.
- **Flipping mode** (`Options.OptionFlipping`): when the player leaves screen band x 90..550 the view jumps
  so the player lands at x 540 (moving left) or x 100 (moving right). **WIDESCREEN:** the band and the
  landing points are 640-based.
- **Smooth mode** (default): an inertial camera with a 5-sample player-velocity average. It starts scrolling
  once the player is more than 2/3 of the half-width away from centre. Speed changes are accepted only if
  they persist for 2 frames, and the camera never overshoots a standing player. Vertical motion is
  inertial too. (H; exact inertia curve in `CalcInertiaVelocity`, not analysed here)
- **Edge scrolling ("C&C scrolling")**:
  - Hot zones: cursor x <= 10 (left, click id 6010) or x >= 630 (right, id 6011), only when y < 440, no
    dialog or menu is open, not paused, and the view is not at its limit.
  - Scroll speed accelerates by 2 px per frame up to 44 px/frame, and decelerates by 4 per frame after
    release. (The intended "faster with LMB" bonus is clamped away by the 44 limit, see Q4.)
  - **WIDESCREEN:** 630 must become `width-10`.
- **Right-click while edge-scrolled**: returns the camera to the player. If that is more than 640 px away
  it jumps with a crossfade (8.4). **WIDESCREEN:** the 640 threshold is the old screen width.
- Losing a followed person (Shift+click focus, `Sim.FocusPerson`) makes the camera return to the player,
  also crossfading when more than 640 px away.
- An off-screen player indicator ("cursor feet") is shown when the player is outside
  `ViewPos.x-40 .. ViewPos.x+650` and walking. **WIDESCREEN:** 650.
- Ambient volume and door sounds are positioned relative to the screen centre (`ViewPos.x+320`, within
  400 px for doors). **WIDESCREEN.**

### 8.3 Clicks in the airport (`AirportView::OnLButtonDown/Up/DblClk/RButtonDown`)

- Ignored before 9:00, while the player is hidden (`DontDisplayPlayer`), while running to the toilet, on
  the edge-scroll ids, and in the status line (y >= 440, forwarded to `CStdRaum`).
- Shift+click on a person makes the camera follow that person.
- A click on another player's figure (within 640 px world distance, that player in the airport, not
  stuck or talking) walks next to them and starts a player dialog (network: `ATNET_DIALOG_REQUEST`).
  **WIDESCREEN** for the 640 distance (M: gameplay rule, probably keep).
- Otherwise `WalkToMouseClick(mouse + ViewPos)`. This happens on button-down, and also on button-up if the
  click was shorter than 500 ms while edge-scrolling. The y tolerance bands are hard-coded for the two
  floors: downstairs y 221..439, upstairs y 94..169. **WIDESCREEN-neutral** (world y), but 440 is the
  play-area bottom.
- A double-click makes the player run.
- Hover tooltips over marked areas: own office / personnel office get an extra "(B)/(O)" or "(P)" key
  hint suffix, other airlines' offices show the airline name, everything else shows `Tooltip` 5000+room id.
  Flight displays use the dynamic flight texts.

### 8.4 Parallax and draw (WIDESCREEN-relevant facts only)

- The background horizon is 5 parallax brick layers (brick ids 5000..5004) scrolling at 16/64, 16/64,
  30/64, 42/64 and 56/64 of `ViewPos.x`. Each layer is tiled `640/tileWidth + 2` times. The taxiway floor
  is brick 5006. **WIDESCREEN:** the tile count must use the real width.
- The final airport clip is (0,0,640,440). Brick and person culling uses `RightAirportClip` (640, or 378
  with the phone panel) with margins of +50/+100/+250. **WIDESCREEN.**

### 8.5 Status bar (drawn by CStdRaum for every room, including the airport)

The status bar is drawn when `StatusCount>0`, during fades, or always with `bNoSpeedyBar`, and only for
rooms whose picture is at most 440 tall. Full-height (480) rooms hide it. (H)
- Left block `StatusLineBms[0]` at (WinP1.x, 440). Right block `StatusLineBms[6]` right-aligned at 640.
- Six inventory slots at x = 248 + 60*i, i = 0..5.
- The right icon column x 610..640: upper half = repeat last advisor message, lower half = statistics.
- The tutorial badge sits left of the right block.
- Clicking the inventory opens Laptop, phone (Handy) or filofax as described above. A right-click on an
  item asks to discard it (`MENU_REQUEST_KILLITEM`).
- **WIDESCREEN:** all x positions are absolute in the 640 frame (the right block uses `WinP2.x`, so it
  follows the window width if WinP2 is widened).

Detailed status-bar layout belongs to the status-bar finding.

### 8.6 Split screen

The `WIN_*` quadrant constants and per-player `PLAYER::WinP1/WinP2` copies exist, but every `CStdRaum`
uses the full (0,0)-(640,480) rect, and no code path uses the `WIN_*` masks. ATD has **no split-screen**;
multiplayer is network only. The many `WinP1`/`WinP2` offsets in AirportView are vestigial and always
(0,0)/(640,480). (H) OpenAT can drop split screen. Keep WinP1/WinP2 as the "viewport rect" abstraction for
widescreen.

---

## 9. Transitions and effects

### 9.1 The room crossfade (the only transition actually used)

Implemented in `Gameframe.cpp: GameFrame::PrepareFade`, `SBBM.cpp: SBPRIMARYBM::Flip`, and
`Colorfx.cpp: SB_CColorFX::ApplyOn2`. (H)

- **Type:** a plain linear cross-dissolve between the old and new full frame, 16-bit, using precomputed
  per-channel blend tables. `ColorFX` is initialised as `SB_COLORFX_FADE` with 8 steps
  (`GameFrame` constructor).
- **State variable** `gBlendState`:
  - `-1` = idle, or snapshot taken but not started.
  - `-2` = start requested.
  - `8..0` = running.
- **Sequence:**
  1. `PrepareFade` copies the current 640x480 front buffer into `gBlendBm` (the "old" image).
  2. Arming sets -2.
  3. On the next Flip after the new room has drawn its first frame, the state becomes 8, the new frame is
     copied into `gBlendBm2`, and the old image is shown.
  4. Each following flip shows old*s/8 + new*(8-s)/8 for s = 7..1.
  5. At 0 both buffers are freed and the state returns to -1.
- **Duration:** 8 presented frames. It is frame-based, not time-based (~0.4 s at 20 fps). `gFramesToDraw-
  BeforeFirstBlend` can delay the start by N frames (reset to 0 by every room constructor). (H; real-time
  duration M)
- **Freeze during fade:** the old/new frames are static images. The new room is not re-rendered during the
  dissolve because `gBlendBm2` is captured once. (H)
- **When it happens:**
  - Leaving a room. Entering a room, unless the leave already took the snapshot, and except Laptop.
  - Globe: snapshot taken after construction.
  - Camera jumps of more than 640 px (focus lost, right-click recentre).
  - Toggling the option on.
  - **Never** while the hour is 9 (i.e. 9:00-9:59) on days after day 0, because the condition is
    "hour > 9 or Date == 0". So the morning briefing and the first exit to the hall cut hard. (H, likely
    deliberate.)
- **Option:** `Options.OptionBlenden` (registry, default TRUE). The options menu label is `Misc` 4015/4016
  (off/on) (`Options.cpp`). When off, all transitions are hard cuts.
- **Pause fade:** entering pause dissolves to the pause picture (`pause.gli`) over 8 frames using the same
  ColorFX (`FrameWnd->PauseFade`), only if fades are enabled. (H)
- **WIDESCREEN:** snapshot and blend copy exactly 640x480 pixels (row length 640*2 bytes). OpenAT should
  dissolve full render targets of any size, e.g. a shader lerp over 8 frames or a ~0.4 s timer.

### 9.2 Other ColorFX uses (for completeness, Colorfx.cpp)

`SB_CColorFX` also provides:
- `SB_COLORFX_GREY` (desaturate; used for greyed menu items in `Stdraum.cpp`);
- a "fire" table type (999);
- `BlitTrans` (8-level translucency, used for mood bubbles and smoke/stench clouds in the airport);
- `BlitWhiteTrans`, `BlitOutline`, `BlitAlpha`, `BlitGlow` (the tutorial / highlight glow);
- `HighlightText` / `RemapColor` (text hover highlight).

All operate on 16-bit (555/565) surfaces using channel masks read from the surface. (H)

### 9.3 Blenden.Cpp / Cblenden.cpp: unused transition library

Two near-identical versions of a small DirectDraw transition library. `Cblenden.cpp` is the newer one, with
a "transparent" flag and three extra effects. Each effect blends surface 1 -> surface 2 into a temp surface
given a 0..100 percent value. The suggested use is ~30 phases. Effects:
- **CoolBlend**: zoom into the old image around a centre point (the click position).
- **FallBlend**: the new image falls in and bounces.
- **RandomBlend**: random block reveal.
- **SlimeBlend**: columns run down like slime.
- **HLineBlend**: N horizontal bars slide.
- **ShadesBlend**: venetian-blind bars, with direction.
- **TerminatorBlend**: a sweep effect.

**Neither file is in the original project (`AT.DSP`), and nothing references the classes. The SDL port
lists only the headers.** They are dead code. (H) OpenAT may offer them as optional extras, but the
faithful default is the 8-step crossfade.

### 9.4 Fx2.cpp: sphere texture blit (not a transition)

`Fx2SphereBlit` maps a horizontally wrapping texture (width must be a power of two) onto a sphere of radius
r at a centre point with a rotation angle, using fixed-point bow-curve stepping. It is the old software
globe renderer. It is also **not in `AT.DSP` and not referenced** (the globe now uses `EarthProjectize` in
`Misc.cpp` and prerendered bitmaps). Dead code. (H)

### 9.5 DeltaVid.cpp: developer screen recorder

A delta-compressed full-screen video format with header "DeltaHeader" + Ctrl-Z, and per-pixel tokens for
"same as previous frame", "same as the scroll-shifted previous frame" (two scroll offsets, for the airport
parallax) and "literal". While Scroll-Lock is on, `SBPRIMARYBM::Flip` (`SBBM.cpp`) records every presented
640x480 frame (with the cursor) to a numbered file, plus a sound log text file. The `/555` command-line
switch and a ":path" video argument (`Takeoff.cpp: InitInstance`) run `Unvideo` to convert a recording
offline and exit. It is a development tool for making trailers. **Not needed for OpenAT.** (H)

### 9.6 HLine.cpp: compressed sprite engine for people

The "HLine engine" stores sprites as pools of horizontal-line runs with palette remapping and shared base
pools.
- Pool files are loaded from `gli\`: `Skelett.pol` (shared skeleton pool), `clan<N>.pol` (per clan), and
  `clang<N>.pol` (clan gimmick animations); `clanw*.pol` also exist in the install (`Takeoff.cpp:
  InitInstance`, `Person.cpp`).
- `CHLObj::BlitAt` / `BlitLargeAt` draw a sprite (BlitLargeAt is the enlarged in-room figure, e.g. in
  Aufsicht, DutyFree and Makler).
- Pools are loaded and unloaded on demand per clan (`CLANS` load logic in `Person.cpp`). Unused clan
  gimmick pools are unloaded after about 2 s of non-use (`Takeoff.cpp: GameLoop`).

This is the format for all walking characters in the airport. The `.pol` format belongs in an asset-format
finding. (H for role; format details not analysed here)

---

## 10. Entry-denial and room messages (resource ids, `Std_Ger.res`, group `Misc`)

| Situation | Menu | Text id | Buttons |
|---|---|---|---|
| Room closed (opening hours) | MENU_CLOSED, par 0..8 | 3200+par | OK = 3002 |
| Room occupied by another player | MENU_REQUEST_THROWNOUT (2) | 3012 (`MenuStart` also tries to print 2100 with two airline names, see Q6) | 3000 / 3001 (wait / don't wait) |
| Board occupied (RouteBox, Security) | MENU_REQUEST_THROWNOUT2 (11) | 3021 | 3000 / 3001 |
| Stayed too long, entry forbidden | MENU_REQUEST_FORBIDDEN (17) | 3027 | OK 3002 |
| Your office destroyed | MENU_REQUEST_DESTROYED (18) | 3028 | OK 3002 |
| Wrong (foreign) office | MENU_REQUEST_WRONGROOM (5) | 3015 | 3000 (go to own) / 3001 |
| Wrong office, that airline is out | MENU_REQUEST_WRONGROOM2 (10) | 3020 | 3000 / 3001 |
| Exit reached / Tab: go home? | MENU_REQUEST_CALLITADAY (1) | 3011 | 3000 / 3001 |
| Demo version stubs | MENU_REQUEST_NO_* (20..26) | 3030..3036 | OK |
| First visit intro (advisor) | - | `Tuto` 10000+RoomId (offices 10010, personnel 10011; low-difficulty ad agency 20000+109) | - |
| Room hover tooltip | - | `Tooltip` 5000+RoomId | - |

Request-box rendering (`Stdraum.cpp`, `MENU_REQUEST`):
- `gli\request.gli` bitmap "REQUEST".
- Text printed at (10,10) inside 280x190.
- Buttons at y=160: left 3000, right 3001, or a single centred 3002 when `CStdRaum::MenuIsPlain`.
- Zoomed in from the person's screen position minus 20 y (occupied/wrong-room cases) or from (320,220).
- **WIDESCREEN:** the zoom origin (320,220) is the frame centre.

Rule: text id = 3010 + MENU_REQUEST code. (H)

---

## 11. WIDESCREEN checklist (all 640x480 assumptions found in this area)

1. Room windows created as (0,0,640,480). Status line at y 440..480 (`CStdRaum::CStdRaum`).
2. Room pictures are 640x440 (WeltAll allocates 640x440 explicitly). Most rooms are fixed art and need
   centring or pillarboxing in widescreen.
3. Airport camera half-width 320 (160 with phone), and the clamp `ViewPos.x+320 <= RightEnd`.
4. Flipping band 90..550, landing points x 540 / x 100.
5. Edge-scroll hot zone x >= 630, scroll limit test `ViewPos.x+320 < RightEnd`.
6. Recentre / focus crossfade threshold 640 px. Player-indicator range -40..+650. Player-dialog distance
   640.
7. Parallax tiling count `640/size+2`. Clip rects (0,0,640,440) and (0,0,640,480).
8. `RightAirportClip` 640 / 378. Person and brick culling margins.
9. Phone panel: start 800, rest 378, slice 340x440, slanted 9x52 border.
10. Fade snapshot and blend copy exactly 640x480x16bpp.
11. Request/closed boxes zoom from (320,220). Extrablatt from (320,240).
12. Status-bar absolute x coordinates: clock 78..102, time area 102..255, items 248+60i, icons 610..640.
13. Network wait bitmap centred with (640-w)/2, (440-h)/2 (`SBPRIMARYBM::Flip`).
14. Ambient and door sound positions relative to `ViewPos.x+320`.
15. Edge-scroll and room-mouse checks `gMousePosition.y < 440`.

---

## 12. Traceability index

- `Takeoff.cpp: CTakeOffApp::GameLoop` covers: the room factory, the leave/enter passes, robot handling,
  speed multipliers, WaitForRoom retry, morning/evening transitions, the tutorial room triggers, and camera
  MoveCamera per frame.
- `Takeoff.cpp: CTakeOffApp::InitInstance` covers: HLine pools, Unvideo.
- `Player.cpp: PLAYER::EnterRoom`, `LeaveRoom`, `LeaveAllRooms`, `GetRoom`, `CalcRoom`, `BroadcastRooms`,
  `WalkToRoom`, `WalkToMouseClick`.
- `Person.cpp` player walking step (RUNE_2SHOP / RUNE_SHOP handling, occupancy and wrong-office checks,
  exit walking), `PERSON::PersonReachedTarget`.
- `Misc.cpp: IsRoomBusy`.
- `Sim.cpp: SIM::UpdateRoomUsage`, `SIM::DoTimeStep` (condition blocks, phone-in calls), `SIM::LoadGame`,
  `SIM::GetHour/GetMinute`, `SIM::ChooseStartup`.
- `Stdraum.cpp: CStdRaum::CStdRaum`, `~CStdRaum`, `OnPaint`, `PostPaint`, `OnRButtonDown`, `MenuStart`,
  `MenuRepaint`, `MenuIsPlain`, `MenuStop`, `StopDialog`, `SetRoomVisited`, the phone-menu click handler,
  and the tables `HandyRoomRemapper`, `FilofaxRoomRemapper`, `RoomTipTable`.
- `AirportView.cpp: AirportView::AirportView`, `FocusCameraOnPos`, `MoveCamera`, `CenterCameraOnPlayer`,
  `OnPaint`, `OnLButtonDown/Up/DblClk`, `OnRButtonDown`, `AIRPORT::IsInMarkedArea`, `GetRandomTypedRune`,
  `LoadAirport`, `PumpDoors`, `TryDoor`, `SetConditionBlock`, and the opening-hour globals.
- `Gameframe.cpp: GameFrame::PrepareFade`, `OnChar` (hotkeys), `OnKeyDown` (Tab/Esc/F2-F4); `SBBM.cpp:
  SBPRIMARYBM::Flip`; `Colorfx.cpp: SB_CColorFX::ReInit/ApplyOn2`.
- `Abend.cpp: CAbend::CAbend/~CAbend/OnPaint`; `Aufsicht.cpp: CAufsicht::OnLButtonDown`; `sabotage.cpp:
  CSabotage::~CSabotage`; `Buero.Cpp: CBuero::OnLButtonDown`; `AtNet.cpp` ENTERROOM / LEAVEROOM /
  ENTERROOMBAD.
- Dead or dev code: `Blenden.Cpp`, `Cblenden.cpp`, `Fx2.cpp`, `DeltaVid.cpp`. Sprite engine: `HLine.cpp`.
- Manual: ch.2 "Moving Around the Game" / "Keyboard" (p.7-8), ch.3 hint "only one player at a time can be
  in a room", ch.11 "The Airport" (p.40-46).

---

## 13. Open questions

- **Q1** (M): The 2-hour max-stay rule (`CalcRoom`) also applies to humans. Does it ever trigger in normal
  play, for example when idling in the bank at high speed? This needs an in-game test.
- **Q2** (M): Pushing several rooms in one step (AIRPORT + AUFSICHT at 9:00) constructs the AirportView
  and destroys it immediately. Does that have side effects that OpenAT must preserve, such as camera
  centring, `bgWarp` reset or `FlushTalkers`? Verify, or just create only the top window.
- **Q3** (M): Is `RUNE_RIGHTEND` placed 320 px left of the hall's visual end, as the half-width clamp
  implies? Check the hall data. This decides how to clamp a wider view.
- **Q4** (H code / L intent): The edge-scroll "faster with LMB" term is cancelled by the hard 44 px clamp,
  yet the manual says scrolling is faster with the button held. Was the clamp meant to be
  `44+(LMB-1)*30`? Test in game.
- **Q5** (M): `Person.cpp` has the expression `c==ROOM_ELECTRO || c==ROOM_ROUTEBOX || ROOM_WELTALL`, which
  is always true and so always clears `c`. This disables the "leaving a room locks manual walking" check
  in that spot. Faithful-quirk or fix?
- **Q6** (M): For `MENU_REQUEST_THROWNOUT`, `MenuStart` prints `Misc` 2100 with airline names indexed by
  `MenuPar1/4` and `MenuPar1%4`, but `MenuRepaint` later redraws the box with 3012. Which text does the
  player actually see? Check in game.
- **Q7** (L): The exact semantics of `bIngnoreNextDoor` and `gRoomJustLeft` (door re-trigger suppression)
  were not traced to their consumers.
- **Q8** (M): In missions without the travel holding, Last Minute and AirTravel shutters are drawn shut,
  but `WalkToRoom` has no matching refusal. Is entry blocked only by the hall condition blocks or by
  missing runes?
- **Q9** (L): The real-time length of the 8-frame crossfade depends on the frame rate of the original
  (target ~20 fps?). Measure it in the running game to pick an OpenAT duration.
- **Q10** (M): The manual's function-key list (F1 options, F2/Esc load, F3 save) is off by one from the code
  (Esc/F2 options toggle, F3 shortcut 5, F4 shortcut 6). Confirm which page shortcuts 5 and 6 open.
