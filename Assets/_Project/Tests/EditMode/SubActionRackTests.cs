using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Procedures;
using PantographSim.Tools;

namespace PantographSim.Tests
{
    /// <summary>教學模式的小動作、工具架拿取與歸還、工具重設、↶ 上一步規則。</summary>
    public class SubActionRackTests
    {
        const string Json = @"{
          ""procedureId"": ""PRC_T"",
          ""steps"": [
            { ""stepId"": ""S1"", ""requiredToolId"": ""tool.socket_wrench"",
              ""subActions"": [
                { ""text"": ""拿起套筒扳手"", ""target"": ""env.tool_rack.slot_6"", ""done"": ""tool.active=tool.socket_wrench"" },
                { ""text"": ""換上 13 mm 套筒"", ""done"": ""tool.socket_wrench.size=13"" },
                { ""text"": ""鬆開螺栓"", ""target"": ""ip.carbon_strip_L.bolt_1"", ""done"": ""ip.carbon_strip_L.bolt_1.state=Loosened"" },
                { ""text"": ""放回工具架"", ""target"": ""env.tool_rack.slot_6"", ""done"": ""tool.active="" }
              ],
              ""completion"": { ""type"": ""StateEquals"", ""key"": ""ip.carbon_strip_L.bolt_1.state"", ""value"": ""Loosened"" } },
            { ""stepId"": ""S2"", ""completion"": { ""type"": ""StateEquals"", ""key"": ""x"", ""value"": ""y"" } }
          ]
        }";

        EventBus bus; SimState s; ProcedureRunner runner;

        void Setup(string mode)
        {
            bus = new EventBus(); s = new SimState(bus);
            s.Set("training.mode", mode);
            for (int k = 1; k <= 9; k++) RackLogic.Initialize(s, k);
            runner = new ProcedureRunner(ProcedureJsonConverter.Build(Json).procedure, bus, s);
            runner.Start();
        }

        int Sub => (int)s.GetFloat("procedure.sub_index");

        [Test]
        public void SubActions_AdvanceInOrder_AndStepNeedsAll()
        {
            Setup("tutorial");
            Assert.AreEqual(0, Sub);
            s.Set(SimIds.ActiveTool, SimIds.ToolSocket);
            Assert.AreEqual(1, Sub);
            s.Set("tool.socket_wrench.size", "13");
            Assert.AreEqual(2, Sub);
            s.Set("ip.carbon_strip_L.bolt_1.state", "Loosened");
            Assert.AreEqual(3, Sub);
            Assert.AreEqual("S1", runner.Current.RunId, "工具還沒放回，步驟不能完成");
            s.Set(SimIds.ActiveTool, "");
            Assert.AreEqual("S2", runner.Current.RunId);
        }

        [Test]
        public void SkippingSubAction_CannotCompleteStep()
        {
            Setup("tutorial");
            // 跳過拿工具、換套筒，直接讓螺栓鬆開：完成條件成立但小動作沒做完 → 不能完成
            s.Set("ip.carbon_strip_L.bolt_1.state", "Loosened");
            Assert.AreEqual(0, Sub);
            Assert.AreEqual("S1", runner.Current.RunId);
        }

        [Test]
        public void PracticeMode_AlsoRequiresSubActions()
        {
            Setup("practice");
            s.Set("ip.carbon_strip_L.bolt_1.state", "Loosened");
            Assert.AreEqual("S1", runner.Current.RunId, "練習模式也要做完小動作（只是不顯示）");
            RackLogic.TryPick(s, 6, SimIds.ToolSocket, "套筒扳手", "");
            s.Set("tool.socket_wrench.size", "13");
            RackLogic.TryReturn(s, 6, SimIds.ToolSocket, true);
            Assert.AreEqual("S2", runner.Current.RunId);
        }

        [Test]
        public void StepBack_RestoresToolRackChanges_RefusesPartChanges()
        {
            Setup("tutorial");
            Assert.AreEqual("已經是這一步的第一個小動作", runner.StepBackSubAction());
            Assert.IsFalse(s.GetBool("procedure.can_back"));

            Assert.IsNull(RackLogic.TryPick(s, 6, SimIds.ToolSocket, "套筒扳手", ""));
            Assert.AreEqual(1, Sub);
            Assert.IsTrue(s.GetBool("procedure.can_back"), "拿起工具只改了工具與工具架，可以退回");
            Assert.AreEqual("已退回上一個小動作", runner.StepBackSubAction());
            Assert.AreEqual(0, Sub);
            Assert.AreEqual("", s.Get(SimIds.ActiveTool), "退回＝工具放回架上");
            Assert.AreEqual(RackLogic.OnRack, s.Get("rack.slot_6"));

            RackLogic.TryPick(s, 6, SimIds.ToolSocket, "套筒扳手", "");
            s.Set("tool.socket_wrench.size", "13");
            Assert.AreEqual(2, Sub);
            Assert.AreEqual("已退回上一個小動作", runner.StepBackSubAction());
            Assert.AreEqual(1, Sub, "退回到「換上 13 mm 套筒」");
            Assert.IsFalse(s.Has("tool.socket_wrench.size"), "退回時還原那個設定，要重新選");
            Assert.AreEqual(SimIds.ToolSocket, s.Get(SimIds.ActiveTool), "更早的小動作（拿工具）不受影響");

            s.Set("tool.socket_wrench.size", "13");
            s.Set("ip.carbon_strip_L.bolt_1.state", "Loosened");
            Assert.AreEqual(3, Sub);
            StringAssert.Contains("不能退回", runner.StepBackSubAction(), "已經改變零件狀態的不能退回");
            Assert.AreEqual("Loosened", s.Get("ip.carbon_strip_L.bolt_1.state"));
            Assert.IsFalse(s.GetBool("procedure.can_back"));
        }

        [Test]
        public void UndoableKeys_OnlyToolRackUiControl()
        {
            foreach (var k in new[] { "tool.active", "tool.caliper.zeroed", "tools.all_returned", "rack.slot_5", "env.tool_rack.slot_5.x", "ui.workorder_open", "ctrl.lift.cmd" })
                Assert.IsTrue(ProcedureRunner.UndoableKey(k), k);
            foreach (var k in new[] { "ip.carbon_strip_L.bolt_1.state", "part.carbon_strip_L.state", "isolation.grounded", "lift.value", "workorder.decision", "hand.item" })
                Assert.IsFalse(ProcedureRunner.UndoableKey(k), k);
        }

        // ---------- 工具架 ----------
        [Test]
        public void Rack_PickReturn_OneToolAtATime()
        {
            bus = new EventBus(); s = new SimState(bus);
            for (int k = 1; k <= 9; k++) RackLogic.Initialize(s, k);
            Assert.IsNull(RackLogic.TryPick(s, 5, SimIds.ToolCaliper, "卡尺", ""));
            Assert.AreEqual(SimIds.ToolCaliper, s.Get(SimIds.ActiveTool));
            Assert.AreEqual(RackLogic.InHand, s.Get("rack.slot_5"));
            var msg = RackLogic.TryPick(s, 6, SimIds.ToolSocket, "套筒扳手", "卡尺");
            StringAssert.Contains("先把它放回工具架", msg, "手上一次只能一件，提示不算錯");
            Assert.AreEqual(SimIds.ToolCaliper, s.Get(SimIds.ActiveTool));
            Assert.IsNull(RackLogic.TryReturn(s, 5, SimIds.ToolCaliper, true));
            Assert.AreEqual("", s.Get(SimIds.ActiveTool));
            Assert.AreEqual(RackLogic.OnRack, s.Get("rack.slot_5"));
            Assert.IsTrue(s.GetBool("tools.all_returned"));
        }

        [Test]
        public void Rack_GroundingRodDeployed_ReturnOnlyAfterRemoved()
        {
            bus = new EventBus(); s = new SimState(bus);
            RackLogic.Initialize(s, 3);
            RackLogic.TryPick(s, 3, SimIds.ToolGroundingRod, "接地棒", "");
            RackLogic.Deploy(s, 3);
            Assert.AreEqual("", s.Get(SimIds.ActiveTool), "掛上後手空出來");
            Assert.AreEqual(RackLogic.InUse, s.Get("rack.slot_3"));
            StringAssert.Contains("使用中", RackLogic.TryReturn(s, 3, SimIds.ToolGroundingRod, false));
            Assert.IsNull(RackLogic.TryReturn(s, 3, SimIds.ToolGroundingRod, true));
            Assert.AreEqual(RackLogic.OnRack, s.Get("rack.slot_3"));
        }

        [Test]
        public void Reset_ClearsToolSettings_KeepsPartResults()
        {
            bus = new EventBus(); s = new SimState(bus);
            s.Set("tool.socket_wrench.size", "13"); s.Set("tool.socket_wrench.dir", "lock"); s.Set("tool.socket_wrench.engaged", "ip.carbon_strip_L.bolt_2");
            s.SetFloat("tool.torque_wrench.setting", 27f); s.SetFloat("tool.torque_wrench.current", 18f);
            s.SetBool("tool.caliper.zeroed", true); s.SetFloat("tool.caliper.jaw", 3f);
            s.Set("ip.carbon_strip_L.bolt_1.state", "Snug"); s.SetFloat("ip.carbon_strip_L.bolt_1.torque_Nm", 25f);
            foreach (var t in new[] { SimIds.ToolSocket, SimIds.ToolTorque, SimIds.ToolCaliper }) ToolResetLogic.Reset(s, t);
            Assert.AreEqual("10", s.Get("tool.socket_wrench.size"));
            Assert.AreEqual("loosen", s.Get("tool.socket_wrench.dir"));
            Assert.AreEqual("", s.Get("tool.socket_wrench.engaged"));
            Assert.AreEqual(20f, s.GetFloat("tool.torque_wrench.setting"));
            Assert.AreEqual(0f, s.GetFloat("tool.torque_wrench.current"));
            Assert.AreEqual(25f, s.GetFloat("tool.caliper.jaw"));
            Assert.IsFalse(s.GetBool("tool.caliper.zeroed"), "重設卡尺＝回到未歸零，要重新歸零");
            Assert.AreEqual("Snug", s.Get("ip.carbon_strip_L.bolt_1.state"), "零件狀態不動");
            Assert.AreEqual(25f, s.GetFloat("ip.carbon_strip_L.bolt_1.torque_Nm"));
        }

        [Test]
        public void RealData_EverySubActionHasTextAndCondition_AndRackSlotsMatchV5()
        {
            var bundle = ProcedureJsonConverter.Build(System.IO.File.ReadAllText("Assets/_Project/Modules/Procedures/Data/carbon_strip_slice.json"));
            var withSubs = bundle.procedure.steps.Where(st => !st.presetDone).ToList();
            Assert.IsTrue(withSubs.All(st => st.subActions.Length > 0), "每個非預設步驟都有小動作：" + string.Join(",", withSubs.Where(st => st.subActions.Length == 0).Select(st => st.stepId)));
            foreach (var st in withSubs)
                foreach (var a in st.subActions)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(a.text), st.stepId);
                    Assert.LessOrEqual(a.text.Length, 20, $"{st.stepId}「{a.text}」超過 20 字");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(a.condition), $"{st.stepId}「{a.text}」沒有完成條件");
                }
            // 兩側展開後的小動作總數（CSR_06–14 各兩次）；改了步驟資料要同步更新這個數字
            var runner = new ProcedureRunner(bundle.procedure, new EventBus(), new SimState(new EventBus()));
            Assert.AreEqual(144, runner.Steps.Sum(st => ProcedureRunner.SubActions(st).Length), "展開後的小動作總數");
            var slots = bundle.tools.ToDictionary(t => t.toolId, t => t.hotkey);
            Assert.AreEqual(1, slots["tool.lockout_tag"]); Assert.AreEqual(2, slots["tool.voltage_detector"]); Assert.AreEqual(3, slots["tool.grounding_rod"]);
            Assert.AreEqual(4, slots["tool.safety_harness"]); Assert.AreEqual(5, slots["tool.caliper"]); Assert.AreEqual(6, slots["tool.socket_wrench"]);
            Assert.AreEqual(7, slots["tool.torque_wrench"]); Assert.AreEqual(8, slots["tool.cleaning_cloth"]); Assert.AreEqual(9, slots["tool.marker"]);
        }
    }
}
