using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// R1.5b b4：工具袋的零件袋（env.tool_bag.bolt_pouch）。集電弓底座、高壓端子螺栓轉到脫出後收進來（RM_03 h、RM_04 c），裝回時從這裡拿；
    /// 計數 env.tool_bag.bolt_pouch.count（WC_01a b 要是 0）。點一下顯示數量。螺栓狀態由 FastenerLogic 改，這裡只收 add:bolt／remove:bolt 計數。
    /// </summary>
    public class BoltPouch : SiteObject
    {
        public const string CountKey = "env.tool_bag.bolt_pouch.count";

        protected override void OnBound() { if (!State.Has(CountKey)) State.SetFloat(CountKey, 0); }

        protected override void OnBareHand(ActionRequested e) => Toast($"零件袋：{State.GetFloat(CountKey):0} 支螺栓");

        protected override void OnToolAction(ActionPerformed e) => Apply(State, e.text);

        /// <summary>add:bolt／remove:bolt：計數 ±1。</summary>
        public static void Apply(ISimState s, string text)
        {
            float n = s.GetFloat(CountKey);
            if (text == "add:bolt") s.SetFloat(CountKey, n + 1);
            else if (text == "remove:bolt") s.SetFloat(CountKey, Mathf.Max(0, n - 1));
        }
    }
}
