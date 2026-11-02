"""ENV_PantographStand（集電弓台架，靜態）：外形 2.0 m（X）× 2.6 m（Z，Blender 的 Y），頂面離地 0.75 m；原點＝地面中心。
底架支撐座 1.0（X）× 1.2（Y）；臂和弓頭懸出那一側（+Y）下方只有外框。定位銷 StandSeat_1..4（間距 X 0.80、Y 1.00）、夾具 StandClamp_1..2、
對位燈 AlignLight_1..4、工具盤 ToolTray（env.panto_stand.tool_tray）、台架用螺栓盤 BoltTray（架體靠 tp.stand.s 的角落）。本輪只有模型和節點。"""
from common import box, cylinder, empty_mesh

W, L = 2.0, 2.6


def build(ctx, p):
    H = p.get("standTopAboveGround", 0.75)          # 支撐座頂面（定位銷、夾具、對位燈在這個高度）
    F = p.get("standFrameTopAboveGround", 0.45)     # F6：外框頂面比支撐座低，懸出的臂和吊帶不會掃到
    root = box(ctx, "ENV_PantographStand", (0.02, 0.02, 0.02), (0, 0, 0), "Dark", "dark")
    frame_t = 0.08
    for sx in (-1, 1):   # 外框長邊
        box(ctx, f"Frame_Long_{'E' if sx > 0 else 'W'}", (frame_t, L, frame_t), (sx * (W / 2 - frame_t / 2), 0, F - frame_t / 2), "Paint", "blue", parent=root)
    for sy in (-1, 1):
        box(ctx, f"Frame_Short_{'N' if sy > 0 else 'S'}", (W, frame_t, frame_t), (0, sy * (L / 2 - frame_t / 2), F - frame_t / 2), "Paint", "blue", parent=root)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(ctx, "Leg", (frame_t, frame_t, F), (sx * (W / 2 - frame_t / 2), sy * (L / 2 - frame_t / 2), F / 2), "Paint", "blue", parent=root)
    seat = box(ctx, "SupportSeat", (1.0, 1.2, 0.06), (0, -0.4, H - 0.03), "Metal", "steel", parent=root)   # 底架支撐座（偏 −Y，+Y 是臂與弓頭懸出側）
    for sx in (-1, 1):   # 支撐座的柱子（外框比支撐座低）
        for sy in (-1, 1):
            box(ctx, "SeatPost", (0.08, 0.08, H - 0.06), (sx * 0.42, sy * 0.52, -(H - 0.06) / 2 - 0.03), "Metal", "steel", parent=seat)
    for k, (sx, sy) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        cylinder(ctx, f"StandSeat_{k + 1}", 0.02, 0.05, (sx * 0.40, sy * 0.50, 0.055), segments=8, mat="Metal", color="metal", parent=seat)
    for k, sx in enumerate((-1, 1)):
        clamp = box(ctx, f"StandClamp_{k + 1}", (0.06, 0.20, 0.08), (sx * 0.55, 0, 0.07), "Paint", "red", parent=seat)
        empty_mesh(ctx, f"StandClamp_{k + 1}_Pivot", (0, -0.1, -0.04), parent=clamp)
    for k, (sx, sy) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        cylinder(ctx, f"AlignLight_{k + 1}", 0.025, 0.03, (sx * (W / 2 - 0.1), sy * (L / 2 - 0.1), H + 0.015), segments=8, mat="Paint", color="green", parent=root)
    box(ctx, "ToolTray", (0.45, 0.30, 0.05), (W / 2 + 0.25, 0.6, F - 0.15), "Metal", "steel", parent=root)
    box(ctx, "BoltTray", (0.25, 0.18, 0.024), (W / 2 - 0.2, -L / 2 + 0.15, F + 0.012), "Metal", "steel", parent=root)   # 靠 tp.stand.s 的角落（在外框上）
    return [("ENV_PantographStand", root)]
