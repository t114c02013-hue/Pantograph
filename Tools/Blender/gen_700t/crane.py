"""ENV_OverheadCrane（天車，靜態）與 ENV_CraneRemote（懸吊按鈕盒）。節點名稱照規格；Unity 依 RoofSceneDef 擺放，本輪沒有邏輯。
Blender 軸：車長（走行方向）沿 +Y、橫移沿 X、上 Z。走行軌道兩條，單位長 1 m（Unity 縮放到 30 m）。"""
from common import box, cylinder, empty_mesh

SPAN = 19.0   # 軌道間距（X +3.0 到 −16.0），EX


def crane(ctx):
    root = box(ctx, "ENV_OverheadCrane", (0.02, 0.02, 0.02), (0, 0, 0), "Dark", "dark")
    vis = box(ctx, "Visual", (0.02, 0.02, 0.02), (0, 0, 0), "Dark", "dark", parent=root)
    for name, x in (("Crane_Runway_E", 0.0), ("Crane_Runway_W", -SPAN)):
        box(ctx, name, (0.4, 1.0, 0.6), (x, 0, 0), "Paint", "craneyellow", parent=vis)
    bridge = box(ctx, "Crane_Bridge", (SPAN, 0.6, 0.9), (-SPAN / 2, 0, 0.75), "Paint", "craneyellow", parent=vis)
    trolley = box(ctx, "Crane_Trolley", (1.6, 1.4, 0.6), (0, 0, 0.6), "Paint", "craneyellow", parent=bridge)
    cylinder(ctx, "Crane_Hoist", 0.3, 1.0, (0, 0, -0.35), axis="Y", segments=12, mat="Metal", color="steel", parent=trolley)
    empty_mesh(ctx, "Crane_RopeAnchor", (0, 0, -0.65), parent=trolley)
    cylinder(ctx, "Crane_WarningLight", 0.06, 0.08, (0.6, 0.5, 0.35), segments=8, mat="Paint", color="orangesafety", parent=trolley)
    hook = box(ctx, "Crane_HookBlock", (0.35, 0.20, 0.40), (0, 0, -2.0), "Paint", "craneyellow", parent=root)
    box(ctx, "Hook_Latch", (0.05, 0.04, 0.12), (0.08, 0, -0.30), "Metal", "metal", parent=hook)
    empty_mesh(ctx, "Hook_AttachPoint", (0, 0, -0.32), parent=hook)
    beam = box(ctx, "ENV_LiftingBeam", (2.2, 0.14, 0.18), (0, 0, -0.9), "Paint", "craneyellow", parent=hook)
    for name, x in (("Beam_SlingEye_L", -1.0), ("Beam_SlingEye_R", 1.0)):
        cylinder(ctx, name, 0.04, 0.03, (x, 0, -0.12), axis="Y", segments=8, mat="Metal", color="metal", parent=beam)
    cylinder(ctx, "Crane_Rope", 0.012, 1.0, (0, 0, -1.0), segments=8, mat="Metal", color="steel", parent=root)   # 單位長 1 m
    return root


def remote(ctx):
    body = box(ctx, "ENV_CraneRemote", (0.09, 0.06, 0.26), (0, 0, 0), "Paint", "craneyellow")
    buttons = ["Btn_Up", "Btn_Down", "Btn_East", "Btn_West", "Btn_North", "Btn_South", "Btn_Speed2"]
    for k, n in enumerate(buttons):
        cylinder(ctx, n, 0.012, 0.012, (0, -0.033, 0.09 - k * 0.028), axis="Y", segments=8, mat="Dark", color="green" if k < 6 else "blue", parent=body)
        empty_mesh(ctx, "Label_" + n, (0.035, -0.033, 0.09 - k * 0.028), parent=body, size=0.004)
    cylinder(ctx, "Btn_EStop", 0.02, 0.02, (0, 0, 0.14), segments=10, mat="Paint", color="red", parent=body)
    empty_mesh(ctx, "Label_Btn_EStop", (0.035, 0, 0.14), parent=body, size=0.004)
    box(ctx, "Key_Power", (0.012, 0.012, 0.02), (0.03, -0.033, -0.11), "Metal", "metal", parent=body)
    empty_mesh(ctx, "Label_Key_Power", (0.045, -0.033, -0.11), parent=body, size=0.004)
    return body


def build(ctx, p):
    return [("ENV_OverheadCrane", crane(ctx)), ("ENV_CraneRemote", remote(ctx))]
