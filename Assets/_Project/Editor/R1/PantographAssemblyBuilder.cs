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
    ///   PAN_LiftPoints／ip.pan_assy.lift_point_1..4（空物件，a5 放網格）、PAN_Connections、PAN_HoldDown（a5）
    /// 一律 SetParent(worldPositionStays: true)：世界位置、紀錄、黑板數值都和改之前相同。
    /// shortcut: 網格與零件都掛在關節（Base／LowerArm／UpperArm／PanHead）底下才會跟著升降，不能拆到 PAN_Visual／PAN_Parts 同一層；
    ///           整組放在 PAN_Mechanism 裡一起移動。要拆成三層時，PantographMechanism 要改成驅動「關節空物件」而不是網格本身。
    /// </summary>
    public static class PantographAssemblyBuilder
    {
        public const string RootName = "PAN_Root";

        /// <summary>螺栓孔中心（世界座標）：底架底面四角往內 0.08（X）、0.10（Z），和 RoofSiteBuilder 的絕緣子同一套算法。</summary>
        public static Vector3[] BaseHoles(Bounds baseR) => new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }
            .Select(c => new Vector3(baseR.center.x + c.Item1 * (baseR.extents.x - 0.08f), baseR.min.y, baseR.center.z + c.Item2 * (baseR.extents.z - 0.1f))).ToArray();

        public static Transform Build(GameObject rig, StringBuilder sb)
        {
            var mech = rig.GetComponent<PantographMechanism>();
            float lift = mech.Lift;
            mech.SetLift(0f);
            var baseR = rig.transform.Find("Base").GetComponent<Renderer>().bounds;
            var all = SliceSceneBuilder.RendererBounds(rig);
            var cog = MassCenter(rig.transform, out var groups);   // F1：降弓姿態、依質量加權
            mech.SetLift(lift);

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
            AddRigging(root, baseR, all, sb);
            AddLiftRig(root, cog, sb);
            return root;
        }

        public const string MassTablePath = "Assets/_Project/Parts/PantographAssembly/Data/pan_mass_table.json";
        public static PanLiftMath.Table MassTable() => JsonUtility.FromJson<PanLiftMath.Table>(System.IO.File.ReadAllText(MassTablePath));

        /// <summary>這次建置算出的吊帶長度（+Z 側、−Z 側），RoofSiteBuilder 擺吊帶時用。</summary>
        public static (float plusZ, float minusZ) SlingLengths { get; private set; } = (1f, 1f);

        /// <summary>F1：各關節網格的中心（只算屬於這個關節、不屬於更深一層關節的網格）依 pan_mass_table.json 的質量加權。</summary>
        static Vector3 MassCenter(Transform rig, out string summary)
        {
            var table = MassTable();
            var joints = table.groups.Select(g => (g, t: rig.Find(g.joint))).Where(x => x.t != null).ToList();
            var parts = new System.Collections.Generic.List<(Vector3, float)>();
            var lines = new System.Collections.Generic.List<string>();
            foreach (var (g, t) in joints)
            {
                var rs = t.GetComponentsInChildren<Renderer>(true).Where(r => joints.Where(o => o.t != t && o.t.IsChildOf(t)).All(o => !r.transform.IsChildOf(o.t))).ToList();
                if (rs.Count == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                parts.Add((b.center, g.massKg));
                lines.Add($"{g.joint.Split('/').Last()} {g.massKg} kg @ {b.center:F2}");
            }
            summary = string.Join("；", lines);
            return PanLiftMath.WeightedCenter(parts);
        }

        /// <summary>F2／F3：吊鉤掛點目標 Hook_AttachPoint_Target 在 COG 正上方；吊梁兩端吊耳 BeamEye_PlusZ／MinusZ；分邊吊帶長度。</summary>
        static void AddLiftRig(Transform root, Vector3 cog, StringBuilder sb)
        {
            var t = MassTable();
            var lp = root.Find("PAN_LiftPoints");
            var pts = Enumerable.Range(1, 4).Select(i => lp.Find("ip.pan_assy.lift_point_" + i).position).ToList();
            float y = pts.Average(p => p.y);
            var hook = new GameObject("Hook_AttachPoint_Target").transform; hook.SetParent(lp, false); hook.position = PanLiftMath.HookPoint(cog, y, t);
            var (ep, em) = PanLiftMath.BeamEyes(cog, y, t);
            var e1 = new GameObject("BeamEye_PlusZ").transform; e1.SetParent(lp, false); e1.position = ep;
            var e2 = new GameObject("BeamEye_MinusZ").transform; e2.SetParent(lp, false); e2.position = em;
            SlingLengths = PanLiftMath.SlingLengths(cog, pts, t);
            sb.AppendLine($"吊運：吊鉤掛點 {hook.position:F3}（COG 正上方）；吊帶 +Z 側 {SlingLengths.plusZ:0.000} m、−Z 側 {SlingLengths.minusZ:0.000} m；吊起後傾斜 {PanLiftMath.HangPlaneTiltDeg(cog, pts, SlingLengths, t):0.00}°");
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

        /// <summary>
        /// R1.5a a5：集電弓整組的新節點（PAN_Rigging.fbx 實例化到 a1 的空物件上）。本輪都不接進任何步驟，只寫各自的初始黑板值；
        /// 每個互動點一個簡單 trigger（layer ContactTarget）；PAN_Root 一個整體 Box（layer PanAssembly，給 R1.5b 的天車碰撞檢查用）。
        /// shortcut: 高壓端子、控制線接頭、降弓固定帶先掛 InteractionPointMarker＋InitialPartState（不是 RemovableBehaviour：那是碳滑板專用，Removed 會跟著滑鼠走、Mounted 會被隱藏）, R1.5b 寫通用的拆裝元件時換掉
        /// </summary>
        static void AddRigging(Transform root, Bounds baseR, Bounds all, StringBuilder sb)
        {
            var pBase = root.Find("PAN_Base");
            // 底座螺栓 part.pan_base.bolt_1..4：FastenerBehaviour，狀態 Torqued
            for (int i = 1; i <= 4; i++)
            {
                var hole = pBase.Find("BaseHole_" + i);
                var bolt = Rig("BaseBolt_M16", hole, hole.position + Vector3.up * 0.0f, "part.pan_base.bolt_" + i);
                Part<FastenerBehaviour>(bolt, "part.pan_base.bolt_" + i, $"底座螺栓 {i}", null, "Torqued");
                Trigger(bolt, 0.03f);
            }
            // 吊耳 ip.pan_assy.lift_point_1..4：底架頂面四角往內 0.05
            var lp = root.Find("PAN_LiftPoints");
            int k = 0;
            foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var t = lp.Find("ip.pan_assy.lift_point_" + (++k));
                t.position = new Vector3(baseR.center.x + sx * (baseR.extents.x - 0.05f), baseR.max.y, baseR.center.z + sz * (baseR.extents.z - 0.05f));
                Rig("LiftLug", t, t.position, "LiftLug");
                t.gameObject.AddComponent<InteractionPointMarker>().Configure(t.name, $"吊耳 {k}");
                Trigger(t.gameObject, 0.05f);
            }
            // 高壓端子座 ip.pan_assy.hv_terminal（RemovableBehaviour，Mounted），底下 HV_Bolt_1..2（FastenerBehaviour）
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
            // 控制線接頭 ip.pan_assy.ctrl_connector（RemovableBehaviour，Mounted），含防塵蓋 CtrlCap
            var ctrl = Rig("Ctrl_Connector", conn, new Vector3(baseR.min.x + 0.08f, baseR.max.y - 0.04f, baseR.max.z - 0.10f), "ip.pan_assy.ctrl_connector");
            Part<InteractionPointMarker>(ctrl, "ip.pan_assy.ctrl_connector", "控制線接頭", null, "Mounted");
            Trigger(ctrl, 0.04f);
            // 分流線：只做外觀
            for (int i = 1; i <= 2; i++) Rig("Shunt_Visual", conn, new Vector3(baseR.center.x + (i == 1 ? -0.3f : 0.3f), baseR.max.y + 0.02f, baseR.max.z - 0.3f), "Shunt_Visual_" + i);
            // 降弓固定帶 ip.pan_assy.hold_down（RemovableBehaviour，Removed：本輪沒裝上，放在底架旁）
            var hd = Rig("HoldDown_Strap", root.Find("PAN_HoldDown"), new Vector3(baseR.min.x - 0.05f, baseR.max.y, baseR.center.z), "ip.pan_assy.hold_down");
            Part<InteractionPointMarker>(hd, "ip.pan_assy.hold_down", "降弓固定帶", null, "Removed");
            Trigger(hd, 0.05f);
            // PAN_Root 整體 Box（降弓範圍，layer PanAssembly）
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.center = root.InverseTransformPoint(all.center); box.size = all.size;
            box.enabled = false;   // shortcut: 先關閉（CleaningClothTool 的射線會打到 trigger，開著會擋住擦托架）, R1.5b 做天車碰撞檢查時開啟並把清潔布射線改成 Ignore trigger
            if (PanLayer >= 0) root.gameObject.layer = PanLayer;
            sb.AppendLine("集電弓新節點：底座螺栓 4、吊耳 4、高壓端子（螺栓 2）、控制線接頭、降弓固定帶（Removed）、分流線外觀 2");
        }

        /// <summary>Logs/PantographAssemblyReport.txt：所有節點的局部座標、和車頂絕緣子頂面的對齊誤差、新網格的三角面數（車頂建好之後呼叫）。</summary>
        public static void WriteReport(Transform root)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"集電弓整組 PAN_Root（{System.DateTime.Now:yyyy-MM-dd HH:mm}）：世界 {root.position:F3}");
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.parent == root || (t.parent != null && t.parent.parent == root) || t.name.StartsWith("part.pan_") || t.name.StartsWith("ip.pan_")))
                sb.AppendLine($"  {t.name,-40} 局部 {root.InverseTransformPoint(t.position):F3}");
            sb.AppendLine("絕緣子頂面對齊（InsulatorTop_n 和 BaseHole_n 的距離）：");
            for (int i = 1; i <= 4; i++)
            {
                var hole = root.Find("PAN_Base/BaseHole_" + i);
                var top = Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t => t.name == "InsulatorTop_" + i);
                sb.AppendLine(top == null ? $"  {i}：找不到 InsulatorTop_{i}" : $"  {i}：{Vector3.Distance(hole.position, top.position) * 1000f:0.0} mm");
            }
            if (root.GetComponent<BoxCollider>() is BoxCollider bc)
                sb.AppendLine($"降弓整組 Bounds（PAN_Root 局部）：中心 {bc.center:F3}、大小 {bc.size:F3}；世界 {root.TransformPoint(bc.center - bc.size / 2f):F3}–{root.TransformPoint(bc.center + bc.size / 2f):F3}");
            var mech = root.GetComponentInChildren<PantographSim.Parts.Mechanism.PantographMechanism>(true);
            sb.AppendLine($"升弓那 29 個網格：場景裡沒有另外一組；同一組網格 {mech.GetComponentsInChildren<Renderer>(true).Length} 個 Renderer 由關節轉到升弓或降弓（全部顯示中：{mech.GetComponentsInChildren<Renderer>(true).All(r => r.enabled)}）");
            Transform Find(string n) => Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t => t.name == n);
            if (Find("StandFrame") is Transform sf && sf.GetComponent<Collider>() is Collider sfc) sb.AppendLine($"台架外框頂面 Y {sfc.bounds.max.y:0.000}");
            if (Find("StandSeatBlock") is Transform ss && ss.GetComponent<Collider>() is Collider ssc) sb.AppendLine($"台架支撐座頂面 Y {ssc.bounds.max.y:0.000}");
            if (Find("ENV_PlatformStair") is Transform st)
            {
                var rs = st.GetComponentsInChildren<Renderer>(true);
                var sbB = rs[0].bounds; foreach (var r in rs) sbB.Encapsulate(r.bounds);
                sb.AppendLine($"平台鋼梯 Bounds：X {sbB.min.x:0.00}…{sbB.max.x:0.00}、Y {sbB.min.y:0.00}…{sbB.max.y:0.00}、Z {sbB.min.z:0.00}…{sbB.max.z:0.00}（場景裡鋼梯 {Object.FindObjectsOfType<Transform>(true).Count(t => t.name == "ENV_PlatformStair")} 組）");
            }
            sb.AppendLine($"吊帶長度：+Z 側 {SlingLengths.plusZ:0.000} m、−Z 側 {SlingLengths.minusZ:0.000} m");
            int tris = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && UnityEditor.AssetDatabase.GetAssetPath(f.sharedMesh).Contains("PAN_Rigging")).Sum(f => f.sharedMesh.triangles.Length / 3);
            sb.AppendLine($"新網格（PAN_Rigging）三角面：{tris}");
            System.IO.File.WriteAllText("Logs/PantographAssemblyReport.txt", sb.ToString(), new UTF8Encoding(true));
        }
    }
}
