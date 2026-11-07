# Rooms & UI — Part 1: Room framework core

Clean-room behaviour specification for the generic room layer of Airline Tycoon Deluxe (ATD):
the room base class (`CStdRaum`), the top-level game frame (`GameFrame`), the status bar, speech
bubbles, tooltips, cursors, keyboard handling, fades, pause and the advisor/notification overlays.

Scope notes
- The on-screen menu system and calculator (`MenuStart/MenuRepaint/MenuLeftClick/MenuRightClick/
  MenuStop/MenuPostPaint/Calc*`) are covered in a separate document. They are referenced here only
  where they interact with the framework (layering, input routing).
- Dialogue *content* (which text follows which answer) lives in `Dialog.cpp` and is covered in a
  separate document. Here: the generic bubble machinery and the `StartDialog/StopDialog` contract.
- Individual rooms (Bank, Makler, …) are covered elsewhere; they are cited here only as examples of
  the shared pattern.

Source citations are `File.cpp: Function`. Confidence: **H** = read directly, unambiguous;
**M** = read, but timing/visual result inferred; **L** = inferred / not verified.

Text references use resource file + group token + id, e.g. `std_ger.res "Tool" 1002`. Resource
files are in `misc\`: `std_ger.res` (StandardTexte), `dlg_ger.res` (DialogTexte), `ein_ger.res`
(unit texts, ETexte). Group tokens are 4-character strings (`"Tool"`, `"Tuto"`, `"Schd"`, `"Misc"`,
`"Advi"`, `"Play"`, `"Pass"`, `"Welt"`, …) (`Global.cpp` token table).

---

## 0. WIDESCREEN — 640x480 assumptions (summary list)

Everything in this layer is hard-wired to a 640x480 frame whose bottom 40 px are the status bar
(room area 640x440). OpenAT must re-anchor each of these:

| # | Assumption | Where | Suggested widescreen treatment |
|---|---|---|---|
| W1 | Back buffer, window and every room window are exactly 640x480 | `Gameframe.cpp: GameFrame::GameFrame`, `Stdraum.cpp: CStdRaum::CStdRaum` (window rect 0,0,640,480) | Keep a 640x480 *logical* canvas per room; position it inside the wide frame (centre or left-anchor) and add side fill / extended art |
| W2 | Room area is 640x440; status bar occupies y 440..479 | everywhere (`StatusLineSizeY=40`, many literal 440s) | Status bar should span the full physical width; room canvas stays 640x440 |
| W3 | Status bar drawn only if the room background is ≤440 px tall | `Stdraum.cpp: CStdRaum::PostPaint` | Same rule; full-screen (480) rooms hide the bar |
| W4 | Status-bar elements are at absolute x: logo 7, clock 78..101, date/money 102..241, items from 248 in 60 px steps, right icons at 640-width | `CStdRaum::PostPaint`, `CStdRaum::OnLButtonDown`, `CStdRaum::InitToolTips` | Left group anchored to left edge, right icons anchored to right edge, items either left-anchored after money or centred |
| W5 | Inventory hit-test uses "x ≥ 40 % of window width" and `(x-250)/60` | `CStdRaum::OnLButtonDown/OnRButtonDown` | Re-derive from the actual slot rectangles |
| W6 | Advisors slide up at the right edge (x = 640 - width) above y=440; the player portrait at x=0 | `CStdRaum::PostPaint`, `Misc.cpp: CMessages::Pump` | Anchor advisor to the right edge of the *room canvas* or the screen (decide; see open question Q1) |
| W7 | Phone ("Handy") pane slides from x=800 to x=378 and is clipped to 640; airport rendering is clipped at x=378 while it is shown | `CStdRaum::OnPaint(BOOL)`, `CStdRaum::PostPaint`, `RightAirportClip` | Express as "right edge minus 262 px" of the room canvas |
| W8 | Speech bubble horizontal extents are absolute (50..600, …630, 615, 620) | `CStdRaum::RepaintText`, `CStdRaum::MakeSayWindow` | Relative to the 640 room canvas |
| W9 | Number window centred at x=320, bottom at y=439 | `CStdRaum::MakeNumberWindow` | Centre of room canvas |
| W10 | Tooltip box clamped to x∈[2, 639-w], y≤452; below-cursor unless cursor y≥439 | `Gameframe.cpp: GameFrame::OnPaint` | Clamp to physical screen |
| W11 | Room tip bitmap clamped to (window width, window height-40) and then *scaled* by width/640 and height/440 (split-screen leftover) | `CStdRaum::PostPaint` | Clamp to room canvas, drop the scaling |
| W12 | Fullscreen cursor clip rect (2,2)-(638,478) | `GameFrame::GameFrame`, `GameFrame::OnActivateApp` | Clip to physical client area |
| W13 | Crossfade and pause snapshots copy exactly 480 rows x 640 px | `GameFrame::PrepareFade`, `GameFrame::OnPaint`, `SBBM.cpp: SBPRIMARYBM::Flip` | Snapshot the full physical frame |
| W14 | Pause image (`pause.gli`/`PAUSE`) is a 640x480 full-screen picture | `GameFrame::OnPaint` | Centre and letterbox, or dim the frame behind it |
| W15 | Menus zoom towards fixed points such as (320,220), (100,470), (248+i*60,446) | `CStdRaum::OnLButtonDown`, `Gameframe.cpp: GameFrame::OnKeyDown` | Convert to canvas-relative anchors |
| W16 | Network chat overlay at (10,10), width 320 | `CStdRaum::PostPaint`, `AtNet.cpp: DisplayBroadcastMessage` | Screen top-left |
| W17 | Competitor portrait at x=640-width, y=440-slide | `CStdRaum::PostPaint` | Right edge of room canvas |
| W18 | Advisor click zone (560,320)-(640,440) | `Dialog.cpp: CStdRaum::PreLButtonDown` | Follow the advisor's actual rectangle |
| W19 | Mouse position is shifted by +2,+2 before use | `GameFrame::OnMouseMove` | Keep (cursor hotspot correction), apply in canvas space |
| W20 | Room-exit hotspots are absolute rectangles in each room, many touching x=0 or x=639 | per-room `OnPaint` (table §8.3) | Room canvas coordinates; optionally extend edge exits into side fill |

---

## 1. Architecture overview

- **GameFrame** is the single top-level window (the "frame"). It owns the back buffer
  (`PrimaryBm`, 640x480, 16-bit), the software mouse cursor, the tooltip box, fades and pause.
  It receives all Windows input and **re-posts** mouse and key messages to the room window(s).
  (`Gameframe.cpp: GameFrame::RePostMessage`) **H**
- **CStdRaum** is the base class of every location window (rooms, airport view, laptop, globe,
  intro/outro, …). Each player that is a local human has at most one *location window*
  (`PLAYER::LocationWin`) and optionally one *dialog window* (`PLAYER::DialogWin`, used for the
  phone pane). **H**
- A modal top window (`TopWin`, e.g. evening screen, new-game popup) takes all input instead of the
  players' rooms when present. **H**
- Rendering is immediate-mode: every frame each room repaints its background into its private
  room bitmap (`RoomBm`), draws its animations into it, then the framework composites bubbles,
  copies the room bitmap to the back buffer, and draws status bar, advisors, menus and tips on top.
  `GameFrame::OnPaint` finally draws the tooltip text box and the cursor and flips. **M** (exact
  Windows paint order parent/child not verified; behaviourally it is "room first, frame overlay last")

### 1.1 Coordinate spaces
- Screen/window space: 0..639 x 0..479.
- Room space: identical in the shipped game (room window always starts at 0,0). The framework keeps
  `WinP1/WinP2` (window top-left / bottom-right; always (0,0)/(640,480)) as a leftover from a
  split-screen design and subtracts `WinP1` in `ConvertMousePosition`. **H**
- `ConvertMousePosition(windowPoint)` returns `windowPoint - WinP1` and a flag "inside window"
  (inclusive of the bottom/right edge). Rooms ignore clicks for which it returns false.
  (`Stdraum.cpp: CStdRaum::ConvertMousePosition`) **H**

---

## 2. CStdRaum lifecycle

### 2.1 Constructor parameters
`CStdRaum(bHandy, PlayerNum, GfxLibName, graficId)` (`Stdraum.cpp: CStdRaum::CStdRaum`) **H**

| Param | Meaning |
|---|---|
| `bHandy` | TRUE if this window is a **phone pane** (the remote partner's room rendered as a slide-in panel during a mobile/office phone call). FALSE for a normal location window. |
| `PlayerNum` | Player the window belongs to (0..3). |
| `GfxLibName` | Room GLI library file name (looked up in `room\`), e.g. `bank.gli`; empty string = no library (airport view, intro, laptop-type windows that load their own). |
| `graficId` | 64-bit bitmap id inside the library used as the room background (`PicBitmap`); 0 = no background. E.g. Bank uses `bank.gli` + `GFX_BANK`. |

### 2.2 What the constructor does (in order)
1. Shows the wait (sand-glass) cursor for the duration of the constructor and forces a status-bar
   redraw.
2. If not a phone pane: closes the player's advisor dialog state (`Messages.StopDialog`).
3. Initialises state: no tip, no menu, no dialog partner, default bubble position (378,200) with
   style 2 ("tail pointing down"), `StatusCount=20`, zoom 0 / minimum zoom 1.0, `HandyOffset=160`,
   not using phone, not a long-distance call, calculator closed, no Smacker partner.
4. Room library: if the previous room called `KeepRoomLib`, the kept library is adopted instead of
   loading; otherwise loads `GfxLibName` from the room path (or none).
5. Phone pane only: scroll position `TempScreenScroll=800`, velocity `-1` (slide in), last scroll
   time "unset".
6. Loads the background bitmap (or an empty one) and sizes `RoomBm` to the background size.
7. Window rect = (0,0,640,480); `StatusLineSizeY=40`; `WinP1=(0,0)`, `WinP2=(640,480)`; status text
   bitmap `StatusBm` = 230 x 40. Copies `WinP1/WinP2` into the player record. **[W1, W2]**
8. Creates the child window (topmost in fullscreen) and activates it.
9. Marks ambient sound slots 0..2 as unused.

Derived rooms then typically: load fonts/extra libs, set up Smacker animations, call
`SetRoomVisited` (first-visit hint, §6.4), increase talker references, set
`DefaultDialogPartner` (who reacts when an inventory item is clicked, §9.4), and register ambient
sounds (only when the "ambience" option is on). (`Bank.cpp: Bank::Bank` as example) **H**

### 2.3 ReSize
- `ReSize(lib, id)`: releases the current room library, loads another, replaces the background and
  resizes `RoomBm`. **H**
- `ReSize(id)`: swaps the background within the current library. **H**

### 2.4 KeepRoomLib
Marks the current room library as "hand over to the next room": the destructor will not release it
and the next `CStdRaum` constructor adopts it. Used by the title/new-game/options screens to avoid
reloading. (`CStdRaum::KeepRoomLib`; users in `NewgamePopup.cpp`, `Options.cpp`) **H**

### 2.5 Destructor
(`CStdRaum::~CStdRaum`) **H**
- Stops calculator and speech sounds; sets the global "room just left" flag (aborts any bubble
  blending in progress); closes an open menu; releases the calculator library.
- If the advisor currently on screen is the player's own portrait (dialog mode, type ≥100), resets
  monologue/talking/talk-phase/countdown; then asks the advisor queue to drop pending *comments*
  (`NoComments`).
- If the player has a phone pane that is not this window, deletes it.
- Releases the background and the room library (unless kept).

### 2.6 Background ambient sounds — SetBackgroundFx
`SetBackgroundFx(slot 0..4, wavFile, avgWaitMs, startWaitMs=0, volumePct=100)`
(`CStdRaum::SetBackgroundFx`, playback in `CStdRaum::OnPaint(BOOL)`) **H**
- First trigger time = now + avgWait × random(50..149 %) − startWait.
- Every frame, for each of the 5 slots: if now > trigger time → play the sample once (no restart if
  already playing) at volume = effects-option × 100/7 × volumePct/100, and schedule the next trigger
  at now + avgWait × random(50..149 %).
- Rooms only register ambience when the ambience option is enabled.
- Unregistered slots are silent (the original leaves them uninitialised; OpenAT: treat as inactive).
- Examples (sounds in `SOUND\`):

| Room | Slot | File | avgWait | startWait | vol |
|---|---|---|---|---|---|
| Bank | 0 | Tippen.raw (typing) | 12000 | 0 | 100 |
| Bank | 1 | printer.raw | 30000 | 0 | 100 |
| Fracht / LastMinute | 0 | Pap3.raw (paper) | 25000 | 0 | 100 |
| Reisebüro | 0 / 2 | Pap3.raw / moeve.raw (gull) | 25000 / 50000 | 0 / 25000 | 100 |
| Ricks | 0 / 2 | Kaffee.raw / toilet.raw | 40000 / 120000 | 27000 / 80000 | 100 / 40 |
| Werkstatt | 1/2/3 | crash.raw / crash2.raw / crash3.raw | 100000 / 90000 / 120000 | 10000 / 40000 / 75000 | 100 |
| Insel, WeltAll | 0 | moeve.raw | 50000 | 25000 | 100 |

### 2.7 Room transitions (enter / leave) — framework side
(`Player.cpp: PLAYER::EnterRoom/LeaveRoom/LeaveAllRooms`, `Takeoff.cpp` main loop) **H**
- A player has a stack of up to 10 *locations* (airport at the bottom). Each entry can carry an
  ENTERING (1024) or LEAVING (2048) flag.
- `EnterRoom(r)`: if not already in the stack, pushes `r|ENTERING` (special: entering own office
  with the "walk to globe" flag immediately stacks the globe on top).
- `LeaveRoom()`: flags the topmost location LEAVING. `LeaveAllRooms()`: flags all non-airport
  locations LEAVING (used when a keyboard hotkey sends the player walking elsewhere).
- Main loop, per local human player, each sim step:
  - **Leaving**: decrements the room's busy counter; if the "fade" option is on (and not laptop, and
    after 9:00 or on day 0) takes a screenshot for the crossfade; deletes the location window; pops
    the entry and marks the entry below as ENTERING.
  - **Entering**: (screenshot again unless one was just taken, not for laptop/globe) deletes the
    old window, clears the flag, creates the new room window by room id (e.g. Insel/WeltAll/Rushmore
    also play `coin.raw`), arms the crossfade, recalculates the top room, broadcasts position/room
    to network peers.
- So leaving a room returns the player to the location underneath (normally the airport view, which
  is re-created). The player's airport figure is placed/handled by the airport/person code
  (out of scope; see airport document). **M**

---

## 3. Phone pane ("Handy" dialog mode)

A telephone conversation with a character located in another room (via the mobile phone item, the
office phone, or the network) shows **that character's room as a narrow panel sliding in from the
right**, while the caller stays in their current location. **H** (mechanics) / **M** (visual)

### 3.1 Creation
- The phone-book menu (another document) checks availability (room busy, opening hours, talker
  busy) and then creates `PLAYER::DialogWin = new <Room>(bHandy=TRUE, player)` and calls
  `StartDialog(talker, MEDIUM_HANDY, …)` **on the caller's location window**.
  (`Stdraum.cpp: CStdRaum::MenuLeftClick` MENU_FILOFAX branch; also `Sim.cpp` for incoming calls)
- `StartDialog` with MEDIUM_HANDY increments `Sim.RoomBusy[thatRoom]` (the room is "occupied" for
  others) and sets the phone pane's `HandyOffset` (which horizontal slice of the remote room is
  shown) and the bubble anchor in *screen* coordinates (table §4.6).

### 3.2 Slide animation (`CStdRaum::OnPaint(BOOL)` on the pane, `CStdRaum::PostPaint` on the caller)
- State: `TempScreenScroll` (screen x of the pane's left edge), `TempScreenScrollV` (−1 in, +1 out,
  0 stopped), `LastScrollTime`.
- Each paint of the pane: x += velocity × elapsed_ms / 3 (≈ 333 px/s), while moving in and x>378,
  or moving out and x<800.
- Slide-in ends at x=378 (velocity 0) and the airport renderer is clipped at x=378
  (`RightAirportClip=378`) so nothing is wasted drawing behind the pane. **[W7]**
- Slide-out (set by `StopDialog`) ends at x≥800; the caller's `OnPaint` then deletes the pane
  window once it is fully out and no dialog is open (and skips 3 message pumps for safety).
- `StopDialog` restores `RightAirportClip=640`.

### 3.3 Compositing (done by the caller's `PostPaint`, phone pane `PostPaint` itself does nothing)
- Source: pane's `RoomBm`, columns `HandyOffset .. HandyOffset+340`, rows 0..439.
- Destination: x = `TempScreenScroll`, y = 0, clipped to the room area (0,0)-(640,440).
- The left edge is **slanted**: 9 horizontal bands of 52 px (starting at y=−24), each band extends
  13 px further left than the band above (i.e. the pane edge leans like "/" … actually each lower
  band is shifted 13 px left), and the bitmap `DIALOG` from `glstd.gli` (`gDialogBarBm`) is drawn
  transparently along the edge of each band as a frame strip. **M** (exact look of the strip)
- If the caller is in a room (has a room bitmap) the pane is composited into the room bitmap;
  at the airport it is drawn straight into the back buffer.
- Order: pane is composited **before** the speech bubble, so bubbles overlay the pane.

### 3.4 Input while a pane exists
Two child windows exist, so either may receive a click. A click that reaches the pane is stored
(position + current click-target globals) and replayed by the caller's next `OnPaint`
(`Dialog.cpp: CStdRaum::PreLButtonDown` bHandy branch, `CStdRaum::OnPaint(BOOL)`). OpenAT: route all
input to the caller only. **H**

### 3.5 Call charges
While a dialog is open, a phone pane exists and the player pays for the call, every
`((2 − usingMobile)·10 + (3 − longDistance)·60) × 50` sim-time units (≈ 9.5 game minutes on the
mobile, 10 on the office phone, less for long-distance) the player is charged 1 currency unit,
booked as a call cost (history + "other expenses" statistic, network sync), and the status bar is
refreshed. (`CStdRaum::PostPaint`) **H** (formula) / **L** (where long-distance is set TRUE — not in
this layer; see Q5)

---

## 4. Speech bubbles / say windows

### 4.1 Data model
- `CurrentTextGroupId` (0 = no bubble), `CurrentTextSubIdVon` / `…Bis` (first/last text id),
  up to 10 option strings (`Optionen` = display text, `OrgOptionen` = with embedded speech-file
  tag), `TextAreaSizeY[i]` (wrapped height per option), `CurrentHighlight` (id under cursor),
  `TextAlign` (0 = partner speaks, 1 = player speaks / chooses), `BubblePos` + `BubbleStyle`
  (partner anchor), `pFontNormal/pFontHighlight`.
- The bubble is painted into `OnscreenBitmap` (full window size, 640x480, black = transparent) and
  composited each frame.
- `IsDialogOpen()` = a text group is active OR a dialog partner is set OR this window is a phone pane.
  (`CStdRaum::IsDialogOpen`) **H**

### 4.1b Font line spacing (`Init.cpp`, `CFont`) **H**
A font advances `cell height × LineSpace` per text line; the default LineSpace is 1.5. At start-up the
game sets **LineSpace 1** on its main fonts: `status.mcf` (cash), `norm_bl/gr/rt/wh/x/pl.mcf`,
`dlgfont1.mcf` and `dlgfont2.mcf` (dialog, highlight, partner), `dlgfont3.mcf` (big grey) and
`norm_drk.mcf`. The others (very small, condensed, ink, big white, normal green) keep 1.5. Verified on the
running original: dialog lines are 20 px apart (dlgfont cell 20) and choice options 25 px (20 + LINE_DIST 5).

### 4.2 MakeSayWindow variants (`Stdraum.cpp: CStdRaum::MakeSayWindow` ×3) **H**
1. **Literal text** `(align, subId, string, font)`: one string; if align≠0 it is treated as a
   single-entry *player* bubble (Von=Bis=subId), otherwise a *partner* bubble (Bis=0). Keeps the
   previous text group id if none is active.
2. **Resource text** `(align, groupToken, subId, font, printf-args…)`: fetches `dlg_ger.res`
   group/id, formats arguments into it, then behaves like (1) and records the group.
3. **Choice list** `(align, groupToken, idFrom, idTo, paramMode, normalFont, highlightFont, args…)`:
   loads ids idFrom..idTo (max 10) from `dlg_ger.res`. `paramMode`: FALSE = every string gets the
   same argument list; TRUE = one 4-byte argument per option, consumed in order; 2 = two arguments
   per option. Each option's wrapped height is measured (see 4.4).
- Common effects of all variants: reset `CanCancelEmpty`, record open time, reset highlight,
  stop any playing speech sample, decide **bubble visibility** (`DisplayThisBubble`), repaint,
  then start the **voice**:
  - Bubble visible if the "speech bubbles" option is on, or voice output is impossible (talk volume
    × master volume = 0, digital sound off/disabled). Choice lists with >1 option are always visible.
  - Voice (only for single texts, voice option on, digital sound on): build a speech sample from the
    speech-file tag embedded in the text, voice of the local player (align 1) or of `DialogPar1`
    (align 0). If the partner has a Smacker animation and is the speaker, playback is delayed until
    the partner animation is in TALKING mood (3-frame countdown), otherwise plays immediately at
    volume talk-option × 100/7. A sample with missing parts disables "talking" tracking.
- Default fonts: partner text `FontDialogPartner`; player options `FontDialog` with highlight
  `FontDialogLight`. (`CStdRaum::StartDialog` usage)

### 4.3 Partner bubble geometry (single text, `TextAlign=0`) — `CStdRaum::RepaintText` **H** [W8]
`h` = wrapped text height. Bubble rectangle (p1–p2) and tail point ("entry"):

| Style | Meaning | Measure width (x from..to) | Bubble rect | Tail | Text area |
|---|---|---|---|---|---|
| 2 | above the speaker, tail down | 90..560 | (50, BY−h−8) – (600, BY+8) | (BX, BY+8) on bottom edge | x 80..560, y BY−h..BY |
| 0 | below the speaker, tail up | 90..560 | (50, BY−8) – (600, BY+h+8) | (BX, BY−9) on top edge | x 80..560, y BY..BY+h |
| 1 | left of the speaker, tail right | L+45..BX−40 (L=5, or 80 when talking to another player) | (L, BY−h/2−8) – (BX−10, BY+h/2+8) | (BX−10, BY) on right edge | x L+45..BX−40, vertically centred |
| 3 | right of the speaker, tail left | BX+40..600 | (BX+10, BY−h/2−8) – (630, BY+h/2+8) | (BX+10, BY) on left edge | x BX+50..600 (hit rect right edge 620) |

(BX,BY = `BubblePos`.) While a partner bubble is shown the partner animation is set to TALKING and a
"talk until" time = now + 100 ms × character count is set.

### 4.4 Player bubble geometry (`TextAlign≠0`, single answer or choice list) **H** [W8]
- Player anchor = `BeraterSprechblasenOffset[12+localPlayer]`:
  P1 (73,377), P2 (73,389), P3 (82,385), P4 (82,385). In the player's **own office** the large
  player figure is used instead: `BigPlayerOffset[localPlayer]` = P1 (210,235), others (220,240).
- Option wrap width: left = anchor.x+48, right = 620−R (R=5; 70 when talking to another player);
  max height 400. In the office: left = big.x+48 (−15 vs. another player), right 620 (540 vs. player).
- Total height S = Σ(hᵢ+5) − 5 + 1 (5 px line gap, `LINE_DIST`).
- Vertical placement: if 430 − S/2 > anchor.y → top = anchor.y − S/2 (centred on the anchor),
  else top = 430 − S (bottom aligned at y=430).
- Bubble rect: normal (anchor.x, top−10) – (630, top+S+10), tail at the anchor (left edge); text
  rect right edge 615. Versus another player: right edge 560 (text 550). In the office: centred on
  big.y, from big.x−15 to 630 (versus a player: big.x to 560).
- Lines: first line at text-rect top+10; each option at x = rect.left+48; for real choice lists a
  bullet glyph (the font's `#` character, a bullet/arrow symbol in the dialog font) at rect.left+30.
  The highlighted option and its bullet use the highlight font.
- While the player "speaks" a single answer (or when there is no voice) the player portrait's
  talking counter is set to 2 × text length (drives mouth animation, §10.2); in the office the
  Smacker player figure is set TALKING for the highlighted/single line.
- Partner listens (mood LISTENING) while player options are shown.

### 4.5 Highlight tracking — CheckHighlight (`CStdRaum::CheckHighlight`) **H**
- Called from `PostPaint` when the bubble is visible. If the cursor is outside the window, clears the
  highlight.
- For choice lists only: recomputes the same vertical layout as 4.4 and finds the option whose band
  [topᵢ, topᵢ+hᵢ+5] contains the cursor y (x is not tested). On change: plays `change.raw`, stores
  the id, repaints. No option under cursor → highlight cleared.
- `InitToolTips` turns the result into a click target: single text → "click anywhere = continue"
  (area −102, id 1, par = text id); list with highlight → (area −102, id 2, par = highlighted id);
  list without highlight → no valid click.

### 4.6 Partner bubble anchors per talker (`CStdRaum::StartDialog`) **H**
In-room (`MEDIUM_AIR`) and phone (`MEDIUM_HANDY`, screen coordinates with the pane at x=378).
"Pane offset" = `HandyOffset` of the phone pane (default 160 if not set).

| Talker | In-room BubblePos / style | Phone BubblePos / style | Pane offset |
|---|---|---|---|
| Fracht (cargo) | (556,126) / 1 | – | – |
| Security | (328,100) / 2 | – | – |
| Designer | (268,126) / 0 | – | – |
| Arab Air | (486,183) / 1 | (526,183) / 1 | 160 |
| Sabotage | (396,198) / 1 | (400,188) / 1 | 367 |
| Banker 1 (man) | (477,157) / 2 | (479,157) / 2 | 367 |
| Banker 2 (woman) | (240,147) / 2 | (467,202) / 1 | 153 |
| Boss (Uhrig) | (400,102) / 2 | (470,158) / 1 | 160 |
| Makler | (540,200) / 2 | (540,200) / 2 | 378 |
| Mechanic | (200,203) / 2 | (445,199) / 2 | 131 |
| Museum | (440,234) / 2 | (440,234) / 2 | 367 |
| Personnel "a" (woman) | (443,208) / 1 | (535,207) / 1 | 280 (caller overrides 320) |
| Personnel "b" (man) | (280,144) / 3 | (435,124) / 1 | 120 (caller overrides 160) |
| Werbung | (400,250) / 2 | (526,300) / 1 | 160 |
| NASA | (394,114) / 2 | (350,292) / 1 | 160 |
| Duty free | (396,128) / 1 | (396,198) / 1 | 160 |
| Kiosk | (288,192) / 2 | – | – |
| Passenger (airport) | (DialogPar2, DialogPar3) = passenger screen pos / 2 | – | – |
| World (foreign city call) | copied from the created world window | – | – |
| Rick | (220,105) / 2 | – | – |
| Drinker (Ricks) | (480,113) / 2 | – | – |
| Competitor (other player) | (640 − anchor.x of that player, 380) / 1 | same | – |

### 4.7 Compositing & timing (`CStdRaum::PostPaint`) **H/M**
- Only when the app is active and a text group is active.
- The bubble is **withheld** while the partner's Smacker animation is still getting ready to talk
  (not yet TALKING and the talk-until time not reached); during that wait the talk-until time is
  pushed forward, and when the partner is LISTENING but the voice has started, it is switched to
  TALKING.
- When shown: first the number window (§4.9) is copied into the room bitmap, then the bubble area
  is blended into the room bitmap (or the back buffer at the airport) with **white-transparency**:
  pixels of the bubble "white" (the colour of the first pixel of `XBLASE09`) are mixed
  ≈ 25 % background / 75 % white, black pixels are transparent, all other pixels (text, outline) are
  opaque. (`Colorfx.cpp: SB_CColorFX::BlitWhiteTrans`) **M** (exact ratio from blend-table
  indices 2 and 6 of 8)
- The partner's mouth animation is switched back to LISTENING when the voice sample's mouth-open
  detector says closed, or the talk time expired.
- **Auto-advance**: if a single (non-choice) text is open with a voice sample that has finished
  playing, the framework simulates a click (`PreLButtonDown(1,1)`), i.e. voiced dialogue advances by
  itself. **H**
- **Competitor (AI) impatience**: in a dialog with a computer-controlled player, after 15 s of the
  same bubble: single text → auto-continue; choice list → the AI says its "bored" line
  (`dlg_ger.res "Play" 2499`) and marks itself bored; if that line is already shown → dialog ends.
- While another human player (network) is thinking, a thought-bubble mood icon (`MoodBms` empty
  mood) is drawn above their portrait when no bubble is open.

### 4.8 RepaintText / CloseTextWindow **H**
- `RepaintText(refreshAll)`: if the highlight changed since last call, forces full refresh. Full
  refresh clears/resizes `OnscreenBitmap` and repaints the bubble frame; partial refresh repaints
  only the text (used for highlight changes). With no active text group it destroys the bubble
  bitmap and sets "room just left" (aborts an in-progress blend).
- `CloseTextWindow()`: closes the number window, stops the player portrait's talking, remembers the
  current group for menu re-entry, clears the group and all options, repaints (→ bubble destroyed).

### 4.9 MakeNumberWindow (number info box) — `CStdRaum::MakeNumberWindow` **H**
Shows a small box with figures that the voice does not speak (prices, counts).
- Variant with resource text (`dlg_ger.res` group/id + args) or literal text; empty text closes the
  box and releases its library.
- Spaces are made non-breaking. Library `gli\glnumber.gli`, bitmap `NUMBERS` (background template).
- Size search: width w from 60 in steps of 10 (up to 630) with height 2/3 w until the text
  (font `FontDialogPartner`, left aligned) fits; then reduce the height in steps of 10 to the
  smallest that still fits; final box = (w+20) x (h+20); the template is stretched to that size and
  the text is drawn at x=10, vertically centred.
- Position: horizontally centred on x=320, bottom edge at y=439. **[W9]**
- Drawn into the room bitmap right before the bubble (same visibility timing).

### 4.10 PaintTextBubble (frame drawing) — `Misc.cpp: PaintTextBubble` **H/M**
Bitmaps `XBLASE01..14` from `gli\glstd.gli` (`XBubbleBms[0..13]`):

| Index | Use |
|---|---|
| 0,1 | top-left / top-right corner pieces (tall bubbles, height ≥ 68) |
| 2,3 | left / right edge strip, tiled vertically |
| 4,5 | bottom-left / bottom-right corner pieces (tall bubbles) |
| 6,7,8,9 | top-left, top-right, bottom-left, bottom-right pieces for small bubbles (< 68 px) |
| 10 | tail pointing down (entry on bottom edge) |
| 11 | tail pointing up (entry on top edge) |
| 12 | tail pointing left (entry on left edge) |
| 13 | tail pointing right (entry on right edge) |

The tail is chosen by which edge the entry point lies on (x==left, x==right, y==top(±1), y==bottom);
it is centred on the entry point. The returned rectangle is the bubble rect enlarged by the tail.
The left/right pieces evidently contain the white interior (no separate fill), i.e. each piece covers
about half the width. **L** — verify sizes in `glstd.gli` (Q2).

### 4.11 CanCancelEmpty
Normally a click is ignored while the partner animation is still preparing to talk. When
`CanCancelEmpty` is TRUE (set by dialogue code when the voice sample for the line is empty), such a
click is accepted. Reset to FALSE by every MakeSayWindow. (`Dialog.cpp: CStdRaum::PreLButtonDown`) **H**

### 4.12 StartDialog / StopDialog semantics
`StartDialog(partner, medium, par1=-1, par2=-1, par3=-1)` (`CStdRaum::StartDialog`) **H**
- Stores partner (talker id, `TALKER_*` 0..26, passenger 998), `DialogMedium` (0 = `MEDIUM_AIR`
  face-to-face, 1 = `MEDIUM_HANDY` telephone), and three generic parameters (`DialogPar1..3`).
  Conventions: Par1 commonly selects the opening line (≥10000 = "reaction to item 10000+item";
  specific values pick special openings); for competitor dialogs Par1 = other player number,
  Par2 = who speaks first; for passengers Par1 = person index and Par2/Par3 = bubble position;
  for world calls Par1/Par2 = player/city.
- Resets zoom, Smacker partner/player, "don't display player" = −1 (show portrait), "player pays
  for the call" = TRUE.
- If the player figure is in an idle gimmick pose it is reset to standing.
- Notifies the talker object (locks it) and the player's advisor queue (dialog mode → player
  portrait slides in, any advisor slides away).
- Office: player Smacker figure → LISTENING.
- Per partner: font, Smacker partner (from this room, or from the phone pane for MEDIUM_HANDY),
  bubble anchor/style (table 4.6), pane offset, room busy counter (phone), and the opening
  `MakeSayWindow` (content: dialogue document). Special: TALKER_WORLD creates a world phone pane
  and either speaks the greeting or opens the foreign-contracts menu directly.
- Competitor: marks the other player as talking.

`StopDialog()` (`CStdRaum::StopDialog`) **H** — only if a dialog is open:
- World call after the contracts menu: marks the city as called today.
- Network competitor: sends dialog-end; clears talking flags; also sends dialog unlock messages.
- Clears phone flags (`bHandy`, using-mobile, long-distance), stops speech.
- Phone medium: decrements that room's busy counter (Arab, Bank, Boss, Makler, Mechanic, Museum,
  NASA, Duty-free, Sabotage, Werbung).
- Advisor queue leaves dialog mode; Smacker partner → IDLE; references cleared.
- NASA special line (id 9999) → player leaves the room. Passenger → passenger stops talking.
- Airport clip back to 640; phone pane starts sliding out; office figure → IDLE; talker unlocked;
  partner = none; `CloseTextWindow`; personnel dialogs → staff overflow re-check.

### 4.13 Generic click handling for dialogs (front part of `Dialog.cpp: CStdRaum::PreLButtonDown`) **H**
Every room's left click first goes through `PreLButtonDown`; it returns TRUE if consumed.
1. Phone pane: store click for replay (§3.4).
2. If an advisor (not dialog mode) is fully visible: click inside (560,320)-(640,440) or inside the
   advisor's speech bubble → skip to next message. **[W18]**
3. Menu open → `MenuLeftClick` (menu document).
4. Dialog open:
   - ignore while partner is preparing to talk (unless `CanCancelEmpty`);
   - ignore a second click on the same option within 1000 ms (double-click guard);
   - game-over menu: ignore within 500 ms of opening;
   - if a bubble is being blended right now, defer the click (`PleaseCancelTextBubble`, replayed by
     `OnPaint`);
   - valid click = area −102 with id 1 (continue) or 2 (choice): the chosen original text is taken,
     the bubble closed, and the dialogue tree continues (dialogue document). Anything else: consumed,
     no effect.

---

## 5. Tooltips, help and highlighting

There are **three** separate hint mechanisms:

### 5.1 Text tooltips (hover box) — `Gameframe.cpp: SetToolTip/SetMouseLook`, `GameFrame::OnPaint` **H**
- Each frame the active hotspot sets a tooltip id via `SetMouseLook(look, tipId, area, id, p1, p2)`
  or a literal string via the overload with a description (internally stored as negative id).
- Id > 0 → text `std_ger.res "Tool" <id>`; id < 0 → the literal string; id 0 → no tooltip.
- Timing: when the id changes, a timer restarts; the box appears after **600 ms** of the same id and
  disappears after **5000 ms**. F1 (`OnHelpInfo`) shows it immediately. Any left button press hides
  it (timer set 8 s into the past). (`Gameframe.cpp: DefaultOnLButtonDown`)
- Box: text in `FontBigGrey`, centred, single line height 25; width found by shrinking from 640 in
  steps of 64 while it still fits on one line, then growing by 8 until it fits; rounded up to a
  multiple of 28 (+2). Background tiles from `glstd.gli` `TOOLTIP1` (left cap), `TOOLTIP2`
  (middle, repeated every 28 px), `TOOLTIP3` (right cap); height 28. Drawn with white-transparency
  (no message pumping).
- Position: x = mouse.x + 16 − w/2, clamped to [2, 639−w]. If mouse.y < 439: y = mouse.y + 32.
  Otherwise (cursor in the status bar's bottom row) y = mouse.y and the box goes to the right of the
  cursor (x = mouse.x+32) if it fits before x=630, else to the left (x = mouse.x−5−w); y ≤ 452.
  **[W10]**
- One string in the German text has a hard-coded runtime substitution (a wording fix); OpenAT should
  fix the resource instead. **H** (existence only)

Tooltip id ranges used by this layer (all `std_ger.res "Tool"`):

| Id | Shown for |
|---|---|
| 1000 | Options / main-menu icon (status bar, bottom right) |
| 1001 | Date & time display |
| 1002 | Money display |
| 1003 | Clock (game speed) |
| 2000 + itemId | Inventory item (item ids 0..25, §9.4) |
| 2100 | "Repeat last message" icon |
| 2101 | "Cancel tutorial" icon |
| 2102 | Airline logo (statistics) |
| 4000..4511 | Menu buttons (menu document) |
| 5000 + roomId | Airport door/area tooltips (airport document; own office/personnel get a key-letter suffix) |
| 3000+, 3102 … | Laptop, plane properties, statistics (room documents) |

### 5.2 Room "tips" (data cards) — SetTip / RepaintTip / AnnouceTipDataUpdate **H**
A richer bitmap card (city, plane, contract, cargo …) drawn near the cursor.
- `SetTip(sourceBm, sourceBm2, forceRedraw, pos, type, id, par1, par2)` — called by rooms each frame
  while hovering; `OnPaint` resets the type to NONE every frame, so a tip shows only while re-set.
- `PostPaint` repaints the card bitmap when type/id/params changed or a redraw is forced, then draws
  it at `pos`, clamped into the room area (right edge, bottom edge = 440) and then scaled by
  (window width/640, (window height−40)/440) — a split-screen leftover, identity in practice. **[W11]**
- `AnnouceTipDataUpdate(type)`: force a redraw if the displayed tip has that type (data changed).

| Tip type | Value | Rendering |
|---|---|---|
| TIP_NONE | 0 | nothing |
| TIP_CITY | 10 | city card (`DrawCityTip`, id = city) |
| TIP_PLANE | 20 | own plane card (`DrawPlaneTip`, id = plane index; only when par1 = 0) |
| TIP_BUYPLANE | 21 | no-op in this layer (room draws its own) |
| TIP_AUFTRAG | 30 | own passenger contract card (`DrawAuftragTip`, par1/par2 = display flags) |
| TIP_BUYAUFTRAG | 31 | last-minute contract card (id −1 = empty) |
| TIP_BUYAUFTRAGR | 32 | travel-agency contract card (id −1 = empty) |
| TIP_BUYFRACHT | 33 | cargo offer card (`DrawFrachtTip`) |
| TIP_FRACHT | 34 | own cargo contract card |
| TIP_ROUTE | 40 | no-op |
| TIP_BUYROUTE | 41 | no-op |
| TIP_AKTIE | 50 | no-op |
| TIP_ITEM | 60 | no-op |
| TIP_MONEY | 70 | defined, unused here |
| TIP_AUTOFLUG | 90 | defined, unused here |

The `Draw*Tip` functions (`Misc.cpp` / tip code) are documented with the rooms that use them.

### 5.3 Hotspot highlighting — InitToolTips / CheckCursorHighlight / PumpToolTips **H**
- `InitToolTips` (called by every room each frame before its own hotspot checks; skipped while the
  calculator is open; does nothing for phone panes):
  - resets the cursor to NORMAL with click target (area −100, id 0) and clears text highlight;
  - menu page-arrow cursors for a few menus; **status bar hotspots** (only when no modal top window):

| Rect (x1,y1)-(x2,y2) inclusive | Cursor | Tooltip | Click target |
|---|---|---|---|
| (80,460)-(242,479) money | HOT | 1002 | −100 / 1002 |
| (0,440)-(79,479) logo | HOT | 2102 | −100 / 1000 |
| (80,440)-(102,459) clock | HOT | 1003 | −100 / 1003 |
| (102,440)-(242,459) date/time | HOT | 1001 | −100 / 1001 |
| (250,440)-(607,479) items, slot = (x−250)/60 < 6 and non-empty | HOT | 2000+item | −100 / 2000+item |

  - menu-specific hotspots (menu document) and dialog click targets (§4.5).
- `CheckCursorHighlight(rect, fontColour, look, tip, area, id, p1, p2)` (`Misc.cpp`): if the cursor is
  inside `rect` (and no dialog is open unless a menu is) → sets the hotspot and records the rect +
  font colour for text highlighting.
- `PumpToolTips` (called by rooms at the very end of their paint): recolours the font-coloured pixels
  inside the highlight rect on the back buffer — black text → green (0x20FF64), grey text →
  0x305030, red text → light red (0xFF8080); then draws tutorial **glow markers** (`gli\glglow.gli`
  bitmaps `TUTMARK1..5`) at the registered positions (offset by the planner window position in the
  globe room).

### 5.4 First-visit room descriptions (help texts) — `Stdraum.cpp: SetRoomVisited` **H**
- Called by each room's constructor. A per-installation bitmask option (`OptionRoomDescription`)
  remembers which room descriptions were already shown (bit index from a room → description-number
  table: offices 1, personnel 2, duty-free 3, bank 4, kiosk 5, museum 6, last-minute 7, Arab Air 8,
  Makler 9, Aufsicht 10, Tafel 11, Werbung 12, Werkstatt 13, Reisebüro 14, Sabotage 15, NASA 16,
  Insel 17, Rushmore 18, statistics 19, plane props 20, Ricks 21, route box 22, cargo 23, WeltAll 24,
  editor 25, security 26, designer 27).
- If the description was not yet shown and the player has never been in that room this game (and not
  in the scripted tutorial), the assistant (girl advisor) queues the text
  `std_ger.res "Tuto" 10000 + roomId` (offices use 10000+10, personnel 10000+11; Werbung on easy
  difficulties uses `"Tuto" 20000 + roomId`; no text for airport, laptop, evening, world, id 254).
- Marks the room visited for the player (offices/personnel collapse to the A variant).
  `WasRoomVisited` answers that question (used by the phone book / filofax to list known rooms).

---

## 6. Mouse

### 6.1 Cursor types — `defines.h`, `Gameframe.cpp: GameFrame::OnPaint`, constructor **H**
Cursor bitmaps from `gli\glbasis.gli` unless noted. The image is re-selected only when the look
changes (exit and feet cursors animate continuously). "Offset" = hotspot (image is drawn at
mouse − offset).

| Constant | Value | Bitmap | Offset | When shown |
|---|---|---|---|---|
| CURSOR_NORMAL | 0 | `CURSOR` | (0,0) | default every frame; also always while paused |
| CURSOR_HOT | 1 | `CURSORH` | (0,0) | over any clickable hotspot, dialog text, status bar elements |
| CURSOR_EXIT | 3 | `EXIT01..EXIT12` (12-frame loop, frame = (ticker/2) mod 12, ticker = ms/32 → ≈64 ms per frame) | (15,12) | over a room's exit area (§8.3) |
| CURSOR_LEFT | 4 | `CURSORL` | (2,16) | page back in menus; scroll left at airport (x ≤ 10) |
| CURSOR_RIGHT | 5 | `CURSORR` | (29,16) | page forward; scroll right at airport (x ≥ 630) |
| CURSOR_MOVE_H | 6 | `CURSORV` | (16,0) | horizontal drag handles |
| CURSOR_MOVE_V | 7 | `CURSORW` | (0,16) | vertical drag handles |
| (wait) | – | `CURSORS` (sand glass) | (16,16) | whenever the wait counter > 0 (room construction, loading) |
| (none) | – | 10x10 empty | – | during intro/credits start-up |
| (feet) | – | `glstd.gli` `CWALKR01..09` / `CWALKL01..09` (frame = ticker mod 9) | (24,13) | at the airport while NORMAL and the player's own figure is walking off-screen: walking right (dirs 1–3) → right-feet set, left (dirs 5–7) → left-feet set |

`CURSORX` exists in the library but is unused. When the cursor is in the lower part of the screen
(y > 380) with feet showing, the status bar is refreshed (feet may overlap it).

The hardware cursor is hidden over the client area; the game draws its own cursor (option to use the
Windows cursor exists, `gUseWindowsMouse`). (`GameFrame::OnSetCursor`, `CStdRaum::OnSetCursor`
forwards to the frame) **H**

### 6.2 Mouse move — `GameFrame::OnMouseMove` (`CStdRaum::OnMouseMove` forwards) **H**
- Each local human player's cursor position = raw point; the global mouse position used for all
  hotspot tests = raw point **+ (2,2)**. **[W19]**
- Resets the departure-board idle timer; moves the software cursor immediately.
- `GameFrame::OnPaint` (every ≥30 ms) clamps each local player's cursor into the player window.

### 6.3 Button routing
- Frame: blocks all input while the network options screen is open for someone else
  (`nOptionsOpen` checks), then **re-posts** button-down, double-click, key and char messages to the
  modal top window if present, else to every player's location window (button-up messages are not
  re-posted). (`GameFrame::OnLButtonDown/OnRButtonDown/OnLButtonDblClk/OnKeyDown/OnChar`,
  `GameFrame::RePostMessage`) **H**
- `RePostClick(player, msg, …)` sends to one specific player's window (network/AI use). **H**
- `DefaultOnLButtonDown` (rooms call it first): un-pauses if paused, hides the tooltip, records
  press time, sets the player's "left button held" bit (used for drag in globe/laptop/planner),
  forces a status-bar redraw, plays `click.raw` (effects volume). `DefaultOnLButtonUp/RButtonDown/Up`
  maintain the held bits. **H**

### 6.4 Standard room left-click flow (pattern; `Bank.cpp: Bank::OnLButtonDown`) **H**
1. `DefaultOnLButtonDown`.
2. Outside the window → ignore.
3. `PreLButtonDown` (§4.13) consumed → stop.
4. Room hotspot by (area = room id, id): id **999 = leave room** (`PLAYER::LeaveRoom`), others room
   specific.
5. Otherwise → `CStdRaum::OnLButtonDown` = **status bar clicks** (§9.3).

### 6.5 Standard room right-click flow (pattern; `Bank.cpp: Bank::OnRButtonDown`, `CStdRaum::OnRButtonDown`) **H**
1. `DefaultOnRButtonDown`; outside window → default.
2. Menu open → `MenuRightClick` (menu document).
3. Otherwise: if **no dialog is open and y < 440 → leave the room**.
4. Then `CStdRaum::OnRButtonDown`:
   - dialog open → `StopDialog` (abort conversation). Special: a foreign-city phone call returns to
     the phone book's city list instead of closing.
   - clock (78..101, 440..459) → game speed −1 (wraps 0→3), network sync, refresh.
   - item slot (no menu, no dialog), non-empty → "throw item away?" confirmation request
     (`MENU_REQUEST` KILLITEM), zooming from (250+slot·60, 446).
   - menu open and y < 440 → close the menu (from the phone's player/branch list → back to the phone
     book with paper sound; toilet menus and the copy-protection prompt cannot be closed this way;
     letters menu plays the paper sound; closing a "thrown out" notice clears the wait flags), then
     full bubble repaint.
- Note: a right click that aborts a dialog does **not** also leave the room in the same click,
  because the room checks "no dialog open" before calling the base. **H**

### 6.6 Double click — `CStdRaum::OnLButtonDblClk` **H**
Inside the window and not in the editor: calculator open → calculator click; personnel menu → extra
page flip with personnel-advisor commentary (menu document); dialog open → treated as a normal click
(`PreLButtonDown`).

---

## 7. Keyboard

### 7.1 Room-level keys — `CStdRaum::OnKeyDown`, `CStdRaum::OnChar` **H**
| Key | Effect |
|---|---|
| Delete | calculator: clear/delete key (calculator document) |
| F2 | open the options room (no dialog/menu, not loading, after 9:00, not in editor) |
| Backspace | delete last character in text-entry menus (rename plane, rename editor plane, IP entry, copy-protection, broadcast, chat) |
| characters | calculator digits; in text-entry menus accept space `: , ; ! ? ' - + ( ) .`, digits, letters and the German umlauts/ß; max 20 characters except chat/broadcast |
| Enter (char) | commit text entry: IP entry → stored as host address; copy-protection → compare (3 wrong tries ends the day); chat → send line to the partner and scroll the chat; broadcast → send to selected players and show locally; rename plane / editor plane → apply name |
| `+` / `-` | in the plane-repair menu with a plane line selected: target condition ±5 % (0..100) |

### 7.2 Global keys — `GameFrame::OnKeyDown` **H**
Pre-filter: everything is blocked while another player's network options screen is open.

**Room hotkeys (walk to a location)** — active only when: after 9:00, not editor, game playing,
hour 8–17, day not ended, not tutorial, the player's window has no dialog/menu, not in options,
not on the stairs pseudo-room, not in the evening screen, not walking to another player. The key
sends the player walking (`WalkToRoom`) and leaves all rooms; **pressing the same key twice within
300 ms makes the figure run** (unless stuck). Mapping by language:

| Location | English/other | German & Dutch | Portuguese | Extra condition |
|---|---|---|---|---|
| Air Travel (Reisebüro) | T | T | T | travel holding usable |
| Own office | O | B | O | |
| Rick's café | C | C | C | |
| Duty-free | D | D | D | |
| Advertising (Werbung) | I | E | I | not while Shift held |
| Globe | G | G | G | walks to own office then globe; if already in office enters globe directly |
| Airport manager (Aufsicht) | U | U | U | |
| Score / telescope | S | S | S | Rushmore; Insel in final mission; WeltAll in add-on mission 10 |
| Kiosk | K | I | K | |
| Last minute | L | L | L | travel holding usable |
| Museum | M | M | M | |
| NASA shop | N | N | N | only final mission / add-on 10 |
| Personnel office | P | P | P | |
| Route box | R | R | R | not in tutorial |
| Plane dealer (Makler) | E | F | E | |
| Bank | B | K | B | |
| Workshop | W | W | W | |
| Arab Air (Petrol Air) | A | A | A | not in tutorial |
| Cargo (Fracht) | H | H | H | add-on 2+ or free game |
| Designer | X | X | – | room must exist at this airport |
| Security | Y | Y | – | room must exist at this airport |

Each also invalidates the corresponding gameplay hint. (Note: the manual's key list differs slightly
from the code, e.g. it lists W twice; the code is authoritative.)

**Other keys** (same activity gate as above unless noted):

| Key | Effect |
|---|---|
| Tab | "call it a day / go home?" confirmation (`MENU_REQUEST` CALLITADAY), zoom from (320,220) |
| Esc, F2 | toggle options room (enter, or leave if already queued) |
| F3 | options room opened directly on the **Load** page |
| F4 | options room opened directly on the **Save** page |
| J (any language, gate: not in options / rename / broadcast) | answer **Yes** in an open confirmation request |
| Esc or N (no activity gate) | answer **No** in an open confirmation request |
| Esc with a modal intro/outro window | skip the video |
| Space | calculator open → speak the calculator value with synthesized number voice; tutorial active → skip the whole tutorial (as the cancel icon); otherwise at the airport with no dialog/menu, player walking, single-player → **time warp while walking** (×150 sim speed until the figure stops / leaves the airport, then camera re-centres) |
| `1`..`4` (cheat "mission" mode only) | make player N fulfil the current mission goal |
| Pause | toggle pause (if allowed, while playing); broadcast to network |
| Alt+F4 | quit immediately (leaves game loop, drops network) |
| F5/F6 | debug only (dump AI flight plans etc.) — ignore |
| Alt, F10 | swallowed (no Windows menu activation) |

After processing, every key (except Alt+F4) is re-posted to the room windows (§7.1).

**Gate of Tab / Esc / F2 / F3 / F4 (H).** In the frame handler these five keys sit inside the same
activity gate as the room hotkeys (after 9:00, day being played, hour 8–17, no call-it-a-day, **not in
the tutorial**, no dialog/menu, not on the stairs, not in the evening screen). Separately the room
window's own key handler opens the options on **F2** when no dialog or menu is open and it is after
9:00, without the tutorial check. Net effect while the tutorial runs: room hotkeys, Tab, Esc, F3 and F4
do nothing; F2 still opens the options. Outside the tutorial the same gate applies: with a dialog or menu
open, before 9:00, outside 8–18 h, after call-it-a-day, on the stairs, in the evening screen or while
walking up to another player, Esc/Tab/F3/F4 do nothing, and F2 works only through the room window's
route (no dialog/menu, after 9:00; so it also opens the options in the evening screen). OpenAT:
`RoomHotkeys.GlobalKeysAllowed` / `RoomF2Allowed` / `HandleOptionKeys`.

**Hotkey details (H).**
- "Already on the stack" check: a key whose target location is already in the player's location stack
  does nothing (the telescope key checks all three telescope rooms).
- Office key: clears the walk-to-globe flag before walking.
- Globe key: when the top location is the own office, the globe opens at once; otherwise the
  walk-to-globe flag is set and the player walks to the own office. When the office is entered with the
  flag set, the office is put on the stack but the globe is entered straight on top of it (the office
  view is never shown) and the flag is cleared.
- Travel agency / last minute: only when the mission has the travel-holding feature (the computer
  players' feature table). NASA: only in the space missions (base mission 5, add-on 10). Route box and
  Arab Air: not in mission 0. Cargo: add-on 2 or later (also ATFS) or the free game. Designer and
  security: only when the airport has that room's door.
- Telescope key: base mission 5 → island telescope, add-on 10 → space station view, else Rushmore.
- Running: the frame handler compares the key with the previous key and the message times; same key
  within 300 ms → the figure runs, unless it is stuck.
- A closed shop answers through the normal walk request (the "closed" note).
- The table follows the install language (`misc/sabbel.dat`): German and Dutch share one, Portuguese has its
  own, every other language uses the English one. OpenAT reads the same file (`TextResource.InstallLanguage`).

### 7.3 Global character keys — `GameFrame::OnChar` **H**
(Not in options; not in the rename menu.)
| Char | Effect |
|---|---|
| Enter | confirmation request open → **Yes**; broadcast menu → send (empty text → close); no menu, >1 human player, after 9:00, no dialog → open the **broadcast chat** entry |
| `+` | game speed +1 (0..3 wraps), network sync, refresh status bar |
| `-` | game speed −1 (wraps) |

Game speed levels 0..3 → sim multipliers 1, 2, 4, 8 (network: 1, 2, 3, 4); the slowest speed among
active human players applies; before 9:00 / after the day ended ×600; time warp ×150.
(`Takeoff.cpp` main loop) **H**

### 7.4 Cheat codes — `GameFrame::OnKeyDown` **H**
Typed letters A..Y (Z is not recorded) are appended to a 30-character rolling buffer; a code fires when
the buffer ends with it. All play `cheat.raw`. Codes marked "net" work in network games only if
cheating is allowed; several broadcast a "player X cheated" chat line (`std_ger.res "Misc"`
7010–7016) and mark the session as cheated.

| Code | Effect |
|---|---|
| RUNNINGMAN | toggle "running man" flag (player always runs) |
| MENTAT | toggle advisor cheat (advisors give full info) (net) |
| PANIC | all ordinary passengers leave the airport |
| SHOWIT | toggle debug animation flag |
| ATTESTIT / ATTESTXX | toggle automated random-input test bots (see §8.4) |
| DONALDTRUMP | +10 million (net) |
| MEGARICHMAN | +1 billion (net) |
| RICHENEMY | +10 million for every other player (net) |
| MOREGLUE / MORESTENCH | fill empty item slots with glue / stink bombs (net) |
| THINKPAD | laptop (best quality, full battery) in item slot 6 (net) |
| NODEBTS | credit set to 0 (net) |
| FAMOUS | image set to maximum (net) |
| BUBBLEGUM | toggle bubble cheat flag |
| SHOWALL | toggle "show all" flag |
| WINNING | immediately fulfil the current mission's target for the local player |
| ATWINNING | toggle "mission cheat" (enables keys 1–4) |
| COFFEECUP | debug: set sabotage trust/flags |
| ATMISSALL | unlock all classic + add-on missions (refreshes new-game screen) |
| EVOLUTIONCOMPLETE | unlock all "Airline Tycoon First Class" missions |
| EXPANDER | expand the airport (single-player, few check-ins) |
| ATMISSNONE | reset mission unlocks |
| CROWD | +200 people at the airport (single-player) |
| ATPROT | debug copy-protection timer reset (debug builds) |

---

## 8. Painting order and room-leave behaviour

### 8.1 Per-frame order (typical room) **H** (sequence) / **M** (frame boundary)
1. Room `OnPaint` → `CStdRaum::OnPaint(FALSE)`:
   a. deferred click replay (from bubble blending or phone pane);
   b. tip type reset to NONE;
   c. background bitmap → room bitmap;
   d. phone pane: advance slide; caller: delete fully slid-out pane;
   e. ambient sound triggers.
2. Room draws its animations/objects into the room bitmap.
3. `InitToolTips` (reset cursor/target, status bar & dialog hotspots), then room hotspots
   (`SetMouseLook` / `CheckCursorHighlight`) — last writer wins.
4. `CStdRaum::PostPaint` (skipped entirely for phone panes):
   a. call charges;
   b. phone pane composited into room bitmap;
   c. highlight check, number window, **speech bubble** into room bitmap;
   d. **room bitmap → back buffer**;
   e. message pump (input processed mid-frame), re-validation (window may have been destroyed);
   f. live-updating menus repaint (toilet, flights);
   g. advisor/portrait while **sliding** (only if room height ≤440);
   h. **status bar** (if refresh pending; only if room height ≤440) incl. hover highlights;
   i. competitor portrait + thinking icon;
   j. **menu** bitmap with zoom / unzoom animation; menu post-paint; calculator;
   k. advisor/portrait **when settled**, with its speech bubble and smiley;
   l. network **chat overlay** (top-left);
   m. room **tip card**;
   n. test-bot input; AI-competitor dialog timeout; voice auto-advance; delayed voice start.
5. Room may draw extra overlays (e.g. bank share-price card), then `PumpToolTips` (text highlight
   recolour, tutorial glow).
6. `GameFrame::OnPaint`: tooltip box, software cursor, crossfade step, flip; pause overlay.

Implication for OpenAT layering (bottom → top): room background → room content → phone pane →
number box → dialog bubble → (back buffer) → sliding advisor → status bar → competitor portrait →
menus/calculator → settled advisor + bubble → chat overlay → tip card → hotspot text highlight /
glow → tooltip box → cursor → fade/pause.

### 8.2 Status bar refresh rule ("speedy bar")
The status bar is only redrawn while `StatusCount > 0` (decremented per draw), during a crossfade,
or with the command-line switch `/nospeedybar`. Anything that changes it calls `UpdateStatusBar()`
(sets the counter to at least 3). Because the room bitmap is only 440 px tall, the bar's pixels
survive between redraws. **OpenAT: simply redraw every frame.** **H**

### 8.3 Leaving a room
- **Exit hotspot**: each room marks one or more areas with CURSOR_EXIT and click id 999 (only when
  no dialog/menu is open); a left click calls `LeaveRoom`. **H**
- **Right click** anywhere in the room area (y < 440) with no dialog and no menu → `LeaveRoom`.
  With a dialog open the right click aborts the dialog instead; with a menu open it closes the menu
  (§6.5). **H**
- **Esc** does *not* leave a room; it opens the options screen (§7.2). **H**
- **Hotkeys** leave all rooms and walk to the target (§7.2).
- Exit hotspot rectangles (inclusive, room coordinates) for reference — per-room documents are
  authoritative: **[W20]**

| Room | Exit rect(s) |
|---|---|
| Arab Air | (0,232)-(144,398), (0,288)-(314,380), (0,351)-(639,439) |
| Aufsicht | (64,18)-(172,240) |
| Bank | (62,64)-(173,186) |
| Designer | (296,240)-(361,337) |
| Duty-free | (493,52)-(570,224) |
| Editor | (602,192)-(640,224) [id 998], (602,158)-(640,188) |
| Fracht | (620,0)-(639,439), (440,357)-(639,430) |
| Globe | (495,241)-(640,440), (540,0)-(640,320) |
| Kiosk | (560,0)-(639,439) |
| Laptop | (37,398)-(74,439) |
| Last minute | (560,0)-(640,440) |
| Makler | (55,106)-(196,405) |
| Museum | (0,0)-(233,227), (533,312)-(605,352) |
| NASA | (195,373)-(424,440) |
| Personnel | (20,0)-(183,293) |
| Ricks | (0,0)-(70,439), (570,0)-(640,225) |
| Route box | (444,380)-(640,439) |
| Security | (4,70)-(112,412) |
| Tafel | (580,369)-(640,423) |
| Werbung | (0,145)-(70,381) |
| Werkstatt | (0,0)-(50,439) |
| Reisebüro | (0,0)-(80,440) |
| Sabotage | (86,98)-(209,439) |
| Insel / WeltAll / Rushmore / Statistics / Plane props | room specific (see those documents) |

(Demo builds turned the whole room into an exit; ignore.)

### 8.4 Test bots (`CheatTestGame`, `CStdRaum::PostPaint`) **H**
Debug feature toggled by cheats: periodically generates random clicks/right-clicks/cursor jumps or
sends the player to a random room. Not needed for OpenAT except as a soak-test idea.

---

## 9. Status bar

### 9.1 Assets
All from `gli\glstd.gli` (loaded at start-up, `Init.cpp: InitStatusLines, InitItems, InitTipBms`):

| Bitmap(s) | Use |
|---|---|
| `STATLIN0` | left part of the bar (logo/clock/date/money background), drawn at (0,440) |
| `STATLIN6` | right end piece with the two icons' background, drawn right-aligned at (640−w, 440) |
| `STATLIN5` | empty item slot tile |
| `STATLIN1..4` | variants used by the airport editor modes (airport document) |
| `CLOCK0..3` | clock icon per game speed 0..3 |
| `OCLOCK0..7` | network clock overlays: +4 = other human's speed (drawn translucent), +8… own speed marker (see 9.2) |
| `ITEM00..ITEM25` | inventory item icons (item id = index) |
| `LOGO0..LOGO6` | airline logos (player's logo index) |
| `REPEAT01/02/03` | "repeat last message" icon: normal / hover / pressed |
| `STAT01/02/03` | options (main menu) icon: normal / hover / pressed |
| `TUT01/02/03` | tutorial cancel button: panel / hover / pressed |
| `TIMEHIGH`, `GELDHIGH` | hover highlight for date-time row / money row |
| font `FontCash` | date/time and money text |

### 9.2 Layout (y relative to the screen; bar top = 440) — `CStdRaum::PostPaint` **H** [W4]
| Element | Position / size | Content |
|---|---|---|
| Bar left part | `STATLIN0` at (0,440) | |
| Bar right end | `STATLIN6` at (640−w₆, 440) | |
| Logo | (7,443) | `LOGO<n>`; click → statistics |
| Clock | (79,440) | `CLOCK<speed>`. Network: for each remote human still playing, `OCLOCK(speed+4)` translucent; then `CLOCK(min speed of all humans)`; then `OCLOCK(own speed+8)` — shows effective vs. own requested speed. **M** |
| Text block | 230x40 bitmap at (102,440) | line 1 at (8,2): weekday name (`std_ger.res "Schd"` 3000 + (start weekday + day) mod 7) + two spaces + time string; line 2 at (8,21): money with thousands separators; ≥ 1 000 000 000 shown as millions + unit suffix (`ein_ger.res` group 1000, id 1000+EINH_MIO). Re-rendered only when time or money changed. |
| Date/time hover | `TIMEHIGH` at (78,440) when cursor in (78,440)-(241,459) | only without dialog/menu |
| Money hover | `GELDHIGH` at (78,461) when cursor in (78,460)-(241,479) | only without dialog/menu |
| Item slots | 6 slots, slot i at (248 + 60·i, 440) | item icon or `STATLIN5` |
| Tutorial panel | `TUT01` at (640−w₆+1−wTUT, 440) while the tutorial runs | hover/press graphics at +8,+11 |
| Repeat-message icon | `REPEAT01` at (640−w₆+1, 441) — only if a previous advisor message exists | hover `REPEAT02`, press `REPEAT03` (400 ms) |
| Options icon | `STAT01` at (640−w₆+1, 461) | hover `STAT02` (only after 9:00, no dialog/menu, not loading), press `STAT03` (400 ms) |

Status bar is not drawn for rooms whose background is taller than 440 (full-screen rooms). **[W3]**

Manual cross-check (manual PDF p.54 screenshot): left to right — airline logo; clock icon (top row)
above a dollar-sign icon (bottom row, part of the `STATLIN0` art, clickable as part of the money
row); weekday + time (top row) and balance (bottom row); six item slots; at the far right two
stacked icons (top = repeat last info text, bottom = main menu/options). Matches the code layout.

### 9.3 Interactions (left click) — `CStdRaum::OnLButtonDown` **H**
Checked in this order:

| Area | Condition | Effect |
|---|---|---|
| Clock (78..101, 440..459) | not test-bot | game speed +1 (wraps 3→0); after day 2 gives the game-speed hint; network sync; refresh |
| Money (80..242, 460..479) | not editor | open bank-statement menu (`MENU_KONTOAUSZUG`), zooming from (100,470), min zoom 0.17 |
| Tutorial button (569..607, 440..479) | tutorial active, no dialog/menu | skip tutorial: advance/finish assistant messages, end tutorial, remove glow markers; if at 9:00 in the first tutorial steps, start the day (leave room) |
| Right icons x ≥ 610, lower half (y ≥ 460) | no dialog/menu, not loading, after 9:00 | enter options room (shows pressed icon) |
| Right icons, upper half | a last message exists | re-queue the last advisor message (as urgent unless it was a comment), pressed feedback |
| Items (x ≥ 256, < 634) slot = (x−250)/60 | slot < 6, time running | use item (9.4) |
| Logo (x < 78) | not test-bot, no dialog/menu, not loading, after 9:00 | toggle statistics room (enter, or leave if queued) |

Right click: §6.5 (clock −1, item → discard confirmation).

Open question: the manual says holding the mouse on the date/time display accelerates time; no such
code exists in this layer (Q3).

### 9.4 Inventory item use (left click on a slot) — `CStdRaum::OnLButtonDown` **H**
"Gift" = if the room has a default dialog partner, start a dialog with `DialogPar1 = 10000 + item`
(partner's reaction line; dialogue document). Items are removed with `0xFF` (empty) and the slots
re-packed (`ReformIcons`).

| Id | Item | Effect |
|---|---|---|
| 0 | Laptop | open laptop room (no dialog/menu) |
| 1 | Mobile phone | in own office: office phone animation; elsewhere: open phone book (`MENU_FILOFAX` mode 2) zooming from (248+60·slot, 446) |
| 2 | Filofax/notebook | open filofax (mode 1, "warp to known room") |
| 3 | Machine gun | in Arab Air: give (trust ≥1), dialog opening 1; else gift |
| 4 | Beer | in workshop: give (mechanic trust, calms him), dialog opening 2; else gift; else drink (`gulps.raw`, drunk +400) |
| 5 | Cigar | nothing |
| 6 | Oil | in globe room: oil the globe; else gift |
| 7 | Postcard | in own personnel office: give, dialog opening 300; else gift |
| 8 | Pills | if sick: cure; else gift |
| 9 | Spider | in sabotage room: give if trusted, dialog 3000; else gift |
| 10 | Dart | in Werbung (normal+ difficulty): give, dialog 8001; else gift |
| 11 | Diskette | if laptop has a virus: cure (leaves laptop room); else gift |
| 12 | Bra | in duty-free: give, dialog 801; else gift |
| 13 | Horseshoe | in Ricks: give to drinker, dialog 800; else gift |
| 14 | Pralines | gift; else eat (`eating.raw`) |
| 15 | Spiked pralines | gift; else eat, drunk +400 |
| 16 | Paper clip | in cargo room (glue not yet obtained): trade, dialog 1020; else gift |
| 17 | Glue | at airport upper level: place glue sabotage at own position (network); lower level → "not here" request; else gift |
| 18 | Glove | gift only |
| 19 | Energy drink | at kiosk: give, dialog 1010; else gift; else drink (`gulps.raw`, caffeine boost) |
| 20 | Stink bomb | at airport lower level: drop (network); upper → "not here"; else gift |
| 21 | Glowing coal | in last-minute / travel agency: burns all offered contracts (`fireauft.raw`); else gift |
| 22 | Coal | gift only |
| 23 | Pliers | gift; else at airport next to the security door: cut power (security off 3 days, network) |
| 24 | Perfume | in workshop after a slime event: converts to item 25; else gift |
| 25 | Perfume (modified) | at airport: player stinks (2000 ticks) |

In the kiosk, while today's headlines are "interesting", most item clicks are ignored. **H**

---

## 10. Advisor messages, portraits and chat overlay (notification layer)

### 10.1 Advisor queue — `Misc.cpp: CMessages` **H**
- 15-entry queue of (advisor type, text, urgency, mood). Types: assistant girl (0), personnel (1),
  kerosene, route, contract, money, info, plane (2..7 share one generic figure), fitness/security
  (8, 9 with gender variant); ≥100 = the player's own portrait in dialog mode.
- Urgency: NORMAL appended; URGENT inserted at the front; COMMENT replaces an existing comment of
  the same advisor (empty comment text dismisses it).
- Suppressed when the assistant option (girl) / advisor option is off; only queued between 9:00 and
  18:00 while the day runs; duplicates ignored.
- Display: the figure slides up from y=440 by 9 px per tick to 440 − slide height (per bitmap:
  girl 126, generic 142, security 120, players 115/115/120/106, phone variants 114/122/120/111),
  talks for 10 + 2·length ticks (mouth frames cycle; blinking every 30–180 ticks), then the next
  message of the same advisor follows or it slides down. Clicking the figure (560,320)-(640,440) or
  its bubble skips. The last shown message can be repeated via the status bar icon.
- Bitmaps: `gli\glberatr.gli` `BERA1_01..04` (girl), `BERA2_..` (generic), `BERA8_/BERA9_` (security
  f/m), `BER91..94_01..08` (player portraits P1..P4; frames 5–8 = with phone). Speech bubbles
  `glstd.gli` `BLASE0..5` (3 sizes plain, 3 sizes with smiley space; text rects
  (15,6)-(266,16), (19,8)-(266,31), (32,8)-(308,51), (30,9)-(498,64), (49,8)-(296,31),
  (62,8)-(338,51)); smiley `SMILEY0..3` at +(5,7) or +(7,18). Font `FontSmallBlack`.
- Placement: advisors at the **right** edge (x = 640 − width), bubble to the left of the figure
  using `BeraterSprechblasenOffset` (girl (26,56), generic (10,84), security (26,56)); the player
  portrait at the **left** edge (x = 0), hidden in the own office (large Smacker figure there) or
  when "don't display player" is set. **[W6]**
- Advisors are not drawn in full-screen rooms (height > 440).

### 10.2 Player portrait in dialogs
During any dialog the player's portrait slides in at bottom-left; its mouth animates while the
player's text is "spoken" (talk counter from §4.4, or voice mouth detector). On the phone the phone
variant frames are used. The other player in a competitor dialog is shown mirrored at the right
edge (`BER9x` of that player, y = 440 − slide), animating while their line plays. **H**

### 10.3 Network chat overlay — `AtNet.cpp: DisplayBroadcastMessage/PumpBroadcastBitmap` **H**
- Network games only. Each incoming line (prefixed with the sender's name) is appended to a 320 px
  wide bubble-white panel (font `FontSmallBlack`, 10 px margin), max height 220 (oldest lines
  scroll off); sound `netmsg.raw` (throttled to 1 per 500 ms) for others' messages.
- After a 60-tick timeout the panel shrinks by 1 px every 3 pumps (every pump when ≤10 or ≥150 px
  tall), dropping the top line pixels; when < 10 px tall it slides out towards the upper-left.
- Drawn with white-transparency at (10,10). **[W16]**

---

## 11. GameFrame

### 11.1 Window — `GameFrame::GameFrame/~GameFrame` **H**
- Requires a 16-bit desktop in windowed mode (otherwise error box and exit; message text not
  reproduced). Forces fullscreen if the desktop is ≤640x480.
- Fullscreen: borderless topmost popup 640x480. Windowed: captioned, minimisable, client 640x480,
  centred.
- Creates back buffer + secondary page (640x480), software cursor; loads `gli\glbasis.gli`
  cursors; clips the cursor to (2,2)-(638,478) in fullscreen; initial look NORMAL.
- Destructor releases all shared bitmaps and ends the game loop.
- `Mainfrm.cpp`: contains only a disabled DirectDraw page-flip sample (commented out) — no
  behaviour. **H**

### 11.2 Fades — `GameFrame::PrepareFade`, `SBBM.cpp: SBPRIMARYBM::Flip` **H/M**
- `PrepareFade` copies the current back buffer into the fade bitmap (old picture).
- Room change arms state −2 → at the next flip state 8: the newly rendered frame is captured as the
  "new" picture and the old one is shown; states 7..1: blend old·(s/8) + new·((8−s)/8); state 0:
  free both. ⇒ an **8-frame crossfade** from old to new screen. Only with the "fade" option, not for
  the laptop, after 9:00 or on day 0. The status bar is redrawn during the fade. **[W13]**

### 11.3 Pause — `GameFrame::Pause`, `GameFrame::OnPaint`, `SBPRIMARYBM::Flip` **H**
- Triggers: Pause key (if not disabled, while playing; network peers informed), app deactivation.
  Any left click (`DefaultOnLButtonDown`) or re-activation unpauses.
- Pause stops the simulation, pauses MIDI (resumes and restores volume on unpause).
- Visual: on first paused frame snapshot the screen; then 7 frames blending snapshot → pause
  picture (`room\pause.gli`, bitmap `PAUSE`, full-screen); then the pause picture stays. After
  unpausing, the flip blends the pause picture back out over the same number of steps and releases
  `pause.gli`. Without the fade option the picture just appears. **[W14]**

### 11.4 Activation — `GameFrame::OnActivateApp` **H**
- Activate: notify network peers, re-clip cursor (fullscreen), restore lost surfaces and airport
  brick graphics, refresh status bar, unpause, bring to front, repaint.
- Deactivate: flush talker state, notify peers, unclip cursor, pause.

### 11.5 OnCommand / misc
- `OnCommand`: forwards command messages to all child windows. MIDI end notification → next song.
  `OnEraseBkgnd` does nothing. **H**
- `MessagePump()`: processes all pending window messages (called from many places mid-frame;
  OpenAT: not needed in an event-driven engine but beware of re-entrancy assumptions — after a pump
  the room may have been destroyed). **H**

---

## 12. Open questions

- **Q1** Widescreen: should advisors / player portrait / competitor portrait anchor to the screen
  edges or to the 640 room canvas? (Canvas keeps their relation to the room art and bubbles.)
- **Q2** Exact sizes of `XBLASE01..14`, `TOOLTIP1..3`, `STATLIN0/5/6`, `DIALOG`, `NUMBERS` — read
  from `glstd.gli`/`glnumber.gli` to confirm the bubble interior is part of the edge pieces and the
  slanted phone-pane frame look.
- **Q3** Manual: "hold the mouse on the date/time display to accelerate time" — not found in this
  layer; may be elsewhere (sim loop) or removed in Deluxe.
- **Q4** Network clock overlay semantics (`OCLOCK` index +4 / +8) inferred; verify against bitmaps.
- **Q5** Where "long-distance call" (`Ferngespraech`) is set TRUE (world/foreign calls?) — affects
  call charge interval only.
- **Q6** Blend ratio of the white-transparent bubbles (assumed 25 % background / 75 % white from blend
  table indices 2 and 6 of 8) — verify visually.
- **Q7** Player bubble in the own office: the tail entry point does not lie on the bubble edge, so
  probably no tail is drawn there — verify in game.
- **Q8** Item slot drawing starts at x=248 but hit-testing uses (x−250)/60 and the 40 %-width rule
  (x ≥ 256): the first 8 px of slot 0 are not clickable in the original. OpenAT may use the drawn
  rects.
- **Q9** `gStatButton=3` is set by a logo click but displays the *options* icon's pressed state —
  likely an original quirk; decide whether to replicate.
