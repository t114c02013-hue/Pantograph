using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Tools
{
    /// <summary>
    /// 接地棒：夾平台端子 → 拉線確認（介面按鈕 pulltest）→ 掛弓頭接地掛點；拆除照反序（先弓頭、再端子）。
    /// 順序錯誤由 SafetyGuard 擋下（E3/E13/E16）；沒拉線確認就掛弓頭會記 isolation.clamped_without_pulltest。
    /// </summary>
    public class GroundingRodTool : ToolBase
    {
        public override void OnClick(ActionRequested e)
        {
            if (e.targetId == SimIds.GroundTerminal)
            {
                if (!Ask(e.targetId, "ground")) return;
                Perform(e.targetId, IsolationLogic.ToggleTerminal(State));
            }
            else if (e.targetId == SimIds.GroundClamp)
            {
                if (!Ask(e.targetId, "ground")) return;
                string result = IsolationLogic.ToggleHead(State);
                Perform(e.targetId, result);
                // 掛上弓頭後接地棒留在場上（掛位顯示使用中），手空出來
                if (result == "hang") Bus.Publish(new ActionRequested { targetId = "tool.belt", text = "deploy", time = SimClock.Now });
            }
        }

        public override void OnCommand(string command)
        {
            if (command != "pulltest") return;
            if (!State.GetBool(SimIds.TerminalClamped)) { State.Set(Key("message"), "請先把接地夾夾在平台接地端子上"); return; }
            if (!Ask(SimIds.GroundTerminal, "pulltest")) return;
            IsolationLogic.PullTest(State);
            State.Set(Key("message"), "拉線確認：端子夾持牢固");
        }
    }
}
