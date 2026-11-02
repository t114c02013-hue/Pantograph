using System.Collections.Generic;
using System.Linq;
using System.Text;
using PantographSim.Parts.Common;
using PantographSim.Parts.Mechanism;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1.5a a1：集電弓整組的父物件 PAN_Root（原點＝降弓時底架底面中心，+Z 朝肘側、+Y 朝上），讓整組可以移動、判定結果不變。可以重跑，結果相同。
    /// PAN_Root
    ///   PAN_Visual、PAN_Parts：目前是空的容器（見下方 shortcut）
    ///   PAN_Mechanism／Pantograph：原本的集電弓（PantographMechanism、網格、所有零件與互動點，名稱與 ID 不改）
    ///   PAN_Base：BaseHole_1..4、LocatingHole_1..4（底架與絕緣子之間的螺栓孔中心）、COG
    ///   PAN_LiftPoints／ip.pan_assy.lift_point_1..4、PAN_Connections、PAN_HoldDown
    /// 一律 SetParent(worldPositionStays: true)：世界位置、紀錄、黑板數值都和改之前相同。
    /// shortcut: 網格與零件都掛在關節（Base／LowerArm／UpperArm／PanHead）底下才會跟著升降，不能拆到 PAN_Visual／PAN_Parts 同一層；
    ///           整組放在 PAN_Mechanism 裡一起移動。要拆成三層時，PantographMechanism 要改成驅動「關節空物件」而不是網格本身。
    /// </summary>
    public static class PantographAssemblyBuilder
    {
        public const string RootName = "PAN_Root";

        /// <summary>螺栓孔中心（世界座標）：底架底面四角往內 0.08（X）、0.10（Z），和 RoofSiteBuilder 的絕緣子同一套算法；依 CornerIndex 排好（第 n−1 個＝n 號）。</summary>
        public static Vector3[] BaseHoles(Bounds baseR) => ByCorner(new[] { (-1, -1), (1, 1), (1, -1), (-1, 1) }
            .Select(c => new Vector3(baseR.center.x + c.Item1 * (baseR.extents.x - 0.08f), baseR.min.y, baseR.center.z + c.Item2 * (baseR.extents.z - 0.1f))), baseR.center);

        /// <summary>a6（model-spec v2.3 §7.7.1）：底座四角的編號只看相對底架中心（PAN_Root 局部）的正負號，順時針 1 (−X,−Z)、2 (+X,−Z)、3 (+X,+Z)、4 (−X,+Z)。</summary>
        public static int CornerIndex(Vector3 local) => local.z < 0f ? (local.x < 0f ? 1 : 2) : (local.x < 0f ? 4 : 3);

        /// <summary>依 CornerIndex 重排（center＝底架中心，世界）；四角各一個，缺或重複就丟例外。</summary>
        public static Vector3[] ByCorner(IEnumerable<Vector3> pts, Vector3 center)
        {
            var arr = new Vector3[4]; var used = new bool[4];
            foreach (var p in pts)
            {
                int n = CornerIndex(p - center);
                if (used[n - 1]) throw new System.Exception($"底座第 {n} 角有兩個點");
                arr[n - 1] = p; used[n - 1] = true;
            }
            if (used.Any(u => !u)) throw new System.Exception("底座四角缺點");
            return arr;
        }

        public static Transform Build(GameObject rig, StringBuilder sb)
        {
            var mech = rig.GetComponent<PantographMechanism>();
            float lift = mech.Lift;
            mech.SetLift(0f);
            var baseR = rig.transform.Find("Base").GetComponent<Renderer>().bounds;
            var cog = MassCenter(rig.transform, out var groups);   // F1：降弓姿態、各關節頂點平均依質量加權（a6）
            var tie = ArmTop(rig.transform, R1Data.RoofScene.holdDownTieXZ);

            var origin = new Vector3(baseR.center.x, baseR.min.y, baseR.center.z);
            var root = new GameObject(RootName).transform;
            root.SetParent(rig.transform.parent, false);
            root.SetPositionAndRotation(origin, Quaternion.identity);
            Transform Kid(Transform p, string n) { var t = new GameObject(n).transform; t.SetParent(p, false); return t; }
            Kid(root, "PAN_Visual");
            var mechRoot = Kid(root, "PAN_Mechanism");
            Kid(root, "PAN_Parts");
            rig.transform.SetParent(mechRoot, true);

            var pBase = Kid(root, "PAN_Base");
            var holes = BaseHoles(baseR);
            for (int i = 0; i < 4; i++)
            {
                Kid(pBase, "BaseHole_" + (i + 1)).position = holes[i];
                Kid(pBase, "LocatingHole_" + (i + 1)).position = holes[i];
            }
            Kid(pBase, "COG").position = cog;
            var lp = Kid(root, "PAN_LiftPoints");
            for (int i = 1; i <= 4; i++) Kid(lp, "ip.pan_assy.lift_point_" + i);
            Kid(root, "PAN_Connections");
            Kid(root, "PAN_HoldDown");
            root.gameObject.AddComponent<InteractionPointMarker>().Configure("part.pan_assy", "集電弓整組");

            sb.AppendLine($"集電弓整組 PAN_Root：原點 {origin:F3}（降弓底架底面中心）；螺栓孔 " + string.Join("、", holes.Select(h => h.ToString("F3"))) + $"；重心 COG {cog:F3}（{groups}）");
            AddRigging(root, baseR, tie, sb);
            AddLiftRig(root, cog, sb);
            Tight = SaveLoweredTightBounds(root, sb);   // 降弓姿態、整組頂點（含新節點）
            var box = root.gameObject.AddComponent<BoxCollider>();   // PAN_Root 整體 Box（layer PanAssembly，給 R1.5b 的天車碰撞檢查用）＝LoweredTightBounds
            box.center = Tight.center; box.size = Tight.size;
            box.enabled = false;   // shortcut: 先關閉（CleaningClothTool 的射線會打到 trigger，開著會擋住擦托架）, R1.5b 做天車碰撞檢查時開啟並把清潔布射線改成 Ignore trigger
            if (PanLayer >= 0) root.gameObject.layer = PanLayer;
            mech.SetLift(lift);
            return root;
        }

        public static PanLiftMath.Table MassTable() => PanLiftMath.LoadDefault();

        /// <summary>這次建置算出的吊帶長度（+X 側＝吊點 2、3；−X 側＝吊點 1、4），RoofSiteBuilder 擺吊帶時用。</summary>
        public static (float plusX, float minusX) SlingLengths { get; private set; } = (1f, 1f);
        /// <summary>這次建置算出的吊梁吊耳間距（＝吊點 X 間距，G2）。</summary>
        public static float BeamSpan { get; private set; } = 1f;
        /// <summary>這次建置算出的 LoweredTightBounds（PAN_Root 局部）。</summary>
        public static Bounds Tight { get; private set; }
        /// <summary>這次建置量到的下臂綁點與半寬、兩支固定帶腳的長度。</summary>
        public static (Vector3 tie, float armHalfWidth, float legA, float legB) HoldDownMeasure { get; private set; }

        /// <summary>F1（a6 建模 B 12.6／12.7）：各關節自己的網格（不含更深一層關節）的頂點平均，依 pan_mass_table.json 的質量加權。降弓固定帶、吊帶、吊梁不在機構底下，不算入。</summary>
        static Vector3 MassCenter(Transform rig, out string summary)
        {
            var table = MassTable();
            var parts = new List<(Vector3, float)>();
            var lines = new List<string>();
            foreach (var g in table.groups)
            {
                var rs = PanGeometry.OwnRenderers(rig, g.joint);
                if (rs.Count == 0) { lines.Add($"{g.joint}：找不到網格"); continue; }
                var c = PanGeometry.VertexMean(rs);
                parts.Add((c, g.massKg));
                lines.Add($"{g.joint} {g.massKg} kg @ {c:F3}");
            }
            summary = string.Join("；", lines) + $"（合計 {parts.Sum(p => p.Item2)} kg，表列總重 {table.total} kg）";
            return PanLiftMath.WeightedCenter(parts);
        }

        /// <summary>
        /// D34（增補 3）：上臂在綁點附近（X 0.15、Z 0.05 m 內）的頂點（降弓姿態）：頂面點（X 取中心）與 X 半寬；
        /// 下臂在那裡和上臂疊在一起（XZ 範圍重疊）時取兩支的外包。找不到就退回整支上臂。
        /// </summary>
        static (Vector3 top, float halfWidth) ArmTop(Transform rig, Vector2 tieXZ)
        {
            // 管件只有兩端有頂點，用 z＝綁點 Z 的截面；取離綁點 X 最近的那一支（截面點 X 相距 0.05 m 內算同一支）
            List<Vector3> Near(string joint)
            {
                var sec = PanGeometry.SectionZ(PanGeometry.OwnRenderers(rig, joint), tieXZ.y);
                if (sec.Count == 0) return sec;
                float x = sec.OrderBy(p => Mathf.Abs(p.x - tieXZ.x)).First().x;
                var arm = new List<Vector3>();
                foreach (var p in sec.OrderBy(p => Mathf.Abs(p.x - x)))
                    if (arm.Count == 0 || arm.Any(q => Mathf.Abs(q.x - p.x) < 0.05f)) arm.Add(p);
                return arm;
            }
            var near = Near("UpperArm");
            if (near.Count == 0) throw new System.Exception($"上臂在 Z {tieXZ.y} 沒有截面：降弓固定帶綁點量不到");
            var lower = Near("LowerArm");   // 上下臂在那裡疊在一起（X 範圍重疊、Y 相接）就取兩支的外包
            if (lower.Count > 0 && lower.Max(v => v.x) >= near.Min(v => v.x) && lower.Min(v => v.x) <= near.Max(v => v.x) && lower.Max(v => v.y) >= near.Min(v => v.y) - 0.01f) near.AddRange(lower);
            float x0 = near.Min(v => v.x), x1 = near.Max(v => v.x);
            return (new Vector3((x0 + x1) / 2f, near.Max(v => v.y), tieXZ.y), (x1 - x0) / 2f);
        }

        public const string TightBoundsPath = "Assets/_Project/Parts/PantographAssembly/Data/pan_lowered_tight_bounds.json";
        [System.Serializable] public class TightBoundsData { public Vector3 center, size; public string note; }

        public static Bounds LoadTightBounds()
        {
            var d = JsonUtility.FromJson<TightBoundsData>(System.IO.File.ReadAllText(TightBoundsPath));
            return new Bounds(d.center, d.size);
        }

        /// <summary>
        /// C1（G1）：降弓姿態整組頂點範圍（PAN_Root 局部），存成資料 pan_lowered_tight_bounds.json。只算顯示中的網格（固定帶、防塵蓋的另一個狀態不算）；
        /// 增補 4（建模 B H1）：排除可拆件——掛 FastenerBehaviour 的節點（底座螺栓、高壓端子螺栓、滑板螺栓）、高壓端子、防塵蓋（DetachablePart 是 R1.5b，到時加進 IsDetachable）。
        /// </summary>
        static Bounds SaveLoweredTightBounds(Transform root, StringBuilder sb)
        {
            var rs = root.GetComponentsInChildren<Renderer>(false).Where(r => !(r is LineRenderer) && !IsDetachable(r.transform, root)).ToList();
            var b = PanGeometry.VertexBounds(rs, root).Value;
            var top = rs.Select(r => (r, y: PanGeometry.VertexBounds(new[] { r }, root)?.max.y ?? float.MinValue)).OrderByDescending(x => x.y).First();
            var data = new TightBoundsData { center = b.center, size = b.size, note = "PAN_Root 局部、降弓、頂點算法（a6 G1 C1），排除可拆件；建置時自動寫入" };
            System.IO.File.WriteAllText(TightBoundsPath, JsonUtility.ToJson(data, true));
            UnityEditor.AssetDatabase.ImportAsset(TightBoundsPath);
            sb.AppendLine($"LoweredTightBounds（PAN_Root 局部，排除可拆件）：{b.min:F3}–{b.max:F3}；Y 最高點 {top.y:0.000}（{top.r.name}，父 {top.r.transform.parent?.name}）");
            TightTopMesh = top.r.name;
            return b;
        }

        /// <summary>這次 LoweredTightBounds 的 Y 最高點網格名稱（報告用）。</summary>
        public static string TightTopMesh { get; private set; }

        static bool IsDetachable(Transform t, Transform root)
        {
            for (var p = t; p != null && p != root; p = p.parent)
                if (p.GetComponent<FastenerBehaviour>() != null || p.name == "ip.pan_assy.hv_terminal" || p.name.StartsWith("DustCap_")) return true;
            return false;
        }

        /// <summary>F2／F3（a6 G2）：吊鉤掛點目標 Hook_AttachPoint_Target 在 COG 正上方；吊梁沿 X，兩端吊耳 BeamEye_PlusX／MinusX 間距＝吊點 X 間距；分邊吊帶長度。</summary>
        static void AddLiftRig(Transform root, Vector3 cog, StringBuilder sb)
        {
            var t = MassTable();
            var lp = root.Find("PAN_LiftPoints");
            var pts = Enumerable.Range(1, 4).Select(i => lp.Find("ip.pan_assy.lift_point_" + i).position).ToList();
            var hook = new GameObject("Hook_AttachPoint_Target").transform; hook.SetParent(lp, false); hook.position = PanLiftMath.HookPoint(cog, pts, t);
            var (ep, em) = PanLiftMath.BeamEyes(cog, pts, t);
            var e1 = new GameObject("BeamEye_PlusX").transform; e1.SetParent(lp, false); e1.position = ep;
            var e2 = new GameObject("BeamEye_MinusX").transform; e2.SetParent(lp, false); e2.position = em;
            SlingLengths = PanLiftMath.SlingLengths(cog, pts, t);
            BeamSpan = 2f * PanLiftMath.BeamHalfSpan(pts);
            sb.AppendLine($"吊運：吊鉤掛點 {hook.position:F3}（COG 正上方）；吊梁高出吊點 {PanLiftMath.BeamAbove(cog, pts, t):0.000} m（吊帶長 {t.slingLength} m 推導）；吊梁吊耳間距 {BeamSpan:0.000} m、梁長 {BeamSpan + t.beamExtraLength:0.000} m；吊帶 +X 側 {SlingLengths.plusX:0.000} m、−X 側 {SlingLengths.minusX:0.000} m；吊起後傾斜 {PanLiftMath.HangPlaneTiltDeg(cog, pts, SlingLengths, t):0.00}°");
            // 建置檢查（建模 B H3、D36）：每條吊帶和垂直線 ≤ maxSlingAngleDeg；同一端（同一個吊耳）兩條之間 ≤ maxSlingPairAngleDeg
            var bad = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                var eye = pts[i].x >= cog.x ? ep : em;
                float a = PanLiftMath.SlingAngleDeg(eye, pts[i]);
                sb.AppendLine($"  吊帶 {i + 1}：和垂直線 {a:0.0}°（≤ {t.maxSlingAngleDeg}°）");
                if (a > t.maxSlingAngleDeg + 1e-3f) bad.Add($"吊帶 {i + 1} 和垂直線 {a:0.0}° 超過 {t.maxSlingAngleDeg}°");
            }
            foreach (var (eye, a, b) in new[] { (em, 0, 3), (ep, 1, 2) })
            {
                float pair = Vector3.Angle(pts[a] - eye, pts[b] - eye);
                sb.AppendLine($"  吊帶 {a + 1}–{b + 1}（同一端）夾角 {pair:0.0}°（≤ {t.maxSlingPairAngleDeg}°）");
                if (pair > t.maxSlingPairAngleDeg + 1e-3f) bad.Add($"吊帶 {a + 1}–{b + 1} 夾角 {pair:0.0}° 超過 {t.maxSlingPairAngleDeg}°");
            }
            if (bad.Count > 0) throw new System.Exception("吊帶角度不合格：" + string.Join("；", bad));
        }

        public const string RiggingFolder = "Assets/_Project/Parts/PantographAssembly/Models";
        static int ContactLayer => LayerMask.NameToLayer("ContactTarget");
        static int PanLayer => LayerMask.NameToLayer("PanAssembly");

        static GameObject Rig(string node, Transform parent, Vector3 pos, string rename = null) =>
            RoofSiteBuilder.Fbx("PAN_Rigging", node, parent, pos, rename: rename, folder: RiggingFolder);

        static void Trigger(GameObject g, float r)
        {
            var c = g.AddComponent<SphereCollider>(); c.isTrigger = true; c.radius = r / Mathf.Max(g.transform.lossyScale.x, 1e-4f);
            if (ContactLayer >= 0) g.layer = ContactLayer;
        }

        static void Part<T>(GameObject g, string id, string name, string stateKey, string state) where T : InteractionPointMarker
        {
            g.AddComponent<T>().Configure(id, name);
            g.AddComponent<InitialPartState>().Configure(stateKey ?? id + ".state", state);
        }

        static Transform Node(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.position = pos; return t;
        }

        /// <summary>FBX 子網格（增補 2 的 PAN_CtrlConnector／PAN_HoldDownStrap，在 Worksite/Models/Generated）。</summary>
        static GameObject Gen(string fbx, string node, Transform parent, Vector3 pos, string rename = null) => RoofSiteBuilder.Fbx(fbx, node, parent, pos, rename: rename);

        /// <summary>
        /// R1.5a a5／a6：集電弓整組的新節點。本輪都不接進任何步驟，只寫各自的初始黑板值；每個互動點一個簡單 trigger（layer ContactTarget）。
        /// 存檔時的顯示＝訓練開始時的現場狀態（增補 2 第一部分第 2 點）；DetachablePart 依黑板切換子節點是 R1.5b b2。
        /// shortcut: 高壓端子、控制線接頭、降弓固定帶先掛 InteractionPointMarker＋InitialPartState（不是 RemovableBehaviour：那是碳滑板專用，Removed 會跟著滑鼠走、Mounted 會被隱藏）, R1.5b 寫通用的拆裝元件時換掉
        /// </summary>
        static void AddRigging(Transform root, Bounds baseR, (Vector3 top, float halfWidth) tie, StringBuilder sb)
        {
            var pBase = root.Find("PAN_Base");
            var d = R1Data.RoofScene;
            // 底座螺栓 part.pan_base.bolt_1..4：FastenerBehaviour，狀態 Torqued（n 跟 BaseHole_n，依正負號編號）
            for (int i = 1; i <= 4; i++)
            {
                var hole = pBase.Find("BaseHole_" + i);
                var bolt = Rig("BaseBolt_M16", hole, hole.position, "part.pan_base.bolt_" + i);
                Part<FastenerBehaviour>(bolt, "part.pan_base.bolt_" + i, $"底座螺栓 {i}", null, "Torqued");
                Trigger(bolt, 0.03f);
            }
            // 吊耳 ip.pan_assy.lift_point_1..4：底架頂面四角往內 0.05（依正負號編號）
            var lp = root.Find("PAN_LiftPoints");
            var lugs = ByCorner(new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }.Select(c => new Vector3(baseR.center.x + c.Item1 * (baseR.extents.x - 0.05f), baseR.max.y, baseR.center.z + c.Item2 * (baseR.extents.z - 0.05f))), baseR.center);
            for (int k = 1; k <= 4; k++)
            {
                var t = lp.Find("ip.pan_assy.lift_point_" + k);
                t.position = lugs[k - 1];
                Rig("LiftLug", t, t.position, "LiftLug");
                t.gameObject.AddComponent<InteractionPointMarker>().Configure(t.name, $"吊耳 {k}");
                Trigger(t.gameObject, 0.05f);
            }
            // 高壓端子座 ip.pan_assy.hv_terminal（Mounted），底下 HV_Bolt_1..2（FastenerBehaviour）
            var conn = root.Find("PAN_Connections");
            var hv = Rig("HV_TerminalSeat", conn, new Vector3(baseR.max.x - 0.08f, baseR.max.y, baseR.max.z - 0.15f), "ip.pan_assy.hv_terminal");
            Part<InteractionPointMarker>(hv, "ip.pan_assy.hv_terminal", "高壓端子", null, "Mounted");
            Trigger(hv, 0.06f);
            foreach (var hb in hv.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("HV_Bolt_")).ToList())
            {
                string id = "ip.pan_assy.hv_terminal." + hb.name.ToLower().Replace("hv_", "");
                Part<FastenerBehaviour>(hb.gameObject, id, "高壓端子螺栓", null, "Torqued");
                Trigger(hb.gameObject, 0.015f);
            }
            // a6 v4.3 第 11 點＋增補 2：控制線接頭 ip.pan_assy.ctrl_connector（Mounted；接頭接著 → DustCap_Off 顯示、DustCap_On 隱藏）
            var ctrlPos = new Vector3(d.ctrlConnectorXZ.x, baseR.max.y, d.ctrlConnectorXZ.y);
            var ctrl = Node(conn, "ip.pan_assy.ctrl_connector", ctrlPos);
            foreach (var n in new[] { "Connector_Body", "DustCap_On", "DustCap_Off" }) Gen("PAN_CtrlConnector", n, ctrl, ctrlPos);
            ctrl.Find("DustCap_On").gameObject.SetActive(false);
            var plug = Node(ctrl, "Ctrl_PlugPoint", ctrlPos + new Vector3(d.ctrlConnectorBodySize.x / 2f + d.ctrlSocketLength, d.ctrlConnectorBodySize.y / 2f, 0f));
            plug.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            Part<InteractionPointMarker>(ctrl.gameObject, "ip.pan_assy.ctrl_connector", "控制線接頭", null, "Mounted");
            Trigger(ctrl.gameObject, 0.04f);
            // 分流線：只做外觀
            for (int i = 1; i <= 2; i++) Rig("Shunt_Visual", conn, new Vector3(baseR.center.x + (i == 1 ? -0.3f : 0.3f), baseR.max.y + 0.02f, baseR.max.z - 0.3f), "Shunt_Visual_" + i);
            // a6 v4.3 第 11 點＋增補 2：降弓固定帶 ip.pan_assy.hold_down。訓練開始時帶子在工具袋 slot_10 → Installed、Removed 都隱藏；扣座 A／B 一直顯示
            var hdRoot = root.Find("PAN_HoldDown");
            float armHalf = tie.halfWidth + 0.005f;
            if (Mathf.Abs(armHalf - d.strapArmHalfWidth) > 0.002f) { sb.AppendLine($"⚠ 上臂半寬＋0.005 ＝ {armHalf:0.0000} m 和 RoofSceneDef 原值 {d.strapArmHalfWidth:0.0000} 不同：已寫回 RoofSceneDef，要重新匯出 car_params.json 並重跑 Blender"); d.strapArmHalfWidth = armHalf; UnityEditor.EditorUtility.SetDirty(d); }
            var hd = Node(hdRoot, "ip.pan_assy.hold_down", tie.top);
            var tiePt = Node(hd, "HoldDown_TiePoint", tie.top);
            var installed = Node(hd, "HoldDown_Installed", tie.top);
            Gen("PAN_HoldDownStrap", "HoldDown_Saddle", installed, tie.top);
            float saddleEndY = tie.top.y - d.strapArmHalfWidth - d.strapThickness;   // 半圓兩端在頂點下方 外半徑
            var legLens = new float[2];
            // D34：扣座 A 在底架 +X 邊、B 在 −X 邊（底架頂面，綁點的 Z）；兩支腳從鞍端拉到各自的扣座（腳的 −Y 對準扣座）
            foreach (var (sfx, sx, k) in new[] { ("A", 1f, 0), ("B", -1f, 1) })
            {
                var buckle = new Vector3(sx > 0 ? baseR.max.x - d.strapBuckleSize.x / 2f : baseR.min.x + d.strapBuckleSize.x / 2f, baseR.max.y, tie.top.z);
                Gen("PAN_HoldDownStrap", "HoldDown_BuckleBase", hd, buckle, "HoldDown_BuckleBase_" + sfx);
                var end = new Vector3(tie.top.x + sx * d.strapArmHalfWidth, saddleEndY, tie.top.z);
                var top = buckle + Vector3.up * d.strapBuckleSize.y;
                var leg = Gen("PAN_HoldDownStrap", "HoldDown_Leg", installed, end, "HoldDown_Leg_" + sfx);
                legLens[k] = Vector3.Distance(end, top);
                RoofSiteBuilder.ResizeWorld(leg.transform, Vector3.up, legLens[k]);   // 單位長 1 m 的腳先沿 Y 縮放，再整支轉向扣座
                leg.transform.rotation = Quaternion.FromToRotation(Vector3.down, top - end) * leg.transform.rotation;
            }
            Gen("PAN_HoldDownStrap", "HoldDown_Removed", hd, new Vector3(baseR.max.x - d.strapBuckleSize.x / 2f, baseR.max.y, tie.top.z - 0.10f));
            installed.gameObject.SetActive(false);
            hd.Find("HoldDown_Removed").gameObject.SetActive(false);
            Part<InteractionPointMarker>(hd.gameObject, "ip.pan_assy.hold_down", "降弓固定帶", null, "InToolBag");
            Trigger(hd.gameObject, 0.05f);
            HoldDownMeasure = (tie.top, armHalf, legLens[0], legLens[1]);
            sb.AppendLine($"集電弓新節點：底座螺栓 4、吊耳 4、高壓端子（螺栓 2）、控制線接頭 {ctrlPos:F3}（DustCap_Off）、降弓固定帶綁點 {tie.top:F3}（上臂半寬 {tie.halfWidth:0.000}、腳長 A {legLens[0]:0.000}／B {legLens[1]:0.000} m；Installed／Removed 都隱藏）、分流線外觀 2");
        }

        /// <summary>Logs/PantographAssemblyReport.txt：所有節點的局部座標、和車頂絕緣子頂面的對齊誤差、新網格的三角面數（車頂建好之後呼叫）。</summary>
        public static void WriteReport(Transform root)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"集電弓整組 PAN_Root（{System.DateTime.Now:yyyy-MM-dd HH:mm}）：世界 {root.position:F3}");
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.parent == root || (t.parent != null && t.parent.parent == root) || t.name.StartsWith("part.pan_") || t.name.StartsWith("ip.pan_") || t.name.StartsWith("HoldDown_") || t.name.StartsWith("Ctrl_") || t.name.StartsWith("DustCap_")))
                sb.AppendLine($"  {t.name,-40} 局部 {root.InverseTransformPoint(t.position):F3}{(t.gameObject.activeSelf ? "" : "（隱藏）")}");
            sb.AppendLine("絕緣子頂面對齊（InsulatorTop_n 和 BaseHole_n 的距離）：");
            for (int i = 1; i <= 4; i++)
            {
                var hole = root.Find("PAN_Base/BaseHole_" + i);
                var top = Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t => t.name == "InsulatorTop_" + i);
                sb.AppendLine(top == null ? $"  {i}：找不到 InsulatorTop_{i}" : $"  {i}：{Vector3.Distance(hole.position, top.position) * 1000f:0.0} mm");
            }
            if (root.GetComponent<BoxCollider>() is BoxCollider bc)
                sb.AppendLine($"降弓整組 Bounds＝LoweredTightBounds（PAN_Root 局部，頂點算法）：中心 {bc.center:F3}、大小 {bc.size:F3}；世界 {root.TransformPoint(bc.center - bc.size / 2f):F3}–{root.TransformPoint(bc.center + bc.size / 2f):F3}");
            var mech = root.GetComponentInChildren<PantographMechanism>(true);
            sb.AppendLine($"升弓那 29 個網格：場景裡沒有另外一組；同一組網格 {mech.GetComponentsInChildren<Renderer>(true).Length} 個 Renderer 由關節轉到升弓或降弓（全部顯示中：{mech.GetComponentsInChildren<Renderer>(true).All(r => r.enabled)}）");
            Transform Find(string n) => Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t => t.name == n);
            if (Find("StandFrame") is Transform sf && sf.TryGetComponent<Collider>(out var sfc)) sb.AppendLine($"台架外框：X {sfc.bounds.min.x:0.000}…{sfc.bounds.max.x:0.000}、Z {sfc.bounds.min.z:0.000}…{sfc.bounds.max.z:0.000}、頂面 Y {sfc.bounds.max.y:0.000}");
            if (Find("StandSeatBlock") is Transform ss && ss.TryGetComponent<Collider>(out var ssc)) sb.AppendLine($"台架支撐座：{ssc.bounds.size.x:0.000} × {ssc.bounds.size.z:0.000} m、中心 ({ssc.bounds.center.x:0.000}, {ssc.bounds.center.z:0.000})、頂面 Y {ssc.bounds.max.y:0.000}");
            if (Find("ENV_PlatformStair") is Transform st)
            {
                var rs = st.GetComponentsInChildren<Renderer>(true);
                var sbB = rs[0].bounds; foreach (var r in rs) sbB.Encapsulate(r.bounds);
                sb.AppendLine($"平台鋼梯 Bounds：X {sbB.min.x:0.00}…{sbB.max.x:0.00}、Y {sbB.min.y:0.00}…{sbB.max.y:0.00}、Z {sbB.min.z:0.00}…{sbB.max.z:0.00}（場景裡鋼梯 {Object.FindObjectsOfType<Transform>(true).Count(t => t.name == "ENV_PlatformStair")} 組）");
            }
            sb.AppendLine($"吊帶長度：+X 側（吊點 2、3）{SlingLengths.plusX:0.000} m、−X 側（吊點 1、4）{SlingLengths.minusX:0.000} m；吊梁吊耳間距 {BeamSpan:0.000} m");
            int tris = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && UnityEditor.AssetDatabase.GetAssetPath(f.sharedMesh).Contains("PAN_")).Sum(f => f.sharedMesh.triangles.Length / 3);
            sb.AppendLine($"新網格（PAN_Rigging、PAN_CtrlConnector、PAN_HoldDownStrap）三角面：{tris}");
            System.IO.File.WriteAllText("Logs/PantographAssemblyReport.txt", sb.ToString(), new UTF8Encoding(true));
        }
    }
}
