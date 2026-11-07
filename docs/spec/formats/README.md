# File formats

All integers little-endian.

## xtRLE (`data/*.csv`, `misc/*.res`, some `misc/*.dat`)
- 6 bytes `xtRLE\0`, int32 version, int32 unpacked size (version >= 0x101).
- Runs: byte `n`. If `n & 0x80`: `n & 0x7F` literal bytes follow, each XOR `0xA5` (version >= 0x102).
  Otherwise the next byte is repeated `n` times.
- Reader: `Formats/XtRle.cs`.

## GLIB graphics library (`*.gli`, `*.glj`)
- `GLIB` magic, int32 header length, ..., int32 entry count @34, int32 directory offset @38.
- Directory entries: int32 entry size, byte type (1 = graphic, 2 = name, 3 = palette); for graphics
  8-byte zero-padded name + int32 offset of the image block.
- Image block: 76-byte header (int32 header length, int32 data size, int32 width, int32 height,
  ..., int32 bit depth @24, RGB masks @32..40), then `data size` bytes of RGB565 pixels,
  pitch = size / height.
- Colour value 0 is the transparency key.
- Reader: `Formats/GliLibrary.cs`.

## MCF bitmap fonts (`misc/*.mcf`)
- 44-byte header: u16 header size, u16 ?, u16 flags, u16 cell width, u16 cell height, i32 ?,
  i32 bit depth, u16 colour count, u16 ?, u16 first char, u16 last char, u16 ?, i32 pixel bytes,
  i32 palette bytes, i32 ?, i32 footer bytes.
- Pixel indices: one cell per character, stacked vertically; palette as BGRA quads; then a
  256-byte advance width table and a 256-byte height table, indexed by character code.
- Index whose colour is black is transparent. (`plain_r.mcf` has a different/encrypted layout; unused.)
- Reader: `Formats/McfFont.cs`.

## Text resources (`misc/*.res`)
- xtRLE packed text. `>TAG` starts a group, `>>ID` an entry (4-character tags are packed into an
  int little-endian, numbers parsed as decimal). Text lines start with two spaces and are
  concatenated. `//` comments.
- Multilingual texts: `X::text` segments, X from `DEFTPNISOBJKLMNQRTUV` (D German, E English, F French,
  ...). Segment ends at the next `Y::` marker. Fallback: English, then German.
- Byte `0xB5` inside a text is a forced line break.
- Reader: `Formats/TextResource.cs`.

## Sounds (`SOUND/*.raw`, `VOICE/**/*.raw`)
- Headerless signed 16-bit mono PCM at 22050 Hz. Music: standard MIDI (`*.mid`).

## Videos (`video/*.smk`, `intro/*.smk`)
- RAD Smacker (SMK2/SMK4). Reader: `Formats/Smacker.cs`.
