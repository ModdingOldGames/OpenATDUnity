# Modding OpenAT

OpenAT is built to be extended: every formula is a replaceable rule, every time-driven process a
system in an ordered pipeline, every player action a command, content sits in id-keyed registries and
rooms in a room registry (`docs/ARCHITECTURE.md` "Extensibility"). The modding system packages those
extension points so they can be used **without changing the game's code**:

- **Data mods**: JSON files that override balance numbers ("tunables"), plane types, cities, routes,
  new-game options and the computer players' tables.
- **Code mods**: a .NET DLL with a class implementing `IOpenATMod`; it can add systems, replace or
  decorate rules, react to events, add commands, rooms and screens, change content and keep saved state.

Mods are loaded by the same code (`Sim/Modding`, no Unity) in the Unity build, the CLI
(`openat sim --mods DIR`) and the tests, so a game with mods plays identically everywhere.

Code: `Assets/OpenAT/Sim/Modding/**` (loader, data mods, tunables, `ModContext`),
`Assets/OpenAT/View/Modding/**` (Unity boot, rooms/screens, mod news, the mod list),
examples in `Mods.Examples/`, tests in `tests/OpenAT.Tests/ModdingTests.cs`.

## 1. Installing and finding mods

A mod is a folder with a `mod.json`:

```
Mods/
  LowFuelPrices/
    mod.json
    data/kerosene.json        <- data files: every *.json under data/ (sorted by path)
  RouteBonusEvent/
    mod.json
    RouteBonusEvent.dll       <- code: every *.dll in the mod folder (or the list in "assemblies")
```

Folders searched (in this order; a folder may also be a single mod folder):

| Where | Unity build | headless |
|-|-|-|
| `<folder of OpenAT.exe>/Mods` (editor: `<project>/Mods`) | yes | - |
| `Application.persistentDataPath/Mods` (e.g. `%USERPROFILE%/AppData/LocalLow/<company>/OpenAT/Mods`) | yes | - |
| every folder in `OPENAT_MODS_DIR` (separated by `;`) | yes | yes (CLI; `--no-mods` ignores it) |
| `-openat-mods DIR` (repeatable) / `--mods DIR` (repeatable) | yes | yes |

`-openat-nomods` starts the Unity build without mods. Mods are loaded once per run, before the first
scene. The log shows one line per mod (`OpenAT mods: lowfuelprices 1.0.0 loaded (1 data file(s))`,
`... FAILED, skipped: ...`) and every changed tunable.

**Mod list**: Options → other page → `MODS...` (or `-openat-screen mods`) lists every mod found; a
click switches it on/off for the next start (stored in PlayerPrefs `OpenAT.Mods.Disabled`). Marks:
`!` broken (see the log), `-` off, `*` changed (restart needed).

### mod.json

```json
{
  "id": "routebonusevent",              // unique; letters, digits, '.', '-', '_' (default: folder name)
  "name": "Route bonus of the week",
  "version": "1.0.0",                   // dotted numbers, compared numerically
  "author": "you",
  "description": "What it does.",
  "dependencies": ["otherid", "lib>=1.2", { "id": "maybe", "optional": true }],
  "loadOrder": 10,                      // lower first (after dependencies); ties by id
  "enabled": true,
  "assemblies": ["RouteBonusEvent.dll"] // optional: which DLLs to load (default: all *.dll)
}
```

Comments (`//`, `/* */`) and trailing commas are allowed in all mod JSON files.

**Load order**: dependencies first, then `loadOrder`, then id (ordinal). A mod whose dependency is
missing, too old or broken fails; so do mods in a dependency cycle. The first folder with a given id
wins (later duplicates are skipped). Everything a mod registers is applied in load order, so a later
mod's override wins and a later decorator wraps an earlier one.

**Failure isolation**: a broken `mod.json`, an invalid data file, a DLL that does not load, an
exception in `OnLoad` → the mod is logged as FAILED and skipped completely (nothing it registered is
applied). An exception in a world hook skips that hook for that world; an exception in a mod's
system switches that system off for that world (`GuardedSystem`); an exception in an event handler
registered with `ctx.Subscribe` is logged. The game keeps running in every case.

## 2. Data mods

Files in `data/*.json`; each is an object with any of these sections (applied in this order):

```json
{
  "tunables": { "kerosene.min": 150, "kerosene.max": 350, "kerosene.start": 250 },
  "options":  { "FixLoanInterest": true, "RouteTheftReachable": true },
  "planeTypes": [
    { "id": 100, "price": 30000000, "seats": 180 },                    // change by csv id (100..126)
    { "id": 140, "basedOn": 105, "name": "757 Stretch", "seats": 300 } // new type: copy of 105, then patched
  ],
  "cities": [ { "name": "Berlin", "population": 4000000 }, { "id": 12, "officeRent": 20000 } ],
  "routes": [
    { "from": "Berlin", "to": "London", "factor": 1.5 },               // existing route: both directions
    { "from": "Berlin", "to": "Rome", "level": 1, "rent": 30000, "bothWays": true }   // new route
  ],
  "robotFeatures": {
    "Sabotage": "------|-|----------|----------",                      // whole row: base 0-5|free|add-on 1-10|ATFS 1-10
    "Freight":  { "free": false, "atfs3": true, "all": false }         // single cells: base0..5, free, addon1..10, atfs1..10, all, 0..26
  },
  "robotTuning": {
    "workTicks": { "Bank": 60, "Office": 80 },                          // RobotAction -> ticks in the room
    "defaultPicks": ["Bank", "Office", "Kiosk"],                        // the random pick table (one entry per weight unit)
    "standStillLimit": 5, "teleportWalkTicks": 200                      // any int field of RobotTuning
  }
}
```

Field names are the public fields of `PlaneType`, `City`, `RouteDef`, `GameOptions`, `RobotTuning`
(case-insensitive); ids, indices and demand cannot be set. A file is checked completely when the mod
loads (unknown fields, cities, features or actions fail the mod with the file and the path in the
message); unknown sections are reported and ignored.

Limits (the content tables are shared with graphics and per-airline arrays):
- **Cities**: existing cities only (by `name` or csv `id`). New cities would need map positions,
  photos, texts and every per-city table; not supported.
- **Plane types**: a new type needs `basedOn` (its graphics and missing values come from that type) and
  a free album slot (30 slots, the original uses 27). Use csv ids from 140 up for new types.
- **Routes**: new routes between existing cities are appended after the original (sorted) table.
- **Options**: are new-game defaults; they apply to games started without explicit options (the
  wizard, `openat sim`). A loaded game keeps the options it was saved with.
- **Robot tables**: `robotFeatures` changes the feature matrix rule (skipped with a log line if a code
  mod replaced that rule); `robotTuning` is part of the saved AI state, so a loaded game keeps the
  values it was saved with.

### Tunables

Balance numbers the rules read from `world.Tunables` instead of constants. Defaults are the
original's values; `GameData.Tunables` holds the values after all mods, every world gets its own copy
(a code mod may change a world's values). Tunables are not saved: a loaded game uses the values of
the mods active now. Values are clamped to the key's range and rounded for whole-number keys.

| Key | Default | Used by |
|-|-|-|
| `kerosene.start` | 500 | `World.InitKerosene` (game start; history[9] = start + 8) |
| `kerosene.min` / `kerosene.max` | 300 / 700 | `KerosenePriceRule` clamp (all fuel prices follow the market price) |
| `route.demandFactor` | 4.27 | `OriginalRouteDemand.NextDemand` (target = potential × factor) |
| `route.potentialPercent` | 100 | `OriginalRouteDemand.Potential` |
| `finance.startMoneyPercent` | 100 | `FinanceSetup` (human and computer start money) |
| `finance.loanRateDelta` / `finance.savingsRateDelta` | 0 / 0 | `FinanceSetup` (added to the mission's rates) |
| `shares.startPrice` | 100 | `FinanceSetup` (share price history at start) |
| `fleet.wearPercent` | 100 | `OriginalPlaneWear` (wear per departure) |
| `incident.seasonalCost` | 75000 | `BasicIncidentEconomics.SeasonalIncident` |
| `incident.qualityImageLoss` | 2 | `BasicIncidentEconomics.QualityIncident` |
| `fuel.tankStartCapacity` | 100 | `FinanceSetup` (tank size at start) |

The full list with descriptions is `Tunables.BuiltIn` (`Sim/Modding/Tunables.cs`). Mods define their
own keys with `ctx.DefineTunable("mymod.something", default)`; name them `<modid>.<name>`. A data mod
may set another mod's tunables (load it after that mod with a dependency).

**Adding a tunable to the game** (contributors): add the key to `TunableKeys` and a `TunableDef` with
the original value and spec section to `Tunables.BuiltIn`, then read it in the rule with a fallback
(`w.Tunables?.Int(TunableKeys.X, original) ?? original`) so worlds without tunables keep working.
Keep the original arithmetic exact for the default (e.g. apply a percentage only when it is not 100).

## 3. Code mods

### Project

A code mod is a netstandard2.1 class library compiled against the game's assemblies. For Sim-only
mods use the SDK projects in `Mods.Examples/Sdk` (they build `OpenAT.Sim.dll` / `OpenAT.Formats.dll`
from the sources, with the Unity assembly names):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <IsOpenATMod>true</IsOpenATMod>   <!-- Mods.Examples/Directory.Build.targets deploys to Build/Mods/<Name> -->
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Sdk/OpenAT.Sim/OpenAT.Sim.csproj" Private="false" />
  </ItemGroup>
</Project>
```

`Private="false"`: never ship `OpenAT.*.dll` with a mod (the loader ignores them anyway). At run time
the game binds the mod's `OpenAT.Sim` reference to its own Sim, whatever assembly that is (Unity:
`OpenAT.Sim`; CLI: `openat`; tests: `OpenAT.Tests`) — `ModAssemblies` loads mod DLLs from bytes and
resolves `OpenAT.Sim`/`OpenAT.Formats` to the host. Mods with rooms or screens also reference
`OpenAT.View.dll` and `UnityEngine.CoreModule.dll` (see `Mods.Examples/HelloRoom/HelloRoom.csproj`:
the editor's `Library/ScriptAssemblies` or a player's `OpenAT_Data/Managed`). Unity uses the Mono
backend, so `Assembly.Load` works in the player (IL2CPP would not load mods).

A mod built against an older game still loads as long as the members it uses exist; if not, the
loader reports "types do not load (built against another game version?)" and skips it.

### Entry point

```csharp
public sealed class MyMod : IOpenATMod
{
    public void OnLoad(ModContext ctx)
    {
        // only registrations here: no game data or world exists yet
    }
}
```

Every public non-abstract `IOpenATMod` class with a parameterless constructor in the mod's DLLs is
created and called (ordered by full name). `OnLoad` runs once per run; what it registers is applied to
the game data once and to **every world** at construction — a new game *and* a loaded game, after the
built-in systems and rules are registered and before the new-game steps run or the save is laid over
the world.

### ModContext API (with the extension point behind each call)

| Call | Applies to | Extension point |
|-|-|-|
| `ctx.EditData(d => ...)` | game data, once | `Registry<T>` content (`d.PlaneTypes.Register(...)`, `d.Cities`, `d.Routes`, ...) |
| `ctx.AddPlaneType(type)` | game data | `Registry<PlaneType>` (same id = replace) |
| `ctx.DefineTunable(key, def, ...)`, `ctx.SetTunable(key, v)` | game data | `Tunables` |
| `ctx.EditNewGameOptions(o => ...)` | new games without explicit options | `GameOptions` |
| `ctx.OnWorldCreated(w => ...)` | every world | anything on `World` |
| `ctx.AddSystem(() => new MySystem())` / `ReplaceSystem` / `RemoveSystem(name)` | every world | `SimPipeline` (ordered by `Order`) |
| `ctx.AddSetupStep(() => new MyStep())` | every new game | `ISetupStep` (ordered by `Order`) |
| `ctx.SetRule<IFoo>(w => new MyFoo())` | every world | `RuleSet.Set` (replace) |
| `ctx.DecorateRule<IFoo>(inner => new MyFoo(inner))` | every world | `RuleSet.Decorate` (wrap) |
| `ctx.Subscribe<FlightLanded>((w, e) => ...)` | every world | `EventBus` |
| `ctx.RegisterCommand("name", (w, args) => new MyCommand(...))` | lookup by name | `ICommand<World>`; `ModSet.CreateCommand` |
| `ctx.RegisterRoom(1000, () => new MyRoom(), "name")` | Unity | `RoomRegistry` |
| `ctx.RegisterScreen("name", () => new MyScreen())` | Unity | `ScreenFactory` (`-openat-screen name`) |
| `ctx.State<T>(w)`, `ctx.PlayerState<T>(p)` | every world | `World.Extensions` / `Player.Extensions` (saved) |
| `ctx.Log(text)` | - | the mod log |

`[Room(id)]` and `[ScreenName("x")]` classes in a mod DLL are registered automatically as well.

### Systems

```csharp
public sealed class RouteBonusSystem : SimSystem
{
    public override string Name => "mod.routebonus";   // unique; prefix with "mod."
    public override int Order => 2150;                 // between built-in steps (here before "ops.route-demand", 2200)
    public override void OnNewDay(World w) { ... }     // also OnTick/OnMinute/OnHour/OnPlayerNewDay/OnMorning/OnEvening
}
```

Built-in orders are the original's step numbers × 100 (sim-core.md §2.4 hour, §2.6 midnight, §2.7
per airline); list them with `world.Systems.Systems`. Examples: `ops.route-demand` 2200 (midnight),
`ops.landings` 3000 (minute), `AdvanceDateSystem` 1300 (the date changes there).

### Rules

Rule interfaces (each with an "original" default): `ICreditLimitRule`, `IInterestRule`,
`ISharePriceRule`, `IKerosenePriceRule`, `ICompanyValueRule`, `IRouteDemandRule`,
`IRoutePassengerRule`, `IPlaneWearRule`, `IMaintenanceRule`, `IFlightFuelCost`, `IIncidentEconomics`,
`IMissionRule`, `IRobotFeatures`, `IRobotBrain`, `IRivalActions`, `IRivalSympathy`, `ICrewMapping`,
`IFuelAccount`, `IMechanicLevel`, `IMaintenanceBooking`, `IStrikeStatus`, `IPurchaseRebate`,
`IAirportGateCount`, `IFlightSabotageState`, `IOperationsBridge`. Prefer `DecorateRule` (keeps other
mods' changes and the original as the inner rule) over `SetRule`:

```csharp
ctx.DecorateRule<IInterestRule>(inner => new DoubleDepositInterest(inner));

sealed class DoubleDepositInterest : IInterestRule
{
    readonly IInterestRule _inner;
    public DoubleDepositInterest(IInterestRule inner) => _inner = inner;
    public void BookOvernightInterest(World w, Player p) { _inner.BookOvernightInterest(w, p); _inner.BookOvernightInterest(w, p); }
}
```

### Events

`ctx.Subscribe<T>((w, e) => ...)` for any `ISimEvent`: `DayChanged`, `MorningStarted`,
`EveningStarted`, `FlightLanded`, `FlightBooked`, `FlightDelayed`, `MoneyChanged`, `ImageChanged`,
`ContractAccepted`, `RouteLicenceChanged`, `PlaneBreakdown`, `PlaneIncident`, `PlaneTraded`,
`StrikeStarted`, `MissionWon`, `PlayerOut`, `LetterSent`, `AdvisorMessage`, `CommandExecuted` (every
command), ... (`grep ": ISimEvent"`). Publish your own events with `w.Events.Publish(new MyEvent())`.
`ModNewsEvent(modId, text, playerIndex = -1)` is shown by the Unity build as an advisor message to the
local player (mods have no entries in the game's text files) and printed by `openat sim`.

### Commands

Player and AI actions are `ICommand<World>` (`Validate` + `Apply`), executed with `w.Execute(cmd)`;
the UI, the AI and tests all use the same path. A mod's commands need no registration;
`ctx.RegisterCommand` only gives them a name for consoles/scripts (`ModSet.CreateCommand(name, w, args)`).

### Rooms and screens (Unity)

```csharp
public sealed class HelloRoom : RoomScreen
{
    protected override string Library => null;     // or a room GLI of the install, e.g. "room/bank.gli"
    protected override string Background => null;
    protected override void Hotspots(InputState input) { Hot(200, 250, 440, 290, 1); Exit(0, 380, 639, 439); }
    protected override void OnClick(int id) { ... }
    protected override void DrawRoom(Screen2D s) { App.Ui.Kit.BigGrey.Draw(s, RX + 320, 90, "HELLO", TextAlign.Center); }
}
// in OnLoad:
ctx.RegisterRoom(RoomIds.FirstCustom, () => new HelloRoom(), "Hello room (mod)");
```

New rooms use ids from `RoomIds.FirstCustom` (1000); registering an original id replaces that room.
Mod rooms appear in the lobby list (`-openat-screen lobby`) under their registered name; enter one
directly with `-openat-screen 1000` or the auto-runner command `room 1000`. See `docs/ROOMS.md` for
the room framework (dialogs, menus, phone panes; `Talkers.Register`, `DialogActions.On`).
Never draw or ship original game graphics in a mod; read them from the install at run time.

### Saved state

- Keep game state in `ctx.State<T>(w)` / `ctx.PlayerState<T>(p)` (key `mod.<id>` in
  `World.Extensions` / `Player.Extensions`). Everything reachable from there is saved by reflection
  (`docs/IMPLEMENTING.md` "Save games"): plain classes with a parameterless constructor, fields of
  primitives, strings, enums, arrays, `List`, `Dictionary`, `HashSet`, own classes, content objects
  (saved by id).
- **Systems, rules and subscriptions are not saved** (they are re-created for the loaded world), so
  never keep game state in a system's or rule's fields. Caches: `[NotSaved]` + `ISaveHooks.OnAfterLoad`.
  Types that must never be saved: `SaveTypes.Exclude(typeof(T))`.
- Determinism (headless runs, save/load lockstep): random numbers only from `TeakRand`/`SimRandom`
  seeded from game state (e.g. `new TeakRand(w.Clock.Date * 7919 + state.Count)`), no wall clock, no
  `System.Random`, no iteration over `Dictionary`/`HashSet` where the order matters, no static mutable
  game state.

## 4. Compatibility rules

- **Ids**: mod ids are unique; state keys `mod.<id>`; system names `mod.<id>...`; tunable keys
  `<id>.<name>`; room ids ≥ 1000; new plane type csv ids from 140; type names in your own namespace
  (save games store full type names).
- **Saves with mods**: a save records the active mods (`ModStamp`, key `openat.mods`, only when mods
  are active). After `SaveGame.Read`, `ModSet.CheckSave(world)` lists the differences
  ("saved with mod X, which is not loaded now", "... was not active when the game was saved",
  version changes); the CLI prints them as `# mods:` lines.
- **Loading a modded save without the mod**: the game loads and plays; the mod's state is dropped
  with a load warning (`unknown type ... (skipped)`), its systems/rules are simply absent, tunables
  return to their defaults. Content the save references by id that no longer exists (a mod's plane
  type) is reported as `content ... not in the game data`.
- **Loading a vanilla save with a mod**: the mod starts with fresh state (its `ctx.State` is created
  on first use); its world hooks run for the loaded world.
- **Mod updates**: added fields of state classes get their initialiser values on load; removed or
  retyped fields are dropped with a warning. Rename state types only with a migration (read the old
  key in a world hook).
- Changing tunables or data never changes a save file; it changes how the loaded game continues.

## 5. Examples (`Mods.Examples/`)

| Mod | Kind | Shows |
|-|-|-|
| `LowFuelPrices` | data | `tunables`: kerosene market 150..350 (start 250) instead of 300..700 |
| `RouteBonusEvent` | code (Sim) | weekly `SimSystem` (TeakRand), `DecorateRule<IRouteDemandRule>`, `DefineTunable`, `ModNewsEvent`, saved state, named command |
| `HelloRoom` | code (View) | room 1000 registered from `OnLoad`, listed in the lobby; reads another mod's state |

Build and run headless:

```
dotnet build Mods.Examples/RouteBonusEvent          # -> Build/Mods/RouteBonusEvent (mod.json + DLL)
dotnet run --project tools/OpenAT.Cli -- sim --days 15 --mods Mods.Examples/LowFuelPrices --mods Build/Mods
#   # OpenAT mods: lowfuelprices 1.0.0 loaded (1 data file(s))
#   # OpenAT mods: routebonusevent 1.0.0 loaded (code: OpenAT.Mods.RouteBonusEvent.RouteBonusMod)
#   # d1  0:00 news [routebonusevent] Travel boom! Demand from ... is up 50% this week.
```

`sim --save-at 5 --save f.sav` and `sim --load f.sav` with the same `--mods` end with the same state
hash as one uninterrupted run (the determinism oracle also holds with mods).

The view mod is built against the Unity assemblies:

```
dotnet build Mods.Examples/HelloRoom -p:OpenATManaged=<player>/OpenAT_Data/Managed -p:UnityManaged=<player>/OpenAT_Data/Managed
```

Copy the `Build/Mods/*` folders (and `Mods.Examples/LowFuelPrices`) into `<player>/Mods/`, start
the player and check the log (`OpenAT mods: ...`), then `-openat-screen lobby` (room list) or
`-openat-screen 1000`.

**Testing a mod** headless: load it with `ModLoader.Load(folder)` or add the instance with
`new ModSet().AddCodeMod(manifest, new MyMod())`, apply it to game data (`set.ApplyData(data)`), build
worlds from that data (`World.NewGame`, `HeadlessRunner.NewGame`, `SaveGame.Read`) — see
`tests/OpenAT.Tests/ModdingTests.cs`.

## 6. Other extension points (not wrapped by ModContext)

Available to code mods through `ctx.OnWorldCreated`/`ctx.EditData` or directly in `OnLoad`:

| Extension point | Where |
|-|-|
| content registries | `GameData.Cities`, `PlaneTypes`, `Routes`, hall content (`HallContent`), designer catalogue |
| robot tables | `RobotFeatureTable.Set(feature, level, on)`, `world.AI.Tuning` (`RobotTuning`) |
| dialogs | `[DialogScript(talker)]`, `DialogActions.On/OnOpen`, `Talkers.Register` (`docs/ROOMS.md`) |
| test scripts | `AutoRunner.Extra` (own auto-runner commands) |
| save system | `[NotSaved]`, `ISaveHooks`, `SaveTypes.Exclude` |
| options | `GameOptions` switches (`Fix*` bug fixes on by default, additions off; all `Fix*` off = original) |
