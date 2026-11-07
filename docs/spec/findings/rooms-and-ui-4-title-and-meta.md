# Rooms & UI 4: Title, new game, options, credits, intro/outro, save/load UI

Clean-room behaviour spec. Everything below is a paraphrase of what the original
does. It contains no source code. In-game texts are named by resource file plus
group/id: `std_ger.res` (loaded as `StandardTexte`), with group tokens `NewG`,
`Misc`, `Cred`, `Sche` and so on. Coordinates are in original 640x480 screen
pixels unless stated otherwise.

Confidence tags: **[H]** read directly from the code; **[M]** inferred from
the code with one assumption; **[L]** a guess.

Widescreen flags are marked **[WS]**.

---

## 0. Source map (traceability)

| Area | Files / functions |
|---|---|
| App boot, loading screen, main state loop | `Takeoff.cpp: CTakeOffApp::InitInstance`, `CTakeOffApp::GameLoop` |
| Gamestate constants | `defines.h` (GAMESTATE_*) |
| Title stub | `Titlepopup.cpp: TitlePopup::TitlePopup` |
| Main menu / new-game wizard / network lobby | `NewgamePopup.cpp: Konstruktor, RefreshKlackerField, OnPaint, OnLButtonDown, OnRButtonDown, OnTimer, OnChar, OnKeyDown, CheckNames, PushNames, PushName` |
| Options + save/load slots | `Options.cpp: Options::Options, ~Options, UpdateSavegameNames, RefreshKlackerField, OnPaint, OnLButtonDown, OnRButtonDown, OnChar, OnKeyDown` |
| Split-flap board | `Klacker.cpp: KLACKER::*`, `CKlackerPlanes::*`; `class.h` (KLACKER, CKlackerPlanes); `Global.cpp` (KlackerFntDef) |
| Credits | `Credits.cpp: CCredits::*` |
| Intro | `Intro.cpp: CIntro::*` |
| Outro | `Outro.cpp: COutro::*`; triggers in `Stdraum.cpp` (MENU_GAMEOVER handling) |
| Global hotkeys (F2/F3/F4/ESC, intro/outro ESC routing) | `Gameframe.cpp: GameFrame::OnKeyDown`, `GameFrame::OnChar` (cheat unlock) |
| Savegame file I/O | `Sim.cpp: SIM::SaveGame, SIM::LoadGame, GetSavegame*`; autosave in `Sim.cpp` (time tick) and `Takeoff.cpp: GameLoop` |
| Highscores | `Sim.cpp: SIM::AddHighscore, SaveHighscores, LoadHighscores`; `Player.cpp` (daily update) |
| Editor-only hall loader | `HalldiskMenu.cpp` (opened from `AirportView.cpp` when the Editor flag is set) |
| `Mainfrm.cpp`, `Init.cpp` | Not relevant to the UI flow. `Init.cpp` only resolves paths and copy protection. `Mainfrm.cpp` forwards messages to `TopWin`. |

---

## 1. App-level state machine

### 1.1 Gamestate values  [H] (`defines.h`)

| Name | Value | Meaning |
|---|---|---|
| BOOT | 0 | "Go to the main menu": the loop tears everything down and rebuilds |
| TITLE | 1 | Title shown |
| INIT | 2 | Main menu, new-game wizard, credits or options opened from the main menu |
| PLAYING | 3 | In game |
| QUIT | 4 | Leaving |
| CREDITS | 5 | Request: open credits |
| OPTIONS | 6 | Request: open options from the main menu |
| INTRO | 7 | Request: play the intro |
| OUTRO | 8 | Request: play the base-game outro |
| OUTRO2 | 9 | Request: play the add-on outro |
| flag WORKING | 32 | The current screen is active |
| flag DONE | 64 | The current screen has finished and can be replaced |
| flag PAUSED | 128 | Pause |

A screen asks for a transition by writing a bare request value (5, 6, 7, 8, 9 or 0).
The game loop polls this every frame, deletes the current full-screen window
(`TopWin`) and creates the next one. The game loop runs at 20 sim steps per second.

### 1.2 Startup sequence  [H] (`Takeoff.cpp: InitInstance`)

1. Read the language from `misc\sabbel.dat`. Read the registry options. Parse the command line
   (`/window`, `/load N`, `/novgaram` and so on). Init DirectX and create the frame.
2. Load `room\titel.gli` and `room\titel2.gli`. Init the sound system.
3. **First run** (the registry flag *viewed intro* is false and the intro path exists):
   play the intro (see §6) in a blocking loop. The backdrop is then `titel.gli:TITEL`.
   **Later runs:** the backdrop is `titel.gli:SPELLOGO` (the developer logo).
4. Run a long sequence of loading steps. Before each step the backdrop is redrawn and one
   status line is printed in the big white font in rect (2,458)-(640,480).
   These loading strings are hard-coded English/Dutch/French debug strings,
   not resources. OpenAT may substitute its own. **[WS]**
5. Partway through ("initializing screen") the backdrop switches to `titel.gli:TITEL`.
   After MIDI init, if the language is not Dutch, it switches to `titel2.gli:TITEL`
   (the Deluxe title art). MIDI starts here only if the intro was already viewed.
   Otherwise the intro's teardown starts the music.
6. During loading the mouse is forced to (600,440) **[WS]**. Then the game loop
   starts with gamestate BOOT.

`room\runesoft.gli` exists in the install but is **not referenced** anywhere in
this source **[H]**. It is probably a publisher logo from another SKU. Leave it out unless wanted.

### 1.3 BOOT handling  [H] (`GameLoop`)

On BOOT the loop deletes `TopWin` and every player's location and dialog windows.
Then:
- If the intro was viewed (or there is no intro path), it creates **TitlePopup** with state
  TITLE|WORKING.
- Otherwise it creates the intro.

**TitlePopup does nothing.** Its constructor immediately marks the state DONE. It
paints nothing and ignores clicks. On TITLE|DONE the loop replaces it with
**NewGamePopup** (state INIT|WORKING). In practice, then, BOOT means "show the main menu".
OpenAT can skip TitlePopup.

### 1.4 Flow diagram

```
 [process start]
      |
      v
 first run? --yes--> INTRO(blocking; ends -> crossfade to TITEL, 3s) --+
      | no                                                             |
      v                                                                v
 SPELLOGO backdrop ----> loading steps (TITEL, TITEL2 backdrops) ---> BOOT
                                                                       |
     +-----------------------------------------------------------------+
     v
  BOOT --> (TitlePopup, instant) --> MAIN MENU  [NewGamePopup page 0]
                                       |  |  |  |  |  |  \
         +-----------------------------+  |  |  |  |  |   \-- Quit --> exit
         |        +-----------------------+  |  |  |  +-- Highscores (page 7) -> back
         |        |        +-----------------+  |  +-- Home airport (page 5) -> back
         |        |        |                    +-- Network (pages 13/15/17/18) -> page 99
         v        v        v
   CREDITS     OPTIONS   INTRO (replay)        Free game (page 2) / Campaigns (150->1/12/122->2/14)
   (any input  (INIT     (click/ESC -> BOOT)          |
    -> BOOT)    context)                              v
                 |  \__ Load (page 5) --select--> LoadGame -> PLAYING
                 \___ back -> BOOT                    |
                                             page 99 "loading" --> INIT|DONE
                                                                       |
                                                                       v
                                           PLAYING (all players enter airport, walk to Uhrig)
                                                |   ^
                    ESC/F2/F3/F4 -> Options room |   | back (LeaveRoom)
                                                v   |
                                     In-game Options (pages 1..8)
                                                |
                     "Leave game" confirm (page 8) --> BOOT
                     Game over menu: won final mission  --> OUTRO  (outro.smk)  --> BOOT
                                     won add-on final   --> OUTRO2 (outro2.smk) --> BOOT
                                     otherwise          --> BOOT
```

INIT|DONE (set by the wizard's loading page) means: delete the menu.
- If started with `/load N`, load slot N-1.
- Otherwise put all players into the airport room, show the status bar, set state
  PLAYING|WORKING, start day state 1 and send every player walking to the Uhrig
  (Aufsicht) room. Tutorial mode starts if the difficulty is the tutorial.

---

## 2. The split-flap board ("Klacker")  [H] (`Klacker.cpp`, `class.h`, `Global.cpp`)

The main menu, the new-game wizard and the options all use one shared widget: a
departure-board grid whose cells flip character by character.

### 2.1 Grid and geometry
- **24 columns x 16 rows.** Each cell is **16 x 22 px**. The board origin is **(128, 63)**,
  so the board covers x 128..511 and y 63..414 **[WS]**: the board sits at a fixed position
  on the 640x480 backdrop `startup.gli:HAUPTMNU`.
- Hit test: column = (mouseX-128) div 16, row = (mouseY-63) div 22. Division truncates
  toward zero. Quirk: x 113..127 also counts as column 0, and y 42..62 counts as row 0.
- Text is written left to right from a (col,row) position. It is truncated at column 23. There is
  no wrap. Each screen redraws the whole board from scratch whenever anything changes
  (clear, then print every line again).

### 2.2 State
- Two 384-byte arrays: **target** (what each cell should show) and **current**
  (what it shows now). Both hold indices into the board's character set.
- A per-row "line disabled" flag (16 rows) makes a row draw in the dark glyph set.
- Clearing sets the target to all blanks and re-enables every row. The current array is untouched,
  so the old text flips over to the new text.
- **Warp** copies target to current at once. It is used whenever a selection must change
  instantly, without animation: selecting a list entry, moving a slider, and just
  before leaving a screen.

### 2.3 Character set (index order matters for the flip animation)
Index 0 is the blank. Then, in order:
- indices 1-26: A..Z
- indices 27-29: Ä Ö Ü
- indices 30-39: digits 0..9
- indices 40-55: `. ! : ; , ? ( ) - # = * | > < +`
- 15 Czech accented capitals, then 8 Polish capitals, 3 more, and 3 Spanish/Portuguese
  capitals (indices 56-84)
- index 85: a "human" pictogram (mapped from code point 0xB2)
- index 86: a "computer" pictogram (mapped from code point 0xB3)

Total: **87 lit glyphs**.

On input, text is upper-cased. For languages without accent glyphs, accented vowels
fold to plain vowels (é→E and so on). Czech, Polish and Spanish/Portuguese have their own
mapping to the extra glyphs. Unknown characters become blank.

### 2.4 Bitmaps (`room\klacker.gli`, header `glklack.h`)
- Lit glyphs: `KL_SP KL_A..KL_Z KL_AE KL_OE KL_UE KL_0..KL_9 KL_PKT KL_AUSR KL_DPKT
  KL_SEMI KL_KOMMA KL_FRAGE KL_KLA KL_KLZ KL_MINUS KL_BULL KL_GLCH KL_MAL KL_KREUZ
  KL_MORE KL_LESS KL_PL`, then the accent glyphs `KL_193 … KL_128`, then `KL_HUMAN
  KL_COMPI`.
  The names map to the characters in §2.3 order: '#'→BULL (bullet), '|'→KREUZ (the
  volume-slider knob), '*'→MAL, '='→GLCH.
- Dark glyphs: the same sequence with the `KD_` prefix, without HUMAN/COMPI. The dark
  glyph for character *i* sits at lit index + **87**. Quirk: in the dark list the '+'
  slot reuses the lit `KL_PL` bitmap.
- Text cursor: 8 animation frames `KL_K1..KL_K8`, cycled by a blink counter that
  increments on every 50 ms timer tick. They are blitted with transparency.
- Sounds: `Klack0.raw`, `Klack1.raw`, `Klack2.raw`.

### 2.5 Flip animation (one "klack" step)
Each step (driven by the 50 ms window timer, about 20 Hz; the code comment says
10 fps) works on every cell whose current value differs from its target:
- Distance = how many steps forward the target is in the cyclic character order.
- Glyphs to advance this step, by distance: more than 20 → 9; 11-20 → 7; 7-10 → 4;
  4-6 → 2; otherwise 1.
- Cells always roll **forward** only, with shortcuts. When a rolling cell reaches the
  start of a later group (index 27 umlauts, 30 digits or 40 punctuation) and its target
  lies before that group, it jumps back to blank. It also jumps back to blank before ever
  showing the last two (pictogram) glyphs. If the cell would overshoot its target, it
  snaps to the target.
- Sound: if any cell moved this step, play Klack0. If more than 20 cells moved, also play
  Klack1. If more than 50, also play Klack2. Volume is effects-volume x 100 / 7 percent.
  The sound is "fire and forget": overlapping playback is allowed.
- "Finished" means every current value equals its target.
- If the timer cannot be created, one step runs per paint instead.

### 2.6 Rendering rules
- Each cell is drawn as the lit or dark glyph of its *current* index, at
  (128+16*col, 63+22*row) (except on the player page, see §3.6).
- Blank cells (index 0) are usually **not** drawn, so the backdrop shows through.
  Exceptions:
  - On the main-menu/wizard screen, rows 13-15 always draw every cell, blank tile included.
  - On the player-selection page, row 15 skips blank cells.
- Wizard quirk: after rows 1, 3, 5 and 7 the code blits a row of blank tiles at an
  off-screen y. This has no visible effect.

### 2.7 How a "line" becomes clickable
There are no widgets. Each screen compares the (row, column-range) under the mouse
with the rows it printed:
- **Hover:** if the mouse is over an active row, the cursor changes to the "hot" cursor
  (in the main menu, the "exit" cursor over Quit). There is **no** highlight on the
  board itself.
- **Click:** the click is matched to the same (row, column) rules. Every left click in these
  screens plays `change.raw` at effects volume, whether or not it hit anything.
- **Disabled/locked rows:** these use the dark glyph set and get no hotspot.

### 2.8 Decorative "klacker planes" (screensaver)  [H] (`CKlackerPlanes`)
Used by the main menu and options.
- An idle counter is reset on every mouse move, key press or character.
- The counter increments **once per paint**, so it depends on frame rate. OpenAT should
  use a time base.
- After 600 idle ticks (and at faster rates after 800 and 1100) a plane spawns in one
  of 10 slots:
  - random direction, moving ±10 px per tick
  - random airline livery (0-3)
  - fuselage length 0..7 middle segments, growing with idle time
  - random y between 80 and 479
- Planes enter at x=640 (moving left) or at x=-190-35*size (moving right). They are removed off-screen **[WS]**.
- A plane whose y is close to the mouse (or to the text cursor while typing) drifts 4 px
  per tick vertically to avoid it.
- Planes are built from the shared universal plane bitmaps (nose, body segments, tail,
  engines, logo overlays) and drawn over the board.
- Spawn rule: with t = idle ticks, a plane starts when (t > 600 and t mod 50 = 0) or (t > 800 and
  t mod 44 = 0) or (t > 1100 and t mod 47 = 0), in the first free slot; fuselage segments =
  min(7, random(0 .. (t − 580) / 30)). Moving planes are retired at x > right edge (flying right) or
  x < −190 − 35·segments (flying left). Dodging: when the avoid point's y lies in (y − 100, y + 20) the
  plane moves 4 px up if the point is below y − 50, 4 px down if above.
- Part layout (y = the plane's base line; list indices of the universal plane bitmaps: 4..8 engine and
  wide pieces `FLUG07..11`, livery l ∈ 0..3 uses 9 + 5·l + part with parts 0..4 = `FLUG?01/02/03/06/07`,
  29 `FLUGB08`, 30 `FLUGB99`, 31 `FLUGB98`, 32 `FLUG12`; h(i)/w(i) = bitmap height/width;
  half = w(body of the livery) × segments / 2):
  - **Flying left** (nose first): neutral nose 30 at (x, y − h(9)), livery nose over it for l > 0;
    x += w(nose); per segment neutral body 31 at (x, y − h(10)) + livery body, x += w(body);
    upper engine 4 at (x − w(10)·segments/2 − w(4)/2 + 60, y − 118); neutral tail 29 at
    (x, y − h(11) + 19) + livery tail; lower engine 6 at (x − w(10)·segments/2 − w(4)/2 + 40, y − 48).
  - **Flying right** (tail first): upper engine 32 at (x − 47 + half, y − 119); tail 13 and the
    livery's part 4 at (x, y − h(13) − 31); x += w(part 4); body segments as above; nose 12 and the
    livery's part 3 at (x, y − h(12)); lower engine 5 at (x₀ + half − 20, y − 46) with x₀ the start x.
- OpenAT: one shared state for the main menu, the new-game pages and the options (the in-game options
  reset it on entry); ticks at a fixed 20 per second (the original counted paint frames, Q7); planes enter
  at the right screen edge on wide screens.

---

## 3. Main menu and new-game wizard (`NewGamePopup`)

### 3.1 Common frame  [H]
- Room library: `room\startup.gli`. Background `HAUPTMNU` (640x480 **[WS]**).
  Also used: `PLAYER0..PLAYER7` (portraits) and `HAKEN` (check mark).
- On open:
  - Load highscores.
  - Load font `misc\stat_1.mcf`.
  - Click sound `change.raw`.
  - Start a 50 ms timer.
  - Set the mouse look to normal.
  - On first ever use, the default home airport is set by language (Berlin; Paris for French;
    Roma for Italian; Madrid for Spanish/Portuguese; London for English; the Dutch spelling of Berlin for Dutch). Verified against the running GOG English build (fidelity pass): first start shows London.
  - Reset every player to logo = index and owner = computer, then make the last chosen player
    (option *last player*, 0..3) human.
  - Fill player name, airline name and 2-letter abbreviation from the saved options. If
    a saved value is empty, use defaults `NewG` 1000+10c (name), 1001+10c (airline) and
    1002+10c (abbreviation), padded to 21, 21 and 2 characters. Then upper-case them.
- The **version string** is drawn bottom-right, right-aligned in rect (0,429)-(519,480)
  in the small red font **[WS]**. It also serves as the network join version check.
- **Copy-protection "bad" state:** when a check fails, a letter is appended to the version
  and the main menu shows only the Quit line. OpenAT: ignore this.
- **"Spawn" mode** (no CD; network join only):
  - Free game, campaigns, network(!), blank row 6 and intro are flagged as disabled (dark).
  - Hotspots for free game, campaigns, load and intro are suppressed.
  - Quirks: the Network row still has a hotspot even though it is drawn dark. The
    Load row is not dimmed even though it is inert.
- Leaving the screen writes the names back into the options (still in capitals) and stores the
  chosen mission as *last mission* (one variable each for the base, add-on and Flight Security
  campaigns). The players' names and airline names used in the game are then **re-capitalised**: the
  first letter and every letter after a space or a hyphen in upper case, all other letters in lower
  case (umlauts included); abbreviations stay as typed. Verified on the running original (letters and
  the briefing card show mixed case). It also creates trimmed copies of names and airlines. If the tutorial was
  chosen, it loads `glglow.gli` tutorial markers `TUTMARK1..5`.

### 3.2 Page numbers  [H]

| Page | Purpose |
|---|---|
| 0 | Main menu |
| 150 | Campaign chooser |
| 1 | Base-game missions (tutorial plus 5) |
| 12 | Add-on missions |
| 122 | Flight Security (ATFS) missions |
| 2 | Player selection (free game or base mission) |
| 14 | Player selection (add-on or ATFS mission) |
| 5 | Home airport chooser |
| 7 | Highscores |
| 13 | Network: choose transport/provider |
| 15 | Network: session list (join / create / load net save) |
| 17 | Network: create session (name, cheats toggle) |
| 18 | Network: lobby (player selection while waiting for others) |
| 99 | "Loading…" (starts the game) |

Every page except 0 and 99 prints a full row of `=` on row 1 (the title underline).

### 3.3 Page 0 - main menu  [H]

| Row | Col | Text (`NewG` id) | Hotspot cols | Action |
|---|---|---|---|---|
| 0 | 0 | 500 (title) | - | - |
| 1 | 0 | `====…` | - | - |
| 2 | 1 | bullet + space + 501 (free game) | 1-20 | difficulty = free game → page 2 |
| 3 | 1 | 515 (campaigns) | 1-20 | → page 150 |
| 4 | 1 | 514 (network game) | 1-20 | → page 13; selection = last provider |
| 5 | 1 | 506 (load game) | 1-20 | set "wants to load" plus "came from menu", warp the board, request OPTIONS. Options opens directly on its load page. |
| 7 | 1 | 504 (home airport) | 1-20 | → page 5 (sub-page set so the current airport is visible) |
| 8 | 1 | 507 (options) | 1-20 | warp, request OPTIONS |
| 10 | 1 | 505 (intro) | 1-20 | warp, request INTRO |
| 11 | 1 | 508 (credits) | 1-20 | warp, request CREDITS |
| 12 | 1 | 509 (highscores) | 1-20 | → page 7 |
| 14 | 1 | 510 (quit) | 1-20 (exit cursor) | quit the application (the demo build instead shows a quit message) |

Right-click on page 0 does nothing.

### 3.4 Page 150 - campaigns  [H]
- Row 0: text 515 with its first two characters dropped (it reuses the menu label without the
  bullet prefix [M]). Row 1: underline.
- Row 2, col 1: 502 (the original missions) → page 1. The difficulty is preset to *last mission*.
- Row 3: 512 (add-on missions) → page 12, preset to *last mission 2*.
- Row 4: 513 (Flight Security missions) → page 122, preset to *last mission 3*.
- Row 6, col 0: 4001 (back) → page 0.
- Hotspots cover cols 1-20 of rows 2, 3, 4 and 6.

### 3.5 Mission pages 1 / 12 / 122  [H]
- Row 0: 1999 (missions title). Row 1: underline.
- Rows 2…: one mission per row, printed from col 1. The current selection gets an arrow
  prefix `==>`; other rows get 3 spaces. Names therefore start at col 4.
  - Page 1: difficulties 0..5 (tutorial, first … final). Text `NewG` 2000+100*d. Rows 2..7.
  - Page 12: difficulties 11..21. Text 3000+d. Rows 2..12. Note: 21 is past the last add-on
    mission (20), so row 12 is probably blank or unused [M].
  - Page 122: difficulties 41..50. Text 3000+d. Rows 2..11.
- **Locked missions** (index above the unlocked maximum: *max difficulty*,
  *max difficulty 2*, *max difficulty 3*) use dark glyphs and have no hotspot.
  - The add-on and ATFS lists are fully unlocked once their maximum equals the last mission
    of the set.
  - Defaults when no registry values exist: base 0, add-on 11, ATFS 41.
- **Click on a mission row:** set the difficulty to that row's mission, clamped to the
  unlocked maximum. Redraw and warp (no flip animation).
  - Quirk: the ATFS page's row-range check uses the wrong offset, so clicking any row below
    the list selects the highest unlocked mission.
- Row 15:
  - Col 0: 4001 (back), hotspot cols 0-10 → page 150.
  - Right-aligned: 4002 (next), hotspot cols 14-23 → page 2 (from page 1) or page 14
    (from pages 12/122).
- A typed cheat code unlocks all missions and refreshes the board (`Gameframe.cpp:
  OnChar`). OpenAT can implement this as a debug unlock.

### 3.6 Player selection pages 2 / 14 / 18  [H]
**Layout.** Logical board rows are drawn with extra 22 px gaps, so each player gets a
3-cell-high block:

| Logical row | Screen y | Content |
|---|---|---|
| 0 | 63 | 999 (title) |
| 1 | 85 | underline |
| 2c+2 (c = player 0..3) | 129 + 66c | player name at col 6 (18 columns visible). Col 3 holds the logo cell. |
| 2c+3 | 151 + 66c | airline name at col 6. Abbreviation (2 characters) at col 3. |
| 15 | 393 | back (4001) at col 0. Start (4000) right-aligned, only when allowed (see below). |

Rows 10-14 are not drawn.

Overlays per player block c:
- **Portrait:** `PLAYER0-3` (human look) or `PLAYER4-7` (computer look). Drawn
  transparent at x=128, bottom-aligned at y=174+66c.
  - Human (local or remote) uses the human look. Computer uses the computer look.
  - A seat reserved in a network savegame blinks between the two looks: computer look for
    600 ms of every 1700 ms.
- **Check mark** `HAKEN` on remote humans (owner 2), at x=158, about 31 px below the portrait top.
- **Airline logo:** the logo cell (col 3, name row) stores a *logo index* instead of a
  character. It flips like a character at start-up. After each step the timer clamps its
  current value into the logo bitmap range. It is drawn as the small logo bitmap at
  (183, 132+66c).
  - The logo is always the player's own index. There is no UI to change it [H].
- A blank tile at (208, 151+66c) masks the column next to the abbreviation.
- Text brightness: a player block is drawn lit only if that player is the local human
  (owner 0). All other blocks use dark glyphs. Portrait columns 0-5 of name rows are never
  drawn as glyphs.

**Owner codes:** 0 = local human, 1 = computer, 2 = remote human (network), 3 = seat
reserved for a human in a network savegame (waiting).

**Hotspots and clicks** (y-ranges are inclusive):
- **Block** x 128..512, y 129+66c..173+66c: make this player the local human. All others
  become computer. Remember it as *last player*. Run the name check.
  - In the network lobby this also swaps network IDs and broadcasts *select player*.
  - The demo build allows only player index 1.
- **Name field** x ≥ 223, y 129+66c..151+66c: place the text cursor at column
  (x-223) div 16, clamped to 0..17, on that player's name.
- **Airline row** x ≥ 223, y 151+66c..173+66c: hides the cursor.
- **Abbreviation** x 175..207 on the airline row: gets a hot cursor only. The click itself
  does nothing special.
- Lobby only: clicking x 128..175 on *your own* block un-seats you (owner → computer) and
  broadcasts *unselect*. While loading a network savegame, only Back is clickable.
- **Back** (row 15, cols 0-10): page 2 → page 1, or page 0 if free game (difficulty is
  reset to min(first mission, max)). Page 14 → page 122 for an ATFS mission, otherwise page 12.
  Page 18 → see §3.9.
- **Start** (row 15, cols 14-23), only when *names OK*:
  - Pages 2/14: clear the network save flags and go to page 99.
  - Page 18: see §3.9.

**Name check** (*names OK*): all four player names are pairwise different, all four airline
names are different, all four abbreviations are different, and there is exactly one local
human. The Start label is hidden until this holds.

**Keyboard (name entry)**, active while the cursor is visible on pages 2/14/18:
- Accepted characters: A-Z (lower case is upper-cased), Ä Ö Ü (both cases), space, '-' and
  '.'. Each overwrites the character under the cursor and advances one column (max col 17).
  In the network lobby an edited name is broadcast (*enter name*).
- Enter: cursor to column 0 of the next player's name (stops at player 3).
- Left / Right: move within 0..17.
- Backspace: delete the character left of the cursor. The rest shifts left and the end is
  padded with a space. Then warp.
- Delete: delete the character at the cursor, same shifting.
- Up / Down: jump to the previous or next player's name. Disabled while in a network session.
- **Editing airline names or abbreviations is effectively unreachable** [H/M]. The input
  logic does support an "airline row" cursor and a negative-column cursor for the
  abbreviation, but no click or key ever puts the cursor there. In practice only player names
  are editable, which matches the manual. OpenAT may add airline/abbreviation editing as
  an enhancement (the name-check logic already covers them).
- The blinking cursor is drawn at x = 128+16*(col+6), y = 129+66c.

### 3.7 Page 5 - home airport  [H]
- Row 0: 600 (title). Row 15:
  - col 22: 4003 (OK)
  - col 10: `<<`, shown when the list is scrolled
  - col 13: `>>`, shown when more cities follow
- Rows 2..13: 12 city slots from the current offset (city table *indices*, which may have
  gaps; gaps stay empty). Name at col 3. The current home airport gets `==>` at col 0.
- Clicking a city row sets the home airport (and the option), then warps.
- `<<` moves back 12 and `>>` moves forward 12.
  - Quirk: the `<<` click test does not check the row, so a click anywhere in cols 10-11
    while scrolled also pages back.
- OK (cols 22-23) or **right-click** → page 0.
- On entry the offset jumps forward in steps of 12 until the current airport is on screen.

### 3.8 Page 7 - highscores  [H]
- Row 0: 1100 (title). For rank n = 1..6:
  - row 2n: "n." at col 0 and the name at col 3
  - row 2n+1: the score with thousands separators, at col 3
- Row 15: back (4001). **Any left click** on this page returns to page 0.
- Data: six entries (name, game ID, score), stored obfuscated in `misc\xmlmap.fla`.
  - Score = the company value of the local human. It is updated every game day unless the
    session allowed cheats (`Player.cpp`).
  - An entry with the same game ID is updated in place. Otherwise the entry is inserted by rank.

### 3.9 Network pages 13 / 15 / 17 / 18  [H] (behaviour; transport is another spec)
- **13 - transport:** title 700.
  - Lists the available connection types except modem: name at col 3 of rows 2…, arrow
    at col 0 for the selection. Clicking a row selects it.
  - Back (row 15, cols 1-7) → page 0. Next (cols 17-24) stores *last provider*.
  - TCP/IP opens the **enter host IP** popup (menu `MENU_ENTERTCPIP`). The connection is
    attempted on the next timer tick. Other transports connect directly.
  - Success → page 15. Failure → stay on page 13.
  - Before handling a click, every ID counter is reset so all peers generate the same object IDs.
- **15 - sessions:** title 701.
  - Up to 6 session names on rows 2..7. Click to select. With no sessions, show 720 at row 2.
  - Rows 10-11: 706/707 (load a network savegame) → Options load page in network mode.
  - Row 13: 702 (create session) → page 17, with the session name defaulted to 703.
  - Next (only if sessions exist) joins the selected session and sends a join request
    with the local version string and the wanted player index → page 18.
  - Back disconnects → page 13.
  - The list refreshes asynchronously about every 0.8 s.
- **17 - create:** title 702.
  - Row 2: editable session name, max 24 characters. Same character set as names. The
    cursor is at (128+16*col, 107). Left, Right, Backspace and Delete work as for names.
  - Row 4: 710 or 711 (the cheats-allowed toggle), click cols 1-16.
  - Next creates the session as host, with a random unique game ID → page 18.
  - Back → page 15.
- **18 - lobby:** the player page (§3.6). It is rebuilt every 0.8 s from the session's
  player list.
  - The host sees Start only when all of these hold:
    - more than one network player
    - at least 5 s since the last join and 2 s since the last name or seat change
    - no pending un-seated players
    - when loading a network save, no seat still waiting (owner 3)
  - Start broadcasts *begin game* (cheats flag, start time, home airport) and every peer
    goes to page 99. When loading a save, everyone loads it instead.
  - Back sends *leave* and closes the session. The host returns to page 17, or to page 15
    after a save load. A client returns to page 15.
  - Host-side handling:
    - joins are refused when 4 humans are present (*full*) or on a version mismatch
    - savegame identity is checked
    - seats are assigned to the requested index or the first free one
  - Session lost, or host migration while in the lobby → back to page 15 (client) or page 17 (host).
- Random start day option: when set, the start time is a random day of the year.
  Otherwise it is the current real date.

### 3.10 Page 99 - loading  [H]
- Text 530 is centred on row 8 (start column 12 - (len-3)/2).
- Once the board has finished flipping, the screen waits 5 more paint frames. Then it
  resets players to "in game", reloads `Routen.csv`, runs game set-up (§ other spec),
  loads the airport halls (all level 1), builds door images and the gate map, and sets the
  state to INIT|DONE. The game loop then starts play (§1.4).

### 3.11 Music and sound on these screens
- No dedicated menu music: MIDI keeps running from start-up.
- Click: `change.raw`. Board flips: `Klack0/1/2.raw`.

---

## 4. Options (`Options`)  [H]

### 4.1 Entry points
- From the main menu: state OPTIONS. It runs as a full-screen window in INIT context
  (the code tests "gamestate & 31 == INIT"), with player number 0.
- In game: the Options room (`ROOM_OPTIONS`) opens as the player's location window.
  - **ESC or F2** toggles it (enter, or leave if already queued).
  - **F3** opens it directly on the **load** page. **F4** opens it on the **save** page.
  - The status-bar button also opens it (another spec).
- Opening:
  - mutes the global ambient volume (unless on the phone)
  - counts an "options open" pause, broadcast to peers (*options open*); the game is paused
    while any player has options open
  - loads sample sounds: `raunen.raw` (ambience loop), `Kaffee.raw` (effects), `prop.raw`
    (planes), `opt_say.raw` (speech), `opt_hinw.raw` (announcements), `change.raw` (click)
  - starts the 50 ms timer
  - resets the klacker planes when in game
- Closing: saves the options, recomputes every player's room, clears pause counters, ends the
  F-key shortcut, and clears the "came from menu" flags.
- Backdrop `startup.gli:HAUPTMNU`. The board is the same as §2 (normal row spacing).
  The version string is drawn as in §3.1. **[WS]**
- In-game key handling while not editing: F3 → load page, F4 → save page, ESC → leave the room.

### 4.2 Pages

| Page | Purpose |
|---|---|
| 1 | Start page |
| 2 | Graphics |
| 3 | Sound |
| 4 | Other |
| 5 | Load |
| 6 | Save |
| 7 | Quit-application confirm (never reached in this build) [H] |
| 8 | Leave-game confirm |

Every page prints the underline on row 1. All texts are in group `Misc`.

**Page 1 - start page.** Row 0: 4000.

| Row | Text | Action |
|---|---|---|
| 2 | 4001 | → graphics |
| 3 | 4002 | → sound |
| 4 | 4003 | → other |
| 6 | 4004 (in game only) | → **load** page (slot names are re-read) |
| 7 | 4005 (in game only) | → **save** page |
| 6 | 4007 (main-menu context only) | back to the main menu (BOOT) |
| 9 | 4006 (in game only) | → page 8 (leave game) |
| 11 | 4008 (in game only) | close the options and return to the game |

Hotspots are whole rows, any column. Right-click on page 1: main-menu context → BOOT; in game → close.

**Page 2 - graphics** (each row toggles a boolean; the label shows the state via text base+value):

| Row | Option | Text |
|---|---|---|
| 2 | planes on apron | 4011+v |
| 3 | passengers | 4013+v |
| 4 | screen blending (turning it on primes a fade) | 4015+v |
| 5 | thought bubbles | 4020+v |
| 6 | scrolling ("flipping") | 4022+v |
| 7 | transparency | 4024+v |
| 8 | shadows | 4026+v |
| 10 | back → page 1 | 4099 |

**Page 3 - sound.** Row 0: 4120.

| Row | Item | Behaviour |
|---|---|---|
| 2 | MIDI on/off (4130+v) | Toggle. When on and music volume is above 0, start the next track; otherwise stop. Warp. |
| 4 (col 1) | Music mode (4150+mode) | Cycles 0..8. Any mode other than 0 skips to the next track at once. [M] 0 = random/sequential with skip; 1..8 = styles (see manual) |
| 5 (col 1) | 4140 "next track" | Shown and clickable only in mode 0. Skips the track. |
| 7 | digital sound on/off (4132+v) | Toggles the DirectSound output |
| 9 | ambience (4122) | slider |
| 10 | announcements (4123) | slider |
| 11 | speech (4124) | slider; works only if voice files were found |
| 12 | effects (4125) | slider |
| 13 | planes (4126) | slider |
| 15 | back (4099) | → page 1; stops the ambience loop |

- **Sliders:** cols 15..22 hold eight `-` cells, with a `|` knob at the current value
  (0..7). Clicking a column sets value = column-15. Holding the left button and dragging
  also updates the value; this is checked every paint.
- Each change plays that channel's sample at the new volume: ambience loops, the others
  play once. Then redraw and warp.
- Volume mapping: percent = value x 100 / 8 for the samples. Effects elsewhere use
  value x 100 / 7.
- Music-volume and master-volume sliders exist in the code but are commented out.

**Page 4 - other.** Row 0: 4030.

| Row | Option | Text |
|---|---|---|
| 2 | assistant | 4031+v |
| 3 | advisor | 4033+v |
| 4 | autosave | 4038+v |
| 5 | fax notices | 4040+v |
| 6 | "real" airport abbreviations (applied immediately) | 4042+v |
| 7 | speech bubbles | 4044+v |
| 8 | short briefing | 4048+v |
| 9 | random start day | 4050+v |
| 11 | back → page 1 | 4099 |

**Page 7 - quit confirm:** rows 0, 2, 4, 5 = 4060, 4061, 4062, 4063. Row 4 leaves the game loop
(exit). Row 5 → page 1. No code path sets page 7.

**Page 8 - leave game:** row 0 = 4065. Rows 2/4/5 = 4061/4062/4063. Row 4 = confirm:
- In network play, broadcast a chat line `Misc` 7022 (formatted with the player name), then
  disconnect.
- Then BOOT (main menu).

Row 5 → page 1.

### 4.3 Load page (5) and save page (6)  [H]

**Slot files:** `Savegame\Game0.dat … Game11.dat` (single player) or `Net0.dat … Net11.dat`
(network). 12 slots. Slot index 11 (the 12th) is the **autosave** slot.

**Slot header**, read for the list:
1. slot name (user text)
2. save version and sub-version (current 1 / 107)
3. if sub-version > 0: an **info string**, built at save time from
   - `Misc` 1150 (mission games): mission number, weekday (`Sche` 3010+wd), day, month,
     airline name
   - `Misc` 1151 (free game): weekday, day, month, airline name
4. then: unique game ID, host flag, number of humans, local player index, 4 owners,
   session name, cheats flag, city count, and the full game state

Missing slot: the name is `Misc` 4073 (the "empty" label) and the slot is invalid.

**Load page:** row 0 = 4070.
- Rows 2..13 = slots 1..12, printed at col 1 as a two-digit number, colon, name.
- Invalid slots are drawn dark.
- Row 15, col 0 = 4096 (back), hotspot cols 0-9.
- Hovering a slot that has an info string shows it as a **tooltip** (ID 5000+row) almost
  immediately. Hovering an empty slot shows nothing.
- Clicking a slot that has an info string:
  - From the main-menu network path: remember the slot and go to BOOT. The wizard then
    creates the session from the save (§3.9).
  - In network play: if the save's human count differs from the current one, show the
    request `MENU_REQUEST_NET_NUM`. Otherwise ask the peers (*load request*) and pause.
  - Otherwise: load the game. Coming from the main menu, this switches the state straight
    to PLAYING and deletes the menu window.

**Save page:** row 0 = 4071.
- Rows 2..12 = slots 1..11 only (the autosave slot is hidden).
- Clicking a slot (when not editing) starts **name editing** on that slot. The cursor
  starts at column 0 of the name (board col 4), and the pause key is disabled.
- While editing:
  - Row 15 shows 4097 at col 0 (abort/back) and 4098 at col 22 (OK). When not editing,
    row 15 shows only 4096.
  - Accepted characters: A-Z (upper-cased), Ä Ö Ü, 0-9, space, '-', '+', '.'. Max 20
    characters (cursor 0..19).
  - The first typed character on an empty slot replaces the "empty" label with blanks.
  - Backspace **blanks the character under the cursor and then moves left** (no
    shifting; different from the wizard). Left and Right move.
  - ESC or right-click cancels: names are re-read from disk.
  - **Enter, or OK** (row 15, cols 22-23): save to that slot. In network play, first pick a
    new unique game ID and save on every peer too. Then close the options and return to the
    game (or BOOT in main-menu context).
- While editing, left clicks outside the save page are ignored.
- Cursor position: x = 129+16*(col+4), y = 85+22*(slot+1).

**Back behaviour quirk** [H]:
- From the load page, Back or right-click → BOOT when it was opened from the main menu.
  Otherwise it **closes the options entirely**.
- Save-page Back does the same.

Reason: the "opened via F3/F4" marker defaults to -1, which counts as true, so pages 5/6
never return to page 1. OpenAT should probably return to page 1 when the page was reached
from page 1. Decide and document.

**Autosave** [H]:
- Single player: when the autosave option is on and the game clock crosses 17:00 during
  a day that has not been ended, save to slot 11. The name is `Misc` 5000.
  The assistant then posts urgent message `Advice` 500.
- Network: the host saves slot 11 at end of day when autosave is on.

**Thumbnails:** none. The only metadata is the info string, shown as a tooltip.

---

## 5. Credits (`CCredits`)  [H]
- Opened by state CREDITS (from the main menu). Full-screen window 640x480 **[WS]**.
- Assets: `room\credits.gli` with `BACK`, `LEFT` and `RIGHT`.
  - BACK is drawn centred horizontally at y=0.
  - LEFT is drawn transparent at (0,0) and RIGHT at (640-width, 0). These are side frames
    drawn on top of the scrolling text.
  - A widescreen layout must anchor them to the 4:3 content area or extend them **[WS]**.
- Text: group `Cred`, ids 1000, 1001, … up to (but excluding) the entry whose text is
  `EOF`. Entries of length ≤ 1 are blank lines.
- **Scrolling:**
  - A ring of 25 line images, each 560x40.
  - The scroll position starts at -2 and advances **1 px per 60 ms** (about 16.7 px/s).
    Holding **Ctrl** advances it every 10 ms (6x faster). Catch-up is time-based.
  - Every 40 px, the next line image is cleared and the next credit line is rendered into it,
    centred, in the big grey font.
  - Line image *i* is drawn at x=40, at a y that moves up from the bottom (480) and wraps
    after 1000 px.
  - After the last entry the ring keeps scrolling empty lines. **Credits never end by
    themselves**.
- Exit: any left/right click or any key except Ctrl → BOOT (main menu).
- No music change. The mouse-startup cursor mode is set while open.

---

## 6. Intro and outro  [H]

### 6.1 Intro (`CIntro`)
- Video: `intro\intro.smk`, played from the intro path.
  - With no intro path, the screen ends at once (BOOT).
  - Decoded to 16-bit, drawn **centred** at (320-w/2, 240-h/2) over black **[WS]**.
  - Played with its audio track. The "no skip" flag means the decoder does not drop frames.
- MIDI is stopped on start. When it closes: mark *intro viewed* (if the video opened),
  restart MIDI if enabled, and restore the music volume.
- **End and skip behaviour** depends on whether this is the first viewing (the *viewed*
  flag is still false while it plays):
  - **First viewing:** on the last frame, or on left click, right click or ESC, take the current
    frame as "fade from" and close the video. Then cross-fade to `titel.gli:TITEL` in 8
    steps over 1 s, hold the title until 3 s after the fade started, then BOOT.
    Clicks during the fade or hold → BOOT immediately.
  - **Replay** (from the main menu): the last frame, or any click or ESC, goes straight to BOOT.
- ESC reaches the intro through the frame's key handler (the intro window itself only
  handles mouse and paint).
- `intro\game.smk` is read only by a copy-protection probe; it is not played.

### 6.2 Outro (`COutro`)
- Triggered from the game-over menu:
  - winning the final base mission (difficulty 5) → state OUTRO → `intro\outro.smk`
  - winning the final add-on mission (difficulty 20) → OUTRO2 → `intro\outro2.smk`
  - losing, or any other mission → BOOT
  - Network play disconnects first.
  - No outro is defined for the ATFS final mission (50) [H]. That one goes to BOOT.
- The loop deletes all room windows and then opens the outro.
- Playback is the same as the intro: centred, MIDI stopped, MIDI restored on close.
- End: last frame → BOOT. A left or right click or ESC → BOOT immediately.
- No credits roll after the outro: it goes straight to the main menu.

---

## 7. HallDiskMenu (editor tool, not player-facing)  [H]
- A developer popup to swap airport hall graphics. It opens in the airport view only when
  the editor flag is set.
- Child window at rect (10,10)-(620,460) **[WS]**. Background `HallDisk.gli:HALLDISK`
  (in the gli path).
- Grid: 10 hall types (columns with fixed x-ranges) x 9 levels (rows of 9 px, starting at
  y 53 inside the bitmap). Missing hall files are greyed out with light lines. Hall files
  are looked up per difficulty (free game uses map 7).
  - A click in the top area loads one hall.
  - A rightmost column loads all halls at a level.
  - The lower area changes one hall's level and reloads the airport.
- A difficulty picker (two columns of 9-px rows) selects which mission map set to browse.
- Right-click closes the popup.
- **OpenAT: optional. Not needed for gameplay.**

---

## 8. Widescreen (640x480) assumptions to resolve  **[WS]**
1. Board origin (128,63), cell 16x22, and all hit-test formulas → define the board in a
   640x480 "content" rect that is centred or scaled. Map mouse coordinates into it.
2. `HAUPTMNU`, `TITEL`, `SPELLOGO` and the credits `BACK` are 640x480 art. Pillarbox them
   or extend the backdrop.
3. Fixed pixel overlays on the player page (portraits at x=128, logos at x=183, check mark
   at x=158, blocks at y=129+66c) are relative to the board origin.
4. The version string rect is (0,429)-(519,480) and the loading text rect is
   (2,458)-(640,480).
5. Klacker planes spawn and despawn at x=640, x=-190-35*size, and y 80..479.
6. Credits: text lines 560 px wide at x=40, scroll from y=480, wrap 1000 px. The side
   frames are at x=0 and x=640-w.
7. Intro and outro are centred on (320,240). Fades use full 640x480 buffers.
8. The mouse is forced to (600,440) during loading. Request popups zoom from (320,220).
9. The HallDiskMenu rect is (10,10)-(620,460).

---

## 9. Open questions
1. The exact semantics of music modes 0..8 (`Misc` 4150-4158) and of "next track" in mode 0
   need the text resource (the manual says random music versus style selection).
2. Page 12 lists difficulty 21 (text `NewG` 3021). Is that row blank, or a leftover entry?
   Check the text resource.
3. Should OpenAT keep the "Back from load/save closes the options" quirk (§4.3)?
4. Airline-name and abbreviation editing is unreachable (§3.6). Should OpenAT expose it?
5. The Quit-confirm page 7 in options is unreachable. Was it meant for a hotkey (the
   manual's key table lists F1 as "options / quit", which differs from the code)?
6. The manual's F-key table (F1 options, F2/Esc load, F3 save) disagrees with the code
   (ESC/F2 options, F3 load, F4 save). The code is authoritative here [H].
7. The idle-plane screensaver runs on paint-frame ticks. What real-time rate should OpenAT
   target? Assume the original ran at roughly 20-60 fps.
8. The klack timer is 50 ms (20 Hz), but the code comment says 10 fps. Measure on the real
   game if the exact feel matters.
9. `runesoft.gli` is unused in this source. Was a publisher splash shown by the GOG
   launcher or by another build?
10. The text-encrypted `std_ger.res` format is needed to resolve ids to strings (another spec).
