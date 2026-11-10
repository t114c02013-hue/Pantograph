"""
水電工維修腰包＋腰包內工具（資深建模師 B，v1.0，2026-11-10）

依據：tool-pouch/references.md（負責人 2026-11-10 裁定：0.30 × 0.26 × 0.10 m、Pocket_05…Pocket_13、深褐皮革、
外型照第 4 節、handleLengthM 0.39／0.26／0.19）。不顯示文字，靠外型分辨。

執行（Blender 4.x／5.x，或 pip 的 bpy 模組）：
  blender -b --factory-startup -P gen_tool_pouch.py -- --out <輸出資料夾> [--render]
輸出：ENV_ToolPouch.fbx、TL_*.fbx（7 件工具）、ToolPouchReport.json、--render 時另出 preview_*.png

座標約定（和 controller-model/gen_controller_fbx.py 相同的匯出設定，已在控制器上驗證）：
  腳本裡所有尺寸都用 Unity 座標寫（+X 右、+Y 上、+Z 前），由 U() 換成 Blender 座標 (x,y,z)→(-x,-z,y)。
  FBX 匯出 axis_forward=-Z、axis_up=Y，Unity 匯入後就回到腳本裡寫的 Unity 座標。
工具原點 = Attach_Grip（握把中心，+Z 朝工具前端，+Y 朝上，T2-collider-spec）。
腰包原點 = 背面中心，+Y 朝上（袋口），+Z 朝外（離開身體）。
"""
import bpy, bmesh, sys, os, json, math, argparse
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--out", required=True)
ap.add_argument("--render", action="store_true")
ap.add_argument("--samples", type=int, default=48)
ap.add_argument("--style", choices=["canvas", "leather"], default="canvas")   # terry 2026-11-10 選黑色帆布（CLC 1505 型）
args = ap.parse_args(argv)
os.makedirs(args.out, exist_ok=True)

# ---------------------------------------------------------------- 參數
P = json.loads("""{
  "pouch": {"w": 0.30, "h": 0.26, "d": 0.10, "backTierTopY": 0.10, "frontTierTopY": 0.00, "tierSplitZ": 0.052},
  "torque_l": {"len": 0.47, "hl": 0.39, "grip": 0.12, "head": 0.040, "headT": 0.024, "tube": 0.026, "drive": 0.0127, "gripColor": "red"},
  "torque_s": {"len": 0.33, "hl": 0.26, "grip": 0.09, "head": 0.032, "headT": 0.018, "tube": 0.020, "drive": 0.0095, "gripColor": "blue"},
  "ratchet":  {"len": 0.26, "hl": 0.19, "grip": 0.088, "head": 0.038, "headT": 0.020, "handle": [0.022, 0.012], "drive": 0.0127}
}""")
# 顏色是線性值（Principled Base Color），不是 sRGB
COL = {
    "leather":  ((0.075, 0.034, 0.014), 0.65, 0.0),
    "leather2": ((0.040, 0.018, 0.008), 0.70, 0.0),
    "canvas":   ((0.012, 0.012, 0.013), 0.92, 0.0),
    "canvas2":  ((0.007, 0.007, 0.008), 0.95, 0.0),
    "binding":  ((0.20, 0.085, 0.025), 0.55, 0.0),
    "caseGray": ((0.42, 0.44, 0.47), 0.45, 0.0),
    "stitch":   ((0.55, 0.40, 0.20), 0.80, 0.0),
    "rivet":    ((0.72, 0.60, 0.35), 0.35, 1.0),
    "steel":    ((0.78, 0.79, 0.80), 0.25, 1.0),
    "steelDark":((0.35, 0.36, 0.38), 0.35, 1.0),
    "black":    ((0.03, 0.03, 0.03), 0.60, 0.0),
    "rubber":   ((0.012, 0.012, 0.012), 0.85, 0.0),
    "red":      ((0.70, 0.04, 0.03), 0.50, 0.0),
    "blue":     ((0.03, 0.18, 0.62), 0.50, 0.0),
    "lcd":      ((0.45, 0.50, 0.42), 0.20, 0.0),
    "label":    ((0.92, 0.92, 0.85), 0.60, 0.0),
    "yellow":   ((0.95, 0.75, 0.02), 0.45, 0.0),
    "white":    ((0.92, 0.92, 0.92), 0.50, 0.0),
    "cloth":    ((0.10, 0.25, 0.62), 0.95, 0.0),
    "clothB":   ((0.92, 0.93, 0.95), 0.95, 0.0),
    "strap":    ((0.95, 0.47, 0.05), 0.80, 0.0),
    "clear":    ((0.80, 0.85, 0.90), 0.05, 0.0),
    "mesh":     ((0.08, 0.08, 0.085), 0.90, 0.0),
}

# 腰包材質：帆布款＝黑色帆布＋棕色尼龍包邊；皮革款＝深褐皮革
BODY, PANEL, BIND = (("canvas", "canvas2", "binding") if args.style == "canvas" else ("leather", "leather2", "leather2"))

def U(x, y, z):
    return Vector((-x, -z, y))

# Unity 軸名 → Blender 軸名
AX = {"x": "X", "y": "Z", "z": "Y"}

# ---------------------------------------------------------------- 基本工具
def mat(name):
    m = bpy.data.materials.get(name)
    if m: return m
    c, r, mt = COL[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*c, 1)
    b.inputs["Roughness"].default_value = r
    b.inputs["Metallic"].default_value = mt
    if name == "clear":
        b.inputs["Alpha"].default_value = 0.25
        try: m.surface_render_method = "BLENDED"
        except Exception: pass
    m.diffuse_color = (*c, 1)
    return m

def _obj(name, bm, material, parent):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = False
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(mat(material))
    if parent:
        o.parent = parent
        o.matrix_parent_inverse = parent.matrix_world.inverted()   # 網格頂點已是世界座標
    return o

def box(name, c, s, material, parent=None, bevel=0.0, seg=2):
    """c、s 是 Unity 座標的中心與尺寸"""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector((s[0], s[2], s[1])), verts=bm.verts)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(s) * 0.45), segments=seg, affect="EDGES", profile=0.5)
    bmesh.ops.translate(bm, vec=U(*c), verts=bm.verts)
    return _obj(name, bm, material, parent)

def cyl(name, c, r, length, axis, material, parent=None, seg=24, r2=None):
    """沿 Unity 軸 axis（'x'/'y'/'z'）的圓柱，r2 給了就是錐台"""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=length)
    rot = {"X": Matrix.Rotation(math.pi / 2, 3, "Y"), "Y": Matrix.Rotation(-math.pi / 2, 3, "X"), "Z": Matrix.Identity(3)}[AX[axis]]
    # Unity +z → Blender −Y：錐台要讓 radius2 那端朝 Unity 正向
    if axis == "z": rot = Matrix.Rotation(math.pi / 2, 3, "X")
    if axis == "x": rot = Matrix.Rotation(-math.pi / 2, 3, "Y")
    bmesh.ops.rotate(bm, verts=bm.verts, matrix=rot)
    bmesh.ops.translate(bm, vec=U(*c), verts=bm.verts)
    return _obj(name, bm, material, parent)

def frame(fwd, up):
    """Unity 方向 → Blender 3×3 旋轉：物件局部 Unity +Z 對 fwd、+Y 對 up"""
    yl = -U(*fwd).normalized(); zl = U(*up).normalized(); xl = yl.cross(zl)
    return Matrix((xl, yl, zl)).transposed()

def empty(name, pos, parent=None, fwd=(0, 0, 1), up=(0, 1, 0), size=0.01):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "ARROWS"; e.empty_display_size = size
    bpy.context.scene.collection.objects.link(e)
    if parent: e.parent = parent
    e.matrix_world = Matrix.Translation(U(*pos)) @ frame(fwd, up).to_4x4()
    return e

def root(name):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "PLAIN_AXES"; e.empty_display_size = 0.02
    bpy.context.scene.collection.objects.link(e)
    return e

def hierarchy(o):
    out = [o]
    for c in o.children: out += hierarchy(c)
    return out

# ---------------------------------------------------------------- 扭力扳手（大、小）
def torque_wrench(name, p):
    r = root(name)
    vis = empty("Visual", (0, 0, 0), r)
    hl, head, ht, tube, grip = p["hl"], p["head"], p["headT"], p["tube"], p["grip"]
    tail = p["len"] - hl - head / 2              # 握把中心到尾端
    grip = min(grip, 2 * (tail - 0.012))          # 尾端留 1.2 cm 給鎖定旋鈕，尾端才是封起來的
    # 頭部：圓形棘輪頭（軸 = Unity Y），中心在 Z = hl
    cyl("Head", (0, 0, hl), head / 2, ht, "y", "steel", vis, seg=32)
    cyl("Head_Cap", (0, ht / 2 + 0.0008, hl), head / 2 * 0.72, 0.0016, "y", "steelDark", vis, seg=32)
    box("Head_Neck", (0, 0, hl - head / 2 - 0.012), (head * 0.55, ht * 0.8, 0.03), "steel", vis, bevel=0.003)
    # 方頭朝 −Y
    dr = p["drive"]
    box("Drive", (0, -ht / 2 - dr * 0.65, hl), (dr, dr * 1.3, dr), "steelDark", vis, bevel=0.0008)
    box("Ratchet_Lever", (0, ht / 2 + 0.003, hl - head * 0.15), (head * 0.45, 0.004, 0.008), "steelDark", vis, bevel=0.0015)
    # 管身
    z0, z1 = grip / 2 + 0.006, hl - head / 2 - 0.024
    cyl("Tube", (0, 0, (z0 + z1) / 2), tube / 2, z1 - z0, "z", "steel", vis)
    # 刻度窗（管身靠握把那端的上面，深色長窗）
    box("Scale_Window", (0, tube / 2 - 0.0005, z0 + 0.03), (tube * 0.42, 0.0025, 0.045), "black", vis, bevel=0.001)
    box("Scale_Ticks", (0, tube / 2 + 0.0004, z0 + 0.03), (tube * 0.22, 0.0008, 0.040), "white", vis)
    # 校正標籤（管身中段，白色小貼紙）
    box("Cal_Label", (0, tube / 2 + 0.0002, (z0 + z1) / 2 + 0.03), (tube * 0.7, 0.0012, 0.025), "label", vis)
    # 握把（千分尺套筒），彩色＋防滑環
    gc = p["gripColor"]
    cyl("Grip", (0, 0, 0), tube / 2 + 0.004, grip, "z", gc, vis, seg=28)
    n = 7
    for i in range(n):
        z = -grip / 2 + grip * (i + 0.5) / n
        cyl(f"Grip_Ring_{i}", (0, 0, z), tube / 2 + 0.0052, 0.004, "z", gc, vis, seg=28)
    cyl("Grip_Collar", (0, 0, grip / 2 + 0.003), tube / 2 + 0.002, 0.006, "z", "steelDark", vis, seg=28)
    # 尾端鎖定旋鈕
    kz = -grip / 2 - (tail - grip / 2) / 2
    kl = tail - grip / 2 - 0.004
    cyl("Lock_Knob", (0, 0, -grip / 2 - kl / 2), tube / 2 + 0.0035, kl, "z", "steelDark", vis, seg=12)
    cyl("End_Cap", (0, 0, -tail + 0.002), tube / 2 + 0.0025, 0.004, "z", "steel", vis, seg=24)
    cyl("End_Cap_Dome", (0, 0, -tail - 0.0005), tube / 2 * 0.6, 0.001, "z", "steelDark", vis, seg=24)
    # 節點
    empty("Socket_Mount", (0, -ht / 2, hl), r, fwd=(0, 0, 1), up=(0, 1, 0))
    empty("Tip", (0, -ht / 2 - dr * 1.3, hl), r, fwd=(0, -1, 0), up=(0, 0, 1))
    empty("Poke_Ratchet", (0, ht / 2 + 0.005, hl - head * 0.15), r)
    empty("FX_Head", (0, 0, hl), r)
    empty("FX_Scale", (0, tube / 2 + 0.001, z0 + 0.03), r)
    empty("FX_CalLabel", (0, tube / 2 + 0.001, (z0 + z1) / 2 + 0.03), r)
    empty("Poke_Plus", (0, tube / 2 + 0.006, grip * 0.25), r)
    empty("Poke_Minus", (0, tube / 2 + 0.006, -grip * 0.25), r)
    return r

# ---------------------------------------------------------------- 棘輪套筒扳手
def ratchet(name, p):
    r = root(name)
    vis = empty("Visual", (0, 0, 0), r)
    hl, head, ht, grip = p["hl"], p["head"], p["headT"], p["grip"]
    hw, hth = p["handle"]
    cyl("Head", (0, 0, hl), head / 2, ht, "y", "steel", vis, seg=32)
    cyl("Head_Cap", (0, ht / 2 + 0.0008, hl), head / 2 * 0.6, 0.0016, "y", "steelDark", vis, seg=32)
    # 淚滴形：頭部往握把收窄
    cyl("Head_Taper", (0, 0, hl - head / 2 - 0.02), hw * 0.55, 0.05, "z", "steel", vis, seg=16, r2=head * 0.42)
    box("Neck", (0, 0, hl - head / 2 - 0.065), (hw * 0.9, hth * 0.85, 0.05), "steel", vis, bevel=0.003)
    box("Drive", (0, -ht / 2 - p["drive"] * 0.65, hl), (p["drive"], p["drive"] * 1.3, p["drive"]), "steelDark", vis, bevel=0.0008)
    box("Ratchet_Lever", (0, ht / 2 + 0.003, hl - head * 0.2), (head * 0.5, 0.004, 0.009), "steelDark", vis, bevel=0.0015)
    # 扁平黑色橡膠握把
    box("Grip", (0, 0, 0), (hw, hth, grip), "rubber", vis, bevel=0.005, seg=3)
    box("Grip_End", (0, 0, -grip / 2 - 0.006), (hw * 1.05, hth * 1.05, 0.012), "rubber", vis, bevel=0.005, seg=3)
    box("Grip_Inlay", (0, hth / 2, 0.005), (hw * 0.4, 0.001, grip * 0.6), "steelDark", vis, bevel=0.0005)
    empty("Socket_Mount", (0, -ht / 2, hl), r)
    empty("Tip", (0, -ht / 2 - p["drive"] * 1.3, hl), r, fwd=(0, -1, 0), up=(0, 0, 1))
    empty("Poke_Ratchet", (0, ht / 2 + 0.005, hl - head * 0.2), r)
    empty("FX_Head", (0, 0, hl), r)
    return r

# ---------------------------------------------------------------- 數位游標卡尺（尺身沿 +X，量爪朝 +Z，螢幕朝 +Y）
def caliper(name):
    r = root(name)
    vis = empty("Visual", (0, 0, 0), r)
    t = 0.0035
    box("Beam", (0.085, 0, 0), (0.23, t, 0.016), "steel", vis, bevel=0.0006)
    box("Beam_Scale", (0.11, t / 2 + 0.0002, 0.004), (0.15, 0.0004, 0.004), "steelDark", vis)
    box("Jaw_Fixed", (-0.022, 0, 0.026), (0.010, t, 0.040), "steel", vis, bevel=0.0006)
    box("Jaw_Fixed_In", (-0.022, 0, -0.016), (0.006, t, 0.016), "steel", vis, bevel=0.0005)
    fx = empty("FX_Jaw", (-0.012, 0, 0), r)
    box("Jaw_Slide", (-0.012, 0, 0.026), (0.010, t, 0.040), "steel", fx, bevel=0.0006)
    box("Jaw_Slide_In", (-0.012, 0, -0.016), (0.006, t, 0.016), "steel", fx, bevel=0.0005)
    box("Slider_Body", (0.022, 0.003, -0.002), (0.07, 0.010, 0.030), "black", fx, bevel=0.002, seg=2)
    box("LCD", (0.020, 0.0082, 0.001), (0.036, 0.0006, 0.013), "lcd", fx)
    cyl("Btn_Zero", (0.046, 0.0085, -0.009), 0.0035, 0.0016, "y", "steelDark", fx, seg=12)
    cyl("Btn_Power", (0.004, 0.0085, -0.009), 0.0030, 0.0016, "y", "red", fx, seg=12)
    cyl("Thumb_Wheel", (0.058, 0, -0.014), 0.004, 0.003, "y", "steelDark", fx, seg=12)
    box("Depth_Rod", (0.20, 0, -0.006), (0.006, 0.0015, 0.002), "steel", vis)
    empty("Tip", (-0.017, 0, 0.026), r)
    empty("Poke_Zero", (0.046, 0.010, -0.009), fx)
    return r

# ---------------------------------------------------------------- 標記筆（黃色油漆筆，+Z 朝筆尖）
def marker(name):
    r = root(name)
    vis = empty("Visual", (0, 0, 0), r)
    L, R = 0.135, 0.0085
    cyl("Body", (0, 0, -0.010), R, 0.10, "z", "yellow", vis, seg=20)
    cyl("Cap", (0, 0, 0.055), R * 1.05, 0.035, "z", "white", vis, seg=20)
    cyl("Cap_Clip", (0, R + 0.001, 0.05), 0.0015, 0.03, "z", "white", vis, seg=6)
    cyl("End", (0, 0, -0.063), R * 0.9, 0.006, "z", "black", vis, seg=20)
    empty("Tip", (0, 0, L / 2), r)
    return r

# ---------------------------------------------------------------- 清潔布（摺起的藍白布，+Z 朝下垂的一角）
def cloth(name):
    r = root(name)
    vis = empty("Visual", (0, 0, 0), r)
    bm = bmesh.new()
    nx, nz, w, l = 10, 12, 0.078, 0.13
    vs = [[bm.verts.new(U(-w / 2 + w * i / nx,
                          0.006 * math.sin(i * 1.3) * math.cos(j * 0.9) + 0.004 * math.sin(j * 1.7),
                          -l / 2 + l * j / nz)) for j in range(nz + 1)] for i in range(nx + 1)]
    for i in range(nx):
        for j in range(nz):
            bm.faces.new((vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]))
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.012)
    _obj("Cloth", bm, "cloth", vis)
    for k in range(3):
        box(f"Cloth_Stripe_{k}", (-w / 2 + w * (k + 1) / 4, 0.010, 0), (0.008, 0.002, l * 0.98), "clothB", vis)
    empty("Tip", (0, 0, l / 2), r)
    return r

# ---------------------------------------------------------------- 降弓固定帶（橘黃織帶捲＋金屬扣，捲軸 = Unity X）
def strap(name):
    r = root(name)
    vis = empty("Visual", (0, 0, 0), r)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.034, minor_radius=0.010, major_segments=28, minor_segments=6)
    t = bpy.context.active_object; t.name = "Coil"
    t.scale = (1, 1, 2.0)
    t.rotation_euler = (0, math.pi / 2, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    t.data.materials.append(mat("strap")); t.parent = vis
    box("Buckle", (0, -0.045, 0.0), (0.045, 0.006, 0.028), "steel", vis, bevel=0.002)
    box("Buckle_Bar", (0, -0.049, 0.0), (0.05, 0.004, 0.004), "steelDark", vis, bevel=0.001)
    box("Strap_Tail", (0, -0.060, 0.0), (0.040, 0.025, 0.003), "strap", vis, bevel=0.001)
    box("Hang_Loop", (0, 0.046, 0), (0.012, 0.016, 0.004), "strap", vis, bevel=0.001)
    empty("Hang_Point", (0, 0.052, 0), r)
    return r

# ---------------------------------------------------------------- 腰包
def pouch():
    P0 = P["pouch"]; W, H, D = P0["w"], P0["h"], P0["d"]
    r = root("ENV_ToolPouch")
    vis = empty("Visual", (0, 0, 0), r)
    sz = P0["tierSplitZ"]; bt, ft = P0["backTierTopY"], P0["frontTierTopY"]
    # 背板（含上緣腰帶隧道）
    box("Back_Panel", (0, 0.0, 0.004), (W, H, 0.008), PANEL, vis, bevel=0.003)
    box("Belt_Tunnel", (0, H / 2 - 0.02, -0.006), (W * 0.55, 0.055, 0.012), PANEL, vis, bevel=0.004)
    # 後排高袋：三格扳手袋＋右邊一格備用（空）
    yb0 = -H / 2 + 0.01
    box("Back_Tier", (0, (yb0 + bt) / 2, (0.008 + sz) / 2), (W - 0.01, bt - yb0, sz - 0.008), BODY, vis, bevel=0.004)
    box("Back_Tier_Welt", (0, bt - 0.002, sz - 0.002), (W - 0.008, 0.010, 0.008), BIND, vis, bevel=0.002)
    # 前排矮袋
    box("Front_Tier", (0, (yb0 + ft) / 2, (sz + D) / 2), (W - 0.01, ft - yb0, D - sz), BODY, vis, bevel=0.005)
    box("Front_Tier_Welt", (0, ft - 0.002, D - 0.002), (W - 0.008, 0.010, 0.008), BIND, vis, bevel=0.002)
    # 底部厚皮
    box("Bottom", (0, yb0 - 0.004, D / 2), (W, 0.01, D), BIND, vis, bevel=0.003)
    # 隔間縫線（前後排，前面看得到的直線）＋邊緣縫線
    back_div = [-0.125, -0.055, 0.010, 0.070]          # 後排隔線 X
    front_div = [-0.085, -0.050, 0.035]                # 前排隔線 X
    for i, x in enumerate(back_div):
        box(f"Back_Div_{i}", (x, (yb0 + bt) / 2 + 0.03, sz + 0.0005), (0.0025, bt - yb0 - 0.06, 0.0015), "stitch", vis)
        box(f"Back_DivWall_{i}", (x, (yb0 + bt) / 2, (0.008 + sz) / 2), (0.003, bt - yb0 - 0.004, sz - 0.012), PANEL, vis)
        box(f"Back_DivBind_{i}", (x, bt - 0.002, (0.008 + sz) / 2), (0.007, 0.008, sz - 0.010), BIND, vis, bevel=0.002)
    for i, x in enumerate(front_div):
        box(f"Front_Div_{i}", (x, (yb0 + ft) / 2, D + 0.0005), (0.0025, ft - yb0 - 0.02, 0.0015), "stitch", vis)
        box(f"Front_DivWall_{i}", (x, (yb0 + ft) / 2, (sz + D) / 2), (0.003, ft - yb0 - 0.004, D - sz - 0.006), PANEL, vis)
        box(f"Front_DivBind_{i}", (x, ft - 0.002, (sz + D) / 2), (0.007, 0.008, D - sz - 0.008), BIND, vis, bevel=0.002)
    for y, z, w in ((ft - 0.010, D + 0.0005, W - 0.03), (yb0 + 0.010, D + 0.0005, W - 0.03), (bt - 0.012, sz + 0.0005, W - 0.03)):
        box(f"Stitch_H_{y:.3f}", (0, y, z), (w, 0.0018, 0.0015), "stitch", vis)
    for x in (-W / 2 + 0.012, W / 2 - 0.012):
        box(f"Stitch_V_{x:+.3f}", (x, (yb0 + ft) / 2, D + 0.0005), (0.0018, ft - yb0 - 0.02, 0.0015), "stitch", vis)
    # 鉚釘（前排四角、後排上緣兩端）
    for i, (x, y, z) in enumerate([(-W / 2 + 0.012, ft - 0.016, D), (W / 2 - 0.012, ft - 0.016, D),
                                   (-W / 2 + 0.012, yb0 + 0.016, D), (W / 2 - 0.012, yb0 + 0.016, D),
                                   (-W / 2 + 0.012, bt - 0.018, sz), (W / 2 - 0.012, bt - 0.018, sz)]):
        cyl(f"Rivet_{i}", (x, y, z + 0.001), 0.0035, 0.003, "z", "rivet", vis, seg=10)
    # 右側：固定帶吊環（D 環）
    box("Ring_Tab", (W / 2 + 0.008, 0.05, 0.03), (0.018, 0.03, 0.006), BIND, vis, bevel=0.002)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.016, minor_radius=0.0025, major_segments=20, minor_segments=6)
    ring = bpy.context.active_object; ring.name = "D_Ring"
    ring.location = U(W / 2 + 0.022, 0.036, 0.03)
    ring.rotation_euler = (math.pi / 2, 0, 0)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    ring.data.materials.append(mat("steel")); ring.parent = vis
    # 左側：螺栓零件袋（灰色網袋，有拉鍊）
    bb = empty("BoltBag", (-W / 2 - 0.022, -0.04, 0.05), r)
    box("BoltBag_Body", (-W / 2 - 0.022, -0.04, 0.05), (0.04, 0.10, 0.07), "mesh", bb, bevel=0.008, seg=3)
    box("BoltBag_Zip", (-W / 2 - 0.022, 0.011, 0.05), (0.008, 0.003, 0.066), "black", bb)
    box("BoltBag_Pull", (-W / 2 - 0.022, 0.013, 0.08), (0.004, 0.003, 0.012), "steel", bb)
    # 套筒盒：前排最右格，黑盒＋透明蓋，2 × 3 孔
    sc_c = (0.090, ft + 0.005, (sz + D) / 2)
    sc = empty("SocketCase", sc_c, r)
    box("SocketCase_Body", (sc_c[0], sc_c[1] - 0.02, sc_c[2]), (0.105, 0.06, 0.044), "caseGray", sc, bevel=0.003)
    box("SocketCase_Lid", (sc_c[0], sc_c[1] + 0.0115, sc_c[2]), (0.103, 0.003, 0.042), "clear", sc, bevel=0.001)
    sockets = [("10", -0.034, -0.010), ("13", 0.0, -0.010), ("17", 0.034, -0.010),
               ("19", -0.034, 0.011), ("24", 0.0, 0.011), ("13_38", 0.034, 0.011)]
    for sid, dx, dz in sockets:
        cyl(f"SocketCase_Hole_{sid}", (sc_c[0] + dx, sc_c[1] + 0.0101, sc_c[2] + dz), 0.0125 if sid != "13_38" else 0.0105,
            0.0012, "y", "steelDark", sc, seg=20)
        empty(f"TGT_Socket_{sid}", (sc_c[0] + dx, sc_c[1] + 0.010, sc_c[2] + dz), sc, fwd=(0, 1, 0), up=(0, 0, 1))
    # 3/8" 區底墊不同色（藍）
    box("SocketCase_Pad38", (sc_c[0] + 0.034, sc_c[1] + 0.0098, sc_c[2] + 0.011), (0.028, 0.0006, 0.018), "blue", sc)
    # ---- 插槽：Pocket_NN（袋口中心，+Y 朝上 = 開口，工具沿 −Y 插入）與子節點 Stow（工具根節點放這裡、Transform 全 0）
    zb, zf = (0.008 + sz) / 2, (sz + D) / 2
    head_y = yb0 + 0.02                                   # 扳手方頭離袋底 1 cm
    pockets = {
        # 名稱: (袋口位置, Stow 位置, Stow 前方, Stow 上方)
        "Pocket_13": ((-0.090, bt, zb), (-0.090, head_y + P["torque_l"]["hl"], zb), (0, -1, 0), (0, 0, -1)),
        "Pocket_07": ((-0.022, bt, zb), (-0.022, head_y + P["torque_s"]["hl"], zb), (0, -1, 0), (0, 0, -1)),
        "Pocket_06": ((0.040, bt, zb), (0.040, bt + 0.04 - P["ratchet"]["hl"], zb), (0, 1, 0), (0, 0, 1)),   # 頭朝上（負責人：黑袋裡要看到銀色頭部）
        "Pocket_05": ((-0.113, ft, zf), (-0.105, 0.082, zf), (-1, 0, 0), (0, 0, 1)),
        "Pocket_09": ((-0.067, ft, zf), (-0.067, 0.008, zf), (0, -1, 0), (0, 0, 1)),
        "Pocket_08": ((-0.008, ft, zf), (-0.008, -0.020, zf), (0, -1, 0), (0, 0, 1)),
        "Pocket_10": ((W / 2 + 0.022, 0.020, 0.03), (W / 2 + 0.022, 0.024 - 0.052, 0.03), (0, 0, 1), (0, 1, 0)),
    }
    for pn, (mouth, stow, f, u) in pockets.items():
        pe = empty(pn, mouth, r)
        empty(pn.replace("Pocket", "Stow"), stow, pe, fwd=f, up=u)
    for n in range(5, 10):
        empty(f"Lanyard_Anchor_{n}", ((n - 7) * 0.05, bt + 0.004, 0.006), r)
    empty("Lanyard_Anchor_13", (-0.09, bt + 0.004, 0.006), r)
    return r

# ---------------------------------------------------------------- 匯出
def export(objs, path):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False)

def tris(objs):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs if o.type == "MESH")

def to_unity(v):
    return [round(-v.x, 4), round(v.z, 4), round(-v.y, 4)]

def bbox_unity(objs):
    lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
    for o in objs:
        if o.type != "MESH": continue
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
    a, b = to_unity(lo), to_unity(hi)
    return [round(min(a[i], b[i]), 4) for i in range(3)], [round(max(a[i], b[i]), 4) for i in range(3)]

bpy.ops.wm.read_factory_settings(use_empty=True)
report = {"blender": bpy.app.version_string, "coords": "Unity (m)", "files": {}}
tools = {
    "TL_TorqueWrench_L": (torque_wrench, P["torque_l"], "Pocket_13"),
    "TL_TorqueWrench_S": (torque_wrench, P["torque_s"], "Pocket_07"),
    "TL_SocketWrench":   (ratchet, P["ratchet"], "Pocket_06"),
    "TL_Caliper":        (caliper, None, "Pocket_05"),
    "TL_Marker":         (marker, None, "Pocket_09"),
    "TL_Cloth":          (cloth, None, "Pocket_08"),
    "TL_HoldDownStrap":  (strap, None, "Pocket_10"),
}
roots = {}
for name, (fn, p, _) in tools.items():
    rt = fn(name, p) if p else fn(name)
    roots[name] = rt
    objs = hierarchy(rt)
    bpy.context.view_layer.update()
    export(objs, os.path.join(args.out, name + ".fbx"))
    lo, hi = bbox_unity(objs)
    report["files"][name + ".fbx"] = {
        "tris": tris(objs), "bboxMin": lo, "bboxMax": hi,
        "nodes": {o.name: to_unity(o.matrix_world.translation) for o in objs if o.type == "EMPTY" and o.name != name and o.name != "Visual"},
    }
    # 名稱在 Blender 內唯一：匯出後把節點改名，避免下一件工具撞名（FBX 已寫出原名）
    for o in objs:
        if o is not rt: o.name = f"{name}__{o.name}"

pr = pouch()
pobjs = hierarchy(pr)
bpy.context.view_layer.update()
export(pobjs, os.path.join(args.out, "ENV_ToolPouch.fbx"))
lo, hi = bbox_unity(pobjs)
report["files"]["ENV_ToolPouch.fbx"] = {
    "tris": tris(pobjs), "bboxMin": lo, "bboxMax": hi,
    "nodes": {o.name: to_unity(o.matrix_world.translation) for o in pobjs if o.type == "EMPTY" and o.name not in ("ENV_ToolPouch", "Visual")},
}

# ---- 預覽：把工具放到各自的 Stow（只用於預覽，不在 FBX 裡）
for name, (_, _, pocket) in tools.items():
    stow = bpy.data.objects[pocket.replace("Pocket", "Stow")]
    roots[name].matrix_world = stow.matrix_world.copy()
bpy.context.view_layer.update()
for t in [o for o in bpy.data.objects if o.name.startswith("TGT_Socket_")]:
    p_ = t.matrix_world.translation
    big = not t.name.endswith("13_38")
    d = cyl("PREVIEW_" + t.name, (0, 0, 0), 0.0105 if big else 0.0090, 0.03, "y", "steel", None, seg=16)
    d.location = p_ - Vector((0, 0, 0.012))
bpy.context.view_layer.update()
allm = [o for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith("PREVIEW_")]
report["stowedBounds"] = dict(zip(("min", "max"), bbox_unity(allm)))
with open(os.path.join(args.out, "ToolPouchReport.json"), "w", encoding="utf-8") as f:
    json.dump(report, f, ensure_ascii=False, indent=1)

if args.render:
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"; sc.cycles.samples = args.samples; sc.cycles.use_denoising = True
    sc.render.resolution_x = sc.render.resolution_y = 900
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"
    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.82, 0.84, 0.87, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    # 地面不放；燈：主光＋補光
    for nm, loc, e in (("Key", U(0.6, 1.2, 0.9), 120), ("Fill", U(-0.8, 0.6, 0.6), 40), ("Top", U(0, 1.5, -0.2), 40)):
        L = bpy.data.lights.new(nm, "AREA"); L.energy = e; L.size = 0.8
        lo_ = bpy.data.objects.new(nm, L); sc.collection.objects.link(lo_)
        lo_.location = loc
        lo_.rotation_euler = (U(0, 0.05, 0.05) - loc).to_track_quat("-Z", "Y").to_euler()
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 50
    target = U(0, 0.08, 0.05)
    views = {
        "preview_front.png": U(0, 0.10, 0.90),
        "preview_45.png": U(0.62, 0.38, 0.62),
        # 學員眼高 1.63 m、腰包中心 1.08 m：眼睛比腰包高 0.55 m。腰包在學員左前方，
        # 建議軟體讓腰包正面（+Z）朝向學員，學員從前上方往下看到所有袋口（見 README）
        "preview_eye.png": U(-0.22, 0.55, 0.32),
    }
    for fn, pos in views.items():
        cam.location = pos
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = 35 if fn == "preview_eye.png" else 50
        sc.render.filepath = os.path.join(args.out, fn)
        bpy.ops.render.render(write_still=True)
print("done", json.dumps({k: v["tris"] for k, v in report["files"].items()}))
