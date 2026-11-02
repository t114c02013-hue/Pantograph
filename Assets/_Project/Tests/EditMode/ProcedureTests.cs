using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Procedures;

namespace PantographSim.Tests
{
    public class ProcedureTests
    {
        const string Json = @"{
          ""procedureId"": ""PRC_T"",
          ""repeatBlock"": { ""fromStepId"": ""B"", ""toStepId"": ""C"", ""sides"": [""L"",""R""], ""sideLabels"": [""肘側滑板"",""外側滑板""] },
          ""onPass"": [ { ""stepId"": ""D"", ""key"": ""wo.decision"", ""value"": ""pass"", ""jumpTo"": ""F"" } ],
          ""steps"": [
            { ""stepId"": ""A"", ""requiredToolId"": ""tool.caliper"", ""targetIds"": [""ip.carbon_strip_L.measure_mid""],
              ""completion"": { ""type"": ""StateEquals"", ""key"": ""a"", ""value"": ""ok"" },
              ""errors"": [ { ""errorId"": ""E_A"", ""severity"": ""Major"", ""trigger"": ""Incomplete"", ""key"": ""a.trigger"", ""keys"": [""a.req""], ""what"": ""w"", ""why"": ""y"", ""fix"": ""f"" } ] },
            { ""stepId"": ""P"", ""presetDone"": true, ""presetStates"": [""lock.breaker=locked""], ""completion"": { ""type"": ""StateEquals"", ""key"": ""never"", ""value"": ""x"" } },
            { ""stepId"": ""B"", ""targetIds"": [""ip.carbon_strip_{side}.bolt_*""],
              ""completion"": { ""type"": ""ValueInRange"", ""key"": ""ip.carbon_strip_{side}.bolt_{n}.angle_deg"", ""min"": 150, ""max"": 210, ""count"": 4 },
              ""errors"": [ { ""errorId"": ""E_B"", ""severity"": ""Major"", ""trigger"": ""ValueOutOfRange"", ""key"": ""ip.carbon_strip_{side}.bolt_{n}.angle_deg"", ""min"": 0, ""max"": 210, ""count"": 4, ""what"": ""w"", ""why"": ""y"", ""fix"": ""f"" } ] },
            { ""stepId"": ""C"", ""completion"": { ""type"": ""OrderedSequence"", ""key"": ""ip.carbon_strip_{side}.mark_order"", ""expected"": ""1,2,3,4"" } },
            { ""stepId"": ""D"", ""completion"": { ""type"": ""Decision"", ""choiceKey"": ""wo.decision"", ""threshold"": 3.0 },
              ""errors"": [ { ""errorId"": ""E6"", ""severity"": ""Critical"", ""trigger"": ""ChoiceEquals"", ""key"": ""wo.decision"", ""value"": ""pass"", ""whenAnswer"": ""both"", ""what"": ""w"", ""why"": ""y"", ""fix"": ""f"" } ] },
            { ""stepId"": ""E"", ""completion"": { ""type"": ""StateEquals"", ""key"": ""e"", ""value"": ""ok"" } },
            { ""stepId"": ""F"", ""completion"": { ""type"": ""ProcedureDone"" } }
          ]
        }";

        EventBus bus; SimState state; ProcedureRunner runner; ErrorDetector detector;
        List<ErrorDetected> errors; List<string> started; bool completed;

        void Setup(params string[] init)
        {
            bus = new EventBus(); state = new SimState(bus);
            foreach (var kv in init) { var p = kv.Split('='); state.Set(p[0], p[1]); }
            var bundle = ProcedureJsonConverter.Build(Json);
            runner = new ProcedureRunner(bundle.procedure, bus, state);
            detector = new ErrorDetector(runner, bus, state);
            errors = new List<ErrorDetected>(); started = new List<string>(); completed = false;
            bus.Subscribe<ErrorDetected>(e => errors.Add(e));
            bus.Subscribe<StepStarted>(e => started.Add(e.stepId));
            bus.Subscribe<ProcedureCompleted>(e => completed = true);
            runner.Start();
        }

        void DoBolts(string side, float angle) { for (int n = 1; n <= 4; n++) state.SetFloat($"ip.carbon_strip_{side}.bolt_{n}.angle_deg", angle); }

        [Test]
        public void RepeatBlock_ExpandsLeftThenRight_AndPresetIsDone()
        {
            Setup();
            CollectionAssert.AreEqual(new[] { "A", "P", "B_L", "C_L", "B_R", "C_R", "D", "E", "F" }, runner.Steps.Select(s => s.RunId).ToArray());
            // R1 T7：預設完成步驟的狀態與完成記號，都等流程走到那一步才在同一批寫入
            Assert.IsFalse(state.GetBool("step.P.done"), "預設步驟的完成記號等流程走到才寫入");
            Assert.IsFalse(state.Has("lock.breaker"), "預設步驟的狀態等流程走到才寫入");
            Assert.AreEqual("A", runner.Current.RunId);
            state.Set("a", "ok");
            Assert.AreEqual("locked", state.Get("lock.breaker"));
            Assert.IsTrue(state.GetBool("step.P.done"));
            Assert.AreEqual("B_L", runner.Current.RunId);
            Assert.AreEqual("外側滑板", runner.Steps[4].sideLabel);
        }

        [Test]
        public void NormalPath_CompletesAllSteps_ReplaceBoth()
        {
            Setup("part.carbon_strip_L.true.mid=2.8", "part.carbon_strip_R.true.mid=3.8");
            state.Set("a", "ok");
            DoBolts("L", 180); state.Set("ip.carbon_strip_L.mark_order", "1,2,3,4");
            DoBolts("R", 180); state.Set("ip.carbon_strip_R.mark_order", "1,2,3,4");
            Assert.AreEqual("D", runner.Current.RunId);
            state.Set("wo.decision", "both");
            Assert.AreEqual("E", runner.Current.RunId);
            state.Set("e", "ok");
            Assert.IsTrue(completed);
            Assert.AreEqual(0, errors.Count);
        }

        [Test]
        public void OnPass_JumpsToClosing()
        {
            Setup("part.carbon_strip_L.true.mid=4.2", "part.carbon_strip_R.true.mid=3.8");
            state.Set("a", "ok");
            DoBolts("L", 180); state.Set("ip.carbon_strip_L.mark_order", "1,2,3,4");
            DoBolts("R", 180); state.Set("ip.carbon_strip_R.mark_order", "1,2,3,4");
            state.Set("wo.decision", "pass");
            Assert.IsTrue(completed, "跳過 E 到 F，F=ProcedureDone 應立即完成");
            Assert.IsTrue(state.GetBool("step.E.skipped"));
        }

        [Test]
        public void WrongDecision_RaisesCriticalAndDoesNotAdvance()
        {
            Setup("part.carbon_strip_L.true.mid=2.8", "part.carbon_strip_R.true.mid=3.8");
            state.Set("a", "ok");
            DoBolts("L", 180); state.Set("ip.carbon_strip_L.mark_order", "1,2,3,4");
            DoBolts("R", 180); state.Set("ip.carbon_strip_R.mark_order", "1,2,3,4");
            state.Set("wo.decision", "pass");
            Assert.AreEqual("D", runner.Current.RunId);
            Assert.AreEqual("E6", errors.Single().errorId);
            Assert.AreEqual(Severity.Critical, errors[0].severity);
        }

        [Test]
        public void Restart_RestoresSnapshot_AndResetsErrors()
        {
            Setup();
            state.Set("a", "ok");
            state.SetFloat("ip.carbon_strip_L.bolt_1.angle_deg", 400);
            Assert.AreEqual(1, errors.Count(e => e.errorId == "E_B"));
            state.SetFloat("score.critical", 1);   // Scoring 記下的錯誤數
            bus.Publish(new RestartStepRequested());
            Assert.IsFalse(state.Has("ip.carbon_strip_L.bolt_1.angle_deg"));
            Assert.AreEqual(1f, state.GetFloat("score.critical"), "重做不會抹掉已記錄的錯誤");
            Assert.AreEqual("B_L", runner.Current.RunId);
            state.SetFloat("ip.carbon_strip_L.bolt_1.angle_deg", 400);
            Assert.AreEqual(2, errors.Count(e => e.errorId == "E_B"), "重做後同一錯誤可再次觸發");
        }

        [Test]
        public void Incomplete_FiresWhenTriggerBeforeRequirement()
        {
            Setup();
            state.SetBool("a.trigger", true);
            Assert.AreEqual("E_A", errors.Single().errorId);
        }

        [Test]
        public void Guard_BlocksWrongToolAndWrongTarget()
        {
            Setup();
            var r = detector.Check(new ActionRequest("tool.socket_wrench", "ip.carbon_strip_L.measure_mid", "click"));
            Assert.IsFalse(r.allowed);
            Assert.AreEqual(ErrorDetector.WrongToolId, r.errorId);
            var r2 = detector.Check(new ActionRequest("tool.caliper", "ip.carbon_strip_R.bolt_1", "click"));
            Assert.IsFalse(r2.allowed);
            Assert.AreEqual(ErrorDetector.WrongTargetId, r2.errorId);
            Assert.IsTrue(detector.Check(new ActionRequest("tool.caliper", "ip.carbon_strip_L.measure_mid", "click")).allowed);
            Assert.IsTrue(errors.All(e => e.severity == Severity.Minor));
        }
    }
}
