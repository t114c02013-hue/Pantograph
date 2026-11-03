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

        // ---------- 第 2 部分：完成條件對應、目標、產生的資產 ----------

        static List<StepsCsvImporter.Resolved> resolved;
        static List<string> resolveErrors;

        static void LoadResolved()
        {
            Load();
            if (resolved != null) return;
            resolveErrors = new List<string>();
            resolved = StepsCsvImporter.Resolve(expanded, StepsCsvImporter.LoadR1(), StepsCsvImporter.LoadTables(), resolveErrors);
        }

        [Test]
        public void EveryRow_HasCompletionRule_TargetsExistInScene()
        {
            LoadResolved();
            Assert.IsEmpty(resolveErrors, string.Join("\n", resolveErrors));
            Assert.AreEqual(expanded.Count, resolved.Count, "每個展開的小動作都要有完成條件");
            Assert.IsTrue(resolved.All(r => !string.IsNullOrEmpty(r.rule)));
            var tools = new HashSet<string>(AssetDatabase.LoadAssetAtPath<ToolSlotTable>("Assets/_Project/Modules/Worksite/Data/ToolSlotTable.asset").slots.Select(s => s.toolId));
            StationTests.WithScene(scene =>
            {
                var errs = new List<string>();
                StepsCsvImporter.CheckTargets(expanded, StepsCsvImporter.SceneIds(scene), tools, StepsCsvImporter.LoadTables(), errs);
                Assert.IsEmpty(errs, string.Join("\n", errs));
            });
        }

        [Test]
        public void Unmapped_MissingPlaceholder_MissingTarget_FailWithLineNumbers()
        {
            var rows = StepsCsvImporter.Rows("stepId,subId,order,instruction,targetId,toolId,condition,courses,templateN,standPointId_periodic,standPointId_daily\n" +
                "STP_X,a,1,做某事,env.nowhere,,沒有這種條件,both,,tp.roof.inner,tp.roof.inner\n" +
                "STP_X,b,2,拿東西,env.tool_rack.slot_6,,手持套筒扳手,both,,tp.roof.inner,tp.roof.inner\n");
            var errs = new List<string>();
            var ex = StepsCsvImporter.Expand(rows, errs);
            var t = StepsCsvImporter.LoadTables();
            var res = StepsCsvImporter.Resolve(ex, new Dictionary<string, StepJson>(), t, errs);
            Assert.IsEmpty(res, "對不到就不產生，不可以填預設值");
            Assert.IsTrue(errs.Any(e => e.Contains("第 2 行") && e.Contains("三層都對不到") && e.Contains("沒有這種條件")), string.Join("\n", errs));
            Assert.IsTrue(errs.Any(e => e.Contains("第 3 行") && e.Contains("{tool}")), "規則要 {tool}、這一列沒有工具：也要失敗");
            errs.Clear();
            StepsCsvImporter.CheckTargets(ex, new[] { "env.tool_rack.slot_6" }, new string[0], t, errs);
            Assert.AreEqual(2, errs.Count, "兩門課各一次");
            Assert.IsTrue(errs.All(e => e.Contains("第 2 行") && e.Contains("env.nowhere")), string.Join("\n", errs));
        }

        [Test]
        public void R1Reuse_TargetMismatch_Fails_ContainerOrEmptyOk()
        {
            var rows = StepsCsvImporter.Rows("stepId,subId,order,instruction,targetId,toolId,condition,courses,templateN,standPointId_periodic,standPointId_daily\n" +
                "STP_Y,a,1,插進來的一列,env.inserted,,隨便,daily,,tp.roof.inner,tp.roof.inner\n" +
                "STP_Y,b,2,看作業單讀數,env.work_order.readings_L,,隨便,daily,,tp.roof.inner,tp.roof.inner\n" +
                "STP_Y,c,3,換套筒,tool.socket_wrench（套筒選單）,,隨便,daily,,tp.roof.inner,tp.roof.inner\n");
            var errs = new List<string>();
            var r1 = new Dictionary<string, StepJson> { { "STP_Y", new StepJson { stepId = "STP_Y", subActions = new[] {
                new SubActionJson { target = "env.tool_rack.slot_6", done = "k1" }, new SubActionJson { target = "env.work_order", done = "k2" }, new SubActionJson { target = "", done = "k3" } } } } };
            var res = StepsCsvImporter.Resolve(StepsCsvImporter.Expand(rows, errs), r1, StepsCsvImporter.LoadTables(), errs);
            Assert.AreEqual(1, errs.Count, string.Join("\n", errs));
            Assert.IsTrue(errs[0].Contains("第 2 行") && errs[0].Contains("錯位"), "中間插一列：位置對不上就失敗，不可以默默沿用");
            CollectionAssert.AreEqual(new[] { "k2", "k3" }, res.Select(r => r.rule).ToArray(), "上層容器（env.work_order）或 R1 空白目標照常沿用");
        }

        [Test]
        public void R1Reuse_SpotCheck_CSR06_CSR11_CSR12_SameJudgement()
        {
            LoadResolved();
            var r1 = StepsCsvImporter.LoadR1();
            var courses = StepsCsvImporter.BuildAll(new string[0], new List<string>(), out _);
            foreach (var id in new[] { "STP_CSR_06_Loosen", "STP_CSR_11_Install", "STP_CSR_12_PreTighten" })
                foreach (var side in new[] { "L", "R" })
                {
                    var src = r1[id];
                    var mine = resolved.Where(r => r.e.course == "daily" && r.e.stepId == id + "_" + side).OrderBy(r => r.e.order).ToList();
                    int rOnly = side == "R" && id == "STP_CSR_06_Loosen" ? 1 : 0;   // 步驟計畫 v3.0：CSR_06 b 只在 L 側
                    Assert.AreEqual(src.subActions.Length - rOnly, mine.Count, $"{id}_{side}：小動作數和 R1 相同（R 側少了只在 L 做的列）");
                    foreach (var m in mine)
                    {
                        Assert.AreEqual(1, m.layer, $"{id}_{side} {m.e.subId} 沿用 R1");
                        Assert.AreEqual(src.subActions[m.e.fullOrder - 1].done.Replace("{side}", side), m.rule, $"{id}_{side} {m.e.subId}：條件和 R1 同一個位置的相同（不因 R 側少一列而錯位）");
                    }
                    var built = courses["daily"].First(s => s.stepId == id + "_" + side);
                    var old = ProcedureJsonConverter.BuildStep(src);
                    try
                    {
                        Assert.AreEqual(Norm(old.completion), Norm(built.completion), $"{id}_{side}：步驟完成條件和 R1 相同");
                        Assert.AreEqual(side, built.side);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(old); }
                }
            foreach (var s in courses.Values.SelectMany(x => x)) UnityEngine.Object.DestroyImmediate(s);
        }

        static string Norm(object o) => System.Text.RegularExpressions.Regex.Replace(UnityEngine.JsonUtility.ToJson(new RuleBox { rule = (CompletionRule)o }), "\"rid\":\\s*-?\\d+", "");
        [System.Serializable] class RuleBox { [UnityEngine.SerializeReference] public CompletionRule rule; }

        [Test]
        public void GeneratedAssets_UpToDate_Reproducible_NoPresetDone()
        {
            var courses = StepsCsvImporter.BuildAll(new string[0], new List<string>(), out _);
            var again = StepsCsvImporter.BuildAll(new string[0], new List<string>(), out _);
            try
            {
                foreach (var c in StepsCsvImporter.Courses)
                {
                    var prc = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(c));
                    Assert.IsNotNull(prc, c + "：要先跑 Tools/Import Steps");
                    CollectionAssert.AreEqual(courses[c].Select(s => s.stepId).ToArray(), prc.steps.Select(s => s.stepId).ToArray(), c + "：步驟順序和 CSV 相同");
                    for (int i = 0; i < courses[c].Count; i++)
                    {
                        Assert.AreEqual(Json(courses[c][i]), Json(again[c][i]), $"{c} {courses[c][i].stepId}：兩次結果相同（可重現）");
                        Assert.AreEqual(Json(courses[c][i]), Json(prc.steps[i]), $"{c} {courses[c][i].stepId}：資產和 CSV 一致（沒有手改、沒有過期）");
                        Assert.IsFalse(prc.steps[i].presetDone, $"{c} {prc.steps[i].stepId}：K1 不可預設完成");
                    }
                    foreach (var id in new[] { "STP_SI_02_LockOut", "STP_SI_03_CylinderPowerOff", "STP_SI_06_RoofEntry", "STP_WC_02_LeaveRoof", "STP_WC_04_Unlock" })
                        Assert.IsNotEmpty(prc.steps.First(s => s.stepId == id).subActions, $"{c} {id}：K1 要有小動作");
                }
            }
            finally { foreach (var s in courses.Values.Concat(again.Values).SelectMany(x => x)) UnityEngine.Object.DestroyImmediate(s); }
        }

        [Test]
        public void ConditionTables_EveryEntryUsed()
        {
            LoadResolved();
            var t = StepsCsvImporter.LoadTables();
            var conds = expanded.Select(e => e.condition).ToList();
            foreach (var p in t.patterns) Assert.IsTrue(conds.Any(c => System.Text.RegularExpressions.Regex.IsMatch(c, p.re)), $"pattern 沒有任何一列用到（刪掉）：{p.re}");
            foreach (var x in t.exact) Assert.IsTrue(expanded.Any(e => e.condition == x.text && (string.IsNullOrEmpty(x.step) || e.stepId.StartsWith(x.step))), $"exact 沒有任何一列用到（刪掉）：{x.text}");
            foreach (var x in t.reuseExcluded.Concat(t.reuseTargetChanged)) Assert.IsTrue(expanded.Any(e => e.stepId.StartsWith(x.step) && e.subId == x.sub), $"沒有這一列：{x.step} {x.sub}");
        }

        static string Json(StepDefinition s) => System.Text.RegularExpressions.Regex.Replace(EditorJsonUtility.ToJson(s), "\"(rid|m_Name)\":\\s*(-?\\d+|\"[^\"]*\")", "");

        /// <summary>b4 審查條件 2：機制還沒做、由後面段落做的完成條件，全部列在 Docs/R1.5b/b4c-stubs.md，和對應表一致，每一列都對到一個項目。</summary>
        [Test]
        public void PendingMechanisms_ListedInStubsDoc_EachMapsToItem()
        {
            LoadResolved();
            var items = new Dictionary<string, string>
            {
                { "b4c:holdpoint", "HoldPointDef 停留點與 NPC" }, { "b4c:ppe", "個人防護具與手套模式" }, { "b4c:ground_register", "接地登記欄" },
                { "b4c:lockout", "一人一鎖" }, { "b4c:si00", "SI_00 限制" }, { "b4c:strip_carry", "日常滑板上下車頂" },
                { "b4d", "車頂邊兩級判定與移動限制" }, { "b4:p3", "b4 第 3 部分 ErrorRule" }, { "b4:p4", "b4 第 4 部分 CourseDef 覆寫" }, { "b4:p5", "b4 第 5 部分 兩門課跑到結算" },
            };
            var doc = System.IO.File.ReadAllLines("Docs/R1.5b/b4c-stubs.md").Where(l => l.StartsWith("| STP_")).Select(l => l.Split('|').Select(x => x.Trim()).ToArray()).ToList();
            var listed = new HashSet<string>(doc.Select(c => $"{c[1]}|{c[2]}|{c[3]}|{c[4].Trim('`')}|{c[5]}"));
            var expected = new HashSet<string>(resolved.Where(r => r.owner.Length > 0).Select(r => $"{r.e.stepId}|{r.e.subId}|{r.e.course}|{r.rule}|{r.owner}"));
            CollectionAssert.IsEmpty(expected.Except(listed).ToList(), "對應表有、清單沒有");
            CollectionAssert.IsEmpty(listed.Except(expected).ToList(), "清單有、對應表已經沒有（做好了要從清單刪掉）");
            foreach (var c in doc) Assert.IsTrue(items.ContainsKey(c[5]), $"{c[1]} {c[2]}：{c[5]} 不是 b4c／後續段落的項目");
            foreach (var p in StepsCsvImporter.LoadTables().pendingTargets) Assert.IsTrue(items.ContainsKey(p.owner), $"待做目標 {p.id}：{p.owner} 不是項目");
        }
    }
}
