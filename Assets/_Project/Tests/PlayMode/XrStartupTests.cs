using System.Collections;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.XR;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PantographSim.Tests
{
    /// <summary>2026-11-04 試玩修正 第 0 點：VR 開場時序（XrStartupSequencer）。用模擬的追蹤（ProbeOverride）跑真場景。</summary>
    public class XrStartupTests
    {
        const string Scene = "SC_CarbonStripSlice";
        bool tracked; float headY;

        [SetUp]
        public void SetUp()
        {
            tracked = false; headY = 0f;
            XrStartupSequencer.ProbeOverride = () => (tracked, headY);
            XrModeSwitch.SimulateRequested = true;
        }

        [TearDown]
        public void TearDown()
        {
            XrStartupSequencer.ProbeOverride = null;
            XrModeSwitch.SimulateRequested = false;
        }

        static XrRig Rig => Object.FindObjectOfType<XrRig>(true);
        static Vector3 Point(string id) => Object.FindObjectsOfType<XrTeleportPoint>(true).First(p => p.PointId == id).transform.position;
        static PanelLayout Layout => Resources.Load<PanelLayout>(PanelLayout.ResourceName);

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        IEnumerator Boot(Vector3 headInRoom, string startStation = null)
        {
            SceneManager.LoadScene(Scene);
            for (int i = 0; i < 10; i++) yield return null;
            if (startStation != null) Sim.State.Set(SimIds.CourseStartStation, startStation);
            Rig.Head.localPosition = headInRoom;
            headY = headInRoom.y;
        }

        IEnumerator WaitReady()
        {
            for (float t = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t < Layout.startupTimeoutSec + 3f && !Sim.State.GetBool(XrStartupSequencer.ReadyKey);) yield return null;
            Assert.IsTrue(Sim.State.GetBool(XrStartupSequencer.ReadyKey), "開場要走完");
            yield return null;
        }

        static Transform Dialog() { Assert.IsTrue(Sim.Registry.TryGetTransform("xr.panel.dialog", out var t), "對話框要登記"); return t; }

        [UnityTest]
        public IEnumerator TrackingLate_NothingPlacedBefore_HeadAtStartAndCardInFrontAfter()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f));
            for (float t = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t < 2f;) yield return null;
            Assert.IsFalse(InputMode.XrActive, "追蹤有效之前不切 VR 模式");
            Assert.IsFalse(Sim.State.GetBool(XrStartupSequencer.ReadyKey));
            Assert.AreEqual("", Sim.State.Get("xr.point"), "追蹤有效之前不擺人");
            tracked = true;
            yield return WaitReady();
            Assert.IsTrue(InputMode.XrActive);
            Assert.AreEqual("tp.platform.lift", Sim.State.Get("xr.point"));
            Assert.Less(Flat(Rig.Head.position, Point("tp.platform.lift")), 0.05f, "頭在開場站點中心");
            Assert.AreEqual(Layout.taskCardDistance, Flat(Dialog().position, Rig.Head.position), 0.05f, "任務卡在頭前");
            Assert.AreEqual(1.0f, Layout.taskCardDistance, 1e-4f, "8.5：眼前 1.0 m");
            Assert.AreEqual(0.15f, Layout.taskCardBelowEye, 1e-4f, "8.5：眼下 0.15 m");
            var to = Dialog().position - Rig.Head.position; to.y = 0f;
            var f = Rig.Head.forward; f.y = 0f;
            Assert.Less(Vector3.Angle(f, to), 3f, "任務卡在視線正前方");
            Assert.AreEqual(Rig.Head.position.y - Layout.taskCardBelowEye, Dialog().position.y, 0.05f, "比眼睛低 0.15 m");

            var before = Dialog().position;
            Rig.Head.Rotate(0f, 90f, 0f);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.Less((Dialog().position - before).magnitude, 1e-3f, "頭轉了，任務卡固定不動");
        }

        /// <summary>第 1 點：沒有重力、不會一直掉；Origin 掉到地板下 1 m 以下就放回站點；頭走出範圍被推回。</summary>
        [UnityTest]
        public IEnumerator NoFall_SafetyNetReturnsToStation_HeadPushedBack()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f));
            tracked = true;
            yield return WaitReady();
            var origin = Rig.Head.parent.parent;
            float y = origin.position.y;
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(y, origin.position.y, 1e-4f, "沒有重力");
            origin.position += Vector3.down * 5f;
            yield return null; yield return null;
            Assert.AreEqual(y, origin.position.y, 0.01f, "下一個 tick 回到站點");

            var def = Resources.Load<BoundaryDef>(BoundaryDef.ResourceName);
            var before = Rig.Head.position;
            origin.position += Vector3.left * 2f;   // 頭走到平台外面
            yield return null; yield return null;
            Assert.Less(HeadBoundaryPusher.Push(def.closed, Rig.Head.position).magnitude, 0.01f, "頭被推回範圍內");
            Assert.AreEqual(before.y, Rig.Head.position.y, 0.01f, "只推水平");
        }

        /// <summary>第 3 點：作業單第一次打開在眼前 0.5 m；移動後位置不變；關掉再開同一個位置；傳送後相對學員不變。</summary>
        [UnityTest]
        public IEnumerator WorkOrder_OpensInFront_StaysWhereMoved_FollowsStation()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f));
            tracked = true;
            yield return WaitReady();
            Assert.IsTrue(Sim.Registry.TryGetTransform("xr.panel.workorder", out var clip));
            Sim.State.SetBool("training.running", true);
            Sim.State.SetBool("ui.workorder_open", true);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(0.5f, Flat(clip.position, Rig.Head.position), 0.05f, "8.5：眼前 0.5 m");
            Assert.AreEqual(Rig.Head.position.y - 0.15f, clip.position.y, 0.05f, "8.5：眼下 0.15 m");

            var panels = Object.FindObjectOfType<XrPanels>();
            var moved = clip.position + Rig.Head.right * 0.3f;
            panels.MovePanel("workorder", moved, clip.rotation);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.Less((clip.position - moved).magnitude, 1e-3f, "放開就停在原地，不會掉也不會彈回");
            Sim.State.SetBool("ui.workorder_open", false);
            for (int i = 0; i < 3; i++) yield return null;
            Sim.State.SetBool("ui.workorder_open", true);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.Less((clip.position - moved).magnitude, 1e-3f, "關掉再開在同一個位置");

            var tp = Object.FindObjectOfType<XrTeleport>();
            var (before, _) = PanelLogic.ToLocal(tp.Find("tp.platform.lift").transform, clip.position, clip.rotation);
            var rack = tp.Find("tp.platform.anchor");
            tp.TeleportTo(rack.transform.position, rack.transform.rotation, rack, immediate: true);
            for (int i = 0; i < 5; i++) yield return null;
            var (after, _) = PanelLogic.ToLocal(rack.transform, clip.position, clip.rotation);
            Assert.Less((after - before).magnitude, 0.01f, "傳送之後相對學員的位置不變");
        }

        /// <summary>第 4 點：手靠近工具袋：訓練沒開始會高亮、震動、提示，拿不到工具；開始後按握把拿出、在格子裡放開放回。</summary>
        [UnityTest]
        public IEnumerator Pouch_HoverHintBeforeStart_TakeAndReturnAfter()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f));
            tracked = true;
            yield return WaitReady();
            yield return null;
            var hover = Object.FindObjectOfType<XrPouchHover>();
            Assert.IsTrue(Sim.Registry.TryGetPosition("env.tool_rack.slot_6", out var slot));
            Rig.LeftHand.position = Vector3.zero; Rig.RightHand.position = slot;
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(Sim.Registry.TryGetPosition("env.tool_rack.slot_6", out slot));
            Rig.RightHand.position = slot;
            yield return null;
            Assert.AreEqual(6f, Sim.State.GetFloat(XrPouchHover.SlotKey), "指到第 6 格");
            Assert.GreaterOrEqual(hover.HoverCount, 1, "輕震一次");
            Assert.AreEqual(ToolPouchDef.Load().notStartedHint, Sim.State.Get("ui.toast"), "還沒開始：提示");
            XrPlayer.ScriptedFrame = new XrFrame { rightGrip = true };
            yield return null; yield return null;
            XrPlayer.ScriptedFrame = new XrFrame();
            yield return null;
            Assert.AreEqual("", Sim.State.Get(SimIds.ActiveTool), "還沒開始拿不到工具");

            Sim.State.SetBool("training.running", true);
            XrPlayer.ScriptedFrame = new XrFrame { rightGrip = true };
            yield return null; yield return null;
            Assert.AreNotEqual("", Sim.State.Get(SimIds.ActiveTool), "按握把拿出工具");
            XrPlayer.ScriptedFrame = new XrFrame();
            yield return null; yield return null;
            XrPlayer.ScriptedFrame = null;
        }

        /// <summary>第 5 點：原廠模型掛在控制器上（Transform 0、錨點和報告一致、方塊拿掉），扳機按到底模型跟著到 max。</summary>
        [UnityTest]
        public IEnumerator Controller_ModelMounted_TriggerMoves()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f));
            tracked = true;
            yield return WaitReady();
            var right = Rig.RightHand;
            var model = right.GetComponentInChildren<XrControllerVisual>(true);
            Assert.NotNull(model, "右手有模型");
            Assert.AreEqual("Visual_Offset", model.transform.parent.name);
            Assert.AreEqual(Vector3.zero, model.transform.localPosition);
            Assert.AreEqual(Quaternion.identity, model.transform.localRotation);
            Assert.IsNull(right.Find("Ring"), "舊的方塊和環拿掉");
            Assert.AreEqual(0, model.GetComponentsInChildren<TextMesh>(true).Length, "按鍵說明不放在模型上");
            StringAssert.Contains("＝", Sim.State.Get("xr.keys.card"), "說明在小卡上");
            Assert.IsNotNull(Rig.LeftHand.GetComponentInChildren<XrControllerVisual>(true), "左手有模型");
            var nodes = model.GetComponentsInChildren<Transform>(true);
            Transform N(string n) => System.Array.Find(nodes, t => t.name == n);
            var lbl = right.InverseTransformPoint(N("LBL_Trigger").position) * 1000f;
            Assert.Less((lbl - new Vector3(-12.3f, -48.3f, 58.7f)).magnitude, 1f, "錨點和報告一致（mm）：" + lbl);
            var b = model.GetComponentInChildren<Renderer>().bounds;
            foreach (var r in model.GetComponentsInChildren<Renderer>()) if (r.GetComponent<TextMesh>() == null) b.Encapsulate(r.bounds);
            Assert.Less(b.size.magnitude, 0.4f, "大小是控制器的大小：" + b.size);
            Assert.Greater(b.size.magnitude, 0.05f, "大小是控制器的大小：" + b.size);

            XrControllerVisual.Scripted = new XrControllerVisual.Buttons { trigger = 1f };
            yield return null; yield return null;
            XrControllerVisual.Scripted = null;
            Assert.Less((N("xr_standard_trigger_pressed_value").localRotation.eulerAngles - N("xr_standard_trigger_pressed_max").localRotation.eulerAngles).magnitude, 0.01f, "扳機到底");
        }

        /// <summary>terry 試玩：身上的工具實走一次：左右手靠近工具袋每一格、按握把拿出、在格子放開放回；腰包、胸前扣環也按得到。教學模式手在 0.30 m 內就拿到這一步要的工具。</summary>
        [UnityTest]
        public IEnumerator BodyItems_Walkthrough_BothHands()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f));
            tracked = true;
            yield return WaitReady();
            Sim.State.SetBool("training.running", true);
            Sim.State.SetBool("xr.tutorial.active", false);
            yield return null;
            XrPlayer.ScriptedFrame = new XrFrame();
            Rig.Head.position = Rig.Head.position;   // 工具袋跟著頭
            yield return null; yield return null;
            Assert.IsTrue(Sim.Registry.TryGetTransform("env.tool_bag", out var bag));
            var slots = SimIds.RackSlotNumbers(Sim.State).Where(k => Sim.Registry.TryGetTransform("env.tool_rack.slot_" + k, out var t) && t.IsChildOf(bag)).ToArray();
            Assert.Greater(slots.Length, 3, "工具袋有格子");
            Assert.Less(Rig.Head.position.y - bag.position.y, 0.62f, "手自然下垂搆得到");
            foreach (bool left in new[] { false, true })
                foreach (int k in slots)
                {
                    var hand = left ? Rig.LeftHand : Rig.RightHand;
                    var other = left ? Rig.RightHand : Rig.LeftHand;
                    Sim.Registry.TryGetPosition("env.tool_rack.slot_" + k, out var at);
                    other.position = at + Vector3.up * 2f;
                    hand.position = at + new Vector3(0.02f, 0.02f, 0f);
                    yield return null;
                    XrPlayer.ScriptedFrame = left ? new XrFrame { leftGrip = true } : new XrFrame { rightGrip = true };
                    yield return null; yield return null;
                    XrPlayer.ScriptedFrame = new XrFrame();
                    yield return null; yield return null;
                    Sim.State.Set("ui.tutorial.tool", "");
                    string held = Sim.State.Get(SimIds.ActiveTool);
                    Assert.AreNotEqual("", held, (left ? "左手" : "右手") + "拿不到第 " + k + " 格");
                    XrPlayer.ScriptedFrame = left ? new XrFrame { leftGrip = true } : new XrFrame { rightGrip = true };
                    yield return null; yield return null;
                    XrPlayer.ScriptedFrame = new XrFrame();
                    yield return null; yield return null;
                    Assert.AreEqual("", Sim.State.Get(SimIds.ActiveTool), (left ? "左手" : "右手") + "放不回第 " + k + " 格");
                }

            int asked = 0; string target = "";
            Sim.Bus.Subscribe<ActionRequested>(e => { asked++; target = e.targetId; });
            foreach (bool left in new[] { false, true })
                foreach (var id in new[] { "env.waist_pouch", PantographSim.Core.BodyRings.ChestRing })
                {
                    var hand = left ? Rig.LeftHand : Rig.RightHand;
                    (left ? Rig.RightHand : Rig.LeftHand).position = Vector3.up * 50f;
                    Assert.IsTrue(Sim.Registry.TryGetPosition(id, out var at), id);
                    hand.position = at;
                    asked = 0;
                    XrPlayer.ScriptedFrame = left ? new XrFrame { leftGrip = true } : new XrFrame { rightGrip = true };
                    yield return null; yield return null;
                    XrPlayer.ScriptedFrame = new XrFrame();
                    yield return null; yield return null;
                    Assert.Greater(asked, 0, id + (left ? " 左手" : " 右手") + "按握把沒有反應");
                }

            Sim.State.Set("training.mode", "tutorial");
            Sim.Bus.Publish(new HighlightRequested { text = "env.tool_rack.slot_" + slots[0] });
            Rig.LeftHand.position = Vector3.up * 50f;
            yield return null;
            Sim.Registry.TryGetPosition("env.tool_rack.slot_" + slots[0], out var want);
            Rig.RightHand.position = want + Vector3.right * 0.25f;
            yield return null;
            Assert.AreEqual(slots[0], (int)Sim.State.GetFloat(XrPouchHover.TargetKey), "記下這一步要拿的格子");
            StringAssert.Contains("工具袋", Sim.State.Get("ui.toast"), "提示怎麼拿");
            XrPlayer.ScriptedFrame = new XrFrame { rightGrip = true };
            yield return null; yield return null;
            Assert.AreNotEqual("", Sim.State.Get(SimIds.ActiveTool), "手在附近就拿到這一步要的工具");
            XrPlayer.ScriptedFrame = null;
        }

        /// <summary>e、f、g：作業單不在視線內打開時回到眼前；教學拿錯工具提示正確工具和位置；已到站按「找目標」有提示。</summary>
        [UnityTest]
        public IEnumerator WorkOrderBackInView_WrongToolHint_FocusAtStation()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f));
            tracked = true;
            yield return WaitReady();
            Sim.State.SetBool("training.running", true);
            Sim.State.SetBool("xr.tutorial.active", false);
            Sim.State.Set("training.mode", "tutorial");
            Assert.IsTrue(Sim.Registry.TryGetTransform("xr.panel.workorder", out var clip));
            Sim.State.SetBool("ui.workorder_open", true);
            for (int i = 0; i < 5; i++) yield return null;
            Sim.State.SetBool("ui.workorder_open", false);
            for (int i = 0; i < 3; i++) yield return null;
            Object.FindObjectOfType<XrPanels>().MovePanel("workorder", Rig.Head.position - Rig.Head.forward * 0.6f, clip.rotation);
            Sim.State.SetBool("ui.workorder_open", true);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.Greater(Vector3.Dot((clip.position - Rig.Head.position).normalized, Rig.Head.forward), 0.5f, "在背後打開要回到眼前");

            Sim.State.Set("hint.sub_tool", "tool.caliper");
            Sim.Bus.Publish(new ToolPickRequested { targetId = "env.tool_rack.slot_6", value = 6, time = 0f });
            yield return null;
            StringAssert.Contains("工具袋第 5 格", Sim.State.Get("ui.toast"), "拿錯工具提示");
            Sim.State.Set("hint.sub_tool", "");

            var st = Object.FindObjectOfType<XrStations>();
            Sim.State.Set("hint.sub_text", "測試小動作"); Sim.State.Set("hint.sub_stand", "");
            st.Focus();
            StringAssert.Contains("測試小動作", Sim.State.Get("ui.toast"), "已到站也有反應");
        }

        /// <summary>第 13 步：拿套筒扳手點螺栓盤，把這一側脫出的螺栓收進盤子。</summary>
        [UnityTest]
        public IEnumerator SocketWrench_ClickTray_TakesOutBolt()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f));
            tracked = true;
            yield return WaitReady();
            Sim.State.SetBool("training.running", true);
            Sim.Bus.Publish(new ToolPickRequested { targetId = "env.tool_rack.slot_6", value = 6, time = 0f });
            yield return null;
            Assert.AreEqual(SimIds.ToolSocket, Sim.State.Get(SimIds.ActiveTool));
            Sim.State.Set("ip.carbon_strip_L.bolt_1.state", "Out");
            yield return null;
            ServiceLocator.Register<IActionGuard>(new CompositeActionGuard());   // 只測收螺栓，安全規則另有測試
            Sim.Bus.Publish(new ActionRequested { targetId = "env.bolt_tray_L", toolId = SimIds.ToolSocket, text = "click", time = 0f });
            yield return null;
            Assert.AreEqual("InTray", Sim.State.Get("ip.carbon_strip_L.bolt_1.state"), "收進盤子");
            Assert.AreEqual("", Sim.State.Get(SimIds.ToolSocket + ".engaged"), "不會套上盤子");
        }

        /// <summary>第 6 點：拿著上鎖掛牌組按右搖桿，鎖和牌切換；高阻計等於按測試鍵；空手沒作用。</summary>
        [UnityTest]
        public IEnumerator RightStickClick_ToolSecondary()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f));
            tracked = true;
            yield return WaitReady();
            Sim.State.SetBool("training.running", true);
            int sent = 0; string text = "", target = "";
            Sim.Bus.Subscribe<ActionRequested>(e => { if (e.text == SimIds.ToolSecondaryCommand) { sent++; text = e.text; target = e.targetId; } });
            IEnumerator Click()
            {
                Sim.State.SetBool("xr.tutorial.active", false); Sim.State.Set("ui.tutorial.tool", "");   // 第一次拿工具的教學卡
                yield return null;
                XrPlayer.ScriptedFrame = new XrFrame { rightStickClick = true };
                yield return null; yield return null;
                XrPlayer.ScriptedFrame = new XrFrame();
                yield return null; yield return null;
            }
            XrPlayer.ScriptedFrame = new XrFrame();
            yield return null;
            yield return Click();
            Assert.AreEqual(0, sent, "空手沒作用");

            Sim.State.Set(SimIds.ActiveTool, "tool.lockout_tag");
            yield return null;
            string before = Sim.State.Get("tool.lockout_tag.part", "lock");
            yield return Click();
            Assert.AreEqual(1, sent);
            Assert.AreNotEqual(before, Sim.State.Get("tool.lockout_tag.part", "lock"), "鎖和牌切換");

            Sim.State.Set(SimIds.ActiveTool, "tool.megger");
            yield return null;
            yield return Click();
            Assert.AreEqual(2, sent); Assert.AreEqual("tool.megger", target);

            Sim.State.Set(SimIds.ActiveTool, "tool.caliper");
            yield return null;
            yield return Click();
            Assert.AreEqual(2, sent, "其他工具沒作用");
            XrPlayer.ScriptedFrame = null;
        }

        /// <summary>第 2 點：頭在 (0, 1.63, 0) 看 +Z，任務卡在頭前 1.0 m、眼下 0.15 m，頭轉動不跟著走，「面板叫回眼前」叫得回來。</summary>
        [UnityTest]
        public IEnumerator TaskCard_FrontOfHead_Fixed_Recalled()
        {
            yield return Boot(new Vector3(0f, 1.63f, 0f));
            tracked = true;
            yield return WaitReady();
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(Sim.Registry.TryGetTransform("xr.panel.dialog", out var card));
            Assert.AreEqual(1f, Flat(card.position, Rig.Head.position), 0.02f, "頭前 1.0 m");
            Assert.AreEqual(Rig.Head.position.y - 0.15f, card.position.y, 0.02f, "眼下 0.15 m");
            var lift = Sim.Registry.TryGetTransform("xr.mount.lift_panel", out var mount) ? mount.position : GameObject.Find("LiftPanelMount").transform.position;
            var head = Rig.Head.position;
            Assert.Less(Vector3.Dot(Vector3.ProjectOnPlane(card.position - head, Vector3.up), Vector3.ProjectOnPlane(lift - head, Vector3.up)), 0f, "在升降盒對面");
            Assert.Greater(Vector3.Dot(card.forward, Vector3.ProjectOnPlane(card.position - head, Vector3.up).normalized), 0.9f, "正面朝學員");
            var at = card.position;
            Rig.Head.Rotate(0f, 90f, 0f, Space.World);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.Less((card.position - at).magnitude, 1e-3f, "不跟著視線走");
            Object.FindObjectOfType<XrPanels>().Recall();
            Assert.Greater(Vector3.Dot((card.position - Rig.Head.position).normalized, Rig.Head.forward), 0.5f, "叫得回來");
        }

        [UnityTest]
        public IEnumerator HeadOffCentre_StillLandsOnStation()
        {
            yield return Boot(new Vector3(0.8f, 1.6f, 0.5f));
            tracked = true;
            yield return WaitReady();
            Assert.Less(Flat(Rig.Head.position, Point("tp.platform.lift")), 0.05f, "扣掉頭離房間中心的偏移");
        }

        [UnityTest]
        public IEnumerator StartStationFromData()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f), "tp.platform.rack");
            tracked = true;
            yield return WaitReady();
            Assert.AreEqual("tp.platform.rack", Sim.State.Get("xr.point"));
            Assert.Less(Flat(Rig.Head.position, Point("tp.platform.rack")), 0.05f);
        }

        [UnityTest]
        public IEnumerator Timeout_UsesFallbackHeight_WithWarning_ThenReplacesWhenTrackingArrives()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("頭盔追蹤沒有生效"));
            yield return Boot(Vector3.zero);
            yield return WaitReady();   // 等滿 startupTimeoutSec
            for (int i = 0; i < 5; i++) yield return null;   // 開場教學的對話框打開之後才擺回預設眼高
            var floor = Point("tp.platform.lift").y;
            Assert.AreEqual(floor + PostureDef.Load().standEye - Layout.taskCardBelowEye, Dialog().position.y, 0.05f, "用預設眼高擺");

            Rig.Head.localPosition = new Vector3(0.5f, 1.7f, 0f); headY = 1.7f; tracked = true;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.Less(Flat(Rig.Head.position, Point("tp.platform.lift")), 0.05f, "追蹤回來後重新擺人");
            Assert.AreEqual(Rig.Head.position.y - Layout.taskCardBelowEye, Dialog().position.y, 0.05f, "追蹤回來後重新擺任務卡");
        }
    }
}
