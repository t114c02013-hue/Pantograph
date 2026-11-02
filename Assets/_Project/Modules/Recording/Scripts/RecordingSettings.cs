using UnityEngine;

namespace PantographSim.Recording
{
    /// <summary>
    /// 紀錄設定（Assets/_Project/Settings/RecordingSettings.asset）。根目錄可以是相對路徑（相對專案根目錄）或絕對路徑，
    /// 例如改成共用資料夾。不要放在 Assets 底下（Unity 會把 mp4 匯入成資產）。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/Recording Settings")]
    public class RecordingSettings : ScriptableObject
    {
        [Tooltip("紀錄根目錄：相對專案根目錄（預設 Recordings）或絕對路徑")] public string rootFolder = "Recordings";
        [Tooltip("影片每幾分鐘自動切成新檔")] public float videoSplitMinutes = 10f;
        [Tooltip("旁觀鏡頭：第三人稱時在學員斜後上方的距離（m）")] public float spectatorDistance = 1.2f;

        public string Root => RecordingPaths.Resolve(rootFolder);
    }
}
