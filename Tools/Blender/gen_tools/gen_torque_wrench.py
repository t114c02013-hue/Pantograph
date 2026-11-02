"""a6 增補 7（鐵道 D39，torque-wrench-sockets.md v1.3 §6.2）：兩支扭力扳手 TL_TorqueWrench_S（3/8"）、TL_TorqueWrench_L（1/2"），同一支參數化腳本、不共用網格。
執行："<Blender>\blender.exe" -b --factory-startup -P Tools/Blender/gen_tools/gen_torque_wrench.py -- --params "Assets/_Project/Tools/TorqueWrench/Data/tool_torque_wrenches.json" --out "Assets/_Project/Tools/TorqueWrench/Models"
原點＝Attach_Grip（握把中心）：Unity +Z 朝扳手頭部（Blender +Y）、+Y 朝上（Blender +Z）。方頭在頭部底面往下伸；Socket_Mount（方頭根部截面中心）由 Unity 用頂點實測建立。
FX_Head、Poke_Plus、Poke_Minus、COL_Body、Tip、Socket_Mount 等空節點由 Unity 建置腳本建（FBX 只含網格）；FX_CalLabel 是獨立網格（40 × 20 mm，貼在握把上方）。每支 ≤ 1.5k 三角面。"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "gen_700t"))

import bpy  # noqa: E402
import bmesh  # noqa: E402
import common  # noqa: E402
from common import add_box, add_cylinder, mesh_object  # noqa: E402
from palette import Palette  # noqa: E402


def wrench(ctx, w):
    # 匯出設定（axis_forward −Z）下 Blender −Y＝Unity +Z：頭部放在 Blender −Y，Unity 才會在 +Z
    L, total, hw, ht, drive = w["handleLengthM"], w["totalLengthM"], w["headWidthM"], w["headThickM"], w["driveSquareM"]
    r = w.get("gripRadiusM", 0.014)
    back = L - total + hw / 2.0                     # 把手尾端（Blender Y）
    bm = bmesh.new()
    add_cylinder(bm, r, L - hw / 2.0 - back, (0, -(back + L - hw / 2.0) / 2.0, 0), axis="Y", segments=12)   # 把手（握把到頭部）
    add_box(bm, (hw, hw * 1.3, ht), (0, -L, 0))                                                           # 頭部（中心＝把手軸）
    body = mesh_object(ctx, w["fbx"], bm, "Metal", "steel")
    bm = bmesh.new()
    add_box(bm, (drive, drive, 0.012), (0, -L, -ht / 2.0 - 0.006))                                        # 方頭（根部在頭部底面 Z＝−ht/2）
    mesh_object(ctx, "DriveSquare", bm, "Metal", "metal", parent=body)
    bm = bmesh.new()
    add_cylinder(bm, r + 0.002, 0.12, (0, 0, 0), axis="Y", segments=12)                                   # 握把套
    mesh_object(ctx, "Grip", bm, "Dark", "rubber", parent=body)
    bm = bmesh.new()
    add_box(bm, (0.020, 0.040, 0.002), (0, -0.10, r + 0.001))                                             # 校正標籤 40 × 20 mm
    mesh_object(ctx, "FX_CalLabel", bm, "Paint", "white", parent=body)
    bm = bmesh.new()
    add_box(bm, (0.012, 0.050, 0.004), (0, -L * 0.55, r + 0.002))                                         # 刻度窗
    mesh_object(ctx, "ScaleWindow", bm, "Paint", "white", parent=body)
    return body


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    a = {argv[i].lstrip("-"): argv[i + 1] for i in range(0, len(argv) - 1, 2)}
    with open(a["params"], encoding="utf-8") as f:
        params = json.load(f)
    import tempfile
    tmp = tempfile.mkdtemp(prefix="gentw_")
    report = []
    for w in params["wrenches"]:
        common.clear_scene()   # 每支各自清場：兩支的子網格同名（DriveSquare、FX_CalLabel），不清會變成 .001
        export_one(a, tmp, w, report)
    print("[gen_torque_wrench] OK", report)


def export_one(a, tmp, w, report):
    pal = Palette({"liveryWhite": "#F4F4F2", "liveryOrange": "#E86A1E", "liveryDark": "#2B2B2B"})
    img = pal.image(os.path.join(tmp, "Palette.png"))
    ctx = common.Ctx(common.materials(img), pal)
    os.makedirs(a["out"], exist_ok=True)
    root = wrench(ctx, w)
    objs = [root] + list(root.children)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=os.path.join(a["out"], w["fbx"] + ".fbx"), use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, object_types={"MESH"}, mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="STRIP")
    report.append((w["fbx"], sum(common.triangles(o) for o in objs)))


try:
    main()
except Exception as e:  # noqa: BLE001
    import traceback
    traceback.print_exc()
    print("[gen_torque_wrench] FAILED:", e)
    sys.exit(1)
