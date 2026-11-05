using System;
using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Procedures
{
    /// <summary>展開後的一個執行步驟（重複區塊會依側別各出現一次）。</summary>
    public class RuntimeStep
    {
        public StepDefinition def;
        public string side = "";
        public string sideLabel = "";
        public int defIndex;   // 在 25 個步驟定義中的序號（1 起算）
        public bool repeated;  // 屬於重複區塊（RunId 加側別後綴）

        public string RunId => repeated ? def.stepId + "_" + side : def.stepId;
        public string DoneKey => "step." + RunId + ".done";
    }

    /// <summary>
    /// 依序執行程序：展開重複區塊、處理預設完成、判定完成、跳轉、重做。
    /// 只透過事件與黑板與其他模組溝通。
    /// </summary>
    public class ProcedureRunner : IDisposable
    {
        readonly ProcedureDefinition procedure;
        readonly IEventBus bus;
        readonly ISimState state;
        readonly IStepGate gate;
        readonly List<RuntimeStep> steps = new List<RuntimeStep>();
        Dictionary<string, string> snapshot;
        /// <summary>每個小動作開始時的狀態快照（subSnaps[i]＝第 i 個小動作開始時）。</summary>
        readonly List<Dictionary<string, string>> subSnaps = new List<Dictionary<string, string>>();
        bool evaluating, restoring, evaluateAgain, snapshotPending;
        readonly FreshWriteGate fresh = new FreshWriteGate();
        bool FreshMode => procedure != null && procedure.requireFreshWrite;

        /// <summary>啟用第 i 個小動作（記下條件鍵的 version；R1 的程序沒開 requireFreshWrite 時不影響判定）。</summary>
        void ArmSub(RuntimeStep s, int i)
        {
            var subs = SubActions(s);
            if (i < subs.Length) fresh.Arm(state, SimIds.SpareCondition(state, subs[i].condition));
        }

        /// <summary>小動作完成：條件成立；requireFreshWrite 時還要啟用後有條件鍵被寫過（或列在 autoCompleteAllowed）。</summary>
        bool SubDone(RuntimeStep s, SubAction a)
        {
            if (Skipped(a)) return true;   // D43：分支小動作，條件不成立就跳過
            string cond = SimIds.SpareCondition(state, a.condition);
            bool done = FreshMode ? fresh.Done(state, cond, FreshWriteGate.Allowed(procedure, s.def.stepId, a.subId)) : ChecklistEval.Done(state, cond);
            if (!done || SubActionHold == null) return done;
            holdReason = SubActionHold(s, a);
            return holdReason == null;
        }

        /// <summary>R1.5b b4（D43）：分支小動作（when）輪到時條件不成立＝跳過。</summary>
        public bool Skipped(SubAction a) => !string.IsNullOrEmpty(a.when) && !ChecklistEval.Done(state, a.when);

        /// <summary>R1.5b K3：條件成立也先不讓這個小動作完成，回傳提示（null＝不擋）。教學模式扭力不足時由 ErrorDetector 提供。</summary>
        public Func<RuntimeStep, SubAction, string> SubActionHold;
        string holdReason, shownHold;
        /// <summary>一次判定裡「狀態又改變→再判定」的上限；超過記 Error。</summary>
        public const int MaxRounds = 8;

        public IReadOnlyList<RuntimeStep> Steps => steps;
        public int CurrentIndex { get; private set; } = -1;
        public RuntimeStep Current => CurrentIndex >= 0 && CurrentIndex < steps.Count ? steps[CurrentIndex] : null;
        public bool Finished { get; private set; }
        public bool Started { get; private set; }
        public ProcedureDefinition Procedure => procedure;
        public event Action<RuntimeStep> StepBegan;
        /// <summary>R1.5b K3：步驟就要完成（完成記號還沒寫）。ErrorDetector 在這裡判「只看最後結果」的錯誤。</summary>
        public event Action<RuntimeStep> StepCompleting;

        public ProcedureRunner(ProcedureDefinition procedure, IEventBus bus, ISimState state, IStepGate gate = null)
        {
            this.procedure = procedure; this.bus = bus; this.state = state; this.gate = gate;
            Expand();
        }

        void Expand()
        {
            var rb = procedure.repeatBlock;
            var defs = procedure.steps;
            int from = rb != null && !string.IsNullOrEmpty(rb.fromStepId) ? defs.FindIndex(s => s.stepId == rb.fromStepId) : -1;
            int to = rb != null && !string.IsNullOrEmpty(rb.toStepId) ? defs.FindIndex(s => s.stepId == rb.toStepId) : -1;
            for (int i = 0; i < defs.Count; i++)
            {
                if (from >= 0 && to >= from && i == from)
                {
                    for (int s = 0; s < rb.sides.Length; s++)
                        for (int j = from; j <= to; j++)
                            steps.Add(new RuntimeStep { def = defs[j], defIndex = j + 1, repeated = true, side = rb.sides[s], sideLabel = s < rb.sideLabels.Length ? rb.sideLabels[s] : rb.sides[s] });
                    i = to;
                    continue;
                }
                string fixedSide = defs[i].side == "L" || defs[i].side == "R" ? defs[i].side : "";
                steps.Add(new RuntimeStep
                {
                    def = defs[i], defIndex = i + 1, side = fixedSide,
                    sideLabel = fixedSide == "" ? "" : rb != null && rb.sideLabels.Length > 1 ? rb.sideLabels[fixedSide == "L" ? 0 : 1] : fixedSide
                });
            }
        }

        public void Start()
        {
            if (Started) return;
            Started = true;
            bus.Subscribe<StateChanged>(OnStateChanged);
            bus.Subscribe<StateTouched>(OnStateTouched);
            bus.Subscribe<RestartStepRequested>(OnRestart);
            bus.Subscribe<ActionRequested>(OnRequest);
            // 預設完成的步驟（SI_02、SI_03、SI_06、WC_02、WC_04）開場不寫任何狀態：流程走到那一步時，
            // 才在同一批裡寫入它的狀態與完成記號（見 NextOpen）。以前開場就寫，SI_01 時已經「上鎖、斷電」，▲ 升不上去。
            state.Set("procedure.id", procedure.procedureId);
            CurrentIndex = -1;
            MoveTo(NextOpen(0));
        }

        public void Dispose()
        {
            bus.Unsubscribe<StateChanged>(OnStateChanged);
            bus.Unsubscribe<StateTouched>(OnStateTouched);
            bus.Unsubscribe<RestartStepRequested>(OnRestart);
            bus.Unsubscribe<ActionRequested>(OnRequest);
        }

        void OnRequest(ActionRequested e)
        {
            if (e.targetId == "procedure.sub_back") StepBackSubAction();
        }

        /// <summary>退回時可以還原的黑板鍵：只有工具、工具架、介面、控制器（不含零件、隔離、步驟紀錄）。</summary>
        public static bool UndoableKey(string key) =>
            key.StartsWith("tool.") || key.StartsWith("tools.") || key.StartsWith("rack.") || key.StartsWith("env.tool_rack.") || key.StartsWith("ui.") || key.StartsWith("ctrl.");

        /// <summary>
        /// 比較差異時略過的鍵：紀錄／顯示用的，以及感測讀值（升降高度、姿態、檢測點距離）——
        /// 它們跟著實體連續變化（例：放開按鈕後仍在減速），不是學員這個小動作造成的，退回也不需要還原。
        /// </summary>
        static bool BookkeepingKey(string key) =>
            key.StartsWith("procedure.") || key.StartsWith("hint.") || key.StartsWith("score.") || key.StartsWith("error.") || key.StartsWith("lift.")
            || key.StartsWith("training.") || key.StartsWith("step.")
            || key.EndsWith(".pose") || key.EndsWith("_distance_cm") || key.EndsWith("_delta_cm");

        /// <summary>從 before 到現在改變了哪些鍵（略過紀錄用的鍵）。</summary>
        List<string> ChangedSince(Dictionary<string, string> before)
        {
            var now = state.Snapshot();
            var keys = new HashSet<string>(now.Keys);
            keys.UnionWith(before.Keys);
            return keys.Where(k => !BookkeepingKey(k))
                       .Where(k => !(now.TryGetValue(k, out var a) && before.TryGetValue(k, out var b) && a == b))
                       .ToList();
        }

        /// <summary>上一個小動作能不能退回：從它開始到現在，改變的只有工具／工具架／介面／控制器的鍵。回傳 null＝可以，否則是原因。</summary>
        public string CanStepBack()
        {
            int idx = (int)state.GetFloat("procedure.sub_index");
            if (Current == null) return "目前沒有進行中的步驟";
            if (idx <= 0 || subSnaps.Count < idx) return "已經是這一步的第一個小動作";
            var blocked = ChangedSince(subSnaps[idx - 1]).Where(k => !UndoableKey(k)).ToList();
            return blocked.Count == 0 ? null : "不能退回：零件已改變，請用「從這一步重做」";
        }

        /// <summary>
        /// 「↶ 上一步」：退回上一個小動作。比對該小動作開始時的狀態快照，只有在改變的鍵全部屬於工具、工具架、介面、控制器時才允許；
        /// 允許時把這些鍵還原（例：拿起的工具放回架上、套筒尺寸恢復），讓那個小動作重新等待完成。其他情況回傳原因。
        /// </summary>
        public string StepBackSubAction()
        {
            string msg = CanStepBack();
            if (msg == null)
            {
                int idx = (int)state.GetFloat("procedure.sub_index");
                var before = subSnaps[idx - 1];
                restoring = true;
                try
                {
                    // 一批還原：其他模組只會看到退回後的完整狀態（例：工具回到架上與手上變空同時生效）
                    state.Batch(() =>
                    {
                        foreach (var k in ChangedSince(before)) state.Set(k, before.TryGetValue(k, out var v) ? v : null);
                    });
                }
                finally { restoring = false; }
                subSnaps.RemoveRange(idx - 1, subSnaps.Count - (idx - 1));
                subSnaps.Add(before);
                state.SetFloat("procedure.sub_index", idx - 1);
                ArmSub(Current, idx - 1);
                msg = "已退回上一個小動作";
                PublishBackState();
            }
            state.Set("ui.toast", msg);
            return msg;
        }

        void PublishBackState()
        {
            string why = CanStepBack();
            if (state.GetBool("procedure.can_back") != (why == null)) state.SetBool("procedure.can_back", why == null);
            if (state.Get("procedure.back_reason") != (why ?? "")) state.Set("procedure.back_reason", why ?? "");
        }

        /// <summary>
        /// 從 from 開始找下一個要做的步驟。途經的預設完成步驟：在同一批裡寫入它的狀態（presetStates）與完成記號，繼續往後找。
        /// </summary>
        int NextOpen(int from)
        {
            var notes = new List<string>();
            int next = NextOpen(from, notes);
            state.Set("procedure.preset_note", string.Join("\n", notes));
            if (notes.Count > 0) { state.Set("ui.notice", ""); state.Set("ui.notice", string.Join("\n", notes)); }
            return next;
        }

        /// <summary>途經的預設完成步驟記進 notes：步驟編號會跳，要讓學員看到跳過的那一步在做什麼。</summary>
        int NextOpen(int from, List<string> notes)
        {
            for (int i = from; i < steps.Count; i++)
            {
                var st = steps[i];
                if (st.def.presetDone && !state.GetBool(st.DoneKey))
                {
                    state.Batch(() =>
                    {
                        KeyValueList.Apply(st.def.presetStates, state);
                        state.SetBool(st.DoneKey, true);
                    });
                    notes.Add($"第 {st.defIndex} 步 {st.def.title}：{st.def.instruction}");
                    continue;
                }
                if (!state.GetBool(st.DoneKey)) return i;
            }
            return steps.Count;
        }

        void MoveTo(int index)
        {
            CurrentIndex = index;
            if (index >= steps.Count)
            {
                Finished = true;
                state.Batch(() => { state.Set("procedure.current", ""); state.SetBool("procedure.finished", true); });
                bus.Publish(new ProcedureCompleted { procedureId = procedure.procedureId, time = SimClock.Now });
                return;
            }
            BeginCurrent();
        }

        void BeginCurrent()
        {
            var s = Current;
            state.Batch(() =>   // 目前步驟的各鍵一批生效
            {
                state.Set("procedure.current", s.RunId);
                state.Set("procedure.side", s.side);
                state.Set("procedure.current_title", string.IsNullOrEmpty(s.sideLabel) || (s.def.title ?? "").Contains(s.sideLabel) ? s.def.title : s.def.title + "（" + s.sideLabel + "）");
                state.SetFloat("procedure.sub_index", 0);
                state.SetFloat("procedure.sub_count", SubActions(s).Length);
                state.Set("procedure.gate_reason", "");   // 上一步的閘門說明不帶到這一步
                state.SetBool("procedure.stalled", false);
                state.Set("procedure.stall_text", "");
                state.Set("procedure.stall_ids", "");
                state.Set("procedure.stall_target", "");
                state.Set("procedure.stall_stand", "");
                state.Set("procedure.stall_tool", "");
                state.SetBool("procedure.stall_hint_shown", false);
            });
            ArmSub(s, 0);
            allDoneSince = -1f;
            stallReported.Clear();
            snapshot = state.Snapshot();   // 先拍一張（Commit 之後）；狀態穩定後在 Evaluate 結尾換成穩定的那一張
            subSnaps.Clear();
            subSnaps.Add(snapshot);
            snapshotPending = true;
            StepBegan?.Invoke(s);
            bus.Publish(new StepStarted
            {
                stepId = s.RunId, title = s.def.title, instruction = s.def.instruction,
                toolHint = s.def.toolHint, targetHint = s.def.targetHint, toolId = s.def.requiredToolId,
                side = s.side, sideLabel = s.sideLabel,
                index = s.defIndex, total = procedure.steps.Count, runIndex = CurrentIndex + 1, runTotal = steps.Count,
                targetIds = ExpandTargets(s), text = s.def.hints?.text, time = SimClock.Now,
                showBoltNumbers = s.def.hints?.showBoltNumbers ?? false, showOutwardArrow = s.def.hints?.showOutwardArrow ?? false,
                valueTags = string.Join(";", s.def.valueTags ?? Array.Empty<string>()),
                highlightIds = string.Join(",", (s.def.hints?.highlightIds ?? Array.Empty<string>()).Select(id => StateKeys.Expand(id, s.side))),
                why = s.def.hints?.why ?? "",
                subActions = string.Join("\n", SubActions(s).Select(a => $"{a.text}\u001f{a.targetId}\u001f{a.toolId}\u001f{a.condition}\u001f{a.why}\u001f{a.standPointId}")),
                checklist = string.Join("\n", (s.def.hints?.checklist ?? Array.Empty<string>()).Select(c => StateKeys.Expand(c, s.side)))
            });
            Evaluate();
        }

        /// <summary>R1.5b K4：教學面板依目前輸入模式顯示對應的指令（VR＝text；桌面＝textDesktop，空白就用 text）。</summary>
        public static string TextFor(SubAction a) => !InputMode.XrActive && !string.IsNullOrEmpty(a.textDesktop) ? a.textDesktop : a.text;

        /// <summary>本步的小動作（已展開側別）。</summary>
        public static SubAction[] SubActions(RuntimeStep s) =>
            (s.def.subActions ?? Array.Empty<SubAction>()).Select(a => new SubAction
            {
                text = StateKeys.Expand(TextFor(a), s.side).Replace(SimIds.ToolCountToken, SimIds.ToolCountLabel), why = StateKeys.Expand(InputMode.XrActive && !string.IsNullOrEmpty(a.hint) ? a.hint : a.why, s.side), targetId = StateKeys.Expand(a.targetId, s.side), textDesktop = StateKeys.Expand(a.textDesktop ?? "", s.side),
                toolId = a.toolId, condition = StateKeys.Expand(a.condition, s.side), standPointId = SubAction.ExpandStand(a.standPointId, s.side), subId = a.subId ?? "", when = a.when ?? ""
            }).ToArray();

        /// <summary>
        /// 依序推進小動作：目前這個的條件成立就鎖定完成、換下一個（可連續推進）。回傳是否全部完成。
        /// 所有模式都要求（跳過小動作不能完成步驟）；教學模式才把小動作顯示出來（由 HintService／介面決定）。
        /// </summary>
        bool AdvanceSubActions(RuntimeStep s)
        {
            var subs = SubActions(s);
            int idx = (int)state.GetFloat("procedure.sub_index");
            int start = idx;
            holdReason = null;
            while (idx < subs.Length && SubDone(s, subs[idx]))
            {
                idx++;
                ArmSub(s, idx);
                if (subSnaps.Count <= idx) subSnaps.Add(state.Snapshot());   // 第 idx 個小動作從現在開始
            }
            if (idx != start) state.SetFloat("procedure.sub_index", idx);
            if (holdReason != shownHold) { state.Set("procedure.gate_reason", holdReason ?? ""); shownHold = holdReason; }   // 擋下的提示（例：扭力不足，請鎖到扳手響）
            PublishBackState();
            return idx >= subs.Length;
        }

        public static string[] ExpandTargets(RuntimeStep s) =>
            (s.def.targetIds ?? Array.Empty<string>()).Select(t => StateKeys.Expand(t, s.side)).ToArray();

        /// <summary>只在 Commit 後（一筆或一整批寫入生效後）判定，所以步驟切換與開始快照都不會拍到寫到一半的狀態。</summary>
        void OnStateChanged(StateChanged e)
        {
            if (restoring || !e.Any(k => !k.StartsWith("procedure."))) return;
            Evaluate();
        }

        /// <summary>同值寫入（例：已經 true 的自檢再點一次）：只有 requireFreshWrite 的程序需要重新判定。</summary>
        void OnStateTouched(StateTouched e)
        {
            if (!FreshMode || restoring || e.keys.All(k => k.StartsWith("procedure."))) return;
            Evaluate();
        }

        void OnRestart(RestartStepRequested e) => RestartCurrentStep();

        /// <summary>還原到本步開始時的狀態，重新開始本步。</summary>
        public void RestartCurrentStep()
        {
            if (Current == null || snapshot == null) return;
            // 錯誤紀錄與分數不還原：重做不會抹掉已經犯過的錯
            var kept = state.Snapshot().Where(kv => kv.Key.StartsWith("score.")).ToList();
            state.Restore(snapshot);
            foreach (var kv in kept) state.Set(kv.Key, kv.Value);
            BeginCurrent();
        }

        public EvalContext ContextFor(RuntimeStep s) => new EvalContext
        {
            state = state,
            side = s.side,
            allPreviousDone = () => steps.Take(steps.IndexOf(s)).All(p => state.GetBool(p.DoneKey) || state.GetBool("step." + p.RunId + ".skipped"))
        };

        /// <summary>判定目前步驟；完成就前進（連續完成的步驟會一路推進）。</summary>
        public void Evaluate()
        {
            if (!Started || Finished) return;
            if (state.InBatch) return;   // 批次還沒 Commit：等 Commit 的 StateChanged 再判定
            // 判定中又收到 StateChanged（例：判定自己寫 procedure.* 時，其他模組跟著寫了別的鍵）：記下來，這一輪結束後再判定一次。
            // 以前直接略過，若那筆寫入發生在條件已經檢查過之後，就沒有人再判定 → 條件成立了步驟卻不跳（漏觸發）。
            if (evaluating) { evaluateAgain = true; return; }
            evaluating = true;
            int rounds = 0;
            try
            {
                do
                {
                    evaluateAgain = false;
                    do
                    {
                        var s = Current;
                        if (s == null) break;
                        bool subsDone = AdvanceSubActions(s);   // 跳過小動作不能完成步驟
                        if (!subsDone || !RuleEvaluators.Evaluate(s.def.completion, ContextFor(s))) break;
                        if (gate != null && !gate.CanComplete(s.RunId, out var reason)) { if (state.Get("procedure.gate_reason") != reason) state.Set("procedure.gate_reason", reason); break; }
                        Complete(s);
                    } while (!Finished);
                    if (evaluateAgain && ++rounds >= MaxRounds)
                    {
                        // 有模組每次通知都寫新值，狀態一直不穩定：停在這裡並記 Error（下一次 Commit 會再判定）
                        ErrorLog($"[ProcedureRunner] 重新判定超過 {MaxRounds} 次狀態仍不穩定：{Current?.RunId}");
                        break;
                    }
                } while (evaluateAgain && !Finished && !state.InBatch);
                // 狀態穩定了：新步驟的開始快照現在才存（進入步驟時其他模組寫入的值也包含在內）
                if (snapshotPending && !evaluateAgain && !state.InBatch && Current != null)
                {
                    snapshotPending = false;
                    snapshot = state.Snapshot();
                    if (subSnaps.Count > 0) subSnaps[0] = snapshot; else subSnaps.Add(snapshot);
                }
            }
            finally { evaluating = false; evaluateAgain = false; }
        }

        // ---------- 卡關偵測（防護網：只讀、只記錄，不推進步驟） ----------
        public const float PollInterval = 0.5f, StallSeconds = 3f;
        public const string StallKinds = "missed_commit,rule_mismatch";
        /// <summary>missed_commit 的記錄方式（預設 Unity Console 的 Error）；測試可換成測試用記錄器並斷言收到。</summary>
        public static Action<string> ErrorLog = UnityEngine.Debug.LogError;
        float nextPoll = float.MinValue, allDoneSince = -1f;
        readonly HashSet<string> stallReported = new HashSet<string>();

        /// <summary>
        /// 每 0.5 s 輕量檢查目前步驟（App 每幀呼叫，內部節流；批次中略過）。只讀判定，不推進步驟：
        /// ・小動作與完成條件（含安全閘門）都已成立、步驟卻沒推進 → StepStallDetected(missed_commit)＋Error log（判定漏觸發，是 bug，不能蓋掉）；
        /// ・小動作全部完成，但完成條件超過 3 秒仍不成立 → StepStallDetected(rule_mismatch)，並把「還差什麼」寫到 procedure.stall_*（介面依模式顯示）。
        /// 每一步每一種只記一次。
        /// </summary>
        public void Poll(float now)
        {
            if (!Started || Finished || evaluating || state.InBatch) return;
            if (now < nextPoll) return;
            nextPoll = now + PollInterval;
            var s = Current;
            if (s == null) return;
            var subs = SubActions(s);
            int idx = (int)state.GetFloat("procedure.sub_index");
            int would = idx;
            while (would < subs.Length && SubDone(s, subs[would])) { would++; if (FreshMode) break; }   // 只有目前這個有啟用時的 version
            var ctx = ContextFor(s);
            bool rule = RuleEvaluators.Evaluate(s.def.completion, ctx);
            string gateReason = "";
            bool gateOk = gate == null || gate.CanComplete(s.RunId, out gateReason);

            if (would > idx || (would >= subs.Length && rule && gateOk))
            {
                Report(s, "missed_commit", RuleExplainer.Explain(s.def.completion, ctx), now,
                    would > idx ? $"小動作 {idx + 1} 的條件已成立卻沒有推進" : "完成條件已成立卻沒有推進");
                return;
            }
            if (idx < subs.Length) { allDoneSince = -1f; return; }
            if (allDoneSince < 0f) { allDoneSince = now; return; }
            if (now - allDoneSince < StallSeconds) return;
            var unmet = RuleExplainer.Explain(s.def.completion, ctx);
            if (!gateOk) unmet.Insert(0, new UnmetCondition { id = "Gate:" + gateReason });
            UpdateStallHint(s, subs, unmet);
            Report(s, "rule_mismatch", unmet, now, "");
        }

        /// <summary>不成立條件的中文說明（見 RuleExplainer.Describe；前面步驟沒完成時用那一步的標題）。</summary>
        public (string text, string source) DescribeUnmet(UnmetCondition u, RuntimeStep s) =>
            RuleExplainer.Describe(u, s, id => steps.FirstOrDefault(x => x.RunId == id)?.def.title);

        /// <summary>「還差什麼」：第一個不成立的條件的說明（從步驟資料讀：小動作指令 → 小步驟清單 → 錯誤規則的怎麼改 → 步驟說明）。</summary>
        void UpdateStallHint(RuntimeStep s, SubAction[] subs, List<UnmetCondition> unmet)
        {
            var first = unmet.FirstOrDefault();
            string text = first == null ? s.def.instruction : DescribeUnmet(first, s).text;
            var missing = first == null ? null : RuleExplainer.SubActionFor(first, subs);
            state.Batch(() =>
            {
                state.Set("procedure.stall_target", missing?.targetId ?? "");   // 提示、C 和「帶我過去」帶到還差的地方
                state.Set("procedure.stall_stand", missing?.standPointId ?? "");
                state.Set("procedure.stall_tool", missing?.toolId ?? "");
                state.SetBool("procedure.stalled", true);
                state.Set("procedure.stall_text", text);
                state.Set("procedure.stall_ids", string.Join("\n", unmet.Select(u => u.id)));
            });
        }

        void Report(RuntimeStep s, string kind, List<UnmetCondition> unmet, float now, string note)
        {
            if (!stallReported.Add(s.RunId + "|" + kind)) return;
            string ids = string.Join("\n", unmet.Select(u => u.id));
            string values = string.Join("\n", unmet.SelectMany(u => u.values));
            if (kind == "missed_commit")
                ErrorLog($"[ProcedureRunner] 判定漏觸發（missed_commit）：{s.RunId}　{note}");
            bus.Publish(new StepStallDetected
            {
                stepId = s.RunId, side = s.side, kind = kind, conditionIds = ids, values = values,
                explain = kind == "rule_mismatch" ? state.Get("procedure.stall_text") : note, time = SimClock.Now
            });
        }

        void Complete(RuntimeStep s)
        {
            StepCompleting?.Invoke(s);
            state.SetBool(s.DoneKey, true);
            bus.Publish(new StepCompleted { stepId = s.RunId, side = s.side, time = SimClock.Now });

            var jump = (procedure.onPass ?? Array.Empty<JumpRule>()).FirstOrDefault(j => j.stepId == s.def.stepId && state.Get(StateKeys.Expand(j.key, s.side)) == j.value);
            if (jump != null)
            {
                int target = steps.FindIndex(x => x.def.stepId == jump.jumpTo);
                if (target > CurrentIndex)
                {
                    for (int i = CurrentIndex + 1; i < target; i++) state.SetBool("step." + steps[i].RunId + ".skipped", true);
                    MoveTo(NextOpen(target));
                    return;
                }
            }
            MoveTo(NextOpen(CurrentIndex + 1));
        }
    }
}
