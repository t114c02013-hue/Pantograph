using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Procedures
{
    /// <summary>
    /// 步驟表匯出（唯讀，不改任何資料）：每列一個小動作（兩側已展開），欄位 stepId、subActionId、textXR、completionRule、errorIds、refTimeTeach、refTimeExam、standPointId。
    /// Editor 選單 Tools/Export Step Table 寫到 Logs/StepTable.csv（UTF-8 with BOM，Excel 開中文不亂碼）。
    /// </summary>
    public static class StepTableExport
    {
        public const string Header = "stepId,subActionId,textXR,completionRule,errorIds,refTimeTeach,refTimeExam,standPointId";

        /// <summary>小動作總數（和 CSV 資料列數相同）。</summary>
        public static int SubActionCount(ProcedureDefinition procedure) =>
            new ProcedureRunner(procedure, new EventBus(), new SimState(new EventBus())).Steps.Sum(st => ProcedureRunner.SubActions(st).Length);

        public static string Build(ProcedureDefinition procedure)
        {
            var runner = new ProcedureRunner(procedure, new EventBus(), new SimState(new EventBus()));
            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");
            foreach (var st in runner.Steps)
            {
                string errors = string.Join(";", (st.def.errors ?? new ErrorRule[0]).Select(e => e.errorId + ":" + e.severity).Distinct());
                var subs = ProcedureRunner.SubActions(st);
                for (int i = 0; i < subs.Length; i++)
                {
                    var a = subs[i];
                    sb.Append(string.Join(",", new[]
                    {
                        st.RunId, st.RunId + "_" + Letter(i), a.text, Describe(a.condition), errors,
                        st.def.refSecondsTutorial.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        st.def.refSecondsExam.ToString(System.Globalization.CultureInfo.InvariantCulture), a.standPointId ?? "",
                    }.Select(Csv))).Append("\r\n");
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
