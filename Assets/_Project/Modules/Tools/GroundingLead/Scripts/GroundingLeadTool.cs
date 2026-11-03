using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Tools
{
    /// <summary>
    /// R1.5b b4：攜帶式短接地線（#2 接地，RM_00、WC_03a）。點車體接地座／電纜端子＝接上或取下；介面指令 ring＝掛到安全帶工具環（隨身）、
    /// take＝從工具環或接著的位置拿到手上、pulltest＝拉線確認。狀態邏輯在 GroundingLeadLogic；拆除順序由 ErrorRules.json 在動作前擋下。其他目標照 CarryTool。
    /// </summary>
    public class GroundingLeadTool : CarryTool
    {
        public override void OnClick(ActionRequested e)
        {
            if (!GroundingLeadLogic.IsPoint(e.targetId)) { base.OnClick(e); return; }
            if (!Ask(e.targetId, "use")) return;
            Perform(e.targetId, GroundingLeadLogic.Toggle(State, e.targetId));
        }

        public override void OnCommand(string command)
        {
            string msg = null;
            if (command == "ring") msg = GroundingLeadLogic.HangOnRing(State, Hotkey);
            else if (command == "take") msg = GroundingLeadLogic.Take(State, Hotkey);
            else if (command == "pulltest" && Ask(GroundingLeadLogic.Cable, "pulltest")) msg = GroundingLeadLogic.PullTest(State, Hotkey);
            if (msg != null) State.Set("ui.toast", msg);
        }
    }
}
