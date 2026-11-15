"""輕軌車（淡海輕軌樣式的通用外觀，沒有任何標誌、字樣、車號）：一列 5 模組鉸接、低地板、單臂集電弓在中間模組。
terry 2026-11-13：車體從 700T 換成輕軌；集電弓、站點、平台的世界座標不動，車體來對齊。

執行（Blender 4.x／5.x）：
  "<Blender>\\blender.exe" -b --factory-startup -P Tools/Blender/gen_lrv/gen_lrv.py -- --params "Assets/_Project/Worksite/Data/car_params.json" --out "Assets/_Project/Worksite/Models/Generated"
輸出：ENV_LRV.fbx、Palette.png（和 gen_700t 共用的色盤；新顏色只往後加，舊模型的 UV 不受影響）。

座標（和 gen_700t 相同）：車長沿 +Y、車寬 X、上 Z，單位 m；原點＝中間模組車頂面中心（Unity 擺在集電弓所在的 (0, 車頂, 0)）。
有來源：全長 34.45、寬 2.65、5 模組、集電弓 1 具在第 3 模組。推估：模組長（7.875／5.5／4.1）、鉸接 0.9、地板高、門窗位置、車頂設備位置與大小。
"""
import json
import math
import os
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "gen_700t"))

import bmesh  # noqa: E402
import bpy  # noqa: E402
import common  # noqa: E402
from common import box, cylinder, extrude_profile  # noqa: E402
from palette import Palette  # noqa: E402

CORNER = 0.30      # 車頂兩側圓角半徑（推估）
SKIRT = 0.30       # 車體下緣離軌面（低地板，推估）
SKIRT_H = 0.42     # 水藍裙板高
WHEEL_R = 0.33     # 低地板車輪（推估）
GAUGE = 1.435


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = {"params": None, "out": None}
    for i in range(0, len(argv) - 1, 2):
        out[argv[i].lstrip("-")] = argv[i + 1]
    if not out["params"] or not out["out"]:
        raise SystemExit("需要 --params <car_params.json> --out <輸出資料夾>")
    return out


def profile(hw, bottom, steps=6):
    """斷面：側牆垂直，車頂兩側小圓角，中央平頂在 z＝0。逆時針（從 +Y 看）。"""
    pts = [(hw, bottom), (hw, -CORNER)]
    for i in range(1, steps + 1):
        a = math.pi / 2 * i / steps
        pts.append((hw - CORNER + CORNER * math.cos(a), -CORNER + CORNER * math.sin(a)))
    for i in range(0, steps + 1):
        a = math.pi / 2 + math.pi / 2 * i / steps
        pts.append((-(hw - CORNER) + CORNER * math.cos(a), -CORNER + CORNER * math.sin(a)))
    pts.append((-hw, bottom))
    out = []
    for q in pts:
        if not out or abs(out[-1][0] - q[0]) > 1e-6 or abs(out[-1][1] - q[1]) > 1e-6:
            out.append(q)
    return out


def bogie(ctx, name, y, rail, parent):
    frame = box(ctx, name, (2.0, 1.9, 0.3), (0, y, rail + WHEEL_R + 0.12), "Dark", "dark", parent=parent)
    for i, (sx, sy) in enumerate(((1, 1), (-1, 1), (1, -1), (-1, -1))):
        cylinder(ctx, f"{name}_Wheel_{i + 1}", WHEEL_R, 0.12, (sx * GAUGE / 2.0, sy * 0.9, -0.12), axis="X", segments=12, mat="Metal", color="steel", parent=frame)
    return frame


def module(ctx, root, name, y0, length, p, head=None):
    """一個模組：車身（白）、窗帶（黑）、裙板色帶（水藍）、車門；head＝-1／+1 表示這一端是車頭。"""
    hw, rail = p["carBodyHalfWidth"], p["groundY"]
    floor = rail + p["floorAboveRail"]
    bottom = rail + SKIRT
    nose = 0.9 if head else 0.0                      # 車頭斜面另外做
    body_len = length - nose
    cy = y0 + length / 2.0 + (nose / 2.0 if head == -1 else -nose / 2.0 if head == 1 else 0.0)
    body = extrude_profile(ctx, name, profile(hw, bottom), body_len, mat="Paint", color="white", roof_mat="Roof", roof_color="roof", parent=root, loc=(0, cy, 0))
    win_lo, win_hi = floor + 0.80, -0.65
    for sgn, tag in ((1, "R"), (-1, "L")):
        x = sgn * (hw + 0.005)
        box(ctx, f"{name}_Window_{tag}", (0.01, body_len - 0.3, win_hi - win_lo), (x, 0, (win_lo + win_hi) / 2), "Glass", "glass", parent=body)
        box(ctx, f"{name}_Skirt_{tag}", (0.01, body_len - 0.1, SKIRT_H), (x, 0, bottom + SKIRT_H / 2), "Paint", "aqua", parent=body)
        box(ctx, f"{name}_RoofLine_{tag}", (0.01, body_len - 0.1, 0.08), (x, 0, -0.45), "Paint", "aqua", parent=body)
        n_doors = 1 if length < 5 else 2 if length < 7 else 1
        for d in range(n_doors):
            dy = 0.0 if n_doors == 1 else (-1 + 2 * d) * body_len * 0.22
            if head:
                dy = -head * body_len * 0.15
            box(ctx, f"{name}_Door_{tag}_{d + 1}", (0.02, 1.30, win_hi - floor - 0.05), (x, dy, (floor + win_hi) / 2), "Dark", "dark", parent=body)
    n_doors = 1 if length < 5 else 2 if length < 7 else 1
    doors = [-head * body_len * 0.15 if head else 0.0 if n_doors == 1 else (-1 + 2 * d) * body_len * 0.22 for d in range(n_doors)]
    dots(ctx, body, name, cy, body_len, doors, hw, bottom, p)
    if head:
        # 車頭：斜擋風玻璃＋下方水藍車頭板＋頭燈
        fy = cy + head * body_len / 2.0
        front = extrude_profile(ctx, f"{name}_Nose", profile(hw - 0.15, bottom + 0.05), nose, mat="Paint", color="aqua", roof_mat="Roof", roof_color="roof", parent=root, loc=(0, fy + head * nose / 2.0, -0.08))
        box(ctx, f"{name}_Windshield", (2 * hw - 0.5, 0.02, 1.5), (0, head * (nose / 2.0 + 0.012), -1.05), "Glass", "glass", parent=front)
        for sx in (-1, 1):
            box(ctx, f"{name}_Headlight_{'L' if sx < 0 else 'R'}", (0.25, 0.02, 0.10), (sx * (hw - 0.45), head * (nose / 2.0 + 0.012), bottom + 0.75), "Metal", "white", parent=front)
    return body


def dots(ctx, body, name, cy, body_len, doors, hw, bottom, p):
    """terry 173B：裙板上的白色圓點帶（lrvDots，建模師 A v2）。一個模組一側一個網格，每顆是朝外的平 8 邊形；碰到車門（＋邊距）就不放。
    波浪相位用整列的車長座標，左右兩側同一個值（對稱、跨模組連續）。"""
    cfg = p.get("lrvDots")
    if not cfg or name[len("Module_"):] not in cfg["modules"]:
        return
    skirt_top, skirt_face = bottom + SKIRT_H, hw + 0.01          # 裙板方塊厚 0.01、貼在 hw＋0.005
    pitch, amp, wl, seg = cfg["pitchZ"], cfg["waveAmplitude"], cfg["waveLength"], int(cfg["segments"])
    for sgn, tag in ((1, "R"), (-1, "L")):
        bm = bmesh.new()
        for i in range(int(cfg["rows"])):
            r, zc, off = cfg["diameters"][i] / 2.0, cfg["rowCenterY"][i], pitch / 2.0 * (i % 2)   # 上下排錯開半格
            k0, k1 = math.ceil((cy - body_len / 2.0 - off) / pitch), math.floor((cy + body_len / 2.0 - off) / pitch)
            for k in range(k0, k1 + 1):
                yw = k * pitch + off
                yl = yw - cy
                if abs(yl) + r > body_len / 2.0 - 0.05 or any(abs(yl - dy) < 0.65 + cfg["skipDoorMargin"] + r for dy in doors):
                    continue
                z = zc + amp * math.sin(2 * math.pi * yw / wl)
                x = sgn * ((skirt_face if z - r < skirt_top else hw) + cfg["offsetFromWall"])   # 壓到裙板就以裙板外表面為準
                vs = [bm.verts.new((x, yl + r * math.cos(2 * math.pi * j / seg), z + r * math.sin(2 * math.pi * j / seg))) for j in range(seg)]
                f = bm.faces.new(vs if sgn > 0 else vs[::-1])   # 法線朝車外（y→z 逆時針＝+X）
                f.normal_update()
        if bm.faces:
            common._finish(ctx, f"{name}_Dots_{tag}", bm, "Paint", "white", parent=body)
        else:
            bm.free()


def roof_box(ctx, name, size, y, root, color="metal"):
    """車頂設備艙（空調、電阻箱、電池箱）：方盒，底面貼車頂。"""
    return box(ctx, name, size, (0, y, size[2] / 2.0), "Metal", color, parent=root)


def build(ctx, p):
    mods = p.get("lrvModules", [7.875, 5.5, 4.1, 5.5, 7.875])
    gap = p.get("lrvArticulation", 0.9)
    total = sum(mods) + gap * (len(mods) - 1)
    mid = len(mods) // 2
    # 中間模組（集電弓）中心在 y＝0
    y = -(sum(mods[:mid]) + gap * mid + mods[mid] / 2.0)
    root = box(ctx, "ENV_LRV", (0.01, 0.01, 0.01), (0, 0, 0), "Dark", "dark")
    rail = p["groundY"]
    names = ["Module_A1", "Module_B1", "Module_C", "Module_B2", "Module_A2"]
    starts = []
    for i, L in enumerate(mods):
        head = -1 if i == 0 else 1 if i == len(mods) - 1 else None
        module(ctx, root, names[i] if i < len(names) else f"Module_{i}", y, L, p, head)
        starts.append(y)
        if i < len(mods) - 1:
            # 鉸接（風擋）：比車身窄一點的深色段
            hw = p["carBodyHalfWidth"]
            extrude_profile(ctx, f"Articulation_{i + 1}", profile(hw - 0.02, rail + SKIRT + 0.1), gap, mat="Dark", color="dark", parent=root, loc=(0, y + L + gap / 2.0, -0.04))   # 鉸接頂蓋幾乎和車身同寬：車頂可走範圍離邊 ≥ 0.5 m（資深鐵道 2026-11-13）
        y += L + gap
    # 轉向架：兩個頭車各一（靠車頭）、中間模組一個
    bogie(ctx, "Bogie_A1", starts[0] + 2.6, rail, root)
    bogie(ctx, "Bogie_C", starts[mid] + mods[mid] / 2.0, rail, root)
    bogie(ctx, "Bogie_A2", starts[-1] + mods[-1] - 2.6, rail, root)
    # 車頂設備（推估）：避開中間模組與車頂作業區 |y| ≤ roofSectionZMax＋0.6
    keep = p.get("roofSectionZMax", 4.0) + 0.6
    b1, b2 = starts[1], starts[3]
    roof_box(ctx, "Roof_HVAC_B1", (1.9, 2.2, 0.38), min(b1 + mods[1] / 2.0, -keep - 1.1), root, "concrete")
    roof_box(ctx, "Roof_HVAC_B2", (1.9, 2.2, 0.38), max(b2 + mods[3] / 2.0, keep + 1.1), root, "concrete")
    roof_box(ctx, "Roof_Resistor_A1", (1.5, 1.6, 0.30), starts[0] + mods[0] - 1.6, root, "steel")
    roof_box(ctx, "Roof_Battery_A2", (1.6, 1.8, 0.34), starts[-1] + 1.8, root, "steel")
    roof_box(ctx, "Roof_Converter_A1", (1.6, 1.8, 0.34), starts[0] + 2.8, root, "steel")
    roof_box(ctx, "Roof_HVAC_A2", (1.9, 2.0, 0.38), starts[-1] + mods[-1] - 3.0, root, "concrete")
    return [("ENV_LRV", root)], total


def descendants(ob):
    yield ob
    for c in ob.children:
        yield from descendants(c)


def report_entry(name, tris, n, lo, hi, path="Logs/CarModelReport.json"):
    """把 ENV_LRV 的面數與範圍寫進 gen_700t 的報告（同名那筆換掉，其餘不動）。"""
    try:
        with open(path, encoding="utf-8") as f:
            rep = json.load(f)
    except (OSError, ValueError):
        rep = {"prefabs": []}
    rep["blender"] = bpy.app.version_string
    rep["prefabs"] = [x for x in rep.get("prefabs", []) if x.get("name") != name] + [{
        "name": name, "triangles": tris, "objects": n, "boundsMin": list(lo), "boundsMax": list(hi), "size": [hi[k] - lo[k] for k in range(3)]}]
    with open(path, "w", encoding="utf-8") as f:
        json.dump(rep, f, ensure_ascii=False, indent=2)


def main():
    a = args()
    with open(a["params"], encoding="utf-8") as f:
        params = json.load(f)
    common.clear_scene()
    tmp = tempfile.mkdtemp(prefix="genlrv_")
    pal = Palette(params)
    img = pal.image(os.path.join(tmp, "Palette.png"))
    ctx = common.Ctx(common.materials(img), pal)
    prefabs, total = build(ctx, params)
    bpy.context.view_layer.update()
    for name, root in prefabs:
        objs = list(descendants(root))
        lo, hi = common.world_bounds(objs)
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = root
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(tmp, name + ".fbx"), use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
            axis_forward="-Z", axis_up="Y", bake_space_transform=False, object_types={"MESH"}, mesh_smooth_type="FACE",
            add_leaf_bones=False, bake_anim=False, use_custom_props=False, path_mode="STRIP")
        tris = sum(common.triangles(o) for o in objs)
        print(f"[gen_lrv] {name}: {tris} tris, size {[round(hi[k] - lo[k], 3) for k in range(3)]}, length param {total:.3f}")
        report_entry(name, tris, len(objs), lo, hi)
    os.makedirs(a["out"], exist_ok=True)
    for fn in os.listdir(tmp):
        shutil.copyfile(os.path.join(tmp, fn), os.path.join(a["out"], fn))
    shutil.rmtree(tmp, ignore_errors=True)
    print("[gen_lrv] OK")


try:
    main()
except SystemExit:
    raise
except Exception as e:
    import traceback
    traceback.print_exc()
    print("[gen_lrv] FAILED:", e)
    sys.exit(1)
