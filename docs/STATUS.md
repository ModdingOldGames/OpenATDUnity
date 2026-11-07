# Status

## Done
- **Formats:** xtRLE, GLIB (.gli/.glj), MCF fonts, .res texts (multilingual), CSV tables with the
  original's parsing rules, RAW sounds, IFF images (globe), Smacker video + audio (frame-exact vs
  ffmpeg on all game videos), HLine .pol person sprites, MIDI files.
- **Music:** MIDI sequencer + software synth (Windows gm.dls, SoundFont 2, built-in fallback).
- **Sim core:** exact original LCG (`TeakRand`), game clock in original units, day-state machine,
  real-time → tick driver with the original speed levels, World/Player partial model, pipeline hooks,
  commands, events, rules, typed data tables (cities, plane types, routes, plane names), new-game core.
- **View:** widescreen virtual framebuffer, bitmap fonts, asset cache, screen stack, scripted
  auto-runner for screenshots, title screen with music, split-flap main menu.
- **Specs:** clean-room behaviour specs for sim core, economy, flights, contracts/routes, data tables,
  airport/people, all rooms and UI, items/security/special rooms, computer players (docs/spec/findings).
- **Computer players:** Sim/AI — action queue, priority chain + weighted pick, all room actions
  through the shared commands (robot-only shortcuts separate), hall walking, nightly route planner and
  pricing, mission feature matrix as data, replaceable brain (`IRobotBrain`), `openat sim`.
- **Save games (Sim side):** Sim/Save — reflection graph serializer (versioned, tagged binary,
  shared references kept, content by id), header for the load menu, `[NotSaved]` + `ISaveHooks`,
  autosave requests (17:00, call-it-a-day), deterministic round trip tested over 10 days;
  `openat sim --save-at/--save/--load`.
- **Save/load UI (View/Session):** options load/save pages with slot names and info tooltips, main-menu
  "load game", resume with the room stack re-entered, autosave (slot 11), campaign unlocks + locked
  missions in the wizard, outro movies after the space finals, in-tray letters as sim state (Sim/Mail),
  rival phone calls (competitor dialog with computer airlines).

- **Gameplay domains:** finance (bank, shares, staff, kerosene, statistics, missions), operations
  (planes, flight plans, flights, contracts, freight, routes, gates, auctions), airport hall (layout,
  persons, walking, gates/boarding, widescreen camera), items/security/sabotage.
- **Rooms and UI:** room framework, status bar, menus, dialogs, new-game wizard, all rooms of the
  original incl. laptop/globe planner, evening screen, statistics, designer, special rooms; room hotkeys,
  per-room global ambient volume with the hall's ambient loops and fly-bys, landing and half-hourly announcements,
  phone pose, idle planes on the main menu.
- **Tutorial (mission 0)** and the open-issues sweep (docs/spec/integration.md).
- **Modding:** data mods (JSON), code mods (`IOpenATMod` DLLs), tunables, mod list (docs/MODDING.md).
- **Fidelity pass** against the running original: docs/FIDELITY.md lists what was compared and fixed.
- **Improvements after `faithful-baseline`** (docs/IMPROVEMENTS.md): the flight planner gets the fleet
  index with search and filters, docked schedules on wide screens, previous/next plane, flight cards,
  precise departure setting, refusal notices and expected profit or loss on flight cards, and a
  high-resolution filofax and laptop (smoothed artwork, the original fonts slightly smaller, more rows
  per page; the framebuffer now renders at the window's resolution). The order counters get a planner
  panel that starts with the planes able to fly the offer, placement trials on an isolated copy of the
  world, and an all-or-nothing "take and plan" or "apply". Gamepad play (Steam Deck, Legion Go, Xbox and
  PlayStation pads): a stick cursor that snaps to hotspots, d-pad and shoulder navigation between them,
  flight scheduling in the planner with the d-pad, and walking the figure with the left stick at a pace
  that follows the tilt.
- **Branches:** `faithful` keeps the game in its original form (from the tag `faithful-baseline`, plus
  fixes of the reproduction); `enhanced` carries the improvements and merges `faithful` after each fix.

## Open
- Day-0 start walk: the figures start from the hall's player spawn runes; the original's appear together
  out of the lift on the lower floor (not re-checked against the spec).
- Network game (shown dark).
- Not yet compared with the original: museum, kiosk, statistics, laptop, evening screen, manager's
  order board, sounds (see docs/FIDELITY.md).
