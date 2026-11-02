using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Recording
{
    /// <summary>
    /// 一場訓練的紀錄器（純 C#，EditMode 可測）：只訂閱 Core 事件、讀黑板，轉成 session.jsonl 的一行一行；
    /// 另外收位姿樣本（poses.bin，分塊）與每 10 s 一筆全場快照（snapshots.bin）。所有寫檔交給 RecordingWriter 的背景執行緒。
    /// 重播中的事件（isReplay）一律不記。
    /// </summary>
    public class SessionRecorder : IDisposable
    {
        public const float SnapshotInterval = 10f;
        public const float HeadRate = 30f, TrackedRate = 15f;
        public const float MoveThreshold = 0.001f, AngleThreshold = 0.5f;
        /// <summary>StateChanged 只記這幾類鍵。</summary>
        public static readonly string[] StatePrefixes = { "part.", "ip.", "tool.", "env.", "isolation.", "ctrl.", "xr.move." };   // xr.move.＝T6 平滑移動距離／時間（每秒一次）

        readonly IEventBus bus;
        readonly ISimState state;
        readonly RecordingWriter writer;
        readonly StepCatalog catalog;
        readonly Func<float> clock;
        readonly List<RecordLine> lines = new List<RecordLine>();
        readonly Dictionary<string, string> lastErrorWhat = new Dictionary<string, string>();
        readonly Dictionary<string, float> stepFirstStart = new Dictionary<string, float>();
        readonly Dictionary<string, int> stepRedo = new Dictionary<string, int>();
        readonly Dictionary<string, float> lastContinuous = new Dictionary<string, float>();
        readonly Dictionary<string, string> pendingContinuous = new Dictionary<string, string>();
        string currentStep = "", lastHover = null, lastLiftCmd = "";
        string[] currentSubs = Array.Empty<string>();
        int lastSub;
        long seq;
        bool begun, ended, disposed, closed;
        float lastSnapshot = -999f;

        // 位姿
        readonly MemoryStream poseBuffer = new MemoryStream();
        readonly BinaryWriter poseWriter;
        readonly Dictionary<string, ushort> poseIds = new Dictionary<string, ushort>();
        readonly Dictionary<string, PoseSample> lastTracked = new Dictionary<string, PoseSample>();
        readonly Dictionary<string, float> lastValues = new Dictionary<string, float>();
        float nextHead, nextTracked, lastChunk;

        public float T0 { get; private set; }
        public string Folder => writer.Folder;
        public IReadOnlyList<RecordLine> Lines => lines;
        public Dictionary<string, object> Meta { get; private set; } = new Dictionary<string, object>();
        public bool Finished => ended;
        /// <summary>結算（SessionResult）寫完 summary.json 後通知（RecordingService 接著產生報表）。</summary>
        public event Action<SessionRecorder> SessionFinished;
        /// <summary>目前所有 ReplayTracked 的位置（快照用；測試可不給）。</summary>
        public Func<IEnumerable<KeyValuePair<string, PoseSample>>> TrackedProvider;
        /// <summary>依 ID 查場景位置（工具架掛位位置；可不給）。</summary>
        public ISceneLocator Locator;

        public SessionRecorder(IEventBus bus, ISimState state, RecordingWriter writer, StepCatalog catalog, Func<float> clock = null)
        {
            this.bus = bus; this.state = state; this.writer = writer; this.catalog = catalog ?? new StepCatalog();
            this.clock = clock ?? (() => SimClock.Now);
            poseWriter = new BinaryWriter(poseBuffer);
            ErrorNames.Preload();
        }

        float Now => clock() - T0;

        // ---------- 開始／結束 ----------
        public void Begin(Dictionary<string, object> meta)
        {
            if (begun) return;
            begun = true;
            T0 = clock();
            Meta = meta ?? new Dictionary<string, object>();
            Meta["steps"] = catalog.ToList();
            Meta["procedureId"] = catalog.procedureId;
            Meta["procedureTitle"] = catalog.procedureTitle;
            writer.WriteFile(RecordFiles.Meta, Json.Write(Meta));
            bus.Subscribe<StepStarted>(OnStepStarted);
            bus.Subscribe<StepCompleted>(OnStepCompleted);
            bus.Subscribe<ActionRequested>(OnRequested);
            bus.Subscribe<ActionPerformed>(OnPerformed);
            bus.Subscribe<ActionBlocked>(OnBlocked);
            bus.Subscribe<ErrorDetected>(OnError);
            bus.Subscribe<ToolSelected>(OnToolSelected);
            bus.Subscribe<ToolPickRequested>(OnPick);
            bus.Subscribe<ToolReturnRequested>(OnReturn);
            bus.Subscribe<MeasurementTaken>(OnMeasured);
            bus.Subscribe<RecordWritten>(OnRecordWritten);
            bus.Subscribe<StateChanged>(OnState);
            bus.Subscribe<PanelMoved>(OnPanelMoved);
            bus.Subscribe<UiCommandIssued>(OnUiCommand);
            bus.Subscribe<HoverChanged>(OnHover);
            bus.Subscribe<SessionResult>(OnResult);
            bus.Subscribe<VideoStarted>(OnVideoStarted);
            bus.Subscribe<VideoStopped>(OnVideoStopped);
            bus.Subscribe<ProcedureCompleted>(OnProcedureCompleted);
            bus.Subscribe<StepStallDetected>(OnStall);
            Write("SessionStarted", "", new Dictionary<string, object>
            {
                { "trainee", Meta.Str("trainee") }, { "mode", Meta.Str("mode") }, { "input", Meta.Str("input") }, { "scenario", Meta.Str("scenarioId") }
            });
            foreach (var s in catalog.steps.Where(s => s.presetDone))
                Write("StepPreset", s.runId, new Dictionary<string, object> { { "name", s.DisplayName }, { "status", "預設完成" } });
            Snapshot(0f);
        }

        /// <summary>沒有結算就結束（重新開始、離開 Play、關閉程式）：記一筆 SessionClosed，沒有 summary.json＝中斷。</summary>
        public void Close(string reason)
        {
            if (!begun || disposed || closed) return;
            closed = true;   // Dispose 會再呼叫一次：只記一筆 SessionClosed
            FlushContinuous(true);
            if (!ended) Write("SessionClosed", currentStep, new Dictionary<string, object> { { "reason", reason ?? "" } });
            FlushPoses();
        }

        public void Dispose()
        {
            if (disposed) return;
            Close("dispose");
            disposed = true;
            if (begun)
            {
                bus.Unsubscribe<StepStarted>(OnStepStarted);
                bus.Unsubscribe<StepCompleted>(OnStepCompleted);
                bus.Unsubscribe<ActionRequested>(OnRequested);
                bus.Unsubscribe<ActionPerformed>(OnPerformed);
                bus.Unsubscribe<ActionBlocked>(OnBlocked);
                bus.Unsubscribe<ErrorDetected>(OnError);
                bus.Unsubscribe<ToolSelected>(OnToolSelected);
                bus.Unsubscribe<ToolPickRequested>(OnPick);
                bus.Unsubscribe<ToolReturnRequested>(OnReturn);
                bus.Unsubscribe<MeasurementTaken>(OnMeasured);
                bus.Unsubscribe<RecordWritten>(OnRecordWritten);
                bus.Unsubscribe<StateChanged>(OnState);
            bus.Unsubscribe<PanelMoved>(OnPanelMoved);
                bus.Unsubscribe<UiCommandIssued>(OnUiCommand);
                bus.Unsubscribe<HoverChanged>(OnHover);
                bus.Unsubscribe<SessionResult>(OnResult);
                bus.Unsubscribe<VideoStarted>(OnVideoStarted);
                bus.Unsubscribe<VideoStopped>(OnVideoStopped);
                bus.Unsubscribe<ProcedureCompleted>(OnProcedureCompleted);
                bus.Unsubscribe<StepStallDetected>(OnStall);
            }
            poseWriter.Dispose();
        }

        // ---------- 寫一行 ----------
        // 同一批黑板變更（一次 StateChanged）寫出的行共用同一個時間點與 seq
        bool inGroup;
        float groupT;
        long groupSeq;

        RecordLine Write(string type, string step, Dictionary<string, object> data, float? at = null)
        {
            long lineSeq = inGroup ? groupSeq : ++seq;
            var line = new RecordLine { t = at ?? (inGroup ? groupT : Now), seq = lineSeq, type = type, step = step ?? "", data = data ?? new Dictionary<string, object>() };
            lines.Add(line);
            writer.WriteLine(line.ToJson());
            return line;
        }

        string Held => state.Get(SimIds.ActiveTool);

        // ---------- 事件 ----------
        void OnStepStarted(StepStarted e)
        {
            if (e.isReplay) return;
            bool redo = stepFirstStart.ContainsKey(e.stepId ?? "");   // 同一步再開始一次＝從這一步重做
            if (!stepFirstStart.ContainsKey(e.stepId ?? "")) stepFirstStart[e.stepId ?? ""] = Now;
            if (redo) stepRedo[e.stepId] = (stepRedo.TryGetValue(e.stepId, out var n) ? n : 0) + 1;
            currentStep = e.stepId ?? "";
            currentSubs = (e.subActions ?? "").Split('\n').Where(x => x.Length > 0).Select(x => x.Split('\u001f')[0]).ToArray();
            lastSub = 0;
            Write("StepStarted", currentStep, new Dictionary<string, object>
            {
                { "name", catalog.NameOf(currentStep) }, { "redo", redo }, { "title", e.title ?? "" }, { "side", e.side ?? "" }, { "sideLabel", e.sideLabel ?? "" },
                { "index", e.index }, { "total", e.total }, { "runIndex", e.runIndex }, { "runTotal", e.runTotal },
                { "instruction", e.instruction ?? "" }, { "toolHint", e.toolHint ?? "" }, { "targetHint", e.targetHint ?? "" }, { "toolId", e.toolId ?? "" },
                { "text", e.text ?? "" }, { "why", e.why ?? "" }, { "checklist", e.checklist ?? "" }, { "subActions", e.subActions ?? "" },
                { "valueTags", e.valueTags ?? "" }, { "highlightIds", e.highlightIds ?? "" }, { "targetIds", e.targetIds ?? Array.Empty<string>() },
                { "showBoltNumbers", e.showBoltNumbers }, { "showOutwardArrow", e.showOutwardArrow }
            });
        }

        void OnStepCompleted(StepCompleted e)
        {
            if (e.isReplay) return;
            string id = e.stepId ?? "";
            float start = stepFirstStart.TryGetValue(id, out var s0) ? s0 : Now;
            int redo = stepRedo.TryGetValue(id, out var n) ? n : 0;
            Write("StepCompleted", id, new Dictionary<string, object>
            {
                { "name", catalog.NameOf(id) }, { "side", e.side ?? "" }, { "seconds", Now - start }, { "status", redo > 0 ? "重做" : "完成" }, { "redoCount", redo }
            });
        }

        void OnRequested(ActionRequested e)
        {
            if (e.isReplay) return;
            Write("ActionRequested", currentStep, new Dictionary<string, object>
            {
                { "kind", ActionRequest.KindOf(e.toolId ?? "", e.text ?? "").ToString() }, { "target", e.targetId ?? "" }, { "point", e.pointId ?? "" },
                { "tool", e.toolId ?? "" }, { "text", e.text ?? "" }, { "value", e.value }
            });
        }

        void OnPerformed(ActionPerformed e)
        {
            if (e.isReplay) return;
            Write("ActionPerformed", currentStep, new Dictionary<string, object>
            {
                { "kind", ActionRequest.KindOf(e.toolId ?? "", (e.text ?? "").Split(':')[0]).ToString() }, { "target", e.targetId ?? "" }, { "point", e.pointId ?? "" },
                { "tool", e.toolId ?? "" }, { "text", e.text ?? "" }, { "value", e.value }
            });
        }

        void OnBlocked(ActionBlocked e)
        {
            if (e.isReplay) return;
            string rule = e.text ?? "";
            Write("ActionBlocked", currentStep, new Dictionary<string, object>
            {
                { "kind", string.IsNullOrEmpty(e.toolId) ? "徒手" : "工具" }, { "target", e.targetId ?? "" }, { "tool", e.toolId ?? "" }, { "held", Held },
                { "rule", rule }, { "ruleName", ErrorNames.Of(rule) }, { "reason", lastErrorWhat.TryGetValue(rule, out var w) ? w : "" }
            });
        }

        void OnError(ErrorDetected e)
        {
            if (e.isReplay) return;
            lastErrorWhat[e.errorId ?? ""] = e.what ?? "";
            Write("ErrorDetected", string.IsNullOrEmpty(e.stepId) ? currentStep : e.stepId, new Dictionary<string, object>
            {
                { "errorId", e.errorId ?? "" }, { "name", ErrorNames.Of(e.errorId, e.what) }, { "severity", e.severity.ToString() },
                { "what", e.what ?? "" }, { "why", e.why ?? "" }, { "fix", e.fix ?? "" }, { "blocks", e.blocks }, { "target", e.targetId ?? "" },
                { "stepName", catalog.NameOf(string.IsNullOrEmpty(e.stepId) ? currentStep : e.stepId) }
            });
        }

        void OnToolSelected(ToolSelected e)
        {
            if (e.isReplay) return;
            Write("ToolSelected", currentStep, new Dictionary<string, object> { { "tool", e.toolId ?? "" }, { "name", e.text ?? "" } });
        }

        void OnPick(ToolPickRequested e)
        {
            if (e.isReplay) return;
            Write("ToolPickRequested", currentStep, new Dictionary<string, object> { { "slot", (int)e.value }, { "target", e.targetId ?? "" }, { "held", Held } });
        }

        void OnReturn(ToolReturnRequested e)
        {
            if (e.isReplay) return;
            Write("ToolReturnRequested", currentStep, new Dictionary<string, object> { { "slot", (int)e.value }, { "target", e.targetId ?? "" }, { "held", Held } });
        }

        void OnMeasured(MeasurementTaken e)
        {
            if (e.isReplay) return;
            bool zeroed = state.GetBool("tool.caliper.zeroed");
            Write("MeasurementTaken", currentStep, new Dictionary<string, object>
            {
                { "target", e.targetId ?? "" }, { "tool", e.toolId ?? "" }, { "value", e.value }, { "display", e.text ?? "" }, { "zeroed", zeroed },
                { "tag", "保養表值" }, { "standard", "≤ 3.0 mm 更換" }
            });
        }

        // R1 T3：面板位置（相對當時站點；重播照這個還原）
        void OnPanelMoved(PanelMoved e) =>
            Write("PanelMoved", currentStep, new Dictionary<string, object> { { "panelId", e.panelId ?? "" }, { "standPointId", e.standPointId ?? "" }, { "x", e.x }, { "y", e.y }, { "z", e.z }, { "yaw", e.yaw } });

        void OnRecordWritten(RecordWritten e)
        {
            if (e.isReplay) return;
            Write("RecordWritten", currentStep, new Dictionary<string, object> { { "field", e.pointId ?? "" }, { "value", e.text ?? "" }, { "target", e.targetId ?? "" } });
        }

        void OnUiCommand(UiCommandIssued e)
        {
            if (e.isReplay) return;
            Write("UiCommandIssued", string.IsNullOrEmpty(e.stepId) ? currentStep : e.stepId, new Dictionary<string, object> { { "name", e.name ?? "" }, { "source", e.text ?? "" } });
        }

        void OnHover(HoverChanged e)
        {
            if (e.isReplay || (e.targetId ?? "") == lastHover) return;
            lastHover = e.targetId ?? "";
            Write("HoverChanged", currentStep, new Dictionary<string, object> { { "target", lastHover }, { "source", e.text ?? "" } });
        }

        /// <summary>卡關（防護網偵測）：步驟、種類（missed_commit／rule_mismatch）、未成立的條件 ID、當時的黑板值、給學員的說明。不扣分。</summary>
        void OnStall(StepStallDetected e)
        {
            if (e.isReplay) return;
            Write("StepStallDetected", string.IsNullOrEmpty(e.stepId) ? currentStep : e.stepId, new Dictionary<string, object>
            {
                { "kind", e.kind ?? "" }, { "side", e.side ?? "" }, { "conditionIds", e.conditionIds ?? "" }, { "values", e.values ?? "" }, { "explain", e.explain ?? "" },
                { "stepName", catalog.NameOf(string.IsNullOrEmpty(e.stepId) ? currentStep : e.stepId) }
            });
        }

        void OnProcedureCompleted(ProcedureCompleted e)
        {
            if (e.isReplay) return;
            Write("ProcedureCompleted", "", new Dictionary<string, object> { { "procedureId", e.procedureId ?? "" } });
        }

        void OnVideoStarted(VideoStarted e)
        {
            if (e.isReplay) return;
            Write("VideoStarted", currentStep, new Dictionary<string, object> { { "file", Path.GetFileName(e.file ?? "") }, { "path", e.file ?? "" }, { "sessionTime", e.t - T0 } }, e.t - T0);
        }

        void OnVideoStopped(VideoStopped e)
        {
            if (e.isReplay) return;
            Write("VideoStopped", currentStep, new Dictionary<string, object> { { "file", Path.GetFileName(e.file ?? "") }, { "reason", e.reason ?? "" }, { "sessionTime", e.t - T0 } });
        }

        /// <summary>一次 StateChanged（一筆或一整批 Commit）＝同一個時間點、同一個 seq；批次裡的連續感測值不節流（與同批的其他鍵一起寫出）。</summary>
        void OnState(StateChanged e)
        {
            if (e.isReplay) return;
            var changes = e.Changes;
            if (changes.Count == 0) return;
            inGroup = true; groupT = Now; groupSeq = ++seq;
            try
            {
                foreach (var c in changes) OnChange(c.key ?? "", c.newValue, c.Value, changes.Count > 1);
            }
            finally { inGroup = false; }
        }

        void OnChange(string key, string text, float value, bool inBatch)
        {
            // 小動作完成（ProcedureRunner 推進 procedure.sub_index）
            if (key == "procedure.sub_index")
            {
                int idx = (int)value;
                for (int k = lastSub; k < idx; k++)
                    Write("SubActionCompleted", currentStep, new Dictionary<string, object> { { "index", k + 1 }, { "text", k < currentSubs.Length ? currentSubs[k] : "" } });
                if (idx < lastSub) Write("SubActionUndone", currentStep, new Dictionary<string, object> { { "index", idx + 1 }, { "text", idx < currentSubs.Length ? currentSubs[idx] : "" } });
                lastSub = idx;
                return;
            }
            // 工具架掛位（rack.slot_k）：拿起／歸還／使用中，附工具與掛位位置
            if (key.StartsWith("rack.slot_"))
            {
                int.TryParse(key.Substring("rack.slot_".Length), out int slot);
                var data = new Dictionary<string, object> { { "slot", slot }, { "state", text ?? "" }, { "tool", ToolInSlot(slot) } };
                if (Locator != null && Locator.TryGetPosition("env.tool_rack.slot_" + slot, out var p)) data["pos"] = new object[] { p.x, p.y, p.z };
                Write("ToolRackState", currentStep, data);
                return;
            }
            // 判定結果（作業單比對、判定選項、高低差）
            if (key == "workorder.decision" || key.EndsWith(".mismatch") && key.StartsWith("workorder.") || key == "workorder.height_diff" || key == "workorder.last_error")
            {
                Write("Judgement", currentStep, new Dictionary<string, object> { { "key", key }, { "value", text } });
                return;
            }
            // 錯誤數（只抄黑板上 Training 算好的值）與錯誤視窗開關，給重播顯示用
            if (key == "score.minor" || key == "score.major" || key == "score.critical" || key == "ui.error.active" || key == "training.paused")
            {
                Write("ScoreChanged", currentStep, new Dictionary<string, object> { { "key", key }, { "value", text } });
                return;
            }
            if (!StatePrefixes.Any(p => key.StartsWith(p))) return;
            // 連續變化的感測值（升降距離、安裝距離、扭力讀數）每幀都在變：最多 15 Hz，最後一個值一定會寫（見 FlushContinuous）
            if (ContinuousKey(key))
            {
                float now = Now;
                if (!inBatch && lastContinuous.TryGetValue(key, out var last) && now - last < 1f / TrackedRate) { pendingContinuous[key] = text; return; }
                lastContinuous[key] = now;
                pendingContinuous.Remove(key);
            }
            WriteState(key, text);
            // 升降：指令變化，以及放開（Up/Down → Stop）時的檢測點距離
            if (key == SimIds.LiftCmd)
            {
                string cmd = text ?? "";
                if ((lastLiftCmd == "Up" || lastLiftCmd == "Down") && cmd != lastLiftCmd)
                    Write("LiftReleased", currentStep, new Dictionary<string, object>
                    {
                        { "from", lastLiftCmd }, { "distanceCm", state.GetFloat(SimIds.PanHeadLowerDistance) }, { "toleranceCm", state.GetFloat(SimIds.PanHeadLowerTolerance, 1f) }, { "tag", "示例值" }
                    });
                lastLiftCmd = cmd;
            }
        }

        /// <summary>每幀都可能變的感測值鍵。</summary>
        public static bool ContinuousKey(string key) =>
            key.EndsWith("_distance_cm") || key.EndsWith("_delta_cm") || key == "tool.torque_wrench.current";

        void WriteState(string key, string value)
        {
            var d = new Dictionary<string, object> { { "key", key }, { "value", value } };
            var rule = ValueTagRules.Find(key);
            if (rule != null) { d["tag"] = rule.tag; d["label"] = rule.label; d["unit"] = rule.unit; }
            Write("StateChanged", currentStep, d);
        }

        /// <summary>把節流中、還沒寫的感測值寫出（force＝不管間隔，例：快照、結算、關閉前）。</summary>
        void FlushContinuous(bool force)
        {
            if (pendingContinuous.Count == 0) return;
            float now = Now;
            foreach (var key in pendingContinuous.Keys.ToList())
            {
                if (!force && lastContinuous.TryGetValue(key, out var last) && now - last < 1f / TrackedRate) continue;
                lastContinuous[key] = now;
                WriteState(key, pendingContinuous[key]);
                pendingContinuous.Remove(key);
            }
        }

        string ToolInSlot(int slot)
        {
            foreach (var entry in state.Get("tools.list").Split(';'))
            {
                var f = entry.Split('|');
                if (f.Length >= 3 && f[0] == slot.ToString()) return f[1];
            }
            return "";
        }

        void OnResult(SessionResult e)
        {
            if (e.isReplay || ended) return;
            FlushContinuous(true);
            ended = true;
            Write("SessionResult", currentStep, new Dictionary<string, object>
            {
                { "passed", e.passed }, { "score", e.score }, { "totalSeconds", e.totalSeconds }, { "minor", e.minor }, { "major", e.major }, { "critical", e.critical },
                { "incompleteSteps", e.incompleteSteps ?? Array.Empty<string>() }
            });
            FlushPoses();
            Snapshot(Now);
            var summary = SummaryBuilder.Build(Meta, lines, false);
            writer.WriteFile(RecordFiles.Summary, Json.Write(summary));
            SessionFinished?.Invoke(this);
        }

        // ---------- 位姿與快照（每幀由 PoseSampler 呼叫） ----------
        /// <summary>每幀呼叫：依頻率取樣頭與手（30 Hz）、ReplayTracked（15 Hz，移動 &gt; 1 mm 或轉 &gt; 0.5° 才記）、數值通道；每 1 s 交出一塊、每 10 s 一筆快照。</summary>
        public void Sample(IPoseSource pose, IEnumerable<KeyValuePair<string, PoseSample>> tracked, IEnumerable<KeyValuePair<string, float>> values)
        {
            if (!begun || disposed) return;
            float t = Now;
            if (pose != null && pose.PoseAvailable && t >= nextHead)
            {
                nextHead = t + 1f / HeadRate;
                WritePose(RecordFiles.Head, t, pose.Head.position, pose.Head.rotation, true);
                if (pose.HasHands)
                {
                    WritePose(RecordFiles.LeftHand, t, pose.LeftHand.position, pose.LeftHand.rotation, true);
                    WritePose(RecordFiles.RightHand, t, pose.RightHand.position, pose.RightHand.rotation, true);
                }
            }
            if (t >= nextTracked)
            {
                nextTracked = t + 1f / TrackedRate;
                if (tracked != null)
                    foreach (var kv in tracked)
                    {
                        if (lastTracked.TryGetValue(kv.Key, out var last) && last.active == kv.Value.active
                            && (last.pos - kv.Value.pos).sqrMagnitude <= MoveThreshold * MoveThreshold && Quaternion.Angle(last.rot, kv.Value.rot) <= AngleThreshold) continue;
                        // 靜止一段時間後又開始動：先補一筆「動之前」的位置，重播時才不會把靜止那段內插成慢慢移動
                        if (lastTracked.TryGetValue(kv.Key, out var prev) && t - prev.t > 2f / TrackedRate) WritePose(kv.Key, t - 1f / TrackedRate, prev.pos, prev.rot, prev.active);
                        var s = kv.Value; s.t = t;
                        lastTracked[kv.Key] = s;
                        WritePose(kv.Key, t, s.pos, s.rot, s.active);
                    }
                if (values != null)
                    foreach (var kv in values)
                    {
                        if (lastValues.TryGetValue(kv.Key, out var v) && Mathf.Abs(v - kv.Value) < 1e-4f) continue;
                        lastValues[kv.Key] = kv.Value;
                        ushort vid = IdOf(kv.Key);   // 第一次出現時先寫 ID 定義
                        PoseFile.WriteValue(poseWriter, vid, t, kv.Value);
                    }
            }
            Tick(t);
        }

        /// <summary>沒有場景（測試）時推進時間：每 1 s 交出位姿塊、每 10 s 一筆快照。</summary>
        public void Tick(float t)
        {
            if (!begun || disposed) return;
            FlushContinuous(false);
            if (t - lastChunk >= 1f) { lastChunk = t; FlushPoses(); }
            if (t - lastSnapshot >= SnapshotInterval) Snapshot(t);
        }

        void WritePose(string id, float t, Vector3 p, Quaternion r, bool active)
        {
            ushort idx = IdOf(id);
            PoseFile.WritePose(poseWriter, idx, t, p, r, active);
        }

        ushort IdOf(string id)
        {
            if (poseIds.TryGetValue(id, out var idx)) return idx;
            idx = (ushort)poseIds.Count;
            poseIds[id] = idx;
            PoseFile.WriteDefine(poseWriter, idx, id);
            return idx;
        }

        void FlushPoses()
        {
            poseWriter.Flush();
            if (poseBuffer.Length == 0) return;
            writer.WritePoseChunk(poseBuffer.ToArray());
            poseBuffer.SetLength(0);
        }

        void Snapshot(float t)
        {
            FlushContinuous(true);   // 快照前把節流中的值寫出：快照之前的行與快照一致
            lastSnapshot = t;
            var snap = new Snapshot { t = t, lastSeq = seq, state = state.Snapshot() };
            if (TrackedProvider != null) foreach (var kv in TrackedProvider()) snap.tracked[kv.Key] = kv.Value;
            writer.WriteSnapshot(snap);
        }
    }
}
