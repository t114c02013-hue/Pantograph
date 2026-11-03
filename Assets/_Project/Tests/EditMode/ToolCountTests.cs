using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Tests
{
    /// <summary>R1.5b b4（5i）：清點（ToolCountLogic）：清點的掛位讀 ToolSlotTable、逐格注視、台架工具盤、車頂遺留、待報廢箱條數。</summary>
    public class ToolCountTests
    {
        [Test]
        public void BagSlots_FromData_IncludeCleaningCloth()
        {
            var bag = ToolCountLogic.BagSlots(BeltRig.Table);
            Assert.Contains(BeltRig.Table.FindTool(SimIds.ToolCloth).slot, bag, "清潔布在工具袋清點清單（homeStorageId 讀資料）");
        }

        [Test]
        public void Viewed_OnlyWhileAllInBag_ClearedWhenAToolLeaves()
        {
            using (var b = new BeltRig())
            {
                var bag = ToolCountLogic.BagSlots(BeltRig.Table);
                int first = bag[0];
                b.s.Set(ToolCountLogic.HoverKey, "env.tool_rack.slot_" + first); ToolCountLogic.Update(b.s, BeltRig.Table);
                Assert.IsTrue(b.s.GetBool(ToolCountLogic.ViewedKey(first)));
                b.belt.ClickSlot(bag[1]); ToolCountLogic.Update(b.s, BeltRig.Table);
                Assert.IsFalse(b.s.GetBool(ToolCountLogic.ViewedKey(first)), "有工具離開工具袋：清點重做");
                b.s.Set(ToolCountLogic.HoverKey, "env.tool_rack.slot_" + first); ToolCountLogic.Update(b.s, BeltRig.Table);
                Assert.IsFalse(b.s.GetBool(ToolCountLogic.ViewedKey(first)), "工具沒收齊：注視不算");
            }
        }

        [Test]
        public void ToolTray_PutAndPickBack_CountFollows()
        {
            using (var b = new BeltRig())
            {
                int k = BeltRig.Table.FindTool(SimIds.ToolCaliper).slot;
                b.belt.ClickSlot(k);
                Assert.IsNull(ToolCountLogic.PutOnTray(b.s, BeltRig.Table));
                ToolCountLogic.Update(b.s, BeltRig.Table);
                Assert.AreEqual(1f, b.s.GetFloat(ToolCountLogic.TrayCount));
                Assert.AreEqual(RackLogic.OnTray, b.Slot(k));
                b.belt.ClickSlot(k);   // 從盤上拿回
                b.belt.ClickSlot(k);   // 收回工具袋
                ToolCountLogic.Update(b.s, BeltRig.Table);
                Assert.AreEqual(0f, b.s.GetFloat(ToolCountLogic.TrayCount));
                Assert.AreEqual(RackLogic.OnRack, b.Slot(k));
            }
        }

        [TestCase("both", 2f, true)]
        [TestCase("L", 1f, true)]
        [TestCase("pass", 0f, true)]
        [TestCase("both", 1f, false)]
        public void StripCountOk_MatchesDecision_LeftoverCountsLooseBolts(string decision, float inBox, bool ok)
        {
            var s = new SimState(new EventBus());
            s.Set("workorder.decision", decision); s.SetFloat("tray.old_strips", inBox);
            s.Set("part.pan_base.bolt_1.state", "Out");
            ToolCountLogic.Update(s, BeltRig.Table);
            Assert.AreEqual(ok, s.GetBool(ToolCountLogic.StripCountOk));
            Assert.AreEqual(1f, s.GetFloat(ToolCountLogic.Leftover), "轉到脫出還沒收的螺栓");
        }
    }
}
