using System;
using System.Globalization;
using System.IO;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Recording
{
    /// <summary>
    /// 影片錄製的後端（Unity Recorder 只能在 Editor 用，所以實作放在 PantographSim.Recording.Editor）。
    /// 執行期只透過這個介面查狀態；開始／停止一律發 VideoCaptureToggled，由後端收到後處理。
    /// </summary>
    public interface IVideoCaptureBackend
    {
        bool IsCapturing { get; }
        string CurrentFile { get; }
        /// <summary>可用（Editor 且有安裝 Recorder）；不可用時 Reason 說明原因。</summary>
        bool Available { get; }
        string Reason { get; }
    }

    /// <summary>後端登錄點（Editor 模組在載入時設定）。</summary>
    public static class VideoCapture
    {
        public static IVideoCaptureBackend Backend;

        /// <summary>影片檔名：video_yyyyMMdd_HHmmss（Recorder 會自己加 .mp4）。</summary>
        public static string FileStem(DateTime when) => "video_" + when.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        // 黑板鍵（後端寫、介面讀）
        public const string ActiveKey = "rec.video.active";        // 錄影中
        public const string StartedKey = "rec.video.started";      // 開始時的 Time.realtimeSinceStartup（算 REC mm:ss）
        public const string FileKey = "rec.video.file";            // 目前檔案
        public const string LastKey = "rec.video.last";            // 剛停止的檔案（畫面顯示路徑與「開啟資料夾」）
        public const string ViewKey = "rec.video.view";            // 旁觀鏡頭：third（第三人稱，預設）／head（頭盔視角，穩定化）
        public const string MessageKey = "rec.video.message";
    }

    /// <summary>
    /// 錄影的執行期入口：桌面按 F9、VR 在 ≡ 選單或左手腕面板按「● 開始錄影／■ 停止錄影」（介面送 ActionRequested 目標 rec.video），
    /// 一律轉成 VideoCaptureToggled 給後端。也負責開始／停止的提示音（震動由 XR 模組聽 VideoStarted／VideoStopped）。
    /// </summary>
    public class VideoCaptureService : MonoBehaviour
    {
        AudioSource beepSource;
        AudioClip startBeep, stopBeep;
        bool bound, wasActive;

        void Start()
        {
            beepSource = gameObject.AddComponent<AudioSource>();
            beepSource.playOnAwake = false; beepSource.spatialBlend = 0f; beepSource.volume = 0.5f;
            startBeep = Tone("rec_start", 880f, 0.12f);
            stopBeep = Tone("rec_stop", 440f, 0.18f);
        }

        static AudioClip Tone(string name, float hz, float seconds)
        {
            const int rate = 44100;
            int n = (int)(rate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float env = Mathf.Min(1f, i / (rate * 0.01f)) * Mathf.Min(1f, (n - i) / (rate * 0.03f));
                data[i] = Mathf.Sin(2 * Mathf.PI * hz * i / rate) * 0.35f * env;
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Bind()
        {
            if (bound || !Sim.Ready) return;
            bound = true;
            Sim.Bus.Subscribe<ActionRequested>(OnRequest);
            if (!Sim.State.Has(VideoCapture.ViewKey)) Sim.State.Set(VideoCapture.ViewKey, "third");
        }

        void OnDestroy()
        {
            if (bound && Sim.Bus != null) Sim.Bus.Unsubscribe<ActionRequested>(OnRequest);
        }

        void OnRequest(ActionRequested e)
        {
            if (e.targetId != "rec.video") return;
            var t = e.text ?? "";
            if (t == "view:toggle") Sim.State.Set(VideoCapture.ViewKey, Sim.State.Get(VideoCapture.ViewKey, "third") == "third" ? "head" : "third");
            else if (t.StartsWith("view:")) Sim.State.Set(VideoCapture.ViewKey, t.Substring(5));
            else if (t == "open") { var f = Sim.State.Get(VideoCapture.LastKey); if (File.Exists(f)) RecordingPlatform.Reveal(f); }
            else Toggle(string.IsNullOrEmpty(e.pointId) ? "ui" : e.pointId, t.Length == 0 ? "toggle" : t);
        }

        public static void Toggle(string source, string text = "toggle")
        {
            if (Sim.Bus == null) return;
            var backend = VideoCapture.Backend;
            if (backend == null || !backend.Available)
            {
                Sim.State.Set(VideoCapture.MessageKey, backend == null ? "影片錄製只能在 Unity Editor 中使用（Unity Recorder）" : backend.Reason);
                Sim.State.Set("ui.toast", Sim.State.Get(VideoCapture.MessageKey));
            }
            Sim.Bus.Publish(new VideoCaptureToggled { text = text, targetId = source ?? "", time = SimClock.Now });
        }

        void Update()
        {
            Bind();
            if (!bound) return;
            if (!InputMode.XrActive && !PointerState.TypingInUi && Input.GetKeyDown(KeyCode.F9)) Toggle("key.F9");
            bool active = Sim.State.GetBool(VideoCapture.ActiveKey);
            if (active != wasActive)
            {
                wasActive = active;
                if (beepSource != null) beepSource.PlayOneShot(active ? startBeep : stopBeep);
            }
        }
    }
}
