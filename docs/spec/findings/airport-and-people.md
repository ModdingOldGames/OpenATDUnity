# Airport hall view and people (behaviour spec)

Status: draft 1, written from a clean-room reading of the GOG source, the SDL port and the
shipped data files. Everything below is described in my own words. Formulas use short math
notation. Numbers and tables copied from data files are facts.

Traceability is given as `File.cpp: Function`. "GOG" means `docs/spec/local/gog_src`, "SDL" means
`docs/spec/local/ref_AirlineTycoon/src`. Confidence tags: **[H]** high (read directly, cross-checked
with data), **[M]** medium (read once, not cross-checked), **[L]** low (inferred).

Widescreen notes are marked **WS:** throughout. Section 13 lists all of them in one place.

---

## 0. Corrections to the task brief

* **`data/builds.csv` is not the airport layout.** It is the plane-designer parts table
  (columns Id, ShortName, Cost, Weight, Power, Noise, Wartung, Passagiere, Verbrauch). It is read
  into a "plane builds" table (`Init.cpp: InitItems`, `Editor.cpp`). **[H]**
* The airport layout is stored in binary **hall segment files** in `misc/` (`Left001.dat`,
  `Check001.dat` and so on). They hold the "BUILD" placements (section 2). **[H]**
* `data/brick.csv` and `data/clan.csv` are **not plain text**. They start with the magic `xtRLE`
  and are run-length encoded with an XOR key (section 1.1). **[H]**
* The layout does **not** depend on the home city. It depends on the mission or difficulty, on
  the expansion level, and on the season (the brick graphics set). **[H]**
* The original has **no split screen**. Each `CStdRaum` window, including the airport view, always
  covers (0,0)-(640,480). Multiplayer works over the network, one player per machine. The per-player
  `WinP1/WinP2` fields and the `WIN_LO..WIN_BIG` defines are leftovers of a planned split-screen
  mode (`Stdraum.cpp: CStdRaum ctor`). **[H]**
* The "Klacker" split-flap board is **not in the hall**. It is used only on the new-game and
  options screens. The hall's flight information is drawn by "text bricks" over the check-in and
  gate counters (section 9). **[H]**

---

## 1. Data files involved

| File | What it holds | Notes |
|---|---|---|
| `data/brick.csv` | Brick table: one graphic piece type per row | xtRLE encoded, `;`-separated, header row. Read by `Brick.cpp: BRICKS::ReInit` |
| `misc/<Hall><NNN>.dat` | Placement lists (BUILD records) for one hall segment | Binary TEAKFILE (section 2.2) |
| `gli/glbrick0..3.gli` | Brick bitmaps, one file per season (0 spring, 1 summer, 2 autumn, 3 winter) | `Sim.cpp: SIM::UpdateSeason`. `gli/glbrick.gli` (523 images) seems unused or old **[M]** |
| `data/clan.csv` | Person types ("clans") and their animation frame lists | xtRLE encoded, 3 rows per clan. `Person.cpp: CLANS::ReInit` |
| `gli/clan<N>.pol`, `clang<N>.pol`, `clanw<N>.pol` | Compressed person sprites (walk/stand/sit, gimmick, waiting gimmick) per clan index N | "HLine pools" (section 6.3) |
| `gli/skelett.pol` | X-ray skeleton sprites (security scanner) | |
| `gli/glstd.gli` | Shadows (`SH_BIG*`), status bar pieces, thought bubbles (`MOOD*`), logos, items, cursor feet | |
| `gli/glbasis.gli` | Mouse cursors | `Gameframe.cpp: GameFrame ctor` |
| `gli/glplanes.gli` | Generic plane parts used in the hall (`999S*`, `FLUG*`) | `Init.cpp: InitTipBms` |
| `room/klacker.gli` | Split-flap glyphs (menus only) | |
| `video/gate-*.smk`, `video/expl.smk` | Gate security animations, office explosion | Played into placeholder bricks (section 8.4) |
| `sound/*.raw` | Hall sounds (section 10) | |

### 1.1 xtRLE container (brick.csv, clan.csv) **[H]**

`SDL TeakLibW/TeakFile.cpp: CRLEReader`.

* 6 bytes: ASCII `xtRLE` plus a NUL byte.
* 4 bytes LE: version. If version is 0x102 or higher, the XOR key is 0xA5, otherwise 0.
* If version is 0x101 or higher: 4 bytes LE, the decoded size.
* Then a stream of runs. Each run starts with a control byte `n`:
  * If `n & 0x80`: a literal run. `n & 0x7F` bytes follow, each XORed with the key.
  * Otherwise: a repeat run. One byte follows and is repeated `n` times (no XOR).
* Stop when the decoded size is reached.

The decoded files are cp1252 text. The brick table has 333 data rows. The clan table has 89 clans
(267 rows plus a header).

---

## 2. Airport construction

### 2.1 Bricks (`brick.csv`) **[H]**

`Brick.cpp: BRICKS::ReInit`. Internal id = csv Id + 0x10000000 (the album key). The table is sized
for MAX_BRICKS = 340.

| Col | Header | Field | Meaning |
|---|---|---|---|
| 1 | Id | id | Brick id (e.g. 100 floor, 720 office door, 2033 rune) |
| 2 | Beschreibung | — | Developer description, ignored |
| 3 | RAM-Priority | RamPriority | Lower values went to video RAM first. Irrelevant now |
| 4 | opaque | NonTrans | 0 = colour-key transparent (key = pixel value 0); 1 = opaque copy; 2 = "glass": 50 % blend when the Transparency option is on, otherwise colour-keyed |
| 5 | Triggered | Triggered | 0 = loops continuously; 1 = one-shot, started by an event (section 8.3); 2 = roll-up shutter (section 8.2). Forced to 90 internally when any frame has a `:n` duration |
| 6 | Layer | Layer | Primary depth-sort key (section 3) |
| 7 | Speed | AnimSpeed | Ticker divisor per frame; 0 = static |
| 8 | FloorDist | FloorOffset | Bias added to the sort y |
| 9-14 | BaseX, BaseY, GridX, GridY, MinY, MaxY | | Editor snapping only (`BRICK::GetIntelligentPosition`) |
| 15 | Obstacle | ObstacleType | Walk-grid effect (section 4.3); 99 = rune |
| 16 | TGA-Name | frame list | Space-separated graphic names in the season GLI. `NAME:n` gives that frame a duration weight `n` (default 1) |

A graphic name becomes a 64-bit id: the ASCII bytes packed little-endian (`StringToInt64`).

Layer values that occur: 3 (horizon strips), 4-6 (back walls and windows), 10-12 (floors), 17-21
(ground-floor furniture, shop fronts, counters), 25, 30, 50-52 (upper-floor slab, office
backgrounds, doors), 59-62, 65, 70-71, 80 (runes, never drawn in the game).

### 2.2 Hall segment files (`misc/*.dat`) **[H]**

`Brick.cpp: BUILDS::Load`, `SDL TeakLibW.h` (serialisation of BUFFER and ALBUM).

Layout, all little-endian:

1. `int32 count`, `int32 deleteOffset` (unused).
2. `count` BUILD records of 13 bytes: `int32 brickId` (csv id + 0x10000000), `int32 x`,
   `int32 y`, `uint8 par`.
3. Album trailer: `uint32 lastId`, then `int32 count2`, `int32 offset2`, then `count2` × `uint32 id`.
   A record whose album id is 0 is an empty slot and must be skipped.

`x,y` is the **top-left corner of the brick bitmap in airport pixel space** (section 3). `par` is a
small parameter: room number, gate number, door or trigger index (assigned at load time),
waypoint group and so on.

`misc/shops.dat` (no number) has a different, unknown format and is apparently unused. **[L]**

#### File naming and selection

Ten segments in fixed order (`Global.cpp: HallFilenames`):

| idx | Name | Content (default layout) |
|---|---|---|
| 0 | `Left` | Left end: museum, last-minute, telescope (Rushmore), stairs 2, flight monitors, phone 2. Add-on variants add cargo, NASA, island, space station, cola machine |
| 1 | `Check` | Check-in counters (one per gate) |
| 2 | `Ofice` | Upper floor offices and staff rooms of players 1-2, advertising agency, travel agency, stairs 1, phone 1 |
| 3 | `Entry` | Main entrance: customer spawn, exit, monitors. Some variants add the security office |
| 4 | `Shops` | Offices of players 3-4 (upper floor), duty free, bank, kiosk, broker, airport supervisor, WCs; variant 101 adds route box, Arab Air, sabotage room |
| 5 | `Cafe` | Rick's cafe, monitors, stairs 3, phone 3, second exit; variant 301 adds the designer |
| 6 | `Secur` | Gate security area |
| 7 | `Suit` | Baggage belt with 5 to 10 pick-up spots |
| 8 | `WaitZ` | Waiting rooms and gates (4 to 8) |
| 9 | `Right` | Right end: workshop (mechanic), airfield |

File name: `<Name><NNN>.dat` with `NNN = 100 × difficulty + level` printed as at least 3 digits
(e.g. `Left201.dat`, `Cafe4101.dat`). Free game uses difficulty 7. If the file does not exist,
difficulty is decremented until a file exists (down to 0). Matching is case-insensitive on Windows.

Files actually chosen at level 1 (computed from the shipped `misc/` directory):

| difficulty | Left | Check | Ofice | Entry | Shops | Cafe | Secur | Suit | WaitZ | Right |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 tutorial | 001 | 001 | 001 | 001 | 001 | 001 | 001 | 001 | 001 | 001 |
| 1 | 001 | 001 | 001 | 001 | 101 | 101 | 001 | 001 | 001 | 001 |
| 2 | 201 | 001 | 001 | 001 | 101 | 201 | 201 | 001 | 001 | 201 |
| 3 | 201 | 001 | 001 | 301 | 101 | 301 | 301 | 001 | 001 | 201 |
| 4 | 401 | 001 | 001 | 301 | 101 | 301 | 301 | 401 | 001 | 201 |
| 5 | 501 | 001 | 001 | 301 | 101 | 301 | 301 | 401 | 001 | 201 |
| free (7) | 701 | 001 | 001 | 701 | 101 | 701 | 701 | 401 | 001 | 201 |
| 11 | 1101 | 001 | 001 | 801 | 101 | 801 | 1101 | 401 | 001 | 201 |
| 12-15 | 1201 | 001 | 001 | 801 | 101 | 801 | 1101 | 401 | 001 | 201 |
| 16 | 1601 | 001 | 1601 | 801 | 101 | 801 | 1101 | 401 | 001 | 201 |
| 17-19 | 1701 | 001 | 1701 | 801 | 101 | 801 | 1101 | 401 | 001 | 201 |
| 20 | 2001 | 001 | 1701 | 801 | 101 | 801 | 1101 | 401 | 001 | 201 |
| 41-50 | 4101 | 001 | 1701 | 4101 | 101 | 4101 | 1101 | 401 | 001 | 201 |

Expansion levels exist only for `Check` (001-005), `Suit` (001-005, 401-405) and `WaitZ`
(001-005).

#### Levels and airport expansion

* At game start all ten levels are 1 (`Sim.cpp: SIM::ChooseStartup`, `NewgamePopup.cpp`).
* Expansion is bought from the airport supervisor for 1,000,000 (`Dialog.cpp`, dialog ids
  4011/4113/4120). It is allowed only if there is no free gate, `Sim.CheckIn < 5` (or `< 2` for
  difficulty ≤ normal, not free game), day ≥ 8, and at least 3 days since the last expansion. It
  sets a flag. On the next new day `CheckIn` and `WaitZone` are incremented, `Suitcase = WaitZone`,
  and the airport is rebuilt (`Sim.cpp: SIM::NewDay`).
* Number of gates = count of RUNE_2WAIT runes = 3 + level (4 to 8). The `.dat` contents match
  this. Each gate has one check-in counter and one waiting room.
* After loading a game, if the total number of rented gates exceeds `CheckIn + 3`, the levels are
  repaired and the airport is rebuilt (`Sim.cpp: SIM::LoadGame`).

### 2.3 Assembling the segments (`AirportView.cpp: AIRPORT::LoadAirport`) **[H]**

1. Load the ten segments.
2. The segments are authored in roughly absolute coordinates, but each one after the first is
   shifted horizontally by a "dilatation":
   `dil[0] = 0`; `dil[c] = dil[c-1] + (rightmost x of a floor brick 100 in segment c-1) + 88 − (leftmost x of a floor brick 100 in segment c)`.
   So the floor tiles (brick 100, 88 px wide) join without a gap. With level 1 the shifts are mostly
   0; expanded check-in or waiting zones push everything to their right further right.
3. Merge all records into one list sorted by the brick comparator (section 3.1). Each segment
   file is already sorted, so this is a k-way merge.
4. `CalcPlates` builds the walk grid and the door, trigger and clip lists (section 4).
5. `CalcCoordinates` builds the click areas and the rune list (section 4.4).
6. Runes that only matter at load time are removed from the draw list. Brick ids are converted to
   direct table indices (a speed-up; the result must never be saved back as a layout).
7. The draw list is copied into **hash buckets** of 320 px (`BUILDHASHSIZE`). Bucket `k`
   contains every build whose x span overlaps
   `[LeftEnd + 320k, LeftEnd + 320(k+3)]`. The bucket count is `(RightEnd − LeftEnd)/320`. The
   renderer picks bucket `(ViewPos.x − LeftEnd)/320`. That always covers at least 640 px from the
   left edge of the view. **WS:** the bucket span (3 × 320 = 960) only covers 640 + 320 px. Use a
   span of at least ⌈(W+320)/320⌉ buckets, or drop the hashing and cull on the fly.
8. `NewDay` (section 6.6).

Resulting extents (difficulty 3, computed with brick sizes from `glbrick0.gli`; ±1 tile):

| Level | LeftEnd | RightEnd | Data x range |
|---|---|---|---|
| 1 | 176 | 5852 | −297 … ≈6400 |
| 2 | 176 | 6292 | |
| 3 | 176 | 6732 | |
| 4 | 176 | 7172 | |
| 5 | 176 | 7700 | −297 … ≈8250 |

For difficulties 41-50 (Left4101): LeftEnd ≈ 132, RightEnd ≈ 6072 at level 1.

### 2.4 Room-id ↔ segment map (rune pars found in the files) **[H]**

RUNE_2SHOP and RUNE_SHOP pars (room numbers, `defines.h ROOM_*`) per segment file:

* Left001/201/401: 102 museum, 103 last minute, 118 telescope (Rushmore), 210-213 last-minute
  customer spots, 230/231/236 monitors, 52/53 stairs 2 up/down, 241 phone 2. Left401+ add 244 cola
  machine. Left501 adds 115 NASA and 116 island. Left1201/1701/4101 add 121 cargo. Left2001 adds
  115 NASA and 122 space station.
* Ofice001: 10/11 office and staff room of player 1, 20/21 for player 2, 109 advertising, 111
  travel agency, 200-203 travel-agency customer spots, 50/51 stairs 1, 240 phone 1. Ofice1601 has
  no travel agency.
* Entry: 232/233/237 monitors, 250 airport exit. 4101 and 701 add 123 security office.
* Shops001: 30/31 player 3, 40/41 player 4, 80 duty free, 100 bank, 101 kiosk, 105 plane broker,
  107 supervisor, 113 women's WC, 114 men's WC. Shops101 adds 85 route box, 104 Arab Air, 112
  sabotage.
* Cafe: 84 Rick's, 234/235/238 monitors, 54/55 stairs 3, 242 phone 3; 201+ add 250 exit; 301+
  add 125 designer.
* Suit: 220-229 belt spots (5, 6, 7, 8 or 10 depending on level).
* Right: 110 workshop.
* Check / WaitZ / Secur: no room pars. They hold gate-indexed runes (par = gate number).

---

## 3. Coordinate systems and draw order

### 3.1 Airport pixel space **[H]**

* One global 2D pixel space for the whole hall. X grows to the right along the hall (thousands of
  px). Y is the screen row, 0 = top of the hall bitmap.
* The camera `ViewPos` is the top-left of the view in this space. Screen = world − ViewPos
  (+ `WinP1`, always (0,0)).
* The hall occupies screen rows 0-439. Rows 440-479 are the status bar.
* Two walkable floors:
  * **Lower floor** (concourse): screen y ≈ 220 to 440.
  * **Upper floor** (office gallery): screen y ≈ 93 to 170.
  * Above about y 168 runs the parallax horizon / window band. Between the floors (≈170-220) is the
    gallery front.

### 3.2 Logical walk position and the oblique projection **[H]**

Persons store a logical `Position` (unskewed) and a derived `ScreenPos` (the foot point).

* Lower floor: `Position.y` ∈ [0, ~242) (row r → y = 22r + 11);
  `ScreenPos = (Position.x − Position.y/2 + 86, Position.y + 220)`.
* Upper floor: `Position.y = 5000 + local`, local ∈ [0, 110);
  `ScreenPos = (Position.x − local/2 + 86, local + 93)`.
* Customers use +90 instead of +86 in x (`Person.cpp: DoOneCustomerStep` vs `DoOnePlayerStep`
  and `DoOneAnimationStep`). This 4 px difference is in the original. Reproduce it or unify it
  (cosmetic). **[H]**
* Moving one row deeper shifts the screen x by −11 px (half a row height): an oblique projection
  with a 2:1 slope.

### 3.3 The walk grid ("plates") **[H]**

* Tile = **44 × 22** logical px.
* Grid = `PlateDimension.x` columns × **16 rows**. Index = `row + 16 × col`. Columns may be
  negative down to −20 (the buffer has 20 spare columns in front).
  `PlateDimension.x = (max build x)/44 + 40`.
* Rows 0-4 = upper floor. Rows 5-15 = lower floor (row = Position.y/22 + 5).
* Column = Position.x / 44 (a floor division in the customer code: `(x+4400)/44 − 100`).
* `PlateOffset.x = 42` (33 + 9).

Build → tile (`AIRPORT::CalcPlateXPosition/CalcPlateYPosition`). Let `w,h` be the brick bitmap
size, `(X,Y)` the build position, `ax` = 0 / w/2 / w for left, centre or right alignment, `dx,dy`
small per-obstacle offsets:

* A build belongs to the upper floor if `h − 2 + Y < 210` (runes use `< 170`).
* Upper: `col = (ax + X − 42 + 11 + dx + (h + Y − 2 − 93)/2)/44 − 1`; `row = floor((h − 2 + Y − 93 + dy)/22)`.
* Lower: same with 220 instead of 93, and `row = 5 + floor((h − 2 + Y − 220 + dy)/22)`.
* Columns outside [0, PlateDimension.x) map to 0.

Rune → logical position (`AIRPORT::CalcCoordinates`), using the centre column:
* Upper: `Position = (44·col + 22, 22·rowLocal + 11 + 5000)`.
* Lower: `Position = (44·col + 22, 22·rowLocal + 11)`, with `rowLocal` 0-based from the lower
  floor top. **Note:** lower runes can lie outside 0-10. The entrance spawn runes sit at local rows
  ≈12-26, below the visible floor. People enter from off-screen. **[H]**

### 3.4 Plate bit flags (`iPlate`) **[H]**

Initial value 0xFC.

| Bit | Meaning |
|---|---|
| 128 | exit north allowed |
| 64 | exit east allowed |
| 32 | exit south allowed |
| 16 | exit west allowed |
| 8 | customers may walk here (needed for occupancy marking) |
| 4 | players may walk here |
| 2 | occupied right now (dynamic) |
| 1 | special (multi-use): belt tile from a belt rune; glue on the upper floor; stench cloud on the lower floor |

A parallel byte array `iPlateDir` holds the occupant's direction: 0-3, 8 = waiting, 254 = slow
walker (step 1), 255 = none.

### 3.5 Depth sort and draw merge **[H]**

* Brick sort key, compared in this order: `Layer`, then `Y + h + FloorOffset` (bottom edge plus
  bias), then `X`. (`BUILDS::Sort`, `LoadAirport` merge, `OnPaint`.)
* Per frame a packed key `Layer·65536 + (Y + h + FloorOffset)` is compared with a person key:
  * a person with ScreenPos.y < 170 (upper floor) → `60·65536 + ScreenPos.y`;
  * otherwise → `20·65536 + ScreenPos.y`.
* The two sorted streams (builds of the active bucket, persons) are merged: at equal keys the
  person is drawn first (`t2 <= t1`).
* Persons are re-sorted every simulation step (`PERSONS::DepthSort`): by `ScreenPos.y +
  FloorOffset`, but every upper-floor person (y < 170) sorts after every lower-floor person.
  Clan `FloorOffset` is −15 for the "old lady" (group 70), else 0.
* Runes (id ≥ 2000) are never drawn outside the editor.

---

## 4. Walk grid construction (`AIRPORT::CalcPlates`) **[H]**

### 4.1 Arrays initialised

* `iPlate` = 0xFC, `iPlateDir` = 0xFF.
* `SeatsTaken[col]`: 16-bit masks of free seats per column. Bit `2r+1` = south-facing seat in
  waiting row r, bit `2r` = north-facing seat. Seats come from bricks 370-372 (`CalcSeats`,
  row = tileRow(dy = −10) − 5).
* Doors (max 30), triggers (max 30), clip markers (max 40). `LeftEnd = −99999`, `RightEnd = 99999`
  until the end runes are found.

### 4.2 Doors and triggers

* Bricks 718-729 (doors): `par` = door index. Door tile = (centre column, row with dy = FloorOffset,
  plus 30 for brick 729). Door type: 727 = sabotage "rotating wall" door; 718/719 = security door;
  729 = cargo door; others = normal.
* Bricks 471 (luggage scanner box) and 303 (cola machine "electro skeleton"): `par` = trigger
  index, used for one-shot animations.

### 4.3 Obstacle types → plate edits

Notation: `x0` = column with dx=+10 and left alignment; `x1` = column with dx=−10 and right
alignment; `y` = row with dy=−8 unless noted. "Remove N on (c,r)" means clear bit 128, and so on.
All edits keep the grid symmetric (a wall removes the exit on both sides).

| Type | Effect |
|---|---|
| 0 NONE | No walk effect (doors and triggers are registered as above) |
| 1 POINT, 3 SITPOINT | Tile (centre col, row dy=0; SITPOINT dy=−10) becomes 0 (blocked); neighbours lose the exit into it |
| 2 WIDE | For columns x0..x1 on row (dy=0): clear bits 8 and 4 (nobody may stand there) but keep the exits |
| 4 | Short horizontal fence at column x0 only: no S exit from (x0,y) and no N exit from (x0,y+1) |
| 10 HORIZONTAL | Same over x0..x1 |
| 14 | Same over x0..x1−1, one row higher |
| 11 VERTIKAL | Between column x(dx=−6, left) and x+1: no E from x, no W from x+1, on row y |
| 12 BIGVERTIKAL | Same on row y−1, and then also VERTIKAL (falls through) |
| 20 HORIZONTALEXIT | Horizontal fence on both sides of the row except a one-tile gap at x1−2 (rear window exit) |
| 21 SHOPFRONT, 26 MUSEUM | Fence across x0..x1−1 (shop frontage); then for each row going up (stopping at row 4 or 0): side walls on the left (SHOPFRONT only) and on the right; then the top row gets "no N" |
| 22 STAIRFRONT | Fence across the front, side walls up to row 4 |
| 23 BUROFRONT_R / 27 BUROFRONT_L | Fence across the front except one gap tile (x1−1 for _R, x0+1 for _L), side walls upward |
| 24 NOENTRY1 | Full fence across the front (no gap), side walls |
| 25 NOENTRY2 | Like 24 but x1 uses dx=−60 |
| 40/41/42/43 UP/RIGHT/DOWN/LEFT | One-way tile: keep only that exit among the four direction bits |
| 99 RUNE | Depends on the rune id (4.4) |

### 4.4 Runes (invisible marker bricks, ids 2000-2070) **[H]**

Defines: `defines.h RUNE_*`. Effects in `CalcPlates`, positions in `CalcCoordinates`, queries in
`AirportView.cpp: AIRPORT::GetRandomTypedRune / GetRuneParNear / GetBestStairs / GetNextWaypointRune`.

| Id | Name | Par | Purpose |
|---|---|---|---|
| 2000 | CREATION | 0 | Customer spawn point (entrance, off-screen) |
| 2001 | PCREATION | player 1-4 | Player spawn at the start of each day (`AIRPORT::NewDay`) |
| 2002 | DESTRUCTION | 0 | Customer exit (they vanish on arrival) |
| 2003 | PCREATION2 | 10·(p+1) or 10·(p+5) | Player placement after the morning briefing (in front of the own office; the second set, used on day 0, near the supervisor) (`Aufsicht.cpp`) |
| 2010-2013 | UP/RIGHT/SOUTH/LEFT | — | One-way tile |
| 2036-2039 | UPRIGHT etc. | — | Two allowed exits |
| 2022/2023 | WESTEAST / NORTHSOUTH | — | Two allowed exits |
| 2020/2021 | BELT_RIGHT/LEFT | — | Moving walkway: one-way plus bit 1 |
| 2025 | NOPLAYER | — | Clear bit 4 |
| 2026 | NOCLIENT | — | Clear bit 8 |
| 2030 | CHECKIN | gate | Position at the check-in counter |
| 2031 | 2CHECKIN | gate | Start of the check-in queue |
| 2044 | 2CHECKIN_EXIT | 0 | Right end of the check-in area |
| 2045 | DURCHLEUCHTER | gate | Gate X-ray scanner |
| 2032 | SHOP | room | Inside spot of a room or shop. Reaching it **enters** the room |
| 2033 | 2SHOP | room | Room "announcement" in front of the door. Click target; where automatic walking starts |
| 2034 | WAIT | gate | Bend in the jetway |
| 2035 | 2WAIT | gate | Entrance of the gate waiting room |
| 2040 | WAITPLANE | gate | Plane door: boarding end point and arrival spawn point |
| 2041 | DROPSUITCASE | gate | Bag drop |
| 2042 | EXIT_SUITCASE | 0 | Where the luggage disappears |
| 2043 | CREATE_SUITCASE | 0 | Where arrival luggage appears (belt) |
| 2050/2051 | LEFTEND/RIGHTEND | — | Scroll limits: `LeftEnd = col·44`, `RightEnd = col·44` |
| 2054 | WAYPOINT_START | group | Spawns a scripted walker of that clan group (section 6.6) |
| 2055 | WAYPOINT | seq | Waypoint number seq |
| 2056 | WAYPOINT_G | seq | Waypoint plus gimmick animation |
| 2057 | WAYPOINT_WAIT | seq | Waypoint plus a pause |
| 2060 | NOLEFT | — | Wall on the west edge of the tile |
| 2061 | NODOWN | — | Wall on the south edge |
| 2062 | NOWAY | — | Walls on all four edges |
| 2063/2064 | AREALO / AREARU | room | Top-left and bottom-right corners of a click and tooltip area. If par is 0 it is auto-filled from a RUNE_2SHOP (or RUNE_SHOP) within 176×132 px. Each AREALO pairs with the nearest AREARU of the same par that lies below and to the right |
| 2065 | CLIPFULL (code) | — | Horizon segment fully covered: the horizon is not drawn |
| 2066 | CLIPMIDDLE (code) | — | Horizon drawn only below y=168 |
| 2067 | CLIPNONE | — | Horizon drawn fully |
| 2070 | CONDBLOCK | id | Togglable wall: blocking clears S on (col,row−1) and N on (col,row). Id 20+p blocks player p's office after an explosion |

**Discrepancy:** `brick.csv` describes 2065 as "ClipMiddle" and 2066 as "ClipFull". The code treats
2065 as FULL and 2066 as MIDDLE. Follow the code. **[H]**

### 4.5 Click and tooltip areas (`AIRPORT::IsInMarkedArea`) **[H]**

* Each area is a rectangle in airport pixel space (rune positions as raw build x,y, *not* converted).
  When areas overlap, the narrowest wins.
* Par 255 = anti-area (e.g. the mailbox): returns nothing.
* Par 254 = WALL: the player walks to just in front of the wall (y = 138 upper / 287 lower).
* Pars 140-169 / 170-199 = flight displays above check-in / gate counters (index into the
  text-brick table). Active only while the display shows text.

---

## 5. Camera, scrolling and viewport

### 5.1 Viewport **[H]**

* Window 640×480. Hall area (0,0)-(640,440). Status bar (0,440)-(640,480), `StatusLineSizeY = 40`
  (`Stdraum.cpp: CStdRaum ctor`).
* The clip rect for the hall is (0,0)-(RightClip,440). `RightClip` = 640, or the slide-in x of the
  mobile-phone panel when a phone dialog is open (section 5.5).

### 5.2 Limits **[H]**

`AirportView.cpp: FocusCameraOnPos, CenterCameraOnPlayer`.

* Horizontal: `ViewPos.x ∈ [LeftEnd, RightEnd − 320]`. `RightEnd` is effectively the **largest
  camera centre**, so the right screen edge can reach `RightEnd + 320`, while `LeftEnd` is the
  **smallest left edge**. This asymmetry is deliberate in the data: the Right segment extends
  ≈320+ px past the RIGHTEND rune, and the Left segment has ≈470 px of content left of LEFTEND.
  **WS:** generalise to `left edge ∈ [LeftEnd, RightEnd + 320 − W]`. With W > 640 the far right
  may run out of authored content. Check what is drawn beyond x ≈ RightEnd+320 (horizon and runway
  continue; the hall bricks stop). Consider extending the limits so the view never shows unauthored
  space, or fill with the last floor or wall.
* Vertical: target y = `playerScreenY − 480/3` (scroll mode) or `− 480/2` (flip mode), clamped to
  [−10, 0]. The formula clamps to `440 − (480 − 40) = 0`. So vertical scrolling is just a 10 px nudge
  that shows a bit more of the top when the player is upstairs. When the view is in the right fifth
  (`ViewPos.x > (LeftEnd + 4·RightEnd)/5`) and y < 0, it drifts back by +1 per frame. If
  `ViewPos.x > RightEnd − 800`, then y = 0.
* `CenterCameraOnPlayer` snaps y to −10 if the player's screen y < 155.

### 5.3 Follow modes **[H]**

Called once per rendered frame (`MoveCamera` from the game loop when at least one sim step ran).
The "half width" `S = 320`, or 160 when a phone dialog panel is on screen.

**Flip mode** (option "Scrolling" off, `OptionFlipping`):
* If the player is less than 90 px from the left edge: `ViewPos.x = player.x − 540`. If more than
  550 px from the left edge: `ViewPos.x = player.x − 100`. Then clamp.

**Scroll mode** (default):
* Target `T = player.x − S`, clamped to the limits.
* Keep a 5-sample history of the player's x deltas. If all samples are within ±8 of the running
  average, `avgDelta` = their mean, else 0.
* Horizontal update only if `|T_unclamped − ViewPos.x| > 2S/3` (213 px), or the camera is already
  moving, or it moved within the last 500 ms:
  * desired = `inertia(T, 2·ViewPos.x − T) + avgDelta`, where
    `inertia(a,b) = sign(a−b) · floor(sqrt((|a−b|+1)/2 + 0.5))` (`SDL TeakLibW/Math.cpp`). That is
    roughly `sqrt(|distance|)`.
  * Speed moves toward desired by ±1 per frame, by ±2 when the speed has the wrong sign or differs
    from avgDelta by more than 4.
  * Anti-jitter: a new speed value only becomes active once it was computed twice in a row (or is 0).
* If the player has not moved for more than 100 sim ticks (5 s): gently re-centre with
  `inertia(T, ViewPos.x)` at ±1 per frame.
* Never overshoot a standing player: if the player did not move this frame, |speed| ≤ 2 and the next
  step would cross T, then the speed becomes 0.
* Vertical: `inertia(targetY, ViewPos.y)`. Increase by at most 1 per frame (only when far, or when
  the remaining speed is below 4); decrease immediately.

**Jump instead of scroll:** if the camera must move more than 640 px (focus target vanished, right
click while far away), set it directly and play the screen crossfade (section 11.3).
**WS:** use W instead of 640.

### 5.4 Manual edge scrolling ("C&C scrolling") **[H]**

* Mouse x ≤ 10 with `ViewPos.x > LeftEnd`, or x ≥ 630 with `ViewPos.x + 320 < RightEnd`, and
  y < 440: the cursor becomes an arrow (click ids 6010/6011).
* Speed changes by 2 per frame. The cap is 14 px/frame while hovering and 44 px/frame while the left
  button is held. Outside the edge, or when released while faster than 8, it decays by 4 per frame.
* While a manual scroll is active (`gMouseScroll`) the camera does not follow. It resumes when the
  player is clicked to walk while visible (−5..645 px), or on right click (jumps back to the
  player, with a fade if more than 640 px away).
* **WS:** thresholds 10 and 630 → 10 and W−10; visibility test 645 → W+5.

### 5.5 Mobile-phone panel **[H]**

`Stdraum.cpp: CStdRaum::OnPaint(bHandy), PostPaint, StopDialog`.

A phone call opens a second room window that slides in from x = 800 to x = 378. Each frame
`x += v·Δt/3` (ms). The 340 px wide panel is copied to x, with a diagonal "torn" left edge drawn in 9
strips of 52 px and the `DIALOG` bar graphic. When it settles, the hall clip becomes 378
(`RightAirportClip`) and the camera half width becomes 160, so the player stays centred in the
visible left part. Hanging up slides the panel out (`v = +1`) and restores clip 640.
**WS:** 378 = "panel left edge". Anchor the panel to the right edge (`W − 262`) and centre the
camera in `[0, panelX)`.

### 5.6 Focus on another person **[H]**

Shift + click on a person makes the camera follow that person (`Sim.FocusPerson`). The person's
thought bubble is kept visible. Clicking or right-clicking in the hall cancels it.

### 5.7 Minimap

None. **[H]** The off-screen indicator for the player is the "feet" cursor: when the player sprite is
more than 40 px left of or more than 650 px right of the view (**WS:** W+10), the normal cursor is
replaced by an animated pair of walking feet (`CWALKL01..`/`CWALKR01..`, 9 frames, hotspot (24,13)).
The left-walking feet are used when the player moves west (dir 5-7), the right-walking feet when
moving east (dir 1-3). (`AirportView.cpp: OnPaint`, `Gameframe.cpp: OnPaint`)

---

## 6. Persons

### 6.1 Clans (`clan.csv`) **[H]**

`Person.cpp: CLANS::ReInit`. Each clan takes 3 consecutive rows:

**Row A (main):** col 1 Type; 2 Group (`-` = 0); 3 comment; 4 UpdateNow; 5 Wkeit (selection
weight); 6 WalkSpeed (px per step); 7 Faktor (animation slowdown); 8 HasSuitcase; 9 GimmickArt1;
10 GimmickArt2; 11-12 sprite Offset x,y; 13 palette file. Then 14 animation slots:

* Slots 0-7 (walk N,E,S,W; run N,E,S,W): three columns each: a label (ignored), a frame spec, and a
  count.
* Slots 8-11 (gimmick, wave, sit N, sit S): frame spec and count.
* Slot 12 (stand): one frame spec, always an explicit list (4 frames, one per facing; players have 8:
  facings 4-7 = standing with the phone).
* Slot 13 (waiting gimmick): frame spec and count.

Frame spec: if count = −1, a space-separated list of names. Otherwise a start name plus N: the
trailing digits are incremented N−1 times with decimal carry (`X01, X02, … X09, X10`).

Internal slot order (`Phasen[14]`): 0-3 walk N/E/S/W; 4 gimmick; 5 wave (re-used as "stuck in glue");
6 sit facing N; 7 sit facing S; 8 stand; 9 waiting gimmick; 10-13 run N/E/S/W. (The csv lists run
before gimmick; the loader remaps.)

**Row B (shadow):** cols 9-10 = GimmickOffset x,y; 11-12 = ShadowOffset x,y. The frame specs name
shadow images in `glstd.gli` (e.g. `SH_BIG`).
**Row C (skeleton):** cols 11-12 = SkelettOffset. Frame specs name X-ray frames in `skelett.pol`.

Types (`defines.h CLAN_*`): 10 female, 11 male, 18/19 "brown" female/male (none in the shipped
table), 21-24 players 1-4, 30 luggage, 40 sabotage effect objects.

The shipped table (index = clan number = `.pol` number):

| idx | Type/Group | Role | Speed | Faktor | HasSuitcase |
|---|---|---|---|---|---|
| 0-50 | 10/11, group 0 | 51 customer variants (nurse, soldier, prisoner, camper, old man, tourists, business people, fans, cyclist, cowboy, painter, robot …) | 1-5 | 1-3 | pairs ±n |
| 51 | 11/10 | X-ray skeleton | 3 | 3 | 0 |
| 52 | 11/11 | Player placeholder | 4 | 1 | 0 |
| 53 | 11/30 | Waiter (waypoint walker) | 3 | 3 | 0 |
| 54 | 11/50 | Cleaning woman (waypoint walker) | 2 | 4 | 0 |
| 55 | 11/70 | Old lady (waypoint walker) | 2 | 4 | 0 |
| 56 | 11/90 | Postman (waypoint walker) | 3 | 3 | 0 |
| 57 | 11/240 | Invisible placeholder that carries the dog's thought bubble | 3 | 3 | 0 |
| 58-61 | 21-24 | Players 1-4 (blue, green, red, yellow) | 4 | 1 | 0 |
| 62-86 | 30 | Luggage pieces, HasSuitcase = 1-25 | 1 | 2 | n |
| 87 | 40/10 | Glue puddle | 1 | 2 | 0 |
| 88 | 40/20 | Stink bomb | 1 | 2 | 0 |

**Suitcase pairing:** a customer clan with `HasSuitcase = +n` carries luggage n. Its twin with
`−n` is the same look without luggage. The type-30 clan with `HasSuitcase = n` is the luggage
sprite. Check-in turns +n into −n and spawns luggage n. Baggage claim does the reverse.

**Daily rotation** (`CLANS::UpdateClansInGame`): clans with Type > 11 or Group > 0 are always active.
Customer clans (Type ≤ 11, Group ≤ 0, HasSuitcase ≥ 0) are toggled: about 10 random flips per day.
After day 1 at most 5 are added and 5 removed. Clans with Wkeit = 1 are only picked 1 time in 20.
A suitcase twin flips together with its partner. Only active clans have their sprite pools loaded.

**Random customer pick** (`GetCustomerId(browned, koffer)`): weighted by Wkeit over active clans.
`browned` 0 = types 10/11 only, 1 = both kinds, 2 = brown only. `koffer` 99 = HasSuitcase ≤ 0;
otherwise sign(HasSuitcase) must equal koffer (−1, 0, +1).

### 6.2 Animation and sprite drawing (`CLAN::BlitAt`) **[H]**

* `Dir`/`LookDir` codes for persons: 0-3 walking N/E/S/W (frame = Phase / Faktor); 4 gimmick;
  5 wave or stuck; 6/7 sitting; 8 standing (Phase = facing 0-3, +4 = on the phone); 9 waiting
  gimmick.
* Phone pose (+4): only for the **local** player's own figure, only while it stands (Dir 8) and its window has
  a dialog open on the phone medium. Computer airlines never show it, even while the human phones them; in a
  network game each side shows it for its own figure. OpenAT addition `GameOptions.ComputerPhonePose`: a computer
  airline on the phone with the human stops where it is and shows the pose (standing frame in its last walking
  direction + 4) until the call ends.
* Running (players only) uses slots 10-13 and Faktor × 2.
* Phase advances by 1 per step and wraps at `frames × Faktor`.
* Sprite anchor: draw at `foot − (w/2, h−1) + clanOffset`. The gimmick adds GimmickOffset.
* In the hall the foot point is `ScreenPos − ViewPos − (7, −1)`. Customers also get a 0-3 px
  jitter: `x += idx & 3`, `y += (idx >> 2) & 3`, where idx = FlightPlaneIndex. Not applied when
  sitting (LookDir 6/7) or for groups > 30.
* Shadow: drawn first with `ColorFX.BlitAlpha` at `foot − (w/2, h−1) + ShadowOffset`. Skipped for
  luggage, for slot 5, when the Shadows option is off, and for a player currently going into or out
  of the security room.
* Culling: skip when `x + 200 < 0` or `x > RightAirportClip + 100`. Actually blit only in
  (−60, clip+50). Count toward the ambient crowd volume within (−260, clip+250). **WS:** use W.
* Skeleton (`BlitSkelettAt`): uses the skeleton slots with SkelettOffset (x mirrored when facing
  west).
* `BlitLargeAt`: 2× pixel-doubled version used in rooms (not in the hall).

### 6.3 Sprite pools (`.pol`, "HLine engine") **[M]**

`HLine.cpp, HLine.h`. A pool file is a TEAKFILE:

* `int32 PoolSize, PoolMaxSize; int32 BytesReal, BytesCompressed, BytesAdmin, LinesInPool, LinesRepeated`.
* `FBUFFER<CHLObj>` objects. Each object: `int64 graphicId`, `XY size` (2 × int32),
  `BUFFER<Gene>` runs (6 bytes each, 2-byte packing: `uint8 xOffset`, `uint8 count`,
  `uint32 ref`), `BUFFER<uint8>` runs-per-row.
* `PoolSize` bytes of 8-bit pixel indices, then 256 × uint16 palette (RGB565).
* A run with count ≤ 4 stores its pixels inline in `ref`. Otherwise `ref` is an offset: 0x0xxxxxxx
  into this pool, 0x1xxxxxxx into base pool 1, 0x2xxxxxxx into base pool 2.
* Base pools: when consecutive clans share a palette file, clan c uses the walk pool of clan c+1 as
  base 1. The gimmick pools use the clan's own walk pool as base 2. Load order matters (load the
  bases first).
* `clan32.pol`, `clang32.pol`, `clanw32.pol` do not exist (and `clanw37.pol`). Those clans have empty
  pools. **[L]** Verify in game what clan 32 looks like.
* Gimmick pools are unloaded when unused for 60 ticker units (about 2 s).

For OpenAT it is probably simpler to pre-extract all frames to textures (an asset-pipeline task).

### 6.4 Player character (`PERSON::DoOnePlayerStep`, `PLAYER::Walk*`) **[H]**

**Speed.** `WalkSpeed = 3 + Σ (talent − 30)/30` over the player's fitness advisors, clamped 1-5
(`PLAYER::UpdateWalkSpeed`). Step vector: E/W ±2W in x; N/S ±2W in y; diagonals ±2W in x and ±W in
y. Directions are 0 N, 1 NE, 2 E, 3 SE, 4 S, 5 SW, 6 W, 7 NW; 8 = stop. The sprite facing maps
0→N, 1-3→E, 4→S, 5-7→W.

**Running:** double-click in the hall (or pressing a room hotkey twice) sets `Running`. A running
player does two steps per sim tick (the animation phase advances only once). Running is not possible
on row-13 tiles whose exits are exactly E+W (the moving walkway). A single click resets running.
Caffeine (`Koffein`) gives two extra steps per tick.

**Click to walk** (`PLAYER::WalkToMouseClick`):
1. If the click hits a marked area with a room → `WalkToRoom(room)`. Hints are triggered for some
   rooms.
2. Otherwise convert the click to a tile. y in (170, 220] is treated as 221; y in (−20, 93] as 94.
   * Lower (220 < y < 440): `py = y − 220`, `px = x + py/2 − 86`, `col = floor(px/44)`,
     `row = floor(py/22) + 5`.
   * Upper (93 < y < 170): `py = y − 93`, `px = x + py/2 − 88`, `row = floor(py/22)`.
3. Move the target south until the tile allows players and has any exit. Abort if row 3 or 15 is
   reached (no valid floor). For upper targets, also abort if the tile is "in the air": following
   S exits from the row reaches row 3.

**Route planning** (`PLAYER::UpdateWaypoints`): there is no A*. Three targets:
* Primary = clicked tile.
* Secondary = primary shifted sideways by up to 2 tiles to the column with the deepest free space
  southward, only when the primary sits in a narrow pocket (fewer than 4 free tiles horizontally and
  less than 3 tiles of depth). This handles clicks on room interiors.
* Tertiary = the nearest stairs (`GetBestStairs`: among RUNE_2SHOP with par 50/52/54 for up, or
  51/53/55 for down, minimise `|sx − targetX| + |sx − playerX|`) when the floor changes, otherwise
  the secondary.

**Per-tile steering** (`PLAYER::UpdateWaypointWalkingDirection`, every tick): if row ≥ 15 → N;
else walk E/W until the column matches, then N/S. When the tertiary target is reached it is replaced
by the secondary, then by the primary; then stop. The waypoints are recomputed every 256 time units
while walking.

**Movement and blocking** (each tick, decisions happen when crossing a tile centre):
* Doors are triggered (section 8.1). Glue on the upper floor makes the player stuck for 600 ticks
  (wave animation, thought bubble).
* The position snaps to the tile centre for orthogonal moves.
* Allowed exits = plate bits, masked by the neighbour's player bit (4).
* On lower rows 5-14, an E or W intent becomes a diagonal (NE/SE/NW/SW) when the target row differs,
  the horizontal distance ≤ the vertical distance, and the diagonal corridor is clear.
* If the intended direction is blocked: try stepping N when the target is above and near; otherwise
  move toward the "standard lanes" (row 13 when walking east, row 11 when walking west) or toward the
  target row; otherwise stop. Upper rows 0-2 have their own up/down dodge logic.
* No collision with other persons (players walk through crowds).

**Rooms.** When the player (not auto-walking) passes within one step of a RUNE_2SHOP with room c:
* 250 exit → "call it a day?" request menu; 240-243 phones → phone/filofax menu (only if aimed at);
  236-238 player monitors → flight list menu; 244 cola machine (section 8.3).
* Other rooms, except kiosk, island, security, Rushmore, space station, Rick's and route box, which
  are only entered when explicitly targeted: switch to automatic walking (`StatePar = c`) and mark
  the room busy. Then:
  * a competitor's office or staff room → bounced back one row south with a "wrong room" request
    (with pliers: the office lock is broken);
  * a dialog is open → abort;
  * room busy (`RoomBusy > 1`) or forbidden (stayed too long before) → bounce back with a
    "thrown out" or "forbidden" message (robots re-plan instead);
  * else target = RUNE_SHOP of the room.
* Automatic walking prefers moving N first while below the target row, then E/W, then S (special
  E/W handling for the workshop on the far right and cargo on the far left). Reaching a RUNE_SHOP
  (any except the cola machine) calls `EnterRoom(c)`. The player then faces a counter-specific
  direction (last minute E, travel agency W, Arab Air, kiosk, Rick's, route box N).
* Leaving: when standing on a RUNE_SHOP while in the airport, auto-walk back to that room's
  RUNE_2SHOP + (0,22) (cargo and NASA: +(44,0); workshop: (−44,0)).

**Stairs** (pseudo-rooms 50-55):
* Going up (lower floor): walk NE until the tile has no E exit. Then snap, switch to the upper floor,
  walk W until the W exit is missing at a tile centre, then done.
* Going down: on the upper floor walk E until no E exit, drop to the lower floor (y − 22), walk SW
  until the W exit is missing, then done.
* There are three staircases (50/51, 52/53, 54/55). The escalator bricks 258/259 are only animated
  decoration.

**Idle:** after 400 ticks (20 s) standing in the hall (not on the walkway), start the waiting gimmick
(slot 9). When it ends, there is a 90 % chance it loops.

**Talking to a competitor:** clicking another player within 640 px who is in the hall, not stuck and
not talking: walk to the free adjacent tile (N, S, or E/W up to 2 tiles) and start a
`TALKER_COMPETITOR` dialog when standing. Robot players freeze and face the player.

### 6.5 Customers (`PERSON::DoOneCustomerStep`, `PersonReachedTarget`) **[H]**

* Step = clan WalkSpeed (1-5 px) in 4 directions. One step per sim tick (twice if Running). A person
  in a waiting state is only updated every 4th tick (`(TimeSlice + ClanId) & 3 == 0`).
* Only processed between 08:00 and 18:00. Waypoint walkers start after 09:00.
* Off-grid (customer row < 0 or > 9, i.e. the spawn area below the floor) the only allowed moves are N
  and S.

**Decisions at tile centres:**
* Allowed = plate exits minus "back where I came from".
* If exactly one way → take it. If none → turn until one exists; if none after 10 tries the person
  is removed.
* Otherwise by state:
  * BROWSING: random allowed direction.
  * WAITING (in a gate room): head for free seats (the seat bits); never leave via row 0 N or row 4 S.
    Entering a free seat tile sits down facing S (bit 2r+1) or N (bit 2r), snaps to the tile centre
    ±3 px and becomes SITWAITING.
  * Goal-seeking (2SHOP, 2CHECKIN, 2WAITROOM, 2EXIT): if the column matches, go N/S. Otherwise
    first move to a **lane row**, then E/W, then N/S.

**Lanes** (customer row → Position.y):

| Row | y | Use |
|---|---|---|
| 5 | 121 | Westbound outer lane (fast walkers, step > 4, when free) |
| 6 | 143 | Normal westbound lane |
| 7 | 165 | Shared middle lane, used to overtake when the next 5 tiles hold no oncoming traffic |
| 8 | 187 | Normal eastbound lane |
| 9 | 209 | Eastbound outer lane (fast walkers) |

  The outer lanes are allowed only if the 3 columns there have an E exit on rows 14 (east) or 10
  (west).

* SHOPPING / EXSHOP / WAYPOINTING: Manhattan walk (y first inside a shop, x first when leaving).
* 2CHECKIN_EXIT: always E (pushes through others).
* LEAVINGPL: E to the target x, then S; then pick the next goal (section 7.2).
* BOARDING / 2DURCHLEUCHTER: go N whenever possible, else E/W toward the target column.

**Collision** (target tile inside the grid, not in browse mode):
* Allowed if the tile is free (bit 2 clear), or `WaitCount ≥ 200`, or the person is in
  2CHECKIN_EXIT, or the occupant is moving in the same horizontal direction.
* Moving onto a tile sets bit 2 there (only on tiles with bit 8; not while boarding, in
  2CHECKIN_EXIT or waiting). The own tile is cleared at the start of each step. `iPlateDir` stores
  the direction (254 for step-1 walkers).
* If blocked while walking S in rows 3-8 and far from the target: sidestep W or E (with a +4 px
  boost).
* Otherwise wait: LookDir 8, facing kept, WAITFLAG set. `WaitCount` +1 per tick while waiting (max
  250), −10 while moving. Crossing points (rows 6-8 N/S, row 5 S) start at a patience of 180.
  2CHECKIN_EXIT row 3 eastbound starts at 198. A head-on meeting (occupant direction opposite) is
  resolved by walking through.
* Stench (bit 1 on a lower tile) changes the mood: passengers get "sick" and the airline loses 1
  image point for one in four clan ids (`ClanId & 3 == 0`).

### 6.6 Spawning **[H]**

* `CPersonQueue::AddPerson` (only 08:00-18:00): queues a spawn per spawn position. The first spawn
  appears 10 ticks later (0.5 s), luggage immediately. Further spawns at the same spot are spaced 20
  ticks (1 s) apart. `Pump` materialises due entries every tick. Arriving passengers force the spot
  time to `round((TimeSlice + 50)/80)·80` for network sync.
* Max persons ≈ 800 buffer slots, extended on demand.
* **Shoppers** (`SIM::AddNewShoppers`, every 64 ticks): during 09:00-12:00 and 14:00-16:00, if
  shoppers < 70 and total persons < 100 → one random white customer without luggage, spawned at a
  random CREATION rune.
* **Departing passengers** (`SIM::AddNewPassengers`, every 32 ticks, offset 16): for each plane
  whose next departure from the home airport starts next hour and has a gate: twice per call, with
  chance 1/6 (always from minute 20 on), add one person (with or without luggage at random) until
  `PArrived ≥` booked passengers. Each person counts for 15 passengers (45 once 100 have arrived).
  First class passengers come first (mood "ticket FC").
* **Arriving passengers:** spawned at the gate's WAITPLANE rune when the taxiing plane reaches the
  gate (section 9.2). One person per 15 passengers (economy and first class separately). Mood by plane
  condition, quality, crew and price.
* **New day** (`AIRPORT::NewDay`): all persons are removed. Players are placed at PCREATION(p+1).
  For each WAYPOINT_START build one walker of that group is created. Par 240 creates the invisible
  dog-bubble carrier, already at its target, with an initial WaitCount of 170 and mood "bone".
* Waypoint walkers: `FlightAirline` = start sequence number, `FlightPlaneIndex` = current number,
  `FlightPlaneId` = pause countdown (WAYPOINT_WAIT sets 100). Waypoint+G plays the gimmick. The
  postman (group 90) only walks after 13:00 on weekdays and leaves when the route wraps. The old lady
  (group 70) starts at 10:00 and her gimmick freezes on frame 18.
* Sabotage objects: the stink bomb (clan group 20) emits smoke for 3960 ticks and sets bit 1 on a
  3×3 area. The glue (group 10) is placed in front of a standing player on the upper floor.

### 6.7 Thought bubbles ("moods") **[H]**

* Mood ids 0-37 (`Global.cpp MoodPerson*`): 0-4 competitor sympathy (very good … hate); 5-13
  passenger opinions; 14-24 shopper intentions (bank, robber, drink, chocolate, none, travel, home,
  screen, museum, toilet, bone); 25 stench; 26 stuck; 27 dog stench; 28 empty (network thinking);
  29-37 first-class variants. Bitmaps: `MoodBms` (`MOOD00..` in `glstd.gli`).
* Shown when `MoodCountdown > 0` and the "Thoughts" option is on, only for persons with screen
  y > 219 or < 150. Countdown start value 60 (+0-14 random). It is decremented per tick; at 30 a
  random 0-29 is subtracted.
* Fade: `grade = 8 − (60 − countdown)/2` for the first 10 counts, `8 − countdown/2` for the last 10,
  else 3. Drawn with BlitTrans at that grade (8 = invisible).
* Position: `foot − ViewPos + (7 − bw·4/5, −bh − spriteHeight − 1)`.
* Moods 5-13 and 29-37 get the airline's small logo stamped at (8,10) inside the bubble.
* Triggered by the mouse hovering within 40×80 px of a person (shown at full length), by two players
  being within 80×60 of each other (robot competitors show sympathy), and by events (stench, stuck,
  toilet).

---

## 7. Passenger flow state machines **[H]**

### 7.1 Departing (REASON_FLYING)

1. 2CHECKIN → random RUNE_2CHECKIN(gate) (queue start).
2. CHECKINGIN → RUNE_CHECKIN(gate). At the counter the person stands still. Each step there is a
   1/100 chance to finish. Mood becomes "flying".
3. With luggage: DROPPING_SUICASE → RUNE_DROPSUITCASE(gate). On arrival: spawn luggage clan n at
   `pos − (0,22)` with REASON_SUITCASE_EXIT (it rides to RUNE_EXIT_SUITCASE and triggers the X-ray
   box animation), switch the person to the twin clan without luggage, wait 200.
4. 2CHECKIN_EXIT → RUNE_2CHECKIN_EXIT (east end of the check-in area).
5. 2WAITROOM → RUNE_2WAIT(gate). On arrival:
   * plane at the gate (Ort −3 and at its TargetX) → 2DURCHLEUCHTER;
   * departure hour passed, or minute ≥ 55 of the departure hour → mood "missed", 2EXIT;
   * else WAITING (browse inside the room, try to sit).
6. When a plane arrives to pick up (section 9.2), all WAITING/SITWAITING persons of that gate stand
   up (free their seats) → 2DURCHLEUCHTER → RUNE_DURCHLEUCHTER(gate).
7. At the scanner → BOARDING → RUNE_WAIT(gate). Gate security (player flags): bit 8 = enhanced
   gate. Then bit 11 → play `gate-shw.smk` and wait 240; else bit 10 → `gate-hb.smk` +
   `gate-ha.smk` and wait 240; else a 1/30 chance of a trap (`gate-trp.smk`) that removes the person.
8. At the jetway bend: plane still there → ENTERINGPL → RUNE_WAITPLANE(gate) → removed. Else
   "missed" → 2EXIT.

Gate −1 at any point (no gate assigned) → removed.

### 7.2 Arriving (REASON_LEAVING)

1. LEAVINGPL: walk E 88 px (target = spawn + (88,0)), then S.
2. Mood "sick" → walk to the WC for the gender. Otherwise 2EXIT to a random DESTRUCTION rune. If the
   clan is a "−n" twin whose "+n" partner is the previous clan entry: pick a random belt spot (at
   most 2 persons per spot), go there (2SHOP → SHOPPING) and spawn luggage n at CREATE_SUITCASE.
3. At the belt spot: wait (150 + (x + y + 33·clan) % 100) and, if matching luggage is within 16×8 px
   north, remove it, switch to the "+n" clan and go to the exit.

### 7.3 Shoppers (REASON_SHOPPING)

2SHOP → RUNE_2SHOP(room) → SHOPPING → RUNE_SHOP(room) → stand and wait (WaitCount
`150 + (x + y + 33·clan) % 100`, decremented by 5 per tick) → EXSHOP → RUNE_2SHOP → 1/4 go home
(2EXIT, mood "home"), else pick a new shop.

Shop choice (`AIRPORT::GetRandomShop`) among RUNE_2SHOP with pars 80-83, monitor 230, museum,
bank, travel agency, last minute, and the WC matching the gender. Never the same as the last one.
Rejected when closed (duty free before 10:00 or after 15:59 Sat/Sun; last minute after 15:59 or on
Saturday; museum before 11:00 or Sat/Sun; travel agency after 16:59 or Sunday). Travel agency, last
minute and monitors map to one of 4 (6) sub-spots that is not busy (`Sim.RoomBusy`). The mood is set
by the destination.

Day indexing: `Sim.Weekday` 5 = Saturday, 6 = Sunday. **[M]** (inferred from the comments)

---

## 8. Doors, shutters, triggers, animated bricks

### 8.1 Doors (`AIRPORT::TryDoor`, `PumpDoors`; frame = `Doors[par].Winkel`) **[H]**

Pumped once per sim tick.

| Kind | Bricks | Frames | Opening | Closing |
|---|---|---|---|---|
| normal | 720-726, 728 (and 729 for cargo) | 4 | State += 2 per tick until ≥ 18, then reverse | State −1 per tick; door-close sound at 0 |
| security | 718/719 | 6 | += 4 until ≥ 22 | −1 per tick |
| sabotage wall | 727 | 10 | += 2 until ≥ 40, then auto-close only if the sabotage room is empty | −1 per tick; reopens if the room is busy |

`Winkel = min(2·(frames−1), State+1)/2`.

Triggering (`TryDoor`, called when a person crosses a tile centre near the back wall): a door
reacts when the person's tile is the door tile, or ±1 column on the same row, or one row south (only
customers; security doors also 4 rows south). The cargo door reacts only to a player heading to or
coming from the cargo room. The door-open sound plays only if the door was closed, the local player
is in the hall and `|ViewPos.x + 320 − doorCol·44| < 400` (**WS:** use the view centre). The first
door after leaving a room is silent.

**Mined office** (sabotage): if a player triggers a door in rows 1-4 within 88 px of a mined office
(OfficeState 1), the office explodes. OfficeState becomes 2, `expl.smk` and `explode.raw` play, the
office is condition-blocked (rune 2070 par 20+p), nearby players are knocked against the railing
(gimmick, y = 150), and the door brick shows the explosion image
(`AIRPORT::UpdateStaticDoorImage`: bricks 734-737 get the 738 image; OfficeState 3 replaces the
office background 700/707/708/709 with 749 "dark").

### 8.2 Shop shutters (Triggered = 2, bricks 950-956) **[H]**

The shutter bitmap is drawn clipped to its own rectangle and shifted up by
`h · open/256`. `open` comes from a per-shop table indexed by the brick's BaseY column:

| BaseY | Shop | Rule (time t, 1 h = 60000 units) |
|---|---|---|
| 0 | Duty free | Opens over 2000 units before 10:00; on Sat/Sun closes over 2000 units after 16:00 |
| 1 | Arab Air | Opens up to 11:00; closed Sunday |
| 2 | Last minute | Closes over 2000 units after 16:00; closed Saturday |
| 3 | Museum | Opens up to 11:00; closed Sat/Sun |
| 4 | Travel agency | Closes over 2000 units after 17:00 |
| 5 | Plane broker | Closed after 16:00 (instant) |
| 6 | Advertising | Open from 12:00; closed Sat/Sun (instant) |

Last minute and travel agency are always closed if the mission has no travel holding
(`ROBOT_USE_TRAVELHOLDING` false). The same opening times gate entering (`PLAYER::WalkToRoom`, menu
"closed") and shopper routing. The advertising hours apply only from difficulty normal up and in the
free game.

### 8.3 Animated bricks (`BRICK::BlitAt`) **[H]**

* `TickerTime = realMs >> 5` (≈31 Hz, real time, not game time).
* Looping: `frame = (TickerTime / AnimSpeed) mod frames`. A phase argument ≥ 100 adds an offset
  (`frame = (offset + TickerTime/AnimSpeed) mod frames`). Conveyor belts 450-461 use
  `offset = ((X + Y/2)/44 + Y/22)·k mod 7` so neighbouring segments line up (k = 2 for one set, 1
  for another, mirrored for the third).
* Duration-weighted (`:n` frames): pick the frame where the cumulative weight first reaches
  `(TickerTime/AnimSpeed) mod sum`.
* One-shot (Triggered = 1, trigger index in par): `Triggers[par].Winkel` = start tick. Frame =
  `(now − start)/AnimSpeed` until the end, then reset. Used by the luggage X-ray box 471 (started
  when luggage spawns or arrives) and the cola machine 303 (electrocution: the player is hidden while
  the skeleton animation plays, then appears smoking). Special cases: kiosk man 841 animates only
  while the newspaper is interesting, else 852 (sleeping); route box pin board 421 shows frame 1
  unless `Sim.ItemClips`.
* Per-gate state bricks: logo bar 492 frame = owner+1 (0 = unrented); cameras 337/338 frame = owner
  security bit 0; counters 521/522 frame 0/1/2 by owner security bits 8/10; gate video placeholders
  760-775 drawn only if the owner has bit 8.
* Horizon strips, the runway and moving walkways are also bricks (section 9.1).

### 8.4 Airport videos (`SIM::AddSmacker`, `OnPaint`) **[H]**

A Smacker file is decoded frame by frame into the bitmap of a placeholder brick (760+gate or
768+gate) at a small offset, one frame per paint. When it ends the bitmap is cleared
(placeholders are cleared to transparent at load, `BRICKS::UpdateBricks`).

---

## 9. Planes, flight displays, departure board

### 9.1 Background layers (`AirportView.cpp: OnPaint`) **[H]**

* **Horizon parallax:** bricks 5000+5001 (sky pair, 640 + 592 px, drawn alternately), 5002
  (528 px), 5003 (528), 5004 (528). Tile x =
  `((100000 − ViewPos.x·k) >> 6) mod size − size + d·size`, with k = 16 / 30 / 42 / 56, i.e.
  scroll factors 0.25 / 0.47 / 0.66 / 0.875. The y offsets are 0 / 34 / 78 / 150.
  Tile count = `640/size + 2`. **WS:** `W/size + 2`.
* The horizon is drawn **per clip segment** between consecutive clip markers (sorted by x):
  MIDDLE → clip y 168-440, NONE → 0-440, FULL → skipped. Clip x = [max(prevMarker − ViewPos.x, 0),
  min(marker − ViewPos.x, RightClip)]. Iteration stops when a marker is at or past ViewPos.x + 640
  (**WS:** + W).
* Runway end: brick 5006 at `(RightEnd − ViewPos.x − 109, 220)`.

### 9.2 Planes in the hall **[H]**

`Planetyp.cpp: CPlane::DoOneStep` (every 10th sim step), `Sim.cpp: SIM::DoTimeStep`,
`CPlaneTypes::BlitPlaneAt`.

* Ort −1 **landing:** at :15 of the landing hour, `AirportPos = ((3·LeftEnd + 5·RightEnd)/16 + k, 22 + k/2)`
  (k staggers several planes). Each step: x += 6, y += 1 up to 40. At y = 39 the landing
  announcement plays. When x > RightEnd/2 + 600: with a gate → Ort −3 at (RightEnd + 600, 190),
  TargetX = gate's RUNE_2WAIT x + 200, "no departure"; without a gate → angry passengers, image −2.
* Ort −3 **taxiing behind the windows:** speed = clamp(|x − TargetX|/30, 3, 10) toward TargetX,
  y = 190. On arrival: unload passengers (arrivals) or call boarding (departures). Departing: at
  :55 of the start hour (or later) taxi left; past LeftEnd − 180 → Ort −2. After unloading: leave
  at minute ≥ 45.
* Ort −2 **takeoff:** starts at (−40, 44); x += 7; climbs y −1 every other px once
  `2x > (LeftEnd + 3·RightEnd)/4`; below y −30 → in the air (Ort −5).
* At :30 of the start hour: departures from home with a gate start at Ort −3 from
  (RightEnd + 200 + k, 191) to TargetX; filler flights go straight to Ort −2.
* Drawing (option "Planes"): runway planes (Ort −1/−2) as a small icon (`999S<colour>`, 100×49) at
  `AirportPos − (ViewPos.x/2, 0) + (0,14)` (half-speed parallax, foot = bottom centre). Close-up
  planes (Ort −3) at `AirportPos − (ViewPos.x, ViewPos.y − 5)`, built from nose + N cabin segments
  (N = passengers/60) + tail + engines with player-coloured overlays (`FLUG*`). Designer planes
  (TypeId −1) have their own composite.
* **WS:** the half-speed runway layer means more runway is visible on wide screens. The landing and
  takeoff paths are defined in world x; check that they enter and leave off-screen for wider views.
  Landing starts near x ≈ (3L+5R)/16 in the halved space. Takeoff ends below y −30.

### 9.3 Flight displays over the counters ("text bricks") **[H]**

`AIRPORT::CreateGateMapper, RepaintTextBricks`.

* `GateMapper[gate]` = owning player or −1. There are 2 × gates text bitmaps of 132×12. The first
  half is for check-in counters, the second half for gates.
* Refreshed every game hour (and on load). For each plane's next start from home:
  * if the start is this hour or next hour → the check-in display of its gate shows
    `FROM-TO hh:00` (city abbreviations, small white font, departure hour + 1);
  * same for the gate display.
  Arrivals this hour → the gate display shows `FROM-TO hh:00` (landing hour). The earliest entry wins
  per display. A full-name version is kept for the tooltip.
* Drawn: check-in counter 500 at +(6,2); gate wall 520 at +(43,5) with tiny owner logos at +(20,5)
  and +(134,5); departure counter 522 small logo at +(17,76). (These overlays use ScreenPos − ViewPos
  without WinP1; equivalent since WinP1 = 0.)

### 9.4 Klacker (split-flap) board — menus only **[H]**

`Klacker.cpp`, `NewgamePopup.cpp`, `Options.cpp`.

* 24 × 16 cells, glyph set `KlackerFntDef` (space, A-Z, umlauts, digits, punctuation, then extra
  Czech, Polish and Iberian letters). Cells hold the target glyph (`Soll`) and the shown glyph
  (`Haben`).
* `Klack()` (50 ms timer): every differing cell advances by 1/2/4/7/9 glyphs when the remaining
  distance is ≤3 / ≤6 / ≤10 / ≤20 / >20. It wraps early at the alphabet, umlaut and digit block
  boundaries when the target is before them, and snaps if it overshoots. Sound `Klack0.raw`, plus
  `Klack1` if more than 20 cells moved, plus `Klack2` if more than 50.
* Drawn at x = 128 + 16·col, y = 63 + 22·row. A second glyph bank (offset 87) is the disabled or
  dim look.
* Idle screensaver: `CKlackerPlanes` — after 600 ticks without mouse movement, planes (random
  logo, length up to 7 cabin segments, speed ±10 px/tick, random y 80-479) fly across. They
  sidestep the cursor or the selected cell vertically by 4 px per tick.

---

## 10. Sounds in the hall **[H]**

| Trigger | File(s) | Volume / rule |
|---|---|---|
| Crowd ambience (loop) | `Raunen.raw` | `4·n + 25` %, n = people on screen, smoothed ±1 per frame; ×2/3 when the player is upstairs (`OnPaint`) |
| Planes taxiing behind the windows (loop) | `jetfield.raw` (AMBIENT_JET_OUTSIDE) | Σ over Ort −3 planes of `(speed + 3)·T/6`, `T = 150 − |x − 320 − ViewPos.x|/4` (clamped 0-200), attenuated inside FULL clip zones; smoothed ±3 |
| Airfield (loop) | `jetout.raw` (AMBIENT_JET_FIELD) | `150 − (RightEnd − (ViewPos.x + 320))/6` → loud near the workshop end |
| Random fly-by | one of `prop, flyby, flyby2..5.raw` | Every 15-55 s real time, plane volume option |
| Door open / close | `Tuerauf.raw` / `Tuerzu.raw` | Within 400 px of the view centre |
| Sabotage door | `sabotage.raw` | Same |
| Office explosion | `explode.raw` + `expl.smk` | |
| Cola machine | `vending.raw` (with glove) / `fused.raw` | |
| Telescope rooms | `coin.raw` on entering | |
| Double-click | `click.raw` | |
| Landing announcement | voices `Der<p>Flug`, city wave, `gelande2` | Once per landing, if no voice is playing; others' landings are skipped while one of the local player's planes is landing |
| Periodic announcement | one sample: `Fahrzeug` (1/10, local generator seeded date + start time + hour) else `ZoneOK` when (hour/2) & 1 = 0, `Hinweis` otherwise | During minute :30 of the even hours 10–16, announcer queue empty, not while warping, local game speed < 2, clock running (no pause/options), not after call-it-a-day; checked every loop, so it repeats if it ends within the minute |

The ambient channels are mixed with the ambience option (`/8`) and a global volume (0 when leaving
the hall). The "outside" and "field" file names look swapped relative to their ids. That is the
original behaviour.

### 10.1 Global ambient volume **[H]**

One global volume in percent (0..100) scales everything that belongs to the airport's sound scene:

* the three ambient loops: channel volume = level × global / 100, clamped to 0..100, then × ambience option / 8
  (0 = loop stopped, it restarts when the value becomes positive again);
* the random fly-by: plane-volume option × global (also updated if one is playing when the global volume changes);
* the vending-machine / fuse sounds of the cola machine: effects option × global;
* the announcer (§12 / rooms-and-ui-3 §5.6): announcements option × global² (fixed when a sample starts).

Who sets it — on entry (the window's constructor), never for a phone ("handy") pane:

| Value | Locations |
|---|---|
| 100 | the airport hall (also every time it is shown again after the last room was left) |
| 60 | kiosk, last minute, travel agency (sets it even as a phone pane; it is never one), Arab Air, route box, cargo, designer, aircraft constructor, security, the three telescope rooms |
| 50 | Rick's café |
| 40 | own office, globe, personnel office, duty free, bank, museum, plane broker, workshop, advertising, saboteur, NASA, plane equipment |
| 30 | the airport manager's office (briefing room) |
| 0 | the evening screen, the options |

Laptop, flight planner, statistics, notice board, world map and toilets do not set it: they keep the value
of the location they were opened from. Leaving a room re-creates the location below, which sets its value again.

Levels set by the game:

* crowd loop: `4·n + 25` while the hall is drawn (n = persons within (−260, width + 250) of the view, × 2/3
  when the local figure is upstairs; moves by 1 per frame towards n; starts at n when the hall view is created);
  inside rooms it keeps its last level, so the crowd is heard faintly at the room's global volume;
* plane loop (behind the windows): recomputed in the plane step, which runs in rooms too (§10 formula,
  smoothed ±3 per loop);
* airfield loop: only while the hall is drawn.

**Quirk:** changing the global volume re-mixes all channels, and for the two plane channels the re-mix feeds a
decibel conversion of the plane volume back in as a percentage. That value is never positive, so both plane
loops drop to 0 on every global-volume change; the airfield loop stays silent until the hall is drawn again,
the planes-behind-the-windows loop comes back with the next plane step. OpenAT keeps this
(`GameAudio.SetGlobalAmbientVolume`).

Random fly-by (`prop`, `flyby`, `flyby2..5`): runs in the game loop whenever the game is not paused and no
options are open, independent of the location; real-time countdown 15..54 s (the first one at once), only with
sound effects on.

---

## 11. Status bar, input, transitions

### 11.1 Status bar layout (y 440-479) **[H]**

`Stdraum.cpp: CStdRaum::PostPaint, OnLButtonDown`.

| x range | Content | Click |
|---|---|---|
| 0-247 | `STATLIN0` background (248×40) | |
| 7,443 | Airline logo (`LOGO0..`, 65×35) | Statistics room (toggle) |
| 79,440 | Clock (`CLOCK0-3` = game speed; network variants `OCLOCK*`) 21×20 | Cycle game speed 0→3 (78-101 × 440-459) |
| 102-331 × 440-479 | 230×40 text bitmap: line 1 at y+2 "weekday hh:mm"; line 2 at y+21 money (thousands separator from the Windows system locale if it is one of dot, comma, colon, slash, apostrophe or backquote, else a dot (`Misc.cpp` Insert1000erDots) — "1,500,000" on an English system; ≥ 10⁹ shown in millions) | Hover highlights `TIMEHIGH`/`GELDHIGH` at (78,440)/(78,461). Holding a button on the time row (102-255 × 440-459, single player only) multiplies sim speed ×5 (left) or ×20 (right). Clicking the money row (80-242 × 460-479) opens the account statement |
| 248 + 60·i (i = 0..5) | Item slots 60×40 (`ITEM00..`, or empty `STATLIN5`) | `slot = (x − 250)/60` for x in 256-633; use item. Right click drops it |
| 608-639 | `STATLIN6` (32×40): top half "repeat last advisor message" (`REPEAT0x`), bottom half options (`STAT0x`) | Top: repeat message; bottom: options screen |
| 529-607 × 440 | Tutorial icon (`TUT01`) while the tutorial runs | Cancel tutorial |

Redraw policy ("speedy bar"): the bar is only re-blitted while `StatusCount > 0` (set to ≥ 3 by any
change, hover or tooltip; it counts down per frame), always if `/nospeedybar`, and during
crossfades. Otherwise it stays in the persistent back buffer. **WS:** with a wider screen the pieces
total 640 px. Anchor the logo, clock and money left and the items and icons right, and fill the gap
(`STATLIN1-4` are 640-wide variants used for editor modes, not fillers). Many hit tests use
absolute 440/460/479/634/640 values.

### 11.2 Mouse and keyboard in the hall **[H]**

* Left click: walk or enter (section 6.4). Ignored before 09:00 (morning briefing), during glue,
  while running to the toilet, or while hidden in the cola machine. Click on a player: talk. Shift +
  click: camera follow.
* Double-click: run.
* Right click: cancel follow / end manual scroll / menus.
* Hotkeys (language dependent, German table in `Gameframe.cpp: OnKeyDown`): one key per room
  (e.g. B office, P staff room, D duty free, K bank, L last minute, M museum, R route box, W
  workshop, A Arab Air, H cargo, X designer, Y security, G globe, U supervisor, S Rushmore, I kiosk,
  C Rick's, E advertising, F broker, N NASA, T travel agency, J special). Pressing twice runs. `+`/`-`
  change the game speed. The English manual lists a different letter assignment (A Petrol Air, O
  office, E plane dealer, K newspaper stand, …) **[M]**. Take the per-language tables from the source
  when implementing.
* Mouse coordinates are offset by +2,+2 internally (`gMousePosition = client + 2`). Fullscreen
  clips the cursor to (2,2)-(638,478).
* Hover over a click area shows the tooltip from `StandardTexte` TOKEN_TOOLTIP id 5000+room
  (offices formatted with the airline name; the own office adds a hotkey suffix). Flight displays use
  the generated route text.
* Tooltips appear after 600 ms, last 5 s, are placed 32 px below the cursor (or beside it near the
  bottom), and are clamped to x ≤ 639 − width.

### 11.3 Room transitions and fades **[H]**

`Takeoff.cpp: GameLoop`, `Gameframe.cpp: PrepareFade`, `SBBM.cpp: SBPRIMARYBM::Flip`.

* Entering or leaving: the player's location stack gets an ENTERING or LEAVING flag. The main loop
  (per sim step) destroys the old room window and creates the new one (`AirportView` for the hall).
* With "Blending" on (and after 09:00, or on day 0; not for the laptop): before the switch the
  current frame is copied (`PrepareFade` → `gBlendBm`). After creating the new window,
  `gBlendState = −2`. At the next flip it becomes 8: the new frame is captured into `gBlendBm2` and
  the old frame is shown. Then for states 7…1 the output is
  `old·s/8 + new·(8−s)/8` (per-channel lookup tables) — an **8-frame crossfade**. At 0 the buffers
  are freed. The globe room fades after its creation.
* The camera jump over more than 640 px uses the same crossfade.
* Pause screen: the current frame is crossfaded into `pause.gli` over 8 frames and back when
  resuming.
* Menus opened from the hall zoom from a world point (e.g. the player's position, minus ViewPos)
  to their final rect (`MenuSetZoomStuff`, ZoomCounter 0-100).
* `Blenden.Cpp`/`Cblenden.cpp` (cool zoom, fall, random, slime, h-line, shades, "terminator" blends)
  are **not referenced** by any game code. Dead code. **[H]**

---

## 12. Rendering pipeline basics

### 12.1 Frame structure **[H]**

* One 640×480 16-bit (RGB565/555) back buffer `PrimaryBm`, flipped to the window each frame. There is
  no dirty-rectangle system for the hall: it is fully redrawn every frame (horizon, planes, builds,
  persons). The only partial-update trick is the status bar (section 11.1).
* Frame pacing: the simulation advances in 50 ms steps (20 Hz × game-speed multiplier 1/2/4/8, or
  150/600 for warp and fast-forward). A repaint is requested after any step, so the hall renders at
  about 20 fps. Animated bricks use real time (`TickerTime`), persons use sim steps.
* Hall paint order (`AirportView::OnPaint`): (1) advance gate videos; (2) shutter table; (3) horizon
  per clip segment, runway end; (4) runway planes, then close-up planes; (5) merged builds and
  persons (with bubbles, smoke, X-ray screen); (6) feet cursor and ambient volumes; (7) the room base
  (`CStdRaum::OnPaint` / `PostPaint`): phone panel, dialog bubbles, advisor, status bar, menus,
  tooltips; (8) area tooltips and edge-scroll cursors.
* X-ray screen (brick 283): clip to (x+4, y+7)-(x+74, y+74) of the brick. Draw the skeleton frame of
  every person within −70…+154 px horizontally and ±14 px of y+72.

### 12.2 Colour effects (`Colorfx.cpp`) **[H]**

* `SB_CColorFX` precomputes 9 lookup tables (steps 0-8) that scale each RGB channel by step/8,
  separately for the low and high byte of a 16-bit pixel. The sum of the two lookups gives the scaled
  pixel.
* **BlitTrans(grade):** `dst = dst·g/8 + src·(8−g)/8` for non-zero source pixels (default g = 4 →
  50 %). Used for glass bricks (NonTrans = 2), thought bubbles, smoke and stench, and network clocks.
* **BlitAlpha (shadows):** the source *pixel value* (0-8) is used as the step: `dst = dst·v/8`.
  Shadow images are therefore darkness maps, not colours. Clipped to y < 440.
* **ApplyOn2:** the crossfade above. **Apply(step):** darken a region.
* **BlitWhiteTrans:** speech-bubble style blending of text panels. **[M]**
* **BlitGlow:** tutorial highlight glow. **[M]**
* The FX table is initialised with 8 steps from the hourglass cursor's pixel format
  (`GameFrame ctor`).

### 12.3 Cursor **[H]**

Software cursor (`SB_CCursor`) from `glbasis.gli`, all 32×32 unless noted:

| Look | Image | Hotspot |
|---|---|---|
| normal | `CURSOR` | (0,0) |
| hot (clickable) | `CURSORH` | (0,0) |
| exit (leave room) | `EXIT01-12` (48×48, animated at TickerTime/2) | (15,12) |
| scroll left / right | `CURSORL` / `CURSORR` | (2,16) / (29,16) |
| move h / v | `CURSORV` / `CURSORW` | (16,0) / (0,16) |
| busy (MouseWait) | `CURSORS` (61×32) | (16,16) |
| feet (player off-screen) | `CWALKL/R01..09` (48×48) | (24,13) |

---

## 13. All 640×480 assumptions (widescreen checklist)

Let W = view width and H = 480 (keep the logical height; the hall stays 440 high plus the 40 px bar).

| Where | Assumption | Suggested generalisation |
|---|---|---|
| `CStdRaum ctor` | Window rect 640×480; `StatusLineSizeY` 40 | W×480 |
| Camera (`FocusCameraOnPos`, `CenterCameraOnPlayer`, `OnRButtonDown`) | Half width `SizeX = 320` (160 with phone); clamps `ViewPos.x + 320 ≤ RightEnd` | Half width W/2; keep the right-edge maximum at `RightEnd + 320` (data-authored) → `ViewPos.x ≤ RightEnd + 320 − W` |
| Flip mode | 90 / 540 / 550 / 100 thresholds | Scale relative to W |
| Jump-instead-of-scroll | 640 px | W |
| Edge scroll | x ≤ 10, x ≥ 630, y < 440 | x ≥ W − 10 |
| Player visible test | −5…645, feet cursor −40…+650 | −5…W+5, −40…W+10 |
| `BRICK::BlitAt` | Cull x > 640 / `RightAirportClip` | Cull x > W |
| `CLAN::BlitAt` | `RightAirportClip + 50/100/250` | W-based |
| Clip rects | (0,0,640,440), (0,0,640,480), scanner reset to (0,0,640,440) | W |
| Horizon | Tile count `640/size + 2`; segment loop stops at ViewPos.x + 640 | W |
| Hash buckets | Bucket spans 3 × 320 starting at the view's left | Span ≥ W + 320, or no buckets |
| Phone panel | Slides 800 → 378, clip 378, 340 px wide | Anchor at W − 262, slide from W + 160 |
| Sounds | View centre = ViewPos.x + 320 | ViewPos.x + W/2 |
| Runway end and taxi math | Uses LeftEnd / RightEnd only (world) | Check that planes start and end off-screen for large W |
| Menus | Zoom-in centre (320,240)/(320,220); "closed" menus at (320,220); network overlay `(640 − w)/2` | Centre at W/2 |
| Tooltips | Width search up to 640; clamp 639 | W |
| Status bar | Fixed absolute x positions for logo 7, clock 78-102, text 102, items 248+60i, icons 608 / 610-640, tutorial 529-607 | Left group + right group + filler |
| `PostPaint` tip scaling | `p.x·(width+1)/640`, `p.y·(height−40+1)/440` | Already relative (identity at 640) |
| Mouse clip | (2,2)-(638,478) | W-based |
| `GameLoop` time click | (102,440)-(255,459) | Follows the status bar layout |
| Klacker screensaver | Planes enter at 640, y 80-479 | W |
| `PrepareFade`/`Flip` | memcpy 640×2 bytes × 480 rows | W |
| `CStdRaum` rooms | Most room backgrounds are exactly 640×440/480 | Rooms need pillarboxing (not part of this doc) |

Content limits for a wider hall: left of LeftEnd there are about 470 px of authored content (Left
segment starts at x = −297); right of `RightEnd + 320` the segment data stops shortly after. A
centred camera with W ≈ 854 (16:9 at 480) needs about 107 extra px on each side. That is available
on the left and marginal on the right. Verify visually, and add "dead zone" filler if needed.

---

## 14. Open questions

1. **Clip-marker naming:** csv text for 2065/2066 contradicts the code (section 4.4). Confirm
   visually which segments hide the horizon.
2. **Weekday numbering:** I assumed 5 = Saturday and 6 = Sunday from the comments. Confirm via
   `Sim.StartWeekday` and the date display.
3. **Clan 32 and 37 pools** (`clan32.pol`, `clanw37.pol` missing): what is drawn for them? Possibly
   these clans are never active.
4. **Rows 3 and 15 as "invalid" click targets:** the click logic stops at rows 3 and 15. Is row 4
   the upper-floor railing (walkable) and row 3 the gallery edge? Needs a visual check against the
   grid overlay (the editor "links" mode drew it).
5. **The lane system uses absolute customer rows (5-9).** Does every hall segment honour those lanes,
   or do some segments rely only on plate bits? A grid dump per segment would answer this.
6. **Exact RightEnd/LeftEnd values** depend on the rune bitmap sizes (35×20 etc.). My numbers used
   `glbrick0.gli`. Season sets should be identical in size, but verify.
7. **`shops.dat`** (no number, different header): unused leftover, or a city or season specific
   file?
8. **Brown clans (types 18/19):** none in `clan.csv`, but arrivals call `GetCustomerId(1, …)`
   ("both"). Confirm no other table adds them.
9. **Frame rate:** rendering is tied to the 20 Hz sim steps in the original. OpenAT may interpolate,
   but walk and lane logic must stay on the 20 Hz tick to keep queue and collision behaviour.
10. **Network sync** of persons (`PersonalRand`, `PersonRandCreate` seeding per minute) matters only
    for multiplayer determinism. Not covered in depth here.
11. The **"First Class" mode** in the SDL port (`bFirstClass` in `AirportView.cpp`) changes the door
    and gate-video index ranges. It is a port addition for the non-deluxe data set and does not apply
    to Deluxe.

---

## 15. Source map (for traceability)

* Layout: `Brick.cpp: BRICKS::ReInit, BUILDS::Load/Sort`; `AirportView.cpp: AIRPORT::Load, LoadAirport, CalcPlates, CalcCoordinates, CalcSeats, DoHashBuilds, RemoveRunes, UnassociateBuilds, NewDay, CreateGateMapper, RepaintTextBricks, IsInMarkedArea, GetRandomShop, GetBestStairs, GetNextWaypointRune, PumpDoors, TryDoor, UpdateStaticDoorImage, SetConditionBlock`
* View: `AirportView.cpp: AirportView::OnPaint, FocusCameraOnPos, MoveCamera, CenterCameraOnPlayer, OnLButtonDown/Up/DblClk, OnRButtonDown`
* Persons: `Person.cpp: CLANS::*, CLAN::BlitAt/BlitSkelettAt, PERSON::PERSON, DoOneCustomerStep, DoOnePlayerStep, DoOneAnimationStep, PersonReachedTarget, PERSONS::DoOneStep, TryMoods, CPersonQueue::*`
* Player walking: `Player.cpp: PLAYER::WalkToRoom, WalkToMouseClick, WalkToPlate, UpdateWaypoints, UpdateWaypointWalkingDirection, UpdateWalkSpeed, EnterRoom, LeaveRoom, CalcRoom`
* Spawning: `Sim.cpp: SIM::AddNewShoppers, AddNewPassengers, AddSmacker, AddStenchSabotage, AddGlueSabotage, DoTimeStep, NewDay, UpdateSeason`
* Planes: `Planetyp.cpp: CPlane::DoOneStep, CPlaneTypes::BlitPlaneAt`
* Loop and transitions: `Takeoff.cpp: CTakeOffApp::GameLoop`; `Gameframe.cpp: GameFrame::OnPaint, PrepareFade, OnMouseMove`; `SBBM.cpp: SBPRIMARYBM::Flip`
* Room base and status bar: `Stdraum.cpp: CStdRaum ctor, OnPaint, PostPaint, OnLButtonDown, StopDialog`
* Effects: `Colorfx.cpp: SB_CColorFX::*`; sprites: `HLine.cpp`
* Board: `Klacker.cpp`; talkers: `Talker.cpp` (pure state counters for room dialog partners: 0 idle,
  1 face-to-face, 2 picking up the phone, 3 on the phone, 4 hanging up; phases advance once per sim
  step; "locking" by robots marks a partner busy)
* Sounds: `Init.cpp: InitSoundSystem`; `Sound.cpp: CAmbienteManager`
