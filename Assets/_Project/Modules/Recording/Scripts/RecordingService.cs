using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Recording
{
    /// <summary>開資料夾／開報表的平台實作（Editor 模組會換成 EditorUtility.RevealInFinder）。</summary>
    public static class RecordingPlatform
    {
        public static Action<string> Reveal = path => Application.OpenURL(new Uri(path).AbsoluteUri);
        public static Action<string> OpenFile = path => Application.OpenURL(new Uri(path).AbsoluteUri);
    }

    /// <summary>
    /// 紀錄的執行期服務（場景中一個物件）：任務卡按「開始」（app.start）就建立 Recordings/&lt;yyyyMMdd_HHmmss_學員&gt;/ 並開始記錄；
    /// 每幀取樣位姿；結算時寫 summary.json 並產生報表。也處理紀錄清單、報表、開資料夾、重播的介面指令（ActionRequested 目標 rec）。
    /// 介面只讀黑板 rec.*：rec.active（● 紀錄中）、rec.folder、rec.report、rec.list、rec.names…
    /// 重播模式下不記錄。
    /// </summary>
    public class RecordingService : MonoBehaviour
    {
        [SerializeField] RecordingSettings settings;

        RecordingWriter writer;
        SessionRecorder recorder;
        List<ReplayTracked> tracked = new List<ReplayTracked>();
        readonly List<KeyValuePair<string, PoseSample>> trackedBuffer = new List<KeyValuePair<string, PoseSample>>();
        readonly List<KeyValuePair<string, float>> valueBuffer = new List<KeyValuePair<string, float>>();
        bool bound;
        string activeFolder;

        public static RecordingService Instance { get; private set; }
        public string Root { get; private set; }
        public SessionRecorder Recorder => recorder;
        public string ActiveFolder => activeFolder;

        public void Setup(RecordingSettings s) => settings = s;

        void Awake()
        {
            Instance = this;
            Root = settings != null ? settings.Root : RecordingPaths.Resolve("Recordings");
            // 不可放在 Assets 底下（Unity 會匯入 mp4）
            var assets = Path.GetFullPath(Application.dataPath);
            if (Root.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[Recording] 紀錄根目錄不能在 Assets 底下，改用專案根目錄的 Recordings/");
                Root = RecordingPaths.Resolve("Recordings");
            }
            ErrorNames.Preload();
        }

        void Start() => Bind();

        void Bind()
        {
            if (bound || !Sim.Ready) return;
            bound = true;
            Sim.Bus.Subscribe<ActionRequested>(OnRequest);
            var s = Sim.State;
            s.Set("rec.root", Root);
            if (SimSession.IsReplay) return;
            try
            {
                int repaired = RecordingIndex.RepairInterrupted(Root);
                if (repaired > 0) Debug.Log($"[Recording] 重建 {repaired} 筆中斷紀錄的摘要（標成「未完成（中斷）」）");
            }
            catch (Exception e) { Debug.LogWarning("[Recording] 掃描紀錄失敗：" + e.Message); }
            var names = SafeNames();
            s.Set("rec.names", string.Join(";", names));
            s.Set("rec.trainee_default", names.FirstOrDefault() ?? "");
        }

        List<string> SafeNames()
        {
            try { return RecordingIndex.LoadNames(Root); } catch (Exception) { return new List<string>(); }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            EndSession("scene_unload");
            if (bound && Sim.Bus != null) Sim.Bus.Unsubscribe<ActionRequested>(OnRequest);
        }

        void OnApplicationQuit() => EndSession("quit");

        // ---------- 開始／結束 ----------
        void BeginSession()
        {
            if (recorder != null || SimSession.IsReplay) return;
            var s = Sim.State;
            var now = DateTime.Now;
            string trainee = s.Get("rec.trainee").Trim();
            if (trainee.Length == 0) trainee = RecordingPaths.DefaultTrainee(now);
            string folder = Path.Combine(Root, RecordingPaths.FolderName(now, trainee));
            for (int i = 2; Directory.Exists(folder); i++) folder = Path.Combine(Root, RecordingPaths.FolderName(now, trainee) + "_" + i);
            try
            {
                Directory.CreateDirectory(folder);
                RecordingIndex.RememberName(Root, trainee);
                writer = new RecordingWriter(folder);
            }
            catch (Exception e)
            {
                Debug.LogError("[Recording] 無法建立紀錄資料夾：" + e.Message);
                s.Set("rec.message", "無法建立紀錄資料夾：" + e.Message);
                return;
            }
            activeFolder = folder;
            var catalog = StepCatalog.From(ServiceLocator.Get<ProcedureDefinition>());
            recorder = new SessionRecorder(Sim.Bus, s, writer, catalog) { Locator = ServiceLocator.Get<ISceneLocator>() };
            tracked = ReplayTracked.FindAll();
            recorder.TrackedProvider = TrackedNow;
            recorder.SessionFinished += OnFinished;
            string modeRaw = s.Get("training.mode", "tutorial");
            recorder.Begin(new Dictionary<string, object>
            {
                { "schemaVersion", RecordFiles.SchemaVersion }, { "trainee", trainee }, { "mode", Labels.Mode(modeRaw) }, { "modeRaw", modeRaw },
                { "input", Labels.Input(InputMode.XrActive) }, { "scenarioId", s.Get("training.scenario") }, { "scenarioTitle", s.Get("training.scenario_title") },
                { "appVersion", Application.version }, { "unityVersion", Application.unityVersion }, { "startedAt", now.ToString("o", CultureInfo.InvariantCulture) },
                { "folder", Path.GetFileName(folder) }, { "tools", s.Get("tools.list") }, { "trackedObjects", tracked.Count },
                { "courseId", s.Get(SimIds.CourseId, SimIds.CourseR1) }
            });
            s.Set("rec.trainee", trainee);
            s.Set("rec.folder", folder);
            s.Set("rec.report", "");
            s.SetBool("rec.finished", false);
            s.SetBool("rec.active", true);
            Debug.Log("[Recording] 開始紀錄：" + folder);
        }

        IEnumerable<KeyValuePair<string, PoseSample>> TrackedNow()
        {
            trackedBuffer.Clear();
            foreach (var t in tracked) if (t != null) trackedBuffer.Add(new KeyValuePair<string, PoseSample>(t.Id, t.Sample()));
            return trackedBuffer;
        }

        void OnFinished(SessionRecorder r)
        {
            writer.Drain(3000);
            try
            {
                string report = ReportBuilder.Generate(r.Folder);
                Sim.State.Set("rec.report", report);
                Debug.Log("[Recording] 報表：" + report);
            }
            catch (Exception e) { Debug.LogError("[Recording] 產生報表失敗：" + e); Sim.State.Set("rec.message", "產生報表失敗：" + e.Message); }
            Sim.State.SetBool("rec.finished", true);
        }

        /// <summary>結束這一筆（重新開始、離開 Play、關閉程式）。沒有結算的會重建摘要並標成「未完成（中斷）」。</summary>
        public void EndSession(string reason)
        {
            if (recorder == null) return;
            bool finished = recorder.Finished;
            recorder.Close(reason);
            recorder.Dispose();
            writer.Dispose();
            if (!finished)
            {
                try { RecordingIndex.RebuildSummary(activeFolder); } catch (Exception e) { Debug.LogWarning("[Recording] 重建摘要失敗：" + e.Message); }
            }
            recorder = null; writer = null;
            if (Sim.State != null) Sim.State.SetBool("rec.active", false);
        }

        // ---------- 每幀 ----------
        void LateUpdate()
        {
            Bind();
            if (recorder == null) return;
            valueBuffer.Clear();
            var s = Sim.State;
            if (s != null && s.Has(SimIds.LiftValue)) valueBuffer.Add(new KeyValuePair<string, float>(RecordFiles.LiftChannel, s.GetFloat(SimIds.LiftValue)));
            recorder.Sample(ServiceLocator.Get<IPoseSource>(), TrackedNow(), valueBuffer);
        }

        // ---------- 介面指令 ----------
        void OnRequest(ActionRequested e)
        {
            if (e.targetId == "app.start" && !SimSession.IsReplay) { BeginSession(); return; }
            if (e.targetId != "rec") return;
            string text = e.text ?? "";
            int colon = text.IndexOf(':');
            string cmd = colon >= 0 ? text.Substring(0, colon) : text;
            string arg = colon >= 0 ? text.Substring(colon + 1) : "";
            var s = Sim.State;
            switch (cmd)
            {
                case "list.open": RefreshList(); s.SetBool("rec.list_open", true); break;
                case "list.close": s.SetBool("rec.list_open", false); break;
                case "list.refresh": RefreshList(); break;
                case "trainee": s.Set("rec.trainee", arg); break;
                case "replay":
                    if (string.IsNullOrEmpty(arg)) arg = s.Get("rec.folder");
                    if (Directory.Exists(arg)) Sim.Bus.Publish(new ActionRequested { targetId = "app.replay", text = arg, toolId = "", time = SimClock.Now });
                    break;
                case "report":
                    if (string.IsNullOrEmpty(arg)) arg = s.Get("rec.folder");
                    OpenReport(arg);
                    break;
                case "open":
                    if (string.IsNullOrEmpty(arg)) arg = s.Get("rec.folder");
                    if (Directory.Exists(arg)) RecordingPlatform.Reveal(arg);
                    break;
                case "open_file":
                    if (File.Exists(arg)) RecordingPlatform.Reveal(arg);
                    break;
            }
        }

        void OpenReport(string folder)
        {
            if (!Directory.Exists(folder)) return;
            try
            {
                if (!File.Exists(Path.Combine(folder, RecordFiles.Summary))) RecordingIndex.RebuildSummary(folder);
                string report = ReportBuilder.Generate(folder);
                Sim.State.Set("rec.message", "報表已產生：" + report);
                RecordingPlatform.OpenFile(report);
                RefreshList();
            }
            catch (Exception e) { Sim.State.Set("rec.message", "產生報表失敗：" + e.Message); Debug.LogException(e); }
        }

        /// <summary>
        /// 紀錄清單寫到黑板 rec.list：一行一筆（由新到舊），欄位以 \u001f 分隔：
        /// 資料夾、學員、日期、模式、輸入方式、分數、通過(1/0/空)、狀態、中斷(1/0)、嚴重、錯誤總數、秒數、影片數。
        /// </summary>
        public void RefreshList()
        {
            List<RecordEntry> list;
            // 已結算的這一場顯示「完成」，還在進行的才是「紀錄中」
            string recording = recorder != null && !recorder.Finished ? activeFolder : null;
            try
            {
                RecordingIndex.RepairInterrupted(Root, activeFolder);
                list = RecordingIndex.List(Root, recording);
            }
            catch (Exception e) { Sim.State.Set("rec.message", "讀取紀錄清單失敗：" + e.Message); return; }
            var rows = list.Select(x => string.Join("\u001f", new[]
            {
                x.folder, x.trainee, DateTime.TryParse(x.startedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d.ToString("yyyy-MM-dd HH:mm") : x.name,
                x.mode, x.input, double.IsNaN(x.score) ? "" : x.score.ToString("0", CultureInfo.InvariantCulture), x.finished ? (x.passed ? "1" : "0") : "",
                x.status, x.interrupted ? "1" : "0", x.finished ? x.critical.ToString("0") : "", x.finished ? x.errorTotal.ToString("0") : "",
                x.totalSeconds.ToString("0", CultureInfo.InvariantCulture), x.videos.Count.ToString()
            }));
            Sim.State.Set("rec.list", string.Join("\n", rows));
            Sim.State.SetFloat("rec.list_count", list.Count);
        }
    }
}
