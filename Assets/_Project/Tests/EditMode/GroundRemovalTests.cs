using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.Procedures;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>
    /// R1.5b b4 3d GroundRemovalTests（K2、D25）：ERR_WC03_RemoveOrder、ERR_Global_GroundRemovedWhileOnRoof 各測「觸發並擋下」「正確順序不觸發」；
    /// 車頂上拆 #1 另測「下車頂後拆就不觸發」。規則讀 ErrorRules.json（BlockWhen，動作前擋下）。兩門課都有。
    /// </summary>
    public class GroundRemovalTests
    {
        class Rig
        {
            public readonly EventBus bus = new EventBus();
            public readonly SimState s;
            public readonly ProcedureRunner runner;
            public readonly ErrorDetector det;
            public readonly List<ErrorDetected> errors = new List<ErrorDetected>();
            readonly ProcedureDefinition prc;

            public Rig(string course, string stepId)
            {
                var src = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(course));
                prc = ScriptableObject.CreateInstance<ProcedureDefinition>();
                prc.procedureId = "PRC_Ground"; prc.steps = new List<StepDefinition> { src.steps.First(x => x.stepId == stepId) };
                prc.globalErrors = src.globalErrors;
                s = new SimState(bus);
                runner = new ProcedureRunner(prc, bus, s);
                det = new ErrorDetector(runner, bus, s);
                bus.Subscribe<ErrorDetected>(e => errors.Add(e));
                runner.Start();
            }

            public bool Try(string targetId, string toolId = "tool.grounding_rod") => det.Check(new ActionRequest(toolId, targetId, "click")).allowed;
            public void Dispose() { det.Dispose(); runner.Dispose(); Object.DestroyImmediate(prc); }
        }

        const string Order = "ERR_WC03_RemoveOrder", OnRoof = "ERR_Global_GroundRemovedWhileOnRoof";

        [TestCase("periodic", "STP_WC_03b_RemoveGroundPlatform")]
        [TestCase("daily", "STP_WC_03_RemoveGround")]
        public void Ground1_TerminalBeforeHead_BlockedCritical_CorrectOrderAllowed(string course, string step)
        {
            var r = new Rig(course, step);
            try
            {
                r.s.Batch(() => { r.s.SetBool(SimIds.HeadClamped, true); r.s.SetBool(SimIds.TerminalClamped, true); });
                Assert.IsFalse(r.Try(SimIds.GroundTerminal), "弓頭端還掛著就先鬆端子：擋下");
                var e = r.errors.Single(x => x.errorId == Order);
                Assert.AreEqual(Severity.Critical, e.severity);
                Assert.IsTrue(e.blocks);
                r.errors.Clear();
                Assert.IsTrue(r.Try(SimIds.GroundClamp), "先取下弓頭端：放行");
                r.s.SetBool(SimIds.HeadClamped, false);
                Assert.IsTrue(r.Try(SimIds.GroundTerminal), "弓頭端取下後鬆端子：放行");
                Assert.IsEmpty(r.errors.Where(x => x.errorId == Order));
            }
            finally { r.Dispose(); }
        }

        [Test]
        public void Ground2_CarBodyBeforeCable_BlockedCritical_CorrectOrderAllowed()
        {
            var r = new Rig("periodic", "STP_WC_03a_RemoveGroundRoof");
            try
            {
                r.s.Batch(() => { r.s.SetBool("ip.roof.carbody_ground.clamped", true); r.s.SetBool("ip.roof.hv_cable_ground.clamped", true); });
                Assert.IsFalse(r.Try("ip.roof.carbody_ground", "tool.grounding_lead"), "電纜端子還掛著就先鬆車體接地座：擋下");
                Assert.AreEqual(Severity.Critical, r.errors.Single(x => x.errorId == Order).severity);
                r.errors.Clear();
                Assert.IsTrue(r.Try("ip.roof.hv_cable_ground", "tool.grounding_lead"));
                r.s.SetBool("ip.roof.hv_cable_ground.clamped", false);
                Assert.IsTrue(r.Try("ip.roof.carbody_ground", "tool.grounding_lead"), "先拆電纜端子再鬆車體接地座：放行");
                Assert.IsEmpty(r.errors.Where(x => x.errorId == Order));
            }
            finally { r.Dispose(); }
        }

        [TestCase("periodic", "STP_WC_03b_RemoveGroundPlatform")]
        [TestCase("daily", "STP_WC_03_RemoveGround")]
        public void Ground1_RemovedWhileAnchoredOnRoof_Blocked_AfterLeavingRoofAllowed(string course, string step)
        {
            var r = new Rig(course, step);
            try
            {
                r.s.Batch(() => { r.s.SetBool("harness.roof", true); r.s.SetBool(SimIds.HeadClamped, true); r.s.SetBool(SimIds.TerminalClamped, true); });
                Assert.IsFalse(r.Try(SimIds.GroundClamp), "車頂掛點扣著時取下 #1 弓頭端：擋下");
                var e = r.errors.Single(x => x.errorId == OnRoof);
                Assert.AreEqual(Severity.Critical, e.severity);
                r.errors.Clear();
                r.s.Batch(() => { r.s.SetBool("harness.roof", false); r.s.SetBool("harness.platform", true); });   // 下車頂
                Assert.IsTrue(r.Try(SimIds.GroundClamp), "下車頂後拆：放行");
                Assert.IsEmpty(r.errors.Where(x => x.errorId == OnRoof), "下車頂後拆就不觸發");
            }
            finally { r.Dispose(); }
        }

        [Test]
        public void StrapLeft_LeavingRoofWithHoldDownInstalled_Major()
        {
            var r = new Rig("periodic", "STP_WC_02_LeaveRoof");
            try
            {
                r.s.Batch(() => { r.s.Set("part.pan_assy.state", "Fastened"); r.s.Set("ip.pan_assy.hold_down.state", "Installed"); r.s.SetBool("harness.roof", true); });
                Assert.IsEmpty(r.errors.Where(x => x.errorId == "ERR_IN11_StrapLeft"), "還在車頂：不記");
                r.s.Batch(() => { r.s.SetBool("harness.roof", false); r.s.SetBool("harness.platform", true); });
                Assert.AreEqual(Severity.Major, r.errors.Single(x => x.errorId == "ERR_IN11_StrapLeft").severity, "固定帶沒拆就下車頂：Major（WC_05 升弓試驗會發現）");
            }
            finally { r.Dispose(); }
            var ok = new Rig("periodic", "STP_WC_02_LeaveRoof");
            try
            {
                ok.s.Batch(() => { ok.s.Set("part.pan_assy.state", "Fastened"); ok.s.Set("ip.pan_assy.hold_down.state", "Removed"); ok.s.SetBool("harness.roof", false); ok.s.SetBool("harness.platform", true); });
                Assert.IsEmpty(ok.errors.Where(x => x.errorId == "ERR_IN11_StrapLeft"), "已拆：不記");
            }
            finally { ok.Dispose(); }
        }
    }
}
