# Rooms & UI 6 — Airport service rooms (behaviour spec, clean-room)

Scope: Personnel office, Bank, Airport supervisor ("Boss" / Aufsicht incl. morning briefing and airport expansion), Bulletin board (Tafel, auctions for gates / branch offices), Travel agency (Reisebüro), Last Minute, Freight counter (Fracht), Route office (RouteBox). Order-card display/take logic shared by the three order counters is in section 0.

Everything here is restated in my own words from reading the original source. Coordinates, rects, timings, ids and asset names are facts and are listed verbatim. In-game text is never quoted. It is referenced as `TOKEN_x / id` (dialog texts) or `StandardTexte TOKEN_x id`.

Coordinate system: all room coordinates are in the 640x480 original frame. The room view is 640x440 (y ≥ 440 is the status bar). **[WS]** marks a 640x480 layout assumption that OpenAT has to handle for widescreen.

Confidence tags: **[H]** read directly and unambiguous; **[M]** inferred from state-machine wiring; **[L]** guess.

---

## 0. Shared conventions (all rooms in this file)

### 0.1 Room base behaviour (CStdRaum, documented elsewhere — summary only)
- Each room is constructed with `(bHandy, PlayerNum)`, a room GLI and a background bitmap id. `bHandy=TRUE` means the room is instantiated only as a **phone-call backdrop** (Handy dialog), not as the player's location.
- Hotspots work through a per-frame "mouse look" call: cursor type + tooltip id + (click area, click id). A left click then dispatches on the (area, id) pair that was last set. Tooltip ids index `TOKEN_TOOLTIP` (the generic tooltip text table). [H] `Gameframe.cpp: SetMouseLook`
- **Right-click leave (all rooms in this file):** a right click inside the room window does three things in order. If a menu is open, it is forwarded to the menu (usually closes it). Else, if no dialog is open and y < 440, the player leaves the room. Then the base right-click handling runs. [H] each room's `OnRButtonDown`
- On enter, every room marks itself visited for the player (`SetRoomVisited`, used by the filofax). Most rooms also clear `Sim.FocusPerson` (camera focus) and lower the ambient airport sound (`AmbientManager.SetGlobalVolume(n)`, only when not a Handy backdrop).
- Smacker character model (CSmackerPerson / CSmackerClip, `Smackprs.h`). Each character is a set of clips. A clip has an smk file (empty = invisible pause clip) and an optional sound (`.raw`, located in `video/`). It also has a screen offset, a mood (IDLE, HOLY, TALKING, LISTENING), a repeat range, a post-wait range (in 1/20 s), and a "cancellable" flag. It may set a shared variable at clip start (PRE), at clip end (POST) or at frame N. It may also read a "decision variable": when that is ≠ −1, the next clip index is taken from it directly. Successors are listed with tokens: A = always-eligible with weight digit, E = only when leaving the current mood. The dialog system sets a desired mood (TALKING while the NPC speaks, LISTENING while the player chooses), and the clip graph walks toward it. In each room below the clip graph is summarised as states, not transcribed.

### 0.2 Order-card counters (Travel agency, Last Minute, Freight) — common rules
Traceability: `reise.cpp`, `Lastmin.cpp`, `Fracht.cpp` (`OnPaint`, `OnLButtonDown`, `RepaintZettel`), `Auftrag.cpp: CAuftraege::RefillForLastMinute / RefillForReisebuero / FillFor*`, `Fracht.cpp: CFrachten::Refill / Fill`, `Sim.cpp: SIM::NewDay` and the minute tick.
- **Pool size:** each counter has a global pool of **6** slots: `LastMinuteAuftraege`, `ReisebueroAuftraege`, `gFrachten`. Position tables in the code have 10 entries, but only the first 6 are used. [H]
- **Empty-slot sentinel:** passenger orders treat premium == 0 as "slot empty" and premium < 0 as "card flying away". Freight treats premium == −1 as empty and premium < −1 as "crate falling". Freight premium 0 is a valid (unpaid) freight. [H]
- **Refill ticks:** every 5 game minutes the tick counters for LastMinute, Reisebüro, Freight and Museum each go up by 1 (`Sim.cpp` minute tick). On **room enter and room leave**, the counter refills up to *tick-count* empty slots and then resets its tick to 0. In network play this is synced first (`Sim.NetRefill(1|2|3)`). At **new day** all pools are completely regenerated (`FillForLastMinute`, `FillForReisebuero`, `gFrachten.Fill`) and the ticks are set to 6. [H]
  - The `Minimum` refill parameter defaults (see Auftrag.h). Its effect is to top up further empty slots when fewer than *Minimum* cards remain. Open question Q-0.1.
- **Card art:** passenger cards use the global `gZettelBms` = `ZETTEL01..03` (standard GLI, loaded in `Init.cpp`, 3 variants, chosen by slot % 3). Freight uses crate bitmaps (see Freight). Card text is drawn in FontSmallBlack, centered. It has 4 lines: "AAA-BBB" airport codes (y 10..29), origin city name (y≈31), a dash (y≈41/42), destination city name (y≈52/54). City names with exactly two spaces are cut to their first word (`reise.cpp: ShortenLongCities`). [H]
- **Hover:** hovering a card sets the HOT cursor (the code reuses click area `ROOM_TAFEL, id 0`, purely cosmetic). It shows the order tip panel (`TipBm` = `BLOC1` from `gli/TipAu.gli`, with plane icons `PL_B00/PL_V00/PL_R00/PL_J00/PL_SW00`, 9 frames each, for the 4 player colours + black). The first time a given card is hovered, the order advisor gives a profitability comment (`PLAYER::CheckAuftragsBerater`; texts `TOKEN_ADVICE 7100..7105, 7110, 7113`). When the mouse leaves the card area, the advisor bubble is cleared. Tip-panel rendering is in the menus/tooltip writer's scope (TIP_BUYAUFTRAG / TIP_BUYAUFTRAGR / TIP_BUYFRACHT). [H] Passenger card layout (`DrawAuftragTipContents`, headline (32,3), contents (32,20), small black font): "From - To" (falls back to name - code, then code - code within 170 px); labels at contents x, values at +85: date (+22: "today" text on the last day, the weekday for a one-day order, otherwise "until" + last weekday), distance (+44), passengers (+55), penalty (+66), premium (+88). Own planes that cannot fly the order are marked grey on the card's map. Verified on the running original (fidelity pass).
- **Click = take:** no confirmation. Steps: (1) the player's order list grows by 10 if fewer than 3 free entries remain, so there is no hard cap. (2) Sound `paptake.raw` plays. (3) The order is copied to the player and network-synced (`NetUpdateOrder` / `NetUpdateFreightOrder`, then `NetUpdateTook(kind, slot)` with kind 1=LM, 2=Reisebüro, 3=Freight). (4) Statistics go up. (5) The slot enters its leave animation. [H]
- **Closing time:** Last Minute closes at 16:00 and Travel agency at 17:00 (`AirportView.cpp` constants `timeLastClose`, `timeReisClose`). When game time reaches it, a player still inside is ejected (`LeaveRoom`) on the next paint. [H]
- **On leave:** a card still mid-animation is snapped to empty, the order-advisor bubble is cleared, and the refill runs. [H]

---

## 1. Personnel office (Personalbüro) — `Personal.cpp`

### 1.1 Entry
- Class `CPersonal(bHandy, PlayerNum)`, GLI `room/personal.gli`, background `PERSONAL`. Each player has their own office: room id `ROOM_PERSONAL_A + PlayerNum*10` (A..D). Visited flag and hotspot area always use `ROOM_PERSONAL_A`. [H] `Takeoff.cpp` room factory, `Personal.cpp: CPersonal::CPersonal`
- **Handy:** yes. The phone list has two entries for personnel. The first calls the "a" talker (female clerk, hiring/advisors) with HandyOffset 280. The second calls the "b" talker (male clerk, staff mood/salary) with HandyOffset 120. Personnel calls are never "busy", unlike other rooms. The game also auto-calls the player via this backdrop when a strike ends (`Sim.cpp` strike notification, talker b, dialog id 1000+StrikeEndType). [H] `Stdraum.cpp` MENU_FILOFAX handler, `Sim.cpp` ~1430
- On enter: ambient volume 40. `PLAYER::UpdatePersonalberater(3)` is called (refreshes the personnel advisor's state). [H]

### 1.2 Bitmaps (personal.gli)
`PERSONAL` (background), `KUGEL` (crumpled paper ball), `ZEITSCHR` (magazine on desk), `MITTELB` (letter lying in the middle), `BRIEF` (stack of letters), `NOSABO` (anti-sabotage overlay). [H] `glpers.h`
Static overlays drawn every frame:
- `NOSABO` at (276,0) when the player has security flag bit 2 (value 4, bought anti-sabotage protection). [H]
- Magazine at (483,225) while "magazine on desk" is true; letter at (342,224) while "letter on table" is set; letter stack at (361,292) while "stack present" is true. [H]

### 1.3 Characters (video/*.smk)
**Female clerk ("a" talker)**, all clips at (340,113):
- Idle loop: take a letter from the stack (`PFBrief`, sound `PFBrief.raw`; toggles the stack graphic at frame 1). Read it (`PFLeseB`, 1.5–4.5 s; puts the letter on the table). Throw a paper ball (`PFWerf`, sound `PFWerf.raw`; launches the paper-ball flight at frame 5). Then either pick up the magazine (`PFWild`, sound; magazine graphic removed), read it (`PFLeseW`, 3.5–6.5 s) and put it back (`PFWildZ`, sound), or turn.
- Talk states: `PFTurn` (turn to player), `PFRede` (talking), `PFTurnW` (listening), `PFTurnK` (short listen variant), `PFTurnZ` (turn back to work).
**Male clerk ("b" talker)**, all clips at (130,80):
- Idle: read (`PMLese`), leaf through papers (`PMBlat`). When a letter is on the table: take it (`PMKorb`; clears "letter on table" at frame 4). Toss it in the bin (`PMKorbB`; starts the dropping-paper clip).
- Talk: `PMTurn` (turn), `PMReden` (talk), `PMWait` (listen, ~5 s cycles), `PMWaitK` (short listen), `PMTurnZ` (turn back).
**Dropping paper:** `Brief.smk` at (133,260), played once when the man bins the letter. [M — the exact handshake between the two clerks is inferred from the shared variables]

### 1.4 Paper-ball animation (code-driven, not smk)
When the woman's throw fires, one of two flights is chosen:
- 95 %: the ball (`KUGEL`, drawn at 5 % scale) flies up and to the right from (491,143). It moves +1 px per 2 ms in x and −1 px per 10 ms in y, and ends when x > 640. **[WS]** (it should leave past the right edge of the *room* view).
- 5 %: a lob toward the camera and down-left. Over 400 ms it scales from 5 % to 100 % while moving from (491,143) to (325,163). Over the next 400 ms it scales back down to 5 % while moving toward (159,388). The flight ends when x < 159. The ball is drawn centered at its position, colour-keyed. [H] `CPersonal::OnPaint`

### 1.5 Hotspots (only while no dialog and no menu is open)
| Rect (x1,y1,x2,y2) | Cursor | Click |
|---|---|---|
| (20,0)-(183,293) | EXIT | leave room |
| (385,145)-(566,352) | HOT | dialog with the "a" talker: `TALKER_PERSONAL1a + PlayerNum*2`, MEDIUM_AIR, default entry |
| (177,94)-(329,248) | HOT | dialog with the "b" talker: `TALKER_PERSONAL1b + PlayerNum*2` |
| (394,378)-(446,425), only if the player does **not** own `ITEM_TABLETTEN` | HOT | "a" talker, entry 20 (pills on the desk) |
[H] `CPersonal::OnPaint/OnLButtonDown`. Note: the hotspot rects are written as 640-minus-x in the source. **[WS]** Their values are as in the table.

### 1.6 Dialog / actions (trees are documented elsewhere — entry points only)
- "a" default entry: the first visit offers a choice (`TOKEN_JOBS 99`), later visits a greeting (102). By phone it states the counts of jobless advisors / pilots / flight attendants (`TOKEN_JOBS 101` + number window 9990101). Choices 200/201/202 open `MENU_PERSONAL` with (`BERATERTYP_GIRL` | `WORKER_PILOT` | `WORKER_STEWARDESS`, mode 1 = **applicants / hire**). The menu zooms from (291,279). [H] `Dialog.cpp` personnel cases
- "b" talker: `TOKEN_JOBS 499` → 600..605 choices. 600 = **raise all salaries 10 %** (strike plan cancelled, morale pushed up to an average of at least 20). 601 → 800 → 810 = **cut all salaries 10 %**. 602/603/604 open `MENU_PERSONAL` mode 2 (**own staff / fire**) for advisors / pilots / attendants, zooming from (405,261). By phone these are refused (text 900). During a strike the opening line is `TOKEN_JOBS 950`. Strike-end calls use ids 1000..1010. [H]
- Pills puzzle: "a" entry 20 answers 301 if the player has earned the clerk's trust (`SeligTrust`), else 302. Option 301 gives `ITEM_TABLETTEN`. Trust is earned by using the **postcard item** in your own personnel office (dialog entry 300). The postcard comes from the bulletin board (section 4). [H] `Stdraum.cpp` item use, `Dialog.cpp` 300/301
- Salary rules used by those options: each change is ±10 % of the current salary. A raise is capped at 1,000,000 and gives +20 morale. A cut gives −25 morale. Morale below −100 means the employee quits: an advisor message `TOKEN_ADVICE 2000 + type + gender*100` is shown and the employee returns to the reserve pool at the original salary. [H] `Personal.cpp: CWorker::Gehaltsaenderung`

### 1.7 Daily refresh visible in the UI (`CWorkers::NewDay`, `CheckShortage`)
- Every new day, the applicant pool shown by the clerk is rebuilt. All previous applicants go back to reserve. Then 10–19 random reserve people become applicants, plus 5–6 extra flight attendants, all at their original salary. RNG is seeded with Date+StartTime, so it is deterministic per game. [H]
- If fewer than 40 unemployed pilots (or attendants) with talent > 60 exist, 10 new random ones are generated. Pilots: salary 3000–9900, gender 80 % male. Attendants: salary 3000–7900, gender 20 % male. Names come from the name tables. [H]
- Employees of human players lose morale daily: 1 point if image < 500, 2 if < 0, 3 if < −500. They lose 20 more per step if their pay was cut below the original, and quit below −100. [H]
- An advisor can be forced into today's pool (`EnsureBerater`). [H]

### 1.8 Leave
Exit hotspot or right click. No special cleanup beyond releasing the talkers. [H]

---

## 2. Bank — `Bank.cpp`

### 2.1 Entry
- Class `Bank(bHandy, PlayerNum)`, `room/bank.gli`, background `BANK`, room `ROOM_BANK`. Default dialog partner `TALKER_BANKER1`. [H]
- **Handy:** yes, two phone entries. In both cases the busy check is on BANKER2 (inconsistent — Q-2.3). Entry 1 calls `TALKER_BANKER1` (man, HandyOffset 367, bubble (479,157)). Entry 2 calls `TALKER_BANKER2` (woman, HandyOffset 153, bubble (467,202)). [H] `Stdraum.cpp`
- On enter: loads fonts `misc/bank_bl.mcf`, `misc/bank_ro.mcf` (not used by this file's painting; probably by the bank menu). Loads `gli/Tip.gli`, sheet `BLOC1` (share-price tip card). Ambient volume 40.
- Ambient FX (only if the ambience option is on): typewriter `Tippen.raw` every 12 s, printer `printer.raw` every 30 s (BackgroundFx slots 0/1). [H]
- **Interest perk:** if the player's finance advisor skill is > 75 and the player still has the base loan rate for the difficulty (Normal 6 %, Hard 10 %, Final 15 %), the loan rate is set to the average of the loan rate and the deposit rate. An advisor message `TOKEN_ADVICE 5000` (old → new) is shown. This happens once, since the rate no longer matches afterwards. [H]
- Hint `HINT_BANK` after day 5. [H]

### 2.2 Bitmaps (bank.gli)
`BANK`, `MONI01..05` (5-frame monitor animation on the clerk's desk, repeat mode, prelude 0, speed 200 ticks of the real-time
ticker (one tick = 32 ms real time, sim-core `TickerTime`), so each picture stays 6.4 s and one cycle takes 32 s; drawn at (187,200)), `MONEY` (banknote in the waste bin, drawn at (281,368) while `Sim.MoneyInBankTrash`). [H]

### 2.3 Share-price charts (code-drawn)
For each active player c (0..3), the last 10 days of the share price are drawn as a line chart in that player's colour (blue 0x0080ff, green 0x00ff40, red 0xff0000, yellow 0xffff00). The chart sits in a perspective quad on the wall display. The vertical scale is 0..(1.5 × max of the 10 values). Quad corners (TL, TR, BL, BR):
- P0: (388,41) (408,47) (388,94) (408,94)
- P1: (414,49) (427,50) (414,92) (427,94)
- P2: (388,99) (408,99) (388,152) (408,148)
- P3: (414,99) (427,99) (414,146) (427,142) [H] `Bank::OnPaint`, `DrawChart` (elsewhere)
**Tip on hover:** with the mouse in (378,33)-(436,160), the cell is column (x−378)/30 and row (y−33)/64, giving player index col + 2*row. If that player is active, a share tip card (`BLOC1` + `DrawKursTipContents(viewer, player)`) is drawn at **screen** position (163,23). This uses the primary buffer, so it sits above the room. [H]

**Share tip card contents** (`Tips.cpp: DrawKursTipContents`; coordinates are inside the card, every text in the small
black font, left-aligned; texts are group `Akti` of the standard texts). Viewer = the local player, A = the airline
under the mouse. [H]
- Heading: A's airline name at (32,3).
- Block "share on the market": title `Akti 1000` at (32,20); row labels `1001`, `1002`, `1003` at x 42, y 31/42/53;
  values at x 98, y 32/42/53: current price (money format, price truncated to whole units), A's dividend (plain
  number), dividend yield = dividend × 100 / price (integer division, percent format).
- Block "share in your portfolio": title `1004` at (32,75); labels `1005`..`1007` at x 42, y 86/97/108; values at x 98:
  shares of A the viewer owns (plain number), the viewer's purchase cost of those shares (money), their current value
  = price × owned (money).
- Block "shareholders": title `1008` at (32,130); then for each of the four players (including bankrupt ones) the
  player's own name (not the airline) at (42, 141 + 11·i) and the stake = owned × 100 / A's issued shares (integer,
  percent format) at x 150.
- Mini chart, top right: a dark cyan mid line (0x084848) from (140,48) to (200,48), axes in near black (0x080808) from
  (140,79) up to (140,17) and from (140,79) to (200,79). The 10-day price line in A's chart colour joins the points
  x = 141 + 60·k/9, y = 79 − price[9−k] × 61 / max for k = 0..9 (oldest left), where max = 1.5 × the highest of the 10
  prices (integer arithmetic, whole price units).

### 2.4 Characters
- **Banker 1 (man, `TALKER_BANKER1`, shares)** at (430,160). Idle: `BM_Wait` (2–4 loops), `BM_Scrib` (writing). Turn `BM_Turn`. Talk: `BM_Rede` and `BM_Taste` (typing, sound `BM_Taste.raw`). Listen: `BM_RedeW`, `BM_RedeK`. Turn back: `BM_TurnZ`.
- **Banker 2 (woman, `TALKER_BANKER2`, account & credit)** at (252,150). Idle: `BF_Wait`, `BF_Work`. Turn `BF_Turn`. Listen: `BF_RedeW` (5 loops), `BF_Taste`, `BF_RedeK`. Talk `BF_Rede`. Turn back `BF_TurnZ`.
- **Carnivorous plant** at (406,122): idle `BlumW` (1.5–2.5 s waits). Twitches: `BlumZug` (sound `BlumZug.raw`), `BlumZugB` (sound `BlumZugB.raw`). Snap `Blum` (sound `SnakEat.raw`).
- **Fly** at (504,99): invisible pauses alternate with `fliege.smk` buzzing (3–6 loops). During one buzz variant, frame 24 signals "fly is catchable". The plant then plays its snap, and at snap frame 8 the fly counts as caught. The fly is then hidden for the rest of the visit. Pure ambience, no interaction. [M]
- **Modem** `Modem.smk` at (307,338), looping, shown only while the player has security flag bit 3 (value 8, an anti-hacking item). [H]

### 2.5 Hotspots
| Rect | Cursor | Click |
|---|---|---|
| (462,207)-(601,352) | HOT | dialog `TALKER_BANKER1` (man) |
| (251,155)-(335,262) | HOT | dialog `TALKER_BANKER2` (woman) |
| (62,64)-(173,186) | EXIT | leave |
| (288,388)-(326,410), only while money is in the bin | HOT | **find money**: +100,000 (money reason 2006), net-synced, the item is removed for all (`ATNET_TAKETHING, ITEM_NONE`), then `TALKER_BANKER1` entry 20 (comment) |
[H]
- `Sim.MoneyInBankTrash` is rolled once at game start (25 % chance, seeded by StartTime) and is not re-rolled daily. [H] `Sim.cpp` ~456

### 2.6 Dialog / actions (entry points)
- Banker 1 default: greeting `TOKEN_BANK 900+medium` (905+medium during a takeover, 920 for the found-money entry). This leads to the share-trading tree: buy/sell shares of airline 0..3 opens `MENU_AKTIE` (airline, 0=buy | 1=sell). By phone, trading is refused (1200). [H] `Dialog.cpp` TALKER_BANKER1
- Banker 2 default (in person): choices `TOKEN_BANK 101..103`. 100 gives the balance statement (110/111 + number windows). 101 = **take a loan**: opens `MENU_BANK(limit, 1)`, where the limit is the credit limit rounded down to 1000; 120 if the limit is 0, refused by phone (150). 102 = **repay**: opens `MENU_BANK(credit/2, 2)`; 140 if there is no debt, refused by phone. [H]
- The `KontoType`/statement overlay code in this file is dead (it is never set). Ignore it. [H]

### 2.7 Leave
Exit hotspot / right click. [H]

---

## 3. Airport supervisor's office (Flugaufsicht / "Boss") — `Aufsicht.cpp`

### 3.1 Entry
- Class `CAufsicht(bHandy, PlayerNum)`, `room/aufsicht.gli`, background `AUFSICHT`, room `ROOM_AUFSICHT`, talker `TALKER_BOSS`. HandyOffset 320. [H]
- **Handy:** yes (busy if the boss talker is busy). The boss phone dialog starts at `TOKEN_BOSS 4000+medium` (applications). It ends via 4999. [H]
- The player is **force-walked here every morning at 9:00** (morning briefing); see 3.6. You can also visit during the day.
- On enter (always): extra-edition newspaper cleared, focus cleared, the 18:00 flag reset, ambient volume 30. Sabotage victims of the previous night have their "days without sabotage" counter reset. The airport-window projection anchor is computed from the room's rune in the hall. [H]

### 3.2 Bitmaps (aufsicht.gli)
`AUFSICHT` (bg), `OPAQUE` + `TRANS` (window frame / glass tint over the hall view at (422,0)), `MONI01..03` (desk monitor, 3 frames, random mode, at (329,188)), `ZETTELK04..06` (miniature bulletin-board notes), `NOCARD`, `OBJ_PB OBJ_PV OBJ_PR OBJ_PJ` (per-player desk objects at the briefing), `FRAUFUSS` (a leg/foot of the blue player figure). [H] `glauf.h`
- `NOCARD` at (260,106) is drawn unless the postcard is still available (postcard available, player's `SeligTrust` = 0, not the tutorial). It covers the postcard on the miniature board once the postcard has been taken. [H]

### 3.3 Window onto the hall (live)
- Hall people near the office are drawn **large** inside the clip rect (422,0)-(640,174). Each person's position relative to the room anchor is rotated into a side view: screen x = 640 − 4·dy − 2·dx, screen y = 220 + dx. They are drawn only if |dy| < 150, 380 < x < 700 and |y−220| < 40. Facing direction is remapped by one quarter turn. Then the glass tint (`TRANS`, colour-FX blend mode 2) and the frame (`OPAQUE`) are drawn over it. **[WS]**: hard-coded 640 right edge and clip. [H] `CAufsicht::OnPaint`

### 3.4 Miniature bulletin board
- The wall board shows small copies of the open auction notes. Each note uses the bulletin board's note position (table in 4.3) scaled to x' = (x−91)·74/441 + 274 and y' = (y−20)·68/366 + 55, with sprite `ZETTELK04+slot%3`. Route notes are shown if present and not rented by the viewer. City notes likewise (see bug B-3.1). Gate notes are shown if present. [H]

### 3.5 Characters
- **Boss**, all clips at (344,105): `bb_wait` idle/listen, `bb_base` talking, `bb_hand` gesture, `bb_fing` (raised finger, for sabotage scolding). Looking left: `bb_left` → hold `bb_leftR` → back `bb_leftZ`. Looking right: `bb_righ` → `bb_righR` → `bb_righZ`. Left-to-right sweep: `bb_turnR`, `bb_turnL`. A dedicated sequence (left, right, left, right, back) glares at everyone after a sabotage report. During the briefing the dialog code steers him through three variables to face the player being discussed: players 0/1 = left, players 2/3 = right. [H] clips; [M] semantics
- **Morning only (9:00:00)**, for each active player, figure + halo:
  - Blue P0 at (70,135): `Pb_wait`, `Pb_guck`/`Pb_guckW`/`Pb_guckZ`/`Pb_GuckK` (look), `Pb_fing`/`Pb_fingW`/`Pb_fingZ` (finger tapping). Halo at (94,119).
  - Green P1 at (165,110): `Pv_wait`, `Pv_hand`, `Pv_handB`/`Pv_handW`/`Pv_handZ`, `Pv_WaitK`. Halo at (165,93).
  - Red P2 at (422,142): `Pr_wait`, `Pr_auf`/`Pr_aufw`/`Pr_aufz`, `Pr_uhr` (checks watch). Halo at (528,130).
  - Yellow P3 at (446,186): `Pj_wait`, `Pj_arm`, `Pj_fing`, `Pj_schu`, `Pj_k`. Halo at (583,164).
  - Halo actor: `BombeW` (idle) / `P1Halo..P4Halo`. When the boss reports a sabotage act, the perpetrator's figure goes to the HOLY mood. It holds a pose and the halo animation plays ("innocent look"). [H/M]
  - Desk objects: OBJ_PV at (224,201), OBJ_PB at (72,245) plus FRAUFUSS at (138,373), OBJ_PR at (403,233), OBJ_PJ at (242,249). Draw order: P1 is drawn before the other figures. [H]
- Draw order per frame: background → postcard cover → mini notes → hall window → boss → (morning) P1/P0 objects → monitor → P2/P3 objects → figures and halos. [H]

### 3.6 Morning briefing flow (9:00)
`CAufsicht` ctor, `OnPaint`, `OnLButtonDown`, `OnRButtonDown`, `TryLeaveAufsicht`, `~CAufsicht`; `Stdraum.cpp` TALKER_BOSS opener; `Dialog.cpp` TALKER_BOSS 2000–2102, 3000–3002, 5000+.
1. **Network:** morning entry syncs meetings. Input is blocked (the "waiting for players" network bitmap is shown) until every human player has flagged ready-for-briefing. Each client sends its own ready message on entry. [H]
2. On day 0 in DIFF_ADDON09 ("Uhrig" mission), the special daily orders are added here. [H]
3. In DIFF_NORMAL the game checks which mission connections each player flies (routes rented with utilisation > 20 between the home city and the 6 mission cities, both directions, counted as bits) and stores the count. This is goal logic, not UI. [H]
4. If DayState==1 (briefing pending) and not tutorial, the briefing starts as follows. On day 0, or if the "short briefing" option is off, the full boss dialog runs: entry 1, or 30 if copy-protection failed. Otherwise the short written briefing `MENU_BRIEFING` opens. [H]
5. Full briefing contents (`TOKEN_BOSS`). Opening: day 0 = mission intro (1000+difficulty×100, 1600+ add-on, 1800+ ATFS, 700 free/tutorial). Later days = 2000–2003 random, 2004 if sabotage happened, 5000 if a takeover is happening. Then for each active player in turn: image remark + plane condition remark + crew quality remark (2010–2047). Then players bankrupt (3000/3001) or with image < −990 (20230/20231) are eliminated: GAME OVER menu if local (`MENU_GAMEOVER, 1`), else marked out. Then sabotage reports (2050–2096, with fines = hints × 10,000 moved from saboteur to victim when hints ≥ 100). Ending line 2102 leaves the room. [H]
6. **Auto-advance:** if voice output is off, a click is simulated at (160,100) after 40 s without input, then every 10 s. Pause resets the timer. [H]
7. **Skipping:** right click while the short briefing is open closes it and starts the full dialog. A right click while `bNoTime` is set leaves immediately (DayState → 2). Skipping is **forbidden** (`CanCancel=false`) at 9:00 if any player has image < −990 or money < DEBT_GAMEOVER, someone has won (except missions with a minimum duration: ADDON03/04/06, ATFS09/10 before their target day), any sabotage reporter has hints ≥ 100, or a takeover is pending. In the tutorial, leaving is blocked until tutorial step > 1310. [H]
8. **Leaving in the morning (network):** marks the player ready-for-morning and shows network bitmap (3,1). The actual leave happens once game time is past 9:00 and nobody is still waiting. [H]
9. **On leaving the morning briefing (destructor, simulation side-effects):** the previous sabotage list is cleared. All player figures are placed at their hall start runes (day 0 uses another set) facing the hall. AI players are sent to their offices. Night sabotage (modes 2 and 3) is applied and logged for the next briefing; details belong to the sabotage spec. If any office was bombed, the local player is stopped and the camera follows the bombed player. Plane globe positions are refreshed. ATFS10 mission advisor messages fire on days 3/18/20/25/35/40/45/55. [H]

### 3.7 Daytime hotspots (only when no dialog/menu and not in no-time mode)
| Rect | Cursor | Click |
|---|---|---|
| (64,18)-(172,240) | EXIT | sets DayState=2 and leaves (via the network-aware leave) |
| (269,41)-(362,137) | HOT | **enter the Bulletin board** (`EnterRoom(ROOM_TAFEL)`, section 4) |
| (400,100)-(505,241) | HOT | boss dialog, entry 2 → `TOKEN_BOSS 4000` "applications" |
- Hint `HINT_AUFSICHT` after day 5, from 10:00. [H]

### 3.8 Airport expansion (buy more check-in/gate capacity) — `Dialog.cpp` TALKER_BOSS 4000–4131, `Sim.cpp: NewDay`
- Applications menu (4010/4011/4012): 4010 = mission/goal reminder (4050 → 4060+/4070+/4084+ per difficulty; normal lists home and mission cities). 4011 = **apply for expansion**. 4012 = goodbye.
- 4011 decision order: free gates exist → refusal 4110. Else CheckIn ≥ 5, or ≥ 2 on difficulty ≤ Normal (not free game) → maximum reached 4111. Else day < 8 → too early 4114. Else fewer than 3 days since the last expansion → 4115. Else an expansion is already ordered → 4112. Otherwise offer 4113 → yes/no (4120/4121).
- Yes: expansion is flagged (network `ATNET_EXPAND_AIRPORT`), **1,000,000 is debited** (money reason 3170), confirmation 4130.
- Next day: CheckIn +1, waiting zones +1, baggage = waiting zones, last-expansion date stored, airport layout rebuilt. More gate slots then appear on the board. [H]
- Gates and branch offices themselves are **not** bought here. They are bid on at the Bulletin board.

---

## 4. Bulletin board (Schwarzes Brett) — `Tafel.cpp`

### 4.1 Entry
- Class `CTafel(bHandy, PlayerNum)`, `room/tafel.gli`, background `TAFEL`, room `ROOM_TAFEL`. It is reached only from the Aufsicht hotspot. Not on the Handy. No talker. [H]
- On enter a **snapshot of the auction state** is taken (used for undo). All 21 note bitmaps are pre-rendered. [H]

### 4.2 Bitmaps / animation
`TAFEL`, `ZETTEL04..06` (note paper, slot % 3), `NOCARD` (covers the postcard at (16,290) once it is gone).
**Fly** at (554,253): idle `FlyW01`/`FlyW02` (5–9 loops). When the mouse enters (500,280)-(640,424), the fly takes off: `Fly.smk` (sound `Fly.raw`), then `FlyOut` (away, 10–30 s), then returns `FlyB` (sound `FlyB.raw`), then idle again. [H]

### 4.3 Note layout (`ZettelPos`, 21 slots: 7 route, 7 city, 7 gate)
- Route slots 0..4: (41,15) (245,278) (180,32) (375,281) (322,12); slots 5,6 = (0,0) unused.
- City slots 0..3: (318,138) (470,61) (18,156) (155,170); slots 4..6 unused.
- Gate slots 0..6: (10,289) (132,258) (466,182) (18,156) (375,281) (318,138) (245,278). These reuse city/route spots, so overlaps are possible. [H]
- Hit box = position + size of the note paper.
- Note text (FontSmallBlack):
  - **Gate:** header `TOKEN_MISC 2500` with the gate number (id+1), centered at y≈33. Current high bidder's airline at (13,75). Current price at (13,90).
  - **City (branch office):** header `TOKEN_MISC 2501` (y≈36), city name (y≈58), high bidder (13,85), price (13,100).
  - **Route** (legacy, see 4.6): codes, from, "-", to, bidder, price.
  - Currency format `EINH_DM` (game currency). [H] `CTafel::RepaintZettel`

### 4.4 Interaction
- Exit hotspot (580,369)-(640,423) **[WS]** bottom-right corner, or right click.
- Postcard: while available (and the player has no `SeligTrust`, not tutorial), the rects (25,317)-(188,410), (116,299)-(182,334) and (37,385)-(116,425) are HOT. A click takes `ITEM_POSTKARTE` if the inventory has space; it is then gone for everyone (`ATNET_TAKETHING`). It respawns daily (`Sim.cpp: NewDay` resets item flags). [H]
- **Bidding on a note** (clicks with y < 440). HOT cursor when the note is a city the viewer doesn't already rent and doesn't lead, or a gate the viewer doesn't lead.
  1. *Undo step:* if this note's high bidder differs from the snapshot taken on entry, the note is first restored to the snapshot.
  2. *Bid step:* if the viewer is then not the leader (and for cities does not already rent that city), the price rises by 10 % (integer, of the current price), the viewer becomes leader, and "was interested" is set.
  - Net effect: the first click bids +10 %. A second click in the same visit restores the entry state and then bids again from there, so it changes nothing (it does **not** take the bid back). If someone else bid during the visit, a click resets to the entry state and bids on top of that. [H]
  - Hit order: slips are tested in posting order (routes, cities, gates); the first slip under the click takes it, even when it cannot be bid on (own lead, or a city slip that is not drawn because the viewer already has a branch there). Bids and taking the postcard make no sound.
  - City slips are drawn only for airlines that do not have a branch in that city.
  - In network play, when outbidding a human, an advisor notice is sent to them (`ATNET_ADVISOR`; they see `TOKEN_ADVICE 9002`). [H]
- On leave: the route advisor bubble is cleared, and all city/gate bidder+price pairs are broadcast (`ATNET_TAKE_CITY`). [H]

### 4.5 Daily auction cycle (`Sim.cpp: NewDay`, `CTafelData::Randomize`)
- **Resolution at new day**, for each slot with a bidder:
  - City: the bidder gets a branch-office rent in that city with the note's rank, image 0, and monthly rent = final price. They are charged **3 × price** once (reason 2040).
  - Gate: the first free gate entry gets rent = price and gate number = slot's gate. Charged 3 × price (reason 2042).
  - The local player receives one letter (`TOKEN_MISC 2600` title, 2601 sender, 2602 "won" list, 2603 "lost" list, item labels 2610 city / 2611 route / 2612 gate). It lists only items they had touched. [H]
- **New notes:** `Randomize(min(Date+2,7))` (start of game: 2). All bidders reset.
  - Cities: only if the AI-usage switch `ROBOT_USE_TRAVELHOLDING` is on. In min(N,10) attempts, with a 30 % chance each and at most 3 random cities, a city is picked that fewer than 3 players rent and that is not already on the board. On Normal difficulty, in addition, up to 4 total with non-mission cities. Price = the city's office rent; rank = renters + 1.
  - Gates: every gate number 0..(number of waiting-area gates − 1; 4 on day 0) not rented by any active player is posted, up to 7, with **starting price 3000 − 100×gateNumber**. [H]
- AI players bid on gates and cities during the day (`Player.cpp` robot actions ~4717–4760). [H]

### 4.6 Route notes
Route slots still exist in data, the letter text and the Aufsicht miniature. However, in this build `Randomize` never fills them, the board never draws them, and they are not clickable. Routes are rented at the RouteBox instead. Implement as unused but keep save compatibility. [H]

---

## 5. Travel agency (Reisebüro) — `reise.cpp`

### 5.1 Entry
- Class `CReisebuero(bHandy, PlayerNum)`, `room/reiseb.gli` (single bitmap `REISEB`), room `ROOM_REISEBUERO`. **No dialog partner, not on the Handy.** Ambient volume 60. Closes at 17:00. [H]
- Effects (if the sound-effects option is on): paper rustle `Pap3.raw` every 25 s, seagull `moeve.raw` every 50 s ± 25 s, plus a looping waves sound `wellen.raw` at effects-volume×100/7. [H]

### 5.2 Characters / ambience
- **Clerk girl** at (200,227): `RBWait` (2–4 s), turns `RBDrehH`/`RBDrehF`, types `RBTaste` (frame 4 changes the monitor picture). There is no talk state. [H]
- **Monitor** at (170,318): shows one of `MoniR`, `MoniRE`, `MoniRC`, `MoniRD` (random), and switches each time the girl types. [M]
- **Snake + spider** (only when `Sim.Difficulty > 0` or free game):
  - Snake: idle `SnakW` at (564,213), tongue `SnakZung` (sound), eating `SnakEat` at (494,194) (sound).
  - Spider: if the **local** player does not own `ITEM_SPINNE`, a spider at (380,320) plays bubbles `BLASEW` and runs across in `Spider.smk`. At frame 11 of the run the snake strikes and eats it.
  - **Catch the spider:** while the spider run is playing and the snake hasn't committed, the hotspots are (327,322)-(415,389) for frames 0–9 and (407,365)-(455,410) for frames 10–14. A click gives `ITEM_SPINNE` (if space); the spider vanishes and the snake returns to idle. [H]

### 5.3 Order cards (see 0.2)
- Pool `ReisebueroAuftraege` (6). These are "Ausland"-type orders around the home airport; generation is documented in the orders spec. Card positions (x,y) for slots 0..5: (499,79) (439,212) (420,116) (532,250) (453,289) (535,150). Slots 6..9 are unused (two of them lie at x > 640). [H]
- Tip panel: generic tip anywhere in (403,73)-(623,343), card tip on a card. The tip is anchored at room (160,125), variant TIP_BUYAUFTRAGR. The advisor bubble is cleared when leaving (401,69)-(630,354). [H]
- Take: statistics `STAT_AUFTRAEGE` +1, network order-count sync. **Take animation:** the card shrinks and flies to the **bottom-right (590,440)** over ≈333 ms (progress = 3 units/ms over 1000 units; scale goes linearly from 100 % to ≈29 %). [H] **[WS]** target corner.
- Exit hotspot: left strip (0,0)-(80,440) **[WS]**. [H]

---

## 6. Last Minute — `Lastmin.cpp`

### 6.1 Entry
- Class `CLastMinute(bHandy, PlayerNum)`, `room/lastmin.gli` (`LASTMIN`), room `ROOM_LAST_MINUTE`. No talker, not on the Handy. Ambient 60, `Pap3.raw` every 25 s. Closes at 16:00. The DEMO build shows a "not in demo" request (`MENU_REQUEST`, `MENU_REQUEST_NO_LM`). [H]

### 6.2 Characters / ambience
- **Clerk girl** at (333,225): `LMWait` (1.25–3 s), `LMGrins` (grin), `LMTaste` (typing → monitor changes), `LMWaitK`. Crane sequence: `LMKnopf` (presses button, sound `Kran1.raw`), holds `LMKnopfW` until the crane arrives, `LMKnopfZ` (release).
- **Crane** at (300,129): resting right `KranRS` / resting left `KranLS`. Moves left `KranL` or right `KranR` (sound `Kran2.raw`). The direction depends on where it rests. At frame 21 it signals arrival to the girl. Purely decorative. [H/M]
- **Monitor** at (448,318): `Moni` / `MoniA`, switched by the girl's typing. [M]

### 6.3 Order cards
- Pool `LastMinuteAuftraege` (6). These are same-day orders; generation is in the orders spec. Slot positions 0..5: (66,79) (126,212) (145,116) (33,250) (128,289) (15,150). [H]
- Tip anywhere in (17,73)-(237,343), anchored at (300,125), TIP_BUYAUFTRAG. The advisor is cleared when leaving (8,69)-(242,347). [H]
- Take: statistics `STAT_AUFTRAEGE` +1 and `STAT_LMAUFTRAEGE` +1. The card flies to the **bottom-left (16,440)** over ≈250 ms (4 units/ms) with the same shrink. [H]
- Exit hotspot: right strip (560,0)-(640,440) **[WS]**. [H]

---

## 7. Freight counter (Fracht, add-on) — `Fracht.cpp`

### 7.1 Entry
- Class `CFrachtRaum(bHandy, PlayerNum)`, `room/fracht.gli` (`FRACHT`), room `ROOM_FRACHT`. Talker `TALKER_FRACHT` (freight master). HandyOffset 320 is set, but the room is **not** in the phone list. Ambient 60, `Pap3.raw` every 25 s. No closing time. [H]
- Sounds loaded: `Kiste1.raw`, `Kiste2.raw` (crate landing), `Warning.raw` (drop alarm). [H]

### 7.2 Bitmaps (fracht.gli)
`FRACHT`, `KISTE01..06` (one crate graphic per slot), `SEIL01..03` (rope, index slot/2), `STEMPEL` (stamp), `MASK` (crane foreground, drawn at (333,0) after the crates), `NOGLUE` (covers the glue pot at (577,396) when the glue is gone or the player has it). [H]

### 7.3 Characters
- **Freight master** at (485,50). Idle: `FM1a` and `FM3` (random, waits 0–1 s). Buzzer `FM2` (frame 5 triggers the crate drop). Turn to front `FM4`. Listening: `FM4SPaS1/2/3`. Talking: `FMSPa1..4`. Turn back `FM4back`. Dialog bubble at (556,126). [H]
- **Forklift:** `Fa1` at (60,240) and `Fa2` at (0,190), alternating with 0.5 s pauses. [H]

### 7.4 Freight crates (orders)
- Pool `gFrachten` (6). Crate top-left positions (`KistePos`): (103,235) (286,12) (268,192) (30,−61) (26,131) (181,57). Text offsets inside each crate (`ZettelOffset`): (−7,99) (−7,90) (0,84) (−11,87) (−4,69) (2,59). Each crate hangs from two stacked rope segments above it, at crate x + width/2 + rope x-offset (−10,−6,−4,−4,−4,−4). Crates are painted from slot 5 down to 0. [H]
- Crate text = the 4-line card text (0.2) printed on the crate at the offset.
- Hit box: crate x-range, from crate top **+30** to crate bottom. Tip at (400,125), TIP_BUYFRACHT. The advisor is cleared when leaving (0,0)-(485,439). [H]
- **Take:** add to the player's freight list (grows by 10 if needed), `paptake.raw`, net sync, `STAT_FRACHTEN` +1. The crate is marked "taken, waiting". The freight master is forced into the **buzzer** clip. At its frame 5 the warning sound plays, and the crate drops. It falls by p²·600/10⁶ px as p goes 0→1000 over 1 s (accelerating, 600 px total, so off-screen), with the `STEMPEL` stamp drawn on it. When it lands, `Kiste1` or `Kiste2` plays (random) and the slot becomes empty (−1). [H]

### 7.5 Hotspots
| Rect | Cursor | Click |
|---|---|---|
| (567,386)-(630,429), only if glue is still there and the player has none | HOT (id 12) | glue state 0 → freight master entry 1010 (refuses). State 1 → take `ITEM_GLUE` (if space); state 2 for all (`ATNET_TAKETHING`) |
| (620,0)-(639,439) or (440,357)-(639,430) | EXIT | leave **[WS]** right edge |
| (465,0)-(639,309) | HOT (id 11) | freight master dialog, entry 1000 |
- **Glue puzzle:** using the paperclip item here (glue state 0, player without glue) plays entry 1020, drops the paperclip and sets glue state 1. Glue state resets to 0 each day. The paperclip comes from the RouteBox (8.5). The freight-master dialog has no branches: any answer ends it (`Dialog.cpp` TALKER_FRACHT). Item-use replies use entry 10000+item. [H]

### 7.6 Leave
Exit / right click. Crates still falling are emptied, then refill runs. [H]

---

## 8. Route office (RouteBox) — `RouteBox.cpp`

### 8.1 Entry
- Class `CRouteBox(bHandy, PlayerNum)`, `room/routebox.gli` (`ROUTEBOX`), room `ROOM_ROUTEBOX`. No talker, not on the Handy. Ambient 60. Remembers the list page per player. [H]
- On leave: the page is saved, and the player figure is turned to look direction 2 and released (no direct-to-room). [H]

### 8.2 Bitmaps (routebox.gli)
`ROUTEBOX`, `P1_01..P4_03` (player toggle buttons: off/hover/on per player), `LINKS`/`RECHTS` (dog-ear page corners at (451,313)/(607,313)), `FILTER1..3` (filter tab strip at (453,326), one image per active filter), `HAKEN` (check mark), `HERE` (mission-city marker), `NOPAPER` (shown at bottom-left (0, 440−h) once the paperclips have been taken). [H]

### 8.3 Layout
- **Map** panel: room (19,28), 415×240, redrawn from the background plus lines.
- **Route list**: (461,29), 172×310. 22 rows of 13 px, then a "page / pages" footer.
- **Info sheet** (tip): (26,275), 177×157. Drawn **only while the paperclips are still in the room** (`Sim.ItemClips`). The same condition applies to the player toggles. This is the joke: the clips hold the sheet. [H]
- Player toggle positions: P0 (241,267), P1 (248,335), P2 (304,258), P3 (313,328). [H]
- Map projection of a city: map position × 1.925, then x × 42/60 and y × 95/90, then + (198,150) inside the map panel. Add (19,28) for room coordinates. Routes whose endpoints are ≥ 180 map units apart in x wrap around: they are drawn as two segments to the panel edges x=1 / x=437 at an interpolated height. Only one direction of each route pair is drawn (from-id < to-id). [H]

### 8.4 Rentability rules (computed on entry)
A route is **offered** (black, white on the map) if one of these holds:
- (a) it is not rented by the player and touches the home airport; or
- (b) it shares a city with any route on which the player has utilisation ≥ 20 %.
It is **not offered** if it is not rented and its "days with low use" counter is < 7. For an unrented route that counter counts up by one per day (to at most 99). Giving a route up **voluntarily** sets it to 99, so the route can be rented back at once; only a **revocation** for low use (20 days under 10 % load) or a theft sets it to 0, which blocks the route for that airline for the next 7 days. (Corrected: an earlier version said a voluntary drop blocks the route for a week.) Rented routes are shown with a check mark and in black. Others are grey. [H]

### 8.5 Hotspots & interaction (only when no dialog/menu)
| Region | Cursor / tooltip | Action |
|---|---|---|
| (444,380)-(640,439) | EXIT | leave **[WS]** |
| (449,314)-(469,332), page > 0 | LEFT | previous page (sound `gMovePaper`). Double-click works too |
| (607,314)-(628,332), page < last | RIGHT | next page |
| (456,328)-(500,361) / (501,330)-(541,363) / (542,331)-(582,362), not the current filter | HOT, tooltips 4600/4601/4602 | filter 0 = all, 1 = rentable & not yet mine, 2 = mine. Resets to page 0 |
| (0,245)-(36,287), (186,276)-(214,322), (225,269)-(245,314), (347,280)-(383,320) while clips are present and the player lacks `ITEM_PAPERCLIP` | HOT | take `ITEM_PAPERCLIP`. Removed for everyone, back next day |
| list rows (461,29)-(632,314) | HOT | hover selects the route (rented/offered rows get a cursor highlight bar (458..635, row)). Click = rent/cancel |
| map (17,27)-(434,295), excluding (9,268)-(200,434) and (230,254)-(361,387) | HOT when within 10 px of a route line | same as the list. Nearest line wins (distance from the point to the segment) |
| player toggle (own always, others only with the Info advisor) | HOT (hover frame) | toggles that player's routes on the map |
- **Hover effects:** the selected route (and its reverse) is drawn fat. The info sheet shows: route name; distance (`TOKEN_AUFTRAG 1007`, km); potential passengers (`TOKEN_ROUTE 998`); demand (999). Rent (1006) shows your rent if rented, else the base rent; if rented, also utilisation (1003). If anyone visible rents it, a renter list follows (1016). Each line is rank + airline, plus utilisation % with the Info advisor; otherwise only your own name, with others shown as `TOKEN_ROUTE 997`.
- The route advisor (if hired) comments once per newly hovered route. Big-city remarks use `TOKEN_ADVICE 8000/8001/8002`; demand trend 8010 (factor > level+0.5) or 8011 (< level−0.3); blocked route 8020. [H]
- **Click on a route:**
  - Rented and used by any of your planes' schedules (either direction) → `MENU_REQUEST / MENU_REQUEST_NORENTROUTE3` (cannot give up).
  - Rented and unused → `MENU_REQUEST_KILLROUTE (route, reverse)`.
  - Not offered → `NORENTROUTE4` (recently dropped) or `NORENTROUTE2` (not connected).
  - Offered: the rank would be highest existing rank + 1. If that is > 3 → `NORENTROUTE1` (fully let). Else `MENU_REQUEST_RENTROUTE (route, reverse)`. The request text shows the one-off fee = rent/30 and the rent (`TOKEN_MISC 3010+request`).
  - Confirm handling (sets ranks, money, low-use counter 0/99) is in `Stdraum.cpp` menu code. Owned by the menus writer. [H]
- **Map drawing passes:** not offered (grey, mix of 0x5e5e5e and 0xd0a890), then offered (white), then routes rented by enabled players. Those use the player colour; multiple renters give a multi-colour line. On DIFF_NORMAL the six mission cities get the `HERE` marker. [H]

---

## 9. Widescreen notes [WS] — summary
- Every room is a fixed 640×440 painting with absolute hotspots. Recommended: render the room in a 640×440 pillarboxed/anchored canvas and map the mouse into it. If you extend backgrounds instead, the edge-anchored items below must move with the edge:
  - Exits on the screen edge: Last Minute (right strip x ≥ 560), Travel agency (left strip x ≤ 80), Freight (x ≥ 620 plus bottom-right block), RouteBox (bottom-right block), Tafel (bottom-right 580..640).
  - Animations ending at the edge: Personnel paper ball (ends at x > 640), card fly-away targets (590,440) and (16,440), falling freight crates (600 px drop), and the Aufsicht hall window (clip to 640, projection uses 640).
  - Screen-space overlays: Bank share tip at screen (163,23). The Bank statement overlay is centered on the window (dead code).
- Unused position table entries at x > 640 (Travel agency slots 7 and 9) are harmless.

## 10. Bugs / quirks found (decide: replicate or fix)
- **B-3.1** The Aufsicht miniature board tests city notes with a truthiness check, while the board uses −1 as "none" and city index 0 is valid (Amsterdam). Effect: the city at index 0 never shows, and −1 slots cause an out-of-range rent lookup. Fix: use ≠ −1. [H]
- **B-4.1** Bulletin board, gate bid network notice: it reads the *city* slot array with the gate slot index (out of range). Fix: use the gate slot. [H]
- **B-4.2** Bulletin board sets its cursor with the wrong area id (ROOM_ARAB_AIR, cosmetic only). [H]
- **B-6.1** Last Minute never initialises its monitor variable (garbage first decision). Init to −1. [H]
- **B-7.1** The freight room releases a reference on the Arab talker it never took, and never references `TALKER_FRACHT`. Fix with a symmetric reference on the freight talker. [H]
- **B-8.1** RouteBox `CurrentTip`/`LastTip` are not initialised in the ctor. Init both to −1. [H]
- **B-2.1** Bank phone entries both check BANKER2 for busy. [H]
- **B-5.1** The Travel agency spider actor exists only if the player lacked the spider at entry, yet it is pumped and drawn unconditionally (whenever snake mode is on). Guard it. [H]

## 11. Open questions
- **Q-0.1** Default `Minimum` for the order-counter refills (Auftrag.h / Fracht.h prototypes, not read). Affects how fast empty card slots reappear.
- **Q-2.2** (answered in §2.3: share tip card contents.)
- **Q-2.3** Should Bank phone entry 1 check BANKER1 for busy (likely intended)?
- **Q-3.1** The exact 1/20 s timing of the Boss turning between players. Implement as "face left for players 0–1, right for 2–3, glare sequence after sabotage reports".
- **Q-4.1** `ROBOT_USE_TRAVELHOLDING` — which game modes enable city notes. See the robot-usage table (`RobotUse`).
- **Q-8.2** (resolved) The low-use counter: rented routes count days under 10 % load (reset when the load recovers, revoked at 20); unrented routes count up daily to 99. A voluntary give-up sets 99 (not blocked), a revocation or theft sets 0 (blocked for 7 days).
- **Q-1.1** The two personnel clerks' paper hand-off loop is inferred. Verify visually in the original that the woman's letter goes into the man's bin.
- Layout cross-check against the manual PNGs (`scratchpad/manual_png/pNN.png`) was not done. Coordinates above come from source only.
