using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Recording;
using PantographSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// 紀錄功能的實機檢查（Editor 開著時從外部觸發）：進入 Play → 任務卡填學員名稱、按開始（練習模式）→
    /// 用與介面相同的事件把 25 步做完（中途故意犯一次 E1）→ 確認 Recordings/&lt;yyyyMMdd_HHmmss_學員&gt;/ 的檔案 →
    /// 截圖紀錄清單（Logs/rig_records_list.png）→ 進重播、拖到錯誤附近截圖時間軸（Logs/rig_replay_timeline.png）→ 離開 Play。
    /// 觸發：建立 Logs/rec-check.request，內容一行一個參數：
    ///   mode=full|quick（quick＝只開始一場、確認資料夾後離開）
    ///   pace=秒（full 時把整場拉長到大約這個秒數，給錄影檢查用；扭力步驟放在最後一分鐘）
    ///   video=1（開始時就按 F9 開始錄影，結束時停止）；videostop=play（錄影中直接離開 Play，不先停止）
    ///   replay=0（不進重播）
    ///   until=步驟 ID（例 STP_CSR_11_Install_L；開頭相符就算）：走到那一步就停，工具收回原位、保持 Play 交人接手；這之前的操作標成測試驅動，不計成績、不進報表
    ///   tutorial=1（教學模式；沒給就是練習）；exit=1（到 until 後離開 Play，檢查用）
    /// 結果寫到 Logs/rec-check.txt。
    /// </summary>
    [InitializeOnLoad]
    public static class RecordingCheckDriver
    {
        const string RequestPath = "Logs/rec-check.request";
        const string SessionKey = "PantographSim.RecCheckPending";
        const string ArgsKey = "PantographSim.RecCheckArgs";
        public const string Trainee = "驗證學員";
        static IEnumerator routine;
        static int waitUntilFrame, lastFrame;
        static double nextPoll;
        static readonly StringBuilder log = new StringBuilder();
        static readonly List<string> errors = new List<string>();
        static Dictionary<string, string> args = new Dictionary<string, string>();
        static float pace;          // 每步之間額外等待（秒）
        static bool stayInPlay;     // 交給人接手：走完不離開 Play
        static float tStart;

        /// <summary>每幀的 unscaledDeltaTime（錄影與不錄影比較用）。</summary>
        static readonly List<float> frameTimes = new List<float>();

        static RecordingCheckDriver()
        {
            EditorApplication.update += Tick;
        }

        static string Arg(string k, string fallback = "") => args.TryGetValue(k, out var v) ? v : fallback;

        static void Tick()
        {
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.timeSinceStartup > nextPoll)
            {
                nextPoll = EditorApplication.timeSinceStartup + 2.0;
                if (File.Exists(RequestPath) && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
                {
                    string text = File.ReadAllText(RequestPath);
                    File.Delete(RequestPath);
                    var active = EditorSceneManager.GetActiveScene();   // 不替人存場景：進 Play 不需要，未存的變動留給開場景的人決定
                    if (active.path != SliceSceneBuilder.ScenePath)
                    {
                        if (active.isDirty && !string.IsNullOrEmpty(active.path)) EditorSceneManager.SaveScene(active);
                        EditorSceneManager.OpenScene(SliceSceneBuilder.ScenePath);
                    }
                    SessionState.SetString(ArgsKey, text);
                    SessionState.SetBool(SessionKey, true);
                    EditorApplication.EnterPlaymode();
                }
                return;
            }
            if (!EditorApplication.isPlaying) return;
            if (routine == null && SessionState.GetBool(SessionKey, false))
            {
                SessionState.SetBool(SessionKey, false);
                args = SessionState.GetString(ArgsKey, "").Split('\n').Select(l => l.Trim()).Where(l => l.Contains("="))
                    .ToDictionary(l => l.Substring(0, l.IndexOf('=')), l => l.Substring(l.IndexOf('=') + 1));
                log.Clear(); errors.Clear(); frameTimes.Clear(); stayInPlay = false;
                routine = Run();
                waitUntilFrame = Time.frameCount + 5;
            }
            if (routine == null) return;
            if (Time.frameCount != lastFrame) { lastFrame = Time.frameCount; frameTimes.Add(Time.unscaledDeltaTime); }   // 只算遊戲幀
            if (Time.frameCount < waitUntilFrame) return;
            try
            {
                if (routine.MoveNext())
                {
                    waitUntilFrame = Time.frameCount + Math.Max(1, routine.Current is int n ? n : 1);
                    if (ReachedStep(S?.Get("procedure.current"), Arg("until"))) { args.Remove("until"); routine = HandOff(); }
                }
                else Finish(!stayInPlay);
            }
            catch (Exception e)
            {
                log.AppendLine("例外：" + e);
                Finish(true);
            }
        }

        /// <summary>走到 until 指定的步驟了沒。</summary>
        public static bool ReachedStep(string current, string until) => !string.IsNullOrEmpty(until) && !string.IsNullOrEmpty(current) && current.StartsWith(until);

        /// <summary>交給人接手：手上的工具收回原位、面板關掉，之後的操作才算數。</summary>
        static IEnumerator HandOff()
        {
            Return();
            yield return 10;
            if (S.GetBool("ui.workorder_open")) Send(SimIds.WorkOrder, "toggle");
            Send("ui.tutorial", "close");
            Send("xr.tutorial", "skip");   // 開場控制器教學不留在眼前
            Sim.Bus.Publish(new FocusRequested { time = SimClock.Now });   // 人轉向這一步的目標（桌面是鏡頭）
            yield return 40;
            S.Set("ui.toast", "");
            Sim.Bus.Publish(new TestDriverHandoff { tag = DriverTag, time = SimClock.Now });
            Note("交給人接手");
            stayInPlay = Arg("exit") != "1";
        }

        static string DriverTag => PantographSim.Data.ToolRulesDef.Load().testDriverTag;

        static void Finish(bool exit)
        {
            routine = null;
            PointerState.ScriptedRay = null;
            PointerState.XrTriggerHeld = false;
            if (Sim.State != null) LiftControl.Release(Sim.State);
            log.AppendLine(errors.Count == 0 ? "過程中沒有錯誤事件。" : "錯誤事件：\n" + string.Join("\n", errors));
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/rec-check.txt", log.ToString());
            Debug.Log("[RecordingCheckDriver] 完成\n" + log);
            if (exit) EditorApplication.ExitPlaymode();
        }

        // ---------- 玩家動作（與介面／場景點擊相同的事件） ----------
        static ISimState S => Sim.State;
        static void Send(string target, string text, string pointId = "") =>
            Sim.Bus.Publish(new ActionRequested { targetId = target, text = text, pointId = pointId, toolId = "", time = SimClock.Now });
        static void Click(string target, string tool = "", string verb = "click") =>
            Sim.Bus.Publish(new ActionRequested { targetId = target, toolId = tool, text = verb, time = SimClock.Now });

        static int SlotOf(string toolId)
        {
            foreach (var entry in S.Get("tools.list").Split(';'))
            {
                var f = entry.Split('|');
                if (f.Length >= 2 && f[1] == toolId && int.TryParse(f[0], out int k)) return k;
            }
            return 0;
        }

        static void Pick(string toolId)
        {
            int k = SlotOf(toolId);
            Sim.Bus.Publish(new ToolPickRequested { targetId = "env.tool_rack.slot_" + k, value = k, time = SimClock.Now });
            Send("ui.tutorial", "close");
        }

        static void Return()
        {
            string active = S.Get(SimIds.ActiveTool);
            if (string.IsNullOrEmpty(active)) return;
            int k = SlotOf(active);
            Sim.Bus.Publish(new ToolReturnRequested { targetId = "env.tool_rack.slot_" + k, value = k, time = SimClock.Now });
        }

        static string R(string key) => S.GetFloat(key).ToString("0.00", CultureInfo.InvariantCulture);
        static float Elapsed => Time.realtimeSinceStartup - tStart;

        static void Note(string what) =>
            log.AppendLine($"[{Elapsed,6:0.0}s] {what}　步驟={S.Get("procedure.current")}　小動作={S.GetFloat("procedure.sub_index"):0}/{S.GetFloat("procedure.sub_count"):0}" +
                           $"　手上={S.Get(SimIds.ActiveTool)}　錯誤數 M{S.GetFloat("score.minor"):0}/J{S.GetFloat("score.major"):0}/C{S.GetFloat("score.critical"):0}");

        static IEnumerable<int> WaitStep(string runIdPrefix, float maxSeconds = 15f)
        {
            float until = Time.realtimeSinceStartup + maxSeconds;
            while (Time.realtimeSinceStartup < until && !S.Get("procedure.current").StartsWith(runIdPrefix) && !S.GetBool("procedure.finished")) yield return 5;
            if (!S.Get("procedure.current").StartsWith(runIdPrefix) && !S.GetBool("procedure.finished"))
                log.AppendLine($"⚠ 等不到步驟 {runIdPrefix}（目前 {S.Get("procedure.current")}，小動作 {S.GetFloat("procedure.sub_index"):0}「{S.Get("hint.sub_text")}」，閘門：{S.Get("procedure.gate_reason")}）");
        }

        static IEnumerable<int> Sec(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return 1;
        }

        static IEnumerable<int> Pace() => Sec(pace);

        // ---------- 主流程 ----------
        static IEnumerator Run()
        {
            while (!Sim.Ready || SliceUI.Instance == null || RecordingService.Instance == null) yield return 5;
            tStart = Time.realtimeSinceStartup;
            Sim.Bus.Subscribe<ErrorDetected>(e => errors.Add($"{S.Get("procedure.current")}：{e.errorId}（{e.severity}）{e.what}"));
            yield return 20;
            string mode = Arg("mode", "full");
            if (args.ContainsKey("source")) EditorPrefs.SetString("PantographSim.VideoSource", Arg("source"));
            // split=秒：測試自動切檔（沒給就清掉，回到 10 分鐘）
            if (args.ContainsKey("split")) EditorPrefs.SetFloat("PantographSim.VideoSplitSeconds", float.Parse(Arg("split"), CultureInfo.InvariantCulture));
            else EditorPrefs.DeleteKey("PantographSim.VideoSplitSeconds");
            float target = float.TryParse(Arg("pace", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0f;
            log.AppendLine($"模式 {mode}　目標長度 {target:0} s　錄影 {Arg("video", "0")}　紀錄根目錄 {RecordingService.Instance.Root}");

            Send("rec", "trainee:" + (args.ContainsKey("until") ? DriverTag : Trainee));
            Send("app.start", (Arg("tutorial") == "1" ? "tutorial" : "practice") + ":SCN_Default");
            yield return 30;
            string folder = S.Get("rec.folder");
            log.AppendLine("紀錄資料夾：" + folder);
            log.AppendLine($"● 紀錄中：{S.GetBool("rec.active")}　訓練模式：{S.Get("training.mode")}");
            bool nameOk = Path.GetFileName(folder).EndsWith("_" + (args.ContainsKey("until") ? DriverTag : Trainee)) && System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(folder), @"^\d{8}_\d{6}_");
            bool rootOk = Path.GetFullPath(folder).StartsWith(Path.GetFullPath("Recordings"), StringComparison.OrdinalIgnoreCase);
            log.AppendLine($"{(nameOk ? "✔" : "✘")} 資料夾名稱 yyyyMMdd_HHmmss_學員　{(rootOk ? "✔" : "✘")} 在專案根目錄 Recordings/ 底下（不在 Assets）");
            foreach (var x in Sec(1.5f)) yield return x;
            ListFolder(folder);

            if (Arg("video") == "1")
            {
                VideoCaptureService.Toggle("key.F9", "start");
                yield return 30;
                log.AppendLine($"開始錄影：{S.Get(VideoCapture.FileKey)}　{S.Get(VideoCapture.MessageKey)}");
            }
            if (mode == "quick")
            {
                float secs = float.TryParse(Arg("seconds", "3"), NumberStyles.Float, CultureInfo.InvariantCulture, out var q) ? q : 3f;
                frameTimes.Clear();
                // 每 5 s 一聲扭力喀聲＋嗶聲（檢查影片有聲音），同時把降弓按住，畫面有在動
                float end = Time.realtimeSinceStartup + secs, nextClick = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < end)
                {
                    LiftControl.Send(S, "Down");
                    if (Time.realtimeSinceStartup >= nextClick)
                    {
                        PantographSim.Tools.ToolInventory.PlayClick();
                        log.AppendLine($"[{Elapsed,6:0.0}s] 喀＋嗶　影片時間≈{VideoSeconds():0.000}");
                        nextClick += 5f;
                    }
                    yield return 1;
                }
                LiftControl.Release(S);
                WriteFrameTimes(Arg("video") == "1" ? "quick_video_on" : "quick_video_off");
                if (Arg("video") == "1" && Arg("videostop") != "play")
                {
                    VideoCaptureService.Toggle("key.F9", "stop");
                    yield return 30;
                    log.AppendLine($"停止錄影：{S.Get(VideoCapture.LastKey)}　訊息：{S.Get(VideoCapture.MessageKey)}");
                }
                else if (Arg("video") == "1") log.AppendLine("錄影中直接離開 Play（不先停止）");
                yield break;   // 離開 Play → 標成中斷
            }

            // 拉長成 target 秒：27 個停頓點平均分配；扭力兩步放到最後一分鐘
            pace = target > 0 ? Mathf.Max(0f, (target - 75f) / 27f) : 0f;
            foreach (var x in Procedure(target)) yield return x;

            foreach (var x in Sec(2f)) yield return x;
            Note($"結算：finished={S.GetBool("score.finished")}　分數 {S.GetFloat("score.total"):0}　通過 {S.GetBool("score.passed")}");
            log.AppendLine($"報表：{S.Get("rec.report")}");
            ListFolder(folder);

            if (Arg("video") == "1")
            {
                if (Arg("videostop") == "play") { log.AppendLine("錄影中直接離開 Play（不先停止）"); WriteFrameTimes("video_on"); yield break; }
                VideoCaptureService.Toggle("key.F9", "stop");
                yield return 60;
                log.AppendLine($"停止錄影：{S.Get(VideoCapture.LastKey)}");
            }
            WriteFrameTimes(Arg("video") == "1" ? "video_on" : "video_off");

            // 紀錄清單
            Send("rec", "list.open");
            yield return 30;
            log.AppendLine($"紀錄清單 {S.GetFloat("rec.list_count"):0} 筆；第一筆：{S.Get("rec.list").Split('\n').FirstOrDefault()?.Replace("\u001f", " | ")}");
            foreach (var x in Capture("rig_records_list")) yield return x;
            Send("rec", "list.close");
            yield return 10;
            if (Arg("replay", "1") == "0") yield break;

            // 重播：場景重新載入（RecordingService 會先結束這一場）
            Send("rec", "replay:" + folder);
            for (int i = 0; i < 400 && (ReplayPlayer.Instance == null || ReplayPlayer.Instance.Timeline == null || SliceUI.Instance == null || !Sim.Ready); i++) yield return 5;
            if (ReplayPlayer.Instance?.Timeline == null) { log.AppendLine("✘ 進不了重播"); yield break; }
            int live = 0, replayed = 0;
            Sim.Bus.Subscribe<ErrorDetected>(e => { if (e.isReplay) replayed++; else live++; });
            var tl = ReplayPlayer.Instance.Timeline;
            log.AppendLine($"重播載入：{tl.lines.Count} 行、{tl.snapshots.Count} 筆快照、長度 {tl.Duration:0.0} s；錯誤標記 {tl.Errors.Count} 個");
            Send("replay", "pause");
            Send("replay", "speed:4");
            Send("replay", "play");
            foreach (var x in Sec(4f)) yield return x;
            // 拖到整場的 55%（肘側扭力附近）：時間軸前段的紅色錯誤標記與進度都看得到
            double seekTo = tl.Duration * 0.55;
            Send("replay", "seek:" + seekTo.ToString("0.###", CultureInfo.InvariantCulture));
            Send("replay", "pause");
            yield return 40;
            log.AppendLine($"拖曳到 {seekTo:0.0} s：replay.time={S.GetFloat("replay.time"):0.0}　步驟={S.Get("procedure.current")}");
            foreach (var x in Capture("rig_replay_timeline")) yield return x;
            Send("replay", "seek:" + (tl.Duration * 0.7).ToString("0.###", CultureInfo.InvariantCulture));
            Send("replay", "play");
            foreach (var x in Sec(3f)) yield return x;
            log.AppendLine($"{(live == 0 ? "✔" : "✘")} 重播中 isReplay=false 的 ErrorDetected：{live} 個（重播重發 {replayed} 個）");
            log.AppendLine($"重播中 score.total={S.GetFloat("score.total"):0}（重播不產生分數）");
        }

        /// <summary>練習模式照標準做完 25 步（SI_01 先故意在升弓時碰螺栓＝E1，選從這一步重做）。</summary>
        static IEnumerable<int> Procedure(float target)
        {
            // ---- E1：還在升弓就拿套筒去碰螺栓 ----
            Pick(SimIds.ToolSocket);
            yield return 10;
            Click("ip.carbon_strip_L.bolt_1", SimIds.ToolSocket);
            yield return 20;
            Note($"故意 E1（錯誤視窗 {S.Get("ui.error.id")}）");
            Send("ui.error", "restart");
            yield return 15;
            Return();
            yield return 10;
            foreach (var x in Pace()) yield return x;

            // ---- SI_01 ----
            float until = Time.realtimeSinceStartup + 90f;   // 依時間等（Editor 幀率不固定）
            while (!SimIds.PanHeadLowered(S) && Time.realtimeSinceStartup < until) { LiftControl.Send(S, "Down"); yield return 2; }
            LiftControl.Release(S);
            yield return 30;
            Click("part.pan_head", "", "inspect");
            yield return 10;
            Click("ip.cab.panel");
            yield return 10;
            Send("ip.cab.pan_indicator", "check");
            yield return 10;
            Send("ip.cab.panel", "close");
            foreach (var x in WaitStep("STP_SI_04")) yield return x;
            Note("SI_01 通過");
            foreach (var x in Pace()) yield return x;

            // ---- SI_04 驗電 ----
            Pick(SimIds.ToolVoltageDetector);
            yield return 10;
            Click(SimIds.VoltageTester, SimIds.ToolVoltageDetector);
            yield return 10;
            Click(SimIds.GroundClamp, SimIds.ToolVoltageDetector);
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_SI_05")) yield return x;
            foreach (var x in Pace()) yield return x;

            // ---- SI_05 接地 ----
            Pick(SimIds.ToolGroundingRod);
            yield return 10;
            Click(SimIds.GroundTerminal, SimIds.ToolGroundingRod);
            yield return 5;
            Send(SimIds.ToolGroundingRod, "pulltest");
            yield return 5;
            Click(SimIds.GroundClamp, SimIds.ToolGroundingRod);
            foreach (var x in WaitStep("STP_CSR_01")) yield return x;
            Note("SI_05 接地完成");
            foreach (var x in Pace()) yield return x;

            // ---- CSR_01–05 ----
            if (!S.GetBool("ui.workorder_open")) Send(SimIds.WorkOrder, "toggle");
            foreach (var x in WaitStep("STP_CSR_02")) yield return x;
            Pick(SimIds.ToolCaliper);
            yield return 10;
            Send(SimIds.ToolCaliper, "close");
            Send(SimIds.ToolCaliper, "zero");
            foreach (var x in WaitStep("STP_CSR_03")) yield return x;
            foreach (var x in Pace()) yield return x;
            foreach (var side in new[] { "L", "R" })
            {
                foreach (var pt in new[] { "left", "mid", "right" }) { Click($"ip.carbon_strip_{side}.measure_{pt}", SimIds.ToolCaliper); yield return 10; }
                foreach (var pt in new[] { "left", "mid", "right" }) Send(SimIds.WorkOrder, R($"ip.carbon_strip_{side}.measure_{pt}.reading"), $"reading.{side}.{pt}");
                yield return 5;
                Send(SimIds.WorkOrder, "true", "submit." + side);
                yield return 10;
                foreach (var x in Pace()) yield return x;
            }
            Return();
            foreach (var x in WaitStep("STP_CSR_05")) yield return x;
            Send(SimIds.WorkOrder, "both", "decision");
            foreach (var x in WaitStep("STP_CSR_06")) yield return x;
            if (S.GetBool("ui.workorder_open")) Send(SimIds.WorkOrder, "toggle");
            yield return 10;
            Note("CSR_05 判定兩條都換");

            // ---- 兩側：CSR_06–12，再兩側 CSR_13–14（扭力放到最後一分鐘） ----
            foreach (var x in ReplaceSide("L")) yield return x;
            foreach (var x in TorqueAndMark("L", target)) yield return x;
            foreach (var x in ReplaceSide("R")) yield return x;
            foreach (var x in TorqueAndMark("R", 0)) yield return x;

            // ---- CSR_15 高低差 ----
            Pick(SimIds.ToolCaliper);
            yield return 10;
            foreach (var side in new[] { "L", "R" }) { Click($"ip.carbon_strip_{side}.measure_mid", SimIds.ToolCaliper); yield return 10; }
            if (!S.GetBool("ui.workorder_open")) Send(SimIds.WorkOrder, "toggle");
            foreach (var side in new[] { "L", "R" }) Send(SimIds.WorkOrder, R($"ip.carbon_strip_{side}.measure_mid.new_reading"), "newmid." + side);
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_WC_01")) yield return x;
            Note("CSR_15 高低差");

            // ---- WC_01 清點 ----
            Click("env.tool_rack");
            yield return 5;
            Click(SimIds.PartsTray);
            yield return 5;
            Send(SimIds.WorkOrder, "true", "toolcount_confirmed");
            yield return 10;
            if (S.GetBool("ui.workorder_open")) Send(SimIds.WorkOrder, "toggle");
            foreach (var x in WaitStep("STP_WC_03")) yield return x;

            // ---- WC_03 拆接地（徒手：先弓頭、再端子，接地棒掛回第 3 格） ----
            Click(SimIds.GroundClamp);
            yield return 10;
            Click(SimIds.GroundTerminal);
            yield return 10;
            int slot = SlotOf(SimIds.ToolGroundingRod);
            Sim.Bus.Publish(new ToolReturnRequested { targetId = "env.tool_rack.slot_" + slot, value = slot, time = SimClock.Now });
            for (int i = 0; i < 200 && !S.GetBool("score.finished"); i++) yield return 5;
            Note("WC_03 拆接地");
        }

        static IEnumerable<int> ReplaceSide(string side)
        {
            string b(int n) => $"ip.carbon_strip_{side}.bolt_{n}";
            foreach (var x in WaitStep("STP_CSR_06")) yield return x;
            Pick(SimIds.ToolSocket);
            yield return 10;
            Send(SimIds.ToolSocket, "size:13");
            Send(SimIds.ToolSocket, "dir:loosen");
            yield return 10;
            for (int n = 1; n <= 4; n++) { Sim.Perform(SimIds.ToolSocket, b(n), "rotate:13:loosen", 180); yield return 3; }
            foreach (var x in WaitStep("STP_CSR_07")) yield return x;
            for (int n = 1; n <= 4; n++)
            {
                Sim.Perform(SimIds.ToolSocket, b(n), "rotate:13:loosen", 600); yield return 3;
                Click(b(n), SimIds.ToolSocket); yield return 3;
            }
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_CSR_08")) yield return x;
            foreach (var x in Pace()) yield return x;
            Send("part.carbon_strip_" + side, "remove:vertical");
            yield return 15;
            Click(SimIds.PartsTray);
            foreach (var x in WaitStep("STP_CSR_09")) yield return x;
            Pick(SimIds.ToolCloth);
            yield return 10;
            for (float u = 0; u <= 1.001f; u += 0.05f)
            {
                Sim.Perform(SimIds.ToolCloth, $"ip.carbon_strip_{side}.bracket", $"clean:{u.ToString(CultureInfo.InvariantCulture)},0.2");
                Sim.Perform(SimIds.ToolCloth, $"ip.carbon_strip_{side}.bracket", $"clean:{u.ToString(CultureInfo.InvariantCulture)},0.8");
            }
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_CSR_10")) yield return x;
            foreach (var x in Pace()) yield return x;
            foreach (var s in new[] { 1, 2, 3 }) { Click($"env.spare_shelf.slot_{s}"); yield return 10; }
            Send(S.Get("spare.good_slot"), "flip");
            yield return 10;
            Pick(SimIds.ToolCaliper);
            yield return 10;
            Click(S.Get("spare.good_slot"), SimIds.ToolCaliper);
            yield return 10;
            Return();
            yield return 5;
            Send(S.Get("spare.good_slot"), "pick");
            foreach (var x in WaitStep("STP_CSR_11")) yield return x;
            if (Sim.Registry.TryGetPosition($"ip.carbon_strip_{side}.bracket", out var bracket))
                PointerState.ScriptedRay = new Ray(bracket + Vector3.up * 2f, Vector3.down);
            yield return 30;
            Send("carried", "rotate180");
            yield return 5;
            Click($"ip.carbon_strip_{side}.bracket");
            yield return 5;
            PointerState.ScriptedRay = null;
            foreach (var x in WaitStep("STP_CSR_12")) yield return x;
            foreach (var x in Pace()) yield return x;
            Pick(SimIds.ToolSocket);
            yield return 10;
            for (int n = 1; n <= 4; n++) { Click(b(n), SimIds.ToolSocket); yield return 3; }
            Send(SimIds.ToolSocket, "dir:lock");
            yield return 5;
            for (int n = 1; n <= 4; n++) { Sim.Perform(SimIds.ToolSocket, b(n), "rotate:13:lock", 360); yield return 3; }
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_CSR_13")) yield return x;
            Note($"CSR_06–12 {side} 完成");
        }

        /// <summary>扭力：真的按住（XR 扳機）讓扭力每秒 +8 N·m 上升，到 25 N·m 喀一聲、扳手閃色就放開。target&gt;0 時先等到整場只剩約 60 s。</summary>
        static IEnumerable<int> TorqueAndMark(string side, float target)
        {
            if (target > 0)
            {
                float wait = target - 60f - Elapsed;
                if (wait > 0) { log.AppendLine($"等 {wait:0} s，讓扭力步驟落在最後一分鐘"); foreach (var x in Sec(wait)) yield return x; }
            }
            Pick(SimIds.ToolTorque);
            yield return 10;
            Send(SimIds.ToolTorque, "set:25");
            yield return 5;
            for (int n = 1; n <= 4; n++)
            {
                string bolt = $"ip.carbon_strip_{side}.bolt_{n}";
                PointerState.XrTriggerHeld = true;
                Click(bolt, SimIds.ToolTorque);
                yield return 1;
                float limit = Time.realtimeSinceStartup + 10f;   // 每秒 +8 N·m：依時間等，不依幀數
                while (!S.GetBool("tool.torque_wrench.reached") && Time.realtimeSinceStartup < limit) yield return 1;
                log.AppendLine($"[{Elapsed,6:0.0}s] 喀（{side} 螺栓 {n}）flash_time={S.GetFloat("tool.torque_wrench.flash_time"):0.000}　影片時間≈{VideoSeconds():0.000}");
                PointerState.XrTriggerHeld = false;
                yield return 6;
            }
            Return();
            foreach (var x in WaitStep("STP_CSR_14")) yield return x;
            Pick(SimIds.ToolMarker);
            yield return 10;
            for (int n = 1; n <= 4; n++) { Click($"ip.carbon_strip_{side}.bolt_{n}", SimIds.ToolMarker); yield return 3; }
            yield return 5;
            Return();
            yield return 10;
            Note($"CSR_13–14 {side} 完成");
        }

        /// <summary>從開始錄影起經過的秒數（對照影片內的時間）。</summary>
        static float VideoSeconds() => S.GetBool(VideoCapture.ActiveKey) ? Time.realtimeSinceStartup - S.GetFloat(VideoCapture.StartedKey) : -1f;

        static void WriteFrameTimes(string name)
        {
            if (frameTimes.Count < 10) return;
            var sorted = frameTimes.Skip(30).OrderBy(x => x).ToList();
            if (sorted.Count == 0) return;
            float avg = sorted.Average(), p95 = sorted[(int)(sorted.Count * 0.95f)], max = sorted.Last();
            log.AppendLine($"影格時間（{name}）：{sorted.Count} 幀　平均 {avg * 1000:0.0} ms　P95 {p95 * 1000:0.0} ms　最大 {max * 1000:0.0} ms");
            File.WriteAllLines($"Logs/frametimes_{name}.txt", frameTimes.Select(x => (x * 1000).ToString("0.00", CultureInfo.InvariantCulture)));
        }

        static void ListFolder(string folder)
        {
            if (!Directory.Exists(folder)) { log.AppendLine("✘ 資料夾不存在：" + folder); return; }
            log.AppendLine("資料夾內容：" + string.Join("、", Directory.GetFiles(folder).Select(f => $"{Path.GetFileName(f)}({new FileInfo(f).Length} B)")));
        }

        /// <summary>主攝影機＋介面輸出 1920×1080（與 UiCaptureDriver 相同的方式）。</summary>
        static IEnumerable<int> Capture(string name)
        {
            var cam = Camera.main;
            var ui = SliceUI.Instance;
            if (cam == null || ui == null) { log.AppendLine("無法截圖：" + name); yield break; }
            var rt = new RenderTexture(1920, 1080, 24);
            ui.BeginCapture(cam, rt);
            yield return 4;
            ui.RefreshAll();
            Canvas.ForceUpdateCanvases();
            ui.RenderCapture();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            ui.EndCapture();
            File.WriteAllBytes($"Logs/{name}.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            log.AppendLine("截圖：Logs/" + name + ".png");
            yield return 2;
        }
    }
}
