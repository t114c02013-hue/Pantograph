using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>R1.5b b4：台架工具盤（env.panto_stand.tool_tray）。拿著工具點它＝放到盤上（掛位 on_tray）；到工具袋那一格點一下就拿回（ToolBelt 照撿回處理）。空手點＝顯示件數。</summary>
    public class ToolTray : SiteObject
    {
        [SerializeField] ToolSlotTable table;

        public void Setup(ToolSlotTable t) => table = t;

        protected override bool AcceptsAnyTool => true;

        protected override void OnBareHand(ActionRequested e)
        {
            if (string.IsNullOrEmpty(e.toolId)) { Toast($"台架工具盤：{State.GetFloat(ToolCountLogic.TrayCount):0} 件工具"); return; }
            var msg = ToolCountLogic.PutOnTray(State, table);
            if (msg != null) Toast(msg);
        }
    }
}
