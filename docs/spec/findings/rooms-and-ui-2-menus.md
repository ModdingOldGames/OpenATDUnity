# Rooms & UI 2: Popup and list menus, and number entry

Clean-room behaviour spec for the on-screen popup/list menu system and the pocket-calculator number pad.
All coordinates are in the original 640x480 frame. "Menu-local" means relative to the menu's top-left corner (`MenuPos`).
Text references use **Std_Ger.res group + id** (the standard text resource). Group tokens: `Misc`, `Kont` (account),
`Tank`, `Filo` (filofax), `Jobs`, `Schd` (schedule), `Lett` (letter), `Akti` (shares), `Mony` (money),
`Tool` (tooltips), `Arab`, `Bank`, `Advi` (advisor), `Welt` (world). Tooltip ids in hotspots come from the `Tool` group.
GLI files live in the install's `gli\` folder (not `room\`). Bitmap sizes come from reading the GOG install's GLI headers.

> **WIDESCREEN** marks every hard-coded 640x480 assumption that OpenAT has to adapt.

Primary sources: `Stdraum.cpp` (MenuStart, MenuRepaint, MenuIsPlain, MenuPostPaint, MenuIsOpen, MenuLeftClick,
MenuRightClick, MenuStop, CalcOpen/CalcRepaint/CalcStop/CalcClick/CalcKey, MenuSetZoomStuff, InitToolTips (hotspots),
PostPaint (zoom drawing), OnChar/OnKeyDown, OnRButtonDown, OnLButtonDblClk), `Stdraum.h`, `defines.h`,
`Tips.Cpp: DrawPlanesNotepad, DrawMoneyTip`, plus the callers listed per menu.

---

## 1. Menu inventory

| Const | Value | Owner / opened from | Purpose | GLI |
|---|---|---|---|---|
| MENU_NONE | 0 | – | No menu open | – |
| MENU_BANK | 1 | Bank, banker dialog (Dialog.cpp, Bank ids 101/102) | Take out or repay a loan | Kredit.gli |
| MENU_FILOFAX | 2 | Inventory item Filofax (Par1=1) / Mobile phone or office phone or airport phone booth (Par1=2) | Room directory (walk to room) or phone book (call a room) | Adressen.gli / Telefons.gli |
| MENU_PERSONAL | 3 | Personnel office dialog (Dialog.cpp, Personal ids 200-202, 602-604) | Binder of job applicants or own staff; hire, fire, salary | Personal.gli |
| MENU_SELLPLANE | 4 | Museum dialog (ids 700/701) | Pick an own plane to sell | List.gli |
| MENU_REQUEST | 5 | Many (see §4.5) | Yes/No question or OK-only notice | Request.gli |
| MENU_BUYPLANE | 6 | Plane broker (Makler) dialog id 120 | Pick a plane type to buy | List.gli |
| MENU_LETTERS | 7 | Own office, clicking the letters on the desk (Buero.Cpp) | Read/trash letters and faxes | Letter.gli |
| MENU_ADROUTE | 9 | Advertising agency dialog id 3500 | Pick a route to advertise | ListRout.gli |
| MENU_SETRENDITE | 10 | Bank dialog id 1004 | Set the dividend | Form.gli |
| MENU_AKTIE | 11 | Bank dialog ids 2010-2013 (buy) / 2020-2023 (sell) | Buy or sell shares of an airline | Form.gli |
| MENU_EMMISSION | 12 | never opened | (issue new shares; unimplemented) | – |
| MENU_FLUEGE | 13 | Walking to an airport departures monitor (Person.cpp) | Arrivals/departures board | Fluege.gli |
| MENU_WC_F / MENU_WC_M | 14 / 15 | Entering the toilets (Person.cpp) | Invisible input-blocking pseudo menu while on the toilet | – |
| MENU_RENAMEPLANE | 16 | Planner (laptop/globe), Planer.cpp | Text entry: rename plane | Rename.gli |
| MENU_GAMEOVER | 17 | Boss/mission dialogs (Dialog.cpp) | End-of-game screen | GameOver.gli |
| MENU_BRANCHLIST | 18 | Sub-state of phone book (Filofax Par1=2) | List of cities with a branch to phone | (reuses Telefons.gli) |
| MENU_PLAYERLIST | 19 | Sub-state of phone book | Competitors to phone | (reuses Telefons.gli) |
| MENU_AUSLANDSAUFTRAG | 20 | Phone call to a foreign branch (Dialog.cpp World id 3001, Stdraum.cpp StartDialog) | Take orders from a foreign city | ListAuft.gli |
| MENU_PLANECOSTS | 21 | Workshop mechanic dialog id 2501 | Maintenance cost list | List.gli |
| MENU_PLANEREPAIRS | 22 | Workshop mechanic dialog id 2003 | Set target condition per plane | Wartung.gli |
| MENU_PLANEJOB | 23 | From MENU_PERSONAL ("assign plane") | Pick the plane a crew member works on | List.gli |
| MENU_KEROSIN | 25 | Own office, tank hotspot (Buero.Cpp) | Kerosene tank status and valve toggle | OilMenu.gli |
| MENU_BUYKEROSIN | 26 | Arab Air dialog ids 500/620 | Buy kerosene quantity | BuyKero.gli |
| MENU_FLIGHTLIST | 27 | never opened | (unimplemented) | – |
| MENU_KONTOAUSZUG | 28 | Click on the money display in the status bar | Bank statement, paged | Konto.gli |
| MENU_SABOTAGEPLANE | 29 | Saboteur dialog ids 1060/1085 | Pick victim's plane | List.gli |
| MENU_SABOTAGEFAX | 30 | Sim events (Sim.cpp) | Fax notice: sabotage hit, plane defect, strike | Fax.gli |
| MENU_PANNEN | 31 | Workshop mechanic dialog id 2004 | Breakdown history per plane | Wartung.gli |
| MENU_CLOSED | 32 | Trying to enter a closed shop (Player.cpp); protection failure (AirportView.cpp) | Opening hours notice | Request.gli |
| MENU_EXTRABLATT | 33 | Entering airport view when `Sim.ShowExtrablatt` is set (AirportView.cpp) | Spinning newspaper "special edition" | ExtraBlt.gli |
| MENU_QUITMESSAGE | 34 | Main menu, demo build (NewgamePopup.cpp) | Demo exit screen | ExitMenu.gli |
| MENU_CHAT | 35 | Phone dialog with a network player (Dialog.cpp ids 2022/2033) | One-to-one chat + money transfer | GlChat.gli |
| MENU_BRIEFING | 36 | Morning boss office (Aufsicht.cpp) | Summary of all 4 airlines | Briefing.gli |
| MENU_BROADCAST | 37 | Enter key in network game (Gameframe.cpp OnChar) | Text message to selected network players | broad.gli |
| MENU_CALLITADAY | 38 | After confirming "call it a day" in a network game | Waiting for other players | Network3.gli |
| MENU_ENTERTCPIP | 39 | Network setup (NewgamePopup.cpp) | Text entry: host IP | Rename.gli |
| MENU_ENTERPROTECT | 40 | Copy protection (call sites disabled/conditional) | Text entry: manual word | Rename.gli |
| MENU_SECURITY | 41 | Security office dialog id 2001 | Toggle security services | Wartung.gli |
| MENU_RENAMEEDITPLANE | 42 | Plane editor (Editor.cpp) | Text entry: rename designed plane | Rename.gli |
| MENU_SABOTAGEROUTE | 43 | Saboteur dialog id 1086 | Pick victim's route | List.gli |
| MENU_BUYXPLANE | 44 | Designer dialog id 4001 | Pick a self-designed plane to buy | List.gli |

Confidence: High (MenuStart switch + full caller grep, including the `.Cpp` files `Buero.Cpp`, `Tips.Cpp`).

---

## 2. Generic machinery

### 2.1 State (Stdraum.h)
Every room window (`CStdRaum`) owns exactly one menu slot:
- `CurrentMenu` (MENU_NONE = closed), `LastMenu`.
- `MenuPar1..3` (ints, meaning per menu), `MenuPar4` (string, used by BUYXPLANE for the design filename).
- Scratch ints `MenuInfo`, `MenuInfo2`, `MenuInfo3`; paging `MenuPage`, `MenuPageMax`; `MenuRemapper` (index list of a filtered subset); `MenuDataTable` (generic table: rows, columns, column titles, per-cell "flag" = draw in red, and a row→object index).
- Bitmaps: `MenuBms` (named bitmaps of the menu GLI), `MenuBms2` (secondary icons), `OnscreenBitmap` (the composed menu image), `pMenuLib1/2`.
- Zoom: `MenuStartPos`, `ZoomFromAirport`, `MinimumZoom`, `ZoomCounter`, `ZoomSpeed`, `MenuPos`.
- Dialog re-entry: `MenuDialogReEntryA` (dialog text group that was active), `MenuDialogReEntryB` (dialog line id to say after closing, −1 = none).
- Text entry buffer: `Optionen[0]` (and `Optionen[1]` for protection answer).
- Calculator: `CalculatorIsOpen`, `CalculatorPos`, `CalculatorValue`, `CalculatorKey`, `CalculatorKeyTimer`, its own bitmaps/lib and click sound.

### 2.2 Opening — `Stdraum.cpp: MenuStart`
1. Shows the wait cursor, stamps `TimeAtStart`, cancels edge-scrolling, stops any playing speech sample.
2. If a dialog is open: when a re-entry line is pending (`MenuDialogReEntryB != −1`) or the menu is BUYPLANE/BUYXPLANE the dialog partner switches to a "listening" pose and the dialog stays alive; otherwise the partner goes idle and the dialog is ended.
3. Stores type and Par1..3, resets `ZoomCounter=0`, `MinimumZoom=1.0` (= no zoom).
4. Per-type init (load GLI, pick bitmaps, fill tables) — see §4. Unknown types return early **after** CurrentMenu was set (dead types 12/27 would leave an empty "open" menu; never used).
5. Composes the image (`MenuRepaint`) and sets **`MenuPos` = centre of the 640x440 room area**: `((640,440) − size)/2`. **WIDESCREEN**: centring is against a fixed 640x440 viewport (status bar = bottom 40 px excluded). In OpenAT centre against the actual room viewport.

Zoom is optional: a caller that wants the zoom-in calls `MenuSetZoomStuff` right after `MenuStart`.

### 2.3 Zoom in/out — `MenuSetZoomStuff` + `Stdraum.cpp: PostPaint`
`MenuSetZoomStuff(startPos, minimumZoom, zoomFromAirport, zoomSpeed=25)` records the origin point and resets the counter.

Each painted frame while a menu is open:
- If `MinimumZoom == 1.0` and the counter is below 100 it snaps to 100 (menu just appears).
- A full-height (480) OnscreenBitmap (GAMEOVER) is blitted opaque at (0,0). **WIDESCREEN**.
- At counter 100: colour-keyed blit at `MenuPos` (EXTRABLATT final page: opaque).
- Otherwise a stretched colour-keyed blit: the **top-left** corner moves linearly from the start point to `MenuPos` (`lerp(start, MenuPos, z/100)`), and the scale goes linearly from `MinimumZoom` to 1.0. If `ZoomFromAirport`, the start point is in airport world pixels and the camera position is subtracted first.
- Counter += `ZoomSpeed` per frame (clamped to 100) → default 4 frames. Frame-rate dependent; OpenAT should make it time-based (~4 frames at the original ~20 fps ≈ 200 ms) but keep the look.

Close animation: after MenuStop the last OnscreenBitmap is kept; while no dialog is open and counter > 0, the counter drops by 25 per frame (immediately to 0 if MinimumZoom was 1.0) and the same lerp is drawn in reverse (shrinks back to the origin).

Typical origins (all 640-space screen coords unless "airport"):
(320,220)/(320,240) screen centre for notices (0.17); status-bar item slot `(248+60*i, 446)` (0.17); money display (100,470) (0.17); office items (Buero: letters (379,382), phone (363,321), coat (480,170), tank (357,107), all 0.1); personnel desk (291,279)/(405,261) (0.1); phone world (480,220) (0.1); airport person position (airport coords, 0 or 0.175). **WIDESCREEN**: all of these are 640x440/480 room or status-bar coordinates; map them through the same transform as the room image.

Extrablatt uses its own scaling (see §4.25).

### 2.4 Painting order (`PostPaint`)
Room image → status bar → menu (zoomed or not) → `MenuPostPaint` (hover overlays drawn straight to the screen) → calculator → advisor figure. So advisor and tooltips draw above menus.

`MenuRepaint` rebuilds OnscreenBitmap only on events (open, click, page turn, hover change requested by a room). Exceptions repainted continuously: FLUEGE (every 2nd sim tick, blinking), WC_F/WC_M (timer logic lives in their repaint), EXTRABLATT (every frame during the spin).

### 2.5 Hover, highlight and hotspots (`InitToolTips`, `PumpToolTips`, `Misc.cpp: CheckCursorHighlight`)
- Each frame the mouse is tested against menu-local rectangles (cursor − MenuPos). A hit sets cursor look (HOT, LEFT arrow, RIGHT arrow), a tooltip id (`Tool` group) and a click descriptor (area/id/par1/par2) that the next left click is dispatched on. Area codes: −100 (letters, personnel page flip), −101 (text-button/list menus), −102 (form/list menus), −103 (personnel actions, phone back/next).
- Highlight = recolour all pixels of the text colour inside the hot rectangle: black text → light green `#20FF64`, grey text → `#305030`, red text → `#FF8080`. It's applied to the screen after drawing, so it doesn't touch the menu bitmap.
- The highlight is only drawn where a menu asks for it (list rows, form cells, the personnel and kerosene actions). The
  text buttons of the request-style menus (REQUEST yes/no/OK, CLOSED OK, RENAMEPLANE, RENAMEEDITPLANE, ENTERTCPIP ...)
  only switch to the HOT cursor and set the click target: the button itself does not change on hover.
- "Selected cell" highlight (cell whose value the calculator will edit) uses the same green but is painted **into** the OnscreenBitmap, so it stays until the next repaint.
- While the calculator is open no hover/hotspot processing happens at all.

### 2.6 Modality
- While `CurrentMenu != NONE`: room hotspots, exits, status-bar highlights, inventory use, F2 options hotkey, airport walking clicks are all disabled (every room checks `!MenuIsOpen()`); left clicks go to `MenuLeftClick`, right clicks to `MenuRightClick` (room OnRButtonDown handlers) or to the generic close path in `CStdRaum::OnRButtonDown`.
- **Game time keeps running** — no menu gates `Sim.DoTimeStep` (Takeoff.cpp main loop). Some event-triggered menus (faxes, strike notice) set the local player's game speed to the slowest step (0) when they open (Sim.cpp). Confidence High.
- Competitors / network players keep acting.
- Exception: dialogs (handled elsewhere) — many menus are opened from inside a dialog and hand control back to it (see re-entry).

### 2.7 Closing — `MenuStop`
1. `LastMenu = CurrentMenu`. For REQUEST and CLOSED the next mouse-button-up is swallowed (so the click doesn't fall through to the room).
2. Special exits: QUITMESSAGE quits the application; CLOSED with the protection type terminates the program; CALLITADAY (network) while the day hasn't actually ended un-finishes the player's day (sends "day back" + chat text `Misc 7021`).
3. LETTERS, or phone book (Par1≠1) inside own office → office player animation returns to idle.
4. AUSLANDSAUFTRAG: if this city wasn't explicitly called before → re-entry line 5000; else the phone dialog is stopped.
5. Clears CurrentMenu and highlight, sends "chat stop" when closing CHAT, clears pending advisor comments (order and personnel advisors), releases bitmaps and GLIs.
6. **Dialog re-entry**: if `MenuDialogReEntryB != −1`, the day isn't over and it's before 18:00 → the dialog partner says line (group `ReEntryA`, id `ReEntryB`) and the dialog continues (medium switches to phone for AUSLANDSAUFTRAG). Otherwise, if a dialog partner is still set, the dialog is ended and the partner goes idle.
7. `MenuDialogReEntryB = −1`.

### 2.8 Right click — `MenuRightClick` (generic cancel)
Order of special cases, then default:
- WC_F/WC_M: ignored. ENTERPROTECT: ignored.
- AKTIE opened with re-entry 2030/2130 (and calculator closed): re-entry id +1 (banker says the "cancelled" variant).
- SABOTAGEPLANE/ROUTE: re-entry id +1 unless it is 1090/1091.
- BUYXPLANE: re-entry cleared (dialog ends).
- AUSLANDSAUFTRAG: same as clicking its "back" row (§4.20).
- LETTERS: triggers the close animation instead of closing directly (§4.7).
- REQUEST/CALLITADAY from the airport exit: player steps back (as for "No").
- PLAYERLIST/BRANCHLIST: back to the phone book page, paper sound.
- PLANEJOB: back to MENU_PERSONAL on the page it came from.
- REQUEST THROWNOUT/THROWNOUT2: clears the "wait for room" state (= "No").
- GAMEOVER: same as left click (but always goes to main menu or the base-game outro; the add-on outro case is missing — inconsistency).
- Default: if the calculator is open → cancel the calculator only; else MenuStop.

`CStdRaum::OnRButtonDown` (used when the room doesn't override) does the same with: phone sub-lists go back, WC/protection ignored, letters paper sound, thrown-out cleanup, then MenuStop.

### 2.9 Keyboard
- Calculator open: digits, Enter, Backspace/Delete, Esc (§3).
- Text-entry menus (RENAMEPLANE, RENAMEEDITPLANE, ENTERTCPIP, ENTERPROTECT, BROADCAST, CHAT): characters allowed = space `: , ; ! ? ' - + ( ) .`, digits, A-Z, a-z and the German letters Ä Ö Ü ä ö ü ß. Max 20 characters except BROADCAST/CHAT (those are limited by the pixel width of the input line instead: trailing characters are dropped while the text plus " _" would wrap). Backspace deletes the last character. Enter confirms (behaviour per menu below). Typing repaints.
- PLANEREPAIRS: `+`/`−` while hovering a row change that plane's target by ±5 (0..100).
- REQUEST (Gameframe.cpp): Enter or `J` = Yes (button 1); `N` or Esc = No (button 2). Enter with BROADCAST and empty text closes it. Global hotkeys are suppressed while RENAMEPLANE/BROADCAST is open.
- Enter with no menu in a game with >1 human player (after 9:00, no dialog) opens BROADCAST.

### 2.10 Double click
`OnLButtonDblClk`: with calculator open → treated as calculator click; in MENU_PERSONAL a double-click on a page arrow turns the page again (so fast clicking flips two pages) including the advisor comment.

### 2.11 Sounds (volume = effects option × 100/7)
`paper.raw` (global "move paper": page turns in all paged lists and phone sub-lists), `Pap2.raw` (paper tear: Bank cancel, Aktie cancel, Rendite OK, letter trash), `Pap3.raw` (paper rustle: letters/fax open, letter page turn, right-click on letters), `calc.raw` (every calculator key), `dial1.raw`/`dial2.raw` (random, phone call placed), `busy.raw`, `noanswer.raw`, toilet sounds (§4.13), `cam.raw` (game-over flash), `printer.raw` (bank ambient slot, share purchase).

---

## 3. The calculator (number entry) — `CalcOpen/CalcRepaint/CalcClick/CalcKey/CalcStop`

Asset: `Rechner.gli` — RECHNER 101x152 (body), D0..D9 display digits (9x15, D3 is 8x15), T0..T9 keys 29x19, TASTE_OK 61x22, TASTE_C 29x19, TASTE_X 29x19, DOT 2x2. Sound `calc.raw`.

Layout (offsets from the calculator's top-left):
| Element | Offset | | Element | Offset |
|---|---|---|---|---|
| 7 | (6,65) | | 8 | (36,65) |
| 9 | (66,65) | | 4 | (6,85) |
| 5 | (36,85) | | 6 | (66,85) |
| 1 | (6,105) | | 2 | (36,105) |
| 3 | (66,105) | | 0 | (6,125) |
| OK | (36,125) 61x22 | | C (clear) | (67,46) |
| X (cancel) | (5,45) | | Rightmost display digit | (82,19) |

Display: the value is drawn right-aligned, one digit per 10 px going left from x=82, at most 8 digits; zero shows an empty display. A thousands dot is drawn below the baseline at (61,33) after the 3rd digit and (31,33) after the 6th. The calculator body is colour-keyed over the screen at an absolute screen position.

Behaviour:
- Open: position given by the menu (`MenuPos + offset`, see each menu). **The start value is always 0** (the passed value is ignored — same in both source trees). Plays nothing on open.
- Clicks are tested against each key rectangle shrunk by 4 px on every side. Any key: click sound, the key's "pressed" bitmap is overlaid at its offset for 300 ms.
  - Digit: appended if the current value < 10,000,000 (max 8 digits, 99,999,999).
  - C: value = 0. OK: confirm. X: cancel.
- Keys: `0-9` digits, Enter = OK, Backspace/Delete = C, Esc = X. Right-click = cancel (§2.8).
- Clicks outside the keys are swallowed (calculator is modal on top of the menu).
- Confirm (`CalcStop(FALSE)`) applies the value to the owning menu (min/max clamping per menu — see BANK, SETRENDITE, AKTIE, BUYKEROSIN, PLANEREPAIRS). Cancel only repaints (removes the green cell highlight).

**WIDESCREEN**: position is absolute screen = MenuPos + menu-specific offset; fine as long as MenuPos is transformed.

Confidence: High.

---

## 4. Per-menu specification

### 4.1 Paged "notepad" list (shared by SELLPLANE, BUYPLANE, BUYXPLANE, PLANECOSTS, PLANEJOB, SABOTAGEPLANE, SABOTAGEROUTE, ADROUTE)
`Tips.Cpp: DrawPlanesNotepad`, `Stdraum.cpp: InitToolTips/MenuLeftClick`.
- Background BLOCK 388x213 (List.gli or ListRout.gli), LEFT arrow 25x22, RIGHT arrow 14x14. Centred → MenuPos ≈ (126,113).
- Left half (x 0..~210) is an empty "tip page": the owning room draws details of the hovered row there (headline at (6,6), body at (6,28)).
- Right half: column 0 title at (216,12); column 1 title (column 2 for PLANECOSTS) right-aligned ending at x=386.
- Rows: 13 per page, row *k* at y = 25 + 13·k. Value column right-aligned to x=386; name left at x=216, the space left for the name is reduced in 4 px steps until the value fits; a name that doesn't fit is shortened with "...". Row in red if the table flags it (otherwise black).
- Page = index of first visible row (multiples of 13). LEFT arrow drawn at (196,191) if page>0; RIGHT arrow at (374,199) if more rows follow.
- Hotspots: row area (216,6)-(387,212), row = (y−25)/13 + page; arrows (196,192)-(212,208) and (374,199)-(390,215). Previous from first page wraps to the last page; next past the end wraps to 0 (arrows are only hot when meaningful). Paper sound on page turn.
- Highlight of hovered row: generic hover band (212, 23+13·k)-(384, 38+13·k). Note: the generic code uses the absolute row index instead of the on-page index for several menus (wrong band on page ≥2); the owning rooms (Makler, Museum, Designer, Workshop, Saboteur) draw their own correct band (216..387, 23+13·k..37+13·k), red-tinted for red rows. OpenAT: use the on-page index.
- Close: right-click (there is no close button; the "−3" close id is never assigned to a hotspot).

### 4.2 MENU_SELLPLANE (Museum)
- Opened: museum dialog lines 700/701 on site (by phone the museum says it can't). Re-entry 699.
- Table: player's planes. Hover → Museum draws plane detail tip on the left page.
- Click an own plane row: menu closes, museum dialog continues at line 100 (+2 if the plane is sponsored) when the plane can be sold, else line 101 (+2), with the plane as parameter. Selling itself happens in the dialog.
- Right-click → back to dialog at 699.

### 4.3 MENU_BUYPLANE (Plane broker)
- Opened: broker dialog line 120; re-entry −1. The broker stays in listening pose.
- Table: all plane types. Hover → broker draws plane-type tip (Makler.cpp PostPaint).
- Click row → close, broker dialog line 130..133 (random) with the type index. (Click row calculation uses (y−21)/13 whereas hover uses (y−25)/13 — 4 px mismatch; use hover's.)
- Right-click → closes, dialog ends.

### 4.4 MENU_BUYXPLANE (Designer)
- Opened: designer dialog line 4001; re-entry 6000.
- Table: self-designed planes (files); column 2 holds the filename. Hover → designer draws design tip.
- Click row → remembers filename (`MenuPar4`) and row, closes, designer dialog line 6000.
- Right-click → re-entry cleared → dialog ends.

### 4.5 MENU_REQUEST (Request.gli REQUEST 290x200; centred → MenuPos (175,120))
Par1 = sub-type, Par2/Par3 = object. On open bgWarp (auto-travel) is cancelled; THROWNOUT types copy "thrown out of room" into "wait for room".

Layout:
- Message text (`Misc 3010 + subtype`, formatted with parameters below) in big grey font, rect (10,10)-(280,190), word-wrapped.
- **Plain** (OK only): label `Misc 3002` centred in (16,160)-(274,190); hotspot (100,160)-(190,200) → button 1.
- **Yes/No**: `Misc 3000` left at (16,160), `Misc 3001` right-aligned to x=274; hotspots Yes (0,160)-(100,200) → 1, No (190,160)-(290,200) → 2.
- Plain set (`MenuIsPlain`): NORENTROUTE1-4, FORBIDDEN, DESTROYED, ITEMS, NOGLUE, NOSTENCH, NET_NUM, NET_LOADONE, NET_LOADTHIS, NET_VERSION, BETATEST, BETATEST2, NO_LM..NO_WERBUNG.
- Keyboard: Enter/J = 1, N/Esc = 2 (§2.9).

| Sub | Id | Opened by | Text params | Yes (1) | No (2) |
|---|---|---|---|---|---|
| CALLITADAY | 1 | Office coat (Par2=0, zoom (480,170)); walking onto the airport exit (Par2=1, zoom from player) | – | If from airport: step player back one tile. Autosave to slot 11 (name `Misc 5000`) if enabled and not network. Mark player done, day state 2. Network: open MENU_CALLITADAY, stop walking, broadcast "day finished" + chat `Misc 7020`. (Single player: menu is not explicitly closed here — evening sequence takes over.) | Step back if from airport; close |
| THROWNOUT / THROWNOUT2 | 2 / 11 | Someone else is in the room / open office (Person.cpp) | – | Close, keep waiting (auto-enter when free) | Clear waiting; close |
| KILLCITY | 3 | Planner: cancel branch | – | Planner blocks showing the city reset to index page; branch rank = 0; close | Close |
| KILLROUTE | 4 | Planner / RouteBox | from city, to city, rent/30, rent | Planner blocks reset; other renters with higher rank move up one; planner post-it cleared; both directions rank 0 and "days with low usage" = 99; RouteBox table refresh; network sync | Close |
| WRONGROOM / WRONGROOM2 | 5 / 10 | Entered another player's office (Person.cpp); 2 = owner is out | – | Walk to the remembered room; close | Close |
| KILLITEM | 6 | Right-click an inventory slot (zoom from slot) | item name (`Tool 2000+item`) | Discard item (leave laptop room if it was the laptop; laptop security flag cleared) | Close |
| KILLAUFTRAG | 7 | Planner: return order with penalty | from, to, penalty | Pay penalty (money history reason 2065, "FROM-TO" codes), remove order, refresh blocks/post-it | Close |
| KILLAUFTRAG0 | 8 | Planner: return order without penalty | from, to, penalty | Remove order, refresh | Close |
| KILLPLAN | 9 | Planner: clear flight plan | plane name | Remove all plan entries starting after now+1 h; recompute, network sync, refresh order usage | Close |
| RENTROUTE | 12 | RouteBox | from, to, rent/30, rent | Rent route; rank = 1 + highest rank of other renters; both directions; network sync | Close |
| NORENTROUTE1..4 | 13-16 | RouteBox (3 rivals / not adjacent / still in use / just lost) | – | plain OK | – |
| FORBIDDEN | 17 | Room refuses entry (stayed too long before) | – | plain | – |
| DESTROYED | 18 | Own office destroyed (Player.cpp) | – | plain | – |
| ITEMS | 19 | No free inventory slot (zoom from mouse) | – | plain | – |
| NO_LM..NO_WERBUNG | 20-26 | Demo build room locks | – | plain | – |
| KILLFAUFTRAG / 0 | 27 / 28 | Planner: freight order with/without penalty | from, to, penalty | Penalty (reason 2060) for 27; freight order marked invalid (not removed) | Close |
| NOGLUE / NOSTENCH | 30 / 31 | Item used in wrong place (Stdraum.cpp) | – | plain | – |
| NET_NUM, NET_LOADONE, NET_LOADTHIS, NET_VERSION | 32-35 | Network load/version errors | – | plain | – |
| BETATEST / 2 | 36/37 | Beta licence checks | – | plain | – |
| KILLPLANE | 38 | Editor delete (Par2=77 delete current design, else clear plane) | – | Do it | Close |

Every confirmed or cancelled click closes the menu (except CALLITADAY Yes, see table). Confidence High; texts Medium (ids are formula-based: `Misc 3010+sub`).

### 4.6 MENU_CLOSED (Request.gli REQUEST)
- Par1 = `MENU_CLOSED_*` (0 duty free, 1 Arab Air, 2 last minute, 3 museum, 4 travel agent, 5 broker, 6 advertising, 7 protection, 8 security power cut). Opened by `Player.cpp` when entering a closed shop (opening-time checks there), zoom from (320,220) with min 0 (grows from a point).
- Text `Misc 3200+Par1` is split into lines at the `µ` (0xB5) separator; each line at y = 20 + 21·i, x 20..270, big grey font; a line containing a `:` not at its end is split into a left-aligned label and right-aligned value (opening-hours table).
- OK label `Misc 3002` centred at y=160; hotspot (100,160)-(190,200). Right-click closes too. Protection type exits the program. OpenAT: drop type 7.

### 4.7 MENU_LETTERS (Letter.gli LETTER 300x390; MenuPos (170,25))
- Opened only in own office: clicking the letters on the desk plays the player's "reading" animation; the animation end opens the menu (zoom from (379,382), 0.1). Paper sound.
- Letters sorted newest first; page = letter index.
- Letter page (regular letter): background LETTER; RETURN arrow 52x58 at (0,332) if not first, NEXT 55x55 at (245,335) if not last; `Misc 3120` at (19,55); player name (57,55), airline (57,67); subject (19,110)-(284,151); body (19,151)-(284,322); closing `Misc 3121` at (19,292); sender (19,322).
- Fax page: background FAX 300x390; subject text (30,70)-(250,290); optional picture (by picture id: SALZ, SKELETT, REIFEN, FEUER, SUPERMAN, KAPUTT, KAPUTT, STREIK) at (30,180); RETURN_F 45x59 at (16,331), NEXT_F 47x59 at (236,328).
- Hotspots: prev (6,332)-(56,389) tooltip `Tool 4000`; next (244,335)-(298,389) `Tool 4002`; OK (131,358)-(150,376) `Tool 4004`; trash (162,359)-(182,376) `Tool 4001`. Hovering OK/trash draws the OK (21x19) / LOECHEN (21x19) button at (131,358)/(162,358).
- Click: prev/next → paper sound, flip. Trash → tear sound, letter deleted, list re-sorted; if none remain the close animation starts, else page clamped. OK or right-click → player's "put letter away" animation; its end closes the menu (so close is delayed by the animation).

### 4.8 MENU_SABOTAGEFAX (Fax.gli FAX 268x388)
- Event-driven (Sim.cpp) when no dialog/menu is open, fax option enabled, not after call-it-a-day; zoom (320,220) 0.17; local game speed set to slowest; a copy is filed as a fax letter; secretary comment `Advi 2308`.
- Par1: 1-5 = sabotage act on own plane (pictures SALZ, SKELETT, REIFEN, FEUER, SUPERMAN; text `Lett 500+Par1` with plane name; Par2 = victim player, Par3 = plane); 6 = plane defect (`Lett 506` with plane name and Par3 = repair hours; picture KAPUTT); 7 = defect variant (adds home city and current city; KAPUTT); 8 = strike (`Lett 508`, Par2 = hours; STREIK).
- Text (30,70)-(250,190) small black; picture at (30,180). Any click closes.

### 4.9 MENU_FILOFAX — room directory (Par1=1) and phone book (Par1=2), plus sub-lists
Assets: Adressen.gli BLOCK 204x239 (MenuPos (218,100)) for Par1=1; Telefons.gli BLOCK 204x210 (MenuPos (218,115)) for Par1=2; both have LEFT 25x22, RIGHT 14x14.
Opened by: inventory Filofax item (Par1=1, zoom from slot); inventory mobile (Par1=2, sets "using mobile", zoom from slot; inside own office it plays the phone animation first, then opens with zoom (363,321)); airport phone booths (Par1=2, zoom from player); after a phone call ends the phone book reopens on the branch list. In own office the player's phone pose is set while the book is open.

**Room directory (Par1=1)**: 18 fixed entries in order: travel agent, Arab Air, saboteur, bank, own office, duty free, boss office, freight, broker, kiosk, last minute, museum, space agency, own personnel office, Rick's bar, route box, advertising, workshop. Text `Filo 1000+i` at (34, 7+13·i), clip x≤204, y≤256. Only rooms the player has visited are listed (others blank); rooms already in the player's walk queue are grey and not clickable. Hotspot rows (32,4)-(204,238), row = (y−4)/13, hover band (32,4+13r)-(204,19+13r). Click: close, leave current room(s), walk running to that room (own office/personnel resolved to the player's own).

**Phone book (Par1=2)**: 14 slots: Arab Air, bank (banker 2), bank (banker 1), boss, broker, museum, space agency, personnel (A), personnel (B), advertising, workshop, *(empty)*, competitors list, branches list. Text `Filo 2000+i` at (34, 7+13·i), clip y≤210. Same visited/queued rules. Click on a room slot (everything except the two list slots closes the menu):
- Busy/closed → `busy.raw` and nothing else. Busy when: the room is occupied (except personnel), the talker is busy, or outside opening hours (Arab: before opening or day 6; museum: before opening or days 5/6; broker: after closing; advertising: before opening or days 5/6), or the player's phone is down.
- Else creates the phone dialog window for that room and starts the call (Arab line 0, workshop/advertising/space agency line 1, personnel A/B variants with different image offsets), plays dial1/dial2, logs a call, charges 1 money unit (stat "other expenses", network sync).
- Clicking the Filofax item in the inventory while the phone book is open switches to the directory and vice versa.

**Competitors (MENU_PLAYERLIST)**: entered by clicking slot 12; same bitmap. Heading `Misc 1130` at row 0; rows 1-3 the three other players' names at x=40 (grey if out); heading `Misc 1131` at row 5; rows 6-8 names again for mobile calls (grey if out or without a mobile). LEFT arrow drawn at (12,187), hotspot (12,187)-(36,208) = back to phone book. Click an office row → close; network human: dial sound + "phone dial" net message; AI: phone down → busy; not in office (office call) or stuck → `noanswer.raw`; already talking → busy; bored of player → no answer; else dial, call cost, competitor dialog over phone, competitor shown telephoning. Mobile rows require the target to own a mobile.

**Branches (MENU_BRANCHLIST)**: entered via slot 13 (or returning from a foreign call). Lists cities (except home) where the player or a cooperating partner has a branch; 15 per page; row at y = 7+13·line, x 34; partner branches suffixed " (ABBR)". LEFT (12,187) = previous page, or back to the phone book from page 0; RIGHT 14x14 at (190,196) if more pages, hotspot (190,196)-(203,209). Click → close, long-distance call, dial sound, call cost, world-call dialog with (owning player, city).

Right-click in a sub-list → back to phone book. Confidence High; open question on how "visited" is evaluated for the two list slots.

### 4.10 MENU_PERSONAL (Personal.gli: ORDNER1 261x330 binder, ORDNER2 35x330 left page-stack strip, ORDNER3 33x36 "next" corner, KREUZLE 38x43 hire stamp)
- MenuPos (189,55). Opened by the personnel manager dialog: Par2=1 job market (lines 200-202, zoom (291,279) 0.1), Par2=2 own staff (lines 602-604, zoom (405,261) 0.1; refused by phone). Par1 = worker type (advisor types / pilot / flight attendant); any advisor type means "all advisors".
- Content = workers of that type who are jobless (Par2=1) or employed by the player (Par2=2). `MenuInfo` = average salary.
- Page 0 (cover): `Jobs 1000+type` centred (40..250, y38) in ink font; count line `Jobs 5001` (market) / `5000` (staff) at y70; average salary `Jobs 5010` at y88 if any.
- Page *n* (worker n): ORDNER2 strip over the left edge; name (40,10) ink font; job title `Jobs 2000+type+1000·gender` y80; salary `Jobs 5050` y93; talent `Jobs 5020+talent/10` y106; age `Jobs 1105` y119; employed: mood `Jobs 5055` (0..100 scale) y132; employed crew: plane `Jobs 1100` (none) / `1101` (plane name) y155 and "assign plane" `Jobs 1102` y181. Jobless: personal comment y184, "hire" `Jobs 5060` y258. Employed in market mode (just hired): `Jobs 5064` y258 + KREUZLE stamp at (216,235). Employed in staff mode: fire `5061` y258, raise `5062` y271, cut `5063` y284. Page counter `Jobs 1020` (n/max) y306. ORDNER3 corner at (225,294) when more pages follow.
- Hotspots: previous (0,5)-(35,330) when page>0; next (230,302)-(260,330); hire (40,254)-(250,268); fire (40,256)-(200,268); raise (40,269)-(200,281); cut (40,282)-(200,295) (staff mode only); assign plane (40,178)-(260,200) for crew.
- Actions: hire → becomes employee, no plane, crew mapping and walk speed updated; fire → jobless; raise/cut → salary change; assign plane → MENU_PLANEJOB (remembers page and worker). Network sync after any action. Page turn: paper sound; holding the button on an arrow auto-repeats (first repeat after 900 ms, then every 300 ms).
- Personnel advisor (market mode, if hired): on each page flip comments on the shown candidate (`Advi 1000-1008`, `1270-1272`, `1004+gender` for a candidate poached by a rival); rating depends on talent and hidden quality.

### 4.11 MENU_PLANEJOB
- Notepad (§4.1) of own planes; red rows per table flag. Click a row → assigns the worker to that plane, recomputes pilot counts, reopens MENU_PERSONAL with the same type/mode at the remembered page. Right-click → back to PERSONAL unchanged.

### 4.12 MENU_KEROSIN (OilMenu.gli OILMENU 256x199)
- Own office tank hotspot, zoom (357,107) 0.1.
- Text: `Tank 1000` at (18,8); `Tank 1030` + market price (x160, y28); `Tank 1031` + player's average tank price (y41); `Tank 1001` + capacity in barrels (y58); `Tank 1002` + content in barrels (n%) (y71); state `Tank 1010+valveOpen` (or `1012` when empty) at y100; `Tank 1020` at y143. Unit word "Barrel"/"Barris" (Portuguese) is literal.
- Hotspots: valve line (18,98)-(300,118) only when not empty → toggles valve, network sync; close line (18,141)-(300,173) → close.

### 4.13 MENU_BUYKEROSIN (BuyKero.gli FORM 330x160, OK 48x32, CANCEL 23x23; MenuPos (155,140))
- Arab Air dialog 500/620, re-entry 650. Only drawn while in the Arab Air room.
- Par1 = quality (0 = double price, 1 normal, 2 half price), Par2 = barrels (start 0). Cost = price × barrels − discount (10% from 50,000 barrels, 5% from 10,000). Barrels automatically reduced so cost never exceeds money − debt limit.
- Text: `Tank 2000` (56,10); `2010` price/barrel (value x200,y37); `2011` capacity (y50); `2020` current content (barrels right to 200, % right to 236, y76); `2021` purchase (barrels / % / cost right to 327, y89); `2022` after purchase (y102); `2030` discount (value x120, y120).
- Hotspots: barrels cell (126,87)-(198,100), percent (198,87)-(234,100), money (234,87)-(327,100) → green cell highlight + calculator at MenuPos+(318,67). Confirm: barrels direct / percent (≤100) of capacity / money ÷ price; clamped to free tank space and ≥0.
- OK (237,125)-(304,156) `Tool 4510`; Cancel (153,133)-(208,159) `Tool 4511`. Hover draws OK image at (248,125); CANCEL image is drawn at (168,137) except while hovering cancel.
- OK: cost>0 but unaffordable → Arab says `Arab 6000`; else adds barrels, adjusts "bad kerosene" share (cheap adds, premium reduces), new weighted average price, charges (reason 2090), bodyguard rebate, Arab says `Arab 700`; menu closes (then re-entry 650 runs — see open questions).

### 4.14 MENU_SETRENDITE (Form.gli FORM 330x160)
- Bank dialog 1004 on site (by phone refused `Bank 1200`). Bank fonts (red title, black body).
- `Akti 2000` title (56,10); `2001` dividend % (value right to 320, y37, clickable (180,35)-(325,49) `Tool 4320`); `2002` total payout = % × shares (y50); `2003` per day = /365 (y63).
- Calculator at MenuPos+(322,15); confirmed value clamped 0..25.
- OK `Tool 4300` → stores dividend, network sync, tear sound, close; Cancel `Tool 4301` closes. Same OK/CANCEL overlay rules as 4.13.

### 4.15 MENU_AKTIE (Form.gli FORM 330x160)
- Bank dialog: buy lines 2010-2013 (Par1 = airline 0-3, Par2=0), sell lines 2020-2023 (Par2=1); refused if that airline is out (`Bank 2016`). Re-entry 2030 (buy) / 2130 (sell); cancel bumps it by 1.
- Initial amount: buy = half of free shares (total minus holdings of all active players); sell = half of own holding.
- Layout: title `Akti 3000+Par2` (60,10, red); column heads at y27: `1001` x90, `3010` x140, `3011` x200, `3012` x240; market row y40 (`1000` at x30): price, free count, % of total, value; own holding y60 (`3020`); trade row y73 (`3021+Par2`): amount, %, value (small black = editable); new holding y86 (`3023`); new balance y111 (`3030`, value at x220).
- Editable cells: count (140,71)-(198,85) `Tool 4220`, percent (200,71)-(238,85) `4221`, money (240,71)-(316,85) `4222` → green cell + calculator at MenuPos+(318,51). Confirm: count direct / percent (≤100) of total shares / money ÷ price; clamped to 0..free (buy) or 0..own holding (sell).
- OK `Tool 4200` (+10 for sell): **buy** — if unaffordable → banker `Bank 6000`; else bought in chunks of ≤1000, each chunk raises the price by factor total/(total−chunk); cost +10% fee +100 flat (reason 3150); share value book updated; printer sound; network advisor notice to a human owner. **Sell** — chunks lower the price by (total−chunk)/total; proceeds −10% +100 (reason 3151). Cancel `Tool 4201` (+10): tear sound, close.

### 4.16 MENU_BANK (Kredit.gli KREDIT 318x200, OK 48x32, CANCEL 31x25; MenuPos (161,120))
- Bank dialog: 101 take loan (Par2=1; Par1 = credit limit rounded down to 1000; none → `Bank 120`), 102 repay (Par2=2; Par1 = half the current loan; none → `Bank 140`); by phone refused `Bank 150`. Re-entry −1.
- Layout (bank fonts): player name (58,17) red; current loan block rows y35/45/55/65 labels `Kont 1000-1003`, values right to x270: loan, interest %, monthly interest (rate×loan/1200), yearly (rate×loan/100); amount row y83 `Kont 1004` (take) / `1005` (repay), value right to 270; new loan block y103..133 labels `Kont 1006, 1001-1003` with loan ± amount.
- Amount row (56,81)-(318,96) `Tool 4102` (+1 when taking) → calculator at MenuPos+(276,60); confirm clamps to 0..credit limit (take) or 0..loan (repay).
- OK (204,156)-(271,187) `Tool 4100` (+10 take); Cancel (111,159)-(198,190) `Tool 4101` (+10). Hover overlays: OK image at (215,156), CANCEL image at (136,169).
- Every click first clamps and repaints. OK take: if ≥1000 → loan and money increase (reason 2003), close, banker line 101; below 1000 → close, banker line 100. OK repay: loan and money decrease (reason 2004), close, banker line 102. Cancel: tear sound, close.

### 4.17 MENU_KONTOAUSZUG (Konto.gli TIPMONEY 400x214, LEFT 27x19, RIGHT 26x18; MenuPos (120,113))
- Click on money in the status bar (80..242, 460..479); zoom (100,470) 0.17. **WIDESCREEN**: status-bar coordinate.
- 10 pages of 10 history lines; opens on page 9 (newest). Content (`DrawMoneyTip`, bank fonts): title `Mony 1000` with weekday (`Schd 3010+..`) and date at (28,11); "page/pages" right-aligned to 390; `Mony 1003` loan (y34), `1004` opening balance (y54), entries at y = 72+11·i (description left x36, amount right to 378), `1005` closing balance (y193). Empty and separator lines are skipped.
- RIGHT arrow at (374,196) when page<9 → hotspot (374,186)-(400,214) newer; LEFT at (0,196) when an older non-empty page exists → hotspot (0,189)-(30,214). Paper sound. Any other left click closes.

### 4.18 MENU_FLUEGE (Fluege.gli MONITOR 360x300, BLINK1/2 14x6, DOT1-4 10x10; MenuPos (140,70))
- Walking onto an airport departures monitor (ROOM_MONITOR7..9); zoom from the monitor in airport coords (0.175). Any click closes.
- Home city name centred at y32 (x 63..310), white small font. Arrivals header y49, departures header y154: time `Misc 1500` right-aligned to x89, route `Misc 1502` (arrivals) / `1501` (departures) at x123, gate `Misc 1503` at x280.
- Up to 4 rows each (13 px): arrivals from y62, departures from y167. Scans hour by hour from the previous hour forward (max 48 h) over all active airlines' scheduled flights to/from home. Row: hh:00 at x57, airline colour dot DOT(1+airline) at x90, airline abbreviation x101, "From-To" at x123 (falls back to city code for destination, then both codes, if too wide), gate at x280 ("-" none, "ext" external, else number). Blinking marker (BLINK1/BLINK2 alternating every 4 ticks) at x40 for flights within ±1 h whose plane is at/near the airport. Repaints every 2nd tick.

### 4.19 MENU_WC_F / MENU_WC_M (pseudo-menus)
- Opened on entering the ladies'/gents' toilet (Par1 = sim tick at start). No visible graphic (an unpainted bitmap the size of the city marker; effectively invisible). Blocks input; right-click ignored.
- After 20 ticks: plays toilet sound (gents: one sample, separate one for player 0; ladies: random variant among three for players 1-3, one for player 0) and the player stops running; in the ladies' a perfume item may be picked up once per game.
- After 80 ticks (40 for player 0 in the gents) the player is sent back out to the corridor and the menu closes.

### 4.20 MENU_AUSLANDSAUFTRAG (ListAuft.gli BLOCK 388x240 + plane-size icons PL_B/V/R/J/SW 19x19; MenuPos (126,100))
- World branch phone call: dialog line 3001, or directly when the city was already called before. Zoom (480,220) 0.1. Par1 = city index. Orders for that city are refreshed on open (network-synchronised).
- Right page: city name (214,12); 6 passenger orders at rows 0-5 (y = 38+13·r), 6 freight orders at rows 7-12, "back" `Misc 1100` at row 14 (y220); x 214..384; route text "City - City" shortened like the departures board.
- Hover row r=(y−33)/13: band (212,33+13r)-(384,48+13r); hovering an order draws its full order/freight tip (map, plane-size icon) on the left page and lets the order advisor comment.
- Click order → taken: added to player's orders/freight, statistics, planner blocks refreshed if in laptop/globe, removed from the list (network "took" message).
- Click back row or right-click: first-time call → close, dialog continues at 5000; otherwise → phone book branch list.
- Note: hover test applies the x-range only to passenger rows (operator precedence) — freight and back rows are hot across the full width.

### 4.21 MENU_PLANECOSTS (List.gli)
- Workshop line 2501, re-entry 2502. Notepad with column-2 title (maintenance costs), values from column 1. Hover → workshop draws the plane tip with costs. Paging only; right-click returns to the dialog.

### 4.22 MENU_PLANEREPAIRS (Wartung.gli WARTUNG 368x249, LEFT/RIGHT 14x14; MenuPos (136,95))
- Workshop line 2003, re-entry 2502. Table of own planes. `MenuInfo3` ("set all" value) starts at 100.
- Header y40: plane column title x63, `Schd 1002` condition x175, `1005` target x220, `1006` cost x265. 13 rows from y53, 12 px pitch: name x63, condition x185, target % x220, cost right-aligned x285 (cost = max(0, target − (worst condition + 20)) × plane price / 110).
- Footer y213: `Schd 500` (close) at x63; `Schd 1008` "set all to N%" at x216. LEFT at (42,218), RIGHT at (338,218).
- Hotspots: rows (55,51)-(348,212) (band (55,51+12k)-(346,64+12k)); arrows (32,208)-(62,238) / (328,208)-(358,238); close (55,211)-(120,226); set-all (214,211)-(346,226).
- Click row → target cell highlighted green, calculator at MenuPos+(350, 30+12k); confirm clamps 0..100, sets that plane's target, network sync, and becomes the new "set all" value. Set-all → applies to all planes. `+`/`−` keys: ±5 on hovered row. Paging wraps.
- Bug: the green cell highlight uses the absolute row (wrong on page 2). Fix in OpenAT.

### 4.23 MENU_PANNEN (Wartung.gli)
- Workshop line 2004, re-entry 2502. List mode: plane title (63,40), `Schd 1009` at x265; rows like 4.22 with name and breakdown count (x265). Click row → detail mode: plane name (63,40); "count + `Schd 1009`" (63,55); entries from y73 (12 px): time "h:mm" x63, date "d.m." x108, reason `Schd 2360+code` x153; footer `Schd 1010` (back). Arrows: list mode page, detail mode previous/next plane. Close hotspot (55,211)-(120,226): detail → list; list → close.

### 4.24 MENU_SECURITY (Wartung.gli)
- Security office line 2001. Header `Misc 8020` (63,40). 9 service rows at y = 55+12·i: `Misc 8000+i`, prefixed "1 x " (i<6) or "N x " (N = planes, i≥6) when active; row 1 grey without laptop; row 8 has three levels (`8008` / `8010` / `8011`). Cost labels `Misc 8021` (155,185), `8022` (155,197), `8023` (155,213) with values right-aligned at x200 (three cost variants). Footer `Schd 500` (63,213) = close.
- Click row (band (63,53+12i)-(346,66+12i)) → toggles that service (row 8 cycles off → level 1 → level 2 → level 3 → off); network sync. Close (55,211)-(120,226).

### 4.25 MENU_EXTRABLATT (ExtraBlt.gli ROT1-8 175x175, BACKGND 202x294, 16 headline pictures 141x116 named by airline pair SUN/FAL/PHO/HON)
- Shown when the airport view opens and an airline was knocked out/taken over; Par1 = pair index (first airline × 4 + second). Zoom origin (320,240), speed 10.
- Spin phase: the 8 rotation frames cycle every 50 ms, scaled around the screen centre (320,220) from 0 to 2× over 20 frames. Then the final page: BACKGND at normal size, centred (MenuPos recomputed), pair picture at (48,59), headline `Misc 2100` with both airline names in (6,37)-(192,57).
- Click → close and the player walks to his own office. **WIDESCREEN**: fixed centre (320,220).

### 4.26 MENU_GAMEOVER (GameOver.gli GAMEOVER 259x41, OKAY 300x100)
- Mission end (boss dialogs). Par1 = 1 lost / demo end, 0 won. Copies the current 640x480 screen, converts it to greyscale, and shows it full-screen; first 270 ms a white flash with `cam.raw`. Overlay GAMEOVER (lost) or OKAY (won) centred at x=320, y=330.
- Click (ignored during the first 500 ms; boss room ignores 3 s) → resumes time, disconnects network, then outro (final mission won), add-on outro (add-on mission 10 won) or main menu. **WIDESCREEN**: whole-screen grab, fixed overlay position.

### 4.27 MENU_BRIEFING (Briefing.gli MENU 640x440 full room, PLAYER0-7 241x50, AUSRUF 30x32, SABOT 55x42, BRIFO01/02 & BRIAB01/02 177x34)
- Morning in the boss office when brief briefing option is on (otherwise boss dialog directly). MenuPos (0,0). **WIDESCREEN**: full-room 640x440 background.
- Four panels, origin p = (61 + 274·(i&1), 83 + 153·(i/2)): airline banner PLAYERi (PLAYER i+4 if out) at p+(0,3); airline name p+(51,5); mission label `Misc 1300+difficulty` p+(51,33) with progress value right-aligned to p.x+240 (format depends on mission: profit mission in the ": money" unit, image mission in percent, ATFS 7 as "a/b", otherwise a plain number, from 10 million on in millions). **The free game uses the first mission's label and metric (passengers)** — verified on the running original (fidelity pass); `Misc 6010` image %, `6011` staff satisfaction %, `6012` average plane condition % at p+(51,66/84/102), values right-aligned to p.x+240. All text on the card is in the big grey font (FontBigGrey). Exclamation AUSRUF at p+(8,105) when an airline has won, lost, been taken over, or a saboteur is exposed; sabotage icon SABOT on victims at p+(−6,57).
- Buttons: BRIFO01 at (136,385) (hover BRIFO02) → hotspot (136,385)-(312,418): close, start boss dialog line 1. BRIAB01 at (325,385) (hover BRIAB02), only if no exclamation → hotspot (325,385)-(500,418): resume time and leave the boss office. Right-click → like the first button.

### 4.28 MENU_CALLITADAY (Network3.gli MENU 206x120, PL0-7 34x32)
- Network: after confirming end of day. For each active human player i an icon at (16+48·i, 76): PLi when finished, PL(i+4) when still playing. No hotspots; right-click closes and un-finishes the player's day (§2.7).

### 4.29 Text-entry menus (Rename.gli MENU 290x200 / broad.gli MENU 290x200)
Common: prompt in big grey at (10,10)-(280,190); input line `text_` (underscore caret) small black at (26,141)-(268,155).
- **RENAMEPLANE** (planner, Par1 = plane): prompt `Misc 3099` with current name; `Misc 3002` left (OK), `3003` right (cancel); OK hotspot (0,160)-(100,200) only when text non-empty, cancel (190,160)-(290,200). OK/Enter sets name. Starts empty.
- **RENAMEEDITPLANE** (editor): same, starts with current design name; applies to the editor plane.
- **ENTERTCPIP**: prompt `Misc 3097`; centred OK (100,160)-(190,200) / Enter → stores host address.
- **ENTERPROTECT**: prompt `Misc 3090` with page/line/word from a table; OK/Enter compares case-insensitively; 3 failures end the day and flag protection failure. Not needed for OpenAT.
- **BROADCAST**: prompt `Misc 3098`; OK/cancel as rename; other players' portraits (PL 48x50) at x = 60+60·slot, y80: selected network humans use PLi, others PL(i+4); clicking a network human's portrait (60x60 area, tooltip "Name (Airline)") toggles it. Par1 = recipient mask, starts all. Send → chat broadcast to selected humans and shown locally.
- **CHAT** (GlChat.gli CHAT 288x198, TEXTAREA 268x133): Par1 = partner. Log area blitted at (11,11); new lines are printed at the bottom (6,119)-(279,147) after scrolling the log up 10 px; first line `Misc 3004` (partner name/airline). Labels `Misc 3005` left at (18,170) (close, hotspot (18,172)-(80,190)), `Misc 3006` right (send money, hotspot (135,172)-(274,190)). Input at (14,152)-(279,170). Enter sends the text. Send money: input must be a positive integer of < 9 digits that the player can afford → transfer (reasons 3700/3701) and log `Misc 3009`; unaffordable → `Misc 3007`; not a number → `Misc 3008`.

### 4.30 MENU_QUITMESSAGE (ExitMenu.gli 640x480)
Demo build only; any click or close quits. Not needed for OpenAT. **WIDESCREEN** full-screen.

---

## 5. Widescreen checklist
1. MenuPos centred on 640x440 (`MenuStart`, Extrablatt) → centre on real room viewport.
2. Zoom origins in 640-space room/status-bar coordinates and airport world coords; Extrablatt and notice zoom centres (320,220)/(320,240).
3. Full-screen menus: GAMEOVER (screen grab 640x480, overlay at y=330), BRIEFING (640x440), QUITMESSAGE (640x480).
4. ADROUTE hover tip drawn at absolute screen (135,119) (`MenuPostPaint`) and by the advertising room at right window edge minus tip width.
5. Inventory clicks inside FILOFAX/sub-lists use fixed slot geometry (x = 250+60·i, right limit 610, status bar bottom 40 px).
6. Calculator at absolute MenuPos+offset.
7. Status-bar money hotspot (80..242, 460..479) opens KONTOAUSZUG.

## 6. Open questions
1. BUYKEROSIN OK: the Arab's confirmation line (`Arab 700`) is issued and then MenuStop immediately issues the re-entry line 650 — which one is actually heard/shown? (Med)
2. CALLITADAY "Yes" in single player never calls MenuStop — is the menu removed by the evening transition? (Med)
3. Phone book visibility of the "competitors" and "branches" slots depends on the visited-room flags at index 254 / ROOM_WORLD — when are those set? (Low)
4. English build: is the "yes" hotkey still `J` or remapped (`Y`)? (Low)
5. WC pseudo-menu bitmap: is anything visible (unpainted marker-sized surface)? Treat as invisible. (Low)
6. KILLCITY has no network sync in this function — synced elsewhere? (Low)
7. Calculator always starts at 0 although callers pass the current value — keep (faithful) or prefill (QoL)? (High that it's 0.)
8. MENU_PLANECOSTS shows column-2 title but column-1 values — check in-game whether title/value match. (Med)
