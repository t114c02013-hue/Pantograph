using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Tests
{
    /// <summary>R1.5b b4（D30）：桌面版工具列的腰包按鈕送出的請求，和 VR 伸手到腰間一樣：手上的上鎖掛牌組收進腰包，再點一次拿出來。</summary>
    public class DesktopPouchTests
    {
        static void Click() => Sim.Bus.Publish(new ActionRequested { targetId = PantographSim.UI.ToolBarView.PouchRequest.target, text = PantographSim.UI.ToolBarView.PouchRequest.text, toolId = "" });

        [Test]
        public void PouchButton_StowsAndTakesWorkInPouchTool()
        {
            using (var b = new BeltRig())
            {
                Assert.AreEqual(ToolSlotTable.WaistPouch, PantographSim.UI.ToolBarView.PouchRequest.target);
                b.belt.ClickSlot(1);   // SI_00 f：領取個人上鎖包，直接掛在腰間
                Assert.AreEqual(RackLogic.InPouch, b.Slot(1));
                Click();
                Assert.AreEqual(RackLogic.InHand, b.Slot(1), "手空著點腰包：拿出來");
                Assert.AreEqual(SimIds.ToolLockout, b.s.Get(SimIds.ActiveTool));
                Click();
                Assert.AreEqual(RackLogic.InPouch, b.Slot(1), "再點一次：收回腰包");
                Assert.AreEqual("", b.s.Get(SimIds.ActiveTool));
            }
        }
    }
}
