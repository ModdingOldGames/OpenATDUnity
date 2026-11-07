# Rooms & UI framework — how to add a room

Code: `Assets/OpenAT/View/Rooms/Framework/**`, `Assets/OpenAT/View/UI/**`. Specs: `docs/spec/findings/rooms-and-ui-*.md`
(part 1 framework, 2 menus, 3 dialog, 9 hosting). The Bank (`View/Rooms/Bank/`) is the reference room.

## Pieces

| Piece | Class | What it does |
|---|---|---|
| Room base | `RoomScreen` (a `GameScreen`) | 640x440 canvas centred (`RX` = its screen x), background from the room GLI with side fill (`WideFill`), per-frame hotspots with cursor + tooltip, exit areas (id 999), right-click leave, ambient sounds, first-visit help, menu slot (`Menus`), dialog (`Dialog`), status bar, advisor layer. |
| Registry | `RoomRegistry` + `[Room(id)]` | Original room id → factory. Found by attribute; `RoomRegistry.Register(id, () => new MyRoom())` for code-registered or replacement rooms. New rooms use ids ≥ `RoomIds.FirstCustom` (1000). |
| Host | `App.Rooms` (`RoomHost`) | The player's location stack. `Enter(id)`, `Leave()`, `LeaveAll()`, `ToggleRoom(id)`, `WalkTo(id)`, `PhoneCall(talker)`, `WasVisited(id)`. Only the top room has a live screen (sub-rooms destroy the room below; leaving re-creates it). Crossfades room changes. |
| Phone pane | `PhoneCallScreen` | Overlay: the partner's room (instantiated with `IsPhonePane`) slides in as a 340 px slice; the dialog runs with phone bubble anchors. |
| Characters | `SmackerPerson` | Clip/mood graph of `video/*.smk` clips (palette index 0 transparent). The dialog sets the desired mood (Talking / Listening / Idle). |
| Dialog | `DialogSession`, `IDialogScript`, `DialogTree`, `DialogActions`, `Talkers` | Bubbles, choice lists, click → node flow, voice (`VOICE/..` from `[[..]]` markers) with lip-sync and auto-advance, number card, menu re-entry. |
| Menus | `MenuHost`, `PopupMenu`, `Calculator`, `RequestMenu`, `ClosedMenu`, `KontoauszugMenu`, `LettersMenu`, `TextEntryMenu`, `NotepadListMenu`, `FilofaxMenu`, `StubMenu` | One menu slot per room with zoom in/out, right-click cancel, calculator on top. |
| Frame UI | `App.Ui` (`GameUi`), `App.Advisors` (`AdvisorLayer`), `StatusBar` | Cursor looks, tooltip box, crossfade, pause; advisor messages (sim `AdvisorMessage` events are queued automatically); status bar for any `IStatusBarHost` (rooms, lobby, the hall). |

## Minimal room

```csharp
[Room(RoomIds.Museum)]          // original id (or >= 1000 for a new room)
[ScreenName("museum")]          // optional: -openat-screen museum
public sealed class MuseumRoom : RoomScreen
{
    protected override string Library => "room/museum.gli";
    protected override string Background => "MUSEUM";
    protected override int DefaultDialogPartner => Talkers.Museum;   // reacts to inventory items

    SmackerPerson _man;

    protected override void OnCreate()          // also runs for phone panes: set up characters here
    {
        _man = new SmackerPerson(Assets, App.Audio);
        // index, file, x, y, mood, successors "A<weight>:<clip>" (A always, S stay, E exit), repeat, wait (1/20 s)
        _man.Clip(0, "MU_PutzA.smk", 420, 250, Mood.Idle, "A5:0 E1:1", 1, 3);
        // ...
    }

    protected override void OnRoomEnter() => SetBackgroundFx(0, "Museum.raw", 30000);   // ambience (not for panes)

    public override SmackerPerson PartnerAnimation(int talker, DialogMedium m) => talker == Talkers.Museum ? _man : null;

    public override void Animate(float dt) => _man.Update(dt);   // every frame, also during dialogs/menus

    protected override void Hotspots(InputState input)           // only when no dialog/menu is open
    {
        Hot(380, 200, 520, 400, 10, tooltip: 0);                 // inclusive canvas rect, click id
        Exit(0, 0, 233, 227);                                    // EXIT cursor, click leaves
    }

    protected override void OnClick(int id) { if (id == 10) StartDialog(Talkers.Museum); }

    protected override void DrawRoom(Screen2D s) => _man.Draw(s, RX);   // canvas content, offset by RX
}
```

Leaving: right click (no dialog/menu) or the exit area call `App.Rooms.Leave()`. Esc/F2 open the options,
F3/F4 its load/save pages, Tab the "call it a day" request, `+`/`-` change the game speed.

Room hotkeys (`RoomHotkeys`, rooms-and-ui-1 §7.2): one letter per location in the hall and in every room (German
table for a German install, the English/other table otherwise; e.g. English B bank, O office, P personnel, K kiosk,
G globe); the key leaves all rooms and walks there, the same key twice within 300 ms runs. Gate: after 09:00 while the
day is played, hours 8–17, no call-it-a-day, no dialog/menu, not on the stairs / in the evening / walking to a player.
**While the tutorial runs the room hotkeys, Esc, Tab, F3 and F4 are off** (F2 still opens the options).

Ambience: every room sets the global ambient volume on entry (not as a phone pane) from the central table
`View/Audio/RoomAmbience.cs` (hall 100, rooms 30–60, evening/options 0; rooms without an entry keep the current value).
Override `RoomScreen.AmbientVolume` in a room or call `RoomAmbience.Set(id, v)` for new rooms. It scales the hall's
ambient loops (`HallAmbience`: crowd, planes behind the windows, airfield), the random fly-bys and (squared) the
announcer (`HallAnnouncer`: landings and the half-hourly notice), so they are heard faintly inside rooms.

## Dialogs

A dialog graph is an `IDialogScript` marked with `[DialogScript(talkerId)]` (found automatically). `DialogTree`
writes it as data — node id → action; an id without a node ends the dialog (part 3 §3.3):

```csharp
[DialogScript(Talkers.Museum)]
public sealed class MuseumDialog : DialogTree
{
    public MuseumDialog() : base("Muse")
    {
        Open(d => d.Say(d.Medium == DialogMedium.Phone ? 901 : 900));    // d.Par1 selects special openings
        Choices(new[] { 900, 901 }, 1000, 1003);                          // clicking 900/901 offers 1000..1003
        On(1000, d => d.OpenMenu(new NotepadListMenu(MenuIds.SellPlane), reEntry: 699));
        On(1001, 1003, (d, id) => d.Say(id + 100));                       // id arithmetic
    }
}
```

Session API (`DialogSession d`): `Say(id, args)`, `SayText(virtualId, text)`, `Choices(from, to, ChoiceArgs, args)`,
`PlayerSays(id)`, `ShowNumbers(9990xxx, args)`, `OpenMenu(menu, reEntry)`, `Stay()`, `End()`, `Par1..3`,
`Medium`, `World`, `Player`. Texts come from `misc/dlg_ger.res` (group of the talker), printf arguments are
formatted, voice files are taken from the `[[..]]` markers.

Effects from other domains attach without touching a tree:

```csharp
DialogActions.On(Talkers.Banker2, 101, d => { /* custom effect */ d.Say(161); return true; });
DialogActions.OnOpen(Talkers.Kiosk, d => { d.Say(500); return true; });
```

Start a dialog from a room: `StartDialog(talker, DialogMedium.Air, par1)`; a phone call: `App.Rooms.PhoneCall(talker)`
(needs the talker's room registered: the pane shows that room). New partners: `Talkers.Register(new TalkerInfo { Id = 1000, Group = "Xxxx", ... })`.

## Menus

```csharp
Menus.Open(RequestMenu.Create(RequestMenu.KillItem, new object[] { name }, yes: () => ..., no: null),
           zoomFrom: new Vector2Int(320, 220), minZoom: 0.17f);
```

Subclass `PopupMenu` for new menus: `Type` (MENU_* number), `Size`, `Background` (used for the zoom), optional
`FixedPos`, `Update(input, local)` (use `Hot(l, x1, y1, x2, y2, tooltip)`), `Draw(s, x, y, local)`, `OnRightClick()`.
Number entry: `Host.Calculator.Open(screenPos, value => ...)`.

## The airport hall and the game day

The hall (`View/Airport/AirportScreen`, `[ScreenName("hall")]`) is the base location and an `IStatusBarHost`
(shared status bar, its own menu slot, advisors, Esc/Tab/+/- keys). `GameFlow` (`View/Rooms/Framework/GameFlow.cs`)
links it to the room framework:
- `RoomRequested` (a figure reached a door) → `App.Rooms.Enter(room)` for the local player; computer players are
  handled by the Sim AI (room action, work countdown, then `ReturnToHallCommand`; ai-robots.md §3.2/§5.1); when the
  room stack falls back to the airport `RoomHost` sends `ReturnToHallCommand`. The hall recounts the room occupancy
  (`HallState.UpdateRoomUsage`, rooms-and-ui-9 §5.4) on every enter/leave, so a room a robot is in is "occupied".
- `HallRequest` → the request menus (call it a day, phone book, wrong office, occupied / forbidden room with
  "wait for the room", closed shop). `HallState.UiBusy` / `IsRoomBusy` read the room stack; `RoomHost.WalkHandler`
  walks the figure (filofax, hotkeys).
- Day loop: at 09:00 (clock frozen, `DayState.Morning`) every room is left and the boss office (107) opens the
  briefing; leaving it runs `StartWorkingDayCommand` (clock on, figures placed in front of their offices).
  `EveningStarted` (18:00 or `CallItADayCommand`) leaves all rooms and shows the evening screen (room 8) while the
  night runs; at the next 09:00 the briefing follows. The morning review's results are in `BriefingLog`
  (`Sim/Missions/DayFlow.cs`).
- Test hooks: `-openat-newgame [mission]`, `-openat-timewarp N`, `-openat-walkto ROOM [-openat-walkday D]
  [-openat-walkat MIN]` (walk MIN minutes after 09:00, default 10), `-openat-evening` (call it a day as soon as the first
  working day runs: the evening screen opens through the normal flow), `-openat-debugflow` (also logs ignored hall clicks). Rooms opened directly (`-openat-screen bank`) skip the briefing. Prepared states: `-openat-scenario` (next section).

## Test scenarios (launch into a prepared state)

`-openat-scenario FILE.json` starts a new game and brings it into a prepared state before you see anything:
any day, time, money, inventory, laptop and security state, extra planes, routes and orders. It can also
open a room straight away. The state is built with the normal Sim commands where one exists
(`Sim/Testing/Scenario.cs`), so it is a state the game could reach. The same file and seed always give the
same world, and it also runs headless (tests: `tests/OpenAT.Tests/ScenarioTests.cs`).

```
OPENAT_BUILD_MIRROR=... tools/build/run-shot.sh 640 480 "wait 240; shot a.png; quit" -openat-scenario tools/scenarios/laptop-day3.json
```

Remember that `wait N` counts frames (about 60 per second). Ready-made scenarios in `tools/scenarios/` (own data only):

| File | State |
|---|---|
| `laptop-day3.json` | day 3 at 10:00, top laptop, phone, antivirus and route protection, 4 planes, 4 routes, 5 orders (planned into the plans), 2 freight orders; opens the laptop |
| `laptop-virus.json` | day 0 at 10:00, infected laptop (virus state 1); opens the laptop |
| `laptop-low-battery.json` | day 0 at 10:00, cheapest laptop with 8 minutes of battery (warning at 2, then dead); opens the laptop |

Keys (all optional; JSON with `//` comments allowed):

| Key | Meaning |
|---|---|
| `mission`, `seed`, `homeCity` | new-game settings (mission number as in `Sim/Mission.cs`, city by name); the calendar always starts on the same day |
| `advanceDays` | play N whole days (computer players act; each day is called off soon after 09:00) and stop at the next morning |
| `skipBriefing` | start the working day at once; the figure stays in the hall (no walk to the office) |
| `time` | "HH:MM": run the clock to that time (implies `skipBriefing`) |
| `money` | cash of the local airline |
| `items` | inventory by name: `laptop`, `phone`, `filofax`, `machinegun`, `beer`, `cigar`, `oil`, `postcard`, `pills`, `spider`, `dart`, `diskette`, `bra`, `horseshoe`, `chocolates`, `spikedchocolates`, `paperclip`, `glue`, `glove`, `redbull`, `stinkbomb`, `glowingcoal`, `coal`, `pliers`, `perfume`, `stinkperfume` (or the item number) |
| `laptop` | `{ "quality": 1..4, "battery": game minutes, "virus": 0..3 }` |
| `security` | security company options: `office`, `laptop` (antivirus), `personnel`, `bank`, `routeboard`, `administration`, `planeinterior`, `planeexterior`, `gate` |
| `planes` | `[{ "type": "114", "count": 2 }]`: buy at the broker, type by csv id or name |
| `routes` | rent N routes from the home airport |
| `orders` / `freight` | accept N travel-agency / cargo-office offers |
| `planFlights` | put each accepted order into the first plane that can fly it |
| `room` | open a room afterwards: room id or screen name (`laptop`, `bank`, `office`...) |

Problems such as an unknown name or a refused purchase are logged as `OpenAT scenario:` warnings, and the
rest is still applied. New keys belong in `Scenario.Parse`/`Apply`, each with a test.

## Save games, autosave, campaign progress, rival calls

`View/Session/**` (`SessionServices`, a MonoBehaviour that re-binds to every new `GameApp.Session`):
- `SaveSlots`: slot files `Game0.sav`..`Game11.sav` (11 = autosave) in `Application.persistentDataPath/saves`.
  `Save` records the room stack (`RecordRoomStackCommand` → `Player.RoomStack`) and writes the world;
  `Load` reads it into a new `GameSession`, shows the hall and re-enters the saved top room
  (`RoomHost.Restore`, no walk). Everything else re-binds by itself on the session change: `GameFlow`
  (hall `UiBusy` / `IsRoomBusy`, walk handler, day loop), `AdvisorLayer`, `SessionServices`; letters are sim
  state (`Player.Mail`, Sim/Mail) and keep arriving. Options pages 5/6 (F3/F4) list the slots with
  `SaveGame.ReadHeader` data (tooltip = `Misc` 1150/1151 info line), the save page edits the name on the board.
- Autosave: `AutosaveRequested` sets a flag and cuts the tick batch (`SimDriver.EndBatch`); the file is written right
  after that tick when the autosave option is on; after the 17:00 save the assistant says `Advi` 500.
- Campaigns: the local player's `MissionWon` unlocks the next mission (`MissionProgressStore`, PlayerPrefs,
  per campaign); locked missions are dark on the wizard's mission pages. The game-over menu plays
  `intro/outro.smk` / `intro/outro2.smk` after winning mission 5 / 20, then the main menu.
- Rival calls: `RobotPhoneCall` to the local player rings (`Phone.raw`, game speed 0) and opens
  `RivalCallScreen` (competitor dialog, `Rooms/Rivals/RivalDialog`) when the player is in a room with no
  dialog/menu open; otherwise the call is dropped. Computer figures never open room screens (`GameFlow`
  ignores their `RoomRequested`).
- Test hooks: `-openat-load N` (load slot N at start), `-openat-savedir DIR`, `-openat-rivalcall P` (airline P calls
  once the player is in a room after 09:20), `-openat-unlockall`, `-openat-camdebug` (logs the hall camera), `-openat-motionlog N` (logs N frames of the walking figure and the camera: frame, frame time, ticks, position, view; then a cadence summary), `-openat-motionstress` (editor-like frame timing for it: no vSync, random frame work, one 400 ms hitch), `-openat-nopacing` (feed raw frame times to the sim driver, for before/after comparisons); auto-runner `key f3|f4`, `type TEXT` (injected characters), `live` (stop injecting: the real mouse and keyboard drive the rest of the run, e.g. to test text entry with OS key presses).
  Sweep helpers (`View/Tutorial/TutorialTestHooks.cs`): `save N`, `tutorial` (log the tutorial state), `skipmsg`
  (end the assistant's line), `walk ROOM`, `talkto P` (walk up to airline P's figure), `call T` (phone dialog partner T, e.g. 8/9 the bank clerks), `phone P [mobile]`
  (phone-book call), `bored P`, `strikeend`, `hallcam X`, `worstad P`, `expand` (rent all gates, log the boss's decision),
  `camto P` (centre the hall camera on airline P's figure), `stop` (the local figure stops where it is).
  `-openat-option Name[=false]` switches a boolean `GameOptions` field for new games (e.g. `ComputerPhonePose`).
- Phone pose: while a phone dialog runs over the hall the local figure, when standing, shows the phone frames (the
  original); with `GameOptions.ComputerPhonePose` (addition, off by default) the computer airline on the phone
  (`PhoneCallCommand`, `HallPlayer.PhonePartner`) also stops and shows them.

## Tutorial (mission 0)

The state is sim (`World.Tutorial`, saved). `View/Tutorial/TutorialView.cs` runs the script steps that wait for the
assistant to be silent or for a place (hall → boss office → own office → planner → hall), gates clicks in the office and
planner, draws the glow markers and the status-bar tutorial panel (click or Space cancels). Rooms ask
`TutorialView.BlockClick` / `BlockRightClick` (done by `RoomScreen`) and draw `DrawGlows` (planner: `TutorialBlockOrigin`).
`GameFlow` lets the script run the first morning and defers room windows before step 1200. Spec: docs/spec/findings/tutorial.md.

## Plane designer (125) and aircraft constructor (124)

`View/Rooms/Designer/**`, Sim side `Sim/Data/PlaneDesign*.cs` (rooms-and-ui-7 §3/§6, planes-and-flights.md §5):
- Designs are `.plane` files in the original format (`PlaneDesign.ReadPln` / `WritePln`, CP1252 names) in
  `Application.persistentDataPath/myplanes` (`DesignFiles`, override `OPENAT_DESIGN_DIR`); `PlaneDesignLibrary`
  does listing (case-insensitive alphabetical), prev/next, `data<N>.plane` numbering and the buildable list.
- `PlaneDesignEditor` holds the editor rules without the screen (enabled strip slots, wing skipping, snap search
  with the mirrored twin, placing, cascade removal); `EditorRoom` is the screen (strip, board, stats, rename via
  MENU_RENAMEEDITPLANE, new/delete/prev/next, OK saves, cancel does not, right click never leaves).
- `DesignerRoom` + `DesignerDialog` (group `Desi`): MENU_BUYXPLANE (`BuyXPlaneMenu`, design card on the left page)
  → quantity → `BuyDesignedPlaneCommand`. The catalogue comes from the install (`GameData.DesignCatalog`); the AI's
  `pl45/pl48.pln` designs are loaded into `GameData.MissionDesigns` and given to the AI in missions 45/48.
- Bought designs are drawn behind the hall windows from their parts' `P2D_*` side views (`AirportScreen.Planes`).
- Test hook: `-openat-debug-designplane FILE.plane` turns the human's first starting plane into that design at
  new game (use with `-openat-debug-autoplan human -openat-debug-advance 155 -openat-debug-camera 4600`).

## Widescreen

Rooms draw in a 640x440 canvas at `RX` (centred); side areas are mirrored/dimmed (`WideFill`). The status bar
spans the full width (left group at x=0, icons at the right edge, extra width filled). Menus are centred on
the canvas; bubbles, number card, phone pane and advisors use canvas coordinates.
