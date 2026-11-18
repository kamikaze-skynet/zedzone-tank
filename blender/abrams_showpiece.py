"""M1A1-style Abrams showpiece: a standalone, as-detailed-as-possible Blender model (not the game model).

Builds a new scene "Abrams HD" (your other scenes are left alone) with:
  - rounded armour with weld beads, hundreds of bolts, hinges, handles and lifting eyes
  - shaped dual road wheels with rubber tyres, toothed sprockets, idlers, return rollers, road arms
  - fully modelled track links (shoe, rubber pads, guide horns, end connectors, pins)
  - 120 mm gun with canvas mantlet boot, thermal sleeve bands, bore evacuator and muzzle sensor
  - commander's .50 cal, loader's 7.62, sights, periscopes, smoke launchers, antennas
  - bustle rack loaded with jerrycans, tarps, duffel bags, ammo cans and ration boxes
  - procedural paint/rubber/steel materials, lights, camera and a ground plane (Cycles)
The turret ("Turret" empty) turns about Z and the gun ("Gun" empty) elevates about Y.

Run it inside Blender (Text Editor > Run Script, or exec() through the Blender MCP), or headless:
  blender -b -P blender/abrams_showpiece.py -- out_folder      (renders previews and saves a .blend)
Works with Blender 4.1 and newer."""
import functools, math, os, sys
import bpy, bmesh
from mathutils import Vector, Matrix, Euler

HEADLESS = bpy.app.background
RX = RY = math.pi / 2
TAU = 2 * math.pi

# ------------------------------------------------------------------ scene
SCENE = "Abrams HD"
old = bpy.data.scenes.get(SCENE)
scn = bpy.data.scenes.new(SCENE + " (new)")
if bpy.context.window is not None:
    bpy.context.window.scene = scn
if old is not None:
    for o in list(old.objects):
        if o.users_scene == (old,):
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.scenes.remove(old)
for me in list(bpy.data.meshes):
    if me.name.startswith("AHD_") and me.users == 0:
        bpy.data.meshes.remove(me)
for m in list(bpy.data.materials):
    if m.name.startswith("AHD ") and m.users == 0:
        bpy.data.materials.remove(m)
scn.name = SCENE
coll = scn.collection

# ------------------------------------------------------------------ materials
def srgb(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)

def setin(node, names, val):
    for n in names:
        if n in node.inputs:
            node.inputs[n].default_value = val
            return

def material(name, hex_, rough=0.6, metal=0.0, grime=0.25, scale=3.0, emit=0.0, rough_var=0.15):
    m = bpy.data.materials.new("AHD " + name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes.get("Principled BSDF")
    col = srgb(hex_)
    setin(b, ["Base Color"], (*col, 1))
    setin(b, ["Metallic"], metal)
    setin(b, ["Roughness"], rough)
    m.diffuse_color = (*col, 1)
    m.roughness = rough
    if grime > 0:
        # large blotchy dirt and fine speckle, from object coordinates (no UVs needed)
        tc = nt.nodes.new("ShaderNodeTexCoord")
        n1 = nt.nodes.new("ShaderNodeTexNoise"); n1.inputs["Scale"].default_value = scale
        n1.inputs["Detail"].default_value = 8.0
        n2 = nt.nodes.new("ShaderNodeTexNoise"); n2.inputs["Scale"].default_value = scale * 25
        n2.inputs["Detail"].default_value = 4.0
        nt.links.new(tc.outputs["Object"], n1.inputs["Vector"])
        nt.links.new(tc.outputs["Object"], n2.inputs["Vector"])
        ramp = nt.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].position = 0.35
        ramp.color_ramp.elements[1].position = 0.75
        nt.links.new(n1.outputs["Fac"], ramp.inputs["Fac"])
        dark = tuple(c * (1 - grime) for c in col)
        mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = "RGBA"
        mix.inputs[6].default_value = (*col, 1)
        mix.inputs[7].default_value = (*dark, 1)
        nt.links.new(ramp.outputs["Color"], mix.inputs[0])
        # fine speckle darkens slightly too
        mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = "RGBA"; mul.blend_type = "MULTIPLY"
        mul.inputs[0].default_value = 0.25
        nt.links.new(mix.outputs[2], mul.inputs[6])
        nt.links.new(n2.outputs["Color"], mul.inputs[7])
        nt.links.new(mul.outputs[2], b.inputs["Base Color"])
        rmap = nt.nodes.new("ShaderNodeMapRange")
        rmap.inputs["To Min"].default_value = max(0.0, rough - rough_var)
        rmap.inputs["To Max"].default_value = min(1.0, rough + rough_var)
        nt.links.new(n2.outputs["Fac"], rmap.inputs["Value"])
        nt.links.new(rmap.outputs["Result"], b.inputs["Roughness"])
        bump = nt.nodes.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.08
        bump.inputs["Distance"].default_value = 0.002
        nt.links.new(n2.outputs["Fac"], bump.inputs["Height"])
        nt.links.new(bump.outputs["Normal"], b.inputs["Normal"])
    if emit > 0:
        setin(b, ["Emission Color", "Emission"], (*col, 1))
        setin(b, ["Emission Strength"], emit)
    return m

MATS = {
    "Paint":    material("Paint", "b9a174", 0.62, grime=0.28),          # desert tan armour
    "Paint2":   material("Paint2", "a99169", 0.66, grime=0.3),          # panels / hatches
    "Paint3":   material("Paint3", "8f7a57", 0.7, grime=0.3),           # fittings, bolts, welds
    "Weld":     material("Weld", "9a8460", 0.75, grime=0.35, scale=12),
    "Rubber":   material("Rubber", "1d1c1a", 0.92, grime=0.15, scale=6),
    "Track":    material("Track", "3a3833", 0.5, 0.75, grime=0.35, scale=6),
    "Gunmetal": material("Gunmetal", "2b2c2b", 0.38, 0.85, grime=0.2, scale=8),
    "Dark":     material("Dark", "141412", 0.8, grime=0.0),
    "Glass":    material("Glass", "0f2a2c", 0.04, 0.6, grime=0.0),
    "Lens":     material("Lens", "fff3d0", 0.1, grime=0.0, emit=0.6),
    "RedLens":  material("RedLens", "b01810", 0.15, grime=0.0, emit=0.4),
    "Canvas":   material("Canvas", "7a7150", 0.95, grime=0.3, scale=10),
    "Tarp":     material("Tarp", "5c6040", 0.95, grime=0.3, scale=8),
    "Strap":    material("Strap", "3c3a2a", 0.9, grime=0.15),
    "Bag":      material("Bag", "8a7f5c", 0.95, grime=0.3, scale=6),
    "Ammo":     material("Ammo", "4b5230", 0.6, grime=0.3),
    "Card":     material("Card", "a48a5e", 0.9, grime=0.25),
    "Brass":    material("Brass", "b08d3c", 0.35, 1.0, grime=0.2),
}
MAT_ORDER = list(MATS)

# ------------------------------------------------------------------ geometry core
PARTS, GROUP = {}, {}
CUR = [None]

def part(name, group):
    CUR[0] = name
    PARTS.setdefault(name, ([], [], []))
    GROUP[name] = group

def put(g, m):
    V, F, Ms = PARTS[CUR[0]]
    off = len(V)
    V.extend(m @ p for p in g[0])
    F.extend(tuple(i + off for i in fa) for fa in g[1])
    Ms.extend(g[2])

def xf(loc=(0, 0, 0), rot=(0, 0, 0), scale=None):
    m = Matrix.Translation(Vector(loc)) @ Euler(rot, "XYZ").to_matrix().to_4x4()
    if scale is not None:
        m = m @ Matrix.Diagonal((*scale, 1.0))
    return m

I4 = Matrix.Identity(4)

def gx(g, m):
    return ([m @ p for p in g[0]], g[1], g[2])

def cat(*gs):
    v, f, ms = [], [], []
    for g in gs:
        off = len(v)
        v.extend(g[0]); f.extend(tuple(i + off for i in fa) for fa in g[1]); ms.extend(g[2])
    return (v, f, ms)

def geo(bm, mat):
    bm.verts.index_update()
    v = [x.co.copy() for x in bm.verts]
    f = [tuple(x.index for x in fa.verts) for fa in bm.faces]
    bm.free()
    return (v, f, [mat] * len(f))

def bevel_bm(bm, w, seg=3, angle=30):
    if w <= 0:
        return
    lim = math.radians(angle)
    es = [e for e in bm.edges if len(e.link_faces) == 2 and e.calc_face_angle(0) > lim]
    if es:
        bmesh.ops.bevel(bm, geom=es, offset=w, offset_type="OFFSET", segments=seg, profile=0.5,
                        affect="EDGES", clamp_overlap=True)

def mk(v, f, mat, w=0.0, seg=3, angle=30):
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in v]
    for fa in f:
        try:
            bm.faces.new([vs[i] for i in fa])
        except ValueError:
            pass
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bevel_bm(bm, w, seg, angle)
    return geo(bm, mat)

@functools.lru_cache(None)
def G_box(sx, sy, sz, mat, w=0.0, seg=3, angle=30):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(sx, sy, sz), verts=bm.verts)
    bevel_bm(bm, min(w, 0.49 * min(sx, sy, sz)), seg, angle)
    return geo(bm, mat)

@functools.lru_cache(None)
def G_cyl(r, d, mat, n=48, r2=None, w=0.0, seg=2):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=n, radius1=r,
                          radius2=r if r2 is None else r2, depth=d)
    bevel_bm(bm, min(w, 0.45 * d, 0.45 * r), seg, 30)
    return geo(bm, mat)

@functools.lru_cache(None)
def G_lathe(prof, mat, n=48, closed=False, w=0.0, seg=2, angle=30):
    v, f, rings = [], [], []
    for (r, z) in prof:
        if r < 1e-6:
            rings.append((len(v),)); v.append(Vector((0, 0, z)))
        else:
            rings.append(tuple(range(len(v), len(v) + n)))
            v.extend(Vector((math.cos(TAU * k / n) * r, math.sin(TAU * k / n) * r, z)) for k in range(n))
    pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
    for A, B in pairs:
        if len(A) == 1 and len(B) == 1:
            continue
        for k in range(n):
            k1 = (k + 1) % n
            if len(A) == 1: f.append((A[0], B[k1], B[k]))
            elif len(B) == 1: f.append((A[k], A[k1], B[0]))
            else: f.append((A[k], A[k1], B[k1], B[k]))
    if not closed:
        if len(rings[0]) > 1: f.append(tuple(reversed(rings[0])))
        if len(rings[-1]) > 1: f.append(rings[-1])
    return mk(v, f, mat, w, seg, angle)

@functools.lru_cache(None)
def G_hull(pts, mat, w=0.0, seg=3, angle=10):
    bm = bmesh.new()
    for p in pts:
        bm.verts.new(p)
    r = bmesh.ops.convex_hull(bm, input=list(bm.verts))
    junk = {g for g in r["geom_interior"] + r["geom_unused"] if isinstance(g, bmesh.types.BMVert)}
    if junk:
        bmesh.ops.delete(bm, geom=list(junk), context="VERTS")
    bmesh.ops.dissolve_limit(bm, angle_limit=0.002, verts=list(bm.verts), edges=list(bm.edges))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bevel_bm(bm, w, seg, angle)
    return geo(bm, mat)

@functools.lru_cache(None)
def G_prism(poly, d, mat, w=0.0, seg=2, angle=40):
    n = len(poly)
    v = [Vector((x, y, -d / 2)) for x, y in poly] + [Vector((x, y, d / 2)) for x, y in poly]
    f = [tuple(reversed(range(n))), tuple(range(n, 2 * n))] + [(k, (k + 1) % n, n + (k + 1) % n, n + k) for k in range(n)]
    return mk(v, f, mat, w, seg, angle)

def tp(prof):
    return tuple((round(a, 5), round(b, 5)) for a, b in prof)

AX = {"z": (0, 0, 0), "-z": (math.pi, 0, 0), "x": (0, RY, 0), "-x": (0, -RY, 0), "y": (-RX, 0, 0), "-y": (RX, 0, 0)}

def box(m, size, loc, rot=(0, 0, 0), w=0.0, seg=3, angle=30, M=I4):
    put(G_box(*map(float, size), m, float(w), seg, angle), M @ xf(loc, rot))

def cyl(m, r, d, loc, rot=(0, 0, 0), n=48, r2=None, w=0.0, seg=2, M=I4):
    put(G_cyl(float(r), float(d), m, n, r2, float(w), seg), M @ xf(loc, AX.get(rot, rot) if isinstance(rot, str) else rot))

def lathe(m, prof, loc=(0, 0, 0), rot=(0, 0, 0), n=48, closed=False, w=0.0, seg=2, angle=30, M=I4):
    put(G_lathe(tp(prof), m, n, closed, float(w), seg, angle), M @ xf(loc, AX.get(rot, rot) if isinstance(rot, str) else rot))

def torus_prof(R, r, mnr=12):
    return [(R + r * math.cos(TAU * k / mnr), r * math.sin(TAU * k / mnr)) for k in range(mnr)]

def torus(m, R, r, loc, rot=(0, 0, 0), maj=48, mnr=12, M=I4):
    lathe(m, torus_prof(R, r, mnr), loc, rot, maj, True, M=M)

def sphere_prof(r, n):
    h = n // 2
    return [(0, -r)] + [(r * math.sin(math.pi * k / h), -r * math.cos(math.pi * k / h)) for k in range(1, h)] + [(0, r)]

def sphere(m, r, loc, n=24, M=I4, scale=None):
    put(G_lathe(tp(sphere_prof(r, n)), m, n), M @ xf(loc, (0, 0, 0), scale))

def hullp(m, pts, w=0.0, seg=3, angle=10, M=I4):
    put(G_hull(tuple(tuple(round(c, 5) for c in p) for p in pts), m, float(w), seg, angle), M)

def prism(m, poly, d, loc, rot=(0, 0, 0), w=0.0, seg=2, angle=40, M=I4):
    put(G_prism(tuple(tuple(round(c, 5) for c in p) for p in poly), float(d), m, float(w), seg, angle), M @ xf(loc, rot))

def seg_xf(a, b):
    a, b = Vector(a), Vector(b)
    d = b - a
    return Matrix.Translation((a + b) / 2) @ d.to_track_quat("Z", "Y").to_matrix().to_4x4(), d.length

def rod(m, a, b, r, n=16, M=I4):
    T, L = seg_xf(a, b)
    put(G_cyl(float(r), round(L, 5), m, n), M @ T)

def tube(m, pts, r, n=16, M=I4):
    for a, b in zip(pts, pts[1:]):
        rod(m, a, b, r, n, M)
    for p in pts[1:-1]:
        sphere(m, r, p, n, M)

def beam(m, a, b, wy, h, w=0.01, M=I4):
    T, L = seg_xf(a, b)
    put(G_box(float(wy), float(h), round(L, 5), m, float(w), 2), M @ T)

def weld(a, b, r=0.013, M=I4):
    T, L = seg_xf(a, b)
    put(G_cyl(float(r), round(L, 5), "Weld", 10), M @ T @ xf(scale=(1, 0.6, 1)))
    # weld ripples
    nrip = max(2, int(L / 0.035))
    for k in range(nrip):
        put(G_cyl(float(r * 1.15), float(r * 0.9), "Weld", 10), M @ T @ xf((0, 0, -L / 2 + (k + 0.5) * L / nrip), scale=(1, 0.6, 1)))

@functools.lru_cache(None)
def G_bolt(r, h, mat):
    return cat(gx(G_cyl(r * 1.5, h * 0.25, mat, 16), xf((0, 0, h * 0.125))),
               gx(G_cyl(r, h * 0.75, mat, 6, w=r * 0.15, seg=1), xf((0, 0, h * 0.25 + h * 0.375))),
               gx(G_cyl(r * 0.55, h * 0.25, mat, 12), xf((0, 0, h + h * 0.1))))

def bolt(loc, axis="z", r=0.02, h=0.028, m="Paint3", M=I4):
    rot = AX[axis] if isinstance(axis, str) else axis
    put(G_bolt(float(r), float(h), m), M @ xf(loc, rot))

def bolt_rect(cx, cy, z, lx, ly, step, r=0.018, M=I4, m="Paint3"):
    nx, ny = max(1, round(lx / step)), max(1, round(ly / step))
    for i in range(nx + 1):
        for y in (-ly / 2, ly / 2):
            bolt((cx - lx / 2 + i * lx / nx, cy + y, z), r=r, M=M, m=m)
    for j in range(1, ny):
        for x in (-lx / 2, lx / 2):
            bolt((cx + x, cy - ly / 2 + j * ly / ny, z), r=r, M=M, m=m)

def ring_of(n, R, fn, a0=0.0):
    for k in range(n):
        a = a0 + TAU * k / n
        fn(a, math.cos(a) * R, math.sin(a) * R)

def handle(m, a, b, out, r=0.012, M=I4):
    """U-shaped grab handle from a to b, standing off by the vector out"""
    a, b, o = Vector(a), Vector(b), Vector(out)
    tube(m, [a, a + o, b + o, b], r, 12, M)

def hinge(m, a, b, r=0.03, knuckles=3, M=I4):
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    kl = L / (knuckles * 2 - 1)
    for k in range(knuckles * 2 - 1):
        p0 = a + (b - a) * (k * kl / L)
        p1 = a + (b - a) * ((k + 1) * kl / L)
        rr = r if k % 2 == 0 else r * 0.92
        T, l = seg_xf(p0, p1)
        put(G_cyl(float(rr), round(l * 0.97, 5), m, 20, w=0.003, seg=1), M @ T)
    rod(m, a - (b - a) * 0.03, b + (b - a) * 0.03, r * 0.4, 12, M)

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
    pts = []
    for (cx, cz, r) in circles:
        for k in range(180):
            a = TAU * k / 180
            pts.append((round(cx + math.cos(a) * (r + pad), 5), round(cz + math.sin(a) * (r + pad), 5)))
    poly = hull2d(pts)
    per = [math.dist(poly[i], poly[(i + 1) % len(poly)]) for i in range(len(poly))]
    total = sum(per); n = int(total / pitch); step = total / n
    out = []
    for k in range(n):
        s = k * step
        acc = 0.0
        for i in range(len(poly)):
            if acc + per[i] >= s: break
            acc += per[i]
        a, b = poly[i], poly[(i + 1) % len(poly)]
        f = (s - acc) / per[i] if per[i] else 0
        out.append((a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f, math.atan2(b[1] - a[1], b[0] - a[0])))
    return out, step

def prism_pts(w, zs):
    pts = []
    for x, zb, zt in zs:
        for y in (-w / 2, w / 2):
            pts += [(x, y, zb), (x, y, zt)]
    return pts

# ================================================================== HULL
W, HW, GX, DECK = 3.66, 2.25, 0.25, 1.72

def gz(x):   # height of the glacis top at x
    return 1.14 + (3.98 - x) * 0.2857
GA = math.atan(0.2857)
def gl(x, y, dz=0.0):
    return xf((x, y, gz(x) + dz), (0, GA, 0))

part("Hull", "hull")
hullp("Paint", prism_pts(HW, [(4.05, 0.72, 1.12), (3.45, 0.42, 1.3), (1.95, 0.42, DECK), (-3.8, 0.42, DECK), (-3.98, 0.62, DECK - 0.04)]), 0.045, 4, 8)
hullp("Paint", prism_pts(W - 0.16, [(3.98, 1.3, 1.14), (1.95, 1.3, DECK), (-3.95, 1.3, DECK), (-3.98, 1.35, DECK - 0.04)]), 0.04, 4, 8)
# belly plate seam and side armour lines
weld((1.95, -1.72, DECK + 0.004), (1.95, 1.72, DECK + 0.004))
weld((3.97, -1.68, 1.15), (3.97, 1.68, 1.15))
weld((4.06, -1.08, 0.74), (4.06, 1.08, 0.74))
for s in (-1, 1):
    weld((-3.85, s * 1.72, DECK + 0.004), (1.9, s * 1.72, DECK + 0.004))
    weld((3.9, s * 1.73, 1.17), (2.0, s * 1.73, DECK - 0.01))
# driver's hatch, periscopes, splash guard
M = gl(2.15, 0)
lathe("Paint3", [(0.44, -0.01), (0.44, 0.02), (0.42, 0.035)] + [(0.405, 0.035), (0.0, 0.035)], M=M, n=64)
lathe("Paint2", [(0.40, 0.03), (0.40, 0.06), (0.39, 0.075), (0.34, 0.09), (0.22, 0.1), (0.0, 0.103)], M=M, n=64, w=0.004)
ring_of(16, 0.425, lambda a, x, y: bolt((x, y, 0.035), M=M, r=0.014, h=0.02))
handle("Paint3", (-0.12, 0.12, 0.1), (0.12, 0.12, 0.1), (0, 0, 0.06), M=M)
hinge("Paint3", (-0.38, -0.25, 0.06), (-0.38, 0.25, 0.06), 0.03, 3, M=M)
for i in (-1, 0, 1):
    Mp = gl(2.67, i * 0.25, 0.04)
    box("Paint2", (0.18, 0.22, 0.12), (0, 0, 0.05), w=0.015, M=Mp)
    box("Paint3", (0.06, 0.24, 0.04), (0.06, 0, 0.12), w=0.008, M=Mp)
    box("Glass", (0.012, 0.17, 0.065), (0.09, 0, 0.05), rot=(0, -0.55, 0), M=Mp)
    bolt((-0.06, -0.08, 0.11), M=Mp, r=0.01, h=0.015); bolt((-0.06, 0.08, 0.11), M=Mp, r=0.01, h=0.015)
box("Paint2", (0.06, 1.9, 0.09), (0, 0, 0.04), w=0.015, M=gl(2.95, 0))
for y in (-0.9, -0.3, 0.3, 0.9):
    box("Paint3", (0.12, 0.03, 0.08), (-0.06, y, 0.03), w=0.006, M=gl(2.95, 0))
# glacis plate bolts and lifting eyes
for x in (3.6, 3.3):
    for y in [k * 0.3 - 1.5 for k in range(11)]:
        if abs(y) > 0.45 or x < 3.4:
            bolt((0, y, 0), M=gl(x, 0), r=0.016)
for y in (-1.35, 1.35):
    M = gl(2.8, y)
    box("Paint3", (0.14, 0.05, 0.04), (0, 0, 0.02), w=0.01, M=M)
    torus("Paint3", 0.065, 0.022, (0, 0, 0.08), (RX, 0, 0), 32, 10, M=M)
# headlight clusters with brush guards
for s in (-1, 1):
    y = s * 1.5
    M = gl(3.72, y)
    box("Paint2", (0.26, 0.36, 0.2), (0, 0, 0.09), w=0.03, M=M)
    for dy, r in ((-0.07 * s, 0.07), (0.08 * s, 0.05)):
        lathe("Paint3", [(0, 0), (r + 0.015, 0), (r + 0.018, 0.04), (r, 0.045), (r * 0.95, 0.02), (0, 0.02)], (0.13, dy, 0.1), "x", 40)
        lathe("Lens", [(0, 0.0), (r * 0.95, 0.0), (r * 0.6, 0.012), (0, 0.016)], (0.15, dy, 0.1), "x", 40)
    tube("Paint3", [(-0.12, -0.2, 0.0), (0.2, -0.2, 0.08), (0.2, -0.2, 0.26), (0.2, 0.2, 0.26), (0.2, 0.2, 0.08), (-0.12, 0.2, 0.0)], 0.016, 12, M=M)
    rod("Paint3", (0.2, 0, 0.08), (0.2, 0, 0.26), 0.012, 10, M=M)
    # front towing clevises: twin lugs welded to the nose, pin, and a hanging shackle
    ty = s * 0.78
    box("Paint3", (0.12, 0.42, 0.3), (4.08, ty, 0.86), w=0.025)
    for dy in (-0.09, 0.09):
        hullp("Paint3", [(4.1, ty + dy - 0.022, 0.74), (4.1, ty + dy + 0.022, 0.74), (4.1, ty + dy - 0.022, 0.98), (4.1, ty + dy + 0.022, 0.98),
                         (4.36, ty + dy - 0.022, 0.8), (4.36, ty + dy + 0.022, 0.8), (4.36, ty + dy - 0.022, 0.9), (4.36, ty + dy + 0.022, 0.9)], 0.012, 2, 20)
        weld((4.11, ty + dy - 0.03, 0.74), (4.11, ty + dy - 0.03, 0.98), 0.01)
    rod("Gunmetal", (4.3, ty - 0.14, 0.85), (4.3, ty + 0.14, 0.85), 0.03, 24)
    for e in (-0.14, 0.14):
        cyl("Gunmetal", 0.045, 0.02, (4.3, ty + e, 0.85), "y", 24)
    torus("Gunmetal", 0.075, 0.02, (4.42, ty, 0.82), (0, 0.3, 0), 32, 10)
    # hinged front fender extension: sloped plate, folded side flanges, ribs, rubber flap
    fy = s * 1.42
    hz = gz(3.72) + 0.02
    beam("Paint2", (3.72, fy, hz), (4.38, fy, 1.02), 0.64, 0.03, 0.008)
    for e in (-0.31, 0.31):
        beam("Paint2", (3.74, fy + e, hz - 0.06), (4.38, fy + e, 0.96), 0.025, 0.12, 0.006)
    for e in (-0.12, 0.12):
        beam("Paint3", (3.76, fy + e, hz - 0.03), (4.34, fy + e, 1.0), 0.03, 0.05, 0.006)
    hinge("Paint3", (3.72, fy - 0.3, hz + 0.02), (3.72, fy + 0.3, hz + 0.02), 0.028, 4)
    box("Rubber", (0.022, 0.62, 0.36), (4.39, fy, 0.84), w=0.006)
    for k in range(5):
        bolt((4.405, fy + (k - 2) * 0.13, 0.97), "x", 0.011, 0.014)
    box("Paint3", (0.03, 0.66, 0.04), (4.39, fy, 0.99), w=0.006)
    # nose plate lip and corner welds
    weld((4.06, s * 1.1, 0.74), (4.04, s * 1.1, 1.11))
box("Paint2", (0.1, 2.24, 0.07), (4.09, 0, 0.73), w=0.02)
for y in [k * 0.25 - 1.0 for k in range(9)]:
    bolt((4.14, y, 0.73), "x", 0.014)
# driver's armoured hood behind the periscopes
hullp("Paint", [tuple(gl(2.48, 0) @ Vector(p)) for p in [(-0.22, -0.5, -0.02), (-0.22, 0.5, -0.02), (0.22, -0.45, -0.02), (0.22, 0.45, -0.02),
                                                         (-0.2, -0.46, 0.12), (-0.2, 0.46, 0.12), (0.06, -0.4, 0.12), (0.06, 0.4, 0.12)]], 0.02, 3, 15)
# engine deck: grilles, doors, fuel caps, panel bolts
for g in range(2):
    gxp = -2.55 - g * 0.85
    box("Paint2", (0.82, 2.05, 0.05), (gxp, 0, DECK + 0.02), w=0.012)
    box("Dark", (0.72, 1.92, 0.02), (gxp, 0, DECK + 0.04))
    for k in range(26):
        box("Paint3", (0.022, 1.9, 0.04), (gxp - 0.34 + k * 0.0272, 0, DECK + 0.055), (0, 0.5, 0), w=0.004, seg=1)
    rod("Paint3", (gxp, -0.96, DECK + 0.075), (gxp, 0.96, DECK + 0.075), 0.012, 10)
    bolt_rect(gxp, 0, DECK + 0.045, 0.78, 2.0, 0.2, 0.012)
for x in (-0.95, -1.75):
    for s in (-1, 1):
        y = s * 0.62
        box("Paint2", (0.76, 0.96, 0.03), (x, y, DECK + 0.012), w=0.01)
        hinge("Paint3", (x - 0.3, y - s * 0.5, DECK + 0.03), (x + 0.3, y - s * 0.5, DECK + 0.03), 0.026, 3)
        handle("Paint3", (x + 0.2, y + s * 0.3, DECK + 0.03), (x - 0.0, y + s * 0.3, DECK + 0.03), (0, 0, 0.05))
        bolt_rect(x, y, DECK + 0.027, 0.66, 0.86, 0.17, 0.012)
for s in (-1, 1):
    M = xf((-3.5, s * 1.5, DECK))
    lathe("Paint3", [(0.0, 0.0), (0.14, 0.0), (0.14, 0.03), (0.12, 0.04), (0.11, 0.06), (0.1, 0.075), (0.0, 0.08)], M=M, n=48, w=0.003)
    for k in range(4):
        a = TAU * k / 4
        box("Paint3", (0.04, 0.025, 0.03), (math.cos(a) * 0.105, math.sin(a) * 0.105, 0.08), (0, 0, a), w=0.004, M=M)
    ring_of(8, 0.155, lambda a, x, y: bolt((x, y, 0.0), M=M, r=0.01, h=0.015))
    for x in [1.7 - k * 0.32 for k in range(18)]:
        bolt((x, s * 1.6, DECK), r=0.014)
# rear plate: louvred exhaust, tail lights, phone box, pintle, tow cable
box("Paint3", (0.07, 1.6, 0.62), (-4.0, 0, 1.3), w=0.015)
box("Dark", (0.05, 1.48, 0.54), (-4.02, 0, 1.3))
for k in range(18):
    box("Paint2", (0.07, 1.46, 0.022), (-4.05, 0, 1.06 + k * 0.03), (0, -0.6, 0), w=0.004, seg=1)
for s in (-1, 1):
    y = s * 1.58
    box("Paint2", (0.1, 0.24, 0.3), (-4.02, y, 1.48), w=0.015)
    box("RedLens", (0.02, 0.1, 0.07), (-4.08, y - s * 0.05, 1.55), w=0.006)
    box("Lens", (0.02, 0.1, 0.07), (-4.08, y - s * 0.05, 1.42), w=0.006)
    box("RedLens", (0.02, 0.06, 0.06), (-4.08, y + s * 0.07, 1.48), w=0.006)
    tube("Paint3", [(-3.98, y - 0.15, 1.3), (-4.16, y - 0.15, 1.3), (-4.16, y - 0.15, 1.66), (-4.16, y + 0.15, 1.66), (-4.16, y + 0.15, 1.3), (-3.98, y + 0.15, 1.3)], 0.014, 12)
    box("Rubber", (0.025, 0.58, 0.4), (-4.02, s * 1.42, 1.0), w=0.008)
    box("Paint3", (0.3, 0.14, 0.18), (-4.05, s * 0.7, 0.82), w=0.03)
    torus("Paint3", 0.1, 0.03, (-4.22, s * 0.7, 0.82), (RX, 0, 0), 40, 12)
box("Paint2", (0.14, 0.3, 0.38), (-4.07, -1.05, 1.3), w=0.02)                 # infantry phone box
hinge("Paint3", (-4.15, -1.21, 1.13), (-4.15, -1.21, 1.47), 0.015, 3)
box("Paint3", (0.02, 0.05, 0.1), (-4.15, -0.92, 1.3), w=0.005)
lathe("Gunmetal", [(0, 0), (0.09, 0), (0.09, 0.2), (0.07, 0.24), (0, 0.24)], (-4.03, 0, 0.75), "-x", 32)
torus("Gunmetal", 0.09, 0.028, (-4.33, 0, 0.75), (0, 0, 0), 40, 12)
cable = [(-4.03, -1.3, 1.66)] + [(-4.07 - 0.01 * math.sin(k), -1.1 + k * 0.2, 1.62 - 0.05 * math.sin(k * 0.6)) for k in range(12)] + [(-4.03, 1.3, 1.66)]
tube("Track", cable, 0.022, 10)
for e in (cable[0], cable[-1]):
    torus("Track", 0.07, 0.02, (e[0], e[1], e[2] + 0.05), (0, RY, 0), 24, 8)

# ================================================================== RUNNING GEAR
TW = 0.64
def road_wheel_half(M):
    lathe("Rubber", [(0.262, -0.09), (0.30, -0.09), (0.312, -0.085), (0.319, -0.07), (0.32, -0.05), (0.32, 0.05),
                     (0.319, 0.07), (0.312, 0.085), (0.30, 0.09), (0.262, 0.09)], M=M, n=72, closed=True)
    lathe("Paint2", [(0.06, -0.1), (0.13, -0.1), (0.16, -0.065), (0.24, -0.075), (0.262, -0.092), (0.272, -0.09),
                     (0.272, 0.09), (0.262, 0.092), (0.24, 0.075), (0.16, 0.065), (0.13, 0.1), (0.06, 0.1)], M=M, n=72, closed=True)
    for k in range(6):
        a = TAU * k / 6 + 0.26
        cyl("Dark", 0.034, 0.02, (math.cos(a) * 0.2, math.sin(a) * 0.2, 0.065), n=24, M=M)
        cyl("Dark", 0.034, 0.02, (math.cos(a) * 0.2, math.sin(a) * 0.2, -0.065), n=24, M=M)
    ring_of(10, 0.1, lambda a, x, y: bolt((x, y, 0.1), r=0.013, h=0.02, M=M))

def sprocket_ring(n, rr, rt, per=16):
    pts = []
    for t in range(n):
        for k in range(per):
            u = k / per
            a = TAU * (t + u) / n
            s = max(0.0, 1 - abs(u - 0.5) / 0.3)
            s = s * s * (3 - 2 * s)
            r = rr + (rt - rr) * s
            pts.append((math.cos(a) * r, math.sin(a) * r))
    return pts

@functools.lru_cache(None)
def G_link(tw):
    gs = [gx(G_box(0.13, tw - 0.02, 0.032, "Track", 0.008, 2), I4)]
    for yy in (-0.17, 0.17):
        gs.append(gx(G_box(0.118, 0.25, 0.042, "Rubber", 0.012, 3), xf((0, yy, -0.036))))
        gs.append(gx(G_box(0.12, 0.2, 0.022, "Rubber", 0.006, 2), xf((0, yy, 0.026))))
        for xx in (-0.03, 0.03):
            gs.append(gx(G_box(0.012, 0.22, 0.006, "Rubber", 0.0, 1), xf((xx, yy, -0.058))))
    gs.append(gx(G_hull(((-0.045, -0.035, 0.015), (0.045, -0.035, 0.015), (-0.045, 0.035, 0.015), (0.045, 0.035, 0.015),
                         (-0.022, -0.022, 0.15), (0.022, -0.022, 0.15), (-0.022, 0.022, 0.15), (0.022, 0.022, 0.15)),
                        "Track", 0.008, 2, 20), I4))
    for yy in (-1, 1):
        y = yy * (tw / 2 + 0.025)
        gs.append(gx(G_box(0.065, 0.06, 0.075, "Track", 0.01, 2), xf((0.08, y, 0))))
        gs.append(gx(G_cyl(0.024, 0.03, "Track", 6, w=0.003, seg=1), xf((0.08, y + yy * 0.042, 0), AX["y"])))
        gs.append(gx(G_cyl(0.01, 0.05, "Track", 8), xf((0.08, y, 0.035))))
    gs.append(gx(G_cyl(0.019, tw + 0.05, "Track", 12), xf((0.08, 0, 0), AX["y"])))
    gs.append(gx(G_cyl(0.019, tw + 0.05, "Track", 12), xf((-0.08, 0, 0), AX["y"])))
    gs.append(gx(G_box(0.07, 0.1, 0.05, "Track", 0.01, 2), xf((0.08, 0, -0.005))))
    return cat(*gs)

for s in (-1, 1):
    part("Running gear " + ("L" if s > 0 else "R"), "hull")
    yc = s * (W / 2 - 0.45)
    wheels = [(2.75 - i * 0.92, 0.36, 0.32) for i in range(7)]
    idler = (3.55, 0.58, 0.32)
    sprocket = (-3.62, 0.78, 0.33)
    rollers = [(1.6, 1.18, 0.1), (-1.2, 1.18, 0.1)]
    ay = s * 1.07   # road arms run just inside the hull side
    for (x, z, r) in wheels:
        for dy in (-0.15, 0.15):
            road_wheel_half(xf((x, yc + dy, z), AX["y"]))
        lathe("Paint3", [(0.0, 0.0), (0.085, 0.0), (0.085, 0.03), (0.07, 0.06), (0.04, 0.075), (0.0, 0.08)],
              (x, yc + s * 0.25, z), "y" if s > 0 else "-y", 40, w=0.003)
        rod("Paint3", (x, ay, z), (x, yc, z), 0.06, 32)
        beam("Paint3", (x, ay, z), (x - 0.5, ay, z + 0.24), 0.11, 0.13, 0.02)
        lathe("Paint3", [(0.0, -0.08), (0.11, -0.08), (0.12, -0.06), (0.12, 0.06), (0.11, 0.08), (0.0, 0.08)],
              (x - 0.5, ay, z + 0.24), "y", 40)
        ring_of(6, 0.085, lambda a, cx, cz: bolt((x - 0.5 + cx, ay + s * 0.08, z + 0.24 + cz), "y" if s > 0 else "-y", 0.012))
    for i in (0, 1, 6):   # shock absorbers on the first two and last stations
        x, z, r = wheels[i]
        rod("Gunmetal", (x - 0.22, ay - s * 0.08, z + 0.12), (x - 0.02, ay - s * 0.08, z + 0.62), 0.05, 24)
        rod("Paint3", (x - 0.16, ay - s * 0.08, z + 0.26), (x - 0.06, ay - s * 0.08, z + 0.5), 0.062, 24)
    # idler and its tensioner arm
    x, z, r = idler
    for dy in (-0.15, 0.15):
        M = xf((x, yc + dy, z), AX["y"])
        lathe("Paint2", [(0.05, -0.09), (0.12, -0.09), (0.15, -0.06), (0.28, -0.07), (0.31, -0.085), (0.32, -0.08),
                         (0.32, 0.08), (0.31, 0.085), (0.28, 0.07), (0.15, 0.06), (0.12, 0.09), (0.05, 0.09)], M=M, n=72, closed=True)
        for k in range(6):
            a = TAU * k / 6
            cyl("Dark", 0.05, 0.16, (math.cos(a) * 0.21, math.sin(a) * 0.21, 0), n=24, M=M)
    rod("Paint3", (x, ay, z), (x, yc + s * 0.25, z), 0.07, 32)
    beam("Paint3", (x, ay, z), (x - 0.55, ay, z + 0.3), 0.12, 0.15, 0.02)
    rod("Gunmetal", (x - 0.45, ay - s * 0.08, z + 0.2), (x - 1.05, ay - s * 0.08, z + 0.55), 0.045, 24)
    # sprocket with final drive
    x, z, r = sprocket
    for dy in (-0.2, 0.2):
        prism("Paint3", sprocket_ring(11, 0.29, 0.36), 0.06, (x, yc + dy, z), AX["y"], w=0.006, seg=2, angle=60)
        ring_of(11, 0.25, lambda a, cx, cz: bolt((x + cx, yc + dy + s * 0.03, z + cz), "y" if s > 0 else "-y", 0.012), 0.14)
    lathe("Paint2", [(0.0, -0.22), (0.24, -0.22), (0.25, -0.2), (0.25, 0.2), (0.24, 0.22), (0.0, 0.22)], (x, yc, z), "y", 64)
    lathe("Paint3", [(0.0, 0.0), (0.16, 0.0), (0.16, 0.04), (0.14, 0.08), (0.08, 0.11), (0.0, 0.12)],
          (x, yc + s * 0.23, z), "y" if s > 0 else "-y", 48, w=0.004)
    ring_of(12, 0.13, lambda a, cx, cz: bolt((x + cx, yc + s * 0.27, z + cz), "y" if s > 0 else "-y", 0.012))
    lathe("Paint3", [(0.0, 0.0), (0.38, 0.0), (0.38, 0.12), (0.3, 0.2), (0.0, 0.22)], (x, ay + s * 0.1, z), "-y" if s > 0 else "y", 48)
    # return rollers on brackets
    for (x, z, r) in rollers:
        for dy in (-0.12, 0.12):
            lathe("Rubber", [(0.0, -0.07), (0.1, -0.07), (0.11, -0.05), (0.11, 0.05), (0.1, 0.07), (0.0, 0.07)], (x, yc + dy, z), "y", 40)
            lathe("Paint3", [(0.0, -0.075), (0.07, -0.075), (0.07, 0.075), (0.0, 0.075)], (x, yc + dy, z), "y", 32)
        rod("Paint3", (x, ay, z), (x, yc + s * 0.22, z), 0.035, 24)
        box("Paint3", (0.16, 0.12, 0.2), (x, ay, z + 0.05), w=0.02)
    # tracks
    part("Track " + ("L" if s > 0 else "R"), "hull")
    links, pitch = belt(wheels + [idler, sprocket] + rollers, 0.16, 0.04)
    for (x, z, a) in links:
        put(G_link(TW), xf((x, yc, z), (0, -a, 0), (pitch / 0.16, 1, 1)))
    # skirts
    part("Skirts " + ("L" if s > 0 else "R"), "hull")
    ys = s * (W / 2 - 0.02)
    so = "y" if s > 0 else "-y"
    for i in range(8):
        x0 = 3.6 - i * 0.92
        heavy = i < 3
        t = 0.2 if heavy else 0.1
        yo = ys - s * (0.0 if heavy else 0.05)
        zb, zt = 0.705, 1.655
        if i == 0:
            poly = [(x0 - 0.9, zb), (x0 - 0.3, zb), (x0, zb + 0.32), (x0, zt), (x0 - 0.9, zt)]
        else:
            poly = [(x0 - 0.9, zb), (x0 - 0.02, zb), (x0 - 0.02, zt), (x0 - 0.9, zt)]
        prism("Paint", [(px, -pz) for px, pz in poly], t, (0, yo, 0), (-RX, 0, 0), w=0.025, seg=3, angle=30)
        face = yo + s * t / 2
        xc = x0 - 0.46
        if heavy:
            box("Paint2", (0.66, 0.03, 0.6), (xc, face + s * 0.012, 1.13), w=0.012)
            for bx in (-0.29, 0.0, 0.29):
                for bz in (0.86, 1.4):
                    bolt((xc + bx, face + s * 0.027, bz), so, 0.016)
            handle("Paint3", (xc - 0.15, face, 1.53), (xc + 0.15, face, 1.53), (0, s * 0.06, 0))
        else:
            for bz in (0.95, 1.3):
                box("Paint2", (0.84, 0.02, 0.04), (xc, face + s * 0.008, bz), w=0.006)
        for bx in (-0.32, -0.11, 0.11, 0.32):
            bolt((xc + bx, face, zt - 0.07), so, 0.016)
        hinge("Paint3", (xc - 0.3, yo, zt + 0.02), (xc - 0.05, yo, zt + 0.02), 0.025, 2)
        hinge("Paint3", (xc + 0.05, yo, zt + 0.02), (xc + 0.3, yo, zt + 0.02), 0.025, 2)
        box("Rubber", (0.88, 0.025, 0.08), (xc, yo, zb - 0.03), w=0.006)
        if i == 0:
            box("Dark", (0.26, 0.03, 0.12), (xc - 0.05, face, 1.0), w=0.01)
            box("Paint3", (0.24, 0.05, 0.02), (xc - 0.05, face, 0.95), w=0.004)
            handle("Paint3", (xc + 0.2, face, 1.45), (xc + 0.2, face, 1.2), (0, s * 0.06, 0))
        for bx in (-0.35, 0.0, 0.35):
            bolt((xc + bx, yo + s * 0.013, zb - 0.02), so, 0.01, 0.014)
    box("Paint2", (7.7, 0.27, 0.045), (-0.1, s * (W / 2 - 0.12), DECK - 0.07), w=0.012)

# ================================================================== FRONT EXTRAS
part("Hull", "hull")
for s in (-1, 1):
    # spare track links on the glacis, held by a retaining bar and two J-bolts
    for k in range(4):
        put(G_link(TW), gl(2.92 + k * 0.165, s * 1.36, 0.095) @ xf(rot=(0, 0, 0)))
    M = gl(3.17, s * 1.36, 0.0)
    for e in (-0.27, 0.27):
        box("Paint3", (0.06, 0.05, 0.24), (0, e, 0.12), w=0.008, M=M)
        rod("Paint3", (-0.36, e, 0.22), (0.36, e, 0.22), 0.016, 12, M=M)
        bolt((0.0, e, 0.24), "z", 0.013, 0.02, M=M)
    # headlight conduit from the cluster into the hull
    tube("Gunmetal", [tuple(gl(3.6, s * 1.5) @ Vector(q)) for q in [(-0.12, -s * 0.12, 0.05), (-0.25, -s * 0.14, 0.02), (-0.6, -s * 0.2, 0.012)]], 0.013, 10)
    box("Paint3", (0.07, 0.05, 0.025), (0, 0, 0.012), w=0.005, M=gl(3.3, s * 1.32))
    # tie-down rings and reflectors on the nose
    for y in (0.35, 0.55):
        box("Paint3", (0.08, 0.06, 0.05), (4.09, s * y, 1.0), w=0.01)
        torus("Paint3", 0.045, 0.012, (4.15, s * y, 0.96), (0, 0, 0), 24, 8)
    lathe("Paint3", [(0, 0), (0.05, 0), (0.05, 0.015), (0, 0.015)], (4.07, s * 1.0, 1.0), "x", 24)
    lathe("RedLens", [(0, 0), (0.04, 0), (0.035, 0.012), (0, 0.016)], (4.085, s * 1.0, 1.0), "x", 24)
# bumper number plates on the nose
for y in (-0.5, 0.5):
    box("Paint2", (0.012, 0.42, 0.14), (4.086, y, 0.9), w=0.004)
    for k in range(4):
        box("Dark", (0.004, 0.05, 0.09), (4.093, y - 0.12 + k * 0.08, 0.9))
# tow cable stowed across the glacis in four clamps, eyes looped over pins
M = gl(3.45, 0, 0.0)
cpts = [(0.0, -1.05, 0.035)] + [(0.015 * math.sin(k * 1.3), -0.95 + k * 0.19, 0.045) for k in range(11)] + [(0.0, 1.05, 0.035)]
tube("Track", cpts, 0.028, 14, M=M)
for e in (-1, 1):
    torus("Track", 0.07, 0.018, (0.0, e * 1.12, 0.04), (0, 0, 0), 24, 8, M=M)
    lathe("Paint3", [(0, 0), (0.025, 0), (0.025, 0.08), (0.035, 0.09), (0, 0.1)], (0.0, e * 1.12, 0.0), M=M, n=16)
for y in (-0.7, -0.25, 0.25, 0.7):
    box("Paint3", (0.08, 0.05, 0.02), (0, y, 0.01), w=0.004, M=M)
    tube("Paint3", [(-0.03, y - 0.04, 0.0), (-0.03, y - 0.04, 0.06), (-0.03, y + 0.04, 0.06), (-0.03, y + 0.04, 0.0)], 0.008, 8, M=M)
    rod("Paint3", (-0.03, y, 0.06), (0.04, y, 0.06), 0.008, 8, M=M)
# turret ring splash guard in front of the turret
for k in range(9):
    a = -0.9 + k * 0.225
    box("Paint2", (0.05, 0.36, 0.12), (GX + math.cos(a) * 1.55, math.sin(a) * 1.55, DECK + 0.06), (0, 0, a), w=0.012)
    bolt((GX + math.cos(a) * 1.58, math.sin(a) * 1.58, DECK + 0.1), (0, RY, a), 0.011, 0.014)

# ================================================================== TURRET (origin = ring centre on the deck)
part("Turret", "turret")
hullp("Paint", [
    (1.78, -0.9, 0.06), (1.78, 0.9, 0.06), (1.2, -1.8, 0.06), (1.2, 1.8, 0.06),
    (-1.95, -1.86, 0.06), (-1.95, 1.86, 0.06), (-2.8, -1.62, 0.12), (-2.8, 1.62, 0.12),
    (1.78, -0.9, 0.8), (1.78, 0.9, 0.8), (1.15, -1.74, 0.8), (1.15, 1.74, 0.8),
    (-1.95, -1.8, 0.8), (-1.95, 1.8, 0.8), (-2.78, -1.58, 0.78), (-2.78, 1.58, 0.78)], 0.055, 5, 8)
for s in (-1, 1):
    cheek = []
    for (x, y, z) in [(2.02, 0.43, 0.06), (2.02, 1.3, 0.06), (1.2, 1.8, 0.06),
                      (2.32, 0.43, 0.24), (2.32, 1.38, 0.24), (1.28, 1.83, 0.24),
                      (2.2, 0.43, 0.72), (2.2, 1.33, 0.72), (1.22, 1.77, 0.72),
                      (2.02, 0.43, 0.8), (2.02, 1.26, 0.8), (1.15, 1.74, 0.8),
                      (1.0, 0.43, 0.06), (1.0, 0.43, 0.8)]:
        cheek.append((x, s * y, z))
    hullp("Paint", cheek, 0.045, 5, 8)
    # cheek face: lifting eye, lower lip weld, armour edge weld
    box("Paint3", (0.14, 0.05, 0.05), (1.7, s * 1.2, 0.82), w=0.012)
    torus("Paint3", 0.07, 0.02, (1.7, s * 1.2, 0.9), (RX, 0, 0), 32, 10)
    weld((2.3, s * 0.45, 0.245), (2.3, s * 1.36, 0.245), 0.011)
    weld((2.03, s * 0.44, 0.79), (2.03, s * 1.25, 0.79), 0.011)
box("Dark", (0.06, 0.88, 0.74), (1.8, 0, 0.43))
lathe("Paint3", [(1.12, -0.02), (1.22, -0.02), (1.24, 0.0), (1.24, 0.07), (1.12, 0.07)], n=128, closed=True)
ring_of(48, 1.2, lambda a, x, y: bolt((x, y, 0.07), r=0.012, h=0.016))
# armour package seams and front roof bolts
for s in (-1, 1):
    weld((2.0, s * 1.785, 0.08), (1.15, s * 1.745, 0.78))
    weld((-1.0, s * 1.83, 0.08), (-1.0, s * 1.78, 0.78))
    weld((2.3, s * 1.39, 0.26), (2.2, s * 1.34, 0.71))
    for k in range(12):
        bolt((1.0 - k * 0.25, s * 1.68, 0.8), r=0.014)
weld((1.15, -1.7, 0.802), (1.15, 1.7, 0.802))
# blow-off panels over the bustle ammunition
for s in (-1, 1):
    box("Paint2", (1.3, 1.25, 0.03), (-1.85, s * 0.82, 0.81), w=0.01)
    bolt_rect(-1.85, s * 0.82, 0.825, 1.2, 1.15, 0.12, 0.014)
    weld((-1.2, s * 0.15, 0.81), (-2.5, s * 0.15, 0.81), 0.01)
# gunner's primary sight doghouse with armoured doors
hullp("Paint", [(1.55, -0.6, 0.78), (1.55, -1.25, 0.78), (0.6, -0.6, 0.78), (0.6, -1.25, 0.78),
                (1.3, -0.6, 1.2), (1.3, -1.25, 1.2), (0.6, -0.6, 1.2), (0.6, -1.25, 1.2)], 0.035, 4, 10)
box("Dark", (0.03, 0.55, 0.3), (1.43, -0.925, 1.0), (0, -0.27, 0))
box("Glass", (0.015, 0.48, 0.24), (1.445, -0.925, 1.0), (0, -0.27, 0))
for s, yy in ((1, -0.6), (-1, -1.25)):
    box("Paint2", (0.32, 0.035, 0.34), (1.56, yy + s * 0.07, 1.0), (0, 0, -s * 0.5), w=0.008)
    hinge("Paint3", (1.42, yy, 0.84), (1.42, yy, 1.16), 0.018, 3)
bolt_rect(0.95, -0.925, 1.205, 0.6, 0.55, 0.15, 0.013)
handle("Paint3", (0.75, -1.27, 1.1), (1.1, -1.27, 1.1), (0, -0.05, 0))
# commander's cupola
cx, cy = -0.55, -0.95
lathe("Paint2", [(0.0, 0.78), (0.54, 0.78), (0.55, 0.8), (0.55, 0.98), (0.53, 1.02), (0.48, 1.06), (0.0, 1.06)], (cx, cy, 0), n=96, w=0.004)
ring_of(24, 0.5, lambda a, x, y: bolt((cx + x, cy + y, 1.06), r=0.011, h=0.016))
for k in range(6):
    a = TAU * k / 6 + 0.3
    M = xf((cx + math.cos(a) * 0.53, cy + math.sin(a) * 0.53, 0.9), (0, 0, a))
    box("Paint3", (0.12, 0.2, 0.16), (0, 0, 0), w=0.015, M=M)
    box("Glass", (0.02, 0.15, 0.09), (0.06, 0, 0.0), w=0.004, M=M)
    box("Paint3", (0.08, 0.22, 0.025), (0.05, 0, 0.085), w=0.006, M=M)
Mh = xf((cx, cy, 1.06), (0, -0.18, 0))
lathe("Paint2", [(0.0, 0.0), (0.44, 0.0), (0.44, 0.03), (0.42, 0.05), (0.35, 0.075), (0.2, 0.09), (0.0, 0.095)], M=Mh @ xf((-0.0, 0, 0.0)), n=96, w=0.003)
handle("Paint3", (-0.1, 0.12, 0.08), (0.1, 0.12, 0.08), (0, 0, 0.05), M=Mh)
hinge("Paint3", (-0.42, -0.2, 0.03), (-0.42, 0.2, 0.03), 0.028, 3, M=Mh)
torus("Paint3", 0.47, 0.022, (cx, cy, 1.07), (0, 0, 0), 96, 10)
# .50 cal on its cradle
part("Commander MG", "turret")
mx, mz = cx + 0.2, 1.2
box("Paint3", (0.1, 0.1, 0.22), (mx, cy, 1.1), w=0.01)
box("Paint3", (0.42, 0.2, 0.08), (mx + 0.2, cy, 1.24), w=0.012)
for dy in (-0.09, 0.09):
    box("Paint3", (0.18, 0.02, 0.16), (mx + 0.25, cy + dy, 1.33), w=0.006)
box("Gunmetal", (0.62, 0.14, 0.17), (cx + 0.5, cy, 1.43), w=0.01)
box("Gunmetal", (0.5, 0.15, 0.03), (cx + 0.52, cy, 1.525), w=0.005)
for k in range(5):
    box("Dark", (0.03, 0.142, 0.1), (cx + 0.3 + k * 0.08, cy, 1.43), w=0.003)
lathe("Gunmetal", [(0.0, 0.0), (0.06, 0.0), (0.06, 0.25), (0.045, 0.27), (0.045, 0.3), (0.0, 0.3)], (cx + 0.8, cy, 1.44), "x", 32)
for k in range(8):
    cyl("Dark", 0.012, 0.13, (cx + 0.86 + k * 0.026, cy, 1.44), (0, 0, 0), 12)
lathe("Gunmetal", [(0.0, 0.0), (0.03, 0.0), (0.03, 0.75), (0.032, 0.76), (0.04, 0.8), (0.045, 0.92), (0.035, 0.94), (0.02, 0.94), (0.0, 0.92)], (cx + 1.0, cy, 1.44), "x", 32)
tube("Gunmetal", [(cx + 0.15, cy - 0.07, 1.42), (cx + 0.08, cy - 0.07, 1.42), (cx + 0.08, cy - 0.07, 1.33)], 0.016, 12)
tube("Gunmetal", [(cx + 0.15, cy + 0.07, 1.42), (cx + 0.08, cy + 0.07, 1.42), (cx + 0.08, cy + 0.07, 1.33)], 0.016, 12)
cyl("Rubber", 0.022, 0.1, (cx + 0.08, cy - 0.07, 1.37), n=16)
cyl("Rubber", 0.022, 0.1, (cx + 0.08, cy + 0.07, 1.37), n=16)
handle("Gunmetal", (cx + 0.75, cy, 1.52), (cx + 0.95, cy, 1.52), (0, 0, 0.08))
box("Ammo", (0.3, 0.12, 0.2), (cx + 0.5, cy + 0.17, 1.36), w=0.012)
box("Ammo", (0.31, 0.13, 0.025), (cx + 0.5, cy + 0.17, 1.465), w=0.006)
handle("Paint3", (cx + 0.42, cy + 0.17, 1.48), (cx + 0.58, cy + 0.17, 1.48), (0, 0, 0.03))
for k in range(6):
    lathe("Brass", [(0.0, 0.0), (0.011, 0.0), (0.011, 0.07), (0.007, 0.085), (0.0, 0.1)], (cx + 0.4 + k * 0.025, cy + 0.1, 1.46), (-0.4, 0, 0), 10)
# loader's hatch and M240
part("Turret", "turret")
lx, ly = -0.45, 0.9
lathe("Paint3", [(0.0, 0.78), (0.46, 0.78), (0.46, 0.84), (0.0, 0.84)], (lx, ly, 0), n=96)
torus("Paint3", 0.5, 0.025, (lx, ly, 0.87), (0, 0, 0), 96, 10)
lathe("Paint2", [(0.0, 0.0), (0.4, 0.0), (0.4, 0.03), (0.37, 0.055), (0.2, 0.07), (0.0, 0.075)], (lx, ly, 0.84), n=96, w=0.003)
ring_of(20, 0.43, lambda a, x, y: bolt((lx + x, ly + y, 0.84), r=0.011, h=0.016))
handle("Paint3", (lx - 0.1, ly - 0.15, 0.91), (lx + 0.1, ly - 0.15, 0.91), (0, 0, 0.05))
hinge("Paint3", (lx - 0.38, ly - 0.2, 0.87), (lx - 0.38, ly + 0.2, 0.87), 0.026, 3)
box("Paint3", (0.12, 0.2, 0.12), (lx + 0.35, ly - 0.45, 0.86), w=0.015)
box("Glass", (0.015, 0.15, 0.06), (lx + 0.41, ly - 0.45, 0.86), w=0.004)
part("Loader MG", "turret")
gx0, gy = lx + 0.32, ly + 0.25
rod("Paint3", (gx0, gy, 0.85), (gx0, gy, 1.12), 0.035, 24)
box("Paint3", (0.14, 0.08, 0.06), (gx0 + 0.05, gy, 1.14), w=0.008)
box("Gunmetal", (0.42, 0.08, 0.12), (gx0 + 0.15, gy, 1.22), w=0.008)
box("Gunmetal", (0.38, 0.085, 0.02), (gx0 + 0.15, gy, 1.29), w=0.004)
box("Gunmetal", (0.32, 0.05, 0.1), (gx0 - 0.18, gy, 1.2), (0, 0.12, 0), w=0.012)
box("Gunmetal", (0.04, 0.03, 0.1), (gx0 + 0.08, gy, 1.12), (0, -0.3, 0), w=0.006)
lathe("Gunmetal", [(0.0, 0.0), (0.018, 0.0), (0.018, 0.5), (0.024, 0.52), (0.026, 0.58), (0.018, 0.6), (0.0, 0.6)], (gx0 + 0.36, gy, 1.24), "x", 24)
rod("Gunmetal", (gx0 + 0.36, gy, 1.2), (gx0 + 0.7, gy, 1.2), 0.012, 12)
for side in (-1, 1):
    rod("Gunmetal", (gx0 + 0.66, gy, 1.2), (gx0 + 0.42, gy + side * 0.05, 1.19), 0.008, 8)
box("Ammo", (0.24, 0.1, 0.16), (gx0 + 0.12, gy - 0.1, 1.18), w=0.01)
# wind sensor, antennas, rails, lifting eyes, loader periscope
part("Turret", "turret")
lathe("Paint3", [(0.0, 0.0), (0.06, 0.0), (0.06, 0.04), (0.025, 0.06), (0.018, 0.5), (0.0, 0.5)], (-1.9, 0.0, 0.8), n=24)
rod("Paint3", (-1.9, -0.15, 1.3), (-1.9, 0.15, 1.3), 0.012, 12)
for y in (-0.15, 0.15):
    lathe("Paint3", [(0.0, 0.0), (0.02, 0.0), (0.02, 0.12), (0.0, 0.14)], (-1.9, y, 1.3), n=16)
    sphere("Paint3", 0.025, (-1.9, y, 1.45), 16)
for y in (-1.35, 1.35):
    lathe("Paint3", [(0.0, 0.0), (0.09, 0.0), (0.09, 0.05), (0.07, 0.06), (0.06, 0.16), (0.03, 0.2), (0.0, 0.2)], (-2.45, y, 0.79), n=32, w=0.003)
    rod("Dark", (-2.45, y, 0.99), (-2.45, y, 1.15), 0.02, 16)
    rod("Dark", (-2.45, y, 1.15), (-2.48, y, 3.4), 0.007, 8)
    sphere("Dark", 0.018, (-2.48, y, 3.41), 12)
for s in (-1, 1):
    tube("Paint3", [(-0.9, s * 1.55, 0.8), (-0.9, s * 1.55, 0.96), (-2.6, s * 1.45, 0.96), (-2.6, s * 1.45, 0.8)], 0.022, 16)
    tube("Paint3", [(0.6, s * 1.86, 0.35), (0.6, s * 1.93, 0.4), (-0.6, s * 1.96, 0.4), (-0.6, s * 1.89, 0.35)], 0.018, 16)
for (x, y) in [(1.3, 1.5), (1.3, -1.5), (-2.5, 1.35), (-2.5, -1.35)]:
    box("Paint3", (0.16, 0.05, 0.05), (x, y, 0.82), w=0.012)
    torus("Paint3", 0.07, 0.02, (x, y, 0.9), (RX, 0, 0), 32, 10)
# smoke grenade launchers
for s in (-1, 1):
    box("Paint2", (0.34, 0.46, 0.22), (1.6, s * 1.72, 0.62), w=0.02)
    bolt_rect(1.6, s * 1.72, 0.73, 0.28, 0.4, 0.14, 0.012)
    for i in range(6):
        M = xf((1.72, s * (1.56 + (i % 3) * 0.12), 0.68 + (i // 3) * 0.11), (0, RY * 0.65, s * 0.5))
        lathe("Paint3", [(0.0, -0.15), (0.055, -0.15), (0.055, 0.12), (0.06, 0.13), (0.06, 0.16), (0.045, 0.16), (0.045, 0.0), (0.0, 0.0)], M=M, n=32)
        lathe("Rubber", [(0.0, 0.0), (0.062, 0.0), (0.062, 0.02), (0.0, 0.03)], (0, 0, 0.16), M=M, n=32)
# side stowage boxes
for s in (-1, 1):
    box("Paint2", (0.95, 0.2, 0.42), (-1.6, s * 1.93, 0.45), w=0.02)
    box("Paint2", (0.97, 0.22, 0.04), (-1.6, s * 1.93, 0.67), w=0.01)
    hinge("Paint3", (-2.0, s * 1.83, 0.67), (-1.2, s * 1.83, 0.67), 0.016, 4)
    for x in (-1.9, -1.3):
        box("Paint3", (0.05, 0.03, 0.08), (x, s * 2.04, 0.6), w=0.006)
    handle("Paint3", (-1.75, s * 2.03, 0.48), (-1.45, s * 2.03, 0.48), (0, s * 0.04, 0))
# bustle rack: welded frame, mesh floor and side bars
part("Bustle rack", "turret")
z0, z1 = 0.22, 0.78
corners = [(-2.4, 1.95), (-3.45, 1.75), (-3.45, -1.75), (-2.4, -1.95)]
for z in (z0, z0 + (z1 - z0) / 3, z0 + 2 * (z1 - z0) / 3, z1):
    tube("Paint3", [(x, y, z) for x, y in corners], 0.02, 12)
def along(poly, step):
    out = []
    for a, b in zip(poly, poly[1:]):
        L = math.dist(a, b); n = max(1, round(L / step))
        out += [(a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n) for k in range(n)]
    return out + [poly[-1]]
for (x, y) in along(corners, 0.12):
    rod("Paint3", (x, y, z0), (x, y, z1), 0.009, 8)
for k in range(30):
    y = -1.75 + k * 3.5 / 29
    rod("Paint3", (-2.4, y * 1.1, z0), (-3.45, y, z0), 0.007, 6)
for k in range(9):
    x = -2.4 - k * 1.05 / 8
    f = (x + 2.4) / -1.05
    yw = 1.95 - 0.2 * f
    rod("Paint3", (x, -yw, z0 - 0.01), (x, yw, z0 - 0.01), 0.007, 6)
for y in (-1.6, -0.6, 0.6, 1.6):
    beam("Paint3", (-1.9, y, 0.3), (-2.4, y * 1.1, z0), 0.06, 0.05, 0.01)
# stowage in the rack
part("Stowage", "turret")
def jerrycan(loc, rot=(0, 0, 0)):
    M = xf(loc, rot)
    box("Ammo", (0.345, 0.165, 0.47), (0, 0, 0), w=0.025, seg=3, M=M)
    for sy in (-1, 1):
        for a in (0.93, -0.93):
            box("Ammo", (0.36, 0.012, 0.03), (0, sy * 0.083, 0), (0, a, 0), w=0.005, M=M)
        box("Ammo", (0.3, 0.01, 0.025), (0, sy * 0.084, 0.2), w=0.004, M=M)
        box("Ammo", (0.3, 0.01, 0.025), (0, sy * 0.084, -0.2), w=0.004, M=M)
    for hx in (-0.09, 0.0, 0.09):
        rod("Ammo", (hx, -0.04, 0.27), (hx, 0.04, 0.27), 0.012, 10, M=M)
        rod("Ammo", (hx, -0.04, 0.235), (hx, -0.04, 0.27), 0.01, 8, M=M)
        rod("Ammo", (hx, 0.04, 0.235), (hx, 0.04, 0.27), 0.01, 8, M=M)
    lathe("Paint3", [(0.0, 0.0), (0.032, 0.0), (0.032, 0.05), (0.028, 0.06), (0.0, 0.06)], (0.13, 0, 0.23), (0, 0.5, 0), 24, M=M)
def tarp(loc, length, r=0.15, mat="Tarp"):
    M = xf(loc, AX["y"])
    lathe(mat, [(0.0, -length / 2), (r * 0.8, -length / 2), (r * 0.95, -length / 2 + 0.02), (r, -length / 2 + 0.06),
                (r * 1.02, 0.0), (r, length / 2 - 0.06), (r * 0.95, length / 2 - 0.02), (r * 0.8, length / 2), (0.0, length / 2)], M=M, n=48)
    lathe(mat, [(0.0, 0.0), (r * 0.6, 0.0), (r * 0.3, 0.03), (0.0, 0.035)], (0, 0, length / 2), M=M, n=32)
    for z in (-length / 3, length / 3):
        torus("Strap", r * 1.03, 0.008, (0, 0, z), (0, 0, 0), 48, 6, M=M)
        box("Gunmetal", (0.04, 0.012, 0.035), (0, r * 1.05, z), M=M)
def duffel(loc, size, rot=(0, 0, 0)):
    box("Bag", size, loc, rot, w=min(size) * 0.42, seg=6)
    M = xf(loc, rot)
    for k in (-1, 1):
        box("Strap", (0.04, size[1] + 0.01, size[2] + 0.01), (k * size[0] * 0.28, 0, 0), w=0.02, seg=3, M=M)
    handle("Strap", (-0.08, 0, size[2] / 2), (0.08, 0, size[2] / 2), (0, 0, 0.04), 0.008, M=M)
def ammo_can(loc, rot=(0, 0, 0)):
    M = xf(loc, rot)
    box("Ammo", (0.28, 0.15, 0.19), (0, 0, 0), w=0.012, M=M)
    box("Ammo", (0.29, 0.16, 0.025), (0, 0, 0.1), w=0.006, M=M)
    handle("Paint3", (-0.06, 0, 0.115), (0.06, 0, 0.115), (0, 0, 0.025), 0.007, M=M)
    box("Paint3", (0.03, 0.04, 0.08), (0.145, 0, 0.05), w=0.006, M=M)
jerrycan((-2.75, 1.55, z0 + 0.24), (0, 0, 0.1))
jerrycan((-3.12, 1.48, z0 + 0.24), (0, 0, 0.05))
tarp((-3.2, 0.55, z0 + 0.16), 1.2)
tarp((-3.2, 0.55, z0 + 0.45), 1.1, 0.13, "Canvas")
duffel((-2.75, 0.55, z0 + 0.18), (0.55, 0.9, 0.34), (0, 0, 0.08))
duffel((-2.8, -0.35, z0 + 0.2), (0.6, 0.65, 0.38), (0, 0, -0.1))
duffel((-3.15, -0.45, z0 + 0.48), (0.4, 0.7, 0.3), (0.1, 0, 0.3))
for k in range(3):
    ammo_can((-2.7 - k * 0.3, -1.45, z0 + 0.1))
    ammo_can((-2.7 - k * 0.3, -1.45, z0 + 0.3))
for k in range(2):
    box("Card", (0.38, 0.27, 0.24), (-3.2, -1.05 + k * 0.02, z0 + 0.12 + k * 0.25), (0, 0, 0.05 * k), w=0.006)
    box("Strap", (0.39, 0.03, 0.245), (-3.2, -1.05 + k * 0.02, z0 + 0.12 + k * 0.25), (0, 0, 0.05 * k), w=0.003)

# ================================================================== GUN (child of the turret, pivot at the trunnions)
TRUN = (1.95, 0.0, 0.42)
part("Gun", "gun")
box("Paint", (0.56, 0.8, 0.62), (2.08, 0, 0.42), w=0.05, seg=5)
box("Paint2", (0.04, 0.7, 0.52), (2.37, 0, 0.42), w=0.012)
for y in (-0.31, 0.31):
    for z in (0.2, 0.42, 0.64):
        bolt((2.39, y, z), "x", 0.016)
for e in (-1, 1):
    weld((2.36, e * 0.4, 0.13), (2.36, e * 0.4, 0.71), 0.01)
    rod("Paint3", (1.95, e * 0.405, 0.42), (1.95, e * 0.43, 0.42), 0.09, 32)
barrel = [(0.0, 2.38), (0.235, 2.38), (0.245, 2.41), (0.245, 2.5), (0.235, 2.53), (0.205, 2.55), (0.205, 2.66), (0.17, 2.69),
          (0.152, 2.72), (0.152, 3.3), (0.165, 3.35), (0.185, 3.42), (0.198, 3.52), (0.2, 3.62), (0.2, 3.96), (0.198, 4.04),
          (0.185, 4.12), (0.165, 4.18), (0.143, 4.22), (0.143, 5.6), (0.137, 5.64), (0.133, 7.42),
          (0.14, 7.45), (0.145, 7.48), (0.145, 7.64), (0.138, 7.68), (0.07, 7.69), (0.06, 7.66), (0.06, 7.4), (0.0, 7.4)]
lathe("Paint", barrel, (0, 0, 0.42), (0, RY, 0), 96)
for x in (2.9, 4.6, 5.05, 5.5, 6.0, 6.5, 7.0):
    r0 = 0.152 if x < 3.3 else (0.143 if x < 5.6 else 0.134)
    lathe("Paint3", [(r0 - 0.005, x - 0.022), (r0 + 0.012, x - 0.022), (r0 + 0.016, x - 0.016), (r0 + 0.016, x + 0.016),
                     (r0 + 0.012, x + 0.022), (r0 - 0.005, x + 0.022)], (0, 0, 0.42), (0, RY, 0), 96, closed=True)
    box("Paint3", (0.035, 0.05, 0.035), (x, 0, 0.42 + r0 + 0.025), w=0.006)
    rod("Paint3", (x, -0.03, 0.42 + r0 + 0.03), (x, 0.03, 0.42 + r0 + 0.03), 0.007, 8)
# muzzle reference sensor: clamp band, mount and mirror box
for k in range(9):
    rz = 0.012 * math.sin(k * 1.7)
    box("Canvas", (0.07, 0.86, 0.05), (1.82 + k * 0.058, 0, 0.75 + rz), (0.02 * math.sin(k), 0.25 * math.sin(k * 2.1), 0), w=0.02, seg=3)
for e in (-1, 1):
    for k in range(6):
        box("Canvas", (0.5, 0.03, 0.1), (2.08, e * 0.418, 0.15 + k * 0.1), (0.15 * math.sin(k * 2.0), 0, 0), w=0.012, seg=2)
    rod("Paint3", (4.25, e * 0.1, 0.42 + 0.1), (7.4, e * 0.095, 0.42 + 0.096), 0.006, 8)
rod("Paint3", (4.25, 0, 0.42 + 0.143), (7.4, 0, 0.42 + 0.134), 0.006, 8)
for x in (4.8, 5.3, 6.25, 6.75):
    lathe("Strap", [(0.13, x - 0.02), (0.15, x - 0.02), (0.15, x + 0.02), (0.13, x + 0.02)], (0, 0, 0.42), (0, RY, 0), 96, closed=True)
    box("Gunmetal", (0.05, 0.04, 0.02), (x, 0.0, 0.42 - 0.15), w=0.004)
# gunner's auxiliary sight armoured flap
box("Paint2", (0.02, 0.14, 0.14), (2.45, -0.27, 0.71), (0, -0.9, 0), w=0.006)
hinge("Paint3", (2.4, -0.33, 0.69), (2.4, -0.21, 0.69), 0.012, 2)
lathe("Paint3", [(0.13, 7.44), (0.158, 7.44), (0.162, 7.46), (0.162, 7.56), (0.158, 7.58), (0.13, 7.58)], (0, 0, 0.42), (0, RY, 0), 96, closed=True)
box("Paint3", (0.12, 0.1, 0.07), (7.51, 0, 0.42 + 0.19), w=0.012)
box("Paint2", (0.2, 0.16, 0.13), (7.5, 0, 0.42 + 0.29), w=0.018)
box("Paint2", (0.22, 0.18, 0.025), (7.5, 0, 0.42 + 0.365), w=0.006)
box("Glass", (0.01, 0.1, 0.08), (7.395, 0, 0.42 + 0.29), w=0.002)
for e in (-0.06, 0.06):
    bolt((7.5 + e, 0.081, 0.42 + 0.29), "y", 0.01, 0.012)
    bolt((7.5 + e, -0.081, 0.42 + 0.29), "-y", 0.01, 0.012)
# coaxial machine gun port and gunner's auxiliary sight
lathe("Paint3", [(0.0, 0.0), (0.06, 0.0), (0.06, 0.1), (0.045, 0.12), (0.03, 0.12), (0.03, 0.06), (0.0, 0.06)], (2.39, 0.27, 0.6), (0, RY, 0), 32)
cyl("Dark", 0.028, 0.06, (2.5, 0.27, 0.6), (0, RY, 0), 16)
lathe("Paint3", [(0.0, 0.0), (0.05, 0.0), (0.05, 0.08), (0.04, 0.1), (0.0, 0.1)], (2.39, -0.27, 0.62), (0, RY, 0), 32)
cyl("Glass", 0.035, 0.01, (2.495, -0.27, 0.62), (0, RY, 0), 24)

# ================================================================== objects
def make_obj(name, origin, parent=None, loc=(0, 0, 0)):
    V, F, Ms = PARTS[name]
    me = bpy.data.meshes.new("AHD_" + name)
    used = [m for m in MAT_ORDER if m in set(Ms)]
    idx = {m: i for i, m in enumerate(used)}
    ox, oy, oz = origin
    me.from_pydata([(v.x - ox, v.y - oy, v.z - oz) for v in V], [], F)
    for m in used:
        me.materials.append(MATS[m])
    me.polygons.foreach_set("material_index", [idx[m] for m in Ms])
    me.validate()
    bm = bmesh.new(); bm.from_mesh(me)
    lim = math.radians(30)
    for e in bm.edges:
        e.smooth = len(e.link_faces) == 2 and e.calc_face_angle(0) <= lim
    for f in bm.faces:
        f.smooth = True
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    coll.objects.link(o)
    o.parent = parent
    o.location = loc
    return o, len(me.polygons)

root = bpy.data.objects.new("Abrams", None); coll.objects.link(root)
root.empty_display_size = 1.0
tur = bpy.data.objects.new("Turret", None); coll.objects.link(tur)
tur.parent = root; tur.location = (0, 0, DECK); tur.empty_display_type = "CIRCLE"; tur.empty_display_size = 1.3
gun = bpy.data.objects.new("Gun", None); coll.objects.link(gun)
gun.parent = tur; gun.location = TRUN; gun.empty_display_type = "SINGLE_ARROW"; gun.empty_display_size = 0.8
gun.rotation_euler = (0, 0, 0)
total = 0
for name, g in GROUP.items():
    if g == "hull":
        o, n = make_obj(name, (GX, 0, 0), root)
    elif g == "turret":
        o, n = make_obj(name, (0, 0, 0), tur)
    else:
        o, n = make_obj(name, TRUN, gun)
    total += n
print("Abrams HD: %d faces in %d objects" % (total, len(GROUP)))

# ------------------------------------------------------------------ lights, ground, camera, render settings
world = bpy.data.worlds.new("AHD World"); scn.world = world
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
bg.inputs[0].default_value = (*srgb("c9d3dc"), 1)
bg.inputs[1].default_value = 0.7
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("AHD Sun", "SUN"))
sun.data.energy = 4.0; sun.data.angle = math.radians(3)
sun.rotation_euler = (math.radians(42), math.radians(10), math.radians(-35))
coll.objects.link(sun)
fill = bpy.data.objects.new("Fill", bpy.data.lights.new("AHD Fill", "AREA"))
fill.data.energy = 3000; fill.data.size = 8
fill.location = (-8, 9, 7); fill.rotation_euler = (math.radians(55), 0, math.radians(-140))
coll.objects.link(fill)
gme = bpy.data.meshes.new("AHD_Ground")
gme.from_pydata([(-40, -40, 0), (40, -40, 0), (40, 40, 0), (-40, 40, 0)], [], [(0, 1, 2, 3)])
gme.materials.append(material("Ground", "8c7d62", 0.95, grime=0.35, scale=0.6))
ground = bpy.data.objects.new("Ground", gme); coll.objects.link(ground)
cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("AHD Camera")); coll.objects.link(cam)
cam.data.lens = 50
scn.camera = cam
def aim(eye, target=(0.3, 0, 1.3)):
    cam.location = eye
    cam.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()
aim((12.5, -10.5, 5.2))
scn.render.engine = "CYCLES"
scn.cycles.samples = 128
scn.render.resolution_x, scn.render.resolution_y = 1920, 1080
try:
    scn.view_settings.view_transform = "AgX"
except TypeError:
    scn.view_settings.view_transform = "Filmic"

# ------------------------------------------------------------------ headless: previews and a .blend
if HEADLESS:
    argv = sys.argv
    out = argv[argv.index("--") + 1] if "--" in argv and len(argv) > argv.index("--") + 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "showpiece")
    os.makedirs(out, exist_ok=True)
    scn.cycles.device = "CPU"
    scn.cycles.samples = int(os.environ.get("AHD_SAMPLES", "24"))
    scn.render.resolution_x, scn.render.resolution_y = 1200, 800
    tur.rotation_euler = (0, 0, math.radians(18))
    gun.rotation_euler = (0, math.radians(-4), 0)
    for name, eye, tgt in [("showpiece_front34", (12.5, -10.5, 5.2), (0.3, 0, 1.3)),
                           ("showpiece_rear34", (-11.5, 9.5, 6.0), (-0.5, 0, 1.2)),
                           ("showpiece_front", (8.5, -3.2, 2.4), (2.5, 0, 1.2)),
                           ("showpiece_wheels", (4.0, -6.2, 1.2), (1.2, -1.6, 0.7)),
                           ("showpiece_turret", (2.0, -4.2, 4.4), (-0.8, -0.3, 2.4))]:
        aim(eye, tgt)
        scn.render.filepath = os.path.join(out, name + ".png")
        bpy.ops.render.render(write_still=True, scene=scn.name)
    tur.rotation_euler = (0, 0, 0); gun.rotation_euler = (0, 0, 0)
    aim((12.5, -10.5, 5.2))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "abrams_showpiece.blend"))
    print("saved to", out)
