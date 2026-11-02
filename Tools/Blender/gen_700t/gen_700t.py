"""R1.5a a2：700T 車廂與檢修庫設備的靜態模型產生器（Blender 3.6 LTS／4.x／5.x；只用 bmesh 與 bpy.ops.export_scene.fbx）。
執行："<Blender>\\blender.exe" -b --factory-startup -P Tools/Blender/gen_700t/gen_700t.py -- --params "Assets/_Project/Worksite/Data/car_params.json" --out "Assets/_Project/Worksite/Models/Generated"
只讀 car_params.json（Unity 選單 Tools/Worksite/Export Car Params 匯出）。開頭先清空場景；任何一步失敗就停止，不輸出不完整的檔案（先寫到暫存資料夾，全部成功才搬過去）。
報告：Logs/CarModelReport.json（版本、每個 prefab 的三角面數與範圍）。"""
import json
import os
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
import common  # noqa: E402
from palette import Palette  # noqa: E402
import importlib.util  # noqa: E402
import car_body, roof_equipment, furniture, crane, rigging, stand, pouch  # noqa: E402,E401

# platform.py 和 Python 標準模組 platform 同名，用檔案路徑載入
_spec = importlib.util.spec_from_file_location("gen_platform", os.path.join(os.path.dirname(os.path.abspath(__file__)), "platform.py"))
platform = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(platform)

MODULES = [car_body, roof_equipment, platform, furniture, crane, rigging, stand, pouch]


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = {"params": None, "out": None, "report": "Logs/CarModelReport.json"}
    for i in range(0, len(argv) - 1, 2):
        out[argv[i].lstrip("-")] = argv[i + 1]
    if not out["params"] or not out["out"]:
        raise SystemExit("需要 --params <car_params.json> --out <輸出資料夾>")
    return out


def descendants(ob):
    yield ob
    for c in ob.children:
        yield from descendants(c)


def export(root, path):
    bpy.ops.object.select_all(action="DESELECT")
    for o in descendants(root):
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", bake_space_transform=False, object_types={"MESH"}, mesh_smooth_type="FACE",
        add_leaf_bones=False, bake_anim=False, use_custom_props=False, path_mode="STRIP")


def main():
    a = args()
    with open(a["params"], encoding="utf-8") as f:
        params = json.load(f)
    common.clear_scene()
    tmp = tempfile.mkdtemp(prefix="gen700t_")
    pal = Palette(params)
    img = pal.image(os.path.join(tmp, "Palette.png"))
    ctx = common.Ctx(common.materials(img), pal)
    prefabs = []
    for m in MODULES:
        prefabs.extend(m.build(ctx, params))
    bpy.context.view_layer.update()
    report = {"blender": bpy.app.version_string, "params": os.path.abspath(a["params"]), "prefabs": [], "totalTriangles": 0,
              "materials": sorted({s.material.name for o in bpy.data.objects if o.type == "MESH" for s in o.material_slots if s.material})}
    for name, root in prefabs:
        objs = list(descendants(root))
        tris = sum(common.triangles(o) for o in objs)
        lo, hi = common.world_bounds(objs)
        export(root, os.path.join(tmp, name + ".fbx"))
        report["prefabs"].append({"name": name, "triangles": tris, "objects": len(objs), "boundsMin": lo, "boundsMax": hi,
                                  "size": [hi[k] - lo[k] for k in range(3)]})
        report["totalTriangles"] += tris
    # 全部成功才搬到輸出資料夾
    os.makedirs(a["out"], exist_ok=True)
    for fn in os.listdir(tmp):
        shutil.copyfile(os.path.join(tmp, fn), os.path.join(a["out"], fn))
    shutil.rmtree(tmp, ignore_errors=True)
    os.makedirs(os.path.dirname(a["report"]) or ".", exist_ok=True)
    with open(a["report"], "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    print("[gen_700t] OK", len(prefabs), "prefabs,", report["totalTriangles"], "triangles")


try:
    main()
except SystemExit:
    raise
except Exception as e:  # 失敗就停止、不輸出
    import traceback
    traceback.print_exc()
    print("[gen_700t] FAILED:", e)
    sys.exit(1)
