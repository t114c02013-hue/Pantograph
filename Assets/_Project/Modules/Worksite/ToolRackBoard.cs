using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Worksite
{
    /// <summary>工具架本體（env.tool_rack）：點它＝查看九個掛位（WC_01 清點用，記 ui.rack_viewed）。</summary>
    public class ToolRackBoard : SiteObject
    {
        protected override void OnBareHand(ActionRequested e)
        {
            State.SetBool("ui.rack_viewed", true);
            int onRack = 0, inUse = 0;
            for (int k = 1; k <= 9; k++)
            {
                var st = State.Get(RackLogic.SlotKey(k), RackLogic.OnRack);
                if (st == RackLogic.OnRack) onRack++; else inUse++;
            }
            Toast($"工具架：{onRack} 件在架、{inUse} 件使用中");
        }
    }
}
