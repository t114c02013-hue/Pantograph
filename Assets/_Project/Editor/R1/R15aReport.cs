using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PantographSim.Core;
using PantographSim.Data;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1.5a 驗收報告 Logs/R1.5aReport.txt（spec 8.3 七項＋附錄）：每項列「頭盔裡看什麼」、自動收集的證據、自動狀態（✔／✘／—）、terry 頭盔驗收欄。
    /// 全部唯讀（開著的場景、資產、Logs、Recordings），只寫報告檔；找不到東西就寫「找不到 X」，不丟例外。
    /// </summary>
    public static class R15aReport
    {
        public const string OutPath = "Logs/R1.5aReport.txt";
        const string TestsPath = "Logs/slice-tests.txt";
        const string LayoutPath = "Logs/StationLayoutCheck.txt";
        const string RecordingsDir = "Recordings";
        const int R1EndTestCount = 208;   // R1 S10 收尾 208/208
        const float R1EyeHeight = 1.60f;  // R1 StationSet.eyeHeight

        // 自動狀態：true＝✔、false＝✘、null＝—（只能在頭盔確認）
        readonly struct Item
        {
            public readonly string title, hint;
            public readonly Func<StringBuilder, bool?> evidence;
            public Item(string title, string hint, Func<StringBuilder, bool?> evidence) { this.title = title; this.hint = hint; this.evidence = evidence; }
        }

        static Dictionary<string, Transform> ids;   // ISimObject.Id → Transform（場景裡）
        static Transform[] all;
        static RoofSceneDef Def => AssetDatabase.LoadAssetAtPath<RoofSceneDef>(R1Data.RoofScenePath);   // 不用 R1Data.RoofScene：那個不存在會建資產
        static StationSet Set => AssetDatabase.LoadAssetAtPath<StationSet>(R1Data.StationSetPath);

        static readonly Item[] Items =
        {
            new Item("桌機 25 步 SI_01→結算，0 個紅色錯誤", "桌機模式從 SI_01 做到結算，Console 沒有紅字", Desktop),
            new Item("車輛外觀：白車身＋塗裝、無標誌；3 節車；車頂弧面、絕緣子、母線", "車身白色＋橘／深色塗裝、看不到任何標誌；三節車連著；車頂弧面、4 支絕緣子、母線", Car),
            new Item("平台：工具架靠外側護欄；車側 1.0 m 通道；活動欄、跨板、防墜器門架", "工具架在外側護欄旁、車側通道走得過、活動欄／跨板／防墜器門架都在", Platform),
            new Item("平台各站眼高與 R1 相同", "每個平台站點傳送後視線高度和 R1 一樣", EyeHeight),
            new Item("平台傳送到地面維修區，繞台架走、不穿模、出不了邊界", "傳送到 tp.stand.*，繞台架走一圈，不穿過台架，走不出維修區", Maintenance),
            new Item("天車、遙控器、吊帶、工具袋看得到但不能操作", "天車、遙控器、4 條吊帶、工具袋都看得到，手把抓不起來", Rigging),
            new Item("PanAssyFrameTests 通過；測試總數與 R1 結束時的差", "（不用頭盔）", TestResult),
        };

        /// <summary>batchmode 用（-executeMethod）：開切片場景再寫報告。</summary>
        public static void BatchWrite()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(SliceSceneBuilder.ScenePath);
            Write();
        }

        [MenuItem("PantographSim/R1.5a/Write Acceptance Report")]
        public static void Write()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"R1.5a 驗收報告（{DateTime.Now:yyyy-MM-dd HH:mm}）　場景：{UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}");
            sb.AppendLine("每項：頭盔檢查／自動證據／自動：✔ 通過、✘ 不通過、— 只能頭盔確認");
            try
            {
                all = Object.FindObjectsOfType<Transform>(true);
                ids = new Dictionary<string, Transform>();
                foreach (var o in Object.FindObjectsOfType<MonoBehaviour>(true).OfType<ISimObject>())
                    if (!string.IsNullOrEmpty(o.Id) && !ids.ContainsKey(o.Id)) ids[o.Id] = ((Component)o).transform;
            }
            catch (Exception e) { all = new Transform[0]; ids = new Dictionary<string, Transform>(); sb.AppendLine("讀場景失敗：" + e.Message); }

            for (int i = 0; i < Items.Length; i++)
            {
                var it = Items[i];
                sb.AppendLine().AppendLine($"== {i + 1}. {it.title} ==");
                sb.AppendLine("頭盔檢查：" + it.hint);
                sb.AppendLine("自動證據：");
                var ev = new StringBuilder();
                bool? ok;
                try { ok = it.evidence(ev); }
                catch (Exception e) { ok = null; ev.AppendLine("  讀取失敗：" + e.Message); }
                sb.Append(ev);
                sb.AppendLine($"自動：{(ok == true ? "✔" : ok == false ? "✘" : "—")}　terry 頭盔驗收：□");
            }

            sb.AppendLine().AppendLine("== 附錄 A：StationLayoutCheck ==");
            sb.AppendLine(File.Exists(LayoutPath) ? File.ReadAllText(LayoutPath).TrimEnd() : "找不到 " + LayoutPath);
            sb.AppendLine().AppendLine("== 附錄 B：Missing scripts ==");
            sb.AppendLine("見 NoMissingScriptTests");
            var nm = TestLines().Where(l => l.Contains("NoMissingScriptTests")).ToArray();
            foreach (var l in nm) sb.AppendLine("  " + l);
            if (nm.Length == 0) sb.AppendLine("  " + TestsPath + " 裡沒有 NoMissingScriptTests（還沒加入或還沒重跑測試）");
            sb.AppendLine().AppendLine("== 附錄 C：a6 補充（位置變更）＝場景實際座標 ==");
            foreach (var (label, key) in new[] { ("桿架錨點", "Anchor_PoleRack"), ("工具架錨點", "Anchor_ToolRack"), ("遙控器掛座 PendantDock", "PendantDock"), ("去駕駛室光圈", "mk.platform.to_cab"),
                ("備品架（日常擺放點）", "Anchor_SpareShelf_Daily"), ("待報廢箱（日常擺放點）", "Anchor_ScrapBox_Daily"), ("車體接地座 Clamp_Point", "Clamp_Point"), ("車頂暫放墊", "ENV_RoofStripMat"), ("腰包", "WaistPouch") })
                Exists(sb, key, true, label);

            Directory.CreateDirectory("Logs");
            File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(true));
            all = null; ids = null;
            Debug.Log("[R15aReport] 已寫入 " + OutPath);
        }

        // ---------- 各項證據 ----------

        static bool? Desktop(StringBuilder sb)
        {
            bool ok = true;
            if (!Directory.Exists(RecordingsDir)) { sb.AppendLine("  找不到 " + RecordingsDir + "/"); ok = false; }
            else
            {
                var dir = new DirectoryInfo(RecordingsDir).GetDirectories().OrderByDescending(d => d.LastWriteTime).FirstOrDefault();
                if (dir == null) { sb.AppendLine("  " + RecordingsDir + "/ 是空的"); ok = false; }
                else
                {
                    bool js = File.Exists(Path.Combine(dir.FullName, "session.jsonl")), html = File.Exists(Path.Combine(dir.FullName, "report.html"));
                    sb.AppendLine($"  最新紀錄：{dir.Name}（{dir.LastWriteTime:MM-dd HH:mm}）　session.jsonl {(js ? "有" : "沒有")}、report.html {(html ? "有" : "沒有")}");
                    ok &= js && html;
                }
            }
            var first = TestLines().FirstOrDefault();
            sb.AppendLine("  測試：" + (first ?? "找不到 " + TestsPath));
            ok &= first != null && TestCount(first, "失敗") == 0;
            return ok;
        }

        static bool? Car(StringBuilder sb)
        {
            var cars = new[] { "Car_South", "Car_Middle", "Car_North" };
            int n = cars.Count(c => Exists(sb, c));
            var d = Def;
            sb.AppendLine($"  車節：{n}（RoofSceneDef.carCount {(d != null ? d.carCount.ToString() : "找不到 RoofSceneDef")}）");
            bool ok = n == 3;
            ok &= Exists(sb, "ENV_RoofSection");
            for (int i = 1; i <= 4; i++) ok &= Exists(sb, "Insulator_" + i);
            ok &= Exists(sb, "Roof_Busbar");
            sb.AppendLine("  白車身／塗裝／無標誌、車頂弧面：只能頭盔確認");
            return ok;
        }

        // shortcut: 只回報數值, a6 補充定案後加門檻
        static bool? Platform(StringBuilder sb)
        {
            var d = Def;
            if (d == null) { sb.AppendLine("  找不到 RoofSceneDef"); return false; }
            sb.AppendLine($"  PlatformInnerX {d.PlatformInnerX:0.000}、PlatformOuterX {d.PlatformOuterX:0.000}、車側通道 platformAisle {d.platformAisle:0.00} m");
            bool ok = Exists(sb, "ENV_InspectionPlatform");
            foreach (var id in new[] { "env.tool_rack", "env.pole_rack" })
            {
                var t = Get(id);
                if (t == null) { sb.AppendLine("  找不到 " + id); ok = false; continue; }
                sb.AppendLine($"  {id}：{t.position:F3}　X − PlatformOuterX {t.position.x - d.PlatformOuterX:+0.000;-0.000}");
            }
            foreach (var n in new[] { "env.platform_gate", "ENV_GateBridge", "BridgePlate", "FallArrester", "FallArrester_Post" }) ok &= Exists(sb, n, true);
            return ok;
        }

        static bool? EyeHeight(StringBuilder sb)
        {
            var set = Set; var d = Def;
            if (set == null) { sb.AppendLine("  找不到 StationSet"); return false; }
            sb.AppendLine($"  StationSet.eyeHeight {set.eyeHeight:0.00}（R1 {R1EyeHeight:0.00}）　R1 平台地板 Y 0");
            var tps = TeleportPoints();
            foreach (var s in set.stations.Where(s => s != null && s.floor == StationFloor.Platform))
            {
                var xz = s.WorldXZ(d);
                sb.Append($"  {s.id,-22} 資料 XZ ({xz.x:0.000}, {xz.y:0.000})　");
                sb.AppendLine(tps.TryGetValue(s.id, out var t) ? $"場景 Y {t.position.y:0.000}　眼高 Y {t.position.y + set.eyeHeight:0.000}" : "找不到 TP（" + s.id + "）");
            }
            return Mathf.Approximately(set.eyeHeight, R1EyeHeight);
        }

        static bool? Maintenance(StringBuilder sb)
        {
            var set = Set;
            var tps = TeleportPoints();
            bool ok = true;
            foreach (var id in new[] { "tp.stand.n", "tp.stand.e", "tp.stand.s", "tp.stand.w" })
            {
                bool inSet = set != null && set.Find(id) != null, inScene = tps.TryGetValue(id, out var t);
                sb.AppendLine($"  {id}：StationSet {(inSet ? "有" : "沒有")}、場景 {(inScene ? t.position.ToString("F3") : "找不到")}");
                ok &= inSet && inScene;
            }
            ok &= Exists(sb, "ENV_MaintenanceArea");
            ok &= Exists(sb, "env.panto_stand");
            var area = Find("ENV_MaintenanceArea");
            foreach (var n in new[] { "Edge_W", "Edge_E", "Edge_S", "Edge_N" })
            {
                var e = area != null ? area.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n) : null;
                var c = e != null ? e.GetComponent<Collider>() : null;
                sb.AppendLine(e == null ? $"  找不到 ENV_MaintenanceArea/{n}" : $"  {n}：layer {LayerMask.LayerToName(e.gameObject.layer)}、Collider {(c != null && c.enabled ? "啟用" : "沒有或停用")}");
                ok &= c != null && c.enabled;
            }
            sb.AppendLine("  繞台架走、不穿模、出不了邊界：只能頭盔確認");
            return ok;
        }

        static bool? Rigging(StringBuilder sb)
        {
            bool ok = true;
            foreach (var id in new[] { "env.crane", "env.crane_pendant", "env.sling_1", "env.sling_2", "env.sling_3", "env.sling_4", "env.tool_bag" })
            {
                var t = Get(id);
                if (t == null) { sb.AppendLine("  找不到 " + id); ok = false; continue; }
                bool grab = t.GetComponentsInChildren<Component>(true).Any(c => c != null && c.GetType().Name == "XRGrabInteractable");
                sb.AppendLine($"  {id}：{(t.gameObject.activeInHierarchy ? "顯示中" : "未啟用")}、XRGrabInteractable {(grab ? "有" : "沒有")}");
                ok &= t.gameObject.activeInHierarchy && !grab;
            }
            return ok;
        }

        static bool? TestResult(StringBuilder sb)
        {
            var lines = TestLines();
            if (lines.Length == 0) { sb.AppendLine("  找不到 " + TestsPath); return false; }
            sb.AppendLine("  " + lines[0]);
            int pass = TestCount(lines[0], "通過"), fail = TestCount(lines[0], "失敗");
            int total = pass + Math.Max(fail, 0);
            sb.AppendLine($"  總數 {total}（R1 結束 {R1EndTestCount}，差 {total - R1EndTestCount:+0;-0;0}）");
            var pan = lines.Where(l => l.Contains(".PanAssy")).ToArray();
            foreach (var l in pan) sb.AppendLine("  " + l);
            var frame = pan.Where(l => l.Contains("PanAssyFrameTests")).ToArray();
            if (frame.Length == 0) sb.AppendLine("  找不到 PanAssyFrameTests");
            return fail == 0 && frame.Length > 0 && frame.All(l => l.StartsWith("[PASS]"));
        }

        // ---------- 工具 ----------

        static Transform Find(string name) => all?.FirstOrDefault(t => t != null && t.name == name);
        /// <summary>先找 ISimObject.Id，再找物件名稱。</summary>
        static Transform Get(string key) => ids != null && ids.TryGetValue(key, out var t) && t != null ? t : Find(key);

        static bool Exists(StringBuilder sb, string key, bool withPos = false, string label = null)
        {
            var t = Get(key);
            sb.AppendLine(t == null ? "  找不到 " + key : $"  {(label != null ? label + " " : "")}{key}：有{(t.gameObject.activeInHierarchy ? "" : "（未啟用）")}{(withPos ? " " + t.position.ToString("F3") : "")}");
            return t != null;
        }

        /// <summary>場景裡的 XrTeleportPoint（Editor asmdef 沒參考 PantographSim.XR，用型別名稱＋反射讀 PointId）。</summary>
        static Dictionary<string, Transform> TeleportPoints()
        {
            var r = new Dictionary<string, Transform>();
            foreach (var mb in Object.FindObjectsOfType<MonoBehaviour>(true).Where(m => m != null && m.GetType().Name == "XrTeleportPoint"))
                if (mb.GetType().GetProperty("PointId")?.GetValue(mb) is string id && !string.IsNullOrEmpty(id) && !r.ContainsKey(id)) r[id] = mb.transform;
            return r;
        }

        static string[] TestLines() => File.Exists(TestsPath) ? File.ReadAllLines(TestsPath) : new string[0];

        static int TestCount(string line, string label)
        {
            var m = Regex.Match(line, label + @"\s*(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }
    }
}
