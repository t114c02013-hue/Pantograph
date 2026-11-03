using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Worksite;

namespace PantographSim.Tests
{
    /// <summary>
    /// R1.5b b4（5g、D43）：高阻計絕緣測試、接地桿放電、空手拆測試線（MeggerLogic，用場景同一種工具元件）、作業單（異常欄、絕緣電阻、合格判定、平台清點完成）。
    /// 數值都讀 ToolRulesDef（放電 5 s、合格 ≥ 100 MΩ、故障讀值 20 MΩ）。
    /// </summary>
    public class MeggerWorkOrderTests
    {
        static void Click(string target, string toolId) => Sim.Bus.Publish(new ActionRequested { targetId = target, text = "click", toolId = toolId });
        static ToolRulesDef Rules => ToolRulesDef.Load();

        [Test]
        public void D43_RulesFromData()
        {
            Assert.AreEqual(5f, Rules.dischargeSeconds, "D43：放電 5 秒");
            Assert.AreEqual(100f, Rules.irPassMinMOhm, "D43：合格下限 100 MΩ");
            Assert.AreEqual(20f, Rules.meggerFaultReadingMOhm);
            StringAssert.Contains("待高鐵 SOP 確認", Rules.irPassNote);
        }

        [Test]
        public void Megger_NeedsBothLeads_ReadingFromData_LeadsStayUntilRemovedByHand_DischargeNeedsContactTime()
        {
            float clock = 0f; var old = SimClock.Source; SimClock.Source = () => clock;
            try
            {
                using (var b = new BeltRig())
                {
                    b.s.Set("training.mode", "practice");
                    b.belt.ClickSlot(BeltRig.Table.FindTool(MeggerLogic.ToolId).slot);
                    Click(MeggerLogic.PanPoint, MeggerLogic.ToolId);
                    Assert.IsNotNull(MeggerLogic.Test(b.s, Rules), "只接一端：不能測");
                    Click(MeggerLogic.BodyPoint, MeggerLogic.ToolId);
                    Assert.AreEqual(2f, b.s.GetFloat(MeggerLogic.Count));
                    Sim.Bus.Publish(new ActionRequested { targetId = MeggerLogic.ToolId, text = "test" });
                    Assert.AreEqual(Rules.meggerReadingMOhm, b.s.GetFloat(MeggerLogic.Reading), "讀值讀資料（示例值）");
                    Assert.IsTrue(b.s.GetBool(MeggerLogic.Measured));
                    Assert.IsTrue(b.s.GetBool(MeggerLogic.Charged));

                    b.belt.ClickSlot(BeltRig.Table.FindTool(MeggerLogic.ToolId).slot);   // RT_02 d 放回
                    Assert.AreEqual(2f, b.s.GetFloat(MeggerLogic.Count), "D43：放下高阻計測試線照樣接著（e2 才空手拆）");
                    b.belt.ClickSlot(BeltRig.Table.FindTool(SimIds.ToolGroundingRod).slot);
                    Click(MeggerLogic.PanPoint, SimIds.ToolGroundingRod);
                    Assert.IsFalse(b.s.GetBool(SimIds.HeadClamped), "帶電時碰弓頭接地掛點＝放電，不是掛上");
                    clock += Rules.dischargeSeconds / 2f; b.belt.Active.Tick();
                    Assert.IsTrue(b.s.GetBool(MeggerLogic.Charged), "還沒放完");
                    clock += Rules.dischargeSeconds; b.belt.Active.Tick();
                    Assert.GreaterOrEqual(b.s.GetFloat(MeggerLogic.DischargeKey), Rules.dischargeSeconds);
                    Assert.IsFalse(b.s.GetBool(MeggerLogic.Charged), "接觸夠久：放完");
                    Assert.IsTrue(b.s.GetBool(MeggerLogic.Discharged));
                    Click(MeggerLogic.PanPoint, SimIds.ToolGroundingRod);   // 移開
                    foreach (var p in new[] { MeggerLogic.PanPoint, MeggerLogic.BodyPoint }) Assert.IsNull(MeggerLogic.RemoveLead(b.s, p, Rules));
                    Assert.AreEqual(0f, b.s.GetFloat(MeggerLogic.Count));
                    Assert.IsFalse(b.s.GetBool(MeggerLogic.LeadRemovedCharged), "放完電才拆：不記錯");
                    Click(MeggerLogic.PanPoint, SimIds.ToolGroundingRod);
                    Assert.IsTrue(b.s.GetBool(SimIds.HeadClamped), "放完電再點：照原本掛上接地桿");
                }
            }
            finally { SimClock.Source = old; }
        }

        static SimState Measured(string mode, bool fault = false)
        {
            var s = new SimState(new EventBus());
            s.Set("training.mode", mode);
            s.SetBool(MeggerLogic.FaultInsulatorDirty, fault);
            MeggerLogic.ConnectLead(s, MeggerLogic.PanPoint); MeggerLogic.ConnectLead(s, MeggerLogic.BodyPoint);
            return s;
        }

        [TestCase("tutorial")]
        [TestCase("practice")]
        [TestCase("exam")]
        public void ZeroReading_WhileGrounded_HintOnly_NoScore_AllModes(string mode)
        {
            var s = Measured(mode);
            s.SetBool(SimIds.HeadClamped, true);
            Assert.AreEqual(Rules.groundedHint, MeggerLogic.Test(s, Rules), "讀值 0：只提示");
            Assert.AreEqual(0f, s.GetFloat(MeggerLogic.Reading));
            Assert.IsFalse(s.GetBool(MeggerLogic.Measured), "不算量好，要重量");
            Assert.IsFalse(s.GetBool(MeggerLogic.Charged));
        }

        [Test]
        public void Fault_InsulatorDirty_Reads20_UntilAllFourCleaned()
        {
            var s = Measured("practice", fault: true);
            MeggerLogic.Test(s, Rules);
            Assert.AreEqual(Rules.meggerFaultReadingMOhm, s.GetFloat(MeggerLogic.Reading));
            Assert.IsFalse(MeggerLogic.Pass(s, Rules), "20 MΩ 不合格");
            for (int n = 1; n <= 3; n++) s.SetFloat(MeggerLogic.InsulatorId(n) + ".clean_ratio", 1f);
            MeggerLogic.Test(s, Rules);
            Assert.AreEqual(Rules.meggerFaultReadingMOhm, s.GetFloat(MeggerLogic.Reading), "還有一支沒擦淨");
            s.SetFloat(MeggerLogic.InsulatorId(4) + ".clean_ratio", Rules.insulatorCleanRatio);
            MeggerLogic.Test(s, Rules);
            Assert.AreEqual(Rules.meggerReadingMOhm, s.GetFloat(MeggerLogic.Reading), "4 支擦淨（≥ 95 %）：恢復正常讀值");
        }

        /// <summary>D43 v3.12：清除率只算計分格（背面不計分），達門檻整支變乾淨並記 wrapped；面向的行每行擦到 1 格＝已目視繞看一圈。</summary>
        [Test]
        public void InsulatorScore_BackNotScored_ThresholdCleansAll_FaceColumnsInspected()
        {
            var s = new SimState(new EventBus());
            const string id = "ip.roof.insulator_1";
            var score = new char[16 * 6]; for (int i = 0; i < score.Length; i++) score[i] = (i % 16) < 8 ? '1' : '0';   // 前 8 行計分
            string mask = new string(score), face = "0,1,2,3,4,5,6,7";
            PantographSim.Parts.Common.DirtMask.Clean(s, id, (12 + 0.5f) / 16f, 0.5f, 6, true, mask, face, 0.95f);
            Assert.AreEqual(0f, s.GetFloat(id + ".clean_ratio"), 1e-4f, "擦背面不計分");
            for (int c = 0; c < 8; c++) for (int r = 0; r < 6; r++)
                PantographSim.Parts.Common.DirtMask.Clean(s, id, (c + 0.5f) / 16f, (r + 0.5f) / 6f, 6, true, mask, face, 0.95f);
            Assert.AreEqual(1f, s.GetFloat(id + ".clean_ratio"), "計分格達門檻：整支擦淨");
            Assert.IsFalse(s.Get(id + ".dirt").Contains('1'), "背面也一起變乾淨");
            Assert.IsTrue(s.GetBool(id + ".wrapped"), "播拉一圈");
            Assert.IsTrue(s.GetBool(id + ".inspected"), "面向的 8 行都擦到＝已目視繞看一圈");
        }

        [Test]
        public void LeadRemovedBeforeDischarge_TutorialBlocks_PracticeRecords()
        {
            var t = Measured("tutorial"); MeggerLogic.Test(t, Rules);
            StringAssert.Contains("放電還沒滿", MeggerLogic.RemoveLead(t, MeggerLogic.PanPoint, Rules), "教學：擋下並提示倒數");
            Assert.AreEqual(2f, t.GetFloat(MeggerLogic.Count));
            Assert.IsFalse(t.GetBool(MeggerLogic.LeadRemovedCharged), "教學不記錯");
            Assert.IsNotNull(MeggerLogic.TutorialCountdown(t, Rules), "教學顯示倒數");
            var p = Measured("practice"); MeggerLogic.Test(p, Rules);
            Assert.IsNull(MeggerLogic.RemoveLead(p, MeggerLogic.PanPoint, Rules));
            Assert.IsTrue(p.GetBool(MeggerLogic.LeadRemovedCharged), "練習／考核：照拆並記錯（ERR_RT02_LeadRemovedBeforeDischarge）");
            Assert.IsNull(MeggerLogic.TutorialCountdown(p, Rules), "練習／考核不顯示倒數");
        }

        [Test]
        public void WorkOrder_Anomaly_IrReadingMismatch_PlatformCount()
        {
            var s = new SimState(new EventBus());
            s.Set("training.mode", "practice");
            Assert.IsFalse(WorkOrder.Write(s, null, WorkOrder.AnomalyField, "maybe"), "只能選無異常／有異常");
            Assert.IsTrue(WorkOrder.Write(s, null, WorkOrder.AnomalyField, "none"));
            Assert.IsTrue(s.GetBool("env.work_order.anomaly.checked"));
            s.SetFloat(MeggerLogic.Reading, 500f);
            Assert.IsTrue(WorkOrder.Write(s, null, WorkOrder.IrReadingField, "50"));
            Assert.IsTrue(s.GetBool("workorder.ir_reading.mismatch"), "和高阻計讀值不符（練習：照寫並記錯）");
            Assert.IsTrue(WorkOrder.Write(s, null, WorkOrder.IrReadingField, "500"));
            Assert.IsFalse(s.GetBool("workorder.ir_reading.mismatch"));
            Assert.IsTrue(WorkOrder.Write(s, null, WorkOrder.PlatformCountField, "true"));
            Assert.IsTrue(s.GetBool("workorder.toolcount_platform_confirmed"));

            var t = new SimState(new EventBus());
            t.Set("training.mode", "tutorial"); t.SetFloat(MeggerLogic.Reading, 500f);
            Assert.IsFalse(WorkOrder.Write(t, null, WorkOrder.IrReadingField, "50"), "教學：讀值不符只提示、不寫入");
            Assert.IsFalse(t.GetBool("workorder.ir_reading.mismatch"));
        }

        [TestCase("tutorial", 20f, "pass", false, false)]
        [TestCase("practice", 20f, "pass", true, true)]
        [TestCase("exam", 500f, "fail", true, true)]
        [TestCase("practice", 500f, "pass", true, false)]
        [TestCase("tutorial", 20f, "fail", true, false)]
        public void IrJudgement_TutorialBlocksWrong_PracticeRecords(string mode, float reading, string tick, bool written, bool wrong)
        {
            var s = new SimState(new EventBus());
            s.Set("training.mode", mode); s.SetFloat(MeggerLogic.Reading, reading);
            Assert.AreEqual(written, WorkOrder.Write(s, null, WorkOrder.IrJudgementField, tick));
            Assert.AreEqual(written, s.GetBool("workorder.ir_judged"));
            Assert.AreEqual(wrong, s.GetBool("workorder.ir_judgement_wrong"), "勾錯才記 ERR_RT02_WrongJudgement");
        }
    }
}
