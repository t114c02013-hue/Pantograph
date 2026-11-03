using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>
    /// R1.5b b4 ToolStowTests（負責人裁定：放開握把）：ToolRulesDef.gripReleaseDrops=true 時放開就掉，regrabGraceSec 內抓回不記；
    /// 超過寬限練習、考核記 Major ERR_Tool_NotStowedOnRoof，教學只提示；同一步同一件只記一次，撿回來也不消掉。false＝留在手上只提示。
    /// </summary>
    public class ToolStowTests
    {
        static BeltRig Rig(string mode, bool drops = true)
        {
            var b = new BeltRig();
            b.belt.Rules = ScriptableObject.CreateInstance<ToolRulesDef>();
            b.belt.Rules.gripReleaseDrops = drops;
            b.s.Set("training.mode", mode);
            b.s.Set("procedure.current", "STP_IN_07_BaseTorque");
            b.belt.ClickSlot(6);
            return b;
        }

        static int NotStowed(BeltRig b) => b.errors.Count(e => e.errorId == "ERR_Tool_NotStowedOnRoof");

        [Test]
        public void RegrabWithinGrace_NoError()
        {
            using (var b = Rig("exam"))
            {
                float t0 = SimClock.Now;
                b.belt.Release(true);
                b.belt.TickRelease(t0 + 1.0f);
                b.belt.ClickSlot(6);   // 1.5 s 內抓回
                b.belt.TickRelease(t0 + 5f);
                Assert.AreEqual(0, NotStowed(b), "寬限內抓回不算");
                Assert.AreEqual(RackLogic.InHand, b.Slot(6));
            }
        }

        [Test]
        public void PastGrace_Major_RegrabDoesNotClear()
        {
            using (var b = Rig("practice"))
            {
                float t0 = SimClock.Now;
                b.belt.Release(true);
                b.belt.TickRelease(t0 + 1.4f);
                Assert.AreEqual(0, NotStowed(b), "還在寬限內");
                b.belt.TickRelease(t0 + 1.6f);
                Assert.AreEqual(1, NotStowed(b), "超過 1.5 s 記一次");
                Assert.AreEqual(Severity.Major, b.errors.Single(e => e.errorId == "ERR_Tool_NotStowedOnRoof").severity);
                b.belt.ClickSlot(6);
                Assert.AreEqual(1, NotStowed(b), "撿回來也不消掉");
            }
        }

        [Test]
        public void Tutorial_HintOnly_NoError()
        {
            using (var b = Rig("tutorial"))
            {
                b.belt.Release(true);
                b.belt.TickRelease(SimClock.Now + 2f);
                Assert.AreEqual(0, NotStowed(b), "教學模式只提示");
                StringAssert.Contains(b.belt.Rules.hangingHint, b.s.Get("ui.toast"));
            }
        }

        [Test]
        public void RepeatedRelease_SameToolSameStep_RecordedOnce()
        {
            using (var b = Rig("exam"))
            {
                float t0 = SimClock.Now;
                b.belt.Release(true); b.belt.TickRelease(t0 + 2f);
                b.belt.ClickSlot(6); b.belt.Release(true); b.belt.TickRelease(t0 + 5f);
                Assert.AreEqual(1, NotStowed(b), "同一步同一件工具重複放開只記一次");
            }
        }

        [Test]
        public void GripReleaseDropsFalse_KeepsInHand_HintOnly()
        {
            using (var b = Rig("exam", drops: false))
            {
                b.belt.Release(true);
                b.belt.TickRelease(SimClock.Now + 5f);
                Assert.AreEqual(RackLogic.InHand, b.Slot(6), "舊做法：工具留在手上");
                Assert.AreEqual("tool.socket_wrench", b.s.Get(SimIds.ActiveTool));
                Assert.AreEqual(b.belt.Rules.keepHint, b.s.Get("ui.toast"), "提示「把手伸到工具架／工具袋」");
                Assert.AreEqual(0, NotStowed(b), "不記錯");
            }
        }
    }
}
