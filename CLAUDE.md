# CLAUDE.md — OpenAT

Open-source Unity 6 (6000.4.3f1, URP) reimplementation of Airline Tycoon Deluxe, with widescreen
support. Read `docs/ARCHITECTURE.md`, `docs/STATUS.md` and `docs/spec/README.md` first.

## Hard rules

- The original game (`C:\Program Files (x86)\GOG Galaxy\Games\Airline Tycoon Deluxe`, env
  `OPENAT_GAME_DIR`) is **read-only**. Never write into it.
- Never commit game assets or anything decoded from them (graphics, sounds, texts, tables,
  screenshots of game graphics). `docs/spec/local/` is gitignored local reference.
- No verbatim original game text in code, tests or docs; read it at run time. Fallbacks in our own words.
- Clean room: implement from the behaviour specs in `docs/spec/findings/` (own words, formulas). The
  original source (`docs/spec/local/gog_src`, non-free, property of Black Forest Games) is reference
  only: never copy or transcribe its code. Numbers/constants/coordinates are facts and may be used.
- `OpenAT.Formats` and `OpenAT.Sim` have `noEngineReferences: true` — no `UnityEngine` there.
  C# 9, netstandard2.1 API surface.
- Sim is deterministic: `SimRandom` only, no wall-clock, no `Dictionary` iteration order dependence.
- Extensibility is a design goal: rules behind interfaces, systems in a pipeline, actions as
  commands, content in registries, rooms in a registry (see ARCHITECTURE.md "Extensibility").
- Original behaviour by default, except bugs: the original's bugs are fixed by default (the original
  behaviour stays available behind a `GameOptions` switch, see docs/QUIRKS.md). Additions behind
  `GameOptions` switches, default off.

## Build & test

- `dotnet test tests/OpenAT.Tests --artifacts-path <scratch>` (artifacts outside the repo).
- `dotnet run --project tools/OpenAT.Cli -- scan` parses every asset of the install.
- Unity editor may be open; it recompiles on file change. Batch mode needs the editor closed.
