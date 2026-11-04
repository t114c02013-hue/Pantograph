using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// 車頂開放規則的執行端（規則只寫在 RoofAccessRule 資產）：黑板改變時判定，結果寫到 roof.open。
    /// 傳送（車頂站點）、活動欄、升降提示（車頂有人作業不能升弓）都只讀這個鍵。
    /// </summary>
    public class RoofAccess : MonoBehaviour
    {
        [SerializeField] RoofAccessRule rule;
        bool bound, onRoof;

        public void Setup(RoofAccessRule r) => rule = r;
        public RoofAccessRule Rule => rule;

        /// <summary>規則判定（純函式，測試用）：open 條件成立、且 close 條件不成立。</summary>
        public static bool IsOpen(RoofAccessRule r, ISimState s) =>
            r != null && s != null && ChecklistEval.Done(s, r.openCondition) && !(string.IsNullOrEmpty(r.closeCondition) ? false : ChecklistEval.Done(s, r.closeCondition));

        public static void Write(RoofAccessRule r, ISimState s)
        {
            bool open = IsOpen(r, s);
            string key = string.IsNullOrEmpty(r.resultKey) ? LiftControl.RoofOpenKey : r.resultKey;
            if (s.GetBool(key) != open || !s.Has(key)) s.SetBool(key, open);
        }

        void Update()
        {
            if (bound || !Sim.Ready || rule == null) return;
            bound = true;
            Sim.Bus.Subscribe<StateChanged>(OnState);
            Write(rule, Sim.State);
        }

        void OnDestroy() { if (bound && Sim.Bus != null) Sim.Bus.Unsubscribe<StateChanged>(OnState); }

        void OnState(StateChanged e)
        {
            if (e.Has(PlatformGate.FloorKey)) CheckEntry(rule, Sim.State, ref onRoof);
            if (e.Has(rule.resultKey)) return;
            Write(rule, Sim.State);
        }

        /// <summary>進到車頂的那一刻：還沒放行，教學只提示，練習、考核記錯（同一次上車頂一次）。
        /// shortcut: hold.1.released 還沒有程式在寫，先用 roof.open 當放行, b4c:holdpoint 做好後改讀 hold.1.released。</summary>
        public static void CheckEntry(RoofAccessRule r, ISimState s, ref bool onRoof)
        {
            bool now = s.Get(PlatformGate.FloorKey) == "Roof";
            if (now == onRoof) return;
            onRoof = now;
            bool skipped = now && !s.GetBool(r.resultKey);
            if (skipped && s.Get("training.mode") == "tutorial") { s.Set("ui.toast", ""); s.Set("ui.toast", r.holdSkippedHint); s.SetFloat("ui.toast_time", Time.time); return; }
            s.SetBool(r.holdSkippedKey, skipped);
        }
    }
}
