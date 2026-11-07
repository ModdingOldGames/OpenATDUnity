# OpenAT improvements

Up to the tag `faithful-baseline`, OpenAT reproduces the original game, with the original's bugs fixed
by default (docs/QUIRKS.md). Later commits add improvements of our own. Each improvement has a
`GameOptions` switch that defaults to **on**. Turning the switch off brings back the original's
interface and behaviour. While the tutorial runs, every improvement is off.

**Switching them off in the game.** Options, Other, "OpenAT extras" lists the improvements with ON/OFF
(`View/Boot/Extras.cs`): planner tools, high-resolution filofax, high-resolution laptop, gamepad and skipping
the start walk. The choice
is kept in the player's settings and applied to every game that starts or is loaded; a change during a game
takes effect at once (through `SetOptionCommand`, so it is part of the game's command stream and its save).

## Flight planner (`GameOptions.PlannerEnhancements`)

The improvements build on the existing planner (globe-room filofax and laptop). The rooms, artwork,
post-it interaction and planning rules stay as they are, and every change still goes through the
same operations commands (`PlaceFlightCommand`, `MoveFlightCommand`, ...).

The canvas is always 480 px high. A wide window gives more width, not more rows, and the planner uses
that width:

| Width of the virtual canvas | Layout |
|-|-|
| below 848 px (e.g. 4:3) | Original layout. The fleet index opens as a popup when you click the plane name of a plane schedule (the title of a laptop plane window, without dragging). |
| 848 px and wider (16:10, 16:9, ...) | The 640 px room moves to the left. A dock column on its right holds the fleet index. A pinned plane schedule takes the lower part of the dock. |
| one more column per extra 205 px (21:9: 2 columns, 32:9: 5 columns) | The fleet index takes the first column. Each further column holds two pinned plane schedules. |

- **Fleet index**: shows the same rows as the plane list (name, condition, location), more at a time,
  with wheel scrolling. It has a name search, a plane-type filter, a state filter (needs attention /
  nothing planned / in the air / on the ground) and a reset button, and shows the result count and an
  empty-list message. While you carry a post-it, planes that cannot fly it are greyed out. Clicking a
  plane shows it in the filofax, or in the front laptop plane window. The open page and the right-hand
  tab are kept.
- **Pinned schedules** (`+` on a fleet row): laptop-style single windows in the dock that show the
  plane's week grid. You can place post-its on them, drag edges and click them like any grid. On a
  compact laptop, `+` opens another laptop window instead. Pins are kept with the player's planner
  state. When the window is narrowed, the extra pins are hidden; when it is widened again, they come
  back.
- **Previous/next plane** (`<` `>` next to a plane schedule's title in the filofax and in pinned
  schedules; the laptop's own jump arrows follow the same order) steps through the filtered fleet order
  and keeps the page. It never switches planes during an edge drag. The laptop's **Arrange windows**
  button cascades its open windows.
- **Readability**: the hovered day and hour are highlighted, a red line marks the current time, and
  automatic ferry flights carry a dotted line, so they do not differ by colour alone.
- **Flight card** (hovering a flight): kind, full city names, departure and landing, the gate (or "no
  free gate" / "away from home"), the original's validity text, the expected income, costs and profit
  or loss, and why the flight cannot be edited (ferry flight, or starting too soon). The money comes
  from `FlightOps.EstimateResult`, which follows the booking at departure: fuel as the original's
  planner estimates it, catering for passenger flights, the order's premium (nothing for a flight that
  breaks its order), a freight order's premium shared by the tons each flight carries, route tickets
  from the plan's current passenger figures (the departure recomputes them), and the original's small
  income of a ferry flight.
- **Precise departure** (Ctrl+click a flight): a card with day and hour steppers. Each step is a
  `MoveFlightCommand` (Shift moves the following flights too, as with edge dragging). The card shows
  the result. A step is committed at once; there is no undo.
- **Refusal notices**: refusals that the original ignored silently now show a reason. A successful
  placement, append or edge drag shows no notice.

### High-resolution planner (`GameOptions.PlannerHiResFilofax`, `GameOptions.PlannerHiResLaptop`)

The globe room's filofax (first switch) and the laptop's planner windows, the docked schedules and
the order counters' planner panel (second switch) keep their size and place but use the window's
real pixels. Their artwork (binder, windows, tabs, week grid, flight bars) is drawn as a smoothed
upscale (Scale3x, sampled bilinearly). Their text is the original's own bitmap fonts, upscaled the same
way and drawn at 8/9 of their size (titles at full size, bar city codes at 3/4) with every glyph on a
real pixel (`ScaledBitmapFont`). The lists therefore hold more rows per page: 9 planes, orders, freight
orders or routes (the original has 6), and 18 cities (the original has 13). Input and layout otherwise
stay in the original's coordinates. The fleet index, the cards and the order counters' info card keep
the original's look.

Texts of our own are in `View/Rooms/Planner/PlannerTexts.cs` (English and German; other languages
fall back to English).

### Planner at the order counters

The travel agency, the last-minute counter and the cargo office show a small planner tab on every
offer ("Try in planner"). Clicking an offer itself still takes it at once, as in the original.
The tab opens a laptop-style planner window in the same room. You do not leave the room for it and
you do not need a laptop. On wide screens the room moves left and the window and an info card fill
a column beside it (only while the panel is open). On narrower screens they form an overlay that you
can move by its title bar. The window first lists the planes that can fly the offer (range, flight
time, seats), those standing at the departure city first, with capacity, location and condition
(wheel or the page arrows to scroll). Clicking one shows its real week; clicking the plane name in
the title comes back to the list. `<` `>` or the mouse wheel on the title step through the planes
that can fly the offer. The offer's date window is shaded as for a carried post-it.
It says whether the plane can fly the offer (range, flight time, seats). It notices when the offer
leaves the board: the trial keeps the offer's content, not only its slot number. Close it with its
close button, Esc or a right click on it.

**Placement trial.** The tried offer hangs on the cursor as a pale, framed post-it. Clicking an hour
places it there, in the plane shown. Clicking the placed trial flight picks it up again, and a right
click drops the post-it. A passenger order has one trial flight. A freight order can have several,
as long as the copy still has tonnage open. Each change is evaluated by `Sim/Contracts/OfferTrial.cs`
on an isolated copy of the world, made by an in-memory save/load round trip. In that copy the offer
is taken with the real accept command and placed with the real `PlaceFlightCommand`, so ferry
insertion, pushed flights, gate planning, the seven-day horizon, opening hours and the planner's
swap of a covered flight all follow the real rules. The panel draws the copy. Trial flights have a
white/magenta frame and flights the trial moved have an orange one. Hovering a flight in the panel
shows the planner's flight card. It also says whether the flight is the trial flight, a ferry flight
the trial adds, or a flight the trial moves (with its current time). The info card lists, for each
flight, the resulting time (and the wanted time when they differ), ferries added or removed, other
flights moved, a missing gate, the original's validity text, a covered flight that would come off
the plan, the planned and open tonnage, and new gate conflicts. It also says that only this plane
and time were tried. When anything the result depends on changes (clock hour, own plans, own
orders, the offer slot), the trial is evaluated again.

The trial never changes the live game. It does not take or reserve the offer, does not touch plans,
gates, money, clock, random state or events, and nothing of it is saved. Making the copy runs the
save hooks, which only refresh cached fields. Closing the panel discards the trial.

**Take and plan.** Once the trial flights are placed and none is refused, the info card offers
**Take and plan**. It takes the offer exactly as clicking its card does (the room's own take, with its
animation), then applies the trial as below. The check after taking must still give what the panel
showed before; otherwise the order stays taken but unscheduled and the card says why. Taking the
offer with the normal card click still works, independent of the trial. After that the card says
"taken, not scheduled yet" and offers **Apply plan**. Apply evaluates the legs again on a fresh copy of the
current live state, now with the real order. It commits them through the same commands only if every
leg succeeds, none takes another flight off a plan, and the result (times, ferries, moved flights,
validity) is still what the panel showed. Otherwise nothing changes and the order stays unscheduled.
A multi-leg freight plan is applied all or nothing. Closing the panel never undoes the taking.
Tests: `tests/OpenAT.Tests/OfferTrialTests.cs`.

## Gamepad (`GameOptions.GamepadSupport`)

For the Steam Deck, the Legion Go and any Xbox or PlayStation pad (the Input System's `Gamepad`). The pad
fills the same per-frame input the mouse and keyboard fill (`View/UI/GamepadInput.cs`), so every screen
works with it. Whatever was used last wins: moving the mouse or pressing a key hands the cursor back.
Outside a game (menus) the switch counts as on.

Buttons can be rebound in Options > Other > OpenAT extras > Gamepad buttons (`View/UI/PadBindings.cs`): choose an
action, then press the button; a button another action had is swapped, and Defaults restores these:

| Control | Action |
|-|-|
| Right stick | Cursor: dead zone, acceleration curve, slower over a hotspot; let go near a target and it is pulled onto it |
| D-pad | Jumps the cursor to the next target in that direction (held: repeats); without targets it acts as the arrow keys |
| Left stick | Walks the figure in the hall (below) |
| A / Cross | Click (held: drag) |
| B / Circle | Back: right mouse button (closes popups, leaves rooms, drops a post-it) |
| Y / Triangle | Fast-travel menu (below) |
| X / Square | Walk to the planner (room hotkey) |
| LB / RB | Previous / next plane in the planner and the counters' panel; elsewhere the previous / next target |
| LT / RT | Scroll (mouse wheel) |
| Start / Options | Esc (options) |
| Select / Share | Tab (end the day) |
| R3 | F1 (tooltip) |
| Touchpad | Cursor to the screen's home target |
| (unbound) | Walk to the own office (room hotkey) |

**Targets.** Screens report their clickable rects every frame (`View/UI/PadTargets.cs`): room hotspots
(`RoomScreen.Hot`), split-flap menu lines, dialog answers, the status bar, doors and counters in the hall,
the filofax tabs, planner list rows, page and plane buttons, fleet rows, the counters' planner panel and
every hour cell of a visible week grid. The target under the pad cursor gets a thin frame. Screens
without reported targets are still played with the stick cursor.

**Scheduling with the d-pad.** A on a plane in the plane list opens its week and puts the cursor on the first
row of the order list. A on an order takes the post-it and the cursor jumps onto the week grid where the
post-it starts at the first free hour after the plane's last planned flight (at least two hours from now).
Left / right move it by one hour, up / down by one day, A places it (the normal placement command, with
ferry flights and gates), B drops it; either way the cursor goes back to the list row. LB / RB switch the
plane, keeping the post-it. The hour cells are targets only while a post-it is carried; otherwise each day
row and each planned flight is one target, so left from an order reaches the day row beside it, and left /
right then step between that day's flights (A picks a flight up to move it). A jump always lands on the
centre of the smallest target under it, so A never hits a flight's edge (which would start an edge drag).
Navigation prefers targets in the current row (or column, for up / down); others count 40 pixels further away.
The DualShock touchpad press puts the cursor on the screen's home target (the planner: the closed filofax's
corner, else the first plane, or the first order when a plane's week is open), else in the middle of the
screen; the pad cursor also starts in the middle when the mouse has not been used. The closed filofax and the
globe's turn buttons are targets too; opening the filofax with the pad puts the cursor on the first plane.

**Focus.** Every screen can name a home target; when the screen changes, the pad cursor goes there (the main
menu and the other split-flap pages: their first line, the player page: Start, the planner and the counters:
see below). A popup takes the focus: request menus, forms, the notepad and phone-book lists, the calculator and
dialog answers register their options, the cursor jumps to the first option when the popup (or a new set of
answers) appears, and the d-pad then moves only between the popup's options. The status bar is reachable only
with Down from the room (and costs extra distance), so the d-pad does not drift into it; from the bar, Up goes
back to the room's hotspots. B leaves a room wherever the pad cursor is (B held on the clock does not speed up
time; that stays a mouse action).

**Skipping the start walk** (`GameOptions.SkipStartWalk`, on). On the first morning of a new game all figures
walk to the boss office; Esc, Space, a right click or the pad's B / Cross skip it and the briefing opens.

**Fast travel** (`View/UI/FastTravelMenu.cs`). The fast-travel button (Triangle / Y) opens a radial menu of the places the walk-to-room hotkeys
reach (the language's hotkey table; names from the game's own tooltips, the planner and the telescope in our
words). The left stick, the right stick or the d-pad points at a place, Cross sends the figure running there
(it leaves the current room first, as the hotkey pressed twice does), Circle or Share closes the menu. Game time
holds while it is open, and it opens only when the room hotkeys are allowed (not in the tutorial, not before
09:00, no dialog or menu open). A closed place answers as with the hotkey (e.g. its opening hours).

**Counters with the pad.** The touchpad goes to the planner tab of the first offer a plane can fly; Cross
opens the panel with the cursor on the first plane that has a free gap for the offer; Cross puts the trial
post-it on that plane's first fitting gap; Cross places it and the cursor moves to "Take and plan"; Cross takes
and schedules the offer.

**Walking with the left stick** (`Sim/Hall/HallStick.cs`, `StickWalkCommand`). The figure walks in eight
directions towards a tile a few steps ahead, with the original's steering, dodging and doors. The pace
follows the tilt: 25 % of a walking step per tick when barely pushed, the normal walk at 60 % tilt, then
running that keeps getting faster the further the stick goes, up to three steps per tick (as fast as the
original's coffee boost) when pushed fully. Letting go stops the figure on its tile. A
click or a walk to a room ends stick walking. The command goes through the simulation like a click, so
the game stays deterministic. Tests: `tests/OpenAT.Tests/HallStickTests.cs`.

**Stick-walk camera** (`GameOptions.PadFollowCamera`, on; *Options > Other > OpenAT extras > PAD CAMERA*). While
the figure walks with the stick, the hall camera drops the original's delayed, inertial scrolling (which waits
until the figure nears the edge and then speeds up slowly). Each camera step moves with the figure and closes 35 %
of the remaining gap to the screen centre, so a walking figure stays centred and an off-centre one is centred
within about half a second. At the hall's ends the view stops at the edge as before. After the stick is let go the
camera finishes centring and the original camera takes over again; mouse walks and edge scrolling keep the original
camera (`View/Airport/AirportScreen.cs`, `FocusCamera`).

The auto scripts have a `pad lx ly rx ry [buttons]` command that feeds a virtual gamepad (tests).

