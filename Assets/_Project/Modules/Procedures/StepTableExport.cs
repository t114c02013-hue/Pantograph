using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Procedures
{
    /// <summary>
    /// 步驟表匯出（唯讀，不改任何資料）：每列一個小動作（兩側已展開）。R1.5b b4：兩門課匯出在同一份表，用 course 欄區分；
    /// 欄位 course、stepId、subActionId、textXR（VR 指令）、textDesktop（桌面指令，空白＝和 VR 相同）、completionRule、errorIds（ErrorRule 的 ID 與嚴重度）、
    /// refTimeTeach、refTimeExam、standPointId（該課程的站點）、zoneId（站點所屬作業區）。
    /// Editor 選單 Tools/Export Step Table 寫到 Logs/StepTable.csv（UTF-8 with BOM，Excel 開中文不亂碼）。
    /// </summary>
    public static class StepTableExport
    {
        public const string Header = "course,stepId,subActionId,textXR,textDesktop,completionRule,errorIds,refTimeTeach,refTimeExam,standPointId,zoneId,optional";   // terry 2026-11-15（88A）：optional＝Y 是選做

        /// <summary>小動作總數（和 CSV 資料列數相同）。</summary>
        public static int SubActionCount(ProcedureDefinition procedure) =>
            new ProcedureRunner(procedure, new EventBus(), new SimState(new EventBus())).Steps.Sum(st => ProcedureRunner.SubActions(st).Length);

        public static string Build(ProcedureDefinition procedure, string course = "", Func<string, string> zoneOf = null) =>
            Build(new[] { (course, procedure) }, zoneOf);

        /// <summary>多門課匯出在同一份表（一個表頭）。zoneOf：站點 ID → 作業區 ID（null＝空白）。</summary>
        public static string Build(IEnumerable<(string course, ProcedureDefinition procedure)> courses, Func<string, string> zoneOf = null)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var (course, procedure) in courses)
            {
                var runner = new ProcedureRunner(procedure, new EventBus(), new SimState(new EventBus()));
                foreach (var st in runner.Steps)
                {
                    string errors = string.Join(";", (st.def.errors ?? new ErrorRule[0]).Select(e => e.errorId + ":" + e.severity).Distinct());
                    var subs = ProcedureRunner.SubActions(st);
                    var raw = st.def.subActions ?? new SubAction[0];
                    for (int i = 0; i < subs.Length; i++)
                    {
                        var a = subs[i];
                        string xr = StateKeys.Expand(raw[i].text, st.side), desk = StateKeys.Expand(raw[i].textDesktop ?? "", st.side);
                        sb.Append(string.Join(",", new[]
                        {
                            course, st.RunId, st.RunId + "_" + (string.IsNullOrEmpty(a.subId) ? Letter(i) : a.subId), xr, desk, Describe(a.condition), errors,
                            st.def.refSecondsTutorial.ToString(ci), st.def.refSecondsExam.ToString(ci), a.standPointId ?? "", zoneOf?.Invoke(a.standPointId ?? "") ?? "", a.optional ? "Y" : "",
                        }.Select(Csv))).Append("\r\n");
                    }
                }
            }
            return sb.ToString();
        }

        static string Letter(int i) => i < 26 ? ((char)('a' + i)).ToString() : "a" + (i - 25);

        /// <summary>完成條件改寫成人看得懂的寫法（判定照舊，只換符號）。空＝原地。</summary>
        public static string Describe(string condition) =>
            string.IsNullOrWhiteSpace(condition) ? "" : condition.Replace("&&", " 且 ").Replace("<=", " ≤ ").Replace(">=", " ≥ ").Replace("!=", " ≠ ").Replace("=", " ＝ ").Replace("  ", " ").Trim();

        static string Csv(string v)
        {
            v = v ?? "";
            return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
    }
}
