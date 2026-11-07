"""Turns the Blender layers of the DualShock 4 (ds4_model.py) into pixel-art sprites for the overlay.

python tools/virtualpad/blender/pixelate.py <render_dir> <out_dir>

Each layer is reduced 4:1 (premultiplied box filter), its alpha made hard, and its colours mapped to one palette
shared by all layers (no dithering), like the game's pre-rendered sprites. The base gets a one-pixel dark
outline. Every layer is cropped to its opaque pixels and written as <name>.bytes (PNG data, loaded as a
TextAsset); index.txt lists "name x y width height" (position in the 200 x 124 frame). preview.png shows the
assembled controller at 4x.
"""
import os
import sys

from PIL import Image

SRC, DST = sys.argv[1], sys.argv[2]
FACTOR = 4
COLORS = 64
OUTLINE = (6, 6, 9, 255)
os.makedirs(DST, exist_ok=True)

layers = {}
for f in sorted(os.listdir(SRC)):
    if not f.endswith(".png"):
        continue
    im = Image.open(os.path.join(SRC, f)).convert("RGBA")
    small = im.convert("RGBa").reduce(FACTOR).convert("RGBA")
    px = small.load()
    for y in range(small.height):
        for x in range(small.width):
            r, g, b, a = px[x, y]
            px[x, y] = (r, g, b, 255) if a >= 128 else (0, 0, 0, 0)
    layers[f[:-4]] = small

# One palette for all layers.
opaque = [p for im in layers.values() for p in im.getdata() if p[3]]
# Saturated pixels (button symbols, glows) are few; weight them so they keep their own palette entries.
opaque += [p for p in opaque if max(p[:3]) - min(p[:3]) > 40] * 40
strip = Image.new("RGB", (len(opaque), 1))
strip.putdata([p[:3] for p in opaque])
pal = strip.quantize(colors=COLORS, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)


def to_palette(im):
    rgb = im.convert("RGB").quantize(palette=pal, dither=Image.Dither.NONE).convert("RGB")
    out = Image.new("RGBA", im.size)
    a = im.getchannel("A")
    out.paste(rgb, (0, 0), a)
    return out


def outline(im):
    out = im.copy()
    src, dst = im.load(), out.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            if src[x, y][3]:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and src[nx, ny][3]:
                    dst[x, y] = OUTLINE
                    break
    return out


index = []
for name, im in layers.items():
    im = to_palette(im)
    if name == "base":
        im = outline(im)
    if name == "lightbar":   # tinted in the game: keep it white with its shape
        im = Image.new("RGBA", im.size, (255, 255, 255, 0)); im.paste((255, 255, 255, 255), (0, 0), layers[name].getchannel("A"))
    box = im.getbbox()
    if box is None:
        continue
    crop = im.crop(box)
    crop.save(os.path.join(DST, name + ".bytes"), format="PNG")
    index.append("%s %d %d %d %d" % (name, box[0], box[1], crop.width, crop.height))
    layers[name] = (im, box)

with open(os.path.join(DST, "index.txt"), "w", newline="\n") as f:
    f.write("\n".join(index) + "\n")

# Preview: rest state, and with some parts pressed.
w, h = layers["base"][0].size
prev = Image.new("RGBA", (w * 2, h), (40, 70, 110, 255))
for i, extra in enumerate(([], ["cross", "dleft", "r1", "lcap_p"])):
    frame = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    for n in ["base", "lightbar"] + extra + [c for c in ("lcap", "rcap") if c + "_p" not in extra]:
        frame.alpha_composite(layers[n][0])
    prev.alpha_composite(frame, (i * w, 0))
prev.resize((prev.width * 4, prev.height * 4), Image.NEAREST).save(os.path.join(DST, "..", "preview.png") if False else os.path.join(SRC, "preview.png"))
print("sprites:", len(index), "->", DST)
