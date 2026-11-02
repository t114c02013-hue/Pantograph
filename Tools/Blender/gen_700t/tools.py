"""a6 增補 7（建模 A blender-addon-3parts.md §6，torque-wrench-sockets.md v1.3 §6.4、§7.2）：套筒 TL_Socket_*。
尺寸只讀 Unity 匯出的 tool_sockets.json（單一來源）；原點在驅動端面中心，口朝 Unity −Y（Blender −Z），掛到 Socket_Mount 時 Transform 全 0。
Tip 等節點由 Unity 建置腳本建。材質 Metal，色環 UV 指色格（24 黃、19 藍、13_38 綠，其餘金屬色）。"""
import json
import os
import bmesh
from common import add_cylinder, mesh_object

RING = {24: "yellow", 19: "blue"}


def build_socket(ctx, s):
    h, r = s["heightM"], s["outerDiaM"] / 2.0
    color = "green" if s["socketId"].endswith("_38") else RING.get(s["sizeMm"], "metal")
    bm = bmesh.new()
    add_cylinder(bm, r, h, (0, 0, -h / 2.0), axis="Z", segments=12)                      # 套筒本體
    add_cylinder(bm, r * 0.62, 0.002, (0, 0, -h + 0.0011), axis="Z", segments=6)         # 口部六角（幾何示意，不超出套筒口）
    add_cylinder(bm, r * 0.97, 0.001, (0, 0, -h + 0.005), axis="Z", segments=12)         # 口部上方 1 mm 淺槽（尺寸識別）
    body = mesh_object(ctx, s["prefab"], bm, "Metal", "metal")
    bm = bmesh.new()
    add_cylinder(bm, r + 0.0004, 0.004, (0, 0, -h / 2.0), axis="Z", segments=12)          # 色票環（外徑多 0.8 mm，仍在 ±1 mm 內）
    mesh_object(ctx, s["prefab"] + "_ColorRing", bm, "Metal", color, parent=body)
    return body


def build(ctx, p):
    path = os.path.join(p.get("_dir", "."), "tool_sockets.json")
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        sockets = json.load(f)["sockets"]
    return [(s["prefab"], build_socket(ctx, s)) for s in sockets]
