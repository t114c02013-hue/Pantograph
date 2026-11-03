using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1.5b b4（第 3 部分）：ErrorRule 的唯一定義 Data/Source/ErrorRules.json（第 2 部分 2.6）。
    /// CSV 的「ID:嚴重度」只是引用：ID 沒有定義、還在 PendingRuling、嚴重度不同、小動作的 errorIds 不在該步 Steps.errorIds 裡，都算匯入失敗（資深軟體 B 審查 S1）。
    /// 產生的步驟沿用 R1 的錯誤規則（觸發條件），嚴重度、severityBeforeK3、inputMode 以這份為準。
    /// </summary>
    public static partial class StepsCsvImporter
    {
        public static string ErrorRulesPath => SourceFolder + "/ErrorRules.json";

        [Serializable]
        public class ErrorRuleDef
        {
            public string errorId, severity, severityBeforeK3, courses, inputMode, status, owner, source, trigger, note;
            /// <summary>global＝不分步驟、全程監看（放進 ProcedureDefinition.globalErrors，觸發 WhenCondition）；空白＝掛在 Steps.csv 引用它的步驟。</summary>
            public string scope;
            /// <summary>觸發條件（ProcedureJson 的 ErrorJson 格式）。有寫就以這份為準（取代 R1 的），並加到 Steps.csv 引用它的步驟。</summary>
            public ErrorJson rule;
            public bool HasRule => rule != null && !string.IsNullOrEmpty(rule.trigger);
        }

        [Serializable] class ErrorRulesFile { public ErrorRuleDef[] rules = Array.Empty<ErrorRuleDef>(); }

        public const string Defined = "Defined", PendingRuling = "PendingRuling";
        /// <summary>R1.5b b4（驗收組長裁定 2026-11-03）：停用的錯誤——併入別條（Deprecated）或本版不適用（NotApplicable）。ID 保留、認得，不判。</summary>
        public const string Deprecated = "Deprecated", NotApplicable = "NotApplicable";
        public static bool Disabled(string status) => status == Deprecated || status == NotApplicable;

        public static Dictionary<string, ErrorRuleDef> LoadErrorRules() =>
            JsonUtility.FromJson<ErrorRulesFile>(File.ReadAllText(ErrorRulesPath, Encoding.UTF8)).rules.ToDictionary(r => r.errorId);

        /// <summary>CSV 的錯誤引用和 ErrorRules.json 比對（行號＋內容寫進 errors）。</summary>
        public static void CheckErrorRefs(List<Row> subRows, List<Row> stepRows, Dictionary<string, ErrorRuleDef> defs, List<string> errors)
        {
            var stepRefs = new Dictionary<string, HashSet<string>>();
            foreach (var r in stepRows)
            {
                var refs = ParseErrorRefs(r["errorIds"], s => errors.Add($"{r}：{s}"));
                stepRefs[r["stepId"]] = new HashSet<string>(refs.Select(x => x.id));
                Check(r, refs);
            }
            foreach (var r in subRows)
            {
                var refs = ParseErrorRefs(r["errorIds"], s => errors.Add($"{r}：{s}"));
                Check(r, refs);
                var stepIds = r["stepId"].Contains("{side}") ? new[] { r["stepId"].Replace("{side}", "L"), r["stepId"].Replace("{side}", "R") } : new[] { r["stepId"] };
                foreach (var sid in stepIds)
                    foreach (var x in refs.Where(x => !stepRefs.TryGetValue(sid, out var s) || !s.Contains(x.id)))
                        errors.Add($"{r}：錯誤 {x.id} 不在 Steps.csv 的 {sid} errorIds 裡");
            }

            void Check(Row r, List<(string id, string severity)> refs)
            {
                foreach (var (id, sev) in refs)
                {
                    if (!defs.TryGetValue(id, out var d)) errors.Add($"{r}：錯誤 {id} 在 ErrorRules.json 沒有定義");
                    else if (Disabled(d.status)) continue;   // 停用的 ID：認得、不判
                    else if (d.status != Defined) errors.Add($"{r}：錯誤 {id} 還沒裁定嚴重度（{d.status}）");
                    else if (d.severity != sev) errors.Add($"{r}：錯誤 {id} 寫 {sev}，ErrorRules.json 是 {d.severity}");
                }
            }
        }

        /// <summary>產生的步驟：沿用的錯誤規則以 ErrorRules.json 的嚴重度為準，加上 K3 的 severityBeforeK3、K8 的 inputMode。</summary>
        public static void ApplyErrorDefs(StepDefinition st, Dictionary<string, ErrorRuleDef> defs, IEnumerable<string> stepErrorIds, List<string> errors)
        {
            var list = new List<ErrorRule>();
            foreach (var e in st.errors ?? Array.Empty<ErrorRule>())
            {
                if (!defs.TryGetValue(e.errorId, out var d)) { errors.Add($"{st.stepId}：沿用的錯誤 {e.errorId} 在 ErrorRules.json 沒有定義"); continue; }
                list.Add(d.HasRule ? Build(d, e) : e);
            }
            foreach (var id in stepErrorIds.Where(id => list.All(x => x.errorId != id)))
                if (defs.TryGetValue(id, out var d) && d.HasRule && d.scope != "global") list.Add(Build(d, null));
            foreach (var e in list)
            {
                var d = defs[e.errorId];
                e.severity = (Severity)Enum.Parse(typeof(Severity), d.severity);
                e.severityBeforeK3 = d.severityBeforeK3 ?? "";
                e.inputMode = d.inputMode ?? "";
            }
            st.errors = list.ToArray();
        }

        /// <summary>這門課全程監看的錯誤（scope=global，依 courses 欄篩選）。</summary>
        public static ErrorRule[] GlobalErrors(string course, Dictionary<string, ErrorRuleDef> defs) =>
            defs.Values.Where(d => d.scope == "global" && d.HasRule && InCourse(string.IsNullOrEmpty(d.courses) ? "both" : d.courses, course)).OrderBy(d => d.errorId)
                .Select(d =>
                {
                    var r = Build(d, null);
                    r.severityBeforeK3 = d.severityBeforeK3 ?? ""; r.inputMode = d.inputMode ?? "";
                    return r;
                }).ToArray();

        /// <summary>ErrorRules.json 的觸發條件建成 ErrorRule；說明文字沒寫就沿用 R1 的。</summary>
        static ErrorRule Build(ErrorRuleDef d, ErrorRule r1)
        {
            var j = JsonUtility.FromJson<ErrorJson>(JsonUtility.ToJson(d.rule));
            j.errorId = d.errorId; j.severity = d.severity;
            if (string.IsNullOrEmpty(j.what)) j.what = r1?.what ?? d.trigger;
            if (string.IsNullOrEmpty(j.why)) j.why = r1?.why ?? "";
            if (string.IsNullOrEmpty(j.fix)) j.fix = r1?.fix ?? "";
            return ProcedureJsonConverter.BuildError(j);
        }
    }
}
