"""轉向架（車身的子物件）：構架＋4 個車輪（只做外觀）。"""
from common import box, cylinder

WHEEL_R = 0.43       # EX
GAUGE = 1.435


def build(ctx, p, name, loc, parent):
    frame = box(ctx, name, (2.4, 2.8, 0.35), loc, "Dark", "dark", parent=parent)
    wheel_z = (p["groundY"] + WHEEL_R) - loc[2]   # 軌面＝地面
    for i, (sx, sy) in enumerate(((1, 1), (-1, 1), (1, -1), (-1, -1))):
        cylinder(ctx, f"{name}_Wheel_{i + 1}", WHEEL_R, 0.14, (sx * GAUGE / 2.0, sy * 1.25, wheel_z), axis="X", segments=12, mat="Metal", color="steel", parent=frame)
    return frame
