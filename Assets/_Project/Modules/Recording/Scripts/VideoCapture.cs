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
}
