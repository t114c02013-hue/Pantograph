using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Tests
{
    /// <summary>R1.5b b4（5d）：D31 胸前扣環（HarnessLogic）、攜帶式短接地線（GroundingLeadLogic、GroundingLeadTool），用場景同一種工具元件（BeltRig）。</summary>
    public class HarnessLeadTests
    {
        static void Send(string target, string text, string toolId = "") => Sim.Bus.Publish(new ActionRequested { targetId = target, text = text, toolId = toolId });

        [Test]
        public void ChestRing_StowUnhooksPlatform_RefusedOnRoof_HookNeedsTakeFirst()
        {
            using (var b = new BeltRig())
            {
                b.s.Batch(() => { b.s.SetBool(HarnessLogic.Roof, true); b.s.SetBool(HarnessLogic.Platform, true); b.s.SetFloat(HarnessLogic.Count, 2); });
                Send(SimIds.ToolHarness, "stow");
                Assert.IsFalse(b.s.GetBool(HarnessLogic.Stowed), "還扣在車頂防墜器：不能收");
                b.s.Batch(() => { b.s.SetBool(HarnessLogic.Roof, false); b.s.SetFloat(HarnessLogic.Count, 1); });
                Send(SimIds.ToolHarness, "stow");
                Assert.IsTrue(b.s.GetBool(HarnessLogic.Stowed));
                Assert.IsFalse(b.s.GetBool(HarnessLogic.Platform), "收到胸前＝平台掛點解開");
                Assert.IsFalse(b.s.GetBool("harness.unhooked"), "在平台收掛鉤不算車頂上解開");
                b.belt.ClickSlot(4);   // 拿著安全帶點掛點
                Send("ip.anchor.platform", "click", SimIds.ToolHarness);
                Assert.IsFalse(b.s.GetBool(HarnessLogic.Platform), "收在胸前：要先取下掛鉤");
                Send(SimIds.ToolHarness, "take");
                Send("ip.anchor.platform", "click", SimIds.ToolHarness);
                Assert.IsTrue(b.s.GetBool(HarnessLogic.Platform), "取下後扣上");
            }
        }

        /// <summary>桌面版「胸前扣環」「工具環」按鈕和 VR 伸手按握把送出同一個請求（BodyRings）：點一下收起／取下、掛上／拿下。</summary>
        [Test]
        public void BodyRingRequests_DesktopAndXr_Toggle()
        {
            using (var b = new BeltRig())
            {
                void Press(System.Func<ISimState, (string target, string text)> req) { var (t, x) = req(b.s); Send(t, x); }
                b.s.SetBool(HarnessLogic.Platform, true);
                Press(BodyRings.ChestRingRequest);
                Assert.IsTrue(b.s.GetBool(HarnessLogic.Stowed), "收起");
                Press(BodyRings.ChestRingRequest);
                Assert.IsFalse(b.s.GetBool(HarnessLogic.Stowed), "再按一次：取下");

                b.belt.ClickSlot(BeltRig.Table.FindTool(GroundingLeadLogic.ToolId).slot);
                Press(BodyRings.ToolRingRequest);
                Assert.IsTrue(b.s.GetBool(GroundingLeadLogic.Carried), "拿著短接地線按：掛上工具環");
                Press(BodyRings.ToolRingRequest);
                Assert.AreEqual(GroundingLeadLogic.ToolId, b.s.Get(SimIds.ActiveTool), "手空著按：拿下");
            }
        }

        [Test]
        public void GroundingLead_RingClampRemove_CannotReturnWhileInstalled()
        {
            using (var b = new BeltRig())
            {
                int slot = BeltRig.Table.FindTool(GroundingLeadLogic.ToolId).slot;
                b.belt.ClickSlot(slot);
                Send(GroundingLeadLogic.ToolId, "ring");
                Assert.IsTrue(b.s.GetBool(GroundingLeadLogic.Carried), "SI_06 a2：掛在安全帶工具環上");
                Assert.AreEqual(RackLogic.InUse, b.Slot(slot));
                Assert.AreEqual("", b.s.Get(SimIds.ActiveTool), "手空著");

                Send(GroundingLeadLogic.ToolId, "take");
                Assert.AreEqual(GroundingLeadLogic.ToolId, b.s.Get(SimIds.ActiveTool));
                Send(GroundingLeadLogic.CarBody, "click", GroundingLeadLogic.ToolId);
                Assert.IsTrue(b.s.GetBool(GroundingLeadLogic.ClampedKey(GroundingLeadLogic.CarBody)));
                Assert.IsFalse(b.s.GetBool(GroundingLeadLogic.Carried), "RM_00 夾上就不在身上");
                Send(GroundingLeadLogic.Cable, "click", GroundingLeadLogic.ToolId);
                Send(GroundingLeadLogic.ToolId, "pulltest");
                Assert.IsTrue(b.s.GetBool(GroundingLeadLogic.PullTestedKey));
                Assert.AreEqual(RackLogic.InUse, b.Slot(slot), "兩端接上、拉線確認後：留在場上");
                Assert.AreEqual("", b.s.Get(SimIds.ActiveTool));

                b.belt.ClickSlot(slot);
                Assert.AreEqual(RackLogic.InUse, b.Slot(slot), "還接在車頂上：不能從工具架收回");

                Send(GroundingLeadLogic.ToolId, "take");
                Send(GroundingLeadLogic.Cable, "click", GroundingLeadLogic.ToolId);
                Send(GroundingLeadLogic.CarBody, "click", GroundingLeadLogic.ToolId);
                Assert.IsFalse(GroundingLeadLogic.Installed(b.s));
                Send(GroundingLeadLogic.ToolId, "ring");
                Assert.IsTrue(b.s.GetBool(GroundingLeadLogic.Carried), "WC_03a c：掛回工具環");
                b.belt.ClickSlot(slot);
                Assert.AreEqual(RackLogic.OnRack, b.Slot(slot), "WC_02 d：掛回工具架");
                Assert.IsFalse(b.s.GetBool(GroundingLeadLogic.Carried), "掛回工具架就不在身上");
            }
        }
    }
}
