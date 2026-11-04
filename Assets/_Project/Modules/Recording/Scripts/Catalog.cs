using System;
using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Recording
{
    /// <summary>一個執行步驟（重複區塊依側別展開）的名稱、參考時間與小動作，存進 meta.json，報表只靠紀錄資料夾就能產生。</summary>
    public class StepInfo
    {
        public string runId = "", stepId = "", title = "", side = "", sideLabel = "";
        public int order;
        public bool presetDone;
        public float refTutorial, refExam;
        public string[] subActions = Array.Empty<string>();

        /// <summary>中文步驟名，例「預鬆螺栓（肘側）」。</summary>
        public string DisplayName => string.IsNullOrEmpty(sideLabel) ? title : $"{title}（{sideLabel.Replace("滑板", "")}）";

        public Dictionary<string, object> ToDict() => new Dictionary<string, object>
        {
            { "runId", runId }, { "stepId", stepId }, { "title", title }, { "side", side }, { "sideLabel", sideLabel }, { "order", order },
            { "presetDone", presetDone }, { "refTutorial", refTutorial }, { "refExam", refExam }, { "subActions", subActions }
        };

        public static StepInfo FromDict(Dictionary<string, object> d) => new StepInfo
        {
            runId = d.Str("runId"), stepId = d.Str("stepId"), title = d.Str("title"), side = d.Str("side"), sideLabel = d.Str("sideLabel"),
            order = (int)d.Num("order"), presetDone = d.Bool("presetDone"), refTutorial = (float)d.Num("refTutorial"), refExam = (float)d.Num("refExam"),
            subActions = d.StrArr("subActions")
        };
    }

    /// <summary>程序的標準順序（展開重複區塊，與 ProcedureRunner 相同規則：重複區塊先做完一側再做另一側）。</summary>
    public class StepCatalog
    {
        public readonly List<StepInfo> steps = new List<StepInfo>();
        public string procedureId = "", procedureTitle = "";

        public StepInfo Find(string runId) => steps.FirstOrDefault(s => s.runId == runId);

        public string NameOf(string runId)
        {
            var s = Find(runId);
            return s != null ? s.DisplayName : string.IsNullOrEmpty(runId) ? "（步驟開始前）" : runId;
        }

        public static StepCatalog From(ProcedureDefinition prc)
        {
            var cat = new StepCatalog();
            if (prc == null) return cat;
            cat.procedureId = prc.procedureId ?? ""; cat.procedureTitle = prc.title ?? "";
            var rb = prc.repeatBlock;
            var defs = prc.steps;
            int from = rb != null && !string.IsNullOrEmpty(rb.fromStepId) ? defs.FindIndex(s => s.stepId == rb.fromStepId) : -1;
            int to = rb != null && !string.IsNullOrEmpty(rb.toStepId) ? defs.FindIndex(s => s.stepId == rb.toStepId) : -1;
            for (int i = 0; i < defs.Count; i++)
            {
                if (from >= 0 && to >= from && i == from)
                {
                    for (int s = 0; s < rb.sides.Length; s++)
                        for (int j = from; j <= to; j++)
                            cat.Add(defs[j], rb.sides[s], s < rb.sideLabels.Length ? rb.sideLabels[s] : rb.sides[s], true);
                    i = to;
                    continue;
                }
                string side = defs[i].side == "L" || defs[i].side == "R" ? defs[i].side : "";
                string label = side == "" ? "" : rb != null && rb.sideLabels.Length > 1 ? rb.sideLabels[side == "L" ? 0 : 1] : side;
                cat.Add(defs[i], side, label, false);
            }
            return cat;
        }

        void Add(StepDefinition d, string side, string label, bool repeated)
        {
            if (d == null) return;
            steps.Add(new StepInfo
            {
                runId = repeated ? d.stepId + "_" + side : d.stepId, stepId = d.stepId, title = d.title ?? "", side = side, sideLabel = label,
                order = steps.Count + 1, presetDone = d.presetDone, refTutorial = d.refSecondsTutorial, refExam = d.refSecondsExam,
                subActions = (d.subActions ?? Array.Empty<SubAction>()).Select(a => StateKeys.Expand(a.text, side)).ToArray()
            });
        }

        public List<object> ToList() => steps.Select(s => (object)s.ToDict()).ToList();

        public static StepCatalog FromMeta(Dictionary<string, object> meta)
        {
            var cat = new StepCatalog { procedureId = meta.Str("procedureId"), procedureTitle = meta.Str("procedureTitle") };
            foreach (var o in meta.Arr("steps")) if (o is Dictionary<string, object> d) cat.steps.Add(StepInfo.FromDict(d));
            return cat;
        }
    }

    /// <summary>中文錯誤名（Resources/recording_error_names.json）；沒有列出的用「做錯什麼」第一句。</summary>
    public static class ErrorNames
    {
        static Dictionary<string, string> names;

        static Dictionary<string, string> Table()
        {
            if (names != null) return names;
            names = new Dictionary<string, string>();
            try
            {
                var asset = Resources.Load<TextAsset>("recording_error_names");
                if (asset != null && Json.Parse(asset.text) is Dictionary<string, object> root)
                    foreach (var o in root.Arr("names"))
                        if (o is Dictionary<string, object> d) names[d.Str("id")] = d.Str("name");
            }
            catch (Exception e) { Debug.LogWarning("[Recording] 讀不到錯誤名稱表：" + e.Message); }
            return names;
        }

        /// <summary>預先載入（Resources 只能在主執行緒讀）。</summary>
        public static void Preload() => Table();

        public static string Of(string errorId, string what = "")
        {
            if (!string.IsNullOrEmpty(errorId) && Table().TryGetValue(errorId, out var n) && !string.IsNullOrEmpty(n)) return n;
            if (!string.IsNullOrEmpty(what))
            {
                int cut = what.IndexOfAny(new[] { '。', '，', '（' });
                return cut > 0 ? what.Substring(0, cut) : what;
            }
            return string.IsNullOrEmpty(errorId) ? "（未知錯誤）" : errorId;
        }

        public static string SeverityLabel(string sev) => sev == "Critical" ? "嚴重" : sev == "Major" ? "錯誤" : sev == "Minor" ? "提醒" : sev;
    }

    /// <summary>
    /// 數值來源標籤：黑板鍵 → 名稱、單位、標籤（保養表值／示例值）與標準。報表與紀錄每個數值都帶標籤。
    /// 鍵可用 * 萬用字元（例 ip.carbon_strip_*.bolt_*.torque_Nm）。
    /// </summary>
    public static class ValueTagRules
    {
        public class Rule { public string pattern, label, unit, tag, standard; }

        public static readonly Rule[] Rules =
        {
            new Rule { pattern = "tool.torque_wrench.setting", label = "扭力扳手設定", unit = "N·m", tag = "示例值", standard = "設定 25 N·m" },
            new Rule { pattern = "tool.torque_wrench.current", label = "扭力扳手目前扭力", unit = "N·m", tag = "示例值", standard = "合格 23–27 N·m" },
            new Rule { pattern = "ip.carbon_strip_*.bolt_*.torque_Nm", label = "螺栓實際扭力", unit = "N·m", tag = "示例值", standard = "合格 23–27 N·m" },
            new Rule { pattern = "ip.carbon_strip_*.bolt_*.torque_setting", label = "鎖扭力時的設定值", unit = "N·m", tag = "示例值", standard = "設定 25 N·m" },
            new Rule { pattern = "tool.socket_wrench.size", label = "套筒尺寸", unit = "mm", tag = "示例值", standard = "13 mm（M8）" },
            new Rule { pattern = "tool.socket_wrench.dir", label = "套筒方向", unit = "", tag = "示例值", standard = "鬆／鎖" },
            new Rule { pattern = "ip.carbon_strip_*.bolt_*.angle_deg", label = "鬆開累計角度", unit = "°", tag = "示例值", standard = "預鬆半圈 180°±30°" },
            new Rule { pattern = "ip.carbon_strip_*.bolt_*.lock_deg", label = "鎖緊累計角度", unit = "°", tag = "示例值", standard = "貼合 360°" },
            new Rule { pattern = "ip.carbon_strip_*.measure_*.reading", label = "厚度讀數", unit = "mm", tag = "保養表值", standard = "≤ 3.0 mm 更換" },
            new Rule { pattern = "ip.carbon_strip_*.measure_*.new_reading", label = "新品厚度讀數", unit = "mm", tag = "示例值", standard = "12.0 ± 0.2 mm" },
            new Rule { pattern = "part.pan_head.lower_distance_cm", label = "降弓檢測點距離", unit = "cm", tag = "示例值", standard = "≤ 1 cm" },
            new Rule { pattern = "ip.carbon_strip_*.bracket.clean_ratio", label = "托架清除率", unit = "", tag = "示例值", standard = "≥ 95%" },
            new Rule { pattern = "tool.caliper.reading", label = "卡尺讀數", unit = "mm", tag = "保養表值", standard = "≤ 3.0 mm 更換" },
        };

        public static Rule Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var r in Rules) if (Match(r.pattern, key)) return r;
            return null;
        }

        public static bool Match(string pattern, string key)
        {
            var parts = pattern.Split('*');
            if (parts.Length == 1) return pattern == key;
            if (!key.StartsWith(parts[0]) || !key.EndsWith(parts[parts.Length - 1])) return false;
            int pos = parts[0].Length;
            for (int i = 1; i < parts.Length - 1; i++)
            {
                int k = key.IndexOf(parts[i], pos, StringComparison.Ordinal);
                if (k < 0) return false;
                pos = k + parts[i].Length;
            }
            return pos <= key.Length - parts[parts.Length - 1].Length;
        }
    }

    /// <summary>模式與輸入方式的中文標籤。</summary>
    public static class Labels
    {
        public static string Mode(string raw) => raw == "practice" ? "練習" : raw == "exam" ? "考核" : "教學";
        public static string Input(bool xr) => xr ? "VR" : "桌機";

        /// <summary>
        /// 參考時間：只有教學模式用教學秒數；練習模式沒有小動作提示，和考核一樣用考核秒數（較嚴格）。
        /// 考核模式目前還沒有，邏輯先預留（modeLabel＝"考核" 也用考核秒數）。
        /// </summary>
        public static float RefSeconds(StepInfo s, string modeLabel) => s == null ? 0f : modeLabel == "教學" ? s.refTutorial : s.refExam;

        /// <summary>報表說明用：這個模式用哪一組參考秒數。</summary>
        public static string RefSecondsKind(string modeLabel) => modeLabel == "教學" ? "教學秒數" : "考核秒數";
    }
}
