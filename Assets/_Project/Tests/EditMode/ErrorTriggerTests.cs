using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.Procedures;
using PantographSim.Safety;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>R1.5b b4（5m）：第 5 部分補上的錯誤觸發（ErrorRules.json）：在課程的那一步，SafetyGuard（跳過退役規則）＋ErrorDetector，各觸發一次、不重複記。</summary>
    public class ErrorTriggerTests
    {
        sealed class Rig : System.IDisposable
        {
            public readonly SimState s; readonly ProcedureRunner runner; readonly ErrorDetector det; readonly CompositeActionGuard guard = new CompositeActionGuard(); readonly ProcedureDefinition prc;
            public readonly List<ErrorDetected> errors = new List<ErrorDetected>();
            public Rig(string course, string stepId)
            {
                var bus = new EventBus(); s = new SimState(bus);
                var src = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(course));
                prc = ScriptableObject.CreateInstance<ProcedureDefinition>();
                prc.procedureId = "PRC_Err"; prc.steps = new List<StepDefinition> { src.steps.First(x => x.stepId == stepId) }; prc.globalErrors = src.globalErrors;
                runner = new ProcedureRunner(prc, bus, s);
                det = new ErrorDetector(runner, bus, s);
                guard.Add(new SafetyGuard(bus, s, StepJsonImporter.LoadAll<SafetyRuleDefinition>(StepJsonImporter.SafetyFolder)) { SkipDeprecated = true });
                guard.Add(det);
                bus.Subscribe<ErrorDetected>(e => errors.Add(e));
                runner.Start();
            }
            public bool Try(string tool, string target) => guard.Check(new ActionRequest(tool, target, "click")).allowed;
            public int Count(string id) => errors.Count(e => e.errorId == id);
            public void Dispose() { det.Dispose(); runner.Dispose(); Object.DestroyImmediate(prc); }
        }

        [TestCase("daily", "STP_SI_05_Grounding")]
        [TestCase("periodic", "STP_SI_05_Grounding")]
        public void GroundHangOrder_HeadBeforeTerminal_BlockedOnce_OldRuleNotDoubled(string course, string step)
        {
            using (var r = new Rig(course, step))
            {
                r.s.Batch(() => { r.s.SetFloat(SimIds.PanHeadLowerDistance, 0f); r.s.SetBool(SimIds.Grounded, true); });
                Assert.IsFalse(r.Try(SimIds.ToolGroundingRod, SimIds.GroundClamp), "端子還沒夾就掛弓頭：擋下");
                Assert.AreEqual(1, r.Count("ERR_Global_GroundHangOrder"));
                Assert.AreEqual(0, r.Count("ERR_Ground_Order"), "R1 的 SR_Ground_Order 已退役，課程不再記");
                r.s.SetBool(SimIds.TerminalClamped, true);
                Assert.IsTrue(r.Try(SimIds.ToolGroundingRod, SimIds.GroundClamp), "先夾端子再掛：放行");
            }
        }

        [Test]
        public void HVDisconnect_WithoutSecondGround_Blocked()
        {
            using (var r = new Rig("periodic", "STP_RM_03_HVDisconnect"))
            {
                r.s.Batch(() => { r.s.Set("ip.pan_assy.hv_terminal.state", "Installed"); r.s.SetBool(SimIds.HeadClamped, true); });
                Assert.IsFalse(r.Try("", "ip.pan_assy.hv_terminal"), "短接地線沒掛就拆電纜：擋下");
                Assert.AreEqual(1, r.Count("ERR_Global_HVDisconnectWithoutSecondGround"));
                r.s.SetBool("ip.roof.hv_cable_ground.clamped", true);
                Assert.IsTrue(r.Try("", "ip.pan_assy.hv_terminal"), "兩支接地都掛著：放行");
            }
        }

        [TestCase("STP_RM_04_BaseUnbolt", "part.pan_base.bolt_2.wrong_socket", "ERR_RM04_WrongSocket")]
        [TestCase("STP_RM_03_HVDisconnect", "part.pan_assy.hv_bolt_1.wrong_socket", "ERR_RM03_WrongSocket")]
        [TestCase("STP_IN_07_BaseTorque", "tool.torque_wrench_l.range_blocked", "ERR_IN07_WrenchRange")]
        [TestCase("STP_IN_06_BasePreTighten", "part.pan_base.bolt_1.tighten_before_all", "ERR_IN06_TightenBeforeAllThreaded")]
        public void StepRule_FiresOncePerStep(string step, string key, string error)
        {
            using (var r = new Rig("periodic", step))
            {
                r.s.SetBool(key, true);
                Assert.AreEqual(1, r.Count(error), error);
                r.s.SetBool(key, false); r.s.SetBool(key, true);
                Assert.LessOrEqual(r.Count(error), 2, "同一步計分只算一次（Scoring 以步驟＋錯誤 ID 去重）");
            }
        }

        [Test]
        public void GlobalRules_NoDischarge_IRFail_RaiseUnderHook_CtrlNotConnected_HarnessUnclipped()
        {
            using (var r = new Rig("periodic", "STP_WC_04_Unlock"))
            {
                r.s.Batch(() => { r.s.SetBool("tool.megger.charged", true); r.s.SetBool("tool.megger.measured", true); r.s.SetFloat("tool.megger.reading", 20f); });
                Assert.AreEqual(1, r.Count("ERR_Global_NoDischargeAfterIR"), "沒放電就到 WC_04");
                Assert.AreEqual(1, r.Count("ERR_RT02_IRFail"), "< 100 MΩ 還要復電");
                r.s.Batch(() => { r.s.SetFloat("part.crane.beam_above_pan_m", 1.5f); r.s.SetFloat("part.crane.hook_cog_offset_cm", 10f); r.s.Set("ip.pan_assy.ctrl_connector.state", "Removed"); r.s.Set(SimIds.LiftCmd, "Up"); });
                Assert.AreEqual(1, r.Count("ERR_Global_RaiseUnderHook"), "吊鉤還在弓上方就升弓");
                Assert.AreEqual(1, r.Count("ERR_IN10_NotConnected"), "控制線沒接就升弓");
                r.s.SetBool("harness.unhooked", true);
                Assert.AreEqual(1, r.Count("ERR_Global_HarnessUnclipped"));
            }
        }

        /// <summary>terry 試玩：還沒放行就上車頂：練習記 Critical，同一次上車頂只記一次；離開再上再記；教學只提示；放行後不記。</summary>
        [Test]
        public void HoldPointSkipped_EnterRoofBeforeRelease()
        {
            var rule = ScriptableObject.CreateInstance<RoofAccessRule>();
            using (var r = new Rig("daily", "STP_SI_06_RoofEntry"))
            {
                bool on = false;
                void Floor(string f) { r.s.Set("xr.floor", f); PantographSim.Worksite.RoofAccess.CheckEntry(rule, r.s, ref on); }
                r.s.Set("training.mode", "practice");
                Floor("Roof"); Assert.AreEqual(1, r.Count("ERR_Global_HoldPointSkipped"));
                Floor("Roof"); Assert.AreEqual(1, r.Count("ERR_Global_HoldPointSkipped"), "同一次只記一次");
                Floor("Platform"); Assert.IsFalse(r.s.GetBool(rule.holdSkippedKey), "離開車頂放掉");
                Floor("Roof"); Assert.IsTrue(r.s.GetBool(rule.holdSkippedKey), "再上一次再判（同一步的重複由 ErrorDetector 累計次數）");
                Floor("Platform");
                r.s.Set("training.mode", "tutorial");
                Floor("Roof"); Assert.IsFalse(r.s.GetBool(rule.holdSkippedKey), "教學不記");
                Assert.AreEqual(rule.holdSkippedHint, r.s.Get("ui.toast"));
                Floor("Platform");
                r.s.Set("training.mode", "exam"); r.s.SetBool("roof.open", true);
                Floor("Roof"); Assert.IsFalse(r.s.GetBool(rule.holdSkippedKey), "放行後不記");
            }
        }

        [Test]
        public void ChecklistEval_Not()
        {
            var s = new SimState(new EventBus());
            Assert.IsTrue(ChecklistEval.Done(s, "!a.b"), "沒寫過＝不成立");
            s.SetBool("a.b", true);
            Assert.IsFalse(ChecklistEval.Done(s, "!a.b"));
            s.SetBool("a.b", false);
            Assert.IsTrue(ChecklistEval.Done(s, "!a.b&&!c.d"));
        }
    }
}
