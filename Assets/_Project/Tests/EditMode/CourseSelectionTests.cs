using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PantographSim.App;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.Procedures;
using PantographSim.Recording;
using PantographSim.Safety;
using PantographSim.Training;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>
    /// R1.5b b4（5a）：課程選擇（預設讀 Courses.json，b4c 前是 r1）、錄影記 courseId（舊錄影沒有＝r1）、
    /// SR_Ground_RemoveOrder 退役（課程裡 #1 順序錯只記 ERR_WC03_RemoveOrder 1 條；R1 照舊擋下；舊 ID 還讀得到名稱）、兩門課跑到結算。
    /// </summary>
    public class CourseSelectionTests
    {
        [Test]
        public void ResolveCourse_ChosenWins_ElseDataDefault_ElseR1()
        {
            Assert.AreEqual("daily", SliceBootstrap.ResolveCourse("daily", "r1"));
            Assert.AreEqual("r1", SliceBootstrap.ResolveCourse("", "r1"));
            Assert.AreEqual("periodic", SliceBootstrap.ResolveCourse("", "periodic"));
            Assert.AreEqual(SimIds.CourseR1, SliceBootstrap.ResolveCourse(null, ""));
        }

        [Test]
        public void CoursesJson_DefaultIsR1_UntilB4c_DevNotesInData()
        {
            var f = StepsCsvImporter.LoadCourses();
            Assert.AreEqual(SimIds.CourseR1, f.defaultCourse, "b4c 做完前預設維持 R1");
            Assert.IsNotEmpty(f.r1Title);
            foreach (var c in f.courses) Assert.AreEqual("開發中", c.menuNote, c.courseId + "：b4c 前選單標「開發中」");
            StringAssert.Contains("daily|開發中", StepsCsvImporter.MenuNotes(f));
        }

        [Test]
        public void RecordingCourse_FromMeta_OldRecordingWithoutCourseIdIsR1()
        {
            var dir = Path.Combine(Path.GetTempPath(), "course_meta_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.AreEqual(SimIds.CourseR1, RecordFiles.CourseOf(dir), "沒有 meta.json：r1");
                File.WriteAllText(Path.Combine(dir, RecordFiles.Meta), "{\"schemaVersion\":3,\"mode\":\"tutorial\"}");
                Assert.AreEqual(SimIds.CourseR1, RecordFiles.CourseOf(dir), "舊錄影沒有 courseId：r1");
                File.WriteAllText(Path.Combine(dir, RecordFiles.Meta), "{\"schemaVersion\":3,\"courseId\":\"daily\"}");
                Assert.AreEqual("daily", RecordFiles.CourseOf(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        static SafetyRuleDefinition[] SafetyAssets() => StepJsonImporter.LoadAll<SafetyRuleDefinition>(StepJsonImporter.SafetyFolder).ToArray();

        [Test]
        public void SR_Ground_RemoveOrder_DeprecatedInData_ReplacedBy_ERR_WC03_IdKept()
        {
            var r = SafetyAssets().Single(x => x.ruleId == "SR_Ground_RemoveOrder");
            Assert.IsTrue(r.deprecated);
            Assert.AreEqual("ERR_WC03_RemoveOrder", r.replacedBy);
            Assert.AreEqual("ERR_Ground_RemoveOrder", r.errorId, "ID 不刪、不改");
            Assert.IsNotEmpty(ErrorNames.Of("ERR_Ground_RemoveOrder"), "舊錄影引用的 ID 照樣讀得到名稱");
        }

        /// <summary>課程裡 #1 弓頭端還掛著就先鬆端子：SafetyGuard（跳過退役規則）＋ErrorDetector，只記 ERR_WC03_RemoveOrder 1 條。R1（不跳過）照舊由 SR 擋下。</summary>
        [TestCase("daily", "STP_WC_03_RemoveGround")]
        [TestCase("periodic", "STP_WC_03b_RemoveGroundPlatform")]
        public void Course_Ground1WrongOrder_RecordsOnlyOneError(string course, string stepId)
        {
            var bus = new EventBus(); var s = new SimState(bus);
            var src = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(course));
            var prc = ScriptableObject.CreateInstance<ProcedureDefinition>();
            prc.procedureId = "PRC_Order"; prc.steps = new List<StepDefinition> { src.steps.First(x => x.stepId == stepId) }; prc.globalErrors = src.globalErrors;
            var runner = new ProcedureRunner(prc, bus, s);
            var det = new ErrorDetector(runner, bus, s);
            var guard = new CompositeActionGuard();
            guard.Add(new SafetyGuard(bus, s, SafetyAssets()) { SkipDeprecated = true });
            guard.Add(det);
            var errors = new List<ErrorDetected>();
            bus.Subscribe<ErrorDetected>(e => errors.Add(e));
            try
            {
                runner.Start();
                s.Batch(() => { s.SetBool(SimIds.HeadClamped, true); s.SetBool(SimIds.TerminalClamped, true); });
                Assert.IsFalse(guard.Check(new ActionRequest(SimIds.ToolGroundingRod, SimIds.GroundTerminal, "click")).allowed, "擋下");
                Assert.AreEqual(1, errors.Count, string.Join(",", errors.Select(e => e.errorId)));
                Assert.AreEqual("ERR_WC03_RemoveOrder", errors[0].errorId);
            }
            finally { det.Dispose(); runner.Dispose(); Object.DestroyImmediate(prc); }

            var bus1 = new EventBus(); var s1 = new SimState(bus1);
            var r1 = new SafetyGuard(bus1, s1, SafetyAssets());
            s1.Batch(() => { s1.SetBool(SimIds.HeadClamped, true); s1.SetBool(SimIds.TerminalClamped, true); });
            Assert.AreEqual("ERR_Ground_RemoveOrder", r1.Check(new ActionRequest(SimIds.ToolGroundingRod, SimIds.GroundTerminal, "click")).errorId, "R1 的 25 步照舊判");
        }

        /// <summary>terry 2026-11-13 選「加快」：只有後台代做時升降、扭力加快，學員操作不變。</summary>
        [Test]
        public void AutoRunScale_OnlyDuringAutoRun()
        {
            Assert.AreEqual(1f, ToolRulesDef.AutoRunScale(false, 5f), "學員操作不變");
            Assert.AreEqual(5f, ToolRulesDef.AutoRunScale(true, 5f));
            Assert.AreEqual(1f, ToolRulesDef.AutoRunScale(true, 0.2f), "倍數填錯也不會變慢");
            var r = ToolRulesDef.Load();
            Assert.GreaterOrEqual(r.autoRunLiftSpeed, 1f); Assert.GreaterOrEqual(r.autoRunTorqueSpeed, 1f);
        }

        /// <summary>terry 2026-11-13：後台面板代做進度（開場算 0、目標那一步算滿格、同一步停太久顯示卡住和原因）。</summary>
        [Test]
        public void AutoRunProgress_CountsStepsAndReportsStuck()
        {
            var r = ScriptableObject.CreateInstance<ToolRulesDef>();
            var (f0, t0) = SliceBootstrap.AutoRunProgress(-1, 12, "", 1f, "", r);
            Assert.AreEqual(0f, f0); Assert.AreEqual("代做中 0／12：開場", t0);
            var (f5, t5) = SliceBootstrap.AutoRunProgress(5, 12, "第 6 步 預鬆螺栓", 1f, "", r);
            Assert.AreEqual(5f / 12f, f5, 1e-4f); Assert.AreEqual("代做中 5／12：第 6 步 預鬆螺栓", t5);
            Assert.AreEqual(1f, SliceBootstrap.AutoRunProgress(12, 12, "x", 0f, "", r).fraction, "到目標滿格");
            StringAssert.Contains("弓頭還沒降到綠框", SliceBootstrap.AutoRunProgress(0, 12, "第 1 步 降弓並確認", r.autoRunStuckSeconds, "弓頭還沒降到綠框", r).text);
            StringAssert.Contains(r.autoRunStuckUnknown, SliceBootstrap.AutoRunProgress(0, 12, "第 1 步", r.autoRunStuckSeconds + 5f, "", r).text, "沒有原因也要說去哪裡查");
            Object.DestroyImmediate(r);
        }
    }
}
