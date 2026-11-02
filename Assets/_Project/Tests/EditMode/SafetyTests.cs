using System.Collections.Generic;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Safety;
using UnityEngine;

namespace PantographSim.Tests
{
    public class SafetyTests
    {
        EventBus bus; SimState state; SafetyGuard guard; List<ErrorDetected> errors; List<ActionBlocked> blocked;

        static SafetyRuleDefinition Rule(SafetyRuleKind kind, string id)
        {
            var r = ScriptableObject.CreateInstance<SafetyRuleDefinition>();
            r.kind = kind; r.errorId = id; r.what = "w"; r.why = "y"; r.fix = "f";
            return r;
        }

        [SetUp]
        public void SetUp()
        {
            bus = new EventBus(); state = new SimState(bus);
            guard = new SafetyGuard(bus, state, new[]
            {
                Rule(SafetyRuleKind.NotLowered, "ERR_Global_NotLowered"), Rule(SafetyRuleKind.NotGrounded, "ERR_Global_NotGrounded"),
                Rule(SafetyRuleKind.GroundOrder, "ERR_Ground_Order"), Rule(SafetyRuleKind.NoVoltageTest, "ERR_Ground_NoVoltageTest"),
                Rule(SafetyRuleKind.RemoveOrder, "ERR_Ground_RemoveOrder")
            });
            errors = new List<ErrorDetected>(); blocked = new List<ActionBlocked>();
            bus.Subscribe<ErrorDetected>(e => errors.Add(e));
            bus.Subscribe<ActionBlocked>(e => blocked.Add(e));
        }

        void Lowered() { state.SetFloat(SimIds.PanHeadLowerDistance, 0.3f); }
        void GroundedAndSteps() { state.SetBool(SimIds.Grounded, true); state.SetBool("step.STP_SI_04_VoltageTest.done", true); state.SetBool("step.STP_SI_05_Grounding.done", true); }
        GuardResult Do(string tool, string target, string verb = "click") => guard.Check(new ActionRequest(tool, target, verb));

        void AssertBlocked(GuardResult r, string id)
        {
            Assert.IsFalse(r.allowed);
            Assert.AreEqual(id, r.errorId);
            Assert.AreEqual(id, errors[errors.Count - 1].errorId);
            Assert.AreEqual(Severity.Critical, errors[errors.Count - 1].severity);
            Assert.AreEqual(id, blocked[blocked.Count - 1].text);
        }

        [Test] public void E1_NotLowered_Blocks()
        {
            state.SetFloat(SimIds.PanHeadLowerDistance, 40f);
            AssertBlocked(Do("tool.socket_wrench", "ip.carbon_strip_L.bolt_1"), "ERR_Global_NotLowered");
        }

        [Test] public void E1_CheckPointNotInPosition_BlocksStateChangingActions()
        {
            state.SetFloat(SimIds.PanHeadLowerDistance, 40f);
            state.SetFloat(SimIds.PanHeadLowerDistance, 3.5f);
            AssertBlocked(Do("", "part.carbon_strip_L", "remove"), "ERR_Global_NotLowered");
            AssertBlocked(Do("tool.socket_wrench", "ip.carbon_strip_L.bolt_1", "rotate"), "ERR_Global_NotLowered");
        }

        [Test] public void E1_InspectPanHead_Allowed_EvenRaised()
        {
            state.Set(SimIds.PanHeadPose, SimIds.PoseRaised);
            Assert.IsTrue(Do("", "part.pan_head", "inspect").allowed);
        }

        [Test] public void SI01_BareClickOnPanBody_WhenLowered_NoError()
        {
            // SI_01 尚未完成、弓已降下：徒手點弓頭或滑板只是查看，不可觸發 E1
            state.Set(SimIds.PanHeadPose, SimIds.PoseLowered);
            Assert.IsTrue(Do("", "part.pan_head", "click").allowed);
            Assert.IsTrue(Do("", "part.carbon_strip_L", "click").allowed);
            Assert.IsTrue(Do("", "ip.carbon_strip_L.bolt_2", "click").allowed);
            Assert.AreEqual(0, errors.Count);
        }

        [Test] public void E1_BareClickWhileRaised_IsLookOnly_Allowed()
        {
            state.SetFloat(SimIds.PanHeadLowerDistance, 40f);
            Assert.IsTrue(Do("", "part.carbon_strip_L", "click").allowed, "徒手點選查看一律放行");
            Assert.AreEqual(0, errors.Count);
        }

        [Test] public void SI04_VoltageTestOnClamp_NotBlockedByGrounding()
        {
            Lowered();
            Assert.IsTrue(Do("tool.voltage_detector", SimIds.GroundClamp, "test").allowed, "驗電點的是弓頭接地掛點，不屬於弓體，不受未接地規則限制");
            Assert.IsTrue(Do("tool.voltage_detector", SimIds.VoltageTester, "selftest").allowed);
        }

        [Test] public void E2_NotGrounded_Blocks_And_Grounded_Allows()
        {
            Lowered();
            AssertBlocked(Do("tool.caliper", "ip.carbon_strip_L.measure_mid", "measure"), "ERR_Global_NotGrounded");
            state.SetBool(SimIds.Grounded, true);
            AssertBlocked(Do("tool.caliper", "ip.carbon_strip_L.measure_mid", "measure"), "ERR_Global_NotGrounded");   // SI_04/SI_05 尚未完成
            GroundedAndSteps();
            Assert.IsTrue(Do("tool.caliper", "ip.carbon_strip_L.measure_mid", "measure").allowed);
        }

        [Test] public void E13_TerminalBeforeVoltageTest_Blocks_ThenAllows()
        {
            Lowered();
            AssertBlocked(Do("tool.grounding_rod", SimIds.GroundTerminal), "ERR_Ground_NoVoltageTest");
            state.SetBool(SimIds.VoltageTested, true);
            Assert.IsTrue(Do("tool.grounding_rod", SimIds.GroundTerminal).allowed);
        }

        [Test] public void E3_HeadBeforeTerminal_Blocks_ThenAllows()
        {
            Lowered(); state.SetBool(SimIds.VoltageTested, true);
            AssertBlocked(Do("tool.grounding_rod", SimIds.GroundClamp), "ERR_Ground_Order");
            state.SetBool(SimIds.TerminalClamped, true);
            Assert.IsTrue(Do("tool.grounding_rod", SimIds.GroundClamp).allowed);
        }

        [Test] public void E16_RemoveTerminalWhileHeadClamped_Blocks_ThenAllows()
        {
            Lowered(); state.SetBool(SimIds.VoltageTested, true);
            state.SetBool(SimIds.TerminalClamped, true); state.SetBool(SimIds.HeadClamped, true);
            AssertBlocked(Do("tool.grounding_rod", SimIds.GroundTerminal), "ERR_Ground_RemoveOrder");
            state.SetBool(SimIds.HeadClamped, false);
            Assert.IsTrue(Do("tool.grounding_rod", SimIds.GroundTerminal).allowed);
        }

        [Test] public void StepGate_BlocksCsrUntilGrounded()
        {
            Lowered();
            Assert.IsFalse(guard.CanComplete("STP_CSR_03_Measure_L", out _));
            Assert.IsTrue(guard.CanComplete("STP_SI_04_VoltageTest", out _));
            GroundedAndSteps();
            Assert.IsTrue(guard.CanComplete("STP_CSR_03_Measure_L", out _));
        }
    }
}
