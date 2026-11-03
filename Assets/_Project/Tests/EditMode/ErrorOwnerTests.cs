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
    /// R1.5b b4（軟體工程裁定 2026-11-03）ErrorOwnerTests：ErrorRules.json 每條錯誤規則的 owner。
    /// ・owner 空白＝觸發已經做好（R1 既有或本段做完）；有 owner＝還沒做，要掛在「還沒結案」的段落，或標本版不適用（R2／n/a，status 要是 NotApplicable／Deprecated）。
    /// ・掛在已結案段落（Completed）的 stub 一律失敗；每段收工時把那一段加進 Completed（b5 收尾時 b4c、b4d、b4b、b4:p5 都要進來）。
    /// </summary>
    public class ErrorOwnerTests
    {
        /// <summary>已結案的段落：還有 stub 掛在這裡就失敗。</summary>
        static readonly string[] Completed = { "b4:p3", "b4:p3b", "b4:p3c", "b4:p3d", "b4:p4" };
        /// <summary>還沒結案、可以掛 stub 的段落（b4c:* 細項都算 b4c）。</summary>
        static readonly string[] Open = { "b4:p5", "b4c", "b4d", "b4b", "b5" };
        static readonly string[] NotApplicable = { "R2", "n/a" };

        static string Segment(string owner) => owner.StartsWith("b4c:") ? "b4c" : owner;

        [Test]
        public void EveryOwner_IsOpenSegment_OrMarkedNotApplicable_NoneUnderCompleted()
        {
            var bad = new List<string>();
            foreach (var d in StepsCsvImporter.LoadErrorRules().Values.Where(d => !string.IsNullOrEmpty(d.owner)))
            {
                string seg = Segment(d.owner);
                if (Completed.Contains(seg)) bad.Add($"{d.errorId}：還掛在已結案的段落 {d.owner}");
                else if (NotApplicable.Contains(d.owner)) { if (d.status != "NotApplicable" && d.status != "Deprecated") bad.Add($"{d.errorId}：owner {d.owner}（不適用）但 status 是 {d.status}"); }
                else if (!Open.Contains(seg)) bad.Add($"{d.errorId}：owner {d.owner} 不是還沒結案的段落");
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void Rulings_2026_11_03()
        {
            var defs = StepsCsvImporter.LoadErrorRules();
            Assert.AreEqual("Deprecated", defs["ERR_Global_RaiseWithHoldDown"].status, "併入 ERR_IN11_StrapLeft");
            Assert.AreEqual("n/a", defs["ERR_SI02_RaiseSwitchUnlocked"].owner);
            Assert.AreEqual("R2", defs["ERR_Crane_RiggedOnArm"].owner);
            Assert.AreEqual("R2", defs["ERR_IN09_Misaligned"].owner);
            Assert.AreEqual("b4d", defs["ERR_SI03_CylinderPowerOn"].owner);
            foreach (var id in new[] { "ERR_IN07_WrongSocket", "ERR_IN09_WrongSocket", "ERR_CSR13_WrongSocket" }) Assert.AreEqual("b4c:torque_socket", defs[id].owner, id);
        }

        /// <summary>審查 F2：Docs/R1.5b/b4c-stubs.md 的表格（小動作、目標、錯誤規則）也不可以有列掛在已結案的段落。</summary>
        [Test]
        public void StubsDoc_NoRowUnderCompleted()
        {
            var bad = System.IO.File.ReadAllLines("Docs/R1.5b/b4c-stubs.md")
                .Where(l => l.StartsWith("|") && l.Split('|').Select(c => c.Trim()).Any(c => Completed.Contains(c))).ToList();
            Assert.IsEmpty(bad, "還掛在已結案段落的 stub：\n" + string.Join("\n", bad));
        }

        /// <summary>審查 F1：停用的錯誤（Deprecated／NotApplicable）不放進兩門課任何一步的 errors，也不放進 globalErrors（執行期不判）。</summary>
        [Test]
        public void DisabledErrors_NotInAnyCourse()
        {
            var disabled = StepsCsvImporter.LoadErrorRules().Values.Where(d => StepsCsvImporter.Disabled(d.status)).Select(d => d.errorId).ToList();
            Assert.IsNotEmpty(disabled, "資料裡有停用的錯誤（例 ERR_Crane_RiggedOnArm）");
            foreach (var c in StepsCsvImporter.Courses)
            {
                var prc = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(c));
                var found = prc.globalErrors.Select(e => "global " + e.errorId).Concat(prc.steps.SelectMany(st => st.errors.Select(e => st.stepId + " " + e.errorId)))
                    .Where(x => disabled.Contains(x.Substring(x.IndexOf(' ') + 1))).ToList();
                Assert.IsEmpty(found, c + "：停用的錯誤還在：\n" + string.Join("\n", found));
            }
        }

        /// <summary>裁定 7a：步驟上的 WhenCondition 只在那一步啟用時判——條件在前一步就成立不記錯，到那一步才記。</summary>
        [Test]
        public void StepWhenCondition_OnlyWhileStepActive()
        {
            var bus = new EventBus(); var s = new SimState(bus);
            var src = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath("periodic"));
            var prc = ScriptableObject.CreateInstance<ProcedureDefinition>();
            prc.procedureId = "PRC_StepWhen";
            prc.steps = new List<StepDefinition> { src.steps.First(x => x.stepId == "STP_RM_03_HVDisconnect"), src.steps.First(x => x.stepId == "STP_RM_04_BaseUnbolt") };
            var runner = new ProcedureRunner(prc, bus, s);
            var det = new ErrorDetector(runner, bus, s);
            var errors = new List<ErrorDetected>();
            bus.Subscribe<ErrorDetected>(e => errors.Add(e));
            try
            {
                runner.Start();
                s.SetBool("part.pan_base.bolt_2.wrong_socket", true);
                Assert.AreEqual(0, errors.Count(e => e.errorId == "ERR_RM04_WrongSocket"), "前一步（RM_03）時成立：不記");
                var step = runner.Current;
                for (int i = 0; i < 40 && runner.Current == step; i++)
                {
                    var subs = ProcedureRunner.SubActions(step); int k = (int)s.GetFloat("procedure.sub_index");
                    if (k < subs.Length) CourseFlowTests.Write(s, subs[k].condition);
                    else s.Set("test.touch", i.ToString());
                }
                Assert.AreEqual("STP_RM_04_BaseUnbolt", runner.Current.def.stepId, "要進到 RM_04");
                Assert.AreEqual(1, errors.Count(e => e.errorId == "ERR_RM04_WrongSocket"), "到 RM_04 才記一次");
            }
            finally { det.Dispose(); runner.Dispose(); Object.DestroyImmediate(prc); }
        }
    }
}
