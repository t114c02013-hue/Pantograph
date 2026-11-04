"""
Quest Touch Plus 控制器：WebXR Input Profiles GLB → Unity 用 FBX（資深建模師 B，v1.0，2026-11-04）

來源：immersive-web/webxr-input-profiles packages/assets/profiles/meta-quest-touch-plus/{left,right}.glb
授權：MIT（Copyright 2019 Amazon），隨附 ThirdParty/WebXRInputProfiles/LICENSE.md。模型原樣顯示，不改皮、不用於宣傳。

執行（Blender 3.6 以上；也可用 pip 的 bpy 模組）：
  blender -b --factory-startup -P gen_controller_fbx.py -- --in <glb 資料夾> --out <輸出資料夾>
輸出：CTRL_TouchPlus_L.fbx、CTRL_TouchPlus_R.fbx、ControllerModelReport.json

座標約定：
  GLB 的 root 等於 WebXR grip space，和 OpenXR grip pose 是同一個定義：原點在掌心，-Z 朝前，+Y 朝上，右手座標。
  Unity OpenXR 會把 OpenXR 的 (x,y,z) 轉成 Unity 的 (x,y,-z)。
  Unity 匯入 FBX 時會把 X 取負號。所以匯出前先把整組繞 Y 轉 180°，(x,y,z)→(-x,y,-z)，匯入 Unity 後就變成 (x,y,-z)。
  這樣 prefab 根節點直接掛在 XRI 控制器的 grip 姿態上，Transform 全 0，不需要再補旋轉。
  （如果頭盔實測方向不對，只要調 prefab 的 Visual_Offset，不必重跑腳本。）
"""
import bpy, sys, os, json, math, argparse, hashlib
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--in", dest="indir", required=True)
ap.add_argument("--out", dest="outdir", required=True)
args = ap.parse_args(argv)
os.makedirs(args.outdir, exist_ok=True)

def gl(x, y, z):
    """glTF 座標（公尺）→ Blender 座標（glTF 匯入器的換算：(x,y,z)→(x,-z,y)）"""
    return Vector((x, -z, y))

# 標示錨點：相對於按鍵 *_pressed_value 節點的偏移（glTF 座標，公尺，EX，頭盔實測後再調）
# side = +1 表示左手，-1 表示右手（右手是左手的 X 鏡像）
def label_offsets(side):
    return {
        "LBL_Trigger": ("xr_standard_trigger_pressed_value",    (0.0, -0.025, -0.020)),
        "LBL_Grip":    ("xr_standard_squeeze_pressed_value",    (side * 0.030, 0.0, 0.0)),
        "LBL_Stick":   ("xr_standard_thumbstick_pressed_value", (0.0, 0.035, 0.0)),
    }
BUTTONS = {
    "L": {"LBL_X": "x_button_pressed_value", "LBL_Y": "y_button_pressed_value"},
    "R": {"LBL_A": "a_button_pressed_value", "LBL_B": "b_button_pressed_value"},
}
BUTTON_LABEL_UP = 0.030   # A/B/X/Y 標示放在按鍵正上方 3 cm

def world_bbox(objs):
    lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
    return lo, hi

def add_empty(name, loc, parent):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "PLAIN_AXES"; e.empty_display_size = 0.005
    bpy.context.scene.collection.objects.link(e)
    e.parent = parent
    e.matrix_world = Matrix.Translation(loc)
    return e

report = {"blender": bpy.app.version_string, "files": {}}
for hand, fname in (("L", "left.glb"), ("R", "right.glb")):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    src = os.path.join(args.indir, fname)
    bpy.ops.import_scene.gltf(filepath=src)
    objs = {o.name: o for o in bpy.data.objects}
    root = objs["root"]
    side = 1 if hand == "L" else -1
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]

    # 錨點（在旋轉之前，以 glTF 座標加入）
    body = objs["controller_mesh"]
    lo, hi = world_bbox([body])
    # Poke_Tip：控制器最前端（Blender +Y = glTF -Z），X、Z 取機身中心
    poke = Vector(((lo.x + hi.x) / 2, hi.y, (lo.z + hi.z) / 2))
    add_empty("Poke_Tip", poke, root)
    # Attach_Tool：握把前下方，掌心往前 5 cm、往下 2 cm（EX）
    add_empty("Attach_Tool", gl(0.0, -0.020, -0.050), root)
    for lbl, (node, off) in label_offsets(side).items():
        add_empty(lbl, objs[node].matrix_world.translation + gl(*off), root)
    for lbl, node in BUTTONS[hand].items():
        add_empty(lbl, objs[node].matrix_world.translation + gl(0, BUTTON_LABEL_UP, 0), root)
    if hand == "L":
        # GLB 沒有選單鍵網格。選單鍵在搖桿靠掌心側下方，位置先用推估值（EX）
        st = objs["xr_standard_thumbstick_pressed_value"].matrix_world.translation
        add_empty("LBL_Menu", st + gl(0.018, 0.030, 0.022), root)

    # 整組繞 glTF Y（Blender Z）轉 180°，再套用到所有子物件
    root.rotation_mode = "XYZ"
    root.matrix_world = Matrix.Rotation(math.pi, 4, "Z") @ root.matrix_world
    root.name = f"CTRL_TouchPlus_{hand}"

    out = os.path.join(args.outdir, f"CTRL_TouchPlus_{hand}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=out, use_selection=False,
        object_types={"MESH", "EMPTY"},          # 按鍵的 min/max/value 節點是空物件，一定要匯出
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False,
        path_mode="COPY", embed_textures=True)

    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)
    anchors = {o.name: [round(v, 4) for v in o.matrix_world.translation]
               for o in bpy.data.objects if o.name.startswith(("LBL_", "Poke_", "Attach_"))}
    report["files"][os.path.basename(out)] = {
        "source": fname,
        "source_sha256": hashlib.sha256(open(src, "rb").read()).hexdigest(),
        "fbx_sha256": hashlib.sha256(open(out, "rb").read()).hexdigest(),
        "triangles": tris,
        "nodes": sorted(o.name for o in bpy.data.objects),
        "anchors_blender_world_after_rot": anchors,
    }

with open(os.path.join(args.outdir, "ControllerModelReport.json"), "w", encoding="utf-8") as f:
    json.dump(report, f, ensure_ascii=False, indent=1)
print("done", list(report["files"]))
