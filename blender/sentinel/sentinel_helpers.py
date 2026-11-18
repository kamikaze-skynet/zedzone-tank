"""Helpers for refining the Desert Sentinel: crisp parts built with bmesh, camo paint matched to the AI texture."""
import math
import bpy, bmesh
from mathutils import Vector, Matrix, Euler

TAU = 2 * math.pi
RY = RX = math.pi / 2

def srgb(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)

def camo_material(name="Sentinel Camo", olive=None, tan=None, dark=None):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    n = nt.nodes.new("ShaderNodeTexNoise"); n.inputs["Scale"].default_value = 0.55; n.inputs["Detail"].default_value = 3.0
    n.inputs["Roughness"].default_value = 0.45
    nt.links.new(tc.outputs["Object"], n.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB"); ramp.color_ramp.interpolation = "CONSTANT"
    e = ramp.color_ramp.elements
    e[0].position = 0.0; e[0].color = (*(olive or srgb("5f6438")), 1)
    e[1].position = 0.56; e[1].color = (*(tan or srgb("a9794a")), 1)
    e3 = e.new(0.42); e3.color = (*(dark or srgb("4b5232")), 1)
    nt.links.new(n.outputs["Fac"], ramp.inputs["Fac"])
    # dust and wear speckle
    n2 = nt.nodes.new("ShaderNodeTexNoise"); n2.inputs["Scale"].default_value = 40.0
    nt.links.new(tc.outputs["Object"], n2.inputs["Vector"])
    mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = "RGBA"; mix.blend_type = "MULTIPLY"
    mix.inputs[0].default_value = 0.2
    nt.links.new(ramp.outputs["Color"], mix.inputs[6]); nt.links.new(n2.outputs["Color"], mix.inputs[7])
    nt.links.new(mix.outputs[2], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = 0.72
    bump = nt.nodes.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.1; bump.inputs["Distance"].default_value = 0.002
    nt.links.new(n2.outputs["Fac"], bump.inputs["Height"]); nt.links.new(bump.outputs["Normal"], b.inputs["Normal"])
    return m

def plain_material(name, hexc, rough=0.5, metal=0.0):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*srgb(hexc), 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m

class Builder:
    """collects geometry for one object, one material slot per name"""
    def __init__(self, name, mats):
        self.name = name; self.mats = mats; self.v = []; self.f = []; self.mi = []

    def add(self, bm, mat, M):
        bm.verts.index_update()
        off = len(self.v)
        self.v.extend(M @ x.co for x in bm.verts)
        self.f.extend(tuple(off + x.index for x in fa.verts) for fa in bm.faces)
        self.mi.extend([list(self.mats).index(mat)] * len(bm.faces))
        bm.free()

    def build(self, coll, parent=None, origin=Vector((0, 0, 0))):
        old = bpy.data.objects.get(self.name)
        if old:
            bpy.data.objects.remove(old, do_unlink=True)
        me = bpy.data.meshes.new(self.name)
        me.from_pydata([tuple(p - origin) for p in self.v], [], self.f)
        for k in self.mats:
            me.materials.append(self.mats[k])
        me.polygons.foreach_set("material_index", self.mi)
        me.validate()
        bm = bmesh.new(); bm.from_mesh(me)
        lim = math.radians(30)
        for e in bm.edges:
            e.smooth = len(e.link_faces) == 2 and e.calc_face_angle(0) <= lim
        for fa in bm.faces:
            fa.smooth = True
        bm.to_mesh(me); bm.free()
        o = bpy.data.objects.new(self.name, me)
        coll.objects.link(o)
        if parent is not None:
            o.parent = parent
            o.matrix_parent_inverse = parent.matrix_world.inverted()
        o.location = origin
        return o

def bevel_bm(bm, w, seg=2, angle=30):
    if w <= 0:
        return
    es = [e for e in bm.edges if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(angle)]
    if es:
        bmesh.ops.bevel(bm, geom=es, offset=w, offset_type="OFFSET", segments=seg, profile=0.5, affect="EDGES", clamp_overlap=True)

def bm_from(v, f):
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in v]
    for fa in f:
        try: bm.faces.new([vs[i] for i in fa])
        except ValueError: pass
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    return bm

def lathe_bm(prof, n=48, closed=False):
    v, f, rings = [], [], []
    for (r, z) in prof:
        if r < 1e-6:
            rings.append((len(v),)); v.append(Vector((0, 0, z)))
        else:
            rings.append(tuple(range(len(v), len(v) + n)))
            v.extend(Vector((math.cos(TAU * k / n) * r, math.sin(TAU * k / n) * r, z)) for k in range(n))
    pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
    for A, B in pairs:
        if len(A) == 1 and len(B) == 1: continue
        for k in range(n):
            k1 = (k + 1) % n
            if len(A) == 1: f.append((A[0], B[k1], B[k]))
            elif len(B) == 1: f.append((A[k], A[k1], B[0]))
            else: f.append((A[k], A[k1], B[k1], B[k]))
    if not closed:
        if len(rings[0]) > 1: f.append(tuple(reversed(rings[0])))
        if len(rings[-1]) > 1: f.append(rings[-1])
    return bm_from(v, f)

def box_bm(sx, sy, sz, w=0.0, seg=2):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(sx, sy, sz), verts=bm.verts)
    bevel_bm(bm, min(w, 0.49 * min(sx, sy, sz)), seg)
    return bm

def cyl_bm(r, d, n=32, r2=None, w=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=n, radius1=r, radius2=r if r2 is None else r2, depth=d)
    bevel_bm(bm, min(w, 0.45 * d, 0.45 * r), 2)
    return bm

def xf(loc=(0, 0, 0), rot=(0, 0, 0)):
    return Matrix.Translation(Vector(loc)) @ Euler(rot, "XYZ").to_matrix().to_4x4()

def axis_xf(origin, direction):
    """matrix whose local Z runs along direction from origin"""
    d = Vector(direction).normalized()
    return Matrix.Translation(Vector(origin)) @ d.to_track_quat("Z", "Y").to_matrix().to_4x4()

def rod(B, mat, a, b, r, n=16, M=Matrix.Identity(4)):
    a, b = Vector(a), Vector(b); d = b - a
    B.add(cyl_bm(r, d.length, n), mat, M @ axis_xf((a + b) / 2, d))

def remove_faces(obj, test):
    """delete faces of obj whose every vertex passes test(co_array)->bool array"""
    import numpy as np
    me = obj.data
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    inside = test(co)
    bm = bmesh.new(); bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    kill = [f for f in bm.faces if all(inside[v.index] for v in f.verts)]
    n = len(kill)
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bm.to_mesh(me); bm.free(); me.update()
    return n
