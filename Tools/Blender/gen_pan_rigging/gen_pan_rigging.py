"""R1.5a a2：集電弓整組的小網格（PAN_Rigging.fbx）：M16 底座螺栓、吊耳、降弓固定帶、高壓端子座與端子、控制線接頭與防塵蓋、Parked 電纜段。合計 ≤ 3k 三角面。
執行："<Blender>\\blender.exe" -b --factory-startup -P Tools/Blender/gen_pan_rigging/gen_pan_rigging.py -- --params "Assets/_Project/Parts/PantographAssembly/Data/pan_rigging_params.json" --out "Assets/_Project/Parts/PantographAssembly/Models"
報告：Logs/PanRiggingModelReport.json。失敗就停止、不輸出。"""
import json
import os
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "gen_700t"))

import bpy  # noqa: E402
import common  # noqa: E402

MAX_TRIS = 3000


class MiniPalette:
    """沿用 gen_700t 的 Palette 介面（色名 → UV）；這個 FBX 只用到幾個顏色，UV 指到同一張 Palette.png 的對應格（色名須在 gen_700t 的色盤裡）。"""
    def __init__(self):
        from palette import Palette
        self.p = Palette({"liveryWhite": "#F4F4F2", "liveryOrange": "#E86A1E", "liveryDark": "#2B2B2B"})

    def uv(self, name):
        return self.p.uv(name)


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = {"params": None, "out": None, "report": "Logs/PanRiggingModelReport.json"}
    for i in range(0, len(argv) - 1, 2):
        out[argv[i].lstrip("-")] = argv[i + 1]
    if not out["params"] or not out["out"]:
        raise SystemExit("需要 --params <pan_rigging_params.json> --out <輸出資料夾>")
    return out


def build(ctx, p):
    box, cyl, emp = common.box, common.cylinder, common.empty_mesh
    root = box(ctx, "PAN_Rigging", (0.01, 0.01, 0.01), (0, 0, 0), "Dark", "dark")
    # M16 底座螺栓（六角頭＋螺桿＋墊圈），原點在墊圈底面
    bolt = cyl(ctx, "BaseBolt_M16", p["boltHeadAcrossFlats"] / 2, 0.01, (0, 0, 0.015), segments=6, mat="Metal", color="steel", parent=root)
    cyl(ctx, "BaseBolt_Shank", p["boltDiameter"] / 2, p["boltLength"], (0, 0, -p["boltLength"] / 2), segments=8, mat="Metal", color="steel", parent=bolt)
    cyl(ctx, "BaseBolt_Washer", 0.016, 0.003, (0, 0, -0.008), segments=10, mat="Metal", color="metal", parent=bolt)
    # 吊耳（板＋孔的位置節點）
    lug = box(ctx, "LiftLug", (p["liftLugThickness"], 0.08, p["liftLugHeight"]), (0.3, 0, p["liftLugHeight"] / 2), "Metal", "steel", parent=root)
    emp(ctx, "LiftLug_Hole", (0, 0, p["liftLugHeight"] * 0.25), parent=lug)
    # 降弓固定帶
    strap = box(ctx, "HoldDown_Strap", (p["holdDownStrapWidth"], p["holdDownStrapLength"], 0.004), (0.6, 0, 0.002), "Paint", "orangesafety", parent=root)
    box(ctx, "HoldDown_Buckle", (0.05, 0.04, 0.02), (0, p["holdDownStrapLength"] / 2 - 0.03, 0.01), "Metal", "metal", parent=strap)
    # 高壓端子座與端子
    s = p["hvTerminalSeatSize"]
    seat = box(ctx, "HV_TerminalSeat", tuple(s), (0.9, 0, s[2] / 2), "Insulator", "insulator", parent=root)
    cyl(ctx, "HV_Terminal", p["hvTerminalDiameter"] / 2, 0.05, (0, 0, s[2] / 2 + 0.025), segments=10, mat="Metal", color="copper", parent=seat)
    for k, x in enumerate((-0.035, 0.035)):
        cyl(ctx, f"HV_Bolt_{k + 1}", 0.006, 0.02, (x, 0, s[2] / 2 + 0.01), segments=6, mat="Metal", color="steel", parent=seat)
    # 控制線接頭與防塵蓋
    conn = cyl(ctx, "Ctrl_Connector", p["ctrlConnectorDiameter"] / 2, p["ctrlConnectorLength"], (1.2, 0, 0.03), axis="X", segments=12, mat="Dark", color="dark", parent=root)
    cyl(ctx, "CtrlCap", p["ctrlConnectorDiameter"] / 2 + 0.003, 0.015, (p["ctrlConnectorLength"] / 2 + 0.008, 0, 0), axis="X", segments=12, mat="Dark", color="rubber", parent=conn)
    # Parked 電纜段（單位長，Unity 縮放）
    cyl(ctx, "Cable_Parked", p["parkedCableDiameter"] / 2, p["parkedCableLength"], (1.5, 0, 0.015), axis="Y", segments=8, mat="Dark", color="rubber", parent=root)
    # 分流線（只做外觀）
    cyl(ctx, "Shunt_Visual", 0.008, 0.4, (1.8, 0, 0.1), axis="Y", segments=6, mat="Metal", color="copper", parent=root)
    return root


def main():
    a = args()
    with open(a["params"], encoding="utf-8") as f:
        p = json.load(f)
    common.clear_scene()
    tmp = tempfile.mkdtemp(prefix="genpan_")
    pal = MiniPalette()
    img = pal.p.image(os.path.join(tmp, "Palette_PanRigging.png"))
    ctx = common.Ctx(common.materials(img), pal)
    root = build(ctx, p)
    bpy.context.view_layer.update()
    objs = [o for o in bpy.data.objects if o.type == "MESH"]
    tris = sum(common.triangles(o) for o in objs)
    if tris > MAX_TRIS:
        raise RuntimeError(f"三角面 {tris} 超過 {MAX_TRIS}")
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = root
    path = os.path.join(tmp, "PAN_Rigging.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, object_types={"MESH"}, mesh_smooth_type="FACE",
                             add_leaf_bones=False, bake_anim=False, path_mode="STRIP")
    os.makedirs(a["out"], exist_ok=True)
    shutil.copyfile(path, os.path.join(a["out"], "PAN_Rigging.fbx"))
    shutil.rmtree(tmp, ignore_errors=True)
    report = {"blender": bpy.app.version_string, "triangles": tris, "limit": MAX_TRIS,
              "objects": [{"name": o.name, "triangles": common.triangles(o)} for o in objs]}
    os.makedirs(os.path.dirname(a["report"]) or ".", exist_ok=True)
    with open(a["report"], "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    print("[gen_pan_rigging] OK", tris, "triangles")


try:
    main()
except SystemExit:
    raise
except Exception as e:
    import traceback
    traceback.print_exc()
    print("[gen_pan_rigging] FAILED:", e)
    sys.exit(1)
