using System.Collections.Generic;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Worksite;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PantographSim.Editor
{
    /// <summary>
    /// terry 2026-11-15（150A、151A、181C、182A、183A、154–166）：指示燈點光源（LampLight）、實體按鈕（PressFollow）、地面教學箱開蓋（LidToggle）、
    /// 地面維修區第二出口。數值都在 RoofSceneDef；節點依名稱找，找不到就略過並寫進建置報告。
    /// </summary>
    public static partial class RoofSiteBuilder
    {
        public const string RoofBoxFolder = "Assets/_Project/Worksite/Models/RoofBoxes";
        /// <summary>要加點光源的燈罩：升降盒、複本燈、自檢器 LED、隔離燈號綠／紅。</summary>
        public static readonly string[] LampNodes = { "PanIndicator/PanIndicator_Lens", "PanIndicator_Repeat", "DetectorTester/LED", "StatusLamp_Green/StatusLamp_Green_Lens", "StatusLamp_Red/StatusLamp_Red_Lens" };

        public const string MaintWalkName = "Walkable_Maintenance";

        /// <summary>教學箱外框（XZ，由 FBX 推導；FBX 不在就不算）。</summary>
        static Rect BoxFootprint(RoofSceneDef d, RoofSceneDef.TrainingBox tb)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{RoofBoxFolder}/{tb.fbx}.fbx");
            if (model == null) return Rect.zero;
            var g = (GameObject)Object.Instantiate(model);
            try
            {
                g.transform.SetPositionAndRotation(new Vector3(tb.centerXZ.x, d.groundY + tb.standHeight, tb.centerXZ.y), Quaternion.Euler(0f, tb.facingNorth ? 0f : 180f, 0f) * model.transform.rotation);
                var rs = g.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) return Rect.zero;
                var b = rs.Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                return Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
            }
            finally { Object.DestroyImmediate(g); }
        }

        /// <summary>要從 Walkable 挖掉的範圍：各箱外框，加上 X 間距 ≤ walkGapFillMax 且 Z 重疊的相鄰兩箱之間的縫（只挖 Z 重疊那段）。</summary>
        public static List<Rect> CarveRects(IList<Rect> boxes, float gapMax)
        {
            var list = boxes.Where(r => r.width > 0f).ToList();
            var gaps = new List<Rect>();
            foreach (var p in list) foreach (var q in list)
            {
                if (q.xMin < p.xMax) continue;   // 只看 q 在 p 東邊
                float gap = q.xMin - p.xMax, z0 = Mathf.Max(p.yMin, q.yMin), z1 = Mathf.Min(p.yMax, q.yMax);
                if (gap > 0f && gap <= gapMax && z1 > z0) gaps.Add(Rect.MinMaxRect(p.xMax, z0, q.xMin, z1));
            }
            return list.Concat(gaps).ToList();
        }

        static List<Rect> TrainingBoxCarve(RoofSceneDef d) => CarveRects((d.trainingBoxes ?? new RoofSceneDef.TrainingBox[0]).Select(tb => BoxFootprint(d, tb)).ToList(), d.walkGapFillMax);

        static bool Inside(Rect r, Vector2 p) => p.x > r.xMin && p.x < r.xMax && p.y > r.yMin && p.y < r.yMax;

        static void CarveCheck(RoofSceneDef d, List<Rect> carved, List<Rect> walk, StringBuilder sb)
        {
            if (carved.Count == 0) { sb.AppendLine("・地面維修區 Walkable：沒有教學箱 FBX，不挖，一整塊"); return; }
            float area = (d.maintMaxXZ.x - d.maintMinXZ.x) * (d.maintMaxXZ.y - d.maintMinXZ.y);
            bool overlap = walk.Any(w => carved.Any(c => w.Overlaps(c)));
            float walkArea = walk.Sum(w => w.width * w.height), carvedArea = carved.Sum(c => c.width * c.height);
            bool sums = Mathf.Abs(walkArea + carvedArea - area) < 0.01f;
            sb.AppendLine($"{(!overlap && sums ? "✔" : "✘")} 地面維修區 Walkable {walk.Count} 塊，和挖掉的 {carved.Count} 塊（箱子＋縫）不重疊；Walkable {walkArea:0.00} ＋ 挖掉 {carvedArea:0.00} ＝ {area:0.00} m²");
            var ground = R1Data.Stations.stations.Where(s => s != null && s.floor == StationFloor.Ground).ToList();
            var bad = ground.Where(s => carved.Any(c => Inside(c, s.WorldXZ(d)))).Select(s => s.id).ToList();
            sb.AppendLine($"{(bad.Count == 0 ? "✔" : "✘")} 地面站點 {ground.Count} 個都不在挖掉的範圍內{(bad.Count > 0 ? "：" + string.Join("、", bad) : "")}");
        }

        static Transform FindPath(Transform root, string path)
        {
            var parts = path.Split('/');
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == parts[parts.Length - 1] && (parts.Length == 1 || (t.parent != null && t.parent.name == parts[0])));
        }

        static void BuildTeachingExtras(Result res, RoofSceneDef d, StringBuilder sb)
        {
            // 指示燈點光源（暗時留弱光；亮暗由各燈的元件呼叫 LampLight.Set）
            int lamps = 0;
            foreach (var path in LampNodes)
            {
                var lamp = FindPath(res.env, path);
                if (lamp == null) { sb.AppendLine($"✘ 指示燈 {path} 找不到，沒有加點光源"); continue; }
                var go = new GameObject("LampLight");
                go.transform.SetParent(lamp, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point; l.shadows = LightShadows.None; l.range = d.lampLightRange;
                l.renderMode = d.lampLightImportant ? LightRenderMode.ForcePixel : LightRenderMode.Auto;
                l.intensity = d.lampLightIntensity * d.lampDimRatio;
                lamp.gameObject.AddComponent<LampLight>().Setup(l, d.lampLightIntensity, d.lampDimRatio);
                lamps++;
            }
            sb.AppendLine($"{(lamps == LampNodes.Length ? "✔" : "✘")} 指示燈點光源 {lamps}／{LampNodes.Length} 個（範圍 {d.lampLightRange} m、亮 {d.lampLightIntensity}、暗 ×{d.lampDimRatio}、{(d.lampLightImportant ? "Important" : "Auto")}）");

            // 實體按鈕（新升降盒、自檢器 FBX 到了才有 Btn_* 節點）
            foreach (var b in d.btnBindings ?? new RoofSceneDef.BtnBinding[0])
            {
                var btn = FindPath(res.env, b.node);
                if (btn == null) { sb.AppendLine($"・按鈕 {b.node}：場景裡還沒有這個節點（等新模型），略過"); continue; }
                btn.gameObject.AddComponent<PressFollow>().Setup(b.key, b.value, b.hold, d.btnPressMm / 1000f, d.btnPressSeconds);
                sb.AppendLine($"✔ 按鈕 {b.node} 跟 {b.key}{(b.hold ? "＝" + b.value + "（按住陷著）" : "（每變一次陷一下）")}");
            }

            // 地面教學箱（車頂與地面共用 FBX；只有地面這組掛 LidToggle）
            foreach (var tb in d.trainingBoxes ?? new RoofSceneDef.TrainingBox[0])
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{RoofBoxFolder}/{tb.fbx}.fbx");
                if (model == null) { sb.AppendLine($"✘ 教學箱 {tb.kind}：找不到 {RoofBoxFolder}/{tb.fbx}.fbx（Tools/Blender/gen_roof_boxes）"); continue; }
                var box = (GameObject)Object.Instantiate(model, res.env);
                box.name = "TrainingBox_" + tb.kind;
                box.transform.position = new Vector3(tb.centerXZ.x, d.groundY + tb.standHeight, tb.centerXZ.y);
                box.transform.rotation = Quaternion.Euler(0f, tb.facingNorth ? 0f : 180f, 0f) * model.transform.rotation;
                // 鋼架 ENV_TrainingBoxStand_<種類>（建模師 B gen_roof_boxes v1.1，原點在地面中心）；掛在箱子底下，挖空、頭穿透和箱子一起算
                var standModel = AssetDatabase.LoadAssetAtPath<GameObject>($"{RoofBoxFolder}/{tb.fbx.Replace("ENV_RoofBox_", "ENV_TrainingBoxStand_")}.fbx");
                if (standModel == null) sb.AppendLine($"✘ 教學箱 {tb.kind}：找不到鋼架 FBX");
                else
                {
                    var stand = (GameObject)Object.Instantiate(standModel, box.transform);
                    stand.name = "Stand";
                    stand.transform.SetPositionAndRotation(new Vector3(tb.centerXZ.x, d.groundY, tb.centerXZ.y), box.transform.rotation);
                    fbxPlaced?.Add((stand, d.groundY));   // RA-27 地板檢查也查鋼架
                }
                // 字牌（Label_* 局部 +Z 朝讀者；TextMesh 正面朝 −Z，所以轉 180°）：箱名、正面與 Lid_2 的警示牌
                void Text(string node, string text, float size, Color ink)
                {
                    var t = box.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == node);
                    if (t == null || string.IsNullOrEmpty(text)) return;
                    Label("Text", t, text, t.position + t.forward * 0.002f, Quaternion.LookRotation(-t.forward, t.up), size, ink);
                }
                Text("Label_Name", tb.displayName, d.boxNameCharSize, Color.black);
                Text("Label_Warn", tb.warnText, d.boxWarnCharSize, Color.black);
                Text("Lid_2_Label_Warn", tb.warnText, d.boxLidWarnCharSize, Color.black);
                var insides = box.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Inside_")).Select(t => t.gameObject).ToArray();
                foreach (var c in insides.SelectMany(g => g.GetComponentsInChildren<Collider>(true))) c.enabled = false;   // 內部不擋射線
                // 4c：箱體 Body 用非凸 MeshCollider（HeadSolidDef），內部是空的；蓋子全關時另有一塊「箱內實心」框（Body 內部空間），任一片打開就關掉
                var bodyR = box.GetComponentsInChildren<Renderer>(true).First(r => r.name == "Body");
                var interior = new GameObject(HeadSolidPrefix + "Interior"); interior.transform.SetParent(box.transform, false); interior.layer = LayerMask.NameToLayer(SceneLayers.HeadSolid);
                var bodyMesh = bodyR.GetComponent<MeshFilter>().sharedMesh.bounds; var ic = interior.AddComponent<BoxCollider>();
                ic.center = interior.transform.InverseTransformPoint(bodyR.transform.TransformPoint(bodyMesh.center)) + Vector3.up * 0.01f; ic.size = bodyMesh.size - new Vector3(0.04f, 0.02f, 0.04f);   // 扣掉牆厚 0.02、底板 0.02
                var lids = box.GetComponentsInChildren<Transform>(true).Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.name, @"^Lid_\d+$")).ToList();
                var toggles = lids.Select(t => t.gameObject.AddComponent<LidToggle>()).ToArray();
                for (int i = 0; i < lids.Count; i++)
                {
                    var lid = lids[i];
                    if (lid.GetComponent<Collider>() == null && lid.GetComponent<MeshFilter>() != null) lid.gameObject.AddComponent<BoxCollider>();
                    toggles[i].Configure($"env.roof_box.{tb.kind}.lid_{lid.name.Substring(4)}", $"{tb.displayName}蓋子 {lid.name.Substring(4)}");
                    toggles[i].Setup(tb.overrideLid > 0 && lid.name == "Lid_" + tb.overrideLid && tb.lidOpenDegOverride > 0f ? tb.lidOpenDegOverride : d.lidOpenDeg, d.lidOpenSeconds, toggles, insides, interior);
                    lid.gameObject.AddComponent<Highlightable>();
                    res.ids[toggles[i].Id] = lid;
                }
                foreach (var g in insides) g.SetActive(false);
                sb.AppendLine($"✔ 教學箱 {tb.kind}：({tb.centerXZ.x}, {tb.centerXZ.y})、鋼架 {tb.standHeight} m、朝{(tb.facingNorth ? "北" : "南")}、蓋子 {lids.Count} 片、內部 {insides.Length} 組");
            }
            // 匯入方向（GeneratedModelPostprocessor）、蓋子全開的間隙、把手伸手（RA-31：站點在箱子正面，每片 ≤ 0.55 m）
            sb.AppendLine("匯入：Generated/ 有 Bake Axis Conversion（正面 −Z、子節點局部軸不跟著轉）；RoofBoxes/ 不 bake（正面 +Z、Lid_n 繞局部 +X 正轉＝往上開）");
            var allLids = res.env.GetComponentsInChildren<LidToggle>(true).Where(l => l.transform.GetComponentsInParent<Transform>(true).Any(p => p.name.StartsWith("TrainingBox_"))).ToList();
            if (allLids.Count > 0)
            {
                Bounds LB(Transform t) => t.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponent<TextMesh>() == null).Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                Transform BoxOf(LidToggle l) => l.transform.GetComponentsInParent<Transform>(true).First(p => p.name.StartsWith("TrainingBox_"));
                var deg = typeof(LidToggle).GetField("openDeg", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var opened = allLids.Select(l => { var r0 = l.transform.localRotation; l.transform.localRotation = r0 * Quaternion.Euler((float)deg.GetValue(l), 0f, 0f); var b = LB(l.transform); l.transform.localRotation = r0; return (l, b); }).ToList();
                float best = float.MaxValue; string pair = "";
                foreach (var x in opened) foreach (var y in opened.Where(y => BoxOf(y.l) != BoxOf(x.l)))
                {
                    float gap = Mathf.Max(Mathf.Max(x.b.min.x - y.b.max.x, y.b.min.x - x.b.max.x), Mathf.Max(x.b.min.z - y.b.max.z, y.b.min.z - x.b.max.z));
                    if (gap < best) { best = gap; pair = $"{BoxOf(x.l).name}/{x.l.name} 和 {BoxOf(y.l).name}/{y.l.name}"; }
                }
                sb.AppendLine($"教學箱蓋子全開：不同箱子的蓋子最近間隙 {best:0.000} m（{pair}）");
                var reach = R1Data.Stations.armReachHorizontal;
                foreach (var l in allLids)
                {
                    var h = l.GetComponentsInChildren<Renderer>(true).First(r => r.name.EndsWith("_Handle")).bounds.center;
                    var st = d.trainingBoxStations.OrderBy(s => Vector2.Distance(s.xz, new Vector2(h.x, h.z))).First();
                    float dist = Vector2.Distance(st.xz, new Vector2(h.x, h.z));
                    sb.AppendLine($"{(dist <= reach + 1e-4f ? "✔" : "✘")} 把手伸手 {BoxOf(l).name}/{l.name}：離 {st.id} 水平 {dist:0.000} m（伸手 ≤ {reach}，RA-31）");
                }
            }
            int roofLids = res.env.GetComponentsInChildren<LidToggle>(true).Count(l => !l.transform.GetComponentsInParent<Transform>(true).Any(p => p.name.StartsWith("TrainingBox_")));
            sb.AppendLine($"{(roofLids == 0 ? "✔" : "✘")} 車頂箱子沒有掛開蓋元件（{roofLids} 個）");
        }

        /// <summary>第二出口：Edge_S 在 maintExitXRange 斷開，放黃色半高逃生小門（門本身就是 BoxCollider，不能點）、兩側警示柱、「緊急出口」牌、地上箭頭。</summary>
        static void BuildMaintExit(Transform parent, RoofSceneDef d, Material mWarn, StringBuilder sb)
        {
            float gy = d.groundY, z = d.maintMinXZ.y, x0 = d.maintExitXRange.x, x1 = d.maintExitXRange.y, h = d.maintExitDoorHeight, th = d.maintExitDoorThickness;
            var root = Child(parent, "MaintExit");
            Box("ExitDoor", root, new Vector3((x0 + x1) / 2f, gy + h / 2f, z - th / 2f), new Vector3(x1 - x0, h, th), mWarn);
            var mBlack = Mat("hazardBlack", new Color(0.08f, 0.08f, 0.08f));
            foreach (float x in new[] { x0, x1 })
                for (int i = 0; i < 5; i++)   // 柱子黃黑相間
                    Box("ExitPost", root, new Vector3(x, gy + h * (i + 0.5f) / 5f, z - th / 2f), new Vector3(0.06f, h / 5f, 0.06f), i % 2 == 0 ? mWarn : mBlack, collider: false);
            Sign("ExitSign", root, "緊急出口", new Vector3((x0 + x1) / 2f, gy + d.maintExitSignHeight, z + 0.005f), Vector3.forward, new Vector2(0.4f, 0.14f), Mat("exitGreen", new Color(0.05f, 0.55f, 0.25f)), Color.white, 0.012f);
            var arrow = Child(root, "ExitArrow");   // 地上黃色箭頭朝門（−Z）
            Box("Shaft", arrow, new Vector3((x0 + x1) / 2f, gy + 0.006f, d.maintExitArrowZ + 0.15f), new Vector3(0.12f, 0.004f, 0.5f), mWarn, collider: false);
            foreach (float s in new[] { -1f, 1f })
            {
                var head = Box("Head", arrow, new Vector3((x0 + x1) / 2f + s * 0.09f, gy + 0.006f, d.maintExitArrowZ - 0.15f), new Vector3(0.08f, 0.004f, 0.3f), mWarn, collider: false);
                head.transform.rotation = Quaternion.Euler(0f, -s * 35f, 0f);
            }
            float gapW = d.maintExitXRange.x - d.maintMinXZ.x, gapE = d.maintMaxXZ.x - d.maintExitXRange.y;
            bool ok = gapW > 0f && gapE > 0f;   // 擋牆兩段＋門的 X 範圍首尾相接（門寬＝斷開的寬度）
            sb.AppendLine($"{(ok ? "✔" : "✘")} 第二出口：Edge_S X {d.maintMinXZ.x}…{x0}｜門 {x0}…{x1}｜{x1}…{d.maintMaxXZ.x}，連起來沒有縫；門高 {h} m、厚 {th} m");
        }
    }
}
