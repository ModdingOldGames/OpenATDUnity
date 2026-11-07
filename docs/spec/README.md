# Behaviour specs (clean room)

Goal: OpenAT behaves like the original Airline Tycoon Deluxe but contains none of its code or assets.

## Rules

1. Behaviour is written down in `docs/spec/findings/*.md` **in our own words**: what happens, when,
   formulas, constants, tables of numbers, confidence. Source locations (`File.cpp: Function`) may be
   cited for traceability.
2. Never paste original source code (or a line-by-line transcription of it) into the repository.
   Short formula notation (`interest = credit * rate / 100`) is fine.
3. Implementation (`Assets/OpenAT/Sim`, `Assets/OpenAT/View`) is written from the findings.
4. `docs/spec/local/` (gitignored) holds local reference material: the GOG source release, the community
   SDL port, the manual, extracted files. Never commit anything from it.
5. Numbers are facts (constants, tables, coordinates) and may be used in code. Original texts
   (dialogue, messages, names) are read at run time from the user's install.

## Sources

- GOG source release of ATD (bonus content of the GOG edition; "property of Black Forest Games GmbH,
  free for non-commercial use") — reference only.
- Community SDL port `openairlinetycoon/AirlineTycoon` (same license) — its re-implemented graphics
  library documents the file formats.
- `openairlinetycoon/OpenATDeluxe` (GPL-3, Godot remake, unfinished) — `FORMATS.md`. Credited.
- The official manual (UI layout reference).

## Formats

See `docs/spec/formats/`.
