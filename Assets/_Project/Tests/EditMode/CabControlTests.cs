using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Tests
{
    /// <summary>R1.5b b4（5f）：控制盤開關（CabControlLogic）與上鎖掛牌組（LockoutTagTool，用場景同一種工具元件）。</summary>
    public class CabControlTests
    {
        static void Send(string target, string toolId) => Sim.Bus.Publish(new ActionRequested { targetId = target, text = "click", toolId = toolId });

        [Test]
        public void Breaker_MustBeOffToLock_LockedCannotToggle_UnlockStowsTagToPouch()
        {
            using (var b = new BeltRig())
            {
                b.s.Set(CabControlLogic.Breaker, CabControlLogic.On);
                b.s.Set(CabControlLogic.CylinderPower, CabControlLogic.On);
                b.belt.ClickSlot(1); Sim.Bus.Publish(new ActionRequested { targetId = SimIds.WaistPouch, text = "click", toolId = "" });   // SI_00 f 進腰包，SI_02 a 拿出
                Assert.AreEqual(SimIds.ToolLockout, b.s.Get(SimIds.ActiveTool));
                Send(CabControlLogic.BreakerId, SimIds.ToolLockout);
                Assert.AreNotEqual("locked", b.s.Get("lockout.breaker"), "主斷路器還是 ON：不能鎖");
                Assert.IsNull(CabControlLogic.ToggleBreaker(b.s));
                Assert.AreEqual(CabControlLogic.Off, b.s.Get(CabControlLogic.Breaker));
                Send(CabControlLogic.BreakerId, SimIds.ToolLockout);
                Assert.AreEqual("locked", b.s.Get("lockout.breaker"));
                Assert.IsNotNull(CabControlLogic.ToggleBreaker(b.s), "已上鎖：不能切");
                Send("ip.cab.raise_switch", SimIds.ToolLockout);

                Assert.IsNull(CabControlLogic.ToggleCylinderPower(b.s));
                CabControlLogic.PressRaise(b.s);
                Assert.IsTrue(b.s.GetBool(CabControlLogic.RaiseTestDone), "電源已關：弓不動，確認完成");
                Assert.IsNotNull(CabControlLogic.ToggleCylinderPower(b.s), "還鎖著：不能恢復電源");

                Send("ip.cab.raise_switch", SimIds.ToolLockout);   // WC_04 b
                Send(CabControlLogic.BreakerId, SimIds.ToolLockout);   // WC_04 c
                Assert.AreEqual("open", b.s.Get("lockout.breaker"));
                Assert.AreEqual(RackLogic.InPouch, b.Slot(1), "解除主斷路器的鎖後，掛牌組收回腰包");
                Assert.AreEqual("", b.s.Get(SimIds.ActiveTool));
                Assert.IsNull(CabControlLogic.ToggleCylinderPower(b.s), "WC_04 d：鎖都解了，恢復電源");
                Assert.AreEqual(CabControlLogic.On, b.s.Get(CabControlLogic.CylinderPower));
            }
        }

        /// <summary>v3.10（SI_02 c／c2、d／d2）：先上鎖再掛牌；沒上鎖就掛牌——教學擋下不掛，練習照掛並記 tag_without_lock；解鎖連牌取下。</summary>
        [Test]
        public void LockThenTag_TagWithoutLock_TutorialBlocks_PracticeRecords()
        {
            using (var b = new BeltRig())
            {
                b.belt.ClickSlot(1); Sim.Bus.Publish(new ActionRequested { targetId = SimIds.WaistPouch, text = "click", toolId = "" });
                void Part(string p) => Sim.Bus.Publish(new ActionRequested { targetId = SimIds.ToolLockout, text = "part:" + p, toolId = "" });
                b.s.Set("training.mode", "tutorial");
                Part("tag"); Send("ip.cab.raise_switch", SimIds.ToolLockout);
                Assert.IsFalse(b.s.GetBool("lockout.raise_switch.tagged"), "教學：沒上鎖不能掛牌");
                Assert.AreEqual("先上鎖再掛牌", b.s.Get("ui.toast"));
                Assert.IsFalse(b.s.GetBool("lockout.raise_switch.tag_without_lock"), "教學不記錯");
                Part("lock"); Send("ip.cab.raise_switch", SimIds.ToolLockout);
                Assert.AreEqual("locked", b.s.Get("lockout.raise_switch"));
                Assert.IsFalse(b.s.GetBool("lockout.raise_switch.tagged"), "上鎖不等於掛牌");
                Part("tag"); Send("ip.cab.raise_switch", SimIds.ToolLockout);
                Assert.IsTrue(b.s.GetBool("lockout.raise_switch.tagged"));
                Assert.IsFalse(b.s.GetBool("lockout.raise_switch.tag_without_lock"));
                Part("lock"); Send("ip.cab.raise_switch", SimIds.ToolLockout);
                Assert.AreEqual("open", b.s.Get("lockout.raise_switch"));
                Assert.IsFalse(b.s.GetBool("lockout.raise_switch.tagged"), "解鎖連牌一起取下");

                b.s.Set("training.mode", "practice");
                Part("tag"); Send("ip.cab.raise_switch", SimIds.ToolLockout);
                Assert.IsTrue(b.s.GetBool("lockout.raise_switch.tagged"), "練習：照掛");
                Assert.IsTrue(b.s.GetBool("lockout.raise_switch.tag_without_lock"), "練習：記 ERR_SI02_TagWithoutLock 的鍵");
            }
        }

        [Test]
        public void R1_WithoutBreakerKey_LockStillAllowed()
        {
            using (var b = new BeltRig())
            {
                b.belt.ClickSlot(1); Sim.Bus.Publish(new ActionRequested { targetId = SimIds.WaistPouch, text = "click", toolId = "" });
                Send(CabControlLogic.BreakerId, SimIds.ToolLockout);
                Assert.AreEqual("locked", b.s.Get("lockout.breaker"), "R1 沒有 cab.breaker：照舊可以鎖");
            }
        }
    }
}
