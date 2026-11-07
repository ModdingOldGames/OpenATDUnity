# Rooms & UI 5 — Office, Globe/Filofax room, Laptop (Notebook), globe rendering

Clean-room behaviour spec. Written in our own words from a reading of the original source (German identifiers), the SDL community port, and the manual (pp. 11–12, 25–29, screenshots checked).
Companion file: `rooms-and-ui-5b-planner-blocks.md` covers the Block windows, the flight planner grid, drag-and-drop and the sim commands it issues. Read both.

Conventions
- Coordinates are in the original 640×480 frame. Rects are `(x1,y1)-(x2,y2)` and inclusive. "Block-relative" means relative to the window's top-left `ScreenPos`.
- **[WS]** marks a hard 640×480 assumption that OpenAT must handle for widescreen.
- Texts are cited as `std_ger.res` (StandardTexte) `group id`. The groups are `Tool` tooltips, `Advi` advisor messages, `Schd` scheduler, `XPrt` expert, `City`, `Plne`, `Auft`, `Rout` and `Tuto`. The tooltip ids passed to the mouse-look call refer to the `Tool` group (confidence: high).
- Confidence tags: **[H]** read directly in the code, **[M]** inferred, **[L]** guess.

---

## 1. Office (`CBuero`) — ROOM_BURO_A + 10·player

Traceability: `Buero.Cpp: CBuero::CBuero, OnPaint, OnLButtonDown, OnRButtonDown`.

### 1.1 Entry points
- Walking into the player's own office door at the airport.
- Hotkey: **O** in English and other languages, **B** in German and Dutch, **O** in Portuguese. The player walks to the office. (`Gameframe.cpp: GameFrame::OnKeyDown`) [H]
- Hotkey **G** for the globe. If the player is not already in the office, they walk there with a "walk-to-globe" flag set. On arrival the game enters ROOM_GLOBE straight away, skipping the office view (`Player.cpp` entering logic, `WalkToGlobe`). If the player is already in the office, the globe opens at once. [H]
- Hotkey guards: game time later than 09:00 and hour between 8 and 18. Not in the tutorial, not after call-it-a-day, no dialog or menu open, not on the stairs, not in the evening room. Pressing the same key twice within 300 ms sets a "run" flag. [H]
- Mobile phone (ITEM_HANDY) used from the status bar while in the office does not open the phone book directly. It triggers the office phone animation sequence (§1.5). [H]

### 1.2 Assets
- Background: `room/buero_a.gli` … `buero_d.gli` (letter = player index 0..3), bitmap `BUERO`. Overlays come from the same library: `LETTERS`, `DOOR_O` (opaque door frame), `DOOR_T` (translucent door glass), `NOSABO` (anti-sabotage badge). Header lists: `glbueroa.h`…`glbuerod.h`. (`buero.gli` / `glbuero.h` is the older 4-bitmap variant.)
- Dark ("power cut") overlay: `room/buerodrk.gli`, bitmap `BUERO`, full screen. It is loaded lazily when the player's `OfficeState==3`. [H]
- Player character Smacker clips. `N` = player index + 1. The clips have no sound unless a `.raw` is listed.
  | # | file | role |
  |---|---|---|
  | 0 | `PL{N}Wait.smk` | idle loop |
  | 1 | `PL{N}Blat.smk` (+`PLxBLAT.raw`) | idle variant: leafing through papers |
  | 2 | `PL{N}Scri1.smk` (+raw) | idle variant: writing |
  | 3 | `PL{N}Scri2.smk` (+raw) | idle variant: writing 2 |
  | 4 | `PL{N}Brief.smk` | picks up letter → at end opens the letters menu |
  | 5 | `PL{N}BrifW.smk` | reading-letter wait loop |
  | 6 | `PL{N}BrifZ.smk` | puts letter down → at end closes the menu |
  | 7 | `PL{N}Telef.smk` (+raw) | picks up phone → at end opens the phone book |
  | 8 | `PL{N}TeleW.smk` | phone: listening loop |
  | 9 | `PL{N}TeleR.smk` | phone: talking loop |
  | 10 | `PL{N}TeleZ.smk` (+raw) | hangs up → back to idle |
  All clips are drawn at offset (40,150), the same for every player.
- Idle transitions: from idle, the clip mostly repeats idle and sometimes picks Blat, Scri1 or Scri2. Each variant returns to idle. With "letter" mood the chain is 4→5 (loop)→6→0. With "listening/talking" mood the chain is 7→8/9 (loop on mood)→10→0. Clips 1–4, 6, 7 and 10 cannot be cancelled. The exact transition weights live in the clip definition strings (CSmackerPerson format, see the Smacker-person spec). [H for the chain, M for the weights]
- Player 2 only (index 1): pendulum clock `Uhr.smk` + `Uhr.raw` at (420,109), looping. [H]
- Sounds: ambient volume is set to 40 on entry. Nothing else in this room.

### 1.3 Paint order
1. Room background. Then the clock animation (player 2 only).
2. "Door window" people. Clip rect (539,67)-(639,289). Every person of a player clan (players 1–4) standing near this office's door rune at the airport (|Δx|<70, at or below the door) is drawn with the large clan sprite. The position is (595,289) minus the person's offset from the door. The look direction is mirrored (dir 1↔3, and dir 8 shifts the phase by 2), so you see airport passers-by from inside. [H] **[WS]**: this is a hard-coded window rect.
3. `DOOR_O` at (521,53). `DOOR_T` blended translucently (level 2) at (542,67).
4. `NOSABO` at (390,309) if anti-sabotage security flag bit 5 is set. [H]
5. `LETTERS` at (351,390) if the player has at least one letter.
6. Player Smacker clip.
7. If `OfficeState==3`: dark overlay at (0,0) over everything (hotspots still work).

### 1.4 Hotspots (checked only when no dialog or menu is open; first match wins)
| rect | cursor | tooltip `Tool` | action |
|---|---|---|---|
| (535,52)-(639,292) | exit | — | leave room (back to the airport) |
| (275,162)-(386,236) | hot | 4400 | **Globe** → enter ROOM_GLOBE |
| (301,66)-(416,160) | hot | 4401 | **Kerosene** → opens menu MENU_KEROSIN, zoom origin (357,107) |
| coat rect (see below) | hot | 4403 | **Call it a day** → hint "Feierabend" if day>1, then request menu CALLITADAY, zoom (480,170) |
| (318,368)-(450,431), only if letters exist | hot | 4404 | **In-tray** → set player mood "letter" (clip 4 opens MENU_LETTERS, zoom (379,382)) |
| (322,302)-(432,368) | hot | 4405 | **Phone** → mood "listening"; clip 7 end opens MENU_FILOFAX page 2 (phone book), zoom (363,321) |
| (396,279)-(490,359) | hot | 4402 | **Equip planes** → enter ROOM_PLANEPROPS, only if the player owns ≥1 plane |
Coat rect depends on `Sim.localPlayer` (note: local player, not the room owner): player 0 → (396,42)-(533,173); players 1–3 → (454,102)-(524,266). [H]

Other click rules
- Left clicks are ignored while clip 6 (letter put-down) is playing. Right clicks are ignored too.
- Right click: if a menu is open → menu right-click. Otherwise, if no dialog is open and y<440 → leave room.
- Tutorial: before step 1401 the globe click does nothing. Up to step 1401, clicking anything else in the office shows a girl-advisor message instead: `Tuto 1520` for the door, `1521` for the letters, `1522` for anything else. At step 1401, right click is disabled.
- On every paint: if `Sim.Date>4`, a hint "Rick" is given (hint system). [H]

### 1.5 Phone flag protocol (behaviour)
Clicking the phone sets the phone flag to "requested" and the mood to listening. When the pick-up clip ends, the clip toggles a bit in the flag. "Requested + toggled" means open the phone book. A different value means "the player is being called" (from network or AI code), which only clears the flag. `AtNet.cpp` checks this flag to see whether the office owner is mid-call. [M]

### 1.6 Office states (`PLAYER.OfficeState`)
- 0 normal.
- 1 bomb planted (sabotage). The victim is forced to walk to the office; at the door it explodes → 2.
- 2 exploded. `expl.smk` and `explode.raw` play, and the airport door bricks are swapped. The room may then sell a "coal" item.
- 3 power cut. Another player used pliers (ITEM_ZANGE) on this office door while it was in state 0. The office and globe get the dark overlay, and the airport door brick changes. No reset was found except a new game. [H for setting it, M for permanence]
- **[WS]** The dark overlay is a 640-wide bitmap. In the globe it is blitted twice (at x=0 and x=500) to cover the wider area. OpenAT should draw a full-screen darkening tint instead.

---

## 2. Globe room / Filofax (`CGlobe`) — ROOM_GLOBE

Traceability: `Globe.cpp: CGlobe::CGlobe, OnPaint, OnLButtonDown, OnLButtonUp, OnRButtonDown/Up, OnLButtonDblClk`. Shared logic is in `Planer.cpp` (see 5b).

### 2.1 Entry and exit
- Entered from the office globe hotspot or the **G** hotkey. Exit returns to the office (room stack).
- Exit hotspots (only when the cursor is not over the filofax and no menu or dialog is open): (495,241)-(640,440) and (540,0)-(640,320). Both mean "leave". During the tutorial up to step 1506 leaving is blocked. [H] **[WS]**
- Right click outside any block, with y<440 → leave room.

### 2.2 Assets
- Background: `room/globe_aa.gli` … `globe_ad.gli` (per player), bitmap `GLOBE`. It also contains `GLASS`, which is loaded but never drawn (unused).
- `room/globe.gli` (`glglobe.h`) is loaded but not used for the globe room's visuals. The laptop uses it (icons, highlight, monitor edges).
- `room/globe_a.gli` (`glglobea.h`), the filofax kit. Contents:
  - `FILOFAX` (the double-page notebook)
  - `NEXT` / `BACK` (dog-ears)
  - `INDEX0..3` (left tab strips): 0 = none, 1 = planes, 2 = cities, 3 = info
  - `INDEXR_1..3` (right tab strips): orders / freight / routes
  - `INHALT` (contents flag)
  - `FILOTOP1/2` (curled page tops)
  - `FILOECKE` (filofax corner shown when closed) and `FILOECK2` (copy-protection variant)
  - `KARTE` (small world map used for city tips)
  - `TIMETAB1/2` (flight-plan grid backgrounds)
  - `PL_B00..08`, `PL_V00..08`, `PL_R00..08`, `PL_J00..08`: map plane sprites per player colour. Frame 0 = on the ground, 1..8 = heading in 45° steps.
  - `LOCK` (padlock)
- From the shared std library (`glstd.h`): `ID_01` / `IE_01` (globe turn arrows, hover images) and `CITYCUR` (city marker).
- Earth texture: `gli/EarthGlo.lbm` (8-bit, palette from `EarthAll.lbm`). Shading: `gli/shade.pcx`.
- Dark overlay `buerodrk.gli` when `OfficeState==3`.
- Sounds: `Quietsch.raw` (squeaky globe, looping while turning, unless the globe has been oiled). `gMovePaper` (shared paper-rustle FX) when the filofax opens or closes, or a page or index changes. Ambient volume 40.

### 2.3 Layout and paint order
1. Background.
2. Globe drawn with its top-left at (141,16) (`GlobeOffset[0]`). The globe bitmap is 381×381, blitted at offset −(5,5). The visible sphere has radius 184. **[WS]**
3. Dark overlay if state 3.
4. If the filofax is visible: block 0 (the only block in this room) at its `ScreenPos`. Pressed-button overlays for "extend" and "delete" are drawn when hovered (see 5b). Then planner post-paint overlays (grid highlighting).
   If the filofax is hidden and the office is not dark: `FILOECKE` at (0, 440−h), the bottom-left corner. **[WS]**
5. Turn-arrow hover images: `ID_01` at (353,397) when hovering (353,397)-(393,417). `IE_01` at (247,396) when hovering (247,396)-(287,416). These are only active when the cursor is not over the filofax.
6. The carried "post-it" (flight being placed) follows the cursor. It snaps to the grid when over the schedule (5b).

### 2.4 Filofax open/close
- Visibility flag `FensterVisible`. It persists per player (`GlobeFileOpen`) across visits. In the tutorial it is always open.
- Closed → clicking the corner (x < corner width and 440−h < y < 440) opens it, with the paper sound.
- Open → right click on the filofax outside both page client areas closes it (paper sound).
- Default first-time block 0: position (80,100). Left side = plane list (type 2, index mode); right side = orders list (type 3).
  In the tutorial the left side starts empty (type 0) and the right side is type 4 (routes). [H, odd but literal]
- Clamp: x ∈ [−w/2, 640−w/2], y ∈ [−20, 400]. **[WS]**: use the real view width.
- Dragging: press inside the block-relative strip (20,0)-(481,49) (the binder top), then move. Same clamp.
- Hit-testing the filofax uses its bitmap mask: pixel index 255 counts as transparent.

### 2.5 Globe interaction (globe room)
- Hold LMB on the right arrow rect (353,397)-(393,417) → rotate by −1000 angle units per frame. Hold on the left arrow (247,396)-(287,416) → +1000 per frame. 65536 units = 360°, so 1000 ≈ 5.5°.
- While turning, the squeak sound loops (effects volume) unless `GlobeOiled`. Oil is applied by using ITEM_OEL from the inventory while in this room.
- Clicking one of **your own** planes on the globe (19×19 hit box, see §4.4) opens the filofax if hidden. It switches the left page to that plane's detail (page 0 = flight plan), with page count 3 + type photos. Only own planes are shown in the globe room.
- When a city detail page is opened, the globe auto-rotates towards that city (§4.3) and shows the city marker.

### 2.6 Keyboard
None specific. Keys go to the generic room handler (global hotkeys).

---

## 3. Laptop / Notebook (`CLaptop`) — ROOM_LAPTOP

Traceability: `Laptop.cpp: CLaptop::CLaptop, OnPaint, OnLButtonDown, OnLButtonUp, OnRButtonDown/Up, OnLButtonDblClk, UpdateWarningLightModes`.

### 3.1 Entry and exit
- Clicking the laptop item (ITEM_LAPTOP) in the status-bar inventory, from any room, when no dialog or menu is open (`Stdraum.cpp` inventory click). It is a pseudo-room pushed on the room stack, and exit returns to the previous room.
- Bought at the Duty Free shop. Each purchase raises `LaptopQuality` 1→4.
- Exit: the exit icon (rotating) or the physical button (37,398)-(74,439) (sound `laptop1.raw`), or right click with y<440 outside any block.
- Selling or discarding the laptop while inside forces leaving.

### 3.2 Battery
- `LaptopBattery` is in game minutes. It is reset every new day according to quality: 1→40, 2→80, 3→200, 4→1440. It is also set on purchase. [H]
- While in the laptop, it drops by 1 per game minute. At 2: warning sound `lapwarn.raw`. At 0: all screen bitmaps are released, the background switches to `globe_c.gli` / `VR2` (dead screen), and `lapdown.raw` plays.
- Battery ≤ 0: nothing but the physical buttons and exit reacts. If the laptop is entered with an empty battery, `globe_c` is used straight away.

### 3.3 Assets
- Backgrounds:
  - `room/globe_b.gli` / `VR`: working laptop. This library also holds all window parts (`glglobeb.h`, 104 bitmaps):
    - `WINDOW` (single window), `BIGWIN` (double window), `BACK1..6` (wallpapers per block type)
    - `CLOSE1/2`, `DWNLEFT1/2`, `DWNRIGT1/2`, `UPLEFT1/2`, `UPRIGHT1/2`
    - `INHALT0..2` (contents button), `JUMP0..4` (prev/next element), `SWITCH1..7` (right-pane type switches + blank)
    - `KARTE`, `LEFT1/2`, `RIGHT1/2` (corner pieces for the open animation)
    - `PLANE`, `PLANE_[BVRG][01]`, `ROUTE`, `ROUTE_[BVRG][012]` (globe display toggle buttons)
    - `SCROLLL/SCROLLR` (scroll thumbs), `WARNING1..3`, `LOCK`, `PL_*` plane sprites
  - `globe_c.gli` / `VR2`: battery empty.
  - `globe_d.gli` / `VR3`: copy-protection triggered. Shows nothing usable.
- `room/globe.gli`: `HELL` (icon highlight), `EXIT_000`, `CITY_000`, `PLANE_00`, `AUFTR_00`, `ROUTE_00`, `INFO_000` (static icon frames), `VR_EDGE1..4` (monitor corner masks).
- `room/icon1..6.gli`: 51-frame rotating icon animations. icon1 `EXIT_*`, icon2 `CITY_*`, icon3 `PLANE_*`, icon4 `AUFTR_*`, icon5 `ROUTE_*`, icon6 `INFO_*`. Only 1, 2, 3 and 6 are shown.
- Earth texture `gli/EarthLap.lbm`, or `EarthVir.lbm` when infected.
- Smacker: `LapLigV2.smk` / `LapLigh.smk` / `LapLigW1.smk` (screen lamp flicker) at (80,403). `Antivir.smk` at (522,57), shown if security flag bit 1 (antivirus) is set. Virus mode: `TDMStand.smk` / `TDM.smk` (+`tdm.raw`), `VirF_GLU/TOU.smk`, `VirR_GLU/TOU.smk`, `ok.smk`.
- Sounds: `laptop.raw` (power-on, on entry), `laptop1..4.raw` (physical buttons), `lapwarn.raw`, `lapdown.raw`.

### 3.4 Screen geometry
- The monitor content area is clipped to (39,29)-(600,409). The four `VR_EDGE` corner masks are drawn on top at the monitor corners. **[WS]**: the monitor is part of the background art. In widescreen, keep the laptop as a centred 640×480 panel or re-skin it; do not stretch.
- Globe top-left at (177,29) (`GlobeOffset[1]`), same 381×381 globe.
- Icons (top-left positions; hover box = `HELL` size; tooltip `Tool 3000+i`):
  | i | icon | pos | action |
  |---|---|---|---|
  | 0 | Exit | (545,353) | leave |
  | 1 | Cities | (63,121) | open/raise city list window |
  | 2 | Planes | (84,49) | open/raise plane list window |
  | 5 | Info | (55,197) | open/raise info (expert) window |
  | 10 | Turn globe | (136,225) | hold → −1000 per frame |
  | 11 | Turn globe | (555,225) | hold → +1000 per frame |
  Icons 10 and 11 use the shared std bitmaps `IB_00` / `IC_00` (2 frames: normal/hover). Orders and routes have no icon on the laptop; they are reached through the plane window's right pane.
  Icon rotation: each icon spins (51 frames, frame = rot>>10 mod 51). Speed decays by 20 per frame. Holding the mouse on an icon adds 2× the elapsed ms (cap 1600). Opening a new window adds +1000. Icon libraries load lazily the first time an icon spins. [H]
- Globe display toggles (hidden while any block is under the cursor):
  - Planes button 2×2 at (66,277), cells 23 px: player 0 (66,277), 1 (66,300), 2 (89,277), 3 (89,300). Click toggles `DisplayPlanes[p]`. Tooltip `Tool 3006` with the airline name.
  - Routes button 2×2 at (92,348): p0 (92,348), p1 (92,371), p2 (115,348), p3 (115,371). Click cycles `DisplayRoutes[p]` 0→1→2→0 (off / route level 1 / route level 2; per the manual, short vs long routes). Tooltip `Tool 3007`.
  - Other players' cells only work if the player has an informer (BERATERTYP_INFO), and are disabled for bankrupt players. On entry, if there is no informer, all foreign toggles are cleared; bankrupt players' toggles are always cleared.
  - Overlays: base `PLANE` at (66,277) and `ROUTE` at (92,348). Active states use `PLANE_xx` / `ROUTE_xx` per colour (B,V,R,G = player 0..3). Hover highlights are drawn as well.
- "Windows on/off" physical button: (608,312)-(638,350) toggles visibility of all windows (sound `laptop3.raw`). Physical buttons (74,398)-(111,439) and (608,358)-(640,394) only play `laptop2.raw` / `laptop4.raw`.
- Warning lights (from `UpdateWarningLightModes`, recomputed after every click):
  - `WARNING1` at (59,40): open passenger orders exist → on; any due today → blink.
  - `WARNING2` at (48,61): the same for freight.
  - `WARNING3` at (60,82): on if any route is rented.
  - Blink = visible while (ms & 1023) < 512.

### 3.5 Windows (Blocks) in the laptop
Several windows can be open at once (block ids 1..19). Full lifecycle is in 5b §1. Laptop-specific points:
- Clicking an icon: if a window of that type already exists **in index mode**, it is raised to the front. Otherwise a new window opens at the remembered position for that type, with the open animation and the lamp flicker.
- Default positions (`GlobeWindows`, laptop icon position + (60,−33)): cities (123,88), planes (144,16), info (115,164). Slots 3 and 4 (orders, routes) are computed but unused. When a window closes, its position is stored back into its type's slot. This lasts for the session only and is not saved.
- Clicking your own plane on the globe always opens a **new** plane window at the click point, showing the flight plan with the orders list on the right. It is clamped so the whole window fits inside (39..600, 29..409).
- Clamp while dragging or after load: x ∈ [49−w/2, 600−w/2], y ∈ [29, 380]. **[WS]**

### 3.6 Virus mode (`LaptopVirus` ≠ 0)
- Earth texture `EarthVir.lbm`. The globe is fixed at angle 8000 on entry. All windows except block 0 are deleted.
- No planes are drawn on the globe, and plane clicks are disabled.
- The icons are replaced by Smacker gags. City icon → `TDM` at (67,128). Planes icon → `TDM` at (88,53). Info icon → `TDM` at (58,207). Each is a standing loop that plays the full clip when clicked.
- The planes-toggle buttons play `VirF_GLU` → `VirF_TOU` at (68,278). The routes toggles play `VirR_*` at (94,349). The exit icon is replaced by `ok.smk` at (545,359).
- Cured by using a diskette item (ITEM_DISKETTE). This forces leaving the laptop if inside.
- Virus state 2 becomes 3 overnight. AI players are cleared. Infection comes from the sabotage menu (`Aufsicht.cpp` case "virus") or a dialog. [H]

### 3.7 Laptop vs filofax (behavioural differences)
| aspect | Filofax (globe room) | Laptop |
|---|---|---|
| Where | office only | anywhere (inventory item) |
| Windows | exactly one double-page block (id 0) | up to 19 movable windows, z-ordered |
| Close | right-click hides the whole filofax | right-click or close button closes that window (animated) |
| Paging | dog-ear prev/next, auto-repeat | arrow buttons, scrollbar drag, auto-repeat, quick-jump prev/next element |
| Right pane switch | tabs at the bottom of the right page | 3 switch buttons in the title bar |
| Globe data | own planes only; own selected plane's flights | any player's planes and routes via toggles (informer needed); rented cities shown as dots |
| Power | none | battery minutes; empty = dead screen |
| Sounds | paper rustle on page actions | no paper sound |
| Text fonts | small black titles | green titles (`FontNormalGreen`), wallpapers per window type |
| Photos | `.gli` city photo libs | `.glj` city photo libs (`gli/*.glj`) |

---

## 4. Globe rendering (shared by both rooms)

Traceability: `Planer.cpp: PaintGlobe (free function), CPlaner::PaintGlobe, PaintGlobeRoutes, PaintGlobeInScreen, TurnGlobe, DoPollingStuff`; `Init.cpp: InitGlobeMapper`; `Misc.cpp: EarthProjectize`; `Planetyp.cpp: CPlane::UpdateGlobePos`; `Sim.cpp` (plane position update every 4 game minutes).

### 4.1 Sphere texture mapping (orthographic, precomputed)
- The earth texture is a 512-wide equirectangular 8-bit image, roughly 500 rows. EarthGlo, EarthLap and EarthVir are variants; the palette comes from EarthAll.
- The sphere has radius 184 (diameter 369). For each screen row y (0..368), the half-width is `xs = sqrt(184² − (y−184)²)`.
  - Row → texture row: `250 + asin((y−184)/184)·(2/π)·250`.
  - Column x in [−xs, xs] → texture column offset: `128 + asin(x/xs)·(2/π)·128`. This spans 256 texture columns, which is half the texture (a hemisphere).
  - Rotation: add `EarthAlpha >> 7` (0..511) to the column and wrap modulo 512.
- Lighting: per pixel, a 0..63 light level is taken from `shade.pcx` (same screen geometry). The final colour comes from a 256×64 mix table: each palette colour scaled by (level+5)/40, with blue overflow added to red and green (a specular highlight). Pure black is replaced by 0x000008 so it is not treated as transparent.
- Output goes to a 381×381 off-screen bitmap, drawn at a 5 px inset. It is only re-rendered when rotation changes or overlays change.
- OpenAT suggestion: a shader on a quad, or a real sphere mesh with an orthographic camera. The angle convention must match exactly so that plane and city projections line up (§4.2).

### 4.2 Lon/lat → globe pixel (`EarthProjectize`)
- Inputs: city `GlobusPosition` (x = longitude −180..180, y = latitude-like value) and `EarthAlpha`.
- `u = (lon+180)·65536/360 − EarthAlpha + 16384 − 1250` (16-bit wrap).
- `py = 190 + 185·sin(lat·2.65/240·π/2)`, `px = 190 + sin((u/32768 − 1)·π)·sqrt(184² − (py−190)²)`.
- The point is on the back side if 16386 < u < 49152 is **false** (the function returns "behind" = TRUE in that case). Lines and dots are drawn only on the front side.
- The constant 2.65 is a latitude stretch. [H]

### 4.3 Rotation
- Manual: ±1000 per frame while a turn control is held (§2.5, §3.4).
- Auto-target: opening a city detail sets the target to `(lon+170)·177 − 16000 + 1300`. The original uses integer 3200/18 = 177, not 182. Each frame the angle moves 2000 toward the target by the shorter direction, and snaps when the difference is under 2000. Routes are only redrawn once the target is reached. [H]
- The angle is stored per player (`EarthAlpha`, saved), so the globe and laptop share the orientation.
- After any rotation, plane screen positions are recomputed for the visible players.

### 4.4 Overlays
- **Planes**: sprite set per owner colour. Frame 0 when on the ground; when flying, 1..8 from the heading of the current leg (angle + 22.5°, 8 sectors). Position = globe origin + projected pixel − (16,19).
  - Each plane gets a ±1 px jitter from the first two characters of its name, so planes at the same spot do not overlap perfectly.
  - Planes behind the globe are drawn *before* the globe bitmap, so they are hidden by it. Planes in front are drawn after.
  - Which players: globe room = own only; laptop = players with `DisplayPlanes` on.
  - Positions update every 4 game minutes while the player is in either room (`Sim.cpp`), using linear interpolation in lon/lat with date-line wrap.
- **Plane hit box** (own planes only; laptop requires own `DisplayPlanes` on): `GlobeOffset + GlobePos − (20,14)`, size 19×19. Tooltip = plane name.
- **Selected plane's flights**: when a plane detail window is open (globe room: block 0 if the filofax is visible; laptop: any window ≥1), every leg of that plane's flight plan is drawn as a polyline. Points are linearly interpolated in lon/lat (shortest way across ±180). The step count depends on distance: <500 km 1 segment, <1000 km 2, <2000 km 4, <5000 km 8, else 16. Colour is red with intensity `255 − |angular offset from the view centre|/64`, fading to black at the limb. Drawn shifted −6 px in x. [H]
- **Laptop extras** per player with `DisplayRoutes[p]` > 0:
  - Rented cities as 3×3 dots in yellow (same fade).
  - Rented routes whose "level" (`Ebene`) equals the toggle value (1 or 2), each city pair once, as red polylines (same algorithm).
  - Quirk: the city-dot loop tests an uninitialised loop counter. In practice dots appear once a route loop has run. Implement it as "always draw dots". [H — bug]
- **City marker**: `CITYCUR` drawn at projected point − (18,18) for each open city-detail window. It is drawn behind the globe if on the back side and in front otherwise.

---

## 5. Open questions
1. How `OfficeState==3` (power cut) ends. No reset was found outside new-game init (Sim.cpp sets a season value when any office is dark). Is the office dark permanently?
2. Exact weights in the Smacker transition strings (`"A9A2A1A2E1E1"` etc.). These belong to the CSmackerPerson spec; confirm the encoding there.
3. Route "level" (`Routen[].Ebene`) values 1/2 — the manual says "short / long". Confirm with the route data spec.
4. Laptop turn-icon direction labels in the original are inconsistent (left/right swapped between rooms). The visual direction should be verified in game.
5. Tooltip id 4402 for "equip planes": confirm the text matches (`std_ger.res`, Tool).
