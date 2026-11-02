"""ENV_RoofEquipment：車頂設備零件（Unity 依 RoofSceneDef 擺放）：絕緣子、母線段、避雷器、車體接地座、高壓電纜端、控制線接頭。"""
from common import box, cylinder, empty_mesh


def insulator(ctx, name, h, parent=None):
    core = cylinder(ctx, name, 0.045, h, (0, 0, h / 2), segments=12, mat="Insulator", color="insulator", parent=parent)
    for k in range(4):   # 傘裙
        cylinder(ctx, f"{name}_Shed_{k + 1}", 0.085, 0.012, (0, 0, -h / 2 + h * (k + 1) / 5), segments=12, mat="Insulator", color="insulator", parent=core)
    empty_mesh(ctx, f"{name}_Top", (0, 0, h / 2), parent=core)
    return core


def build(ctx, p):
    root = box(ctx, "ENV_RoofEquipment", (0.02, 0.02, 0.02), (0, 0, 0), "Dark", "dark")
    insulator(ctx, "Insulator", p["insulatorHeight"], parent=root)
    bus = box(ctx, "Busbar", (0.08, 1.0, 0.06), (0, 0, 0.12), "Metal", "copper", parent=root)   # 單位長 1 m，Unity 依長度縮放
    for k, y in enumerate((-0.4, 0.4)):
        insulator(ctx, f"Busbar_Support_{k + 1}", 0.09, parent=bus).location.y = y
    arr = cylinder(ctx, "Arrester", 0.07, 0.55, (0, 0, 0.275), segments=12, mat="Insulator", color="insulator", parent=root)
    cylinder(ctx, "Arrester_Cap", 0.05, 0.05, (0, 0, 0.3), segments=12, mat="Metal", color="metal", parent=arr)
    seat = box(ctx, "Roof_CarBodyGround", (0.16, 0.16, 0.05), (0, 0, 0.025), "Metal", "steel", parent=root)
    box(ctx, "Clamp_Point", (0.04, 0.04, 0.06), (0, 0, 0.055), "Metal", "yellow", parent=seat)
    hv = box(ctx, "HV_Cable_End", (0.14, 0.14, 0.18), (0, 0, 0.09), "Dark", "dark", parent=root)
    cylinder(ctx, "HV_Cable_Connected", 0.025, 0.8, (0, 0.4, 0.0), axis="Y", segments=8, mat="Dark", color="rubber", parent=hv)
    cylinder(ctx, "HV_Cable_Parked", 0.025, 0.8, (0.3, 0.2, -0.05), axis="Y", segments=8, mat="Dark", color="rubber", parent=hv)
    ctrl = box(ctx, "CtrlCable_End", (0.08, 0.10, 0.08), (0, 0, 0.04), "Dark", "dark", parent=root)
    cylinder(ctx, "CtrlCable_Connected", 0.012, 0.6, (0, 0.3, 0.0), axis="Y", segments=8, mat="Dark", color="rubber", parent=ctrl)
    cylinder(ctx, "CtrlCable_Parked", 0.012, 0.6, (0.15, 0.2, -0.03), axis="Y", segments=8, mat="Dark", color="rubber", parent=ctrl)
    return [("ENV_RoofEquipment", root)]


# ---------- a6 增補 2（建模 A）：控制線接頭、降弓固定帶。尺寸寫 Blender 軸（X＝Unity X、Y＝Unity Z、Z＝Unity Y），原點在底面中心 ----------
import bmesh  # noqa: E402
import math  # noqa: E402
from common import add_box, add_cylinder, mesh_object  # noqa: E402


def _node_root(ctx, name):
    """FBX 根節點：0.01 m 小方塊，放在底面以上（不會讓 Bounds 往下長）。"""
    bm = bmesh.new()
    add_box(bm, (0.01, 0.01, 0.01), (0, 0, 0.005))
    return mesh_object(ctx, name, bm, "Dark", "dark")


def build_ctrl_connector(ctx, p):
    q = p.get("ctrlConnector", {})
    bx, by, bz = q.get("bodySize", [0.10, 0.08, 0.08])          # Unity X × Y × Z
    sd, sl = q.get("socketDiameter", 0.040), q.get("socketLength", 0.050)
    cd, cl = q.get("capDiameter", 0.046), q.get("capLength", 0.020)
    root = _node_root(ctx, "PAN_CtrlConnector")
    bm = bmesh.new()
    add_box(bm, (bx, bz, by), (0, 0, by / 2), bevel=0.005)      # 座（圓角 5 mm）
    add_cylinder(bm, sd / 2, sl, (bx / 2 + sl / 2, 0, by / 2), axis="X", segments=12)   # 插座，軸向 +X
    body = mesh_object(ctx, "Connector_Body", bm, "Dark", "dark", parent=root)
    bm = bmesh.new()
    add_cylinder(bm, sd / 2 + 0.002, 0.004, (bx / 2 + sl - 0.002, 0, by / 2), axis="X", segments=12)
    mesh_object(ctx, "Connector_SocketRing", bm, "Metal", "metal", parent=body)   # 插座口一圈 Metal
    mouth = bx / 2 + sl
    bm = bmesh.new()   # 蓋上：套在插座口外＋繫繩小環到座頂
    add_cylinder(bm, cd / 2, cl, (mouth - cl / 2 + 0.004, 0, by / 2), axis="X", segments=12)
    add_box(bm, (0.003, 0.003, 0.012), (mouth - cl / 2, 0, by / 2 + cd / 2 + 0.006))
    mesh_object(ctx, "DustCap_On", bm, "Dark", "rubberdarkgrey", parent=root)
    bm = bmesh.new()   # 蓋下：吊在繫繩下、座 +X 面下方（蓋中心在插座口下 0.06、往 +X 0.02）
    cx, cz = mouth + 0.02, by / 2 - 0.06
    add_cylinder(bm, cd / 2, cl, (cx, 0, cz), axis="Z", segments=12)
    add_box(bm, (0.004, 0.003, 0.08), (cx, 0, cz + cl / 2 + 0.04))
    mesh_object(ctx, "DustCap_Off", bm, "Dark", "rubberdarkgrey", parent=root)
    return root


def build_hold_down_strap(ctx, p):
    q = p.get("holdDownStrap", {})
    w, t = q.get("strapWidth", 0.050), q.get("strapThickness", 0.003)
    r = q.get("armHalfWidth", 0.035)
    bs = q.get("buckleSize", [0.06, 0.02, 0.08])                  # Unity X × Y × Z
    rd = q.get("rollDiameter", 0.07)
    root = _node_root(ctx, "PAN_HoldDownStrap")
    bm = bmesh.new()   # 扣座＋口字形扣環（0.05 × 0.03，厚 4 mm）
    add_box(bm, (bs[0], bs[2], bs[1]), (0, 0, bs[1] / 2))
    for sx in (-1, 1):
        add_box(bm, (0.004, 0.004, 0.03), (sx * 0.023, 0, bs[1] + 0.015))
    add_box(bm, (0.05, 0.004, 0.004), (0, 0, bs[1] + 0.028))
    mesh_object(ctx, "HoldDown_BuckleBase", bm, "Metal", "metal", parent=root)
    bm = bmesh.new()   # 鞍：半圓帶，內徑 r、12 段，兩端朝 −Z（Unity −Y），原點在半圓頂點（外緣）
    segs, ri, ro, hz = 12, r, r + t, w / 2
    ring = []
    for k in range(segs + 1):
        a = math.pi * k / segs
        ring.append([bm.verts.new((rr * math.cos(a), y, rr * math.sin(a) - ro)) for rr in (ri, ro) for y in (-hz, hz)])
    for k in range(segs):
        a, b = ring[k], ring[k + 1]
        for i, j in ((0, 1), (2, 3), (0, 2), (1, 3)):
            bm.faces.new((a[i], a[j], b[j], b[i]))
    for e in (ring[0], ring[-1]):
        bm.faces.new((e[0], e[1], e[3], e[2]))
    mesh_object(ctx, "HoldDown_Saddle", bm, "Paint", "strapyellow", parent=root)
    bm = bmesh.new()   # 腳：單位長 1 m，原點在上端
    add_box(bm, (t, w, 1.0), (0, 0, -0.5))
    mesh_object(ctx, "HoldDown_Leg", bm, "Paint", "strapyellow", parent=root)
    bm = bmesh.new()   # 收起的帶子：扁圓柱＋露出的扣頭
    add_cylinder(bm, rd / 2, w, (0, 0, rd / 2), axis="Y", segments=12)
    add_box(bm, (0.03, w * 0.6, 0.006), (rd / 2 + 0.012, 0, 0.003))
    mesh_object(ctx, "HoldDown_Removed", bm, "Paint", "strapyellow", parent=root)
    return root


_build_roof_equipment = build


def build(ctx, p):   # noqa: F811
    return _build_roof_equipment(ctx, p) + [("PAN_CtrlConnector", build_ctrl_connector(ctx, p)), ("PAN_HoldDownStrap", build_hold_down_strap(ctx, p))]
