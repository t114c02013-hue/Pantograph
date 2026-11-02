"""地面與平台的設備外觀：工作檯、工具架、長桿桿架（D25）、2.5 m 伸縮絕緣桿（驗電器／接地棒的外觀）、遙控器掛座、備品架、零件盒（待報廢紅色）。"""
from common import box, cylinder, empty_mesh


def workbench(ctx):
    top = box(ctx, "ENV_WorkBench", (1.5, 0.6, 0.04), (0, 0, 0.88), "Metal", "steel")
    for sx in (-0.7, 0.7):
        for sy in (-0.26, 0.26):
            box(ctx, "Leg", (0.05, 0.05, 0.86), (sx, sy, -0.45), "Metal", "dark", parent=top)
    box(ctx, "Shelf", (1.4, 0.5, 0.03), (0, 0, -0.6), "Metal", "steel", parent=top)
    box(ctx, "Clipboard", (0.23, 0.32, 0.01), (-0.5, 0, 0.025), "Paint", "white", parent=top)
    return top


def tool_rack(ctx):
    board = box(ctx, "ENV_ToolRack", (0.05, 1.2, 0.7), (0, 0, 1.3), "Paint", "blue")
    for k in range(4):
        box(ctx, f"Hook_{k + 1}", (0.12, 0.02, 0.02), (0.08, -0.45 + k * 0.3, 0.2), "Metal", "metal", parent=board)
    for sy in (-0.55, 0.55):
        box(ctx, "Post", (0.05, 0.05, 1.6), (0, sy, -0.5), "Metal", "steel", parent=board)
    return board


def pole_rack(ctx):
    base = box(ctx, "ENV_PoleRack", (0.30, 0.50, 0.06), (0, 0, 0.03), "Metal", "steel")
    box(ctx, "Upright", (0.05, 0.05, 1.6), (0, 0, 0.8), "Metal", "steel", parent=base)
    for k, h in enumerate((0.9, 1.5)):
        box(ctx, f"Cradle_{k + 1}", (0.12, 0.45, 0.04), (0.05, 0, h), "Paint", "yellow", parent=base)
    for k, sy in enumerate((-0.12, 0.12)):
        empty_mesh(ctx, f"PoleSlot_{k + 1}", (0.08, sy, 0.05), parent=base)
    return base


def insulated_pole(ctx, length=2.5):
    """伸縮絕緣桿（EX 2.5 m）＋護環；原點在桿底，沿 +Z。"""
    pole = cylinder(ctx, "ENV_InsulatedPole", 0.018, length, (0, 0, length / 2), segments=10, mat="Insulator", color="orangesafety")
    cylinder(ctx, "GuardRing", 0.045, 0.02, (0, 0, -length / 2 + 0.9), segments=12, mat="Paint", color="red", parent=pole)
    cylinder(ctx, "Grip", 0.022, 0.6, (0, 0, -length / 2 + 0.35), segments=10, mat="Dark", color="rubber", parent=pole)
    empty_mesh(ctx, "Tip", (0, 0, length / 2), parent=pole)
    return pole


def pendant_dock(ctx):
    plate = box(ctx, "ENV_PendantDock", (0.04, 0.20, 0.30), (0, 0, 1.10), "Paint", "craneyellow")
    box(ctx, "Hook", (0.06, 0.03, 0.03), (0.04, 0, 0.12), "Metal", "metal", parent=plate)
    return plate


def spare_shelf(ctx):
    top = box(ctx, "ENV_SpareShelf", (1.25, 0.40, 0.03), (0, 0, 0.75), "Metal", "steel")
    for sx in (-0.6, 0.6):
        box(ctx, "Leg", (0.04, 0.38, 0.75), (sx, 0, -0.375), "Metal", "dark", parent=top)
    return top


def parts_tray(ctx):
    tray = box(ctx, "ENV_PartsTray", (0.40, 0.30, 0.10), (0, 0, 0.05), "Paint", "red")   # 待報廢：紅色
    box(ctx, "Label_Scrap", (0.20, 0.005, 0.05), (0, -0.153, 0.0), "Paint", "white", parent=tray)
    return tray


def build(ctx, p):
    return [("ENV_WorkBench", workbench(ctx)), ("ENV_ToolRack", tool_rack(ctx)), ("ENV_PoleRack", pole_rack(ctx)),
            ("ENV_InsulatedPole", insulated_pole(ctx, p.get("insulatedPoleLength", 2.5))), ("ENV_PendantDock", pendant_dock(ctx)), ("ENV_SpareShelf", spare_shelf(ctx)),
            ("ENV_PartsTray", parts_tray(ctx))]


def build_roof_strip_mat(ctx, p):
    """a6 增補 2：車頂暫放墊 ENV_RoofStripMat（一個網格；墊體倒角 3 mm＋四邊邊框）。尺寸讀 car_params.json 的 roofStripMat；原點在底面中心。"""
    import bmesh
    from common import add_box, mesh_object
    q = p.get("roofStripMat", {})
    sx, sz = q.get("size", [1.20, 0.30])                 # Unity X × Z
    th, rw, rh = q.get("thickness", 0.01), q.get("rimWidth", 0.02), q.get("rimHeight", 0.02)
    bm = bmesh.new()
    add_box(bm, (sx - 2 * rw, sz - 2 * rw, th), (0, 0, th / 2), bevel=0.003)
    top = th + rh
    for s in (-1, 1):
        add_box(bm, (sx, rw, top), (0, s * (sz / 2 - rw / 2), top / 2))
        add_box(bm, (rw, sz - 2 * rw, top), (s * (sx / 2 - rw / 2), 0, top / 2))
    return mesh_object(ctx, "ENV_RoofStripMat", bm, "Dark", "rubberdarkgrey")


_build_furniture = build


def build(ctx, p):   # noqa: F811
    return _build_furniture(ctx, p) + [("ENV_RoofStripMat", build_roof_strip_mat(ctx, p))]
