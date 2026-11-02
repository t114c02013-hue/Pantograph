using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.XR;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>R1 按鍵表（B 段 6）：唯一來源、同鍵同義、右搖桿只移動、XRInputMapper 依表送事件、左撇子對調。</summary>
    public class InputBindingTableTests
    {
        static InputBindingTable T => InputBindingTable.LoadDefault();
        static readonly System.Text.RegularExpressions.Regex Cjk = new System.Text.RegularExpressions.Regex(@"[一-鿿]");

        [Test]
        public void EveryContext_EveryActiveControl_HasChineseLabel()
        {
            Assert.IsTrue(T.Contexts.Contains(InputBindingTable.Empty) && T.Contexts.Contains(InputBindingTable.PanelOpen) && T.Contexts.Contains(InputBindingTable.ErrorOpen)
                && T.Contexts.Contains(InputBindingTable.Tutorial) && T.Contexts.Contains(InputBindingTable.ToolPrefix + SimIds.ToolSocket));
            foreach (var r in T.rows.Where(r => r.enabled && r.functionId != "None"))
                Assert.IsTrue(Cjk.IsMatch(r.labelKey ?? ""), $"{r.context} {r.control} 沒有中文標示");
        }

        [Test]
        public void SameControl_SameFunction_InAllContexts_RightStickOnlyMove()
        {
            foreach (var g in T.rows.GroupBy(r => r.control))
                Assert.AreEqual(1, g.Select(r => r.functionId).Distinct().Count(), g.Key + " 在不同情境的功能不一樣");
            Assert.IsTrue(T.rows.Where(r => r.control == "RightStick").All(r => r.functionId == "Move" && r.enabled), "右搖桿所有情境都只有移動");
        }

        [Test]
        public void Mapper_FollowsTable_EmptyTrigger_SocketA_ErrorA()
        {
            var m = new XRInputMapper(XrBindingTable.LoadDefault(), T);
            var use = m.Map(new XrFrame { rightTrigger = true }, new XrContext(), 0f).Single();
            Assert.AreEqual(T.FunctionOf("RightTrigger"), use.command, "空手按右扳機");
            m.Map(new XrFrame(), new XrContext(), 0.1f);
            var reset = m.Map(new XrFrame { a = true }, new XrContext { heldTool = SimIds.ToolSocket }, 0.2f).Single();
            Assert.AreEqual(T.FunctionOf("A"), reset.command, "拿套筒扳手按 A");
            Assert.IsTrue(T.Enabled(InputBindingTable.ToolPrefix + SimIds.ToolSocket, "A"));
            m.Map(new XrFrame(), new XrContext(), 0.3f);
            Assert.IsEmpty(m.Map(new XrFrame { a = true }, new XrContext { heldTool = SimIds.ToolSocket, dialogOpen = true, errorOpen = true }, 0.4f), "錯誤視窗開著按 A 沒作用");
            Assert.IsFalse(T.Enabled(InputBindingTable.ErrorOpen, "A"));
            m.Map(new XrFrame(), new XrContext(), 0.5f);
            Assert.AreEqual("Crouch", m.Map(new XrFrame { leftStickClick = true }, new XrContext(), 0.6f).Single().command, "左搖桿按下＝蹲低視角");
        }

        [Test]
        public void Context_Priority_ErrorThenPanelThenTutorialThenTool()
        {
            Assert.AreEqual(InputBindingTable.ErrorOpen, new XrContext { errorOpen = true, dialogOpen = true, heldTool = SimIds.ToolCaliper }.BindingContext);
            Assert.AreEqual(InputBindingTable.PanelOpen, new XrContext { dialogOpen = true, heldTool = SimIds.ToolCaliper }.BindingContext);
            Assert.AreEqual(InputBindingTable.Tutorial, new XrContext { tutorial = true }.BindingContext);
            Assert.AreEqual(InputBindingTable.ToolPrefix + SimIds.ToolCaliper, new XrContext { heldTool = SimIds.ToolCaliper }.BindingContext);
            Assert.AreEqual(InputBindingTable.Empty, new XrContext().BindingContext);
        }

        [Test]
        public void LeftHanded_SwapStillConsistent_AndHelpTextFromTable()
        {
            var swapped = T.rows.Select(r => HandednessMap.Map(r, true)).ToList();
            foreach (var g in swapped.GroupBy(r => r.control))
                Assert.AreEqual(1, g.Select(r => r.functionId).Distinct().Count(), "對調後 " + g.Key + " 仍只有一種功能");
            Assert.IsTrue(swapped.Where(r => r.functionId == "Move").All(r => r.control == "LeftStick" && r.hand == "Left"), "左撇子：移動換到左搖桿");
            Assert.AreEqual("RightTrigger", HandednessMap.Control(HandednessMap.Control("RightTrigger", true), true), "對調兩次回原樣");
            var help = XRInputMapper.HelpText(T, InputBindingTable.Empty);
            StringAssert.Contains("右搖桿", help); StringAssert.Contains("平滑移動", help);
            StringAssert.DoesNotContain("重設手上的工具", help, "空手時 A 沒作用，不列");
            StringAssert.Contains("重設手上的工具", XRInputMapper.HelpText(T, InputBindingTable.ToolPrefix + SimIds.ToolSocket));
        }
    }
}
