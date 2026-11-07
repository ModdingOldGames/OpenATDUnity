# Data tables and text resources (ATD install: `data/`, `misc/`, `gli/*.pol`)

Clean-room behaviour spec. Everything here is written in our own words from reading the
reference sources (`docs/spec/local/gog_src`, `docs/spec/local/ref_AirlineTycoon/src`) and from looking at
decoded copies of the shipped files. No code is transcribed. In-game text is not copied: text
is referred to by resource file plus group and id.

Confidence tags: **[H]** verified in code and data, **[M]** read in code but not run, **[L]** inferred.

---

## 0. Conventions shared by all tables

### 0.1 Container: xtRLE

Most `data/*.csv` and the big `misc/*.res` files are wrapped in xtRLE (format given in the task
brief). The loader is the generic "load complete file" helper (`TeakLibW/TeakFile.cpp: LoadCompleteFile`
via `CRLEReader`). It detects the `xtRLE\0` magic and passes non-RLE files through unchanged.
Every shipped RLE file uses version `0x102`, so literal runs are XOR `0xA5`. **[H]**

| File | Wrapped in xtRLE? |
|---|---|
| `Names.csv`, `brick.csv`, `city.csv`, `clan.csv`, `piloten.csv`, `planetyp.csv`, `pnames.csv`, `routen.csv`, `stdpaper.csv` | yes |
| `builds.csv`, `relation.csv` | **no**, plain text (they are read with a plain text-file reader, see §1.10) |
| `misc/std_ger.res`, `misc/dlg_ger.res`, `misc/ein_ger.res` | yes |
| `misc/register.res`, all `misc/*.dat`, `misc/*.pln`, `gli/*.pol` | no |

An implementation should always go through one "open, un-RLE if the magic is present" helper.

### 0.2 Character set

The bytes are Windows-1252 (German umlauts, accented letters). A few fields go through an
umlaut fix-up helper (`Misc.cpp: KorrigiereUmlaute`), applied to city names, worker comments and
the route table's city-name lookups. Treat the files as CP1252 and decode to Unicode at load. **[M]**

### 0.3 Line splitting (`Misc.cpp: ReadLine`)

* A line ends at any run of the bytes CR (13), LF (10) or SUB (26). A run of several terminators
  counts as one break, so blank lines never come through as records. **[H]**
* `routen.csv` uses **CR-only** line endings. All the other CSVs use CRLF. The rule above covers both. **[H]**
* Each loader has a line-buffer limit: 300 bytes for most tables, 800 for `planetyp.csv`, 5000 for
  `clan.csv`. A longer line is cut, and the rest is read as the next record. No shipped line hits
  its limit, but a modder could. Rejecting such lines is the safe choice. **[M]**
* The **first line is always a header** and is skipped. No loader reads a second header row. **[H]**
* After reading, trailing CR/LF/SUB bytes are removed from the line.

### 0.4 Field splitting (this matters for compatibility)

* The delimiter set is `;` plus the backspace byte `0x08` (`Global.cpp: TabSeparator`).
* For the first field of a row, several loaders also treat `"` as a delimiter.
* The splitting works like C `strtok`: **runs of delimiters collapse, so an empty field does not
  exist and every later column shifts left.** No shipped table relies on empty fields, apart from
  one blank header cell in `clan.csv`, and header rows are skipped anyway. A reimplementation must
  either use the same rule or reject rows with empty fields. **[H]**
* Quotes are **not** CSV quoting. They stay inside the text. Only the worker table strips one
  leading and one trailing `"` from its comment column (§1.6).

### 0.5 Number parsing (C `atoi`/`atof` semantics, which matter for the data)

* Integers are read from the leading digits, with an optional sign, and parsing stops at the first
  other character. So `-52,2` becomes **-52** (the comma is not a decimal point), and
  `x` or `-` become **0**. **[H]**
* `atof` accepts a dot as the decimal point, so `1.21` becomes 1.21 and `.8` becomes 0.8. It stops
  at a comma, so `1,1` becomes **1.0**.
* Several `planetyp.csv` cells were turned into dates by Excel (for example `11. Mrz` or `01. Jun`).
  Under the rules above they parse as the leading integer: `11. Mrz` gives 11, and `01. Jun` read as
  a float gives 1.0. The game uses those mangled values as they are. To reproduce the original
  numbers, apply the same prefix rule. **[H]**

### 0.6 "Album" containers and ID schemes (`TeakLibW/Album.cpp`, `TeakLibW.h: ALBUM`)

Most tables live in an *album*: a fixed-capacity array in which each slot carries a 32-bit id,
with 0 meaning empty.

* A value **≥ 0x1000000** is treated as an **id** and looked up by a linear search over the slots.
  A smaller value is treated as a **slot index**. Every lookup accepts either form. **[H]**
* Two kinds of insert exist. *Front-add* places the new id in the first free slot from index 0
  upwards. *Back-add* places it in the first free slot from the **last** index downwards. Which one a
  loader uses decides the index order. **[H]**
* Generated ids start at `0x1000000` (the album's "last id" counter starts at `0xFFFFFF` and
  pre-increments).
* "Pick a random used slot" helpers walk the slots **from the highest index downwards** and return
  the k-th used one, with k = `Rand(n)+1`. To reproduce original random sequences, keep this
  descending walk. **[H]**

| Table | Id stored | Insert | Capacity | Resulting index order |
|---|---|---|---|---|
| Cities (`city.csv`) | csv Id + `0x1000000` | front | 80 (`MAX_CITIES`), then shrunk to the row count | index = file row order (row 1 → index 0) |
| Plane types (`planetyp.csv`) | csv Id + `0x10000000` | back | 30 (`MAX_PLANETYPES`), **not** shrunk | first row → index 29, so the low indices stay empty |
| Bricks (`brick.csv`) | csv Id + `0x10000000` | back | 340 (`MAX_BRICKS`) | first row → index 339 |
| Routes (`routen.csv`) | generated (`0x1000000`, `0x1000001`, …) | back | 370 (`MAX_ROUTES`) | then **sorted** by departure-city name (§1.3) |
| Clans (`clan.csv`) | generated | back | 90 (`MAX_CLANS`) | first clan → index 89 |
| Workers (`piloten.csv`) | plain array, no ids | — | 350 (`MAX_WORKERS`), then shrunk | file order |

### 0.7 Random number generator used by the table logic (`Misc.cpp: TEAKRAND`)

This is a 32-bit LCG: `value = value*7381 + 0x269EC3 (mod 2^32)`, returning `(value >> 16) & 0xFFFF`.
`Rand(n)` returns `Rand() % n`, and `Rand(a,b)` returns `Rand() % (b-a+1) + a`. Seeding sets both the
seed and the value. Every contract, route and headline generator documented here uses this RNG
with an explicit seed, so the original draw order can be reproduced. **[H]**

### 0.8 Loading order and when tables are (re)loaded

* At startup (`Takeoff.cpp`), in this order: language byte (`misc/sabbel.dat`), dialogue and standard
  text (`Dlg_Ger.res`, `Std_Ger.res`), unit table (`ein_ger.res`), clans (`Clan.csv`) and their
  `.pol` pools, cities (`City.csv`), plane types (`PlaneTyp.csv`), plane names (`PNames.csv`),
  newspaper headlines (`StdPaper.csv`). The plane designer's `relation.csv` and `builds.csv` are read
  during graphics init (`Init.cpp`). **[H]**
* Bricks (`Brick.csv`) are loaded with the airport (`Sim.cpp`).
* Workers (`piloten.csv`, `names.csv`) are loaded at new game (`Sim.cpp`).
* Routes (`Routen.csv`) are re-parsed **at every new game and every savegame load**
  (`NewgamePopup.cpp`, `Sim.cpp: LoadGame`). Old saves made before the Deluxe cities existed are
  extended with `ReInitExtend` (§1.3).
* File names are written in mixed case in code (`City.csv`, `Left%03li.dat`). The install has
  lowercase names, which Windows accepts. A port should resolve names case-insensitively.

---

## 1. `data/*.csv`

### 1.1 `city.csv`: cities (`City.Cpp: CITIES::ReInit`) **[H]**

75 rows plus a header, 18 columns.

| # | Header (German) | Field | Meaning / unit |
|---|---|---|---|
| 1 | Id | id | 100–177 (with gaps). Runtime id = Id + `0x1000000`. |
| 2 | Name | Name | Display name, run through the umlaut fix-up. Used as the **lookup key** by `routen.csv` (case-insensitive) and as the route sort key. |
| 3 | Lage | Lage | Free-text region label (trailing spaces in some rows; shown in UI only). |
| 4 | Areacode | Areacode | Region bucket used by contract generation: 1 = Europe, 2 = Americas, 3 = Africa/India, 4 = Asia/Oceania (per the field comment in `class.h`). Data counts: 26/27/9/13. One row (Id 132) is labelled North Africa but has area 2. Keep the data as it is. |
| 5 | Abk. | KuerzelGood | Three-letter "readable" short code. |
| 6 | Abk. | KuerzelReal | Real IATA code. The option "real codes" (`Options.OptionRealKuerzel`) picks column 5 or 6 into the active code (`CITIES::UseRealKuerzel`); **default on** (registry default TRUE), so the shipped game shows real codes such as LHR or TXL on the travel-agency notes and planner (verified on the running original). |
| 7 | Raw | Wave | Base name of the city's voice sample (airport announcer, branch phone call). `-` means none. |
| 8 | TextResBaseId | TextRes | First id of the city description pages in `std_ger.res`, group `City`. Page p (0-based) uses id `TextRes+p`. |
| 9 | AnzText | AnzTexts | Number of description text pages (1–2). |
| 10 | PhotoFilename / GLI-Filename | PhotoName | Graphic name of the first city photo in the picture library. Photo page k is made by adding k to the **last character** of the name (`AMSTER1`, `AMSTER2`, …). `NOCITY` with count 0 means no photo. |
| 11 | AnzPhoto | AnzPhotos | Number of photos (0–6). |
| 12 | Einwohner Stadt | Einwohner | Population (persons). Drives route demand (see contracts-and-routes.md §3.2). |
| 13 | Breite (X) | GlobusPosition.x | **Longitude in whole degrees**, east positive. Used for distances and for the 3-D globe. Decimals after a comma are dropped (§0.5). |
| 14 | Höhe (Y) | GlobusPosition.y | **Latitude in whole degrees, north negative** (Europe is about -50). The sign convention does not affect distances. |
| 15 | Breite (X) | MapPosition.x | Position on the flat world map (2-D views). Usually equal to column 13, nudged for some cities to avoid label overlap. Same units. |
| 16 | Höhe (Y) | MapPosition.y | Same, for the flat-map latitude. |
| 17 | Miete | BuroRent | **Monthly** rent of a branch office in this city (game currency, 1 000–420 000). Also the opening bid on the auction board (contracts-and-routes.md §6). |
| 18 | NewInAddOn | bNewInAddOn | 0 = base game city, 1 = added by the first add-on, 2 = added by Deluxe. Value 2 marks the routes touching that city as "new in Deluxe" and drives save-game migration (`Sim.cpp: LoadGame`). Counts: 48/7/20. |

Notes:
* Duplicate Ids raise a fatal error.
* The album is shrunk to the actual row count, so **city index = row order**. Several systems
  (route lookups, foreign contract arrays, branch arrays) are indexed by this order.
* Derived data (distances, flight times) is in contracts-and-routes.md §1.

### 1.2 `routen.csv`: predefined route pairs (`Route.Cpp: CRouten::ReInit`) **[H]**

175 data rows, **CR-only line endings**, 5 columns.

| # | Header | Field | Meaning |
|---|---|---|---|
| 1 | Ebene | Ebene | "Level": 1 = fine/regional (135 rows), 2 = coarse/long-haul (40 rows). Used as a display filter (the "short/long routes" toggle on the globe and laptop) and in AI/start-up heuristics. |
| 2 | Von | VonCity | Departure city, given by **name**, resolved through the city table (case-insensitive, after umlaut fix-up). An unknown name is a fatal error. |
| 3 | Nach | NachCity | Arrival city, by name. |
| 4 | Miete | Miete | **Monthly** licence fee (50 000–1 600 000). |
| 5 | Beliebtheitsfaktor | Faktor | Popularity multiplier, a float with a dot decimal (0.3–3.5, mostly 1). Scales demand. |

Load behaviour:
1. The route album is reset to capacity 370.
2. Each row creates **two directed routes**: A→B, then B→A. Both get freshly generated ids and the
   same level, fee and factor. Demand (`Bedarf`) starts at 0. 175 rows give 350 directed routes.
3. With the de-duplication flag on (the default for new games), a row is skipped when an A→B route
   already exists. The shipped file has no duplicates, in either orientation.
4. Each route is flagged "new in Deluxe" when either endpoint has `NewInAddOn==2`. This flag is not
   saved.
5. Finally the album is **sorted by the departure city's display name** (string compare). Empty
   slots move to the end. So route **indices depend on the language of the city names**. Ids stay
   stable, so persist routes by id, never by index.
* `ReInitExtend` (old-save migration) skips the first `NumUsed/2` rows, appends the remaining rows,
  then sorts again.
* Missions may add extra synthetic routes at runtime (contracts-and-routes.md §3.1).

### 1.3 `planetyp.csv`: plane types (`Planetyp.cpp: CPlaneTypes::ReInit`) **[H]**

26 rows, 24 columns. The runtime id is Id + `0x10000000` (back-filled, see §0.6).

| # | Header | Field | Unit / note |
|---|---|---|---|
| 1 | Id | — | 100–126 |
| 2 | Hersteller | Hersteller | Manufacturer (text) |
| 3 | Bezeichnung | Name | Model name |
| 4 | Notizblockphoto | NotizblockPhoto | Graphic name packed into a 64-bit id (§1.11). All rows say `999XL`. |
| 5 | AnzPhotos | AnzPhotos | |
| 6 | Ab Mission | FirstMissions | Mission index (difficulty number) from which the type is available |
| 7 | Ab Tag | FirstDay | Day within that mission from which it is available |
| 8 | verfügbar ab | Erstbaujahr | First year built (display) |
| 9 | Spannweite (m) | Spannweite | Integer metres. Excel damage: `29. Jun` is read as 29. `x` is read as 0. |
| 10 | Länge (m) | Laenge | m |
| 11 | Höhe (m) | Hoehe | m (often date-mangled) |
| 12 | Max. Startgewicht kg | Startgewicht | kg |
| 13 | Passagiere | Passagiere | Seat count (pax). The cargo payload per flight is derived as `Passagiere/10` tonnes (contracts-and-routes.md §5). |
| 14 | Vreise kph | Geschwindigkeit | Cruise speed in km/h |
| 15 | Reichweite km | Reichweite | Range in km |
| 16 | Triebwerke | Triebwerke | Engine description (text) |
| 17 | Schub (lb) | Schub | lbf |
| 18 | Besatzung | AnzPiloten | Pilots needed |
| 19 | Service | AnzBegleiter | Cabin crew needed |
| 20 | Fuel max. (l) | Tankgroesse | Litres |
| 21 | Verbrauch (l/h) | Verbrauch | Fuel burn in litres/hour |
| 22 | Neupreis | Preis | New price |
| 23 | Wartungsfaktor | Wartungsfaktor | Float maintenance factor. **Mangled cells apply**: `01. Jun` gives 1.0, `1,1` gives 1.0, `2,5` gives 2.0, `.8` gives 0.8. |
| 24 | Bemerkung | Kommentar | Description text. This is in-game text, so do not copy it. It may contain `"` and `#`, and is taken as the rest of the line up to the next `;`. |

`GetRandomExistingType`: a type counts as available when `FirstMissions < difficulty`, or when it
equals the difficulty (or this is free play) and `FirstDay ≤ today`.

### 1.4 `brick.csv`: airport building blocks (`Brick.cpp: BRICKS::ReInit`) **[H]**

333 rows, 16 columns. The runtime id is Id + `0x10000000` (back-filled). Ids run from 100 to 5006.

| # | Header | Field | Meaning |
|---|---|---|---|
| 1 | Id | — | |
| 2 | Beschreibung | Filename | Descriptive name (stored as "Filename", used for editor display) |
| 3 | RAM-Priority | RamPriority | Caching priority |
| 4 | opaque | NonTrans | 1 = opaque bitmap (no colour key) |
| 5 | Triggered | Triggered | Animation trigger mode. Forced to 90 when any frame carries an explicit wait (see column 16). |
| 6 | Layer | Layer | Z-layer for sorting placed bricks |
| 7 | Speed | AnimSpeed | Animation speed |
| 8 | FloorDist | FloorOffset | Added to the bottom y when depth-sorting |
| 9–10 | BaseX, BaseY | BaseOffset | Anchor offset (pixels) |
| 11–12 | GridX, GridY | Grid | Placement snap grid in the editor |
| 13–14 | MinY, MaxY | MinY, MaxY | Allowed vertical placement range |
| 15 | Obstacle | ObstacleType | Walk-map obstacle class |
| 16 | TGA-Name | graphicIDs / WaitTimes | **Space-separated** list of frame graphic names. A frame written `NAME:n` waits n ticks, otherwise 1. `WaitSum` is the total. Each name is packed into a 64-bit graphic id (§1.11). |

Brick ids 5000+ are backgrounds and horizon. Special "rune" bricks (`RUNE_*`) carry room and gate
semantics in the airport layout (§2.2).

### 1.5 `clan.csv`: pedestrian/person animation sets (`Person.cpp: CLANS::ReInit`) **[M]**

89 clans, **3 physical rows per clan** (main row, shadow row, skeleton row), 48 columns.

Main row: col 1 `Typ` (10 = female, 11 = male, 18/19 = brown female/male, 21–24 = player figures
1–4, 30 = suitcase, 40 = special animation), col 2 `Group`, col 3 comment (ignored), col 4
`UpdateNow`, col 5 `W'keit` (spawn probability), col 6 `WalkSpeed`, col 7 `Faktor`, col 8
`HasSuitcase`, cols 9–10 gimmick kinds, cols 11–12 sprite offset x/y, col 13 palette file. A clan is
"in game today" when Type > 11 or Group > 0. Group 70 gets a floor offset of -15.

Shadow row: cols 9–12 = gimmick offset x/y and shadow offset x/y. Skeleton row: cols 11–12 =
skeleton offset x/y. Cols 1–8 (shadow row) and 1–10 (skeleton row) are ignored.

Then 14 animation lists in columns 14–48 (same layout on all three rows, giving phases, shadows and
skeleton):
* Walk N/E/S/W and Run N/E/S/W each use three columns: *ignored preview*, *start name*, *count*.
* Gimmick, Wave, Sit-N, Sit-S each use two columns: *start name*, *count*.
* Stand uses one column: an explicit list.
* Stand-gimmick uses two columns.
* A count of **-1** means the name column is an explicit space-separated frame list. Otherwise the
  frames are `count` consecutive names made by incrementing the trailing two-digit number
  (`X_N01`, `X_N02`, …, `X_N09`, `X_N10`, with the carry going into the tens digit).
* Internal phase slots are remapped as follows: walk = 0–3, gimmick/wave/sitN/sitS/stand/standgimmick
  = 4–9, run = 10–13.

The bitmaps come from `gli/clan%d.pol`, `clang%d.pol` (gimmicks) and `clanw%d.pol` (waiting
gimmicks) (§3).

### 1.6 `piloten.csv`: staff pool (`Personal.cpp: CWorkers::ReInit`) **[H]**

319 rows, 7 columns.

| # | Header | Field | Meaning |
|---|---|---|---|
| 1 | Name | Name | Full name |
| 2 | Geschlecht | Geschlecht | 0 = female, 1 = male |
| 3 | Art | Typ | 1–9 = advisor kinds (1 personnel, 2 fuel, 3 route, 4 order/contract, 5 money, 6 info/spy, 7 plane, 8 fitness, 9 security; see `BERATERTYP_*`), 10 = pilot, 11 = flight attendant |
| 4 | Gehalt in DM | Gehalt | Monthly salary (1 100–25 000) |
| 5 | Talent | Talent | Skill 0–100 |
| 6 | Alter | Alter | Age in years |
| 7 | Kommentar | Kommentar | CV text (in-game text). **Everything up to the end of line**, so it may contain `;`. One leading and one trailing `"` are stripped. |

At load every worker starts in the "reserve" pool, with happiness 100 and `OriginalGehalt = Gehalt`.

### 1.7 `Names.csv`: random person names and manual-check words (`Personal.cpp`, `Protect.cpp`) **[H]**

202 rows, 2 columns: `Art;Name`.
* Art 0 = female first names (39), 1 = male first names (39), 2 = last names (79). Each is
  loaded into its own list, capped at 40/40/80. These names are used to make random people (for
  example generated staff).
* Art ≥ 1 000 000 (45 rows) are **copy-protection words** (`Protect.cpp: GetProtectionString`).
  The number encodes three 2-digit values, `1 aa bb cc`, which are shown as page `aa`,
  paragraph `bb`, word `cc` of the printed manual (prompt text: `std_ger.res`, group `Misc`, id 3090).
  Column 2 is the expected word. The Steam and GOG builds do not need this check. A port can skip it.

### 1.8 `pnames.csv`: plane names (`Planetyp.cpp: CPlaneNames::ReInit`) **[H]**

150 rows: `Name;Qualität`. Quality 1 rows (110, capacity 200) go to the preferred list, all others
(40, capacity 100) to the fallback list. New planes get a random unused name: first random picks
from list 1, then a linear scan of list 1, then the same for list 2, then a literal placeholder.
**Entry 0 of each list is never chosen at random**: the random pick is `1+Rand(n-1)`, and the
linear scans also start at 1.

### 1.9 `stdpaper.csv`: newspaper headlines (`Misc.cpp: HEADLINES::*`) **[H]**

304 rows: `Zeitung;Kette;Schlagzeile`.
* Zeitung is 1–3: one of the three newspapers (127/97/80 rows).
* Kette 0 means a standalone random filler (38/63/41 per paper). A non-zero value is a **story-chain
  step number** (for example 1010, 1011, 1012 …). Consecutive numbers continue a story day after day.
* Each day (seeded with `date + game start time`), each paper gets one random filler. With
  probability 1/20 it may instead start a chain at a chain head (a step that does not follow its
  predecessor in the file). It continues its own chain (step+1), or continues a chain begun in one of
  the other papers.
* Tokens in the text: `{city}` is replaced with a random city name, which is remembered per paper.
  `{{city}}` repeats the remembered city. `{rand(a,b)}` is replaced with a random number, remembered
  per paper. The original computes it as `Rand(b+1)`, a quirk that ignores `a`. `{rand}` repeats the
  remembered number. `{n}` with n ≥ 1 is replaced with the airline name of player `(n-1) mod players`.
* Game events add "override" headlines with a picture id and priority. Those fill the empty slots of
  that paper (10 per paper), which are then sorted by priority.

### 1.10 `builds.csv` and `relation.csv`: plane designer parts (`Init.cpp`, `Editor.cpp`) **[H]**

These two files are **plain text, not RLE**. They are read with a plain text-line reader and split
with a simple `;` splitter (`Editor.cpp: ParseTokens`, which **keeps empty fields**, unlike §0.4).
The part list and its structure (graphics, attachment slots, which part attaches to which) are
**compiled into the executable** (`Editor.cpp: gPlaneBuilds[37]`, `gPlanePartRelations[307]`). The
CSV only **overrides numeric balance values**, row by row, **in compiled order**. A row id that does
not match the compiled id is only logged, and the values are still applied by position. The CSV
values differ from the compiled defaults, so **the CSV is authoritative**.

`builds.csv` (37 rows): `Id;ShortName;Cost;Weight;Power;Noise;Wartung;Passagiere;Verbrauch`.
Id/ShortName are informational: 1000+ body (B1–B5), 2000+ cockpit (C*), then wings (R*),
engines (M*), tails (H*), and so on. Units: Cost in currency, Weight in kg, Power (engine thrust;
negative for drag parts), Noise (dB-ish; negative values reduce noise), Wartung (maintenance
modifier), Passagiere (seats added), Verbrauch (fuel burn added).

`relation.csv` (307 rows): `Id;From;To;Offset2dX;Offset2dY;Offset3dX;Offset3dY;Note1;Note2;Note3;Noise`.
From/To are the part short names this relation connects (`-` = root/body). They are informational
only; the links themselves are compiled in. The loader takes the 2-D blueprint offset, the 3-D view
offset, three "note" codes (crew and capacity remarks shown in the designer) and a noise modifier.

### 1.11 Graphic name → 64-bit id (used by bricks, clans, plane photos)

A graphic name of up to 8 ASCII characters is packed into a 64-bit integer, little-endian: character
i goes into byte i (`StringToInt64` / `GetIdFromString`). Pools and GLI libraries index their bitmaps
by this id. **[H]**

---

## 2. `misc/*.dat`

### 2.1 Common binary serialisation (TEAKFILE) **[H]**

Little-endian. `SLONG`/`ULONG` = 4 bytes, `UBYTE` = 1, `XY` = two SLONG (8 bytes). A buffer is
`count:i32, deleted-offset:i32`, then `count` elements. A string is `len+1:i32`, then the bytes
including the NUL. An album is its element buffer, followed by `lastId:u32` and an id buffer
(`count:i32, offset:i32, ids:u32[count]`).

### 2.2 Airport layout pieces: `Left*.dat`, `Check*`, `Ofice*`, `Entry*`, `Shops*`, `Cafe*`, `Secur*`, `Suit*`, `WaitZ*`, `Right*` (`Brick.cpp: BUILDS::Load`, `AirportView.cpp`) **[H]**

The airport hall is made of 10 consecutive segments: left end, check-in, offices, entry, shops,
cafe, security, suitcase, waiting zone (gates), right end. Each segment has variants stored as
`<Segment>%03d.dat`, where the number is `100*difficulty + level`. Examples: `left001.dat` is
difficulty 0 level 1, `left1101.dat` is difficulty 11 (add-on mission 1), and `left4101.dat` is
difficulty 41 (Flight Security mission 1). Free play (difficulty -1) maps to difficulty 7 (`…701`).
If no file exists for the current difficulty, the loader **falls back to lower difficulties** one
by one until it finds a file. If none exists, the segment is empty.

Layout: the build list (`count:i32, offset:i32`, then `count` × {`BrickId:i32`, `ScreenPos:XY`,
`Par:u8`} = 13 bytes each), followed by the album trailer (`lastId`, id buffer). Example:
`left001.dat` has count 540, so 8 + 540·13 + 4 + 8 + 540·4 = 9200 bytes, which matches the file
size. `BrickId` is the runtime brick id (`0x10000000 + brick.csv Id`). `Par` is a per-placement
parameter, for example the room number of a door rune or the gate number of a gate rune. The
number of gate runes (`RUNE_2WAIT`) in the waiting-zone segment is the number of rentable gates
(contracts-and-routes.md §7).

### 2.3 Other `.dat`

| File | Content |
|---|---|
| `sabbel.dat` | First 4 bytes (i32) = **UI/text language index** into the language letter string `D E F T P N I S O B J K L M N Q R T U V` (0 = German, 1 = English, …). Here it is 1, English. The remaining bytes are padding. (`Takeoff.cpp`) **[H]** |
| `shops.dat` | First i32 = expected checksum of the executable (anti-tamper check, `Protect.cpp: CheckMainFile`). The rest is noise. Not needed by a port. **[H]** |
| `xmlmap.fla` (not .dat, but same folder) | **High-score table**, 6 text lines: `Name;GameId;k1;k2;k3;k4;k5`. The score is accepted only when `k4^k1^k3 == k5^k2`, and then score = `k5^k2`. On write, k1..k3 are random and k4 = score^k1^k3, k5 = score^k2. A row of zeros means empty. (`Sim.cpp: Save/LoadHighscores`) **[H]** |
| `_version.txt` | Language/edition tag (`english`). |
| `*.mcf` | Bitmap fonts (out of scope here). |

### 2.4 `pl45.pln`, `pl48.pln`: prebuilt designer planes for the AI (`Player.cpp`, `Editor.cpp: CXPlane`) **[H]**

In difficulty 45 and 48 (Flight Security missions 5 and 8), an AI visiting the designer loads
`pl<difficulty>.pln` and buys copies while its money exceeds plane cost + 6 000 000.

Format: a "preview header" for external tools, consisting of `size:u32` (= 4 + 6·4 + name length),
six i32 values (cost, weight, consumption, noise, "reliability" (holds the consumption value
again, an original quirk) and speed), then the plane name as a NUL-terminated string. After that
come the real fields: name as a length-prefixed string, `Cost:i32` (garbage `0xCDCDCDCD` in the
shipped files) and the parts album. Each part is `Pos2d:XY, Pos3d:XY, Shortname:str,
ParentShortname:str, ParentRelationId:i32`. The writer emits 7 header values (it adds range), but the
reader skips by `size`, so both variants load. Example: `pl45.pln` = "Miss5solver", cost 60 100 000.

### 2.5 `register.res` (32 bytes, binary)

A leftover registration stub. Its loader is commented out in the shipped code (`Takeoff.cpp`).
**Not used.** **[H]**

---

## 3. `gli/*.pol`: clan sprite pools (`HLine.cpp: CHLPool::Load`) **[M]**

These are "horizontal-line" compressed sprite pools for pedestrians. The header is seven i32 values:
`PoolSize, PoolMaxSize, BytesReal, BytesCompressed, BytesAdministration, LinesInPool, LinesRepeated`.
Then an object buffer (each object: 64-bit graphic id, size, line-gene buffer, per-line entry
counts), then `PoolSize` bytes of pixel pool, then a 256-entry × u16 palette (RGB555-like) that is
remapped to the display format. A pool can reference up to two **base pools**: gimmick pools
`clang%d`/`clanw%d` reuse lines from `clan%d`, and pools of consecutive clans chain to clan+1. There
is also `Skelett.pol`. Decoding the gene format belongs to the graphics spec. **Open question:** the
exact `CHLGene` byte layout (it is serialised as a raw struct).

---

## 4. `misc/*.res`: text resources (`TeakLibW/TextRes.cpp`) **[H]**

### 4.1 Syntax

Text, RLE-wrapped, CRLF. Processing per line:
1. `//` starts a comment that runs to the end of the line. Then trailing spaces are removed.
2. `>X…` (one `>`) starts a **group**. If the token after `>` is **exactly 4 characters**, the group
   key is those 4 ASCII bytes read as a little-endian u32 (`>City` gives the integer value of
   `'C','i','t','y'`). Otherwise the key is the integer value of the token (`>00001000` gives 1000).
   Code asks for groups by these 4-character tokens.
3. `>>N` starts an **entry** with id N inside the current group. The same 4-character rule applies,
   but every shipped id is 7–8 digits, so all ids are numeric. A trailing `// comment` (often the
   German city name) is ignored.
4. A line that starts with **two spaces** and has more characters is a text line. The first such
   line becomes the entry's text. Each further line is **appended**. The SDL port appends with no
   separator. The space-trimming in step 5 suggests the original joined with a single space.
   **Open question:** check against the original binary. Joining with a single space is the safer
   choice.
5. **Language segments.** If the joined text starts with `X::` (X = a language letter), it holds
   several languages, written `B::…E::…F::…D::…`. On lookup the game picks the segment for the
   current language letter (index from `sabbel.dat` into `DEFTPNISOBJKLMNQRTUV`). If that is
   missing, it falls back to English (`E`), then German (`D`). The segment runs until the next `Y::`
   marker, and one trailing space is trimmed. Text without a leading marker is used as it is.
6. Lookup is a linear search on (group, id). A missing entry is a fatal error. Strings over 1023
   bytes are rejected by the fixed-buffer getter.

Inline markup in dialogue text (handled by the dialogue system, not the resource loader):
`[[P1\BA\101]]`-style prefixes name the voice sample. Other control characters are covered in the
dialogue spec.

### 4.2 Contents

| File | Purpose | Groups (entries, id range) |
|---|---|---|
| `std_ger.res` | Standard UI text. The "ger" name is historical: every file holds all languages via §4.1 step 5. | `Date` (16: ordinals, week/month names), `NewG` (72: new-game screens), `Schd` (75: schedule), `Plne` (28: plane info), `Rout` (32: route info labels, 900–1300), `City` (84: **city descriptions**, ids = `TextRes` from city.csv, 2000–2999), `Auft` (31: contract labels, 900–2502), `XPrt` (35: expert advisors), `Kont` (7: account), `Akti` (26: shares), `Tank` (16: fuel), `Mony` (71: money/ledger lines), `Item` (28: inventory), `Filo` (32: filofax), `Jobs` (52: personnel), `Tool` (170: tooltips), `Advi` (165: advisor messages, including contract advisor 7000–7113 and route advisor 8000–8020), `Tuto` (64), `Lett` (93: letters, including penalty letters 1000–1017 and route warnings 3000–3102), `Cred` (97: credits), `Misc` (312: confirmations; for example 3014 drop-route, 3017/3018 drop-order, 3090 manual check, 2600–2612 auction results), `STAT` (51: statistics) |
| `dlg_ger.res` | Dialogue lines for characters and rooms | `Bank`, `Makl` (broker), `Muse` (museum), `Arab` (fuel), `Sabo` (saboteur), `Boss` (airport manager), `Jobs`, `Mech`, `Werb` (ads), `Duty`, `Welt` (**branch phone calls**, ids 1000–5000, see contracts-and-routes.md §2.6), `Kios`, `Nasa`, `Play` (player-to-player), `Rick`, `Frac` (cargo), `Secu`, `Desi` (designer) |
| `ein_ger.res` | **Unit formats**, one group `00001000`. Entries 1000–1013 are printf-style format strings with `%s` for the formatted number: km, km/h, m, kN, l, l/h, currency, %, age, ": currency", kg, t, Mio-currency, "Mio" suffix. Entries 2000–2013 are the matching **conversion factors** as decimal text: 1.0 for all, except 0.000001 for the two "Mio" formats. A comment notes that miles would use 0.626. (`Misc.cpp: InitEinheiten`) |
| `register.res` | Binary, unused (§2.5) |

Currency display: the "currency" format switches to the "Mio" format automatically for values ≥ 1e9
(`CEinheit::bString64`). Numbers get thousands separators before they are inserted.

---

## 5. Relations between the tables

```
city.csv ──Name──► routen.csv (Von, Nach by name)
   │  Id+0x1000000 = city id used everywhere (contracts, branches, flights)
   │  TextRes ──► std_ger.res group "City"
   │  Wave ──► VOICE samples; PhotoName ──► picture library graphics
planetyp.csv Id+0x10000000 ──► plane type of each plane
brick.csv Id+0x10000000 ◄── misc/<Segment>NNN.dat BrickId
clan.csv row triple ──► gli/clanN.pol, clangN.pol, clanwN.pol
builds.csv / relation.csv ──override──► compiled designer part tables ──► *.pln part lists
Names.csv / piloten.csv ──► staff pool and random names
stdpaper.csv ──► daily newspapers (tokens use city names and airline names)
```

## 6. Open questions

1. Joining of multi-line `.res` texts (empty vs single space), §4.1 step 4.
2. The `CHLGene` layout in `.pol` pools (graphics spec).
3. Whether any modded `city.csv` with decimal *dot* coordinates (`52.2`) is expected to keep
   fractions. The original drops them. Our parser should match unless we choose to diverge.
