using System;
using System.Globalization;
using System.IO;
using PantographSim.Core;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace PantographSim.Recording.Editor
{
    /// <summary>
    /// 影片錄製後端（Unity Recorder，只在 Editor 的 Play 中可用）。收到 VideoCaptureToggled 就用 RecorderController 開始／停止。
    /// ・VR：錄 SpectatorCamera 的 1920×1080 RenderTexture（旁觀鏡頭，頭盔裡的 REC 紅點拍不到）；桌面：錄 Game 視窗（含介面與 REC 字樣），1920×1080。
    /// ・MP4 H.264、約 10 Mbps、含音訊；FrameRatePlayback 一律 Variable（不可用 Constant：Constant 會改 Time.captureDeltaTime，VR 會變慢、卡頓）。
    /// ・幀率：Recorder 本身不限速（見 RecorderCeiling）。桌面錄影時遊戲限 30 fps，影片 30 fps；
    ///   VR 不限制遊戲（頭盔決定 72/90 Hz），旁觀鏡頭每秒只更新 30 次，影片內容是 30 fps（檔案裡重複的幀幾乎不佔位元率）。
    /// ・存到這一場的紀錄資料夾，檔名 video_yyyyMMdd_HHmmss.mp4；開始時發 VideoStarted（紀錄器寫進 session.jsonl）。
    /// ・離開 Play、關閉 Editor 時自動停止收尾；每 10 分鐘自動切成新檔。
    /// </summary>
    [InitializeOnLoad]
    public sealed class RecorderVideoBackend : IVideoCaptureBackend
    {
        public const float TargetFps = 30f;
        public const float BitrateMbps = 10f;
        public const float SplitSeconds = 600f;
        /// <summary>
        /// 給 Recorder 的「上限 fps」。Recorder 4 的 Variable 模式在一幀比上限快時會讓主執行緒睡眠（最長 1 s），
        /// 在 Editor 裡實測會出現每隔 1–2 s 卡 1 s、影片與聲音變短；設很高就不會睡。實際幀率另外控制（桌面 targetFrameRate=30）。
        /// 也不能用「每 N 幀錄一幀」降幀：被跳過的幀的聲音會被丟掉。
        /// </summary>
        public const float RecorderCeiling = 1000f;
        int savedTargetFrameRate = -1;
        bool frameRateOverridden;
        /// <summary>桌面錄影來源（EditorPrefs，測試用）：gameview（Game 視窗，含介面，預設）／camera_ui／camera。主相機的「含介面」抓不到本專案的介面，所以桌面預設錄 Game 視窗（1920×1080）。</summary>
        public const string SourcePref = "PantographSim.VideoSource";
        /// <summary>切檔間隔覆寫（EditorPrefs，只給測試用；沒設就是 10 分鐘）。</summary>
        public const string SplitPref = "PantographSim.VideoSplitSeconds";

        static readonly RecorderVideoBackend instance = new RecorderVideoBackend();

        RecorderController controller;
        RecorderControllerSettings controllerSettings;
        MovieRecorderSettings movieSettings;
        IEventBus boundBus;
        string currentFile = "";
        float segmentStart;

        public bool IsCapturing => controller != null && controller.IsRecording();
        public string CurrentFile => currentFile;
        public bool Available => EditorApplication.isPlaying;
        public string Reason => EditorApplication.isPlaying ? "" : "影片只能在 Unity Editor 按 Play 時錄製";

        static RecorderVideoBackend()
        {
            VideoCapture.Backend = instance;
            RecordingPlatform.Reveal = path => EditorUtility.RevealInFinder(path);
            EditorApplication.update += instance.Update;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingPlayMode) instance.Stop("play_exit");
            };
            EditorApplication.quitting += () => instance.Stop("editor_quit");
            AssemblyReloadEvents.beforeAssemblyReload += () => instance.Stop("reload");
        }

        void Update()
        {
            // 每次進 Play，Sim 會建立新的事件匯流排：重新訂閱
            var bus = EditorApplication.isPlaying && Sim.Ready ? Sim.Bus : null;
            if (bus != boundBus)
            {
                boundBus?.Unsubscribe<VideoCaptureToggled>(OnToggled);
                boundBus = bus;
                boundBus?.Subscribe<VideoCaptureToggled>(OnToggled);
            }
            if (IsCapturing && Time.realtimeSinceStartup - segmentStart >= EditorPrefs.GetFloat(SplitPref, SplitSeconds))
            {
                // 每 10 分鐘切新檔：先收尾目前這個，再立刻開下一個
                Stop("split");
                Start();
            }
        }

        void OnToggled(VideoCaptureToggled e)
        {
            if (e.isReplay) return;
            string text = e.text ?? "toggle";
            if (text == "start" || text == "toggle" && !IsCapturing) Start();
            else if (text == "stop" || text == "toggle") Stop("user");
        }

        // ---------- 開始 ----------
        void Start()
        {
            if (IsCapturing || !EditorApplication.isPlaying || !Sim.Ready) return;
            var s = Sim.State;
            string folder = s.Get("rec.folder");
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                // 還沒按開始：放在紀錄根目錄底下另一個資料夾
                string root = s.Get("rec.root");
                if (string.IsNullOrEmpty(root)) root = RecordingPaths.Resolve("Recordings");
                folder = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_影片");
                Directory.CreateDirectory(folder);
            }
            string stem = Path.Combine(folder, VideoCapture.FileStem(DateTime.Now));
            for (int i = 2; File.Exists(stem + ".mp4"); i++) stem = Path.Combine(folder, VideoCapture.FileStem(DateTime.Now) + "_" + i);

            bool xr = InputMode.XrActive && SpectatorCamera.Instance != null;
            if (!xr)
            {
                // 桌面：錄影時把遊戲限制在 30 fps（停止後還原），影片就是 30 fps；VR 的幀率由頭盔決定，不動它
                savedTargetFrameRate = Application.targetFrameRate;
                Application.targetFrameRate = (int)TargetFps;
                frameRateOverridden = true;
            }

            movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movieSettings.name = "PantographSim Video";
            movieSettings.Enabled = true;
            movieSettings.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Custom,
                TargetBitRate = BitrateMbps
            };
            movieSettings.CaptureAudio = true;
            movieSettings.CaptureAlpha = false;
            if (xr)
            {
                var spec = SpectatorCamera.Instance;
                spec.Capturing = true;
                movieSettings.ImageInputSettings = new RenderTextureInputSettings
                {
                    RenderTexture = spec.Texture, OutputWidth = SpectatorCamera.Width, OutputHeight = SpectatorCamera.Height, FlipFinalOutput = false
                };
            }
            else
            {
                string src = EditorPrefs.GetString(SourcePref, "gameview");
                if (src == "gameview")
                    movieSettings.ImageInputSettings = new GameViewInputSettings { OutputWidth = SpectatorCamera.Width, OutputHeight = SpectatorCamera.Height };
                else
                    movieSettings.ImageInputSettings = new CameraInputSettings
                    {
                        Source = ImageSource.MainCamera, OutputWidth = SpectatorCamera.Width, OutputHeight = SpectatorCamera.Height, CaptureUI = src == "camera_ui"
                    };
            }
            movieSettings.OutputFile = stem;   // Recorder 自己加 .mp4

            controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            controllerSettings.AddRecorderSettings(movieSettings);
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRatePlayback = FrameRatePlayback.Variable;   // 一定是 Variable
            controllerSettings.FrameRate = RecorderCeiling;                      // 見 RecorderCeiling：不讓 Recorder 讓主執行緒睡眠
            controllerSettings.CapFrameRate = false;
            controllerSettings.ExitPlayMode = false;

            controller = new RecorderController(controllerSettings);
            controller.PrepareRecording();
            if (!controller.StartRecording())
            {
                s.Set(VideoCapture.MessageKey, "無法開始錄影（Unity Recorder 啟動失敗，請看 Console）");
                s.Set("ui.toast", s.Get(VideoCapture.MessageKey));
                Cleanup();
                return;
            }
            currentFile = stem + ".mp4";
            segmentStart = Time.realtimeSinceStartup;
            s.SetFloat(VideoCapture.StartedKey, segmentStart);
            s.Set(VideoCapture.FileKey, currentFile);
            s.Set(VideoCapture.MessageKey, $"錄影中：{Path.GetFileName(currentFile)}（{(xr ? "旁觀鏡頭" : "主相機")}）");
            s.SetBool(VideoCapture.ActiveKey, true);
            Sim.Bus.Publish(new VideoStarted { file = currentFile, t = SimClock.Now, time = SimClock.Now });
            Debug.Log($"[Video] 開始錄影：{currentFile}　來源 {(xr ? "SpectatorCamera RenderTexture（30 Hz 更新）" : "Main Camera（遊戲限 30 fps）")}　{BitrateMbps} Mbps、含音訊、Variable");
        }

        // ---------- 停止 ----------
        void Stop(string reason)
        {
            if (controller == null) return;
            string file = currentFile;
            try { if (controller.IsRecording()) controller.StopRecording(); }
            catch (Exception e) { Debug.LogException(e); }
            Cleanup();
            Debug.Log($"[Video] 停止錄影（{reason}）：{file}");
            var s = Sim.State;
            if (s == null) return;
            s.SetBool(VideoCapture.ActiveKey, false);
            s.Set(VideoCapture.FileKey, "");
            s.Set(VideoCapture.LastKey, file);
            s.SetFloat("rec.video.stopped", Time.realtimeSinceStartup);
            s.Set(VideoCapture.MessageKey, "影片已存：" + file);
            if (reason == "user") s.Set("ui.toast", "影片已存：" + file);
            Sim.Bus?.Publish(new VideoStopped { file = file, reason = reason, t = SimClock.Now, time = SimClock.Now });
        }

        void Cleanup()
        {
            if (SpectatorCamera.Instance != null) SpectatorCamera.Instance.Capturing = false;
            if (frameRateOverridden) { Application.targetFrameRate = savedTargetFrameRate; frameRateOverridden = false; }
            controller = null;
            if (movieSettings != null) UnityEngine.Object.DestroyImmediate(movieSettings);
            if (controllerSettings != null) UnityEngine.Object.DestroyImmediate(controllerSettings);
            movieSettings = null; controllerSettings = null;
            currentFile = "";
        }
    }
}
