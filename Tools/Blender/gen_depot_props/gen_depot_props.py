"""gen_depot_props.py v1.0（資深建模師 A，2026-11-15 定稿交付版）
產生平台設備與外接電源的 FBX：LiftControlBox、IsolationStatusLight、LotoBoard、PlatformShelf、
ShorePowerBox、ShorePlug、ShoreSocket（terry 2026-11-16 預覽 01–07 通過）。
材質只用 GeneratedModelTests 的固定名稱（Paint／Dark／Metal／Glass），UV 指到 Palette.png 色格；
色格順序優先讀 ../gen_700t/palette.py，讀不到才用內建表。只需 Blender 內建模組，不輸出預覽圖。
Blender 軸向：寬 X、深 Y（正面 −Y）、高 Z，單位 m；匯出後 Unity 正面 +Z、原點在底面中心。
用法（實驗室）：blender.exe -b --factory-startup -P Tools/Blender/gen_depot_props/gen_depot_props.py --
          --fbx all --out "Assets/_Project/Worksite/Models/Generated" [--report 報告.json]
雲端預覽：同資料夾有 preview_render.py 時加 --preview，另出 PNG（實驗室不用）。
"""
import bpy, bmesh, math, sys, os, json
from mathutils import Vector, Matrix
from bpy_extras.io_utils import axis_conversion

ARGS = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else sys.argv[1:]
def arg(k, d=None): return ARGS[ARGS.index(k)+1] if k in ARGS else d
OUT = os.path.abspath(arg('--out', '.'))
ALL = ['LiftControlBox', 'IsolationStatusLight', 'LotoBoard', 'PlatformShelf', 'ShorePowerBox', 'ShorePlug', 'ShoreSocket']
FBX = ALL if arg('--fbx') == 'all' else (arg('--fbx').split(',') if arg('--fbx') else [])
HERE = os.path.dirname(os.path.abspath(__file__))

# 色盤：gen_700t/palette.py 的 colors 順序（第 i 格：欄 i%8、列 i//8）。實驗室查核 2026-11-15。
BUILTIN = ['white', 'orange', 'dark', 'roof', 'walkway', 'glass', 'metal', 'insulator',
           'yellow', 'red', 'green', 'craneyellow', 'rail', 'concrete', 'blue', 'steel',
           'rubber', 'copper', 'orangesafety', 'strapyellow', 'rubberdarkgrey', 'aqua']
def palette_order():
    try:
        sys.path.insert(0, os.path.join(HERE, '..', 'gen_700t'))
        import palette as P
        for src in (P, getattr(P, 'Palette', None)):
            for k in ('COLORS', 'colors'):
                d = getattr(src, k, None)
                if isinstance(d, dict) and list(d)[:len(BUILTIN)] == BUILTIN: return list(d), 'palette.py'
    except Exception: pass
    return BUILTIN, '內建表'
ORDER, ORDER_SRC = palette_order()

# 零件用的顏色名 → (固定材質, 色格)。燈罩用 Glass，亮暗交給 Unity。
COLOR = {'Grey': ('Paint', 'concrete'), 'White': ('Paint', 'white'), 'Yellow': ('Paint', 'yellow'),
         'Red': ('Paint', 'red'), 'Green': ('Paint', 'green'), 'Orange': ('Paint', 'orange'),
         'DarkGrey': ('Dark', 'dark'), 'Black': ('Dark', 'rubber'), 'Rubber': ('Dark', 'rubber'),
         'Metal': ('Metal', 'metal'), 'LensRed': ('Glass', 'red'), 'LensGreen': ('Glass', 'green')}

def cell_uv(cell):
    i = ORDER.index(cell)
    return ((i % 8 + 0.5) / 8, (i // 8 + 0.5) / 8)   # Blender 圖片第 0 列在下，v 不用 1−

def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def empty(name, loc, parent=None):
    e = bpy.data.objects.new(name, None); e.empty_display_size = 0.03
    bpy.context.scene.collection.objects.link(e); e.location = loc
    if parent: e.parent = parent
    return e

def box(name, size, loc, m, parent=None, bevel=0.0):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co = Vector((v.co.x*size[0], v.co.y*size[1], v.co.z*size[2]))
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, segments=2, affect='EDGES')
    return _obj(name, bm, loc, m, parent)

def cyl(name, r, h, loc, m, parent=None, axis='Z', seg=16, r2=None, caps=True):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
    rot = {'Z':None,'Y':('X',90),'X':('Y',90)}[axis]
    if rot:
        bmesh.ops.rotate(bm, verts=bm.verts, matrix=Matrix.Rotation(math.radians(rot[1]),3,rot[0]))
    return _obj(name, bm, loc, m, parent)

def _obj(name, bm, loc, m, parent):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    mname, cell = COLOR[m]
    me.materials.append(bpy.data.materials.get(mname) or bpy.data.materials.new(mname))
    uv = cell_uv(cell)
    for d in me.uv_layers.new(name='UVMap').data: d.uv = uv
    o['cell'] = cell; o.location = loc
    if parent: o.parent = parent
    return o

def tri_prism(name, w, h, depth, loc, m, parent=None, up=True, local_z=False):
    """凸起的 ▲／▼ 按鈕：在 XZ 平面畫三角形，往 −Y 擠出 depth。"""
    bm = bmesh.new()
    s = 1 if up else -1
    if local_z:   # 三角形在 XY 平面（局部 +Y＝世界上方），往 +Z 擠出
        vs = [bm.verts.new((-w/2, -s*h/2, 0)), bm.verts.new((w/2, -s*h/2, 0)), bm.verts.new((0, s*h/2, 0))]
        ext = (0, 0, depth)
    else:
        vs = [bm.verts.new((-w/2, 0, -s*h/2)), bm.verts.new((w/2, 0, -s*h/2)), bm.verts.new((0, 0, s*h/2))]
        ext = (0, -depth, 0)
    f = bm.faces.new(vs if up else vs[::-1])
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    bmesh.ops.translate(bm, verts=[e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)], vec=ext)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces: f.smooth = False
    return _obj(name, bm, loc, m, parent)

def tris():
    n = 0
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            n += sum(len(p.vertices)-2 for p in o.data.polygons)
    return n

# ---------- 1. 升降操作盒（LiftControlBox 0.08 深 × 0.32 寬 × 0.36 高，實驗室電腦回報）----------
def lift_box():
    root = empty('LiftControlBox', (0,0,0))
    W, D, H = 0.32, 0.08, 0.36
    box('Body', (W, D, H), (0, 0, H/2), 'Grey', root, bevel=0.008)
    box('FrontCover', (W-0.02, 0.006, H-0.02), (0, -D/2-0.003, H/2), 'Grey', root, bevel=0.003)
    for i,(x,z) in enumerate([(-1,-1),(1,-1),(1,1),(-1,1)],1):
        cyl(f'CoverScrew_{i}', 0.005, 0.004, (x*(W/2-0.02), -D/2-0.008, H/2+z*(H/2-0.02)), 'Metal', root, axis='Y', seg=8)
    # 燈 PanIndicator（上方）：燈座＋燈罩＋遮光罩
    lamp = empty('PanIndicator', (0, -D/2-0.006, 0.30), root)
    cyl('PanIndicator_Base', 0.024, 0.012, (0, -0.006, 0), 'DarkGrey', lamp, axis='Y', seg=20)
    cyl('PanIndicator_Lens', 0.018, 0.016, (0, -0.020, 0), 'LensGreen', lamp, axis='Y', seg=20, r2=0.009)  # 圓頂燈罩，和按鈕區分
    empty('Label_Lamp', (0.07, -D/2-0.007, 0.30), root)
    # ▲▼ 兩顆按鈕：護圈＋按鈕本體（樞軸在按鈕底面，局部 −Y 是按下）
    for name, z, up in [('Up', 0.20, True), ('Down', 0.12, False)]:
        cyl(f'Guard_{name}', 0.028, 0.008, (0, -D/2-0.010, z), 'DarkGrey', root, axis='Y', seg=20)
        b = empty(f'Btn_{name}', (0, -D/2-0.006, z), root)
        b.rotation_euler = (math.radians(90), 0, 0)   # 軟體規格：局部 −Y 是按下 → Blender 局部 −Z 要朝盒內（世界 +Y）
        # 子物件直接用按鈕節點的局部座標：局部 +Z 朝外（學員），局部 −Z 朝盒內＝按下
        cyl(f'Btn_{name}_Base', 0.020, 0.010, (0, 0, 0.009), 'Black', b, axis='Z', seg=20)
        tri_prism(f'Btn_{name}_Cap', 0.026, 0.022, 0.008, (0, 0, 0.014), 'White', b, up=up, local_z=True)   # 凸起的 ▲▼（建模 B 建議）
        empty(f'Label_{name}', (0.07, -D/2-0.007, z), root)
    empty('Label_Title', (0, -D/2-0.007, 0.345), root)
    # 黃色警示長條（字牌位置）
    box('WarnStrip', (W-0.04, 0.003, 0.035), (0, -D/2-0.0075, 0.045), 'Yellow', root)
    empty('Label_Warn', (0, -D/2-0.009, 0.045), root)
    # 底部電纜固定頭＋電纜
    cyl('CableGland', 0.012, 0.02, (0.08, 0, -0.01), 'DarkGrey', root, seg=12)
    cyl('Cable', 0.007, 0.25, (0.08, 0, -0.145), 'Black', root, seg=10)
    # 欄杆夾具（靠車側欄杆，盒背 +Y）
    for z in (0.08, 0.28):
        box(f'Clamp_{z:.2f}', (0.06, 0.03, 0.03), (0, D/2+0.015, z), 'Metal', root)
    return root, (0, 0, 0.18), 0.9

# ---------- 2. 隔離燈號（照 terry 照片 01、05：灰色鋼管立柱上的灰燈箱，紅燈在上、綠燈在下）----------
def isolation_light():
    root = empty('IsolationStatusLight', (0,0,0))
    box('PoleBase', (0.22, 0.22, 0.015), (0, 0, 0.0075), 'Metal', root)
    cyl('Pole', 0.03, 1.40, (0, 0, 0.70), 'Grey', root, seg=16)
    box('Housing', (0.22, 0.14, 0.34), (0, 0, 1.55), 'Grey', root, bevel=0.008)
    box('Housing_Door', (0.20, 0.006, 0.32), (0, -0.073, 1.55), 'Grey', root, bevel=0.003)
    box('Housing_Hinge', (0.008, 0.01, 0.30), (-0.10, -0.075, 1.55), 'Metal', root)
    for name, z, lens in [('Red', 1.65, 'LensRed'), ('Green', 1.55, 'LensGreen')]:
        lamp = empty(f'StatusLamp_{name}', (0, -0.076, z), root)
        cyl(f'StatusLamp_{name}_Bezel', 0.030, 0.010, (0, -0.005, 0), 'Metal', lamp, axis='Y', seg=20)
        cyl(f'StatusLamp_{name}_Lens', 0.022, 0.018, (0, -0.016, 0), lens, lamp, axis='Y', seg=20, r2=0.012)
        empty(f'Label_{name}', (0.065, -0.077, z), root)
    empty('Label_Title', (0, -0.077, 1.45), root)
    box('StatusText_Frame', (0.38, 0.015, 0.22), (0, -0.04, 1.22), 'DarkGrey', root)
    box('StatusText_Board', (0.36, 0.01, 0.20), (0, -0.05, 1.22), 'White', root)
    empty('StatusText', (0, -0.056, 1.22), root)
    for z in (1.16, 1.28):
        box(f'BoardClamp_{z:.2f}', (0.08, 0.04, 0.02), (0, -0.015, z), 'Metal', root)
    return root, (0, 0, 1.35), 1.6

# ---------- 3. 掛牌板（上鎖掛牌站）----------
def tag_board():
    root = empty('LotoBoard', (0,0,0))
    W, H = 0.60, 0.45
    box('Board', (W, 0.012, H), (0, 0, 1.20), 'Yellow', root, bevel=0.006)
    box('Header', (W-0.02, 0.004, 0.07), (0, -0.008, 1.39), 'Red', root)
    empty('Label_Title', (0, -0.011, 1.39), root)
    n = 6
    for i in range(n):
        x = -W/2 + 0.07 + i*(W-0.14)/(n-1)
        h = empty(f'Hook_Lock_{i+1}', (x, -0.006, 1.31), root)
        cyl(f'Hook_Lock_{i+1}_Pin', 0.005, 0.05, (0, -0.025, 0), 'Metal', h, axis='Y', seg=8)
        box(f'Hook_Lock_{i+1}_Tip', (0.008, 0.008, 0.02), (0, -0.05, 0.008), 'Metal', h)
        box(f'NamePlate_{i+1}', (0.07, 0.003, 0.03), (x, -0.0075, 1.255), 'White', root)
        empty(f'Label_Slot_{i+1}', (x, -0.009, 1.255), root)
        t = empty(f'Hook_Tag_{i+1}', (x, -0.006, 1.14), root)
        cyl(f'Hook_Tag_{i+1}_Pin', 0.004, 0.04, (0, -0.02, 0), 'Metal', t, axis='Y', seg=8)
        box(f'TagSilhouette_{i+1}', (0.06, 0.002, 0.10), (x, -0.008, 1.075), 'White', root)
    box('Footer', (W-0.02, 0.004, 0.04), (0, -0.008, 1.005), 'White', root)
    empty('Label_Footer', (0, -0.011, 1.005), root)
    for x in (-W/2+0.02, W/2-0.02):
        for z in (1.0, 1.40):
            cyl(f'Screw_{x:.2f}_{z:.2f}', 0.006, 0.004, (x, -0.008, z), 'Metal', root, axis='Y', seg=8)
    for c in root.children: c.location.z -= 0.975   # v0.7：原點移到板子底邊中心（板底原本在 0.975）
    return root, (0, 0, 0.22), 1.1

# ---------- 4. 平台置物架＋零件盒＋R1 螺栓方盤 L／R ----------
def shelf():
    root = empty('PlatformShelf', (0,0,0))
    L, D, H = 1.40, 0.40, 0.85      # 檯面 0.40 × 1.40、頂面 0.85（RoofSceneDef）
    for i,(x,y) in enumerate([(-1,-1),(1,-1),(1,1),(-1,1)],1):
        px, py = x*(L/2-0.02), y*(D/2-0.02)   # 角鋼 40 × 40 × 4 mm（L 形斷面，建模 B 建議）
        box(f'Post_{i}_A', (0.04, 0.004, H), (px, py + y*0.018, H/2), 'Grey', root)
        box(f'Post_{i}_B', (0.004, 0.04, H), (px + x*0.018, py, H/2), 'Grey', root)
        box(f'Foot_{i}', (0.08, 0.08, 0.008), (x*(L/2-0.02), y*(D/2-0.02), 0.004), 'Rubber', root)
    for z, nm in [(H-0.015, 'Top'), (0.25, 'Lower')]:
        box(f'Shelf_{nm}', (L, D, 0.015), (0, 0, z), 'Grey', root)
        box(f'Shelf_{nm}_Lip', (L, 0.004, 0.035), (0, -D/2-0.002, z+0.01), 'Grey', root)
        box(f'Shelf_{nm}_Rib', (L-0.08, 0.03, 0.03), (0, 0, z-0.022), 'DarkGrey', root)
        empty(f'Label_{nm}', (0, -D/2-0.005, z+0.01), root)
    # 零件盒 PartsTray 0.34 × 0.06 × 0.26（實驗室電腦回報；放檯面 +X 端）
    pt = empty('PartsTray', (0.45, 0, H), root)
    box('PartsTray_Bottom', (0.34, 0.26, 0.006), (0, 0, 0.003), 'DarkGrey', pt)
    for nm,s,l in [('N',(0.34,0.006,0.06),(0,0.127,0.03)),('S',(0.34,0.006,0.06),(0,-0.127,0.03)),
                   ('E',(0.006,0.26,0.06),(0.167,0,0.03)),('W',(0.006,0.26,0.06),(-0.167,0,0.03))]:
        box(f'PartsTray_Wall_{nm}', s, l, 'DarkGrey', pt)
    # R1 螺栓方盤 L／R（方盤，尺寸未定，預覽暫用 0.20 × 0.15 × 0.03）
    for side, x in [('L', -0.20), ('R', 0.05)]:
        t = empty(f'BoltTray_Square_{side}', (x, 0, H), root)
        box(f'Tray_{side}_Bottom', (0.20, 0.15, 0.004), (0, 0, 0.006), 'Metal', t)
        for nm,s,l in [('N',(0.20,0.004,0.03),(0,0.073,0.019)),('S',(0.20,0.004,0.03),(0,-0.073,0.019)),
                       ('E',(0.004,0.15,0.03),(0.098,0,0.019)),('W',(0.004,0.15,0.03),(-0.098,0,0.019))]:
            box(f'Tray_{side}_Rim_{nm}', s, l, 'Metal', t)
        for j,(a,b) in enumerate([(-1,-1),(1,-1),(1,1),(-1,1)]):
            cyl(f'Tray_{side}_Foot_{j}', 0.008, 0.004, (a*0.085, b*0.06, 0.002), 'Rubber', t, seg=8)
        for k in range(4):   # 示意：4 支螺栓
            cyl(f'Tray_{side}_Bolt_{k}', 0.006, 0.04, (-0.06+k*0.04, 0, 0.012), 'Metal', t, axis='X', seg=8)
        empty(f'Label_Tray_{side}', (0, -0.08, 0.02), t)
    return root, (0, 0, 0.45), 3.2

# ---------- 5. 外接電源插座箱（terry 175B；照片 05 車側立柱上的大灰箱；鐵道工程師草稿 shore-power-box-draft v3）----------
# 正面朝 −Y（Blender）＝朝學員。箱上：紅燈「供電中」、綠燈「已切離」、可上鎖的旋轉開關、插頭座、高壓警示三角。
def shore_box():
    root = empty('ShorePowerBox', (0,0,0))
    W, D, H, Z0 = 0.50, 0.25, 0.70, 0.95          # 箱底離地 0.95，箱頂 1.65（推估，照片沒有尺寸）
    box('PoleBase', (0.25, 0.25, 0.015), (0, 0.06, 0.0075), 'Metal', root)
    cyl('Pole', 0.045, Z0, (0, 0.06, Z0/2), 'Grey', root, seg=16)
    box('Body', (W, D, H), (0, 0, Z0+H/2), 'Grey', root, bevel=0.01)
    fy = -D/2
    box('Door', (W-0.03, 0.008, H-0.03), (0, fy-0.004, Z0+H/2), 'Grey', root, bevel=0.004)
    for z in (Z0+0.12, Z0+H-0.12):
        box(f'Door_Hinge_{z:.2f}', (0.012, 0.014, 0.06), (-W/2+0.004, fy-0.006, z), 'Metal', root)
    box('Door_Latch', (0.02, 0.02, 0.07), (W/2-0.035, fy-0.016, Z0+H/2), 'DarkGrey', root)
    box('RainHood', (W+0.04, D+0.06, 0.015), (0, -0.01, Z0+H+0.0075), 'Grey', root)
    # 燈：紅＝供電中（上左）、綠＝已切離（上右）。新做的燈用 Lamp_<名>
    for name, x, lens in [('SupplyOn', -0.09, 'LensRed'), ('SupplyOff', 0.09, 'LensGreen')]:
        lamp = empty(f'Lamp_{name}', (x, fy-0.008, Z0+0.58), root)
        cyl(f'Lamp_{name}_Bezel', 0.028, 0.010, (0, -0.005, 0), 'Metal', lamp, axis='Y', seg=20)
        cyl(f'Lamp_{name}_Lens', 0.020, 0.016, (0, -0.015, 0), lens, lamp, axis='Y', seg=20, r2=0.011)
        empty(f'Label_{name}', (x, fy-0.009, Z0+0.52), root)
    empty('Label_Title', (0, fy-0.009, Z0+0.655), root)
    # 旋轉開關：樞軸在把手中心，繞面板法線轉；建模時是 ON（把手直立），轉 90°（從正面看順時針）到 OFF（橫）
    box('Switch_Plate', (0.16, 0.006, 0.16), (0, fy-0.011, Z0+0.38), 'DarkGrey', root)
    sw = empty('Switch_Supply', (0, fy-0.014, Z0+0.38), root)
    cyl('Switch_Supply_Hub', 0.03, 0.02, (0, -0.01, 0), 'Black', sw, axis='Y', seg=20)
    box('Switch_Supply_Handle', (0.03, 0.025, 0.13), (0, -0.025, 0), 'Red', sw, bevel=0.004)
    box('Switch_Supply_HaspTab', (0.012, 0.004, 0.03), (0.022, -0.012, -0.05), 'Metal', sw)   # 把手上的鎖耳，OFF 時對上固定鎖耳
    tab = empty('Hook_Lock_Supply', (0.075, fy-0.016, Z0+0.38), root)   # 固定鎖耳：掛鎖、掛牌掛這裡
    box('Hook_Lock_Supply_Tab', (0.03, 0.004, 0.012), (0, 0, 0), 'Metal', tab)
    empty('Label_Switch_On', (0, fy-0.015, Z0+0.47), root)
    empty('Label_Switch_Off', (0.06, fy-0.015, Z0+0.30), root)
    # 插頭座（插頭拔下後掛這裡）：開口朝上的杯座
    hol = empty('PlugHolster', (0, fy-0.06, Z0+0.12), root)
    cyl('PlugHolster_Cup', 0.05, 0.08, (0, 0, 0), 'DarkGrey', hol, seg=20)
    box('PlugHolster_Arm', (0.03, 0.05, 0.03), (0, 0.035, -0.02), 'Metal', hol)
    empty('PlugSeat', (0, fy-0.06, Z0+0.16), root)   # 插頭掛好時的位置（插頭原點對到這裡）
    empty('Label_Holster', (0, fy-0.009, Z0+0.22), root)
    # 高壓警示：黃色三角牌加黑框、黑色閃電（幾何，不放字）
    tri_prism('Warn_Frame', 0.13, 0.11, 0.003, (-0.16, fy-0.009, Z0+0.25), 'Black', root)
    tri_prism('Warn_Fill', 0.11, 0.09, 0.003, (-0.16, fy-0.012, Z0+0.245), 'Yellow', root)
    box('Warn_Bolt', (0.008, 0.002, 0.045), (-0.16, fy-0.0145, Z0+0.24), 'Black', root)
    # 電纜：箱底接頭往下，沿立柱到地面（之後由建置程式從地面拉到插頭）
    cyl('CableGland', 0.022, 0.03, (0.15, 0, Z0-0.015), 'DarkGrey', root, seg=12)
    cyl('Cable_Drop', 0.016, Z0-0.03, (0.15, 0, (Z0-0.03)/2), 'Black', root, seg=10)
    empty('CableAnchor', (0.15, 0, 0.02), root)
    for c in root.children: c.location.y -= 0.06   # v0.7：原點移到立柱底面中心
    return root, (0, 0, 1.15), 3.0

def shore_plug():
    """插頭（學員抓著拔下、掛回插頭座）：原點在插頭前端中心，插入方向＝局部 +Y（Blender），握柄朝 −Y。"""
    root = empty('ShorePlug', (0,0,0))
    cyl('Plug_Nose', 0.035, 0.05, (0, 0.025, 0), 'DarkGrey', root, axis='Y', seg=20)
    cyl('Plug_Ring', 0.045, 0.02, (0, -0.01, 0), 'Orange', root, axis='Y', seg=20)
    cyl('Plug_Grip', 0.04, 0.12, (0, -0.08, 0), 'Black', root, axis='Y', seg=16, r2=0.03)
    cyl('Plug_Boot', 0.02, 0.06, (0, -0.17, 0), 'Black', root, axis='Y', seg=12)
    empty('GrabPoint', (0, -0.08, 0), root)
    empty('CableAnchor', (0, -0.20, 0), root)
    return root, (0, -0.07, 0), 0.6

def shore_socket():
    """車側插座（裝在裙板上）：原點在插座面中心，正面朝 −Y＝朝車外。"""
    root = empty('ShoreSocket', (0,0,0))
    box('Socket_Flange', (0.18, 0.012, 0.18), (0, 0.006, 0), 'Grey', root, bevel=0.004)
    cyl('Socket_Body', 0.05, 0.04, (0, -0.02, 0), 'DarkGrey', root, axis='Y', seg=20)
    box('Socket_LidHinge', (0.08, 0.01, 0.01), (0, -0.01, 0.065), 'Metal', root)
    empty('PlugSeat', (0, -0.04, 0), root)   # 插頭插好時的位置
    empty('Label_Socket', (0, -0.002, -0.075), root)
    for x in (-0.07, 0.07):
        for z in (-0.07, 0.07):
            cyl(f'Screw_{x:+.2f}_{z:+.2f}', 0.005, 0.004, (x, -0.002, z), 'Metal', root, axis='Y', seg=8)
    return root, (0, 0, 0), 0.6

# ---------- FBX 匯出（照建模 B tool-pouch v1.2 的做法：先把軸向換算進網格與子節點，bake_space_transform 關掉，避免巢狀空物件位移）----------
CONV = axis_conversion(to_forward='-Z', to_up='Y').to_4x4()

def _conj(objs, rt, C):
    Ci = C.inverted()
    for o in objs:
        if o.type == 'MESH': o.data.transform(C)
        if o is rt: continue
        o.matrix_parent_inverse = C @ o.matrix_parent_inverse @ Ci
        o.matrix_basis = C @ o.matrix_basis @ Ci

def export_fbx(rt, path):
    objs = [rt] + list(rt.children_recursive)
    keep = rt.matrix_basis.copy()
    _conj(objs, rt, CONV); rt.matrix_basis = CONV.inverted(); bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
        bake_space_transform=False, mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False)
    _conj(objs, rt, CONV.inverted()); rt.matrix_basis = keep; bpy.context.view_layer.update()

def to_unity(v):   # Blender (x, y, z) → Unity (−x, z, −y)；正面 Blender −Y → Unity +Z
    return [round(-v.x, 4), round(v.z, 4), round(-v.y, 4)]

def node_table(rt):
    rows = {}
    for o in [rt] + list(rt.children_recursive):
        if o.type == 'EMPTY': rows[o.name] = to_unity(o.matrix_world.translation)
    return rows

if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    preview = '--preview' in ARGS and os.path.exists(os.path.join(HERE, 'preview_render.py'))
    rep = {'script': 'gen_depot_props v1.0', 'palette': ORDER_SRC, 'blender': bpy.app.version_string,
           'coords': 'Unity (m), 原點在底面中心, 正面 +Z', 'files': {}}
    for fn in [lift_box, isolation_light, tag_board, shelf, shore_box, shore_plug, shore_socket]:
        clear(); rt, tgt, dist = fn(); n = tris()
        print(f'{rt.name}\t{n} tris')
        if preview:
            sys.path.insert(0, HERE); import preview_render; preview_render.render(rt, tgt, dist, OUT)
        if rt.name in FBX:
            fname = f'ENV_{rt.name}.fbx'; export_fbx(rt, os.path.join(OUT, fname))
            meshes = [o for o in rt.children_recursive if o.type == 'MESH']
            rep['files'][fname] = {'tris': n, 'materials': sorted({o.data.materials[0].name for o in meshes}),
                                   'meshCell': {o.name: o['cell'] for o in meshes}, 'nodes': node_table(rt)}
    print('palette:', ORDER_SRC)
    if arg('--report'):
        json.dump(rep, open(arg('--report'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
