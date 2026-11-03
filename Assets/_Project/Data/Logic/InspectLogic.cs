using System;
using System.Linq;
using PantographSim.Core;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4：查看。ToolRulesDef.dwellInspectIds 裡的東西，指著（ui.hover_id，桌面滑鼠、VR 射線都寫這個鍵）停 inspectDwellSeconds 就算看過（&lt;id&gt;.inspected）；
    /// 吊帶（env.sling_n）看過之後要判「可用／不可用」（RM_05 f），答案在 RiggingDef.defectiveSlings。純函式，App 與測試共用。
    /// </summary>
    public static class InspectLogic
    {
        public const string HoverKey = "ui.hover_id", SlingPrefix = "env.sling_", SlingVerdictUi = "ui.sling_inspect";

        /// <summary>每幀：同一個東西指著累計秒數，到了就記看過（吊帶另外打開判定）。回傳新的 (id, 秒數)。</summary>
        public static (string id, float t) Dwell(ISimState s, string id, float t, float dt, ToolRulesDef rules)
        {
            string hover = s.Get(HoverKey);
            if (hover != id) return (hover, 0f);
            if (string.IsNullOrEmpty(hover) || rules == null || !rules.dwellInspectIds.Contains(hover)) return (hover, t);
            float before = t; t += dt;
            if (before < rules.inspectDwellSeconds && t >= rules.inspectDwellSeconds)
                s.Batch(() =>
                {
                    s.SetBool(hover + ".inspected", true);
                    if (hover.StartsWith(SlingPrefix)) s.Set(SlingVerdictUi, hover);   // 吊帶：打開「可用／不可用」
                });
            return (hover, t);
        }

        /// <summary>吊帶判定：ok＝可用。對不對讀 RiggingDef.defectiveSlings（有割傷＝不合格）。</summary>
        public static void SlingVerdict(ISimState s, string slingId, bool ok, RiggingDef def)
        {
            if (!slingId.StartsWith(SlingPrefix) || !int.TryParse(slingId.Substring(SlingPrefix.Length), out int n)) return;
            bool defective = def != null && Array.IndexOf(def.defectiveSlings, n) >= 0;
            s.Batch(() =>
            {
                s.Set(slingId + ".verdict", ok ? "ok" : "ng");
                s.SetBool(slingId + ".verdict_correct", ok != defective);
                s.Set(SlingVerdictUi, "");
            });
        }
    }
}
