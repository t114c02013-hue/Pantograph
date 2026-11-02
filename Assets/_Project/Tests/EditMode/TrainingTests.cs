using System.Collections.Generic;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Training;
using UnityEngine;

namespace PantographSim.Tests
{
    public class TrainingTests
    {
        EventBus bus; SimState s;

        [SetUp] public void SetUp() { bus = new EventBus(); s = new SimState(bus); }

        [Test]
        public void ErrorFeedback_CriticalPauses_RestartRequestsRedo()
        {
            using var fb = new ErrorFeedback(bus, s);
            int restarts = 0;
            bus.Subscribe<RestartStepRequested>(_ => restarts++);
            bus.Publish(new ErrorDetected { errorId = "ERR_Global_NotGrounded", severity = Severity.Critical, what = "w", why = "y", fix = "f", stepId = "STP_CSR_06_Loosen_L" });
            Assert.IsTrue(s.GetBool("training.paused"));
            Assert.AreEqual("w", s.Get("ui.error.what"));
            bus.Publish(new ActionRequested { targetId = "ui.error", text = "dismiss" });
            Assert.IsTrue(s.GetBool("training.paused"), "Critical 不能只按知道了");
            bus.Publish(new ActionRequested { targetId = "ui.error", text = "restart" });
            Assert.IsFalse(s.GetBool("training.paused"));
            Assert.AreEqual(1, restarts);
        }

        [Test]
        public void ErrorFeedback_MinorDoesNotOverrideCritical()
        {
            using var fb = new ErrorFeedback(bus, s);
            bus.Publish(new ErrorDetected { errorId = "C", severity = Severity.Critical, what = "critical" });
            bus.Publish(new ErrorDetected { errorId = "M", severity = Severity.Minor, what = "minor" });
            Assert.AreEqual("critical", s.Get("ui.error.what"));
        }

        [Test]
        public void Scoring_CountsUniquePerStep_AndFails_OnCritical()
        {
            using var sc = new Scoring(bus, s);
            sc.Start(0f);
            bus.Publish(new ErrorDetected { errorId = "A", stepId = "S1", severity = Severity.Minor });
            bus.Publish(new ErrorDetected { errorId = "A", stepId = "S1", severity = Severity.Minor });
            bus.Publish(new ErrorDetected { errorId = "B", stepId = "S2", severity = Severity.Major });
            bus.Publish(new ProcedureCompleted { time = 600f });
            Assert.AreEqual(1f, s.GetFloat("score.minor"));
            Assert.AreEqual(93f, s.GetFloat("score.total"));
            Assert.IsTrue(s.GetBool("score.passed"));
            bus.Publish(new ErrorDetected { errorId = "C", stepId = "S3", severity = Severity.Critical });
            bus.Publish(new ProcedureCompleted { time = 700f });
            Assert.IsFalse(s.GetBool("score.passed"));
        }

        [Test]
        public void Hints_TutorialShowsHighlights_PracticeDoesNot()
        {
            using var hs = new HintService(bus, s);
            var highlights = new List<string>();
            bus.Subscribe<HighlightRequested>(e => highlights.Add(e.text));
            s.Set("training.mode", "tutorial");
            bus.Publish(new StepStarted { highlightIds = "ip.carbon_strip_L.bolt_*", showBoltNumbers = true, showOutwardArrow = true, text = "依 1→4" });
            Assert.AreEqual("ip.carbon_strip_L.bolt_*", highlights[0]);
            Assert.IsTrue(s.GetBool("hint.show_arrow"));
            s.Set("training.mode", "practice");
            bus.Publish(new StepStarted { highlightIds = "ip.carbon_strip_L.bolt_*", showBoltNumbers = true, showOutwardArrow = true });
            Assert.AreEqual("", highlights[1]);
            Assert.IsFalse(s.GetBool("hint.show_arrow"), "箭頭只在教學模式出現");
        }

        [Test]
        public void ModeController_RaisedScenarioAllowsPoseToggleOnlyInPractice()
        {
            var mc = new ModeController(s);
            var scn = ScriptableObject.CreateInstance<PantographSim.Data.ScenarioDefinition>();
            scn.scenarioId = "SCN_Test"; scn.startRaised = true; scn.initialStates = new[] { "part.carbon_strip_L.true.mid=2.8" };
            mc.Begin(TrainingMode.Tutorial, scn);
            Assert.IsFalse(s.GetBool("training.allow_pose_toggle"));
            mc.Begin(TrainingMode.Practice, scn);
            Assert.IsTrue(s.GetBool("training.allow_pose_toggle"));
            Assert.AreEqual(2.8f, s.GetFloat("part.carbon_strip_L.true.mid"), 1e-4f);
        }
    }
}
