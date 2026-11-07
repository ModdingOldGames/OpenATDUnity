# Items, sabotage, security and special rooms — gameplay behaviour spec

Clean-room behaviour spec for OpenAT. Written from reading the original source (gog_src), the SDL
port (ref_AirlineTycoon; identical unless noted), the manual and the installed `misc/*.res`
resources. No code is reproduced; in-game text is referenced only by resource token + id.
Room layout/animation/UI is specified in `rooms-and-ui-*.md`; this file covers **gameplay effects**.

Confidence tags: **[H]** read directly and cross-checked, **[M]** read but some path not fully
traced, **[L]** inferred.

---------------------------------------------------------------------------------------------------

## 1. Rival pranks ("sabotage")

Traceability: `Dialog.cpp: CStdRaum::ProcessDialog` (TALKER_SABOTAGE branch, price tables at file top),
`Stdraum.cpp: CStdRaum::StartDialog` (TALKER_SABOTAGE opener), `Stdraum.cpp` item use (ITEM_MG,
ITEM_SPINNE), `Stdraum.cpp: MenuLeftClick` (MENU_SABOTAGEPLANE / MENU_SABOTAGEROUTE),
`Sim.cpp: SIM::DoTimeStep` (hourly plane-prank execution), `Aufsicht.cpp: CAufsicht::~CAufsicht`
(morning execution of person/special pranks), `Dialog.cpp` TALKER_BOSS ids 2004/2050–2074
(briefing report and fine), `Player.cpp: PLAYER::NewDay`, `PLAYER::RobotExecuteAction`
(ACTION_SABOTAGE), `PLAYER::RouteWegnehmen`, `PLAYER::DoBodyguardRabatt`,
`Sim.cpp: SIM::NewDay` (DIFF_ATFS06 "foreign sabotage"). SDL port identical for every number below.

### 1.1 Per-player sabotage state

| state | meaning |
|---|---|
| trust (0..5/6) | how far the saboteur trusts the player; gates which pranks are offered. Starts 0 each game. |
| hints (0..) | "evidence" accumulated by the player's pranks. Decays by 3 per day (never below 0). Exposure threshold 100. |
| pending plane prank | mode 1..5, victim, target plane, armed flag |
| pending person prank | mode 1..4, victim |
| pending special prank | mode 1..6, victim, target plane (mode 5) or route (mode 6) |

Only **one** prank may be pending at a time across all three categories: if any is pending, the
saboteur refuses new orders (Sabo 1100). [H]

### 1.2 Prerequisites / access [H]

* The saboteur room is reached through a secret door (see rooms-and-ui-8 §3). Not in the DEMO.
* With trust 0 the saboteur only gives a brush-off line (Sabo 1021). Trust becomes ≥1 when the
  player **uses the Kalashnikov case item (ITEM_MG) inside the Arab Air / Petrol Air counter**
  (item consumed, Arab Air dialog opens). That is the only way to get trust 1.
* Dialog flow: pick victim (the three other airlines; an eliminated airline gives Sabo 1045 and
  nothing), then category: plane / person / special.
* Offered options per category: options 1..min(cap, trust), where cap = 5 (plane), 4 (person),
  6 (special). So trust 1 shows only the first option in each list.
* Trust growth:
  * Plane prank bought: trust +1, capped at 5.
  * Person prank k bought: trust = max(trust, k+1).
  * Special prank k (1..4) bought: trust = max(trust, k+1).
  * Special 5 and 6: **no trust gain** (the code computes a large negative value and takes the max:
    the step uses the person list's dialog-id base instead of the special one; the intended value is
    k + 1, i.e. 6 after the impound, which would unlock route theft). Bug.
  * Consequence: the highest trust any path gives is 5 (plane cap 5, person 4 → 5, special 4 → 5),
    so the special list never shows option 6. **Special 6 (route theft, needs trust 6) is
    unreachable in normal play**; only a debug cheat sets trust ≥6. [H for the arithmetic; M that
    nothing else raises trust — only `Gameframe.cpp` debug keys were found]. OpenAT fixes it by default
    (`GameOptions.FixSpecialPrankTrust`; off restores the original).
* Affordability: the order is refused (Sabo 6000) if money − price would go below the debt limit
  (`DEBT_LIMIT`, see economy spec).
* Security check: if the victim has the matching protection (§2.2), the saboteur refuses with a
  victim-specific line (Sabo 2090..2098) and nothing is charged.
* The victim's "security needed" flag (§2.4) is set only when the order passes the security check
  (for plane pranks: when the option is chosen, before the plane is picked and paid).

### 1.3 Price list and effects

Prices are fixed (no inflation, no difficulty scaling). Payment is immediate on ordering (plane:
after the plane is picked). Bodyguard rebate (§1.7) applies.

**Plane pranks** (category "plane", victim's plane chosen from a list of the victim's planes):

| # | price | trust needed | hints | victim effect when it fires |
|---|---|---|---|---|
| 1 | 1 000 | 1 | +2 | share price ×0.95; dividend trust −1; victim's sympathy to saboteur −5; if the current flight is a route flight, that route's image ×0.99 |
| 2 | 5 000 | 2 | +4 | money −2 000; share price ×0.90; dividend trust −2; sympathy −15; image −2; route image ×0.95 |
| 3 (flat tyre) | 10 000 | 3 | +10 | money −8 000; share price ×0.85; dividend trust −3; sympathy −35; image −3; route image ×0.90; **next departure delayed by 1 h** (counts as a delay in the saboteur's statistics – quirk: the delay stat is booked to the *saboteur*, not the victim); advisor message to victim |
| 4 | 50 000 | 4 | +20 | money −40 000; share price ×0.75; dividend trust −4; sympathy −80; image −8; route image ×0.50 |
| 5 | 100 000 | 5 | +100 | money −70 000; dividend trust −5; sympathy −200; **no share-price change, no image change, plane is kept** (the "plane prank variant keeps the plane": victim loses money and investor trust only) |

Notes [H]:
* "Dividend trust" (investor trust in the dividend) is clamped at 0; share price clamped at 0;
  image clamped −1000..1000. Hourly share recalculation pulls the price toward 10× dividend trust,
  so lowering it has a lasting effect beyond the direct multiplier.
* **No plane-condition damage** is applied by any plane prank (no change to wear/condition).
* Firing time: the prank is first *armed* once the target plane is on the ground at an airport.
  After arming, it fires at the next hourly tick on which the plane is **airborne** (modes 1,2,4,5)
  or, for mode 3, on the hourly tick equal to the plane's next scheduled departure hour while still
  on the ground. If the plane is sold/destroyed before firing, the prank silently lapses (money not
  refunded).
* Quirk: if the plane has **no next flight** in its schedule when it fires, the accident/sabotage
  statistics are incremented but nothing else happens and the prank stays pending, so it
  re-triggers every hour until a flight exists. [M]
* On firing: victim's accidents statistic +1, saboteur's sabotage statistic +1; a newspaper
  headline override is created (Std `Misc` 2000+mode, picture id per mode); the act is logged for the
  next briefing. If the saboteur is the local player he gets the saboteur's call (rooms-and-ui-8 §3);
  if the victim is the local human he gets a letter (Std `Letter` 500+mode) and either a fax popup
  (when fax option on and no dialog/menu open) or an advisor message (Std `Advice` 2301+mode).

**Person pranks** (executed next morning when everyone leaves the briefing, effect lasts that day):

| # | price | trust needed | hints | effect |
|---|---|---|---|---|
| 1 (bad coffee) | 10 000 | 1 | +8 | victim sick for the day: between 09:00 and 18:00, at random moments (roughly every 127 ticks a 1-in-4 roll) the victim's character is forced to run to the toilet; sympathy −10. Cured by the pills item (§3). |
| 2 (laptop virus) | 25 000 | 2 | 0 | only if victim owns a laptop (ordering is refused, Sabo 1300, otherwise; no charge). Laptop becomes infected (§3, laptop). **No hints added** for this one. |
| 3 (office bomb) | 50 000 | 3 | +25 | office armed; victim is sent walking to his office; when he reaches the door it explodes: office destroyed for the rest of the day (cannot be used), explosion video; sympathy −50. The local player's camera follows the victim to the explosion; if no bomb was set, every boss walks to his own office as usual [M]. Entering the destroyed office later yields the glowing-coal item (§3). |
| 4 (provoke strike) | 250 000 | 4 | +40 | sets "strike planned" on the victim; the normal strike machinery (`Sim.cpp`) then starts a strike between 07:00 and 17:00 once the victim has a plane on the ground or landing within 2 h. Strike length: 2 h if the victim had >10 strike-free days, 72 h if average staff happiness < −10, else 4..23 h. All departures are delayed while it lasts. |

**Special pranks** (also executed next morning):

| # | price | trust needed | hints | effect |
|---|---|---|---|---|
| 1 (fake brochures) | 100 000 | 1 | +8 | for that day, image changes earned by the victim's flights go to the **saboteur's** airline and arriving passengers are tagged with the saboteur's airline; advisor message to victim per flight (Std `Advice` 2357). |
| 2 (phones dead) | 500 000 | 2 | +15 | victim cannot use the mobile phone and cannot be called for the day. |
| 3 (press release) | 1 000 000 | 3 | +25 | for the day every route flight of the victim books only 1..3 passengers per class (passenger counts reduced to 1 + (n mod 3)); all of the victim's route flights are recalculated immediately. A letter goes to the local player and an advisor message to the victim. |
| 4 (bank hack) | 2 000 000 | 4 | +30 | victim −1 000 000, saboteur +1 000 000 (net cost to saboteur 1 000 000). |
| 5 (impound plane) | 5 000 000 | 5 | +50 | chosen plane gets a 15-hour "pseudo problem": each hour, a departure due next hour is pushed back 1 h (advisor message 2358). Counts down 1 per hour. **Quirk: the delay logic skips robot-owned airlines entirely, so this prank has no effect on computer rivals.** |
| 6 (steal route) | 8 000 000 | 6 | +70 (always, even foreign) | chosen route **and its reverse** removed from the victim (removed from his schedules from the next hour on; lower-ranked holders move up a rank). **Quirk:** the code intends to hand the route to the saboteur, but the guard compares an unsigned byte with −1 so the hand-over branch never runs — the saboteur gets nothing. Advisor message 2021 to victim. |

### 1.4 Being caught [H]

* Each executed prank is listed at the next **morning briefing** (supervisor's office). The boss
  comments per act according to the saboteur's current hints: <15 / <40 / <100 → three escalating
  "no idea who did it" lines (Boss 2070/2071/2072); **≥100 → exposed**.
* Exposure consequence: the saboteur pays a **fine of hints × 10 000** to the victim (booked as
  fines in the statistics); the briefing cannot be skipped while an exposed act exists. No jail, no
  image penalty, no lost days. Hints are **not** reset by the fine, so a player who stays ≥100 is
  fined again for every further listed act. A plane prank 5 alone adds 100, but it fires during the day
  and the night's decay of 3 comes first, so at the briefing the saboteur has 97 and is **not** exposed by
  it alone (only if the plane took off after midnight, or with evidence left from earlier acts).
  (Corrected: an earlier version said a single plane prank 5 exposes by itself.)
* There is no random "chance of being caught" — exposure is deterministic from hints.
* The boss also notes each victim's "days without sabotage" (reset to 0 for every listed victim);
  used by the "no sabotage for N days" missions (DIFF_ATFS04/06).

### 1.5 Robots [H]

* Computer bosses order pranks through their action loop (ACTION_SABOTAGE) **without paying**
  anything. Their trust rises by 1 per attempt (capped 4); they skip if hints ≥100, image ≤ −500,
  or a prank is pending; usually only every other day-hour pattern; normally only against a
  **human** player unless the mission flag "much sabotage" is set; extreme-sabotage missions target
  the leader.
* Category chosen by game hour mod 3 (plane / person / special). Mode = min(cap, trust), with
  per-robot random downgrades (robot 0 mostly modes 1–2, robot 2 mostly ≤3). The virus prank is
  downgraded to coffee if the victim has no laptop.
* If the victim's security blocks the choice, the robot counts an "annoyance"; with ≥2 annoyances
  on an even day, and the pliers still lying in the saboteur's room, the robot takes the pliers and
  then cuts the Security office's power (§2.5).
* Robots also have their hints decay and can be exposed and fined like humans.

### 1.6 Foreign sabotage (mission DIFF_ATFS06) [H]

Every day each non-local airline without a pending prank and hints <90 is given a random prank
(any category, mode 1..4 / 1..4 / 1..5) against a random other airline with planes. These are
anonymous: no hints, no trust, no fine, the briefing shows no culprit; security still blocks them.

### 1.7 Bodyguard rebate [H]

If the player employs a security advisor of quality >20, every sabotage payment (and most other
shop purchases — kerosene, planes, ads, duty-free items) triggers a refund of
`price / 100 × (quality / 10)` (i.e. quality/10 percent), with an advisor message.

### 1.8 Multiplayer quirk [H]

For special pranks the network money-change message uses the person-prank price table at the
same index (wrong amount sent to peers; special 5/6 send the correct value). OpenAT should
broadcast the real price.

## 2. Security office (protections)

Traceability: `Stdraum.cpp` MENU_SECURITY (paint + click), `Player.cpp: PLAYER::CalcSecurityCosts`,
`Player.cpp: PLAYER::NewDay` (daily billing), the protection checks listed in §1, flag consumers in
`AirportView.cpp`, `Person.cpp` (gate scanner), `Bank.cpp`, `Laptop.cpp`, `Personal.cpp`,
`PlanProp.cpp`, `Block.cpp`. `SecurityManager.cpp` is **not** gameplay (CD copy protection).

### 2.1 Buying [H]

* Dialog in the Security office → menu with 9 rows (ids Std `Misc` 8000..8008; gate row upgrades
  show 8010/8011). Clicking a row **toggles** that protection on/off immediately; no up-front fee.
  The laptop row is greyed and not clickable unless the player owns a laptop.
* The gate row is a 4-step cycle: off → level 1 → level 2 → level 3 → off.
* Billing: once per day at day change, the total is deducted (statistics "other expenses").
  Office-type rows are a flat daily fee; plane-type rows are charged **per plane owned**.

### 2.2 Protection list

| row | flag | daily cost | billed | blocks | other effect |
|---|---|---|---|---|---|
| 0 office | bit 0 | 25 000 | flat | person 1 (coffee) **and** person 3 (office bomb) | security camera brick at the gate shows "on" |
| 1 laptop | bit 1 | 20 000 | flat | person 2 (virus) | antivirus animation in laptop view |
| 2 personnel office | bit 2 | 25 000 | flat | person 4 (strike) | "no sabotage" sign in personnel office |
| 3 bank | bit 3 | 30 000 | flat | special 4 (bank hack) | modem animation in bank |
| 4 route board | bit 4 | 30 000 | flat | **nothing** — only draws a padlock on the routes page of the planner/laptop | — |
| 5 administration | bit 5 | 30 000 | flat | special 2 (phones) **and** special 3 (press release), and special 6 (route theft) | — |
| 6 plane interior | bit 6 | 30 000 | per plane | plane 1, 2, 5 | badge on stewardess in plane-props screen |
| 7 plane exterior | bit 7 | 30 000 | per plane | plane 3, 4 | — |
| 8 gate L1 | bit 8 | 40 000 | per plane | special 1 (**fake brochures** – mapping quirk) and special 5 (impound) | gate scanner graphics; 1-in-30 boarding passengers is "trapped" and leaves |
| 8 gate L2 | +bit 10 | +50 000 (=90 000) | per plane | same as L1 | scanner holds each passenger 240 ticks (animation), no trap roll |
| 8 gate L3 | +bit 11 | +50 000 (=140 000) | per plane | same as L1 | as L2 with a different animation |

Bit 9 is unused. Levels 2/3 cost more but **block nothing extra** (and remove the trap roll). [H]

Mapping quirks to preserve: brochures are blocked by the gate flag, route theft by the
administration flag, the route-board protection blocks no prank at all.

### 2.3 When protection is checked [H]

Only when the prank is **ordered** (human dialog / robot decision / foreign-sabotage roll). Buying
protection after a prank was ordered does not cancel it; cancelling protection after ordering does
not let it through retroactively either way (no re-check at execution).

### 2.4 "Needed" flags and robot buying [H]

Every successfully ordered prank sets the matching flag in the victim's "security needed" mask.
Robots visiting the Security office (ACTION_VISITSECURITY) do 2 passes: if money > 1 000 000 they
pick a random flag 0..8 and switch it on only if it is in their "needed" mask; otherwise (poor) they
switch a random flag 0..8 off. So robots buy protection only against pranks already used on them.

### 2.5 Power outage (pliers) [H]

* The pliers lie in the saboteur's room once per day (shared world item, see §3). Taken by clicking.
* Using the pliers in the airport **next to the Security office entrance** consumes them, wipes
  **every** airline's protections (all flags 0) and closes the Security office for 3 days (closed
  sign "power outage"; counter decremented at each day change).
* Walking into a rival's office door while carrying the pliers (the "wrong room" popup) instead
  consumes them and cuts that rival's office power: office goes dark for the day (cosmetic dark
  overlay over office and planner globe) [M: no functional lockout found]. At the next day change
  the airport graphics are force-reloaded (implementation detail: the loaded-season marker is set
  to an invalid value) so the darkened door art is reset. Office power returns next day.

## 3. Carried items

Traceability: `Stdraum.cpp: CStdRaum::OnLButtonDown` (inventory click switch = "use item"),
`Player.cpp: BuyItem/DropItem/ReformIcons/NewDay`, `DutyFree.cpp: OnLButtonDown/OnPaint`,
`Dialog.cpp` (TALKER_DUTYFREE, TALKER_SABOTAGE 3000/3002, TALKER_PERSONAL 300/301,
TALKER_WERBUNG 8001/8002, TALKER_KIOSK 1021, TALKER_COMPETITOR pralines ids 2012/2032),
`Stdraum.cpp: StartDialog` (TALKER_TRINKER strike end, trust-gated openers), room click handlers
`ArabAir.cpp, Makler.cpp, Fracht.cpp, RouteBox.cpp, Tafel.cpp, Werkstat.cpp, reise.cpp, Kiosk.cpp,
sabotage.cpp`, `Person.cpp` (vending machine, glue, stench), `Sim.cpp: AddGlueSabotage,
AddStenchSabotage, NewDay`, `Laptop.cpp`. Confidence [H] unless noted.

### 3.1 Inventory rules

* 6 slots, start empty. Picking up an item fails silently when all 6 are full (most sources check
  "has space" first). After any change the inventory is compacted/sorted.
* Using an item = clicking its slot in the status bar (not while the clock is stopped). If the item
  has a special effect in the current room/context it is applied; otherwise, if the room has a
  dialog partner, a canned "what is that for" reaction is shown (rooms-and-ui-3 §7) and nothing
  happens. In the newspaper kiosk, items do nothing unless the headline is "interesting".
* **World items** (glove, paper clip, glue, glowing coal, perfume, pliers, postcard) exist **once per
  day for all players together**: the first player to take one removes it for everyone; all are
  restored at each day change (glue returns to its "locked" state).

### 3.2 Bought items (duty-free shop, `ROOM_SHOP1`)

Prices come from Std `Item` 2000+id (names Std `Item` 1000+id); values below are from the installed
English `std_ger.res`. Bodyguard rebate (§1.7) applies to all.

| item | price | availability | use |
|---|---|---|---|
| Laptop (4 models) | 10 000 / 30 000 / 50 000 / 100 000 (Item 2000..2003) | from day 8 on (days 0..7 locked); only **one laptop sale per day for the whole airport** (first buyer of the day locks it); each purchase upgrades to the next model (old one replaced); max model 4 | opens the portable planner (same as the office planner, usable anywhere). Battery per day: 40 / 80 / 200 / 1440 game minutes for models 1..4, recharged at day change; 1 unit per game minute while open, warning beep at 2, planner inert at 0. |
| Kalashnikov in violin case (MG) | 80 000 (Item 2400) | from day 1 on, only while the player's saboteur-trust is 0 and he does not own one | use at Arab Air counter → saboteur trust 1 (§1.2); consumed |
| Filofax | 5 000 (Item 2500) | if not owned | address book: pick a room → the boss walks there automatically |
| Mobile phone | 300 000 (Item 2600) | if not owned | phone menu from anywhere (call rooms/rivals; see rooms-and-ui-3). Required to be callable. Disabled by the phones prank. |
| Beer ("liquor") | 5 000 (Item 2700) | if not owned | in workshop: mechanic trust ≥1 and "angry" cleared, consumed (unlocks oil; trust 2 via dialog unlocks extra maintenance modes — see economy/planes specs). Elsewhere with no partner: drinks it → drunk (+400 wobble units: the mouse cursor wobbles, 1 unit per sim step). |
| Chocolates / spiked chocolates | 10 000 each (Item 2801) | via saleswoman dialog, only if neither is owned | see 3.4 |

Robots get laptop upgrades (same once-per-day lock, money > 100 000, day > 7) and a phone
(money > 100 000) **for free** at their duty-free visit.

### 3.3 Trade-chain items (found/given, no price)

The chains: *bra → horseshoe*, *spider → dart → diskette*, *postcard → pills*, *Red Bull → stink bomb*,
*paper clip → glue*, *glove → Red Bull*. "Trust" flags below are per player, permanent for the game
(except kiosk-trust, which is used up by one stink bomb). Once a trust flag is set, the reward
dialog appears on every later talk and hands out another copy whenever the player has a free slot
(pills, dart, diskette, horseshoe, oil) [M: repeatability read from code, not tested]. The dart
dialog additionally needs saboteur-trust ≥1 (otherwise the saboteur only brushes the player off).

| item | where obtained | condition | use (room) → effect |
|---|---|---|---|
| Bra | plane broker room, click the hatch | not owned, no horseshoe, drinker-trust not yet earned, not tutorial | duty free → duty-trust; next talk to the saleswoman gives the **horseshoe** |
| Horseshoe | duty free (after bra) | — | Rick's café, give to the drinker → drinker-trust. Afterwards, whenever the player's staff is **on strike**, talking to the drinker ends the strike after a short countdown (10) |
| Spider | travel agency, catch it while its animation is in the catchable frames | not owned | saboteur room, only if saboteur-trust ≥1 → spider-trust (consumed); then the dartboard hotspot dialog gives the **dart** |
| Dart | saboteur's dartboard (after spider) | no dart, no diskette, ad-trust not earned | advertising agency, only in Normal-or-harder missions or free game → ad-trust; next talk gives the **diskette** |
| Diskette | advertising agency (after dart) | — | anywhere while the own laptop is infected → virus removed instantly (leaves the laptop view if open), consumed |
| Postcard | route board in the supervisor's office (world item) | personnel-trust not yet earned, not tutorial | own personnel office → personnel-trust; next talk to the personnel manager gives the **pills** |
| Pills | personnel office (after postcard) | — | anywhere while sick from the coffee prank → cured, consumed |
| Glove | Arab Air counter (world item) | not owned | at the vending machine in the airport: turns into **Red Bull**. (Without a glove the vending machine gives the boss an electric shock: short smoke effect, cosmetic; also sobers him up.) The Security office has a dead copy of the glove hotspot. |
| Red Bull | vending machine (glove) | — | kiosk → kiosk-trust (consumed); next kiosk talk gives the **stink bomb** (kiosk-trust reset). Anywhere without a partner: drink → caffeine 2400 ticks: on the airport floor the boss takes 2 extra walking steps per step (≈3× speed); decays 1 per sim step during office hours; reset at day change. |
| Stink bomb | kiosk (after Red Bull) | not owned | airport **lower level** only (upper level → refusal popup): drops a stink cloud at the boss's feet that lasts 3960 ticks; passers-by avoid it [M: avoidance behaviour lives in the people sim]. |
| Paper clip | route box (world item) | — | freight counter, while glue still locked and not owned → glue unlocked (clip consumed, freight dialog) |
| Glue | freight counter (after clip; world item) | — | airport **upper level** only, standing still: glues the floor tile in front (in facing direction, if walkable). Any boss walking onto it is stuck for 600 ticks (cannot be phoned meanwhile). Lower level → refusal popup. |
| Glowing coal | trying to enter an office destroyed by the bomb prank (human players only; world item) | — | Last-Minute counter or travel agency: **all remaining offers there are burnt** (removed for everyone). Cools to plain coal at day change. |
| Coal | glowing coal after the night | — | no use |
| Perfume | men's toilet (bosses in slots 2–4; slot 1 uses the ladies' room; world item) | — | in the workshop while the "slime" event is active (resets every 6 days) → becomes **stink perfume** |
| Stink perfume | workshop (from perfume) | — | airport: the boss stinks (2000 units, decreasing per airport frame): smoke puffs and nearby floor cells are marked so people keep away [M] |
| Pliers | saboteur room (world item) | — | §2.5: at the Security entrance → all protections wiped, office closed 3 days; at a rival's office door → rival's office power cut for the day |
| Cigar | never obtainable | — | no effect (case exists, empty) |

### 3.4 Chocolates [H]

Bought from the duty-free saleswoman (dialog offers the normal and the "special" box, 10 000 each).
* Eaten (no partner present): normal → nothing; spiked → drunk (+400).
* Offered to a rival boss during a face-to-face talk (option only appears when carrying chocolates):
  * Robot rival: normal → his sympathy toward the player +10; spiked → −10 (clamped ±1000).
  * Human (network) rival: normal → just a line; spiked → the other human becomes drunk (+400).

### 3.5 Laptop virus lifecycle [H]

* Infection (person prank 2): laptop view shows only broken buttons; planes cannot be dragged and
  planner buttons do not work.
* Cures: diskette (instant); or take it to the duty-free woman: day 1 she keeps it (laptop removed
  from inventory), it is "repaired overnight", next visit she returns it free of charge.
* Robots' laptops are cleaned automatically at day change.

## 4. Special rooms with gameplay effect

UI/animation for all rooms below: rooms-and-ui-8 (§5 NASA, §6–9 telescopes) and rooms-and-ui-6 §3
(supervisor). Only gameplay effects here.

### 4.1 NASA shop — rocket (DIFF_FINAL) and space station (DIFF_ADDON10) [H]

Traceability: `Dialog.cpp` TALKER_NASA (part ids 5000–5003, 5100–5107, 8000–8202; price tables
at file top), `Player.cpp: PLAYER::HasWon`, `PLAYER::RobotExecuteAction` (ACTION_VISITNASA,
saving logic), `Misc.cpp` (progress headlines), SDL `Player.cpp: PLAYER::AddRocketPart`.

* The shop exists only in the airport layouts of the two space missions. Usable in person or by
  phone. Both missions share one 10-bit "parts" mask per player; **winning = all 10 parts owned**.
* Purchase rule: refused if the part is owned (text), if a prerequisite is missing (text), or if
  money < price (plain comparison with current money, no debt allowance). Otherwise the bit is set
  and the price is paid (money reason 3400), fanfare plays, salesman animation.
* Menus are staged: stage-2 parts are only listed once all stage-1 parts are owned (stage 3 for the
  station likewise).

**Rocket (mission "The Final Frontier")**

| stage | part (bit) | price | prerequisite |
|---|---|---|---|
| 1 | launch base (0) | 200 000 | — |
| 1 | gantry tower (1) | 400 000 | base |
| 1 | gantry arm (2) | 600 000 | tower |
| 2 | airframe (3) | 5 000 000 | (stage 1 complete) |
| 2 | wings (4) | 8 000 000 | airframe |
| 2 | passenger capsule (5) | 10 000 000 | airframe |
| 2 | tail section (8) | 20 000 000 | airframe |
| 2 | side boosters (6) | 25 000 000 | airframe + wings |
| 2 | main engine (7) | 50 000 000 | airframe |
| 2 | cockpit (9) | 85 000 000 | airframe |

Total 204 200 000. Headlines appear when an airline first completes stage 1 and when it owns the
first 7 bits (bits 0–6).

**Space station (mission "Beyond the Final Frontier")**

| stage | part (bit) | price |
|---|---|---|
| 1 | ring A (0) / ring B (1) / ring C (2) | 1 000 000 / 2 000 000 / 3 000 000 |
| 2 | oxygen (3) / communications (4) / main module (5) / living module (6) / office module (7) | 2 000 000 / 10 000 000 / 20 000 000 / 35 000 000 / 50 000 000 |
| 3 | power plant (8) / solar panels (9) | 35 000 000 / 80 000 000 |

Total 238 000 000. Within a stage there are no prerequisites.

**Robots:** only with the mission's "uses NASA" robot flag; at each visit they buy the **lowest
missing bit** if its price (indexed by bit number) is below their money. Because the rocket price
table is ordered by menu position, not bit number, robots pay 20 M for bit 6, 25 M for bit 7, 50 M
for bit 8 while humans pay 25 M / 50 M / 20 M for those bits (same total). Robots switch to "saving
for rocket" when they have >3 planes and are not ahead of their rival in parts.

**SDL-port bug (do not copy):** the port routes human purchases through a helper that ignores the
passed price and always charges the cheapest rocket part (200 000), for rocket *and* station
parts; it also passes the rocket table for station parts. The original charges the table prices
above. OpenAT must charge the per-part price.

### 4.2 Telescope views: Island, Space, Mount Rushmore [H]

* Each visit (constructor) costs **1** currency unit (money reason 3300, "telescope coin"), also for
  the "no island" variant.
* **Island** (`Insel.cpp`): displays each active airline's rocket on its launch island, part by part
  from the parts mask. Pure display; below Normal difficulty the "nothing to see" picture is shown.
* **Space** (`WeltAll.cpp`): displays each airline's station modules; lit windows/active modules
  require both power plant and solar panels; full station special art when all 10 bits are owned.
  Pure display.
* **Mount Rushmore** (`Rushmore.cpp`): ranks the active airlines by current mission rating (sign
  flipped for the "lowest wins" mission DIFF_ADDON01) and shows logos/values in that order. Pure
  display; robots visit it as a time-filler (or the island in DIFF_FINAL).

### 4.3 Supervisor's office (`Aufsicht.cpp`) — non-economic effects

Fully specified in rooms-and-ui-6 §3. Gameplay hooks relevant to this file:
* Morning briefing lists the previous day's pranks and applies the exposure fine (§1.4). It cannot
  be skipped while an exposed prank, a game-over condition (image < −990 or money below the
  game-over debt), a mission win or a takeover is pending.
* On leaving the morning briefing: the prank list is cleared, then all pending person/special
  pranks are executed (§1.3) and appended for the next briefing; bosses are placed at their start
  positions and walk to their offices (unless an office bomb redirects the victim).
* The bulletin board in this room holds the postcard world item (§3.3).

### 4.4 Other rooms touched by this file (pointers)

* Arab Air counter: kerosene trading is economy (economy.md); here only MG trust and the glove.
* Workshop: maintenance modes/trust in rooms-and-ui-7 §4 and planes-and-flights; here only beer,
  oil (mechanic gives oil once trust ≥1 → using oil in the office globe room silences the squeaky
  globe, cosmetic) and the perfume/slime interaction.
* Rick's café: rumours only (no economic effect) + the drinker strike-ender (§3.3).

## 5. Open questions

1. Special prank 6 (route theft) appears unreachable (trust never exceeds 5). Confirm in the real
   game; decide whether OpenAT keeps it hidden. Even if reached, the route is only removed, never
   transferred (byte vs −1 comparison).
2. Plane prank firing with no next flight: does it really re-fire hourly (statistics inflation) in
   the shipped binary? Needs an in-game test.
3. Stink bomb / stink perfume: exact avoidance behaviour of passengers is in the people simulation
   (airport-and-people.md) — confirm the floor-cell flag meaning (bit 1 = "avoid").
4. Office power cut by pliers: only a dark overlay was found; check whether planner interaction is
   blocked in practice.
5. Fine exposure uses hints at briefing time, after the overnight −3 decay; confirm ordering of
   NewDay vs briefing for plane pranks that pushed hints to exactly 100–102.
6. Gate security levels 2/3 cost more but block nothing extra and only delay boarding passengers
   (240 ticks each) — confirm the boarding delay never causes missed flights.
7. Rocket price/bit mismatch for robots (§4.1) — intentional or bug? Totals equal, order differs.
8. Flat-tyre (plane 3) delay statistic is booked to the saboteur rather than the victim — likely an
   original bug; OpenAT should decide (fidelity vs. fix).
9. Network: special-prank price broadcast uses the wrong table (§1.8).
