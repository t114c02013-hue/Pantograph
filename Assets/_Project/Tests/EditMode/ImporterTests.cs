using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Data;
using PantographSim.Editor;
using UnityEditor;

namespace PantographSim.Tests
{
    /// <summary>
    /// R1.5b b4 ImporterTests（第 1 部分：解析與展開）。只檢查資料本身的條件，不和任何寫死的總數比對（資深軟體 B 審查 2）；
    /// 展開後的總數寫進 Logs/R1.5b-assembly.txt 對照。
    /// </summary>
    public class ImporterTests
    {
        static List<StepsCsvImporter.Expanded> expanded;
        static List<StepsCsvImporter.StepMeta> steps;
        static List<string> errors;

        static void Load()
        {
            if (expanded != null) return;
            errors = new List<string>();
            expanded = StepsCsvImporter.Expand(StepsCsvImporter.LoadSubActionRows(), errors);
            steps = StepsCsvImporter.Steps(StepsCsvImporter.LoadStepRows(), errors);
        }

        [Test]
        public void Template_Forms_Parse()
        {
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, StepsCsvImporter.ParseTemplate("n=1→4").n);
            CollectionAssert.AreEqual(new[] { 1, 3, 2, 4 }, StepsCsvImporter.ParseTemplate("n=1,3,2,4").n);
            var t = StepsCsvImporter.ParseTemplate("side=L,R;n=1→4;interleave=a+b");
            CollectionAssert.AreEqual(new[] { "L", "R" }, t.sides); Assert.AreEqual(("a", "b"), t.interleave.Value); Assert.AreEqual(4, t.n.Count);
            Assert.Throws<System.FormatException>(() => StepsCsvImporter.ParseTemplate("x=1"));
        }

        [Test]
        public void StandMap_MustCoverAllN_OnlyWithTemplate()
        {
            var t = StepsCsvImporter.ParseTemplate("n=1→4");
            Assert.AreEqual("tp.b", StepsCsvImporter.ResolveStand("n1:tp.a;n2:tp.b;n3:tp.c;n4:tp.d", t, "", 2, out var e)); Assert.IsNull(e);
            StepsCsvImporter.ResolveStand("n1:tp.a;n2:tp.b", t, "", 1, out e); Assert.IsNotNull(e, "沒涵蓋 n3、n4 要報錯");
            StepsCsvImporter.ResolveStand("n1:tp.a", new StepsCsvImporter.Template(), "", 0, out e); Assert.IsNotNull(e, "沒有 templateN 不可寫對照表");
            Assert.AreEqual("tp.roof.outer", StepsCsvImporter.ResolveStand("L:tp.roof.inner;R:tp.roof.outer", StepsCsvImporter.ParseTemplate("side=L,R"), "R", 0, out e));
        }

        [Test]
        public void Csv_ExpandsWithoutFormatErrors()
        {
            Load();
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsNotEmpty(expanded);
        }

        [Test]
        public void NoDuplicateSubIds_OrderContinuous()
        {
            Load();
            foreach (var g in expanded.GroupBy(e => (e.course, e.stepId)))
            {
                var dup = g.GroupBy(e => e.subId).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
                Assert.IsEmpty(dup, $"{g.Key}：小動作 ID 重複 {string.Join(",", dup)}");
                CollectionAssert.AreEqual(Enumerable.Range(1, g.Count()).ToArray(), g.Select(e => e.order).ToArray(), $"{g.Key}：展開後順序要連續");
            }
            foreach (var g in StepsCsvImporter.LoadSubActionRows().GroupBy(r => r["stepId"]))
                CollectionAssert.AreEqual(Enumerable.Range(1, g.Count()).ToArray(), g.Select(r => int.Parse(r["order"])).OrderBy(x => x).ToArray(), $"{g.Key}：CSV 的 order 要連續");
        }

        [Test]
        public void StepsCsv_OrdersContiguousPerCourse()
        {
            Load();
            var p = steps.Where(s => s.orderPeriodic > 0).Select(s => s.orderPeriodic).OrderBy(x => x).ToArray();
            var d = steps.Where(s => s.orderDaily > 0).Select(s => s.orderDaily).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(Enumerable.Range(1, p.Length).ToArray(), p, "orderPeriodic 連號");
            CollectionAssert.AreEqual(Enumerable.Range(1, d.Length).ToArray(), d, "orderDaily 連號");
            foreach (var s in steps)
            {
                Assert.AreEqual(StepsCsvImporter.InCourse(s.courses, "periodic"), s.orderPeriodic > 0, $"{s.stepId}：courses 和 orderPeriodic 一致");
                Assert.AreEqual(StepsCsvImporter.InCourse(s.courses, "daily"), s.orderDaily > 0, $"{s.stepId}：courses 和 orderDaily 一致");
            }
        }

        [Test]
        public void SubCourses_WithinStepCourses_EveryStepHasSubActions()
        {
            Load();
            var meta = steps.ToDictionary(s => s.stepId);
            foreach (var e in expanded)
                Assert.IsTrue(meta.ContainsKey(e.stepId) && StepsCsvImporter.InCourse(meta[e.stepId].courses, e.course), $"{e}：小動作的課程不在步驟的課程裡（或 Steps.csv 沒有這一步）");
            foreach (var s in steps)
                foreach (var c in StepsCsvImporter.Courses.Where(c => StepsCsvImporter.InCourse(s.courses, c)))
                    Assert.IsTrue(expanded.Any(e => e.course == c && e.stepId == s.stepId), $"{s.stepId}（{c}）：沒有任何小動作");
        }

        [Test]
        public void StandPoints_SingleExistingStation_NotRackForSi04Si05()
        {
            Load();
            var set = AssetDatabase.LoadAssetAtPath<StationSet>("Assets/_Project/Modules/Worksite/Data/StationSet.asset");
            foreach (var e in expanded)
            {
                Assert.IsNotEmpty(e.standPointId, $"{e}：站點不可空白（K9）");
                Assert.IsFalse(e.standPointId.Contains(":") || e.standPointId.Contains(";"), $"{e}：匯出的站點要是單一站點");
                Assert.IsNotNull(set.Find(e.standPointId), $"{e}：站點 {e.standPointId} 不存在");
            }
            Assert.IsFalse(expanded.Any(e => (e.stepId.StartsWith("STP_SI_04") || e.stepId.StartsWith("STP_SI_05")) && e.standPointId == "tp.platform.rack"), "D25：SI_04、SI_05 沒有小動作在 tp.platform.rack");
        }

        [Test]
        public void ExpandedIds_TemplateAndInterleaveNaming()
        {
            Load();
            var rm04 = expanded.Where(e => e.course == "periodic" && e.stepId == "STP_RM_04_BaseUnbolt").Select(e => e.subId).ToList();
            CollectionAssert.AreEqual(new[] { "a", "b2", "c2", "b3", "c3", "b1", "c1", "b4", "c4", "d" }, rm04, "interleave=b+c、n=2,3,1,4：b2 c2 b3 c3 …（不再像 R1 那樣重新編字母）");
            Assert.IsTrue(expanded.Any(e => e.stepId == "STP_CSR_06_Loosen_L") && expanded.Any(e => e.stepId == "STP_CSR_06_Loosen_R"), "{side} 換成 L、R");
            var csr06 = expanded.Where(e => e.course == "daily" && e.stepId == "STP_CSR_06_Loosen_L").Select(e => e.subId).ToList();
            CollectionAssert.IsSubsetOf(new[] { "d1", "d2", "d3", "d4" }, csr06, "範本 d 展開成 d1–d4");
            var perN = expanded.Where(e => e.course == "periodic" && e.stepId == "STP_RM_04_BaseUnbolt" && e.subId.StartsWith("b")).ToDictionary(e => e.n, e => e.standPointId);
            Assert.AreEqual("tp.roof.base_w.s", perN[1]); Assert.AreEqual("tp.roof.base_e.n", perN[3]);
        }

        [Test]
        public void ToolIds_EachExists()
        {
            Load();
            var table = AssetDatabase.LoadAssetAtPath<ToolSlotTable>("Assets/_Project/Modules/Worksite/Data/ToolSlotTable.asset");
            foreach (var e in expanded)
                foreach (var t in e.toolId.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0))
                    Assert.IsNotNull(table.FindTool(t), $"{e}：工具 {t} 不存在");
        }
    }
}
