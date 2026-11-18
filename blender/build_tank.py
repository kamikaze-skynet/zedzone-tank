"""Builds the 3D main battle tank in Blender (our own design) and exports it for the mod.

Run headless:  blender -b -P blender/build_tank.py
   or with the bpy module (pip install bpy):  python blender/build_tank.py
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

def bolt(group, loc, axis="z", r=0.025, h=0.03, m="Steel"):
    rot = {"z": (0, 0, 0), "y": (RX, 0, 0), "x": (0, RY, 0)}[axis]
    return cyl(group, m, r, h, loc, rot, 6)

def ring_of(n, R, fn):
    for k in range(n):
        a = 2 * math.pi * k / n
        fn(a, math.cos(a) * R, math.sin(a) * R)

# ------------------------------------------------------------------ hull
L, W = 7.9, 3.6          # hull length, width (with skirts)
HW = 2.3                 # hull body width between tracks
GX = -0.3                # ring sits a little behind the hull's middle
bev(hull_from_points("hull", "Olive", prism(0, 0, HW, [
    (3.95, 0.75, 1.25), (3.3, 0.45, 1.9), (2.4, 0.45, 2.0), (-3.6, 0.45, 2.0), (-3.95, 0.7, 1.85)])), 0.04, 2)
bev(hull_from_points("hull", "Olive", prism(0, 0, W - 0.1, [
    (3.5, 1.45, 1.6), (2.2, 1.45, 1.98), (-3.75, 1.45, 1.98), (-3.95, 1.5, 1.9)])), 0.03, 2)
# glacis: applique armour plate with bolt rows
gl = boxb("hull", "Olive2", (1.05, 2.0, 0.07), (2.85, 0, 1.97), (0, 0.11, 0), 0.015)
for i in range(7):
    for j in (-1, 1):
        bolt("hull", (2.45 + i * 0.13, j * 0.95, 2.0 - (i - 3) * 0.045))
# driver's hatch with periscopes and hinge
cyl("hull", "Olive2", 0.36, 0.08, (2.45, 0, 2.03), verts=32)
cyl("hull", "Olive", 0.3, 0.06, (2.45, 0, 2.09), verts=32)
for i in range(3):
    boxb("hull", "Olive2", (0.12, 0.2, 0.12), (2.85, -0.24 + i * 0.24, 2.05), w=0.01)
    box("hull", "Glass", (0.02, 0.16, 0.08), (2.92, -0.24 + i * 0.24, 2.06), (0, -0.4, 0))
cyl("hull", "Steel", 0.04, 0.3, (2.12, 0, 2.06), (RX, 0, 0), 8)
# engine deck: louvre grilles (slats), access panels with handles, fuel caps
for g in range(3):
    gx = -1.9 - g * 0.6
    boxb("hull", "Olive2", (0.52, 1.9, 0.03), (gx, 0, 1.995), w=0.008)
    for k in range(9):
        box("hull", "Dark", (0.04, 1.8, 0.03), (gx - 0.22 + k * 0.055, 0, 2.02))
for x in (-0.6, -1.2):
    boxb("hull", "Olive", (0.5, 0.8, 0.025), (x, 0.55, 2.0), w=0.006)
    boxb("hull", "Olive", (0.5, 0.8, 0.025), (x, -0.55, 2.0), w=0.006)
    for y in (0.55, -0.55):
        box("hull", "Steel", (0.03, 0.18, 0.04), (x, y, 2.035))
for y in (-1.45, 1.45):
    cyl("hull", "Steel", 0.1, 0.05, (-3.4, y, 2.0), verts=16)
# rear: exhaust grille, tail lights, tow pintle, spare track links
boxb("hull", "Dark", (0.08, 1.6, 0.4), (-3.97, 0, 1.55), w=0.01)
for k in range(10):
    box("hull", "Steel", (0.03, 1.55, 0.02), (-4.02, 0, 1.38 + k * 0.035))
for y in (-1.5, 1.5):
    boxb("hull", "Dark", (0.06, 0.18, 0.12), (-3.98, y, 1.8), w=0.008)
    box("hull", "Light", (0.02, 0.07, 0.05), (-4.02, y + 0.04, 1.8))
cyl("hull", "Steel", 0.08, 0.25, (-4.08, 0, 0.95), (0, RY, 0), 12)
torus("hull", "Steel", 0.1, 0.025, (-4.2, 0, 0.95), (0, RY, 0), 16, 6)
for k in range(4):
    boxb("hull", "Dark", (0.06, 0.62, 0.17), (-3.99, 1.0, 1.0 + k * 0.19), w=0.01)
# front: headlight clusters with guards, tow hooks, mud flaps
for y in (-1.45, 1.45):
    boxb("hull", "Steel", (0.2, 0.26, 0.2), (3.75, y, 1.7), w=0.02)
    cyl("hull", "Light", 0.07, 0.03, (3.86, y - 0.05, 1.72), (0, RY, 0), 16)
    cyl("hull", "Light", 0.05, 0.03, (3.86, y + 0.07, 1.68), (0, RY, 0), 12)
    for dz in (-0.12, 0.12):
        box("hull", "Steel", (0.3, 0.02, 0.02), (3.85, y, 1.7 + dz))
    boxb("hull", "Steel", (0.3, 0.12, 0.16), (3.98, y * 0.55, 0.85), w=0.02)
    torus("hull", "Steel", 0.08, 0.025, (4.1, y * 0.55, 0.85), (RX, 0, 0), 16, 6)
    boxb("hull", "Steel", (0.25, 0.12, 0.15), (-4.0, y * 0.55, 0.85), w=0.02)
# tow cables along the hull sides
for s in (-1, 1):
    cyl("hull", "Steel", 0.03, 4.5, (-0.6, s * 1.12, 2.02), (0, RY, 0), 8)
    for x in (1.6, -2.8):
        torus("hull", "Steel", 0.07, 0.02, (x, s * 1.12, 2.02), (0, RY, 0), 12, 5)

# tracks: individual links around the run (sprocket at the rear, idler at the front)
def track_path(n_target):
    """points (x, z, angle) along a loop: bottom run, front arc, top run, rear arc"""
    xf, xr, zc, R = 3.45, -3.4, 0.66, 0.66
    segs = []
    pts = []
    def add_line(x0, z0, x1, z1, step):
        d = math.hypot(x1 - x0, z1 - z0); n = max(1, int(d / step))
        a = math.atan2(z1 - z0, x1 - x0)
        for i in range(n):
            t = (i + 0.5) / n
            pts.append((x0 + (x1 - x0) * t, z0 + (z1 - z0) * t, a))
    def add_arc(cx, cz, r, a0, a1, step):
        n = max(1, int(abs(a1 - a0) * r / step))
        for i in range(n):
            a = a0 + (a1 - a0) * (i + 0.5) / n
            pts.append((cx + math.cos(a) * r, cz + math.sin(a) * r, a + math.pi / 2))
    step = 0.17
    add_line(xr, zc - R, xf, zc - R, step)               # bottom run, rear to front
    add_arc(xf, zc, R, -math.pi / 2, math.pi / 2, step)  # around the idler
    add_line(xf, 1.31, xr, 1.31, step)                   # top run
    add_arc(xr, zc, R, math.pi / 2, 3 * math.pi / 2, step)
    return pts

for s in (-1, 1):
    yc = s * (W / 2 - 0.42)
    tw = 0.62
    for (x, z, a) in track_path(0):
        o = boxb("hull", "Dark", (0.15, tw, 0.06), (x, yc, z), (0, -a, 0), w=0.012, seg=1)
        box("hull", "Rubber", (0.09, tw * 0.35, 0.075), (x, yc + 0.17, z), (0, -a, 0))
        box("hull", "Steel", (0.05, 0.05, 0.09), (x, yc, z), (0, -a, 0))   # guide horn
    # road wheels: rubber tyre, dished disc, hub with bolts (pairs of wheels)
    for i in range(7):
        x = 2.55 - i * 0.86
        for dy in (-0.16, 0.16):
            y = yc + dy
            cyl("hull", "Rubber", 0.4, 0.2, (x, y, 0.45), (RX, 0, 0), 32)
            cyl("hull", "Olive2", 0.34, 0.22, (x, y, 0.45), (RX, 0, 0), 32)
        cyl("hull", "Steel", 0.13, tw + 0.04, (x, yc, 0.45), (RX, 0, 0), 20)
        ring_of(6, 0.085, lambda a, cx, cz: bolt("hull", (x + cx, yc + s * (tw / 2 + 0.03), 0.45 + cz), "y", 0.018, 0.04))
        # suspension arm
        box("hull", "Steel", (0.5, 0.12, 0.1), (x + 0.22, s * (HW / 2 + 0.02), 0.6), (0, 0.4, 0))
    # idler (front): spoked
    xi = 3.45
    for dy in (-0.16, 0.16):
        cyl("hull", "Steel", 0.48, 0.12, (xi, yc + dy, 0.66), (RX, 0, 0), 32)
        cyl("hull", "Olive2", 0.4, 0.13, (xi, yc + dy, 0.66), (RX, 0, 0), 32)
    ring_of(6, 0.25, lambda a, cx, cz: cyl("hull", "Dark", 0.07, tw, (xi + cx, yc, 0.66 + cz), (RX, 0, 0), 12))
    cyl("hull", "Steel", 0.12, tw + 0.05, (xi, yc, 0.66), (RX, 0, 0), 16)
    # drive sprocket (rear): two toothed rings
    xs = -3.4
    for dy in (-0.2, 0.2):
        cyl("hull", "Steel", 0.48, 0.08, (xs, yc + dy, 0.66), (RX, 0, 0), 32)
        ring_of(13, 0.52, lambda a, cx, cz: box("hull", "Steel", (0.09, 0.08, 0.12), (xs + cx, yc + dy, 0.66 + cz), (0, -a, 0)))
    cyl("hull", "Olive2", 0.3, tw, (xs, yc, 0.66), (RX, 0, 0), 24)
    ring_of(10, 0.2, lambda a, cx, cz: bolt("hull", (xs + cx, yc + s * (tw / 2 + 0.02), 0.66 + cz), "y", 0.02, 0.04))
    # return rollers
    for i in range(3):
        cyl("hull", "Steel", 0.13, 0.3, (1.8 - i * 1.8, yc, 1.18), (RX, 0, 0), 16)
    # side skirts: armour panels with bolts and a rubber lower edge
    ys = s * (W / 2 - 0.02)
    for i in range(6):
        x = 2.75 - i * 1.0
        boxb("hull", "Olive", (0.97, 0.07, 0.62), (x, ys, 1.25), w=0.012)
        for bx in (-0.38, 0, 0.38):
            for bz in (1.05, 1.45):
                bolt("hull", (x + bx, ys + s * 0.04, bz), "y", 0.022, 0.03)
    box("hull", "Rubber", (6.0, 0.04, 0.12), (0.25, ys, 0.9))
    boxb("hull", "Olive2", (0.9, 0.07, 0.5), (3.55, ys, 1.32), (0, 0.35, 0), w=0.012)
    # fender storage boxes at the rear
    boxb("hull", "Olive2", (0.9, 0.45, 0.35), (-3.3, s * 1.5, 2.15), w=0.03)
    box("hull", "Steel", (0.05, 0.2, 0.05), (-2.85, s * 1.5, 2.2))

# ------------------------------------------------------------------ turret (origin = ring centre, z = hull deck)
T0 = 2.0
turret_pts = []
for x, w, zb, zt in [(2.05, 1.2, 0.05, 0.45), (1.7, 2.5, 0.0, 0.75), (0.0, 3.3, 0.0, 0.85), (-1.6, 3.2, 0.05, 0.85),
                     (-2.75, 2.7, 0.15, 0.8)]:
    for y in (-w / 2, w / 2):
        turret_pts += [(x, y, zb), (x, y, zt)]
bev(hull_from_points("turret", "Olive", turret_pts), 0.05, 3)
cyl("turret", "Olive2", 1.25, 0.12, (0, 0, -0.02), verts=48)
# add-on armour cheek blocks with bolts
for s in (-1, 1):
    boxb("turret", "Olive2", (1.2, 0.08, 0.5), (1.0, s * 1.42, 0.45), (0, 0, s * -0.38), w=0.015)
    for i in range(4):
        bolt("turret", (0.6 + i * 0.27, s * (1.32 + i * 0.1), 0.62), "y", 0.02, 0.03)
# gun mantlet: rounded block, dust cover bellows rings
boxb("turret", "Olive2", (0.5, 1.0, 0.62), (2.1, 0, 0.38), w=0.08, seg=3)
for k in range(4):
    cyl("turret", "Dark", 0.26 - k * 0.02, 0.06, (2.4 + k * 0.07, 0, 0.42), (0, RY, 0), 24)
# 105 mm gun: smooth barrel, thermal sleeve bands, bore evacuator, muzzle reference sensor, muzzle
cyl("turret", "Olive", 0.13, 5.4, (4.95, 0, 0.42), (0, RY, 0), 32)
cyl("turret", "Olive2", 0.17, 0.8, (2.8, 0, 0.42), (0, RY, 0), 32, r2=0.14)
cyl("turret", "Olive2", 0.2, 0.7, (4.6, 0, 0.42), (0, RY, 0), 32)
cyl("turret", "Olive2", 0.2, 0.08, (4.21, 0, 0.42), (0, RY, 0), 32, r2=0.13)
cyl("turret", "Olive2", 0.13, 0.08, (4.99, 0, 0.42), (0, RY, 0), 32, r2=0.2)
for x in (3.3, 3.7, 5.5, 6.1, 6.7):
    torus("turret", "Olive2", 0.145, 0.025, (x, 0, 0.42), (0, RY, 0), 32, 8)
cyl("turret", "Steel", 0.155, 0.35, (7.55, 0, 0.42), (0, RY, 0), 32)
cyl("turret", "Dark", 0.075, 0.37, (7.6, 0, 0.42), (0, RY, 0), 16)
boxb("turret", "Steel", (0.08, 0.06, 0.08), (7.3, 0, 0.6), w=0.01)
cyl("turret", "Steel", 0.015, 0.1, (7.3, 0, 0.55), verts=6)
# coaxial MG, gunner's primary sight with doors, laser rangefinder window
cyl("turret", "Dark", 0.05, 0.4, (2.3, -0.42, 0.3), (0, RY, 0), 12)
boxb("turret", "Olive2", (0.6, 0.48, 0.42), (1.2, -1.05, 0.96), w=0.04, seg=2)
box("turret", "Glass", (0.03, 0.34, 0.24), (1.5, -1.05, 0.99))
for s in (-1, 1):
    box("turret", "Olive2", (0.03, 0.17, 0.26), (1.53, -1.05 + s * 0.24, 0.99), (0, 0, s * 0.5))
# commander's cupola with vision blocks, hatch and hinge; independent viewer
cyl("turret", "Olive2", 0.5, 0.35, (-0.5, -0.8, 1.0), verts=40)
cyl("turret", "Olive", 0.43, 0.1, (-0.5, -0.8, 1.22), verts=40)
torus("turret", "Olive2", 0.43, 0.03, (-0.5, -0.8, 1.27), maj=40, mnr=8)
ring_of(8, 0.5, lambda a, cx, cy: (boxb("turret", "Olive2", (0.13, 0.17, 0.13), (-0.5 + cx, -0.8 + cy, 1.05), (0, 0, a), w=0.01),
                                   box("turret", "Glass", (0.02, 0.12, 0.07), (-0.5 + cx * 1.13, -0.8 + cy * 1.13, 1.06), (0, 0, a))))
cyl("turret", "Steel", 0.04, 0.3, (-0.5, -0.38, 1.28), (0, RY, 0), 8)
cyl("turret", "Olive2", 0.16, 0.3, (0.05, -0.45, 1.05), verts=24)
boxb("turret", "Olive2", (0.3, 0.3, 0.22), (0.05, -0.45, 1.28), w=0.03)
box("turret", "Glass", (0.02, 0.22, 0.14), (0.21, -0.45, 1.3))
# loader's hatch and roof machine gun with ammo box and shield
cyl("turret", "Olive2", 0.42, 0.08, (-0.4, 0.85, 0.9), verts=40)
cyl("turret", "Olive", 0.36, 0.06, (-0.4, 0.85, 0.96), verts=40)
torus("turret", "Steel", 0.45, 0.025, (-0.4, 0.85, 1.0), maj=40, mnr=6)
box("turret", "Steel", (0.08, 0.08, 0.35), (-0.15, 0.85, 1.12))
boxb("turret", "Steel", (0.7, 0.13, 0.15), (0.12, 0.85, 1.32), w=0.015)
cyl("turret", "Steel", 0.035, 0.85, (0.88, 0.85, 1.34), (0, RY, 0), 12)
cyl("turret", "Steel", 0.05, 0.12, (1.33, 0.85, 1.34), (0, RY, 0), 12)
box("turret", "Steel", (0.12, 0.04, 0.2), (0.35, 0.85, 1.22))
boxb("turret", "Olive2", (0.22, 0.2, 0.18), (-0.08, 1.0, 1.27), w=0.015)
box("turret", "Steel", (0.18, 0.03, 0.03), (-0.08, 1.0, 1.38))
# smoke grenade launchers on both cheeks (tubes with caps)
for s in (-1, 1):
    boxb("turret", "Olive2", (0.3, 0.4, 0.18), (1.25, s * 1.38, 0.66), (0, 0, 0), w=0.015)
    for i in range(4):
        cyl("turret", "Dark", 0.055, 0.32, (1.42, s * (1.25 + i * 0.09), 0.75 + (i % 2) * 0.09), (0, RY * 0.7, s * 0.4), 16)
        cyl("turret", "Olive2", 0.06, 0.03, (1.55, s * (1.29 + i * 0.09), 0.8 + (i % 2) * 0.09), (0, RY * 0.7, s * 0.4), 16)
# rear bustle stowage basket: floor, posts, two rails, and its load
box("turret", "Dark", (0.7, 2.9, 0.04), (-3.05, 0, 0.35))
for y in (-1.4, -1.05, -0.7, -0.35, 0, 0.35, 0.7, 1.05, 1.4):
    cyl("turret", "Dark", 0.02, 0.45, (-3.38, y, 0.55), verts=6)
for z in (0.6, 0.78):
    cyl("turret", "Dark", 0.02, 2.9, (-3.38, 0, z), (RX, 0, 0), 6)
    for y in (-1.43, 1.43):
        cyl("turret", "Dark", 0.02, 0.7, (-3.05, y, z), (0, RY, 0), 6)
boxb("turret", "Olive2", (0.5, 0.6, 0.35), (-3.0, -0.8, 0.55), w=0.03)          # ammo crate
boxb("turret", "Tan", (0.55, 0.9, 0.3), (-3.05, 0.3, 0.5), w=0.08, seg=3)        # rolled tarp
cyl("turret", "Tan", 0.15, 0.9, (-3.05, 0.9, 0.55), (RX, 0, 0), 16)              # bedroll
for y in (1.15, 1.32):                                                            # jerry cans
    boxb("turret", "Olive2", (0.35, 0.14, 0.45), (-3.1, y, 0.6), w=0.02)
# antennas with bases, wind sensor, grab handles, lifting eyes
for y in (-1.1, 1.1):
    cyl("turret", "Steel", 0.06, 0.12, (-2.4, y, 0.86), verts=12)
    cyl("turret", "Dark", 0.012, 2.2, (-2.4, y, 1.95), verts=6)
cyl("turret", "Steel", 0.015, 0.4, (-1.9, -0.4, 1.05), verts=6)
cyl("turret", "Steel", 0.04, 0.1, (-1.9, -0.4, 1.27), verts=10)
for (x, y) in [(1.0, 1.0), (-1.2, 1.4), (-1.2, -1.45), (0.4, -1.5)]:
    torus("turret", "Steel", 0.07, 0.015, (x, y, 0.88), (RX, 0, 0), 12, 5)
for x in (1.6, -2.4):
    for s in (-1, 1):
        torus("turret", "Steel", 0.06, 0.02, (x, s * 1.2, 0.86), (0, RY, 0), 12, 5)
# roof panel bolts
for x in (-1.6, -1.2, -0.8):
    for y in (-0.2, 0.2):
        bolt("turret", (x, y, 0.86))

# tan camouflage patches (thin decals on the plates)
for (x, y, z, sx, sy, sz) in [(0.6, 1.62, 0.55, 1.0, 0.02, 0.3), (-1.4, -1.6, 0.5, 1.2, 0.02, 0.3),
                              (-1.0, 0.4, 0.865, 1.1, 0.7, 0.02), (0.9, -0.3, 0.865, 0.6, 0.5, 0.02)]:
    bev(box("turret", "Tan", (sx, sy, sz), (x, y, z)), 0.01, 1)
for (x, y, z, sx, sy) in [(1.0, 0, 2.005, 1.2, 0.9), (-1.5, 0.6, 2.005, 1.4, 1.0)]:
    box("hull", "Tan", (sx, sy, 0.012), (x, y, z))

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
