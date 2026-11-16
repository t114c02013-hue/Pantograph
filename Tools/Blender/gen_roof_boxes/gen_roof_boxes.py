"""gen_roof_boxes.py v1.1（資深建模師 B，2026-11-16；v1.1 依 RA-28：空調中間蓋板加氣壓撐桿、鉸鏈看得見、電池箱／變流器加「高壓危險」牌、電阻箱加「高溫注意」牌與網孔蓋板、電池端子加絕緣蓋；色格照實驗室 palette.py 查核表，和 gen_depot_props v1.0 一致）
車頂箱體／地面教學箱 4 種（空調、煞車電阻箱、電池箱、變流器）＋ 4 個地面鋼架。
依據：depot-props/training-boxes-20261115.md 第 7、8 節（定稿座標）、spec-prop-motion-20261115.md 第 2、7 節、
roof-boxes-B.md 第 6 節（140B 外框）、負責人 11-16 要求（原點在底面中心；箱體、蓋子、鋼架腳分開子節點）。

座標一律用 Unity（m）寫：+X 東、+Y 上、+Z 北＝箱子正面；原點在底面中心。
建模時把 Unity 座標換成 Blender（x,y,z)->(−x,−z,y)；匯出照 tool-pouch v1.2（先換算整條階層，bake 關閉）。

節點（spec 2.1）：
  ENV_RoofBox_<種類>
    Body              箱體（底板、四面牆、頂框）
    Detail_*          外觀小細節（螺栓、百葉、銘牌、吊環、電纜頭），車頂遠處可在建置時關掉
    Hinge_n           鉸鏈固定座（不動）
    Lid_n             蓋板，原點在鉸鏈線上，局部 +X 沿鉸鏈，局部 −Z 指向蓋板本體；繞局部 +X 正轉＝打開
      Lid_n_Handle    把手（外觀）
    Inside_1          內部零件的父節點（空節點），底下都是網格
    Label_Name        TextMesh 字牌位置（空節點），局部 +Z 朝讀者
  ENV_TrainingBoxStand_<種類>
    Stand_Frame、Stand_Leg_1..4、Stand_Brace

用法：python gen_roof_boxes.py --out <資料夾> [--fbx] [--render] [--samples 32]
"""
import bpy, bmesh, math, sys, os, json
from mathutils import Vector, Matrix
from bpy_extras.io_utils import axis_conversion

ARGS = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else sys.argv[1:]
OUT = ARGS[ARGS.index('--out')+1] if '--out' in ARGS else '.'
DO_FBX = '--fbx' in ARGS
DO_RENDER = '--render' in ARGS
SAMPLES = int(ARGS[ARGS.index('--samples')+1]) if '--samples' in ARGS else 32

# ---------------- 材質：零件顏色 → (7 個固定材質名, Palette 色格)。色格順序和建模 A gen_depot_props v1.0 相同：
# 優先讀 ../gen_700t/palette.py 的 colors 順序（前 22 個要和下表一致），讀不到才用內建表；格 i 的 UV＝((i%8+0.5)/8, (i//8+0.5)/8)，v 沒有 1−。
HERE = os.path.dirname(os.path.abspath(__file__))
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
COL = {'body': (0.60, 0.62, 0.64), 'dark': (0.17, 0.17, 0.17), 'black': (0.08, 0.08, 0.08), 'metal': (0.55, 0.57, 0.60),
       'copper': (0.72, 0.45, 0.20), 'red': (0.80, 0.12, 0.10), 'green': (0.20, 0.75, 0.30), 'yellow': (0.98, 0.80, 0.05),
       'white': (0.55, 0.32, 0.20)}   # 預覽近似色（照 palette 色值）；'white' 鍵是絕緣礙子，用 palette 的 insulator 棕色
FIXED = {'body': ('Paint', 'concrete'), 'dark': ('Dark', 'dark'), 'black': ('Dark', 'rubber'), 'metal': ('Metal', 'metal'),
         'copper': ('Paint', 'copper'), 'red': ('Paint', 'red'), 'green': ('Paint', 'green'), 'yellow': ('Paint', 'yellow'),
         'white': ('Insulator', 'insulator')}
def cell_uv(cell):
    i = ORDER.index(cell)
    return ((i % 8 + 0.5) / 8, (i // 8 + 0.5) / 8)

def mat(key):
    m = bpy.data.materials.get('PV_' + key)
    if m is None:
        m = bpy.data.materials.new('PV_' + key); m.diffuse_color = (*COL[key], 1)
    return m

# ---------------- Unity ↔ Blender
MI = Matrix(((-1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))   # Unity → Blender
M = MI.inverted()                                                          # Blender → Unity

def T(p=(0, 0, 0), X=(1, 0, 0), Y=(0, 1, 0)):
    """Unity 局部變換：位置 p，局部 X、Y 軸（Z = X×Y 的左手對應：用 Y×Z=X → Z = X×Y）。"""
    X = Vector(X).normalized(); Y = Vector(Y).normalized(); Z = X.cross(Y)
    m = Matrix.Identity(4)
    for i in range(3):
        m[i][0], m[i][1], m[i][2], m[i][3] = X[i], Y[i], Z[i], p[i]
    return m

def link(o, parent, Tu):
    bpy.context.scene.collection.objects.link(o)
    if parent is not None:
        o.parent = parent; o.matrix_parent_inverse = Matrix.Identity(4)
    o.matrix_basis = MI @ Tu @ M
    return o

def empty(name, parent, Tu=None):
    e = bpy.data.objects.new(name, None); e.empty_display_size = 0.05
    return link(e, parent, Tu or T())

class G:
    """在 Unity 局部座標裡收集幾何，最後換成 Blender 網格。"""
    def __init__(self): self.bm = bmesh.new()
    def _add(self, tmp, mtx):
        tmp.transform(mtx); me = bpy.data.meshes.new('t'); tmp.to_mesh(me); tmp.free()
        self.bm.from_mesh(me); bpy.data.meshes.remove(me)
    def box(self, c, s):
        t = bmesh.new(); bmesh.ops.create_cube(t, size=1.0)
        self._add(t, Matrix.Translation(c) @ Matrix.Diagonal((*s, 1)))
    def cyl(self, a, b, r, seg=8, caps=True, r2=None):
        a, b = Vector(a), Vector(b); d = b - a; L = d.length
        t = bmesh.new(); bmesh.ops.create_cone(t, cap_ends=caps, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=L)
        rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        self._add(t, Matrix.Translation((a + b) / 2) @ rot)
    def plate(self, pts2d, o, u, v, n, t):
        """凸多邊形薄片：pts2d 在 (u, v) 平面，往 n 方向厚 t。o、u、v、n 是父節點 Unity 局部座標。"""
        o, u, v, n = Vector(o), Vector(u), Vector(v), Vector(n)
        b = [o + u * x + v * y for x, y in pts2d]; tp = [q + n * t for q in b]
        bm = self.bm; vb = [bm.verts.new(q) for q in b]; vt = [bm.verts.new(q) for q in tp]
        bm.faces.new(vt); bm.faces.new(vb[::-1]); k = len(b)
        for i in range(k):
            j = (i + 1) % k; bm.faces.new((vb[i], vb[j], vt[j], vt[i]))
    def quad(self, c, sx, sz, y):
        """朝 +Y 的方形面（散熱孔的凹格），中心 c=(x,z)。"""
        bm = self.bm; x, z = c
        vs = [bm.verts.new((x - sx / 2, y, z + sz / 2)), bm.verts.new((x + sx / 2, y, z + sz / 2)),
              bm.verts.new((x + sx / 2, y, z - sz / 2)), bm.verts.new((x - sx / 2, y, z - sz / 2))]
        bm.faces.new(vs)
    def tube(self, pts, r, seg=6):
        for i in range(len(pts) - 1):
            self.cyl(pts[i], pts[i + 1], r, seg, caps=(i == 0 or i == len(pts) - 2))
    def mesh(self, name, key, parent, Tu=None):
        self.bm.transform(MI)
        bmesh.ops.reverse_faces(self.bm, faces=self.bm.faces[:])   # MI 是鏡射，翻回法線
        me = bpy.data.meshes.new(name); self.bm.to_mesh(me); self.bm.free()
        for p in me.polygons: p.use_smooth = False
        o = bpy.data.objects.new(name, me); o.data.materials.append(mat(key))
        # 網格頂點已是 Unity 局部座標 × MI；物件變換另外給
        o2 = link(o, parent, Tu or T())
        # 頂點要放在物件局部空間：局部 Unity v → Blender 局部 MI v，物件 basis = MI Tu M → 世界 = MI Tu v  ✔
        return o2

def tris(objs):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs if o.type == 'MESH')

# ---------------- 共用外殼
WALL = 0.02; LID_T = 0.015; INSET = 0.015; GAP = 0.004

def shell(rt, W, D, H, louver_sides=False):
    g = G(); hw, hd = W / 2, D / 2
    g.box((0, 0.01, 0), (W, 0.02, D))                                   # 底板
    g.box((0, H / 2, hd - WALL / 2), (W, H, WALL)); g.box((0, H / 2, -hd + WALL / 2), (W, H, WALL))
    g.box((hw - WALL / 2, H / 2, 0), (WALL, H, D - 2 * WALL)); g.box((-hw + WALL / 2, H / 2, 0), (WALL, H, D - 2 * WALL))
    g.mesh('Body', 'body', rt)
    d = G()                                                              # Detail_Body：銘牌框、吊環、電纜頭、底部角座
    d.box((0, H * 0.55, hd + 0.003), (0.36, 0.12, 0.006))               # 銘牌板（字由 TextMesh 放）
    for sx in (-1, 1):
        for sz in (-1, 1):
            d.box((sx * (hw - 0.05), 0.03, sz * (hd + 0.006)), (0.06, 0.06, 0.012))       # 角座
            d.box((sx * (hw + 0.006), H - 0.05, sz * (hd - 0.08)), (0.012, 0.05, 0.05))   # 吊耳
    for i, x in enumerate((-0.18, -0.06, 0.06, 0.18)):
        d.cyl((x, 0.08, hd), (x, 0.08, hd + 0.03), 0.018, 8)           # 電纜固定頭
    if louver_sides:
        for sx in (-1, 1):
            for k in range(8):
                y = 0.07 + k * (H - 0.12) / 8
                d.box((sx * (hw + 0.008), y, 0), (0.012, 0.012, D * 0.7))  # 百葉
    d.mesh('Detail_Body', 'dark', rt)
    empty('Label_Name', rt, T((0, H * 0.55, hd + 0.007)))               # +Z 朝讀者
    return hw, hd

def lids(rt, W, D, H, n, kinds, low=(), perforated=False, struts=()):
    """n 片蓋板沿 X 排；kinds 每片 'W'＝鉸鏈在西緣、'E'＝東緣、'B'＝後緣（−Z）。回傳 [(名稱, 鉸鏈說明, 寬)]。"""
    hw, hd = W / 2, D / 2
    inner_w = W - 2 * INSET; pw = inner_w / n
    out = []
    for i in range(n):
        x0 = -hw + INSET + i * pw; x1 = x0 + pw; k = kinds[i]; name = f'Lid_{i+1}'
        Lz = D - 2 * INSET                                                # 沿 Z 的長度
        if k == 'W':    # 鉸鏈線 x=x0（沿 Z），本體往 +X → 局部 −Z = 世界 +X
            Tu = T((x0, H, 0), X=(0, 0, 1), Y=(0, 1, 0)); along, width = Lz, pw - GAP
        elif k == 'E':  # 鉸鏈線 x=x1，本體往 −X
            Tu = T((x1, H, 0), X=(0, 0, -1), Y=(0, 1, 0)); along, width = Lz, pw - GAP
        else:           # 後緣鉸鏈 z=−hd+INSET（沿 X），本體往 +Z（前）
            Tu = T(((x0 + x1) / 2, H, -hd + INSET), X=(-1, 0, 0), Y=(0, 1, 0)); along, width = pw - GAP, Lz
        g = G()
        g.box((0, LID_T / 2, -width / 2), (along, LID_T, width))          # 蓋板（局部 −Z 方向）
        g.box((0, LID_T + 0.004, -width / 2), (along - 0.06, 0.008, 0.03)) # 補強肋
        for j in range(4):                                                # 蓋板螺栓 4 顆（自由緣）
            a = -along / 2 + 0.06 + j * (along - 0.12) / 3
            g.cyl((a, LID_T, -width + 0.03), (a, LID_T + 0.008, -width + 0.03), 0.009, 6)
        lid = g.mesh(name, 'body', rt, Tu)
        h = G()                                                           # 把手：自由緣、靠正面那一端
        if k in 'WE':
            # 側片的局部 X 沿 Z：W 片 +X＝北（正面），E 片 −X＝北；把手放在靠正面那一端
            ax = along / 2 - 0.15 if k == 'W' else -(along / 2 - 0.15)
        else:
            ax = 0.0
        hz = -width + 0.06; hh = 0.012 if name in low else 0.035      # 開到 90° 會朝鄰箱的蓋板用矮把手
        h.cyl((ax - 0.07, LID_T, hz), (ax - 0.07, LID_T + hh, hz), 0.007, 6)
        h.cyl((ax + 0.07, LID_T, hz), (ax + 0.07, LID_T + hh, hz), 0.007, 6)
        h.cyl((ax - 0.075, LID_T + hh, hz), (ax + 0.075, LID_T + hh, hz), 0.008, 6)
        h.mesh(f'{name}_Handle', 'dark', lid)
        hg = G(); hl = G()                                                # 鉸鏈：固定座＋關節（不動，在根節點下）、蓋板側葉片（跟著蓋子）
        for j in (-1, 0, 1):
            a = j * (along / 2 - 0.12)
            hg.cyl((a - 0.035, 0, 0), (a + 0.035, 0, 0), 0.012, 10)          # 關節，軸心就在鉸鏈線上
            hg.box((a, -0.03, INSET + 0.0015), (0.07, 0.05, 0.003))          # 箱壁外側的固定葉片
            hg.box((a, -0.006, INSET / 2), (0.07, 0.012, INSET))            # 關節到箱壁的連接片
            hl.box((a, LID_T + 0.002, -0.04), (0.06, 0.004, 0.07))         # 蓋板上的葉片
            for dz in (-0.02, -0.06):
                hl.cyl((a, LID_T + 0.004, dz), (a, LID_T + 0.009, dz), 0.006, 6)
        hg.mesh(f'Hinge_{i+1}', 'metal', rt, Tu)
        hl.mesh(f'{name}_HingeLeaf', 'metal', lid)
        if perforated:                                                    # 網孔板外觀：凹格（深色方格貼在蓋板上 0.5 mm），不打洞
            gp = G(); y = LID_T + 0.0005; skip = 0.25 if name == 'Lid_2' else 0.0
            zs = [-0.06 - k * 0.04 for k in range(int((width - 0.12) / 0.04) + 1)]
            xs = [-along / 2 + 0.08 + k * 0.04 for k in range(int((along - 0.16) / 0.04) + 1)]
            for z in zs:
                if abs(z + width / 2) < 0.03: continue                   # 補強肋
                for x in xs:
                    if skip and abs(x) < skip and abs(z + width / 2) < skip: continue   # 警示牌位置
                    gp.quad((x, z), 0.018, 0.018, y)
            gp.mesh(f'{name}_Mesh', 'black', lid)
        if name in struts:                                                # 氣壓撐桿（外觀，掛在蓋板底下跟著轉；全開時下端落在鉸鏈附近）
            for j, xs_ in enumerate((-0.28, 0.28)):
                gs = G(); P = Vector((xs_, -0.008, -0.50)); B = Vector((xs_, -0.03, -0.06)); Mp = P.lerp(B, 0.55)
                gs.cyl(P, Mp, 0.012, 8); gs.cyl(Mp, B, 0.006, 8)
                gs.box(P + Vector((0, 0.004, 0)), (0.03, 0.012, 0.03)); gs.box(B, (0.02, 0.02, 0.02))
                gs.mesh(f'{name}_Strut_{j+1}', 'metal', lid)
        LIDOBJ[name] = (lid, k, width, along)
        out.append((name, {'W': '西緣（沿 Z）', 'E': '東緣（沿 Z）', 'B': '後緣（沿 X）'}[k], round(width, 3), round(along, 3)))
    return out

LIDOBJ = {}

# ---------------- 警示牌（黃底黑框三角＋黑色符號；下方黃色字牌，字用 Label 空節點給 TextMesh）
def sign(parent, prefix, o, u, v, n, s, symbol, label):
    o, u, v, n = Vector(o), Vector(u), Vector(v), Vector(n)
    gy = G(); gk = G(); h3 = s * math.sqrt(3)
    tri = lambda k: [(-s * k / 2, -h3 * k / 6), (s * k / 2, -h3 * k / 6), (0, h3 * k / 3)]
    gk.plate(tri(1.22), o, u, v, n, 0.002)                                # 黑框
    gy.plate(tri(1.0), o + n * 0.002, u, v, n, 0.002)                     # 黃底
    so = o + n * 0.004
    if symbol == 'bolt':                                                  # 閃電
        for poly in ([(0.05, 0.30), (-0.10, 0.03), (-0.02, 0.03), (0.13, 0.30)],
                     [(-0.10, 0.03), (0.08, 0.03), (0.08, -0.02), (-0.10, -0.02)],
                     [(0.01, -0.02), (0.09, -0.02), (-0.05, -0.22)]):
            gk.plate([(x * s, y * s) for x, y in poly], so, u, v, n, 0.002)
    else:                                                                 # 高溫：三條熱氣＋底線
        for x in (-0.10, 0.0, 0.10):
            gk.plate([(x * s - 0.012 * s, -0.12 * s), (x * s + 0.012 * s, -0.12 * s), (x * s + 0.012 * s, 0.18 * s), (x * s - 0.012 * s, 0.18 * s)], so, u, v, n, 0.002)
        gk.plate([(-0.18 * s, -0.20 * s), (0.18 * s, -0.20 * s), (0.18 * s, -0.16 * s), (-0.18 * s, -0.16 * s)], so, u, v, n, 0.002)
    pc = o - v * (h3 * 1.22 / 6 + 0.03 * s + 0.20 * s)                      # 字牌中心
    rect = lambda w, h: [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]
    gk.plate(rect(1.34 * s, 0.40 * s), pc, u, v, n, 0.002)
    gy.plate(rect(1.26 * s, 0.32 * s), pc + n * 0.002, u, v, n, 0.002)
    gy.mesh(f'{prefix}Sign', 'yellow', parent); gk.mesh(f'{prefix}Sign_Black', 'black', parent)
    lp = pc + n * 0.006
    empty(label, parent, T(lp, X=u, Y=v))                                 # 局部 +Z＝n 朝讀者

def signs(rt, W, D, H, symbol, lid_name):
    """正面一塊（靠東側，避開銘牌和電纜頭），指定蓋板上一塊。"""
    s = min(0.18, (H - 0.06) / 1.5)
    cy = 0.03 + s * 1.05
    sign(rt, 'Front_', (W / 2 - 0.6 * s - 0.06, cy, D / 2), (1, 0, 0), (0, 1, 0), (0, 0, 1), s, symbol, 'Label_Warn')
    lid, k, width, along = LIDOBJ[lid_name]
    # 蓋板頂面上：v（牌子的上方）朝世界 −Z（離開站在正面的學員），n＝蓋板局部 +Y
    vw = {'B': (0, 0, 1), 'E': (1, 0, 0), 'W': (-1, 0, 0)}[k]
    u = Vector(vw).cross(Vector((0, 1, 0)))
    sign(lid, f'{lid_name}_', (0, LID_T + 0.009, -width / 2 + 0.05), u, vw, (0, 1, 0), 0.22, symbol, f'{lid_name}_Label_Warn')

# ---------------- 內部
def cable(parent, name, pts, key, r=0.009):
    g = G(); g.tube(pts, r, 6); g.mesh(name, key, parent)

def term_block(parent, name, c, n=8):
    g = G()
    g.box(c, (0.03 * n + 0.02, 0.03, 0.05))
    for i in range(n):
        x = c[0] - 0.015 * (n - 1) + 0.03 * i
        g.box((x, c[1] + 0.02, c[2]), (0.022, 0.012, 0.04))
        g.cyl((x, c[1] + 0.026, c[2] + 0.01), (x, c[1] + 0.034, c[2] + 0.01), 0.006, 6)
    g.mesh(name, 'dark', parent)

def hvac(rt):
    W, D, H = 2.2, 1.9, 0.38
    shell(rt, W, D, H, louver_sides=True)
    L = lids(rt, W, D, H, 3, 'WBE', struts=('Lid_2',))
    ins = empty('Inside_1', rt)
    y0 = 0.02
    g = G()                                                               # 壓縮機 ×2
    for z in (-0.35, 0.25):
        g.cyl((-0.72, y0, z), (-0.72, y0 + 0.24, z), 0.11, 16)
        g.cyl((-0.72, y0 + 0.24, z), (-0.72, y0 + 0.30, z), 0.11, 16, r2=0.06)
        g.box((-0.72, y0 + 0.01, z), (0.30, 0.02, 0.30))
    g.mesh('Compressor', 'black', ins)
    g = G()                                                               # 冷凝器、蒸發器盤管（框＋鰭片）
    for x, zc, zl in ((0.55, 0.0, 1.6), (-0.15, 0.0, 1.5)):
        g.box((x, y0 + 0.15, zc), (0.06, 0.28, zl))
    g.mesh('Coil_Frame', 'dark', ins)
    g = G()
    for x, zl in ((0.55, 1.6), (-0.15, 1.5)):
        for k in range(10):
            g.box((x + 0.035, y0 + 0.15, -zl / 2 + 0.08 + k * (zl - 0.16) / 9), (0.01, 0.26, 0.004))
    g.mesh('Coil_Fins', 'metal', ins)
    g = G()                                                               # 冷凝風扇 ×2：風罩＋輪轂＋5 葉
    for z in (-0.45, 0.45):
        g.cyl((0.85, y0 + 0.24, z), (0.85, y0 + 0.30, z), 0.24, 20, caps=False)
        g.cyl((0.85, y0 + 0.25, z), (0.85, y0 + 0.31, z), 0.05, 10)
        for b in range(5):
            a = b * 2 * math.pi / 5
            c = Vector((0.85 + 0.13 * math.cos(a), y0 + 0.28, z + 0.13 * math.sin(a)))
            t = bmesh.new(); bmesh.ops.create_cube(t, size=1.0)
            t.transform(Matrix.Translation(c) @ Matrix.Rotation(-a, 4, 'Y') @ Matrix.Rotation(0.35, 4, 'X') @ Matrix.Diagonal((0.17, 0.006, 0.07, 1)))
            me = bpy.data.meshes.new('t'); t.to_mesh(me); t.free(); g.bm.from_mesh(me); bpy.data.meshes.remove(me)
        g.cyl((0.85, y0, z), (0.85, y0 + 0.24, z), 0.03, 8)              # 馬達柱
    g.mesh('CondenserFan', 'dark', ins)
    g = G()                                                               # 送風機（渦殼）
    g.cyl((-0.45, y0 + 0.16, -0.30), (-0.45, y0 + 0.16, 0.30), 0.14, 16)
    g.box((-0.45, y0 + 0.24, 0), (0.18, 0.12, 0.40))
    g.mesh('Blower', 'metal', ins)
    g = G()                                                               # 控制箱＋斷路器＋接觸器
    g.box((-0.85, y0 + 0.15, 0.68), (0.36, 0.30, 0.28))
    g.mesh('ControlBox', 'body', ins)
    g = G()
    for i in range(4): g.box((-0.97 + i * 0.07, y0 + 0.32, 0.72), (0.05, 0.05, 0.10))
    for i in range(2): g.box((-0.72 + i * 0.0, y0 + 0.32, 0.62 + i * 0.12), (0.08, 0.06, 0.08))
    g.mesh('Breakers', 'dark', ins)
    term_block(ins, 'TerminalBlock', (-0.85, y0 + 0.31, 0.58), 8)
    g = G()                                                               # 銅管 ×10
    for z in (-0.35, 0.25):
        g.tube([(-0.62, y0 + 0.22, z), (-0.40, y0 + 0.22, z), (-0.40, y0 + 0.28, z * 0.5), (0.50, y0 + 0.28, z * 0.5)], 0.009)
        g.tube([(-0.72, y0 + 0.30, z), (-0.72, y0 + 0.33, z), (-0.20, y0 + 0.33, z)], 0.009)
        g.tube([(-0.82, y0 + 0.06, z), (-0.95, y0 + 0.06, z), (-0.95, y0 + 0.06, z - 0.15)], 0.009)
    g.tube([(0.50, y0 + 0.05, -0.70), (-0.10, y0 + 0.05, -0.70)], 0.009)
    g.tube([(0.50, y0 + 0.05, 0.70), (-0.10, y0 + 0.05, 0.70)], 0.009)
    g.tube([(-0.15, y0 + 0.30, -0.75), (-0.60, y0 + 0.30, -0.75), (-0.60, y0 + 0.30, -0.50)], 0.009)
    g.tube([(-0.15, y0 + 0.30, 0.75), (-0.60, y0 + 0.30, 0.75), (-0.60, y0 + 0.30, 0.40)], 0.009)
    g.mesh('CopperPipe', 'copper', ins)
    for i, (key, tgt) in enumerate([('red', (-0.72, 0.20, -0.35)), ('black', (-0.72, 0.18, -0.30)), ('red', (-0.72, 0.20, 0.25)),
                                     ('black', (-0.72, 0.18, 0.30)), ('red', (0.85, 0.12, -0.45)), ('black', (0.85, 0.10, -0.40)),
                                     ('red', (0.85, 0.12, 0.45)), ('black', (0.85, 0.10, 0.40)), ('red', (-0.45, 0.10, -0.30)),
                                     ('green', (-1.05, 0.04, 0.40))]):
        s = (-0.95 + 0.02 * i, y0 + 0.05, 0.54)
        cable(ins, f'Cable_{i+1:02d}', [s, (s[0], 0.06, tgt[2] * 0.3 + 0.2), (tgt[0], 0.06, tgt[2]), tgt], key, 0.008)
    return W, D, H, L

def resistor(rt):
    W, D, H = 1.5, 1.6, 0.30
    shell(rt, W, D, H, louver_sides=True)
    L = lids(rt, W, D, H, 2, 'WE', low=('Lid_1',), perforated=True)
    signs(rt, W, D, H, 'heat', 'Lid_2')
    ins = empty('Inside_1', rt); y0 = 0.02
    g = G(); gi = G(); gb = G()
    for b in range(4):
        x = -0.54 + b * 0.36
        gb.box((x, y0 + 0.02, 0), (0.30, 0.02, 1.36))                     # 支架
        for sx in (-1, 1):                                                # 上方只留兩條側軌，看得到元件
            gb.box((x + sx * 0.14, y0 + 0.23, 0), (0.02, 0.02, 1.36))
        for k in range(10):                                               # 電阻元件（波浪帶簡化成薄片）
            z = -0.60 + k * 0.1333
            g.box((x, y0 + 0.125, z), (0.24, 0.18, 0.004))
            g.box((x, y0 + 0.125, z + 0.02), (0.24, 0.004, 0.04))
        for k in range(10):                                               # 絕緣礙子（上下各一排，共 20／排架 → 40 個）
            z = -0.62 + k * 0.137
            gi.cyl((x - 0.13, y0 + 0.03, z), (x - 0.13, y0 + 0.07, z), 0.012, 6)
            gi.cyl((x + 0.13, y0 + 0.18, z), (x + 0.13, y0 + 0.22, z), 0.012, 6)
    g.mesh('ResistorGrid', 'metal', ins); gi.mesh('Insulator', 'white', ins); gb.mesh('GridFrame', 'dark', ins)
    g = G()                                                               # 匯流排
    for b in range(4):
        x = -0.54 + b * 0.36
        g.box((x, y0 + 0.25, 0.70), (0.30, 0.01, 0.04))
    g.box((0, y0 + 0.25, 0.70), (1.12, 0.008, 0.03))
    g.mesh('Busbar', 'copper', ins)
    term_block(ins, 'TerminalBlock', (0, y0 + 0.03, 0.74), 6)
    for i, key in enumerate(['red', 'black', 'red', 'black', 'red', 'green']):
        x = -0.075 + i * 0.03
        cable(ins, f'Cable_{i+1:02d}', [(x, y0 + 0.07, 0.74), (x, y0 + 0.07, 0.78), (x * 3, y0 + 0.07, 0.78)], key, 0.008)
    return W, D, H, L

def battery(rt):
    W, D, H = 1.8, 1.0, 0.74
    shell(rt, W, D, H)
    L = lids(rt, W, D, H, 3, 'WBE', low=('Lid_3',))
    signs(rt, W, D, H, 'bolt', 'Lid_2')
    ins = empty('Inside_1', rt); y0 = 0.02
    gm = G(); gt = G(); gl = G(); gc = G(); gt2 = G()
    for layer in range(2):
        yb = y0 + 0.02 + layer * 0.33
        gm.box((0, yb - 0.01, -0.05), (1.62, 0.02, 0.72))                  # 層板
        for ix in range(6):
            for iz in range(2):
                x = -0.65 + ix * 0.26; z = -0.23 + iz * 0.36
                gm.box((x, yb + 0.15, z), (0.24, 0.29, 0.32))
                for s in (-1, 1):
                    gt.cyl((x + s * 0.07, yb + 0.30, z), (x + s * 0.07, yb + 0.31, z), 0.012, 6)
                    (gc if s > 0 else gt2).cyl((x + s * 0.07, yb + 0.30, z), (x + s * 0.07, yb + 0.335, z), 0.019, 8, r2=0.014)  # 絕緣端子蓋
                if ix < 5:
                    gl.box((x + 0.13, yb + 0.322, z), (0.14, 0.006, 0.025))  # 模組間連接排
    gm.mesh('BatteryModule', 'dark', ins); gt.mesh('ModuleTerminal', 'metal', ins); gl.mesh('ModuleLink', 'copper', ins)
    gc.mesh('TerminalCap_Pos', 'red', ins); gt2.mesh('TerminalCap_Neg', 'black', ins)                # 正極紅、負極黑
    g = G()
    g.box((0.78, y0 + 0.50, 0.33), (0.18, 0.14, 0.22))                     # BMS
    g.mesh('BMS', 'body', ins)
    g = G()
    g.box((0, y0 + 0.66, -0.40), (1.66, 0.06, 0.12))                      # 冷卻風道（後側上方）
    for k in range(6): g.box((-0.65 + k * 0.26, y0 + 0.62, -0.40), (0.10, 0.02, 0.10))
    g.mesh('CoolingDuct', 'metal', ins)
    for i, key in enumerate(['red', 'black', 'red', 'black']):
        z = 0.15 + 0.05 * i
        cable(ins, f'Cable_{i+1:02d}', [(-0.70, y0 + 0.66, -0.23 if i % 2 else 0.13), (-0.70, y0 + 0.68, z), (0.70, y0 + 0.68, z), (0.80, y0 + 0.58, 0.30)], key, 0.011)
    term_block(ins, 'TerminalBlock', (0.78, y0 + 0.58, 0.44), 6)
    return W, D, H, L

def converter(rt):
    W, D, H = 1.7, 1.6, 0.45
    shell(rt, W, D, H, louver_sides=True)
    L = lids(rt, W, D, H, 3, 'WBE')
    signs(rt, W, D, H, 'bolt', 'Lid_2')
    ins = empty('Inside_1', rt); y0 = 0.02
    g = G(); gf = G()
    for zc in (-0.35, 0.25):                                               # 散熱片 ×2（底板＋20 片鰭）
        g.box((-0.25, y0 + 0.17, zc), (0.90, 0.02, 0.50))
        for k in range(20):
            gf.box((-0.25 - 0.43 + k * 0.045, y0 + 0.08, zc), (0.004, 0.16, 0.48))
    g.mesh('HeatsinkBase', 'metal', ins); gf.mesh('HeatsinkFins', 'metal', ins)
    g = G()
    for zc in (-0.35, 0.25):                                               # IGBT 模組 ×6
        for k in range(3):
            g.box((-0.55 + k * 0.30, y0 + 0.205, zc), (0.14, 0.05, 0.19))
    g.mesh('IGBT', 'dark', ins)
    g = G()
    for k in range(6):                                                     # 電容 ×6
        g.cyl((0.50 + (k % 2) * 0.17, y0, -0.45 + (k // 2) * 0.32), (0.50 + (k % 2) * 0.17, y0 + 0.30, -0.45 + (k // 2) * 0.32), 0.07, 12)
    g.mesh('Capacitor', 'body', ins)
    g = G()                                                                # 匯流排
    for zc in (-0.35, 0.25):
        g.box((-0.25, y0 + 0.25, zc - 0.05), (0.90, 0.006, 0.04)); g.box((-0.25, y0 + 0.26, zc + 0.05), (0.90, 0.006, 0.04))
    g.box((0.58, y0 + 0.33, -0.13), (0.06, 0.006, 0.90))
    g.mesh('Busbar', 'copper', ins)
    g = G()
    g.box((-0.62, y0 + 0.17, 0.62), (0.30, 0.30, 0.20))                    # 控制模組
    g.box((0.30, y0 + 0.12, 0.62), (0.18, 0.20, 0.14))                     # 接觸器
    g.mesh('ControlModule', 'body', ins)
    g = G()
    g.cyl((0.0, y0 + 0.10, 0.62), (0.0, y0 + 0.10, 0.70), 0.12, 16, caps=False)  # 冷卻風扇
    g.cyl((0.0, y0 + 0.10, 0.62), (0.0, y0 + 0.10, 0.68), 0.04, 10)
    for b in range(5):
        a = b * 2 * math.pi / 5
        g.box((0.07 * math.cos(a), y0 + 0.10 + 0.07 * math.sin(a), 0.65), (0.09, 0.03, 0.004))
    g.mesh('CoolingFan', 'dark', ins)
    term_block(ins, 'TerminalBlock', (-0.25, y0 + 0.03, 0.70), 8)
    for i in range(12):
        key = ['red', 'black', 'red', 'black', 'red', 'black', 'red', 'black', 'red', 'black', 'green', 'green'][i]
        zc = -0.35 if i < 6 else 0.25
        x = -0.55 + (i % 3) * 0.30
        cable(ins, f'Cable_{i+1:02d}', [(x, y0 + 0.235, zc + (0.06 if i % 2 else -0.06)), (x, y0 + 0.30, zc + 0.10), (x * 0.4 - 0.25, y0 + 0.30, 0.55), (-0.355 + 0.06 * (i % 8), y0 + 0.07, 0.70)], key, 0.008)
    return W, D, H, L

# ---------------- 鋼架（黃色，外框和箱子一樣大，原點在地面中心）
def stand(rt, W, D, Hs):
    t = 0.05; hw, hd = W / 2 - t / 2, D / 2 - t / 2
    g = G()
    g.box((0, Hs - t / 2, hd), (W, t, t)); g.box((0, Hs - t / 2, -hd), (W, t, t))
    g.box((hw, Hs - t / 2, 0), (t, t, D - 2 * t)); g.box((-hw, Hs - t / 2, 0), (t, t, D - 2 * t))
    g.box((0, Hs - t / 2, 0), (W - 2 * t, t * 0.8, t * 0.8))               # 中間橫樑
    g.mesh('Stand_Frame', 'yellow', rt)
    for i, (sx, sz) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        g = G()
        g.box((sx * hw, (Hs - t) / 2, sz * hd), (t, Hs - t, t))
        g.box((sx * hw, 0.004, sz * hd), (0.10, 0.008, 0.10))              # 腳墊
        g.mesh(f'Stand_Leg_{i+1}', 'yellow', rt)
    g = G()
    yb = min(0.15, Hs * 0.35)
    g.box((0, yb, hd), (W - 2 * t, t * 0.6, t * 0.6)); g.box((0, yb, -hd), (W - 2 * t, t * 0.6, t * 0.6))
    g.box((hw, yb, 0), (t * 0.6, t * 0.6, D - 2 * t)); g.box((-hw, yb, 0), (t * 0.6, t * 0.6, D - 2 * t))
    g.mesh('Stand_Brace', 'yellow', rt)

# ---------------- 匯出（tool-pouch v1.2 做法）
CONV = axis_conversion(to_forward='-Z', to_up='Y').to_4x4()

def _conj(objs, rt, C):
    Ci = C.inverted()
    for o in objs:
        if o.type == 'MESH': o.data.transform(C)
        if o is rt: continue
        o.matrix_parent_inverse = C @ o.matrix_parent_inverse @ Ci
        o.matrix_basis = C @ o.matrix_basis @ Ci

def to_fixed(objs):
    used = {}
    for o in objs:
        if o.type != 'MESH': continue
        key = o.data.materials[0].name[3:]
        fname, cell = FIXED[key]
        m = bpy.data.materials.get(fname) or bpy.data.materials.new(fname)
        o.data.materials[0] = m
        uv = cell_uv(cell)
        lay = o.data.uv_layers.get('UVMap') or o.data.uv_layers.new(name='UVMap')
        for d in lay.data: d.uv = uv
        used[o.name] = [fname, cell]
    return used

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

def unity_local(o):
    """節點在 Unity 裡的局部位置與局部 X/Y/Z 軸（以父節點為準）。"""
    m = M @ o.matrix_basis @ MI
    r = lambda v: [round(x, 4) + 0.0 for x in v]
    return {'pos': r(m.translation), 'X': r(m.col[0][:3]), 'Y': r(m.col[1][:3]), 'Z': r(m.col[2][:3])}

def bbox_unity(objs):
    pts = [M @ (o.matrix_world @ v.co) for o in objs if o.type == 'MESH' for v in o.data.vertices]
    return [round(min(p[i] for p in pts), 4) for i in range(3)], [round(max(p[i] for p in pts), 4) for i in range(3)]

# ---------------- 預覽
def render(name, objs, open_deg):
    sc = bpy.context.scene
    # 依 Unity 規則打開蓋子：局部 +X 正轉 open_deg（Unity 的 X 旋轉矩陣）
    saved = {}
    for o in objs:
        if o.name.startswith('Lid_') and not o.name.endswith('Handle'):
            saved[o.name] = o.matrix_basis.copy()
            deg = open_deg.get(o.name, 100)
            Ru = Matrix.Rotation(math.radians(deg), 4, 'X')   # 標準 X 旋轉矩陣（Unity 與數學同式）
            o.matrix_basis = MI @ ((M @ o.matrix_basis @ MI) @ Ru) @ M
    bpy.context.view_layer.update()
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = SAMPLES; sc.cycles.use_denoising = False
    sc.render.resolution_x, sc.render.resolution_y = 1400, 1000
    sc.view_settings.view_transform = 'Standard'
    if sc.world is None:
        w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True
        w.node_tree.nodes['Background'].inputs[0].default_value = (0.75, 0.77, 0.80, 1)
        w.node_tree.nodes['Background'].inputs[1].default_value = 0.6
        sun = bpy.data.lights.new('Sun', 'SUN'); sun.energy = 2.5
        so = bpy.data.objects.new('Sun', sun); sc.collection.objects.link(so)
        so.rotation_euler = (math.radians(40), math.radians(15), math.radians(35))
        fl = bpy.data.meshes.new('Floor'); bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=6); bm.to_mesh(fl); bm.free()
        fo = bpy.data.objects.new('PREVIEW_Floor', fl); sc.collection.objects.link(fo); fo.data.materials.append(mat('body'))
    for m in bpy.data.materials:
        if m.name.startswith('PV_'):
            m.use_nodes = True; b = m.node_tree.nodes.get('Principled BSDF')
            if b: b.inputs['Base Color'].default_value = m.diffuse_color; b.inputs['Roughness'].default_value = 0.55
    lo, hi = bbox_unity(objs)
    c = MI @ Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2))
    size = max(hi[0] - lo[0], hi[2] - lo[2], hi[1] - lo[1])
    cam = bpy.data.cameras.new('C'); co = bpy.data.objects.new('Cam', cam); sc.collection.objects.link(co)
    # 從正面（Unity +Z、偏東、偏高）看：Unity 方向 (0.55, 0.75, 1.0)
    d = (MI @ Vector((0.55, 0.85, 1.0))).normalized() * size * 2.1
    co.location = c + d; co.rotation_euler = (c - co.location).to_track_quat('-Z', 'Y').to_euler()
    cam.lens = 40; sc.camera = co
    sc.render.filepath = os.path.join(OUT, f'{name}.png'); bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co, do_unlink=True)
    for n, mb in saved.items(): bpy.data.objects[n].matrix_basis = mb
    bpy.context.view_layer.update()

# ---------------- 主程式
KINDS = [('HVAC', hvac, 0.6), ('Resistor', resistor, 0.6), ('Battery', battery, 0.4), ('Converter', converter, 0.6)]
# 相鄰會撞的蓋板最大 90°（training-boxes 第 8 節；電池箱東側、電阻箱西側）
OVERRIDE = {'Battery': {'Lid_3': 90}, 'Resistor': {'Lid_1': 90}}

if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    report = {'script': 'gen_roof_boxes v1.1', 'blender': bpy.app.version_string,
              'coords': 'Unity (m)；原點在底面中心；正面 +Z；Lid_n 原點在鉸鏈線上、局部 +X 沿鉸鏈、繞 +X 正轉＝打開',
              'paletteOrder': ORDER_SRC, 'paletteRule': '格 i：UV ((i%8+0.5)/8, (i//8+0.5)/8)，和 gen_depot_props v1.0 相同', 'files': {}}
    total = 0
    for kind, fn, hs in KINDS:
        for o in list(bpy.data.objects):
            if not o.name.startswith('PREVIEW_') and o.type != 'LIGHT': bpy.data.objects.remove(o, do_unlink=True)
        rt = empty(f'ENV_RoofBox_{kind}', None)
        W, D, H, L = fn(rt)
        objs = [rt] + list(rt.children_recursive)
        bpy.context.view_layer.update()
        n = tris(objs); total += n
        st = empty(f'ENV_TrainingBoxStand_{kind}', None, T((0, 0, 0)))
        stand(st, W, D, hs)
        sobjs = [st] + list(st.children_recursive)
        ns = tris(sobjs); total += ns
        if DO_RENDER:
            rt.matrix_basis = MI @ T((0, hs, 0)) @ M; bpy.context.view_layer.update()
            render(f'preview_{kind}_open', objs + sobjs, OVERRIDE.get(kind, {}))
            render(f'preview_{kind}_closed', objs + sobjs, {o.name: 0 for o in objs if o.name.startswith('Lid_')})
            rt.matrix_basis = Matrix.Identity(4); bpy.context.view_layer.update()
        lo, hi = bbox_unity(objs)
        entry = {'tris': n, 'outer': [W, D, H], 'bboxMin': lo, 'bboxMax': hi,
                 'lids': [{'node': a, 'hinge': b, 'widthAcrossHinge': c, 'lengthAlongHinge': d,
                           'maxOpenDeg': OVERRIDE.get(kind, {}).get(a, 100)} for a, b, c, d in L],
                 'nodes': {o.name: unity_local(o) for o in objs if o is not rt and (o.type == 'EMPTY' or o.name.startswith(('Lid_', 'Hinge_', 'Body', 'Detail_')))},
                 'meshCount': len([o for o in objs if o.type == 'MESH'])}
        sentry = {'tris': ns, 'height': hs, 'outer': [W, D, hs], 'nodes': [o.name for o in sobjs if o is not st]}
        if DO_FBX:
            entry['meshMaterial'] = to_fixed(objs); entry['materials'] = sorted({v[0] for v in entry['meshMaterial'].values()})
            sentry['meshMaterial'] = to_fixed(sobjs); sentry['materials'] = sorted({v[0] for v in sentry['meshMaterial'].values()})
            export_fbx(rt, os.path.join(OUT, f'ENV_RoofBox_{kind}.fbx'))
            export_fbx(st, os.path.join(OUT, f'ENV_TrainingBoxStand_{kind}.fbx'))
        report['files'][f'ENV_RoofBox_{kind}.fbx'] = entry
        report['files'][f'ENV_TrainingBoxStand_{kind}.fbx'] = sentry
    report['totalTris'] = total
    json.dump(report, open(os.path.join(OUT, 'RoofBoxReport.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print('TOTAL', total, {k: v['tris'] for k, v in report['files'].items()})
