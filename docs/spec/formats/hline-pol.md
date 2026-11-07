# HLine sprite pools (`gli/*.pol`)

The people in the airport hall are stored as palette-indexed sprites made of horizontal runs of
opaque pixels ("HLines"). A pool file holds a set of sprites ("objects") plus one shared byte
array of pixel indices. Long runs don't keep their own pixels: they point into that byte array,
or into the byte array of up to two *base* pools. When the original encoder found an identical
or near-identical run that was already stored, it reused it, so frames of one person share most
of their pixel data.

Reader: `Assets/OpenAT/Formats/HLinePool.cs` (`OpenAT.Formats.HLinePool`).

## Files

| File | Contents |
|---|---|
| `skelett.pol` | X-ray images ("Skelett") shown at the security check. Standalone (no base pools), own palette. 108 objects. |
| `clan<N>.pol` | All frames of person type ("clan") *N*, except the two gimmick animations. |
| `clang<N>.pol` | The clan's walking gimmick (phase list 4), e.g. police, cake, explosions. |
| `clanw<N>.pol` | The clan's waiting gimmick (phase list 9). |

*N* is the clan's slot in the game's clan album, **not** its CSV row: the 90-slot album
(`MAX_CLANS`) is filled from the end, so the *k*-th clan record in `data/clan.csv` (0-based) is
slot `89 - k`. The shipped `clan.csv` has 89 records, which gives slots 1..89. Slot 32 has no pool
files because its art does not exist, and `clanw37.pol` is also absent. `gli/glclan.gli` is the
uncompressed GLIB library the pools were built from; it ships with the game and has the same
names and sizes.

## Byte layout

All integers are little-endian. "Array" means the engine's generic serialised buffer:
`int32 count`, `int32 cursor` (an internal position; ignore it), then `count` elements.

```
Header (28 bytes)
  int32 poolSize            number of bytes in the pixel-index pool below
  int32 poolCapacity        allocation size used by the encoder (>= poolSize, informational)
  int32 bytesReal           statistics written by the encoder (uncompressed size,
  int32 bytesCompressed       compressed size, overhead, runs stored, runs reused);
  int32 bytesAdministration   not needed for decoding
  int32 linesInPool
  int32 linesRepeated
Array of objects
  per object:
    uint64 graphicId        sprite name, see "Ids"
    int32  width
    int32  height
    Array  runs             6 bytes per element:
             uint8  x         start column within the row
             uint8  length    run length in pixels (1..255)
             uint32 data      inline pixels or pool reference (see below)
    Array  runsPerRow       1 byte per element, count == height
uint8[poolSize]            pixel-index pool
uint16[256]                palette, RGB565
```

Nothing follows the palette. For example, `clan1.pol` is 725 bytes: a 28-byte header, a 1-object
array (`STINK`, 21x10, 12 runs, 10 row counts), a 63-byte pool and a 512-byte palette.

### Rows and runs

The rows are stored top to bottom. Row *y* uses the next `runsPerRow[y]` runs in order: runs are
consumed sequentially across the whole object, and the sum of `runsPerRow` equals the run count.
That holds for every object in all 264 shipped files. Pixels that no run covers are transparent.
A run never contains palette index 0. The original encoder treated index 0 as transparent and
split runs there.

The `data` field of a run:

* **length <= 4**: the run's palette indices are stored inline. The first pixel is the
  least-significant byte, and unused high bytes are junk or zero.
* **length > 4**: a pool reference. The high nibble selects the pool and the low 28 bits are the
  byte offset into it:
  * `0x0…`: this file's own pool
  * `0x1…`: base pool 1
  * `0x2…`: base pool 2

  The run's `length` index bytes are read starting at that offset.

### Palette and colour

Each pool stores 256 RGB565 values (blue in bits 0-4, green in bits 5-10, red in bits 11-15).
The encoder built them from the clan's `.LBM` palette (`PalFilename` column of clan.csv) or
from `Skel_pal.lbm` for the skeletons. When it loads them, the game drops the lowest green bit
(it only keeps 5 bits per channel) before converting to the screen format. Index 0 is never
drawn. Pixels from a base pool are always coloured with the **referencing** pool's palette.
Base pools are only used between clans with the same palette file, so this makes no difference
in practice.

The encoder was lossy. Source pixels were quantised to the palette, and a stored run was
reused when every pixel was within a small colour distance (quality 95 %: a summed RGB
difference of about 38 for clans; 98 % for skeletons). When decoded frames are compared with
`glclan.gli`, the silhouettes and sizes match exactly, and the colour error per pixel averages
about 15/765.

`HLinePool.Decode` returns a `Bitmap16`. An opaque pixel whose colour is pure black is written as
`0x0020`, so it does not collide with the colour key 0.

### Ids

Each object id is the sprite name: up to 8 ASCII characters packed little-endian (first
character in the low byte, zero-padded), e.g. `SCHW_N01`, `B1KWN_01`, `SK_O01`. Lookups compare
the full 64-bit value. If an id appears twice, the game finds the last one.

## Base pools (how the files depend on each other)

The base pools have to be loaded first, and the order depends on clan.csv. Let *N* be a clan
slot and *N+1* the clan record just **before** it in the CSV:

* If slot *N+1* exists, has the same `PalFilename`, and clan *N* is not of type 30:
  * `clan<N>` uses base1 = `clan<N+1>`
  * `clang<N>` uses base1 = `clan<N+1>` and base2 = `clan<N>`
  * `clanw<N>` uses base1 = `clan<N+1>` and base2 = `clan<N>`
* Otherwise:
  * `clan<N>` has no base pools
  * `clang<N>` uses base1 = `clan<N>`
  * `clanw<N>` uses base1 = `clan<N>`. At runtime the game passes base2 = `clang<N>` here. Its
    pool-builder passed `clan<N>` instead, so no shipped file in this branch contains `0x2…`
    references.
* `skelett.pol` has no base pools.

Chains can be long (`clan<N>` → `clan<N+1>` → …), so build from the highest slot downwards.
With this setup, all 6806 existing frames referenced by clan.csv resolve completely.

Most `clang`/`clanw` files have an empty own pool (`poolSize` 0) and consist only of references
into the clan pools.

## How clan.csv selects frames

`data/clan.csv` is xtRLE-packed and `;`-separated. Empty fields are skipped, the way C `strtok`
does it. After a header line, each clan takes three lines: the person frames, the shadows and the
skeletons. The shadows are not in a `.pol`; they come from the standard GLI library (`SH_BIG`, …).

* Line 1, columns 0-12: type, group, comment, update flag, probability, walk speed, animation
  divisor ("Faktor"), suitcase, gimmick args ×2, sprite offset x/y, palette file.
* Line 2, columns 8-11: gimmick offset x/y and shadow offset x/y.
* Line 3, columns 10-11: skeleton offset x/y.

From column 13, each line has 14 phase lists in this CSV order:

| Lists | Columns per list | Meaning |
|---|---|---|
| walk N, E, S, W | 3 | the first column is ignored |
| run N, E, S, W | 3 | the first column is ignored |
| gimmick | 2 | |
| wave | 2 | |
| sit N | 2 | |
| sit S | 2 | |
| stand | 1 | |
| wait gimmick | 2 | |

A two- or three-column list ends in `name;count`:

* With a count, the names are `name` with its last character incremented `count`-1 times.
  The increment carries into the previous character after `9` (`X_08`, `X_09`, `X_10`).
* With count `-1`, the name field is a space-separated list of names.
* The single-column stand list is always a space-separated list. It holds 4 entries, one per
  direction.

The game's internal direction slots are: 0-3 walk, 4 gimmick, 5 wave, 6/7 sit, 8 stand, 9 wait
gimmick, 10-13 run. Slot 4 frames are looked up in `clang<N>.pol`, slot 9 frames in
`clanw<N>.pol`, all others in `clan<N>.pol`, and skeleton lists in `skelett.pol`. A few names in
the CSV do not exist in any pool or in glclan.gli; the game skips them.

## Placement (no hotspot in the file)

The pools store only width and height. The game anchors a person frame at its foot point
`(width/2, height-1)` on the person's screen position. It then adds the clan's sprite offset
from clan.csv. Gimmick frames also get the gimmick offset, and skeletons get the skeleton offset
(mirrored in x when facing west). The animation advances one frame every `Faktor` ticks while
walking and every `2*Faktor` ticks while running. There is also a 2× blit mode, which plain
pixel doubling reproduces.
