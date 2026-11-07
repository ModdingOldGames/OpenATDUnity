# Implementation guide (for contributors and agents)

Read first: `CLAUDE.md`, `docs/ARCHITECTURE.md`, `docs/spec/README.md`.

## Sim conventions
- `World` and `Player` are `partial` classes. Each domain keeps its state in its own files
  (`Player.<Domain>.cs`, `World.<Domain>.cs`) and registers itself in its `Init<Domain>()` partial
  method (`World.Core.cs`): systems (`Systems.Add`), rules (`Rules.Set<T>`), setup steps (`SetupSteps.Add`).
- Time-driven behaviour = `SimSystem` subclass. `Order` = the step number of the original sequence
  × 100 (sim-core.md §2.6 midnight steps, §2.7 per-airline steps, §2.4 hour steps), so the order of
  the original is reproduced and extensions can slot in between.
- Formulas that a modder may want to change = an `IRule` interface + default implementation named
  after the spec section, registered with `Rules.Set`.
- Balance numbers a modder may want to change = a tunable (`Sim/Modding/Tunables.cs`: key in
  `TunableKeys`, original value in `Tunables.BuiltIn`, read with `w.Tunables`); see `docs/MODDING.md`.
- Player actions = `ICommand<World>` classes (Validate + Apply). UI and AI both use them.
- Money only through `Player.Book(amount, MoneyReason.X)` (or `ChangeMoneySilently` where the
  original bypasses the books). Image only through `Player.ChangeImage`.
- Randomness only through `TeakRand`, seeded exactly where the spec says.
- Original behaviour by default, but not the original's bugs: where a spec marks a QUIRK that is a
  genuine bug (logic slip, overflow, wrong comparison or table, dead code path), implement the fixed
  behaviour by default behind a `Fix*` flag (`GameOptions`, or `AiOptions` for the AI) that defaults
  to on, so turning it off restores the original; list it in `docs/QUIRKS.md` and test both settings.
  Deliberate design choices stay as they are. Additions that are not in the original default to off.
- Text shown to players comes from the install's .res files by group/id; never hard-code original text.
- Publish `ISimEvent`s for things the view or other domains react to.
- Units: money `long`, time in `GameClock` units, dates as `Date` day numbers, ids as in the original.

## Save games (Sim/Save)
- Everything reachable from `World` through instance fields (public or private) is saved by
  reflection: new state fields need no save code. Rules, systems, setup steps, the event bus,
  delegates and game data are not saved; the loaded world is a fresh `World(data, options)` with the
  saved state laid over it. Content objects (`IContent` in a `GameData` registry) are saved by id.
- Supported: primitives, enums, strings, arrays (any rank), `List`, `Dictionary`, `HashSet`, `Queue`,
  `Stack`, own classes and structs (polymorphic, shared references kept). Other framework types fail
  the save with the field path.
- Caches and derived content: mark the field (or type) `[NotSaved]` and rebuild it in
  `ISaveHooks.OnAfterLoad` (explicit implementation in a `*.Save.cs` partial, e.g. `HallState.Save.cs`).
- Give state classes a parameterless constructor (private is fine), so field initialisers of unsaved
  fields (delegates, caches) and of fields added after a save was written still run on load
  (`SaveGame.Audit` lists offenders; a test keeps it empty).
- Determinism oracle: `SaveGameTests` runs a game, saves, loads and plays both worlds in lockstep
  comparing the daily rows and `SaveGame.StateHash`; `SaveGame.Dump` diffs two worlds field by field.
- State the view would otherwise keep (in-tray letters, the room stack) belongs in the Sim so it is saved:
  `Player.Mail` (Sim/Mail, filled from `LetterSent` from the first minute), `Player.RoomStack`
  (`RecordRoomStackCommand` before each save). View subscriptions are not saved: re-bind them when
  `GameApp.Session` changes (see `GameFlow`, `AdvisorLayer`, `View/Session/SessionServices`).

## Tests
`dotnet test tests/OpenAT.Tests --artifacts-path <scratch dir>` — add tests per rule with numbers from
the spec. Tests needing the install must return early when `GameInstall.TryLocate()` is null.
