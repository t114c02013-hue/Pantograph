using System.Collections.Generic;
using System.Globalization;
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
    /// R1.5b b4（軟體工程裁定 2026-11-03）CourseFlowTests：用匯入產生的兩門課（ProcedureRunner＋requireFreshWrite），
    /// 腳本照正確順序一個 tick 寫一個小動作的條件鍵，把整門課跑完。有小動作在啟用的同一個 tick 就完成（不在 autoCompleteAllowed）就失敗。
    /// 還沒有機制的鍵由腳本直接寫黑板，這些都列在 Docs/R1.5b/b4c-stubs.md（ImporterTests 檢查一致）。
    /// 步驟完成條件（R1 沿用的 ValueInRange、OrderedSequence、Decision…）也由腳本依規則型別寫入。
    /// </summary>
    public class CourseFlowTests
    {
        class Flow
        {
            public readonly EventBus bus = new EventBus();
            public readonly SimState s;
            public readonly ProcedureRunner runner;
            public readonly ProcedureDefinition prc;
            public readonly List<string> autoCompleted = new List<string>();   // 啟用同一個 tick 就完成（違規）
            public readonly List<string> wouldAuto = new List<string>();       // 啟用時條件已成立（沒有 version 規則就會自動完成）

            public Flow(string course)
            {
                prc = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(course));
                Assert.IsNotNull(prc, course + "：要先跑 Tools/Import Steps");
                Assert.IsTrue(prc.requireFreshWrite, course + "：匯入的課程要開 requireFreshWrite");
                s = new SimState(bus);
                runner = new ProcedureRunner(prc, bus, s);
            }

            int Sub => (int)s.GetFloat("procedure.sub_index");

            /// <summary>跑到 stop(步驟, 小動作) 成立為止（null＝跑完整門課）。回傳是否停在 stop。</summary>
            public bool Run(System.Func<string, string, bool> stop = null)
            {
                runner.Start();
                for (int guard = 0; guard < 5000 && !runner.Finished; guard++)
                {
                    var st = runner.Current;
                    var subs = ProcedureRunner.SubActions(st);
                    int idx = Sub;
                    if (idx < subs.Length)
                    {
                        if (stop != null && stop(st.def.stepId, subs[idx].subId)) return true;
                        if (ChecklistEval.Done(s, subs[idx].condition)) wouldAuto.Add($"{st.RunId} {subs[idx].subId}：{subs[idx].condition}{(FreshWriteGate.EventKeys(subs[idx].condition).Any() ? "" : "　〔只讀 LiveState：照規則一出現就完成〕")}");
                        Write(s, subs[idx].condition);   // 一個 tick
                        if (runner.Current == st && Sub == idx) Assert.Fail($"{st.RunId} {subs[idx].subId}：寫了條件鍵還是沒完成（{subs[idx].condition}）");
                        CheckNoAuto(st, idx);
                    }
                    else
                    {
                        s.Batch(() => Satisfy(st.def.completion, new EvalContext { state = s, side = st.side }));
                        if (runner.Current == st && !runner.Finished) Assert.Fail($"{st.RunId}：小動作都完成、完成條件也寫了，步驟還是沒完成");
                        CheckNoAuto(st, idx);
                    }
                }
                Assert.IsTrue(runner.Finished, "整門課要跑完");
                return false;
            }

            /// <summary>這一個 tick 之後：同一步最多前進一個；換到新的一步時，新步驟的第一個小動作不能已經完成。</summary>
            void CheckNoAuto(RuntimeStep before, int idx)
            {
                if (runner.Finished) return;
                var now = runner.Current;
                var subs = ProcedureRunner.SubActions(now);
                int first = now == before ? idx + 1 : 0;
                for (int i = first; i < System.Math.Min(Sub, subs.Length); i++)
                    if (!FreshWriteGate.Allowed(prc, now.def.stepId, subs[i].subId) && FreshWriteGate.EventKeys(subs[i].condition).Any())   // 只讀 LiveState 鍵的照規則可以一出現就完成
                        autoCompleted.Add($"{now.RunId} {subs[i].subId}：啟用的同一個 tick 就完成（鍵 {string.Join(",", RuleExplainer.KeysOf(subs[i].condition))}）");
            }
        }

        /// <summary>寫入讓條件成立的值（ChecklistEval 格式）。</summary>
        internal static void Write(ISimState s, string condition) => s.Batch(() =>
        {
            foreach (var part in condition.Split(new[] { "&&" }, System.StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()))
            {
                int ge = part.IndexOf(">="), le = part.IndexOf("<="), eq = part.IndexOf('=');
                int op = ge > 0 ? ge : le > 0 ? le : -1;
                if (op > 0)
                {
                    string key = part.Substring(0, op), rhs = part.Substring(op + 2);
                    if (!float.TryParse(rhs, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { if (!s.Has(rhs)) s.SetFloat(rhs, 1f); v = s.GetFloat(rhs); }
                    s.SetFloat(key, v);
                }
                else if (eq > 0) s.Set(part.Substring(0, eq), part.Substring(eq + 1));
                else s.SetBool(part, true);
            }
        });

        /// <summary>寫入讓步驟完成條件成立的值（依規則型別）。</summary>
        static void Satisfy(CompletionRule r, EvalContext c)
        {
            switch (r)
            {
                case AllOf a: foreach (var x in a.rules) Satisfy(x, c); break;
                case StateEquals se: c.state.Set(c.K(se.key), se.value); break;
                case ChecklistAll cl: foreach (var k in cl.keys) c.state.SetBool(c.K(k), true); break;
                case OrderedSequence os: c.state.Set(c.K(os.key), os.expected); break;
                case ValueInRange vr: foreach (var k in ValueInRangeEvaluator.Expand(vr.key, vr.count, c)) c.state.SetFloat(k, (vr.min + vr.max) / 2f); break;
                case Decision d: c.state.Set(c.K(d.choiceKey), DecisionEvaluator.Answer(d, c.state)); break;
                case ReadingMatches rm:
                    for (int i = 0; i < rm.recordKeys.Length; i++)
                    {
                        var read = c.K(rm.readingKeys[i]);
                        if (!c.state.Has(read)) c.state.SetFloat(read, 1f);
                        c.state.SetFloat(c.K(rm.recordKeys[i]), c.state.GetFloat(read));
                    }
                    break;
            }
        }

        [TestCase("periodic")]
        [TestCase("daily")]
        public void Course_RunsToEnd_NoSubActionCompletesOnActivation(string course)
        {
            var f = new Flow(course);
            try
            {
                f.Run();
                Assert.IsEmpty(f.autoCompleted, string.Join("\n", f.autoCompleted));
                System.IO.Directory.CreateDirectory("Logs");
                System.IO.File.WriteAllText($"Logs/CourseFlow_{course}.txt",
                    $"{course}：{f.runner.Finished}，啟用時條件已成立、靠 version 規則擋下（沒有這條規則就會自動完成）的小動作 {f.wouldAuto.Count} 個：\n" + string.Join("\n", f.wouldAuto) + "\n");
            }
            finally { f.runner.Dispose(); }
        }

        /// <summary>R1.5b b4（5a）：兩門課從頭跑到結算（ErrorDetector＋Scoring），結算畫面要的分數、Critical／Major／Minor 數都產生出來。</summary>
        [TestCase("periodic")]
        [TestCase("daily")]
        public void Course_RunsToSettlement_ScoreAndCountsProduced(string course)
        {
            var f = new Flow(course);
            var det = new ErrorDetector(f.runner, f.bus, f.s);
            var scoring = new PantographSim.Training.Scoring(f.bus, f.s);
            SessionResult? got = null;
            f.bus.Subscribe<SessionResult>(r => got = r);
            try
            {
                scoring.Start(0f);
                f.Run();
                Assert.IsTrue(got.HasValue, "要發結算（SessionResult）");
                var result = got.Value;
                Assert.IsTrue(f.s.GetBool("score.finished"));
                Assert.AreEqual(f.s.GetFloat("score.total"), result.score);
                Assert.AreEqual(System.Math.Max(0f, 100f - 2f * result.minor - 5f * result.major - 15f * result.critical), result.score, 1e-4f);
                System.IO.File.AppendAllText($"Logs/CourseFlow_{course}.txt",
                    $"結算：分數 {result.score}、Critical {result.critical}、Major {result.major}、Minor {result.minor}、通過 {result.passed}\n");
            }
            finally { scoring.Dispose(); det.Dispose(); f.runner.Dispose(); }
        }

        /// <summary>反例：在 SI_04 b 做過自檢後，IN_04b b 啟用時 selftest 已經是 true，不能自動完成；要再點一次自檢器。</summary>
        [Test]
        public void IN04b_SelfTest_NeedsNewSelfTest()
        {
            AssertNeedsAction("STP_IN_04b_RegroundPan", "b", "tool.voltage_detector.selftest");
        }

        /// <summary>反例：LF_02 b 查看過，IN_02 b 啟用時 inspected 已經是 true，不能自動完成。</summary>
        [Test]
        public void IN02_Inspect_NeedsNewInspection()
        {
            AssertNeedsAction("STP_IN_02_TrialLift", "b", "part.pan_assy.inspected");
        }

        static void AssertNeedsAction(string stepId, string subId, string actionKey)
        {
            var f = new Flow("periodic");
            try
            {
                Assert.IsTrue(f.Run((st, sub) => st == stepId && sub == subId), $"要跑到 {stepId} {subId}");
                var cond = ProcedureRunner.SubActions(f.runner.Current)[(int)f.s.GetFloat("procedure.sub_index")].condition;
                Assert.IsTrue(ChecklistEval.Done(f.s, cond), $"前提：{stepId} {subId} 啟用時條件已經成立（{cond}）");
                f.s.Set("test.unrelated", "1");   // 別的鍵變動：照樣判定一次
                Assert.AreEqual(stepId, f.runner.Current.def.stepId);
                Assert.AreEqual(subId, ProcedureRunner.SubActions(f.runner.Current)[(int)f.s.GetFloat("procedure.sub_index")].subId, "沒有再做動作就不能完成");
                f.s.SetBool(actionKey, true);   // 再做一次（值本來就是 true，也要寫入）
                Assert.IsTrue(f.runner.Current.def.stepId != stepId || ProcedureRunner.SubActions(f.runner.Current)[(int)f.s.GetFloat("procedure.sub_index")].subId != subId, "再做一次就完成（比對步驟＋小動作：下一步的第一個可能接著完成）");
            }
            finally { f.runner.Dispose(); }
        }

        [Test]
        public void SimState_VersionCountsEveryWrite_SameValueToo_DiscardedBatchNot()
        {
            var s = new SimState(new EventBus());
            Assert.AreEqual(0, s.Version("k"));
            s.SetBool("k", true); s.SetBool("k", true);
            Assert.AreEqual(2, s.Version("k"), "值相同也要加");
            using (s.BeginBatch()) { s.Set("k", "x"); s.Set("k", "y"); }
            Assert.AreEqual(3, s.Version("k"), "一批只算一次生效");
            var old = SimState.ErrorLog; SimState.ErrorLog = _ => { };
            try { Assert.Throws<System.Exception>(() => s.Batch(() => { s.Set("k", "z"); throw new System.Exception("放棄"); })); }
            finally { SimState.ErrorLog = old; }
            Assert.AreEqual(3, s.Version("k"), "放棄的批次不算");
            Assert.AreEqual("y", s.Get("k"));
        }

        [Test]
        public void AutoCompleteAllowed_IsEmpty()
        {
            foreach (var c in StepsCsvImporter.Courses)
                Assert.IsEmpty(AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(c)).autoCompleteAllowed, c + "：例外清單目前應該是空的（要加先回報）");
        }
    }
}
