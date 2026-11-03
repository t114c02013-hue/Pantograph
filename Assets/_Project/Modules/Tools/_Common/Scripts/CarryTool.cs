using PantographSim.Core;

namespace PantographSim.Tools
{
    /// <summary>
    /// R1.5b b3：只要「拿著、點目標」的工具（降弓固定帶、高阻計、攜帶式短接地線）。點到目標＝送出 ActionPerformed（text＝use），
    /// 目標自己的元件（例：DetachablePart 的 toolId）決定要做什麼。量測、接地的判定在步驟資料（b4）。
    /// </summary>
    public class CarryTool : ToolBase
    {
        public override void OnClick(ActionRequested e)
        {
            if (string.IsNullOrEmpty(e.targetId) || e.targetId == "tool.belt") return;
            if (Ask(e.targetId, "use")) Perform(e.targetId, "use");
        }
    }
}
