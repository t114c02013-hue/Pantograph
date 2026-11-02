using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Tools
{
    /// <summary>
    /// 驗電器：先點自檢器（ip.tool.voltage_tester）→ 綠燈＋嗶聲並記自檢時間；再點弓頭接地掛點 → 顯示「無電」。
    /// 沒自檢就量：一樣記 isolation.voltage_measured，但不算驗電完成（錯誤由步驟資料判定）。
    /// </summary>
    public class VoltageDetectorTool : ToolBase
    {
        public override void OnSelected()
        {
            if (!State.Has(Key("display"))) State.Set(Key("display"), "待自檢");
        }

        public override void OnClick(ActionRequested e)
        {
            if (e.targetId == SimIds.VoltageTester)
            {
                if (!Ask(e.targetId, "selftest")) return;
                IsolationLogic.SelfTest(State, SimClock.Now);
                ToolInventory.PlayBeep();
                Perform(e.targetId, "selftest");
            }
            else if (e.targetId == SimIds.GroundClamp)
            {
                if (!Ask(e.targetId, "test")) return;
                Perform(e.targetId, IsolationLogic.MeasureHead(State) ? "test:dead" : "test:unchecked");
            }
        }
    }
}
