# Architecture

```
 Airline Tycoon Deluxe install (user's own copy, read-only)
   gli/ room/ misc/ data/ video/ SOUND/ VOICE/ intro/
          │  OpenAT.Formats  (GameInstall, XtRle, GliLibrary, McfFont, TextResource, CsvTable,
          │                   Smacker, RawSound, ...)
          ▼
 ┌──────────────────── OpenAT.Formats + OpenAT.Sim  (no UnityEngine) ──────────────────────┐
 │ Formats/  readers for the original data                                                  │
 │ Sim/      deterministic game model                                                       │
 │   Core/     GameClock, SimRandom, EventBus, Registry<T>, GameOptions                     │
 │   Data/     typed content loaded from the original tables (cities, plane types, ...)     │
 │   Model/    World state: players, planes, routes, contracts, persons ...                 │
 │   Systems/  ISimSystem implementations run by SimPipeline (per minute/hour/day/week)     │
 │   Rules/    replaceable formulas (IRule interfaces + default "original" implementations) │
 │   Commands/ every player/AI action is an ICommand executed by World.Execute()            │
 │   AI/       computer players issue Commands (same API as the human)                      │
 └──────────────────────────────────────────────────────────────────────────────────────────┘
          ▲ queries + events                       │ commands
          │                                        ▼
 ┌──────────────────── OpenAT.View (Unity) ────────────────────────────────────────────────┐
 │ Boot/     install lookup, asset cache (Texture2D/AudioClip from Formats)                 │
 │ Render/   Screen2D: virtual-pixel renderer (480 px high, width follows aspect ratio)     │
 │ Rooms/    one class per screen, registered in RoomRegistry by RoomId                     │
 │ Airport/  hall view, persons, scrolling (shows more airport on wide screens)             │
 │ UI/       status bar, menus, dialogs, fonts                                              │
 └──────────────────────────────────────────────────────────────────────────────────────────┘

 tools/OpenAT.Cli and tests/OpenAT.Tests compile the Formats + Sim sources with the .NET SDK.
```

## Principles

1. **Core is the source of truth.** The view never mutates simulation state; every button maps to
   an `ICommand`, so everything is testable headless (`openat sim`).
2. **Original behaviour by default, from specs, without the original's bugs.** Rules are implemented
   from the clean-room specs in `docs/spec/findings/`, using integer state in the original's units
   (money in whole currency units, time in game minutes) so thresholds and integer divisions match.
   Genuine bugs of the original are fixed by default; each fix is a `Fix*` switch (on by default) that
   restores the original when turned off (`docs/QUIRKS.md`).
3. **Deterministic.** One seeded `SimRandom` stream per world; no wall clock, no
   `System.Random`/`UnityEngine.Random`, no dictionary-order dependence in the Sim.
4. **Assets are never part of the build.** `GameInstall` reads the user's install at run time.

## Extensibility (first-class design goal)

The game must be easy to extend and to modify. Every layer has an explicit extension point, and
the original game is itself built through those same extension points ("the original is the first
mod"):

| Want to ... | Extension point |
|-|-|
| change a formula (ticket demand, interest, wear, ...) | implement the rule interface in `Sim/Rules` and register it: `world.Rules.Replace<IInterestRule>(new MyInterest())` |
| add per-tick/per-day behaviour | implement `ISimSystem`, register in `SimPipeline` with an order key (before/after named systems) |
| react to things happening | subscribe to typed events on `world.Events` (`DayStarted`, `FlightLanded`, `MoneyChanged`, ...) |
| add a player action | add an `ICommand` (validation + effect in one place); UI and AI both use it |
| add content (plane type, city, item, sabotage act, ...) | `Registry<T>` per content kind; original tables load first, extra JSON in `Mods/<name>/` merges/overrides by id |
| add or replace a room/screen | implement `IRoom`, register with `RoomRegistry` under a `RoomId` (new ids for new rooms) |
| store new state | `IExtensionState` components attached to `World`/`Player`/`Plane` (saved with the game) |
| toggle new behaviour | `GameOptions` flags; `Fix*` bug fixes default on (off = original), additions default off |
| tune a balance number | `world.Tunables` (`Sim/Modding/Tunables.cs`, keys in `TunableKeys`); data mods override them |
| ship any of the above without touching the code | a mod: `Mods/<name>/mod.json` + `data/*.json` and/or a DLL with an `IOpenATMod` (`docs/MODDING.md`) |

Rules for contributors:
- No `static` mutable game state; everything hangs off `World` so multiple worlds/tests coexist.
- A rule or system does one thing and names the spec section it implements.
- New features never edit a default rule in place; they add an alternative behind an option.
- Prefer data (tables, JSON) over code for tunables.

## Widescreen

The original renders 640x480 (rooms 640x440 + 40 px status bar). OpenAT renders into a virtual
framebuffer that is always 480 px high and `round(480 * aspect)` px wide (minimum 640). All layout
and input use these virtual pixels. The render texture itself has the window's resolution
(`Screen2D.DensityX/Y` device pixels per virtual pixel). Original bitmaps are point-sampled, so
they look as with nearest-neighbour upscaling. High-resolution content (`ScaledBitmapFont` text and
`PixelArtScaler` smoothed artwork, used by OpenAT improvements) uses the real pixels.
- **Airport hall:** the viewport simply gets wider; more of the airport is visible, scroll limits are
  derived from the viewport width.
- **Rooms:** fixed 640-wide artwork is centred; side areas are filled (blurred/dimmed edge
  extension of the room background, option: black bars).
- **Status bar:** stretches to full width; extra space shows additional information (option).
- Mouse coordinates are converted to virtual pixels once, in `Screen2D`.

## Fidelity checks (oracles)

- Format readers are verified against the real files (`openat scan`), Smacker frames against ffmpeg.
- Rule implementations carry unit tests with numbers taken from the specs.
- `openat sim` runs headless games for regression diffs.
