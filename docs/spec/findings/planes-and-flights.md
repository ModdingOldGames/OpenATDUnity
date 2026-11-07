# Planes and Flight Operations — Behaviour Specification

Clean-room behaviour spec for OpenAT. Describes *what* Airline Tycoon Deluxe (ATD) does with
planes, flight plans and flight execution. Written from reading the original source (GOG
release) and the community SDL port. No source code is reproduced; formulas are given in
short notation, constants and number tables are facts.

- Primary reference: SDL port (`ref_AirlineTycoon/src`). Where the GOG original behaves
  differently it is called out in §19.
- Traceability: `File.cpp: Function`. Confidence tags: **[H]** high (read directly, unambiguous),
  **[M]** medium (read, but depends on context not fully traced), **[L]** low / inferred.
- Integer arithmetic: unless stated otherwise all formulas are evaluated with 32-bit signed
  integers, left to right, truncating division (C semantics: toward zero). Where the order of
  operations changes the result this document writes the expression in evaluation order.
- In-game texts are referenced as `TOKEN_xxx id` (StandardTexte resource group / id). Never copy them.

Cross-references to other findings (written separately): economy / kerosene market, routes &
route auctions, contracts (Auftrag/Fracht generation), staff/workers, sabotage, AI.

---

## 1. Time model relevant to flights

[H] `Sim.cpp: SIM::DoTimeStep, SIM::GetHour, SIM::GetMinute`

- Sim time `Time` runs 0 .. 24·60000 ticks per day. `hour = Time / 60000`,
  `minute = (Time / 1000) % 60`. One game minute = 1000 ticks.
- `Date` = day counter (0 = first day). Day rollover at `Time >= 24·60000` calls the day-change
  (`SIM::NewDay`), which increments `Date` and resets `Time` to 0.
- Flight plans use whole hours (`0..23`) plus a day index (`Date` value).
- Flights operate 24 h a day (also at night when the player has ended the working day).

Per-tick event ordering inside `DoTimeStep` that matters for planes (each block fires once
when the given minute threshold is crossed):

| When | What (details in §11) |
|---|---|
| hour rollover (minute wraps) | strike start/stop; *breakdown trigger* for the local human player (hours 6–16) |
| hour rollover | sabotage execution |
| every 4th game minute (`minute % 4 == 0`) | in-flight incident rolls, globe position interpolation |
| hour rollover | stock prices; per plane: `Problem--`, `PseudoProblem--`; recompute route passenger counts; *delay checks* for flights starting next hour |
| crossing hh:15 | landings for flights with `Landezeit == hour` |
| crossing hh:30 | departures for flights with `Startzeit == hour` |
| 17:00 (autosave) | unrelated |

At day change (`SIM::NewDay` → `PLAYER::NewDay`): contract penalties, flight-plan rollover,
maintenance/repair, used-plane market refresh, route demand update (§10.7, §7, §4.3).

---

## 2. Plane types (`planetyp.csv`)

### 2.1 File and parsing

[H] `Planetyp.cpp: CPlaneTypes::ReInit`

- File `data/planetyp.csv` (xtRLE-compressed, see task notes). Text, Latin-1, one header line
  then one row per type. Field separators: `;` (and byte 0x08). Empty fields are **skipped**
  (tokenizer semantics: consecutive separators collapse) — fields must never be empty.
- Integer fields are parsed with "leading integer" semantics (`atoi`): text after the number is
  ignored; non-numeric (`x`) → 0. The float field is parsed with "leading float, `.` decimal"
  semantics (`atof`, C locale): a comma terminates the number (`"1,1"` → 1.0), Excel-mangled
  dates like `"01. Jun"` → 1.0, `".8"` → 0.8.
- Type ID = first column + `0x10000000`. Max 80 types. Duplicate IDs are a fatal error.

Columns in order (names = meaning; units from source comments):

| # | Field (internal) | Meaning |
|---|---|---|
| 0 | Id | type number (100..126 in shipped data) |
| 1 | Hersteller | manufacturer name (display) |
| 2 | Name | model name (display) |
| 3 | NotizblockPhoto | 64-bit picture id for the notepad (UI) |
| 4 | AnzPhotos | number of photo pages in the planner plane info (UI) |
| 5 | FirstMissions | availability: mission index from which the type exists |
| 6 | FirstDay | availability: day within that mission |
| 7 | Erstbaujahr | first year of construction |
| 8 | Spannweite | wing span m (display only) |
| 9 | Laenge | length m (display only) |
| 10 | Hoehe | height m (display only) |
| 11 | Startgewicht | max take-off weight kg (display only) |
| 12 | Passagiere | seat units (a first-class passenger uses 2 units) |
| 13 | Geschwindigkeit | cruise speed km/h |
| 14 | Reichweite | range km |
| 15 | Triebwerke | engine description (display) |
| 16 | Schub | thrust lb (display) |
| 17 | AnzPiloten | pilots required |
| 18 | AnzBegleiter | flight attendants required (base value) |
| 19 | Tankgroesse | tank litres (display only; not used by the sim) |
| 20 | Verbrauch | fuel burn l/h |
| 21 | Preis | new price (DM) |
| 22 | Wartungsfaktor | maintenance factor (float) |
| 23 | Kommentar | free text (display) |

### 2.2 Availability rule

[H] `Planetyp.cpp: CPlaneTypes::GetRandomExistingType`, `DataTabl.cpp` (broker list)

A type is available when
`FirstMissions < Difficulty` OR
`(FirstMissions == Difficulty OR Difficulty == FREEGAME(-1)) AND FirstDay <= Date`.

Consequences: in add-on (11–20) and ATFS (41–50) missions every type is available from day 0;
type 124 (FirstMissions 10) never appears in classic missions 0–5 nor in free-game-map (7),
but does in free game (-1) and add-on/ATFS. The same rule drives the broker list and the
random used-plane generator. Advisor hints when a type becomes available today / in 3 days:
`Dialog.cpp` around the advisor talk (UI only).

### 2.3 Shipped data, gameplay-relevant columns (effective parsed values)

Extracted from GOG install `data/planetyp.csv`. "Maint. factor" shows raw text → value the game
actually uses after parsing (most values collapse to 1.0; only `.8` and `2,5`→2 differ). [H]

| Id | Maker | Model | FirstMission | FirstDay | FirstYear | Seats | km/h | Range km | Pilots | Attendants | Burn l/h | Price DM | Maint. factor |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 100 | Boeing | 737-400 | 0 | 0 | 1988 | 168 | 815 | 3870 | 2 | 3 | 3050 | 41000000 | 1 |
| 101 | Boeing | 737-800 | 0 | 10 | 1998 | 189 | 815 | 5420 | 2 | 3 | 3050 | 48000000 | 1 |
| 102 | Boeing | 747-100 | 1 | 0 | 1971 | 370 | 917 | 7163 | 3 | 5 | 13862 | 30000000 | 1 |
| 103 | Boeing | 747-400 Domestic | 3 | 20 | 1989 | 568 | 927 | 3185 | 2 | 6 | 12788 | 84000000 | 0.8 |
| 104 | Boeing | 747-400 | 3 | 0 | 1989 | 420 | 927 | 13490 | 3 | 5 | 12788 | 92000000 | 1 |
| 105 | Boeing | 757-300 | 4 | 0 | 1998 | 240 | 890 | 6455 | 2 | 5 | 4194 | 69000000 | 1 |
| 106 | Boeing | 777-300 | 4 | 10 | 1998 | 550 | 911 | 10370 | 2 | 6 | 8290 | 56000000 | 0.8 |
| 107 | McDonnell Douglas | DC 10 | 1 | 0 | 1976 | 380 | 965 | 9252 | 3 | 5 | 9376 | 29000000 | 1 |
| 108 | Airbus Industrie | A 320 | 3 | 25 | 1988 | 149 | 853 | 3717 | 2 | 3 | 3025 | 12000000 | 1 |
| 110 | Airbus Industrie | A 310 | 3 | 15 | 1982 | 250 | 900 | 7350 | 2 | 4 | 2850 | 22500000 | 1 |
| 111 | Airbus Industrie | A 300 | 3 | 0 | 1973 | 375 | 875 | 8000 | 2 | 5 | 2983 | 28125000 | 1 |
| 112 | Ilyushin | Il 62 | 1 | 0 | 1969 | 198 | 880 | 9200 | 3 | 3 | 10500 | 9900000 | 1 |
| 113 | Ilyushin | Il 86 | 1 | 0 | 1980 | 380 | 950 | 4950 | 2 | 5 | 9800 | 17100000 | 1 |
| 114 | Tupolev | Tu 154 B | 0 | 0 | 1972 | 180 | 900 | 3000 | 2 | 3 | 9600 | 10440000 | 1 |
| 115 | Lockheed | L-1011 Tristar 500 | 1 | 0 | 1980 | 300 | 800 | 8500 | 3 | 4 | 9100 | 30000000 | 1 |
| 116 | BAC Aerospatiale | Concorde | 4 | 3 | 1976 | 100 | 2150 | 6230 | 3 | 2 | 25629 | 30000000 | 1 |
| 117 | Boeing | 707-320C | 0 | 0 | 1961 | 189 | 975 | 5840 | 3 | 4 | 15345 | 14175000 | 1 |
| 118 | Boeing | 720 | 0 | 0 | 1963 | 165 | 965 | 6835 | 3 | 3 | 11000 | 14025000 | 1 |
| 119 | Grumman | Gulfstream II | 0 | 0 | 1966 | 15 | 950 | 6000 | 2 | 1 | 1800 | 10000000 | 1 |
| 120 | Boeing | 767-300 ER | 0 | 0 | 1988 | 290 | 1012 | 9940 | 2 | 4 | 2800 | 25000000 | 1 |
| 121 | McDonnell Douglas | MD 81 | 0 | 0 | 1978 | 172 | 950 | 2900 | 2 | 2 | 3000 | 20000000 | 1 |
| 122 | McDonnell Douglas | DC 8 Super 70 | 0 | 0 | 1980 | 259 | 932 | 12600 | 3 | 5 | 14000 | 80000000 | 1 |
| 123 | de Havilland Canada | DHC 8 Dash 8 | 0 | 0 | 1983 | 40 | 555 | 2150 | 2 | 1 | 1800 | 14000000 | 1 |
| 124 | (fictional) | Space Voyager 2000 | 10 | 0 | 1998 | 90 | 5000 | 15000 | 4 | 4 | 125000 | 200000000 | 2 |
| 125 | Boeing | 727-200 | 0 | 0 | 1964 | 189 | 1000 | 4700 | 3 | 3 | 15000 | 26000000 | 1 |
| 126 | Lockheed | C5A Galaxy | 0 | 0 | 1968 | 345 | 919 | 6000 | 3 | 5 | 14000 | 25000000 | 1 |

(No row 109.) OpenAT should parse the data file at runtime rather than hard-code this table, but
must reproduce the parse quirks to match original numbers. Recommendation: offer a "faithful"
parse (quirks) and keep the raw strings so a "corrected" mode can be added later.

---

## 3. Individual plane state (`CPlane`)

[H] `class.h: CPlane`, `Planetyp.cpp: CPlane::CPlane`, serialization operators.

Each player owns an album (ID-addressed list) of planes, sorted by name after purchases.

### 3.1 Identity and type data

| Field | Meaning |
|---|---|
| Name | unique plane name (≤ 20 chars on rename, `Stdraum.cpp` MENU_RENAMEPLANE) |
| TypeId | plane type id, or -1 for a designer-built plane |
| pt* copies | per-plane copy of type data taken at creation: maker, name, first year, range, speed, seat units, pilots, attendants, tank, burn, price, maintenance factor, comment, **noise** (`ptLaerm`, 0 for catalogue types). **All game logic uses these copies**, never the type table, so designer planes and catalogue planes are handled uniformly. |
| XPlane | the designer blueprint (parts list) when TypeId == -1 |
| Baujahr | build year |
| Sponsored | starting fleet flag; resale value /10 |

### 3.2 Location / animation state

`Ort` encodes where the plane is [H]:

| Ort | Meaning |
|---|---|
| ≥ 0 | on the ground at city (city index) |
| -1 | landing roll at the home airport (animation) |
| -2 | taking off at the home airport (animation) |
| -3 | taxiing "behind the windows" to/at a gate at the home airport (boarding/deboarding) |
| -5 | airborne |

Other position fields are visual: `Position` (lon/lat on globe, interpolated in flight),
`GlobePos`, `BehindGlobe`, `GlobeAngle` (0 on ground, 1..8 heading octant in the air),
`AirportPos`, `TargetX` (gate x target), `Startzeit` (byte copy of the departing flight's start
hour while at gate; 255 = "arrived, unloading").

### 3.3 Condition

| Field | Meaning |
|---|---|
| Zustand | condition 0..100 % (byte) |
| TargetZustand | repair target set by player (0..100, default = initial condition) |
| WorstZustand | "worst condition watermark" used for repair pricing (§7.3) |
| Problem | hours of remaining technical breakdown (0 = none) |
| PseudoProblem | hours the plane is held (sabotage "impound") |
| NumPannen, Pannen[≤10] | incident counter and last 10 incidents (date, time, code), newest first |

### 3.4 Cabin & equipment

Levels 0..2, each with a current value and a `...Target` value (§6):
`Sitze` seats, `Essen` food, `Tabletts` trays, `Deco` decoration,
Deluxe additions: `Triebwerk` engines, `Reifen` tyres, `Elektronik` electronics, `Sicherheit` safety.
`MaxPassagiere` / `MaxPassagiereFC`: current economy / first class seat counts (FC uses 2 units).
`OhneSitze`: seats removed for freight (conversion state, §12.6).

### 3.5 Crew

`AnzPiloten`, `AnzBegleiter`: crew currently assigned. `MaxBegleiter`: desired attendant count
(between ptAnzBegleiter and 2·ptAnzBegleiter). `PersonalQuality`: average talent of assigned
crew (60 if none). See §8.

### 3.6 Statistics

`Salden[7]` daily balance of the last 7 days (index 0 = today), `Auslastung`/`AuslastungFC`
(running average load %), `Kilometer`, `SummePassagiere`, `Wartungskosten` (cumulative maintenance).

### 3.7 Defaults at creation

[H] `CPlane::CPlane(Name, TypeId, Zustand, Baujahr)`:
Zustand = Target = Worst = given; Ort = home airport; plan start city = home; all equipment 0;
crew 0; no problems; not sponsored; seats: `MaxPassagiere = Passagiere·6/8`,
`MaxPassagiereFC = Passagiere·1/8` (integer); `MaxBegleiter = type attendants`; balances 0.

Save-format note: Deluxe fields (Triebwerk…Sicherheit, OhneSitze, PersonalQuality) only exist
from save sub-version ≥ 100; older saves load them as 0.

---

## 4. Acquiring and disposing of planes

### 4.1 New planes from the broker (Makler)

[H] `Dialog.cpp` TALKER_MAKLER, `Player.cpp: PLAYER::BuyPlane(type)`

- Player picks a type in the broker's catalogue menu (available types, §2.2), then a quantity
  from {1, 2, 3, 5, 10}. Only quantities with `Money − Price·n ≥ DEBT_LIMIT (−1,000,000)` are
  offered; if even one is unaffordable the broker refuses (`TOKEN_MAKLER 6000`).
- Each plane: price = type `Preis`; Zustand 100; `Baujahr = 2002 + Date/365`; random unused name
  (seeded `TEAKRAND(Date)`, shared across the batch); located at the home airport, **delivered
  immediately** (no delivery delay in code, despite a dialog line about delivery).
- `MaxBegleiter = type attendants × fleet_avg_ratio`, clamped to [0, 2×type attendants], where
  `fleet_avg_ratio = Σ MaxBegleiter / Σ min(AnzBegleiter, ptAnzBegleiter)` over current fleet
  (1.0 if the denominator is 0). [M]
- After purchase: security-advisor rebate (§4.6), crew re-mapping (§8), staff advice.
- The broker also answers "sell?" by referring the player to the museum: **selling is only
  possible at the museum**.

### 4.2 Starting fleet

[H] `Sim.cpp: SIM::ChooseStartup`

Every player starts with two planes, bought before the start money is assigned (so they are
free): type 100 plus 114 (mission EASY), 104 (mission NORMAL), or 105 (all others). They are
`Sponsored` (resale /10). Add-on mission 1 sets their condition (all three values) to 60,
add-on mission 7 to 35.

### 4.3 Used planes (Museum)

[H] `Sim.cpp: SIM::CreateRandomUsedPlanes / CreateRandomUsedPlane / UpdateUsedPlanes`,
`Dialog.cpp` TALKER_MUSEUM, `Museum.cpp`

- 3 offer slots, regenerated every day change. Slot *i* uses `TEAKRAND(Date + i)`:
  name = unused name; type = random available type; then
  - if type first year < 1990: `Baujahr = 1990 − Rand(1990 − FirstYear)`,
    else `Baujahr = 1996 − Rand(1996 − FirstYear)`
    (`Rand(n)` returns `r % n`, i.e. 0..n−1; for FirstYear 1998 the argument is −2 and the
    result is 0 or 1 → 1995/1996; FirstYear 1996 would divide by zero — not present in data).
  - `Zustand = (Baujahr − 1950) + 25 + Rand(40) − 20`, then if < 20 (or byte-wrapped > 200) → 20,
    if > 100 → 100. TargetZustand = Zustand.
  - (A throw-away `Rand(80)+11` condition is drawn in the constructor call; argument evaluation
    order affects which draw is which — see Open Questions.)
- Price = market value (§4.5) of that plane. Buy requires `Money − price ≥ DEBT_LIMIT`.
  On purchase: `WorstZustand = Zustand − 20`; the plane is copied into the fleet (keeps its
  equipment 0, its seat split, etc.); the slot is emptied; refill tick counter reset.
- Refill of an emptied slot: a counter increments every 5 game minutes (reset to 6 at day start,
  0 on purchase); on entering the museum, `n = min(3, counter/20)` empty slots are regenerated
  (i.e. ≈ 100 game minutes after the last purchase). Regenerated slots reuse the
  `Date + i` seed → the same plane as in the morning reappears.
- ATFS10 (days 40–50): museum is empty.
- Museum closed on weekdays 5 and 6 (handy call refuses too, `Stdraum.cpp`).

### 4.4 Selling

[H] `Stdraum.cpp` MENU_SELLPLANE, `Dialog.cpp` TALKER_MUSEUM 720, `Planetyp.cpp: CPlane::CanBeSold`

- A plane can be sold only if no route, contract or freight flight in its plan lands after
  `now − 2 h` (i.e. `Landedate > Date` or `Landedate == Date and Landezeit > hour − 2`).
  Automatic transfer flights do not block. Not checked: whether the plane is currently airborne
  on an automatic flight.
- Sale price = `market_value × 9 / 10`. The plane is removed; crew re-mapped.
  Sponsored planes get a different dialog line but are sold the same way (their market value
  already includes /10).

### 4.5 Market value

[H] `CPlane::CalculatePrice`

`value = ptPreis × Z / 10000 × Z × (Baujahr − 1900) / 120` (64-bit intermediate, left to
right), then `/10` if Sponsored.
Example: new plane (Z 100, built 2002) = 85 % of list price.

### 4.6 Security-advisor rebate (all purchases)

[H] `PLAYER::DoBodyguardRabatt`: if the player employs a security advisor of quality q > 20,
the player is refunded `price/100 × (q/10)` DM (i.e. q/10 percent of the price; reason 3130).
Applies to broker, museum, designer purchases and kerosene purchases.

### 4.7 Leasing

[H] No leasing exists in ATD. (Gates and routes are rented, planes are not.)

### 4.8 Plane names

[H] `Planetyp.cpp: CPlaneNames`
`data/pnames.csv`: column 1 = name, column 2 = tier (1 = first tier, else second tier). Entry 0
of each tier is never used. Unused-name search: up to N random tier-1 picks, then sequential
tier-1, then random tier-2, then sequential tier-2, else a hardcoded fallback name. Uniqueness
is checked across all players' fleets.

---

## 5. Plane designer (Deluxe: Designer + Editor rooms)

[H] `Editor.cpp: CXPlane::*`, `Editor.h`, `Dialog.cpp` TALKER_DESIGNER, `Designer.cpp`

### 5.1 Flow

- Designer room: talk to the designer → buy a built design, or enter the Editor room.
- Editor: compose a plane from parts; the design is saved as a `.plane` file in the player's
  plane directory (`data.plane` is the work-in-progress). Saved designs are shared files, not
  game state.
- Buying: designer lists all *buildable* designs; quantities {1,2,3,5,10} as with the broker,
  same debt-limit rule; each plane is created via `BuyPlane(design)` (§5.5).

### 5.2 Part catalogue (`data/builds.csv`, overrides compiled-in defaults)

The game ships hard-coded defaults but **always imports** `data/builds.csv` and
`data/relation.csv` when the editor opens (import overrides cost/weight/power/noise/maint/
pax/burn for parts and offsets/notes/noise for relations). Shipped data:

Categories by first letter: B body, C cockpit, H tail, R right wing, L left wing, M engine.

| Part | Cost | Weight | Power | Noise | Maint | Pax | Burn |
|---|---|---|---|---|---|---|---|
| B1 | 10000000 | 30000 | 0 | 0 | -15 | 95 | 0 |
| B2 | 40000000 | 90000 | -10725 | -5 | 0 | 600 | 0 |
| B3 | 15000000 | 45000 | -5100 | -40 | -15 | 190 | 0 |
| B4 | 25000000 | 70000 | -6125 | -20 | -15 | 355 | 0 |
| B5 | 20000000 | 60000 | -5125 | -10 | -15 | 280 | 500 |
| C1 | 100000 | 2000 | 200 | 5 | 10 | 0 | 0 |
| C2 | 1100000 | 2500 | 320 | 2 | 5 | 0 | 0 |
| C3 | 1200000 | 4000 | 100 | 0 | 0 | 0 | -150 |
| C4 | 200000 | 500 | 0 | 5 | 20 | 0 | 0 |
| C5 | 4000000 | 5000 | 0 | -15 | 0 | 0 | -250 |
| H1 | 1500000 | 6500 | 0 | 10 | 0 | 0 | 0 |
| H2 | 800000 | 4500 | 0 | 0 | 0 | 0 | 300 |
| H3 | 1200000 | 2500 | 0 | 0 | 0 | 0 | 0 |
| H4 | 4500000 | 3000 | 0 | 0 | 0 | 0 | -200 |
| H5 | 2000000 | 4000 | -2000 | 20 | -20 | 0 | 0 |
| H6 | 6000000 | 8000 | 0 | -20 | 10 | 0 | -400 |
| H7 | 5000000 | 3500 | 0 | -10 | 0 | 0 | 0 |
| R1 / L1 | 6000000 | 5200 | -5760 | -10 | 0 | 0 | -200 |
| R2 / L2 | 3000000 | 4600 | -5012 | 0 | 0 | 0 | 0 |
| R3 / L3 | 1700000 | 3800 | 0 | 2 | 0 | 0 | 0 |
| R4 / L4 | 1200000 | 2500 | 0 | 5 | 0 (L4: 10) | 0 | 0 |
| R5 / L5 | 500000 | 1500 | 0 | 0 | 0 (L5: 10) | 0 | 0 |
| R6 / L6 | 2000000 | 8750 | 0 | 0 | 0 | 0 | 500 |
| M1 | 2300000 | 1000 | 7500 | 3 | 0 | 0 | 1100 |
| M2 | 100000 | 300 | 5000 | 20 | 10 | 0 | 200 |
| M3 | 250000 | 500 | 6000 | 20 | 10 | 0 | 450 |
| M4 | 4300000 | 1400 | 9000 | 3 | 0 | 0 | 1500 |
| M5 | 2400000 | 800 | 5500 | 0 | 0 | 0 | 1250 |
| M6 | 4500000 | 1800 | 13500 | 14 | 5 | 0 | 2900 |
| M7 | 7000000 | 2300 | 20000 | 20 | 10 | 0 | 2650 |
| M8 | 3000000 | 3100 | 15000 | 15 | 12 | 0 | 3400 |

(Compiled-in defaults differ noticeably, e.g. B1 weight 40000 / pax 140; the CSV values are what
the shipped game uses.) Ids in the CSV are not unique (several 3003/4004/5003/6004); rows are
matched by position.

### 5.3 Attachment graph ("relations")

Each placed part records the relation through which it was attached. A relation =
(Id, parent part or "desktop", child part, 2-D offset for airport view, 3-D offset for editor,
three *notes*, extra noise, the *slot* it needs, the *slots it blocks*, z-order adjustment).
`relation.csv` provides offsets, notes and noise; parent/child/slot/blocked-slots are
compiled in. Grouped summary of the compiled-in graph (Id: child):

- Desktop → body, slot `B*` (one body only): 100 B1, 101 B2, 102 B3, 103 B4, 104 B5.
- Body Bn → cockpit, slot `C0`: ids 200+10·(n−1) + {0:C1, 1:C2, 2:C3, 3:C4, 4:C5}.
- Body Bn → tail, slot `H0`: ids 300+10·(n−1) + {0:H1 … 6:H7}.
- Body → wings (slots `Rx` / `Lx`, mirrored ids differ by +200):
  - B1: 400 R4, 401 R5 (block Rx,R1,R2,R3); 600 L4, 601 L5.
  - B2 (four mounting positions a–d with different blocked slots): 411/413 R1, 420/421/422/423 R2,
    424 R3, 430/431/432/433 R4, 444 R6; left mirrors 611…644.
  - B3: 450 R1, 451 R2, 452 R4, 453 R5 (+200 left).
  - B4: 460 R2, 461 R4, 462 R5 (+200 left).
  - B5: 470 R1 … 474 R5 (+200 left).
- Wing → engine. Right wing Rk uses ids `600+100k+…`(R1: 700s, R2: 800s, R3: 900s, R4: 1000s,
  R5: 1100s, R6: 1200s); the matching left-wing relation id is **right id + 10**. Engine slots on
  the right wing are `M1`,`M2`,`M3` (positions outer→inner) and `MR`; on the left `M4`,`M5`,`M6`,`ML`.
  Large engines (M6, M7, M8) block neighbouring slots. M5 can also attach under the wing
  (`MR`/`ML`) and on tails H2, H5, H7 (`Mr`/`Ml`, ids 1304–1306 / 1314–1316).

  OpenAT must ship the full 307-row graph as a data file. It is factual data; extract it once
  from the original table into `relation` data with columns Id, From, To, Slot, BlockedSlots,
  zAdd (offsets/notes/noise come from `relation.csv`).

Placement rule: a relation is usable only if its slot is not in the blocked-slot list of any
already-placed part's relation (slot codes are 2-character, case-sensitive; `Mr` ≠ `MR`).
Wings and engines are placed as mirrored pairs (UI convenience). [M]

**Notes** (codes): 100 standard (none); 101 "breaks often" **and** "high consumption"
(same code!); 102 "breaks very often" **and** "very high consumption"; 103–108 speed caps
300/400/500/600/700/800 km/h; 109–112 +1/+2/+3/+4 pilots; 113–116 +4/+6/+8/+10 attendants.

Notes in shipped data:
- Bodies: B1 +4 att.; B2 +8 +4 = 12 att.; B3 +6; B4 +6 +4 = 10; B5 +8.
- Cockpits (same on every body): C1 +3 pilots, cap 400; C2 +3, cap 500; C3 +4, cap 800;
  C4 +2, cap 300; C5 +4 +3 = 7 pilots, no cap.
- Tails: H4 cap 500 (on B2 additionally note 102); H7 cap 800.
- Engines: every M7 on a right wing: +1 pilot; M5 on the right side of a tail: +1 pilot.
  Engines on the outermost slot (`M3`/`M6`) of wings R2/L2 and R3/L3: M1/M4 note 101,
  M6/M7/M8 note 102.
- Extra relation noise (per engine): M1 on any wing +2; M2 on R1/R2 +15, on R3–R6 +20;
  M3 on R4–R6 +20; M4 on R4–R6 +6; M6 on R4–R6 +14; M7 on R4–R6 (outer slot) +20;
  M8 on R1/R2 +8, on R3–R6 +15 (only specific slot positions; see data).

### 5.4 Derived statistics

[H] `CXPlane::Calc*`. Sums run over all placed parts.

| Stat | Formula |
|---|---|
| Cost | Σ part cost |
| Seat units | Σ part pax |
| Weight | Σ part weight |
| Power | Σ part power (bodies/wings/H5 are negative) |
| Speed | `Power == 0 → 0`, else `(Power − 6000)·730/44000 + 270` (integer, left→right); then for **each** speed-cap note occurrence (in part order) with cap c: if speed > c → `speed = (2·speed + c)/3` |
| Burn l/h | Σ part burn + 2500 per note 101 + 5000 per note 102 |
| Tank l | 7 × Σ weight of wing parts (L*, R*); if Burn > 0 and Tank/Burn > 22 → Tank = 22·Burn |
| Range km | `Burn > 0 ? Tank / Burn × Speed : 0` (integer division first → whole hours × speed) |
| Pilots | max(1, Σ pilot notes) |
| Attendants | max(1, Σ attendant notes) |
| Noise | Σ part noise + Σ relation noise |
| Maint. | Σ part maint + 10 per note 101 + 20 per note 102 |

Buildable iff at least one B, C, H, R and M part are present and no error:
1. every engine on a right wing has an engine on a left wing whose relation id = right id + 10,
   and vice versa (error text `TOKEN_MISC 8300`);
2. `Power × 4 ≥ Weight` (error `TOKEN_MISC 8301`).

### 5.5 Creating a plane from a design

[H] `PLAYER::BuyPlane(CXPlane)`
TypeId = −1; Zustand 100; Baujahr `2002 + Date/365`; pt-fields from §5.4
(maker empty, first year 2002, name = design name, price = cost, noise = Noise,
maintenance factor = `(Maint + 100)/100.0`); seats 6/8 + 1/8 of seat units; MaxBegleiter as in
§4.1 using design attendants; the blueprint is stored on the plane.

---

## 6. Equipment, cabin configuration (PlanProp room)

[H] `PlanProp.cpp`, `Planetyp.cpp: CPlane::DoOneStep (default case)`, `Player.cpp: CalcPlanePropSum`

### 6.1 Levels and prices

| Item | Level 0 | Level 1 | Level 2 | Unit |
|---|---|---|---|---|
| Seats | 300 | 1200 | 3000 | per seat unit |
| Food | 10 | 20 | 50 | per economy passenger **per flight** |
| Trays | 100 | 800 | 3400 | per seat unit |
| Decoration | 500 | 1500 | 6000 | per seat unit |
| Engines | 100000 | 750000 | 3500000 | per plane |
| Tyres | 50000 | 300000 | 900000 | per plane |
| Electronics | 600000 | 1500000 | 8000000 | per plane |
| Safety | 0 | 2000000 | 6000000 | per plane |

Change cost when switching an item from level `old` to `new`:
- per-seat items: `ptPassagiere × (Cost[new] − Cost[old]/2)` (seat units of the plane type,
  independent of current FC split);
- per-plane items: `Cost[new] − Cost[old]/2`;
- food: no change cost (it is a running cost).
Negative results are refunds (downgrades recover half the old value; e.g. safety 1→0 refunds
1,000,000).

### 6.2 When changes take effect

- In the room the player edits `…Target` values. A selection is rejected (reverted, message
  `TOKEN_SCHED 1810`) if the *sum of all pending changes over the whole fleet* would exceed
  current money.
- The actual switch happens in the per-frame plane update when the plane is **on the ground at a
  city** (`Ort ≥ 0`) and the owner is not currently inside the PlanProp room (or the day has been
  ended). Cost is charged then (human players only — Owner 0 or 2); booked to "other"
  income/expense statistics; money reason 2110.
- AI planes switch without this charge (AI pays a flat 170,000 per upgrade step in its own logic).
- Pending cost / refund is shown as an advisor comment on leaving (`TOKEN_SCHED 1820/1821`).

### 6.3 Effects of equipment (where it is read)

| Item | Effects |
|---|---|
| Seats, Food, Trays, Deco | passenger satisfaction quality sum (§13); image change per flight (−1 per item at level 0, +2 per item at level 2, §12.8); FC luxury factor (§12.3) |
| Engines, Tyres | FC luxury factor only; mission checks (add-on "modernise") |
| Electronics | FC luxury factor; level 2 protects from seasonal incidents in spring, autumn, winter (§7.6) |
| Safety | FC luxury factor; level 2 protects from seasonal incidents in summer |

Engine level does **not** change speed or fuel burn. [H]

### 6.4 First-class split

Seat units `U = MaxPassagiere + 2·MaxPassagiereFC` (initially = type seats).
FC share in % of units `p = 2·FC·100/U`, displayed rounded to 10 %. The +/- buttons step p by
±10 % (after rounding to the nearest 10): `FC' = U·p'/2/100`, `Eco' = U − 2·FC'`; if Eco' equals
the old Eco value, FC is forced by ±1 (Eco ∓2). The change is refused if any count would be
negative or if `Eco' + FC'` < largest passenger/freight need of any not-yet-landed contract or
freight flight in the plan (freight need = tons × 10). No cost, immediate effect.

### 6.5 Attendants

The player sets `MaxBegleiter` between type attendants and 2× type attendants (+/- by 1). Extra
attendants above the type value raise the satisfaction quality sum and image (§13, §12.8).

### 6.6 Copy / paste configuration

"Copy" stores the current plane's 8 target levels and attendant ratio as a template; "paste"
applies **only the four technical items** (engines, tyres, electronics, safety) to the current
plane (cabin items are backed up but not overwritten), with the same money check. [H — literal
behaviour, likely an original oversight]

---

## 7. Condition, wear, maintenance and incidents

### 7.1 Wear per flight

[H] `Schedule.cpp: CFlugplanEintrag::BookFlight` (end)

At every departure (all flight types):
`wear = (1 + distance_m·10/40040174) × (2015 − Baujahr) × BKF / 15`
(`distance_m·10/40040174` = number of full 4004-km units; BKF = bad-kerosene factor, 1 or 3,
§16). `Zustand = Zustand − wear` as an unsigned byte; if it wrapped (> 200) → 0.

Consequences: a plane built 2002 has 0 wear on flights < 4004 km with good fuel; old planes
(e.g. 1970: factor 45) lose 3 points per short flight. If `Baujahr > 2015` wear becomes
negative (condition increases) — not reachable normally.

### 7.2 Mechanic (Werkstatt) modes

[H] `Player.cpp: PLAYER::NewDay` ("Flugzeuge warten"), `Global.cpp gRepairPrice`,
`Dialog.cpp` TALKER_MECHANIKER

Player-wide `MechMode` (default 2):

| Mode | Monthly fee per plane | Daily fee per plane (fee/30) | Daily condition change (before target cap) |
|---|---|---|---|
| 0 cleaning crew | 1200 | 40 | if (plane index + Zustand + Date) is even: −2 |
| 1 apprentice | 18000 | 600 | if (index + Zustand + Date) % 8 == 0: −2, else +5 |
| 2 mechanic | 48000 | 1600 | Z < 60: +2..+6; else +2..+9 (`TEAKRAND(Date)` shared per player) |
| 3 engineer | 150000 | 5000 | Z < 60: +18; else +15 |

All increases cap at 100 and then at `TargetZustand`. Mode 0 can only be chosen once the
mechanic fully trusts the player (trust level 2; bribe with beer → trust ≥1; a dialog option then
raises 1→2). [M] Mechanic also offers menus: per-plane repair targets (0..100 in steps of 5, or
"all planes to X"), cost overview, incident list.

### 7.3 Daily maintenance processing (order)

For each plane (album order, last to first), after the flight-plan rollover (§10.7):

Only if `Zustand < TargetZustand + 2` (i.e. target not set clearly below current):
1. Today's balance gets `−fee/30`, then the 7-day balance history shifts (new day slot 0 = 0).
   (Planes whose target is ≥2 below current skip this — their balance history does not shift.)
2. `OldZ = Z`; if `Z < WorstZustand` → `WorstZustand = Z`.
3. Apply mode change; cap at 100 and target.
4. If `Z > Worst + 20`: `Improvement = Z − (Worst + 20)`; `Worst = (Z < 20 ? 0 : Z − 20)`.
5. Plane maintenance statistic += fee/30.
6. If `Z > OldZ`:
   `cost += Improvement × ptPreis / 110` (32-bit, can overflow for expensive planes),
   `cost += (Z − OldZ)·10·ptWartungsfaktor·(2100 − Baujahr)/100·(200 − Z)/100` (float, truncated),
   `Worst = max(Worst, Z − 20)`; both added to the plane maintenance statistic.

After all planes: player pays `Σcost + fee/30 × number_of_planes` (fee for **all** planes,
including skipped ones), reason 3110, statistic STAT_WARTUNG.

Interpretation: repairing within 20 points of the plane's worst recent condition costs almost
nothing beyond the flat fee; every point above that band costs ≈ 0.9 % of the plane price.
The repair menu shows this "improvement" cost estimate per plane using the target value.

### 7.4 Grounding and delays caused by condition

[H] `Sim.cpp: DoTimeStep` delay block — `Zustand < 15` delays departures hour by hour (§11.2).

### 7.5 Breakdowns ("Problem")

[H] `Sim.cpp: DoTimeStep` ("Probleme bei einem Flugzeug auslösen")

- Checked at each hour rollover between 06:00 and 16:59, not after "end of day", **only for the
  local human player**, only if none of his planes currently has a problem and he owns ≥ 2 planes.
- RNG `TEAKRAND(Date + hour + minute)`; planes iterated last→first; candidates: airborne
  (`Ort −5`) and landing this hour. First candidate with
  `Rand(10) == 0 AND Rand(100) > Zustand AND Zustand < 90` gets
  `Problem = 4 + Rand(101 − Zustand)/3 + (Rand(4) == 0 ? 15 : 0)` hours; then stop.
- If the flight lands at home: `Problem = max(4, Problem − 15)`.
  Else if `Problem > 20`: a **spare-part contract** is created for the player: from home to the
  plane's destination, 0 passengers, premium 0, penalty 0, valid today until
  `Date + (Problem − (24 − hour))/24`; letter/fax shown.
- Effect: every hour `Problem` decreases by 1; while > 0 every departure of that plane is delayed
  (§11.2). When any plane lands (flight type contract with 0 passengers) at the city where a
  problem plane stands, that plane's `Problem = max(1, Problem − 15)` (if it was > 1).
- Faxes/letters: `TOKEN_LETTER 506/507`, advice `TOKEN_ADVICE 2308/2309`, game pauses.

### 7.6 In-flight incidents

[H] `Sim.cpp: DoTimeStep` ("Unfälle wegen schlechter Qualität / Jahreszeit")

Every 4th game minute, for every player's airborne plane whose flight started ≥ 1 h ago
(`now ≥ Startzeit + 1`): a fresh `TEAKRAND(Date + hour + minute)` (same seed for all planes in
that minute).
1. **Quality incident** — only evaluated if `Zustand < 80` (otherwise no draw is consumed):
   if `Rand(5000) > Zustand + 4920` and the flight is a route/contract flight with > 0 passengers:
   player image −2, route image ×8/10, incident code `Rand(9)` (0..8) recorded, advice
   `TOKEN_ADVICE 2360+code`, stock price ×0.7, trusted dividend −1 if > 4.
   Probability per roll ≈ `(79 − Zustand)/5000`.
2. **Seasonal incident**: if `Rand(5000) > 4980` (19/5000) and the plane is not protected
   (spring/autumn/winter: electronics ≠ 2; summer: safety ≠ 2): pay 75,000 (reason 3501),
   incident code `10 + season`, advice `TOKEN_ADVICE 2370+season`. Any flight type.
Season by calendar day (each month treated as 30 days, `Month·30 + day`):
spring 5.3.–20.5., summer 21.5.–11.8., autumn 12.8.–19.12., winter otherwise (`SIM::GetSeason`).

There are **no crashes / plane losses** in normal play. (Mission ATFS10 removes a random plane on
day 40 and damages all planes on day 35 — mission scripting.) Sabotage effects on planes are in
the sabotage findings (impound = `PseudoProblem 15`, flat tyre = +1 h delay, etc.).

---

## 8. Crew requirements (summary; see staff findings)

[H] `Player.cpp: PLAYER::MapWorkers`, `CPlane::UpdatePersonalQuality`

- Employed pilots/attendants are auto-assigned to planes: first fill each plane up to its
  required counts (pilots = ptAnzPiloten, attendants = ptAnzBegleiter), choosing the plane with
  the smallest remaining need; spare attendants then fill up to `MaxBegleiter`; attendants above
  base may be moved to fill other planes' base needs.
- A plane departs only if `AnzPiloten ≥ ptAnzPiloten` and `AnzBegleiter ≥ ptAnzBegleiter`
  (else hourly delay, §11.2). Salaries of assigned crew are charged to the plane's daily balance
  (`salary/30`).
- `PersonalQuality` = mean talent of assigned crew (60 if none); recomputed on every mapping.

---

## 9. Flight plan representation

[H] `class.h: CFlugplan, CFlugplanEintrag`, `Schedule.cpp`

Per plane: `StartCity` (city where the plan begins, = location after the last completed flight),
fixed array of **60 entries** (10 per day × 6), `NextFlight`, `NextStart`.

Entry fields:

| Field | Meaning |
|---|---|
| ObjectType | 0 empty, 1 route, 2 passenger contract (Auftrag), 3 automatic (empty transfer), 4 freight contract |
| ObjectId | route id / contract id / freight id (album ids) |
| VonCity, NachCity | from / to (refreshed from the object for future entries) |
| Startdate, Startzeit | day and hour (0–23) of the *departure block* |
| Landedate, Landezeit | day and hour of arrival |
| Passagiere, PassagiereFC | booked economy / FC passengers (freight: tons carried) |
| PArrived | passengers already spawned at the home terminal (visual) |
| HoursBefore | lead time (h, ≤ 255) between placing the flight and its start — demand modifier |
| Ticketpreis, TicketpreisFC | prices captured for this flight (routes) |
| Gate | gate number ≥ 0; −1 = no gate free (home airport); −2 = flight does not touch home |
| GateWarning | UI warning flag (gate conflict / superfluous freight flight) |
| Okay | 0 ok; 1 wrong day for contract; 2 already done (UI); 3 passengers don't fit |

`NextFlight` = first non-empty entry with landing ≥ current hour (date-aware).
`NextStart` = first non-empty entry with `Startzeit + 1 ≥` current hour (date-aware).

Hour semantics [H]: a flight with `Startzeit S` is processed for departure at S:30, takes off
around S:55 at home (animation) and is shown on airport displays as departing at **S+1:00**.
It lands at L:15 with `L = S + duration`. The plane is free for the next flight from `L + 1`.

---

## 10. Planning rules

### 10.1 Placing / removing flights (planner UI)

[H] `Planer.cpp: CPlaner::HandleLButtonDown / HandleLButtonUp`

- The week view shows 7 day rows (today … today+6) × 24 hour columns; the day view one day.
- The player drags a "post-it" (route, contract or freight from the side lists) onto a time.
  Checks before placement:
  - distance > plane range (`ptReichweite·1000` m) → refused (`TOKEN_ADVICE 2310`);
  - flight duration (§12.1) ≥ 24 h → refused (same text);
  - contract already done (InPlan −1, `TOKEN_ADVICE 2311`) or already planned (InPlan 1, 2312);
  - list flags also gray out contracts too big for the plane type's seat units (2316) and
    routes the player cannot use (2314 / 2301).
- Start time on today is forced to ≥ `hour + 2`.
- If the target time is covered by an existing *non-automatic* future flight (start > hour+1),
  that flight is replaced and picked up onto the cursor (swap); otherwise the new flight goes into
  the last array slot (overwrites it if the plan is full — no "full" error).
- New entry: Okay 0, ticket prices copied from the player's route settings,
  `HoursBefore = (Startdate − Date)·24 + (Startzeit − hour)`, passenger count computed (§12.3),
  PArrived 0; then plan validation (§10.2), contract/freight usage update (§10.5, 10.6),
  gate planning (§10.8).
- Shift-click keeps a route on the cursor for repeated placement. Freight stays on the cursor
  while tons are still open.
- Removing: clicking a future (start > hour+1) non-automatic flight picks it up.
- Buttons: "extend" (§10.4) and "clear plan" (confirmation).

### 10.2 Plan validation and automatic flights (`CPlane::CheckFlugplaene`)

[H] `Planetyp.cpp: CPlane::CheckFlugplaene`. Run after every plan edit, delay, day rollover.

1. Sort: empty entries to the end; non-empty by (Startdate, Startzeit) (when sorting enabled).
2. For every *future* entry (start date > today, or today with `Startzeit > hour + 1`):
   automatic flights (type 3) are deleted; others get Von/Nach refreshed from their object and
   `Landezeit = (Startzeit + duration) % 24`, `Landedate = Startdate (+1 if Landezeit < Startzeit)`.
   If duration ≥ 24 the entry is deleted with advice 2310.
3. Sort again.
4. If the first entry departs from a city ≠ `StartCity`: insert an automatic flight at index 0
   from `StartCity` to that city, starting today at `hour + 2`.
5. For each consecutive pair where flight *c* lands elsewhere than flight *c+1* departs: insert an
   automatic flight from c's destination to c+1's origin, starting at c's landing hour/day
   (clamped to ≥ today `hour + 2`).
6. Push forward: each flight must start at ≥ previous `Landezeit + 1` (date-aware); otherwise its
   start is moved to exactly that and its landing recomputed. (Cascades down the plan.)
7. Horizon: if not currently dragging in the planner, delete entries starting on or after
   `Date + 7` (and an automatic flight directly preceding such an entry).
8. Automatic flights (future ones) are moved as **late as possible**: landing at next flight's
   start − 1 h (date-aware) if that is later than the current landing; start = landing − duration.
9. Re-plan gates (§10.8).

Notes: inserted automatic flights (step 5) start at the previous landing hour and are then pushed
+1 h by step 6; a tight gap can therefore push the following real flight later. The plan array
is fixed at 60 entries; insertions shift entries toward the end (the last ones fall off).

### 10.3 Lead time and horizon

- Earliest start: today `hour + 2`. Latest start: day `Date + 6` (anything later is cut).
- `HoursBefore` reduces demand for short-notice route flights (§12.3).

### 10.4 "Extend plan" button (`CPlane::ExtendFlugplaene`)

[H] Takes the block of entries from the first not-yet-started route flight to the end of the
plan as a pattern. Pattern length in hours
`R = (last landing abs-hour − first start abs-hour) + 1`, plus, if the last flight does not end
where the first starts, `duration(last.Von → first.Nach) + 1` (sic, uses those two cities).
While `last landing + R < (Date + 7)·24`: copy the pattern's *route* entries (only routes the
player still rents) shifted by R hours; non-route pattern entries leave empty slots. Then the plan
is validated without re-sorting (§10.2). Hour normalisation uses `> 24` (an hour value of 24
can survive until validation). No bound check against the 60-slot array (OpenAT: clamp).

### 10.5 Contract usage flags (`PLAYER::UpdateAuftragsUsage`)

[H] Human players only (Owner 0/2). For each contract: Okay := 1, InPlan := 0 (unless done −1).
For each contract entry in any plan:
- if (type seats ≥ persons and start ≤ contract end) or the flight is still in the future:
  first such entry sets contract InPlan := 1 and entry Okay := 0;
- if type seats < persons: contract Okay := 0, entry Okay := 3;
- if start date outside [contract Date, BisDate]: contract Okay := 0, entry Okay := 1.
All non-contract, non-freight entries get Okay 0. AI contracts are never flagged.

### 10.6 Freight tonnage allocation (`PLAYER::UpdateFrachtauftragsUsage`)

[H] Human players only. Reset per freight order: Okay 1; if not done and tons left:
InPlan 0, `TonsOpen = TonsLeft`. For each freight entry (plan order):
- start outside [Date, BisDate] → order Okay 0, InPlan 1, entry Okay 1;
- else if tons left: entry carries `ptPassagiere/10` tons (stored in Passagiere); if the flight
  has not started yet (SDL fix: start date > today, or today and start hour ≥ now) it reduces
  `TonsOpen`; once `TonsOpen ≤ 0` the entry's tonnage is reduced by the overshoot, TonsOpen 0,
  InPlan 1, entry Okay 0. An entry whose tonnage became 0 gets GateWarning (superfluous flight).

### 10.7 Daily rollover (`CPlane::FlugplaeneFortfuehren`) — routes repeat weekly

[H] At day change, before maintenance, for each plane:
1. Find the latest landing in the plan → `LastDate/LastTime = landing + 1 h`.
2. For each entry that landed before today (`Landedate < Date`):
   - contract / automatic / freight: deleted; `StartCity` := its destination;
   - route flight on a route still rented: `StartCity` := its destination; shifted **+7 days**;
     PArrived 0; `HoursBefore = 48`; economy ticket price refreshed from current route setting
     (FC price is *not* refreshed); if it now starts before `LastDate/LastTime` it is moved to
     start exactly there (landing shifted by the same amount) and LastDate/LastTime advance;
   - a route flight still in the past after this → deleted. (Non-rented routes: deleted too,
     because they stay in the past.)
3. Validate the plan (§10.2).
4. Every automatic flight whose (from, to) equals a route flight somewhere in the plan is converted
   into that route flight (type, id, ticket price copied).

So a route schedule is a **weekly repeating** pattern; contracts and freight are one-off.

### 10.8 Gate planning (`PLAYER::PlanGates`)

[H] Per player, a 7-day × 24-hour occupancy array (index = hours from today 00:00), capacity =
number of rented gates. All route/contract flights (not automatic, not freight) touching home:
- the default for future flights (start > hour+1) is Gate −1;
- departures from home occupy hours `S−1` and `S`; arrivals at home occupy hour `L−1`;
- only slots with index in 2..166 are considered (earlier/later flights keep their gate value);
- if free: future flights get the gate *number* of the k-th rented gate (k = current occupancy);
  occupancy incremented (departures set both hours to max+1);
- if full: Gate −1 (future flights), occupancy forced to capacity+1, and for human players all
  flights overlapping that hour get `GateWarning`.
Flights not touching home get Gate −2. Flights within the next ~1 h keep their gate.
AI: after contract clean-up the AI also delays flights without a gate by up to 6 hours
(`PLAYER::DelayFlightsIfNecessary`, AI only).

---

## 11. Flight execution

### 11.1 Hourly passenger refresh

At each hour rollover, for every plane and every route entry: recompute passengers (§12.3). The
recompute only changes the counts while the flight starts at least 2 h later (`hour + 1 <
Startzeit` on the same day, or a later day). Counts are therefore frozen from `S−2:00`.

### 11.2 Delays

[H] `Sim.cpp: DoTimeStep` (hour rollover, after Problem/PseudoProblem decrement)

For human players (Owner ≠ 1; **AI flights are never delayed by these rules**), for each entry
whose start is exactly the next hour (`Startdate·24 + Startzeit − 1 == now`), delay if any of:
1. `PseudoProblem > 0` (advice 2358);
2. strike active (advice 2356); else
3. crew below requirement (2350), else
4. `Zustand < 15` (2351), else
5. contract entry with Okay == 1 (wrong day) (2352), else
6. `Problem > 0` (2355).
A delay: `Startzeit + 1` (wrap to next day), statistic STAT_VERSPAETUNG +1, NextFlight/NextStart
refresh, plan validation (pushes later flights), contract usage update. The same check runs
again next hour, so a persistent cause delays repeatedly.

### 11.3 Departure (at S:30)

[H] `Sim.cpp: DoTimeStep` ("Flugzeuge zum Gate fahren lassen")
For each plane whose `NextFlight` starts today at the current hour: count a flight
(`NumFlights`), then
- **home airport, real gate (≥ 0), route or contract**: `Ort = −3`, plane appears beyond the
  right end of the terminal and taxis to its gate; boarding passengers walk to the plane; at
  S:55 (or later) it taxis out, then takes off (`Ort −2`); when the climb animation leaves the
  screen the flight is **booked** (§12) and `Ort = −5`.
- **otherwise** (abroad, no gate, automatic, freight): `Ort = −5` and the flight is booked
  immediately. If it was a home departure of a route/contract without a gate: advice 2353 and
  player image −2.
Revenue and costs are therefore realised at **departure**, not at arrival.

### 11.4 In flight

Globe position interpolates linearly (lon wrap-aware) between origin and destination from
S+1:00 to L:00 (visual). Incident rolls (§7.6).

### 11.5 Landing (at L:15)

[H] For each plane whose `NextFlight` lands today at the current hour:
- If it is a contract with 0 passengers (spare-part contract): reduce Problem of planes standing
  at the destination (§7.5).
- **Destination = home**: `Ort = −1` (landing roll, planes staggered). At the end of the roll:
  - automatic, freight, or not touching home: `Ort = destination` directly;
  - Gate −1 (no gate): unhappy-passenger statistic `+ (P/5 + 1) × 5` (P = economy passengers;
    always one extra group), `Ort = home`, advice 2354, player image −2, route image −2 when
    `player image % 10 == 0` (sic), satisfaction evaluation (§13);
  - real gate: taxi to gate (`Ort −3`, unloading mode), passengers disembark (visual persons
    with moods, §13) and the satisfaction evaluation runs; then from minute 45 the plane taxis
    away and finally `Ort = home`.
- **Destination ≠ home**: satisfaction evaluation for route/contract flights; `Ort = destination`.

### 11.6 Airport view and displays (high level)

[H/M] `AirportView.cpp: RepaintTextBricks`, `Sim.cpp: AddNewPassengers`, `Planetyp.cpp: DoOneStep`
- Only the home airport is visualised. Planes of all players with Ort −1/−2/−3 are drawn on the
  runway / behind the gate windows; ground planes elsewhere and airborne planes are not.
- Check-in and gate displays: for each route/contract flight with a gate departing from home in
  the current or next hour → "FROM-TO (S+1):00" on that gate's check-in sign and gate sign;
  arrivals landing this hour → "FROM-TO L:00" on the gate sign.
- From S−1:00 visual passengers spawn for departures with a gate (groups of 5, of 15 once 100
  have spawned; first `PassagiereFC` of them first-class).
- Landing announcements are voiced 09:00–18:00 for route/contract arrivals.
- The plane animation is frame-driven in the original (gameplay booking of gated home departures
  depends on the animation finishing). OpenAT should drive these milestones from sim time
  (recommended: book at S:55 for gated home departures) — see Open Questions.

---

## 12. Formulas

### 12.1 Distance and duration

[H] `city.cpp: CITIES::CalcDistance, CalcFlugdauer`
- Distance (metres): great-circle on a unit sphere from city lon/lat (degrees, π approximated as
  3.14159, single-precision floats): angle α in degrees between the two direction vectors;
  `d = trunc(|α| · 40040174 / 360)`; result capped at 16,440,000 m. Cached per city pair.
- Duration (hours): `D = ceil( trunc(d / speed_kmh) / 1000 ) + 1`, minimum 2.
  (= airborne hours rounded up, plus one hour of ground handling.)
- Planning limits: `d ≤ range_km·1000` and `D < 24`.

### 12.2 Fuel and fuel cost

[H] `Misc.cpp: CalculateFlightKerosin, CalculateFlightCost, CalculateFlightCostRechnerisch,
CalculateRealFlightCost`

- Fuel units (nominally barrels): `F = d/1000 × burn / 160 / speed` (integer, left to right).
- Market price `K = Sim.Kerosin` (DM per unit, daily random walk clamped to 300..700, start 500;
  see economy findings). Kind factor by the player's chosen quality `KerosinKind`:
  0 premium → `F·K·2`, 1 normal → `F·K`, 2 cheap → `F·K/2`.
- Tank: if the player's tank valve is open (`TankOpen`), up to `TankInhalt` units are taken from
  the tank first.
- Three cost variants:
  1. *Estimate* (`CalculateFlightCost`, used for prices/premiums/advisors): tank units count as
     free; rest × K × kind factor; minimum 1000. With "no player" (−1): `F·K`, minimum 1000.
  2. *Planner display* (`…Rechnerisch`): tank units at the tank's average purchase price
     `TankPreis` + rest at market × kind factor; minimum 1000.
  3. *Booking* (`CalculateRealFlightCost`): tank units free (already paid), rest at market × kind
     factor; **no minimum**.
- Reference fare base used throughout demand logic:
  `Base(route) = Estimate(von, nach, burn 800, speed 800, no player) × 3 / 180 × 2`
  (depends on today's K). Default ticket when renting a route: `Base × 2 /10 ×10`; FC default
  = 2 × economy (routes findings).

### 12.3 Route passengers (`CFlugplanEintrag::CalcPassengers`, type 1)

[H] `Schedule.cpp`. T = this flight entry's economy ticket price, B = route demand `Bedarf`,
Max = plane `MaxPassagiere`, S/L = start/landing hour.

Economy:
1. Weights for the 4 players `w_i`: if player i rents the route (Rang ≠ 0):
   `w = T ≠ 0 ? 10000/T : 100000` (**uses this flight's T for every player**);
   `×9/10` if `T > 3·Base`, again `×9/10` if `T > 5·Base`, `×8/10` if `T > 6·Base`; min 1;
   rank 1: `×150/100`, rank 2: `×120/100`; if `AvgFlown > 4`: `w = max(1, w·10/(10 + min(AvgFlown−3, 10)))`
   (AvgFlown is set to 30 when renting and never updated → this halving applies to every renter);
   AI (Owner 1): `w += w/4`. Non-renters: 0 (the flying player itself gets 1 if not renting).
   `W = Σ w_i`.
2. `share = W > 0 ? B·w_me/W : 0`; `x = min(share, Max + Max/2)`.
3. Lead time: `HoursBefore` is clamped to ≤ 48 (stored); if < 48: `x = x·(HB + 48)/96`.
4. Over-pricing: `C = 3·Base`; if `T > C and C > 10 and T > 0`: `x = x·(C − 10)/T`.
5. Night: `×5/6` if `S < 5 or S > 22`; again `×5/6` if `L < 5 or L > 22`.
6. Image: `I = clamp(4·routeImage + playerImage + 200, 0, 1000)`; `x = x·(400 + I)/1100`
   (result truncated to 16 bit).
7. Noise (`ptLaerm`): `×1000/1100` for each threshold exceeded: > 60, > 80, > 100, > 110.
8. `x = min(x, share, Max)` → `Passagiere`.
9. If the player's "press statement" sabotage flag is set: `Passagiere = 1 + Passagiere % 3`.

First class (same structure, differences only):
- price TF = FC ticket; thresholds `TF > 9·Base`, `> 15·Base`, `> 18·Base`; rank 1 `×170/100`,
  rank 2 `×120/100`; cap `MaxFC + MaxFC/2`;
- lead time cap 72 but `HoursBefore` was already clamped to 48 by the economy pass, so the factor
  is always `(HB + 48)/120` (max 80 %) [H, literal quirk];
- over-pricing: if `TF > 3·C and C > 10`: `x = x·(C − 10)/TF`;
- luxury: `Lux = Sitze + Essen + Tabletts + Deco + Triebwerk + Reifen + Elektronik + Sicherheit`
  `+ (security option bit 10) + (bit 11)` (0..18); `Lux < 6 → 0`; `6 ≤ Lux ≤ 18 → x·(Lux−6)/8`;
- noise thresholds > 40, > 60, > 80, > 100;
- final caps `share` and `MaxFC`; press statement same.

Contracts (type 2): `Passagiere = min(Max, persons)`, `PassagiereFC = persons − Passagiere`
(may exceed MaxFC). Freight (type 4): Passagiere = tons (§10.6). Automatic: 0.

### 12.4 Booking a flight (`CFlugplanEintrag::BookFlight`) — at departure

[H] `Schedule.cpp`. Steps in order:
1. Bad-kerosene factor BKF (§16).
2. Humans flying a route: a "route usage" counter increments (AI helper).
3. Recompute passengers (no effect so close to start, see §11.1).
4. Plane load averages: `Auslastung = first ? P·100/Max : (old + P·100/Max)/2` (economy and FC
   separately; 0 if no seats). `Kilometer += d/1000`; `SummePassagiere += P` (economy only);
   player `NumMiles += d/1609`.
5. Income `E` / expense `A`:
   - route: `E = T·P + TF·PFC`; `A` = booking fuel cost;
   - contract with Okay 0: `E` = premium; `A` = fuel; with Okay ≠ 0: `E = 0`, `A` = fuel;
   - automatic: `E = ptPassagiere·d/1000/40` (a small positive "empty flight" credit) [H, literal];
     `A` = fuel;
   - freight: `A` = fuel; if tons left > 0: `NumFracht += min(left, cap)` (also `NumFrachtFree`
     when premium 0), `left −= cap` (`cap = ptPassagiere/10`); when it reaches ≤ 0 → 0 and `E` =
     premium (paid once, on the completing flight).
   - routes and contracts additionally: `A += P × FoodCost[Essen]` (economy passengers only).
6. Statistics (routes & contracts): passengers (+P +PFC), flights +1, home-airport passengers.
   Route: `HeuteBefoerdert += P` on this route and `+= P + PFC` on the reverse route.
7. Bilanz: fuel+food to "kerosene" line and STAT_A_KEROSIN; route E to tickets, contract E to
   contracts (+contract counter), STAT_E_ROUTEN / STAT_E_AUFTRAEGE; plane balance `+E −A`.
8. `Saldo = E − A`; hidden AI bonus: `Saldo += min(Bonus, |Saldo|/10)`, `Bonus −=` same (Bonus is
   non-zero only for AI players).
9. Seat conversion (§12.6), then `Money += Saldo` (reason 2099 + type), `Gewinn += Saldo`.
10. Tank: if open, `used = min(TankInhalt, F)`; `TankInhalt −= used`; `BadKerosin −= used`
    (≥ 0); kerosene advisor warns when the tank runs empty; book `used × TankPreis` as
    calculatory cost to Bilanz, statistic and plane balance (no cash movement).
11. Route effects: `LastFlown = 0`; first route flight of the player ever → newspaper headline;
    route load EMA `Auslastung = first ? P·100/Max : (3·old + P·100/Max)/4` (FC likewise);
    `Bedarf −= P` (shared by all players, ≥ 0); route image +1 (if < 100).
    Contract effects: InPlan := −1 (done) if entry Okay 0 or 1, else advice 2313 (not fulfilled
    → penalty later); add-on mission 9 order counting.
12. Image effects (routes & contracts), §12.8.
13. `NumPassengers += P` (economy).
14. Wear (§7.1).

### 12.5 Penalties for contracts / freight

[H] `Player.cpp: PLAYER::UpdateAuftraege` (day change, humans only — AI never pays)
- On the morning after the last valid day (`BisDate == Date − 1`): if not done and not
  (InPlan 1 with Okay 1): pay `Strafe` (reason 2060 / 2065 freight), STAT_A_STRAFEN, mission
  profit reduced, letter (`TOKEN_LETTER 1000ff / 1005ff`); if Strafe is 0 just a letter
  (`1010ff / 1015ff`).
- Two days after (`BisDate == Date − 2`, freight `≤`): remove the order and every not-yet-started
  plan entry of it (start later today than 02:00, or later day); if an entry cannot be removed
  the order is kept.
- Late flights: a contract flight delayed past its last day becomes Okay 1 and is delayed
  hourly instead of departing (human players), so it effectively never flies.

### 12.6 Freight seat conversion

[H] At booking of any non-automatic flight: if the plane's "seats removed" state differs from
"this is a freight flight", toggle it and pay 15,000 (reason 2111).

### 12.7 Gate-less operations

Home departure without gate: plane leaves directly, image −2 (advice 2353). Home arrival without
gate: see §11.5.

### 12.8 Image effects per route/contract flight

[H] Image changes go to `pn` = the player himself, or the player named by an active
"advertising brochure" sabotage (then the flying player gets advice 2357).
For non-AI flying players:
- crew quality: if `PersonalQuality < 50` and `ObjectId % 10 == 0` → image −1; if `> 90` and
  `ObjectId % 10 == 0` → +1;
- route flights: route image −1 (if > 0) (cancels the +1 from §12.4 step 11).
Then `Add` (all players):
- condition: `< 20` → −50, `< 60` → −10 (cumulative: < 20 gives −60), `> 98` → +10;
- each of seats/food/trays/deco: level 0 → −1, level 2 → +2;
- non-AI: `+ 3·AnzBegleiter / ptAnzBegleiter` (integer);
- routes: compare T with `C2 = 3·Base` (AI: `C2·9/10`): `T < C2/2` → +10; `T > C2` → −10;
  `T > 1.5·C2` → −10; `T > 2·C2` → −10.
`image(pn) += Add/10`; route image `+= Add/10` if the result stays > 0, clamped 0..100;
player images clamped −1000..1000.

---

## 13. Passenger satisfaction

[H] `Planetyp.cpp: CPlane::CalculateHappyPassengers` (statistics) and the deboarding part of
`CPlane::DoOneStep` (visual moods). Evaluated once per arrival of route/contract flights.

- `QualitySum = Sitze + Essen + Tabletts + Deco + (AnzBegleiter − ptAnzBegleiter)`.
- `CrewQ = PersonalQuality`.
- Routes only: `C2 = 3·Base`; `TooExpensive = T > C2 ? min(100, T·100/C2) : 0` (so 0 or 100).
- RNG `TEAKRAND(player + Date + hour + QualitySum + VonCity)`.
- Total passengers N = P + PFC are processed in groups of 15 (the "customers per figure" constant):
  N/15 groups of 15 plus one last group of N % 15 (possibly empty). (Corrected: an earlier
  version of this spec said groups of 5.)
  Per group, first matching rule:
  1. `Rand(70) > Zustand` → unhappy;
  2. `QualitySum > 4` and `Rand(100) < 40` → happy;
  3. `QualitySum > 8` and `Rand(100) < 55` → happy;
  4. `Rand(65) > CrewQ` → unhappy;
  5. `TooExpensive` and `Rand(130) < TooExpensive` → unhappy;
  6. else happy.
  Adds the group size to STAT_ZUFR_PASSAGIERE / STAT_UNZUFR_PASSAGIERE.
- (A debug/observer special case weakens player 3 when the local player is AI — ignore.)
- Visual moods at the home gate follow the same rule chain per spawned person (FC uses Rand(90),
  thresholds 8/10 and Rand(100) for crew; the visual "too expensive" check uses a two-step ratio).
  Purely cosmetic.

---

## 14. Statistics touched by flights (summary)

STAT_PASSAGIERE, STAT_PASSAGIERE_HOME, STAT_FLUEGE, STAT_E_ROUTEN, STAT_E_AUFTRAEGE,
STAT_A_KEROSIN (fuel + food + tank calc. cost), STAT_ZUFR/UNZUFR_PASSAGIERE, STAT_VERSPAETUNG
(delays), STAT_WARTUNG, STAT_A_STRAFEN, STAT_A/E_SONSTIGES (equipment, purchase/sale),
STAT_UNFAELLE (only sabotage acts increment it). Player counters: NumFlights, NumPassengers,
NumMiles, NumFracht, NumFrachtFree, NumAuftraege. Plane: Salden, Auslastung(FC), Kilometer,
SummePassagiere, Wartungskosten, NumPannen/Pannen.

---

## 15. Route demand (dependency; see routes findings)

[H] `Route.cpp: CRoute::AnzPassagiere, CRouten::NewDay`
`potential = sqrt(pop_from × pop_to) × Faktor / DayF / 2 × 3 / 4` with `DayF = 1000` (Date < 3),
900 (< 9), 750 (< 29), else 650. Day 0: `Bedarf = potential × 4.27`; later days:
`Bedarf = (6·Bedarf + potential × 4.27)/7`. Flown passengers reduce Bedarf (§12.4).

---

## 16. Kerosene quality interplay

[H] `Schedule.cpp: BookFlight`, `Stdraum.cpp` MENU_BUYKEROSIN, `Dialog.cpp` TALKER_ARAB
- The player chooses a quality at Arab Air; it is remembered (`KerosinKind`) and applies both to
  tank purchases and to every un-tanked flight purchase (§12.2).
- Tank purchase of amount m: `TankInhalt += m`; cheap: `BadKerosin += m`; premium:
  `BadKerosin = max(0, BadKerosin − m)`; `TankPreis` = weighted average purchase price.
- Consumption drains BadKerosin first (`BadKerosin −= used`).
- **BKF = 3** (triple wear) if (tank open, tank not empty and `BadKerosin·100/TankInhalt > 50`)
  or (tank closed or empty and the current kind is cheap); else 1. Evaluated before the tank is
  drained. Premium fuel has no positive effect beyond cleansing the tank. Bad fuel has no other
  effect (no extra incidents).

---

## 17. Randomness summary

Game RNG `TEAKRAND`: LCG `v = v·7381 + 0x269EC3` (32-bit), output `v >> 16` (16 bit);
`Rand(n) = output % n` (0..n−1; `n` negative → C remainder semantics; `n == 0` crashes).

| Use | Seed |
|---|---|
| broker/designer purchase names | `Date` (one generator per purchase batch) |
| used planes | `Date + slot` |
| mechanic mode 2 increments | `Date` (per player, planes last→first) |
| breakdown trigger | `Date + hour + minute` |
| in-flight incidents | `Date + hour + minute` (new generator per plane, same seed) |
| landing stagger / gate approach stagger | `Date + hour` |
| satisfaction | `player + Date + hour + QualitySum + VonCity` |
| visual deboarding | `gate + Startzeit + hour + Date` |
| image "random" effects | deterministic via `ObjectId % 10` |

---

## 18. Plane-related room/feature checklist for implementation

1. Type table loader with quirk-faithful parsing (§2).
2. Plane entity with all fields of §3, Ort state machine (§3.2, §11).
3. Broker, museum (3 daily offers + refill), selling with block rule, market value (§4).
4. Designer: part catalogue + relation graph data files, stats, validation, purchase (§5).
5. Equipment room: targets, money check, application when grounded, costs/refunds, FC split,
   attendant target, copy/paste-technical (§6).
6. Daily maintenance with WorstZustand pricing; mechanic modes; target repair menu (§7).
7. Flight plan model, planner placement rules, validation algorithm with auto flights,
   extend button, weekly rollover, gate planning, contract/freight usage flags (§9–10).
8. Hourly sim: passenger refresh, delays, departures (booking), incidents, landings, satisfaction (§11–13).

---

## 19. Differences GOG original vs. SDL port (behavioural)

[H] by diffing the two trees (`diff -w`):
1. **First-class demand** (`CalcPassengers`, FC pass): the GOG original uses the *economy*
   ticket price for the FC weights (`10000/T`, thresholds 9/15/18·Base) and for the FC
   over-pricing penalty. The SDL port uses the FC price. Most other code identical.
2. **Freight tonnage counting** (`UpdateFrachtauftragsUsage`): the GOG original counts an entry
   toward `TonsOpen` if `Startdate > Date OR Startzeit > hour OR (Startzeit == hour and plane not
   airborne)` (date and hour tested independently — past-day flights with a later hour are
   counted). The SDL port ignores entries that started on an earlier day or earlier hour today.
3. Boarding animation: GOG never switches waiting persons to the boarding state in one branch
   (commented out); SDL boards FC passengers directly. Cosmetic.
Decision for OpenAT: default to SDL behaviour (fixes), optionally "GOG-faithful" switch.

---

## 20. Open questions

1. **Animation-coupled booking**: gated home departures are booked when the take-off animation
   leaves the screen and arrivals are unloaded when the taxi animation reaches the gate. Under fast
   time (night skip) do these always complete within the hour? Proposal: book at S:55 and unload at
   L:30 deterministically; verify against the original by observation.
2. Argument evaluation order in the used-plane constructor call (name, type, `Rand(80)+11`) — MSVC
   evaluates right-to-left, which would draw the throw-away condition first, then type, then name.
   Needed only for bit-exact museum offers. Verify empirically (load a save, compare offers).
3. `relation.csv` parent/child columns are informative only (not imported); the compiled table's
   slot/blocked-slot data must be extracted. Confirm no gameplay differences between compiled and
   CSV offsets/notes beyond what §5.3 lists (spot check showed identical notes/noise).
4. Mechanic trust levels (beer → 1, negotiation → 2) and which dialog options appear at each level
   — dialog flow only partially traced [M].
5. Planner route list filtering (which routes appear, flags 3/4 meaning "no office in city" vs.
   "not rented") — belongs to routes findings; only the refusal texts were traced here.
6. `Startzeit == 24` can survive `ExtendFlugplaene` until validation; whether any code path
   observes it in between is untested.
7. The museum dialog has separate lines for sponsored planes (`TOKEN_MUSEUM 712/713`), but no
   additional rule was found; confirm there is no hidden sale block.
8. Units of `TankInhalt` vs. fuel units F (assumed identical "barrels"); confirm with the economy
   findings for the tank capacity/price menu.
9. The manual PDF could not be rendered in this session (no PDF tool); its descriptions of
   equipment effects were not cross-checked. Source is authoritative regardless.
