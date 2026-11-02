using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Procedures;

namespace PantographSim.Tests
{
    /// <summary>
    /// 卡關：判定漏觸發的重現與修正、防護網（每 0.5 s 只讀檢查，不推進步驟）的 missed_commit／rule_mismatch、
    /// 「還差什麼」對應到小動作指令、25 步正常流程 0 次卡關、小動作與完成條件的雙向一致（含雙側 side）。
    /// </summary>
    public class StallDetectionTests
    {
        readonly List<string> runnerErrors = new List<string>();
        Action<string> originalLog;

        [SetUp]
        public void SetUp()
        {
            runnerErrors.Clear();
            originalLog = ProcedureRunner.ErrorLog;
            ProcedureRunner.ErrorLog = m => runnerErrors.Add(m);   // 預期的 Error 由各測試斷言後清掉
        }

        [TearDown]
        public void TearDown()
        {
            ProcedureRunner.ErrorLog = originalLog;
            Assert.IsEmpty(runnerErrors, "收到預期以外的 Error：\n" + string.Join("\n", runnerErrors));
        }

        const string Json = @"{ ""procedureId"": ""PRC_S"", ""steps"": [
            { ""stepId"": ""S1"", ""completion"": { ""type"": ""StateEquals"", ""key"": ""a"", ""value"": ""1"" } },
            { ""stepId"": ""S2"", ""completion"": { ""type"": ""StateEquals"", ""key"": ""x"", ""value"": ""y"" } } ] }";

        /// <summary>完成前要問的閘門：可以讀黑板，也可以讀黑板以外的狀態（模擬「條件依賴非黑板狀態」）。</summary>
        class TestGate : IStepGate
        {
            public Func<bool> allow = () => true;
            public bool CanComplete(string stepId, out string reason) { bool ok = allow(); reason = ok ? "" : "閘門還沒開"; return ok; }
        }

        [Test]
        public void Repro_WriteDuringEvaluation_WasMissed_NowReevaluated()
        {
            // 判定時閘門擋下 → 寫 procedure.gate_reason → 其他模組收到通知後補寫 g=true（閘門條件）。
            // 以前：這筆寫入發生在判定進行中，被「判定中」略過，之後沒有人再判定 → 條件都成立但步驟不跳。
            var bus = new EventBus(); var s = new SimState(bus);
            var gate = new TestGate { allow = () => s.GetBool("g") };
            var runner = new ProcedureRunner(ProcedureJsonConverter.Build(Json).procedure, bus, s, gate);
            bus.Subscribe<StateChanged>(e => { if (e.Has("procedure.gate_reason") && !s.GetBool("g")) s.SetBool("g", true); });
            runner.Start();
            s.Set("a", "1");
            Assert.AreEqual("S2", runner.Current.RunId, "判定中收到的寫入要再判定一次");
            var stalls = new List<StepStallDetected>();
            bus.Subscribe<StepStallDetected>(e => stalls.Add(e));
            runner.Poll(10f);
            Assert.IsEmpty(stalls);
        }

        [Test]
        public void Poll_ReadOnly_DetectsMissedCommit_LogsError_DoesNotAdvance()
        {
            var bus = new EventBus(); var s = new SimState(bus);
            bool open = false;
            var gate = new TestGate { allow = () => open };   // 黑板以外的狀態：改變時沒有 StateChanged
            var runner = new ProcedureRunner(ProcedureJsonConverter.Build(Json).procedure, bus, s, gate);
            var stalls = new List<StepStallDetected>();
            bus.Subscribe<StepStallDetected>(e => stalls.Add(e));
            runner.Start();
            s.Set("a", "1");
            Assert.AreEqual("S1", runner.Current.RunId);
            open = true;   // 條件都成立了，但沒有觸發判定
            runner.Poll(1f);
            Assert.AreEqual("S1", runner.Current.RunId, "防護網只記錄，不自動推進步驟");
            Assert.AreEqual(1, stalls.Count);
            Assert.AreEqual("missed_commit", stalls[0].kind);
            Assert.AreEqual(1, runnerErrors.Count(m => m.Contains("missed_commit") && m.Contains("S1")), "missed_commit 要記 Error");
            runnerErrors.Clear();
            runner.Poll(2f);
            Assert.AreEqual(1, stalls.Count, "同一步同一種只記一次");
            // 批次中不輪詢
            using (s.BeginBatch()) runner.Poll(5f);
            Assert.AreEqual(1, stalls.Count);
        }

        [Test]
        public void RuleMismatch_After3s_ExplainsWithSubActionText_InRecordAndState()
        {
            var sim = new SliceDryRunTests.SliceSim("SCN_Default", tutorial: true);
            var stalls = new List<StepStallDetected>();
            sim.bus.Subscribe<StepStallDetected>(e => stalls.Add(e));
            sim.RunUntil("STP_CSR_03_Measure_L");
            foreach (var p in new[] { "left", "mid", "right" }) sim.Measure($"ip.carbon_strip_L.measure_{p}");
            sim.Write("reading.L.left", "9.99"); sim.Write("reading.L.mid", "9.99"); sim.Write("reading.L.right", "9.99"); sim.Write("submit.L", "true");
            Assert.AreEqual(4f, sim.s.GetFloat("procedure.sub_index"), "小動作都打勾了");
            Assert.AreEqual("STP_CSR_03_Measure_L", sim.Current, "讀數抄錯：完成條件不成立");

            sim.runner.Poll(100f);
            sim.runner.Poll(101f);
            Assert.IsEmpty(stalls, "3 秒內不算卡關");
            sim.runner.Poll(103.2f);
            Assert.AreEqual(1, stalls.Count);
            var st = stalls[0];
            Assert.AreEqual("rule_mismatch", st.kind);
            Assert.AreEqual("STP_CSR_03_Measure_L", st.stepId);
            Assert.AreEqual("L", st.side, "雙側步驟：side 正確");
            StringAssert.Contains("ReadingMatches:workorder.reading.L.left", st.conditionIds);
            StringAssert.Contains("workorder.reading.L.left=9.99", st.values);
            Assert.IsTrue(sim.s.GetBool("procedure.stalled"));
            Assert.AreEqual("三個讀數填入作業單", sim.s.Get("procedure.stall_text"), "還差什麼＝那個小動作的指令文字（不是條件公式）");
            Assert.AreEqual("STP_CSR_03_Measure_L", sim.Current, "不跳過這一步");
            Assert.AreEqual(0, sim.errors.Count(e => e.errorId.StartsWith("ERR_Stall")), "卡關不是錯誤、不扣分");

            // 改正讀數 → 步驟完成，卡關狀態清掉
            foreach (var p in new[] { "left", "mid", "right" }) sim.Write($"reading.L.{p}", sim.s.Get($"ip.carbon_strip_L.measure_{p}.reading"));
            Assert.AreEqual("STP_CSR_04_Measure_R", sim.Current);
            Assert.IsFalse(sim.s.GetBool("procedure.stalled"));
        }

        [Test]
        public void NormalPath_25Steps_ZeroStalls_BothModes()
        {
            foreach (bool tutorial in new[] { true, false })
            {
                var sim = new SliceDryRunTests.SliceSim("SCN_Default", tutorial);
                var stalls = new List<StepStallDetected>();
                sim.bus.Subscribe<StepStallDetected>(e => stalls.Add(e));
                float clock = 0f;
                sim.bus.Subscribe<StateChanged>(_ => sim.runner.Poll(clock += 1f));   // 每次通知後都輪詢（時間往前 1 s）
                for (int guard = 0; guard < 80 && !sim.completed; guard++)
                {
                    sim.DoStep();
                    for (int i = 0; i < 5; i++) sim.runner.Poll(clock += 1f);   // 每一步之後再等 5 秒
                }
                Assert.IsTrue(sim.completed, sim.Describe());
                Assert.IsEmpty(stalls, "正常流程不應卡關：" + string.Join(", ", stalls.Select(x => $"{x.stepId}:{x.kind}:{x.conditionIds}")));
            }
        }

        /// <summary>
        /// 小動作與完成條件雙向一致（兩側展開後逐一檢查）：
        /// ・完成條件的每個條件都對應得到一個小動作（「還差什麼」才能用小動作指令說明），而且是同一側；
        /// ・小動作全部完成 ⇒ 完成條件成立：用正常流程在「最後一個小動作完成的那一次通知」檢查，步驟必須同時完成。
        /// </summary>
        [Test]
        public void SubActions_And_CompletionRule_Consistent_PerSide()
        {
            var sim = new SliceDryRunTests.SliceSim("SCN_Default", true);
            var missing = new List<string>();
            foreach (var step in sim.runner.Steps.Where(x => !x.def.presetDone))
            {
                var subs = ProcedureRunner.SubActions(step);
                var ctx = new EvalContext { state = new SimState(new EventBus()), side = step.side };   // 空黑板：所有條件都不成立，逐一列出
                foreach (var u in RuleExplainer.Explain(step.def.completion, ctx))
                {
                    // 每個條件都要有從資料來的中文說明（小動作、小步驟清單或錯誤規則），不能只剩整步的步驟說明
                    var (text, source) = sim.runner.DescribeUnmet(u, step);
                    if (source == "instruction") { missing.Add($"{step.RunId}：{u.id} 沒有對應的小動作／清單／錯誤說明"); continue; }
                    var a = RuleExplainer.SubActionFor(u, subs);
                    string side = step.side;
                    if (a != null && !string.IsNullOrEmpty(side) && u.keys.Any(k => k.Contains("_" + side + ".") || k.Contains("." + side + ".")))
                        if (!RuleExplainer.KeysOf(a.condition).Any(k => k.Contains("_" + side + ".") || k.Contains("." + side + ".")))
                            missing.Add($"{step.RunId}：{u.id} 對應到的小動作「{a.text}」不是 {side} 側");
                }
            }
            Assert.IsEmpty(missing, string.Join("\n", missing));

            // 小動作全部完成 ⇒ 完成條件成立（正常流程）
            var bad = new List<string>();
            int lastIdx = -1; string lastStep = "";
            sim.bus.Subscribe<StateChanged>(e =>
            {
                var cur = sim.runner.Current;
                if (cur == null) return;
                int idx = (int)sim.s.GetFloat("procedure.sub_index"), count = ProcedureRunner.SubActions(cur).Length;
                if (cur.RunId == lastStep && idx == lastIdx) return;
                lastStep = cur.RunId; lastIdx = idx;
                if (count > 0 && idx >= count && !RuleEvaluators.Evaluate(cur.def.completion, sim.runner.ContextFor(cur)))
                    bad.Add($"{cur.RunId}：小動作全部完成但完成條件不成立：" + string.Join(", ", RuleExplainer.Explain(cur.def.completion, sim.runner.ContextFor(cur)).Select(u => u.id)));
            });
            sim.RunUntil(null);
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
        [Test]
        public void EnteringStep_WritesAreJudgedByNewStep_AndSnapshotTakenWhenStable()
        {
            const string json = @"{ ""procedureId"": ""PRC_E"", ""steps"": [
                { ""stepId"": ""S1"", ""completion"": { ""type"": ""StateEquals"", ""key"": ""a"", ""value"": ""1"" } },
                { ""stepId"": ""S2"", ""completion"": { ""type"": ""StateEquals"", ""key"": ""b"", ""value"": ""1"" } },
                { ""stepId"": ""S3"", ""completion"": { ""type"": ""StateEquals"", ""key"": ""x"", ""value"": ""y"" } } ] }";
            var bus = new EventBus(); var s = new SimState(bus);
            var runner = new ProcedureRunner(ProcedureJsonConverter.Build(json).procedure, bus, s);
            // 進入步驟時其他模組立刻寫入：S2 一開始就寫 b=1（要用 S2 的規則判定 → 直接完成）；S3 一開始寫 entered=S3（快照要包含）
            bus.Subscribe<StepStarted>(e =>
            {
                if (e.stepId == "S2") s.Set("b", "1");
                if (e.stepId == "S3") s.Set("entered", "S3");
            });
            runner.Start();
            s.Set("a", "1");
            Assert.AreEqual("S3", runner.Current.RunId, "進入 S2 時寫入的 b=1 用 S2 的規則判定，S2 完成");
            Assert.IsTrue(s.GetBool("step.S2.done"));
            s.Set("entered", "later");
            runner.RestartCurrentStep();
            Assert.AreEqual("S3", s.Get("entered"), "S3 的開始快照在狀態穩定後才存：包含進入時寫入的值");
        }

        [Test]
        public void Reevaluation_CappedAt8Rounds_LogsError()
        {
            var bus = new EventBus(); var s = new SimState(bus);
            int n = 0;
            var runner = new ProcedureRunner(ProcedureJsonConverter.Build(Json).procedure, bus, s, new ReasonGate(() => "r" + (n++)));
            // 每次閘門說明改變，就有模組寫一個新值 → 判定中又收到通知 → 一直不穩定
            bus.Subscribe<StateChanged>(e => { if (e.Has("procedure.gate_reason") && s.Get("procedure.gate_reason") != "") s.SetFloat("tick", n); });
            runner.Start();
            s.Set("a", "1");
            Assert.AreEqual("S1", runner.Current.RunId);
            Assert.AreEqual(1, runnerErrors.Count(m => m.Contains("超過 " + ProcedureRunner.MaxRounds + " 次")), string.Join("\n", runnerErrors));
            runnerErrors.Clear();
        }

        /// <summary>永遠不放行、每次說明都不同的閘門（讓狀態一直改變）。</summary>
        class ReasonGate : IStepGate
        {
            readonly Func<string> next;
            public ReasonGate(Func<string> next) { this.next = next; }
            public bool CanComplete(string stepId, out string reason) { reason = next(); return false; }
        }
    }
}
