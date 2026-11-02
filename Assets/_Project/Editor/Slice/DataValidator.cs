using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Data;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// 資料與架構檢查：步驟 ID 不重複、目標都存在於場景、每個工具都有 3–5 頁教學、每個錯誤都有三段說明、
    /// 五條安全規則齊全、UI 只引用 Core、功能層之間互不引用。結果寫 Logs/DataValidation.txt。
    /// </summary>
    public static class DataValidator
    {
        const string ReportPath = "Logs/DataValidation.txt";
        static readonly string[] FeatureLayers =
        {
            "PantographSim.Parts.Common", "PantographSim.Parts.CarbonStrip", "PantographSim.Parts.Mechanism", "PantographSim.Tools",
            "PantographSim.Worksite", "PantographSim.Procedures", "PantographSim.Safety", "PantographSim.Interaction"
        };

        [MenuItem("PantographSim/Slice/Validate Data")]
        public static void ValidateMenu()
        {
            bool ok = Validate(out var report);
            if (ok) Debug.Log("[DataValidator] 通過\n" + report); else Debug.LogError("[DataValidator] 有問題\n" + report);
        }

        /// <param name="sceneIds">場景中存在的 ID；null 時以目前開啟的場景掃描。</param>
        public static bool Validate(out string report, ICollection<string> sceneIds = null)
        {
            var sb = new StringBuilder();
            int problems = 0;
            void Fail(string msg) { problems++; sb.AppendLine("✘ " + msg); }
            void Pass(string msg) => sb.AppendLine("✔ " + msg);

            var prc = StepJsonImporter.LoadProcedure();
            if (prc == null) { report = "找不到 PRC_CarbonStripSlice，請先 Import Steps"; File.WriteAllText(ReportPath, report); return false; }

            // 1. 步驟 ID 不重複、25 步
            var dup = prc.steps.GroupBy(s => s.stepId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dup.Count > 0) Fail("步驟 ID 重複：" + string.Join(", ", dup)); else Pass($"步驟 ID 不重複（{prc.steps.Count} 步）");
            if (prc.steps.Count != 25) Fail($"步驟數應為 25，實際 {prc.steps.Count}");

            // 2. 每個錯誤都有三段說明；錯誤 ID 在不同步驟間不重複（同一步內多條規則共用同一 ID 允許）
            int errCount = 0;
            foreach (var s in prc.steps)
                foreach (var e in s.errors)
                {
                    errCount++;
                    if (string.IsNullOrWhiteSpace(e.what) || string.IsNullOrWhiteSpace(e.why) || string.IsNullOrWhiteSpace(e.fix))
                        Fail($"{s.stepId} 的 {e.errorId} 缺少做錯什麼／為什麼／怎麼改");
                }
            var crossDup = prc.steps.SelectMany(s => s.errors.Select(e => (s.stepId, e.errorId))).Distinct().GroupBy(x => x.errorId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (crossDup.Count > 0) Fail("錯誤 ID 在多個步驟重複：" + string.Join(", ", crossDup));
            Pass($"錯誤規則 {errCount} 條皆檢查三段說明");

            // 3. 安全規則
            var rules = StepJsonImporter.LoadAll<SafetyRuleDefinition>(StepJsonImporter.SafetyFolder);
            foreach (SafetyRuleKind k in System.Enum.GetValues(typeof(SafetyRuleKind)))
            {
                var r = rules.FirstOrDefault(x => x.kind == k);
                if (r == null) Fail("缺少安全規則 " + k);
                else if (string.IsNullOrWhiteSpace(r.what) || string.IsNullOrWhiteSpace(r.why) || string.IsNullOrWhiteSpace(r.fix)) Fail(r.ruleId + " 缺三段說明");
            }
            Pass($"安全規則 {rules.Count} 條");

            // 4. 工具教學
            var tools = StepJsonImporter.LoadTools();
            foreach (var t in tools)
                if (t.tutorial?.pages == null || t.tutorial.pages.Length < 3 || t.tutorial.pages.Length > 5) Fail($"{t.toolId} 教學頁數 {t.tutorial?.pages?.Length ?? 0}（需 3–5）");
            if (tools.Count != 9) Fail($"工具應有 9 個，實際 {tools.Count}");
            Pass($"工具 {tools.Count} 個皆有教學");
            foreach (var s in prc.steps.Where(s => !string.IsNullOrEmpty(s.requiredToolId)))
                if (tools.All(t => t.toolId != s.requiredToolId)) Fail($"{s.stepId} 需要的工具 {s.requiredToolId} 不存在");

            // 5. 目標存在於場景
            var ids = sceneIds ?? Object.FindObjectsOfType<MonoBehaviour>(true).OfType<ISimObject>().Select(x => x.Id).Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (ids.Count == 0) sb.AppendLine("（略過目標檢查：目前場景沒有互動物件，請在 SC_CarbonStripSlice 開啟時執行）");
            else
            {
                int missing = 0;
                foreach (var s in prc.steps)
                    foreach (var side in string.IsNullOrEmpty(s.side) ? new[] { "L", "R" } : new[] { s.side })
                        foreach (var t in s.targetIds.Select(x => StateKeys.Expand(x, side)).Distinct())
                        {
                            bool exists = t.EndsWith("*") ? ids.Any(id => id.StartsWith(t.TrimEnd('*'))) : ids.Contains(t);
                            if (!exists && t.StartsWith("ip.cab.") && ids.Contains("ip.cab.panel")) exists = true;   // 控制盤上的按鈕在 2D 面板
                            if (!exists && !IsPresetOnly(s)) { missing++; Fail($"{s.stepId} 的目標 {t} 不在場景中"); }
                        }
                if (missing == 0) Pass($"所有步驟目標都存在於場景（{ids.Count} 個互動物件）");
            }

            // 5b. R1 站點：每個 standPointId 都在 StationSet；每一步至少一個小動作填了站點（全部原地的步驟只列出，不算問題）
            var stationIds = new HashSet<string>(R1Data.Stations.stations.Where(x => x != null).Select(x => x.id));
            int badStand = 0;
            foreach (var s in prc.steps.Where(s => !IsPresetOnly(s)))
            {
                var stands = (string.IsNullOrEmpty(s.side) ? new[] { "L", "R" } : new[] { s.side })
                    .SelectMany(side => s.subActions.Select(a => SubAction.ExpandStand(a.standPointId, side))).Where(x => x.Length > 0).Distinct().ToList();
                foreach (var id in stands.Where(id => !stationIds.Contains(id))) { badStand++; Fail($"{s.stepId} 的站點 {id} 不在 StationSet"); }
                if (stands.Count == 0) sb.AppendLine($"・{s.stepId}：小動作都在原地（B 鍵不傳送）");
            }
            if (badStand == 0) Pass($"小動作的站點都在 StationSet（{stationIds.Count} 站）");

            // 6. 架構：UI 只引用 Core；功能層互不引用
            foreach (var path in Directory.GetFiles("Assets/_Project", "*.asmdef", SearchOption.AllDirectories))
            {
                var def = JsonUtility.FromJson<AsmdefRefs>(File.ReadAllText(path));
                if (def.name == "PantographSim.UI" && def.references.Any(r => r != "PantographSim.Core" && r != "UnityEngine.UI")) Fail("UI 引用了 Core（與 uGUI）以外的組件：" + string.Join(", ", def.references));
                if (FeatureLayers.Contains(def.name))
                {
                    var bad = def.references.Where(r => FeatureLayers.Contains(r) && !(def.name == "PantographSim.Parts.CarbonStrip" && r == "PantographSim.Parts.Common")).ToList();
                    if (bad.Count > 0) Fail($"{def.name} 引用了其他功能層：{string.Join(", ", bad)}");
                }
                if (def.autoReferenced && def.name.StartsWith("PantographSim")) Fail(def.name + " 的 Auto Referenced 沒有關閉");
            }
            Pass("組件引用檢查完成（UI 只引用 Core；功能層互不引用，CarbonStrip→Parts.Common 為同一零件層）");

            sb.Insert(0, problems == 0 ? "結果：通過\n" : $"結果：{problems} 個問題\n");
            report = sb.ToString();
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, report);
            return problems == 0;
        }

        static bool IsPresetOnly(StepDefinition s) => s.presetDone;

        [System.Serializable]
        class AsmdefRefs { public string name = ""; public string[] references = new string[0]; public bool autoReferenced = true; }
    }
}
