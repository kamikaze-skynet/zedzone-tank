"""Builds the Battle Tank in Blender: an M1A1 Abrams-style main battle tank (our own model) for the mod.

Run headless:  blender -b -P blender/build_abrams.py
   or with the bpy module (pip install bpy):  python blender/build_abrams.py
An output folder can be given after "--" (default blender/out). blender/build.ps1 does it all on Windows.

Outputs in the output folder (the .obj/.mtl are also copied to src/model3d, which the plugin embeds):
  tank_hull.obj / tank_turret.obj  (+ .mtl)   loaded by the plugin (src/Model3D.cs)
  tank_model.blend                             the editable scene
  preview_*.png                                quick renders
Coordinates: metres, +X = front of the tank, +Y = left, +Z = up. The turret's pivot (ring
centre) is the turret object's origin; the hull origin is the ground point under the ring."""
import math, os, sys
import bpy, bmesh
from mathutils import Vector, Matrix

argv = sys.argv
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
if "--" in argv and len(argv) > argv.index("--") + 1:
    out = argv[argv.index("--") + 1]
elif "--" not in argv and len(argv) > 1 and not argv[1].startswith("-"):
    out = argv[1]
else:
    out = os.path.join(HERE, "out")
out = os.path.abspath(out)
os.makedirs(out, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scn = bpy.context.scene

# ------------------------------------------------------------------ materials
def mat(name, rgb, rough=0.7, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    m.diffuse_color = (*rgb, 1)
    return m

def srgb(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)

M = {
    "Olive":  mat("Olive", srgb("5b6236")),
    "Olive2": mat("Olive2", srgb("4a5030")),
    "Tan":    mat("Tan", srgb("9a8a5a")),
    "Dark":   mat("Dark", srgb("2a2a26"), 0.9),
    "Steel":  mat("Steel", srgb("3c3e3a"), 0.45, 0.6),
    "Rubber": mat("Rubber", srgb("1c1c1a"), 0.95),
    "Glass":  mat("Glass", srgb("1a2a30"), 0.2),
    "Light":  mat("Light", srgb("e8e2c0"), 0.3),
}

parts = {"hull": [], "turret": []}

def link(obj, group, m):
    obj.data.materials.clear()
    obj.data.materials.append(M[m])
    parts[group].append(obj)
    return obj

def box(group, m, size, loc, rot=(0, 0, 0), bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.object
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if bevel > 0:
        md = o.modifiers.new("b", "BEVEL"); md.width = bevel; md.segments = 1
    return link(o, group, m)

def cyl(group, m, r, depth, loc, rot=(0, 0, 0), verts=16, r2=None):
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=depth, location=loc, rotation=rot, vertices=verts)
    else:
        bpy.ops.mesh.primitive_cone_add(radius1=r, radius2=r2, depth=depth, location=loc, rotation=rot, vertices=verts)
    return link(bpy.context.object, group, m)

def hull_from_points(group, m, pts, bevel=0.0):
    me = bpy.data.meshes.new("h")
    bm = bmesh.new()
    for p in pts: bm.verts.new(p)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("h", me)
    scn.collection.objects.link(o)
    if bevel > 0:
        md = o.modifiers.new("b", "BEVEL"); md.width = bevel; md.segments = 1
    return link(o, group, m)

def prism(x0, x1, w, zs):
    """points of a hull section from a list of (x, z_bottom, z_top) for full width w"""
    pts = []
    for x, zb, zt in zs:
        for y in (-w / 2, w / 2):
            pts += [(x, y, zb), (x, y, zt)]
    return pts


RX = math.pi / 2
RY = math.pi / 2
M["Sand"] = mat("Sand", srgb("b8a073"))
M["Sand2"] = mat("Sand2", srgb("a28c62"))
M["Sand3"] = mat("Sand3", srgb("8c7853"))

def bev(o, w, seg=2, angle=40):
    md = o.modifiers.new("b", "BEVEL"); md.width = w; md.segments = seg
    md.limit_method = "ANGLE"; md.angle_limit = math.radians(angle)
    return o

def boxb(group, m, size, loc, rot=(0, 0, 0), w=0.02, seg=2):
    return bev(box(group, m, size, loc, rot), w, seg)

def torus(group, m, R, r, loc, rot=(0, 0, 0), maj=32, mnr=8):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, location=loc, rotation=rot,
                                     major_segments=maj, minor_segments=mnr)
    return link(bpy.context.object, group, m)

def bolt(group, loc, axis="z", r=0.022, h=0.03, m="Sand3"):
    rot = {"z": (0, 0, 0), "y": (RX, 0, 0), "x": (0, RY, 0)}[axis]
    return cyl(group, m, r, h, loc, rot, 6)

def ring_of(n, R, fn):
    for k in range(n):
        a = 2 * math.pi * k / n
        fn(a, math.cos(a) * R, math.sin(a) * R)

def tube(group, m, pts, r, verts=8):
    """a bent rail through a list of points"""
    for a, b in zip(pts, pts[1:]):
        a, b = Vector(a), Vector(b)
        d = b - a
        bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=d.length, location=(a + b) / 2, vertices=verts)
        o = bpy.context.object
        o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        link(o, group, m)
    for p in pts:
        bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=p, segments=verts, ring_count=4)
        link(bpy.context.object, group, m)

def hull2d(points):
    pts = sorted(set(points))
    def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, up = [], []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0: lo.pop()
        lo.append(p)
    for p in reversed(pts):
        while len(up) >= 2 and cross(up[-2], up[-1], p) <= 0: up.pop()
        up.append(p)
    return lo[:-1] + up[:-1]

def belt(circles, pitch, pad):
    """track links around a set of wheels (x, z, r): convex outline, pushed out by pad, resampled"""
    pts = []
    for (cx, cz, r) in circles:
        for k in range(72):
            a = 2 * math.pi * k / 72
            pts.append((round(cx + math.cos(a) * (r + pad), 4), round(cz + math.sin(a) * (r + pad), 4)))
    poly = hull2d(pts)
    per = [math.dist(poly[i], poly[(i + 1) % len(poly)]) for i in range(len(poly))]
    total = sum(per); n = int(total / pitch); step = total / n
    out, i, t = [], 0, 0.0
    for k in range(n):
        s = k * step
        acc = 0.0
        for i in range(len(poly)):
            if acc + per[i] >= s: break
            acc += per[i]
        a, b = poly[i], poly[(i + 1) % len(poly)]
        f = (s - acc) / per[i] if per[i] else 0
        x, z = a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f
        out.append((x, z, math.atan2(b[1] - a[1], b[0] - a[0])))
    return out

# ------------------------------------------------------------------ hull (M1A1-style proportions, metres)
W = 3.66
HW = 2.25
GX = 0.25      # turret ring centre, in front of the hull's middle
DECK = 1.72
# lower hull between the tracks: long shallow glacis, flat deck, sloped rear
bev(hull_from_points("hull", "Sand", prism(0, 0, HW, [
    (4.05, 0.72, 1.12), (3.45, 0.42, 1.3), (1.95, 0.42, DECK), (-3.8, 0.42, DECK), (-3.98, 0.62, DECK - 0.04)])), 0.03, 2)
# sponsons over the tracks (the deck and glacis continue out to the skirts)
bev(hull_from_points("hull", "Sand", prism(0, 0, W - 0.16, [
    (3.98, 1.3, 1.14), (1.95, 1.3, DECK), (-3.95, 1.3, DECK), (-3.98, 1.35, DECK - 0.04)])), 0.025, 2)
# glacis details: driver's hatch with three periscopes, hull lifting eyes, headlights with guards
cyl("hull", "Sand2", 0.38, 0.06, (2.05, 0, DECK - 0.04), (0, 0.27, 0), 40)
cyl("hull", "Sand", 0.33, 0.06, (2.05, 0, DECK + 0.0), (0, 0.27, 0), 40)
for i in (-1, 0, 1):
    boxb("hull", "Sand2", (0.14, 0.2, 0.1), (2.5, i * 0.24, DECK - 0.1), (0, 0.27, 0), w=0.012)
    box("hull", "Glass", (0.02, 0.15, 0.06), (2.58, i * 0.24, DECK - 0.11), (0, 0.27 - 0.6, 0))
for y in (-1.3, 1.3):
    torus("hull", "Sand3", 0.07, 0.02, (2.6, y, DECK - 0.15), (RX, 0, 0), 12, 5)
for y in (-1.5, 1.5):
    boxb("hull", "Sand2", (0.22, 0.3, 0.16), (3.75, y, 1.28), w=0.02)
    cyl("hull", "Light", 0.065, 0.03, (3.87, y - 0.06, 1.3), (0, RY, 0), 16)
    cyl("hull", "Light", 0.045, 0.03, (3.87, y + 0.07, 1.27), (0, RY, 0), 12)
    tube("hull", "Sand3", [(3.62, y - 0.18, 1.2), (3.95, y - 0.18, 1.32), (3.95, y + 0.18, 1.32), (3.62, y + 0.18, 1.2)], 0.018)
    boxb("hull", "Sand3", (0.3, 0.12, 0.16), (4.08, y * 0.5, 0.82), w=0.02)
    torus("hull", "Sand3", 0.08, 0.025, (4.2, y * 0.5, 0.82), (RX, 0, 0), 16, 6)
# engine deck: big grille, access doors with handles, fuel caps
for g in range(2):
    gx = -2.55 - g * 0.85
    boxb("hull", "Sand2", (0.8, 2.0, 0.03), (gx, 0, DECK + 0.005), w=0.008)
    for k in range(14):
        box("hull", "Dark", (0.035, 1.9, 0.03), (gx - 0.36 + k * 0.055, 0, DECK + 0.02))
for x in (-0.95, -1.75):
    for y in (0.62, -0.62):
        boxb("hull", "Sand", (0.75, 0.95, 0.02), (x, y, DECK + 0.005), w=0.006)
        box("hull", "Sand3", (0.04, 0.2, 0.04), (x + 0.25, y, DECK + 0.03))
for y in (-1.55, 1.55):
    cyl("hull", "Sand3", 0.1, 0.05, (-3.5, y, DECK), verts=16)
# rear: turbine exhaust grille, tail lights, tow pintle, tow cable
boxb("hull", "Dark", (0.06, 1.7, 0.55), (-4.0, 0, 1.3), w=0.01)
for k in range(14):
    box("hull", "Sand3", (0.03, 1.65, 0.02), (-4.04, 0, 1.06 + k * 0.036))
for y in (-1.55, 1.55):
    boxb("hull", "Dark", (0.06, 0.2, 0.14), (-4.0, y, 1.5), w=0.008)
    box("hull", "Light", (0.02, 0.08, 0.06), (-4.04, y + 0.04, 1.5))
cyl("hull", "Sand3", 0.08, 0.25, (-4.1, 0, 0.8), (0, RY, 0), 12)
torus("hull", "Sand3", 0.1, 0.025, (-4.22, 0, 0.8), (0, RY, 0), 16, 6)
tube("hull", "Steel", [(-4.03, -1.2, 1.62), (-4.06, -0.6, 1.55), (-4.06, 0.6, 1.55), (-4.03, 1.2, 1.62)], 0.025)

# running gear: 7 road wheels, raised rear sprocket, front idler, 2 support rollers, track links
for s in (-1, 1):
    yc = s * (W / 2 - 0.45)
    tw = 0.64
    wheels = [(2.75 - i * 0.92, 0.36, 0.32) for i in range(7)]
    idler = (3.55, 0.58, 0.32)
    sprocket = (-3.62, 0.78, 0.33)
    rollers = [(1.6, 1.18, 0.1), (-1.2, 1.18, 0.1)]
    for (x, z, a) in belt(wheels + [idler, sprocket] + rollers, 0.16, 0.05):
        boxb("hull", "Dark", (0.14, tw, 0.05), (x, yc, z), (0, -a, 0), w=0.01, seg=1)
        box("hull", "Steel", (0.05, 0.05, 0.1), (x, yc, z), (0, -a, 0))
        for dy in (-0.2, 0.2):
            box("hull", "Rubber", (0.1, 0.16, 0.07), (x, yc + dy, z), (0, -a, 0))
    for (x, z, r) in wheels:
        for dy in (-0.15, 0.15):
            cyl("hull", "Rubber", r, 0.2, (x, yc + dy, z), (RX, 0, 0), 32)
            cyl("hull", "Sand2", r - 0.06, 0.22, (x, yc + dy, z), (RX, 0, 0), 32)
            ring_of(8, r - 0.14, lambda a, cx, cz: cyl("hull", "Sand3", 0.035, 0.24, (x + cx, yc + dy, z + cz), (RX, 0, 0), 8))
        cyl("hull", "Sand3", 0.11, tw + 0.04, (x, yc, z), (RX, 0, 0), 20)
        ring_of(6, 0.07, lambda a, cx, cz: bolt("hull", (x + cx, yc + s * (tw / 2 + 0.03), z + cz), "y", 0.016, 0.04))
        box("hull", "Sand3", (0.55, 0.12, 0.1), (x - 0.25, s * (HW / 2 + 0.03), z + 0.2), (0, -0.35, 0))
    x, z, r = idler
    for dy in (-0.16, 0.16):
        cyl("hull", "Sand2", r, 0.12, (x, yc + dy, z), (RX, 0, 0), 32)
    ring_of(6, 0.18, lambda a, cx, cz: cyl("hull", "Dark", 0.06, tw, (x + cx, yc, z + cz), (RX, 0, 0), 12))
    cyl("hull", "Sand3", 0.1, tw + 0.05, (x, yc, z), (RX, 0, 0), 16)
    x, z, r = sprocket
    for dy in (-0.2, 0.2):
        cyl("hull", "Sand3", r - 0.03, 0.08, (x, yc + dy, z), (RX, 0, 0), 32)
        ring_of(11, r + 0.01, lambda a, cx, cz: box("hull", "Sand3", (0.08, 0.08, 0.11), (x + cx, yc + dy, z + cz), (0, -a, 0)))
    cyl("hull", "Sand2", 0.22, tw, (x, yc, z), (RX, 0, 0), 24)
    ring_of(10, 0.15, lambda a, cx, cz: bolt("hull", (x + cx, yc + s * (tw / 2 + 0.02), z + cz), "y", 0.02, 0.04))
    for (x, z, r) in rollers:
        cyl("hull", "Sand3", r, 0.3, (x, yc, z), (RX, 0, 0), 16)
    # skirts: heavy armoured panels at the front, thinner ones behind, bolted, with a dipped nose
    ys = s * (W / 2 - 0.02)
    for i in range(8):
        x0 = 3.6 - i * 0.92
        heavy = i < 3
        t = 0.2 if heavy else 0.1
        h = 0.95
        zc = 1.18
        p = boxb("hull", "Sand", (0.9, t, h), (x0 - 0.45, ys - s * (0.0 if heavy else 0.05), zc), w=0.02)
        for bx in (-0.35, 0.0, 0.35):
            bolt("hull", (x0 - 0.45 + bx, ys + s * (t / 2 - (0.0 if heavy else 0.05) + 0.01), zc + h / 2 - 0.08), "y")
    box("hull", "Rubber", (7.2, 0.03, 0.06), (0.0, ys + s * 0.06, 0.69))
    # fender top lip
    box("hull", "Sand2", (7.6, 0.25, 0.04), (0.0, s * (W / 2 - 0.12), DECK - 0.07))

# ------------------------------------------------------------------ turret (origin = ring centre on the deck)
T0 = DECK
bev(hull_from_points("turret", "Sand", [
    (2.35, -1.4, 0.06), (2.35, 1.4, 0.06), (2.0, -1.78, 0.06), (2.0, 1.78, 0.06),
    (-1.95, -1.86, 0.06), (-1.95, 1.86, 0.06), (-2.8, -1.62, 0.12), (-2.8, 1.62, 0.12),
    (1.45, -1.38, 0.8), (1.45, 1.38, 0.8), (1.15, -1.74, 0.8), (1.15, 1.74, 0.8),
    (-1.95, -1.8, 0.8), (-1.95, 1.8, 0.8), (-2.78, -1.58, 0.78), (-2.78, 1.58, 0.78)]), 0.045, 3)
cyl("turret", "Sand2", 1.15, 0.1, (0, 0, 0.02), verts=48)
# front: recessed gun slot and mantlet shield
box("turret", "Dark", (0.12, 0.78, 0.5), (2.3, 0, 0.36))
boxb("turret", "Sand2", (0.3, 0.66, 0.44), (2.38, 0, 0.38), w=0.04)
for k in range(3):
    cyl("turret", "Dark", 0.24 - k * 0.02, 0.05, (2.52 + k * 0.06, 0, 0.42), (0, RY, 0), 24)
# 120 mm smoothbore with thermal sleeve, bore evacuator, muzzle reference sensor
cyl("turret", "Sand", 0.135, 2.0, (3.55, 0, 0.42), (0, RY, 0), 32)
cyl("turret", "Sand", 0.19, 0.62, (4.85, 0, 0.42), (0, RY, 0), 32)
cyl("turret", "Sand", 0.19, 0.1, (4.49, 0, 0.42), (0, RY, 0), 32, r2=0.135)
cyl("turret", "Sand", 0.135, 0.1, (5.21, 0, 0.42), (0, RY, 0), 32, r2=0.19)
cyl("turret", "Sand", 0.125, 2.3, (6.39, 0, 0.42), (0, RY, 0), 32)
for x in (2.9, 3.35, 3.8, 4.25, 5.5, 5.95, 6.4, 6.85, 7.25):
    torus("turret", "Sand2", 0.138, 0.018, (x, 0, 0.42), (0, RY, 0), 32, 6)
cyl("turret", "Sand3", 0.14, 0.12, (7.6, 0, 0.42), (0, RY, 0), 32)
cyl("turret", "Dark", 0.06, 0.14, (7.63, 0, 0.42), (0, RY, 0), 16)
boxb("turret", "Sand2", (0.12, 0.08, 0.1), (7.42, 0, 0.6), w=0.01)
cyl("turret", "Sand3", 0.015, 0.1, (7.42, 0, 0.54), verts=6)
# coaxial machine gun port
cyl("turret", "Dark", 0.045, 0.3, (2.45, 0.5, 0.48), (0, RY, 0), 12)
# gunner's primary sight "doghouse" (front right of the roof)
bev(hull_from_points("turret", "Sand", [
    (1.55, -0.6, 0.78), (1.55, -1.25, 0.78), (0.6, -0.6, 0.78), (0.6, -1.25, 0.78),
    (1.3, -0.6, 1.2), (1.3, -1.25, 1.2), (0.6, -0.6, 1.2), (0.6, -1.25, 1.2)]), 0.03, 2)
box("turret", "Glass", (0.02, 0.5, 0.28), (1.44, -0.92, 1.0), (0, -0.25, 0))
box("turret", "Sand3", (0.25, 0.04, 0.3), (1.45, -0.6, 1.0))
box("turret", "Sand3", (0.25, 0.04, 0.3), (1.45, -1.25, 1.0))
# commander's cupola (right rear) with vision blocks and the .50 cal
cx, cy = -0.55, -0.95
cyl("turret", "Sand2", 0.5, 0.28, (cx, cy, 0.92), verts=40)
cyl("turret", "Sand", 0.44, 0.08, (cx, cy, 1.1), verts=40)
torus("turret", "Sand2", 0.44, 0.025, (cx, cy, 1.15), maj=40, mnr=8)
ring_of(6, 0.5, lambda a, x, y: (boxb("turret", "Sand2", (0.12, 0.17, 0.12), (cx + x, cy + y, 0.95), (0, 0, a), w=0.01),
                                  box("turret", "Glass", (0.02, 0.12, 0.07), (cx + x * 1.13, cy + y * 1.13, 0.96), (0, 0, a))))
box("turret", "Sand3", (0.08, 0.08, 0.3), (cx + 0.2, cy, 1.25))
boxb("turret", "Dark", (0.55, 0.14, 0.17), (cx + 0.45, cy, 1.42), w=0.015)
cyl("turret", "Dark", 0.045, 1.15, (cx + 1.25, cy, 1.44), (0, RY, 0), 12)
cyl("turret", "Dark", 0.06, 0.16, (cx + 1.85, cy, 1.44), (0, RY, 0), 12)
for k in range(6):
    torus("turret", "Dark", 0.05, 0.008, (cx + 0.8 + k * 0.08, cy, 1.44), (0, RY, 0), 12, 4)
boxb("turret", "Sand2", (0.25, 0.2, 0.2), (cx + 0.3, cy + 0.17, 1.33), w=0.015)
box("turret", "Dark", (0.06, 0.04, 0.2), (cx + 0.2, cy, 1.55))
# loader's hatch (left) with the M240 on a skate ring
lx, ly = -0.45, 0.9
cyl("turret", "Sand2", 0.42, 0.07, (lx, ly, 0.82), verts=40)
cyl("turret", "Sand", 0.36, 0.06, (lx, ly, 0.88), verts=40)
torus("turret", "Sand3", 0.46, 0.025, (lx, ly, 0.9), maj=40, mnr=6)
box("turret", "Sand3", (0.07, 0.07, 0.3), (lx + 0.3, ly + 0.2, 1.02))
boxb("turret", "Dark", (0.55, 0.11, 0.13), (lx + 0.5, ly + 0.2, 1.2), w=0.012)
cyl("turret", "Dark", 0.03, 0.6, (lx + 1.05, ly + 0.2, 1.22), (0, RY, 0), 10)
box("turret", "Dark", (0.15, 0.03, 0.25), (lx + 0.4, ly + 0.14, 1.1))
# wind sensor mast, antenna bases and whips, rear roof rails, lifting eyes, roof bolts
cyl("turret", "Sand3", 0.02, 0.45, (-1.9, 0.0, 1.0), verts=6)
cyl("turret", "Sand3", 0.04, 0.12, (-1.9, 0.0, 1.25), verts=10)
for y in (-1.35, 1.35):
    cyl("turret", "Sand3", 0.06, 0.12, (-2.45, y, 0.84), verts=12)
    cyl("turret", "Dark", 0.012, 2.4, (-2.45, y, 2.05), verts=6)
for s in (-1, 1):
    tube("turret", "Sand3", [(-0.9, s * 1.55, 0.8), (-0.9, s * 1.55, 0.95), (-2.6, s * 1.45, 0.95), (-2.6, s * 1.45, 0.8)], 0.022)
for (x, y) in [(1.3, 1.5), (1.3, -1.5), (-2.5, 1.35), (-2.5, -1.35)]:
    torus("turret", "Sand3", 0.07, 0.016, (x, y, 0.83), (RX, 0, 0), 12, 5)
for x in (0.3, -1.2, -2.2):
    for y in (-0.25, 0.25):
        bolt("turret", (x, y, 0.8))
# smoke grenade launchers: six tubes on each front cheek
for s in (-1, 1):
    boxb("turret", "Sand2", (0.3, 0.42, 0.2), (1.6, s * 1.72, 0.62), w=0.015)
    for i in range(6):
        cyl("turret", "Dark", 0.05, 0.3, (1.75, s * (1.56 + (i % 3) * 0.11), 0.68 + (i // 3) * 0.1), (0, RY * 0.65, s * 0.5), 16)
# bustle rack (stowage basket) around the turret rear, loaded
z0, z1 = 0.25, 0.75
corners = [(-2.4, 1.95), (-3.45, 1.75), (-3.45, -1.75), (-2.4, -1.95)]
for z in (z0, (z0 + z1) / 2, z1):
    tube("turret", "Sand3", [(x, y, z) for x, y in corners], 0.02, 6)
for (x, y) in corners:
    cyl("turret", "Sand3", 0.02, z1 - z0, (x, y, (z0 + z1) / 2), verts=6)
for k in range(1, 8):
    f = k / 8
    y = 1.75 - 3.5 * f
    cyl("turret", "Sand3", 0.015, z1 - z0, (-3.45, y, (z0 + z1) / 2), verts=6)
box("turret", "Dark", (1.0, 3.6, 0.03), (-2.95, 0, z0))
boxb("turret", "Sand3", (0.6, 0.7, 0.35), (-2.95, -1.0, 0.45), w=0.04)
boxb("turret", "Sand2", (0.7, 0.9, 0.3), (-3.0, 0.2, 0.42), w=0.09, seg=3)
cyl("turret", "Sand3", 0.16, 1.0, (-3.0, 1.15, 0.45), (RX, 0, 0), 16)
for y in (-1.55, -1.35):
    boxb("turret", "Sand2", (0.35, 0.14, 0.45), (-3.1, y, 0.5), w=0.02)
# turret side stowage boxes
for s in (-1, 1):
    boxb("turret", "Sand2", (0.9, 0.18, 0.4), (-1.6, s * 1.92, 0.45), w=0.02)
    box("turret", "Sand3", (0.2, 0.03, 0.04), (-1.6, s * 2.02, 0.55))

# ------------------------------------------------------------------ join, place, export
def join(objs, name):
    for o in objs:
        for md in list(o.modifiers):
            bpy.context.view_layer.objects.active = o
            bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.object
    o.name = name
    return o

hull = join(parts["hull"], "TankHull")
turret = join(parts["turret"], "TankTurret")
# hull: origin at the ground point under the turret ring
hull.location = (-GX, 0, 0)
bpy.ops.object.select_all(action="DESELECT"); hull.select_set(True)
bpy.context.view_layer.objects.active = hull
bpy.ops.object.transform_apply(location=True)
turret.location = (0, 0, T0)   # sits on the deck above the ring
for o in (hull, turret):
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))

def export(o, path):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    loc = o.location.copy(); o.location = (0, 0, 0)
    bpy.ops.wm.obj_export(filepath=path, export_selected_objects=True, forward_axis="X", up_axis="Z",
                          export_materials=True, export_triangulated_mesh=True, export_normals=True, export_uv=False, export_smooth_groups=False,
                          apply_modifiers=True)
    o.location = loc

export(hull, os.path.join(out, "tank_hull.obj"))
export(turret, os.path.join(out, "tank_turret.obj"))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "tank_model.blend"))
import shutil
dst = os.path.join(REPO, "src", "model3d")
os.makedirs(dst, exist_ok=True)
for n in ("tank_hull.obj", "tank_hull.mtl", "tank_turret.obj", "tank_turret.mtl"):
    shutil.copyfile(os.path.join(out, n), os.path.join(dst, n))

# ------------------------------------------------------------------ previews (Cycles on the CPU, works headless)
scn.render.engine = "CYCLES"
scn.cycles.device = "CPU"
scn.cycles.samples = 24
scn.cycles.use_denoising = False
scn.render.resolution_x, scn.render.resolution_y = 900, 600
world = bpy.data.worlds.new("w"); scn.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.52, 0.5, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.data.energy = 3.5
sun.rotation_euler = (math.radians(40), math.radians(15), math.radians(-30))
scn.collection.objects.link(sun)
bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
bpy.context.object.data.materials.append(mat("Ground", srgb("6b6658"), 0.95))
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scn.collection.objects.link(cam); scn.camera = cam
cam.data.type = "ORTHO"
turret.rotation_euler = (0, 0, math.radians(25))
for name, ang, tilt, size in [("preview_iso", 35, 50, 13), ("preview_top", 0, 89.9, 13), ("preview_game45", 270, 45, 12)]:
    a, t = math.radians(ang), math.radians(tilt)
    d = Vector((math.cos(t) * math.cos(a), math.cos(t) * math.sin(a), math.sin(t)))
    cam.location = Vector((0.8, 0, 1.2)) + d * 30
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam.data.ortho_scale = size
    scn.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)
print("Tank model built in", out)
