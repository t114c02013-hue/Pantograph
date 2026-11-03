using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Tools
{
    /// <summary>R1.5b b4：高阻計（RT_02）。點弓頭接地掛點、車體接地點＝接上／取下測試線；介面指令 test＝按測試鍵。放下時收起測試線。邏輯在 MeggerLogic；其他目標照 CarryTool。</summary>
    public class MeggerTool : CarryTool
    {
        public override void OnClick(ActionRequested e)
        {
            if (!MeggerLogic.IsPoint(e.targetId)) { base.OnClick(e); return; }
            if (!Ask(e.targetId, "use")) return;
            MeggerLogic.ToggleLead(State, e.targetId);
            Perform(e.targetId, "lead");
        }

        public override void OnCommand(string command)
        {
            if (command != "test" || !Ask(MeggerLogic.PanPoint, "test")) return;
            var msg = MeggerLogic.Test(State, ToolRulesDef.Load());
            if (msg != null) State.Set("ui.toast", msg); else Perform(MeggerLogic.PanPoint, "test", State.GetFloat(MeggerLogic.Reading));
        }

        public override void OnDeselected() => MeggerLogic.ClearLeads(State);
    }
}
