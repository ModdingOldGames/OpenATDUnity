# Fidelity pass: OpenAT vs. the original

Side-by-side comparison of OpenAT with the original game (GOG release, "VERSION 1.03", English) running on
the same machine, screen by screen. Screenshots of both games stay outside the repository (game graphics).
All descriptions below are in our own words; text ids refer to the install's resource files.

## Setup

| | Original | OpenAT |
|---|---|---|
| Binary | `At.exe` (GOG), run read-only | player built with `tools/build/build-player.sh` (mirror) |
| Window | 640x480 windowed: command-line switch `/window` plus the Windows compatibility layer "16-bit colour" for `at.exe` (HKCU `AppCompatFlags\Layers`, value `~ 16BITCOLOR HIGHDPIAWARE`; the game refuses windowed mode on a 32-bit desktop otherwise). No extra software. The value was set back to its previous `HIGHDPIAWARE` after the pass; set it again for the next pass. | `-screen-width 640 -screen-height 480`, test hooks `-openat-newgame`, `-openat-walkto`, auto-runner |
| Driving | `um win drive` (clicks, keys), `um win shot` (window capture). The original minimises itself when another window takes the focus; restore with `ShowWindow` and re-size the client to 640x480. Clicks are occasionally dropped: move first, then click. Room walk hotkeys (B bank, E broker, P personnel, T travel agency...) are the fastest way around the hall. | auto-runner script |
| Game | free game, player 1 (blue airline), home airport London | same (`-openat-newgame`) |
| Start day | random: run 1 a Friday in winter (snow, Christmas decoration), run 2 a Monday | Wednesday in these runs |

Legend: **=** matches, **≠** difference, **fixed** changed in this pass, **open** not fixed (reason given).

## Title and boot

| Item | Result |
|---|---|
| Title picture (Deluxe art, 2009 line) | = identical picture |
| Behaviour | **fixed**: the original's title is only the loading backdrop and moves on to the main menu by itself (about 2 s, wait cursor, one loading line at the bottom). OpenAT waited for a click; it now continues after 2.5 s (click/key still skips). |
| Intro video on first start | = (both play `intro.smk` first; skippable) |
| Loading status line at the bottom | open: cosmetic, the original prints hard-coded debug strings |

## Main menu

| Item | Result |
|---|---|
| Board position, characters, rows, bullets, title | = pixel match |
| Version line bottom right of the board (small red font, right-aligned at x 519, y 429) | **fixed**: was missing; OpenAT now prints "VERSION" + its own version number |
| "Network game", "Highscores" rows | ≠ OpenAT shows them dark (not implemented); the original has them active. open (features) |
| Decorative flying planes after ~30 s idle (CKlackerPlanes) | open: not implemented in OpenAT (needs the universal plane part offsets, spec rooms-and-ui-4 §2.8) |

## New-game pages

| Item | Result |
|---|---|
| Player page: portraits, logos, names, BACK/START | = pixel match |
| Home airport page: list, "==>" marker, paging arrows | = |
| Default home airport on first start | **fixed**: English → London (OpenAT chose Berlin). Spec rooms-and-ui-4 §3.1 said "London for Polish"; corrected to English (the code maps English; confirmed in the running game). |

## Start of the game and morning briefing (day 0)

| Item | Result |
|---|---|
| Start sequence | **fixed (part 2)**: on the first morning every figure now walks to the airport manager's office at a frozen 09:00 (camera on the player) and the briefing opens when the player arrives, as in the original; a 60 s safety net opens it directly if the walk cannot finish. Verified in OpenAT; the figures start from OpenAT's day-0 spawn points (the original's come out of the lift, not re-checked). |
| Briefing dialog lines, bubble, characters | = same lines (Boss group), same layout; bubble line spacing **fixed** (see "Fonts") |
| After the briefing | **fixed**: the original walks the player's figure into its own office (camera follows), arriving a few minutes after 09:00. OpenAT left the figure standing in the hall. Human figures now walk to their office after `StartWorkingDayCommand` (unless an office bomb is armed), as in sim-core §2.8 step 5. Computer players keep getting their walk from the AI work cycle. |
| Written briefing card (next mornings) | **fixed**: all lines now use the big grey font (OpenAT used the small white font for the three rating rows); the free game now shows the first mission's label and passenger count (the line was empty); values use the unit formats. Spec rooms-and-ui-2 §4.27 updated. |

## Status bar

| Item | Result |
|---|---|
| Layout, icons, clock and weekday | = |
| Money "1,500,000" | **fixed**: OpenAT always used dots ("1.500.000"). The original takes the thousands separator from the Windows *system* locale (dot, comma, colon, slash, apostrophe or backquote; otherwise a dot), so this English system shows commas. OpenAT now does the same (Windows API, with a culture fallback and an `OPENAT_THOUSANDS` override). Spec airport-and-people (status bar row) updated. |
| Starting money | = 1,500,000 |

## Office

| Item | Result |
|---|---|
| Room picture, character, hotspots | = |
| Envelope in the in-tray and the welcome letter | **fixed**: the original starts with a welcome letter (Lett 1500 subject, 1501 body, 1502 sender; a second one in the rocket mission). OpenAT had an empty in-tray. Added as a new-game step (Sim/Mail); the letter shows the sender line. |
| Letter header names | **fixed (part 2)**: when the wizard closes, the original re-capitalises the player and airline names (first letter and every letter after a space or hyphen upper case, the rest lower case); the split-flap board keeps capitals. OpenAT now does the same in the new-game setup (`World.Recapitalize`, test added), so letters, the briefing card and tooltips show mixed case. Spec rooms-and-ui-4 §3.1 updated. |
| Kerosene info (oil painting) | **fixed**: values were right-aligned; the original prints them left-aligned at x 160 and in the currency format. Numbers: original day-1 market price $535 (OpenAT day 0: $500), tank 100 barrels in both. Open: the help text wraps one word earlier in OpenAT (2-3 px difference in the word-wrap rule; not changed because it affects every text block). |
| First-visit help by the assistant | = implemented; remembered per installation like the original (it no longer showed on this machine because earlier test runs had used it up) |

## Globe / filofax planner

| Item | Result |
|---|---|
| Globe, filofax, tabs, page curls | = |
| Plane list: names red, "0 (needs: 2)" pilots | **fixed** (real bug): the starting crew was hired after the operations step had copied the crew counts onto the planes, so the human's starting planes showed no crew (red in every list, would fly without crew). The crew mapping is now re-run after hiring. Test added. |
| Plane technical page | **fixed**: values were right-aligned with no gaps and an extra condition line; the original prints them left-aligned at x 75 with block gaps and no condition line, passengers as "180 (30)". Exact rows put into spec 5b §4.2. |
| Order list | **fixed**: route text "London - Madrid" (spaced hyphen, was "London-Madrid"); money "$34,500" style (was "34,500 $"). |
| Starting orders | = two orders of 90 passengers from the home airport (destinations and premiums are random) |
| Starting planes | = two Boeing 757-300 "Augsburg" and "Halle" in London, 100 %, same technical data |

## Units and money everywhere

**fixed**: OpenAT hard-coded German-style suffixes ("12,000 $", "6,455 km", "24t"). The original formats every
unit through the install's unit table (`misc/ein_ger.res`, CEinheit), which in the English release puts the
dollar sign in front. Added `UiKit.Unit/Money/UnitFormat` and replaced the hard-coded suffixes in all rooms
and menus (bank, kerosene, Arab Air, broker, museum, workshop, route box, statistics, order counters, world
phone, designer, security, planner, account statement).

## Fonts

**fixed**: the original sets single line spacing on its main fonts (cash, all `norm_*`, `dlgfont1/2/3`,
`norm_drk`); the default 1.5 applies only to the others. OpenAT used 1.5 for all, so dialog bubbles were
50 % taller (30 px per line instead of 20, choices 35 px apart instead of 25) and the choice hit rows were in
different places. Fixed in the font loader; spec rooms-and-ui-1 §4.1b added. Open: "big white" shares
`dlgfont3.mcf` with "big grey" but keeps 1.5 in the original; OpenAT caches one font per file.

## Bank, personnel

| Item | Result |
|---|---|
| Room pictures, characters | = |
| Help text on first visit | = (see office) |

## Broker (plane dealer)

| Item | Result |
|---|---|
| Room, dialog choices | = (after the font fix the bubble matches pixel for pixel) |
| Catalogue list | **fixed**: one column titled "Type" (Schd 1601) with the type name only; OpenAT showed manufacturer + name and a price column. |
| Type card | **fixed**: maker and type on two lines; rows at the original offsets with values left-aligned 70 px in; thrust row added; crew and attendants as plain numbers; no year and no description. Individual plane cards (museum, workshop, sabotage) follow the same layout (condition only for own planes in the workshop). Spec rooms-and-ui-7 §1 updated. |
| Right-click in the catalogue | open (not verified in OpenAT): the original closes the menu *and* ends the dialog |
| Opening hours message | the original refused a visit at 17:13 with an "opening hours" card; OpenAT has the closed-shop menu (not re-checked) |

## Travel agency

| Item | Result |
|---|---|
| Room, notes board, 7 notes | = |
| Codes on the notes | **fixed**: the original shows real airport codes (TXL-LHR, LHR-RUH) because the "real codes" option defaults to on; OpenAT showed the readable codes (LON-BRU) and the option was not wired. City codes are now switchable (`GameData.UseRealCodes`), default on, toggled from the options page; voice lookup keeps the readable code. Spec data-tables updated. |
| Order card on hover | **fixed**: the original card has "From - To" on one line, then date, distance, passengers, penalty and (after a gap) premium, values left-aligned 85 px in; the date reads the weekday or "until" + weekday. OpenAT had the city pair on two lines, a different row order, right-aligned values and a relative date (days from today). Spec rooms-and-ui-6 updated. Open: the original marks own planes on the card's small map; OpenAT draws the route line instead. |
| Offer numbers (original, day 1) | Berlin–London 916 km, 90 passengers, premium $31,320, penalty $20,880; Dublin–London 579 km, premium $18,450; London–Riyadh 4,995 km, premium $85,725. These fit the contract formulas in contracts-and-routes.md (premium per km falling with distance); OpenAT's offers are random, so only the ranges were compared. |

## Evening and next morning

| Item | Result |
|---|---|
| Evening screen | not captured in the original (it passed between two screenshots); open |
| Money after night 0 | original 1,441,950; OpenAT 1,458,259 (different random contracts and start day; same order of magnitude: salaries, gate and branch rent) |
| Briefing card | see above |

## Part 2: rooms not reached in part 1

**How part 2 was compared.** The original could not be shown live this time. The monitor had gone to sleep
or been disconnected while the user was away (Windows reported a 1024x768 fallback display with no monitor
attached), so the game window never drew anything: captures were black or white, with and without the extra
"DWM 8/16-bit" compatibility shim. Display settings were not touched. These rooms were therefore compared
against the original's code facts (positions, art names, sounds, rules), and OpenAT's result was checked
with `run-shot` screenshots. Repeat the live check with the monitor on.

### Museum

| Item | Result |
|---|---|
| Room, rotor animation, miniatures, hotspots, exits | = |
| Keeper and skeleton scene | **fixed**: OpenAT had a simplified skeleton loop and no keeper reaction. Now the full scene from the spec (rooms-and-ui-7 §2): while the keeper listens he may look up or catch the flying skeleton (`MU_Guck`, `MU_GuckZ`, `MU_Hit`), which then grins (`FlyGrinz`) or says "ouch" (`Fly_auah`); the cleaning and turn clips cannot be cut short. |
| Ambience | **fixed**: `museum.raw` now loops continuously (the original's room loop); OpenAT played it only every 10 to 30 s. New `RoomScreen.PlayAmbientLoop`. |
| Plane card on hover | **fixed**: drawn at the tip block's (32,3)/(32,20) origin like the other plane cards. |
| Sell list | **fixed**: columns "name / condition" (Schd 1001/1002) instead of "plane / sale price"; red name only for a plane short of crew or with a problem (the value stays black); red rows first, then by name; the card shows the plane's value. |
| Dialog | **fixed**: the offer line names the plane's price; by phone the second-visit choices (211 to 213) are offered too; no extra paper sound after buying. |
| Open | the original lowers the general ambience volume to 40 % in the museum and 60 % at the kiosk; OpenAT has no global ambience level yet. |

### Kiosk and newspapers

| Item | Result |
|---|---|
| Room, man (awake/asleep), stink bomb, hotspots | = ; three clips made non-cancellable as in the original |
| Snoring | **fixed**: `Schnarch.raw` loops continuously while the man sleeps (was a random repeat). |
| Reading (zoom) | **fixed**: only one paper zooms at a time (original order: middle, left, top; none over the stink bomb); the zoom now scales the fully composed page (OpenAT showed an empty template until fully zoomed; new `Screen2D.SetTransform`); nothing is drawn during a dialog. |
| Page composition | **fixed**: a background photo is chosen before the text and wins over earlier inline pictures; it is placed against the paper's own bottom-right corner; inline pictures follow their headline; all picture x offsets are relative to the text start; the paper-1 fillers and the paper-0 end decorations are drawn; text is clipped, not cut off. Spec rooms-and-ui-7 §8 rewritten in our words (it gave the offsets without saying what they are relative to). |
| Dialog | **fixed**: a caller-chosen line (id 10000 and up) is spoken instead of ending the dialog. |

### Statistics screen

| Item | Result |
|---|---|
| Layout, rows, colours, tooltips, hot rects | = |
| Airline portrait buttons | **fixed**: selected = `SWTCHnON`, hovered = `HI`, both = `SU` (OpenAT had ON and HI swapped); spec rooms-and-ui-8 §11.3 had the same swap and is corrected. |
| `TOPSAVER` strip | **fixed**: always drawn over the buttons. |
| Values | **fixed**: count rows without separators, percent rows with the unit format, share value from each airline's recorded share price of the day. |
| Default period | **fixed**: a new game starts with 7 days (was 30). |
| Zoom step, refresh | **fixed**: the zoom moves (new − current) / 3 · 2 per step; statistics refresh every 8th frame while open. |
| Exit button | **fixed**: EXIT cursor over the logo in this room. |
| Open | the monthly graph's quirks (zero months drawn past the left edge, empty graph in the first 29 days), hover darkening on sum rows, the sheet-arrow art edge cases, shift-click on a sum row. |

### Laptop planner

| Item | Result |
|---|---|
| Globe, icons, warning lights, windows | = |
| Window buttons | **fixed**: prev/next/index/close show frame 1 while hovered and frame 2 while pressed, at the original positions (were always frame 2 and 1 px off); the right index button is drawn on detail pages. |
| Pane switch buttons | **fixed**: only the active type is drawn (`SWITCH1..3`), a hovered other type as `SWITCH4..6` (was inverted); the active one is not clickable; no tooltips; four blank slots. |
| Page number | **fixed**: always shown ("1/1" too) in the title font; the filofax had the same omission. |
| Red plane title, route lock, toggles | **fixed**: crew-short plane titles red on the laptop too; lock only on the route list with route protection; second route-toggle column at x 116. |
| Icons | **fixed**: icons spin up when the laptop opens; hovering speeds them up (not only holding the button); the boost only for a new window; the hide-windows button's hit area. |
| Open | lamp animation (`LapLig*`), antivirus and virus-mode clips, monitor corner bitmaps, the original's window open/close sweep, quick-jump buttons, dragging only the scrollbar thumb, remembered right-pane state, plane pages opened from the globe (3 + photos), button sounds with a dead battery. |

### Boss's order board (Tafel)

| Item | Result |
|---|---|
| Slips, texts, postcard, fly, exits | = |
| City slips | **fixed**: shown only to airlines without a branch in that city. |
| Clicks | **fixed**: slips tested in posting order; the first slip under the mouse takes the click even if it cannot be bid on. Bids and taking the postcard are silent (OpenAT played a paper sound). |
| Spec | rooms-and-ui-6 §4.4 said a second click takes the bid back; in the original it re-bids from the entry state, so nothing changes (OpenAT already behaved like that). Corrected. |
| Open | clearing the route advisor bubble on leaving. |

### Evening screen

| Item | Result |
|---|---|
| Pictures, crossfade hours, sound files | = |
| Sounds | **fixed**: one hourly sound at a time (a new one only when the last has ended). |
| Season change during the night | **fixed**: the night picture stays from the start of the night, only the day picture follows the new season (was both). |
| Open | the entry sound quirk at or before 17:00; the ambience volume set to 0 while the screen is shown. The screen could not be captured in OpenAT with the test hooks (a directly opened evening falls into the morning briefing), so it was checked by code review only. |

### Sounds

Looped room sounds (museum, kiosk) and the extra sounds removed from the museum and the order board are
listed above. The original's audio could not be heard in this pass (no live capture); a listening
comparison remains open.

## Part 3: live check with the monitor back on

The original ran windowed again (same setup as part 1; compatibility value set only for the session and
restored to `HIGHDPIAWARE`). The user was at the machine for long stretches, so the original was only driven
during idle periods. The first attempt was closed from outside (most likely by the user; no crash in the
event log) and was not relaunched until the machine was idle again. Captures were taken during this
session's idle windows. In both programs the first-visit help bubble only shows on a fresh installation.

| Item | Original (live) | Result |
|---|---|---|
| Day-0 start walk | the four figures appear together on the lower floor, walk up to the manager's office as a group, the camera follows, **WAIT cursor** the whole time; the briefing opens on arrival with the greeting line first | walk and briefing = since part 2; **fixed**: WAIT cursor during the walk. Open: OpenAT places the figures at its day-start spawn points, not together on the lower floor; the exact spawn rule is still to be read from the hall spec. |
| Kiosk | papers zoom one at a time and sit **lower** than centred | **fixed**: OpenAT now reproduces the original's vertical offset (half the paper's width instead of half its height; OQ-8 settled in rooms-and-ui-7 §8). Page composition matches (headline, picture, decorations). |
| Museum | plane card on hover | = pixel for pixel, down to the plane, its price and build year (the museum offer is deterministic). |
| Statistics | table, colours, portrait buttons, exit, 7-day caption on day 0 | = (only the random salaries differ). |
| Order board (Tafel) | empty on day 0, pizza flyer, postcard, fly | = |
| Evening | after "call it a day" the season's evening pictures crossfade within a few seconds, **WAIT cursor** | **fixed**: WAIT cursor on the evening screen. The night speed could not be timed exactly (captures are about 0.5 s apart): the original goes from 18:18 to 00:54 between two captures; OpenAT runs about 6 game hours per second. |
| Laptop | not checked live (needs a laptop from the duty-free shop first) | see below |
| Sounds | the capture had no audio; nothing could be judged by ear | open |

### Laptop leftovers (implemented from the original's facts, checked in OpenAT)

| Item | Result |
|---|---|
| Window open/close | **fixed**: the original's sweep. Corner pieces part along the title bar, then run down the sides and along the bottom, and the window fades in. It takes about 1.1 s and runs backwards on close. The spec said 2.25 s; at 20 ticks per second it is 22.5 ticks ≈ 1.1 s, corrected. |
| Lamp | **fixed**: `LapLig*` lamp at (80,403). It flickers when a window opens and blinks on a close, and is hidden with a flat battery. |
| Antivirus / virus mode | **fixed**: `Antivir.smk` with the security company's laptop protection. In virus mode the icons and toggles become the virus clips: clicking them only makes them twitch, and the exit still works. |
| Monitor corners | **fixed**: `VR_EDGE1..4` drawn over the windows. |
| Quick-jump buttons | **fixed**: `JUMP0x` arrows on detail pages (not Info) step to the previous/next element of the list, wrapping round. |
| Scrollbar | **fixed**: only the 29 px thumb can be dragged; the grab offset is kept. |

### Statistics monthly graph

**fixed**: the monthly view now follows the original. Nothing is drawn before 30 days of history; then 11
month points (months without data as zero, running off the left edge), spaced width / (period / 30); the
scale is taken over min(11, ⌈period / 30⌉) months. Hovering any non-heading row (sum rows included) darkens
the other series. Spec rooms-and-ui-8 §11.4 extended.

### Evening test hook

`-openat-evening` calls it a day as soon as the first working day runs, so the evening screen opens through
the normal flow (documented in ROOMS.md). Verified: day picture, crossfade to night, next morning's briefing.

## Laptop (live, both games in the same state)

### Getting OpenAT into a state

`-openat-scenario tools/scenarios/<name>.json` (docs/ROOMS.md, "Test scenarios"). For this comparison:
`laptop-day0.json` (day 0 after the briefing, top laptop, nothing else; the same state as the original
below). Further: `laptop-day3.json` (planes, routes, orders, freight, plans; every laptop page has content),
`laptop-virus.json`, `laptop-low-battery.json`.

### Getting the original into the same state

- **Cheat words** (facts from the original's keyboard handler, described in our own words). Typed as
  capital letters in the running game (no input field needed), they play a "cheat" sound. One of them gives
  the **top laptop with a full day's battery** (it is put into the last inventory slot). Another gives
  10 million, another 1 billion, another fills the free inventory slots with glue; there are also ones for
  zero debts, more image, a test mode and seeing all animations. The words are in the source
  (`Gameframe.cpp`, key handler); they are not repeated here. Caution: while typing in the hall the letters
  also work as walk hotkeys, so type them in a room or close the opening-hours card afterwards.
- **Saves:** the original writes its saves into the install folder, `Savegame\Game<N>.dat` (slot N+1). We
  never copy files there ourselves; the game's own save menu does it.
- **Recipe:** launch `At.exe /window` (with the session compatibility value), "free game", start, click
  through the briefing, type the laptop word, open the laptop from the inventory. Save with F4 in slot 1.
  Done on 20 Oct 2026: **slot 1 "OPENAT TEST" = `Savegame\Game0.dat`** (London, day 0 about 09:56, top
  laptop, the two start planes and orders). A copy is kept in `docs/spec/local/saves/Game0.OPENAT_TEST.dat`
  (md5 501981ed…6a04) to notice if it gets overwritten. Next time: start with `At.exe /window /load 1`
  (the option counts from 1 and loads slot 1 directly; from the source, not tried live yet), or load it from the menu.
- The original's autosave `Game11.dat` is also in that folder (from the earlier sessions; a copy taken
  before this session is `docs/spec/local/saves/Game11.before.dat`).

### Compared page by page

| Item | Result |
|---|---|
| Laptop screen | **fixed**: the monitor was dark blue-grey in OpenAT and is black in the original. The cause was general: the project renders in Linear colour space, and vertex colours (all `FillRect`s and tints) were not converted, so every non-black fill came out brighter than written ((4,8,24) showed as (34,50,86)). `Screen2D` now converts vertex colours to linear, so fills and tints everywhere show the values in the code. |
| Globe, icons, toggles, warning lights | = |
| Window open/close | the original's build-up (corners, bottom, fade-in) as implemented in part 3; it runs within about a second in both |
| Plane list | **fixed**: condition left-aligned at x 123, location line at x 0 in the row's font (was grey and indented) |
| Plane detail, week grid | **fixed**: day labels in the small black font, short weekday names, today first (was the very small font) |
| Technical page | = (fixed in part 1) |
| Crew page | **fixed**: names of the assigned pilots and attendants in the normal grey font at x 18, 13 px apart (OpenAT showed "#1, #2"); missing seats as the "missing" text |
| Index and jump arrows | **fixed**: the jump arrows (`JUMP0..4`; OpenAT looked for the wrong names, so none were drawn) and the left/right index arrows are drawn on every detail page; the blank title-row pieces were drawn over the right title, now under it |
| Quick-jump | = after the fixes: stays on the same page and moves to the next plane; **fixed**: a plane window then has one page fewer (3/4 instead of 3/5), an original quirk |
| Order detail | **fixed**: title "LHR - BCN" (was hidden), full city names at y 1, rows at the original y positions with values at x 75 (were right-aligned, with other spacing) |
| Cities window | **fixed**: the right pane always shows the world map; for the hovered city a star, "Name (code)" in the title bar and population/rent below (OpenAT showed nothing until hovering, and then a different layout) |
| City detail | **fixed**: rows at y 1/14 with values at x 85 (were right-aligned, one row lower) |
| Info window | **fixed**: list title from `Schd 1500` (OpenAT used the expert-reports title) |
| Lamp, battery | lamp clip present in both; the dead-battery screen shows in OpenAT with the low-battery scenario. The original's battery warning was not triggered live (the cheat gives a full battery). |
| Virus / antivirus | not reachable live without a sabotage; implemented from the source facts (part 3) and seen in OpenAT with `laptop-virus.json` |
| Scrollbar drag | only with more than one page; the day-0 lists fit on one page, so not compared live |
| List click rows | **fixed**: the laptop measures list clicks and hover from the original's mouse origin (one pixel above the drawn rows, and one pixel left/right on the left/right page), so a click at the very top edge of the first row opens it, as in the original |
| Thrown out with the laptop open | **fixed**: the maximum stay rule (two game hours in a shared room, rooms-and-ui-9 §5.5) was missing; it now pushes the human out of the room and everything above it, the laptop included, and bars the room for two hours (the "forbidden" note at its door). Test added. |

## Specs corrected

- rooms-and-ui-4 §3.1: default home airport London is for English (not Polish).
- airport-and-people (status bar): thousands separator from the system locale.
- rooms-and-ui-1 §4.1b (new): font line spacing.
- rooms-and-ui-2 §4.27: briefing card fonts and free-game mission line.
- rooms-and-ui-5b §4.2/§4.3: plane technical page layout, spaced hyphen in route pairs.
- rooms-and-ui-7 §1: broker catalogue columns and plane tip layout.
- data-tables city.csv: real codes are the default.
- Part 2: rooms-and-ui-4 §3.1 (names re-capitalised), rooms-and-ui-7 §2 (museum sell list) and §8 (newspaper
  composition and reading), rooms-and-ui-8 §11.3/§11.7 (statistics portrait art, 7-day default),
  rooms-and-ui-6 §4.4 (Tafel second click, hit order, city slips), rooms-and-ui-5b §2 (laptop buttons).

## Test suite

The suite became flaky during the pass (a different test failed on most full runs, once the run hung): the
hall module and the briefing recorder add systems to static factory lists the first time a test class
registers them, while other test classes create worlds in parallel. Worlds created around that moment saw
different system sets ("collection was modified", lockstep differences). Fixed by registering both once in a
module initializer of the test assembly (`tests/OpenAT.Tests/TestAssemblySetup.cs`); five full runs green
afterwards. New tests: welcome letter, starting crew on the planes, human walks into the office after the
briefing (days 0 and 1).

## Notes for the next pass

- Re-add the compatibility value (see Setup), launch `At.exe /window`; restore with `ShowWindow` whenever
  another window was focused, and keep the OpenAT runs and the original's session apart.
- Make sure the monitor is on before driving the original (a sleeping monitor leaves it black).
- Still to compare live: museum, kiosk, statistics, laptop, evening screen, order board (part 2 was code-based),
  game-speed behaviour, sounds by ear.
