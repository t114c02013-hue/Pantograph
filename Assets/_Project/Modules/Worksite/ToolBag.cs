using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// R1.5b b4：工具袋（env.tool_bag）。相關的鍵一變就重算清點用的鍵（ToolCountLogic：逐格注視、台架工具盤件數、車頂遺留、待報廢箱條數）。
    /// 哪些掛位要清點讀 ToolSlotTable（homeStorageId）。
    /// </summary>
    public class ToolBag : SiteObject
    {
        [SerializeField] ToolSlotTable table;

        public void Setup(ToolSlotTable t) => table = t;

        protected override bool Watches(string key) =>
            key == ToolCountLogic.HoverKey || key.StartsWith("rack.slot_") || (key.Contains(".bolt_") && key.EndsWith(".state")) || key == "tray.old_strips" || key == "workorder.decision";

        protected override void Refresh() => ToolCountLogic.Update(State, table);
    }
}
