using UnityEngine;

namespace PantographSim.Core
{
    /// <summary>執行模式：一般訓練，或重播一筆紀錄（同一個場景，不啟動判定與輸入）。</summary>
    public enum SimMode { Training, Replay }

    /// <summary>
    /// 這一場的執行模式（跨場景重新載入保留，由 App 設定）。Replay 時 EventBus 會把所有發出的事件標成 isReplay=true，
    /// 所以重播中按任何東西都不會被計分或寫進紀錄。
    /// </summary>
    public static class SimSession
    {
        public static SimMode Mode { get; private set; } = SimMode.Training;
        /// <summary>要重播的紀錄資料夾（Replay 時）。</summary>
        public static string ReplayFolder { get; private set; } = "";
        public static bool IsReplay => Mode == SimMode.Replay;

        public static void EnterReplay(string folder) { Mode = SimMode.Replay; ReplayFolder = folder ?? ""; }
        public static void EnterTraining() { Mode = SimMode.Training; ReplayFolder = ""; }

        /// <summary>每次進入 Play 都從訓練模式開始（關閉 Domain Reload 時靜態值不會殘留）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => EnterTraining();
    }

    /// <summary>
    /// 學員的頭與雙手位姿（世界座標）。VR：頭盔與控制器；桌面：主攝影機當頭、沒有手。
    /// XR Rig 與桌面攝影機各自實作，App 依輸入模式註冊到 ServiceLocator；紀錄、旁觀鏡頭只讀這裡。
    /// </summary>
    public interface IPoseSource
    {
        /// <summary>來源名稱：xr／desktop。</summary>
        string SourceName { get; }
        /// <summary>目前可用（物件啟用中）。</summary>
        bool PoseAvailable { get; }
        Pose Head { get; }
        bool HasHands { get; }
        Pose LeftHand { get; }
        Pose RightHand { get; }
    }
}
