using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Worksite
{
    /// <summary>R1.5b b4：控制盤上的主斷路器、電動缸電源、升弓鈕（空手點）。邏輯在 CabControlLogic；上鎖掛牌由 LockoutTagTool 處理。</summary>
    public class CabSwitch : SiteObject
    {
        protected override void OnBareHand(ActionRequested e)
        {
            string msg = null;
            if (id == CabControlLogic.BreakerId) msg = CabControlLogic.ToggleBreaker(State);
            else if (id == CabControlLogic.CylinderPowerId) msg = CabControlLogic.ToggleCylinderPower(State);
            else if (id == CabControlLogic.RaiseButtonId) CabControlLogic.PressRaise(State);
            if (msg != null) Toast(msg); else Sim.Perform("", id, "click");
        }
    }
}
