using System;
using System.Collections.Generic;

namespace PantographSim.Core
{
    /// <summary>簡單服務登錄。只有 App 註冊；其他模組在 Start 之後取用。</summary>
    public static class ServiceLocator
    {
        static readonly Dictionary<Type, object> services = new Dictionary<Type, object>();

        public static void Register<T>(T service) where T : class => services[typeof(T)] = service;
        public static T Get<T>() where T : class => services.TryGetValue(typeof(T), out var s) ? (T)s : null;
        public static bool TryGet<T>(out T service) where T : class { service = Get<T>(); return service != null; }
        public static void Clear() => services.Clear();
    }

    /// <summary>依序詢問多個守門者，第一個擋下的為準。</summary>
    public class CompositeActionGuard : IActionGuard
    {
        readonly List<IActionGuard> guards = new List<IActionGuard>();
        public void Add(IActionGuard guard) { if (guard != null && !guards.Contains(guard)) guards.Add(guard); }

        public GuardResult Check(ActionRequest request)
        {
            foreach (var g in guards)
            {
                var r = g.Check(request);
                if (!r.allowed) return r;
            }
            return GuardResult.Allow;
        }
    }

    /// <summary>多個步驟閘門的組合。</summary>
    public class CompositeStepGate : IStepGate
    {
        readonly List<IStepGate> gates = new List<IStepGate>();
        public void Add(IStepGate gate) { if (gate != null && !gates.Contains(gate)) gates.Add(gate); }

        public bool CanComplete(string stepId, out string reason)
        {
            foreach (var g in gates)
                if (!g.CanComplete(stepId, out reason)) return false;
            reason = "";
            return true;
        }
    }

    /// <summary>
    /// 滑鼠的共用狀態：UI 標記指標是否在介面上；工具標記是否占用滾輪。
    /// 每一幀由 UI 與工具更新，攝影機與點選讀取。
    /// </summary>
    public static class PointerState
    {
        /// <summary>手上工具要用左鍵拖曳：鏡頭不跟著轉。</summary>
        public static bool DragCaptured;
        public static bool OverUi;
        public static bool ScrollCaptured;
        public static bool LeftDragCaptured;
        /// <summary>正在介面輸入框打字（此時鍵盤快捷鍵不作用）。</summary>
        public static bool TypingInUi;
        /// <summary>目前滑鼠射線（Interaction 每幀更新；拿在手上的零件跟著它移動）。</summary>
        public static UnityEngine.Ray Ray;
        public static bool HasRay;
        /// <summary>自動測試用：有值時取代滑鼠射線（Editor 截圖腳本沒有真的滑鼠）。</summary>
        public static UnityEngine.Ray? ScriptedRay;
        /// <summary>VR：右手扳機按住中（工具的「按住施力／轉動／擦拭」用；桌面永遠是 false）。由 XR 模組每幀寫入。</summary>
        public static bool XrTriggerHeld;
        /// <summary>VR 右搖桿推的格數，工具當成滾輪讀（讀一次就歸零）。</summary>
        public static float InjectedScroll;
        public static float ConsumeScroll() { float v = InjectedScroll; InjectedScroll = 0f; return v; }
    }

    /// <summary>
    /// 畫面可見範圍：Editor 的 Game 視窗放大（Scale &gt; 1）時，只看得到中央 1/Zoom 的區域；
    /// 由 Editor 腳本寫入，介面據此把面板縮進可見範圍。執行檔中永遠是 1。
    /// </summary>
    public static class ViewportInfo
    {
        public static float Zoom = 1f;
    }
}
