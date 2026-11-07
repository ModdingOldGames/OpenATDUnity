# Rooms and UI, part 7: shops and plane rooms

Clean-room behaviour spec for OpenAT. It covers these rooms:

1. Plane broker (`ROOM_MAKLER`)
2. Museum, used planes (`ROOM_MUSEUM`)
3. Designer office (`ROOM_DESIGNER`)
4. Workshop (`ROOM_WERKSTATT`)
5. Plane properties / equipping (`ROOM_PLANEPROPS`)
6. Aircraft constructor / editor (`ROOM_EDITOR`)
7. Advertising agency (`ROOM_WERBUNG`, plus the closed `nowerb.gli` variant)
8. Newspaper kiosk (`ROOM_KIOSK`)
9. Duty free shop (`ROOM_SHOP1`)

Sources: `docs/spec/local/gog_src` (original) and `ref_AirlineTycoon/src` (SDL port). For these nine rooms the SDL port matches the original apart from platform code, so behaviour is taken from either. Data tables were checked against the shipped game files (`data/builds.csv`, `data/relation.csv`).

Texts are referenced as **Std** = `misc/Std_<lang>.res` (StandardTexte) and **Dlg** = `misc/Dlg_<lang>.res` (DialogTexte), followed by the 4-letter group token and the numeric id. For example, "Std Tool/3100" means tooltip id 3100. The group tokens are: Tool, Schd, Misc, Plne, Item, Advi, Lett. The dialog groups are Makl, Muse, Desi, Mech, Werb, Duty and Kios. Do not copy any text into OpenAT; load it from the resource.

Confidence tags: **[H]** read directly from code, **[M]** inferred from several code sites, **[L]** a guess.

---

## 0. Conventions shared by all rooms (summary; the base-room writer owns the details)

* **Coordinates.** All coordinates are in the 640x440 room area. The room area is the 640x480 screen minus a 40 px status bar at y>=440. Rect notation `(x1,y1)-(x2,y2)` is inclusive. **WIDESCREEN:** every rect, sprite position, clip rect and "y<440" test below assumes a fixed 640-wide canvas with its origin at the top-left. OpenAT should render each room on a 640x440 logical canvas and letterbox or pillarbox it, or add side art. Do not move the hotspots. A per-room flag is listed wherever a room draws something that depends on the screen edge.
* **Room lifecycle.** The constructor loads a room GLI (`room/<name>.gli`) and its background bitmap. It marks the room as visited for the player, clears the airport focus person and, when not in phone mode, sets the ambient global volume (40 or 60). It increments the reference count of the room's `TALKER_*` and sets that talker as the default dialog partner. That default partner receives items dropped on the room and receives `StartDialog` calls with id 10000+ITEM.
* **Hotspots.** Each frame, while no dialog or menu is open, the paint routine tests the mouse against the rects in priority order (the first match wins). It sets the cursor (`CURSOR_EXIT` / `CURSOR_HOT` / `CURSOR_NORMAL`), an optional tooltip id (Std Tool/<id>), and a click area and click id. Left-click then acts on the stored (area, id).
* **Generic leave.** Click id 999 with `CURSOR_EXIT` leaves the room. In all rooms here except the editor, a right-click inside the room area with y<440, no dialog and no menu open also leaves. A right-click while a menu is open is passed to the menu (`MenuRightClick`).
* **Smacker "person" clips (CSmackerPerson).** These are used for every animated character. Each clip has: id, `.smk`, optional sync sound (`.raw`), screen pos (top-left), mood (IDLE / TALKING / LISTENING), a repeat range `CRepeat(min,max)`, a post-wait range `CPostWait(min,max)` in 1/20 s, and a cancellable flag (whether the clip may be cut short when the mood changes). A clip may also write to an integer "comm variable" and may read a "decision variable".
  * The comm variable is written with set, add or xor. The write happens at clip start (PRE), at clip end (POST), or at frame N (FRAME+N).
  * The decision variable is read when the clip ends. If it is not -1, its value is the next clip id, which overrides the random choice.
  * Each clip also has a successor list with tokens `A<w>` and `E<w>`. `A` means "a normal successor while the mood stays the same". `E` means "an exit edge used to reach a different desired mood". `<w>` is a weight from 1 to 9.
  * The dialog system sets the desired mood: LISTENING while the player speaks and TALKING while the partner speaks. The clip graph walks along `E` edges to reach that mood.
  * The tables below list successors as `token->clip`.
* **Simple animations (CAnimation).** These are frame strips from the GLI with the parameters (prefix, count, sfx, show-first-frame-before-start, mode ONCE / REPEAT / RANDOM, prelude, speed, cyclic wait, repeat count). Prelude, speed and wait are in ticks of the real-time ticker (`TickerTime` = real milliseconds / 32, so one tick is 32 ms; sim-core). Speed is ticks per frame. A REPEAT animation with a sfx and prelude 0 plays the sfx in a loop. Otherwise the sfx plays once each time the animation passes frame 0.
* **Background random sfx.** `SetBackgroundFx(slot, file, avgWait, startWait)` plays the file at random intervals. Each interval is about avgWait x (0.5 to 1.5) ms. The first play comes startWait ms earlier.
* **Plane-list tooltips in menus.** Several rooms add hover behaviour to a list menu. While the menu is open and the mouse is inside menu-relative rect (216,6)-(387,212), the hovered row is `(mouseY-(MenuPos.y+22 or 25))/13 + MenuPage`. Up to 13 visible rows, each 13 px tall. The room redraws the left part of the menu with the plane or route tip at (6,6)/(6,28), and highlights the row rect `(MenuPos.x+216, rowY-2or3)-(MenuPos.x+387, rowY+12)` in black. A row flagged in the menu data table (used or locked plane) is highlighted in red instead. The menu internals belong to the menus writer.

---

## 1. Plane broker - `CMakler` (Makler.cpp) [H]

**Entry:** constructor `Makler.cpp: CMakler::CMakler`. Room `ROOM_MAKLER` (105), GLI `room/makler.gli`, background `MAKLER`. Talker `TALKER_MAKLER`. Ambient volume 40.

**Opening hours:** if the game time is at or after `timeMaklClose` (16:00), the paint routine makes the player leave the room. The airport door is blocked by condition block 5 under the same condition (`Sim.cpp` hourly update). After day 5 the paint routine calls hint `HINT_FLUGZEUGMAKLER`.

### Assets
| Element | Bitmaps / files | Position |
|---|---|---|
| Glass door foreground | `OPAQUE` (blitted with transparency) | (0,149) |
| Glass panes (additive/trans blend, level 2) | `TRANS1`..`TRANS4` | (325,200), (225,184), (102,172), (0,157) |
| Fish tank bubbles | `BUL00`..`BUL06` (7), REPEAT, speed 2 | (394,400) |
| Fish | `FISH00`..`FISH34` (35), REPEAT, prelude 400, speed 2, cyclic wait 300 | (394,400) |
| Left lights | `LICHTL00`..`04` (5), REPEAT speed 5, sfx `neon.raw` | (88,400), shown only while "lights on" |
| Right lights | `LICHTR00`..`04` (5), REPEAT speed 5 | (193,375), only while lights on |
| Wall flap (easter egg) | `KLAPPE00`..`03` (bra visible) or `KLAPOH00`..`03` (empty), ONCE, prelude 500, speed 5, sfx `Klappe.raw` | (0,0) |
| Fountain water | 9 frames `SPRITZW0` (idle), `SPRITZA0`,`SPRITZA1` (start), `SPRITZ00..02` (loop), `SPRITZE0..2` (stop) | (226,253) |
| Fountain sound | `Spring.raw`, looping while the water runs | - |

Wall flap: it is created on entry with 50% probability (always when the debug flag "anim now" is set). The `KLAPPE` variant is used only if the player has neither the bra item (`ITEM_BH`) nor the horseshoe (`ITEM_HUFEISEN`), `TrinkerTrust` is false, and the game is not the tutorial. Otherwise the `KLAPOH` variant is used.

### Broker character `SP_Makler` (all clips at (470,218))
| Clip | smk / sound | Mood | Repeat | PostWait | Cancel | Comm var | Successors |
|---|---|---|---|---|---|---|---|
| 0 | M_Wait | IDLE | 1 | 15 | yes | - | A9->0, A2->1, A2->2, E1->3 |
| 1 | M_Dreh / M_Dreh.raw | IDLE | 1 | 0 | no | xor "Light" with 1 at frame 6 | A9->0, E1->3 |
| 2 | M_Druc / M_Druc.raw | IDLE | 1 | 0 | no | xor "Water" with 1 at frame 4 | A9->0, E1->3 |
| 3 | M_Turn | IDLE | 1 | 0 | no | - | A1->4, E1->5 |
| 4 | M_Rede | TALKING | 2 | 0 | yes | - | A9->4, A5->7, E1->5, E1->6 |
| 7 | M_RedeB | TALKING | 1 | 0 | yes | - | A9->4, E1->5, E1->6 |
| 5 | M_List | LISTENING | 1 | 10-20 | yes | - | A9->5, A9->8, E1->4, E1->6 |
| 8 | M_ListK | LISTENING | 1 | 1 | no | - | A9->5 |
| 6 | M_back | IDLE | 1 | 0 | no | - | A9->0 |

So, while idle, the broker sometimes turns a knob (`M_Dreh`), which toggles the lights. He also sometimes presses a button (`M_Druc`), which toggles the fountain.

**Fountain state machine.** It advances one step per 100 ms of real time, with at most 20 steps of catch-up per frame.
* Water off and frame 0: stay on frame 0.
* Water on: go 0 -> 1 -> 2, then cycle 3, 4, 5.
* Water off and frame > 0: advance to 6, 7, 8, then return to 0.
* The sound starts when the water turns on and stops when it turns off. Volume = effects option x 100/7.

**Passers-by through the glass:** airport persons are drawn behind the door, clipped to (0,149)-(343,235). A person is drawn only if their airport screen y is within 40 of the shop rune's y. The room position is `(126,331) - (personScreenPos - shopAnchor)`, where shopAnchor = the shop rune position with y adjusted by -5000+93. Persons are drawn mirrored: look directions 1 and 3 are swapped, and the walk phase is shifted by 2 for direction 8. The local player's own figure is skipped. Each person is drawn only if p.x>-50 and p.y<470. The door overlays are drawn on top. **WIDESCREEN:** this view is a fixed window; no change is needed.

### Hotspots (priority order)
| Rect | Cursor | Click |
|---|---|---|
| (55,106)-(196,405) | EXIT | leave room |
| (335,199)-(640,440) | HOT | `StartDialog(TALKER_MAKLER, MEDIUM_AIR)` |
| (0,15)-(38,79), only if flap frame==3 and the same item conditions as above | HOT | if the player has inventory space and no `ITEM_BH`: give `ITEM_BH` (free) and swap the flap animation to `KLAPOH` without restarting it |

**WIDESCREEN:** the broker hotspot extends to x=640 (the right edge of the room).

### Dialog-driven actions (reference only; dialog writer owns the trees) - `Dialog.cpp: TALKER_MAKLER`
* The greeting depends on medium and history (Dlg Makl/80..83).
* "Buy" opens `MENU_BUYPLANE`. In the menu, hovering a row shows a plane-type tip (see §0). Picking a type returns to the dialog with the type id.
  * List: column title `Schd 1601`, rows = type name only (no manufacturer, no price; second column blank), types available by mission/day. Verified on the running original.
  * Type tip (`DrawPlaneTipContents`, headline (6,6), contents (6,28), small black font, values left-aligned at contents x + 70): manufacturer, type name on the next line (+12); then passengers / payload (t) / range (km) at +15, +26, +37; speed at +52; thrust (kN) / tank (l) / consumption (l/h) at +63, +74, +85; pilots / attendants at +115, +126; price at +141 (offsets from the contents y). No build year and no description text for types. Individual planes: plane name as headline, manufacturer and type at contents y −5 / +7, the same rows shifted by 12 without thrust, crew as "assigned (needs)", condition only for own planes in the workshop, then price and build year.
* Quantity choice: 1, 2, 3, 5 or 10 planes. Only quantities that keep money >= `DEBT_LIMIT` are offered.
* Purchase: for each plane, `BuyPlane(type)` creates a new plane with an unused random name, condition 100 and build year 2002+year. It debits the list price (ledger reason 2010). Stewardess count is set from the fleet average and clamped to [0, 2x type value].
* After the purchase the code sends a network message (`ATNET_BUY_NEW`), applies the bodyguard rebate, re-maps workers and refreshes the personnel advisor.
* "Sell" is not done here; the broker refers the player to the museum (Dlg Makl/110). "When delivered" gives Dlg Makl/170.
* Phone medium: a different greeting, and the room counts as busy.

### Bubble / phone
Bubble at (540,200), style 2. When reached by phone, the room window is created as a dialog window with `HandyOffset` 378.

### Leave
EXIT hotspot, right-click, or the closing time. The destructor releases the talker.

---

## 2. Museum (used planes) - `CMuseum` (Museum.cpp) [H]

**Entry:** `Museum.cpp: CMuseum::CMuseum`. `ROOM_MUSEUM` (102), GLI `room/museum.gli`, background `MUSEUM`. It also loads `gli/TipPl.gli` and uses its bitmap `BLOC1` as the plane-tip background. Talker `TALKER_MUSEUM`. Ambient volume 40.

Open from 11:00, closed on weekday 5 and 6 (condition block 3).

On entry the code calls `Sim.UpdateUsedPlanes()`. This refills empty used-plane slots: up to `TickMuseumRefill/20` slots are filled with random used planes, and the counter resets. In mission `DIFF_ATFS10` between day 40 and 50 all slots are emptied.

### Assets
* Rotor animation `ROTOR01`,`ROTOR02`: REPEAT, speed 1, at (82,304).
* Mini planes in the hall: `MINI0101`..`MINI0304` (index = slot\*4 + size). They are drawn at slot positions (140,281), (206,293), (291,276) when that used-plane slot has a name. Size = clamp((passengers-100)\*4/400, 0..3).
* GLI also contains `FLUG00`..`FLUG03`, which the code does not use.
* Ambient loop `museum.raw` (engine hum) when effects are on.

### Characters
Museum keeper `SP_Mann` at (420,250), and a flying skeleton gag `SP_Skelett` at (420,180). Three comm variables couple them: Trigger Skeleton, Trigger Man and Trigger Emergency, all starting at -1.

| Clip | smk/sound | Mood | Wait | Comm | Decision | Succ |
|---|---|---|---|---|---|---|
| M0 | MU_PutzA (+raw) | IDLE | 0 | - | - | A9->0, A9->1, E1->2 |
| M1 | MU_PutzB (+raw) | IDLE | 0 | - | - | A9->0, A9->1, E1->2 |
| M2 | MU_Turn | IDLE | 0 | Emergency:=-1 (pre) | - | A1->5, E1->3, E1->5, E1->6 |
| M3 | MU_RedeA | TALKING | 0 | - | - | A8->3, A8->4, E1->5, E1->6 |
| M4 | MU_RedeB | TALKING | 0 | - | - | same as M3 |
| M5 | MU_RedeW | LISTENING | 5 | Man:=-1 (post) | - | A9->5, A1->9, E1->3, E1->5, E1->6 |
| M9 | MU_RedeW | LISTENING | 370 | Skeleton:=1 (pre) | Man | A1->5, E1->3, E1->5, E1->6 |
| M7 | MU_Guck | LISTENING | 0 | Man:=-1 (pre) | - | A1->8 |
| M8 | MU_GuckZ | LISTENING | 0 | Skeleton:=3 (post) | - | A1->5 |
| M10 | (none) | LISTENING | 0 | Man:=-1 (post) | - | A1->11 |
| M11 | MU_Guck | LISTENING | 10 | Man:=-1 (pre) | - | A1->12 |
| M12 | MU_GuckZ | LISTENING | 0 | - | - | A1->13 |
| M13 | MU_Hit | LISTENING | 0 | Skeleton:=4 at frame 3 | Man | A1->5 |
| M6 | MU_TurnZ | IDLE | 0 | Emergency:=0 (pre) | - | A9->0 |

| Clip | smk/sound | Repeat | Wait | Comm | Decision | Succ |
|---|---|---|---|---|---|---|
| S0 | FlyingMW (idle) | 1 | 60 | - | Skeleton | A9->0 |
| S1 | (none) | 1 | 0 | Skeleton:=-1 (pre) | - | A9->2, A9->6 |
| S2 | FlyingM (+raw) | 1-3 | 0 | Man:=7 (post) | Emergency | A9->0 |
| S6 | FlyingM (+raw) | 1-2 | 0 | Man:=10 (post) | Emergency | A9->7 |
| S7 | FlyingM (+raw) | 2 | 0 | - | Emergency | A9->0 |
| S3 | FlyGrinz (+raw) | 1 | 0 | Skeleton:=-1 (pre) | Emergency | A9->0 |
| S4 | (none) | 1 | 0 | Skeleton:=-1 (pre) | - | A9->5 |
| S5 | Fly_auah | 1 | 0 | Skeleton:=-1 (pre) | Emergency | A9->0 |

How the gag plays out [M]:
1. While the keeper waits in the long listening pose (M9), he triggers the skeleton.
2. The skeleton flies (S2 or S6, 50/50).
3. If it is not caught (S2), the keeper looks around (M7/M8) and the skeleton grins (S3).
4. If it is caught (S6/S7), the keeper looks around and hits it (M11..M13), and the skeleton says "ouch" (S5).
5. When a dialog ends, the keeper's turn-back clip sets Emergency to 0. That forces the skeleton back to its idle clip.

### Hotspots / tooltips
| Rect | Cursor | Click |
|---|---|---|
| (0,0)-(233,227) or (533,312)-(605,352) | EXIT | leave |
| (125,226)-(374,330) | HOT (id 10) | slot = (x-125)\*3/240 (0..2). If the slot holds a plane: clear the advisor comment, then `StartDialog(TALKER_MUSEUM, MEDIUM_AIR, slot)` |
| (408,193)-(590,430) | HOT (id 11) | `StartDialog(TALKER_MUSEUM, MEDIUM_AIR)` (greeting) |

Hovering (125,226)-(364,330), with no dialog or menu open:
* The hovered slot is computed the same way.
* If the slot holds a plane, the code draws a plane tip on `BLOC1`, centred at x = 245 + (slot-1)\*100 and y = window top + 50.
* If the player has the plane advisor, an advisor comment is shown (Std Advi/4000..4005). Built before 1960 gives 4001. Otherwise the condition decides: <20 gives 4000, <40 gives 4002, <60 gives 4003, <80 gives 4004, otherwise 4005.
* If the slot is empty, the cursor is normal.
* When the mouse is not over a slot, the advisor comment is cleared.

**WIDESCREEN:** the tip is drawn in screen coordinates (PrimaryBm), not room coordinates. Offset it by the room's canvas origin.

### Dialog-driven actions (`Dialog.cpp: TALKER_MUSEUM`, `Stdraum.cpp: StartDialog`)
* Started with a slot (0..2): the keeper quotes the price `UsedPlanes[slot].CalculatePrice()` (Dlg Muse/500).
* Buying (node 600) requires money minus price >= `DEBT_LIMIT`. The plane is copied into the player's fleet with worst condition = condition-20 and globe angle 0. Money is debited (reason 2010, statistic "other expenses"). The code then sends the network messages, applies the bodyguard rebate, clears the slot name (the slot becomes empty), resets `TickMuseumRefill` to 0, ends the dialog, re-maps workers and updates the personnel advisor.
* Selling: node 700/701 opens `MENU_SELLPLANE`; phone medium cannot sell (it offers the 201..203 choices, or 211..213 after the second visit). The list is the original's plane table: column titles `Schd 1001` / `Schd 1002`, the plane name and its condition in percent; a plane with fewer pilots or attendants than its type needs, or with a problem, has a red name (the value stays black); red rows come first, then by name. The hover card shows the plane's value (not the sale price). The offer line for a museum plane (500) names its value as an argument.
  * Picking a plane re-enters the dialog with par 100..103, which selects texts 710..713.
  * The keeper offers 90% of `CalculatePrice()`.
  * On acceptance (720): money is credited (reason 2011), the plane is removed, the network message is sent and money is synchronised. If the removed plane was the reference plane, the reference is cleared. Workers are re-mapped.
  * 711 means "plane in use, cannot sell".
* Greeting variants depend on `DialogOvertureFlags` DIALOG_MUSEUM / DIALOG_MUSEUM2. The destructor sets MUSEUM2 if MUSEUM was set, so the second visit uses different greeting texts.
* Bubble at (440,234), style 2. Phone `HandyOffset` 367.

### Quirks
* `ReloadBitmaps()` decrements the talker reference right after the constructor incremented it. The net effect is that the museum never holds a talker reference. This is harmless but worth noting for anyone porting the reference counting.
* `BuyUsedPlane`/`SellUsedPlane` are dead stubs; the dialog does the real work.

---

## 3. Designer office - `CDesigner` (Designer.cpp) [H]

**Entry:** `Designer.cpp: CDesigner::CDesigner`. `ROOM_DESIGNER` (125), GLI `room/designer.gli`, background `DESIGNER`. The GLI also contains `CONSTRUCTION_OFFICE`, which this code does not use. `HandyOffset` 320. Talker `TALKER_DESIGNER`. Ambient volume 60. Background loop `design.raw` when effects are on; the destructor stops it at minimum volume. The airport shortcut key is 'X'. The SDL port also creates the `myplanes/` folder here.

### Characters / animations (all smacker persons, no dialog moods except Stan)
| Person | Pos | Clips |
|---|---|---|
| Stan (designer) `SP_Stan` | (198,18) | 0 `DeManW` IDLE wait 10: A9->0, E1->1, E1->2, A1->3, A1->4, A1->5. 1 `DeManW` LISTENING wait 50: A9->1, E5->2, E5->0. 2 `DeManS` TALKING: A9->2, E1->1, E1->0. 3 `DeManSh` IDLE (scales/"Schuppen"): A9->0. 4 `DeManFl` IDLE (flash): A9->0. 5 `DeManCo` IDLE (couch): A9->0 |
| Bat | (0,13) | 0 `DeBatW` wait 50-190: A9->0, A1->1. 1 `DeBat`: A9->0 |
| Worker / swallow | (330,262) | 0 `DeWorker`: A9->0, A1->1. 1 `DePlane`: A9->0 |
| Blender | (337,92) | 0 `DeBatW` wait 10-90: A9->0, A1->1. 1 `DeBlnder` wait 10-90: A9->0. Note: clip 1 is registered with id 0 in the source, so it is unclear whether `DeBlnder` ever plays [M] |
| Leg | (435,222) | 0 `DeLeg` wait 10: A9->1. 1 `DeLegW` wait 10-90: A9->1, A2->0 |

Draw order: bat, worker, leg, blender, Stan.

### Hotspots
| Rect | Cursor | Click |
|---|---|---|
| (296,240)-(361,337) | EXIT | leave |
| (200,26)-(354,341) | HOT | `StartDialog(TALKER_DESIGNER, MEDIUM_AIR, 1000)` |
| (43,112)-(250,307) | HOT | `EnterRoom(ROOM_EDITOR)` (aircraft constructor, §6) |

### Dialog-driven actions (`Dialog.cpp: TALKER_DESIGNER`)
* Explanatory chain: Dlg Desi/2000..3040.
* "Buy a design" (4000): the dialog checks whether any buildable `.plane` exists. The menu data table is filled with "XPlane types", meaning buildable designs in `myplanes/`. If there are none it says 4002; otherwise 4001 opens `MENU_BUYXPLANE`.
* Hovering a menu row shows that design's tip (`DrawXPlaneTipContents`, using the file name from data column 2).
* Quantity choice: 1, 2, 3, 5 or 10, limited by `DEBT_LIMIT` as at the broker. The price is `CalcCost()` of the design.
* Purchase: `BuyPlane(CXPlane)` for each copy. It creates a plane with type id -1 and condition 100. Plane-type stats are derived from the design:
  * economy seats = passengers\*6/8, first class = passengers\*1/8
  * the range, speed, passenger count, pilot count, crew count, tank, fuel use, price and noise values come from the design's Calc\* functions
  * maintenance factor = (CalcWartung+100)/100
  * crew is set from the fleet average, clamped to [0, 2x]
  * the design itself is embedded in the plane (`p.XPlane`)
  * money is debited with reason 2010
* After the purchase: network buy, bodyguard rebate, re-map workers, update the advisor.
* Bubble at (268,126), style 0. Phone `HandyOffset` 320.

---

## 4. Workshop - `CWerkstatt` (Werkstat.cpp) [H]

**Entry:** `Werkstat.cpp: CWerkstatt::CWerkstatt`. `ROOM_WERKSTATT` (110), GLI `room/werkstat.gli`, background `WERKSTAT`. Talker `TALKER_MECHANIKER`. Ambient volume 40. (`CAnimation` is also implemented in this file.)

### Static bitmaps
* `DOOR` at (534,73), blitted with transparency. It is drawn unless the player's security flag bit 7 (planes guarded from outside) is set.
* `OIL` (oil can) at (72,294), drawn while the player does not own `ITEM_OEL`.

### Animations
* `SOUD0000..0020` (21 frames, welding/saw): exists on only 1 in 6 visits (or with the "anim now" flag). REPEAT, prelude 150, speed 2, cyclic wait 100, sfx `saw.raw`, at (470,290).
* `FLAME01..03`: REPEAT speed 1, first frame shown, at (104,314).
* `LIGHT01..02`: REPEAT, prelude 400, speed 2, cyclic wait 300, repeated 30 times, transparent, at (244,89).
* Random background sfx: `crash.raw` (avg 100 s, start -10 s), `crash2.raw` (90 s / 40 s), `crash3.raw` (120 s / 75 s).

### Characters
**Mechanic Jack `SP_Mann`** at (190,209); clip 8 is at (173,209).

| Clip | smk | Mood | Repeat | Wait | Succ |
|---|---|---|---|---|---|
| 0 | WK_Wait | IDLE | 1 | 10 | A9->0, A1->4, E1->1 |
| 4 | WK_Tuch | IDLE | 1 | 0 | A9->5 |
| 5 | WK_Putz | IDLE | 1-3 | 0 | A9->6 |
| 6 | WK_TuchZ | IDLE | 1 | 0 | A9->0 |
| 1 | WK_Turn | IDLE | 1 | 0 | E1->2, E1->3 |
| 2 | WK_Rede | TALKING | 1 | 0 | A9->2, E1->3, E1->7 |
| 3 | WK_RedeW | LISTENING | 1 | 10 | A9->3, E1->2, E1->7, A3->8 |
| 8 | WK_Rauch (smoke) | LISTENING | 1 | 0 | sets KommVar:=1 at frame 18; A9->3, E1->2, E1->7 |
| 7 | WK_TurnZ | IDLE | 1 | 0 | A9->0 |

**Plane in the hangar door** at (535,72):
* Normal case: `SP_Wer` cycles WerStand (wait 100-210) -> WerL -> WerStand -> WerR, which looks like a worker in a plane.
* If security flag bit 7 is set: `SP_Hund` plays `Spike.smk` (guard dog) in a loop instead, and the door bitmap is not drawn.

**Slime** at (0,275). It depends on the global `Sim.Slimed`: -1 means clean. `Slimed` is reset to -1 every 6th day (`Sim.cpp`).
* Clean: clip 0 `SLIMEW` waits 30 ticks. Its decision variable is `Sim.Slimed`. On 10% of visits (or with the "anim now" flag) it has successor A1->1. Otherwise it only loops.
* Clip 1 `SLIME01`(+raw) is the slime spill. At its end it sets `Sim.Slimed`:=2, and then clip 2 `SLIME02` loops.
* Already slimed on entry: `SLIME02` loops for the whole visit.

**Bubbles** (only while slimed): 5 copies at (93,441), (111,418), (137,430), (54,427), (14,427). Each cycles `BLASEW` -> `BLASE0n` (n = index%3+1) -> `BLASEW` with a random wait of 30-110 to 130-210 ticks. Their decision variable is `Sim.Slimed`. **WIDESCREEN/clip:** these sprites extend below y=440. The original clips them at the room area, so keep that clipping.

**Bomb gag** at (180,386): `BombeW` waits 100-210 ticks with decision variable KommVar; then `Bombe`(+raw) plays and resets KommVar to -1 at its start. Each frame, KommVar is forced to -1 while the floor is clean. So the bomb fires only when the floor is slimed and the mechanic lights a cigarette (frame 18 of `WK_Rauch`) [M].

Draw order: the dog or the plane, then slime, bubbles, door, oil can, mechanic, bomb, saw, flame, light.

### Hotspots
| Rect | Cursor | Click |
|---|---|---|
| (0,0)-(50,439) | EXIT | leave |
| (195,212)-(263,437) | HOT | `StartDialog(TALKER_MECHANIKER, MEDIUM_AIR, 1)` |
| (72,294)-(101,315), only without `ITEM_OEL` | HOT | `MechTrust==0`: dialog par 20 (refuses). Otherwise, if there is inventory space: dialog par 21 and give `ITEM_OEL` |

Note: the paint routine sets the default look with area `ROOM_ARAB_AIR` instead of the workshop area. This is a copy-paste slip and harmless.

### Item interactions (from `Stdraum.cpp` item use)
* Beer (`ITEM_BIER`) given to the mechanic: `MechTrust` = max(1, ·), `MechAngry`=0, the beer is consumed, then dialog par 2.
* Perfume (`ITEM_PARFUEM`) used while slimed: it turns into `ITEM_XPARFUEM` (stink perfume).

### Dialog-driven actions (`Dialog.cpp: TALKER_MECHANIKER`)
* Maintenance mode: nodes 4010..4013 set `MechMode` 0..3, followed by a network update. The daily cost is shown as `gRepairPrice[mode] x numPlanes / 30`, with `gRepairPrice` = {1200, 18000, 48000, 150000}.
* Node 4014: `MechTrust` 1 -> 2 (the hidden extra mode).
* The dialog offers three menus:
  * `MENU_PLANEREPAIRS` (node 2003): a plane condition list
  * `MENU_PLANECOSTS` (2501): hover highlights the plane rows and shows a tip with the extra flag TRUE
  * `MENU_PANNEN` (2004): breakdowns
* Smoking warning (2005): sets `MechAngry`:=1. If the warning repeats, `MechAngry`:=2; the player is thrown out (leaves the room when the medium is AIR) and the dialog stops. While `MechAngry==2` the greeting refuses (5002/5003).
* Bubble at (200,203), style 2. On the phone the bubble is at (445,199) and `HandyOffset` is 131.

---

## 5. Plane properties / equipping - `CPlaneProps` (PlanProp.cpp) [H]

**Entry:** `PlanProp.cpp: CPlaneProps::CPlaneProps`. `ROOM_PLANEPROPS` (120). Reached from the office desk plane (Buero click id 15), but only if the player owns at least one plane. GLI `room/planprop.gli`. There is no background id; the canvas is a fresh 640x440 buffer. `HandyOffset` 320. Ambient volume 40. No talker.

### Bitmaps
| Use | Names |
|---|---|
| Comfort page background | `BACK` |
| Technical page background | `BACK2` |
| Headline mask | `MENUMASK` |
| Stewardess badge (security flag bit 6) | `BADGE` |
| Floor/deco preview | `DECO1`,`DECO2` |
| Seat preview [seat\*3+tray] | `SITZ1_1`..`SITZ3_3` |
| Food on tray [food\*2 + A/B] | `ESSEN1A,1B,2A,2B,3A,3B` |
| First-class percentage label | `0_PRZ`..`100_PRZ` (11) |
| Technical previews (level 1/2; level 0 shows none) | `SOS2,3` safety, `REIFEN2,3` tyres, `COCKPIT2,3` electronics, `TURBINE2,3` engines |
| Animated selection frame (8 frames, 150 ms each) | `HIGH01`..`HIGH08` |
| Stars (current level) | `STERN1..3` |
| Buttons | `LEFT COPY OK PASTE RIGHT MINUS PLUS USEL USER USELOFF USEROFF` (indices 0..10) |
| Digits | `KL_0`..`KL_9` |
| Stewardess strip | `S_A_1 S_A_2 S_A_3` (normal crew: left cap, middle, right cap), `S_B_1..3` (extra crew), `S_BC_2` (joiner), `S_C_1..3` (unfilled) |

### Model
Each plane has a current level and a target level for each of eight properties. All levels are 0..2. A star column shows the current level, and the animated frame marks the target if it differs.
* Comfort page: seats (Sitze), trays/entertainment (Tabletts), decor (Deco), food (Essen).
* Technical page: tyres (Reifen), engines (Triebwerk), safety (Sicherheit), electronics/cockpit (Elektronik).

Edits only change the targets. The plane is refitted later, the next time it is on the ground in a city while the owner is not in this room (or at end of day) (`Planetyp.cpp` daily/ground handling). At that point each changed property costs the amount in the table below. A negative result is a refund, booked to "other income". Food has no one-off cost.

Cost per property change = new price minus half the old price:

| Property | Price per level 0/1/2 | Multiplied by |
|---|---|---|
| Seats | 300 / 1200 / 3000 | plane-type passengers |
| Trays | 100 / 800 / 3400 | passengers |
| Decor | 500 / 1500 / 6000 | passengers |
| Food (running cost per flight) | 10 / 20 / 50 | passengers (displayed only) |
| Engines | 100000 / 750000 / 3500000 | 1 |
| Tyres | 50000 / 300000 / 900000 | 1 |
| Electronics | 600000 / 1500000 / 8000000 | 1 |
| Safety | 0 / 2000000 / 6000000 | 1 |

`PLAYER::CalcPlanePropSum` adds up all pending costs over the whole fleet. A target change is rejected if that sum would exceed the player's money; the advisor girl then says Std Schd/1810. Food changes are never rejected.

### Page / scroll state
* `ActivePage` runs from 0 to 100. 100 = comfort page fully shown, 0 = technical page.
* On entry `ActivePage` is restored from `Sim.RFActivePage`, which starts at 100. On leaving it is saved as 0 or 100 (>50 counts as 100).
* `ActiveDir` (-1/0/+1) moves the page by elapsed ms/10, so a full flip takes about 1 s.
* When `ActivePage` >= 50, the comfort page is drawn shifted right by (100-AP)\*229/100. Otherwise the technical page is drawn shifted left by AP\*229/100.
* During the transition the outgoing side is darkened with ColorFX in 8 bands 25 px wide. **WIDESCREEN:** the slide uses absolute x 400 or 215 to 240 plus the scroll offset, and draws on a 640-wide buffer.
* Interactive hotspots are active only when the page is fully at 100 (comfort) or 0 (technical).
* The first time per game (`Sim.UsedPlaneProp2` false), the page-flip arrow `USEL` blinks for 5 s (500 ms on/off) at (172,50)+headline offset.

### Plane selection
* The list holds all of the player's planes (`FillWithPlanes`).
* On entry the room selects the plane with id `Sim.PlanePropId` if it still exists, otherwise the first plane. The id is stored again on leaving.
* `PlaneIndex` -1 means there are no planes, and nothing plane-specific is drawn.

### Headline bar
`MENUMASK` is drawn at (229\*(100-AP)/100, 2), so the bar slides with the pages. The plane name is printed centred in big grey font in (20,3)-(400,20) plus the offset.

The hotspots below are only active at AP 0 or 100. Their x values are relative to the headline offset (+229 on the comfort page, +0 on the technical page):

| Rect (rel.) | Tooltip | Cursor/Id | Action |
|---|---|---|---|
| (134,26)-(164,44), if not the first plane | Tool/3100 | HOT 10 | previous plane. Holding the button auto-repeats: after 800 ms, then every 400 ms. Double-click also steps |
| (164,26)-(190,50) | Tool/3101 | HOT 11 | **Copy**: store all 8 targets and the crew factor `MaxBegleiter/ptAnzBegleiter` in global `Sim.RF*` |
| (172,50)-(192,55), (178,56)-(195,69), (182,60)-(207,67); comfort page only | Tool/3105 | HOT 90 | flip to the technical page (ActiveDir -1), stop the blinking |
| (224,48)-(243,52), (218,53)-(239,56), (210,57)-(234,64); technical page only | Tool/3106 | HOT 91 | flip to the comfort page (ActiveDir +1) |
| (190,26)-(226,62) | Tool/3102 | EXIT 12 | **OK / leave**. While hovering, the advisor shows the total pending cost of the fleet (Std Schd/1820 if positive, 1821 refund if negative) |
| (226,26)-(253,50) | Tool/3103 | HOT 13 | **Paste**: apply the stored engine, tyre, electronics and safety targets. Rolled back if unaffordable |
| (253,26)-(282,44), if not the last plane | Tool/3104 | HOT 14 | next plane (same auto-repeat as previous) |

The hovered button shows its bitmap at fixed spots relative to the offset: LEFT at (135,27), COPY at (164,27), OK at (191,27), PASTE at (210,27), RIGHT at (248,27), USEL/USER at (172,50)/(210,50). When the comfort page is not active, `USELOFF` is drawn at (172,50). When the technical page is not active, `USEROFF` is drawn at (210,50).

**Paste quirk:** Paste backs up the seat, tray and decor targets but does not apply the stored comfort values. Only the 4 technical targets are pasted, and the crew factor is never pasted. This matches the original's behaviour [H]. The manual describes the copy/paste as covering "the equipment"; whether it was meant to include comfort is an open question (OQ-5).

### Comfort page (AP = 100). Coordinates are absolute; add the scroll offset during transitions.
* Star rows (`StarOffsets` 0..3): seats (498,8), trays (498,87), decor (498,167), food (498,284). Level c occupies the cell `(x+47c, y)-(x+47c+43, y+36)`.
  * Hovering a cell shows the animated frame at (cell-1,-1) with tooltip Tool/3200+row and click id 40+c+10\*row (40..42, 50..52, 60..62, 70..72).
  * Clicking sets the target (with the money check, except for food), followed by a network update of the plane props.
  * **While hovering, the preview and cost labels temporarily show the hovered level** as if it were the target.
* Big preview, left side:
  * floor `DECO1/2` at (0,27), drawn if the decor target < 2
  * seat sprite at `SeatOffsets[seatTarget]` = (95,130) / (94,124) / (75,104)
  * two food sprites centred at seatOffset + `FoodOffsets[(seat\*3+tray)\*2 + 0/1]`; the pairs are listed in Appendix B. An offset y of 999 puts the sprite off screen, meaning there is no second tray
* Cost labels: right-aligned in x 502..631, printed only if target ≠ current. Each is "<Std Schd/1800> <amount>". Positions: seats y 58, trays y 138, decor y 218, sum y 252. The food running cost is always printed: "<amount> <Std Schd/1801>" at y 334.
* Crew (flight attendants):
  * Minus (497,367)-(541,402): Tool/3210, id 15. It lowers `MaxBegleiter`, but not below the type's base crew `ptAnzBegleiter`.
  * Plus (592,367)-(636,402): Tool/3211, id 16. It raises `MaxBegleiter` up to 2x base.
  * Both buttons re-map workers and send a network update.
* Crew digits: `KL_n` at (567,374), tens digit at (551,374).
* Crew strip at y 408, starting at x 473, drawn as three groups:
  * hired base crew, min(AnzBegleiter, base): `S_A` caps
  * hired extra crew: `S_B` caps
  * unfilled slots, MaxBegleiter-AnzBegleiter: `S_C`. The joiner `S_BC_2` is used when it follows the extra group; it is shifted 9 px left.
* The badge is drawn at (420,352) when security bit 6 is set.

### Technical page (AP = 0)
* Star rows (`StarOffsets` 4..7): tyres (66,8), engines (66,87), safety (66,167), electronics (66,246). Same cell layout as the comfort page. Tooltip Tool/3204+row, click ids 140..142, 150..152, 160..162, 170..172. Each click has the money check. Hovering previews the level the same way as on the comfort page.
* Previews, shown only if the target > 0: tyres at (325,74), engines at (444,42), safety at (305,247), electronics/cockpit at (442,237).
* Cost labels are left-aligned at x 8..205: y 58 / 137 / 216 / 295, and the sum at y 329.
* **First-class share:**
  * `total = economy + 2 x firstClass` (one first-class seat takes two economy seats).
  * The displayed value `FCpercent` = round(2\*FC\*100/total, to 10s) / 10, as digits at (133,374) and the tens at (117,374). The `N_PRZ` label is drawn at (62,408).
  * Minus (65,367)-(106,402): Tool/3213, id 115. Plus (161,367)-(204,402): Tool/3212, id 116. Each press moves the share by ±10 percentage points:
    1. newFC = total x pct / 2 / 100, and newEco = total - 2\*newFC.
    2. If newEco is unchanged, nudge FC by ±1 (and Eco by ∓2).
    3. Accept only if both values are >= 0 and FC + Eco >= the largest passenger count of any not-yet-landed scheduled job on that plane (`GetMaxPassengerOpenFlight`).
  * No network update is sent here (OQ-6). Double-click repeats the action.
  * This replaced the manual's "cleaning crew" control (manual p.39). The cleaning-crew code is commented out.

### Leave
OK button (id 12), right-click, or id 999. The destructor stores `PlanePropId` and the page.

---

## 6. Aircraft constructor (editor) - `CEditor` (Editor.cpp) [H]

**Entry:** `Editor.cpp: CEditor::CEditor`, entered from the designer office (§3). `ROOM_EDITOR` (124), GLI `room/editor.gli`, background `EDITOR`. `HandyOffset` 320. Ambient volume 60. Background loop `secure.raw`. Fonts: `misc/stat_3.mcf` (red, errors) and `misc/stat_4.mcf` (yellow, stats). No talker.

### Data
**Part catalogue (`gPlaneBuilds`, 37 entries).** The values are overridden at startup and on room entry from `data/builds.csv`, columns `Id;ShortName;Cost;Weight;Power;Noise;Wartung;Passagiere;Verbrauch`, matched by row order. Use the CSV values shipped with the game. Fields that are not in the CSV:

| Group | Short names | Bitmap index (into the part list) | z-base |
|---|---|---|---|
| Body (B) | B1..B5 | 0..4 | 10000 |
| Cockpit (C) | C1..C5 | 5..9 | 10010 |
| Tail (H) | H1..H7 | 10..16 | 9000 |
| Right wing (R) | R1..R6 | 17..22 | 12000 |
| Motor (M) | M1..M8 | 23..30 | 7500 |
| Left wing (L) | L1..L6 | 31..36 | 8000 |

Editor bitmaps (`room/editor.gli`), in the same index order: `BODY_A01..05`, `CPIT_A01..05`, `HECK_A01..07`, `RWNG_A01..06`, `MOT_A01..08`, `LWNG_A01..06`.

Strip icons: `S_B_01..05`, `S_C_01..05`, `S_H_01..07`, `S_W_01..06`, `S_M_01..08`. The strip shows right-wing icons for wings; the left wing is added automatically.

Airport/side-view bitmaps (std GLI, loaded in `Init.cpp`): `P2D_<shortname>`. They are drawn at the stored 2D offsets in "behind glass" views, for example plane tips.

**Relations (`gPlanePartRelations`, 307 entries, Appendix A).**
* A relation means: part To can attach to part From (or to the empty desk when From = -1).
* The editor placement is the parent's 3D position + offset3d; the airport/side view uses parent pos2d + offset2d.
* Notes 1-3 are modifiers, `zAdd` is a draw-order bias, and Noise is extra noise.
* Slot is the 2-char slot this relation needs. RulesOutSlots is the list of 2-char slots that the relation occupies once used.
* `relation.csv` overrides offsets, notes and noise by row order. From/To, zAdd, Slot and RulesOut exist only in code, and they are listed in Appendix A.
* The array index is stored in saved files and is used for mirror pairing (R relation at index i, L twin at index i+1).

Note codes:

| Code | Name | Effect |
|---|---|---|
| 100 | STD | none |
| 101 | KAPUTT / VERBRAUCH (same value!) | maintenance +10 **and** consumption +2500 |
| 102 | KAPUTTXL / VERBRAUCHXL | maintenance +20 and consumption +5000 |
| 103..108 | SPEED300..800 | speed cap |
| 109..112 | PILOT1..4 | +1..4 pilots |
| 113..116 | BEGLEITER4/6/8/10 | +4/6/8/10 cabin crew |

The shared values for 101 and 102 make maintenance notes also add fuel consumption, because both sums test the same code. This is a faithful quirk; flag it as OQ-1.

### Derived stats (`CXPlane::Calc*`) [H]
* **Cost, Weight, Passengers, Power:** sums of the part values.
* **Consumption:** sum of the part values plus the note bonuses.
* **Noise:** sum of the part noise plus the noise of each part's relation.
* **Maintenance (Wartung):** sum plus the note bonuses.
* **Speed:** 0 if power = 0. Otherwise (power-6000)\*730/44000 + 270, as integer math. Then, for every SPEEDxxx note where speed > cap, speed = (2\*speed + cap)/3, applied repeatedly in part order.
* **Pilots:** sum of the PILOT notes, minimum 1. **Cabin crew:** sum of the BEGLEITER notes, minimum 1.
* **Tank:** 7 x (sum of the weight of all L/R wing parts). The non-display version caps the tank at 22 x consumption.
* **Range:** tank/consumption x speed, or 0 if consumption <= 0. Integer division happens first.
* **Noise label** (Std Misc): <=0 gives 8400; >0 gives 8401; >20 gives 8402; >40 gives 8403; >60 gives 8404; >80 gives 8405; >100 gives 8406.
* **Maintenance label:** the default (<=-30) is 8508; >-30 gives 8507; >-20 gives 8506; >-10 gives 8505; >=0 gives 8504; >20 gives 8503; >50 gives 8502; >80 gives 8501; >110 gives 8500.
* **Validity (`GetError`):** returns nothing until at least one part of each of B, C, H, R and M is present. Then:
  * every motor on an R wing needs a motor on an L wing whose relation Id is +10, and the reverse. If not: error Std Misc/8300.
  * power x 4 < weight gives error Std Misc/8301.
  * Tail motors are not checked for symmetry.
* **`IsBuildable`:** all five categories are present and there is no error. Only buildable designs appear at the designer.

### Screen layout (all absolute 640x440)
* Background `EDITOR`. The plane parts are drawn in z order. Then the masks `MASKE_O` at (0,0) and `MASKE_U` at (0,343) are drawn over them to cover parts that hang over the board edge.
* The plane name is centred in green in (193,7)-(471,28). Clicking (212,0)-(438,15) (id 10) opens `MENU_RENAMEEDITPLANE`; confirming it sets the name.
* Prev/next design buttons: `BUT_DL0/1/2` at (185,0), id 800, rect (185,0)-(212,23); `BUT_DR0/1/2` at (438,0), id 801, rect (438,0)-(465,23). The three bitmap states are normal, hover and pressed (all buttons here).
* New: `NEW0/1/2` at (0,158), rect (0,158)-(38,188), id 901. Delete: `DELETE0/1/2` at (0,192), rect (0,192)-(38,224), id 900.
* OK: `OK0/1/2` at (602,158), rect (602,158)-(640,188), EXIT cursor, id 999. Cancel: `CANCEL0/1/2` at (602,192), rect (602,192)-(640,224), EXIT cursor, id 998. **WIDESCREEN:** these buttons touch x=640, so pin them to the right edge of the 640 canvas.
* Part strip, 5 slots, c = 0..4 for B, C, H, W, M, with slot width 127:
  * prev arrow `BUT_TL*` drawn at (4+127c, 370), hit (4+127c,370)-(27+127c,426), id 100c+100
  * next arrow `BUT_TR*` at (101+127c, 370), hit (101+127c,370)-(124+127c,426), id 100c+101
  * pick area (27+127c,363)-(101+127c,436), id 100c+105
  * the icon of the selected variant is centred at (66+127c, 399)
  * a disabled slot draws its icon translucent (ColorFX level 5) and has no hotspots or arrows
* Stats, shown only if the plane has at least one part:
  * column at x 480, lines at y 20 (weight, Misc/8200), 35 (passengers, Misc/8201, with args passengers and passengers/10), 50 (consumption, 8202, if >0), 65 (noise, 8204, label + |noise|, if ≠0), 80 (maintenance, 8206, label), 95 (speed, 8207, if >0), 110 (tank, Plne/1008, if >0), 125 (range, Plne/1001, if >0), 150 (cost, 8203)
  * each line is drawn 9 times in black (offsets -1..1) as an outline, then in yellow
  * the error line is red at (480,180), wrapping at x 580
  * while the mouse is over an enabled strip slot, the stats show that single part's values (weight, passengers, noise, maintenance, cost; the others are 0)
  * **WIDESCREEN:** the column is right-anchored at x 480..640
* Enabled slots (`UpdateButtonState`):
  * Body: only while there is no body.
  * Cockpit, tail, wings: once a body exists and that category is still absent. The wing slot is disabled once an L wing is present.
  * Motor: enabled when any L wing or any tail is present.
* Wing variant skipping (`CheckUnusablePart`): while wings are enabled, cycling skips wings that do not fit the present body. Moving forward (or backward after a "prev" click) skips:

  | Body | Skipped wings |
  |---|---|
  | B1 | R1, R2, R3, R6 |
  | B2 | R5 |
  | B3 | R3, R6 |
  | B4 | R1, R3, R6 |
  | B5 | R6 |

### Interaction state machine
State: `PartUnderCursor` (short name or empty), `PartUnderCursorB` (the twin), `DragDropMode` (false / true / -1), the current grip.

1. **Pick from the strip** (left-click on a pick area): `PartUnderCursor` = the selected variant, `DragDropMode` = true. The twin is set as follows: R wing gives `L<n>`, motor gives the same motor, otherwise none.
2. **Every frame while carrying a part**, find the snap target:
   1. Start with `bestDist` = 100 and the default grip (the part centred on the mouse, no snap).
   2. Look at every relation whose To equals the carried part and whose Slot is not ruled out by any placed part (`IsSlotFree` checks each placed part's relation RulesOut list in 2-char units; `*` is a literal char).
   3. **Desk relation (bodies):** the target is offset3d - size/2. It is accepted if |mouse - target| < 2 x bestDist; then bestDist = that distance.
   4. **Parent relation:** for every placed part whose name equals From, the target is parent.pos3d + offset3d. It is accepted if |mouse - (target + size/2)| < bestDist.
   5. With a twin: the twin parent is the *other* wing if the parent is a wing; otherwise it is the same part. The twin relation is index+1, or index-1 if the parent is an L wing or the parent is a tail and the slot's second char is 'L'. The twin position is twinParent.pos3d + twinRelation.offset3d.
   6. If a snap was found, draw the carried part (and the twin) with a white outline. The carried part is drawn interleaved in z order: before the first placed part whose (zBase + y) >= the carried part's (zBase + gripY + zAdd).
3. **Mouse up:** if a snap exists and `DragDropMode` ≠ -1:
   1. Add the part with pos2d, pos3d, parent short name ("" for the desk) and the relation index.
   2. Add the twin the same way if present.
   3. Sort the parts by (zBase + pos3d.y + relation.zAdd) with a bubble sort.
   4. Non-motor parts leave the cursor. Motors stay on the cursor for repeated placement, unless the gesture was a drag from the strip (`DragDropMode` true at release), which clears both.
   5. Finally `DragDropMode` = false.
   * Practical result: a click on the strip, then a click on the board, places a part. Motors can be stamped several times in a row.
4. **Hover over a placed part** (nothing carried): hit-testing uses the bitmap pixels (pixel ≠ 0). Motors are tested first, then the other parts, topmost first. The part is outlined white and gets click id 10000+index.
5. **Left-click on a placed part:** the part is picked up. It is removed and becomes the carried part (an L wing becomes the R name), with `DragDropMode` = -1, so the release does not drop it at once.
   * A body while other parts exist asks `MENU_REQUEST` / `MENU_REQUEST_KILLPLANE` (clear the whole design) instead.
   * Cascade removal repeats until nothing else is removed. It removes any part whose parent short name no longer exists. For a wing, it also removes the mirror wing (relation Id ±200). For a motor on a wing (relation Id 700..1400), it also removes the mirror motor (Id differs by 10 and the array index by 1).
   * Note: the original's boolean grouping lets the "Id = rel-200 for rel 600..799" test apply without the R/parent guard. This is harmless in practice [M].
6. **Right-click:**
   * carrying a part: drop it, which cancels the carry
   * over a placed part: delete it with the same cascade and the same body confirmation
   * over a strip pick area: delete every part of that category (both wings for W) and their orphans; a body with other parts present asks for confirmation
   * right-click never leaves the editor
7. **Buttons:**
   * OK (999): save to the current file, then leave.
   * Cancel (998): leave without saving. Changes since the last automatic save are lost.
   * New (901): save the current design, clear it, create a free numbered file name `myplanes/data<N>.plane`, and set the name to Std Misc/8210 + the number part.
   * Delete (900): with more than one part, confirm (`KILLPLANE`, par 77); otherwise delete at once. Deleting removes the file and loads the alphabetically previous `.plane`. If there is none, the board is cleared and the default name is set.
   * Prev/Next (800/801): save the current design, then load the previous/next `*.plane` in `myplanes/` (wrapping order as given by the file list helper).
   * The confirmation `KILLPLANE` with par 77 calls Delete; without par 77 it only clears the board.
8. **Entry:** create `myplanes/` if needed. The current file is `myplanes/data.plane`, loaded if it exists. The default name is Std Misc/8210. The strip selection starts at the first variant of each slot.

### `.plane` file format (TEAKFILE stream, little-endian) [H]
1. A preview header for external tools or the website:
   * `DWORD size` = 4 + 6\*4 + strlen(name)
   * seven `long` values: cost, weight, consumption, noise, "reliability" (the original stores consumption again here, a bug), speed, range
   * name bytes + NUL
2. Then the TEAKFILE-serialised fields: `CString Name`, `long Cost`, and the parts container. The parts container is a buffer of parts plus the ALBUM bookkeeping.
3. Each part holds: `XY pos2d`, `XY pos3d`, `CString shortname`, `CString parentShortname`, `long parentRelationIndex`.

The loader reads the size and 6 longs, then skips size-24+1 bytes: that covers the 7th long, the name and the NUL. An empty file gives an empty design. TEAKFILE CString/ALBUM encodings are documented by the save-format writer. OpenAT may use its own format but should import originals.

### Internet functions
None exist in this source tree [H]. The manual (p.53) mentions a web exchange on spellbound.de for publishing and downloading designs. That service is defunct. The preview header in `.plane` files was probably meant for it [L]. OpenAT: optional import/export of `.plane` files only.

---

## 7. Advertising agency - `CWerbung` (Werbung.cpp) [H]

**Entry:** `Werbung.cpp: CWerbung::CWerbung`. `ROOM_WERBUNG` (109). Ambient volume 40.

**Closed variant:** if difficulty < `DIFF_NORMAL` and the game is not the free game, the room loads `room/nowerb.gli` (background `WERBUNG`, plus the season overlays `WERBUNG0..3`) and nothing else. In that case there is no talker, no visited mark, no character and no animations. The airport also never shows the agency open hours for these difficulties: condition block 6 applies only for >= NORMAL or free game. Otherwise it is open from 12:00 and closed on weekday 5 and 6.

**Open variant:** `room/werbung.gli`, background `WERBUNG`. The visited mark is set and the talker `TALKER_WERBUNG` is used.

### Assets (open)
* Season overlay `WERBUNG0..3` indexed by `Sim.GetSeason()`, at (495,0). In the closed room it is at (491,0).
* `OHNEDISK` at (82,281), drawn when the player owns the diskette; it shows the empty spot.
* `NOSABO` at (139,241), drawn when security flag bit 5 is set.
* `BOY_01..03`: RANDOM mode, first frame shown, speed 25, at (486,139).
* `SCREEN01..06`: REPEAT, prelude 250, speed 4, at (287,77).
* `CAFE01..15`: coffee machine, REPEAT, prelude 280, speed 3, cyclic wait 380, sfx `Kaffee.raw`, at (161,30).
* `SPRATZL0..4`: sparks, REPEAT, prelude 280, speed 1, cyclic wait 190, sfx `Spratzel.raw`, at (388,79).

### Ad man `SP_Mann` (290,240)
| Clip | smk/sound | Mood | Wait | Succ |
|---|---|---|---|---|
| 0 | WA_copyC (+raw) | IDLE | 0 | A9->0, A3->1, A2->2, E1->3 |
| 1 | WA_copy (+raw) | IDLE | 0 | A9->0, E1->3 |
| 2 | WA_wait | IDLE | 30-60 | A9->0, E1->3 |
| 3 | WA_Turn | IDLE | 0 | A9->4, E1->4, E1->5 |
| 4 | WA_RedeA | TALKING | 0 | A9->4, E1->5, E1->7 |
| 5 | WA_RedeW | LISTENING | 2 | A9->5, A1->6, E1->4, E1->7 |
| 6 | WA_RedeK (wink) | LISTENING | 2 | A9->5, E1->4, E1->7 |
| 7 | WA_TurnZ | IDLE | 0 | A9->0 |

After day 4 the room calls hint `HINT_WERBUNG`.

### Hotspots
| Rect | Condition | Cursor | Click |
|---|---|---|---|
| (0,145)-(70,381) | always | EXIT | leave |
| (302,257)-(454,419) | open | HOT 10 | `StartDialog(TALKER_WERBUNG, AIR, 1)` |
| (74,274)-(111,310) | open and the player has no diskette | HOT 800 | `StartDialog(TALKER_WERBUNG, AIR, 800)`: if `WerbungTrust`, text 8002 (gives the diskette), else 8000 |

Click-only easter eggs (no cursor change):
* Open: clicking (287,136)-(322,164), (322,131)-(351,156) or (354,126)-(393,150) restarts the screen animation.
* Closed: clicking (178,72)-(183,88) or (447,85)-(470,100) plays `fused.raw` (a blown fuse) at plane volume x global ambient.

### Items
A dart (`ITEM_DART`) used in the open agency sets `WerbungTrust`, is consumed, and starts dialog 8001. Node 8002 gives `ITEM_DISKETTE` if there is space. The diskette later cures the laptop virus.

### Dialog actions (`Dialog.cpp: TALKER_WERBUNG`, built only in non-demo builds)
* Image query (2000): clamps the image to ±1000 and reports a band text plus the number.
* Campaign type: 0 = company image, 1 = one route (`MENU_ADROUTE`; hover shows the route tip, which the room positions at `WinP2.x-10-tipWidth` in screen coordinates; **WIDESCREEN:** anchored to the right edge), 2 = everything.
* Six campaign sizes per type. `gWerbePrice`:
  * type 0: 1000, 10000, 50000, 100000, 250000, 2500000
  * type 1: 2000, 8000, 22000, 56000, 120000, 1400000
  * type 2: 5000, 93000, 290000, 870000, 4925000, 60000000
* A campaign must not push money below `DEBT_LIMIT`. Effects:
  * Type 0: image += price/10000 x (size+6)/55, clamped to ±1000.
  * Type 1: the route image += price/30000 (byte), clamped to 0..100. The reverse route gets the same.
  * Type 2: image += price/15000 x (size+6)/55. Every route the player ranks on gets + price x (size+6)/6/120000.
  * Size 0 (the cheapest, "ridiculous" campaign) sends every *human* player a mocking letter (Std Lett/9900-02, 9910-12 or 9920-22).
  * Money is debited with reason 3120+size, statistic "other". The code then syncs the image over the network and applies the bodyguard rebate.
* Bubble at (400,250), style 2. Phone: (526,300), style 1.

### Quirk
In the closed variant the destructor still decrements the talker reference, which was never incremented. Guard against this.

---

## 8. Newspaper kiosk - `CKiosk` (Kiosk.cpp) [H]

**Entry:** `Kiosk.cpp: CKiosk::CKiosk`. `ROOM_KIOSK` (101), GLI `room/kiosk.gli`, background `KIOSK`. Ambient volume 60.

Two states, depending on `Sim.Headlines.IsInteresting`. It is true when today's headlines contain player-related override items (`Misc.cpp` headline generation).

**Awake (interesting):** the kiosk man `SP_Mann` at (120,205) reads. The talker is set, but the reference count is never incremented (quirk).

| Clip | smk | Wait | Succ |
|---|---|---|---|
| 0 | K_LeseR | 40-80 | A1->1 |
| 1 | K_Seite (page turn) | 0 | A1->2 |
| 2 | K_LeseL | 40-80 | A1->3 |
| 3 | K_Turn (+raw) | 0 | A3->0, A1->4 |
| 4 | K_Seite | 0 | A1->3 |

There are no TALKING/LISTENING clips; he keeps reading during dialog [M].

**Asleep (boring):** `SLEEPER` is stamped into the background at (180,170). `Schnarch.raw` loops. The man is not clickable. Items cannot be used on him: every item use in the kiosk is ignored while he sleeps.

### Newspapers
Three papers, composed on entry by `CKiosk::RepaintTip`. Sim headlines: paper c, items d = 0..9.
* Templates: paper 0 uses `PAPER1` (with pictures) or `PAPER1A` (none), paper 1 uses `PAPER2`, paper 2 uses `PAPER3`.
* Text start / wrap box: paper 0 at (8,55) with max (165,242) (y +85 if it has no pictures), paper 1 at (3,34) with max (194,290), paper 2 at (14,44) with max (170,240).
* Each headline is printed in the small black font, clipped to the wrap box (later headlines are still printed, clipped), followed by its height + 5 px.
* At most one picture per paper. Two passes:
  * First pass, before any text: the first headline with a **background photo** (`PASSAG`, `PILOT`, `STEWARD`, `TRASHP0..3`) wins, even if a headline with another kind of picture comes earlier. The paper is overlaid with `PAPER3A` and the photo from `gli/KioskLoc.gli` is placed at (paper width − photo width − 3, paper height − photo height), measured on that paper's own template (no inset at the bottom).
  * Second pass: each headline is printed, and directly **after** the first headline that carries a picture (if no background photo was used) its picture follows at the current text y; the text y then advances by the picture height + 5. All x offsets are relative to the paper's **text start x** (8 / 3 / 14), not to the paper edge:
    * inline pictures `FEUER`, `REIFEN`, `SALZ`, `SKELETT`, `SUPERMAN` from `gli/KioskS.gli`: paper 0 at text x + 37 with `PAPER1B` at (0, y) and `PAPER1C` below it (+ its height + 3), both at the paper's left edge; other papers at text x + 10;
    * `INTRVIEW`/`STREIK` from the room GLI at text x + 10;
    * player pictures `1`,`2`,`3`,`4`,`LIEBSTE`,`PLANES`,`GELD`,`ROUTEN` (+ 100·player) from `gli/KioskP<player+1>.gli`: the `PAPER2A` frame at text x − 3 and the picture at text x + 47, both at the current y.
* Decorations: paper 0 with a picture adds `PAPER1D` at the left at the current y when it and `PAPER1E` still fit with 10 px to spare, and `PAPER1E` at the bottom when the text has not reached it. Paper 1 places `PAPER2A`, `PAPER2D`, `PAPER2C`, `PAPER2B` in that order, each only if it fits (10 px to spare), and adds a weather icon `WET01..06` = (date%6) at (134,4).
* Reading: only one paper zooms at a time; the hot rects are checked in the order paper 1, paper 0, paper 2, and none while the mouse is over the stink bomb. The zoom scales the fully composed page; nothing is drawn while a dialog is open.

### Hover zoom ("reading")
Each paper has a zoom value from 0 to 100.
* Each frame, while no dialog is open, every zoom drops by 10. A hovered paper's zoom rises by 20, so the net gain is +10 per frame, clamped to 0..100.
* A paper with zoom > 0 is drawn stretched around a centre, with size = zoom%:
  * hotspot (419,239)-(512,403) zooms paper 0 around (183,236)
  * hotspot (494,169)-(555,325) zooms paper 1 around (349,246)
  * hotspot (426,98)-(516,247) zooms paper 2 around (140,185)
* Quirk: the top of the rect uses width/2 instead of height/2, so the papers sit lower than centred. Confirmed on the running original; OpenAT replicates it (OQ-8 settled).
* The zoom happens only while the window is active. There is no page flipping.

### Hotspots (priority order)
| Rect | Cursor | Click |
|---|---|---|
| (560,0)-(639,439) | EXIT | leave. **Note:** this rect is evaluated first but does not stop the later tests (the code uses `if`, not `else if`); the stink bomb test below overrides it |
| (158,397)-(209,439), no stink bomb owned | HOT 20 | `StartDialog(KIOSK, AIR, 1000)`: if `KioskTrust`, text 1021, whose node gives `ITEM_STINKBOMBE` and resets `KioskTrust`; else 1020 |
| the three paper rects above | - | zoom only |
| (167,179)-(339,429), awake only | HOT 10 | `StartDialog(TALKER_KIOSK, AIR)` (1000-series chat) |

* `STINK` (the stink bomb on the counter) is drawn at (158,397) while it is not owned.
* Red Bull (`ITEM_REDBULL`) used in the kiosk: dialog 1010, the item is consumed, `KioskTrust`=1.
* Bubble at (288,192), style 2.
* **WIDESCREEN:** the exit strip is the right edge of the room (x 560..639).

---

## 9. Duty free shop - `CDutyFree` (DutyFree.cpp) [H]

**Entry:** `DutyFree.cpp: CDutyFree::CDutyFree`. `ROOM_SHOP1` (80), GLI `room/dutyfree.gli`, background `DUTYFREE`. It loads `gli/Tip.gli`, whose `BLOC1` is the item note paper. Talker `TALKER_DUTYFREE` is set as the default partner (no reference increment). Ambient volume 40.

Sounds: `windchim.raw` plays once on entry (effects on), and `pay.raw` plays on every purchase.

Hours: open from 10:00. On weekday 5 and 6 it also closes after 16:00 (condition block 0). After day 4 the room calls hint `HINT_DUTYFREE`.

### Bitmaps
* `KONTRA` (violin case with the gun) at (275,197): shown when date > 0 and `ArabTrust`==0.
* `OHNEHUF` at (14,36): the empty horseshoe hook. It is shown when the player has the horseshoe or `TrinkerTrust` is set.
* Shop window: `TRANS` blended (level 4) and `OPAQUE` at (432,70).
* Language sign `SCHILDD/E/F` (indexed by language; slots after 2 reuse E) at (434,387). It is shown while date <= `DAYS_WITHOUT_LAPTOP` (7) or the laptop has been sold to someone ("sold out" or "not yet" sign [M]).

### Passers-by through the window
Clipped to (432,70)-(559,246). Every person within 150 px x of the shop anchor (shop rune + (88,0)) is drawn at y=280, x = 511 - dx\*2\*400/380 - 20, mirrored (direction (d+2)&3). Persons are drawn in reverse list order. The window glass layer is blended between persons who are in front and behind (by y<300). The x must be in 390..700.

### Saleswoman `SP_Frau` (310,47)
| Clip | smk/sound | Mood | Repeat | Succ |
|---|---|---|---|---|
| 0 | DuKau (+raw) chewing | IDLE | 2-4 | A6->0, A4->1, A3->2, E1->6, E1->7 |
| 1 | DuBlase (+raw) bubble gum | IDLE | 1 | A1->0 |
| 2 | DuNagelH | IDLE | 1 | A1->3 |
| 3 | DuNagelF (+raw) nail filing | IDLE | 5 | A1->4 |
| 4 | DuNagelG | IDLE | 1 | A6->3, A1->5, E1->5 |
| 5 | DuNagelZ | IDLE | 1 | A1->0, E1->6, E1->7 |
| 6 | DuRede | TALKING | 1 | A1->6, E1->7, E1->0 |
| 7 | DuRedeW | LISTENING | 1 | A9->7, A1->8, E1->6, E1->0 |
| 8 | DuRedeK | LISTENING | 1 (wait 1) | A1->7, E1->6, E1->0 |

### Hotspots (priority order; tips are drawn on `BLOC1` at (48,94), or (68,94) for the chocolates)
| Rect | Condition | Cursor / id | Tip (`DrawItemTipContents`) | Click |
|---|---|---|---|---|
| (493,52)-(570,224) | - | EXIT 999 | - | leave |
| (11,33)-(42,68) | no horseshoe and no `TrinkerTrust` | HOT 800 | - | dialog 800: if `DutyTrust`, text 802, which gives `ITEM_HUFEISEN`; else 800 |
| (448,327)-(542,439) or (396,395)-(542,439) | laptop quality < 4 (if the quality is 4 but the laptop is not owned, it is first reset to 3) | HOT 10 | item tip id = `LaptopQuality` (0..3) | laptop purchase (below) |
| (399,55)-(492,301) | - | HOT 111 | - | talk to the saleswoman. If `LaptopVirus` is 1, 2 or 3: dialog 2000/2010/2020. Else dialog 2 (Dlg Duty/1000 menu) |
| (280,196)-(350,287) | no gun, date > 0, `ArabTrust`==0 | HOT 400 | 400 | buy `ITEM_MG` |
| (329,247)-(395,309) | no chocolates (either kind) | HOT 801 | 801 | dialog 3000 (chocolate choice) |
| (346,306)-(445,357) | no filofax | HOT 500 | 500 | buy `ITEM_FILOFAX` |
| (545,334)-(582,439) | no mobile phone | HOT 600 | 600 | buy `ITEM_HANDY` |
| (258,289)-(361,344) | no beer | HOT 700 | 700 | buy `ITEM_BIER` |

**Direct purchase (ids 400/500/600/700):**
1. Requires a free inventory slot (6 slots).
2. Add the item and play `pay.raw`.
3. price = the integer value of Std Item/(2000+id); the ledger text is Std Item/(1000+id).
4. Debit the price with reason 9999, statistic "other". Then the network money message and the bodyguard rebate.

**Laptop (id 10):**
1. If `LaptopVirus` is 1, 2 or 3, open the virus dialogs instead (2000: confiscates the laptop and sets the virus to 2; 2020: returns a clean laptop).
2. Else, if date <= 7: dialog 3. It says "N days left" (Dlg Duty/97 with the number 8-date), or 98 after that.
3. Else, if nobody has bought the laptop today (`Sim.LaptopSoldTo`==-1):
   1. Drop any owned laptop; if there is space, give a new one. Send `ATNET_TAKETHING`.
   2. Price = Std Item/(2000+quality); ledger text Std Item/(1000+quality). Then the rebate and `pay.raw`.
   3. `LaptopQuality`++. Battery = 40, 80, 200 or 1440 for the new quality 1, 2, 3 or 4.
   4. If the quality is still < 4, dialog 4 (Dlg Duty/1010, "upgrade available").
   5. `LaptopSoldTo` = player.
4. Else: dialog 1 with the buyer. Dlg Duty/100 names the competitor; 99 is shown if it is you.

**Chocolates** (dialog 3000 -> choice 3001/3002): give `ITEM_PRALINEN_A` (3001) or `ITEM_PRALINEN` (3002). The price for both is Std Item/2801 and the ledger text is Item/1801.

**Bra item** (from the broker's flap) used in the duty free: `DutyTrust`=TRUE, the item is consumed, dialog 801.

**Other:** bubble at (396,128), style 1. On the phone: (396,198).

**Item quest chain across rooms (summary) [M]:**
1. The broker's flap gives the bra.
2. The bra goes to the duty free woman, which unlocks the horseshoe.
3. The horseshoe goes to the drinker at Rick's (`TrinkerTrust`).
4. A dart at the advertising agency gives the diskette.
5. Red Bull at the kiosk gives the stink bomb.
6. Beer for the mechanic gives the oil can.
7. Perfume in the slimed workshop gives the stink perfume.

---

## 10. Cross-room widescreen flags (summary)
* All hotspots, sprites and clip rects assume a 640x440 room. Render rooms on a fixed logical canvas.
* Rects and draws that touch x=639/640: broker dialog area, editor OK/Cancel, kiosk exit strip, PlaneProps slide bands and the plus button at x 636.
* Screen-space (not room-space) overlays: the museum hover tip (`PrimaryBm`), the advertising route tip (`WinP2.x`-based), and the speech bubble positions. Offset them by the canvas origin.
* PlaneProps' page slide shifts both backgrounds by 229 px and darkens 25-px bands up to x=640. In widescreen, hide the off-canvas parts or extend the art.
* The y<440 right-click test equals "inside the room canvas".

## 11. Open questions
* **OQ-1** Note codes 101/102 count as both "breaks often" and "high consumption" (shared values), so maintenance notes also add fuel use. Keep this for faithfulness? (Recommend: yes, behind a "classic" flag.)
* **OQ-2** Designer blender: clip 1 (`DeBlnder.smk`) is registered with id 0. Does it ever play in the original? Verify in game.
* **OQ-3** Relation 1005 (R4->M6) has Slot `ML` with RulesOut `M3`. This looks like a typo for `M3`. The mirror (1015) uses M6/M6. Is it placeable in the original?
* **OQ-4** `.plane` header: the "reliability" field duplicates consumption. Did the website use it? Irrelevant unless .plane export is offered.
* **OQ-5** Paste in PlaneProps applies only the technical targets; the comfort targets and crew factor are saved but never pasted. Bug or intent?
* **OQ-6** First-class share changes are not network-synced (no `NetUpdatePlaneProps` call). Multiplayer desync risk.
* **OQ-7** Exact semantics of `TickMuseumRefill` timing (incremented in `Sim.cpp` around line 1304; the unit is unclear) belong to the sim writer.
* **OQ-8** Kiosk zoom: the vertical centring uses the width. Replicate it?
* **OQ-9** The Workshop `MENU_PANNEN` / `MENU_PLANEREPAIRS` content and the `MechMode` effects on breakdowns belong to the menu and sim writers.
* **OQ-10** The order of `.plane` files for prev/next comes from the file-listing helper (`GetMatchingNext`). Alphabetical? Verify.
* **OQ-11** The duty-free language sign index for languages beyond D/E/F always uses `SCHILDE`.

---

## Appendix B - PlaneProps food offsets
`FoodOffsets`, relative to the seat offset. These are tray hotspot centres A and B, for index (seat\*3+tray):

| seat\tray | 0 | 1 | 2 |
|---|---|---|---|
| 0 | (116,98) / (104,999) | (116,90) / (116,286) | (116,102) / (116,330) |
| 1 | (116,108) / (116,292) | (116,108) / (116,303) | (116,109) / (116,330) |
| 2 | (135,110) / (135,330) | (135,112) / (135,325) | (135,118) / (135,999) |

A y value of 999 puts the sprite off screen, so it is hidden.

## Appendix A - Plane part relations (array order matters)
Index `#` = array index, which is stored in `.plane` files and used for twin pairing (the R entry is followed by its L twin). Offsets and notes come from the shipped `data/relation.csv` (authoritative). From/To, zAdd, Slot and RulesOut exist only in code. "desk" = attaches to the empty board. Notes column: codes other than 100, comma-separated.

| # | Id | From | To | Off2d | Off3d | Notes | zAdd | Noise | Slot | RulesOut |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | 100 | desk | B1 | -44,-75 | 320,190 | 113 | 0 | 0 | B* | B* |
| 1 | 101 | desk | B2 | -107,-120 | 320,190 | 115,113 | 0 | 0 | B* | B* |
| 2 | 102 | desk | B3 | -67,-76 | 320,190 | 114 | 0 | 0 | B* | B* |
| 3 | 103 | desk | B4 | -80,-99 | 320,190 | 114,113 | 0 | 0 | B* | B* |
| 4 | 104 | desk | B5 | -80,-94 | 320,190 | 115 | 0 | 0 | B* | B* |
| 5 | 200 | B1 | C1 | -48,-1 | 103,30 | 111,104 | 0 | 0 | C0 | C0 |
| 6 | 201 | B1 | C2 | -64,0 | 104,30 | 111,105 | 0 | 0 | C0 | C0 |
| 7 | 202 | B1 | C3 | -76,0 | 104,30 | 112,108 | 0 | 0 | C0 | C0 |
| 8 | 203 | B1 | C4 | -37,0 | 104,30 | 110,103 | 0 | 0 | C0 | C0 |
| 9 | 204 | B1 | C5 | -99,0 | 104,31 | 112,111 | 0 | 0 | C0 | C0 |
| 10 | 210 | B2 | C1 | -48,36 | 250,105 | 111,104 | 0 | 0 | C0 | C0 |
| 11 | 211 | B2 | C2 | -64,37 | 250,105 | 111,105 | 0 | 0 | C0 | C0 |
| 12 | 212 | B2 | C3 | -76,37 | 250,105 | 112,108 | 0 | 0 | C0 | C0 |
| 13 | 213 | B2 | C4 | -37,37 | 250,106 | 110,103 | 0 | 0 | C0 | C0 |
| 14 | 214 | B2 | C5 | -99,37 | 250,106 | 112,111 | 0 | 0 | C0 | C0 |
| 15 | 220 | B3 | C1 | -48,-1 | 157,46 | 111,104 | 0 | 0 | C0 | C0 |
| 16 | 221 | B3 | C2 | -64,1 | 157,45 | 111,105 | 0 | 0 | C0 | C0 |
| 17 | 222 | B3 | C3 | -76,1 | 158,46 | 112,108 | 0 | 0 | C0 | C0 |
| 18 | 223 | B3 | C4 | -37,1 | 158,46 | 110,103 | 0 | 0 | C0 | C0 |
| 19 | 224 | B3 | C5 | -99,1 | 158,46 | 112,111 | 0 | 0 | C0 | C0 |
| 20 | 230 | B4 | C1 | -48,8 | 208,60 | 111,104 | 0 | 0 | C0 | C0 |
| 21 | 231 | B4 | C2 | -64,8 | 209,60 | 111,105 | 0 | 0 | C0 | C0 |
| 22 | 232 | B4 | C3 | -76,8 | 209,60 | 112,108 | 0 | 0 | C0 | C0 |
| 23 | 233 | B4 | C4 | -37,8 | 209,60 | 110,103 | 0 | 0 | C0 | C0 |
| 24 | 234 | B4 | C5 | -99,9 | 209,60 | 112,111 | 0 | 0 | C0 | C0 |
| 25 | 240 | B5 | C1 | -48,8 | 207,60 | 111,104 | 0 | 0 | C0 | C0 |
| 26 | 241 | B5 | C2 | -64,9 | 208,60 | 111,105 | 0 | 0 | C0 | C0 |
| 27 | 242 | B5 | C3 | -76,9 | 208,60 | 112,108 | 0 | 0 | C0 | C0 |
| 28 | 243 | B5 | C4 | -37,9 | 208,61 | 110,103 | 0 | 0 | C0 | C0 |
| 29 | 244 | B5 | C5 | -99,10 | 208,61 | 112,111 | 0 | 0 | C0 | C0 |
| 30 | 300 | B1 | H1 | 89,-71 | -131,-121 | - | 0 | 0 | H0 | H0 |
| 31 | 301 | B1 | H2 | 89,-47 | -127,-91 | - | 0 | 0 | H0 | H0 |
| 32 | 302 | B1 | H3 | 89,-43 | -112,-90 | - | 0 | 0 | H0 | H0 |
| 33 | 303 | B1 | H4 | 89,-46 | -79,-85 | 105 | 0 | 0 | H0 | H0 |
| 34 | 304 | B1 | H5 | 89,-47 | -112,-87 | - | 0 | 0 | H0 | H0 |
| 35 | 305 | B1 | H6 | 89,-30 | -100,-59 | - | 0 | 0 | H0 | H0 |
| 36 | 306 | B1 | H7 | 89,-59 | -136,-113 | 108 | 0 | 0 | H0 | H0 |
| 37 | 310 | B2 | H1 | 214,-60 | -128,-120 | - | 0 | 0 | H0 | H0 |
| 38 | 311 | B2 | H2 | 214,-36 | -126,-91 | - | 0 | 0 | H0 | H0 |
| 39 | 312 | B2 | H3 | 214,-32 | -112,-90 | - | 0 | 0 | H0 | H0 |
| 40 | 313 | B2 | H4 | 214,-35 | -78,-84 | 102,105 | 0 | 0 | H0 | H0 |
| 41 | 314 | B2 | H5 | 214,-36 | -111,-87 | - | 0 | 0 | H0 | H0 |
| 42 | 315 | B2 | H6 | 214,-19 | -99,-58 | - | 0 | 0 | H0 | H0 |
| 43 | 316 | B2 | H7 | 214,-48 | -136,-113 | 108 | 0 | 0 | H0 | H0 |
| 44 | 320 | B3 | H1 | 134,-71 | -130,-121 | - | 0 | 0 | H0 | H0 |
| 45 | 321 | B3 | H2 | 134,-46 | -126,-91 | - | 0 | 0 | H0 | H0 |
| 46 | 322 | B3 | H3 | 134,-43 | -112,-90 | - | 0 | 0 | H0 | H0 |
| 47 | 323 | B3 | H4 | 134,-45 | -79,-85 | 105 | 0 | 0 | H0 | H0 |
| 48 | 324 | B3 | H5 | 134,-47 | -112,-87 | - | 0 | 0 | H0 | H0 |
| 49 | 325 | B3 | H6 | 134,-29 | -100,-59 | - | 0 | 0 | H0 | H0 |
| 50 | 326 | B3 | H7 | 134,-59 | -136,-113 | 108 | 0 | 0 | H0 | H0 |
| 51 | 330 | B4 | H1 | 178,-63 | -130,-121 | - | 0 | 0 | H0 | H0 |
| 52 | 331 | B4 | H2 | 178,-39 | -127,-91 | - | 0 | 0 | H0 | H0 |
| 53 | 332 | B4 | H3 | 178,-35 | -113,-90 | - | 0 | 0 | H0 | H0 |
| 54 | 333 | B4 | H4 | 178,-38 | -80,-85 | 105 | 0 | 0 | H0 | H0 |
| 55 | 334 | B4 | H5 | 178,-39 | -113,-87 | - | 0 | 0 | H0 | H0 |
| 56 | 335 | B4 | H6 | 178,-21 | -100,-59 | - | 0 | 0 | H0 | H0 |
| 57 | 336 | B4 | H7 | 178,-51 | -137,-113 | 108 | 0 | 0 | H0 | H0 |
| 58 | 340 | B5 | H1 | 178,-62 | -130,-121 | - | 0 | 0 | H0 | H0 |
| 59 | 341 | B5 | H2 | 178,-38 | -126,-91 | - | 0 | 0 | H0 | H0 |
| 60 | 342 | B5 | H3 | 178,-34 | -112,-90 | - | 0 | 0 | H0 | H0 |
| 61 | 343 | B5 | H4 | 178,-37 | -80,-85 | 105 | 0 | 0 | H0 | H0 |
| 62 | 344 | B5 | H5 | 178,-38 | -113,-87 | - | 0 | 0 | H0 | H0 |
| 63 | 345 | B5 | H6 | 178,-20 | -101,-59 | - | 0 | 0 | H0 | H0 |
| 64 | 346 | B5 | H7 | 178,-50 | -136,-113 | 108 | 0 | 0 | H0 | H0 |
| 65 | 400 | B1 | R4 | 4,34 | -128,59 | - | 0 | 0 | Rx | RxR1R2R3 |
| 66 | 600 | B1 | L4 | 4,-33 | 43,-96 | - | 0 | 0 | Lx | LxL1L2L3 |
| 67 | 401 | B1 | R5 | 0,35 | -66,58 | - | 0 | 0 | Rx | RxR1R2R3 |
| 68 | 601 | B1 | L5 | 0,-7 | 37,-53 | - | 0 | 0 | Lx | LxL1L2L3 |
| 69 | 411 | B2 | R1 | 46,41 | -64,77 | - | 0 | 0 | Rx | RxR1R2R3 |
| 70 | 611 | B2 | L1 | 46,-32 | 77,-112 | - | 0 | 0 | Lx | LxL1L2R5 |
| 71 | 413 | B2 | R1 | 32,74 | -43,124 | - | -400 | 0 | Rx | RxR4 |
| 72 | 613 | B2 | L1 | 32,1 | 98,-64 | - | -400 | 0 | Lx | LxL4 |
| 73 | 420 | B2 | R2 | 98,43 | -137,64 | - | 0 | 0 | Rx | RxR1R2 |
| 74 | 620 | B2 | L2 | 98,-16 | 29,-98 | - | 0 | 0 | Lx | LxL1L2 |
| 75 | 421 | B2 | R2 | 58,43 | -88,79 | - | 0 | 0 | Rx | RxR1R2R3 |
| 76 | 621 | B2 | L2 | 58,-16 | 79,-83 | - | 0 | 0 | Lx | LxL1L2R5 |
| 77 | 422 | B2 | R2 | 23,43 | -37,94 | - | 0 | 0 | Rx | RxR2R3 |
| 78 | 622 | B2 | L2 | 23,-16 | 130,-68 | - | 0 | 0 | Lx | LxL2L3 |
| 79 | 423 | B2 | R2 | 44,76 | -67,127 | - | -400 | 0 | Rx | RxR4 |
| 80 | 623 | B2 | L2 | 44,17 | 100,-36 | - | -400 | 0 | Lx | LxL4 |
| 81 | 424 | B2 | R3 | 17,42 | -84,71 | - | -400 | 0 | Rx | RxR1R2R3 |
| 82 | 624 | B2 | L3 | 17,-12 | 45,-73 | - | -400 | 0 | Lx | LxL1L2R5 |
| 83 | 430 | B2 | R4 | 110,43 | -130,59 | - | 0 | 0 | Rx | RxR1R2 |
| 84 | 630 | B2 | L4 | 110,-21 | 30,-98 | - | 0 | 0 | Lx | LxL1L2 |
| 85 | 431 | B2 | R4 | 70,43 | -84,73 | - | 0 | 0 | Rx | RxR1R2R3 |
| 86 | 631 | B2 | L4 | 70,-21 | 82,-84 | - | 0 | 0 | Lx | LxL1L2R5 |
| 87 | 432 | B2 | R4 | 35,43 | -33,87 | - | 0 | 0 | Rx | RxR2R3 |
| 88 | 632 | B2 | L4 | 35,-21 | 133,-69 | - | 0 | 0 | Lx | LxL2L3 |
| 89 | 433 | B2 | R4 | 67,76 | -60,121 | - | -400 | 0 | Rx | RxR4 |
| 90 | 633 | B2 | L4 | 67,12 | 103,-36 | - | -400 | 0 | Lx | LxL4 |
| 91 | 444 | B2 | R6 | 17,42 | -82,70 | - | -400 | 0 | Rx | RxR1R2R3 |
| 92 | 644 | B2 | L6 | 17,17 | 42,-44 | - | -400 | 0 | Lx | LxL1L2R5 |
| 93 | 450 | B3 | R1 | 7,34 | -104,63 | - | 0 | 0 | Rx | Rx |
| 94 | 650 | B3 | L1 | 12,-39 | 39,-121 | - | 0 | 0 | Lx | Lx |
| 95 | 451 | B3 | R2 | 14,34 | -124,66 | - | 0 | 0 | Rx | Rx |
| 96 | 651 | B3 | L2 | 14,-25 | 50,-97 | - | 0 | 0 | Lx | Lx |
| 97 | 452 | B3 | R4 | 26,34 | -100,57 | - | 0 | 0 | Rx | Rx |
| 98 | 652 | B3 | L4 | 26,-30 | 61,-95 | - | 0 | 0 | Lx | Lx |
| 99 | 453 | B3 | R5 | 23,35 | -39,60 | - | 0 | 0 | Rx | Rx |
| 100 | 653 | B3 | L5 | 23,-8 | 54,-42 | - | 0 | 0 | Lx | Lx |
| 101 | 460 | B4 | R2 | 39,60 | -101,100 | - | 0 | 0 | Rx | Rx |
| 102 | 660 | B4 | L2 | 39,1 | 92,-99 | - | 0 | 0 | Lx | Lx |
| 103 | 461 | B4 | R4 | 51,60 | -88,98 | - | 0 | 0 | Rx | Rx |
| 104 | 661 | B4 | L4 | 51,-4 | 103,-93 | - | 0 | 0 | Lx | Lx |
| 105 | 462 | B4 | R5 | 48,55 | -21,98 | - | 0 | 0 | Rx | Rx |
| 106 | 662 | B4 | L5 | 48,15 | 98,-44 | - | 0 | 0 | Lx | Lx |
| 107 | 470 | B5 | R1 | 27,46 | -75,80 | - | 0 | 0 | Rx | Rx |
| 108 | 670 | B5 | L1 | 27,-28 | 76,-118 | - | 0 | 0 | Lx | Lx |
| 109 | 471 | B5 | R2 | 36,48 | -99,83 | - | 0 | 0 | Rx | Rx |
| 110 | 671 | B5 | L2 | 36,-14 | 86,-93 | - | 0 | 0 | Lx | Lx |
| 111 | 472 | B5 | R3 | -1,47 | -107,71 | - | 0 | 0 | Rx | Rx |
| 112 | 672 | B5 | L3 | -1,-8 | 31,-88 | - | 0 | 0 | Lx | Lx |
| 113 | 473 | B5 | R4 | 51,47 | -82,80 | - | 0 | 0 | Rx | Rx |
| 114 | 673 | B5 | L4 | 51,-19 | 95,-89 | - | 0 | 0 | Lx | Lx |
| 115 | 474 | B5 | R5 | 48,48 | -14,79 | - | 0 | 0 | Rx | Rx |
| 116 | 674 | B5 | L5 | 48,6 | 90,-41 | - | 0 | 0 | Lx | Lx |
| 117 | 700 | R1 | M1 | -1,19 | 179,35 | - | 3000 | 2 | M1 | M1 |
| 118 | 710 | L1 | M1 | -4,61 | 93,157 | - | 0 | 2 | M4 | M4 |
| 119 | 701 | R1 | M2 | -15,13 | 191,37 | - | 3000 | 15 | M1 | M1 |
| 120 | 711 | L1 | M2 | -18,55 | 105,156 | - | 0 | 15 | M4 | M4 |
| 121 | 704 | R1 | M5 | 90,22 | -33,40 | - | 5000 | 0 | MR | MR |
| 122 | 714 | L1 | M5 | 64,-14 | 102,-27 | - | 1000 | 0 | ML | ML |
| 123 | 707 | R1 | M8 | -16,19 | 99,37 | - | 3000 | 8 | M1 | M1M2 |
| 124 | 717 | L1 | M8 | -19,56 | 31,136 | - | 0 | 8 | M4 | M4M5 |
| 125 | 720 | R1 | M1 | 19,27 | 105,52 | - | 3000 | 0 | M2 | M2 |
| 126 | 730 | L1 | M1 | 11,49 | 101,106 | - | 0 | 0 | M5 | M5 |
| 127 | 721 | R1 | M2 | 5,21 | 117,54 | - | 3000 | 0 | M2 | M2 |
| 128 | 731 | L1 | M2 | -3,43 | 113,102 | - | 0 | 0 | M5 | M5 |
| 129 | 722 | R1 | M3 | 13,13 | 106,45 | - | 3000 | 0 | M2 | M2 |
| 130 | 732 | L1 | M3 | 5,35 | 102,99 | - | 0 | 0 | M5 | M5 |
| 131 | 723 | R1 | M4 | 15,27 | 108,54 | - | 3000 | 0 | M2 | M2 |
| 132 | 733 | L1 | M4 | 7,49 | 104,108 | - | 0 | 0 | M5 | M5 |
| 133 | 725 | R1 | M6 | 12,27 | 71,28 | - | 3000 | 0 | M2 | M1M2M3 |
| 134 | 735 | L1 | M6 | 4,49 | 64,82 | - | 0 | 0 | M5 | M4M5M6 |
| 135 | 726 | R1 | M7 | 9,27 | 63,27 | 109 | 3000 | 0 | M2 | M1M2M3 |
| 136 | 736 | L1 | M7 | 1,49 | 56,81 | - | 0 | 0 | M5 | M4M5M6 |
| 137 | 727 | R1 | M8 | 6,27 | 35,53 | - | 3000 | 0 | M2 | M1M2M3 |
| 138 | 737 | L1 | M8 | -4,44 | 42,85 | - | 0 | 0 | M5 | M4M5M6 |
| 139 | 740 | R1 | M1 | 38,39 | 35,75 | - | 3000 | 0 | M3 | M3 |
| 140 | 750 | L1 | M1 | 25,38 | 109,54 | - | 0 | 0 | M6 | M6 |
| 141 | 741 | R1 | M2 | 24,33 | 47,77 | - | 3000 | 0 | M3 | M3 |
| 142 | 751 | L1 | M2 | 11,32 | 121,56 | - | 0 | 0 | M6 | M6 |
| 143 | 742 | R1 | M3 | 32,25 | 36,68 | - | 3000 | 0 | M3 | M3 |
| 144 | 752 | L1 | M3 | 19,24 | 110,47 | - | 0 | 0 | M6 | M6 |
| 145 | 743 | R1 | M4 | 34,39 | 38,77 | - | 3000 | 0 | M3 | M3 |
| 146 | 753 | L1 | M4 | 21,38 | 112,56 | - | 0 | 0 | M6 | M6 |
| 147 | 745 | R1 | M6 | 38,41 | 1,51 | - | 3000 | 0 | M3 | M2M3 |
| 148 | 755 | L1 | M6 | 27,36 | 72,30 | - | 0 | 0 | M6 | M5M6 |
| 149 | 746 | R1 | M7 | 36,41 | -7,50 | 109 | 3000 | 0 | M3 | M2M3 |
| 150 | 756 | L1 | M7 | 25,36 | 64,29 | - | 0 | 0 | M6 | M5M6 |
| 151 | 747 | R1 | M8 | 23,39 | -35,73 | - | 3000 | 0 | M3 | M2M3 |
| 152 | 757 | L1 | M8 | 10,33 | 53,47 | - | 0 | 0 | M6 | M5M6 |
| 153 | 800 | R2 | M1 | -4,11 | 176,23 | - | 3000 | 2 | M1 | M1 |
| 154 | 810 | L2 | M1 | -4,42 | 61,116 | - | 0 | 2 | M4 | M4 |
| 155 | 801 | R2 | M2 | -18,5 | 188,25 | - | 3000 | 15 | M1 | M1 |
| 156 | 811 | L2 | M2 | -18,36 | 73,125 | - | 0 | 15 | M4 | M4 |
| 157 | 807 | R2 | M8 | -32,11 | 126,25 | - | 3000 | 8 | M1 | M1M2 |
| 158 | 817 | L2 | M8 | -32,42 | 12,110 | - | 0 | 8 | M4 | M4M5 |
| 159 | 820 | R2 | M1 | 20,22 | 105,42 | - | 3000 | 0 | M2 | M2 |
| 160 | 830 | L2 | M1 | 20,26 | 57,68 | - | 0 | 0 | M5 | M5 |
| 161 | 821 | R2 | M2 | 6,16 | 117,44 | - | 3000 | 0 | M2 | M2 |
| 162 | 831 | L2 | M2 | 6,20 | 69,80 | - | 0 | 0 | M5 | M5 |
| 163 | 822 | R2 | M3 | 11,5 | 112,26 | - | 3000 | 0 | M2 | M2 |
| 164 | 832 | L2 | M3 | 11,15 | 60,74 | - | 0 | 0 | M5 | M5 |
| 165 | 823 | R2 | M4 | 13,19 | 114,41 | - | 3000 | 0 | M2 | M2 |
| 166 | 833 | L2 | M4 | 13,29 | 64,80 | - | 0 | 0 | M5 | M5 |
| 167 | 824 | R2 | M5 | 87,33 | -37,43 | - | 5000 | 0 | MR | MR |
| 168 | 834 | L2 | M5 | 86,-14 | 52,-23 | - | 0 | 0 | ML | ML |
| 169 | 825 | R2 | M6 | 5,19 | 77,15 | - | 3000 | 0 | M2 | M1M2M3 |
| 170 | 835 | L2 | M6 | 5,29 | 26,60 | - | 0 | 0 | M5 | M4M5M6 |
| 171 | 826 | R2 | M7 | 3,19 | 69,14 | 109 | 3000 | 0 | M2 | M1M2M3 |
| 172 | 836 | L2 | M7 | 3,29 | 18,59 | - | 0 | 0 | M5 | M4M5M6 |
| 173 | 827 | R2 | M8 | -11,19 | 65,41 | - | 3000 | 0 | M2 | M1M2M3 |
| 174 | 837 | L2 | M8 | -11,29 | 9,73 | - | 0 | 0 | M5 | M4M5M6 |
| 175 | 840 | R2 | M1 | 59,32 | 12,55 | 101 | 3000 | 0 | M3 | M3 |
| 176 | 850 | L2 | M1 | 59,8 | 49,13 | 101 | 0 | 0 | M6 | M6 |
| 177 | 841 | R2 | M2 | 45,26 | 24,57 | - | 3000 | 0 | M3 | M3 |
| 178 | 851 | L2 | M2 | 45,2 | 65,19 | - | 0 | 0 | M6 | M6 |
| 179 | 842 | R2 | M3 | 53,18 | 13,48 | - | 3000 | 0 | M3 | M3 |
| 180 | 852 | L2 | M3 | 53,-6 | 48,10 | - | 0 | 0 | M6 | M6 |
| 181 | 843 | R2 | M4 | 55,32 | 15,57 | 101 | 3000 | 0 | M3 | M3 |
| 182 | 853 | L2 | M4 | 55,8 | 56,19 | 101 | 0 | 0 | M6 | M6 |
| 183 | 845 | R2 | M6 | 47,32 | 4,35 | 102 | 3000 | 0 | M3 | M2M3 |
| 184 | 855 | L2 | M6 | 47,8 | 19,8 | 102 | 0 | 0 | M6 | M5M6 |
| 185 | 846 | R2 | M7 | 45,32 | -25,34 | 102,109 | 3000 | 0 | M3 | M2M3 |
| 186 | 856 | L2 | M7 | 45,8 | 11,7 | 102 | 0 | 0 | M6 | M5M6 |
| 187 | 847 | R2 | M8 | 31,32 | -34,57 | 102 | 3000 | 0 | M3 | M2M3 |
| 188 | 857 | L2 | M8 | 31,8 | 1,16 | 102 | 0 | 0 | M6 | M5M6 |
| 189 | 900 | R3 | M1 | 17,21 | 206,49 | - | 3000 | 2 | M1 | M1 |
| 190 | 910 | L3 | M1 | 17,39 | 150,112 | - | 0 | 2 | M4 | M4 |
| 191 | 901 | R3 | M2 | 3,15 | 218,51 | - | 3000 | 20 | M1 | M1 |
| 192 | 911 | L3 | M2 | 3,33 | 162,114 | - | 0 | 20 | M4 | M4 |
| 193 | 904 | R3 | M5 | 115,54 | -19,81 | - | 5000 | 0 | MR | MR |
| 194 | 914 | L3 | M5 | 115,-13 | 126,-18 | - | 0 | 0 | ML | ML |
| 195 | 907 | R3 | M8 | -11,21 | 160,51 | - | 3000 | 15 | M1 | M1M2 |
| 196 | 917 | L3 | M8 | -11,39 | 104,114 | - | 0 | 15 | M4 | M4M5 |
| 197 | 920 | R3 | M1 | 47,37 | 130,73 | - | 3000 | 0 | M2 | M2 |
| 198 | 930 | L3 | M1 | 54,19 | 145,68 | - | 0 | 0 | M5 | M5 |
| 199 | 921 | R3 | M2 | 30,28 | 142,75 | - | 3000 | 0 | M2 | M2 |
| 200 | 931 | L3 | M2 | 37,16 | 157,70 | - | 0 | 0 | M5 | M5 |
| 201 | 922 | R3 | M3 | 38,20 | 137,63 | - | 3000 | 0 | M2 | M2 |
| 202 | 932 | L3 | M3 | 45,8 | 146,61 | - | 0 | 0 | M5 | M5 |
| 203 | 923 | R3 | M4 | 40,34 | 139,72 | - | 3000 | 0 | M2 | M2 |
| 204 | 933 | L3 | M4 | 47,22 | 148,70 | - | 0 | 0 | M5 | M5 |
| 205 | 925 | R3 | M6 | 32,34 | 102,46 | - | 3000 | 0 | M2 | M1M2M3 |
| 206 | 935 | L3 | M6 | 39,22 | 107,58 | - | 0 | 0 | M5 | M4M5M6 |
| 207 | 926 | R3 | M7 | 30,34 | 94,45 | 109 | 3000 | 0 | M2 | M1M2M3 |
| 208 | 936 | L3 | M7 | 37,22 | 99,57 | - | 0 | 0 | M5 | M4M5M6 |
| 209 | 927 | R3 | M8 | 16,34 | 84,75 | - | 3000 | 0 | M2 | M1M2M3 |
| 210 | 937 | L3 | M8 | 23,22 | 99,70 | - | 0 | 0 | M5 | M4M5M6 |
| 211 | 940 | R3 | M1 | 77,49 | 36,91 | 101 | 3000 | 0 | M3 | M3 |
| 212 | 950 | L3 | M1 | 77,10 | 140,28 | 101 | 0 | 0 | M6 | M6 |
| 213 | 941 | R3 | M2 | 63,43 | 48,93 | - | 3000 | 0 | M3 | M3 |
| 214 | 951 | L3 | M2 | 63,4 | 152,30 | - | 0 | 0 | M6 | M6 |
| 215 | 942 | R3 | M3 | 71,35 | 37,84 | - | 3000 | 0 | M3 | M3 |
| 216 | 952 | L3 | M3 | 71,-4 | 137,21 | - | 0 | 0 | M6 | M6 |
| 217 | 943 | R3 | M4 | 73,49 | 39,93 | 101 | 3000 | 0 | M3 | M3 |
| 218 | 953 | L3 | M4 | 73,10 | 139,27 | 101 | 0 | 0 | M6 | M6 |
| 219 | 945 | R3 | M6 | 65,49 | 2,67 | 102 | 3000 | 0 | M3 | M2M3 |
| 220 | 955 | L3 | M6 | 65,10 | 98,10 | 102 | 0 | 0 | M6 | M5M6 |
| 221 | 946 | R3 | M7 | 63,49 | -6,66 | 102,109 | 3000 | 0 | M3 | M2M3 |
| 222 | 956 | L3 | M7 | 63,10 | 90,9 | 102 | 0 | 0 | M6 | M5M6 |
| 223 | 947 | R3 | M8 | 49,49 | -10,93 | 102 | 3000 | 0 | M3 | M2M3 |
| 224 | 957 | L3 | M8 | 49,10 | 94,30 | 102 | 0 | 0 | M6 | M5M6 |
| 225 | 1000 | R4 | M1 | -10,20 | 121,37 | - | 3000 | 2 | M3 | M3 |
| 226 | 1010 | L4 | M1 | -10,49 | 66,86 | - | 0 | 2 | M6 | M6 |
| 227 | 1001 | R4 | M2 | -24,14 | 133,39 | - | 3000 | 20 | M3 | M3 |
| 228 | 1011 | L4 | M2 | -24,43 | 78,88 | - | 0 | 20 | M6 | M6 |
| 229 | 1002 | R4 | M3 | -16,6 | 122,30 | - | 3000 | 20 | M3 | M3 |
| 230 | 1012 | L4 | M3 | -16,35 | 67,79 | - | 0 | 20 | M6 | M6 |
| 231 | 1003 | R4 | M4 | -14,20 | 124,39 | - | 3000 | 6 | M3 | M3 |
| 232 | 1013 | L4 | M4 | -14,49 | 69,88 | - | 0 | 6 | M6 | M6 |
| 233 | 1004 | R4 | M5 | 18,61 | -24,107 | - | 5000 | 0 | MR | MR |
| 234 | 1014 | L4 | M5 | 18,-13 | 137,-24 | - | 0 | 0 | ML | ML |
| 235 | 1005 | R4 | M6 | -8,20 | 87,16 | - | 3000 | 14 | ML | M3 |
| 236 | 1015 | L4 | M6 | -8,49 | 32,62 | - | 0 | 14 | M6 | M6 |
| 237 | 1006 | R4 | M7 | -14,20 | 79,15 | 109 | 3000 | 20 | M3 | M3 |
| 238 | 1016 | L4 | M7 | -14,49 | 24,61 | - | 0 | 20 | M6 | M6 |
| 239 | 1007 | R4 | M8 | -35,20 | 71,34 | - | 3000 | 15 | M3 | M3 |
| 240 | 1017 | L4 | M8 | -35,44 | 13,83 | - | 0 | 15 | M6 | M6 |
| 241 | 1020 | R4 | M1 | 1,43 | 58,77 | - | 3000 | 0 | M2 | M2 |
| 242 | 1030 | L4 | M1 | 1,21 | 106,39 | - | 0 | 0 | M5 | M5 |
| 243 | 1021 | R4 | M2 | -13,37 | 70,79 | - | 3000 | 0 | M2 | M2 |
| 244 | 1031 | L4 | M2 | -13,15 | 118,41 | - | 0 | 0 | M5 | M5 |
| 245 | 1022 | R4 | M3 | -5,29 | 59,70 | - | 3000 | 0 | M2 | M2 |
| 246 | 1032 | L4 | M3 | -5,7 | 107,32 | - | 0 | 0 | M5 | M5 |
| 247 | 1023 | R4 | M4 | -3,43 | 61,79 | - | 3000 | 0 | M2 | M2 |
| 248 | 1033 | L4 | M4 | -3,21 | 109,41 | - | 0 | 0 | M5 | M5 |
| 249 | 1025 | R4 | M6 | 0,43 | 24,56 | - | 3000 | 0 | M2 | M2 |
| 250 | 1035 | L4 | M6 | 0,21 | 72,15 | - | 0 | 0 | M5 | M5 |
| 251 | 1026 | R4 | M7 | -3,43 | 16,55 | 109 | 3000 | 0 | M2 | M2 |
| 252 | 1036 | L4 | M7 | -3,21 | 64,14 | - | 0 | 0 | M5 | M5 |
| 253 | 1027 | R4 | M8 | -24,43 | 8,74 | - | 3000 | 0 | M2 | M2 |
| 254 | 1037 | L4 | M8 | -24,16 | 53,36 | - | 0 | 0 | M5 | M5 |
| 255 | 1100 | R5 | M1 | -9,18 | 57,48 | - | 3000 | 2 | M3 | M3 |
| 256 | 1110 | L5 | M1 | -9,27 | 81,36 | - | 0 | 2 | M6 | M6 |
| 257 | 1101 | R5 | M2 | -23,12 | 67,50 | - | 3000 | 20 | M3 | M3 |
| 258 | 1111 | L5 | M2 | -23,21 | 92,39 | - | 0 | 20 | M6 | M6 |
| 259 | 1102 | R5 | M3 | -15,4 | 56,41 | - | 3000 | 20 | M3 | M3 |
| 260 | 1112 | L5 | M3 | -15,13 | 81,29 | - | 0 | 20 | M6 | M6 |
| 261 | 1103 | R5 | M4 | -13,18 | 58,52 | - | 3000 | 6 | M3 | M3 |
| 262 | 1113 | L5 | M4 | -13,27 | 85,41 | - | 0 | 6 | M6 | M6 |
| 263 | 1104 | R5 | M5 | 13,37 | -16,67 | - | 5000 | 0 | MR | MR |
| 264 | 1114 | L5 | M5 | 13,-13 | 103,-22 | - | 0 | 0 | ML | ML |
| 265 | 1105 | R5 | M6 | -7,24 | 21,31 | - | 3000 | 14 | M3 | M2M3 |
| 266 | 1115 | L5 | M6 | -7,30 | 46,22 | - | 0 | 14 | M6 | M5M6 |
| 267 | 1106 | R5 | M7 | -13,24 | 13,28 | 109 | 3000 | 20 | M3 | M2M3 |
| 268 | 1116 | L5 | M7 | -13,30 | 38,16 | - | 0 | 20 | M6 | M5M6 |
| 269 | 1107 | R5 | M8 | -34,18 | 5,45 | - | 3000 | 15 | M3 | M2M3 |
| 270 | 1117 | L5 | M8 | -34,27 | 30,33 | - | 0 | 15 | M6 | M5M6 |
| 271 | 1200 | R6 | M1 | 15,21 | 186,58 | - | 3000 | 2 | M3 | M3 |
| 272 | 1210 | L6 | M1 | 40,17 | 146,78 | - | 0 | 2 | M6 | M6 |
| 273 | 1201 | R6 | M2 | 1,15 | 198,60 | - | 3000 | 20 | M3 | M3 |
| 274 | 1211 | L6 | M2 | 26,11 | 155,80 | - | 0 | 20 | M6 | M6 |
| 275 | 1202 | R6 | M3 | 9,7 | 188,51 | - | 3000 | 20 | M3 | M3 |
| 276 | 1212 | L6 | M3 | 34,3 | 144,71 | - | 0 | 20 | M6 | M6 |
| 277 | 1203 | R6 | M4 | 11,21 | 190,60 | - | 3000 | 6 | M3 | M3 |
| 278 | 1213 | L6 | M4 | 36,17 | 146,80 | - | 0 | 6 | M6 | M6 |
| 279 | 1204 | R6 | M5 | 116,76 | -19,112 | - | 5000 | 0 | MR | MR |
| 280 | 1214 | L6 | M5 | 104,-15 | 124,-19 | - | 0 | 0 | ML | ML |
| 281 | 1205 | R6 | M6 | 3,21 | 150,37 | - | 3000 | 14 | M3 | M3 |
| 282 | 1215 | L6 | M6 | 28,17 | 107,60 | - | 0 | 14 | M6 | M6 |
| 283 | 1206 | R6 | M7 | 1,21 | 142,36 | 109 | 3000 | 20 | M3 | M3 |
| 284 | 1216 | L6 | M7 | 26,17 | 102,61 | - | 0 | 20 | M6 | M6 |
| 285 | 1207 | R6 | M8 | -13,21 | 140,60 | - | 3000 | 15 | M3 | M3 |
| 286 | 1217 | L6 | M8 | 12,17 | 97,80 | - | 0 | 15 | M6 | M6 |
| 287 | 1220 | R6 | M1 | 54,48 | 72,99 | - | 3000 | 0 | M2 | M2 |
| 288 | 1230 | L6 | M1 | 69,8 | 136,33 | - | 0 | 0 | M5 | M5 |
| 289 | 1221 | R6 | M2 | 40,42 | 84,101 | - | 3000 | 0 | M2 | M2 |
| 290 | 1231 | L6 | M2 | 55,2 | 148,35 | - | 0 | 0 | M5 | M5 |
| 291 | 1222 | R6 | M3 | 48,34 | 73,92 | - | 3000 | 0 | M2 | M2 |
| 292 | 1232 | L6 | M3 | 63,-6 | 136,26 | - | 0 | 0 | M5 | M5 |
| 293 | 1223 | R6 | M4 | 50,48 | 75,101 | - | 3000 | 0 | M2 | M2 |
| 294 | 1233 | L6 | M4 | 65,8 | 139,35 | - | 0 | 0 | M5 | M5 |
| 295 | 1225 | R6 | M6 | 42,48 | 42,75 | - | 3000 | 0 | M2 | M2 |
| 296 | 1235 | L6 | M6 | 57,8 | 100,13 | - | 0 | 0 | M5 | M5 |
| 297 | 1226 | R6 | M7 | 40,48 | 34,74 | 109 | 3000 | 0 | M2 | M2 |
| 298 | 1236 | L6 | M7 | 55,8 | 92,14 | - | 0 | 0 | M5 | M5 |
| 299 | 1227 | R6 | M8 | 26,48 | 26,101 | - | 3000 | 0 | M2 | M2 |
| 300 | 1237 | L6 | M8 | 41,8 | 90,29 | - | 0 | 0 | M5 | M5 |
| 301 | 1304 | H2 | M5 | 40,68 | -47,99 | 109 | 1550 | 0 | Mr | Mr |
| 302 | 1314 | H2 | M5 | 40,25 | 69,11 | - | 1450 | 0 | Ml | Ml |
| 303 | 1305 | H5 | M5 | 24,71 | -34,106 | 109 | 1550 | 0 | Mr | Mr |
| 304 | 1315 | H5 | M5 | 24,40 | 76,13 | - | 1450 | 0 | Ml | Ml |
| 305 | 1306 | H7 | M5 | 72,21 | -33,33 | 109 | 1550 | 0 | Mr | Mr |
| 306 | 1316 | H7 | M5 | 72,-11 | 34,-24 | - | 1450 | 0 | Ml | Ml |
