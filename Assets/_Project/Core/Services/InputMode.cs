using UnityEngine;

namespace PantographSim.Core
{
    /// <summary>
    /// 輸入模式：桌面（滑鼠鍵盤）或 VR（頭盔＋控制器）。XR 模組偵測到頭盔後呼叫 SetXr(true)；
    /// 其他模組只讀這裡，不引用任何 XR 套件（VR 只是另一個輸入來源與顯示方式）。
    /// </summary>
    public static class InputMode
    {
        public static bool XrActive { get; private set; }
        /// <summary>命令列 -desktop（或環境變數 PANTO_DESKTOP=1）：有頭盔也用桌面模式。</summary>
        public static bool ForceDesktop;
        public static event System.Action Changed;

        public static void SetXr(bool active)
        {
            if (XrActive == active) return;
            XrActive = active;
            Changed?.Invoke();
        }

        /// <summary>新場景開始時（App 的 Awake）回到桌面，清掉上一場景留下的訂閱。</summary>
        public static void ResetForScene()
        {
            XrActive = false;
            Changed = null;
        }
    }

    /// <summary>VR 裝置在場景中的位置（XR 模組註冊到 ServiceLocator；UI 用來擺放世界空間面板）。</summary>
    public interface IXrRig
    {
        Camera Eye { get; }
        Transform Head { get; }
        Transform LeftHand { get; }
        Transform RightHand { get; }
    }

    /// <summary>
    /// 指到即顯示的延遲規則（純邏輯）：指著新物件停留 ShowDelay 秒才換成它；什麼都沒指 HideDelay 秒後收起。一次只顯示一個。
    /// </summary>
    public class HoverDwell
    {
        public float ShowDelay = 0.2f, HideDelay = 0.3f;
        string candidate = "", shown = "";
        float since;

        public string Shown => shown;

        public string Update(string current, float now)
        {
            current = current ?? "";
            if (current != candidate) { candidate = current; since = now; }
            float wait = candidate.Length > 0 ? ShowDelay : HideDelay;
            if (shown != candidate && now - since >= wait - 1e-5f) shown = candidate;
            return shown;
        }

        public void Clear() { candidate = shown = ""; }
    }
}
