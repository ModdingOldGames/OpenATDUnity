# Rooms & UI 5b — Block windows and the flight planner

Clean-room behaviour spec, the second half of `rooms-and-ui-5-office-planner.md` (read §0 conventions there; **[WS]** = 640×480 assumption, **[H]/[M]/[L]** = confidence).
Traceability: `Block.cpp: BLOCK::BlitAt, Refresh, RefreshData, UpdatePageSize, LoadLib, SetTip, UpdateTip; BLOCKS::RefreshAuftragsBloecke`; `Planer.cpp: CPlaner::DoPollingStuff, DoPostPaintPollingStuff, HandleLButtonDown/Up, HandleRButtonDown, HandleLButtonDouble, ButtonNext/Prev/Index/NextB/PrevB/IndexB/Close, PaintPostIt`; `Globe.cpp`/`Laptop.cpp` (window hotspots); `DataTabl.Cpp: CDataTable::Fill*`; `Auftrag.cpp/Fracht.cpp: BlitGridAt, FitsInPlane`; `Stdraum.cpp` (MENU_REQUEST KILL* handlers).

---

## 1. Block model and lifecycle

### 1.1 Data per block (saved with the player)
- `ScreenPos`, `PlayerNum`, `StyleType` (0 = filofax/globe, 1 = laptop).
- `Destructing`: 0 alive, 1 closing animation, 2 dead and waiting for removal.
- `AnimationStart` (time slice; 20 slices = 1 s).
- `DoubleBlock`: two panes. Laptop: every type except Info. Filofax: always.
- Left pane: `BlockType`, `Index` (1 = list/index page, 0 = detail of `SelectedId`), `Page`, `AnzPages`, `PageSize`.
- Right pane: `BlockTypeB`, `IndexB`, `SelectedIdB`, `PageB`, `AnzPagesB`, `PageSizeB`.
- Per-type memory `Indexes[6]`, `SelectedIds[6]`, `Pages[6]`. These remember each type's view when switching panes; they are only fully used by the filofax tabs.
- Tooltip state (`Tip`/`TipB` + up to 7 params). A changed tip re-renders the block.
- Data tables `Table` (left) and `TableB` (right): rows of strings, flags and a line-index → object-id.
- Rendered `Bitmap` (the whole window is pre-rendered, then blitted).

Block types: **1 Cities, 2 Planes, 3 Orders (passenger), 4 Routes, 5 Info/Experts, 6 Freight orders.** Types 3, 4 and 6 only ever appear in the right pane, and only while the left pane is type 2 (the plane window). The right pane of a non-plane window shows hover tips (e.g. city tip).

Page sizes: lists hold 6 rows of 26 px (planes, orders, routes, freight) or 13 rows of 13 px (cities, info). Detail views have page size 1. Page numbers are shown as "n/m", where n = Page/PageSize+1.

### 1.2 Collection
- Per player, up to **20** slots (`Sim.cpp` resizes to 20). Slot **0 is reserved for the globe filofax**; slots 1..19 are laptop windows. A new laptop window needs a free slot.
- Z-order = slot order: a lower index is in front. The laptop draws from the highest slot down to 1. Clicking any window moves it to slot 1 by successive swaps, so it becomes topmost. New windows are also moved to slot 1.
- `IsTopWindow`: true only for the front-most live window. Several hover features (grid highlighting, tips, extend/delete buttons) need the top window.
- On entering either room, every block is checked:
  - Plane detail whose plane no longer exists → back to index (laptop: window deleted).
  - City detail → always reset to index.
  - Order detail whose order is gone → index. Route detail whose route is gone or no longer rented → index.
  - Pages are clamped and the window is clamped to the screen.
  - Laptop windows saved mid-animation skip the animation.
- Data refresh:
  - Every other paint frame, one block (round robin) is re-rendered.
  - At every game minute that is a multiple of 10, all blocks re-render.
  - A global "RepaintAll" flag re-fetches data and re-renders all blocks. A route-detail block whose route is no longer rented is deleted during this pass.
  - When an order list changes, `RefreshAuftragsBloecke` re-renders all order-list blocks.

### 1.3 Open, close, animation (laptop style only)
- Open: phase = (now − start)·2 − 7 (in time slices).
  - phase < 0: two corner pieces sit at the top centre.
  - 0–9: the top edge grows outward from the centre.
  - 10–19: the side edges grow down.
  - 20–29: the bottom edge closes inward (corner sprites `LEFT1/2`, `RIGHT1/2` travel along).
  - 22–37: the whole window fades in (translucent blit, level (38−phase)/2).
  - ≥38: fully drawn.
  - The single window uses half-width numbers (centre 80–100 instead of 178–198). The phase advances two per sim tick, so the whole build-up takes 22.5 ticks, about 1.1 s at 20 ticks per second (it counts the day's time slices, which stand still while the clock is frozen; OpenAT uses real time).
- The screen lamp at (80,403) flickers (`LapLigh`) whenever a new window opens or a plane window is opened from the globe, and blinks briefly (`LapLigW1`) on a close or a right click with a window under the mouse; it is only drawn while the battery has charge.
- Quick-jump arrows (`JUMP0`, hovered/pressed `JUMP1/2` back and `JUMP3/4` forward; drawn on every detail page, like the left and right index arrows `INHALT0`) at (167,4) left and (331,4) right on detail pages (not Info): back half x 0..10, forward half x 11..21; they step to the previous/next element of the pane's list, wrapping around, and keep the page where possible. Quirk: a plane window then has one page fewer (3 + photos instead of 4 + photos; verified live).
- Scrollbar: only the 29 px thumb (x 5..20 left, 376..391 right, top at 43 + 95·page/(pages−1)) can be grabbed; while dragged the page follows the thumb's top with the grab offset kept, (y − 43)·(pages − 1)/95, clamped.
- Close: the close button (laptop) or right click on a non-client part. It runs the same animation backwards. When it finishes the block is marked dead. The next poll removes it, remembers its position for that type, and repaints the globe (flight lines disappear).
- The filofax (style 0) has no animation. Hiding and showing is instant with a paper sound.

### 1.4 Moving
- Filofax: drag strip block-relative (20,0)-(481,49). Laptop: (34,2)-(165,19) or (223,2)-(356,19), the title bars.
- The window follows the mouse while the LMB is held. Clamps are in part 5 §2.4/§3.5. **[WS]** All clamps use 640 or the monitor rect.

---

## 2. Pane geometry

| | Filofax (style 0) | Laptop (style 1) |
|---|---|---|
| window art | `FILOFAX` (globe_a.gli) | `BIGWIN` (double) / `WINDOW` (single) |
| left title | (48,57); separator line under it, colour 0x303030 | (41,7), green font |
| left client (draw origin) | (48,72), 172×170 | (21,22); hit area (20,21) 172×170 |
| left page number | centred on (137,242) ±30 | (104,195) |
| right title | (280,57) | (228,7) (lists: +60 px to clear the switch buttons) |
| right client | (280,72) | draw (206,22); hit (207,21) |
| right page number | (369,242) | (291,195) |
| prev page L | `BACK` at (40,237), hit (40,237)-(60,256) | up arrow, hit (0,23)-(19,44) |
| next page L | `NEXT` at (208,233), hit (208,233)-(231,255) | down arrow, hit (0,165)-(19,186) |
| back to index L | `INHALT` flag at (0,78), hit (0,78)-(36,181) | `INHALT` at (16,0), hit (16,0)-(33,19) |
| prev/next R | +232 in x on the filofax | up (375,23)-(395,44), down (375,165)-(395,186) |
| index R | flag at (232,78), hit +232 | (202,0)-(219,19) |
| close | — (right click hides) | (359,0)-(380,20) double; (173,3)-(191,19) single; art `CLOSE1` while hovered, `CLOSE2` while pressed, at (360,0) / (174,0) |
| scrollbar | — | left thumb x 5..19, right thumb x 376..391; thumb y = 43 + 95·(page/pagesize)/(pages−1), 29 px tall |
| jump to prev/next element | — | left (167,4) / (178,4), 10×15, not in index mode and not for Info; right (331,4) / (342,4) |
| pane switch (right) | bottom tabs, see §3 | buttons at (217,5) orders, (237,5) freight, (257,5) routes, 19×16; shown only when the right pane is a list: only the active type is drawn (`SWITCH1..3`), a hovered other type as `SWITCH4..6`; the active one is not a button; no tooltips; otherwise four blank `SWITCH7` at x 217/237/257/277 |
| route lock | `LOCK` at (441,55) | `LOCK` at (341,6), only on the route list and only with the security company's route protection |
| laptop button art | — | prev/next/index: frame 1 while hovered, frame 2 while pressed; `UPLEFT` (0,22), `DWNLEFT` (0,168), `UPRIGHT` (376,22), `DWNRIGT` (376,169); right `INHALT0..2` at (203,0) (the left index button is part of the window art) |
| page number | always shown, also "1/1" | always shown, in the title font (green) |

- Prev/next appear only when there is a previous or next page.
- Holding LMB on prev/next auto-repeats: the first repeat after 1000 ms, then every ~130 ms. The laptop route ticket-price +/- also repeats (800 ms, then ~400 ms).
- Laptop wallpapers: `BACK{type}` at (20,20). None behind the flight-plan grid or the gate chart. The right pane shows the wallpaper of its own type, or of the left type while a tip is shown.
- Laptop scrollbar: dragging the thumb maps the cursor y to a page (snapped to the page size).
- Back to index (button or right click inside a client area): the list reopens on the page containing the previously selected entry. In fullscreen the OS cursor is also warped onto that row. **[WS]** The warp offsets are hard-coded.
- The paper sound (`gMovePaper`) plays on filofax page and index actions only.

---

## 3. Pane selection (filofax tabs)
Bottom tab strip of the filofax, block-relative. Hover rects are evaluated in order, so the last match wins.
- Left: Planes (28,260)-(108,290) `Tool 3002`; Cities (108,260)-(188,290) `Tool 3001`; Info (169,260)-(268,290) `Tool 3005`. Info overlaps Cities for x 169..188 and wins there.
- Right, only while the left pane is Planes: Orders (272,258)-(343,289) `Tool 3003`; Freight (343,258)-(414,289) `Tool 3008`; Routes (414,258)-(486,289) `Tool 3004`.
- Switching saves the current pane's index, selection and page into that type's memory slot, then restores the new type's slot and recomputes the page count. List page counts: ceil(rows/13) for cities and info, ceil(rows/6) for planes and right lists.
- Tab art: left `INDEX0..3` at (36,256) (0 = nothing selected); right `INDEXR_1..3` at (268,256); page curls `FILOTOP1` at (32,5) and `FILOTOP2` at (261,0).
- The laptop has no left tabs. The window type is fixed by the icon that opened it; only the right pane switches (§2).
- Tutorial gates: left tab changes only at step 1502, and opening Planes then advances to 1503. The right pane allows only Orders, from step 1504 (→1505). Glow highlights point at the next target.

---

## 4. Pane contents

### 4.1 Cities (type 1)
- **List**: title `Schd 1400`. 13 rows. City names in black if the player rents a branch there, grey otherwise. Hovering a row highlights it and shows the city tip in the right pane (a `KARTE` map with city info; drawing helper in Tips code). Click → detail.
- **Detail**: title = city name. Page 0 rows at y 1/14/27/40 (population, rent, rank, image) with values left-aligned at x 85. On opening, the city's photo library (`<PhotoName>.gli` for the filofax, `.glj` for the laptop, under `gli/`) is loaded and the globe auto-rotates to the city.
  - Page 0: population and branch rent (`City 1000/1001`). If rented, also rank and image % (`City 1002/1003`) and a "close branch" line at y 66 (`City 1010`, not shown for the home airport). Clicking it asks for confirmation (MENU_REQUEST KILLCITY): the branch rank is cleared and all city-detail windows of that city return to the index.
  - Pages 1..AnzTexts: description text `City TextRes+n`.
  - Remaining pages: photos. The bitmap name is the photo name with its last character incremented per page. Each photo is centred in the client area.

### 4.2 Planes (type 2) — left pane
- **List**: title `Schd 1000`. 6 entries of 26 px. Line 1: name at x 0, condition % at x 123. Line 2 (y+10): "in <city>" (`Schd 1105`) or "to <city>" (`Schd 1102`), also at x 0. All three texts use the row's font (black, or red for a crew shortfall or problem); the condition is left-aligned. Verified on the running original.
  - An entry is red if crew is missing or the plane has a problem. Red entries sort first, then by name.
  - Click → detail; the page count is 4 + photos of the type. Tutorial: only from step 1503 (→1504).
- **Detail** title = plane name, red if crew is missing.
  - Page 0: flight-plan week grid (§5).
  - Page 1: technical data (`Plne 1000/1017/1001/1002/1008/1009/1041/1011/1013`). This covers the manufacturer and type, max passengers (and first class), payload, range, speed, tank, consumption, pilots and attendants assigned vs required (`Plne 1014` format), and build year. Layout (client-relative, small black font): labels at x 2, values **left-aligned at x 75**; type y 4; passengers y 25 ("max (first class)"), payload label y 37 / value y 36 (tons unit), range 48 (km unit), speed 60 (km/h), tank 73 (l), consumption 85 (l/h), pilots 107, attendants 119, build year 136. No condition line on this page (verified on the running original). "Rename" line `Plne 1030` at y 155; clicking it (rect (0,155)-(169,166)) opens MENU_RENAMEPLANE.
  - Page 2: crew list (`Plne 1040/1041/1042`). Assigned pilots and attendants are listed by name; missing seats are listed as `Plne 1045`. Layout: heading (2,4), the pilots heading at (10,17), names from y 30 at x 18, 13 px apart, in the normal grey font (`norm_drk`); then the attendants heading at x 10 and their names the same way.
  - Week grid (page 0): the day labels are the short weekday names (`Schd 3000+`) in the small black font at (2, 19 + 19·row), today first.
  - Page 3: statistics (`Plne 1050..1053`): total passengers, km, load factor economy / first class.
  - Page ≥4: type name + type comment text (the photos promised by the page count are not actually drawn here). [H]
  - Quirk: opening a plane via a globe click sets the page count to 3 + photos instead of 4 + photos. [H]
- The left plane list doubles as a filtered list when a right-pane detail is on its "planes using this" page: order page 1, freight page 1, route page 2 (both directions of the route).

### 4.3 Right-pane lists (types 3, 4, 6)
Each row is 26 px. A small post-it icon sits at x 1, and an info "i" icon (`INFO01`) at (3, row+9). Text starts after the post-it width.
- **Orders** `Schd 1200`.
  - Line 1: "From - To" (hyphen with a space on each side), falling back to shorter forms (name–code, then code–code) if it does not fit in 142 px. Passengers "(n)" right-aligned.
  - Line 2: date, either the weekday (`Schd 3010+wd`) or "until" (`Schd 3009`) + weekday, and the premium right-aligned.
  - Only orders not given up and not expired are listed. Sort: by usage state (not-OK first), then deadline, then name.
  - Post-it `POST_SG` (green). `POST_SG2` (red frame) when the order is planned but the plan is not OK.
  - While a plane detail is open on the left, rows that don't fit that plane are grey: range or duration ≥24 h → flag 2; too many passengers → flag 4. Rows already planned are grey too (flag 1).
- **Routes** `Schd 1100`. Rented routes only, sorted by from/to name. Post-it `POST_SB` (blue). Line 2: number of own planes on the route + `Rout 1015`. Grey if out of range or ≥24 h for the selected plane.
- **Freight** `Schd 1300`. Like orders, with tons in place of passengers. Post-it `POST_SB2` if not OK, otherwise `POST_SR`. [M: the original picks frame indices 4/5 from the list "POST_SR POST_SG POST_SR2 POST_SG2 POST_SB POST_SB2"]
- Row click:
  - On the "i" icon (x 3..13/15, y within the row ≥10) → detail (orders/freight 2 pages, routes 3 pages).
  - Anywhere else in the row (x 0..167) → **pick up as post-it** (§6). Refusals use advisor messages:
    - order: flag 2 → `Advi 2310`, flag 1 → `Advi 2312`, flag 4 → `Advi 2316`, already used by another plane → `Advi 2300` (plane name inserted).
    - route: flag 2 → `Advi 2310`, flag 3 → `Advi 2314`, flag 4 → `Advi 2301`.
    - freight: flag 2 → `Advi 2310`, flag 1 → `Advi 2330`.
  - Hovering the "i" zone shows the hot cursor. Hovering a fitting row in plane view highlights it.
- Tutorial step 1505: picking up an order advances to 1506. The "i" detail is blocked (`Tuto 1510`).

### 4.4 Right-pane details
- **Order** (title "AAA – BBB" codes; line 1 full names).
  - Page 0: the full city names at y 1; passengers `Auft 1000` (y 28), date `Auft 1003` (y 40), premium `Auft 1001` (y 53), penalty `Auft 1002` (y 66), distance `Auft 1007` (y 92); values left-aligned at x 75. If not yet planned, a "give up" button (sched frame 57) at (0,118) with label `Auft 2400` (no penalty) or `Auft 2401` (with penalty). Click rect (0,118)-(169,134) → confirm menu KILLAUFTRAG / KILLAUFTRAG0. On yes: the penalty is charged (money-log entry 2065), the order is removed, open detail panes revert to the index, and a carried post-it of that order is dropped.
  - Page 1: `Auft 2500` + the left list filtered to planes carrying it.
- **Freight**: tons total / left / open (`Auft 1008/1010/1009`), date, premium, penalty, distance. "Give up" at (0,135)-(169,151), only if not planned and nothing loaded yet → KILLFAUFTRAG(0). On yes: penalty (log 2060); the freight is marked given up (InPlan = −1) rather than deleted. Page 1: `Auft 2501` + filtered planes.
- **Route** (3 pages)
  - Page 0, ticket prices:
    - Economy at y 27, first class at y 40 (`Rout 1007`), each with +/− hot zones at x 148–160 (+10) and 160–172 (−10).
    - Four presets at y 66/79/92/105 (`Rout 1102..1105`) with values Cost/2, Cost, 2·Cost, 4·Cost (rounded to 10). Clicking a preset sets economy to that value and first class to 2× economy.
    - Cost = flight-cost estimate for an 800/800 reference plane × 3/180 × 2 (same function as the route office).
    - Clamp: economy 0 .. Cost·1.6, first class 0 .. 3× that.
    - Changes also update the return route's economy price. Prices are pushed to planned flights and synced over the network.
    - With an informer: up to 3 competitor rows from y 128 (airline name + their economy price, or "−"). Clicking a rented competitor row copies their prices (this direction only).
  - Page 1: distance, image, rank, demand, load (economy/first class), route utilisation and rent (`Rout 1004/1011/1013/1017/1018/1019/1006`), plus a "cancel route" button at (2,142) labelled `Rout 1200`. Click (2,144)-(169,157):
    - If any own plane has either direction of the route in its plan → `Advi 2315` (plane name), no menu.
    - Otherwise confirm KILLROUTE: both directions are cancelled, competitors with a worse rank move up, and open panes revert.
  - Page 2: `Auft 2502` + filtered planes.

### 4.5 Info / experts (type 5; laptop single window)
- List title `Schd 1500` (not `XPrt 1000`), 4 entries `XPrt 2000..2003`.
- 0 **Gate usage**: the week grid background with red bar outlines per hour. Height = usage·16/(rented gates+1); the bar is filled solid when overbooked. Day labels as in the flight plan.
- 1 **Daily balance**: title `XPrt 2100` + weekday.
  - Needs the money advisor. Without it: `XPrt 3000`. Skill <80: `XPrt 3001`.
  - Otherwise 3 pages with yesterday's figures (in thousands): income rows `XPrt 3400..3404/3604`, expenses `XPrt 3500..`, and so on. [M for exact rows]
- 2 **Plane week balance**: the plane list with weekly balance, red if negative, 13 per page, extra pages.
- 3 **Competitors**: needs the informer. 3 pages: money & debts, planes & image, routes & branches per airline.
- Without the required advisor, the detail collapses to one page.

### 4.6 Hover tips in the right pane
A cities window always shows the world map (`KARTE`) on its right, at client (−4,−2) on the laptop (−9,−11) in the filofax. For the hovered city (it stays after the mouse leaves) a star marks it on the map, "Name (code)" is shown in the title bar, and population and office rent (the own rent with a branch) sit at y 105/116 with values at x 90, followed by the airlines with a branch there in rank order (at the home airport, all airlines). The blank title-row pieces and the index arrows are part of the window and are drawn under the texts. Verified on the running original.

While the cursor is over a flight in the week grid of the top window, the right pane is temporarily replaced by a tip:
- `TIP_AUFTRAG`: order, costs, OK flag, gate, unlocked flag.
- `TIP_FRACHT`: freight, costs, OK, tons on this flight, unlocked, income share.
- `TIP_ROUTE`: route, passengers (0 if still in the future), costs, gate, prices, unlocked, first-class passengers.
- `TIP_AUTOFLUG`: costs, seats, from, to, unlocked.

Costs = flight-cost estimate (plane consumption and speed) + catering per passenger. The tip renderers are shared with other rooms (`Tips.Cpp: Draw*TipContents`); see that spec.

---

## 5. Flight-plan week grid (plane detail page 0)

### 5.1 Geometry (client-relative)
- Grid background `TIMETAB1` (filofax) or sched frame 0 (laptop) at (21,−2).
- Cells: origin (24,17); **6 px per hour × 24 columns = 144 px; 19 px per day × 7 rows = 133 px**. Row 0 = today, rows 1..6 = the following days.
- Day labels at (2, 19+19·d): `Schd 3000 + (date+startWeekday) mod 7`.
- Button strip under the grid: sched frame 54 (filofax) at (22,150) or frame 40 (laptop) at (24,150).
  - **Extend plan**: rect (22,150)-(58,164), `Tool 3021`.
  - **Delete plan**: rect (135,150)-(170,164), `Tool 3022`.
  - Pressed images: frames 55/56 (filofax, block-relative (70,222)/(179,222)) or 52/53 (laptop, (45,172)/(154,172)).
- Past time: today's row is darkened from x 24 to 24 + (time of day in game-ms)/10000. One hour = 60000 game-ms, so 6 px.
- Plane problem: a translucent red bar starting at "now", `Problem` hours long, wrapping to the next rows.
- **[WS]** The grid is fixed-pixel; scale the whole block as a unit.

### 5.2 Flight bars
Sprite set `SCHED01` (59 frames, std lib), each frame one 6 px hour cell:
- Routes (blue) frames 1..5: start cap, 2nd hour, middle, 2nd-to-last, end cap. Orders (green) 6..10. Freight 20..24.
- Auto/positioning flights (yellow): 11 start, 13/14 middle (alternating on odd hours), 15 end.
- Overlays:
  - Order/freight confirmed (OK > 0): 41 start, 44 middle, 45 end.
  - Frame 17 at the second cell when the flight is locked (already started or departing within the next hour).
  - Frame 18 "gate warning" on the departure/arrival cell of home-airport flights (and freight arrivals) flagged `GateWarning`.
- City codes: departure code near the start and arrival code right-aligned at the end. For flights under 4 h a very small font is used; for flights over 6 h the labels move inward.
  Both codes are always shown, on two lines: departure on the upper line (bar top + 1, left edge at bar start + 2), arrival on the lower line (bar top + 10, right-aligned at bar end + 1). Small black font from 4 h, the very small font under 4 h (both codes shifted 2 px right, the departure 2 px down). Over 6 h the departure starts one cell in (bar start + 6) and the arrival ends at bar end − 4. A code is skipped when its end of the bar lies in the first hours of a row (bar wrapped across midnight). [H]
- Bars crossing midnight continue on the next row. Bars before today or after day 6 are clipped.
- Diagnostic: Shift+Ctrl while the grid re-renders dumps the plan to the debug log (do not replicate).

### 5.3 Hover feedback
- Carrying an order or freight post-it, or hovering an order/freight row in the right list while the grid is visible: days outside the item's [from, until] window are greyed with frame 51 (one per row). Items already planned grey all 7 rows.
- Plane list (left index) while carrying or hovering an item: planes that cannot fly it (too few seats for an order, out of range, or ≥24 h) are recoloured grey.

---

## 6. Planner interactions (state machine)

State: `Carry` = (type, id) of the post-it on the cursor. Type 0 = none, 1 route, 2 order, 4 freight; type 3 = auto flight, never carried. `DragFlight` = 0 / 1 (grabbing start) / 2 (grabbing end).
"Locked" = starts today at or before current hour + 1, or earlier. Locked flights and auto flights cannot be picked up or moved.

### 6.1 Post-it image
`PaintPostIt`: a horizontal strip of bar frames, as long as the flight duration + 1 cells. The duration is computed with the selected plane's speed if a plane detail is open, otherwise speed 700. It has the same frames and code labels as §5.2. It is drawn centred on the cursor. Over the grid it snaps so its left edge sits on the hour column and row under its centre: column = (cursorX − w/2 + 3 − 24)/6, row = (cursorY − h/2 + 9 − 17)/19. The laptop adds +1 px.

### 6.2 Pick up
- From a right-pane list row (§4.3). Records "taken with this click".
- From the grid: click a non-auto, unlocked flight → it moves to the cursor and is removed from the plan.
  - Exception: **Shift** with a route — the route stays in the plan and a copy goes on the cursor.
  - After removal: re-validate the plan, network update, refresh order lists, repaint the globe lines.

### 6.3 Place (click with Carry ≠ 0 inside grid rect (24,17)-(167,149))
1. Target start (date, hour) = snapped post-it origin (§6.1); hours <0 / >23 wrap into the previous/next day.
2. Refuse with `Advi 2310` if the distance exceeds the plane's range or the duration is ≥24 h.
3. Tutorial step 1506: the target is forced to today, hour + 2, and the step advances to 1507.
4. Today and hour ≤ current hour + 1 → start = current hour + 2.
5. An order that is already planned (`InPlan` ≠ 0): −1 → `Advi 2311`, 1 → `Advi 2312`; refused.
6. If an unlocked non-auto flight occupies the clicked cell, its plan entry is reused and its object goes to the cursor (**swap**). Otherwise the last plan slot is used.
7. Write the entry: OK=0, start date and time, object type and id. Routes copy their current ticket prices. Passengers are recomputed and the arrival counter is reset.
8. Sim calls, in order: update next flight and next start; `CheckFlugplaene` (re-sorts, inserts auto positioning flights, computes landing times, OK and conflict flags); `NetUpdateFlightplan(plane)`; update order and freight usage; refresh order-list blocks.
9. Cursor afterwards:
   - A route with **Shift** held stays on the cursor (multi-placement).
   - Freight with tons still open stays on the cursor.
   - Otherwise the cursor takes the swapped object, or becomes empty. An auto flight is never carried.

### 6.4 Drag and drop variant
If the LMB is released more than 10 px from where the post-it was taken in the same click (not in the tutorial), the release counts as a placement click (§6.3) at the release point. Releasing outside the grid drops the post-it. Without movement the post-it stays on the cursor (click-to-carry mode).

### 6.5 Moving placed flights by their edges
- With no carry, hovering a flight's **start cell** shows the horizontal-move cursor. Hovering its **landing cell** does the same if the flight is unlocked. Pressing begins DragFlight 1 or 2 (types 1, 2, 4 only).
- Each frame while held:
  - New start = cell under the cursor (for an end-grab, minus the flight duration, borrowing days).
  - Clamp: today → ≥ current hour + 2; date within today..today+6; hour 24 rolls to the next day 0 (or 23 on the last day).
  - The flight is shifted. With **Shift**, every later flight keeps its relative offset (chain move).
  - Passengers are recomputed; freight OK is reset. Then the plan is re-validated with a network update.
- Release ends the drag with a final validation and network update.
- The drag is cancelled automatically if the flight's start time is reached.

### 6.6 Double-click to append
With an item carried (the first click of the double-click picks it up) and the grid visible, double-clicking an order or route row in the right list appends it after the last planned flight. Start = previous landing + 1 h, but at least now + 2 h. Same sim calls; the cursor is emptied. Freight rows are not handled. Disabled in the tutorial. [H]

### 6.7 Grid buttons
- **Extend**: `ExtendFlugplaene` repeats the week's routes to fill the 7-day window. Then usage update, network update, validation and refresh.
- **Delete plan**: confirm menu KILLPLAN. On yes, every unlocked entry is cleared (orders go back to the list), then network update, usage update and repaint.

### 6.8 Right mouse
- Carrying → drop the post-it. The item stays in the list; there is no penalty.
- Else, inside a detail client area → back to index (that pane).
- Else, on the block → laptop: close the window; filofax: hide the filofax.
- Else (y<440, no dialog) → leave the room.
- Disabled in the tutorial up to step 1506.

### 6.9 Commands issued (summary for the sim/network layer)
`Flugplan.Flug[i]` writes (start date and time, object, OK, ticket prices) · `CFlugplanEintrag::FlightChanged + CalcPassengers` · `CFlugplan::UpdateNextFlight/UpdateNextStart` · `CPlane::CheckFlugplaene` · `CPlane::ExtendFlugplaene` · `PLAYER::UpdateAuftragsUsage/UpdateFrachtauftragsUsage` · `PLAYER::NetUpdateFlightplan(plane)` · `PLAYER::UpdateTicketpreise + NetSynchronizeRoutes + NetRouteUpdateTicketpreise` · menu-confirmed kills (branch, route, order, freight, plan) · `MENU_RENAMEPLANE`.
`CheckFlugplaene` also trims flights that start ≥7 days ahead, except while the local player is dragging in the globe or laptop (`Planetyp.cpp`).

---

## 7. Vestigial / do-not-implement
- **Day view** (`Index==2`: vertical 24-hour column per day, "104" hotspots, vertical move cursor). Handled in input code, but nothing ever sets it and `Block.cpp` doesn't render it. It is unreachable.
- Laptop icons 6–9 hit areas (positions (29,391),(75,421),(565,421),(607,391)) have no action. The commented-out "icon opens block" path in Globe.cpp is dead.
- The `GLASS` bitmap in the globe room is unused.

## 8. Widescreen notes (summary)
All block clamps (640, 400, the 39..600 monitor), exit rects on the right edge, the filofax corner at (0,440−h), the office door-window clip rect, cursor-warp offsets, the dark-overlay double blit, and status-bar y≥440 checks. Recommendation: lay out the globe room and laptop as a centred 640×480 canvas with decorative side fill. Allow filofax and laptop windows to be clamped to the real viewport only if the background art is extended.

## 9. Open questions
1. The exact `CheckFlugplaene` conflict rules (OK flag, auto-flight insertion, overlap resolution) live in `Planetyp.cpp` and belong to the sim spec. This doc only covers the triggers.
2. Freight post-it frame mapping (§4.3) needs to be confirmed visually.
3. The daily-balance info pages: exact row labels and values (Block.cpp ~L880–1000) were skimmed, not transcribed.
4. Whether the route-preset clicks should also update the return route's first-class price. The original only copies economy, which may be a bug.
5. Tutorial default right pane = routes (type 4) while only orders are allowed. Verify in game.
