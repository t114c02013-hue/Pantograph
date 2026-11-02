"""ENV_PlatformKit：檢修平台的外觀零件（Unity 依 RoofSceneDef 排列；可走面與碰撞由建置腳本用 primitive 產生）。
格柵板 1 m × 1 m、護欄柱、護欄橫桿（單位長 1 m）、活動欄、跨板、鋼梯（只做外觀）。"""
from common import box, cylinder


def build(ctx, p):
    gh = p["guardrailHeight"]
    root = box(ctx, "ENV_PlatformKit", (0.02, 0.02, 0.02), (0, 0, 0), "Dark", "dark")
    box(ctx, "Grating_1m", (1.0, 1.0, 0.05), (0, 0, -0.025), "Walkway", "walkway", parent=root)
    cylinder(ctx, "Rail_Post", 0.025, gh, (0, 0, gh / 2), segments=8, mat="Paint", color="yellow", parent=root)
    cylinder(ctx, "Rail_Bar_1m", 0.02, 1.0, (0, 0, gh), axis="Y", segments=8, mat="Paint", color="yellow", parent=root)
    box(ctx, "Toe_Board_1m", (0.01, 1.0, 0.15), (0, 0, 0.075), "Paint", "yellow", parent=root)
    gate = box(ctx, "Gate_Leaf", (0.04, p["gateWidth"], 0.04), (0, 0, gh), "Paint", "orangesafety", parent=root)
    box(ctx, "Gate_Leaf_Mid", (0.04, p["gateWidth"], 0.04), (0, 0, -gh / 2), "Paint", "orangesafety", parent=gate)
    box(ctx, "Gate_Bridge", (1.0, p["gateWidth"], 0.04), (0, 0, -0.02), "Walkway", "walkway", parent=root)
    stair = box(ctx, "Stair_Stringer", (0.06, 4.6, 0.25), (0, 0, 0), "Metal", "steel", parent=root)
    for k in range(12):
        box(ctx, f"Stair_Tread_{k + 1}", (0.9, 0.26, 0.04), (0.45, -2.2 + k * 0.38, -1.7 + k * 0.3), "Walkway", "walkway", parent=stair)
    return [("ENV_PlatformKit", root)]


def build_gate_slice_guard(ctx, p, width):
    """a6 增補 7 第 8 點（建模 A model-spec v3.3 §4）：活動欄開口側的擋欄 ENV_GateSliceGuard（只出外觀，collider 由 Unity 的方塊負責）。
    兩支端柱＋上橫桿（頂 1.0 m）＋中橫桿（0.5 m）＋踢腳板（高 0.10、厚 0.01）；0.04 m 方管；寬度＝擋牆範圍（不足 0.3 m 只做一支柱＋短橫桿）。
    黃黑相間：每段 0.10 m 交替指黃／黑色格（材質沿用 Paint，不新增材質；斜條用水平色段近似）；踢腳板純黃。原點在底面中心，長軸沿 Unity Z（Blender Y）。"""
    import bmesh
    from common import add_box, mesh_object
    H, mid, kick, t = 1.0, 0.5, 0.10, 0.04
    root = box(ctx, "ENV_GateSliceGuard", (0.12, 0.12, 0.01), (0, 0, 0.005), "Paint", "dark")   # 立柱底座 0.12 × 0.12 × 0.01（夾在車頂邊上）
    posts = [0.0] if width < 0.3 else [-width / 2 + t / 2, width / 2 - t / 2]

    def striped(name, size, center, axis):
        """沿 axis（0＝X、1＝Y、2＝Z）每 0.10 m 換色的方管：分段建網格，黃、黑各一個物件。"""
        n = max(1, int(round(size[axis] / 0.10)))
        bms = {"yellow": bmesh.new(), "dark": bmesh.new()}
        for k in range(n):
            seg = list(size); seg[axis] = size[axis] / n
            c = list(center); c[axis] = center[axis] - size[axis] / 2 + seg[axis] * (k + 0.5)
            add_box(bms["yellow" if k % 2 == 0 else "dark"], seg, c)
        for color, bm in bms.items():
            if bm.verts:
                mesh_object(ctx, f"{name}_{'Y' if color == 'yellow' else 'K'}", bm, "Paint", color, parent=root)
            else:
                bm.free()

    for i, y in enumerate(posts):
        striped(f"Post_{i + 1}", (t, t, H - 0.01), (0, y, 0.01 + (H - 0.01) / 2), 2)
    span = max(width, t)
    striped("TopRail", (t, span, t), (0, 0, H - t / 2), 1)
    striped("MidRail", (t, span, t), (0, 0, mid), 1)
    bm = bmesh.new()
    add_box(bm, (0.01, span, kick), (0, 0, 0.01 + kick / 2))
    mesh_object(ctx, "KickBoard", bm, "Paint", "yellow", parent=root)
    return root


_build_platform = build


def build(ctx, p):   # noqa: F811
    w = p.get("gateSliceGuard", {}).get("width", 0.0)
    return _build_platform(ctx, p) + ([("ENV_GateSliceGuard", build_gate_slice_guard(ctx, p, w))] if w > 0.001 else [])
