"""ENV_CarBody：一節 700T 車身（三節共用這個網格）。原點＝車頂中央平段頂面的車長中點；車長沿 +Y、車寬 X、上 Z。
斷面：中央平段寬 roofFlatWidth，兩側圓弧（半徑 roofArcRadius，縮放到車頂邊剛好下降 roofEdgeDrop），側牆垂直到車底。
色帶：白色車身、窗帶（深色玻璃）、窗下橘色帶；沒有任何標誌、字樣、車號。"""
import math
from common import extrude_profile, box
import bogie

RAIL_TOP = 0.0           # D27：軌面＝地面 Y −3.65，車廂地板＝軌面 ＋ floorAboveRail（−2.35）
UNDERFRAME = 0.35        # 地板面以下的車體深度（EX）


def profile(p, arc_steps=10):
    hw, flat, r, drop = p["carBodyHalfWidth"], p["roofFlatWidth"] / 2.0, p["roofArcRadius"], p["roofEdgeDrop"]
    floor_z = p["groundY"] + RAIL_TOP + p["floorAboveRail"]
    bottom = floor_z - UNDERFRAME
    raw = lambda x: -r + math.sqrt(max(r * r - (x - flat) ** 2, 0.0))
    raw_edge = raw(hw) if raw(hw) < 0 else -1e-3
    pts = [(hw, bottom)]
    for i in range(arc_steps, -1, -1):          # 右側圓弧：車頂邊 → 平段
        x = flat + (hw - flat) * i / arc_steps
        pts.append((x, drop * raw(x) / -raw_edge * 1.0 if x > flat else 0.0))
    for i in range(0, arc_steps + 1):           # 左側（鏡像）：平段 → 車頂邊
        x = flat + (hw - flat) * i / arc_steps
        pts.append((-x, drop * raw(x) / -raw_edge if x > flat else 0.0))
    pts.append((-hw, bottom))
    # 去掉重複的平段端點
    out = []
    for q in pts:
        if not out or abs(out[-1][0] - q[0]) > 1e-6 or abs(out[-1][1] - q[1]) > 1e-6:
            out.append(q)
    # 逆時針（從 +Y 看）：目前是右下 → 右上 → 左上 → 左下，剛好逆時針
    return out, floor_z, bottom


def build(ctx, p):
    prof, floor_z, bottom = profile(p)
    length = p["carLength"]
    hw = p["carBodyHalfWidth"]
    body = extrude_profile(ctx, "ENV_CarBody", prof, length, mat="Paint", color="white", roof_mat="Roof", roof_color="roof")
    side_top = prof[1][1]
    # 窗帶（深色玻璃）與窗下橘色帶：兩側各一條，貼在側牆外 5 mm
    win_lo, win_hi = floor_z + 0.75, floor_z + 1.65
    band_lo, band_hi = floor_z + 0.55, floor_z + 0.68
    for sgn, tag in ((1, "R"), (-1, "L")):
        x = sgn * (hw + 0.005)
        box(ctx, f"WindowBand_{tag}", (0.01, length - 2.0, win_hi - win_lo), (x, 0, (win_lo + win_hi) / 2), "Glass", "glass", parent=body)
        box(ctx, f"OrangeBand_{tag}", (0.01, length - 0.4, band_hi - band_lo), (x, 0, (band_lo + band_hi) / 2), "Paint", "orange", parent=body)
    # 車端門（只有外觀）
    for sgn, tag in ((1, "N"), (-1, "S")):
        box(ctx, f"EndDoor_{tag}", (0.8, 0.02, 1.9), (0, sgn * (length / 2 + 0.01), floor_z + 0.95), "Dark", "dark", parent=body)
    # 轉向架（前後各一）
    for sgn, tag in ((1, "N"), (-1, "S")):
        bogie.build(ctx, p, f"Bogie_{tag}", (0, sgn * p["bogieCenterDistance"] / 2.0, bottom - 0.05), parent=body)
    return [("ENV_CarBody", body)]
