# Rooms & UI 3: the dialog system (talking to characters)

Clean-room behaviour spec. Everything here is written in our own words. Text is referred to only as
`resource file / group / id`. The groups are 4-character tokens inside `misc/dlg_<lang>.res` (for example
`Bank`, `Boss`, `Jobs`). The per-partner dialog graphs are in
[`rooms-and-ui-3b-dialog-trees.md`](rooms-and-ui-3b-dialog-trees.md).

Traceability notation: `File.cpp: Function`. Confidence: **H** = read directly in source, **M** = inferred
from several places, **L** = guess or unverified.

> **Widescreen:** the original lays everything out for a 640x480 screen with a 640x440 room area and a
> 40 px status bar at y=440. Every coordinate below is in that space unless stated otherwise. Items
> tagged **[WS]** are 640-px assumptions that OpenAT has to re-anchor (see §9).

---

## 1. Concepts and vocabulary

| Term | Meaning |
|---|---|
| **Dialog partner** (talker) | One of the `TALKER_*` ids (§2). There is at most one per room window at a time. |
| **Medium** | `MEDIUM_AIR`=0: the player is in the room. `MEDIUM_HANDY`=1: a phone call (office phone or mobile). Many nodes branch on this. |
| **Text group** | A 4-char token (`Bank`, `Boss`, ...) that selects the group in `dlg_<lang>.res`. Each partner has a fixed group (§2). |
| **Node / text id** | A numeric sub-id inside the group. A node is either a **partner line** (one speech bubble from the character) or a **choice list** (a range `from..to` of player options). |
| **Player line** | A choice list where `from == to`: one option, shown in the player's bubble. Clicking it "says" it. |
| **Number window** | A separate small card that shows exact figures (money, counts, percentages) while the partner says the line. Its text lives in the same group under id `999xxxx` (7 digits starting with 999). These entries have no voice. |
| **Re-entry** | When a dialog node opens a full-screen menu (buy plane, bank amount, etc.), it stores a re-entry node id. When the menu closes, the partner says that node and the dialog continues. |

Example: the bank clerk says `Bank/900`, the player gets the choices `Bank/1000..1005`, and clicking
`Bank/1000` makes the clerk say `Bank/2000`.

---

## 2. Dialog partners (`TALKER_*`)

`Talkers` is a fixed array with one `CTalker` per id (`TALKER_ANZ`=27). Rooms add a reference to the
talkers they show when they open (`IncreaseReference`).

| Id | Constant | Room / character | Text group | Voice dir (`VOICE/`) | Partner animation object (room file) | Clip prefix (smk) |
|---|---|---|---|---|---|---|
| 0,2,4,6 | `TALKER_PERSONAL1a..4a` | Personnel office of player 1..4, hiring clerk (left desk) | `Jobs` | `PE` | `SP_Frau` (Personal.cpp) | `PF*` (PFRede = talk, PFTurnW/PFTurnK = listen) |
| 1,3,5,7 | `TALKER_PERSONAL1b..4b` | Personnel office, staff/salary clerk | `Jobs` | `PE` | `SP_Mann` (Personal.cpp) | `PM*` (PMReden talk, PMWait/PMWaitK listen) |
| 8 | `TALKER_BANKER1` | Bank, male clerk (shares) | `Bank` | `BA` | `SP_Mann` (Bank.cpp) | `BM_*` (BM_Rede/BM_Taste talk, BM_RedeW/BM_RedeK listen) |
| 9 | `TALKER_BANKER2` | Bank, female clerk (loans) | `Bank` | `B2` | `SP_Frau` (Bank.cpp) | `BF_*` |
| 10 | `TALKER_MUSEUM` | Museum (used planes) attendant | `Muse` | `MU` | `SP_Mann` (Museum.cpp) | `MU_*` |
| 11 | `TALKER_ARAB` | Arab Air (kerosene) | `Arab` | `AA` | `SP_Araber` (ArabAir.cpp) | `Arab*` (ArabP/ArabS talk, ArabL listen) |
| 12 | `TALKER_BOSS` | Airport boss (Aufsicht) | `Boss` | `BO` | `SP_Boss` (Aufsicht.cpp) | `bb_*` (many talk variants, look-left/right/turn) |
| 13 | `TALKER_MAKLER` | Plane broker | `Makl` | `MA` | `SP_Makler` (Makler.cpp) | `M_*` |
| 14 | `TALKER_MECHANIKER` | Workshop mechanic | `Mech` | `ME` | `SP_Mann` (Werkstat.cpp) | `WK_*` (WK_Rauch = smoking, listen mood) |
| 15 | `TALKER_WERBUNG` | Advertising agency | `Werb` | `WE` | `SP_Mann` (Werbung.cpp) | `WA_*` |
| 16 | `TALKER_DUTYFREE` | Duty-free shop assistant | `Duty` | `DU` | `SP_Frau` (DutyFree.cpp) | `Du*` |
| 17 | `TALKER_SABOTAGE` | Saboteur in the Arab Air back room | `Sabo` | `SA` | `SP_Araber` (sabotage.cpp) | `s_*` / `S_*` |
| 18 | `TALKER_NASA` | NASA / space shop clerk | `Nasa` | `NA` | `SP_Mann` (Nasa.cpp) | `LE_*` |
| 19 | `TALKER_WORLD` | Phone call to a branch office abroad (female voice) | `Welt` | `WO` | none (a `CWorld` window is created as the phone picture) | – |
| 20 | `TALKER_COMPETITOR` | Another player (AI or network human) | `Play` | `P1..P4` (sub-dir `PL`) | none: the partner's **portrait** is drawn mirrored | – |
| 21 | `TALKER_KIOSK` | Newspaper kiosk owner | `Kios` | `KI` | none assigned (the kiosk's `SP_Mann` keeps reading) | – |
| 22 | `TALKER_RICK` | Rick's Cafe bar keeper | `Rick` | `RI` | `SP_Rick` (Ricks.cpp) | `Bar_*` |
| 23 | `TALKER_TRINKER` | Regular drinker at Rick's | `Rick` | `TR` | `SP_TrinkerMund` (mouth overlay only) | `Mund*` |
| 24 | `TALKER_FRACHT` | Freight-room worker | `Frac` | `FR` | `SP_Fracht` (Fracht.cpp) | `FM*` |
| 25 | `TALKER_SECURITY` | Security office | `Secu` | `SE` | `SP_Secman` (Security.cpp) | `SO_*` |
| 26 | `TALKER_DESIGNER` | Plane designer | `Desi` | `DE` | `SP_Stan` (Designer.cpp) | `DeMan*` |
| 998 | `TALKER_PASSENGER` | Passenger at the airport | `Pass` (**group missing from the shipped res**) | – | – | – |
| 999 | `TALKER_PLAYER` | Marker only | – | – | – | – |

There are no dialog partners for the travel agency, Last Minute, or the Lufthansa/ATFS rooms. Those rooms
use menus only. **H** (defines.h list; StdRaum.cpp: StartDialog).

Every room sets a `DefaultDialogPartner`. This is the character who answers when the player uses an
inventory item in that room (§7). **H** (room constructors).

### 2.1 Bubble anchor per partner (room coordinates) **[WS]**

`BubblePos` is the tip of the bubble's tail. `BubbleStyle` gives where the bubble sits relative to it:
0 = bubble below the tail, 1 = bubble to the left with the tail pointing right, 2 = bubble above the tail
(the default), 3 = bubble to the right. *Phone* means the values used when the room is shown as a phone
inset; `HandyOffset` is the x of the 340-px-wide strip cut out of the partner's room picture (§5.3).
**H** (StdRaum.cpp: StartDialog).

| Partner | In room: pos / style | Phone: pos / style / HandyOffset |
|---|---|---|
| Freight | (556,126) / 1 | – |
| Security | (328,100) / 2 | – |
| Designer | (268,126) / 0 | – |
| Arab Air | (486,183) / 1 | (526,183) / 1 / 160 |
| Saboteur | (396,198) / 1 | (400,188) / 1 / 367 |
| Banker 1 (male) | (477,157) / 2 | (479,157) / 2 / 367 |
| Banker 2 (female) | (240,147) / 2 | (467,202) / 1 / 153 |
| Boss | (400,102) / 2 | (470,158) / 1 / 160 |
| Broker | (540,200) / 2 | (540,200) / 2 / 378 |
| Mechanic | (200,203) / 2 | (445,199) / 2 / 131 |
| Museum | (440,234) / 2 | (440,234) / 2 / 367 |
| Personnel a | (443,208) / 1 | (535,207) / 1 / 320, then overridden to 280 when dialled |
| Personnel b | (280,144) / 3 | (435,124) / 1 / 160, then overridden to 120 |
| Advertising | (400,250) / 2 | (526,300) / 1 / 160 |
| NASA | (394,114) / 2 | (350,292) / 1 / 160 |
| Duty free | (396,128) / 1 | (396,198) / 1 / 160 |
| Kiosk | (288,192) / 2 | – |
| Rick | (220,105) / 2 | – |
| Drinker | (480,113) / 2 | – |
| Competitor | (640 − portraitOffset.x, 380) / 1 | same |
| Passenger | (Par2, Par3) / 2 | – |
| World | copied from the `CWorld` phone window | – |

---

## 3. Dialog state and the line/choice machinery

### 3.1 State held by the room window (`CStdRaum`) **H**

| Field | Purpose |
|---|---|
| `DialogPartner` | Current `TALKER_*`, or `TALKER_NONE` (−1) when no dialog is running. |
| `DialogMedium` | AIR or HANDY. |
| `DialogPar1..3` | Free parameters. They come from `StartDialog` and are reused by the nodes as scratch space: chosen player, chosen tank, share amount, plane index, and so on. The default is −1. |
| `CurrentTextGroupId` | The group token currently shown. 0 means no bubble. |
| `CurrentTextSubIdVon/Bis` | The id range. `Bis==0` means a single partner line. `Von==Bis` means a single player line. `Von<Bis` means a choice list. |
| `TextAlign` | 0 = partner bubble (top), 1 = player bubble (bottom-left, near the player's portrait). |
| `Optionen[0..9]` / `OrgOptionen[0..9]` | Display text without and with the embedded voice markers. A list has at most 10 options. |
| `CurrentHighlight` | The id under the mouse in a choice list, or 0. |
| `pSmackerPartner` | The partner's animated character (§4), or null. |
| `SmackerTimeToTalk` | Wall-clock deadline for "the partner is still speaking" when no voice is playing. |
| `TalkingSpeechFx`, `SpeechFx`, `ReadyToStartSpeechFx` | Voice state (§5). |
| `DisplayThisBubble` | Whether the bubble is drawn at all (§5.4). |
| `TimeBubbleDisplayed` | Wall-clock time when the current bubble appeared (used by the AI-competitor timeout). |
| `CanCancelEmpty` | Allows the click-through guard to be skipped (§3.4). |
| `MenuDialogReEntryA/B` | The group and node id to resume at after a menu (§3.6). |
| `TalkedToA/B` | Per-room-visit flags ("we already greeted"). They select a shorter greeting on the second talk in the same visit. |
| `UsingHandy`, `Ferngespraech`, `LetzteEinheit`, `PayingForCall` | Phone-cost state (§6). |

Global flags in `Sim.DialogOvertureFlags` remember first meetings **across the whole game**: `WERBUNG`=1,
`MUSEUM`=2, `MUSEUM2`=4, `NASA`=8, `MAKLER`=16, `RICK`=32, `RICK_TODAY`=64. All of them are cleared at
game start. `RICK_TODAY` is cleared every new day. `MUSEUM2` is set when the museum window closes if
`MUSEUM` was already set, so it means "visited the museum before this visit". **H** (Sim.cpp,
Museum.cpp destructor).

### 3.2 Showing a line **H** (StdRaum.cpp: MakeSayWindow ×3, RepaintText)

There are three entry points:

1. **Partner/player line from the resource:** group, id, and printf-style arguments. The text is
   formatted (airline names, numbers with thousands dots, city names) and stored. The current group
   becomes that group.
2. **Composed line:** the caller builds a string itself (for example the boss's daily review, which
   concatenates fragments) and passes an arbitrary id. That id is used **only** for branching on the next
   click, so it is a "virtual node" such as `12040` or `6001`. The group stays the last one used.
3. **Choice list:** group, `from`, `to`, and an argument mode. The modes are: the same arguments for every
   option; one 4-byte argument per option; or two arguments per option (used by share lists of
   "airline, count"). The options are stored in order `from..to`.

What happens on display:

* Any voice still playing is stopped.
* The text is laid out into an off-screen bitmap the size of the room (640x440) **[WS]**.
* **Partner line** (`Bis==0`): the partner's desired mood is set to TALKING. The talk deadline is set to
  `now + 100 ms × number of characters`. The bubble is drawn according to `BubbleStyle` around
  `BubblePos`. Text wraps inside x≈80..560 for styles 0/2. For style 1 the bubble spans from x=5 (or x=80
  when talking to a competitor) to `BubblePos.x−10`. For style 3 it spans from `BubblePos.x+10` to x=630.
  **[WS]**
* **Player lines / choices** (`TextAlign`=1): the options are stacked vertically with a fixed gap
  (`LINE_DIST`). Each option is preceded by a bullet glyph when there is more than one. The hovered
  option uses the highlight font (`FontDialogLight`); the others use `FontDialog`. The partner's lines use
  `FontDialogPartner`. The bubble is anchored at the player's portrait mouth point
  `BeraterSprechblasenOffset[12+player]`, which is (73,377), (73,389), (82,385), (82,385) for players 1..4
  **[WS]**. The bubble is vertically centred on that point if it fits, otherwise it is pushed up so that
  its bottom is at y=430. Its right edge is at x=630, or x=560 when the partner is a competitor (to leave
  room for their portrait). In the player's own office a different anchor is used:
  `BigPlayerOffset` = (210,235) for player 1 and (220,240) for players 2..4. While a choice list is
  shown, the partner's desired mood is LISTENING and the talk deadline is cleared.
* The number window (`MakeNumberWindow`) is a card built from template `NUMBERS` in `glnumber.gli`. It
  grows in 10 px steps (aspect 3:2) until the text fits, gets a 10 px margin, and is centred at x=320
  with its bottom at y=439 **[WS]**. Spaces are turned into non-breaking spaces. It is cleared whenever
  the text window closes.

**Hit testing:** while a dialog is open and no menu is shown, the whole screen is a "hot" click area.
For a single line (partner or player), a click anywhere advances. For a choice list, only a click on a
highlighted option counts. The highlight is found by vertical position inside the stacked options, and
moving onto a new option plays `change.raw`. **H** (StdRaum.cpp: InitToolTips, CheckHighlight).

### 3.3 How a click maps to the next node **H** (Dialog.cpp: CStdRaum::PreLButtonDown)

Clicking a bubble produces the **id of what was clicked**:

* For a single line, the id is the line's own id.
* For a list, it is the hovered option's id.

That id is fed into a big per-partner switch. The switch decides the next node: it either shows another
partner line, shows a choice list, opens a menu, applies a game effect, or ends the dialog. The rules
are:

* **"Goto" semantics:** a node id is the event "this text was clicked". The handler for `X` shows the
  next text `Y`, and the next click delivers `Y`. Several ids can share one handler. For example, every
  "back to main menu" id points to the same choice list.
* **Default = end:** any id that has no case in the partner's switch calls `StopDialog`. Farewell options
  are simply ids without a case. Terminal partner lines ("goodbye", "you can't afford it") are the same.
* **Id arithmetic:** handlers compute ids directly. Examples: `2050 + n` (n-th sabotage act),
  `4000 + MechMode`, `5000 + id − 4000`, `1000 + Difficulty × 100`, `2503 + Image/251`, and
  `1050 + min(5, trust)` as the upper bound of a list. This means **the list length depends on game
  state**: options beyond the bound simply aren't offered.
* **Echo step:** when the player picks from a list of more than one option *and* speech is on (or it is
  a network dialog), the chosen option is first shown again as a single player bubble with its own voice
  sample. The handler only runs when that echo is clicked or auto-advances. Without speech the handler
  runs immediately. In network dialogs the echo is also sent to the other side (§8).
* **Choice lists of one** (`from == to`) are "player says X" lines. They are used whenever the player
  must speak before the partner answers (first meetings, the world phone, Rick, and so on).

### 3.4 Click guards **H** (PreLButtonDown)

A click is ignored when any of these is true:

* The phone inset is active: clicks are queued and replayed later.
* A menu is open: the click goes to the menu.
* The partner has an animation, the current bubble is a partner line, the partner has not yet reached
  its TALKING mood, and the talk deadline has not passed. This stops the player clicking a line away
  before the character starts speaking. **Exception:** when `CanCancelEmpty` is set. The boss's
  mission-report summary lines set it, because their voice may be empty.
* The same id was clicked within the last 1000 ms (double-click guard).
* The game-over menu was opened less than 500 ms ago.
* A bubble is still being painted: painting is cancelled and the click is dropped.
* In a network dialog, there is no bubble on screen (the other human is still choosing).

### 3.5 Ending a dialog **H** (StdRaum.cpp: StopDialog)

`StopDialog` runs when the default case is reached, on a right-click anywhere (`OnRButtonDown` ends any
dialog), or when an explicit handler calls it. It:

* Stops the voice.
* Clears the phone flags.
* For a phone call, decrements `Sim.RoomBusy[room]` for the partner's room.
* For a competitor, clears `IsTalking` on both players and broadcasts unlock/end messages.
* Ends the player's portrait-dialog state, sets the partner's mood to IDLE, and tells the talker it
  stopped (§4.1).
* Closes the text and number windows.

There are side effects for some partners:

* **NASA:** if the last line was `Nasa/9999` (the farewell on the first exit), the player leaves the
  room.
* **Passenger:** clears the `PERSON_TALKING` flag.
* **Personnel partners:** remaps workers (`MapWorkerOverflow`).
* **World:** if the last menu was the foreign-orders menu, the city is marked as called.
* **Phone:** the inset window is told to slide out (§5.3).

Right-click in a **World** call returns to the branch list instead of ending. The boss room has its own
right-click handler (§3.8).

### 3.6 Menus inside dialogs and re-entry **H** (StdRaum.cpp: MenuStart, MenuStop, MenuRightClick)

A node can open a menu: buy plane, sell plane, bank amount, shares, kerosene, sabotage target, ad route,
personnel lists, security, designer planes, repairs, foreign orders, chat, game over. Before opening it
the handler usually sets `MenuDialogReEntryB` to a node id. While the menu is open:

* The voice stops.
* If a re-entry is pending (or the menu is "buy plane" / "buy designer plane"), the partner stays in
  LISTENING. Otherwise the partner goes IDLE and the player's dialog state ends.

On `MenuStop`, if `MenuDialogReEntryB != −1`, the player has not called it a day, and it is before 18:00,
then the partner says `group(MenuDialogReEntryA) / MenuDialogReEntryB` and the player's dialog state
resumes. `MenuDialogReEntryA` is the group that was active when the text window was closed for the menu.
If there is no re-entry, the dialog quietly ends: the partner goes IDLE, and `DialogPartner` is cleared
without `StopDialog`.

Cancelling a menu with right-click changes the re-entry:

| Menu | Effect on re-entry |
|---|---|
| Shares, with re-entry 2030 or 2130 | Incremented to the "cancelled" variant (2031 or 2131). |
| Sabotage plane/route, re-entry not 1090 or 1091 | Incremented (1070 → 1071). |
| Designer planes | Cleared (dialog ends). |
| Foreign orders, city not yet called | Re-entry 5000. |
| Foreign orders, city already called | Dialog stopped, branch list reopened. |

Some menus do not use re-entry. Instead they start a fresh dialog with a result node, for example
`StartDialog(TALKER_MAKLER, AIR, 130+rand4, planeType)` after a plane is picked. See the trees.

### 3.7 Opening a dialog **H** (StdRaum.cpp: StartDialog)

`StartDialog(partner, medium, par1=−1, par2=−1, par3=−1)`:

1. Stores the parameters.
2. If the player's walking figure was in a special "look" state, resets it.
3. Calls the talker's start (§4.1) and the player-portrait start (§4.3). In the player's office, the
   player figure's desired mood becomes LISTENING.
4. Per partner: picks the font, `pSmackerPartner`, and `BubblePos/Style`, sets the partner to LISTENING,
   and shows an **opening node chosen from `par1`**. The convention is:

   | `par1` value | Opening |
   |---|---|
   | −1 | Default greeting. |
   | 10000 + item | Reaction to being shown an inventory item (§7). |
   | Small values | Special entries, for example 20 = "money found in the bank bin", 30 = "demo/protection ended". |
   | A literal node id | Start straight at that node (re-entry style, for example `Sabo/1070`). |

   For a phone call, `Sim.RoomBusy[room]` is incremented and the partner animation is taken from the
   off-screen phone window (`DialogWin`).

The callers are: room click handlers (MouseClickId 10/11 usually = the character), the inventory item
handler (`StdRaum.cpp: OnLButtonDown` item switch), the phone book (§6), menu results, simulation events
(strike-ended call, sabotage-done call, an AI player calling, the morning briefing), and network
messages. The full list is per partner in the trees file.

### 3.8 Boss-room specifics **H** (Aufsicht.cpp: ctor, PostPaint, OnRButtonDown)

* **Morning briefing at 09:00** (`DayState==1`):
  * On day 0, or when the "short briefing" option is off, the boss dialog starts with `par1=1`.
    `par1=30` is used instead if copy protection failed.
  * Otherwise the briefing menu is shown first, and right-clicking it starts the dialog.
  * The local player's portrait is hidden (`DontDisplayPlayer`).
* **Auto-advance** while the clock stands at 09:00:
  * With speech off (talking × digital sound = 0), the dialog auto-clicks after 40 s of no clicks and
    then every 10 s (the timer is reset to "now − 30 s").
  * In test/cheat mode it auto right-clicks after 5 s.
* **Boss look direction:** a few nodes write decision variables (`ExitFromLeft/Middle/Right`). These
  force the boss animation to turn toward players 1–2 or 3–4, point a finger, or glare at everyone.
  `SP_Player[n].SetDesiredMood(SPM_HOLY)` makes the accused player's figure look innocent during the
  sabotage report.

---

## 4. Character animation

### 4.1 `CTalker` – the logical "is busy" state **H** (Talker.cpp)

The original drew talker bitmaps here. That code is commented out, so what remains is bookkeeping.

| State | Meaning | Transition |
|---|---|---|
| 0 | Free | `StartDialog(direct)` → 1; `StartDialog(phone)` → 2 with phase 0 |
| 1 | In a face-to-face dialog | Phase toggles 0/1 every pump. `StopDialog` → 0 |
| 2 | Picking up the phone | Phase increments each pump. At phase 4 → state 3 |
| 3 | On the phone | Phase toggles 0/1. `StopDialog` → 4 with phase 0 |
| 4 | Hanging up | At phase 4 → 0 |

* `IsBusy` is true when the state is not 0 or 4, **or** `Locking > 0`.
* The simulation locks a room's talker(s) while any player is inside that room (Sim.cpp, around line
  3700; AtNet.cpp for network). As a result, phoning a character is "busy" while someone stands in front
  of them or is already talking to them.
* `NumRef` is a reference count for rooms that use the talker.
* State, Talking, Phase, NumRef, Locking and StatePar are saved with the game.

### 4.2 `CSmackerPerson` – the clip/mood engine **H** (Smackprs.cpp)

Each on-screen character is a set of numbered **clips**. Each clip is one `.smk` file from the `video/`
path, plus an optional sound. Static properties per clip:

| Property | Meaning |
|---|---|
| `ClipId` | Its index. |
| File, sound file | The sound may be a list separated by `\|` ("syllables"). |
| Screen offset | Room coordinates **[WS]**. |
| **Mood id** | One of IDLE=1, HOLY=2, TALKING=3, ANGRY_TALKING=4, LISTENING=5, LETTER=6. |
| Repeat (min,max) | The clip plays a random number of times in that range. |
| Post-wait (min,max) | Hold the last frame for a random time in units of 50 ms. |
| Can-cancel | Whether the clip may be cut off when the desired mood changes. |
| Variable hook | Pointer, operation (add/sub/set/xor) and value. Fires at clip start (`PRE`), at clip end (`POST`), or when a given frame is reached (`FRAME`, frame number = op >> 13). |
| Decision variable | Optional. When its value is ≠ −1 it forces the next clip id. |
| Successor list | Pairs of (kind letter, weight digit 0–9) with a successor clip id. Kind: `A` = always allowed, `S` = only while staying in the same mood, `E` = only as an exit when changing mood. |

The person has a current mood and a desired mood. A desired mood can also name up to two **accepted
intermediate moods**.

**Pump each frame:**

1. If a clip has no file (an "empty" clip), skip straight to the next clip.
2. On the very first pump, start clip 0. Its mood becomes both current and desired if they are unset.
3. If the active clip is cancellable and (the desired mood differs from the current mood **or** the
   decision variable is set), stop it now and choose the next clip.
4. While playing: when the Smacker timer allows, decode the next frame and run any FRAME hook. At the
   last frame, decrement the repeat counter; it is forced to 0 if the moods differ. When the clip
   repeats in a non-talking mood, its sound is replayed.
5. When the repeat counter reaches 0: if post-wait max > 0, hold the last frame for the random wait.
   Otherwise go to the next clip. If the mood is changing, or the clip is not a talking clip, its sound
   is stopped first.
6. Syllable sounds: while a clip with several syllables plays, the next random syllable starts when the
   previous one ends. The duration is byte length × 1000 / 22000 ms. The choice avoids the last 3 picks
   when there are 3 or more syllables. The volume is the effects volume.

**Choosing the next clip** (`NextClip`):

1. Run the POST hook of the clip that is ending.
2. If the decision variable is set (≠ −1), jump to that clip.
3. **Staying in the same mood:** weighted random choice among successors whose mood equals the desired
   mood and whose kind is not `E`. If there are none, take a weighted random choice among all
   successors.
4. **Changing mood**, trying in order:
   1. `E` successors whose mood equals the desired mood.
   2. `E` successors whose mood is in {desired, current, accepted1, accepted2}.
   3. Non-`S` successors whose mood equals the desired mood.
   4. Non-`S` successors whose mood is in the same set.
   5. Any successor.
5. Entering a talking clip from a non-talking one starts the optional looping "speak murmur" sound
   (`SetSpeakFx`). Leaving talking stops it. No shipped room enables it (the only call is commented out).
6. Run the PRE hook of the new clip. If the new clip is empty and has no wait, recurse immediately.

`ForceNextClip` stops the active clip immediately (used to make the boss turn and glare). `GetFrame`
returns the current frame number.

So a character is a weighted graph of clips. "Talking" and "listening" are sets of clips, and the
transition clips in between (turn to the customer, turn back) are `E`-tagged successors. For example,
an idle-work clip has an `E` exit to "turn", "turn" leads to "talk", and "talk" has an `E` exit to
"turn back".

### 4.3 How the dialog drives the partner's moods **H** (StdRaum.cpp: RepaintText, PostPaint)

| Dialog event | Partner desired mood |
|---|---|
| `StartDialog` | LISTENING (the character turns toward the player) |
| Partner line shown | TALKING. The bubble and number card are **not drawn** until the partner actually reaches TALKING or the talk deadline passes, so the text appears in sync with the mouth starting. |
| While a partner line is shown | It falls back to LISTENING when there is no voice and the deadline has passed, or the voice is playing but "mouth closed" (below), or the bubble is a player bubble. It goes back to TALKING when the voice says "mouth open" again. This alternation **is the lip-sync**. |
| Player list or player line shown | LISTENING |
| Menu with re-entry | LISTENING; otherwise IDLE |
| `StopDialog` | IDLE |

"Mouth open" (`FX::IsMouthOpen(t)`, as implemented in the SDL port, SSE.cpp) looks *t* ms ahead of the
current playback position in the 16-bit sample buffer. It checks 10 samples spread over roughly the next
1000 samples (about 45 ms at 22.05 kHz) and reports "open" if any absolute value exceeds 512. Look-ahead
values: 400 ms for the partner animation, 200 ms for portraits.

While a voice sample plays, the talk deadline is extended every frame to `now + 100 ms × len`. When the
sample stops, the deadline is set to "now".

**The player's side:** when a dialog starts, the player's portrait (advisor bitmap set 12+player) slides
up from below the room at 9 px per pump. The final y is `440 − BeraterSlideY[12+player (+4 on phone)]`,
with slide heights 115, 115, 120, 106 normally and 114, 122, 120, 111 when holding a phone. Any advisor
currently on screen slides out first. While the player "talks", the portrait cycles mouth frames in the
order 0,2,1,3,2,3,0,1,2, advancing every 3 talk-phase ticks; frames +4 are the phone-holding variant. If
voice is playing, the mouth frame is forced closed whenever the sample is "mouth closed" (200 ms
look-ahead). In the player's **own office** no portrait is drawn. The big office figure `SP_Player` is
set to TALKING/LISTENING instead. **H** (Misc.cpp: CMessages::Pump/StartDialog; StdRaum.cpp: PostPaint)

**Competitor partner:** that player's portrait is drawn mirrored at the bottom-right, at
x = 640 − portrait width, with the same slide table. It shows mouth frames from the same sequence,
driven by wall-clock time (one step per 150 ms) while the competitor's line is "talking". When the other
human is choosing and no bubble exists, an empty thought bubble (`MoodPersonEmpty`) is drawn above the
portrait. **[WS]** **H**

### 4.4 Per-room clip tables (summary)

Clip index = mood, at the room offset. The full successor graphs belong to each room's own spec. Only
the dialog-relevant moods are listed here. **H** (room constructors)

* **Arab Air** (425,108): 0 ArabW idle, 1 ArabA idle, 2 ArabL listen (post-wait 2.5 s), 3 ArabP talk,
  4 ArabS talk, 5 ArabZ idle (with sound), and a variant clip 0 ArabG idle.
* **Boss** (344,105):
  * Idle/listen: 0 bb_wait (idle, or listen in a variant).
  * Talking: 1 bb_base, 2 bb_fing (finger), 3 bb_hand, 4/5/6 bb_left/leftR/leftZ (look left / return),
    7/8/9 bb_righ*, 10 bb_turnR.
  * Idle turns: 12–16 (left, turnR, turnL, turnR, righZ).
* **Bank male** (430,160): 0 Wait, 1 Scrib, 2 Turn, 7 TurnZ (idle); 3 RedeW, 8 RedeK (listen);
  4/6 Rede, 5 Taste (talk).
* **Bank female** (252,150): 0 Wait, 1 Work, 2 Turn, 9 TurnZ (idle); 3 RedeW, 5 Taste, 7 RedeK (listen);
  4 Rede (talk).
* **Designer** (198,18): DeManW idle/listen, DeManS talk, plus idle extras Sh/Fl/Co.
* **Duty free** (310,47): Kau, Blase, Nagel* (idle); Rede (talk); RedeW, RedeK (listen).
* **Freight** (485,50): FM1a/FM2/FM3/FM4 (idle); FM4SPaS1–3 (listen); FMSPa1–4 (talk); FM4back (idle).
* **Broker** (470,218): Wait, Dreh, Druc, Turn, back (idle); Rede, RedeB (talk); List, ListK (listen).
* **Museum** (420,250): PutzA/B, Turn, TurnZ (idle); RedeA/B (talk); RedeW, Guck, GuckZ, Hit (listen;
  one RedeW variant holds 18.5 s).
* **NASA** (210,140): Wait, WaitK, lock, strck*, turn, TurnZ (idle); RedeW, RedeK, taste (listen);
  RedeA/B/C and a RedeW variant (talk).
* **Personnel woman** (340,113): Brief, Lese*, Werf, Wild*, Turn, TurnZ (idle); Rede (talk);
  TurnW, TurnK (listen).
* **Personnel man** (130,80): Lese, Blat, Korb*, Turn, TurnZ (idle); Reden (talk); Wait, WaitK (listen).
* **Rick** (157,130): Wait, Aug (idle and listen variants); Rede, Fing, Arm (talk).
* **Drinker mouth** (483,230): Mund_W/Mund/MundA/MundAW/MundZ (idle), Mund (talk), Mund_W (listen).
* **Security** (240,79):
  * Idle: normal, zum_reden.
  * Listen: reden_mund_zu (post-wait 2.5 s).
  * Talk: reden, and **ArabP.smk** (it reuses an Arab Air clip; probably an original bug, see Q-6).
  * Idle extras: reden_zurueck, erschrecken, hindrehen, druecken, herdrehen, tasse_*.
* **Advertising** (290,240): copyC, copy, wait, Turn, TurnZ (idle); RedeA (talk); RedeW, RedeK (listen).
* **Workshop** (190,209): Wait, Tuch, Putz, TuchZ, Turn, TurnZ (idle); Rede (talk); RedeW, Rauch
  (listen).
* **Saboteur** (413,255): clic, clicB, turn, raus, wait, WaitK (idle); rede (talk, repeated 8 times);
  wait, left, rigt (listen).

---

## 5. Voice playback

### 5.1 Where voice comes from: embedded markers **H** (Sound.cpp: CreateSpeechSBFX, GetSpeechFilename, RemoveSpeechFilename)

Each text in `dlg_<lang>.res` carries its voice reference **inside the text**, as one or more
`[[...]]` markers. The display text has the markers removed (`RemoveSpeechFilename`, which also
collapses double spaces and trims). This means voice lookup is *not* derived from group or id. OpenAT
must parse the markers from the user's resource file. The marker forms are:

| Marker | Meaning |
|---|---|
| `[[XX\nnnn]]` | Play `VOICE/XX/nnnn.raw`. `XX` is the speaker directory (table in §2). Sub-parts use suffixes such as `905a`, `905b`, `905c`. |
| `[[P1\XX\nnnn]]` | A **player line**. `P1` is rewritten to the speaking player's directory, `P<n+1>`: the local player for player lines, `DialogPar1` for partner lines (the competitor). Files live in `VOICE/P<n>/<partnerDir>/nnnn.raw`, so each player has their own recorded lines per partner. |
| `[[XX\]]` (directory only) | Sets the current directory for later `[[*]]` tokens without playing anything. |
| `[[*]]` | **Dynamic word.** The text that follows the marker is matched, in this order, against: (1) the 4 airline names → `<dir>\name<k>.raw`; (2) all city names → `<dir>\<city code>.raw` (`KuerzelReal`, for example `ber`, `lon`, `NYc`); (3) plane type names → `<dir>\pl<n>b.raw`, or manufacturers → `<dir>\pl<n>h.raw`, where `n` is the type index; (4) numbers or money, which were meant to be synthesised but the call is disabled, so numbers are silent. `<dir>` is the directory part of the **previous** marker (up to its first backslash: `P1` for player lines, `BA` for `BA\905a`). |

All the parts are loaded as separate samples and concatenated (`Fusion`) into one sample, which is then
played at volume `OptionTalking × 100/7` %. Each part played is logged to the sound log.

**Missing file:** if a part loads with length 0, it is replaced with `none.raw` (silence), "No voice for:
…" is logged, and the any-missing flag is set. For the plain-string `MakeSayWindow` overload this sets
`TalkingSpeechFx = FALSE`. The line is then timed like a text-only line: no auto-advance, and the mouth
is driven by the text-length deadline. The list overload ignores missing parts and keeps
`TalkingSpeechFx = TRUE`. **H**/**M**

A line with no markers at all produces no voice. This applies to `999xxxx` number-window texts and to
the 17 unvoiced entries. In that case `TalkingSpeechFx` is FALSE.

### 5.2 When voice plays **H** (MakeSayWindow, PostPaint)

* Voice plays only if `OptionTalking × MasterVolume ≠ 0` and digital sound is on. Choice lists with more
  than one option are never voiced. Only single lines and echoes are.
* For a partner line with an animated partner, playback is **deferred**: it starts 3 frames after the
  partner has reached TALKING (`ReadyToStartSpeechFx` 3 → 0). Otherwise it plays at once.
* **Auto-advance:** when a single line's voice finishes (not playing and not waiting to start), the room
  sends itself a synthetic click, so the dialog moves on. With voice on, partner monologues therefore
  play through without clicks, and the game waits only on choice lists.
* Opening a menu or a new line stops the current voice.

### 5.3 Phone inset (visual) **H** (StdRaum.cpp: OnPaint, PostPaint)

For phone dialogs with a room-based partner, the partner's room is created off-screen with its "handy"
flag set (`DialogWin`). That room renders and animates normally but is not interactive. The current room
then draws a vertical strip of it: 340 px wide starting at `HandyOffset`, 440 px tall, at
x = `TempScreenScroll`. The strip's left edge is a slanted, saw-tooth cut: 9 steps, each 52 px tall and
13 px deeper, with the bar sprite `gDialogBarBm` drawn along it.

The inset slides in from x=800 to x=378 at about 333 px/s (1 px per 3 ms). On `StopDialog` it slides
back out to 800, and then the off-screen room is deleted. While it is shown, the airport view is clipped
at x=378 (`RightAirportClip`). **[WS]** (all of 378/800/340/440 are 640-space constants).

### 5.4 Subtitles and speech options

`DisplayThisBubble` controls whether the bubble is drawn. It is true when any of these holds:

* the "speech bubbles" option is on;
* talking volume × master volume is 0;
* digital sound is off;
* the text is a choice list.

So with speech on and bubbles off, partner and player lines are **heard but not shown**. Choices are
always shown. The number card is still drawn. **H**

Caveat (**M**): when a voice file is missing and bubbles are off, the bubble is still hidden while the
line no longer auto-advances. OpenAT should show the bubble whenever the voice failed (Q-3).

### 5.5 Number synthesis (`Synthese.cpp`) **H**

`SynthesizeNumber(target, speakerDir, number, currency)` builds German-style spoken numbers from a
**tokenised sample bank** `<speakerDir>numbers.raw`. That file is one raw sample with a 64-bit marker
value `0x80007FFF80007FFF` between its segments. The bank's segment index is the `SAY_D_*` table:

| Index | Segment |
|---|---|
| 0 | zero |
| 1–20 | stand-alone 1..20 |
| 20–27 | stand-alone tens 20..90 (20 is shared with the end of the 1..20 range) |
| 28 | the feminine "one" |
| 29–47 | prefix forms 1..19 |
| 48–55 | prefix tens 20..90 |
| 56–64 | "N-and-" units for 21, 22, ... |
| 65 | "and" |
| 66 | minus |
| 67/68 | hundred (final / prefix) |
| 69/70 | thousand (final / prefix) |
| 71/72 | million (final / prefix) |
| 73 | currency word |

How a number is assembled:

* If the "round numbers" option is on and |n| ≥ 1000, the number is rounded down to 2–3 significant
  digits.
* It is then split into millions, thousands and units.
* Hundreds use the prefix form when more follows or currency is spoken. Compound tens use the
  "unit-and" segment followed by the ten. The "final" forms are used at the end of the phrase.
* The currency word is appended when requested.
* The two most recently used speakers' banks are cached (2 slots, round-robin). `FlushTalkers` clears
  them.

**Actual use in the shipped game:** the call from dialog voice is commented out, so dialog numbers are
never spoken; that is why the number card exists. The only live caller is pressing **Space while the
pocket calculator is open**, which speaks the calculator value with the local player's voice
(`p<n>\numbers.raw` in the misc path) and a random choice of saying the currency or not. The GOG install
ships only `VOICE/P1/numbers.bak` and no `numbers.raw`, so this is silent in practice. OpenAT can skip
it, or implement it with locale-appropriate TTS. **H** (Gameframe.cpp: OnChar), **M** (install
listing).

### 5.6 Airport announcements (not dialog, same voice infrastructure) **H** (Sound.cpp: CVoiceScheduler; Planetyp.cpp; Takeoff.cpp)

`CVoiceScheduler` is a FIFO of sample names. Each name is played as `<name>.raw` from the sound path
when the previous one ends, at volume `OptionDurchsagen` × the global ambient volume squared. It plays
only while the clock is between 09:00 and 18:00 (the queue waits outside that time). It is **not** cleared
on room changes: announcements go on while the player is in a room, quieter through the room's global
ambient volume (airport-and-people.md §10.1; e.g. 40 → 16 %), and are muted in the evening screen and
the options (global 0). It is emptied when a game is loaded and when the game session ends.

* **Landing announcement:** a plane descends past a fixed height on the airport view between 09:00 and
  18:00, nobody has called it a day, nothing else is announcing, and the plane has a real origin city
  with a recorded city wave (automatic/transfer flights are never announced).
  The scheduler then queues `der<airline>flug`, the city's `Wave`, and `gelande2`. If it is another
  player's plane while the local player has a plane in the air, the announcement is suppressed.
* **Half-hourly notice:** during minute 30 of the even hours (10:30, 12:30, 14:30, 16:30), when the
  queue is empty, the figure is not warping, the local game speed is below 2 and the clock runs (no
  pause, no options, not call-it-a-day), the scheduler queues **one** sample: a local generator seeded
  with date + start time + hour picks the vehicle notice `Fahrzeug` with 1 in 10, otherwise `ZoneOK`
  when (hour / 2) is even and `Hinweis` when it is odd. The check runs every loop, so a sample that ends
  before the minute is over is queued again.

---

## 6. Phone calls

### 6.1 Placing a call **H** (StdRaum.cpp: MenuLeftClick MENU_FILOFAX / BRANCHLIST / PLAYERLIST)

There are two ways to open the phone book:

* Using the mobile item (`ITEM_HANDY`) outside the office sets `UsingHandy=1`.
* In the office, the desk phone puts the office figure into the phone pose (`KommVarTelefon`).

The phone book (`MENU_FILOFAX` page 2) lists rooms through `HandyRoomRemapper`. Selecting an entry
calls the corresponding room partner. It fails with **busy** (`busy.raw`) when any of these is true:

* the player is already queued for that room;
* the phone is "down" (sabotage, `TelephoneDown`);
* `Sim.RoomBusy[room]` is set (the personnel office is exempt);
* the talker `IsBusy` (§4.1);
* the room is closed:

  | Room | Closed when |
  |---|---|
  | Arab Air | before `timeArabOpen` (11:00) or on weekday 6 |
  | Museum | before 11:00 or on weekdays 5/6 |
  | Broker | after `timeMaklClose` (16:00) |
  | Advertising | before 12:00 or on weekdays 5/6 |

The times are from the SDL port (AirportView.cpp); the GOG build may differ (**M**).

When the call is accepted:

1. Any previous inset is deleted and a new off-screen room window is created.
2. `StartDialog(partner, MEDIUM_HANDY, ...)` runs.
3. One of `dial1.raw` / `dial2.raw` plays at random.
4. The call is recorded (`History.AddNewCall(UsingHandy + Ferngespraech×2)`) and **1 currency unit** is
   charged as a connection fee. The charge goes to statistic `STAT_A_SONSTIGES` and is broadcast in
   network games.

How each entry is called:

| Entry | Call |
|---|---|
| Bank | Two entries (by position): the second one calls `BANKER2`, otherwise `BANKER1`. The `BANKER2` talker is the one checked for busy in both cases (**H**, probably a bug). |
| Personnel | Two entries: a (hiring) or b (staff). |
| Mechanic | `par1=1` |
| Advertising | `par1=1` |
| NASA | `par1=1` |
| Arab Air | `par1=0` |
| Boss, museum, broker, both bankers | Default `par1` |
| "World" | Opens the **branch list**: cities where the player, or a cooperating partner, owns a branch (`RentCities`). Picking one starts `TALKER_WORLD` with `par1` = the owning player and `par2` = the city. |
| Players | Opens the **player list**: entries 0–3 = that player's office phone, 4–7 = their mobile. |

### 6.2 Call cost **H** (StdRaum.cpp: PostPaint)

While a dialog with an inset is open and `PayingForCall`, 1 unit is deducted every
*T* × 50 game-time ticks. Game time runs at 1000 ticks per game minute, so the period is
*T*/20 game minutes, with

`T = (2 − UsingHandy) × 10 + (3 − Ferngespraech) × 60`

| Call | Ferngespraech | T | Period (game minutes) |
|---|---|---|---|
| Office phone, local (rooms, players) | 0 | 200 | 10.0 |
| Mobile, local | 0 | 190 | 9.5 |
| Office, branch in home city | 1 | 140 | 7.0 |
| Mobile, branch in home city | 1 | 130 | 6.5 |
| Office, branch abroad | 2 | 80 | 4.0 |
| Mobile, branch abroad | 2 | 70 | 3.5 |

Each unit is logged with `History.AddCallCost(1)`, added to `STAT_A_SONSTIGES`, and broadcast.

Note: the long-distance level is decided by comparing a stale `DialogPar1` with the home airport id, not
the picked city (`MENU_BRANCHLIST` handler). In practice almost every branch call is level 2 (**M**, see
Q-4). Incoming calls are free for the receiver (`PayingForCall=FALSE`): AI-initiated calls,
network-initiated calls, the sabotage "job done" call, and the strike-ended call.

### 6.3 Incoming calls **H**

* **AI player calls the human:** happens during the AI's turn. A random target is picked, and the call
  is placed if `IsOkayToCallThisPlayer` holds:
  * time 09:00–17:59, nobody has called it a day;
  * the target has the mobile item and a location window, is not stuck, has the phone up, has no dialog
    or menu open, and is not in a special walk state.

  The AI then calls if it cooperates with the target but its sympathy is < −20 (to cancel), or does not
  cooperate, has sympathy > 50, and hasn't called yet (to propose). It must also be at its start-of-day
  action or own a mobile. Effects: `Phone.raw` plays, the target's game speed drops to 0, fast-forward is
  cancelled, and `StartDialog(TALKER_COMPETITOR, HANDY, ai, par2=1)` runs with `par2=1` meaning "the
  partner speaks first". (Player.cpp, around line 3210.)
* **Strike ended:** the staff clerk calls (`par1 = 1000 + StrikeEndType`). The call goes through the
  mobile, or is face-to-face if the player is in their personnel office. (Sim.cpp, around line 1430.)
* **Sabotage done:** the saboteur calls (`Sabo/2000`), or speaks directly if the player is in the
  saboteur's room. Game speed drops to 0. (Sim.cpp, around line 1760.)
* **Network human calls:** `ATNET_PHONE_DIAL`. The receiver accepts if no dialog or menu is open, the
  phone is not being picked up, the phone is not down, and the receiver is in their office (or the call
  went to their mobile). Otherwise it replies NOTHOME (`noanpure.raw`) or BUSY (`busypure.raw`). On
  accept, both sides drop to game speed 0 and both figures show the phone pose.

Calling an AI player gives these failure sounds:

| Situation | Sound |
|---|---|
| Our phone is down | `busy.raw` |
| Office line and the AI is not in its office, or it is stuck | `noanswer.raw` |
| The AI is in a dialog | `busy.raw` |
| The AI is "bored of player" | `noanswer.raw` |

Otherwise `StartDialog(TALKER_COMPETITOR, HANDY, other, par2=0)` runs (we speak first).

---

## 7. Showing inventory items to characters **H** (StdRaum.cpp: OnLButtonDown item switch)

Using an item in a room either triggers its **special effect** when the item belongs to that room, or
else starts `StartDialog(DefaultDialogPartner, AIR, 10000 + itemId)`. In the second case the partner
says `<group>/100xx`, a canned "what am I supposed to do with that" reaction, and the next click falls
through to the default case and ends the dialog. Every partner group contains ids 10003..10024 for this.
Item ids are: MG 3, beer 4, cigar 5, oil 6, postcard 7, pills 8, spider 9, dart 10, diskette 11, bra 12,
horseshoe 13, pralines 14, pralines-A 15, paper clip 16, glue 17, glove 18, energy drink 19, stink bomb
20, glowing coal 21, coal 22, pliers 23, perfume 24.

Note: the boss handles only 10003 and 10004 explicitly. Every other item reaction goes through the
boss's default branch, which also sets `DayState=2` and `bNoTime=FALSE` (harmless after the briefing;
**M**). In the kiosk, items are ignored entirely unless the newspaper headline is "interesting" (the
early `break`).

Special effects that open a dialog:

| Item | Room | Effect | Dialog |
|---|---|---|---|
| MG | Arab Air | `ArabTrust ≥ 1`, item consumed | `Arab` par1=1 → 1010 |
| Beer | Workshop | `MechTrust ≥ 1`, `MechAngry=0`, consumed | `Mech` par1=2 → 10000 |
| Postcard | Own personnel office | `SeligTrust`, consumed | `Jobs` (a) par1=300 |
| Spider | Saboteur | If `ArabTrust`: `SpiderTrust`, consumed | `Sabo` par1=3000 (always) |
| Dart | Advertising, only in Normal or harder, or Free game | `WerbungTrust`, consumed | `Werb` par1=8001 |
| Bra | Duty free | `DutyTrust`, consumed | `Duty` par1=801 |
| Horseshoe | Rick's | `TrinkerTrust`, consumed | Drinker par1=800 |
| Paper clip | Freight, glue not yet taken and not owned | Clip dropped, `ItemGlue=1` | `Frac` par1=1020 |
| Energy drink | Kiosk | `KioskTrust=1`, consumed | `Kios` par1=1010 |

Effects without a dialog: oil on the globe, pills curing sickness, diskette removing the laptop virus,
glue or stink bomb on the airport floors, glowing coal destroying offers in Last Minute or the travel
agency, pliers near security, perfume in the workshop. These belong to the item spec.

---

## 8. Talking to other players (`TALKER_COMPETITOR`) **H** (Dialog.cpp, StdRaum.cpp, AtNet.cpp, Person.cpp)

**Face-to-face start:** the player walks into another player's figure on the airport floor. When the
approach completes (`PlayerDialogState==-1`), the dialog starts with `par1` = the other player and
`par2` = 1 if the initiator is not human-local (meaning the partner speaks first). With a network human,
the initiator first sends `DIALOG_REQUEST`. The other side accepts only if it is on the airport floor,
not stuck, and has no menu or dialog open. It then stops walking, turns, and replies `DIALOG_YES` (or
`DIALOG_NO`), followed by `DIALOG_START` with positions.

**Turn parity:** `DialogPar2` toggles on every exchange. When it is 1 after toggling, it is *our* turn
and we get a choice list. When it is 0, the partner speaks:

* **AI partner:** the line is computed locally (sympathy, money, and so on).
* **Network human:** we send `DIALOG_NEXT` and wait. Their pick arrives as `DIALOG_SAY(id)` (which
  injects a click with that id into our state machine) and `DIALOG_TEXT(align, id, text)` (which shows
  their line on our side with the bubble alignment inverted).

Both sides therefore run the same switch in lockstep.

**Timeouts:** with an AI partner, if a bubble has been visible for more than 15 s, the AI acts:

* On a single line it clicks to continue.
* On our choice list it says `Play/2499` (bored) and sets `BoredOfPlayer` on itself. Later calls to it
  get no answer.
* If `Play/2499` is already showing and still not answered, the dialog ends.

**Opening:** the opening line is `Play/1000` (AIR) or `Play/1001` (phone). It is said by the partner when
`par2=0` (formatted with their airline), or by us as a single option when `par2=1`.

**Locking:** during the dialog both players have `IsTalking=TRUE`. `DIALOG_LOCK/UNLOCK` keeps this in
sync. `StopDialog` sends `DIALOG_END` to a network partner and `DIALOG_UNLOCK` for both players.

**Chat:** an option that leads into `MENU_CHAT` (network humans only) ends the dialog and opens the text
chat. Leaving the chat sends `CHATSTOP`.

The node graph is in the trees file, section *Competitor*.

---

## 9. Widescreen notes (all **[WS]**)

* **Room-space anchors:** every `BubblePos`, clip offset and hit rectangle is in a 640x440 room. In
  OpenAT, anchor them to the room picture's rectangle, not to the screen. A bubble may extend past the
  room's left/right edges only if the room is letterboxed.
* **Absolute bubble extents:** the partner bubble spans x 50..600 (styles 0/2) or 5..`pos−10` /
  `pos+10`..630 (styles 1/3). The player bubble's right edge is 630, or 560 against a competitor; the
  competitor reserve is 70–80 px. These are screen-absolute in the original. Recommendation: express
  them relative to the room rectangle and keep the 560..640 band for the competitor portrait anchored to
  the **right edge of the room rectangle**.
* **Portraits:** the player portrait is at the left screen edge, x=0. The competitor portrait is at
  640 − width. The advisor click zone is (560,320)..(640,440). The slide baseline is y=440 (the top of
  the status bar). Choose whether portraits stick to the room rectangle edges or the screen edges; using
  the room edges preserves the bubble-to-portrait geometry.
* **Number card:** centred at x=320 with its bottom at y=439, so it centres on the room.
* **Phone inset:** a 340x440 strip that slides from x=800 to x=378, clipping the airport view at 378.
  For widescreen, slide it to "room right edge − 262" and start it off-screen at "room right edge + 160",
  or redesign it as a side panel.
* **Text layout:** the off-screen text bitmap equals the room size (640x440), and the layout probes use
  a 400 px maximum height. The number-card fitting loop is bounded by 640.
* **Hit area:** while a single line is shown, a click *anywhere* advances. On wide screens this should
  include the pillarbox area.

---

## 10. Open questions

1. **Q-1:** the `Pass` (passenger) group is absent from the shipped `dlg_ger.res`, so starting a
   passenger dialog would fail. Is there any live call site? None was found besides `StartDialog`
   handling (**L**: dead feature).
2. **Q-2:** the security officer's talk clip 3 is `ArabP.smk` (an Arab Air file). Is this a shipped bug,
   or is the file really present in the security room's video set?
3. **Q-3:** behaviour when a voice part is missing and subtitles are off: the bubble stays hidden and
   there is no auto-advance. Confirm in game; OpenAT should show the bubble.
4. **Q-4:** the long-distance level is computed from a stale `DialogPar1`. What do players actually pay
   for calls to a home-city branch?
5. **Q-5:** the opening and closing times (`timeArabOpen` and the others) were read from the SDL port.
   Check the GOG values.
6. **Q-6:** the exact semantics of `Sim.RoomBusy` beyond phone calls (the in-room lock is in Sim.cpp,
   around line 3700) belongs to the rooms spec.
7. **Q-7:** the `Sa` voice dir contains `igor/mario/siggi/tina.raw` and `euro(s).raw`. No `dlg` marker
   references them, and the `[[*]]` matcher doesn't produce them, so they look unused. Ignore them unless
   a code path turns up (**M**).
8. **Q-8:** the GOG build ships only `P1/numbers.bak`. Confirm that number synthesis (calculator + Space)
   is silent in the original.
