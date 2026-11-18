"""Make a game-ready copy of the Desert Sentinel and export it for the Tank mod.
Run in Blender:  exec(open(r"F:\zed-zone-mods\tank-blender\export_game.py",encoding="utf-8").read())
Work happens on timers (long steps), progress in scene["game_export"]."""
import bpy, bmesh, math, os, time
from mathutils import Matrix, Vector

OUT = r"F:\zed-zone-mods\tank-blender\game"
PIVOT = Vector((-0.9, 0.0, 1.86))      # turret ring centre (world); hull origin = ground under it
RATIO = {"Sentinel Track L": 0.12, "Sentinel Track R": 0.12, "Sentinel Running gear L": 0.15, "Sentinel Running gear R": 0.15,
         "ERA Tiles": 0.5, "Main Gun": 0.45, "Smoke Launchers": 0.4, "Turret Rivets": 0.5}
DEFAULT_RATIO = 0.75
TEX = 2048
sc = bpy.context.scene

def status(s):
    sc["game_export"] = s
    print("game export:", s)

def visible_meshes():
    tur = bpy.data.objects["Turret"]
    tset = {o.name for o in tur.children_recursive}
    hull, turret = [], []
    for o in sc.objects:
        if o.type != 'MESH' or o.hide_render or not o.visible_get(): continue
        if o.name in ("Ground", "Turret") or o.name.startswith("GAME_"): continue
        if any(c.name == "Studio" for c in o.users_collection): continue
        (turret if o.name in tset else hull).append(o)
    return hull, turret

def make_copy(o, coll):
    r = RATIO.get(o.name, DEFAULT_RATIO)
    mod = None
    if r < 0.999 and len(o.data.polygons) > 200:
        mod = o.modifiers.new("GAME_dec", 'DECIMATE'); mod.ratio = r; mod.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    if mod: o.modifiers.remove(mod)
    me.transform(o.matrix_world)
    c = bpy.data.objects.new("GAME_part_" + o.name, me)
    coll.objects.link(c)
    return c

def join(parts, name, offset):
    keep = parts[0]
    with bpy.context.temp_override(active_object=keep, selected_editable_objects=parts, object=keep):
        bpy.ops.object.join()
    keep.name = name; keep.data.name = name
    keep.data.transform(Matrix.Translation(-offset))
    return keep

def phase_a():
    t = time.time()
    coll = bpy.data.collections.get("Game Export")
    if coll:
        for o in list(coll.objects): bpy.data.objects.remove(o, do_unlink=True)
    else:
        coll = bpy.data.collections.new("Game Export"); sc.collection.children.link(coll)
    lc = bpy.context.view_layer.layer_collection.children.get("Game Export")
    if lc: lc.exclude = False
    hull, turret = visible_meshes()
    hp = [make_copy(o, coll) for o in hull]
    tp = [make_copy(o, coll) for o in turret]
    h = join(hp, "GAME_Hull", Vector((PIVOT.x, PIVOT.y, 0.0)))
    tr = join(tp, "GAME_Turret", PIVOT)
    tri = lambda o: sum(len(p.vertices) - 2 for p in o.data.polygons)
    sc["game_tris"] = f"hull {tri(h)}, turret {tri(tr)}"
    status(f"A done {time.time()-t:.0f}s: {sc['game_tris']}")
    bpy.app.timers.register(lambda: (phase_b(), None)[1], first_interval=0.2)

def textured(m):
    """materials whose look comes from a texture or a procedural pattern get baked; plain colours stay plain"""
    return m is not None and m.use_nodes and any(n.bl_idname.startswith("ShaderNodeTex") for n in m.node_tree.nodes)

def bake_one(o, img):
    me = o.data
    tex_idx = {i for i, sl in enumerate(o.material_slots) if textured(sl.material)}
    old = me.uv_layers[0].name if len(me.uv_layers) else None
    uv = me.uv_layers.new(name="GameUV")
    me.uv_layers.active = uv
    if old: me.uv_layers[old].active_render = True
    for p in me.polygons: p.select = p.material_index in tex_idx
    for ob in bpy.context.view_layer.objects: ob.select_set(False)
    o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0, area_weight=0.0)
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(udim_source='CLOSEST_UDIM', rotate=False, scale=True, margin_method='FRACTION', margin=0.0015, shape_method='AABB')
    bpy.ops.object.mode_set(mode='OBJECT')
    added = []
    for i in tex_idx:
        m = o.material_slots[i].material
        if any(mm is m for mm, _ in added): continue
        n = m.node_tree.nodes.new("ShaderNodeTexImage"); n.image = img; n.name = "GAME_bake"
        m.node_tree.nodes.active = n; added.append((m, n))
    # plain materials get a throwaway target node too so the bake doesn't complain
    for i, sl in enumerate(o.material_slots):
        m = sl.material
        if i in tex_idx or m is None or any(mm is m for mm, _ in added): continue
        if not m.use_nodes: m.use_nodes = True
        n = m.node_tree.nodes.new("ShaderNodeTexImage"); n.image = bpy.data.images.get("GAME_scratch") or bpy.data.images.new("GAME_scratch", 8, 8)
        n.name = "GAME_bake"; m.node_tree.nodes.active = n; added.append((m, n))
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, use_selected_to_active=False, margin=4)
    for m, n in added: m.node_tree.nodes.remove(n)
    pm = bpy.data.materials.get(o.name + "_Paint") or bpy.data.materials.new(o.name + "_Paint")
    pm.use_nodes = True
    nt = pm.node_tree
    tex = nt.nodes.get("Paint") or nt.nodes.new("ShaderNodeTexImage"); tex.name = "Paint"; tex.image = img
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    for i in tex_idx: o.material_slots[i].material = pm
    for l in [l for l in me.uv_layers if l.name != "GameUV"]: me.uv_layers.remove(l)
    sc["game_tex_mats"] = ",".join(sorted({o.material_slots[i].name for i in tex_idx}))

def phase_b():
    t = time.time()
    os.makedirs(OUT, exist_ok=True)
    eng, smp = sc.render.engine, sc.cycles.samples
    sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
    for name, file in (("GAME_Hull", "tank_hull"), ("GAME_Turret", "tank_turret")):
        o = bpy.data.objects[name]
        img = bpy.data.images.get(file + "_paint") or bpy.data.images.new(file + "_paint", TEX, TEX)
        bake_one(o, img)
        img.filepath_raw = os.path.join(OUT, file + ".png"); img.file_format = 'PNG'; img.save()
    sc.render.engine = eng; sc.cycles.samples = smp
    status(f"B baked {time.time()-t:.0f}s")
    bpy.app.timers.register(lambda: (phase_c(), None)[1], first_interval=0.2)

def phase_c():
    for name, file in (("GAME_Hull", "tank_hull"), ("GAME_Turret", "tank_turret")):
        o = bpy.data.objects[name]
        for ob in bpy.context.view_layer.objects: ob.select_set(False)
        o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.wm.obj_export(filepath=os.path.join(OUT, file + ".obj"), export_selected_objects=True,
                              export_uv=True, export_normals=True, export_materials=True,
                              export_triangulated_mesh=True, forward_axis='X', up_axis='Z', path_mode='STRIP')
    lc = bpy.context.view_layer.layer_collection.children.get("Game Export")
    if lc: lc.exclude = True
    bpy.ops.wm.save_mainfile()
    status("done " + sc.get("game_tris", ""))

status("queued")
bpy.app.timers.register(lambda: (phase_a(), None)[1], first_interval=0.2)
