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
# ------------------------------------------------------------------ hull
L, W = 7.9, 3.6          # hull length, width (with skirts)
HW = 2.3                 # hull body width between tracks
GX = -0.3                # ring sits a little behind the hull's middle
# lower hull (between tracks): sloped glacis at the front, sloped rear
hull_from_points("hull", "Olive", prism(0, 0, HW, [
    (3.95, 0.75, 1.25), (3.3, 0.45, 1.9), (2.4, 0.45, 2.0), (-3.6, 0.45, 2.0), (-3.95, 0.7, 1.85)]), 0.03)
# upper hull deck over the tracks (sponsons)
hull_from_points("hull", "Olive", prism(0, 0, W - 0.1, [
    (3.5, 1.45, 1.6), (2.2, 1.45, 1.98), (-3.75, 1.45, 1.98), (-3.95, 1.5, 1.9)]), 0.03)
# front glacis plate and driver's hatch
box("hull", "Olive2", (1.0, 0.9, 0.06), (2.85, 0, 1.97), (0, -0.33, 0))
box("hull", "Olive", (0.55, 0.6, 0.08), (2.5, 0, 2.03), bevel=0.02)
for i in range(3):
    box("hull", "Glass", (0.12, 0.16, 0.08), (2.82, -0.2 + i * 0.2, 2.05))
# rear engine deck grilles and exhaust
for i in range(4):
    box("hull", "Dark", (0.35, 1.8, 0.03), (-2.4 - i * 0.42, 0, 2.0))
box("hull", "Steel", (0.12, 1.4, 0.35), (-3.95, 0, 1.55))
# headlights, tow hooks, fenders
for y in (-1.45, 1.45):
    box("hull", "Steel", (0.18, 0.22, 0.18), (3.75, y, 1.7))
    box("hull", "Light", (0.03, 0.14, 0.12), (3.85, y, 1.7))
    box("hull", "Steel", (0.25, 0.12, 0.15), (3.95, y * 0.55, 0.85))
    box("hull", "Steel", (0.25, 0.12, 0.15), (-4.0, y * 0.55, 0.85))
# tracks: road wheels, sprocket (rear), idler (front), track belt, side skirts
for s in (-1, 1):
    yc = s * (W / 2 - 0.42)
    tw = 0.62
    # track belt: a flattened loop made of a box with rounded ends
    box("hull", "Rubber", (6.9, tw, 0.12), (0.05, yc, 0.06))
    box("hull", "Rubber", (6.3, tw, 0.1), (0.0, yc, 1.25))
    for x in (3.45, -3.4):
        cyl("hull", "Rubber", 0.62, tw, (x, yc, 0.66), (RX, 0, 0), 20)
    # track pads (links) along the bottom and the top run
    for i in range(30):
        x = -3.3 + i * 0.226
        box("hull", "Dark", (0.12, tw + 0.02, 0.05), (x, yc, 0.0))
    for i in range(27):
        x = -3.0 + i * 0.226
        box("hull", "Dark", (0.12, tw + 0.02, 0.05), (x, yc, 1.31))
    # road wheels
    for i in range(7):
        x = 2.55 - i * 0.86
        cyl("hull", "Olive2", 0.4, tw - 0.08, (x, yc, 0.45), (RX, 0, 0), 20)
        cyl("hull", "Steel", 0.16, tw + 0.02, (x, yc, 0.45), (RX, 0, 0), 12)
    # idler and sprocket
    cyl("hull", "Steel", 0.48, tw - 0.06, (3.45, yc, 0.66), (RX, 0, 0), 24)
    cyl("hull", "Steel", 0.52, tw - 0.06, (-3.4, yc, 0.66), (RX, 0, 0), 12)
    # return rollers
    for i in range(3):
        cyl("hull", "Steel", 0.12, 0.3, (1.8 - i * 1.8, yc, 1.15), (RX, 0, 0), 10)
    # side skirt (armour panels) over the upper track run
    ys = s * (W / 2 - 0.02)
    for i in range(6):
        x = 2.75 - i * 1.0
        box("hull", "Olive", (0.98, 0.06, 0.62), (x, ys, 1.25), bevel=0.01)
    box("hull", "Olive2", (0.9, 0.06, 0.5), (3.55, ys, 1.32), (0, 0.35, 0))

# ------------------------------------------------------------------ turret (origin = ring centre, z = hull deck)
T0 = 2.0  # deck height; turret objects are moved down by T0 and back by GX at export
def tz(z): return z
turret_pts = []
for x, w, zb, zt in [(2.05, 1.2, 0.05, 0.45), (1.7, 2.5, 0.0, 0.75), (0.0, 3.3, 0.0, 0.85), (-1.6, 3.2, 0.05, 0.85),
                     (-2.75, 2.7, 0.15, 0.8)]:
    for y in (-w / 2, w / 2):
        turret_pts += [(x, y, zb), (x, y, zt)]
hull_from_points("turret", "Olive", turret_pts, 0.04)
# turret ring/base
cyl("turret", "Olive2", 1.25, 0.12, (0, 0, -0.02), verts=32)
# gun mantlet
box("turret", "Olive2", (0.5, 1.0, 0.62), (2.1, 0, 0.38), bevel=0.05)
# 105 mm gun: barrel, thermal sleeve bands, bore evacuator, muzzle
cyl("turret", "Olive", 0.13, 5.4, (4.95, 0, 0.42), (0, RX, 0), 16)
cyl("turret", "Olive2", 0.17, 0.8, (2.8, 0, 0.42), (0, RX, 0), 16, r2=0.14)
cyl("turret", "Olive2", 0.2, 0.7, (4.6, 0, 0.42), (0, RX, 0), 18)
for x in (3.5, 5.6, 6.6):
    cyl("turret", "Olive2", 0.15, 0.08, (x, 0, 0.42), (0, RX, 0), 16)
cyl("turret", "Steel", 0.15, 0.3, (7.55, 0, 0.42), (0, RX, 0), 16)
cyl("turret", "Dark", 0.07, 0.32, (7.6, 0, 0.42), (0, RX, 0), 12)
# coaxial MG port and gunner's sight
box("turret", "Dark", (0.25, 0.12, 0.12), (2.2, -0.42, 0.3))
box("turret", "Olive2", (0.55, 0.45, 0.4), (1.2, -1.05, 0.95), bevel=0.03)
box("turret", "Glass", (0.03, 0.32, 0.22), (1.48, -1.05, 0.98))
# commander's cupola (right side) with vision blocks, loader's hatch (left)
cyl("turret", "Olive2", 0.5, 0.35, (-0.5, -0.8, 1.0), verts=24)
cyl("turret", "Olive", 0.42, 0.1, (-0.5, -0.8, 1.22), verts=24)
for k in range(6):
    a = k * math.pi / 3
    box("turret", "Glass", (0.1, 0.14, 0.1), (-0.5 + math.cos(a) * 0.5, -0.8 + math.sin(a) * 0.5, 1.05), (0, 0, a))
cyl("turret", "Olive2", 0.42, 0.08, (-0.4, 0.85, 0.9), verts=24)
# roof machine gun on the loader's hatch ring
box("turret", "Steel", (0.1, 0.1, 0.35), (-0.15, 0.85, 1.1))
box("turret", "Steel", (0.75, 0.12, 0.14), (0.15, 0.85, 1.3))
cyl("turret", "Steel", 0.035, 0.9, (0.95, 0.85, 1.32), (0, RX, 0), 8)
box("turret", "Olive2", (0.22, 0.18, 0.18), (-0.05, 0.98, 1.27))
# smoke grenade launchers on both cheeks
for s in (-1, 1):
    for i in range(4):
        cyl("turret", "Dark", 0.06, 0.28, (1.35, s * (1.25 + i * 0.09), 0.7 + (i % 2) * 0.1), (0, RX * 0.7, s * 0.4), 8)
# rear bustle stowage basket and antennas
box("turret", "Dark", (0.7, 2.9, 0.05), (-3.05, 0, 0.35))
for y in (-1.4, -0.7, 0, 0.7, 1.4):
    box("turret", "Dark", (0.04, 0.04, 0.45), (-3.38, y, 0.55))
box("turret", "Dark", (0.04, 2.9, 0.04), (-3.38, 0, 0.78))
for y in (-1.35, 1.35):
    box("turret", "Dark", (0.7, 0.04, 0.04), (-3.05, y, 0.78))
for y in (-1.1, 1.1):
    cyl("turret", "Dark", 0.02, 1.6, (-2.4, y, 1.6), verts=6)
# tan camouflage patches on the turret sides and roof
for (x, y, z, sx, sy) in [(0.6, 1.6, 0.55, 1.0, 0.03), (-1.4, -1.58, 0.5, 1.2, 0.03), (-1.0, 0.4, 0.87, 1.1, 0.7),
                          (0.9, -0.3, 0.87, 0.6, 0.5)]:
    box("turret", "Tan", (sx, sy, 0.02 if sy > 0.1 else 0.3), (x, y, z))
for (x, y, z, sx, sy) in [(1.0, 0, 2.01, 1.2, 0.9), (-1.5, 0.6, 2.01, 1.4, 1.0), (-3.2, -0.9, 2.0, 0.5, 0.8)]:
    box("hull", "Tan", (sx, sy, 0.02), (x, y, z))

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
    bpy.ops.object.shade_flat()

def export(o, path):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    loc = o.location.copy(); o.location = (0, 0, 0)
    bpy.ops.wm.obj_export(filepath=path, export_selected_objects=True, forward_axis="X", up_axis="Z",
                          export_materials=True, export_triangulated_mesh=True, export_normals=True, export_uv=False,
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
