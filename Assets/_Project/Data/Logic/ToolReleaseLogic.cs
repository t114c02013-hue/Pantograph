using System.Collections.Generic;
using System.Linq;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4 車頂放開工具（ToolRulesDef）的純邏輯：放開時開始計時，regrabGraceSec 內再抓回就取消；
    /// 超過時回傳要記錯的工具（教學模式改成提示），同一步同一件工具只記一次，撿回來也不消掉。
    /// </summary>
    public class ToolReleaseLogic
    {
        readonly Dictionary<string, float> pending = new Dictionary<string, float>();
        readonly HashSet<string> recorded = new HashSet<string>();
        string step;

        /// <summary>放開：回傳 false＝不掉（gripReleaseDrops=false，工具留在手上）。在車頂才開始計時。</summary>
        public bool Release(ToolRulesDef rules, string toolId, bool onRoof, float now)
        {
            if (rules != null && !rules.gripReleaseDrops) return false;
            if (onRoof) pending[toolId] = now;
            return true;
        }

        /// <summary>把工具抓回來（寬限內＝不記；已經記過的不消掉）。</summary>
        public void Regrab(string toolId) => pending.Remove(toolId);

        /// <summary>寬限到了的工具：errors＝要記錯，hints＝只提示（教學模式）。</summary>
        public void Due(ToolRulesDef rules, float now, string stepId, string mode, List<string> errors, List<string> hints)
        {
            if (stepId != step) { step = stepId; recorded.Clear(); }
            float grace = rules != null ? rules.regrabGraceSec : 1.5f;
            foreach (var kv in pending.Where(kv => now - kv.Value >= grace).ToList())
            {
                pending.Remove(kv.Key);
                bool hintOnly = (rules?.hintOnlyModes ?? new[] { "tutorial" }).Contains(string.IsNullOrEmpty(mode) ? "tutorial" : mode);
                if (hintOnly) hints.Add(kv.Key);
                else if (recorded.Add(kv.Key)) errors.Add(kv.Key);
            }
        }
    }
}
