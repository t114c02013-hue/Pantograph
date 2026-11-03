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
        /// <summary>R1.5b K3：考核政策（預設讀 Resources/ExamPolicy；測試可換）。</summary>
        public ExamPolicyDef Policy = ExamPolicyDef.Load();

        public ErrorDetector(ProcedureRunner runner, IEventBus bus, ISimState state)
        {
            this.runner = runner; this.bus = bus; this.state = state;
            bus.Subscribe<StateChanged>(OnStateChanged);
            bus.Subscribe<StepStarted>(OnStepStarted);
            runner.StepCompleting += OnStepCompleting;
            if (runner.SubActionHold == null) runner.SubActionHold = HoldSubAction;
        }

        public void Dispose()
        {
            bus.Unsubscribe<StateChanged>(OnStateChanged);
            bus.Unsubscribe<StepStarted>(OnStepStarted);
            runner.StepCompleting -= OnStepCompleting;
            if (runner.SubActionHold == HoldSubAction) runner.SubActionHold = null;
        }

        /// <summary>R1.5b K3：步驟完成時，還沒判的螺栓（最後一顆）判一次；合格卻沒畫標記也在這時判。</summary>
        void OnStepCompleting(RuntimeStep s)
        {
            var ctx = runner.ContextFor(s);
            foreach (var kv in leaving.ToList()) JudgeLeave(kv.Key, kv.Value, s);
            leaving.Clear();
            foreach (var r in ActiveRules(s.def).Where(r => r.trigger == ErrorTrigger.UnsetAtCompletion && !fired.Contains(r.errorId)))
                if (Enumerable.Range(1, Math.Max(1, r.count)).Any(n =>
                    {
                        var k2 = ctx.K(r.key2, n);
                        return state.Has(k2) && ValueInRangeEvaluator.InRange(state.GetFloat(k2), r.min, r.max) && !state.GetBool(ctx.K(r.key, n));
                    }))
                    Fire(r, s, r.key);
        }

        void OnStepStarted(StepStarted e) { fired.Clear(); leaving.Clear(); }

        // ---------- R1.5b K3：扭力不足（驗收組長裁定）----------
        /// <summary>每條 OnLeave 規則目前正在鎖的那顆螺栓的鍵（離開時判）。</summary>
        readonly Dictionary<ErrorRule, string> leaving = new Dictionary<ErrorRule, string>();

        /// <summary>開始鎖另一顆、換工具時，判上一顆的最終扭力；這一顆記成「正在鎖」。</summary>
        void TrackLeave(string key, RuntimeStep s, EvalContext ctx)
        {
            foreach (var r in ActiveRules(s.def).Where(r => r.trigger == ErrorTrigger.OutOfRangeOnLeave))
            {
                bool bolt = ValueInRangeEvaluator.Expand(r.key, r.count, ctx).Contains(key);
                if (leaving.TryGetValue(r, out var prev) && (key == SimIds.ActiveTool || bolt && key != prev)) { JudgeLeave(r, prev, s); leaving.Remove(r); }
                if (bolt) leaving[r] = key;
            }
        }

        void JudgeLeave(ErrorRule r, string key, RuntimeStep s)
        {
            if (fired.Contains(r.errorId) || !state.Has(key)) return;
            float v = state.GetFloat(key);
            if (v >= r.min - ValueInRangeEvaluator.Epsilon) return;
            if (!ExamPolicyDef.In(Policy?.recordModes, state.Get("training.mode"))) return;   // 教學模式擋下（HoldSubAction），不記
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string bolt = key.Substring(0, key.LastIndexOf('.'));   // …bolt_3.torque_Nm → 3
            Fire(r, s, key, $"（{bolt.Substring(bolt.LastIndexOf('_') + 1)} 號螺栓 {v.ToString("0.0", ci)} N·m，差 {(r.min - v).ToString("0.0", ci)} N·m）");
        }

        /// <summary>教學模式（holdModes）：這個小動作的螺栓扭力不足時不讓它完成，回傳提示（null＝不擋）。給 ProcedureRunner.SubActionHold。</summary>
        public string HoldSubAction(RuntimeStep s, SubAction a)
        {
            if (!ExamPolicyDef.In(Policy?.holdModes, state.Get("training.mode")) || string.IsNullOrEmpty(a.targetId)) return null;
            var ctx = runner.ContextFor(s);
            foreach (var r in ActiveRules(s.def).Where(r => r.trigger == ErrorTrigger.OutOfRangeOnLeave))
                foreach (var k in ValueInRangeEvaluator.Expand(r.key, r.count, ctx).Where(k => k.StartsWith(a.targetId + ".")))
                    if (state.Has(k) && state.GetFloat(k) < r.min - ValueInRangeEvaluator.Epsilon) return Policy.underHint;
            return null;
        }

        static bool Matches(string pattern, string id) =>
            pattern.EndsWith("*") ? id.StartsWith(pattern.Substring(0, pattern.Length - 1)) : pattern == id;

        // ---------- 通用：拿錯工具／點錯目標 ----------
        public GuardResult Check(ActionRequest req)
        {
            var s = runner.Current;
            if (s == null || runner.Finished || req.verb == "inspect" || req.verb == "select") return GuardResult.Allow;
            var block = BlockedByGlobal(req, s);
            if (block != null) return block.Value;
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
            foreach (var c in e.Changes) TrackLeave(c.key, s, ctx);
            WatchGlobal(s);
            foreach (var c in e.Changes)
                foreach (var rule in ActiveRules(s.def))
                {
                    if (fired.Contains(rule.errorId)) continue;
                    if (Triggered(rule, c, ctx, s)) Fire(rule, s, c.key);
                }
        }

        /// <summary>R1.5b b4 3d：BlockWhen 的全程規則（拆接地順序、車頂上拆 #1）：對目標動作前的狀態符合就擋下並記錯（徒手點也算：拆掛鉤是徒手）。</summary>
        GuardResult? BlockedByGlobal(ActionRequest req, RuntimeStep s)
        {
            var ctx = runner.ContextFor(s);
            foreach (var r in runner.Procedure?.globalErrors ?? Array.Empty<ErrorRule>())
            {
                if (r.trigger != ErrorTrigger.BlockWhen || (r.inputMode == "desktop" && InputMode.XrActive)) continue;
                foreach (var k in r.keys)
                {
                    int bar = k.IndexOf('|');
                    if (bar <= 0 || ctx.K(k.Substring(0, bar)) != req.targetId || !ChecklistEval.Done(state, ctx.K(k.Substring(bar + 1)))) continue;
                    fired.Remove(r.errorId);   // 擋下的錯誤每次都記（每次都要讓學員知道為什麼不能做）
                    Fire(r, s, req.targetId);
                    return Blocked(req, r.errorId);
                }
            }
            return null;
        }

        // ---------- R1.5b b4 3c：全程監看的錯誤（天車安全等）----------
        readonly Dictionary<string, bool> globalWas = new Dictionary<string, bool>();

        /// <summary>前提 key2 成立且 keys 任一條件成立，由不成立變成立時記一次（同一步同一個 ID 只記一次）。R1.5b b4：這一步自己的 WhenCondition 規則也一起看（例 ERR_RM04_WrongSocket）。</summary>
        void WatchGlobal(RuntimeStep s)
        {
            foreach (var r in (runner.Procedure?.globalErrors ?? Array.Empty<ErrorRule>()).Concat(ActiveRules(s.def).Where(x => x.trigger == ErrorTrigger.WhenCondition)))
            {
                if (r.trigger != ErrorTrigger.WhenCondition || (r.inputMode == "desktop" && InputMode.XrActive)) continue;
                var ctx = runner.ContextFor(s);
                bool now = (string.IsNullOrEmpty(r.key2) || ChecklistEval.Done(state, ctx.K(r.key2))) && r.keys.Any(k => ChecklistEval.Done(state, ctx.K(k)));
                bool was = globalWas.TryGetValue(r.errorId, out var w) && w;
                globalWas[r.errorId] = now;
                if (now && !was && !fired.Contains(r.errorId)) Fire(r, s, "global");
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

        readonly HashSet<string> firedRun = new HashSet<string>();   // oncePerRun 的規則：整次作業記過就不再記

        void Fire(ErrorRule r, RuntimeStep s, string target, string detail = "")
        {
            if (r.oncePerRun && !firedRun.Add(r.errorId)) return;
            if (!fired.Add(r.errorId)) return;
            state.Set("error.last", r.errorId);
            // R1.5b K3：喀聲回饋還沒確認（k3Ready=false）時，k3DowngradeModes（練習、考核）改記 severityBeforeK3，報告註明
            var severity = r.severity; var what = Format(r.what) + detail;
            string mode = state.Get("training.mode");
            if (!string.IsNullOrEmpty(r.severityBeforeK3) && Policy != null && !Policy.k3Ready && ExamPolicyDef.In(Policy.k3DowngradeModes, mode)
                && Enum.TryParse<Severity>(r.severityBeforeK3, out var before))
            { severity = before; what += ExamPolicyDef.K3Note; }
            bool deferred = r.trigger == ErrorTrigger.OutOfRangeOnLeave && ExamPolicyDef.In(Policy?.deferModes, mode);   // 放行，只進報表
            bus.Publish(new ErrorDetected
            {
                errorId = r.errorId, severity = severity, what = what, why = Format(r.why), fix = Format(r.fix),
                stepId = s.RunId, blocks = !deferred && (r.blocks || severity == Severity.Critical), deferred = deferred, targetId = target, time = SimClock.Now
            });
        }
    }
}
