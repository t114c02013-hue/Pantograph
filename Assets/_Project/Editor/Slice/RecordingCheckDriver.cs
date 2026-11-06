using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Recording;
using PantographSim.Training;
using PantographSim.UI;
using static PantographSim.Training.ProcedureAutoRun;
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
    ///   xr=off（這次 Play 走桌機模式：只設這個 Editor 程序的 PANTO_DESKTOP，不改專案檔，離開 Play 自動恢復）
    /// 結果寫到 Logs/rec-check.txt。
    /// </summary>
    [InitializeOnLoad]
    public static class RecordingCheckDriver
    {
        const string RequestPath = "Logs/rec-check.request";
        const string SessionKey = "PantographSim.RecCheckPending";
        const string ArgsKey = "PantographSim.RecCheckArgs";
        const string DesktopKey = "PantographSim.RecCheckDesktop";
        public const string Trainee = "驗證學員";
        static IEnumerator routine;
        static int waitUntilFrame, lastFrame;
        static double nextPoll;
        static readonly StringBuilder log = new StringBuilder();
        static readonly List<string> errors = new List<string>();
        static Dictionary<string, string> args = new Dictionary<string, string>();
        const float VrWaitSeconds = 6f;   // 比 XrModeSwitch 等頭盔的 4 秒長（ProcedureAutoRun.GoTo 同）
        static bool stayInPlay;     // 交給人接手：走完不離開 Play
        static ProcedureAutoRun auto;

        /// <summary>每幀的 unscaledDeltaTime（錄影與不錄影比較用）。</summary>
        static readonly List<float> frameTimes = new List<float>();

        static RecordingCheckDriver()
        {
            EditorApplication.update += Tick;
        }

        static string Arg(string k, string fallback = "") => args.TryGetValue(k, out var v) ? v : fallback;

        /// <summary>request 內容有 xr=off。</summary>
        public static bool WantsDesktop(string request) => (request ?? "").Split('\n').Any(l => l.Trim() == "xr=off");

        /// <summary>xr 選項要設的環境變數；沒有選項是 null。</summary>
        public static string XrEnv(string request) =>
            WantsDesktop(request) ? "PANTO_DESKTOP" : null;

        static void Tick()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetString(DesktopKey, "") != "")
            {
                Environment.SetEnvironmentVariable(SessionState.GetString(DesktopKey, ""), null);
                SessionState.SetString(DesktopKey, "");
            }
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
                    string xrEnv = XrEnv(text);
                    if (xrEnv != null)
                    {
                        SessionState.SetString(DesktopKey, xrEnv);
                        Environment.SetEnvironmentVariable(xrEnv, "1");
                    }
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
        public static bool ReachedStep(string current, string until) => ProcedureAutoRun.ReachedStep(current, until);

        /// <summary>交給人接手：之後的操作才算數。</summary>
        static IEnumerator HandOff()
        {
            foreach (var x in auto.HandOff()) yield return x;
            Note("交給人接手");
            stayInPlay = Arg("exit") != "1";
        }

        static void Note(string what) => auto.Note(what);

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

        // ---------- 主流程 ----------
        static IEnumerator Run()
        {
            while (!Sim.Ready || SliceUI.Instance == null || RecordingService.Instance == null) yield return 5;
            auto = new ProcedureAutoRun { DemoError = true, VideoSeconds = VideoSeconds, Log = l => log.AppendLine(l) };
            Sim.Bus.Subscribe<ErrorDetected>(e => errors.Add($"{S.Get("procedure.current")}：{e.errorId}（{e.severity}）{e.what}"));
            yield return 20;
            string mode = Arg("mode", "full");
            if (args.ContainsKey("source")) EditorPrefs.SetString("PantographSim.VideoSource", Arg("source"));
            // split=秒：測試自動切檔（沒給就清掉，回到 10 分鐘）
            if (args.ContainsKey("split")) EditorPrefs.SetFloat("PantographSim.VideoSplitSeconds", float.Parse(Arg("split"), CultureInfo.InvariantCulture));
            else EditorPrefs.DeleteKey("PantographSim.VideoSplitSeconds");
            float target = float.TryParse(Arg("pace", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0f;
            log.AppendLine($"模式 {mode}　目標長度 {target:0} s　錄影 {Arg("video", "0")}　紀錄根目錄 {RecordingService.Instance.Root}");

            // 等 VR 開場走完再開始：不然紀錄的輸入方式會寫成桌機（Link 關著時等滿就照桌機走）
            for (float until = Time.realtimeSinceStartup + VrWaitSeconds; Time.realtimeSinceStartup < until && !InputMode.ForceDesktop && !S.GetBool("xr.ready");) yield return 5;
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
                        log.AppendLine($"[{auto.Elapsed,6:0.0}s] 喀＋嗶　影片時間≈{VideoSeconds():0.000}");
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
            auto.Pace = target > 0 ? Mathf.Max(0f, (target - 75f) / 27f) : 0f;
            auto.Target = target;
            foreach (var x in auto.Procedure()) yield return x;

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
