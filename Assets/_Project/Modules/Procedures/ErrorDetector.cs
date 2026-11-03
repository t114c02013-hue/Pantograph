using System;
using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Procedures
{
    /// <summary>
    /// 依目前步驟的 ErrorRule 偵測錯誤並發 ErrorDetected（每步每個錯誤 ID 只發一次，重做後重置）。
    /// 也是 IActionGuard：對本步目標拿錯工具、或碰本步以外的弓體零件時擋下（Minor，不改變零件狀態）。
    /// </summary>
    public class ErrorDetector : IActionGuard, IDisposable
    {
        public const string WrongToolId = "ERR_Generic_WrongTool";
        public const string WrongTargetId = "ERR_Generic_WrongTarget";

        readonly ProcedureRunner runner;
        readonly IEventBus bus;
        readonly ISimState state;
        readonly HashSet<string> fired = new HashSet<string>();

        public ErrorDetector(ProcedureRunner runner, IEventBus bus, ISimState state)
        {
            this.runner = runner; this.bus = bus; this.state = state;
            bus.Subscribe<StateChanged>(OnStateChanged);
            bus.Subscribe<StepStarted>(OnStepStarted);
        }

        public void Dispose()
        {
            bus.Unsubscribe<StateChanged>(OnStateChanged);
            bus.Unsubscribe<StepStarted>(OnStepStarted);
        }

        void OnStepStarted(StepStarted e) => fired.Clear();

        static bool Matches(string pattern, string id) =>
            pattern.EndsWith("*") ? id.StartsWith(pattern.Substring(0, pattern.Length - 1)) : pattern == id;

        // ---------- 通用：拿錯工具／點錯目標 ----------
        public GuardResult Check(ActionRequest req)
        {
            var s = runner.Current;
            if (s == null || runner.Finished || req.verb == "inspect" || req.verb == "select") return GuardResult.Allow;
            if (req.verb == "click" && string.IsNullOrEmpty(req.toolId)) return GuardResult.Allow;   // 徒手點一下只是查看
            var targets = ProcedureRunner.ExpandTargets(s);
            bool inStep = targets.Any(t => Matches(t, req.targetId));

            if (inStep && !string.IsNullOrEmpty(s.def.requiredToolId) && req.toolId != s.def.requiredToolId && !string.IsNullOrEmpty(req.toolId))
            {
                Fire(new ErrorRule
                {
                    errorId = WrongToolId, severity = Severity.Minor,
                    what = $"這一步要用「{ToolName(s.def.requiredToolId)}」，你拿的是「{ToolName(req.toolId)}」。",
                    why = "用錯工具無法完成作業，也可能損壞零件。",
                    fix = $"按工具列或數字鍵換成「{ToolName(s.def.requiredToolId)}」再操作。"
                }, s, req.targetId);
                return Blocked(req, WrongToolId);
            }
            if (!inStep && SimIds.IsPanBody(req.targetId) && targets.Length > 0)
            {
                Fire(new ErrorRule
                {
                    errorId = WrongTargetId, severity = Severity.Minor,
                    what = "這個零件不是這一步要處理的目標。",
                    why = "照步驟順序作業才不會漏做或做錯側。",
                    fix = "看左上的步驟說明，點畫面上發亮的目標。"
                }, s, req.targetId);
                return Blocked(req, WrongTargetId);
            }
            return GuardResult.Allow;
        }

        GuardResult Blocked(ActionRequest req, string errorId)
        {
            bus.Publish(new ActionBlocked { targetId = req.targetId, pointId = req.pointId, toolId = req.toolId, text = errorId, time = SimClock.Now });
            return GuardResult.Block(errorId);
        }

        static string ToolName(string toolId)
        {
            switch (toolId)
            {
                case "": return "徒手";
                case "tool.voltage_detector": return "驗電器";
                case "tool.grounding_rod": return "接地棒";
                case "tool.caliper": return "數位卡尺";
                case "tool.socket_wrench": return "套筒扳手";
                case "tool.torque_wrench": return "扭力扳手";
                case "tool.cleaning_cloth": return "清潔布";
                case "tool.marker": return "標記筆";
                case "tool.lockout_tag": return "上鎖掛牌組";
                case "tool.safety_harness": return "安全帶";
                default: return toolId;
            }
        }

        // ---------- 步驟自訂的錯誤規則 ----------
        /// <summary>收到（一筆或一整批 Commit 後的）StateChanged：黑板已是完整狀態，逐筆變更比對本步的錯誤規則。</summary>
        void OnStateChanged(StateChanged e)
        {
            var s = runner.Current;
            if (s == null || runner.Finished || s.def.errors == null) return;
            var ctx = runner.ContextFor(s);
            foreach (var c in e.Changes)
                foreach (var rule in ActiveRules(s.def))
                {
                    if (fired.Contains(rule.errorId)) continue;
                    if (Triggered(rule, c, ctx, s)) Fire(rule, s, c.key);
                }
        }

        /// <summary>R1.5b K8：目前輸入模式下載入的錯誤規則（inputMode=desktop 的只在桌面模式；VR 讀數自動填入，抄錯讀數不會發生）。</summary>
        public static IEnumerable<ErrorRule> ActiveRules(StepDefinition def) =>
            (def.errors ?? Array.Empty<ErrorRule>()).Where(r => r.inputMode != "desktop" || !InputMode.XrActive);

        bool Triggered(ErrorRule r, StateChange c, EvalContext ctx, RuntimeStep s)
        {
            string key = c.key, text = c.newValue;
            switch (r.trigger)
            {
                case ErrorTrigger.ValueOutOfRange:
                    if (!ValueInRangeEvaluator.Expand(r.key, r.count, ctx).Contains(key) || text == null) return false;
                    return !ValueInRangeEvaluator.InRange(c.Value, r.min, r.max);   // 與完成判定同一個容差
                case ErrorTrigger.SequenceMismatch:
                    if (ctx.K(r.key) != key || string.IsNullOrEmpty(text)) return false;
                    return !(r.expected ?? "").StartsWith(text);
                case ErrorTrigger.ActionBefore:
                    if (!ValueInRangeEvaluator.Expand(r.key, r.count, ctx).Contains(key) || text != "true") return false;
                    return !ChecklistEval.Done(state, ctx.K(r.key2));   // key2 可以是鍵或條件（例：距離<=容許值鍵）
                case ErrorTrigger.Incomplete:
                    if (!ValueInRangeEvaluator.Expand(r.key, r.count, ctx).Contains(key) || string.IsNullOrEmpty(text) || text == "false") return false;
                    return r.keys.Any(k => !state.GetBool(ctx.K(k)));
                case ErrorTrigger.ChoiceEquals:
                    if (!ValueInRangeEvaluator.Expand(r.key, r.count, ctx).Contains(key) || text != r.value) return false;
                    if (string.IsNullOrEmpty(r.whenAnswer)) return true;
                    var decision = RuleEvaluators.FindDecision(s.def.completion);
                    return decision != null && DecisionEvaluator.Answer(decision, state) == r.whenAnswer;
                default:
                    return false;   // WrongTool / WrongTarget 由 Check 處理
            }
        }

        /// <summary>說明文字中的 {值:鍵} 換成黑板目前的數值，例「還差 {值:part.pan_head.lower_distance_cm} cm」。</summary>
        string Format(string text) =>
            string.IsNullOrEmpty(text) ? text : System.Text.RegularExpressions.Regex.Replace(text, @"\{值:([^}]+)\}",
                m => SimIds.IsDistanceKey(m.Groups[1].Value) ? SimIds.CmText(state.GetFloat(m.Groups[1].Value))   // 距離往上進位：不會出現「還差 0.0 cm」
                    : state.GetFloat(m.Groups[1].Value).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

        void Fire(ErrorRule r, RuntimeStep s, string target)
        {
            if (!fired.Add(r.errorId)) return;
            state.Set("error.last", r.errorId);
            bus.Publish(new ErrorDetected
            {
                errorId = r.errorId, severity = r.severity, what = Format(r.what), why = Format(r.why), fix = Format(r.fix),
                stepId = s.RunId, blocks = r.blocks || r.severity == Severity.Critical, targetId = target, time = SimClock.Now
            });
        }
    }
}
