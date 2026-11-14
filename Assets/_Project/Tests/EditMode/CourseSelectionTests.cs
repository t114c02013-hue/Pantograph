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

        /// <summary>terry 2026-11-14（32A、33A）：meta.json 沒有 flowVersion 的是舊版流程，紀錄清單標出來（只顯示）。</summary>
        [Test]
        public void OldFlow_NoFlowVersionInMeta_LabelledInList()
        {
            var root = Path.Combine(Path.GetTempPath(), "flow_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var (name, meta) in new[] { ("old", "{\"schemaVersion\":3,\"trainee\":\"a\"}"), ("new", "{\"schemaVersion\":3,\"trainee\":\"b\",\"flowVersion\":1}") })
                {
                    Directory.CreateDirectory(Path.Combine(root, name));
                    File.WriteAllText(Path.Combine(root, name, RecordFiles.Meta), meta);
                }
                var list = RecordingIndex.List(root);
                Assert.IsTrue(list.Single(e => e.name == "old").oldFlow, "沒有 flowVersion：舊版流程");
                Assert.IsFalse(list.Single(e => e.name == "new").oldFlow);
                Assert.IsTrue(RecordFiles.IsOldFlow(null));
                Assert.AreEqual("舊版流程", RecordFiles.OldFlowLabel);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        /// <summary>terry 2026-11-14（35A、37A）：選做小動作不擋後面——做了記 done；直接做下一個必做的記 skipped；兩種步驟都照樣完成。</summary>
        [TestCase(true, "done")]
        [TestCase(false, "skipped")]
        public void OptionalSubAction_DoneOrSkipped_NeverBlocks(bool doOptional, string expected)
        {
            var bus = new EventBus(); var s = new SimState(bus);
            var st = ScriptableObject.CreateInstance<StepDefinition>();
            st.stepId = "STP_OPT"; st.completion = new ChecklistAll(); st.hints = new HintSet();
            st.subActions = new[]
            {
                new SubAction { text = "必做 a", condition = "opt.a" },
                new SubAction { text = "選做 o", condition = "opt.o", optional = true },
                new SubAction { text = "必做 b", condition = "opt.b" },
            };
            var prc = ScriptableObject.CreateInstance<ProcedureDefinition>();
            prc.procedureId = "PRC_Opt"; prc.steps = new List<StepDefinition> { st };
            var runner = new ProcedureRunner(prc, bus, s);
            try
            {
                runner.Start();
                s.SetBool("opt.a", true);
                Assert.AreEqual(1f, s.GetFloat("procedure.sub_index"), "輪到選做");
                if (doOptional) { s.SetBool("opt.o", true); Assert.AreEqual(2f, s.GetFloat("procedure.sub_index")); }
                s.SetBool("opt.b", true);
                Assert.IsTrue(runner.Finished, "選做不擋完成");
                Assert.AreEqual(expected, s.Get(ProcedureRunner.OptionalKey("STP_OPT", 1)));
            }
            finally { runner.Dispose(); Object.DestroyImmediate(prc); Object.DestroyImmediate(st); }
        }

        /// <summary>選做欄：Y／選做＝選做；小動作表的四列選做只在日常課程，R1 的「拿回」在第 16 步拿新滑板之前。</summary>
        [Test]
        public void OptionalColumn_ParsedAndPlaced()
        {
            Assert.IsTrue(StepsCsvImporter.IsOptional("Y")); Assert.IsTrue(StepsCsvImporter.IsOptional("選做")); Assert.IsFalse(StepsCsvImporter.IsOptional(""));
            var daily = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath("daily"));
            var periodic = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath("periodic"));
            var d07 = daily.steps.First(x => x.stepId == "STP_CSR_07_RemoveBolts_L").subActions;
            Assert.IsTrue(d07[d07.Length - 2].optional, "日常：拆完 4 支、收扳手前倒進盤子");
            Assert.IsFalse(d07[d07.Length - 1].optional);
            Assert.IsTrue(daily.steps.First(x => x.stepId == "STP_CSR_11_Install_R").subActions[0].optional, "日常：裝新滑板前拿回");
            Assert.IsFalse(periodic.steps.SelectMany(x => x.subActions).Any(a => a.optional), "定期沒有");
            Assert.IsFalse(daily.steps.SelectMany(x => x.hints.checklist).Any(c => c.Contains("螺栓盤清點") || c.Contains("拿回腰間")), "選做不算完成");
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

        /// <summary>terry 2026-11-13（5B）：建置寫的 Logs 報告改成 LF。</summary>
        [Test]
        public void BuildReports_NormalizedToLf()
        {
            const string f = "Logs/_lf_check_tmp.txt";
            System.IO.File.WriteAllText(f, "第一行\r\n第二行\r\n");
            try
            {
                Assert.GreaterOrEqual(SliceSceneBuilder.NormalizeLogsToLf(), 1);
                Assert.AreEqual("第一行\n第二行\n", System.IO.File.ReadAllText(f));
                Assert.AreEqual(0, SliceSceneBuilder.NormalizeLogsToLf(), "已經是 LF 的不再改寫");
            }
            finally { System.IO.File.Delete(f); }
        }


        /// <summary>terry 2026-11-13（16A～25B）：R1／日常點螺栓盤＝倒進盤子、再點＝拿回袋子；定期課程沒有；計數、合計（判定用）不變。</summary>
        [Test]
        public void BoltPour_ToggleOnlyInR1AndDaily_CountUnchanged()
        {
            var r = ToolRulesDef.Load();
            CollectionAssert.AreEquivalent(new[] { "r1", "daily" }, r.boltPourCourses, "資料：R1 和日常");
            var s = new SimState(new EventBus());
            string key = PantographSim.Worksite.BoltTray.PouredKey("L");
            s.Set(SimIds.CourseId, "periodic");
            s.SetFloat(PantographSim.Worksite.BoltTray.CountKey("L"), 4);
            Assert.IsNull(PantographSim.Worksite.BoltTray.Pour(s, "L", r), "定期課程沒有");
            Assert.IsFalse(s.GetBool(key));
            s.Set(SimIds.CourseId, "daily");
            StringAssert.Contains("4", PantographSim.Worksite.BoltTray.Pour(s, "L", r));
            Assert.IsTrue(s.GetBool(key), "倒進盤子");
            Assert.AreEqual(4f, s.GetFloat(PantographSim.Worksite.BoltTray.CountKey("L")), "計數不變");
            StringAssert.Contains("拿回", PantographSim.Worksite.BoltTray.Pour(s, "L", r));
            Assert.IsFalse(s.GetBool(key), "拿回袋子");
            s.SetFloat(PantographSim.Worksite.BoltTray.CountKey("L"), 0);
            Assert.IsNull(PantographSim.Worksite.BoltTray.Pour(s, "L", r), "沒有螺栓不能倒");
        }

        /// <summary>terry 2026-11-13 選 A：驗電器改成直流 750 V 用（機身標示讀資料，判定不變）。</summary>
        [Test]
        public void VoltageDetector_IsDc750_InData()
        {
            var td = AssetDatabase.LoadAssetAtPath<ToolDefinition>("Assets/_Project/Modules/Tools/VoltageDetector/Data/TL_voltage_detector.asset");
            Assert.AreEqual("DC 750 V", td.Param("rating"));
            Assert.AreEqual("tool.voltage_detector", td.toolId, "ID 不變");
            StringAssert.Contains("直流 750 V", td.tutorial.pages[0].body);
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
