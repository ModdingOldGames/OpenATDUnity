# Rooms & UI — index and reading guide

Clean-room behaviour spec of the ATD room/UI layer, for the OpenAT (Unity/C#) reimplementation.
Everything here is written in our own words from the original source (`docs/spec/local/gog_src`,
German identifiers), the community SDL port (`docs/spec/local/ref_AirlineTycoon/src`), the English
manual and the GOG install's assets. No source code and no in-game text is reproduced: texts are
referenced as *resource file + group + id* (`std_ger.res`, `dlg_ger.res`, `ein_ger.res`), code as
`File.cpp: Function`. Each part marks confidence (H/M/L) and ends with open questions.

Widescreen: every part flags 640x480 assumptions (usually tagged **[WS]**) and has a summary
checklist section. The original room area is 640x440 with a 40 px status bar at y=440 (20 px
variant exists); popups, bubbles, phone pane, zoom anchors and exits are all positioned in that
fixed frame.

## Parts

| Part | File | Covers |
|---|---|---|
| 1 | [rooms-and-ui-1-framework.md](rooms-and-ui-1-framework.md) | `CStdRaum` lifecycle, background FX, phone ("Handy") pane, speech bubbles / say windows, number window, `StartDialog`/`StopDialog` entry/exit, tooltips & tip types, help/tutorial texts, highlighting, cursors, mouse & keyboard routing, global hotkeys & cheats, paint order, room-leave behaviour and exit rects, status bar (layout, 26 items), advisor/portrait notifications, chat overlay, `GameFrame` (fade, pause, activation). §0 = master widescreen list. |
| 2 | [rooms-and-ui-2-menus.md](rooms-and-ui-2-menus.md) | The `MenuStart` popup/list menu system: inventory of all 44 `MENU_*` types with owner rooms, generic machinery (zoom-in/out, highlight, paging, cancel, time keeps running), the calculator number pad, and a per-menu spec (lists, REQUEST notices, phone book, letters, kerosene, loans, shares, workshop lists, briefing, game over, special-edition newspaper…). |
| 3 | [rooms-and-ui-3-dialog.md](rooms-and-ui-3-dialog.md) | Dialog machinery (`Dialog.cpp`, `Talker.cpp`, `Synthese.cpp`): id-driven node flow, choice lists, menu-suspend/resume, the 27 talkers (room, text group, voice dir, smk clips, bubble anchors), clip/mood animation engine with amplitude-based lip-sync, voice lookup via per-text markers in the res file, number synthesis (disabled in shipped game), phone busy/closed/ringing and call costs, player/AI talks (15 s timeout). |
| 3b | [rooms-and-ui-3b-dialog-trees.md](rooms-and-ui-3b-dialog-trees.md) | Per-partner dialog graphs (node → text group/id, options, conditions, effects/prices), incl. boss briefing, mission/sabotage reports, takeover. Travel agency, Last Minute and freight have no dialogs (menus only). |
| 4 | [rooms-and-ui-4-title-and-meta.md](rooms-and-ui-4-title-and-meta.md) | App state machine + flow diagram, split-flap board ("Klacker", 24x16 cells at (128,63)), main menu and the 14-page new-game wizard (campaigns, missions, players, home airport, highscores, network), Options pages 1–8 (incl. load 12 slots / save 11 slots, autosave slot at 17:00), Credits, Intro/Outro (`intro.smk`, `outro.smk`, `outro2.smk`), HallDiskMenu (editor-only). |
| 5 | [rooms-and-ui-5-office-planner.md](rooms-and-ui-5-office-planner.md) | Office (`CBuero`, per-airline `buero_a..d.gli`, dark `buerodrk.gli`), Globe room / Filofax (`CGlobe`), Laptop / Notebook (`CLaptop`, battery per model, virus mode), globe rendering. |
| 5b | [rooms-and-ui-5b-planner-blocks.md](rooms-and-ui-5b-planner-blocks.md) | The "Block" window system (lifecycle, z-order, panes, tabs), pane contents, the flight-plan week grid (6 px/hour x 19 px/day), and the full planner drag/drop/carry state machine with issued sim commands. |
| 6 | [rooms-and-ui-6-airport-services.md](rooms-and-ui-6-airport-services.md) | Personnel office, Bank, Boss office (Aufsicht, 9:00 briefing, airport expansion), Bulletin board (Tafel auctions), Travel agency, Last Minute, Freight, Route office (RouteBox); order-card display/take; item puzzles. |
| 7 | [rooms-and-ui-7-shops-and-planes.md](rooms-and-ui-7-shops-and-planes.md) | Plane broker (Makler), Museum, Designer, Workshop, Plane properties (PlanProp), Aircraft constructor (Editor, `.plane` format, part relation table appendix), Advertising (Werbung / `nowerb.gli`), Kiosk, Duty free. |
| 8 | [rooms-and-ui-8-special-rooms.md](rooms-and-ui-8-special-rooms.md) | Rick's, Petrol Air (Arab Air), Sabotage room, Safe&Secure (Security), NASA, telescope views (Island, Rushmore, Space/WeltAll), World (branch phone call view), Statistics (full layout & series), Evening / day-end screen (`abend0..3` seasons). Classifies non-UI files (Checkup, SecurityManager, Brick, Askbrick). |
| 9 | [rooms-and-ui-9-room-hosting.md](rooms-and-ui-9-room-hosting.md) | Complete `ROOM_*` list, room factory table (id → class → GLI → manual name), rooms reachable by phone, player room stack and room-switch protocol, walk-in/occupancy/opening hours/max-stay rules, time flow inside rooms, airport view camera/scroll/clicks, the 8-frame crossfade, classification of Blenden/Cblenden/Fx2/DeltaVid/HLine, entry-denial message ids. |

Related findings by other topics: `sim-core.md`, `planes-and-flights.md`, `ai-robots.md`.

## Suggested reading order for implementers

1. Part 9 §2–§6 (what a room is, how it is created/destroyed, the room stack).
2. Part 1 (base room behaviour every screen inherits: bubbles, tips, cursors, status bar, leave).
3. Part 2 (menus) and Part 3 (dialogs) — almost every room uses both.
4. Individual rooms (Parts 5–8), title/meta (Part 4).

## Cross-cutting facts (summary; details in the parts)

- **One room window per player.** Entering a sub-screen (e.g. Office → Laptop) destroys the
  underlying room and leaving re-creates it fresh; persistent state must live in the player/sim,
  not the room object (Part 9 §6.2). Phone dialogs are a *second* window (slide-in pane) beside
  the current room, not a stack entry (Part 1 §3, Part 9 §3.1).
- **Game time keeps running** inside rooms and while menus are open; only Options, pause, network
  waits and the briefing/evening freezes stop it (Part 2 §2, Part 9 §7).
- **Leaving a room:** exit hotspot (generic id 999 at screen-edge rects), right-click when no
  menu/dialog is open, or hotkeys; Esc opens Options instead (Part 1 §8).
- **Transitions:** only an 8-frame crossfade between airport and rooms, frame-counted, can be
  disabled in options (Part 9 §9.1).
- **Assets:** each room uses one `room\*.gli` (bitmap names listed in the matching `gl*.h`
  header) plus shared `gli\glstd.gli` (status bar, bubbles, cursors, tip frames) and
  `gli\*.gli` for menus (`list.gli`, `request.gli`, `rechner.gli`, `letter.gli`, `fax.gli`,
  `oilmenu.gli`, `kredit.gli`, `konto.gli`, `telefons.gli`, `tip*.gli`, …). Character animations
  are `video\*.smk` (plus `.raw` sounds), voices under `VOICE\<2-letter partner dir>\`.

## Original-game bugs and quirks catalogued

Each part has a bugs/quirks list with "replicate or fix" notes (e.g. list-menu highlight row
bug on page ≥2, Space-room left-click exits dead, Back on load/save closes Options, Bulletin
board route notices never populated, laptop city dots depend on an uninitialised counter).
Decision policy for OpenAT is an open question for the project lead.

## Top open questions (collected)

1. Widescreen anchoring policy: should room content stay a centred 640x440 canvas with
   extended backgrounds, and should status bar / advisors / portraits anchor to screen edges or
   to the 640 area? (Parts 1, 2, 9)
2. Real-time targets for frame-counted behaviour (crossfade, Klacker flip rate, screensaver,
   camera scroll cap) — the original is frame-rate bound. (Parts 4, 9)
3. Manual vs. code disagreements on F-key bindings (code taken as authoritative) and on
   "hold mouse on clock to speed up time" (not found in code). (Parts 1, 4, 9)
4. Encoded `std_ger.res` strings were not decoded, so some meanings (music modes, some page
   labels) are inferred. A res-format finding would close several questions. (Part 4)
5. Several behaviours need in-game verification (room-occupied message 2100 vs 3012, globe turn
   direction, designer blender clip, personnel paper hand-off). (Parts 5–9)
