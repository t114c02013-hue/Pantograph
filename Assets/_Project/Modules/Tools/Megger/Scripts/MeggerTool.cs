using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Tools
{
    /// <summary>
    /// R1.5b b4：高阻計（RT_02，D43）。拿著它點弓頭接地掛點、車體接地點＝接上測試線；介面指令 test＝按測試鍵。
    /// 測試線放下高阻計也照樣接著，要空手拆（MeggerLeads，放電滿 5 秒才可以）。邏輯在 MeggerLogic；其他目標照 CarryTool。
    /// </summary>
    public class MeggerTool : CarryTool
    {
        public override void OnClick(ActionRequested e)
        {
            if (!MeggerLogic.IsPoint(e.targetId)) { base.OnClick(e); return; }
            if (!Ask(e.targetId, "use")) return;
            MeggerLogic.ConnectLead(State, e.targetId);
            Perform(e.targetId, "lead");
        }

        public override void OnCommand(string command)
        {
            if (command != "test" || !Ask(MeggerLogic.PanPoint, "test")) return;
            var msg = MeggerLogic.Test(State, ToolRulesDef.Load());
            if (msg != null) State.Set("ui.toast", msg);
            if (State.GetBool(MeggerLogic.Measured)) Perform(MeggerLogic.PanPoint, "test", State.GetFloat(MeggerLogic.Reading));
        }
    }
}
