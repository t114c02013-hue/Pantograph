using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Procedures
{
    /// <summary>完成條件裡一個不成立的條件：id＝規則型別:鍵，keys＝相關黑板鍵，values＝當時的值（鍵=值）。</summary>
    public class UnmetCondition
    {
        public string id;
        public List<string> keys = new List<string>();
        public List<string> values = new List<string>();
    }

    /// <summary>
    /// 完成條件的 Explain：列出目前「不成立」的條件（只讀黑板，不改任何狀態）。判定邏輯沿用 RuleEvaluators（同一套判定、同一個容差），
    /// 規則資料（Data/Rules.cs）只放資料，所以 Explain 放在判定這一層。給卡關偵測與「還差什麼」用。
    /// </summary>
    public static class RuleExplainer
    {
        public static List<UnmetCondition> Explain(CompletionRule rule, EvalContext c)
        {
            var list = new List<UnmetCondition>();
            Collect(rule, c, list);
            return list;
        }

        static UnmetCondition Item(string type, EvalContext c, params string[] keys)
        {
            var u = new UnmetCondition { id = type + ":" + string.Join(",", keys) };
            foreach (var k in keys) { u.keys.Add(k); u.values.Add(k + "=" + (c.state.Has(k) ? c.state.Get(k) : "∅")); }
            return u;
        }

        static void Collect(CompletionRule rule, EvalContext c, List<UnmetCondition> list)
        {
            switch (rule)
            {
                case null: return;
                case AllOf all: foreach (var r in all.rules) Collect(r, c, list); return;
                case ChecklistAll ca:
                    foreach (var k in ca.keys.Select(x => c.K(x))) if (!c.state.GetBool(k)) list.Add(Item("ChecklistAll", c, k));
                    return;
                case ValueInRange vr:
                    foreach (var k in ValueInRangeEvaluator.Expand(vr.key, vr.count, c))
                        if (!c.state.Has(k) || !ValueInRangeEvaluator.InRange(c.state.GetFloat(k), vr.min, vr.max)) list.Add(Item("ValueInRange", c, k));
                    return;
                case ReadingMatches rm:
                    for (int i = 0; i < Math.Min(rm.recordKeys.Length, rm.readingKeys.Length); i++)
                    {
                        var single = new ReadingMatches { recordKeys = new[] { rm.recordKeys[i] }, readingKeys = new[] { rm.readingKeys[i] }, tolerance = rm.tolerance };
                        if (!RuleEvaluators.Evaluate(single, c)) list.Add(Item("ReadingMatches", c, c.K(rm.recordKeys[i]), c.K(rm.readingKeys[i])));
                    }
                    return;
                case StateEquals se: if (!RuleEvaluators.Evaluate(se, c)) list.Add(Item("StateEquals", c, c.K(se.key))); return;
                case OrderedSequence os: if (!RuleEvaluators.Evaluate(os, c)) list.Add(Item("OrderedSequence", c, c.K(os.key))); return;
                case Decision d: if (!RuleEvaluators.Evaluate(d, c)) list.Add(Item("Decision", c, c.K(d.choiceKey))); return;
                default:
                    if (!RuleEvaluators.Evaluate(rule, c)) list.Add(new UnmetCondition { id = rule.GetType().Name });
                    return;
            }
        }

        /// <summary>條件字串裡的黑板鍵（「a=1 && b<=c」→ a、b、c）。</summary>
        public static IEnumerable<string> KeysOf(string condition)
        {
            if (string.IsNullOrEmpty(condition)) yield break;
            foreach (var part in condition.Split(new[] { "&&" }, StringSplitOptions.RemoveEmptyEntries))
                foreach (var tok in part.Trim().Split(new[] { "<=", ">=", "=" }, StringSplitOptions.None))
                {
                    var t = tok.Trim();
                    if (t.Length > 0 && t.Contains(".") && !float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) yield return t;
                }
        }

        static int CommonPrefix(string a, string b)
        {
            var x = a.Split('.'); var y = b.Split('.');
            int n = 0;
            while (n < x.Length && n < y.Length && x[n] == y[n]) n++;
            return n;
        }

        /// <summary>
        /// 給學員看的「還差什麼」（都從步驟資料讀，不顯示條件公式）。依序找：
        /// 1. 小動作的指令（subActions.text）；2. 小步驟清單的文字（hints.checklist「文字|條件」）；3. 同一個鍵的錯誤規則的「怎麼改」（errors.fix）；
        /// 4. 安全閘門的說明；5. 都沒有就用步驟說明（instruction）。source 回傳用了哪一種（測試檢查用）。
        /// </summary>
        public static (string text, string source) Describe(UnmetCondition u, RuntimeStep s, Func<string, string> stepTitle = null)
        {
            if (u.id.StartsWith("Gate:")) return (u.id.Substring(5), "gate");
            // 前面的步驟還沒完成（step.<RunId>.done）：用那一步的標題
            var done = u.keys.FirstOrDefault(k => k.StartsWith("step.") && k.EndsWith(".done"));
            string title = done != null ? stepTitle?.Invoke(done.Substring(5, done.Length - 10)) : null;
            if (!string.IsNullOrEmpty(title)) return ($"先完成「{title}」", "stepTitle");
            var a = SubActionFor(u, ProcedureRunner.SubActions(s));
            if (a != null) return (a.text, "subAction");
            foreach (var item in s.def.hints?.checklist ?? Array.Empty<string>())
            {
                var (text, cond) = ChecklistEval.Parse(StateKeys.Expand(item, s.side));
                if (KeysOf(cond).Any(k => u.keys.Contains(k)) || u.keys.Any(k => cond == k)) return (text, "checklist");
            }
            foreach (var e in s.def.errors ?? Array.Empty<ErrorRule>())
                if (!string.IsNullOrEmpty(e.fix) && u.keys.Contains(StateKeys.Expand(e.key, s.side))) return (e.fix, "errorFix");
            return (s.def.instruction, "instruction");
        }

        /// <summary>
        /// 不成立的條件對應到哪一個小動作（學員看得懂的指令文字）：鍵完全相同優先，否則取共同前綴最長（至少 3 段，例 workorder.reading.L）。
        /// 找不到回傳 null。
        /// </summary>
        public static SubAction SubActionFor(UnmetCondition u, SubAction[] subs)
        {
            SubAction best = null; int bestScore = 2;
            foreach (var key in u.keys)
                foreach (var a in subs)
                    foreach (var k in KeysOf(a.condition))
                    {
                        int score = k == key ? 100 : CommonPrefix(k, key);
                        if (score > bestScore) { bestScore = score; best = a; }
                    }
            return best;
        }
    }
}
