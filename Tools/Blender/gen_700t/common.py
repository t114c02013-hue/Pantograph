"""共用工具：清場、材質（最多 7 個）、色盤 UV、用 bmesh 建方塊／圓柱／斷面擠出、父子關係。
軸向：車長沿 +Y、車寬沿 X、上方 Z（匯出時轉成 Unity 的 Z 前、Y 上）。單位 m。"""
import bpy
import bmesh
from mathutils import Vector

MATERIALS = ["Paint", "Roof", "Walkway", "Glass", "Dark", "Metal", "Insulator"]


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.objects):
        for b in list(block):
            block.remove(b)


def materials(palette_image):
    mats = {}
    for n in MATERIALS:
        m = bpy.data.materials.new(n)
        m.use_nodes = True
        tex = m.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = palette_image
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        if bsdf is not None:
            m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        mats[n] = m
    return mats


class Ctx:
    """建模時的共用狀態：材質、色盤（色名 → UV）。"""
    def __init__(self, mats, palette):
        self.mats = mats
        self.palette = palette


def _finish(ctx, name, bm, mat, color, parent=None, loc=(0, 0, 0)):
    uv = bm.loops.layers.uv.verify()
    u, v = ctx.palette.uv(color)
    for f in bm.faces:
        for l in f.loops:
            l[uv].uv = (u, v)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(ctx.mats[mat])
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = Vector(loc)
    if parent is not None:
        ob.parent = parent
    return ob


def box(ctx, name, size, loc=(0, 0, 0), mat="Metal", color="metal", parent=None):
    """size＝(x, y, z) 全長；loc＝中心（相對父物件）。"""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    return _finish(ctx, name, bm, mat, color, parent, loc)


def cylinder(ctx, name, radius, depth, loc=(0, 0, 0), axis="Z", segments=12, mat="Metal", color="metal", parent=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius, radius2=radius, depth=depth)
    if axis == "X":
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=__import__("mathutils").Matrix.Rotation(1.5707963, 3, "Y"))
    elif axis == "Y":
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=__import__("mathutils").Matrix.Rotation(1.5707963, 3, "X"))
    return _finish(ctx, name, bm, mat, color, parent, loc)


def empty_mesh(ctx, name, loc=(0, 0, 0), parent=None, size=0.01):
    """節點用的極小方塊（匯出只帶 MESH，節點也要是網格才會保留名稱與階層）。"""
    return box(ctx, name, (size, size, size), loc, "Dark", "dark", parent)


def extrude_profile(ctx, name, profile, length, mat="Paint", color="white", roof_mat=None, roof_color=None, parent=None, loc=(0, 0, 0)):
    """斷面 profile＝[(x, z), ...]（凸多邊形、逆時針），沿 Y 擠出 length（以原點為中心）。
    roof_mat：朝上的面（法線 z > 0.5）改用這個材質（第二個材質槽）。"""
    bm = bmesh.new()
    y0, y1 = -length / 2.0, length / 2.0
    a = [bm.verts.new((x, y0, z)) for x, z in profile]
    b = [bm.verts.new((x, y1, z)) for x, z in profile]
    n = len(profile)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(list(reversed(a)))
    bm.faces.new(b)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    uv = bm.loops.layers.uv.verify()
    cu, cv = ctx.palette.uv(color)
    ru, rv = ctx.palette.uv(roof_color or color)
    for f in bm.faces:
        up = roof_mat is not None and f.normal.z > 0.5
        f.material_index = 1 if up else 0
        for l in f.loops:
            l[uv].uv = (ru, rv) if up else (cu, cv)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(ctx.mats[mat])
    if roof_mat is not None:
        me.materials.append(ctx.mats[roof_mat])
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = Vector(loc)
    if parent is not None:
        ob.parent = parent
    return ob


def triangles(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons) if ob.type == "MESH" else 0


def world_bounds(objs):
    import math
    lo = [math.inf] * 3
    hi = [-math.inf] * 3
    for ob in objs:
        for c in ob.bound_box:
            w = ob.matrix_world @ Vector(c)
            for k in range(3):
                lo[k] = min(lo[k], w[k]); hi[k] = max(hi[k], w[k])
    return lo, hi


def add_box(bm, size, center=(0, 0, 0), bevel=0.0):
    """在既有 bmesh 裡加一個方塊（size 全長、center 中心，直接寫進頂點，不動物件原點）；bevel＞0 時倒角（1 段）。"""
    r = bmesh.ops.create_cube(bm, size=1.0)
    vs = r["verts"]
    bmesh.ops.scale(bm, vec=Vector(size), verts=vs)
    bmesh.ops.translate(bm, vec=Vector(center), verts=vs)
    if bevel > 0:
        edges = list({e for v in vs for e in v.link_edges})
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=1, affect="EDGES")
    return vs


def add_cylinder(bm, radius, depth, center=(0, 0, 0), axis="Z", segments=12):
    """在既有 bmesh 裡加一個圓柱（軸 axis，中心 center）。"""
    from mathutils import Matrix
    r = bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius, radius2=radius, depth=depth)
    vs = r["verts"]
    if axis == "X":
        bmesh.ops.rotate(bm, verts=vs, cent=(0, 0, 0), matrix=Matrix.Rotation(1.5707963, 3, "Y"))
    elif axis == "Y":
        bmesh.ops.rotate(bm, verts=vs, cent=(0, 0, 0), matrix=Matrix.Rotation(1.5707963, 3, "X"))
    bmesh.ops.translate(bm, vec=Vector(center), verts=vs)
    return vs


def mesh_object(ctx, name, bm, mat, color, parent=None, loc=(0, 0, 0)):
    """把組好的 bmesh 變成物件（原點＝物件位置 loc，頂點已含偏移）。"""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _finish(ctx, name, bm, mat, color, parent, loc)
