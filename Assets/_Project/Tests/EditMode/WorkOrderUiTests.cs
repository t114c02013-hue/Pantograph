using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.UI;
using PantographSim.Worksite;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PantographSim.Tests
{
    /// <summary>
    /// 作業單回歸：從工作桌（場景點擊，pointId＝擊中點座標）打開、經由 EventSystem 按介面上的每一種按鈕（記錄、判定四選一、新滑板中點、清點完成、簽名、收起），
    /// 黑板值確實改變；打開作業單不會卡住步驟（CSR_01、WC_01）。之前漏掉是因為測試直接呼叫 WorkOrder.Write。
    /// </summary>
    public class WorkOrderUiTests
    {
        readonly List<GameObject> created = new List<GameObject>();
        EventSystem eventSystem;
        RectTransform safe;

        GameObject Make(string name, params System.Type[] types)
        {
            var go = new GameObject(name, types);
            created.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in created) if (go != null) Object.DestroyImmediate(go);
            created.Clear();
            ServiceLocator.Clear();
        }

        void Register(IEventBus bus, ISimState s)
        {
            ServiceLocator.Clear();
            ServiceLocator.Register<IEventBus>(bus);
            ServiceLocator.Register<ISimState>(s);
        }

        /// <summary>場景裡的作業單（WorkOrder 元件，與 SliceSceneBuilder 相同的 ID），手動呼叫 Start 讓它訂閱事件。</summary>
        void MakeTableWorkOrder()
        {
            var go = Make("WorkOrder");
            go.AddComponent<WorkOrder>().Configure(SimIds.WorkOrder, "作業單（點開）");
            typeof(SiteObject).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(go.GetComponent<WorkOrder>(), null);
        }

        WorkOrderView MakeView()
        {
            eventSystem = Make("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            var canvas = Make("Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            safe = Ui.Rect(canvas.transform, "Safe");
            safe.sizeDelta = new Vector2(1880, 1040);
            var view = new WorkOrderView();
            view.Build(safe, null);
            return view;
        }

        /// <summary>和滑鼠左鍵／VR 扳機點場景物件相同：pointId 是擊中點座標。</summary>
        static void ClickTable(string tool = "") =>
            Sim.Bus.Publish(new ActionRequested { targetId = SimIds.WorkOrder, toolId = tool, text = string.IsNullOrEmpty(tool) ? "click" : "click", pointId = "0.412,0.806,-1.25" });

        void Press(GameObject go) => ExecuteEvents.Execute(go, new PointerEventData(eventSystem), ExecuteEvents.pointerClickHandler);

        Button ButtonLabeled(WorkOrderView view, string label) =>
            view.Card.GetComponentsInChildren<Button>(true).First(b => b.GetComponentInChildren<Text>(true).text == label);

        Button SendButton(WorkOrderView view, string rowName) =>
            view.Card.GetComponentsInChildren<RectTransform>(true).First(r => r.name == rowName).GetComponentsInChildren<Button>(true).First(b => b.GetComponentInChildren<Text>(true).text == "送出");

        [Test]
        public void TableClick_OpensAndCloses_WithAndWithoutTool_NoJunkField()
        {
            var bus = new EventBus(); var s = new SimState(bus);
            Register(bus, s);
            MakeTableWorkOrder();
            ClickTable();
            Assert.IsTrue(s.GetBool("ui.workorder_open"), "徒手點工作桌上的作業單要打開（以前被當成填欄位）");
            Assert.IsFalse(s.Keys.Any(k => k.StartsWith("workorder.0.") || k.Contains(",")), "不能把擊中點座標當成欄位寫進黑板");
            ClickTable(SimIds.ToolCaliper);
            Assert.IsFalse(s.GetBool("ui.workorder_open"), "拿著卡尺也能點作業單收起（邊量邊抄）");
            ClickTable(SimIds.ToolCaliper);
            Assert.IsTrue(s.GetBool("ui.workorder_open"));
            // 介面填欄位照常
            Sim.Bus.Publish(new ActionRequested { targetId = SimIds.WorkOrder, text = "2.80", pointId = "reading.L.mid" });
            Assert.AreEqual("2.80", s.Get("workorder.reading.L.mid"));
            Assert.IsTrue(s.GetBool("ui.workorder_open"), "填欄位不會關掉作業單");
        }

        [Test]
        public void EveryButton_ViaEventSystem_ChangesBlackboard_AndPanelIsDockedRight()
        {
            var bus = new EventBus(); var s = new SimState(bus);
            Register(bus, s);
            MakeTableWorkOrder();
            var view = MakeView();
            var written = new List<string>();
            bus.Subscribe<RecordWritten>(e => written.Add(e.pointId + "=" + e.text));
            s.SetBool("training.running", true);
            ClickTable();
            view.Refresh(s);
            Assert.IsTrue(view.Card.gameObject.activeInHierarchy, "作業單面板要顯示");

            // 停靠右側、約 30% 寬，不是置中對話框；根節點沒有遮罩（不擋 3D 場景）
            Assert.AreEqual(new Vector2(1f, 1f), view.Card.anchorMin, "錨點在右上");
            Assert.AreEqual(1880f * WorkOrderView.WidthFraction, view.Card.sizeDelta.x, 1f);
            Assert.IsNull(view.Card.parent.GetComponent<Image>(), "沒有全畫面遮罩");

            // 記錄：兩側三點讀數＋送出
            s.SetFloat("ip.carbon_strip_L.measure_left.reading", 2.81f);
            foreach (var (p, v) in new[] { ("left", "2.81"), ("mid", "2.79"), ("right", "3.12") }) view.Fields[$"reading.L.{p}"].text = v;
            Press(SendButton(view, "Row_L").gameObject);
            Assert.AreEqual("2.81", s.Get("workorder.reading.L.left"));
            Assert.AreEqual("2.79", s.Get("workorder.reading.L.mid"));
            Assert.AreEqual("3.12", s.Get("workorder.reading.L.right"));
            Assert.IsTrue(s.GetBool("workorder.reading.L.submitted"));
            Assert.IsFalse(s.GetBool("workorder.reading.L.left.mismatch"));
            foreach (var p in new[] { "left", "mid", "right" }) view.Fields[$"reading.R.{p}"].text = "3.80";
            Press(SendButton(view, "Row_R").gameObject);
            Assert.IsTrue(s.GetBool("workorder.reading.R.submitted"));
            CollectionAssert.IsSubsetOf(new[] { "reading.L.left=2.81", "submit.L=true", "submit.R=true" }, written, "RecordWritten 在 Commit 後照常發出");

            // 判定四選一
            foreach (var (label, value) in new[] { ("只換靠關節", "L"), ("只換外側", "R"), ("合格", "pass"), ("兩條都換", "both") })
            {
                Press(ButtonLabeled(view, label).gameObject);
                Assert.AreEqual(value, s.Get("workorder.decision"), label);
            }

            // 新滑板中點
            view.Fields["newmid.L"].text = "12.01"; view.Fields["newmid.R"].text = "11.98";
            Press(SendButton(view, "NewMid").gameObject);
            Assert.AreEqual(0.03f, s.GetFloat("workorder.height_diff"), 1e-4f);

            // 清點完成、簽名
            Press(ButtonLabeled(view, "確認工具與零件清點").gameObject);
            Assert.IsTrue(s.GetBool("workorder.toolcount_confirmed"));
            var signBtn = ButtonLabeled(view, "作業人員簽名");
            var sign = signBtn.gameObject;
            view.Refresh(s);
            Assert.IsFalse(signBtn.interactable, "不在開工／完工步驟：按鈕變灰、不能按");
            Assert.IsTrue(view.Card.GetComponentsInChildren<Text>().Any(t => t.text.Contains("開工／完工時才能簽")), "旁邊有說明小字");
            Press(sign);
            Assert.IsFalse(s.Has("workorder.sign_start") || s.Has("workorder.sign_end"), "灰色按鈕按了不寫入");
            s.Set("procedure.current", "STP_SI_06_RoofEntry");
            view.Refresh(s);
            Assert.IsTrue(signBtn.interactable);
            Press(sign);
            Assert.IsTrue(s.GetBool("workorder.sign_start"), "開工簽名");
            s.Set("procedure.current", "STP_WC_04_Unlock");
            view.Refresh(s);
            Press(sign);
            Assert.IsTrue(s.GetBool("workorder.sign_end"), "完工簽名");

            // 點格子＝選中（VR 一按填入的目標），填完跳下一格
            Press(view.Fields["reading.R.mid"].gameObject);
            Assert.AreEqual("reading.R.mid", s.Get(WorkOrderView.SelectedKey));
            Assert.AreEqual("reading.R.right", WorkOrderView.NextField("reading.R.mid"));
            Assert.AreEqual("reading.R.mid", XrWristView.FillTarget(s), "VR 一按填入填進選中的格子");

            // 收起
            Press(ButtonLabeled(view, "收起").gameObject);
            Assert.IsFalse(s.GetBool("ui.workorder_open"));
            view.Refresh(s);
            Assert.IsFalse(view.Card.gameObject.activeInHierarchy);
        }

        [Test]
        public void HoldingTool_ClickWorkOrder_NoE1_NotToolUse_AndPanelClicksDoNotPassThrough()
        {
            var sim = new SliceDryRunTests.SliceSim();   // SI_01：還在升弓，碰弓體會觸發 E1
            Register(sim.bus, sim.s);
            MakeTableWorkOrder();
            var view = MakeView();
            sim.s.SetBool("training.running", true);

            // 真的工具列＋卡尺（與場景相同的元件），拿起卡尺
            var beltGo = Make("ToolBelt");
            var toolGo = new GameObject("Caliper"); toolGo.transform.SetParent(beltGo.transform, false);
            toolGo.AddComponent<PantographSim.Tools.CaliperTool>().Setup(null, SimIds.ToolCaliper);
            var belt = beltGo.AddComponent<PantographSim.Tools.ToolBelt>();
            foreach (var m in new[] { "Awake", "Start" })
                typeof(PantographSim.Tools.ToolBelt).GetMethod(m, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(belt, null);
            belt.ClickSlot(0);
            Assert.AreEqual(SimIds.ToolCaliper, sim.s.Get(SimIds.ActiveTool));

            int performed = 0, measured = 0;
            sim.bus.Subscribe<ActionPerformed>(_ => performed++);
            sim.bus.Subscribe<MeasurementTaken>(_ => measured++);
            int blockedBefore = sim.blocked.Count;
            ClickTable(SimIds.ToolCaliper);
            Assert.IsTrue(sim.s.GetBool("ui.workorder_open"), "拿著卡尺點作業單＝打開");
            Assert.AreEqual(0, sim.errors.Count, "不能觸發 E1 或任何錯誤：" + string.Join(",", sim.errors.Select(e => e.errorId)));
            Assert.AreEqual(blockedBefore, sim.blocked.Count, "沒有被守門者擋（不是工具動作）");
            Assert.AreEqual(0, performed + measured, "不算成工具使用");
            Assert.AreEqual(SimIds.ToolCaliper, sim.s.Get(SimIds.ActiveTool), "手上的工具不變");

            // 桌面：只有面板本身擋點選（根節點沒有可被射線打到的圖形），滑鼠在面板上時 Picker 不送點選到後面的場景
            view.Refresh(sim.s);
            var cardImage = view.Card.GetComponent<UnityEngine.UI.Image>();
            Assert.IsTrue(cardImage != null && cardImage.raycastTarget, "面板本身接住點擊");
            Assert.IsFalse(view.Card.parent.GetComponents<UnityEngine.UI.Graphic>().Any(g => g.raycastTarget), "面板以外不擋點選");
            Assert.IsFalse(PantographSim.Interaction.Picker.ShouldSendClick(overUi: true, paused: false, running: true), "點在面板上不穿透");
            Assert.IsTrue(PantographSim.Interaction.Picker.ShouldSendClick(overUi: false, paused: false, running: true), "面板外照常點場景");
            Assert.IsFalse(sim.s.GetBool("training.paused"), "作業單打開不暫停");
        }

        [Test]
        public void OpeningFromTable_DoesNotBlockSteps_CSR01_And_WC01()
        {
            var sim = new SliceDryRunTests.SliceSim();
            Register(sim.bus, sim.s);
            MakeTableWorkOrder();
            var view = MakeView();
            sim.s.SetBool("training.running", true);
            sim.RunUntil("STP_CSR_01_CheckIsolation");
            ClickTable();   // 從工作桌打開作業單＝CSR_01 的小動作
            Assert.AreNotEqual("STP_CSR_01_CheckIsolation", sim.Current, "從工作桌打開作業單後 CSR_01 要通過：" + sim.Describe());
            Assert.IsFalse(sim.s.GetBool("training.paused"), "打開作業單不暫停");
            view.Refresh(sim.s);
            Press(ButtonLabeled(view, "收起").gameObject);

            sim.RunUntil("STP_WC_01_ToolCount");
            sim.s.SetBool("ui.rack_viewed", true); sim.s.SetBool("ui.tray_viewed", true);   // 前兩個小動作（查看工具架、零件盒）
            ClickTable();
            view.Refresh(sim.s);
            Press(ButtonLabeled(view, "確認工具與零件清點").gameObject);
            Assert.AreNotEqual("STP_WC_01_ToolCount", sim.Current, "按作業單的清點完成後 WC_01 要通過：" + sim.Describe());
            sim.RunUntil(null);
            Assert.IsTrue(sim.completed, sim.Describe());
            Assert.AreEqual(0, sim.errors.Count, string.Join(",", sim.errors.Select(e => e.errorId)));
        }
    }
}
