using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Procedures;
using PantographSim.Recording;
using PantographSim.Tools;
using PantographSim.Worksite;

namespace PantographSim.Tests
{
    /// <summary>
    /// 同一個值只有一個結論：降弓距離的步驟判定（SI_01 a 小動作、ValueInRange 完成條件）與全域安全規則（SR_Global_NotLowered）一致；
    /// 安裝距離（CSR_11，0.5 cm）一致；顯示往上進位不和判定矛盾；卡尺讀數；紀錄讀回不變；作業人員簽名。
    /// </summary>
    public class JudgementConsistencyTests
    {
        const string BoltL1 = "ip.carbon_strip_L.bolt_1";

        [TestCase(0.99f, true)]
        [TestCase(1.00f, true)]
        [TestCase(1.04f, false)]
        [TestCase(1.10f, false)]
        public void SI01_StepRules_And_GlobalRule_Agree(float distanceCm, bool lowered)
        {
            var sim = new SliceDryRunTests.SliceSim();
            var s = sim.s;
            s.SetFloat(SimIds.PanHeadLowerDistance, distanceCm);
            var step = sim.runner.Current;
            Assert.AreEqual("STP_SI_01_ConfirmLowered", step.RunId);

            bool subA = ChecklistEval.Done(s, ProcedureRunner.SubActions(step)[0].condition);          // SI_01 a 小動作
            var range = (step.def.completion as AllOf)?.rules.OfType<ValueInRange>().FirstOrDefault() ?? step.def.completion as ValueInRange;
            Assert.IsNotNull(range, "SI_01 完成條件裡的 ValueInRange");
            bool completion = RuleEvaluators.Evaluate(range, sim.runner.ContextFor(step));              // 完成條件
            bool panel = SimIds.PanHeadLowered(s);                                                      // 升降面板到位燈
            sim.Rotate(BoltL1, 180, "loosen");                                                          // 碰弓體：全域規則
            bool globalBlocksNotLowered = sim.blocked.Any(b => b.text == "ERR_Global_NotLowered");

            Assert.AreEqual(lowered, subA, "SI_01 a");
            Assert.AreEqual(lowered, completion, "ValueInRange");
            Assert.AreEqual(lowered, panel, "到位燈");
            Assert.AreEqual(!lowered, globalBlocksNotLowered, "SR_Global_NotLowered");
            // 顯示：往上進位到 0.1 cm，未到位不會顯示成 1.0
            string shown = SimIds.CmText(distanceCm);
            Assert.AreEqual(lowered, float.Parse(shown, System.Globalization.CultureInfo.InvariantCulture) <= 1.0f, $"顯示 {shown} 要和判定一致");
        }

        [TestCase(0.49f, true)]
        [TestCase(0.51f, false)]
        public void Install_StepCondition_And_PositionColor_Agree(float distanceCm, bool ok)
        {
            var s = new SimState(new EventBus());
            s.Set("part.carbon_strip_L.state", "NewMounted");
            s.SetFloat("part.carbon_strip_L.install_distance_cm", distanceCm);
            var bundle = ProcedureJsonConverter.Build(System.IO.File.ReadAllText("Assets/_Project/Modules/Procedures/Data/carbon_strip_slice.json"));
            var def = bundle.procedure.steps.First(x => x.stepId == "STP_CSR_11_Install");
            string cond = def.subActions.Select(a => StateKeys.Expand(a.condition, "L")).First(c => c.Contains("install_distance_cm"));
            Assert.AreEqual(ok, ChecklistEval.Done(s, cond), cond);
            Assert.AreEqual(ok, PositionCheckLogic.InPosition(distanceCm, 0.5f), "檢測點顏色");
        }

        [Test]
        public void Display_RoundsUp_NeverShowsZeroRemaining()
        {
            Assert.AreEqual("1.1", SimIds.CmText(1.04f));
            Assert.AreEqual("1.0", SimIds.CmText(1.0f));
            Assert.AreEqual("1.0", SimIds.CmText(1.0000001f), "浮點誤差不進位");
            Assert.AreEqual("0.1", SimIds.CmText(0.01f), "還差一點點也不會顯示 0.0");
            Assert.AreEqual("37.4", SimIds.CmText(37.4f));
            Assert.IsTrue(ValueInRangeEvaluator.InRange(1.0000001f, 0f, 1.0f), "恰好在門檻上（浮點誤差）算在範圍內");
            Assert.IsFalse(ValueInRangeEvaluator.InRange(1.001f, 0f, 1.0f));
        }

        [Test]
        public void Caliper_Raw3004_Shows300_JudgedReplace_Unchanged()
        {
            float reading = CaliperLogic.Reading(3.004f, true, null);   // 卡尺 0.01 mm 解析度（沒有改）
            Assert.AreEqual(3.00f, reading, 1e-5f);
            Assert.AreEqual("3.00", CaliperLogic.Display(reading));
            var bundle = ProcedureJsonConverter.Build(System.IO.File.ReadAllText("Assets/_Project/Modules/Procedures/Data/carbon_strip_slice.json"));
            var decide = bundle.procedure.steps.First(x => x.stepId == "STP_CSR_05_Decide");
            var rule = RuleEvaluators.FindDecision(decide.completion);
            Assert.AreEqual(3.0f, rule.threshold, "保養表值 3.0 mm 不變");
            Assert.IsTrue(reading <= rule.threshold, "學員看到 3.00 ≤ 3.0：該換");
        }

        [Test]
        public void Session_DistanceValues_RoundTripUnchanged()
        {
            float raw = 0.9437f;
            var line = new RecordLine
            {
                t = 12.5f, seq = 7, type = "LiftReleased", step = "STP_SI_01_ConfirmLowered",
                data = new Dictionary<string, object> { { "distanceCm", raw }, { "key", SimIds.PanHeadLowerDistance }, { "value", SimState.Format(raw) }, { "d", 1.0000001 } }
            };
            Assert.IsTrue(RecordLine.TryParse(line.ToJson(), out var back));
            Assert.AreEqual(raw, (float)back.N("distanceCm"), 0f, "float 寫入再讀回完全一樣");
            Assert.AreEqual("0.944", back.S("value"));
            Assert.AreEqual(1.0000001, back.N("d"), 0.0, "double 也不四捨五入");
            Assert.GreaterOrEqual(RecordFiles.SchemaVersion, 2, "距離改存原始值：schema 版本 2 起");   // R1 T3 升到 3（新增 PanelMoved），原本的斷言改成「至少 2」
        }

        [Test]
        public void Sign_ByCurrentStep_StartEnd_OtherwiseNotYet_NoError()
        {
            var bus = new EventBus(); var s = new SimState(bus);
            var written = new List<string>();
            bus.Subscribe<RecordWritten>(e => written.Add(e.pointId));

            s.Set("procedure.current", "STP_SI_06_RoofEntry");
            Assert.IsTrue(WorkOrder.Write(s, bus, WorkOrder.SignField, "true"));
            Assert.IsTrue(s.GetBool("workorder.sign_start") && s.GetBool("workorder.si_signed"), "開工簽名");
            Assert.IsFalse(s.Has("workorder.sign_end"));

            s.Set("procedure.current", "STP_WC_04_Unlock");
            Assert.IsTrue(WorkOrder.Write(s, bus, WorkOrder.SignField, "true"));
            Assert.IsTrue(s.GetBool("workorder.sign_end") && s.GetBool("workorder.unlock_signed"), "完工簽名");
            CollectionAssert.AreEqual(new[] { WorkOrder.SignStart, WorkOrder.SignEnd }, written);

            foreach (var cur in new[] { "", "STP_SI_01_ConfirmLowered", "STP_CSR_03_Measure_L" })
            {
                var t = new SimState(bus);
                t.Set("procedure.current", cur);
                Assert.IsFalse(WorkOrder.Write(t, bus, WorkOrder.SignField, "true"), cur);
                Assert.AreEqual(WorkOrder.NotYet, t.Get("ui.toast"));
                Assert.IsFalse(t.Keys.Any(k => k.StartsWith("workorder.")), "不寫入任何格子：" + cur);
            }

            // 流程中（CSR 進行中）按簽名：不算錯誤
            var sim = new SliceDryRunTests.SliceSim();
            sim.RunUntil("STP_CSR_03_Measure_L");
            Assert.IsFalse(WorkOrder.Write(sim.s, sim.bus, WorkOrder.SignField, "true"));
            Assert.AreEqual(0, sim.errors.Count);
            Assert.AreEqual("STP_CSR_03_Measure_L", sim.Current);
        }
    }
}
