# Rooms & UI 8 — Special rooms, Statistics, Evening screen

Clean-room behaviour spec for OpenAT. Written from reading the original source (gog_src), the SDL
port (ref_AirlineTycoon, behaviour identical for everything below unless noted), the English manual
and the installed game files. No code is reproduced. In-game text is referenced only by resource
file + token + id.

Scope: Rick's Café, Petrol Air (Arab Air) counter, Sabotage room, Safe & Secure (Security), NASA shop,
Island telescope (Insel), Mount Rushmore telescope, Space telescope (WeltAll), World (branch phone
call), Statistics screen, Evening / day-end screen (Abend), plus a short classification of
Checkup.cpp, SecurityManager.cpp, Askbrick.cpp and Brick.cpp.

---------------------------------------------------------------------------------------------------

## 0. Shared conventions (read first)

### 0.1 Coordinate space
* Every room is a 640x440 picture ("room bitmap"); the 640x40 status bar lives at y 440..479 and is
  owned by the standard-room base (another writer). All coordinates below are in this 640x480
  space with origin top-left.
* Rectangles are written `(x1,y1)-(x2,y2)` and are **inclusive on both ends** (the original
  hit test is `x>=x1 && y>=y1 && x<=x2 && y<=y2`).
* **WIDESCREEN** markers flag layout that assumes a 640-wide / 440-high room. OpenAT should treat
  each room as a 640x440 "canvas" that is centred (or pillarboxed) in the wider viewport, unless
  the item says the room could naturally extend (the three telescope rooms are panoramas and are the
  natural candidates for showing more of the panorama).

### 0.2 Common room behaviour (applies to every room below unless stated)
Owned by the standard-room base class (`Stdraum.cpp`), listed so this document is self-contained:
* On construction: mark room visited for the player (`SetRoomVisited`), clear the focused airport
  person, set the ambient-sound global volume to a per-room value (only when not a handy/phone view),
  register with the room's talker (reference count) and set the default dialog partner.
* Each frame: default cursor = normal; then hotspots are evaluated in order and the **first match
  wins**; hotspots are only active when no dialog and no menu is open.
* Left click: base pre-processing first (status bar, items, open menus/dialog). If not consumed,
  the room checks the hotspot id remembered from the last hover (area = room id, id = number).
* Right click inside the window: if a menu is open → menu right-click handling; else if no dialog
  open and y<440 → **leave room**; then base handling (which also ends an open dialog).
* "Leave room" = return to the airport hall (player walks out of the door).
* Hotspot id 999 is always "exit", shown with the EXIT cursor.
* Hint system: some rooms call "give hint X" each frame after a given day; the hint system shows a
  one-time advisor tip (owned by another writer).

### 0.3 Smacker character ("SmackerPerson") model
Characters are made of **clips**; each clip is one `.smk` (in `video/`) drawn transparently at a
fixed room position. Each clip has:
* `mood` – IDLE, TALKING, LISTENING (the dialog system sets the desired mood: partner talks →
  TALKING, player talks → LISTENING, no dialog → IDLE).
* `repeat` min..max – how many times the clip loops before choosing a successor.
* `postwait` min..max – pause after the clip, in 1/20 s, while the last frame stays visible.
* `cancel` – whether the clip may be interrupted when the desired mood changes.
* successor list – pairs (token, weight 0..9, target clip). Token **A** = allowed always,
  **E** = only used to *exit* the current mood towards the desired mood, **S** = only when staying in
  the mood. Next clip = weighted random among candidates whose target mood equals the desired mood
  (A/S candidates preferred while staying, E/A when switching).
* optional `decision variable`: if it holds a value other than -1, that value is the next clip id
  (overrides random choice) – used for scripted reactions (e.g. NASA button press).
* optional `post variable`: a variable set to a value at clip **start** (PRE), clip **end** (POST) or
  at a given **frame** – used to trigger other animations.

In the tables below, successor lists are written like `A9→0, A1→1, E1→2` (token+weight→target).
The full engine semantics are owned by the animation-framework writer; this document only lists the
data.

### 0.4 Simple frame animations ("CAnimation")
Parameters given as: bitmap prefix + frame count, sound, start-with-first-frame-visible flag,
mode (ONCE / REPEAT / RANDOM / NEVER = only when started explicitly), prelude, speed, cyclic wait,
repeat-core. Exact timing semantics are owned by the animation-framework writer.

### 0.5 Text resources
* `Std_<lang>.res` (misc/) holds standard texts: tokens `Tool` (tooltips), `Misc`, `STAT`, `Advi`
  (advisor), `Nasa`, `Welt`, …
* `Dlg_<lang>.res` holds dialog texts (tokens `Arab`, `Sabo`, `Secu`, `Nasa`, `Rick`, …).
* Tooltip ids given as numbers below are ids under token `Tool`.

---------------------------------------------------------------------------------------------------

## 1. Rick's Café (ROOM_RICKS = 84)

Traceability: `Ricks.cpp: CRicks::CRicks, OnPaint, OnLButtonDown, OnRButtonDown`;
`Gameframe.cpp` (hotkey C); `Player.cpp: WalkToRoom` (no closing time).
Confidence: high.

**Access:** always open; reachable from the airport hall (hotkey `C`). Robots visit via
ACTION_VISITRICK.

**Background:** `room/rick.gli`, bitmap `RICK` (full 640x440).

**Ambient:** global ambient volume 50. If sound effects on: random background FX slot 0
`Kaffee.raw` (mean pause 40000 ms, random range 27000) and slot 2 `toilet.raw` (120000 / 80000,
volume 40).

**Hint:** from game day 5 on (date > 4), hint HINT_RICKS is offered; the hint flag HINT_RICK is
invalidated on entry.

**Characters** (all smk in `video/`):

Rick (barkeeper), position (157,130), talker TALKER_RICK:

| clip | file | mood | repeat | postwait | cancel | successors |
|---|---|---|---|---|---|---|
| 0 | Bar_Wait.smk | IDLE | 1 | 0 | yes | A9→0, A1→1, E1→2 |
| 1 | Bar_Aug.smk (eye roll) | IDLE | 1 | 0 | no | A9→0, A1→1, E1→2 |
| 2 | Bar_Rede.smk | TALKING | 1 | 0 | yes | A9→2, A2→3, A2→4, E1→5, E1→0 |
| 3 | Bar_Fing.smk | TALKING | 1 | 0 | yes | A9→2, A2→4, E1→5, E1→0 |
| 4 | Bar_Arm.smk | TALKING | 1 | 0 | yes | A9→2, A2→3, E1→5, E1→0 |
| 5 | Bar_Wait.smk | LISTENING | 1 | 0 | yes | A9→5, A2→6, E1→0, E1→2 |
| 6 | Bar_Aug.smk | LISTENING | 1 | 0 | no | A9→5, A2→6, E1→0, E1→2 |

Neon sign: `Neon.smk` at (514,0), IDLE, repeat 99, loops on itself.

The drinker at the bar (talker TALKER_TRINKER) is two independent overlays:
* Eyes at (479,199): clip 0 `AugW.smk` (postwait 10..90) A9→0, A9→1; clip 1 `Aug.smk` A9→0, A9→1.
* Mouth at (483,230):

| clip | file | mood | postwait | successors |
|---|---|---|---|---|
| 0 | Mund_W.smk | IDLE | 10..90 | A9→0, E1→5, E1→6 |
| 1 | Mund.smk | IDLE | 0 | A9→2, E1→5, E1→6 |
| 2 | MundA.smk (drinks) | IDLE | 0..30 | A9→3 |
| 3 | MundAW.smk | IDLE | 0 | A9→4 |
| 4 | MundZ.smk | IDLE | 10..90 | A9→0 |
| 5 | Mund.smk | TALKING | 0 | A9→5, E1→6, E1→0 |
| 6 | Mund_W.smk | LISTENING | 10..90 | A9→6, E1→5, E1→0 |

(Note: clip 1 is only reachable through no successor list – the drinking chain 1→2→3→4 is
effectively dead in IDLE; open question whether intended.)

Draw order: background, neon, Rick, drinker mouth, drinker eyes.

**Hotspots** (first match wins):

| rect | cursor | id | click |
|---|---|---|---|
| (0,0)-(70,439) or (570,0)-(640,225) | EXIT | 999 | leave room |
| (159,131)-(302,346) | HOT | 10 | start dialog TALKER_RICK, medium "air" (face to face), start id 0 |
| (466,141)-(639,439) | HOT | 11 | start dialog TALKER_TRINKER, start id 0 |

No tooltips. Right click leaves. No menus opened by the room itself (dialog content: Dialog.cpp
TALKER_RICK / TALKER_TRINKER, other writer).

**WIDESCREEN:** exit zones touch x=0 and x=640; when pillarboxed keep them relative to the canvas.

---------------------------------------------------------------------------------------------------

## 2. Petrol Air counter (Arab Air, ROOM_ARAB_AIR = 104) – kerosene & tanks

Traceability: `ArabAir.cpp: CArabAir::CArabAir, OnPaint, OnLButtonDown`;
`Player.cpp: PLAYER::WalkToRoom` (closing); `AirportView.cpp` (open time constant);
`Dialog.cpp` TALKER_ARAB branch (reference). Confidence: high.

**Access / opening hours:** closed before 11:00 game time and on weekday index 6 (the 7th weekday,
Sunday) – walking there opens MENU_CLOSED with sub-id MENU_CLOSED_ARAB_AIR instead. Hotkey `A`.

**Background:** `room/arab.gli`, bitmap `ARAB_AUS`. The GLI also holds `FUNK00..FUNK06` (a sparkle
animation, disabled in the shipped code) and `GLOVE` (item picture).

**Sound:** ambient volume 60. If effects on: one-shot `arab.raw` on entry (stopped when leaving);
`radio.raw` prepared for the radio hotspot.

**Kerosene advisor on entry:** if the player owns a kerosene advisor:
* if world kerosene price < 400 **and** (tank contents × 2 < tank capacity × 4 – always true for a player
  who owns a tank, but **false for a player without any tank** (0 < 0), so a tankless player never hears
  "buy now"; corrected, an earlier version called it practically always true) **and** money > 20000 →
  advisor message `Advi` 3000 (buy now);
* else if price > 600 → advisor message `Advi` 3001 (don't buy).

**Hint:** from day 6 (date > 5) hint HINT_ARABAIR.

**Character:** the Petrol Air clerk, talker TALKER_ARAB, all clips at (425,108):

| clip | file | mood | postwait | cancel | successors |
|---|---|---|---|---|---|
| 0 | ArabW.smk (wait) | IDLE | 0 | yes | A9→0, E1→1 |
| 1 | ArabA.smk (wait→speak) | IDLE | 0 | no | E1→2, E1→4, E1→6 |
| 2 | ArabL.smk (listen) | LISTENING | 50 | yes | A9→2, E5→4, E5→6 |
| 3 | ArabP.smk ("psst") | TALKING | 20 | yes | A9→4 (unreferenced) |
| 4 | ArabS.smk (speak) | TALKING | 0 | yes | A9→4, E1→2, E1→6 |
| 5 | ArabZ.smk + sound ArabZ.raw (speak→wait) | IDLE | 0 | no | A9→0 |
| 6 | ArabG.smk (look around) | IDLE | 0 | no | A9→5 |

**Kerosene price chart on the wall:** each frame a small line chart of the kerosene price history
(the sim's past-price series) is drawn in red (0xff2020) into a skewed quad with corners
TL (313,136), TR (371,146), BL (313,166), BR (371,176); auto-scaled. Chart drawing routine is shared
with the bank (other writer).

**Glove item:** if the global "glove still lies around" flag is set and the player does not own
ITEM_GLOVE, bitmap `GLOVE` is drawn at (314,280).

**Hotspots:**

| rect | cursor | id | click |
|---|---|---|---|
| (0,232)-(144,398) or (0,288)-(314,380) or (0,351)-(639,439) | EXIT | 999 | leave |
| (314,280)-(386,342), only if glove present & not owned | HOT | 12 | take glove: player "buys" ITEM_GLOVE (needs a free item slot); on success clear the global glove flag and broadcast "thing taken" (net) |
| (437,89)-(600,323) | HOT | 10 | dialog TALKER_ARAB, start id 0 |
| (313,136)-(371,176) (price chart) | NORMAL | 0 | tooltip `Tool` 4500 formatted with current kerosene price; shown immediately (tooltip delay skipped) |
| (179,215)-(242,261) radio – **click only, no cursor change** | – | – | plays `radio.raw`. Tested before the other click handling, so it fires in addition to whatever else is hit. |

**Dialog (reference only, Dialog.cpp TALKER_ARAB):** main choices: general info; kerosene market
(shows 3 price grades = 2x, 1x, 0.5x current price; player picks grade, then amount via
MENU_BUYKEROSIN with the chosen grade); storage tanks (choose tank type, then quantity 1/2/3/5/10,
charged and added to tank capacity). Kerosene advisor comments on grade choice (`Advi` 3020+grade).

**WIDESCREEN:** the bottom exit band (0,351)-(639,439) spans the full width.

---------------------------------------------------------------------------------------------------

## 3. Sabotage room – the saboteur in the basement (ROOM_SABOTAGE = 112)

Traceability: `sabotage.cpp: CSabotage::CSabotage, ~CSabotage, OnPaint, OnLButtonDown`;
`AirportView.cpp: AIRPORT::PumpDoors` (secret door); `Sim.cpp` (~line 1745, sabotage call-back);
`Dialog.cpp` TALKER_SABOTAGE (reference). Confidence: high for room, medium for ArabMode 6.

**Access:** via a secret door in the airport (a fire extinguisher on the wall rotates inward when
the room is entered and back out when it is free – animated by the airport, not this room).
The room is not on a hotkey. In the DEMO build entering shows MENU_REQUEST /
MENU_REQUEST_NO_SABOTAGE (zoom from (320,220), 0.17) and everything is an exit.

**Also opened as phone window:** when a sabotage the player ordered has been executed and the player
is *not* in this room, a phone ring (`Phone.raw`) plays and this room is created in "handy" mode as
a dialog window (the saboteur calls). If the player *is* in the room, dialog TALKER_SABOTAGE id 2000
starts directly. Game speed is set to 0 in both cases.

**Background:** `room/sabotage.gli`, bitmap `SABOTAGE`. Other GLI bitmaps: `S_BASE16`
(saboteur's empty chair/body base), `OHNEDART` (dartboard without dart), `PINCERS`,
`DAMPF00..07`, `FEUER00..02`, `AUGE00..06`, `RAUCH00..10`.

**Sound:** ambient 40. If effects on: looping `SaboBack.raw`. One-shot `morse.raw` on entry
(universal FX). `zisch.raw` (steam hiss) and `lunte.raw` (fuse) bound to animations.

**Frame animations:**

| anim | frames | sound | pos | behaviour |
|---|---|---|---|---|
| steam DAMPF00 | 8 | zisch.raw | (423,0) | repeat, prelude 300, speed 3, cyclic wait 300 |
| dynamite fuse FEUER00 | 3 | lunte.raw | (287,386) | only created with 1/10 chance (or cheat); repeat, speed 2 |
| camel eye AUGE00 | 7 | – | (20,37) | mode NEVER: played once when triggered (see below), speed 7, wait 300 |
| lamp smoke RAUCH00 | 11 | – | (344,299) | repeat, speed 5 |

**Static overlays:**
* `OHNEDART` at (148,42) when the player owns ITEM_DART **or** ITEM_DISKETTE **or** has advertising
  trust (i.e. the dart has already been taken) – hides the dart.
* `PINCERS` at (369,188) while the global "pincers lie around" flag is set.
* `S_BASE16` at (377,194) whenever the saboteur is in any clip other than 0, 1, 10 (i.e. he has
  turned round – the base fills the gap left at his desk).

**Character:** saboteur, talker TALKER_SABOTAGE (12 clips):

| clip | file | sound | pos | mood | repeat | postwait | cancel | successors |
|---|---|---|---|---|---|---|---|---|
| 0 | S_clic.smk (typing, back turned) | – | (413,255) | IDLE | 1 | 0 | yes | A9→0, A4→10, E1→1 |
| 10 | S_clicB.smk | – | (413,255) | IDLE | 1 | 0 | yes | A9→0, E1→1 |
| 1 | s_turn.smk (turns round) | S_Turn.raw | (275,101) | IDLE | 1 | 0 | no | E1→2, E1→3 |
| 2 | s_rede.smk | – | (155,102) | TALKING | 8 | 0 | yes | A9→2, E1→3, E1→6 |
| 3 | s_wait.smk | – | (155,102) | LISTENING | 2 | 20 | yes | A9→3, A1→4, A1→5, E1→2, E1→6 |
| 4 | s_left.smk | – | (155,102) | LISTENING | 1 | 0 | no | A9→3, E1→2, E1→6 |
| 5 | s_rigt.smk | – | (155,102) | LISTENING | 1 | 0 | no | A9→3, E1→2, E1→6 |
| 6 | s_raus.smk (looks out) | – | (155,102) | IDLE | 1 | 0 | no | A9→7, E1→2, E1→3; at clip start sets "play camel eye" flag |
| 7 | s_wait.smk | – | (155,102) | IDLE | 1 | 20 | yes | A9→7, A1→11, A1→6, A1→8, A1→9, E1→2, E1→3 |
| 11 | S_WaitK.smk (blink) | – | (155,102) | IDLE | 1 | 1 | yes | A9→7 |
| 8 | s_left.smk | – | (155,102) | IDLE | 1 | 0 | no | A9→7, E1→2, E1→3 |
| 9 | s_rigt.smk | – | (155,102) | IDLE | 1 | 0 | no | A9→7, E1→2, E1→3 |

Behaviour: until first spoken to he types with his back to the player (0/10). On a dialog he turns
(1) and stays facing the player in an idle loop (7) afterwards. When clip 6 starts, the camel-eye
animation is started on the next paint.

**Hotspots (no menu/dialog open):**

| rect | condition | cursor | id | click |
|---|---|---|---|---|
| (86,98)-(209,439) | – | EXIT | 999 | leave |
| (138,9)-(211,120) dartboard | player has neither dart nor diskette nor ad-trust | HOT | 800 | dialog TALKER_SABOTAGE start id 800 (about the dart) |
| (376,106)-(546,440) and (288,227)-(631,323) | saboteur turned (clip not 0/1/10/none) | HOT | 10 | dialog TALKER_SABOTAGE start id 0 |
| (422,265)-(548,439) | saboteur still with back turned | HOT | 10 | same |
| (298,395)-(337,423) dynamite – click only, no cursor | – | – | – | removes the burning-fuse animation (stops it) |
| (384,204)-(426,263) pincers – click only, no cursor | pincers present | – | – | take ITEM_ZANGE (needs free slot); on success clear global flag, broadcast "thing taken" |

**While MENU_SABOTAGEPLANE is open:** hovering the list area (menu-relative (216,6)-(387,212)) maps
the y position to a row (rows 13 px high, first row at menu y+25, 13 visible rows, offset by the
menu page). If the row is a valid plane of the *victim* player, the menu's left panel is redrawn
with that plane's tip (plane details, small black font at (6,6)/(6,28)) and the row gets a cursor
highlight (red if the row's flag column is set, black otherwise). Leaving the list clears the tip.
(Menu itself: other writer.)

**Leave behaviour:** on destruction, if the local player's sabotage mode equals 6, the player is put
into ROOM_WORLD (phone-world view). See open questions.

**Dialog (reference, Dialog.cpp TALKER_SABOTAGE):** choose victim airline (1030..1032), then category:
plane sabotage (ArabMode 1..5, opens MENU_SABOTAGEPLANE), personal sabotage (ArabMode2 1..4) or
"special" (ArabMode3 1..6; 5 opens MENU_SABOTAGEPLANE, 6 opens MENU_SABOTAGEROUTE). Options
available grow with "Arab trust". Blocked if victim owns the matching security feature.

---------------------------------------------------------------------------------------------------

## 4. Safe & Secure Ltd. – Security office (ROOM_SECURITY = 123)

Traceability: `Security.cpp: CSecurity::CSecurity, OnPaint, OnLButtonDown`;
`Player.cpp: WalkToRoom` (closure); `Dialog.cpp` TALKER_SECURITY (lines ~509-535).
Confidence: high.

**Access:** hidden entrance in the airport (manual: "the entrance is secret"). Hotkey `Y`. Closed
(MENU_CLOSED / MENU_CLOSED_NOSECURITY, "power outage") while the sim's security-outage day counter
> 0. Robots: ACTION_VISITSECURITY / ACTION_VISITSECURITY2.

**SecurityManager.cpp is NOT this room** – it is the CD copy-protection / anti-debugging module
(VxD loader, decryption, CRC). It has no UI. Ignore for OpenAT.

**Background:** `room/security.gli`, bitmap `SECURITY` (only bitmap).

**Sound:** ambient 60; if effects on, looping `secure.raw` (stopped on leave).

**Characters / animated props** (all smk):

Security officer (Mr Fightworthy), TALKER_SECURITY, position (240,79) except clip 8 at (256,63):

| clip | file | mood | repeat | postwait | cancel | successors |
|---|---|---|---|---|---|---|
| 0 | SO_normal.smk | IDLE | 2..3 | 0 | yes | A1→0, A9→14, A1→7, A1→10, E1→1 |
| 1 | SO_zum_reden.smk (turn to talk) | IDLE | 1 | 0 | no | E1→2, E1→4, E1→6 |
| 2 | SO_reden_mund_zu.smk | LISTENING | 1 | 50 | yes | A9→2, E5→4, E5→5 |
| 3 | ArabP.smk (left-over, unreachable) | TALKING | 1 | 20 | yes | A9→4 |
| 4 | SO_reden.smk | TALKING | 1 | 0 | yes | A9→4, E1→2, E1→5 |
| 5 | SO_reden_zurueck.smk | IDLE | 1 | 0 | no | A9→0 |
| 6 | SO_erschrecken.smk (startled) | IDLE | 1 | 0 | no | A9→0 |
| 7 | SO_hindrehen.smk (turn to monitors) | IDLE | 1 | 0 | no | A2→8, A1→9, E1→9 |
| 8 | SO_druecken.smk (presses buttons) @ (256,63) | IDLE | 1 | 0 | no | A9→8, A9→9, E1→9 |
| 9 | SO_herdrehen.smk | IDLE | 1 | 0 | no | A1→0 |
| 10 | SO_tasse_nehmen.smk (takes cup) | IDLE | 1 | 0 | no | A1→11, A1→12, E1→13 |
| 11 | SO_tasse_trinken.smk | IDLE | 1 | 0 | no | A1→12, A1→13, E1→13 |
| 12 | SO_tasse_zittern.smk (cup shakes) | IDLE | 1 | 0 | no | A1→11, A1→12, A1→13, E1→13 |
| 13 | SO_tasse_hinsetzen.smk | IDLE | 1 | 0 | no | A1→0 |
| 14 | SO_normal.smk | IDLE | 1 | 120 | yes | A1→6, E1→1; at start sets "comm" variable = 1 |

Peephole spy ("Spion") at (85,327): clip 0 `SO_spion_pause.smk` (A9→0, A0→1; decision variable =
comm variable), clip 1 `SO_spion.smk` (A9→0; at start resets comm variable to 0). Effect: when the
officer enters clip 14 the comm variable becomes 1, the spy clip plays once, then the officer
(after his 6 s wait) is startled (clip 6). Comm variable initial value -1 (no forcing).

Door `SO_tuer.smk` at (38,145) loops. Safe: `SO_tresor_pause.smk` (postwait 20; A9→0, A2→1) and
`SO_tresor.smk` (repeat 1..3; A9→1, A3→0) at (528,198). Gas mask: `SO_gasmask_pause.smk`
(postwait 80; A9→0, A4→1) and `SO_gasmask.smk` (A9→0) at (468,89).

Surveillance monitors (each a ring of camera-view clips, all postwait 0..20, each "view" clip
followed by a "pan" clip with two weight-9 options, so they wander between views):
* Monitor 1 at (125,85): KamA12, KamA2, KamA23, KamA3, KamA34, KamA4, KamA45, KamA5, KamA56, KamA6,
  KamA67, KamA7, KamA71, KamA1 (14 clips, ring 1→7→1).
* Monitor 2 – KamB* at (125,85): constructed but **never drawn** (dead).
* Monitor 3 at (362,83): KamC12, KamC2, KamC23, KamC3, KamC31, KamC1.
* Monitor 4 at (348,277): KamD12, KamD2, KamD23, KamD3, KamD31, KamD1.
* Monitor 5 at (509,47): KamE12, KamE2, KamE23, KamE3, KamE31, KamE1.

Draw order: door, gas mask, monitors 1,3,4,5, spy, safe, officer.

**Hotspots:**

| rect | cursor | id | click |
|---|---|---|---|
| (4,70)-(112,412) | EXIT | 999 | leave |
| (288,144)-(401,267) | HOT | 10 | dialog TALKER_SECURITY start id 1000 |

(A glove hotspot id 12 exists in the click handler, copied from Arab Air, but no hover ever sets
it – dead.)

**Dialog (reference):** start 1000 → player choice (`Secu` 2000..2002): explanation / open
**MENU_SECURITY** (security feature purchase menu, other writer) / goodbye.

---------------------------------------------------------------------------------------------------

## 5. NASA shop (ROOM_NASA = 115)

Traceability: `Nasa.cpp: CNasa::CNasa, OnPaint, OnLButtonDown, OnRButtonDown`;
`Dialog.cpp` TALKER_NASA (~5181+); `Player.cpp` robot filters. Confidence: high.

**Access:** the shop only exists in the airport layout of the rocket missions (manual: "The Final
Frontier" DIFF_FINAL and "Beyond the Final Frontier" DIFF_ADDON10). Hotkey `N`. Whole room is
compiled out in the DEMO build.

**Background:** `room/nasa.gli`, bitmap `NASA`.

**Sound:** ambient 40; if effects on, looping `funk.raw` (radio chatter).

**Hint:** from day 6 hint HINT_NASA. (The cursor reset in paint uses the sabotage room id – harmless.)

**Characters:** NASA salesman, talker TALKER_NASA, all at (210,140):

| clip | file | mood | postwait | cancel | successors / notes |
|---|---|---|---|---|---|
| 14 | LE_Wait.smk | IDLE | 10 | yes | A9→14, A2→1, A1→2, A1→3, E1→6 (idle hub) |
| 1 | LE_WaitK.smk (blink) | IDLE | 0 | yes | A1→14 |
| 2 | LE_lock.smk | IDLE | 0 | no | A1→14 |
| 3 | LE_strck.smk (stretch) | IDLE | 0 | no | A1→4 |
| 4 | LE_strkW.smk | IDLE | 20..90 | yes | A1→5 |
| 5 | LE_strkZ.smk | IDLE | 0 | no | A1→14 |
| 6 | LE_turn.smk (turn to player) | IDLE | 0 | no | A9→7, E1→7, E1→9, E1→13 |
| 7 | LE_RedeW.smk | LISTENING | 10 | yes | A9→7, A2→8, E1→9, E1→10, E1→11, E1→13, A1→15; decision var = "tip now" |
| 8 | LE_RedeK.smk | LISTENING | 0 | yes | A9→7, E1→9, E1→13; decision var |
| 9/10/11 | LE_RedeA/B/C.smk | TALKING | 0 | yes | A4→9, A4→10, A4→11, E1→13, E1→7; decision var |
| 12 | LE_taste.smk (presses button) | LISTENING | 0 | no | A9→7, E1→9, E1→13; at start resets "tip now" to -1 |
| 15 | LE_taste.smk | LISTENING | 0 | no | A9→7, E1→9, E1→13; at **frame 6** sets "alien" variable = 1 |
| 13 | LE_TurnZ.smk (turn back) | IDLE | 0 | no | A9→14 |
| 0 | LE_RedeW.smk | TALKING | 2 | no | A1→7 (start clip variant) |

Display case at (445,175): clip 0 `Chick_.smk` (A1→0, decision var = alien), clip 1 `Alien_.smk`
(at start resets alien to -1; A9→0). So the chick in the case occasionally turns into an alien when
the salesman's rare button-press clip 15 hits frame 6.

**Purchase feedback:** whenever the dialog sells a rocket/station part it sets "tip now" = 12, so the
salesman plays the button-press clip next.

**Entry:** the dialog TALKER_NASA starts **automatically** on entry (start id 0).

**Hotspots:**

| rect | cursor | id | click |
|---|---|---|---|
| (195,373)-(424,440) | EXIT | 999 | if the player has never talked to NASA (per-player flag): set flag, start dialog id 2 (farewell/intro) and hide the player figure; otherwise leave |
| (197,140)-(398,377) | HOT | 10 | dialog id 1 |

Right click (no menu/dialog, y<440) behaves like the exit click (first time → dialog id 2).

**Dialog (reference):** mission-dependent menus `Nasa` 2000.. (DIFF_FINAL: rocket) or 2200.. (else:
space station); "buy" lists parts 5000..5003 (first stage: base/tower/arm) or 5100..5107 (rest).
Each purchase sets the corresponding bit in the player's rocket/station flags and charges
RocketPrices[...] (money reason 3400). Same dialog works by phone (medium handy, ids +100).

---------------------------------------------------------------------------------------------------

## 6. Telescope views (common entry)

Traceability: `Gameframe.cpp` (hotkey S / telescope rune click, ~1093), `Player.cpp` (robot
ACTION_VISITTELESCOPE, hint HINT_FERNGLASS), `Takeoff.cpp` (room creation, ~1559).

* The telescope stands at the upper-left of the airport hall (manual p.40). Hotkey `S`.
* Clicking it goes to:
  * **ROOM_INSEL** (rocket islands) if mission = DIFF_FINAL,
  * **ROOM_WELTALL** (space stations) if mission = DIFF_ADDON10,
  * **ROOM_RUSHMORE** (score board on the mountain) otherwise.
* On creating any of the three: universal FX `coin.raw`, and the player is charged **1** money unit
  (money-change reason 3300). Hint HINT_FERNGLASS is offered on entering.
* All three use the binocular frame overlay and the same exit zones (8 corner rectangles):
  (0,0)-(100,50), (0,0)-(50,100), (0,390)-(100,440), (0,340)-(50,440), (590,0)-(640,100),
  (540,0)-(640,50), (590,340)-(640,440), (540,390)-(640,440) → EXIT, id 999.
* Ambient volume 60.

### 6.1 Binocular overlay (Insel + WeltAll; Rushmore has it baked into its picture – assumption)
Bitmaps in the room GLI: `O1 O2 ATOP ABOTTOM AL1 AL2 AL3 AR1 AR2 AR3`.
* Alpha-blended edge pieces: ATOP at (108,0), ABOTTOM at (108,411), AL1 (39,30), AL2 (0,95),
  AL3 (39,346), AR1 (513,30), AR2 (589,94), AR3 (515,346).
* Opaque black corners: O1 at (532,0), (532,410), (0,0), (0,410); O2 at (601,30), (601,346),
  (0,30), (0,346).

**WIDESCREEN:** the binocular mask is built for exactly 640x440. For widescreen either pillarbox the
whole telescope view, or widen the view window and stretch/fill the outer black area; the alpha
edge pieces must stay at the edge of the (possibly wider) eye-piece.

### 6.2 Panorama scrolling (Insel + WeltAll)
* Panorama is 1280 px wide; view offset X ∈ [0,640], stored **per player** (shared between Insel
  and WeltAll; persists between visits).
* Scroll hotspots (no dialog/menu, not paused): mouse x ≤ 10 and offset > 0 → LEFT cursor
  (id 6010); mouse x ≥ 630 and offset < 640 → RIGHT cursor (id 6011).
* Speed model (per frame): while hovering a scroll zone, speed grows by 2 per frame up to 14 px/frame
  with no button, 44 with the left button held; absolute cap 66. Outside the zone (or button released
  while speed > 8) speed decays by 4 per frame towards 0. Offset += speed, clamped.
* First-ever telescope use in the game (global flag): left/right arrow cursors blink for 5 s
  (500 ms on / 500 ms off) at (0,220) and (640-arrowWidth,220).

**WIDESCREEN:** scroll zones are at the 640 edges; with a wider view the scroll range should become
[0, 1280-viewWidth].

---------------------------------------------------------------------------------------------------

## 7. Island – rocket launch islands (ROOM_INSEL = 116)

Traceability: `Insel.cpp: CInsel::CInsel, OnPaint, PrepareReflexionTable, WaterBlur`. Confidence:
high for layout, medium for water-effect details.

**Access:** telescope in DIFF_FINAL (see §6). Wait (hourglass) cursor shown while loading.

**Low-difficulty variant:** if difficulty < DIFF_NORMAL (3) the room loads `room/noinsel.gli`
(single picture `NOINSEL`) instead and shows nothing else (still charges 1). Since only DIFF_FINAL
leads here this is unreachable in practice (open question: demo leftover).

**Assets (`room/insel.gli`):** `INSEL` (1280-wide island panorama), `REFLEX` (water reflection
layer), `REFINSEL` (island reflection), `FRONT` (foreground mountains, 1280 wide), `REFMASK`
(reflection motion mask), `VOGEL01..08` (seagull), `BOAT`, `BOATM` (boat reflection), `BOATW01..03`
(bow spray), `BOATMASK`, rocket parts `ROCKxx` and reflections `RROCKxx`, binocular pieces.

**Sound:** if effects on: random background FX `moeve.raw` (gull; 50000/25000) and looping
`wellen.raw` (waves).

**Draw sequence per frame:**
1. Island panorama at (-viewX, 0).
2. Reflection layer: copy of REFLEX; on it, for each non-bankrupt player, the reflections of the
   owned rocket parts (bottom-aligned at the player's reflection anchor), then REFINSEL at (10,4).
3. Water ripple: the reflection layer is copied into the room from y=232 down with a per-row
   horizontal displacement (see below), respecting view offset.
4. Boat reflection (BOATM rippled with BOATMASK) at boat + (-14,61).
5. Rocket parts per player (not bankrupt) at the player's anchor minus viewX.
6. Boat at boat position; spray frame BOATW0n (cycles every 100 ms) translucent (level 4) at
   boat + (0,57).
7. FRONT at (-viewX, 440 - FRONT.height).
8. Seagull.
9. Binocular overlay (§6.1), blinking arrows (§6.2), exits.

**Rocket parts.** Player rocket flags bits: BASE 1, TOWER 2, ARM 4, AIRFRAME 8, WINGS 16, CAPSULE 32,
PROP 64, MAINPROP 128, HECK 256, COCKPIT 512 (all = 1023). Each part bitmap is a full-size layer
drawn at the player's anchor (parts carry their own offsets). Bitmap per (player n, bit):

| bit | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|---|---|
| bitmap | ROCK00 | ROCK01 | ROCKn2 | ROCK03 | ROCKn5 | ROCKn7 | ROCKn6 | ROCK09 | ROCKn8 | ROCKn4 |

(n = player 0..3, "ROCK02" etc. for player 0; shared pieces 00/01/03/09 are uncoloured).
Reflections use the same names with prefix `R`, except bit 6 for players 2 and 3 uses `RROCK16`
(player 1's) – likely an asset-sharing bug; keep or fix (open question).
Draw order of bits: 0,1,2,3,9,4,6,5,8,7.
Anchors (panorama coords): P0 (115,82), P1 (406,83), P2 (738,87), P3 (1048,86).
Reflection anchors (reflection-layer coords, bottom of part): P0 (115,164), P1 (406,155),
P2 (738,163), P3 (1048,172).

**Water ripple effect (own description).** A per-pixel "motion strength" table 0..15 is built from
the mask image: pixels within 16 px of the left/right edge ramp from 0 at the border to 15;
bright (near-white) mask pixels get strength 0 and cast a soft cone below them (strength grows by 1
per row, over 15 rows) so the area right under objects moves least; grey pixels get 0. Each frame,
every row y gets a horizontal shift `tx = 7·sin((t + sin((y+phase)/10)·2400)/500)` (t in ms);
a pixel with strength s is fetched from x + tx·s/15. Strength 0 pixels are not overwritten (keep
what is underneath).

**Boat:** with t = ms since boat start: x = 1300 - t/80 - viewX, y = 264 + ((1300 - t/80) - 1280)/60.
After 140 s the boat restarts after a random 2..7 s delay.

**Seagull:** x = (t - start)/25 - viewX, y starts at 22. Flapping: 8 frames, 100 ms each. When on
frame index 4 there is a 1/10 chance (per frame) to start gliding (frame 4 held, y sinks 4 px per
second); while gliding on that phase a 1/7 chance to resume flapping. Restart after 140 s with a
random 2..7 s delay, y reset to 22.

**Interaction:** only scrolling and exit. Right click leaves.

**WIDESCREEN:** water starts at y=232 and FRONT is bottom-aligned at 440 – both assume 440 high.

---------------------------------------------------------------------------------------------------

## 8. Mount Rushmore – score board (ROOM_RUSHMORE = 118)

Traceability: `Rushmore.cpp: CRushmore::CRushmore, OnPaint`. Confidence: high.

**Access:** telescope in all missions except DIFF_FINAL / DIFF_ADDON10.

**Assets:** `room/rushmore.gli`: `RUSHMORE` (background), `LOGO00..03` (airline logos per player).

**Display:** each frame:
* For every player compute the mission rating and a secondary rating (`GetMissionRating()` and
  `GetMissionRating(true)` – defined by the mission writer).
* Sort players by rating descending (in mission DIFF_ADDON01 – freight tonnage – ascending, i.e.
  lower is better, implemented by sorting the negated value).
* For rank r = 0..3 (Y = 261, 302, 346, 388) if that player is not bankrupt: logo of that player at
  (181, Y); text at (225, Y-1), big grey font, left aligned, clipped to (640,440): format string
  `Misc` 1200+difficulty (free game uses the DIFF_FIRST string) filled with the rating and secondary
  rating, both with thousand separators. A bankrupt player's row stays empty (no compaction).

**Interaction:** exit zones only (§6); no scrolling, no binocular code (the mask is presumably part of
the background picture). Right click leaves.

**WIDESCREEN:** fixed positions; pillarbox.

---------------------------------------------------------------------------------------------------

## 9. Space – space stations through the telescope (ROOM_WELTALL = 122)

Traceability: `WeltAll.cpp: CWeltAll::CWeltAll, ~CWeltAll, OnPaint, OnLButtonDown`. Confidence:
high.

**Access:** telescope in DIFF_ADDON10 ("Beyond the Final Frontier").

**Assets (`room/weltall.gli`):** `WELTALL` (1280-wide starfield), station parts `RING1 RING2 RING3
COMM CENTRAL LIVING BUSINES SOLAR` (+ placeholders for O2/POWER which have no generic bitmap),
coloured parts `O2B/G/R/Y`, `RING1B/G/R/Y`, `RING2…`, `RING3…` (B,G,R,Y = player 0..3 colour),
lit windows `CENTRALL COMML BUSINESL LIVINGL`, shooting stars `STAR1..3`, binocular pieces.
Smacker: `FlagB/G/R/Y.smk`, `UFO.smk`.

**Sound:** none (wave/gull code is commented out).

**Station flags:** RINGA 1, RINGB 2, RINGC 4, OXYGEN 8, COM 16, MAIN 32, LIVING 64, OFFICE 128,
POWER 256, SOLAR 512, ALL 1023. "Lit" = SOLAR and POWER both owned.

**Station anchors (panorama coords):** P0 (70,60), P1 (370,160), P2 (710,60), P3 (1000,100);
subtract viewX (and viewY, always 0).

Draw order and offsets relative to the anchor:

| part (flag) | bitmap @ offset | colour/lit overlay |
|---|---|---|
| RINGC | RING3 @ (44,41) | RING3<c> @ (88,44) |
| RINGB | RING2 @ (49,65) | RING2<c> @ (94,67) |
| RINGA | RING1 @ (47,82) | RING1<c> @ (89,84) |
| COM | COMM @ (97,105) | if lit: COMML @ (103,116) |
| OXYGEN | – | O2<c> @ (50,31) |
| OFFICE | BUSINES @ (90,13) | if lit: BUSINESL @ (90,13) |
| MAIN | CENTRAL @ (5,4) | if lit: CENTRALL @ (30,34) |
| LIVING | LIVING @ (10,114) | if lit: LIVINGL @ (10,114) |
| SOLAR | SOLAR @ (71,36) | – |
| POWER | (no visual of its own; only enables "lit") | |
| all parts | flag smk Flag<c>.smk looping at panorama (142,63) / (442,163) / (782,63) / (1072,103) for P0..P3 | |

Note: stations are drawn for bankrupt players too (no IsOut check) – open question.

**Shooting stars:** up to 20 at once. Once per real second, with 1/3 chance, a new one spawns at
panorama x random 0..1579, y 0, type random 0..2 with velocity in px per ms:
type0 (5,9)·4/256, type1 (-11,13)·4/256, type2 (18,12)·4/256 (≈ (78,141), (-172,203),
(281,188) px/s). Drawn with STAR<type+1>
at (x-150-viewX, y-20); removed when y ≥ 500.

**UFO:** UFO.smk (looping) drawn at its position minus view. Initial position (640,800), velocity
(0,-12) per frame, 50 steps, starts after 3..5 s. Moves only after its wait time. When it leaves the
box (-300,-300)-(1580,810) it resets to (640,800), velocity (0,-4), 140 steps, waits 13..15 s.
When the step count runs out it picks a new random move: vector (-150..149, -90..89) or, 1 in 6,
straight up (0,-500); steps = length/12 (≥2), velocity = vector/steps; then pauses 1..2 s.
(Movement is per painted frame, not time based – frame-rate dependent.)

**Interaction:** scrolling (§6.2) and exit zones. **Bug:** the exit hover uses the island room id
while the click handler checks the space room id, so left-clicking an exit corner does nothing;
only right click leaves. OpenAT should make left-click exit work (deliberate fix).
The destructor releases a TALKER_ARAB reference it never acquired (bug; ignore).

---------------------------------------------------------------------------------------------------

## 10. World – long-distance phone call to a branch (ROOM_WORLD = 117)

Traceability: `World.cpp: CWorld::CWorld, ~CWorld, OnPaint, OnLButtonDown, OnRButtonDown`;
`Stdraum.cpp` (TALKER_WORLD start ~1763, right-click ~3718, phone dialing ~6085);
`Takeoff.cpp` (room creation with home airport). Confidence: high.

**What it is:** not a telescope. It is the view shown while phoning one of the player's branch
offices (Niederlassungen) abroad with the handy/filofax: a globe turned to the called city with a
satellite in the foreground.

**Entry:** player chooses a city in the phone menu (MENU_FILOFAX → MENU_BRANCHLIST); dialing costs
1 money (statistics "other expenses"), plays dial sounds, then dialog TALKER_WORLD (medium handy)
starts, which creates this view as the dialog window with the chosen city. Can also be created as
a normal room (ROOM_WORLD) with the home airport city (from the sabotage room, see §3).

**Assets:** `room/world.gli`: `WORLD` (background), `SAT1` (satellite), `SATLICHT` (1 frame blinking
light), `SENDER1..3` (transmit waves). Globe texture `EarthAll.lbm` from the GLI path.

**Construction:**
* Globe rotation angle derived from the city's globe longitude (city globe position x) so the city
  faces the viewer; the globe is rendered into the background at (100,20) (globe renderer shared with
  the office globe, other writer). Planes' globe positions are refreshed.
* Speech bubble position: the city's projected position + (100+170, 20). If its y > 200 the bubble
  style is 2 (pointing up) and it moves by (+20,-30); else style 1 and moves (+35,0).

**Paint:** background, `SAT1` at (30,285), `SATLICHT` anim (repeat, prelude 100, speed 2, wait 100)
at (183,389), `SENDER` anim (3 frames, repeat, speed 5) at (203,147). No hotspots.

**Dialog behaviour (reference):** first call to a city → greeting (`Welt` 1000 voiced if the city
has a wave sample, else `Welt` 2000 text); later calls → MENU_AUSLANDSAUFTRAG for that city
(zoom from (480,220), 0.1), the call is billed while open. Right click while in this dialog stops
it and returns to MENU_FILOFAX page 2 / MENU_BRANCHLIST (choose another city).

**Leave:** right click (y<440, no menu/dialog) leaves. On destruction an empty comment message is
pushed to the order advisor (clears his bubble). While the local player's sabotage mode is 6 all
clicks are ignored.

**WIDESCREEN:** globe placement (100,20) and satellite at bottom-left assume 640x440.

---------------------------------------------------------------------------------------------------

## 11. Statistics screen (ROOM_STATISTICS = 119)

Traceability: `Statistk.cpp: CStatistik::CStatistik, ~CStatistik, OnPaint, CalcGraph,
RepaintGraphWindow, RepaintTextWindow, OnLButtonDown, OnRButtonDown`; `Statistk.h`;
`Stdraum.cpp` (~3684, logo click); `Bank.cpp` (line colours); `defines.h` (STAT_*).
Manual ch. 14 pp. 55-56. Confidence: high.

### 11.1 Access
Click the airline logo at the left of the status bar (x < 78, status-bar row) after 09:00 game time,
no dialog/menu open. Clicking it again while statistics are queued leaves. Not a physical room – the
player figure does not walk.

### 11.2 Assets
* `room/statistk.gli`: background `STATISTK`; `TOPSAVER` (strip redrawn at (374,0) to hide the zoom
  buttons when the graph sheet is up); `DECKEL` (graph sheet background, the drop-down "lid");
  `DOWN DOWNHIGH UPHIGH` (sheet toggle arrows); `BUTA<n>OFF/HI/ON` (n = local player 1..4: magnifier
  with own logo); `ZOOMIN ZOOMOUT`; `BUTBHI BUTBON` (money button); `BUTCHI BUTCON` (route button);
  `EXIT EXITHIGH`; `SWTCH<1..4>ON/HI/SU` (player portrait toggles: on, hover, super-hi);
  `POBEL_1..4` (5x5 data-point markers per player colour); `HAKCHEN` (red tick).
* Fonts: `misc/stat_1.mcf .. stat_4.mcf` (one per player colour); also the shared small black,
  small "plastic", dialog-partner fonts.
* Texts: token `STAT` in Std res: labels 8002–8221 (see 11.6), 9000 (day label), 9001 ("days"),
  9002 ("months"), 9010/9011 (remaining days, singular/plural). Tooltips `Tool` 3300/3301/3302.

### 11.3 Screen layout (640x480)

```
 y=0 ┌──────────────┬─────────────────────────────────────────────┐
     │ day label    │  (sheet arrow button 390,10)  zoom 374/412   │
 15  │ "N. day"     │                                              │
 40  │ label list   │   TEXT TABLE 440x330 @ (190,40)              │
 56  │ rows @20px   │   4 columns x 109 px (players 0..3)          │
     │ x 34..155    │   — or — GRAPH SHEET sliding down over it    │
 370 ├──────────────┴─────────────────────────────────────────────┤
     │ [mag 10][money 70][route 130] [P0 190][P1 300][P2 410][P3 520]│
 430 └────────────────────────────────────────────────────────────┘
 440 status bar; EXIT button (0,440)-(79,479) over the logo
```

**Left column – day label:** text centred around x ≈ 97 (centre of 35..160), y 15, dialog-partner
font: "<date+1>. " + `STAT` 9000. In missions DIFF_ADDON03, ADDON04, ADDON06 it alternates every
3 s with the remaining days (`STAT` 9010, or 9011 when exactly 1), remaining = target days (or
target "miles days" for ADDON04) - date.

**Left column – label list:** up to 16 rows; row i has text rect (45, 56+20i)-(155, 66+20i).
* Group headings: small plastic font, x offset -18 (x=27).
* Sum rows: plastic font, x offset -11.
* Value rows: small black font, x offset 0; if the value is ticked ("visible"), `HAKCHEN` at
  (34, rowTop).
* Hover rect per row: (left + o2, top-5)-(155, bottom+4) where o2 = -11 for values/sums, -18 for
  headings; hovered row gets cursor highlight; in table mode a white 439x21 bar is blended
  (translucency level 6) at (190, rowTop-7) across the table.

**Right area – text table (sheet up):** `TextTableBm` 440x330 at (190,40), filled with the background
underneath (so it is "transparent"), then for each non-bankrupt player p a column x = 109p .. 109p+109,
values right-aligned with 5 px right margin, row i at table y 16+20i (screen y 56+20i, aligned with
labels), in player font stat_<p+1>. Bankrupt players' columns stay empty. Repainted when dirty or
every 1.5 s while the sheet is still.

Value formatting per row type:
* VALUE: plain integer. CURRENCY: money format. SINGLE_PERCENT: percent format of raw value.
* PERCENT: value × 100 / value of the row directly above (0 → "0%").
* SUM_CURR: money; sum of the ticked value rows since the previous sum (then reset); adds to the
  running difference.
* SUM_DIFF: money; sum of the preceding SUM_CURR rows (income sum + expense sum, expenses are stored
  negative), then reset.
* SHAREVALUE: money; only if row 2 (share price) is ticked: Σ over ticked rows 3..6 of
  (shares this player holds in airline k) × (current share price of airline k).
All values are "today" (past-day index 0).

**Right area – graph sheet (drop-down):** `DECKEL`-sized bitmap drawn at (190, 40-360+sheetY) with
clip (190,10)-(630,370); sheetY ∈ [0,329] (0 = hidden, 329 = fully down). The text table is drawn
only while sheetY ≠ 329; the sheet only while sheetY ≠ 0.
* Inside the sheet: zoom caption at (263,12) in font stat_2 ("<days> " + `STAT` 9001 when ≤ 30
  days, else "<days/30> " + 9002).
* Graph rectangle inside sheet: left 6, top 30, right 439, bottom 329 (width 433, height 299) →
  on screen (196,70)-(629,369) when fully down.
* Sheet slide: arrow click sets speed ±25; each frame speed grows by 10 in magnitude but is limited
  so that (1+2+…+speed)/3 never exceeds the remaining distance (ease-out); snaps at 0 / 329.

**Bottom buttons** (`StatButtons`, hover/click art as listed; toggles keep a latched state):

| id | hot rect | art (normal/hi/clicked/superHi) | toggle | tooltip | action |
|---|---|---|---|---|---|
| 999 | (0,440)-(79,479) | EXIT / EXITHIGH / – | no | – (EXIT cursor) | leave |
| 100 | (190,11)-(639,38) **+ sheet offset** (moves with sheetY); art at (390,10)+offset | DOWN* (see below) | no | – | toggle sheet up/down |
| 110..113 | (190,370)-(299,429), (300,…409), (410,…519), (520,…629) | –/SWTCHnHI/SWTCHnON/SWTCHnSU (hovered = HI, selected = ON, selected and hovered = SU) | yes | airline name + " (" + manager name + ")" | toggle player n in the graph |
| 120 | (10,370)-(65,429) | BUTAnOFF/BUTAnHI/BUTAnON/BUTAnON | radio | `Tool` 3300 | group 0 (income/expenses) |
| 121 | (70,370)-(125,429) | –/BUTBHI/BUTBON/BUTBON | radio | `Tool` 3301 | group 1 (shares & finance) |
| 122 | (130,370)-(185,429) | –/BUTCHI/BUTCON/BUTCON | radio | `Tool` 3302 | group 2 (operations) |
| 130 | (374,-318)-(408,-293) + sheet offset (→ (374,11)-(408,36) when down) | –/ZOOMOUT/ZOOMOUT | no | – | step to the **next shorter** period |
| 131 | (412,-318)-(446,-293) + offset | –/ZOOMIN/ZOOMIN | no | – | step to the **next longer** period |

* Sheet arrow art: while the sheet is up the hover art is `DOWNHIGH`; after pressing "down" the
  normal/clicked art becomes `DOWN`, hover none; when fully down hover art becomes `UPHIGH`, normal
  none; pressing "up" restores hover `DOWNHIGH`.
* Player portraits: while the sheet is not fully down they are always drawn in their "clicked"
  (SWTCHnHI art slot) look, hovering only shows the name tooltip and clicks do nothing – player
  filtering only works in graph view. Buttons of bankrupt players are skipped entirely (not drawn,
  not clickable).
* Non-toggle buttons show their clicked art for 300 ms after a click.
* The exit button art is re-drawn after the status bar so it covers the airline logo there.
* Periods (days): 7, 14, 21, 30, 90, 180, 270, 360 (manual says 7, 14 days, 1, 3, 6, 12 months –
  code also has 21 and 270). Zoom changes animate: each frame current days move 2/3 of the
  (integer) remaining difference towards the target.

### 11.4 Graph drawing
* Series = every ticked value row (type ≥ VALUE) of the current group × every selected,
  non-bankrupt player. Sum rows are not plotted.
* Y scale: min/max over all plotted series over the visible range (min starts at 0, max at 0, so 0
  is always included); scale = 299 / (max - min); baseline y = bottom - (-min·scale). PERCENT rows
  are plotted as percentages (value×100/row above, 0 when the divisor is 0).
* Period ≤ 30 days: daily samples, today at the right edge (x = 439), sample d at
  x = 439 - d·(433/days), d = 0..min(days, date+1).
* Period > 30 days: monthly samples (`past month` d, months of 30 days), spacing 433/(days/30);
  plotted months = min(11, available days) (original has an operator-precedence slip; effectively at
  most 11 segments, i.e. 12 points); for the scale up to min(11,(days+29)/30) months. Available days =
  min(period, date + 1): with fewer than 30 of them nothing is drawn in the monthly view (caption only);
  from then on all 11 months are drawn, months without history as zero. The hover darkening applies to
  any hovered non-heading row (sum rows included).
* Each segment drawn as a 1 px line in the player colour (Bank line colours: P0 0x0080FF blue,
  P1 0x00FF40 green, P2 0xFF0000 red, P3 0xFFFF00 yellow). If the mouse hovers a *different* value
  row of the label list, all other series are drawn in the dark variant (0x00407F, 0x007F20,
  0x7F0000, 0x7F7F00) – this highlights the hovered series. Every sample gets the 5x5 `POBEL_<p+1>`
  marker centred on it.
* If at least one segment was drawn, a white horizontal zero line is drawn across the graph at
  the baseline.
* On day 0 (date = 0) no graph is drawn (only caption).
* Graph lines are clipped to the graph rectangle.

### 11.5 Label clicking (both views)
* Click on a group heading: toggles the tick of every row below it up to the next heading.
* Click on a value row: toggles its tick. With **Shift** held: unticks all rows of its group, then
  ticks only the clicked row ("solo").
* After any change: table and graph are recomputed.

### 11.6 Data series (groups)
Group 0 – income/expenses (text id → statistic):
heading 8002 (income); 8003 routes income (E_ROUTEN 30), 8004 order income (E_AUFTRAEGE 31),
8006 other income (E_SONSTIGES 33); 8014 sum; blank; heading 8007 (expenses); 8008 rents
(A_MIETEN 34), 8009 salaries (GEHALT 22), 8010 kerosene (A_KEROSIN 35), 8011 maintenance (WARTUNG
19), 8012 fines (A_STRAFEN 36), 8006 other (A_SONSTIGES 38); 8014 sum; blank; 8015 balance
(SUM_DIFF). All currency.

Group 1 – shares & finance: heading 8100; 8101 shares issued (AKTIEN_ANZAHL 25, value); 8102 share
price (AKTIENKURS 5, currency); 8103..8106 shares held in airline 0..3 (AKTIEN_SA/FL/PT/HA 26..29,
value); 8107 total share value (SHAREVALUE); blank; heading 8110; 8114 account balance (KONTO 0),
8116 credit (KREDIT 1), 8115 company value (FIRMENWERT 10) – currency; 8113 awareness/image
(BEKANNTHEIT 23, single percent).

Group 2 – operations: heading 8200; 8201 planes (2), 8202 routes (21), 8221 branches (20),
8203 flights (6), 8204 orders (7), 8205 last-minute orders (8), 8206 delays (14), 8207 accidents
(15), 8208 sabotage acts commissioned (16); blank; heading 8210; 8211 passengers (3),
8212 satisfied passengers (11, PERCENT of passengers), 8111 employees (17), 8112 satisfied staff
(13, PERCENT of employees).

### 11.7 State & persistence
Entering copies from the sim (saved with the game): player mask, group, graph-visible flag,
period and target period (a new game starts with 7 days), sheet position, per-group tick array. Leaving writes them back; the sheet
position is snapped to 0 or 329 (nearest). Statistics are refreshed on entry and every 8th frame.
Leaving forces a status-bar refresh. Right click leaves.

**WIDESCREEN:** everything is absolute 640x480 including an exit button inside the status-bar
row; the graph width (433 px) could be widened for wide screens, but the sheet/table art is fixed
440 px wide. Recommend pillarbox; optionally widen only the graph sheet.

---------------------------------------------------------------------------------------------------

## 12. Evening / day-end screen (ROOM_ABEND = 8)

Traceability: `Abend.cpp: CAbend::CAbend, ~CAbend, OnPaint, OnLButtonDown, OnRButtonDown,
OnKeyDown`; `Takeoff.cpp` main loop (~1220 speed, ~1789-1840 sequence); `Sim.cpp: SIM::GetSeason,
DoTimeStep`. Confidence: high.

### 12.1 When it appears
* Day state machine: 1 = morning briefing pending, 2 = working day, 3 = evening screen shown,
  4 = day finished.
* Triggered when game time reaches 18:00, or earlier when the player "calls it a day" (MENU_REQUEST
  CALLITADAY, all humans agreed in network play). At 18:00 the local player's call-it-a-day flag is
  cleared and sim time is frozen ("no time") until the screen is up.
* In state 2 the game first ends any open dialog and makes every non-robot player that is inside a
  room leave it; only once nobody is inside a room is the evening screen created (as the top window)
  and the state becomes 3.
* While the screen is up the clock keeps running at the fastest speed (speed level 5) whenever it
  is before 08:00, after 18:00 with state 4, or the day was called early.
* The screen ends (state 4) when: time ≥ 18:00 is observed in paint, or right click, or Esc. If the
  day was called early, right click/Esc therefore jumps straight to "finished" and time runs fast
  through the rest of the day and the night.
* The window is destroyed when state = 4 **and** time is between 09:00 and 10:00 of the next day; then
  time freezes, state → 1, wait cursor on, and every active player enters the airport and then the
  Airport Authority (morning briefing, other writer). Midnight rollover (new day, airport new day)
  happens inside the time step.

### 12.2 Construction effects
* Ambient volume 0, player walking stopped, statistics updated, the player's advisor messages get a
  "new day" reset, all players: not ready for briefing, not stuck, game speed 0 (unless test cheat),
  "ready for morning" = false (true if robots exist), work-wait timers normalised.
* Clears the 18-o'clock flag and the call-it-a-day time.
* Entry sound (effects on): if time ≤ 17:00 → `birds.raw` with 1/3 chance (otherwise the already
  loaded sound restarts – quirk); else 1/3 chance `Hupe.raw` (car horn).

### 12.3 Pictures per season
GLI = `room/abend<season>.gli`, season from the in-game calendar date:
* 0 spring: 5 Mar – 20 May; 1 summer: 21 May – 11 Aug; 2 autumn: 12 Aug – 19 Dec;
  3 winter: 20 Dec – 4 Mar (months treated as 30-day blocks for this test).
* Each holds two 640x440 pictures: `AIRPORT` (airport by day) and `NACHT` (by night).
  (`abend.gli` without number also ships – unused.)
* If the season changes while the screen is open (crossing a boundary at midnight), the night
  picture is kept in memory and the day picture is reloaded from the new season's GLI.

### 12.4 What is shown (per frame, by game hour h)
* 8 ≤ h ≤ 19: day picture.
* h ≤ 4 or h ≥ 23: night picture.
* 5 ≤ h ≤ 7 (dawn): crossfade night→day over 3 hours in 9 steps: level = ((h-5)·60 + minute)·8/180
  (0..7); day weight = level, night weight = 8-level.
* 20 ≤ h ≤ 22 (dusk): level computed from 20:00, day weight = 8-level, night weight = level.
* Drawn directly to screen rows 0..439; the status bar (clock running) is drawn below as usual.
  No text, no buttons, no hotspots.

### 12.5 Hourly ambience
On each change of game hour, if effects on:
* with 1/5 chance, and no FX currently playing: 2/3 chance → one plane sound at random from
  `prop.raw, flyby.raw, flyby2.raw … flyby5.raw`; 1/3 chance → animal sound by hour: 10–21 →
  `Chilp.raw`/`Chilp2.raw`/`Bark.raw`; 22–2 → `Eule.raw` (owl) / `Loon.raw` / `Zirpen.raw`
  (crickets); 6 → `Hahn.raw` (rooster).
* otherwise at 06:00 with 1/3 chance (and nothing playing) → `Hahn.raw`.

### 12.6 Input
* Right click (inside window) → end sequence (state 4).
* Esc → end sequence. Space / left click only affect an unused "clock picture" phase (no effect).
* Leaving destroys pictures, stops FX, sets state 4.

**WIDESCREEN:** the day/night pictures are 640x440; pillarbox or scale-to-fit (no interactive
elements, so scaling is safe).

---------------------------------------------------------------------------------------------------

## 13. Non-UI files in scope (classification)

* **Checkup.cpp / Checkup.h** – a Windows registry wrapper (read/write typed values as text under
  "Software\Spellbound Software\…") plus an unused hardware probe (CPU MHz, RAM, DirectX version,
  OS, CD speed). Used only for options/registry persistence (`Sim.cpp`, `Takeoff.cpp`,
  `Init.cpp`). **No UI.** OpenAT: replace with its own settings storage.
* **SecurityManager.cpp** – copy-protection (see §4). No UI.
* **Brick.cpp** – airport building-block data (bricks = airport tiles/props, build lists per hall and
  level, load/save). Belongs to the airport-hall writer. No UI.
* **Askbrick.cpp** – developer-only airport-editor popup: a child window (10,10)-(620,460) with
  background `ASKBRICK` from `Askbrick.gli`, showing a 5x4 grid of brick thumbnails (cells 124x115,
  centred) starting at a brick group; arrow keys / PgUp / PgDn shift the group by 1/5/20; left click
  returns the clicked brick id and closes; right click closes. (Click grid uses 5 rows of 112 px vs 4
  rows of 115 px drawn – mismatch.) **Not needed for the game**; only for an OpenAT airport editor.

---------------------------------------------------------------------------------------------------

## 14. Open questions

1. **ArabMode 6:** World view ignores clicks and the sabotage room forces ROOM_WORLD on exit when the
   player's sabotage mode is 6, but the dialog only sets plane-sabotage modes 1..5. Possibly a
   removed/network-only sabotage type, or dead code. Needs Sim.cpp sabotage-execution review.
2. Rick's drinker mouth clips 1–4 (drinking chain) are unreachable from the IDLE hub – verify in the
   real game whether he ever drinks.
3. Island reflection bitmap for bit 6 of players 2/3 is `RROCK16` (player 1's colour) – asset bug or
   deliberate (uncoloured piece)? Check the GLI.
4. Space stations are drawn even for bankrupt players (Rushmore and Island skip them).
5. Statistics zoom buttons: the button using art `ZOOMOUT` shortens the period (= zooms in). Confirm
   visually which magnifier sign appears on which button.
6. Statistics periods 21 and 270 days exist in code but not in the manual.
7. (Answered) CAnimation prelude/speed/cyclic-wait are in ticks of the real-time ticker `TickerTime`
   (real milliseconds / 32, so 32 ms per tick; sim-core), not 1/20 s.
8. Whether the Rushmore background already contains the binocular mask (code draws none) – check
   `rushmore.gli`.
9. `noinsel.gli` (shown below DIFF_NORMAL) is unreachable in the retail flow – keep or drop.
10. The security office's entrance location and the sabotage secret door are airport-hall features
    (other writer); confirm hotspot runes there.
