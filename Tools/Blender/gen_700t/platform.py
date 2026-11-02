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
