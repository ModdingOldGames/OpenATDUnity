"""Renders the layers of the pixel-art DualShock 4 for the controller overlay (docs/VIRTUALPAD.md).

Run:  blender -b -P tools/virtualpad/blender/ds4_model.py -- <out_dir>
Then: python tools/virtualpad/blender/pixelate.py <out_dir> Assets/OpenAT/View/UI/Resources/PadOverlay

A procedural model built from an outline traced off a front photo of the controller (coordinates in photo
pixels, origin on the centre line at the top edge, y down; 100 px = 1 Blender unit). It is rendered top-down
with an orthographic camera: one image of the whole controller at rest without the stick caps ("base"), and one
image per moving part (pressed buttons, stick caps), each alone on a transparent background in the same frame.
"""
import math
import os
import sys

import bmesh
import bpy

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "ds4_render"
SCALE = 4                      # rendered at 4x the final pixel size, then reduced by pixelate.py
FINAL_W, FINAL_H = 200, 124
TOP = 0.45                     # body top surface height


def P(x, y, z=0.0):
    return (x / 100.0, -y / 100.0, z)


# ------------------------------------------------------------------------------------------- scene

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    s = bpy.context.scene
    s.render.engine = "BLENDER_EEVEE"
    s.render.resolution_x = FINAL_W * SCALE
    s.render.resolution_y = FINAL_H * SCALE
    s.render.film_transparent = True
    s.render.image_settings.file_format = "PNG"
    s.render.image_settings.color_mode = "RGBA"
    s.view_settings.view_transform = "Standard"
    s.view_settings.look = "None"
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.08, 0.09, 0.11, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.22
    s.world = world

    cam = bpy.data.cameras.new("cam")
    cam.type = "ORTHO"
    cam.ortho_scale = 10.0      # photo width covered: 1000 px
    co = bpy.data.objects.new("cam", cam)
    co.location = (0, -3.0, 20)
    s.collection.objects.link(co)
    s.camera = co

    for name, rot, energy in (("key", (math.radians(35), math.radians(-25), 0), 2.6),
                              ("fill", (math.radians(-30), math.radians(30), 0), 0.5)):
        l = bpy.data.lights.new(name, "SUN")
        l.energy = energy
        lo = bpy.data.objects.new(name, l)
        lo.rotation_euler = rot
        s.collection.objects.link(lo)


def mat(name, color, rough=0.55, emit=None, strength=0.0, spec=0.35):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    if "Specular IOR Level" in b.inputs:
        b.inputs["Specular IOR Level"].default_value = spec
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = strength
    return m


def link(obj, material, group):
    obj.data.materials.append(material)
    bpy.context.scene.collection.objects.link(obj)
    obj["group"] = group
    return obj


def curve_obj(name, pts, cyclic=True, extrude=0.0, bevel=0.0, z=0.0, smooth=True, fill=True):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "2D" if fill else "3D"
    if fill:
        cu.fill_mode = "BOTH"
    cu.extrude = extrude
    cu.bevel_depth = bevel
    cu.bevel_resolution = 4
    cu.resolution_u = 16
    sp = cu.splines.new("BEZIER" if smooth else "POLY")
    if smooth:
        sp.bezier_points.add(len(pts) - 1)
        for bp, p in zip(sp.bezier_points, pts):
            bp.co = p
            bp.handle_left_type = bp.handle_right_type = "AUTO"
    else:
        sp.points.add(len(pts) - 1)
        for pp, p in zip(sp.points, pts):
            pp.co = (*p, 1)
    sp.use_cyclic_u = cyclic
    o = bpy.data.objects.new(name, cu)
    o.location.z = z
    return o


def mesh_obj(name, build):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    build(bm)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    return bpy.data.objects.new(name, me)


def cylinder(name, r, h, at, segs=40):
    o = mesh_obj(name, lambda bm: bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs,
                                                       radius1=r, radius2=r, depth=h))
    o.location = at
    bev = o.modifiers.new("b", "BEVEL")
    bev.width = min(r, h) * 0.35
    bev.segments = 3
    return o


def box(name, sx, sy, sz, at, rot=0.0, round_=0.08):
    o = mesh_obj(name, lambda bm: bmesh.ops.create_cube(bm, size=1.0))
    o.scale = (sx, sy, sz)
    o.location = at
    o.rotation_euler.z = rot
    bev = o.modifiers.new("b", "BEVEL")
    bev.width = round_
    bev.segments = 3
    bev.affect = "EDGES"
    return o


def ring_pts(cx, cy, r, n=28):
    return [P(cx + r * math.cos(2 * math.pi * i / n), cy + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def rounded_rect_pts(x0, y0, x1, y1, r, n=6):
    pts = []
    for cx, cy, a0 in ((x1 - r, y0 + r, -90), (x1 - r, y1 - r, 0), (x0 + r, y1 - r, 90), (x0 + r, y0 + r, 180)):
        for i in range(n + 1):
            a = math.radians(a0 + 90 * i / n)
            pts.append(P(cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


# ------------------------------------------------------------------------------------------- model

# Left half of the outline, from the top centre round the left grip to the bottom centre (photo px).
HALF = [(0, 30), (-150, 30), (-175, 44), (-240, 52), (-305, 58), (-350, 72), (-388, 104), (-414, 152),
        (-430, 222), (-444, 320), (-455, 415), (-452, 478), (-428, 528), (-386, 556), (-336, 552),
        (-298, 524), (-268, 474), (-238, 418), (-204, 380), (-150, 360), (-78, 350), (0, 347)]

DPAD = (-285, 192)
FACE = (285, 180)
FACE_R = 31
FACE_POS = {"triangle": (0, -76), "circle": (66, -8), "cross": (0, 58), "square": (-66, -8)}
LSTICK, RSTICK = (-140, 292), (142, 292)
SYMBOL = {"triangle": (0.25, 0.85, 0.65), "circle": (1.0, 0.33, 0.36), "cross": (0.45, 0.65, 1.0), "square": (0.95, 0.42, 0.9)}


def build():
    m_body = mat("body", (0.011, 0.012, 0.015), rough=0.42, spec=0.55)
    m_panel = mat("panel", (0.012, 0.013, 0.017), rough=0.75)
    m_btn = mat("btn", (0.03, 0.032, 0.038), rough=0.4, spec=0.5)
    m_btn_p = mat("btn_p", (0.1, 0.11, 0.14), rough=0.35, emit=(0.45, 0.55, 0.85), strength=0.3)
    m_grey = mat("grey", (0.16, 0.17, 0.19), rough=0.35, spec=0.6)
    m_grey_p = mat("grey_p", (0.35, 0.37, 0.42), rough=0.3, emit=(0.55, 0.65, 0.9), strength=0.25)
    m_cap = mat("cap", (0.07, 0.075, 0.085), rough=0.65)
    m_cap_p = mat("cap_p", (0.16, 0.17, 0.2), rough=0.5, emit=(0.45, 0.55, 0.85), strength=0.22)
    m_bar = mat("bar", (1, 1, 1), emit=(1, 1, 1), strength=2.5)

    full = [P(x, y) for x, y in HALF] + [P(-x, y) for x, y in reversed(HALF[1:-1])]
    link(curve_obj("body", full, extrude=0.3, bevel=0.15), m_body, "base")

    # L1 / R1 bumpers peeking out above the top edge.
    for side, n in ((-1, "l1"), (1, "r1")):
        at = P(side * 275, 44, 0.25)
        link(box(n, 1.25, 0.45, 0.3, at, rot=side * 0.12, round_=0.12), m_grey, "base")
        link(box(n + "_p", 1.25, 0.45, 0.3, (at[0], at[1], 0.15), rot=side * 0.12, round_=0.12), m_grey_p, n)

    # Recessed discs behind the d-pad and the face buttons.
    for c in (DPAD, FACE):
        link(cylinder("recess", 1.12, 0.04, P(c[0], c[1], TOP + 0.01), segs=64), m_panel, "base")

    # Touchpad, its press variant, and the light bar (tinted in the game).
    tp = rounded_rect_pts(-148, 34, 148, 192, 22)
    link(curve_obj("touchpad", tp, extrude=0.03, z=TOP + 0.05), m_panel, "base")
    link(curve_obj("touchpad_p", tp, extrude=0.03, z=TOP + 0.03), m_cap_p, "touchpad")
    link(curve_obj("lightbar", rounded_rect_pts(-115, 40, 115, 46, 3, 2), extrude=0.02, z=TOP + 0.12), m_bar, "lightbar")

    # D-pad arms.
    for n, (dx, dy, rot) in {"dup": (0, -46, 0), "ddown": (0, 46, 0), "dleft": (-46, 0, math.pi / 2), "dright": (46, 0, math.pi / 2)}.items():
        at = P(DPAD[0] + dx * 1.12, DPAD[1] + dy * 1.12, TOP + 0.12)
        link(box(n, 0.4, 0.44, 0.14, at, rot=rot, round_=0.07), m_btn, "base")
        link(box(n + "_p", 0.4, 0.44, 0.14, (at[0], at[1], TOP + 0.06), rot=rot, round_=0.07), m_btn_p, n)

    # Face buttons with their symbols.
    for n, (dx, dy) in FACE_POS.items():
        cx, cy = FACE[0] + dx, FACE[1] + dy
        col = SYMBOL[n]
        for pressed in (False, True):
            group = n if pressed else "base"
            z = TOP + (0.08 if pressed else 0.14)
            # Pressed: the button glows in its symbol's colour.
            face = mat(n + "_face_p", tuple(c * 0.3 for c in col), rough=0.35, emit=col, strength=0.55) if pressed else m_btn
            link(cylinder(n + ("_p" if pressed else ""), FACE_R / 100, 0.16, P(cx, cy, z)), face, group)
            sm = mat(n + "_sym" + ("_p" if pressed else ""), tuple(c * 0.5 for c in col), rough=0.4, emit=col,
                     strength=1.6 if pressed else 1.1)
            s = 15
            if n == "triangle":
                pts = [P(cx, cy - s), P(cx + s * 0.95, cy + s * 0.65), P(cx - s * 0.95, cy + s * 0.65)]
                link(curve_obj(n + "_sym", pts, bevel=0.022, z=z + 0.09, smooth=False, fill=False), sm, group)
            elif n == "circle":
                link(curve_obj(n + "_sym", ring_pts(cx, cy, s * 0.85), bevel=0.022, z=z + 0.09, fill=False), sm, group)
            elif n == "square":
                k = s * 0.72
                pts = [P(cx - k, cy - k), P(cx + k, cy - k), P(cx + k, cy + k), P(cx - k, cy + k)]
                link(curve_obj(n + "_sym", pts, bevel=0.022, z=z + 0.09, smooth=False, fill=False), sm, group)
            else:
                k = s * 0.8
                for i, (a, b) in enumerate(((P(cx - k, cy - k), P(cx + k, cy + k)), (P(cx + k, cy - k), P(cx - k, cy + k)))):
                    link(curve_obj(n + "_sym%d" % i, [a, b], cyclic=False, bevel=0.022, z=z + 0.09, smooth=False, fill=False), sm, group)

    # Share / Options pills and the PS button.
    for n, x in (("share", -186), ("options", 186)):
        link(box(n, 0.17, 0.36, 0.1, P(x, 72, TOP + 0.08), rot=0.25 * (1 if x < 0 else -1), round_=0.07), m_grey, "base")
        link(box(n + "_p", 0.17, 0.36, 0.1, P(x, 72, TOP + 0.04), rot=0.25 * (1 if x < 0 else -1), round_=0.07), m_grey_p, n)
    link(cylinder("ps", 0.15, 0.08, P(0, 300, TOP + 0.08)), m_grey, "base")
    link(cylinder("ps_p", 0.15, 0.08, P(0, 300, TOP + 0.04)), m_grey_p, "ps")

    # Speaker grille dots.
    for i in range(-3, 4):
        for j in range(2):
            if abs(i) == 3 and j == 1:
                continue
            link(cylinder("dot", 0.022, 0.02, P(i * 9 + (4.5 if j else 0), 222 + j * 9, TOP + 0.02), segs=8), m_panel, "base")

    # Stick wells (base) and caps (drawn at the rest position, moved in the game).
    for n, c in (("l", LSTICK), ("r", RSTICK)):
        link(cylinder("well", 0.78, 0.05, P(c[0], c[1], TOP + 0.01), segs=64), m_panel, "base")
        link(curve_obj("wellrim", ring_pts(c[0], c[1], 80, 48), bevel=0.06, z=TOP + 0.04, fill=False), m_body, "base")
        for pressed in (False, True):
            g = n + "cap" + ("_p" if pressed else "")
            z = TOP + (0.18 if pressed else 0.25)
            link(cylinder(g, 0.5, 0.22, P(c[0], c[1], z)), m_cap_p if pressed else m_cap, g)
            link(curve_obj(g + "rim", ring_pts(c[0], c[1], 47, 48), bevel=0.045, z=z + 0.11, fill=False), m_grey_p if pressed else m_grey, g)


def render(group, name):
    for o in bpy.context.scene.objects:
        g = o.get("group")
        if g is not None:
            o.hide_render = g != group
    s = bpy.context.scene
    s.render.filepath = os.path.join(os.path.abspath(OUT), name + ".png")
    bpy.ops.render.render(write_still=True)


reset()
build()
os.makedirs(OUT, exist_ok=True)
render("base", "base")
render("lightbar", "lightbar")
for n in ("cross", "circle", "square", "triangle", "dup", "ddown", "dleft", "dright", "l1", "r1",
          "share", "options", "ps", "touchpad", "lcap", "lcap_p", "rcap", "rcap_p"):
    render(n, n)
print("DS4 layers rendered to", os.path.abspath(OUT))
