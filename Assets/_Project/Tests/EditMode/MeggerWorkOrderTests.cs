using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Worksite;

namespace PantographSim.Tests
{
    /// <summary>R1.5b b4（5g）：高阻計絕緣測試、接地桿放電（MeggerLogic，用場景同一種工具元件）、作業單新欄位（異常欄、絕緣電阻、平台清點完成）。</summary>
    public class MeggerWorkOrderTests
    {
        static void Click(string target, string toolId) => Sim.Bus.Publish(new ActionRequested { targetId = target, text = "click", toolId = toolId });

        [Test]
        public void Megger_NeedsBothLeads_ReadingFromData_DischargeNeedsContactTime()
        {
            float clock = 0f; var old = SimClock.Source; SimClock.Source = () => clock;
            try
            {
                using (var b = new BeltRig())
                {
                    var rules = ToolRulesDef.Load();
                    b.belt.ClickSlot(BeltRig.Table.FindTool(MeggerLogic.ToolId).slot);
                    Click(MeggerLogic.PanPoint, MeggerLogic.ToolId);
                    Assert.IsNotNull(MeggerLogic.Test(b.s, rules), "只接一端：不能測");
                    Click(MeggerLogic.BodyPoint, MeggerLogic.ToolId);
                    Assert.AreEqual(2f, b.s.GetFloat(MeggerLogic.Count));
                    Sim.Bus.Publish(new ActionRequested { targetId = MeggerLogic.ToolId, text = "test" });
                    Assert.AreEqual(rules.meggerReadingMOhm, b.s.GetFloat(MeggerLogic.Reading), "讀值讀資料（示例值）");
                    Assert.IsTrue(b.s.GetBool(MeggerLogic.Charged));

                    b.belt.ClickSlot(BeltRig.Table.FindTool(MeggerLogic.ToolId).slot);   // 放回
                    Assert.AreEqual(0f, b.s.GetFloat(MeggerLogic.Count), "放下時收起測試線");
                    b.belt.ClickSlot(BeltRig.Table.FindTool(SimIds.ToolGroundingRod).slot);
                    Click(MeggerLogic.PanPoint, SimIds.ToolGroundingRod);
                    Assert.IsFalse(b.s.GetBool(SimIds.HeadClamped), "帶電時碰弓頭接地掛點＝放電，不是掛上");
                    clock += rules.dischargeSeconds / 2f; b.belt.Active.Tick();
                    Assert.IsTrue(b.s.GetBool(MeggerLogic.Charged), "還沒放完");
                    clock += rules.dischargeSeconds; b.belt.Active.Tick();
                    Assert.GreaterOrEqual(b.s.GetFloat(MeggerLogic.DischargeKey), rules.dischargeSeconds);
                    Assert.IsFalse(b.s.GetBool(MeggerLogic.Charged), "接觸夠久：放完");
                    Click(MeggerLogic.PanPoint, SimIds.ToolGroundingRod);   // 移開
                    Click(MeggerLogic.PanPoint, SimIds.ToolGroundingRod);
                    Assert.IsTrue(b.s.GetBool(SimIds.HeadClamped), "放完電再點：照原本掛上接地桿");
                }
            }
            finally { SimClock.Source = old; }
        }

        [Test]
        public void WorkOrder_Anomaly_IrReadingMismatch_PlatformCount()
        {
            var s = new SimState(new EventBus());
            Assert.IsFalse(WorkOrder.Write(s, null, WorkOrder.AnomalyField, "maybe"), "只能選無異常／有異常");
            Assert.IsTrue(WorkOrder.Write(s, null, WorkOrder.AnomalyField, "none"));
            Assert.IsTrue(s.GetBool("env.work_order.anomaly.checked"));
            s.SetFloat(MeggerLogic.Reading, 500f);
            Assert.IsTrue(WorkOrder.Write(s, null, WorkOrder.IrReadingField, "50"));
            Assert.IsTrue(s.GetBool("workorder.ir_reading.mismatch"), "和高阻計讀值不符");
            Assert.IsTrue(WorkOrder.Write(s, null, WorkOrder.IrReadingField, "500"));
            Assert.IsFalse(s.GetBool("workorder.ir_reading.mismatch"));
            Assert.IsTrue(WorkOrder.Write(s, null, WorkOrder.PlatformCountField, "true"));
            Assert.IsTrue(s.GetBool("workorder.toolcount_platform_confirmed"));
        }
    }
}
