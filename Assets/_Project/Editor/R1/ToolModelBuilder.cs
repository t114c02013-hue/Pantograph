using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PantographSim.Data;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// a6 增補 7（D39，torque-wrench-sockets.md v1.3）：套筒 prefab TL_Socket_*（Tip 在 (0, −heightM, 0)、旋轉 (90, 0, 0)、Capsule r 1 cm 長 2 cm）、
    /// 兩支扭力扳手 prefab PF_TorqueWrench／PF_TorqueWrench_L（Attach_Grip、Visual、FX_Head、Poke_Plus、Poke_Minus、COL_Body、Tip、Socket_Mount、FX_CalLabel），
    /// 以及建置檢查（§4 第 1–4 項、§6.6 第 5–8 項、§7.5 第 9–12 項）。執行時的判定（選錯套筒、量程、方頭）是 R1.5b b4c。
    /// shortcut: PF_SocketWrench 沒有 prefab（R1 的套筒扳手是 XrToolWidgets 執行期建的，Socket_Mount 在那裡）, R1.5b 把套筒扳手做成 prefab 時套用同一條 Socket_Mount 規則
    /// </summary>
    public static class ToolModelBuilder
    {
        public const string SocketPrefabFolder = "Assets/_Project/Tools/Sockets/Prefabs";
        public const string WrenchModelFolder = "Assets/_Project/Tools/TorqueWrench/Models";
        public const string WrenchPrefabFolder = "Assets/_Project/Tools/TorqueWrench/Prefabs";
        public const string WrenchParamsPath = "Assets/_Project/Tools/TorqueWrench/Data/tool_torque_wrenches.json";

        [System.Serializable] class WrenchParams { public string toolId, fbx, type; public float totalLengthM, handleLengthM, headWidthM, headThickM, driveSquareM; }
        [System.Serializable] class WrenchFile { public WrenchParams[] wrenches; }

        /// <summary>這次建置實測的 Socket_Mount（Attach_Grip 局部）。</summary>
        public static readonly Dictionary<string, Vector3> MeasuredMount = new Dictionary<string, Vector3>();

        public static string Build()
        {
            var sb = new StringBuilder();
            var cat = ToolSocketSetup.Catalog;
            Directory.CreateDirectory(SocketPrefabFolder); Directory.CreateDirectory(WrenchPrefabFolder);
            foreach (var s in cat.sockets)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{RoofSiteBuilder.GeneratedFolder}/{s.prefab}.fbx");
                if (model == null) { sb.AppendLine($"⚠ 缺 {s.prefab}.fbx（跑 gen_700t.py）"); continue; }
                var go = new GameObject(s.prefab);   // 根節點 Transform 全 0（掛到 Socket_Mount 用）；FBX 放在 Visual，保留它的軸向轉換
                var vis = (GameObject)PrefabUtility.InstantiatePrefab(model); vis.name = "Visual"; vis.transform.SetParent(go.transform, false);
                var tip = new GameObject("Tip").transform;
                tip.SetParent(go.transform, false);
                tip.localPosition = new Vector3(0f, -s.heightM, 0f); tip.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var cap = tip.gameObject.AddComponent<CapsuleCollider>(); cap.radius = 0.01f; cap.height = 0.02f; cap.direction = 2; cap.isTrigger = true;
                PrefabUtility.SaveAsPrefabAsset(go, $"{SocketPrefabFolder}/{s.prefab}.prefab");
                Object.DestroyImmediate(go);
            }
            var file = JsonUtility.FromJson<WrenchFile>(File.ReadAllText(WrenchParamsPath));
            foreach (var w in file.wrenches)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{WrenchModelFolder}/{w.fbx}.fbx");
                if (model == null) { sb.AppendLine($"⚠ 缺 {w.fbx}.fbx（跑 Tools/Blender/gen_tools/gen_torque_wrench.py）"); continue; }
                var tool = w.toolId == "tool.torque_wrench_l" ? ToolSocketSetup.LargeWrench : StepJsonImporter.LoadTools().First(t => t.toolId == w.toolId);
                var root = new GameObject(tool.prefab);
                Transform Kid(string n, Vector3 p) { var t = new GameObject(n).transform; t.SetParent(root.transform, false); t.localPosition = p; return t; }
                Kid("Attach_Grip", Vector3.zero);
                var vis = (GameObject)PrefabUtility.InstantiatePrefab(model); vis.name = "Visual"; vis.transform.SetParent(root.transform, false);
                // Socket_Mount：方頭（DriveSquare）根部截面中心＝方頭頂點中 Y 最大那一圈的中心（不用 Bounds 中心）
                var drive = vis.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == "DriveSquare");
                Vector3 mount = new Vector3(0f, -w.headThickM / 2f, w.handleLengthM);   // 推估值（EX）
                if (drive != null)
                {
                    var vs = drive.sharedMesh.vertices.Select(v => root.transform.InverseTransformPoint(drive.transform.TransformPoint(v))).ToList();
                    float top = vs.Max(v => v.y);
                    var ring = vs.Where(v => v.y > top - 1e-4f).ToList();
                    mount = new Vector3(ring.Average(v => v.x), top, ring.Average(v => v.z));
                }
                MeasuredMount[w.toolId] = mount;
                Kid("Socket_Mount", mount);   // 旋轉和 Attach_Grip 相同；−Y＝套筒軸（朝套筒口）
                Kid("Tip", mount + Vector3.down * 0.012f).gameObject.SetActive(true);   // 扳手本身的 Tip（掛上套筒後判定改讀套筒的 Tip，R1.5b 停用）
                Kid("FX_Head", new Vector3(0f, 0f, w.handleLengthM));
                if (w.type == "ratchet") { var pk = vis.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Poke_Ratchet"); if (pk != null) pk.SetParent(root.transform, true); }   // 撥桿是獨立小件，放到根下面
                else
                {
                    Kid("Poke_Plus", new Vector3(0.02f, 0.02f, w.handleLengthM * 0.55f));
                    Kid("Poke_Minus", new Vector3(-0.02f, 0.02f, w.handleLengthM * 0.55f));
                }
                var col = Kid("COL_Body", new Vector3(0f, 0f, w.handleLengthM - w.totalLengthM / 2f + w.headWidthM / 2f)).gameObject.AddComponent<BoxCollider>();
                col.size = new Vector3(w.headWidthM, w.headThickM, w.totalLengthM);
                PrefabUtility.SaveAsPrefabAsset(root, $"{WrenchPrefabFolder}/{tool.prefab}.prefab");
                Object.DestroyImmediate(root);
                sb.AppendLine($"{tool.prefab}：Socket_Mount 實測（Attach_Grip 局部）{mount:F4}（推估 {new Vector3(0f, -w.headThickM / 2f, w.handleLengthM):F4}）");
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        /// <summary>建置檢查（回傳不合格項目，空＝通過；info 是量測值）。</summary>
        public static List<string> Check(List<string> info)
        {
            var bad = new List<string>();
            var cat = ToolSocketSetup.Catalog;
            var tools = StepJsonImporter.LoadTools().Append(ToolSocketSetup.LargeWrench).Where(t => t != null && t.driveIn > 0f).ToList();
            GameObject Pf(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var t in tools.Where(t => t.toolId.StartsWith("tool.torque_wrench") || t.toolId == "tool.socket_wrench"))
            {
                var pf = Pf($"{WrenchPrefabFolder}/{t.prefab}.prefab");
                if (pf == null) { bad.Add($"§4-1 缺 {t.prefab}"); continue; }
                var mountT = pf.transform.Find(t.socketMountNode); var grip = pf.transform.Find("Attach_Grip");
                if (mountT == null || grip == null) { bad.Add($"§4-1 {t.prefab} 缺 Socket_Mount／Attach_Grip"); continue; }
                // §4-1：方頭根部截面中心 ± 2 mm（用網格重新量）
                var drive = pf.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == "DriveSquare");
                if (drive == null) bad.Add($"§4-1 {t.prefab} 缺 DriveSquare 網格");
                else
                {
                    var vs = drive.sharedMesh.vertices.Select(v => pf.transform.InverseTransformPoint(drive.transform.TransformPoint(v))).ToList();
                    float top = vs.Max(v => v.y); var ring = vs.Where(v => v.y > top - 1e-4f).ToList();
                    var c = new Vector3(ring.Average(v => v.x), top, ring.Average(v => v.z));
                    float e = Vector3.Distance(c, pf.transform.InverseTransformPoint(mountT.position));
                    info.Add($"§4-1 {t.prefab} Socket_Mount 和方頭根部截面中心差 {e * 1000f:0.0} mm");
                    if (e > 0.002f) bad.Add($"§4-1 {t.prefab} Socket_Mount 偏離方頭根部 {e * 1000f:0.0} mm");
                }
                // §6.6-5：Socket_Mount 到 Attach_Grip 的 Z 距離＝handleLengthM ± 2 mm
                float dz = mountT.position.z - grip.position.z;
                info.Add($"§6.6-5 {t.prefab} Socket_Mount 到 Attach_Grip Z {dz:0.000}（handleLengthM {t.handleLengthM}）");
                if (Mathf.Abs(dz - t.handleLengthM) > 0.002f) bad.Add($"§6.6-5 {t.prefab} Z {dz:0.000} ≠ handleLengthM {t.handleLengthM}");
                // §6.6-7：FX_CalLabel、calibration
                if (t.toolId != "tool.socket_wrench")   // 套筒扳手不是量具，沒有校正標籤
                {
                    if (!pf.GetComponentsInChildren<Transform>(true).Any(x => x.name == "FX_CalLabel")) bad.Add($"§6.6-7 {t.prefab} 缺 FX_CalLabel");
                    if (t.calibration == null || !t.calibration.IsSet) bad.Add($"§6.6-7 {t.toolId} 缺 calibration");
                }
                // §4-2：掛上每一顆套筒後 Tip 到 Socket_Mount＝heightM ± 1 mm、Tip +Z 和 Socket_Mount −Y 夾角 ≤ 1°
                foreach (var id in t.socketIds)
                {
                    var s = cat.Find(id); var sp = s != null ? Pf($"{SocketPrefabFolder}/{s.prefab}.prefab") : null;
                    if (sp == null) { bad.Add($"§4-2 {t.toolId} 的 {id} 沒有 prefab"); continue; }
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf); var m = inst.transform.Find(t.socketMountNode);
                    var sock = (GameObject)PrefabUtility.InstantiatePrefab(sp); sock.transform.SetParent(m, false);
                    var tip = sock.transform.Find("Tip");
                    float d = Vector3.Distance(tip.position, m.position), ang = Vector3.Angle(tip.forward, -m.up);
                    info.Add($"§4-2 {t.toolId}＋{s.prefab}：Tip 到 Socket_Mount {d:0.0000}（heightM {s.heightM}）、夾角 {ang:0.0}°");
                    if (Mathf.Abs(d - s.heightM) > 0.001f || ang > 1f) bad.Add($"§4-2 {t.toolId}＋{s.prefab} Tip 距離 {d:0.0000}／夾角 {ang:0.0}°");
                    Object.DestroyImmediate(inst);
                }
            }
            // §4-3：TL_Socket_* 的 Bounds 高＝heightM ± 1 mm
            foreach (var s in cat.sockets)
            {
                var sp = Pf($"{SocketPrefabFolder}/{s.prefab}.prefab");
                if (sp == null) { bad.Add($"§4-3 缺 {s.prefab}.prefab"); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(sp);
                var rs = inst.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                info.Add($"§4-3 {s.prefab} Bounds 高 {b.size.y:0.0000}（heightM {s.heightM}）、外徑 {b.size.x:0.0000}（{s.outerDiaM}）");
                if (Mathf.Abs(b.size.y - s.heightM) > 0.001f) bad.Add($"§4-3 {s.prefab} 高 {b.size.y:0.0000} ≠ {s.heightM}");
                Object.DestroyImmediate(inst);
            }
            // §4-4：tool_sockets.json 的雜湊和目前資料一致
            var json = File.ReadAllText(ToolSocketSetup.SocketsJsonPath);
            if (ToolSocketSetup.Hash(json) != File.ReadAllText(ToolSocketSetup.SocketsHashPath).Trim()) bad.Add("§4-4 tool_sockets.json 雜湊不一致");
            foreach (var s in cat.sockets)
                if (!json.Contains($"\"socketId\": \"{s.socketId}\"") || !json.Contains($"\"heightM\": {s.heightM.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}")) bad.Add($"§4-4 tool_sockets.json 和目錄的 {s.socketId} 不一致");
            // §6.6-6／§7.5-9：每顆套筒的 driveIn＝那支扳手的 driveIn
            foreach (var t in tools)
                foreach (var id in t.socketIds)
                {
                    var s = cat.Find(id);
                    if (s == null) bad.Add($"§7.5-9 {t.toolId} 的 {id} 不在套筒目錄");
                    else if (Mathf.Abs(s.driveIn - t.driveIn) > 1e-4f) bad.Add($"§7.5-9 {t.toolId}（{t.driveIn}\"）的 {id} 方頭 {s.driveIn}\"");
                }
            // §7.5-10：同一個 prefab 在多支扳手裡數值相同——數值只在目錄寫一次（SocketDef 唯一），檢查目錄裡沒有重複 prefab
            foreach (var g in cat.sockets.GroupBy(s => s.prefab).Where(g => g.Count() > 1)) bad.Add($"§7.5-10 目錄裡 {g.Key} 出現 {g.Count()} 次");
            info.Add("§7.5-10 " + string.Join("、", cat.sockets.Select(s => $"{s.prefab}：{string.Join("／", tools.Where(t => t.socketIds.Contains(s.socketId)).Select(t => t.toolId))}")));
            // §6.6-8：掛位在隨身工具袋（R1.5b b3）：slot_6 套筒扳手、slot_7 小扭力扳手、slot_13 大扭力扳手（只有定期檢修）
            foreach (var (id, slot) in new[] { ("tool.socket_wrench", 6), ("tool.torque_wrench", 7), ("tool.torque_wrench_l", 13) })
            {
                bool ok = Object.FindObjectsOfType<Transform>(true).Any(x => x.name == "Pocket_" + slot);
                info.Add($"§6.6-8 {id} 的工具袋掛位 Pocket_{slot}：{(ok ? "有" : "沒有")}");
                if (!ok) bad.Add($"§6.6-8 找不到 {id} 的工具袋掛位 Pocket_{slot}");
            }
            // §7.5-11：socket_case 有 6 個 TGT_Socket_*，名稱和所有扳手 sockets[] 的 prefab 一一對應
            var tgts = Object.FindObjectsOfType<Transform>(true).Where(x => x.name.StartsWith("TGT_Socket_")).Select(x => x.name.Substring(11)).OrderBy(x => x).ToList();
            var need = tools.SelectMany(t => t.socketIds).Distinct().Select(id => cat.Find(id)?.prefab?.Replace("TL_Socket_", "")).Where(x => x != null).OrderBy(x => x).ToList();
            info.Add($"§7.5-11 套筒盒 TGT_Socket_* {tgts.Count} 個：{string.Join("、", tgts)}");
            if (!tgts.SequenceEqual(need)) bad.Add($"§7.5-11 套筒盒 {string.Join("、", tgts)} ≠ 扳手套筒 {string.Join("、", need)}");
            // §7.5-12（R1.5b b3 改寫）：SubActions 裡「換上 N mm」那一步，該步工具的 sockets[] 有 sizeMm＝N 的套筒，而且這一步的螺栓 FastenerDef.sizeMm＝N
            foreach (var step in StepJsonImporter.LoadAll<StepDefinition>(StepJsonImporter.StepFolder))
                foreach (var a in step.subActions ?? new SubAction[0])
                {
                    var m = Regex.Match(a.text ?? "", @"換上\s*(\d+)\s*mm");
                    if (!m.Success) continue;
                    int mm = int.Parse(m.Groups[1].Value);
                    var t = tools.FirstOrDefault(x => x.toolId == a.toolId);
                    var boltSizes = step.subActions.Where(x => (x.targetId ?? "").Contains(".bolt_")).Select(x => cat.SizeFor(StateKeys.Expand(x.targetId, "L", 1))).Where(x => x > 0).Distinct().ToList();
                    bool ok = t != null && t.socketIds.Any(id => cat.Find(id)?.sizeMm == mm) && boltSizes.All(x => x == mm);
                    info.Add($"§7.5-12 {step.stepId}「{a.text}」工具 {a.toolId}：{(ok ? "有" : "沒有")} {mm} mm；這一步的螺栓要 {string.Join("／", boltSizes)} mm");
                    if (!ok) bad.Add($"§7.5-12 {step.stepId}「{a.text}」的工具 {a.toolId} 沒有 {mm} mm 套筒");
                }
            return bad;
        }
    }
}
