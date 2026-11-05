using System;
using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Training
{
    public enum TrainingMode { Tutorial, Practice }

    /// <summary>
    /// 模式與情境：教學（提示、高亮、螺栓編號、朝外箭頭全開）／練習（只顯示步驟，不高亮）。
    /// 情境（全部從升弓開始，要先用升降控制器降弓）：正常、未接地直接拆（E2）、兩條都合格（E6c）。寫入黑板 training.*、scenario.*。
    /// </summary>
    public class ModeController
    {
        readonly ISimState state;
        public TrainingMode Mode { get; private set; }
        public ScenarioDefinition Scenario { get; private set; }

        public ModeController(ISimState state) { this.state = state; }

        public void Begin(TrainingMode mode, ScenarioDefinition scenario)
        {
            Mode = mode; Scenario = scenario;
            state.Set("training.mode", mode == TrainingMode.Tutorial ? "tutorial" : "practice");
            state.SetFloat("ui.notice_seconds", ToolRulesDef.Load()?.noticeSeconds ?? 6f);
            state.Set("training.scenario", scenario != null ? scenario.scenarioId : "");
            state.Set("training.scenario_title", scenario != null ? scenario.title : "");
            state.SetBool("scenario.start_raised", scenario != null && scenario.startRaised);
            state.SetBool("training.allow_pose_toggle", mode == TrainingMode.Practice && scenario != null && scenario.startRaised);
            state.SetFloat("scenario.seed", scenario != null ? scenario.randomSeed : 1);
            if (scenario != null) KeyValueList.Apply(scenario.initialStates, state);
            state.SetBool("training.running", true);
        }
    }

    /// <summary>
    /// 提示：每步開始時把「下一步做什麼、用什麼、點哪裡」寫到黑板 hint.*；教學模式才高亮目標、顯示螺栓編號與朝外箭頭。
    /// </summary>
    public class HintService : IDisposable
    {
        readonly IEventBus bus; readonly ISimState state;

        string[][] subs = new string[0][];
        string stepHighlight = "";
        int lastIndex = -1;
        bool hasStep, lastStalled;

        public HintService(IEventBus bus, ISimState state)
        {
            this.bus = bus; this.state = state;
            bus.Subscribe<StepStarted>(OnStep);
            bus.Subscribe<StateChanged>(OnState);
        }

        public void Dispose()
        {
            bus.Unsubscribe<StepStarted>(OnStep);
            bus.Unsubscribe<StateChanged>(OnState);
        }

        void OnStep(StepStarted e)
        {
            bool tutorial = state.Get("training.mode") != "practice";
            state.SetBool("ui.tray_open", false);   // 零件盒說明框只留在看它的那一步
            state.Set("hint.text", e.text ?? "");
            state.SetBool("hint.show_bolt_numbers", tutorial && e.showBoltNumbers);
            state.SetBool("hint.show_arrow", tutorial && e.showOutwardArrow);
            state.Set("hint.side", e.side ?? "");
            stepHighlight = e.highlightIds ?? "";
            hasStep = true;
            subs = (e.subActions ?? "").Split('\n').Where(x => x.Length > 0).Select(x => x.Split('\u001f')).ToArray();
            lastIndex = -1;
            UpdateSub();
        }

        void OnState(StateChanged e)
        {
            if (!hasStep) return;   // 還沒開始任何步驟時不發提示
            if (e.Has("procedure.sub_index") || e.Has("training.mode") || e.Has("procedure.stalled") || e.Has("procedure.stall_target")) UpdateSub();
        }

        /// <summary>教學模式：只高亮目前這一個小動作的目標，並把它的短指令寫到 hint.sub_*。</summary>
        void UpdateSub()
        {
            bool tutorial = state.Get("training.mode") != "practice";
            int idx = (int)state.GetFloat("procedure.sub_index");
            string mode = state.Get("training.mode");
            bool stalled = state.GetBool("procedure.stalled") && (mode == "tutorial" || mode == "");   // 練習、考核不直接講還差什麼
            if (idx == lastIndex && stalled == lastStalled && !stalled && state.Has("hint.highlight")) return;
            lastIndex = idx; lastStalled = stalled;
            string highlight = stepHighlight;
            using (state.BeginBatch())   // 提示的各鍵一批生效；HighlightRequested 在 Commit 之後發
            {
                state.SetFloat("hint.sub_count", subs.Length);
                state.SetFloat("hint.sub_index", idx);
                // 「↶ 上一步」是否可用由 ProcedureRunner 比對狀態快照後寫在 procedure.can_back／procedure.back_reason
                if (idx < subs.Length)
                {
                    var a = subs[idx];
                    state.Set("hint.sub_text", a.Length > 0 ? a[0] : "");
                    if (a.Length > 1 && a[1] == SimIds.SpareToken) a[1] = SimIds.SpareTarget(state);
                    state.Set("hint.sub_target", a.Length > 1 ? a[1] : "");
                    state.Set("hint.sub_tool", a.Length > 2 ? a[2] : "");
                    state.Set("hint.sub_why", a.Length > 4 ? a[4].Replace("{吸附}", state.GetFloat("strip.snap_cm").ToString("0.#")) : "");
                    state.Set("hint.sub_stand", a.Length > 5 ? a[5] : "");   // 這個小動作的站點（B 鍵傳到這裡）
                    if (a.Length > 1 && a[1].Length > 0) highlight = a[1];
                }
                else
                {
                    // 小動作都做完不等於步驟完成：完成了會直接換步，還留在這裡就是在等完成條件
                    string target = stalled ? state.Get("procedure.stall_target") : "";
                    state.Set("hint.sub_text", subs.Length == 0 ? "" : stalled ? "還差：" + state.Get("procedure.stall_text") : "確認這一步的結果…");
                    state.Set("hint.sub_target", target); state.Set("hint.sub_why", "");
                    state.Set("hint.sub_tool", stalled ? state.Get("procedure.stall_tool") : "");
                    state.Set("hint.sub_stand", stalled ? state.Get("procedure.stall_stand") : "");
                    if (subs.Length > 0) highlight = !stalled ? "" : target.Length > 0 ? target : stepHighlight;
                }
                state.Set("hint.highlight", tutorial ? highlight : "");
            }
            bus.Publish(new HighlightRequested { text = tutorial ? highlight : "", time = SimClock.Now });
        }
    }

    /// <summary>
    /// 錯誤回饋：把錯誤的「做錯什麼／為什麼／怎麼改」寫到黑板 ui.error.* 給對話框顯示；
    /// Critical（或規則要求）時暫停操作（training.paused），只能「從這一步重做」。
    /// 介面送 ActionRequested(ui.error, restart / dismiss)。
    /// </summary>
    public class ErrorFeedback : IDisposable
    {
        readonly IEventBus bus; readonly ISimState state;

        public ErrorFeedback(IEventBus bus, ISimState state)
        {
            this.bus = bus; this.state = state;
            bus.Subscribe<ErrorDetected>(OnError);
            bus.Subscribe<ActionRequested>(OnRequest);
        }

        public void Dispose()
        {
            bus.Unsubscribe<ErrorDetected>(OnError);
            bus.Unsubscribe<ActionRequested>(OnRequest);
        }

        static int Rank(string severity) => severity == "Critical" ? 2 : severity == "Major" ? 1 : 0;

        void OnError(ErrorDetected e)
        {
            if (e.deferred) return;   // R1.5b K3：只進報表，進行中不跳提示
            // 已有更嚴重的錯誤在畫面上時，不被較輕的蓋掉
            if (state.GetBool("ui.error.active") && Rank(state.Get("ui.error.severity")) > Rank(e.severity.ToString())) return;
            bool blocking = e.severity == Severity.Critical || e.blocks;
            state.Batch(() =>   // 錯誤視窗的內容與暫停一批生效（不會先顯示視窗、內容還是上一個錯誤）
            {
                state.Set("ui.error.id", e.errorId);
                state.Set("ui.error.severity", e.severity.ToString());
                state.Set("ui.error.what", e.what ?? "");
                state.Set("ui.error.why", e.why ?? "");
                state.Set("ui.error.fix", e.fix ?? "");
                state.Set("ui.error.step", e.stepId ?? "");
                state.SetBool("ui.error.blocking", blocking);
                state.SetBool("ui.error.active", true);
                if (blocking) state.SetBool("training.paused", true);
            });
        }

        void OnRequest(ActionRequested e)
        {
            if (e.targetId != "ui.error") return;
            if (e.text == "dismiss" && state.GetBool("ui.error.blocking")) return;
            state.Batch(() =>
            {
                state.SetBool("ui.error.active", false);
                state.SetBool("ui.error.blocking", false);
                state.SetBool("training.paused", false);
            });
            if (e.text == "restart") state.AfterCommit(() => bus.Publish(new RestartStepRequested { time = SimClock.Now }));   // 重做（還原快照）一定在 Commit 之後
        }
    }

    /// <summary>
    /// 結算：依嚴重度計錯（同一步同一錯誤只算一次）與用時。分數 = 100 − 2×Minor − 5×Major − 15×Critical（示例值），
    /// 有 Critical 即不通過。寫入黑板 score.*，並發一次 SessionResult（內容就是結算畫面顯示的數字）。
    /// 重播中的事件（isReplay）一律不計分。
    /// </summary>
    public class Scoring : IDisposable
    {
        readonly IEventBus bus; readonly ISimState state;
        readonly HashSet<string> counted = new HashSet<string>();
        float startTime;

        /// <summary>結算時還沒完成（也沒被跳過）的執行步驟；App 依 ProcedureRunner 提供，沒有就是空的。</summary>
        public Func<string[]> IncompleteSteps;

        public Scoring(IEventBus bus, ISimState state)
        {
            this.bus = bus; this.state = state;
            bus.Subscribe<ErrorDetected>(OnError);
            bus.Subscribe<ProcedureCompleted>(OnDone);
        }

        public void Dispose()
        {
            bus.Unsubscribe<ErrorDetected>(OnError);
            bus.Unsubscribe<ProcedureCompleted>(OnDone);
        }

        public void Start(float now)
        {
            startTime = now;
            counted.Clear();
            foreach (var k in new[] { "minor", "major", "critical" }) state.SetFloat("score." + k, 0);
            state.SetFloat("score.start_time", now);
        }

        void OnError(ErrorDetected e)
        {
            if (e.isReplay) return;
            if (!counted.Add(e.stepId + "|" + e.errorId)) return;
            string key = "score." + e.severity.ToString().ToLowerInvariant();
            state.Batch(() =>   // 次數與紀錄一批
            {
                state.SetFloat(key, state.GetFloat(key) + 1);
                string log = state.Get("score.log");
                string title = state.Get("procedure.current_title");
                state.Set("score.log", (log.Length > 0 ? log + "\n" : "") + $"[{e.severity}] {(string.IsNullOrEmpty(title) ? "" : title + "：")}{e.what}");
            });
        }

        void OnDone(ProcedureCompleted e)
        {
            if (e.isReplay) return;
            float minor = state.GetFloat("score.minor"), major = state.GetFloat("score.major"), critical = state.GetFloat("score.critical");
            float score = Math.Max(0f, 100f - 2f * minor - 5f * major - 15f * critical);
            state.Batch(() =>   // 結算的數字一批寫入，SessionResult 在 Commit 之後才發
            {
                state.SetFloat("score.total", score);
                state.SetFloat("score.elapsed", e.time - startTime);
                state.SetBool("score.passed", critical == 0 && score >= 80f);
                state.SetBool("score.finished", true);
            });
            // 結算畫面讀的就是上面寫進黑板的值；SessionResult 從黑板讀回同一組數字，保證兩邊一致
            bus.Publish(new SessionResult
            {
                passed = state.GetBool("score.passed"), score = state.GetFloat("score.total"), totalSeconds = state.GetFloat("score.elapsed"),
                minor = (int)state.GetFloat("score.minor"), major = (int)state.GetFloat("score.major"), critical = (int)state.GetFloat("score.critical"),
                incompleteSteps = IncompleteSteps?.Invoke() ?? Array.Empty<string>(), time = e.time
            });
        }
    }
}
