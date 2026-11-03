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
    /// R1.5b K3（組長、驗收組長裁定）扭力不足：CSR_13、IN_07、IN_09 同一套。
    /// 教學模式擋下並提示、鎖到位才能往下；練習與考核放行並記 Under，進行中不跳提示（只進報表），報表寫出哪一顆、差多少 N·m；
    /// 離開這顆螺栓（開始鎖下一顆、換工具、步驟完成）時判一次，離開前補鎖到位不記；考核且 k3Ready=false 記 Major 並註明。
    /// </summary>
    public class K3SeverityTests
    {
        class Rig
        {
            public readonly EventBus bus = new EventBus();
            public readonly SimState s;
            public readonly ProcedureRunner runner;
            public readonly ErrorDetector detector;
            public readonly List<ErrorDetected> errors = new List<ErrorDetected>();
            readonly ProcedureDefinition prc;
            readonly HashSet<string> ordered = new HashSet<string>();

            public Rig(string course, string stepId, string mode, bool k3Ready = false)
            {
                var src = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(course));
                prc = ScriptableObject.CreateInstance<ProcedureDefinition>();
                prc.procedureId = "PRC_K3"; prc.steps = new List<StepDefinition> { src.steps.First(x => x.stepId == stepId) };   // 只跑這一步（小動作照 CSV）
                s = new SimState(bus);
                s.Set("training.mode", mode);
                runner = new ProcedureRunner(prc, bus, s);
                detector = new ErrorDetector(runner, bus, s) { Policy = ScriptableObject.CreateInstance<ExamPolicyDef>() };
                detector.Policy.k3Ready = k3Ready;
                bus.Subscribe<ErrorDetected>(e => errors.Add(e));
                runner.Start();
            }

            public void Dispose() { detector.Dispose(); runner.Dispose(); Object.DestroyImmediate(prc); Object.DestroyImmediate(detector.Policy); }

            public SubAction Current
            {
                get
                {
                    if (runner.Finished) return null;
                    var subs = ProcedureRunner.SubActions(runner.Current); int i = (int)s.GetFloat("procedure.sub_index");
                    return i < subs.Length ? subs[i] : null;   // 小動作都完成、步驟完成條件還沒成立
                }
            }

            /// <summary>鎖 boltId（扭力 nm、設定 setting），和 FastenerLogic 一樣一批寫入狀態、扭力與順序。</summary>
            public void Torque(string boltId, float nm, float setting) => s.Batch(() =>
            {
                s.SetFloat(boltId + ".torque_setting", setting);
                s.SetFloat(boltId + ".torque_Nm", nm);
                s.Set(boltId + ".state", "Torqued");
                if (ordered.Add(boltId))
                {
                    string key = boltId.Substring(0, boltId.LastIndexOf('.')) + ".torque_order", cur = s.Get(key), no = boltId.Substring(boltId.LastIndexOf('_') + 1);
                    s.Set(key, string.IsNullOrEmpty(cur) ? no : cur + "," + no);
                }
            });

            /// <summary>照順序做完這一步：螺栓的小動作用 torque(螺栓 ID) 決定扭力，其他小動作寫條件鍵（標記一併畫上）。</summary>
            public void Run(System.Func<string, float> torque, float setting)
            {
                for (int guard = 0; guard < 60 && !runner.Finished; guard++)
                {
                    var a = Current;
                    Assert.IsNotNull(a, $"小動作都完成了，步驟卻沒完成（{runner.Current.def.stepId}）");
                    if (a.condition.Contains("state=Torqued"))
                    {
                        Torque(a.targetId, torque(a.targetId), setting);
                        if (a.condition.Contains(".marked")) s.SetBool(a.targetId + ".marked", true);
                    }
                    else CourseFlowTests.Write(s, a.condition);
                    if (!runner.Finished && Current?.subId == a.subId) Assert.Fail($"{a.subId} 沒有前進：{a.condition}（{s.Get("procedure.gate_reason")}）");
                }
                Assert.IsTrue(runner.Finished, "這一步要完成");
            }

            public ErrorDetected? Find(string id) => errors.Where(e => e.errorId == id).Cast<ErrorDetected?>().FirstOrDefault();
        }

        const string Bolt1 = "ip.carbon_strip_L.bolt_1";

        [Test]
        public void Tutorial_UnderTorque_HoldsWithHint_UntilFixed()
        {
            var r = new Rig("daily", "STP_CSR_13_Torque_L", "tutorial");
            try
            {
                CourseFlowTests.Write(r.s, r.Current.condition);   // a 拿扳手
                CourseFlowTests.Write(r.s, r.Current.condition);   // b 套筒與設定
                var c1 = r.Current;
                Assert.AreEqual(Bolt1, c1.targetId);
                r.Torque(Bolt1, 20f, 25f);
                Assert.AreEqual(c1.subId, r.Current.subId, "扭力不足：擋下，不能往下做");
                Assert.AreEqual(r.detector.Policy.underHint, r.s.Get("procedure.gate_reason"), "提示：扭力不足，請鎖到扳手響");
                r.Torque(Bolt1, 25f, 25f);
                Assert.AreNotEqual(c1.subId, r.Current.subId, "鎖到位就往下");
                Assert.AreEqual("", r.s.Get("procedure.gate_reason"));
                r.Run(_ => 25f, 25f);
                Assert.IsNull(r.Find("ERR_CSR13_Under"), "教學模式擋下，不記錯");
            }
            finally { r.Dispose(); }
        }

        [Test]
        public void Exam_UnderTorque_PassesAndRecordsMajor_WithK3Note_Deferred()
        {
            var r = new Rig("daily", "STP_CSR_13_Torque_L", "exam");
            try
            {
                r.Run(b => b == Bolt1 ? 20f : 25f, 25f);
                var e = r.Find("ERR_CSR13_Under");
                Assert.IsNotNull(e, "考核：放行並記 Under");
                Assert.AreEqual(Severity.Major, e.Value.severity, "k3Ready=false 記 Major");
                StringAssert.Contains(ExamPolicyDef.K3Note, e.Value.what, "報表註明 K3 完成後升為 Critical");
                Assert.IsTrue(e.Value.deferred, "進行中不跳提示，只在最後報表");
                Assert.IsFalse(e.Value.blocks, "放行，不要求重做");
            }
            finally { r.Dispose(); }
        }

        [Test]
        public void Exam_K3Ready_RecordsCritical_NoNote()
        {
            var r = new Rig("daily", "STP_CSR_13_Torque_L", "exam", k3Ready: true);
            try
            {
                r.Run(b => b == Bolt1 ? 20f : 25f, 25f);
                var e = r.Find("ERR_CSR13_Under").Value;
                Assert.AreEqual(Severity.Critical, e.severity);
                StringAssert.DoesNotContain(ExamPolicyDef.K3Note, e.what);
            }
            finally { r.Dispose(); }
        }

        [Test]
        public void Practice_ReportShowsBoltAndShortfall_Deferred()
        {
            var r = new Rig("daily", "STP_CSR_13_Torque_L", "practice");
            try
            {
                r.Run(b => b.EndsWith("bolt_3") ? 21.5f : 25f, 25f);
                var e = r.Find("ERR_CSR13_Under").Value;
                StringAssert.Contains("3 號螺栓 21.5 N·m，差 1.5 N·m", e.what, "報表指出哪一顆、差多少");
                Assert.IsTrue(e.deferred, "步驟進行中不提示");
            }
            finally { r.Dispose(); }
        }

        [Test]
        public void Practice_K3NotReady_RecordsMajor_WithNote()
        {
            var r = new Rig("daily", "STP_CSR_13_Torque_L", "practice");
            try
            {
                r.Run(b => b == Bolt1 ? 20f : 25f, 25f);
                var e = r.Find("ERR_CSR13_Under").Value;
                Assert.AreEqual(Severity.Major, e.severity, "驗收組長：k3Ready=false 時練習也記 Major");
                StringAssert.Contains(ExamPolicyDef.K3Note, e.what, "練習的報表也註明");
            }
            finally { r.Dispose(); }
        }

        [Test]
        public void FixedBeforeLeavingBolt_NotRecorded()
        {
            var r = new Rig("daily", "STP_CSR_13_Torque_L", "exam");
            try
            {
                CourseFlowTests.Write(r.s, r.Current.condition);
                CourseFlowTests.Write(r.s, r.Current.condition);
                r.Torque(Bolt1, 20f, 25f);   // 不足（放行，c1 完成）
                r.Torque(Bolt1, 25f, 25f);   // 還沒離開這顆：補鎖到位
                r.Run(_ => 25f, 25f);
                Assert.IsNull(r.Find("ERR_CSR13_Under"), "離開前補到位不記錯");
            }
            finally { r.Dispose(); }
        }

        [Test]
        public void WrongSetting_AndUnder_BothRecorded()
        {
            var r = new Rig("daily", "STP_CSR_13_Torque_L", "exam");
            try
            {
                r.Run(b => b == Bolt1 ? 20f : 25f, 20f);
                Assert.IsNotNull(r.Find("ERR_CSR13_Setting"), "設定錯");
                Assert.IsNotNull(r.Find("ERR_CSR13_Under"), "最後不足：兩條都記");
            }
            finally { r.Dispose(); }
        }

        [Test]
        public void IN07_Under_Major_NoMarkMissing()
        {
            var r = new Rig("periodic", "STP_IN_07_BaseTorque", "exam");
            try
            {
                r.Run(b => b.EndsWith("bolt_1") ? 90f : 100f, 100f);
                var e = r.Find("ERR_IN07_Under");
                Assert.IsNotNull(e);
                Assert.AreEqual(Severity.Major, e.Value.severity);
                StringAssert.Contains("1 號螺栓 90.0 N·m，差 2.0 N·m", e.Value.what);
                Assert.IsNull(r.Find("ERR_IN07_MarkMissing"), "扭力不足時只記 Under，不另記 MarkMissing");
                Assert.IsNull(r.Find("ERR_IN07_Order"), "照對角順序 1→3→2→4 鎖");
            }
            finally { r.Dispose(); }
        }

        [Test]
        public void IN09_Under_Major()
        {
            var r = new Rig("periodic", "STP_IN_09_HVReconnect", "exam");
            try
            {
                r.Run(b => b.EndsWith("hv_bolt_2") ? 40f : 50f, 50f);
                var e = r.Find("ERR_IN09_Under");
                Assert.IsNotNull(e, "最後一顆在步驟完成時判");
                Assert.AreEqual(Severity.Major, e.Value.severity);
                StringAssert.Contains("2 號螺栓 40.0 N·m，差 6.0 N·m", e.Value.what);
            }
            finally { r.Dispose(); }
        }
    }
}
