using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.Procedures;
using UnityEditor;

namespace PantographSim.Tests
{
    /// <summary>
    /// R1.5b b4（第 3 部分）ErrorRule 的唯一定義 ErrorRules.json：CSV 的引用都有定義且嚴重度一致（S1）、沒有 PendingRuling、
    /// 產生的步驟用這份的嚴重度、K8 桌面限定的規則 VR 不載入、K4 VR 指令沒有鍵盤字樣、觸發條件還沒做的都在 b4c-stubs.md。
    /// </summary>
    public class ErrorRuleTests
    {
        static readonly string Head = "stepId,subId,order,instruction,targetId,toolId,condition,courses,templateN,standPointId_periodic,standPointId_daily,errorIds\n";
        static readonly string StepHead = "stepId,title,phase,courses,orderPeriodic,orderDaily,errorIds\n";

        [Test]
        public void CsvRefs_AllDefined_SeverityMatches_NoPendingRuling()
        {
            var defs = StepsCsvImporter.LoadErrorRules();
            var errs = new List<string>();
            StepsCsvImporter.CheckErrorRefs(StepsCsvImporter.LoadSubActionRows(), StepsCsvImporter.LoadStepRows(), defs, errs);
            Assert.IsEmpty(errs, string.Join("\n", errs));
            var pending = defs.Values.Where(d => d.status != StepsCsvImporter.Defined && !StepsCsvImporter.Disabled(d.status)).Select(d => d.errorId).ToList();   // 停用（Deprecated／NotApplicable）是已裁定
            Assert.IsEmpty(pending, "要給驗收組長裁定的（PendingRuling）：" + string.Join(", ", pending));
            foreach (var d in defs.Values) Assert.IsTrue(new[] { "Critical", "Major", "Minor" }.Contains(d.severity), d.errorId + "：嚴重度 " + d.severity);
        }

        [Test]
        public void CsvRefs_Undefined_Mismatch_NotInStep_Pending_FailWithLine()
        {
            var defs = new Dictionary<string, StepsCsvImporter.ErrorRuleDef>
            {
                { "ERR_A", new StepsCsvImporter.ErrorRuleDef { errorId = "ERR_A", severity = "Major", status = StepsCsvImporter.Defined } },
                { "ERR_P", new StepsCsvImporter.ErrorRuleDef { errorId = "ERR_P", severity = "", status = StepsCsvImporter.PendingRuling } },
            };
            var subs = StepsCsvImporter.Rows(Head +
                "STP_X,a,1,t,,,c,both,,tp.a,tp.a,ERR_A:Critical\n" +    // 第 2 行：嚴重度不同
                "STP_X,b,2,t,,,c,both,,tp.a,tp.a,ERR_NONE:Minor\n" +    // 第 3 行：沒有定義
                "STP_X,c,3,t,,,c,both,,tp.a,tp.a,ERR_P:Major\n");       // 第 4 行：還沒裁定
            var steps = StepsCsvImporter.Rows(StepHead + "STP_X,x,SI,both,1,1,ERR_A:Major\n");
            var errs = new List<string>();
            StepsCsvImporter.CheckErrorRefs(subs, steps, defs, errs);
            Assert.IsTrue(errs.Any(e => e.Contains("第 2 行") && e.Contains("ERR_A 寫 Critical")), string.Join("\n", errs));
            Assert.IsTrue(errs.Any(e => e.Contains("第 3 行") && e.Contains("ERR_NONE") && e.Contains("沒有定義")));
            Assert.IsTrue(errs.Any(e => e.Contains("第 4 行") && e.Contains("PendingRuling")));
            Assert.IsTrue(errs.Any(e => e.Contains("第 3 行") && e.Contains("不在 Steps.csv")), "小動作的錯誤要出現在該步的 errorIds");
        }

        [Test]
        public void GeneratedSteps_UseCatalogSeverity_K3Field_K8InputMode()
        {
            var defs = StepsCsvImporter.LoadErrorRules();
            foreach (var c in StepsCsvImporter.Courses)
                foreach (var s in AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(c)).steps)
                    foreach (var e in s.errors)
                    {
                        Assert.AreEqual(defs[e.errorId].severity, e.severity.ToString(), $"{c} {s.stepId} {e.errorId}：嚴重度以 ErrorRules.json 為準");
                        Assert.AreEqual(defs[e.errorId].severityBeforeK3 ?? "", e.severityBeforeK3);
                        Assert.AreEqual(defs[e.errorId].inputMode ?? "", e.inputMode);
                    }
            Assert.AreEqual("Critical", defs["ERR_CSR13_Under"].severity, "steps-v6：Critical（R1 的 Major 不再用）");
            Assert.AreEqual("Major", defs["ERR_CSR13_Under"].severityBeforeK3);
            foreach (var id in new[] { "ERR_CSR03_CopyError", "ERR_CSR04_CopyError" }) Assert.AreEqual("desktop", defs[id].inputMode, id + "：K8 只在桌面");
        }

        [Test]
        public void K8_VrMode_CopyErrorRulesNotLoaded()
        {
            var daily = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath("daily"));
            var csr03 = daily.steps.First(s => s.stepId == "STP_CSR_03_Measure_L");
            try
            {
                InputMode.SetXr(false);
                Assert.IsTrue(ErrorDetector.ActiveRules(csr03).Any(r => r.errorId == "ERR_CSR03_CopyError"), "桌面模式載入");
                InputMode.SetXr(true);
                var ids = ErrorDetector.ActiveRules(csr03).Select(r => r.errorId).ToList();
                CollectionAssert.DoesNotContain(ids, "ERR_CSR03_CopyError", "VR 模式不載入");
                Assert.IsNotEmpty(ids, "其他規則照常載入");
            }
            finally { InputMode.SetXr(false); }
        }

        [Test]
        public void K4_VrText_NoKeyboardWords()
        {
            Assert.IsTrue(DataValidator.XrKeyboardWords.IsMatch("按 R 讓箭頭朝外"));
            Assert.IsTrue(DataValidator.XrKeyboardWords.IsMatch("用滑鼠拖到托架上方"));
            Assert.IsFalse(DataValidator.XrKeyboardWords.IsMatch("按 ZERO 鍵歸零"), "卡尺上的鍵不算");
            foreach (var c in StepsCsvImporter.Courses)
                foreach (var s in AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(c)).steps)
                    foreach (var a in s.subActions) Assert.IsFalse(DataValidator.XrKeyboardWords.IsMatch(a.text), $"{c} {s.stepId} {a.subId}：{a.text}");
        }

        /// <summary>觸發條件還沒做的錯誤（owner 不空）都列在 b4c-stubs.md，兩邊一致；owner 都是 b4c／後續段落的項目。</summary>
        [Test]
        public void PendingTriggers_ListedInStubsDoc()
        {
            var defs = StepsCsvImporter.LoadErrorRules();
            var owners = new HashSet<string> { "b4c:holdpoint", "b4c:ppe", "b4c:ground_register", "b4c:lockout", "b4c:si00", "b4c:strip_carry", "b4c:torque_socket", "b4d", "b4:p3b", "b4:p3c", "b4:p3d", "b4:p4", "b4:p5", "R2", "n/a" };
            foreach (var d in defs.Values.Where(d => !string.IsNullOrEmpty(d.owner))) Assert.IsTrue(owners.Contains(d.owner), $"{d.errorId}：{d.owner} 不是項目");
            var doc = System.IO.File.ReadAllLines("Docs/R1.5b/b4c-stubs.md").Where(l => l.StartsWith("| ERR_")).Select(l => l.Split('|').Select(x => x.Trim()).ToArray()).Select(c => c[1] + "|" + c[2]).ToList();
            var expected = defs.Values.Where(d => !string.IsNullOrEmpty(d.owner)).Select(d => d.errorId + "|" + d.owner).ToList();
            CollectionAssert.AreEquivalent(expected, doc, "ErrorRules.json 的 owner 和 b4c-stubs.md 不一致（做好的要兩邊都刪）");
        }
    }
}
