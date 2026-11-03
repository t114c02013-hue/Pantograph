using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Parts.Common;
using PantographSim.Recording;
using PantographSim.Worksite;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1 真實高度場景（T1）：車頂段、檢修平台（護欄、活動欄、跨板）、樓梯、駕駛室、背景，與放在上面的設備。
    /// 每個場景件存成獨立 prefab（Assets/_Project/Modules/Worksite/Prefabs/ENV_*），底下有 Visual／Walkable／Collision／Anchors；
    /// 之後換正式模型只換 Visual。所有尺寸與位置讀 RoofSceneDef／StationSet，不寫死座標。
    /// </summary>
    public static class RoofSiteBuilder
    {
        public const string PrefabFolder = "Assets/_Project/Modules/Worksite/Prefabs";
        public const string MonitorTexturePath = "Assets/_Project/Content/Textures/RT_PanHeadMonitor.renderTexture";

        public class Result
        {
            public Transform env, roof, platform, stair, cab, backdrop;
            public readonly Dictionary<string, Transform> ids = new Dictionary<string, Transform>();
            public readonly List<(string name, Transform t)> layout = new List<(string, Transform)>();
            public readonly List<Bounds> obstacles = new List<Bounds>();   // 車頂不可走物件（已外擴）
            public Bounds union;
            public readonly List<Bounds> footprints = new List<Bounds>();   // a6：會動的關節在升降全程的頂點範圍（世界）
            public float stripTop;
            public Vector3 roofStation;
            public readonly List<Transform> equipment = new List<Transform>();   // 設備 prefab（工具箱、工具架、螺栓盤）
        }

        /// <summary>設備包成場景件：content 進 Visual、支架進 Collision、位置進 Anchors（之後換模型只換 Visual）。</summary>
        static Transform Wrap(Result res, string name, Transform content, params GameObject[] supports)
        {
            var p = Piece(res.env, name);
            p.root.position = content.position;
            content.SetParent(p.visual, true);
            foreach (var s in supports) s.transform.SetParent(p.col, true);
            Anchor(p.anchors, "Anchor_" + name.Replace("ENV_", ""), content.position, Vector3.zero);
            res.equipment.Add(p.root);
            return p.root;
        }

        static int L(string name) => LayerMask.NameToLayer(name);

        public const string GeneratedFolder = "Assets/_Project/Worksite/Models/Generated";

        /// <summary>從 Blender 產生的 FBX 複製一個部件（child＝null 時整個模型；名稱找最深一層都可以）到 parent 底下，擺到世界位置 pos。找不到 FBX 就丟例外（先跑 gen_700t.py）。</summary>
        internal static GameObject Fbx(string fbx, string child, Transform parent, Vector3 pos, Vector3? scale = null, string rename = null, float yaw = 0f, string folder = GeneratedFolder)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{fbx}.fbx");
            if (model == null) throw new System.Exception($"找不到 {GeneratedFolder}/{fbx}.fbx：先匯出 car_params.json 並跑 Tools/Blender/gen_700t/gen_700t.py");
            var src = child == null ? model.transform : model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == child);
            if (src == null) throw new System.Exception($"{fbx}.fbx 裡沒有 {child}");
            var go = (GameObject)Object.Instantiate(src.gameObject, parent);
            go.name = rename ?? src.name;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * src.rotation;   // 保留 FBX 節點本身的旋轉（Blender → Unity 的軸向轉換在這裡）
            if (scale.HasValue) go.transform.localScale = scale.Value;
            return go;
        }

        static Material Mat(string key, Color c, bool unlit = false)
        {
            string path = $"{SliceModelBuilder.MaterialFolder}/MAT_R1_{key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(unlit ? "Unlit/Color" : "Standard");
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader; m.color = c;
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat, int layer = -1, bool collider = true, bool visible = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.position = center;
            g.transform.localScale = size;
            if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
            if (!visible) Object.DestroyImmediate(g.GetComponent<MeshRenderer>());
            else g.GetComponent<Renderer>().sharedMaterial = mat;
            if (layer >= 0) g.layer = layer;
            return g;
        }

        /// <summary>只有 BoxCollider 的不可見方塊（Walkable 地板、Boundary／LocomotionBlocker 牆）。</summary>
        static BoxCollider Col(string name, Transform parent, Vector3 min, Vector3 max, string layer)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.position = (min + max) / 2f;
            g.layer = L(layer);
            var c = g.AddComponent<BoxCollider>();
            c.size = max - min;
            return c;
        }

        static Transform Child(Transform parent, string name) { var g = new GameObject(name).transform; g.SetParent(parent, false); return g; }

        static (Transform root, Transform visual, Transform walk, Transform col, Transform anchors) Piece(Transform parent, string name)
        {
            var root = Child(parent, name);
            return (root, Child(root, "Visual"), Child(root, "Walkable"), Child(root, "Collision"), Child(root, "Anchors"));
        }

        static Transform Anchor(Transform anchors, string name, Vector3 pos, Vector3 facing)
        {
            var a = Child(anchors, name);
            a.position = pos;
            if (facing.sqrMagnitude > 0) a.rotation = Quaternion.LookRotation(facing, Vector3.up);
            return a;
        }

        static void SavePrefab(Transform root)
        {
            Directory.CreateDirectory(PrefabFolder);
            PrefabUtility.SaveAsPrefabAssetAndConnect(root.gameObject, $"{PrefabFolder}/{root.name}.prefab", InteractionMode.AutomatedAction);
        }

        static void NoShadow(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
        }

        /// <summary>矩形 [a] 減掉矩形 [b]（XZ 平面），回傳剩下的矩形（最多 4 塊）。</summary>
        public static List<Rect> Subtract(Rect a, Rect b)
        {
            var list = new List<Rect>();
            if (!a.Overlaps(b)) { list.Add(a); return list; }
            float x0 = Mathf.Max(a.xMin, b.xMin), x1 = Mathf.Min(a.xMax, b.xMax);
            if (b.yMin > a.yMin) list.Add(Rect.MinMaxRect(a.xMin, a.yMin, a.xMax, b.yMin));
            if (b.yMax < a.yMax) list.Add(Rect.MinMaxRect(a.xMin, b.yMax, a.xMax, a.yMax));
            float y0 = Mathf.Max(a.yMin, b.yMin), y1 = Mathf.Min(a.yMax, b.yMax);
            if (x0 > a.xMin) list.Add(Rect.MinMaxRect(a.xMin, y0, x0, y1));
            if (x1 < a.xMax) list.Add(Rect.MinMaxRect(x1, y0, a.xMax, y1));
            return list.Where(r => r.width > 0.01f && r.height > 0.01f).ToList();
        }

        static Rect XZ(Bounds b) => Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);

        public static Result Build(RoofSceneDef d, StationSet stations, Dictionary<string, Material> mats, GameObject rig, Transform panHead, Transform headClamp,
            List<ToolDefinition> tools, StringBuilder sb)
        {
            var res = new Result();
            var env = new GameObject("Worksite").transform;
            res.env = env;
            var mBody = Mat("carBody", new Color(0.78f, 0.80f, 0.82f));
            var mWalk = Mat("walkway", new Color(0.28f, 0.30f, 0.32f));
            var mWarn = Mat("warning", new Color(1f, 0.82f, 0.05f));
            var mRail = Mat("rail", new Color(0.95f, 0.65f, 0.1f));
            var mGrating = Mat("grating", new Color(0.42f, 0.44f, 0.46f));
            var mInsul = Mat("insulator", new Color(0.55f, 0.32f, 0.2f));
            var mWall = Mat("cabWall", new Color(0.7f, 0.72f, 0.68f));
            var mBack = Mat("backdrop", new Color(0.6f, 0.62f, 0.64f));
            var mTrack = Mat("track", new Color(0.25f, 0.24f, 0.22f));
            float roofY = d.roofFloorY;

            // ---------- 集電弓聯集範圍（降弓∪升弓）、滑板頂面 ----------
            var mech = rig.GetComponent<PantographSim.Parts.Mechanism.PantographMechanism>();
            float lift = mech.Lift;
            mech.SetLift(0f);
            var union = SliceSceneBuilder.RendererBounds(rig);
            res.stripTop = new[] { "L", "R" }.Select(s => panHead.Find("CarbonStrip_" + s)).Where(t => t != null)
                .SelectMany(t => t.GetComponentsInChildren<Renderer>()).Max(r => r.bounds.max.y) - roofY;
            mech.SetLift(1f); union.Encapsulate(SliceSceneBuilder.RendererBounds(rig));
            mech.SetLift(lift);
            res.union = union;
            var baseR = rig.transform.Find("Base").GetComponent<Renderer>().bounds;
            // a6：不可走範圍改用各關節自己的頂點範圍（LowerArm／UpperArm／PanHead 在 5 個升降姿態的聯集）；底架不動，用自己的頂點範圍
            sb.AppendLine("車頂升降範圍（各剛體段自己的頂點範圍，5 個姿態）：");
            res.footprints.AddRange(PanGeometry.SweptFootprints(mech, 5, sb).Values);
            var baseV = PanGeometry.VertexBounds(PanGeometry.OwnRenderers(rig.transform, "Base")).Value;
            DeriveStand(d, sb);   // G2：台架外框依 LoweredTightBounds 推導，tp.stand.* 跟著更新（要在站點錨點之前）

            // ---------- ENV_RoofSection ----------
            var roof = Piece(env, "ENV_RoofSection");
            res.roof = roof.root;
            // R1.5a：車頂可走 |X| ≤ roofWalkHalfWidth（車寬 3.38 時 0.80）
            float hw = d.carBodyHalfWidth, walkHalf = d.roofWalkHalfWidth, z0 = d.roofSectionZMin, z1 = d.roofSectionZMax;
            // a6 第一節 8（方案 A，鐵道裁定）：底架兩側（底架前後緣各外擴 roofBaseWalkZMargin）Walkable 放寬到 roofBaseWalkHalfWidth；警示紅線在 roofBaseWarningX
            float zb0 = d.roofBaseWalkZMin, zb1 = baseV.max.z + d.roofBaseWalkZMargin, wide = d.roofBaseWalkHalfWidth;   // D36：前端到 roofBaseWalkZMin（避開降弓羊角）
            d.roofBaseWalkZMax = Mathf.Round(zb1 * 1000f) / 1000f; EditorUtility.SetDirty(d);
            foreach (int sgn in new[] { -1, 1 })
                foreach (var (x, a, b) in new[] { (walkHalf, z0, zb0), (d.roofBaseWarningX, zb0, zb1), (walkHalf, zb1, z1) })
                    Box("WarningLine", roof.visual, new Vector3(sgn * x, roofY + 0.002f, (a + b) / 2f), new Vector3(0.05f, 0.004f, b - a), mWarn, collider: false);

            // ---------- ENV_CarBody：700T 三節（共用 Blender 產生的 ENV_CarBody.fbx；中間那節有集電弓，弓在 Z 0） ----------
            var cars = Piece(env, "ENV_CarBody");
            for (int k = -1; k <= 1; k++)
            {
                float cz = d.MiddleCarCenterZ + k * (d.carLength + d.carGap);
                Fbx("ENV_CarBody", null, cars.visual, new Vector3(0f, roofY, cz), rename: k == 0 ? "Car_Middle" : k < 0 ? "Car_South" : "Car_North");
                Anchor(cars.anchors, k == 0 ? "Anchor_Car_Middle" : k < 0 ? "Anchor_Car_South" : "Anchor_Car_North", new Vector3(0f, roofY, cz), Vector3.forward);
            }
            NoShadow(cars.root);
            res.equipment.Add(cars.root);

            // 絕緣子（底架下 4 根，高 pantographBaseHeight）與車頂電纜（母線）
            // 絕緣子：位置＝集電弓底架的螺栓孔 BaseHole_n（PantographAssemblyBuilder 同一套算法），回寫 RoofSceneDef.insulatorPositions
            var insulators = new List<Bounds>();
            var insP = Piece(roof.root, "Roof_Insulators");   // 巢狀 prefab：Insulator_1..4／InsulatorTop_1..4
            var holes = PantographAssemblyBuilder.BaseHoles(baseR);
            if (d.insulatorPositions == null || d.insulatorPositions.Length != 4) d.insulatorPositions = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                var top = new Vector3(holes[i].x, roofY + d.insulatorHeight, holes[i].z);
                var ins = Fbx("ENV_RoofEquipment", "Insulator", insP.visual, new Vector3(top.x, roofY + d.insulatorHeight / 2f, top.z), rename: "Insulator_" + (i + 1));
                var t = new GameObject("InsulatorTop_" + (i + 1)).transform;
                t.SetParent(ins.transform, false); t.position = top;
                // 增補 7 第 4 點（model-spec §7.7.1b）：collider 貼合視覺頂點、不外擴——頂面＝InsulatorTop_n，側面＝最大傘裙半徑的圓柱（凸 MeshCollider，平頂）
                var vb = PanGeometry.VertexBounds(ins.GetComponentsInChildren<Renderer>()).Value;
                float rad = Mathf.Max(vb.extents.x, vb.extents.z), h = top.y - roofY;
                var b = new Bounds(new Vector3(top.x, roofY + h / 2f, top.z), new Vector3(2f * rad, h, 2f * rad));
                insulators.Add(b);
                var icol = new GameObject("InsulatorCollision");
                icol.transform.SetParent(insP.col, false);
                var cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
                icol.transform.position = b.center; icol.transform.localScale = new Vector3(2f * rad / cyl.bounds.size.x, h / cyl.bounds.size.y, 2f * rad / cyl.bounds.size.z);   // 照內建圓柱網格的實際大小縮放
                icol.layer = L(SceneLayers.Boundary);
                var mc = icol.AddComponent<MeshCollider>(); mc.sharedMesh = cyl; mc.convex = true;
                Anchor(insP.anchors, "Anchor_Insulator_" + (i + 1), top, Vector3.zero);
                d.insulatorPositions[i] = top;
            }
            EditorUtility.SetDirty(d);
            SavePrefab(insP.root);

            // 母線（沿 X busbarX 往 +Z，寬 busbarWidth）＋高壓電纜端；避雷器；車體接地座；控制線接頭
            var busP = Piece(roof.root, "Roof_Busbar");
            var busMin = new Vector3(d.busbarX - d.busbarWidth / 2f, roofY, baseR.max.z);
            var busMax = new Vector3(d.busbarX + d.busbarWidth / 2f, roofY + 0.10f, z1);
            Fbx("ENV_RoofEquipment", "Busbar", busP.visual, new Vector3(d.busbarX, roofY + 0.06f, (busMin.z + busMax.z) / 2f), new Vector3(d.busbarWidth / 0.08f, 1f, busMax.z - busMin.z));
            var busbar = new Bounds((busMin + busMax) / 2f, busMax - busMin);
            var hv = Fbx("ENV_RoofEquipment", "HV_Cable_End", busP.visual, new Vector3(d.busbarX, roofY + 0.09f, busMin.z + 0.10f));
            hv.transform.Find("HV_Cable_Parked")?.gameObject.SetActive(false);   // 本輪只顯示 Connected
            hv.AddComponent<BoxCollider>();
            hv.AddComponent<InteractionPointMarker>().Configure("ip.roof.hv_cable_ground", "高壓電纜端接地掛點");
            res.ids["ip.roof.hv_cable_ground"] = hv.transform;
            var arr = Fbx("ENV_RoofEquipment", "Arrester", busP.visual, new Vector3(d.arrester.x, roofY + 0.275f, d.arrester.z));
            var arrB = new Bounds(new Vector3(d.arrester.x, roofY + 0.3f, d.arrester.z), new Vector3(0.14f, 0.6f, 0.14f));
            SavePrefab(busP.root);
            // D34：車體接地座在外肩（不在 Walkable 上、不挖洞）：0.10 m 見方焊接小平台＋接地螺樁＋黃綠標示；Clamp_Point 在螺樁頂（+X 軸朝 +X）
            var gndP = Piece(roof.root, "Roof_CarBodyGround");
            float padY = d.RoofSurfaceY(d.carBodyGround.x), pad = d.carBodyGroundPadSize;
            var seatC = new Vector3(d.carBodyGround.x, padY + 0.01f, d.carBodyGround.z);
            var seat = Box("Roof_CarBodyGround", gndP.visual, seatC, new Vector3(pad, 0.02f, pad), mats["steel"], collider: false);
            var stud = SliceModelBuilder.Prim(PrimitiveType.Cylinder, "GroundStud", gndP.visual, seatC + Vector3.up * (0.01f + d.carBodyGroundStudHeight / 2f), new Vector3(0.024f, d.carBodyGroundStudHeight / 2f, 0.024f), mats["steel"]);
            Object.DestroyImmediate(stud.GetComponent<Collider>());
            Box("GroundMark", gndP.visual, seatC + Vector3.up * 0.0105f, new Vector3(pad * 0.9f, 0.001f, pad * 0.9f), mats["groundStripe"], collider: false);
            var clampPt = new GameObject("Clamp_Point").transform;
            clampPt.SetParent(gndP.visual, false);
            clampPt.SetPositionAndRotation(seatC + Vector3.up * (0.01f + d.carBodyGroundStudHeight), Quaternion.identity);
            clampPt.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.04f, 0.04f, 0.04f);
            clampPt.gameObject.AddComponent<InteractionPointMarker>().Configure("ip.roof.carbody_ground", "車體接地座");
            res.ids["ip.roof.carbody_ground"] = clampPt;
            // #2 短接地線：沿 Z 從接地座往 +X 拉到底架 −X 邊
            float wx0 = seatC.x + pad / 2f, wx1 = baseR.min.x;
            Box("GroundWire_2", gndP.visual, new Vector3((wx0 + wx1) / 2f, d.RoofSurfaceY((wx0 + wx1) / 2f) + 0.01f, seatC.z), new Vector3(wx1 - wx0, 0.01f, 0.01f), mats["ground"], collider: false);
            SavePrefab(gndP.root);
            var workP = Piece(roof.root, "Car_Roof_Work");
            var ctrl = Fbx("ENV_RoofEquipment", "CtrlCable_End", workP.visual, new Vector3(d.ctrlCableEnd.x, roofY + d.ctrlCableEnd.y, d.ctrlCableEnd.z));
            ctrl.transform.Find("CtrlCable_Parked")?.gameObject.SetActive(false);
            SavePrefab(workP.root);

            // 不可走範圍（增補 6 第 7 點 A）：會動的臂／弓頭／連桿（升降全程頂點範圍）外擴 movingCarveInflate；不會動的底架、絕緣子只挖自己的頂點範圍（staticCarveInflate）；母線、避雷器外擴 obstacleMargin
            float m = d.obstacleMargin;
            Bounds Grow(Bounds b, float k) { b.Expand(new Vector3(2 * k, 0, 2 * k)); return b; }
            var insulatorV = insulators;   // 和 InsulatorCollision 同一個範圍（最大傘裙半徑的圓柱），挖除邊才不會和 collider 差幾 mm
            var moving = res.footprints.Select(b => Grow(b, d.movingCarveInflate)).ToList();
            var statics = insulatorV.Append(baseV).Select(b => Grow(b, d.staticCarveInflate)).ToList();
            var obstacles = moving.Concat(new[] { busbar, arrB }.Select(b => Grow(b, m))).Concat(statics).ToList();
            res.obstacles.AddRange(obstacles);
            var walk = new List<Rect> { Rect.MinMaxRect(-walkHalf, z0, walkHalf, z1), Rect.MinMaxRect(walkHalf, zb0, wide, zb1), Rect.MinMaxRect(-wide, zb0, -walkHalf, zb1) };
            foreach (var o in obstacles) walk = walk.SelectMany(r => Subtract(r, XZ(o))).ToList();
            int wi = 0;
            foreach (var r in walk)
            {
                Col($"Walkable_{wi++}", roof.walk, new Vector3(r.xMin, roofY - 0.1f, r.yMin), new Vector3(r.xMax, roofY, r.yMax), SceneLayers.Walkable);
                Box("Walkway", roof.visual, new Vector3(r.center.x, roofY + 0.004f, r.center.y), new Vector3(r.width - 0.04f, 0.008f, r.height - 0.04f), mWalk, collider: false);
            }
            // 不可走的地方用 LocomotionBlocker 擋住（不然會從挖空處掉下去）；車頂邊界牆 X＝±walkHalf，底架兩側那段在 ±roofBaseWalkHalfWidth（活動欄那一段留開口，由閘門控制）
            foreach (var o in obstacles)
            {
                var r = XZ(o);
                r = Rect.MinMaxRect(Mathf.Max(r.xMin, -wide), Mathf.Max(r.yMin, z0), Mathf.Min(r.xMax, wide), Mathf.Min(r.yMax, z1));
                // 不會動零件的擋牆叫 Blocker_Static：「離不可見牆」不查它，改由第16項 (i) 的 staticPartClearance 保證
                if (r.width > 0.01f && r.height > 0.01f) Col(statics.Contains(o) ? "Blocker_Static" : "Blocker", roof.col, new Vector3(r.xMin, roofY, r.yMin), new Vector3(r.xMax, roofY + d.roofWallHeight, r.yMax), SceneLayers.LocomotionBlocker);
            }
            float gz0 = d.gateCenterZ - d.gateWidth / 2f, gz1 = d.gateCenterZ + d.gateWidth / 2f, wh = roofY + d.roofWallHeight;
            Col("EdgeWall_+X", roof.col, new Vector3(walkHalf, roofY, z0), new Vector3(walkHalf + 0.05f, wh, zb0), SceneLayers.Boundary);
            Col("EdgeWall_+X_Base", roof.col, new Vector3(wide, roofY, zb0), new Vector3(wide + 0.05f, wh, zb1), SceneLayers.Boundary);
            Col("EdgeWall_+X_C", roof.col, new Vector3(walkHalf, roofY, zb1), new Vector3(walkHalf + 0.05f, wh, z1), SceneLayers.Boundary);
            Col("EdgeWall_-X_A", roof.col, new Vector3(-walkHalf - 0.05f, roofY, z0), new Vector3(-walkHalf, wh, gz0), SceneLayers.Boundary);
            if (zb0 > gz1) Col("EdgeWall_-X_B", roof.col, new Vector3(-walkHalf - 0.05f, roofY, gz1), new Vector3(-walkHalf, wh, zb0), SceneLayers.Boundary);
            // 放寬帶前端伸進活動欄開口（Z gz0…gz1）時，那一段的車頂邊在 −roofBaseWalkHalfWidth：固定擋牆（GapBlocker 只管 zb0 以前）
            if (zb0 < gz1)
            {
                var slice = Col("EdgeWall_-X_GateSlice", roof.col, new Vector3(-wide - 0.05f, roofY, zb0), new Vector3(-wide, wh, gz1), SceneLayers.Boundary);
                // 看得見的擋欄 ENV_GateSliceGuard（model-spec v3.3 §4）：寬度＝擋牆範圍，網格不比 collider 更往開口內伸；沒有 FBX 時用黃黑方塊暫代（高 1.0、厚 0.05）
                var sb0 = slice.bounds; var at = new Vector3(sb0.center.x, roofY, sb0.center.z);
                if (AssetDatabase.LoadAssetAtPath<GameObject>($"{GeneratedFolder}/ENV_GateSliceGuard.fbx") != null) Fbx("ENV_GateSliceGuard", null, roof.visual, at);
                else Box("ENV_GateSliceGuard", roof.visual, at + Vector3.up * (d.gateGuardHeight / 2f), new Vector3(0.05f, d.gateGuardHeight, sb0.size.z), mWarn, collider: false);
            }
            Col("EdgeWall_-X_Base", roof.col, new Vector3(-wide - 0.05f, roofY, zb0), new Vector3(-wide, wh, zb1), SceneLayers.Boundary);
            Col("EdgeWall_-X_C", roof.col, new Vector3(-walkHalf - 0.05f, roofY, zb1), new Vector3(-walkHalf, wh, z1), SceneLayers.Boundary);
            foreach (int sgn in new[] { -1, 1 })   // 放寬那段前後的轉角牆
            {
                float xa = sgn > 0 ? walkHalf : -wide - 0.05f, xb = sgn > 0 ? wide + 0.05f : -walkHalf;
                if (sgn > 0 || zb0 >= gz1) Col(sgn > 0 ? "EdgeWall_+X_Step0" : "EdgeWall_-X_Step0", roof.col, new Vector3(xa, roofY, zb0 - 0.05f), new Vector3(xb, wh, zb0), SceneLayers.Boundary);   // −X 側前端在活動欄開口內：由 GapBlocker 擋
                Col(sgn > 0 ? "EdgeWall_+X_Step1" : "EdgeWall_-X_Step1", roof.col, new Vector3(xa, roofY, zb1), new Vector3(xb, wh, zb1 + 0.05f), SceneLayers.Boundary);
            }
            Col("EndWall_-Z", roof.col, new Vector3(-walkHalf, roofY, z0 - 0.05f), new Vector3(walkHalf, wh, z0), SceneLayers.LocomotionBlocker);
            Col("EndWall_+Z", roof.col, new Vector3(-walkHalf, roofY, z1), new Vector3(walkHalf, wh, z1 + 0.05f), SceneLayers.LocomotionBlocker);
            sb.AppendLine($"車頂：可走區 {walk.Count} 塊（|X|≤{walkHalf:0.00}，底架兩側 Z {zb0:0.000}…{zb1:0.000} 放寬到 |X|≤{wide:0.00}、紅線 {d.roofBaseWarningX:0.00}；挖掉會動的段升降全程頂點範圍外擴 {d.movingCarveInflate} m、母線與避雷器外擴 {m} m、底架與絕緣子頂點範圍外擴 {d.staticCarveInflate} m）；降弓後滑板頂面在車頂上 {res.stripTop:0.000} m");
            foreach (var (o, i) in obstacles.Select((o, i) => (o, i))) sb.AppendLine($"  挖除 {i}：X {o.min.x:0.000}…{o.max.x:0.000}、Z {o.min.z:0.000}…{o.max.z:0.000}");

            // ---------- ENV_InspectionPlatform（活動欄、跨板） ----------
            var plat = Piece(env, "ENV_InspectionPlatform");
            res.platform = plat.root;
            float px1 = d.PlatformInnerX, px0 = d.PlatformOuterX, pz0 = d.platformZMin, pz1 = d.platformZMax, py = d.platformFloorY;
            Box("Grating", plat.visual, new Vector3((px0 + px1) / 2f, py - 0.03f, (pz0 + pz1) / 2f), new Vector3(px1 - px0, 0.06f, pz1 - pz0), mGrating, collider: false);
            Col("Walkable_Platform", plat.walk, new Vector3(px0, py - 0.1f, pz0), new Vector3(px1, py, pz1), SceneLayers.Walkable);
            for (float z = pz0; z <= pz1 + 0.01f; z += 1.5f)
                foreach (float x in new[] { px0 + 0.05f, px1 - 0.05f })
                    Box("Leg", plat.visual, new Vector3(x, (py + d.groundY) / 2f, z), new Vector3(0.08f, py - d.groundY, 0.08f), mats["steel"], collider: false);
            void Rail(string name, Vector3 a, Vector3 b)
            {
                var dir = b - a; float len = dir.magnitude;
                var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                foreach (float h in new[] { d.guardrailHeight, d.guardrailHeight / 2f })
                {
                    var bar = Box(name + "_Bar", plat.visual, (a + b) / 2f + Vector3.up * h, new Vector3(0.04f, 0.04f, len), mRail, collider: false);
                    bar.transform.rotation = rot;
                }
                for (float t = 0; t <= len + 0.01f; t += 1.0f)
                    Box(name + "_Post", plat.visual, a + dir.normalized * Mathf.Min(t, len) + Vector3.up * d.guardrailHeight / 2f, new Vector3(0.04f, d.guardrailHeight, 0.04f), mRail, collider: false);
                var c = Col(name, plat.col, Vector3.zero, new Vector3(0.05f, d.guardrailHeight, len), SceneLayers.Boundary);
                c.transform.position = (a + b) / 2f + Vector3.up * d.guardrailHeight / 2f;
                c.transform.rotation = rot;
                c.center = Vector3.zero;
            }
            Rail("Rail_Outer", new Vector3(px0, py, pz0), new Vector3(px0, py, pz1));
            Rail("Rail_End+Z_StairMouth", new Vector3(px0, py, pz1), new Vector3(px1, py, pz1));   // D28：鋼梯在 +Z 端
            Rail("Rail_End-Z", new Vector3(px0, py, pz0), new Vector3(px1, py, pz0));
            Rail("Rail_CarSide_A", new Vector3(px1, py, pz0), new Vector3(px1, py, gz0));
            Rail("Rail_CarSide_B", new Vector3(px1, py, gz1), new Vector3(px1, py, pz1));
            // 活動欄（開＝Boundary 停用）與跨板（開＝Walkable 啟用）；車頂邊牆開口用 LocomotionBlocker 補（閘門關時擋住）
            var gateRoot = Child(plat.root, "ENV_PlatformGate");
            var gateVis = new GameObject("GateLeaf").transform; gateVis.SetParent(gateRoot, false); gateVis.position = new Vector3(px1, py, gz0);
            foreach (float h in new[] { d.guardrailHeight, d.guardrailHeight / 2f })
                Box("GateBar", gateVis, new Vector3(px1, py + h, (gz0 + gz1) / 2f), new Vector3(0.04f, 0.04f, gz1 - gz0), mWarn, collider: false);
            var gateCol = Col("GateBoundary", gateRoot, new Vector3(px1 - 0.025f, py, gz0), new Vector3(px1 + 0.025f, py + d.guardrailHeight, gz1), SceneLayers.Boundary);
            var bridgeRoot = Child(plat.root, "ENV_GateBridge");
            // 增補 7 第 8 點（組長）：跨板寬要完全落在車頂側開口內（開口＝gz0 到 EdgeWall_-X_GateSlice），不能壓在擋牆後面
            float bridgeZ1 = Mathf.Min(gz1, zb0);
            var bridgeVis = Box("BridgePlate", bridgeRoot, new Vector3((px1 - walkHalf) / 2f, roofY - 0.02f, (gz0 + bridgeZ1) / 2f), new Vector3(-walkHalf - px1, 0.04f, bridgeZ1 - gz0), mGrating, collider: false);
            var bridgeCol = Col("Walkable_Bridge", bridgeRoot, new Vector3(px1, roofY - 0.1f, gz0), new Vector3(-walkHalf, roofY, bridgeZ1), SceneLayers.Walkable);
            var gapCol = Col("GapBlocker", bridgeRoot, new Vector3(-walkHalf - 0.05f, roofY, gz0), new Vector3(-walkHalf, roofY + d.roofWallHeight, Mathf.Min(gz1, zb0)), SceneLayers.LocomotionBlocker);   // a6：zb0 以後是底架放寬帶，邊在 EdgeWall_-X_GateSlice
            var gate = gateRoot.gameObject.AddComponent<PlatformGate>();
            gate.Setup(gateCol, gateVis.gameObject, bridgeCol, bridgeVis, gapCol);
            res.ids["env.platform_gate"] = gateRoot;

            // 平台上的設備錨點
            // R1.5a：位置都相對平台邊（RoofSceneDef.FromInner／FromOuter），車寬改了自動跟著變
            var aLift = Anchor(plat.anchors, "Anchor_LiftPanel", d.FromInner(d.liftPanelFromInner), Vector3.right);
            var aBus = Anchor(plat.anchors, "Anchor_GroundBusbar", d.FromInner(d.groundBusbarFromInner), Vector3.right);
            var aTester = Anchor(plat.anchors, "Anchor_TesterSelfCheck", d.FromInner(d.testerFromInner), Vector3.right);
            var aHarness = Anchor(plat.anchors, "Anchor_HarnessPoint", d.FromInner(d.platformAnchorFromInner), Vector3.right);
            var aRack = Anchor(plat.anchors, "Anchor_ToolRack", d.FromOuter(d.toolRackFromOuter), Vector3.right);
            var aPole = Anchor(plat.anchors, "Anchor_PoleRack", d.FromOuter(d.poleRackFromOuter), Vector3.right);
            var aDock = Anchor(plat.anchors, "Anchor_PendantDock", d.FromOuter(d.pendantDockFromOuter), Vector3.right);
            var aArrester = Anchor(plat.anchors, "Anchor_FallArrester", d.FromInner(d.fallArresterFromInner), Vector3.right);
            var aHook = Anchor(plat.anchors, "Anchor_RoofAnchorHook", d.FromInner(d.roofAnchorHookFromInner), Vector3.right);
            // 防墜器門架：柱子＋橫臂，防墜器在門架頂，掛鉤（ip.roof.anchor）垂在下方
            var post = d.FromInner(d.fallArresterPostFromInner);
            var postGo = Box("FallArrester_Post", plat.visual, new Vector3(post.x, py + aArrester.position.y / 2f, post.z), new Vector3(0.10f, aArrester.position.y, 0.10f), mats["steel"]);
            postGo.layer = L(SceneLayers.Boundary);
            Box("FallArrester_Arm", plat.visual, new Vector3(post.x, aArrester.position.y, (post.z + aArrester.position.z) / 2f), new Vector3(0.08f, 0.08f, Mathf.Abs(aArrester.position.z - post.z) + 0.1f), mats["steel"], collider: false);
            Box("FallArrester", plat.visual, aArrester.position + Vector3.down * 0.08f, new Vector3(0.16f, 0.16f, 0.16f), mWarn, collider: false);
            // 增補 3：防墜器繩子＝Ø10 mm 單位長細圓柱（橘），從防墜器本體縮放到掛鉤（學員扣上後由 R1.5b 重畫到 D 環，最長 fallArresterLineLength）
            var lineStart = aArrester.position + Vector3.down * 0.16f;
            var lanyard = SliceModelBuilder.Prim(PrimitiveType.Cylinder, "FallArrester_Line", plat.visual, (lineStart + aHook.position) / 2f,
                new Vector3(d.fallArresterLineDiameter, Mathf.Min(Vector3.Distance(lineStart, aHook.position), d.fallArresterLineLength) / 2f, d.fallArresterLineDiameter), mRail);
            Object.DestroyImmediate(lanyard.GetComponent<Collider>());
            lanyard.transform.rotation = Quaternion.FromToRotation(Vector3.up, lineStart - aHook.position);
            // D32：遙控器掛座（掛鉤高 aDock.y）用 pendantDockArmLength 的橫臂固定在外護欄立柱上，面向 +X
            var dock = Fbx("ENV_PendantDock", null, plat.visual, aDock.position);
            dock.name = "PendantDock";
            var dockHook = dock.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Hook");
            if (dockHook != null) dock.transform.position += new Vector3(aDock.position.x - dockHook.position.x + 0.04f, aDock.position.y - dockHook.position.y, aDock.position.z - dockHook.position.z);   // 掛鉤點對到 aDock（高 1.20），板面在掛鉤 −X 0.04
            var armX0 = px0; var armX1 = aDock.position.x;
            Box("PendantDock_Arm", plat.visual, new Vector3((armX0 + armX1) / 2f, aDock.position.y, aDock.position.z), new Vector3(Mathf.Max(0.02f, Mathf.Abs(armX1 - armX0)), 0.04f, 0.04f), mats["steel"], collider: false);
            if (Mathf.Abs(Mathf.Abs(armX1 - armX0) - d.pendantDockArmLength) > 0.02f) sb.AppendLine($"⚠ 遙控器掛座橫臂實際 {Mathf.Abs(armX1 - armX0):0.000} m，資料 {d.pendantDockArmLength} m");
            res.ids["env.pendant_dock"] = dock.transform;
            // a6 第一節 7：日常課程的備品架、待報廢箱擺放點（只放錨點；R1.5b 課程覆寫把物件移過來）；第一節 4：去駕駛室光圈的位置（XrRigBuilder 放標記）
            Anchor(plat.anchors, "Anchor_SpareShelf_Daily", d.spareShelfDaily, Vector3.right);
            Anchor(plat.anchors, "Anchor_ScrapBox_Daily", d.scrapBoxDaily, Vector3.right);
            Anchor(plat.anchors, "Anchor_ToCabMarker", new Vector3(d.toCabMarkerXZ.x, py, d.toCabMarkerXZ.y), Vector3.zero);

            // ---------- ENV_PlatformStair（D28：從平台 +Z 端沿 +Z 下到地面；只有外觀與碰撞，不能走，兩區照舊用傳送） ----------
            var stair = Piece(env, "ENV_PlatformStair");
            res.stair = stair.root;
            int steps = Mathf.CeilToInt((py - d.groundY) / 0.185f);
            float sx0 = (px0 + px1) / 2f;
            for (int i = 0; i < steps; i++)
            {
                float y = py - (i + 1) * (py - d.groundY) / steps;
                var c = new Vector3(sx0, y, pz1 + 0.25f * (i + 0.5f));
                Box("Step", stair.visual, c, new Vector3(0.9f, 0.04f, 0.26f), mGrating, collider: false);
                Col("StepCollision", stair.col, c - new Vector3(0.45f, 0.02f, 0.13f), c + new Vector3(0.45f, 0.02f, 0.13f), SceneLayers.Boundary);
            }
            float stairLen = 0.25f * steps;
            foreach (float hx in new[] { sx0 - 0.47f, sx0 + 0.47f })   // 扶手（斜桿，沿樓梯）
            {
                var a0 = new Vector3(hx, py + d.guardrailHeight * 0.9f, pz1); var a1 = new Vector3(hx, d.groundY + d.guardrailHeight * 0.9f, pz1 + stairLen);
                var bar = Box("Stair_Handrail", stair.visual, (a0 + a1) / 2f, new Vector3(0.04f, 0.04f, Vector3.Distance(a0, a1)), mRail, collider: false);
                bar.transform.rotation = Quaternion.LookRotation(a1 - a0, Vector3.up);
            }

            // ---------- ENV_CabFloor（另一個樓層的小房間，只能傳送） ----------
            var cab = Piece(env, "ENV_CabFloor");
            res.cab = cab.root;
            var cc = d.cabCenter; float half = d.cabSize / 2f, cy = d.cabFloorY;
            Box("Floor", cab.visual, new Vector3(cc.x, cy - 0.03f, cc.z), new Vector3(d.cabSize, 0.06f, d.cabSize), mWalk, collider: false);
            Col("Walkable_Cab", cab.walk, new Vector3(cc.x - half, cy - 0.1f, cc.z - half), new Vector3(cc.x + half, cy, cc.z + half), SceneLayers.Walkable);
            Box("Ceiling", cab.visual, new Vector3(cc.x, cy + d.cabWallHeight, cc.z), new Vector3(d.cabSize + 0.1f, 0.05f, d.cabSize + 0.1f), mWall, collider: false);
            foreach (var (n, c, s) in new[]
            {
                ("Wall-X", new Vector3(cc.x - half - 0.025f, cy + d.cabWallHeight / 2f, cc.z), new Vector3(0.05f, d.cabWallHeight, d.cabSize)),
                ("Wall+X", new Vector3(cc.x + half + 0.025f, cy + d.cabWallHeight / 2f, cc.z), new Vector3(0.05f, d.cabWallHeight, d.cabSize)),
                ("Wall-Z", new Vector3(cc.x, cy + d.cabWallHeight / 2f, cc.z - half - 0.025f), new Vector3(d.cabSize, d.cabWallHeight, 0.05f)),
                ("Wall+Z", new Vector3(cc.x, cy + d.cabWallHeight / 2f, cc.z + half + 0.025f), new Vector3(d.cabSize, d.cabWallHeight, 0.05f)),
            })
            {
                Box(n, cab.visual, c, s, mWall, collider: false);
                Col(n + "_Boundary", cab.col, c - s / 2f, c + s / 2f, SceneLayers.Boundary);
            }
            var cabLight = new GameObject("CabLight").AddComponent<Light>();
            cabLight.transform.SetParent(cab.visual, false);
            cabLight.transform.position = new Vector3(cc.x, cy + d.cabWallHeight - 0.2f, cc.z);
            cabLight.type = LightType.Point; cabLight.range = 3f; cabLight.intensity = 1.2f;
            var aPanel = Anchor(cab.anchors, "Anchor_ControlPanel", new Vector3(cc.x - half + 0.06f, cy + d.cabPanelHeight, cc.z), Vector3.right);

            // ---------- ENV_Backdrop（檢修庫廠房：屋頂桁架下緣 shedTrussY、牆、地面、股道；沒有架空線；不投影，2 個材質） ----------
            var back = Piece(env, "ENV_Backdrop");
            res.backdrop = back.root;
            float shedTop = d.shedTrussY, gy = d.groundY;
            float bx0 = -18f, bx1 = 8f, bz0 = -50f, bz1 = 60f;   // 廠房範圍（EX）：涵蓋三節車、平台、地面維修區、天車軌道
            Box("Ground", back.visual, new Vector3((bx0 + bx1) / 2f, gy - 0.05f, (bz0 + bz1) / 2f), new Vector3(bx1 - bx0, 0.1f, bz1 - bz0), mBack, collider: false);
            Box("TrussChord", back.visual, new Vector3((bx0 + bx1) / 2f, shedTop + 0.1f, (bz0 + bz1) / 2f), new Vector3(bx1 - bx0, 0.2f, bz1 - bz0), mBack, collider: false);
            for (float z = bz0; z <= bz1 + 0.01f; z += 6f)
                Box("Truss", back.visual, new Vector3((bx0 + bx1) / 2f, shedTop + 0.6f, z), new Vector3(bx1 - bx0, 1.0f, 0.15f), mTrack, collider: false);
            foreach (float x in new[] { bx0, bx1 })
            {
                Box("Wall", back.visual, new Vector3(x, (gy + shedTop) / 2f, (bz0 + bz1) / 2f), new Vector3(0.2f, shedTop - gy, bz1 - bz0), mBack, collider: false);
                for (float z = bz0; z <= bz1 + 0.01f; z += 6f)
                    Box("Column", back.visual, new Vector3(x + (x < 0 ? 0.3f : -0.3f), (gy + shedTop) / 2f, z), new Vector3(0.4f, shedTop - gy, 0.4f), mTrack, collider: false);
            }
            foreach (float x in new[] { -0.7175f, 0.7175f })   // 標準軌距 1.435
                Box("Rail", back.visual, new Vector3(x, gy + 0.085f, (bz0 + bz1) / 2f), new Vector3(0.07f, 0.17f, bz1 - bz0), mTrack, collider: false);
            NoShadow(back.root);

            // ---------- 共用車頂站點（建置時依規則移位） ----------
            var inner = stations.Find("tp.roof.inner");
            var rs = new Vector3(inner.positionXZ.x, roofY, inner.positionXZ.y);
            // B 段 3.1：肘側最遠螺栓 > roofBoltMaxDistance 就沿 +Z 每次移 roofShiftStep（最多到 roofShiftMaxZ），每次確認身體膠囊和降弓弓頭（含羊角）淨距
            mech.SetLift(0f);
            var headB = SliceSceneBuilder.RendererBounds(panHead.gameObject);
            mech.SetLift(lift);
            float Far(string side, Vector3 at) => rig.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith($"Bolt_{side}_"))
                .Select(t => Vector2.Distance(new Vector2(t.position.x, t.position.z), new Vector2(at.x, at.z))).DefaultIfEmpty(0f).Max();
            bool Clear(Vector3 at) => Vector2.Distance(new Vector2(at.x, at.z), new Vector2(Mathf.Clamp(at.x, headB.min.x, headB.max.x), Mathf.Clamp(at.z, headB.min.z, headB.max.z))) >= 0.25f + stations.panHeadClearance;
            while (Far("L", rs) > stations.roofBoltMaxDistance && rs.z + stations.roofShiftStep <= stations.roofShiftMaxZ + 1e-4f && Clear(rs + new Vector3(0, 0, stations.roofShiftStep)))
                rs.z += stations.roofShiftStep;
            stations.measuredFarBoltL = Far("L", rs); stations.measuredFarBoltR = Far("R", rs);
            stations.sideStepHintEnabled = stations.measuredFarBoltL > stations.roofBoltMaxDistance;
            EditorUtility.SetDirty(stations);
            res.roofStation = rs;
            // StationAnchor_<id>：站點的 x、z（共用車頂點用移位後的位置）；y 由 XrRigBuilder 往下打 Walkable 決定
            var anchorRoot = Child(env, "StationAnchors");
            // D33：base_w／base_e 的 Z＝底架中心、子站點的 Z＝那支底座螺栓的實測世界 Z（從模型推導，寫回站點資料）
            var holesByN = PantographAssemblyBuilder.BaseHoles(baseR);
            foreach (var (id, z) in new[] { ("tp.roof.base_w", baseV.center.z), ("tp.roof.base_e", baseV.center.z), ("tp.roof.base_e.n", holesByN[2].z), ("tp.roof.base_w.n", holesByN[3].z) })   // .s 固定 roofBaseSouthZ（資料）
                if (stations.Find(id) is StationDef bs) { bs.positionXZ = new Vector2(bs.positionXZ.x, Mathf.Round(z * 1000f) / 1000f); EditorUtility.SetDirty(bs); }
            foreach (var s in stations.stations.Where(x => x != null))
            {
                var xz = s.floor == StationFloor.Roof && (s.id == inner.id || s.sharedWith == inner.id) ? new Vector2(rs.x, rs.z) : s.WorldXZ(d);
                Child(anchorRoot, "StationAnchor_" + s.id).position = new Vector3(xz.x, 0f, xz.y);
            }
            sb.AppendLine($"共用車頂站點：({rs.x:0.00}, {rs.z:0.00})；肘側最遠螺栓 {stations.measuredFarBoltL:0.000} m、外側 {stations.measuredFarBoltR:0.000} m；側移提示 {(stations.sideStepHintEnabled ? "啟用" : "不啟用")}；stripTopAboveRoof={res.stripTop:0.000}");

            // ---------- 設備 ----------
            BuildPlatformEquipment(res, d, mats, aLift, aBus, aTester, aHarness, aRack, headClamp, tools, sb);
            BuildPoleRackAndAnchorHook(res, d, mats, aPole, aHook, tools, sb);
            BuildRoofEquipment(res, d, mats, rs, tools, sb);
            BuildRoofStripMat(res, d, sb);
            BuildCabEquipment(res, d, mats, aPanel, panHead, sb);

            BuildMaintenanceArea(res, d, mats, mWarn, sb);   // R1.5a a4：地面維修區（台架、工作檯）
            BuildCraneAndRigging(res, d, aDock, sb);            // R1.5a a5：天車、遙控器、吊具、工具袋（靜態）
            WireRigging(res, d, sb);                            // R1.5b b2：吊運（集電弓狀態機、對位燈、台架夾具、吊物探測 collider）
            var pieces = new[] { roof.root, plat.root, stair.root, cab.root, back.root }.Concat(res.equipment).ToList();
            foreach (var p in pieces) SavePrefab(p);
            sb.AppendLine("場景件 prefab：ENV_Insulators、" + string.Join("、", pieces.Select(t => t.name)) + $"（{PrefabFolder}）");
            return res;
        }

        // ---------- 平台：升降操作盒（▲▼＋弓位指示燈）、接地母線端子、驗電器自檢器、平台掛點、平台工具架（掛位 1–4）、作業單夾 ----------
        static void BuildPlatformEquipment(Result res, RoofSceneDef d, Dictionary<string, Material> mats, Transform aLift, Transform aBus, Transform aTester, Transform aHarness, Transform aRack,
            Transform headClamp, List<ToolDefinition> tools, StringBuilder sb)
        {
            var env = res.env;
            // 升降操作盒：只有外觀（不擋站點），面朝 −X；▲▼ 是世界空間介面（XrPanelLayout 掛在 xr.mount.lift_panel）
            var box = Box("LiftControlBox", env, aLift.position + new Vector3(-0.04f, 0, 0), new Vector3(0.08f, 0.32f, 0.36f), mats["panel"], collider: false);
            var mount = new GameObject("LiftPanelMount");
            mount.transform.SetParent(box.transform, true);
            mount.transform.SetPositionAndRotation(aLift.position + new Vector3(-0.09f, 0.05f, 0), Quaternion.LookRotation(Vector3.right, Vector3.up));
            mount.AddComponent<RegisteredPoint>().Configure("xr.mount.lift_panel");
            var lamp = SliceModelBuilder.Prim(PrimitiveType.Sphere, "PanIndicator", env, Vector3.zero, Vector3.one * 0.06f, mats["checkPoint"]);
            lamp.transform.position = aLift.position + new Vector3(-0.09f, 0.21f, -0.12f);
            lamp.AddComponent<ControlPanelLamp>().Configure("ip.cab.pan_indicator", "弓位指示燈");
            lamp.GetComponent<ControlPanelLamp>().Setup(lamp.GetComponent<Renderer>());
            lamp.AddComponent<Highlightable>();
            res.ids["ip.cab.pan_indicator"] = lamp.transform;

            // 接地母線端子（黃綠），接地線畫到弓頭接地掛點
            var term = SliceModelBuilder.Prim(PrimitiveType.Cylinder, "GroundTerminal", env, Vector3.zero, new Vector3(0.08f, 0.08f, 0.08f), mats["ground"]);
            term.transform.position = aBus.position + new Vector3(-0.06f, 0, 0);
            SliceModelBuilder.Prim(PrimitiveType.Cylinder, "Stripe", term.transform, new Vector3(0, 0.3f, 0), new Vector3(1.05f, 0.15f, 1.05f), mats["groundStripe"]);
            var termClamp = SliceModelBuilder.Prim(PrimitiveType.Cube, "ClampAttached", term.transform, new Vector3(0, 1.05f, 0), new Vector3(1.4f, 0.25f, 1.4f), mats["groundStripe"]);
            foreach (var col in term.GetComponentsInChildren<Collider>().Where(x => x.gameObject != term)) Object.DestroyImmediate(col);
            var line = term.AddComponent<LineRenderer>();
            line.widthMultiplier = 0.012f; line.sharedMaterial = mats["groundStripe"]; line.enabled = false;
            var terminal = term.AddComponent<GroundTerminal>();
            terminal.Configure(SimIds.GroundTerminal, "平台接地母線端子（黃綠）");
            terminal.Setup(termClamp, headClamp, line);
            Track(termClamp, SimIds.GroundTerminal, ".rod");
            term.AddComponent<Highlightable>();
            res.ids[SimIds.GroundTerminal] = term.transform;

            // 驗電器自檢器
            var tester = SliceModelBuilder.Prim(PrimitiveType.Cube, "DetectorTester", env, Vector3.zero, new Vector3(0.1f, 0.08f, 0.12f), mats["panel"]);
            tester.transform.position = aTester.position + new Vector3(-0.06f, 0, 0);
            var led = SliceModelBuilder.Prim(PrimitiveType.Sphere, "LED", tester.transform, new Vector3(0, 0.6f, 0), new Vector3(0.3f, 0.3f, 0.3f), mats["led"]);
            Object.DestroyImmediate(led.GetComponent<Collider>());
            tester.AddComponent<VoltageTesterStand>().Configure(SimIds.VoltageTester, "驗電器自檢器");
            tester.GetComponent<VoltageTesterStand>().Setup(led.GetComponent<Renderer>());
            tester.AddComponent<Highlightable>();
            res.ids[SimIds.VoltageTester] = tester.transform;

            // 平台安全帶掛點
            var pAnchor = SliceModelBuilder.Prim(PrimitiveType.Cylinder, "PlatformAnchor", env, Vector3.zero, new Vector3(0.05f, 0.08f, 0.05f), mats["steel"]);
            pAnchor.transform.position = aHarness.position + new Vector3(-0.05f, 0, 0);
            pAnchor.AddComponent<AnchorPoint>().Configure("ip.depot.platform_anchor", "平台安全帶掛點");
            res.ids["ip.depot.platform_anchor"] = pAnchor.transform;

            // 作業單（夾在護欄上；VR 由左手夾板 X 鍵開關，這個物件給桌面點選與 CSR_01 的目標）
            var wo = SliceModelBuilder.Prim(PrimitiveType.Cube, "WorkOrder", env, Vector3.zero, new Vector3(0.012f, 0.28f, 0.21f), mats["paper"]);
            wo.transform.position = aLift.position + new Vector3(-0.03f, -0.15f, -0.42f);
            wo.AddComponent<WorkOrder>().Configure(SimIds.WorkOrder, "作業單（點開）");
            wo.AddComponent<Highlightable>();
            res.ids[SimIds.WorkOrder] = wo.transform;

            // 平台工具架：掛位 1–4（上鎖掛牌組、驗電器、接地棒、安全帶），面朝 −X
            // R1.5a（D25）：工具架靠外護欄、面向 +X，只放掛位 1（上鎖掛牌組）、4（安全帶）；2、3 的絕緣桿在長桿桿架
            var rack = BuildStorage("ToolRack_Platform", env, aRack.position, Vector3.right, tools.Where(t => t.hotkey == 1 || t.hotkey == 4).ToList(), mats, "env.tool_rack", "平台工具架（掛位 1、4）", d.toolRackHookTop - aRack.position.y);
            Wrap(res, "ENV_PlatformToolRack", rack);
            res.ids["env.tool_rack"] = rack;
            res.layout.Add(("平台工具架", rack));
            res.layout.Add(("接地母線端子", term.transform));
            sb.AppendLine($"平台：升降操作盒 {aLift.position:F2}、接地端子 {aBus.position:F2}、自檢器 {aTester.position:F2}、平台掛點 {aHarness.position:F2}、工具架 {aRack.position:F2}（掛位 1–4）");
        }

        // ---------- 車頂：工具箱（掛位 5–9＋套筒座）、零件盒、備品架、兩個螺栓盤 ----------
        // shortcut: R1 車頂物件暫留給現有 25 步使用, R1.5b 移除（b3 工具袋、b4 課程覆寫完成後）
        static void BuildRoofEquipment(Result res, RoofSceneDef d, Dictionary<string, Material> mats, Vector3 roofStation, List<ToolDefinition> tools, StringBuilder sb)
        {
            var env = res.env;
            float roofY = d.roofFloorY;
            GameObject Support(Vector3 top, Vector3 size, string name)
            {
                var c = Box(name + "_Support", env, new Vector3(top.x, (roofY + top.y) / 2f, top.z), new Vector3(size.x, top.y - roofY, size.z), mats["steel"]);
                c.layer = LayerMask.NameToLayer(SceneLayers.Boundary);
                return c;
            }

            // 車頂工具箱（面朝 −X）
            var box = BuildStorage("ToolBox_Roof", env, d.roofToolbox, Vector3.left, tools.Where(t => t.hotkey >= 5).ToList(), mats, SimIds.RoofToolbox, "車頂工具箱（掛位 5–9）", 0.4f);
            var boxSupport = Support(d.roofToolbox, new Vector3(0.3f, 0, 1.2f), "ToolBox");
            res.ids[SimIds.RoofToolbox] = box;
            res.layout.Add(("車頂工具箱", box));
            // 套筒盒 SocketCase（a6 增補 7 §7.4）：1/2" 區 10、13、17、19、24（5 格），3/8" 區 13_38（1 格），兩區之間隔板＋不同顏色底墊；
            // 目標節點 TGT_Socket_*（膠囊半徑 1.5 cm），放在掛位 6 旁。套筒是「種類」：裝到扳手上盒裡那顆不消失（R1.5b）
            var slot6 = box.Find("Slot_6");
            if (slot6 != null)
            {
                var seats = Child(box, "SocketCase");
                var origin = slot6.position + new Vector3(-0.04f, -0.2f, -0.12f);
                int i = 0;
                foreach (var (zone, sizes, pad) in new[] { ("1/2\"", new[] { "10", "13", "17", "19", "24" }, mats["steel"]), ("3/8\"", new[] { "13_38" }, mats["checkPoint"]) })
                {
                    float z0 = 0.05f * i;
                    foreach (var size in sizes)
                    {
                        var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                        cap.name = "TGT_Socket_" + size;
                        cap.transform.SetParent(seats, false);
                        cap.transform.position = origin + new Vector3(0f, 0f, 0.05f * i++);
                        cap.transform.localScale = new Vector3(0.03f, 0.02f, 0.03f);
                        cap.GetComponent<Renderer>().sharedMaterial = mats["steel"];
                        var cc = cap.GetComponent<CapsuleCollider>(); cc.isTrigger = true;
                        cap.AddComponent<InteractionPointMarker>().Configure("env.socket_seat_" + size, (size.EndsWith("_38") ? "3/8\" 13" : size) + " mm 套筒座");
                    }
                    var padGo = Box("Pad_" + (zone.StartsWith("1") ? "Half" : "ThreeEighths"), seats, origin + new Vector3(0f, -0.025f, z0 + 0.05f * (sizes.Length - 1) / 2f), new Vector3(0.05f, 0.004f, 0.05f * sizes.Length), pad, collider: false);
                    if (i < 6) Box("Divider", seats, origin + new Vector3(0f, -0.01f, 0.05f * i - 0.025f), new Vector3(0.05f, 0.03f, 0.004f), mats["panel"], collider: false);
                }
            }
            Wrap(res, "ENV_RoofToolbox", box, boxSupport);

            // 零件盒（只收舊滑板）
            Support(d.partsTray, new Vector3(0.4f, 0, 0.3f), "PartsTray");
            var tray = SliceModelBuilder.Prim(PrimitiveType.Cube, "PartsTray", env, Vector3.zero, new Vector3(0.34f, 0.06f, 0.26f), mats["tray"]);
            tray.transform.position = d.partsTray + new Vector3(0, 0.03f, 0);
            var stripIcons = Enumerable.Range(0, 2).Select(i =>
            {
                var g = SliceModelBuilder.Prim(PrimitiveType.Cube, "StripIcon" + i, tray.transform, new Vector3(0, 0.6f, -0.15f + 0.3f * i), new Vector3(0.9f, 0.3f, 0.15f), mats["crack"]);
                Object.DestroyImmediate(g.GetComponent<Collider>()); g.SetActive(false); return g;
            }).ToArray();
            var trayComp = tray.AddComponent<PartsTray>();
            trayComp.Configure(SimIds.PartsTray, "零件盒（舊滑板）");
            trayComp.Setup(new GameObject[0], stripIcons);
            tray.AddComponent<Highlightable>();
            res.ids[SimIds.PartsTray] = tray.transform;
            res.layout.Add(("零件盒", tray.transform));

            // 螺栓盤：共用車頂站點的 +X（R）／−X（L）boltTrayOffset，盤面高 boltTrayHeight；|X| 不能超過可走寬度
            float walkHalf = d.carBodyHalfWidth - d.roofEdgeNoWalk;
            foreach (var (side, sgn) in new[] { ("R", 1f), ("L", -1f) })
            {
                float x = roofStation.x + sgn * d.boltTrayOffset;
                if (Mathf.Abs(x) > walkHalf) x = roofStation.x - sgn * d.boltTrayOffset;
                var top = new Vector3(x, d.boltTrayHeight, roofStation.z);
                var btSupport = Support(top, new Vector3(0.14f, 0, 0.14f), "BoltTray_" + side);
                // 0.25 × 0.18 m、4 格（B 段 2.2）
                var bt = SliceModelBuilder.Prim(PrimitiveType.Cube, "BoltTray_" + side, env, Vector3.zero, new Vector3(0.25f, 0.024f, 0.18f), mats["tray"]);
                bt.transform.position = top + new Vector3(0, 0.012f, 0);
                for (int k = 1; k < 4; k++)
                    Object.DestroyImmediate(SliceModelBuilder.Prim(PrimitiveType.Cube, "Divider" + k, bt.transform, new Vector3(-0.5f + 0.25f * k, 0.75f, 0), new Vector3(0.02f, 0.5f, 0.95f), mats["steel"]).GetComponent<Collider>());
                var icons = Enumerable.Range(0, 4).Select(i =>
                {
                    var g = SliceModelBuilder.Prim(PrimitiveType.Cylinder, "BoltIcon" + i, bt.transform, new Vector3(-0.375f + 0.25f * i, 1.5f, 0), new Vector3(0.12f, 1.2f, 0.16f), mats["steel"]);
                    Object.DestroyImmediate(g.GetComponent<Collider>()); g.SetActive(false); return g;
                }).ToArray();
                var comp = bt.AddComponent<BoltTray>();
                comp.Configure(SimIds.BoltTray(side), (side == "L" ? "肘側" : "外側") + "螺栓盤");
                comp.Setup(side, icons);
                bt.AddComponent<Highlightable>();
                Wrap(res, "ENV_BoltTray_" + side, bt.transform, btSupport);
                res.ids[SimIds.BoltTray(side)] = bt.transform;
            }

            // 備品架（3 條：型號不符、正確、正確但背面裂紋），在車頂支架上
            var shelf = BuildSpareShelf(env, d.spareShelf, mats);
            Support(d.spareShelf - new Vector3(0, 0.12f, 0), new Vector3(0.3f, 0, 0.3f), "SpareShelf");
            res.ids["env.spare_shelf"] = shelf;
            res.layout.Add(("備品架", shelf));

            // R1.5a：車頂安全帶掛點 ip.roof.anchor 改成平台防墜器門架的掛鉤（BuildPoleRackAndAnchorHook）
            sb.AppendLine($"車頂：工具箱 {d.roofToolbox:F2}（掛位 5–9＋套筒座）、零件盒 {d.partsTray:F2}、備品架 {d.spareShelf:F2}、螺栓盤 R/L 在 {roofStation:F2} 的 ±{d.boltTrayOffset} m");
        }

        /// <summary>
        /// a6 第一節 9＋增補 2：車頂暫放墊 ENV_RoofStripMat（env.roof_strip_mat）：FBX 原點在底面，放在 roofStripMatCenter；StripMat_Surface、唯一的 StripMat_Slot_Old 在局部 (0, 墊厚, 0)；
        /// 上方 trigger（墊子範圍、高 roofStripMatTriggerHeight）給 R1.5b 判定滑板在不在墊上。不挖 Walkable、不加 Boundary。存檔時 inactive（日常課程由課程覆寫打開）。
        /// shortcut: 課程覆寫（和 ENV_Catenary 同一列）還沒有，ENV_Catenary 也還沒建, R1.5b b4 做課程覆寫時把這個物件加進日常課程那一列
        /// </summary>
        static void BuildRoofStripMat(Result res, RoofSceneDef d, StringBuilder sb)
        {
            var p = Piece(res.env, "ENV_RoofStripMat");
            var c = d.roofStripMatCenter + Vector3.up * d.roofFloorY;
            p.root.position = c;
            Fbx("ENV_RoofStripMat", null, p.visual, c);
            Anchor(p.anchors, "Anchor_RoofStripMat", c, Vector3.forward);
            foreach (var n in new[] { "StripMat_Surface", "StripMat_Slot_Old" }) Child(p.root, n).localPosition = new Vector3(0f, d.roofStripMatThickness, 0f);
            var trig = new GameObject("StripMat_Trigger");
            trig.transform.SetParent(p.col, false);
            trig.transform.position = c + Vector3.up * (d.roofStripMatThickness + d.roofStripMatTriggerHeight / 2f);
            var bc = trig.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(d.roofStripMatSize.x, d.roofStripMatTriggerHeight, d.roofStripMatSize.y);
            if (LayerMask.NameToLayer("Ignore Raycast") >= 0) trig.layer = LayerMask.NameToLayer("Ignore Raycast");
            p.root.gameObject.AddComponent<InteractionPointMarker>().Configure("env.roof_strip_mat", "車頂暫放墊");
            res.ids["env.roof_strip_mat"] = p.root;
            p.root.gameObject.SetActive(false);
            res.equipment.Add(p.root);
            sb.AppendLine($"車頂暫放墊：中心 {c:F3}、{d.roofStripMatSize.x} × {d.roofStripMatSize.y} m（存檔 inactive，日常課程打開）");
        }

        /// <summary>
        /// R1.5a a4：地面維修區 ENV_MaintenanceArea（X maintMin…maintMax、地面 Y groundY，全區 Walkable、四周 LocomotionBlocker 牆）；
        /// 台架 ENV_PantographStand（env.panto_stand，工具盤 env.panto_stand.tool_tray，台架用螺栓盤在靠 tp.stand.s 的角落）；工作檯 ENV_WorkBench（作業單夾板、3 條新滑板沿 Z 平放）；
        /// 零件盒位置只放空物件 Anchor_PartsTray_Maint（R1.5b 才把 env.parts_tray 移過來，場景裡只能有一個 env.parts_tray）。本輪只放模型和節點，沒有邏輯。
        /// </summary>
        static void BuildMaintenanceArea(Result res, RoofSceneDef d, Dictionary<string, Material> mats, Material mWarn, StringBuilder sb)
        {
            var env = res.env;
            float gy = d.groundY;
            Vector2 a = d.maintMinXZ, b = d.maintMaxXZ;
            var area = Piece(env, "ENV_MaintenanceArea");
            Box("Floor", area.visual, new Vector3((a.x + b.x) / 2f, gy + 0.005f, (a.y + b.y) / 2f), new Vector3(b.x - a.x, 0.01f, b.y - a.y), Mat("maintFloor", new Color(0.36f, 0.38f, 0.40f)), collider: false);
            Col("Walkable_Maintenance", area.walk, new Vector3(a.x, gy - 0.1f, a.y), new Vector3(b.x, gy + 0.01f, b.y), SceneLayers.Walkable);
            const float wallH = 1.2f, t = 0.05f;
            Col("Edge_W", area.col, new Vector3(a.x - t, gy, a.y), new Vector3(a.x, gy + wallH, b.y), SceneLayers.LocomotionBlocker);
            Col("Edge_E", area.col, new Vector3(b.x, gy, a.y), new Vector3(b.x + t, gy + wallH, b.y), SceneLayers.LocomotionBlocker);
            Col("Edge_S", area.col, new Vector3(a.x, gy, a.y - t), new Vector3(b.x, gy + wallH, a.y), SceneLayers.LocomotionBlocker);
            Col("Edge_N", area.col, new Vector3(a.x, gy, b.y), new Vector3(b.x, gy + wallH, b.y + t), SceneLayers.LocomotionBlocker);
            // 平台和維修區之間的地面通道：只做視覺（兩區用傳送移動）
            Box("Aisle", area.visual, new Vector3((d.PlatformOuterX + b.x) / 2f, gy + 0.004f, (a.y + b.y) / 2f), new Vector3(b.x - d.PlatformOuterX, 0.008f, b.y - a.y), Mat("aisle", new Color(0.46f, 0.48f, 0.50f)), collider: false);
            // 台架外 standClearance 的黃色走道線
            var sc = new Vector3(d.standCenterXZ.x, gy, d.standCenterXZ.y);
            var fc = new Vector3(d.StandFrameCenterXZ.x, gy, d.StandFrameCenterXZ.y);   // 外框中心（增補 4：可能偏離台架中心）
            float lx = d.standSizeXZ.x / 2f + d.standClearance, lz = d.standSizeXZ.y / 2f + d.standClearance;
            foreach (var (pos, size) in new[] { (new Vector3(fc.x, gy + 0.012f, fc.z + lz), new Vector3(2 * lx, 0.004f, 0.06f)), (new Vector3(fc.x, gy + 0.012f, fc.z - lz), new Vector3(2 * lx, 0.004f, 0.06f)),
                                                (new Vector3(fc.x + lx, gy + 0.012f, fc.z), new Vector3(0.06f, 0.004f, 2 * lz)), (new Vector3(fc.x - lx, gy + 0.012f, fc.z), new Vector3(0.06f, 0.004f, 2 * lz)) })
                Box("WalkwayLine", area.visual, pos, size, mWarn, collider: false);
            Anchor(area.anchors, "Anchor_PartsTray_Maint", new Vector3(d.partsTrayMaintXZ.x, gy, d.partsTrayMaintXZ.y), Vector3.zero);   // 紅色「待報廢」零件盒的位置（R1.5b）
            res.equipment.Add(area.root);

            // 台架
            var standP = Piece(env, "ENV_PantographStand");
            standP.root.position = sc;
            var standVis = Fbx("ENV_PantographStand", null, standP.visual, sc);
            // F6：外框（四周與四角立柱）頂面 standFrameTopAboveGround；G2：支撐座、定位銷、外框尺寸由建置從實測推導（FitStand）
            var seatSize = FitStand(standVis.transform, d, sc, sb);
            Col("StandFrame", standP.col, fc - new Vector3(d.standSizeXZ.x / 2f, 0f, d.standSizeXZ.y / 2f), fc + new Vector3(d.standSizeXZ.x / 2f, d.standFrameTopAboveGround, d.standSizeXZ.y / 2f), "Default");
            Col("StandSeatBlock", standP.col, new Vector3(sc.x - seatSize.x / 2f, gy, sc.z - seatSize.y / 2f), new Vector3(sc.x + seatSize.x / 2f, gy + d.standTopAboveGround, sc.z + seatSize.y / 2f), "Default");
            standP.root.gameObject.AddComponent<InteractionPointMarker>().Configure("env.panto_stand", "集電弓台架");
            res.ids["env.panto_stand"] = standP.root;
            var tray = standVis.GetComponentsInChildren<Transform>(true).First(x => x.name == "ToolTray");
            tray.gameObject.AddComponent<BoxCollider>();
            tray.gameObject.AddComponent<InteractionPointMarker>().Configure("env.panto_stand.tool_tray", "台架工具盤");
            res.ids["env.panto_stand.tool_tray"] = tray;
            Anchor(standP.anchors, "Anchor_StandTop", new Vector3(sc.x, d.StandTopY, sc.z), Vector3.forward);
            foreach (var n in standVis.GetComponentsInChildren<Transform>(true).Where(x => x.name.StartsWith("StandSeat_")))
                Anchor(standP.anchors, "Anchor_" + n.name, n.position, Vector3.zero);
            res.equipment.Add(standP.root);

            // 工作檯（檯面沿 Z）＋作業單夾板＋3 條新滑板沿 Z 平放
            var benchP = Piece(env, "ENV_WorkBench");
            var wc = new Vector3(d.workbenchCenterXZ.x, gy, d.workbenchCenterXZ.y);
            benchP.root.position = wc;
            Fbx("ENV_WorkBench", null, benchP.visual, wc, yaw: 90f);
            Col("BenchBody", benchP.col, wc - new Vector3(0.3f, 0f, 0.75f), wc + new Vector3(0.3f, 0.9f, 0.75f), "Default");
            for (int i = 0; i < 3; i++)
                Box("NewStrip_" + (i + 1), benchP.visual, wc + new Vector3(-0.18f + 0.18f * i, 0.91f, 0.1f), new Vector3(0.074f, 0.016f, 1.1f), mats["shelf"], collider: false);
            Anchor(benchP.anchors, "Anchor_BenchTop", wc + Vector3.up * 0.9f, Vector3.right);
            res.equipment.Add(benchP.root);
            sb.AppendLine($"地面維修區：X {a.x}…{b.x}、Z {a.y}…{b.y}、地面 {gy}；台架中心 ({sc.x}, {sc.z})、頂面 Y {d.StandTopY:0.00}；工作檯 ({wc.x}, {wc.z})；零件盒位置 Anchor_PartsTray_Maint ({d.partsTrayMaintXZ.x}, {d.partsTrayMaintXZ.y})");
        }

        /// <summary>
        /// a6 G2（model-spec 7.3、7.5）：台架 FBX（Blender 照 2.0 × 2.6 建）依實測調整：支撐座＝BaseHole 的 X、Z 間距＋standSeatMarginXZ，中心對準台架中心，頂面不變；
        /// 定位銷 StandSeat_n 放在 BaseHole_n（PAN_Root 局部，原點＝底架中心）；外框、立柱、對位燈、工具盤、螺栓盤依 standSizeXZ 往外推。回傳支撐座 X、Z。
        /// </summary>
        static Vector2 FitStand(Transform vis, RoofSceneDef d, Vector3 sc, StringBuilder sb)
        {
            var all = vis.GetComponentsInChildren<Transform>(true).ToList();
            Transform N(string n) => all.First(x => x.name == n);
            var root = GameObject.Find(PantographAssemblyBuilder.RootName).transform;
            var holes = Enumerable.Range(1, 4).Select(i => root.InverseTransformPoint(root.Find("PAN_Base/BaseHole_" + i).position)).ToArray();
            var seatSize = new Vector2(holes[1].x - holes[0].x + d.standSeatMarginXZ.x, holes[3].z - holes[0].z + d.standSeatMarginXZ.y);
            float oldW = N("Frame_Short_N").GetComponent<Renderer>().bounds.size.x, oldL = N("Frame_Long_E").GetComponent<Renderer>().bounds.size.z;
            var seat = N("SupportSeat");
            var seatKids = seat.Cast<Transform>().ToList();
            foreach (var k in seatKids) k.SetParent(vis, true);
            seat.position = new Vector3(sc.x, seat.position.y, sc.z);
            ResizeWorld(seat, Vector3.right, seatSize.x);
            ResizeWorld(seat, Vector3.forward, seatSize.y);
            float S(float v) => v < 0f ? -1f : 1f;
            foreach (var k in seatKids)
            {
                var o = k.position - sc;
                if (k.name.StartsWith("StandSeat_")) { int n = int.Parse(k.name.Substring(10)); k.position = new Vector3(sc.x + holes[n - 1].x, k.position.y, sc.z + holes[n - 1].z); }
                else if (k.name.StartsWith("SeatPost")) k.position = new Vector3(sc.x + S(o.x) * (seatSize.x / 2f - 0.08f), k.position.y, sc.z + S(o.z) * (seatSize.y / 2f - 0.08f));
                else if (k.name.StartsWith("StandClamp_")) k.position = new Vector3(sc.x + S(o.x) * (seatSize.x / 2f + 0.05f), k.position.y, sc.z);
            }
            // 外框四邊從 FBX 原本的邊（台架中心 ± 原尺寸/2）移到新的邊（外框中心 ± standSizeXZ/2），每一側各自的位移
            var fc = new Vector3(d.StandFrameCenterXZ.x, sc.y, d.StandFrameCenterXZ.y);
            float Shift(float o, float c0, float c1, float oldHalf, float newHalf) => Mathf.Abs(o) <= 0.05f ? c1 - c0 : (c1 + S(o) * newHalf) - (c0 + S(o) * oldHalf);
            foreach (var t in all.Where(x => x.name.StartsWith("Frame_") || x.name == "Leg" || x.name.StartsWith("AlignLight_") || x.name == "ToolTray" || x.name == "BoltTray"))
            {
                var o = t.position - sc;
                t.position += new Vector3(Shift(o.x, sc.x, fc.x, oldW / 2f, d.standSizeXZ.x / 2f), 0f, Shift(o.z, sc.z, fc.z, oldL / 2f, d.standSizeXZ.y / 2f));
                if (t.name.StartsWith("Frame_Long_")) ResizeWorld(t, Vector3.forward, d.standSizeXZ.y);
                if (t.name.StartsWith("Frame_Short_")) ResizeWorld(t, Vector3.right, d.standSizeXZ.x);
            }
            sb.AppendLine($"台架（G2）：支撐座 {seatSize.x:0.000} × {seatSize.y:0.000} m（BaseHole 間距 {holes[1].x - holes[0].x:0.000} × {holes[3].z - holes[0].z:0.000} ＋ {d.standSeatMarginXZ}）；外框 {d.standSizeXZ.x:0.000} × {d.standSizeXZ.y:0.000} m（FBX 原本 {oldW:0.00} × {oldL:0.00}）");
            return seatSize;
        }

        /// <summary>把 t（含子物件）的網格沿世界軸 worldAxis 的實際長度改成 length（量目前的 Renderer 範圍、乘上對應局部軸的縮放；網格不必是單位長）。</summary>
        public static void ResizeWorld(Transform t, Vector3 worldAxis, float length)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float cur = Vector3.Dot(b.size, new Vector3(Mathf.Abs(worldAxis.x), Mathf.Abs(worldAxis.y), Mathf.Abs(worldAxis.z)).normalized);
            if (cur < 1e-5f) return;
            var local = t.InverseTransformDirection(worldAxis.normalized);
            var s = t.localScale;
            float ax = Mathf.Abs(local.x), ay = Mathf.Abs(local.y), az = Mathf.Abs(local.z), k = length / cur;
            if (ax >= ay && ax >= az) s.x *= k; else if (ay >= az) s.y *= k; else s.z *= k;
            t.localScale = s;
        }

        /// <summary>
        /// a6 G2＋增補 4：台架外框＝降弓整組 LoweredTightBounds（PAN_Root 局部，原點＝底架中心＝台架中心）的 X、Z 範圍兩端各外擴 standFrameMargin（不加擺動餘裕）；
        /// 範圍前後不對稱，外框中心跟著偏（standFrameOffsetXZ）。寫回 RoofSceneDef，再重算 tp.stand.*。
        /// </summary>
        static void DeriveStand(RoofSceneDef d, StringBuilder sb)
        {
            var t = PantographAssemblyBuilder.Tight;
            float R(float v) => Mathf.Round(v * 1000f) / 1000f;
            d.standSizeXZ = new Vector2(R(t.size.x + 2f * d.standFrameMargin), R(t.size.z + 2f * d.standFrameMargin));
            d.standFrameOffsetXZ = new Vector2(R(t.center.x), R(t.center.z));
            EditorUtility.SetDirty(d);
            R1Data.StandStations(R1Data.Stations);
            sb.AppendLine($"台架外框（G2／增補 4）：LoweredTightBounds X {t.min.x:0.000}…{t.max.x:0.000}、Z {t.min.z:0.000}…{t.max.z:0.000}，兩端各外擴 {d.standFrameMargin}、不加擺動餘裕 → {d.standSizeXZ.x:0.000} × {d.standSizeXZ.y:0.000} m，外框中心偏 {d.standFrameOffsetXZ}");
        }

        /// <summary>把 t 沿世界軸 worldAxis 的長度設成 length（FBX 節點帶著軸向轉換的旋轉，要找對應的局部軸）。單位長 1 m 的網格用。</summary>
        static void StretchWorld(Transform t, Vector3 worldAxis, float length)
        {
            var local = t.InverseTransformDirection(worldAxis.normalized);
            var s = t.localScale;
            float ax = Mathf.Abs(local.x), ay = Mathf.Abs(local.y), az = Mathf.Abs(local.z);
            if (ax >= ay && ax >= az) s.x = length; else if (ay >= az) s.y = length; else s.z = length;
            t.localScale = s;
        }

        public const string LoadProbeLayer = "LoadProbe";

        /// <summary>D40：台車底面（Crane_Hoist、Crane_Trolley 自己的網格頂點最低 Y）、吊鉤組頂面（Crane_HookBlock 自己的網格頂點最高 Y，不含吊梁等子物件），回傳（間距, 台車底面, 吊鉤組頂面）。</summary>
        public static (float gap, float hoistBottom, float hookTop) HookTopGap(Transform hookBlock, Transform trolley)
        {
            float Ys(Transform t, bool max)
            {
                var mf = t.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return max ? float.MinValue : float.MaxValue;
                var ys = mf.sharedMesh.vertices.Select(v => t.TransformPoint(v).y);
                return max ? ys.Max() : ys.Min();
            }
            var hoist = trolley.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Crane_Hoist");
            float bottom = Mathf.Min(Ys(trolley, false), hoist != null ? Ys(hoist, false) : float.MaxValue);
            float top = Ys(hookBlock, true);
            return (bottom - top, bottom, top);
        }

        /// <summary>
        /// R1.5b b2（crane-software v0.5 §2.3、§3.1；a5 留下的第 2 件事）：PAN_Root 掛 PanAssyRigging；整體 Box 改成吊物探測 collider（LoadProbe 層、trigger、只在 Lifted 時開，
        /// 碰撞矩陣只和 Default、Boundary 互動，不和手、工具、清潔布互動）；台架夾具 env.panto_stand.clamp_n；落點：台架＝StandSeat_n 的 XZ、高度＝台架頂面＋BaseHole 在 PAN_Root 的高度，
        /// 車頂＝InsulatorTop_n；對位燈：台架 AlignLight_n（依最近的定位銷編號）、車頂在每個絕緣子旁加一顆。
        /// </summary>
        static void WireRigging(Result res, RoofSceneDef d, StringBuilder sb)
        {
            var rigging = R1Data.Rigging;
            var root = GameObject.Find(PantographAssemblyBuilder.RootName).transform;
            var holes = Enumerable.Range(1, 4).Select(i => root.Find("PAN_Base/BaseHole_" + i)).ToArray();
            int probeLayer = EnsureLayer(LoadProbeLayer);
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(probeLayer, i, !(i == 0 || i == LayerMask.NameToLayer(SceneLayers.Boundary)));   // 寫進專案的碰撞矩陣
            var box = root.GetComponent<BoxCollider>();
            box.isTrigger = true; box.enabled = false; root.gameObject.layer = probeLayer;
            var standP = res.env.Find("ENV_PantographStand");
            var standAll = standP.GetComponentsInChildren<Transform>(true);
            var pins = new Transform[4];
            for (int n = 1; n <= 4; n++)
            {
                var seat = standAll.First(x => x.name == "StandSeat_" + n);
                var t = new GameObject("SeatTarget_" + n).transform; t.SetParent(standP.Find("Anchors"), false);
                t.position = new Vector3(seat.position.x, d.StandTopY + (holes[n - 1].position.y - root.position.y), seat.position.z);
                pins[n - 1] = t;
            }
            var lightsAll = standAll.Where(x => x.name.StartsWith("AlignLight_")).ToList();
            var standLights = pins.Select(p => lightsAll.OrderBy(l => Vector2.Distance(new Vector2(l.position.x, l.position.z), new Vector2(p.position.x, p.position.z))).First().GetComponent<Renderer>()).ToArray();
            for (int k = 1; k <= 2; k++)
            {
                var c = standAll.FirstOrDefault(x => x.name == "StandClamp_" + k);
                if (c == null) continue;
                if (c.GetComponent<Collider>() == null) c.gameObject.AddComponent<BoxCollider>();
                c.gameObject.AddComponent<InteractionPointMarker>().Configure("env.panto_stand.clamp_" + k, $"台架夾具 {k}");
                res.ids["env.panto_stand.clamp_" + k] = c;
            }
            var tops = Enumerable.Range(1, 4).Select(i => res.env.GetComponentsInChildren<Transform>(true).First(x => x.name == "InsulatorTop_" + i)).ToArray();
            var roofLights = tops.Select(t =>
            {
                var g = Box("AlignLight_Roof_" + t.name.Substring(t.name.Length - 1), t, t.position + new Vector3(0f, 0.05f, 0.12f), new Vector3(0.04f, 0.04f, 0.04f), Mat("alignLight", new Color(0.2f, 0.9f, 0.3f)), collider: false);
                return g.GetComponent<Renderer>();
            }).ToArray();
            foreach (var r in standLights.Concat(roofLights)) r.enabled = false;   // 吊物在落點上方 indicatorRange 內才亮
            var crane = Object.FindObjectOfType<PantographSim.Parts.Mechanism.CraneMechanism>(true);
            crane.SetRigging(rigging);
            var latch = crane.GetComponentsInChildren<Transform>(true).First(x => x.name == "Hook_Latch");   // LF_01 舌片：part.crane.hook（Operate 切換 part.crane.hook.latched）
            if (latch.GetComponent<Collider>() == null) latch.gameObject.AddComponent<BoxCollider>();
            latch.gameObject.AddComponent<InteractionPointMarker>().Configure(PantographSim.Parts.Mechanism.PanAssyRigging.HookId, "吊鉤安全舌片");
            res.ids[PantographSim.Parts.Mechanism.PanAssyRigging.HookId] = latch;
            var rig = root.GetComponent<PantographSim.Parts.Mechanism.PanAssyRigging>() ?? root.gameObject.AddComponent<PantographSim.Parts.Mechanism.PanAssyRigging>();
            rig.Setup(rigging, crane, holes, pins, tops, standLights, roofLights, box);
            sb.AppendLine($"吊運（b2）：LoadProbe 層 {probeLayer}（只和 Default、Boundary 互動）；台架落點 Y {pins[0].position.y:0.000}；對位燈 台架 {standLights.Length}、車頂 {roofLights.Length}；吸附 {rigging.snapRadius} m");
        }

        /// <summary>沒有這個 layer 就加在第一個空的使用者 layer（8 以後）。</summary>
        static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { layers.GetArrayElementAtIndex(i).stringValue = name; tm.ApplyModifiedProperties(); return i; }
            throw new System.Exception("沒有空的 layer 可以放 " + name);
        }

        /// <summary>
        /// R1.5a a5：天車 ENV_OverheadCrane（env.crane；走行軌道 X craneRunwayEastX／WestX、Z ±craneRunwayHalfLength；橋停在 Z craneParkZ，小車 X craneParkTrolleyX，吊鉤在最高點，不擋站點視線）、
        /// 遙控器 ENV_CraneRemote（env.crane_pendant，掛在 PendantDock，不能拿）、吊具 ENV_Sling（env.sling_1..4，掛在維修區的吊具架）、工具袋 ENV_ToolPouch（env.tool_bag，零件袋 env.tool_bag.bolt_pouch，放在工作檯上）。
        /// 都只有模型和節點，沒有邏輯。
        /// </summary>
        static void BuildCraneAndRigging(Result res, RoofSceneDef d, Transform aDock, StringBuilder sb)
        {
            var env = res.env;
            var craneP = Piece(env, "ENV_OverheadCrane");
            var crane = Fbx("ENV_OverheadCrane", null, craneP.visual, new Vector3(d.craneRunwayEastX, d.craneRailY, 0f));
            Transform N(string n) => crane.GetComponentsInChildren<Transform>(true).First(x => x.name == n);
            var runE = N("Crane_Runway_E"); var runW = N("Crane_Runway_W");
            if (runW.position.x > runE.position.x) crane.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * crane.transform.rotation;   // 西軌要在 −X 側
            foreach (var r in new[] { N("Crane_Runway_E"), N("Crane_Runway_W") }) StretchWorld(r, Vector3.forward, 2f * d.craneRunwayHalfLength);
            var bridge = N("Crane_Bridge");
            bridge.position = new Vector3(bridge.position.x, bridge.position.y, d.craneParkZ);
            var trolley = N("Crane_Trolley");
            trolley.position = new Vector3(d.craneParkTrolleyX, trolley.position.y, d.craneParkZ);
            var hook = N("Crane_HookBlock");
            hook.position = new Vector3(d.craneParkTrolleyX, bridge.position.y - d.hookTopBelowBridge, d.craneParkZ);
            // D40（鐵道）：吊鉤上限位＝吊鉤組頂點最高點到台車底面（Crane_Hoist／Crane_Trolley 頂點最低點）留 hookTopClearance；在原本的最高點已經不夠就往下移
            var profile = R1Data.Crane;
            var (gap, hoistBottom, hookTop) = HookTopGap(hook, trolley);
            float pivot0 = hook.position.y;
            if (gap < profile.hookTopClearance - 1e-4f) hook.position += Vector3.down * (profile.hookTopClearance - gap);
            profile.hookYMax = Mathf.Round(hook.position.y * 1000f) / 1000f; EditorUtility.SetDirty(profile);
            hook.position = new Vector3(hook.position.x, profile.hookYMax, hook.position.z);
            var (gap1, _, hookTop1) = HookTopGap(hook, trolley);
            if (gap1 < profile.hookTopClearance - 1e-3f) throw new Exception($"D40：吊鉤上限位 {profile.hookYMax} 時吊鉤組頂面到台車底面只有 {gap1:0.000} m（要 ≥ {profile.hookTopClearance}）");
            sb.AppendLine($"D40 吊鉤上限位：台車底面（Crane_Hoist／Crane_Trolley 頂點最低）Y {hoistBottom:0.000}；吊鉤 pivot 在 {pivot0:0.000} 時吊鉤組頂面（頂點最高）Y {hookTop:0.000}、間距 {gap:0.000} m → hookYMax（pivot）{profile.hookYMax:0.000}，頂面 Y {hookTop1:0.000}、間距 {gap1:0.000} m（≥ {profile.hookTopClearance}）");
            var anchor = N("Crane_RopeAnchor"); var rope = N("Crane_Rope");
            float ropeLen = Mathf.Max(0.05f, anchor.position.y - hook.position.y - 0.2f);
            rope.position = new Vector3(hook.position.x, hook.position.y + 0.2f + ropeLen / 2f, hook.position.z);
            StretchWorld(rope, Vector3.up, ropeLen);
            craneP.root.gameObject.AddComponent<InteractionPointMarker>().Configure("env.crane", "天車");
            res.ids["env.crane"] = craneP.root;
            // a6 G2：吊梁 ENV_LiftingBeam 兩端吊耳間距＝吊點 X 間距（PantographAssemblyBuilder.BeamSpan），梁長＝間距＋beamExtraLength
            var beam = N("ENV_LiftingBeam");
            var eyeL = N("Beam_SlingEye_L"); var eyeR = N("Beam_SlingEye_R");
            var axis = (eyeR.position - eyeL.position).normalized;
            foreach (var e in new[] { eyeL, eyeR }) e.SetParent(beam.parent, true);
            float beamLen = PantographAssemblyBuilder.BeamSpan + PantographAssemblyBuilder.MassTable().beamExtraLength;
            ResizeWorld(beam, axis, beamLen);
            var bc = beam.GetComponent<Renderer>().bounds.center;
            eyeL.position = new Vector3(bc.x, eyeL.position.y, bc.z) - axis * PantographAssemblyBuilder.BeamSpan / 2f;
            eyeR.position = new Vector3(bc.x, eyeR.position.y, bc.z) + axis * PantographAssemblyBuilder.BeamSpan / 2f;
            foreach (var e in new[] { eyeL, eyeR }) e.SetParent(beam, true);
            sb.AppendLine($"吊梁 ENV_LiftingBeam（G2）：梁長 {beam.GetComponent<Renderer>().bounds.size.x:0.000} m（沿 X）、吊耳間距 {Vector3.Distance(eyeL.position, eyeR.position):0.000} m");
            foreach (var n in new[] { "Hook_AttachPoint", "Beam_SlingEye_L", "Beam_SlingEye_R", "Crane_RopeAnchor" }) Anchor(craneP.anchors, "Anchor_" + n, N(n).position, Vector3.zero);
            res.equipment.Add(craneP.root);
            // R1.5b b1：天車機構（讀 ctrl.crane.*）。上限位 CraneProfile.hookYMax 上面已依 D40 量測寫回；晃動只轉吊梁
            var craneRules = StepJsonImporter.LoadAll<SafetyRuleDefinition>(StepJsonImporter.SafetyFolder).Where(x => x.kind == SafetyRuleKind.CranePersonUnderLoad || x.kind == SafetyRuleKind.CraneCollision).ToArray();
            craneP.root.gameObject.AddComponent<PantographSim.Parts.Mechanism.CraneMechanism>().Setup(profile, bridge, trolley, hook, anchor, rope, beam, craneRules);
            sb.AppendLine($"天車機構（b1）：上限位 Y {profile.hookYMax:0.000}、小車 X {profile.trolleyXMin}…{profile.trolleyXMax}、橋架 Z {profile.bridgeZMin}…{profile.bridgeZMax}、安全規則 {craneRules.Length} 條");

            var remote = Fbx("ENV_CraneRemote", null, env, aDock.position + new Vector3(0.06f, -0.1f, 0f));
            remote.AddComponent<InteractionPointMarker>().Configure("env.crane_pendant", "天車遙控器（掛在掛座）");
            res.ids["env.crane_pendant"] = remote.transform;

            var g = d.groundY;
            var rack = Fbx("ENV_SlingRack", null, env, new Vector3(d.slingRackXZ.x, g, d.slingRackXZ.y));
            for (int i = 1; i <= 4; i++)
            {
                var s = Fbx("ENV_Sling", null, rack.transform, new Vector3(d.slingRackXZ.x - 0.24f + 0.16f * (i - 1), g + 1.2f, d.slingRackXZ.y), rename: "env.sling_" + i);
                // F2（a6 吊梁沿 X）：2、3 號接 +X 側吊點、1、4 號接 −X 側，長度由 PantographAssemblyBuilder 依 COG 計算（1 t 扁吊帶規格不變）
                float len = i == 2 || i == 3 ? PantographAssemblyBuilder.SlingLengths.plusX : PantographAssemblyBuilder.SlingLengths.minusX;
                StretchWorld(s.transform, Vector3.up, len);
                s.transform.position = new Vector3(s.transform.position.x, g + 1.2f - len / 2f, s.transform.position.z);
                s.AddComponent<InteractionPointMarker>().Configure("env.sling_" + i, $"吊帶 {i}");
                res.ids["env.sling_" + i] = s.transform;
            }

            var bench = new Vector3(d.workbenchCenterXZ.x, g + 0.92f, d.workbenchCenterXZ.y + 0.45f);
            var pouch = Fbx("ENV_ToolPouch", null, env, bench, yaw: 90f);
            pouch.AddComponent<InteractionPointMarker>().Configure("env.tool_bag", "工具袋");
            res.ids["env.tool_bag"] = pouch.transform;
            var bolts = pouch.GetComponentsInChildren<Transform>(true).First(x => x.name == "BoltBag");
            bolts.gameObject.AddComponent<InteractionPointMarker>().Configure("env.tool_bag.bolt_pouch", "零件袋");
            res.ids["env.tool_bag.bolt_pouch"] = bolts;
            sb.AppendLine($"天車：軌道 X {d.craneRunwayEastX}／{d.craneRunwayWestX}、Z ±{d.craneRunwayHalfLength}，停在 Z {d.craneParkZ}，吊鉤 Y {hook.position.y:0.00}；遙控器在掛座 {aDock.position:F2}；吊帶 4 條在 ({d.slingRackXZ.x}, {d.slingRackXZ.y})；工具袋在工作檯上");
        }

        /// <summary>R1.5a（D25）：長桿桿架 env.pole_rack（掛位 2 驗電器、3 接地棒，ID 不變，直立放），以及防墜器掛鉤 ip.roof.anchor。</summary>
        static void BuildPoleRackAndAnchorHook(Result res, RoofSceneDef d, Dictionary<string, Material> mats, Transform aPole, Transform aHook, List<ToolDefinition> tools, StringBuilder sb)
        {
            var env = res.env;
            var poleVis = Fbx("ENV_PoleRack", null, env, new Vector3(aPole.position.x, d.platformFloorY, aPole.position.z));
            var rack = BuildStorage("PoleRack_Slots", env, aPole.position + new Vector3(0.12f, 0.9f, 0f), Vector3.right, tools.Where(t => t.hotkey == 2 || t.hotkey == 3).ToList(), mats, "env.pole_rack", "長桿桿架（掛位 2、3）", 0.6f);
            poleVis.transform.SetParent(rack, true);
            // D24：驗電器（掛位 2）、接地棒（掛位 3）的外觀改成 insulatedPoleLength 的伸縮絕緣桿＋護環，直立放在桿架上（判定不改；雙手握持是 R1.5b）
            foreach (var slot in rack.Cast<Transform>().Where(t => t.name == "Slot_2" || t.name == "Slot_3").ToList())
            {
                var model = slot.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("Model_"));
                if (model == null) continue;
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                var pole = Fbx("ENV_InsulatedPole", null, rack, new Vector3(slot.position.x + 0.08f, d.platformFloorY + 0.05f, slot.position.z), rename: "InsulatedPole");
                pole.transform.SetParent(model, true);   // 先放在沒有縮放的桿架下再搬進工具模型（保留世界大小），跟著掛位的「在架／拿走」顯示或隱藏
                var len = pole.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }).size.y;
                if (Mathf.Abs(len - d.insulatedPoleLength) > 0.05f) Debug.LogWarning($"[RoofSiteBuilder] 絕緣桿長度 {len:0.00} m 和資料 {d.insulatedPoleLength} m 不同：重跑 Blender");
            }
            Wrap(res, "ENV_PoleRack", rack);
            res.ids["env.pole_rack"] = rack;
            res.layout.Add(("長桿桿架", rack));
            var hook = SliceModelBuilder.Prim(PrimitiveType.Cube, "RoofAnchorHook", env, Vector3.zero, new Vector3(0.05f, 0.08f, 0.05f), mats["steel"]);
            hook.transform.position = aHook.position;
            hook.AddComponent<AnchorPoint>().Configure("ip.roof.anchor", "防墜器掛鉤（車頂作業用）");
            res.ids["ip.roof.anchor"] = hook.transform;
            sb.AppendLine($"平台：長桿桿架 {aPole.position:F2}（掛位 2、3）、防墜器掛鉤 {aHook.position:F2}");
        }

        // ---------- 駕駛室：控制盤（主斷路器、升弓開關、升弓鈕、電動缸電源）、弓位指示燈複本、弓頭監視畫面 ----------
        static void BuildCabEquipment(Result res, RoofSceneDef d, Dictionary<string, Material> mats, Transform aPanel, Transform panHead, StringBuilder sb)
        {
            var env = res.env;
            var panel = SliceModelBuilder.Prim(PrimitiveType.Cube, "ControlPanel", env, Vector3.zero, new Vector3(0.1f, 0.5f, 0.7f), mats["panel"]);
            panel.transform.position = aPanel.position;
            panel.AddComponent<ControlPanel>().Configure("ip.cab.panel", "駕駛室控制盤（點開面板）");
            panel.AddComponent<Highlightable>();
            res.ids["ip.cab.panel"] = panel.transform;
            res.layout.Add(("控制盤", panel.transform));
            int i = 0;
            foreach (var (id, name, mat) in new[] { ("ip.cab.breaker", "主斷路器", "mark"), ("ip.cab.raise_switch", "升弓開關", "arrow"), ("ip.cab.raise_button", "升弓鈕", "measure"), ("ip.cab.cylinder_power", "電動缸電源", "groundStripe") })
            {
                var knob = SliceModelBuilder.Prim(PrimitiveType.Cylinder, "Knob_" + id, panel.transform, new Vector3(0.6f, 0.25f - 0.17f * i++, -0.25f), new Vector3(0.4f, 0.2f, 0.1f), mats[mat]);
                knob.transform.localRotation = Quaternion.Euler(0, 0, 90);
                Object.DestroyImmediate(knob.GetComponent<Collider>());
                knob.AddComponent<SphereCollider>().radius = 0.5f;
                knob.AddComponent<InteractionPointMarker>().Configure(id, name);
                res.ids[id] = knob.transform;
            }
            // 弓位指示燈複本（只顯示，不能點）
            var rep = SliceModelBuilder.Prim(PrimitiveType.Sphere, "PanIndicator_Repeat", panel.transform, new Vector3(0.6f, 0.3f, 0.2f), new Vector3(0.5f, 0.1f, 0.07f), mats["checkPoint"]);
            Object.DestroyImmediate(rep.GetComponent<Collider>());
            rep.AddComponent<ControlPanelLamp>().Configure("ip.cab.pan_indicator_repeat", "弓位指示燈（顯示）");
            rep.GetComponent<ControlPanelLamp>().Setup(rep.GetComponent<Renderer>());
            // 弓頭監視畫面：固定相機對著弓頭 → RenderTexture → 盤面上方的螢幕
            Directory.CreateDirectory(Path.GetDirectoryName(MonitorTexturePath));
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(MonitorTexturePath);
            if (rt == null) { rt = new RenderTexture(512, 288, 16); AssetDatabase.CreateAsset(rt, MonitorTexturePath); }
            var camGo = new GameObject("PanHeadMonitorCam");
            camGo.transform.SetParent(env, false);
            var head = panHead.GetComponent<Renderer>().bounds.center;
            camGo.transform.position = head + new Vector3(-1.4f, 0.9f, -1.6f);
            camGo.transform.rotation = Quaternion.LookRotation(head - camGo.transform.position, Vector3.up);
            var mcam = camGo.AddComponent<Camera>();
            mcam.targetTexture = rt; mcam.fieldOfView = 35f; mcam.nearClipPlane = 0.05f; mcam.clearFlags = CameraClearFlags.SolidColor; mcam.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "PanHeadMonitor";
            screen.transform.SetParent(env, false);
            screen.transform.position = aPanel.position + new Vector3(0.06f, 0.55f, 0);
            screen.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            screen.transform.localScale = new Vector3(0.48f, 0.27f, 1f);
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            var sm = Mat("monitor", Color.white, true);
            sm.shader = Shader.Find("Unlit/Texture"); sm.mainTexture = rt;
            screen.GetComponent<Renderer>().sharedMaterial = sm;
            sb.AppendLine($"駕駛室：控制盤 {aPanel.position:F2}（盤面朝 +X）、指示燈複本、弓頭監視畫面（{MonitorTexturePath}）");
        }

        /// <summary>工具存放處（平台工具架／車頂工具箱）：背板＋掛位（ToolRackSlot，ID env.tool_rack.slot_k 不變）＋簡易工具模型。facing＝掛位朝向（學員那一側）。</summary>
        static Transform BuildStorage(string name, Transform env, Vector3 pos, Vector3 facing, List<ToolDefinition> list, Dictionary<string, Material> mats, string boardId, string label, float height)
        {
            var root = new GameObject(name).transform;
            root.SetParent(env, false);
            root.position = pos;
            root.rotation = Quaternion.LookRotation(facing, Vector3.up);   // 本地 +Z＝朝學員（工具模型在掛位前方）
            float width = Mathf.Max(0.6f, 0.3f * list.Count + 0.1f);
            var board = SliceModelBuilder.Prim(PrimitiveType.Cube, "Board", root, new Vector3(0, height / 2f + 0.05f, -0.04f), new Vector3(width, height + 0.1f, 0.04f), mats["shelf"]);
            board.AddComponent<ToolRackBoard>().Configure(boardId, label + "（點一下查看掛位）");
            int i = 0;
            foreach (var t in list.OrderBy(t => t.hotkey))
            {
                var slotPos = new Vector3(-width / 2f + 0.2f + 0.3f * i++, height / 2f + 0.05f, 0f);
                var slot = SliceModelBuilder.Prim(PrimitiveType.Cube, $"Slot_{t.hotkey}", root, slotPos, new Vector3(0.26f, Mathf.Min(0.32f, height), 0.03f), mats["panel"]);
                var model = SliceSceneBuilder.BuildToolModelPublic(t.toolId, slot.transform, mats);
                Track(model, t.toolId);
                var comp = slot.AddComponent<ToolRackSlot>();
                comp.Configure($"env.tool_rack.slot_{t.hotkey}", $"{(boardId == SimIds.RoofToolbox ? "車頂工具箱" : "工具架")}：{t.displayName}");
                comp.Setup(t.hotkey, model, slot.GetComponent<Renderer>());
                slot.AddComponent<Highlightable>();
            }
            return root;
        }

        /// <summary>備品架：3 條滑板疊放（型號不符、正確、正確但背面裂紋）。</summary>
        static Transform BuildSpareShelf(Transform env, Vector3 pos, Dictionary<string, Material> mats)
        {
            var shelf = new GameObject("SpareShelf").transform;
            shelf.SetParent(env, false);
            shelf.position = pos;
            var stripMesh = AssetDatabase.LoadAssetAtPath<GameObject>(SliceModelBuilder.StripPrefabPath("L")).GetComponentInChildren<MeshFilter>().sharedMesh;
            var stripMats = AssetDatabase.LoadAssetAtPath<GameObject>(SliceModelBuilder.StripPrefabPath("L")).GetComponentInChildren<MeshRenderer>().sharedMaterials;
            var specs = new[] { ("CS-1100-64B", false), (SpareShelf.CorrectModel, false), (SpareShelf.CorrectModel, true) };
            for (int i = 0; i < 3; i++)
            {
                var slot = new GameObject($"Slot_{i + 1}");
                slot.transform.SetParent(shelf, false);
                slot.transform.localPosition = new Vector3(0, -0.1f + 0.1f * i, 0);
                SliceModelBuilder.Prim(PrimitiveType.Cube, "Board", slot.transform, new Vector3(0, -0.012f, 0), new Vector3(1.2f, 0.01f, 0.14f), mats["shelf"]);
                var body = new GameObject("Body"); body.transform.SetParent(slot.transform, false);
                var mesh = new GameObject("Mesh"); mesh.transform.SetParent(body.transform, false);
                mesh.AddComponent<MeshFilter>().sharedMesh = stripMesh;
                mesh.AddComponent<MeshRenderer>().sharedMaterials = stripMats;
                GameObject crack = null;
                if (specs[i].Item2)
                {
                    crack = new GameObject("CrackDecal"); crack.transform.SetParent(body.transform, false);
                    for (int k = 0; k < 4; k++)
                    {
                        var seg = SliceModelBuilder.Prim(PrimitiveType.Cube, "Seg" + k, crack.transform, new Vector3(-0.06f + 0.035f * k, stripMesh.bounds.min.y - 0.0006f, k % 2 == 0 ? 0.01f : -0.01f), new Vector3(0.04f, 0.001f, 0.003f), mats["crack"]);
                        seg.transform.localRotation = Quaternion.Euler(0, k % 2 == 0 ? 30 : -30, 0);
                        Object.DestroyImmediate(seg.GetComponent<Collider>());
                    }
                }
                var bc = slot.AddComponent<BoxCollider>();
                bc.center = stripMesh.bounds.center; bc.size = stripMesh.bounds.size + new Vector3(0, 0.02f, 0.02f);
                var spare = slot.AddComponent<SpareShelf>();
                spare.Configure($"env.spare_shelf.slot_{i + 1}", $"備品滑板 {i + 1}（點開檢視）");
                spare.Setup(specs[i].Item1, specs[i].Item2, SpareShelf.Nominal, body.transform, crack);
                slot.AddComponent<Highlightable>();
            }
            return shelf;
        }

        static void Track(GameObject go, string id, string suffix = "")
        {
            var t = go.GetComponent<ReplayTracked>() ?? go.AddComponent<ReplayTracked>();
            t.Configure(id, suffix);
        }
    }
}
