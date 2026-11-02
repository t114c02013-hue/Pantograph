using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Parts.Mechanism;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>R1.5a a0：集電弓結構確認（只讀，寫 Logs/PantographRiggingCheck.txt）。觸發：Logs/pan-probe.request 或選單。</summary>
    public static class PanRiggingProbe
    {
        public const string FbxPath = "Assets/_Project/ExternalModels/Vendor/3d66/EXT_3d66_JJI54552119072_v01.fbx";
        public const string ReportPath = "Logs/PantographRiggingCheck.txt";

        static string P(Vector3 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000})";
        static string PathOf(Transform t) { var s = t.name; for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s; return s; }

        [MenuItem("PantographSim/R1.5/Pantograph Rigging Check")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"集電弓結構確認（R1.5a a0，{System.DateTime.Now:yyyy-MM-dd HH:mm}）");

            // 1. 原始 FBX：網格數、底架底面以下的網格（絕緣子）
            sb.AppendLine("\n== 1. 原始 FBX " + FbxPath + " ==");
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null) sb.AppendLine("找不到 FBX");
            else
            {
                var preview = EditorSceneManager.NewPreviewScene();
                try
                {
                    var inst = (GameObject)Object.Instantiate(fbx);
                    SceneManager_Move(inst, preview);
                    var rs = inst.GetComponentsInChildren<Renderer>(true);
                    sb.AppendLine($"網格（Renderer）數：{rs.Length}");
                    var all = rs.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                    // 底架：名稱含 Base／底 的網格；沒有就用最低 30% 高度內水平面積最大的網格
                    var baseR = rs.FirstOrDefault(r => r.name.ToLower().Contains("base") || r.name.Contains("底"))
                        ?? rs.Where(r => r.bounds.min.y < all.min.y + all.size.y * 0.3f).OrderByDescending(r => r.bounds.size.x * r.bounds.size.z).First();
                    float baseBottom = baseR.bounds.min.y;
                    sb.AppendLine($"判定為底架的網格：{baseR.name}，底面 y＝{baseBottom:0.000}（整體範圍 {P(all.min)}–{P(all.max)}）");
                    var below = rs.Where(r => r != baseR && r.bounds.max.y <= baseBottom + 0.005f).ToList();
                    sb.AppendLine(below.Count == 0 ? "底架底面以下沒有網格：FBX 裡沒有絕緣子。" : $"底架底面以下有 {below.Count} 個網格（可能是絕緣子）：");
                    foreach (var r in below) sb.AppendLine($"  {PathOf(r.transform)}　{P(r.bounds.min)}–{P(r.bounds.max)}");
                    sb.AppendLine("全部網格（依最低點排序）：");
                    foreach (var r in rs.OrderBy(r => r.bounds.min.y)) sb.AppendLine($"  {r.name,-40} min {P(r.bounds.min)} max {P(r.bounds.max)}");
                }
                finally { EditorSceneManager.ClosePreviewScene(preview); }
            }

            // 2–5：場景裡的集電弓
            var mech = Object.FindObjectsOfType<PantographMechanism>(true).FirstOrDefault();
            if (mech == null) { sb.AppendLine("\n場景裡沒有 PantographMechanism（請先開 SC_CarbonStripSlice）"); Write(sb); return; }
            var rig = mech.transform;
            sb.AppendLine($"\n== 2. 場景裡的父子結構 ==\nPantographMechanism 在：{PathOf(rig)}（世界 {P(rig.position)}）");
            var meshParents = rig.GetComponentsInChildren<Renderer>(true).GroupBy(r => r.transform.parent != null ? PathOf(r.transform.parent) : "(root)");
            sb.AppendLine("網格的父物件（數量）：");
            foreach (var g in meshParents.OrderBy(g => g.Key)) sb.AppendLine($"  {g.Key}（{g.Count()}）");
            var sims = Object.FindObjectsOfType<MonoBehaviour>(true).OfType<ISimObject>().Where(o => !string.IsNullOrEmpty(o.Id) && (o.Id.StartsWith("ip.") || o.Id.StartsWith("part."))).ToList();
            sb.AppendLine($"ip.*／part.* 互動物件 {sims.Count} 個：");
            foreach (var o in sims.OrderBy(o => o.Id)) { var t = ((Component)o).transform; sb.AppendLine($"  {o.Id,-42} {PathOf(t)}{(t.IsChildOf(rig) ? "" : "　〔不在集電弓底下〕")}"); }
            var named = Object.FindObjectsOfType<Transform>(true).Where(t => t.name.StartsWith("PF_StripBolt") || t.name.StartsWith("Bolt_") || t.name.StartsWith("TGT_") || t.name.Contains("Measure")).ToList();
            sb.AppendLine($"PF_StripBolt／Bolt_／TGT_／量測點節點 {named.Count} 個：");
            foreach (var t in named.OrderBy(t => t.name)) sb.AppendLine($"  {t.name,-30} {PathOf(t)}{(t.IsChildOf(rig) ? "" : "　〔不在集電弓底下〕")}");

            // 3. 底架底面中心與四角（降弓）
            sb.AppendLine("\n== 3. 底架底面（PAN_Root 原點與 4 個螺栓孔） ==");
            float lift = mech.Lift;
            mech.SetLift(0f);
            var baseT = rig.Find("Base");
            if (baseT != null && baseT.GetComponent<Renderer>() is Renderer br)
            {
                var b = br.bounds;
                sb.AppendLine($"底架 Base：範圍 {P(b.min)}–{P(b.max)}");
                sb.AppendLine($"底面中心（PAN_Root 原點）：{P(new Vector3(b.center.x, b.min.y, b.center.z))}");
                foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                    sb.AppendLine($"  四角 ({sx:+0;-0}, {sz:+0;-0})：{P(new Vector3(b.center.x + sx * b.extents.x, b.min.y, b.center.z + sz * b.extents.z))}");
            }
            else sb.AppendLine("找不到 Base 網格");
            var ins = Object.FindObjectsOfType<Transform>(true).Where(t => t.name == "Insulator").ToList();
            sb.AppendLine($"車頂絕緣子（RoofSiteBuilder 產生）{ins.Count} 支：" + string.Join("、", ins.Select(t => P(t.position + Vector3.up * t.lossyScale.y))) + "（頂面）");
            mech.SetLift(lift);

            // 4. 升降方式
            sb.AppendLine("\n== 4. PantographMechanism 的升降方式 ==");
            sb.AppendLine("旋轉臂（關節內插）：模型網格是升弓姿態；每個 Joint 記錄降弓時相對升弓的角度與位移，SetLift 依 Lift 內插 segment.localRotation／localPosition；AimLink 把連桿另一端對準目標。");
            sb.AppendLine("不是切換網格，也不是內插世界位置。");
            foreach (var f in new[] { "Assets/_Project/Modules/Parts/Mechanism", "Assets/_Project/Modules/Parts/_Common", "Assets/_Project/Modules/Parts/CarbonStrip" })
                foreach (var file in Directory.GetFiles(f, "*.cs", SearchOption.AllDirectories))
                {
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                        if (lines[i].Contains("transform.position =") || lines[i].Contains(".position = ") || lines[i].Contains("Vector3.zero"))
                            sb.AppendLine($"  {file.Replace('\\', '/')}:{i + 1}　{lines[i].Trim()}");
                }

            // 5. 升弓姿態的網格
            sb.AppendLine("\n== 5. 升弓姿態那組網格 ==");
            var raised = Object.FindObjectsOfType<Transform>(true).Where(t => t.name.ToLower().Contains("raised") || t.name.Contains("升弓")).ToList();
            sb.AppendLine(raised.Count == 0
                ? "場景裡沒有另外一組升弓網格：同一組網格由關節轉到升弓或降弓（見第 4 項）。"
                : "名稱含 raised／升弓 的物件：\n" + string.Join("\n", raised.Select(t => $"  {PathOf(t)}　{P(t.position)}　active={t.gameObject.activeInHierarchy}")));
            Write(sb);
        }

        static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene s) => UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, s);

        static void Write(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(true));
            Debug.Log("[PanRiggingProbe] " + ReportPath);
        }
    }
}
