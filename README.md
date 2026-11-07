# OpenAT

An open-source reimplementation of **Airline Tycoon Deluxe** in Unity 6, with widescreen support and
an engine designed to be extended.

OpenAT contains no original game data. It reads graphics, sounds, texts and tables from **your own
copy** of Airline Tycoon Deluxe (for example the GOG or Steam edition).

## Which branch?

| Branch | What it is | Use it if |
|-|-|-|
| [`faithful`](../../tree/faithful) | **The faithful recreation.** The game as it was: same rules, numbers, screens and controls. Only clear bugs of the original are fixed (each fix can be switched back, see [docs/QUIRKS.md](docs/QUIRKS.md)). | you want the original game, nothing added |
| [`enhanced`](../../tree/enhanced) (default) | The faithful game plus improvements: full gamepad support (Steam Deck, Legion Go, Xbox, PlayStation), analog walking speed, d-pad flight scheduling, fast-travel radial menu, rebindable buttons, skippable start walk and more ([docs/IMPROVEMENTS.md](docs/IMPROVEMENTS.md)). Every improvement can be switched off in *Options > Other > OpenAT extras*. | you want the original game with modern comforts |

Fixes for the original game are made on `faithful` and merged into `enhanced`, so both always play the same
underneath.

## Installing (Windows)

Download a setup from the [releases](../../releases):

- `OpenAT-Setup-<version>.exe` — the enhanced version (`enhanced` branch);
- `OpenAT-Faithful-Setup-<version>.exe` — the faithful recreation (`faithful` branch).

The setup wizard asks for your Airline Tycoon Deluxe folder. It finds GOG and Steam installs by itself
and checks the folder you pick. It then copies the game's data (about 550 MB) into a `Game` folder next to
`OpenAT.exe`; your Airline Tycoon Deluxe install is only read. Both setups can be installed side by side.

Silent install: `OpenAT-Setup-<version>.exe /VERYSILENT /GAMEDIR="D:\Games\Airline Tycoon Deluxe" [/DIR=...]`.

## Running from source

1. Install Airline Tycoon Deluxe (GOG or Steam).
2. Open this folder with Unity 6000.4.3f1 and press Play (or build a player).
3. OpenAT finds the GOG install automatically; otherwise it asks for the folder
   (or set `OPENAT_GAME_DIR`, or pass `-openat-game <dir>`, or put the game's files in a `Game` folder
   next to `OpenAT.exe`).

## Features beyond the original

- Widescreen: any aspect ratio; the airport shows more of the hall, rooms are centred.
- Extensible engine: replaceable rules, simulation systems pipeline, commands, content registries,
  room registry (see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)). Mods: [docs/MODDING.md](docs/MODDING.md).
- On `enhanced`: the improvements listed above.

Status of the reimplementation: [docs/STATUS.md](docs/STATUS.md).

## Development

- `dotnet test tests/OpenAT.Tests` — format and simulation tests (no Unity needed).
- `dotnet run --project tools/OpenAT.Cli -- scan` — parse every asset of the install.
- `tools/build/build-player.sh [outdir]` — build a Windows player from a mirror (works while the editor is open;
  `OPENAT_BUILD_MIRROR` picks the mirror folder, use a separate one per branch).
- `tools/installer/build-installer.ps1 -Flavor enhanced|faithful` — build the player and the setup wizard
  (needs Inno Setup 6: `winget install JRSoftware.InnoSetup`). Run it on the matching branch.

## Legal

Airline Tycoon Deluxe is © Spellbound / Kalypso Media / Black Forest Games. OpenAT is an unofficial fan
project, not affiliated with them. Game behaviour is reimplemented from written behaviour
descriptions (see [docs/spec/README.md](docs/spec/README.md)); no original code or assets are included.
OpenAT's own code is released into the public domain ([LICENSE](LICENSE)).

Thanks to the `openairlinetycoon` community projects (OpenATDeluxe format notes, AirlineTycoon SDL port)
whose documentation of the file formats helped.
