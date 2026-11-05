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

        /// <summary>terry 裁定：拿在手上的驗電桿和架上一樣短。</summary>
        [UnityTest]
        public IEnumerator HeldPole_SameShortLength()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f));
            tracked = true;
            yield return WaitReady();
            Sim.State.SetBool("training.running", true);
            Sim.Bus.Publish(new ToolPickRequested { targetId = "env.tool_rack.slot_2", value = 2, time = 0f });
            yield return null; yield return null;
            var held = GameObject.Find("Held_tool.voltage_detector");
            Assert.NotNull(held);
            var rs = held.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            Assert.Less(b.size.magnitude, 0.6f, "不再是 2.5 m：" + b.size);
            Assert.Greater(b.size.magnitude, 0.2f, "看得到：" + b.size);
        }

        /// <summary>工具列選工具袋的格子就拿得到（放回之後也拿得回來）。</summary>
        [UnityTest]
        public IEnumerator ToolBar_BagSlot_PicksTool()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f));
            tracked = true;
            yield return WaitReady();
            Sim.State.SetBool("training.running", true);
            Sim.Bus.Publish(new ActionRequested { targetId = "tool.belt", text = "hint:5", time = 0f });
            yield return null;
            Assert.AreEqual(SimIds.ToolCaliper, Sim.State.Get(SimIds.ActiveTool));
            Sim.Bus.Publish(new ToolReturnRequested { targetId = "env.tool_rack.slot_5", value = 5, time = 0f });
            yield return null;
            Assert.AreEqual("", Sim.State.Get(SimIds.ActiveTool));
            Sim.Bus.Publish(new ActionRequested { targetId = "tool.belt", text = "hint:5", time = 0f });
            yield return null;
            Assert.AreEqual(SimIds.ToolCaliper, Sim.State.Get(SimIds.ActiveTool), "放回後再拿");
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
            // 安全規則的前提：弓頭已降下、已驗電、已接地（規則照常運作）
            Sim.State.SetFloat(SimIds.PanHeadLowerDistance, 0f);
            Sim.State.SetBool(SimIds.Grounded, true);
            Sim.State.SetBool("step.STP_SI_04_VoltageTest.done", true);
            Sim.State.SetBool("step.STP_SI_05_Grounding.done", true);
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
        /// <summary>terry 2026-11-05：桌機用 XR Device Simulator 走 VR：Link 關著時等完就進 VR，頭、兩手都有裝置，追蹤沒被關掉，按鍵走 Input System。</summary>
        [UnityTest]
        public IEnumerator DeviceSimulator_EntersVr_DevicesTrack()
        {
            XrModeSwitch.SimulateRequested = false;
            XrStartupSequencer.ProbeOverride = null;
            XrModeSwitch.HeadWornOverride = () => false;   // 測試不看有沒有人戴著頭盔：loader 在線、沒有人戴
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/XR Interaction Toolkit/2.6.5/XR Device Simulator/XR Device Simulator.prefab");
            Assert.IsNotNull(prefab, "範例要匯入");
            // 批次模式沒有鍵盤滑鼠，模擬器的畫面說明要讀按鍵名稱
            var keyboard = UnityEngine.InputSystem.Keyboard.current == null ? UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>() : null;
            var mouse = UnityEngine.InputSystem.Mouse.current == null ? UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>() : null;
            var sim = Object.Instantiate(prefab); Object.DontDestroyOnLoad(sim);
            try
            {
                SceneManager.LoadScene(Scene);
                for (int i = 0; i < 10; i++) yield return null;   // 等新場景的黑板，上一個測試的 xr.ready 不算
                for (float t = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t < 12f && !(Sim.State?.GetBool(XrStartupSequencer.ReadyKey) ?? false);) yield return null;
                var mgr = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager;
                Debug.Log($"[SimCheck] status {XrModeSwitch.Status} loader {(mgr != null && mgr.activeLoader != null ? mgr.activeLoader.name : "none")} simExists {sim != null}");
                Assert.IsTrue(XrModeSwitch.DeviceSimulated, XrModeSwitch.Status);
                Assert.IsTrue(InputMode.XrActive, "進 VR");
                Assert.IsFalse(XrModeSwitch.Simulated, "不是截圖模擬：追蹤要開著");
                var devs = UnityEngine.InputSystem.InputSystem.devices;
                Assert.IsTrue(devs.Any(d => d is UnityEngine.InputSystem.XR.XRHMD), "頭");
                Assert.AreEqual(2, devs.Count(d => d is UnityEngine.InputSystem.XR.XRController), "兩手");
                foreach (var d in Rig.GetComponentsInChildren<UnityEngine.InputSystem.XR.TrackedPoseDriver>(true)) Assert.IsTrue(d.enabled, d.name);
                for (int i = 0; i < 10; i++) yield return null;
                var origin = Rig.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() ?? Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
                Debug.Log($"[SimCheck] eye {Rig.Head.position.y - origin.transform.position.y:0.000} head {Rig.Head.position} left {Rig.LeftHand.position} right {Rig.RightHand.position} mode {origin.CurrentTrackingOriginMode}");
                Assert.Greater(Rig.Head.position.y - Rig.RightHand.position.y, 0.3f, "手放低，不擋面板");
                Assert.Greater(Rig.Head.position.y - Rig.LeftHand.position.y, 0.3f, "手放低，不擋面板");
                Assert.AreEqual(PostureDef.Load().standEye, Rig.Head.position.y - origin.transform.position.y, 0.02f, "175 cm 站姿眼高，沒有疊加桌面的 1.2 m");
                Assert.AreEqual(Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor, origin.RequestedTrackingOriginMode, "Floor");
                var mgrNow = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager;
                Assert.IsTrue(mgrNow == null || mgrNow.activeLoader == null, "OpenXR 要停掉，畫面才不是 Left Eye");

                // 不用右鍵：開場就是身體模式，方向鍵（和滑鼠移動）直接轉頭
                var kb = UnityEngine.InputSystem.Keyboard.current;
                float yaw0 = Rig.Head.eulerAngles.y;
                while (Time.time < 1.5f) yield return null;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.RightArrow));
                for (int i = 0; i < 30; i++) yield return null;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                for (int i = 0; i < 3; i++) yield return null;
                float turned = Mathf.Abs(Mathf.DeltaAngle(yaw0, Rig.Head.eulerAngles.y));
                Debug.Log($"[SimCheck] 方向鍵轉頭 {turned:0.0} 度");
                Assert.Greater(turned, 5f, "方向鍵要能轉頭");

                // 模擬器的扳機只送給選中的手：身體模式按左鍵沒有扳機，按 Y 選右手才有
                var right = devs.OfType<UnityEngine.InputSystem.XR.XRController>().First(d => d.usages.Contains(UnityEngine.InputSystem.CommonUsages.RightHand));
                var trigger = (UnityEngine.InputSystem.Controls.ButtonControl)right["triggerPressed"];
                var pressed = new UnityEngine.InputSystem.LowLevel.MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
                var ms = UnityEngine.InputSystem.Mouse.current;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(ms, pressed);
                for (int i = 0; i < 5; i++) yield return null;
                Assert.IsFalse(trigger.isPressed, "身體模式：左鍵不到手把");
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(ms, new UnityEngine.InputSystem.LowLevel.MouseState());
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Y));
                for (int i = 0; i < 3; i++) yield return null;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(ms, pressed);
                for (int i = 0; i < 5; i++) yield return null;
                Assert.IsTrue(trigger.isPressed, "按 Y 選右手後左鍵＝扳機，按住一直有");
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(ms, new UnityEngine.InputSystem.LowLevel.MouseState());
                for (int i = 0; i < 3; i++) yield return null;
                Assert.IsFalse(trigger.isPressed, "放開就沒有");
                // 選完手馬上按 G：下一幀就要到手把
                var grip = (UnityEngine.InputSystem.Controls.ButtonControl)right["gripPressed"];
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.G));
                for (int i = 0; i < 3; i++) yield return null;
                Debug.Log("[SimCheck] 選手後 3 幀 G " + grip.isPressed);
                Assert.IsTrue(grip.isPressed, "已選右手時 G 直接送出");
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                for (int i = 0; i < 3; i++) yield return null;

                // N＝B（帶我到目標）：一樣要先選右手
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.N));
                for (int i = 0; i < 4; i++) yield return null;
                Assert.IsTrue(((UnityEngine.InputSystem.Controls.ButtonControl)right["secondaryButton"]).isPressed, "選右手後 N 就是 B 鍵");
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                for (int i = 0; i < 3; i++) yield return null;

                // 傳送：左手搖桿往前。按 T 選左手，W A S D 開場就是搖桿（按 1 反而會關掉），按住 W 瞄準、放開傳送
                foreach (var key in new[] { UnityEngine.InputSystem.Key.T })
                {
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(key));
                    for (int i = 0; i < 3; i++) yield return null;
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                    for (int i = 0; i < 3; i++) yield return null;
                }
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
                for (int i = 0; i < 6; i++) yield return null;
                Assert.IsTrue(Object.FindObjectOfType<XrTeleport>().Aiming, "按住 W 要開始瞄準傳送");
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                for (int i = 0; i < 6; i++) yield return null;
                Assert.IsFalse(Object.FindObjectOfType<XrTeleport>().Aiming, "放開結束瞄準");
                if (Sim.Registry.TryGetTransform(XrPanelLayoutMount, out var mount)) Debug.Log($"[SimCheck] 升降面板 {mount.position} 離頭 {Vector3.Distance(mount.position, Rig.Head.position):0.00} m");
            }
            finally
            {
                XrModeSwitch.HeadWornOverride = null;
                Object.Destroy(sim);
                if (keyboard != null) UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
                if (mouse != null) UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
            }
#else
            yield break;
#endif
        }

        const string XrPanelLayoutMount = "xr.mount.lift_panel";

        /// <summary>第 18 輪：VR 記錯時，錯誤視窗在頭前方 ±30° 內，就算工具教學卡還開在別處。</summary>
        [UnityTest]
        public IEnumerator ErrorDialog_OpensInFrontOfHead()
        {
            yield return Boot(new Vector3(0f, 1.63f, 0f));
            tracked = true; headY = 1.63f;
            yield return WaitReady();
            Sim.State.SetBool("training.running", true);
            Sim.State.SetBool("xr.tutorial.active", false);
            XrPlayer.ScriptedFrame = new XrFrame();
            Sim.State.Set("ui.tutorial.tool", SimIds.ToolGroundingRod);
            for (int i = 0; i < 5; i++) yield return null;
            Rig.Head.Rotate(0f, 110f, 0f, Space.World);
            for (int i = 0; i < 3; i++) yield return null;
            Sim.Bus.Publish(new ErrorDetected { errorId = "ERR_Ground_Order", severity = Severity.Critical, blocks = true, what = "x", why = "y", fix = "z", time = 1f });
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(Sim.State.GetBool("ui.error.active"));
            var to = Vector3.ProjectOnPlane(Dialog().position - Rig.Head.position, Vector3.up);
            Assert.Less(Vector3.Angle(to, Vector3.ProjectOnPlane(Rig.Head.forward, Vector3.up)), 30f, "錯誤視窗要在眼前");
            Sim.State.Set("ui.tutorial.tool", "");
        }

        /// <summary>第 17 輪：從人走得到的位置（走道最靠弓處、接地站點），手在 1.23 m，射線到掛點不被擋；線條不停在走動阻擋體上。</summary>
        [UnityTest]
        public IEnumerator HoldingRod_FromWalkway_ReachesGroundClamp()
        {
            yield return Boot(new Vector3(0f, 1.63f, 0f));
            tracked = true; headY = 1.63f;
            yield return WaitReady();
            Sim.State.SetBool("training.running", true);
            Sim.State.SetBool("xr.tutorial.active", false);
            XrPlayer.ScriptedFrame = new XrFrame();
            yield return null;
            Sim.Bus.Publish(new ToolPickRequested { targetId = "env.tool_rack.slot_3", value = 3 });
            for (int i = 0; i < 20; i++) yield return null;
            Sim.State.Set("ui.tutorial.tool", "");
            Assert.AreEqual(0, Object.FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.XRRayInteractor>(true).raycastMask.value & LayerMask.GetMask(SceneLayers.LocomotionBlocker), "線條不停在走動阻擋體");
            Assert.IsTrue(Sim.Registry.TryGetPosition(SimIds.GroundClamp, out var clamp));
            var ground = Object.FindObjectsOfType<XrTeleportPoint>(true).First(p => p.PointId == "tp.platform.ground").transform.position;
            foreach (var hand in new[] { new Vector3(-1.565f, 1.23f, -0.565f), ground + new Vector3(0.3f, 1.23f, -0.2f) })
            {
                Rig.LeftHand.position = hand + Vector3.up * 3f;
                Rig.RightHand.SetPositionAndRotation(hand, Quaternion.LookRotation(clamp - hand));
                for (int i = 0; i < 5; i++) yield return null;
                string hovered = Sim.State.Get("ui.hover_id");
                Assert.AreEqual(SimIds.GroundClamp, SimIds.RayTarget(Sim.State, SimIds.ToolGroundingRod, hovered, clamp), "從 " + hand + " 打到 " + hovered);
            }
        }

        /// <summary>第 16 輪：手持接地棒。組長紀錄裡的水平射線從弓頭上方飛過（弓頭比手低 0.6 m）；往下指弓頭任一處，點擊算掛點。</summary>
        [UnityTest]
        public IEnumerator HoldingRod_RayAtPanHead_ClicksGroundClamp()
        {
            yield return Boot(new Vector3(0f, 1.63f, 0f));
            tracked = true; headY = 1.63f;
            yield return WaitReady();
            Sim.State.SetBool("training.running", true);
            Sim.State.SetBool("xr.tutorial.active", false);
            XrPlayer.ScriptedFrame = new XrFrame();
            yield return null;
            Sim.Bus.Publish(new ToolPickRequested { targetId = "env.tool_rack.slot_3", value = 3 });
            yield return null; yield return null;
            Sim.State.Set("ui.tutorial.tool", "");
            Assert.AreEqual(SimIds.ToolGroundingRod, Sim.State.Get(SimIds.ActiveTool));
            var hand = new Vector3(-1.55f, 1.23f, -0.51f);   // 紀錄 20261105_184055 t=600 的右手位置
            Rig.LeftHand.position = hand + Vector3.up * 3f;
            Rig.RightHand.SetPositionAndRotation(hand, Quaternion.LookRotation(new Vector3(0.08f, -0.01f, 1f)));
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual("", Sim.State.Get("ui.hover_id"), "水平的射線本來就打不到弓頭");
            Assert.IsFalse(PointerState.OverUi, "不是介面擋住");
            Assert.IsTrue(Sim.Registry.TryGetPosition(SimIds.PanHead, out var pan));
            Rig.RightHand.SetPositionAndRotation(hand, Quaternion.LookRotation(pan - hand));
            for (int i = 0; i < 5; i++) yield return null;
            string hovered = Sim.State.Get("ui.hover_id");
            Assert.AreNotEqual("", hovered, "往下指要 hover 到弓頭");
            Assert.AreEqual(SimIds.GroundClamp, SimIds.RayTarget(Sim.State, SimIds.ToolGroundingRod, hovered, pan), "弓頭任一處都算掛點");
        }

        /// <summary>第 15 輪：站在桿架前，右手射線指著 3 號掛位那一帶按握把，要送出 ToolPickRequested（打到桿子或後面的掛架都算）。</summary>
        [UnityTest]
        public IEnumerator PoleRack_RayGrip_PicksGroundingRod()
        {
            yield return Boot(new Vector3(0f, 1.63f, 0f));
            tracked = true; headY = 1.63f;
            yield return WaitReady();
            Sim.State.SetBool("training.running", true);
            Sim.State.SetBool("xr.tutorial.active", false);
            XrPlayer.ScriptedFrame = new XrFrame();
            yield return null;
            Assert.IsTrue(Sim.Registry.TryGetPosition("env.tool_rack.slot_3", out var slot));
            int picked = 0;
            Sim.Bus.Subscribe<ToolPickRequested>(e => picked = (int)e.value);
            var from = slot + Vector3.right * 1.2f + Vector3.up * 0.1f;
            Rig.RightHand.SetPositionAndRotation(from, Quaternion.LookRotation(slot - from));
            Rig.LeftHand.position = from + Vector3.up * 3f;
            for (int i = 0; i < 5; i++) yield return null;
            XrPlayer.ScriptedFrame = new XrFrame { rightGrip = true };
            yield return null; yield return null;
            XrPlayer.ScriptedFrame = new XrFrame();
            yield return null;
            Assert.AreEqual(3, picked, "握把要拿到 3 號");
        }

        /// <summary>第 14 輪：射線按住 ▼ 持續 1 秒要一直是 Down，放開 Stop（XRUIInputModule 送的就是這兩個事件）。</summary>
        [UnityTest]
        public IEnumerator LiftButton_HoldOneSecond_Down_ReleaseStops()
        {
            yield return Boot(new Vector3(0f, 1.63f, 0f));
            tracked = true; headY = 1.63f;
            yield return WaitReady();
            Sim.State.Set("procedure.current", "STP_SI_01_ConfirmLowered");   // 升降面板在降弓那一步才顯示
            Sim.State.SetBool("training.running", true);
            for (int i = 0; i < 5; i++) yield return null;
            var button = Object.FindObjectsOfType<UnityEngine.UI.Button>(true).First(b => b.GetComponentInChildren<UnityEngine.UI.Text>(true)?.text.Contains("下降（按住）") ?? false);
            var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            Assert.IsTrue(button.gameObject.activeInHierarchy, "升降面板要顯示");
            UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
            Assert.AreEqual("Down", Sim.State.Get(SimIds.LiftCmd), "按下就 Down");
            yield return new WaitForSecondsRealtime(1f);
            Assert.AreEqual("Down", Sim.State.Get(SimIds.LiftCmd), "按住 1 秒要持續 Down");
            UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
            yield return null;
            Assert.AreEqual("Stop", Sim.State.Get(SimIds.LiftCmd), "放開就停");
        }

        /// <summary>terry 2026-11-05：選好模式後開場面板都要消失，包含任務卡背後的深色底板和把手；之後跳出來的對話框還是有底板。</summary>
        [UnityTest]
        public IEnumerator StartPanels_GoneAfterModeChosen()
        {
            yield return Boot(new Vector3(0f, 1.63f, 0f));
            tracked = true;
            yield return WaitReady();
            Sim.State.SetBool("xr.tutorial.active", false);
            Sim.State.SetBool("xr.tutorial.done", true);
            for (int i = 0; i < 5; i++) yield return null;
            var plate = Dialog().Find("BackPlate");
            var handle = GameObject.Find("UI_PanelHandle_dialog");
            Assert.IsNotNull(plate); Assert.IsNotNull(handle);
            Assert.IsTrue(plate.gameObject.activeSelf, "任務卡開著要有底板");
            Assert.IsTrue(Dialog().Find("TaskCard_Dim").gameObject.activeSelf, "任務卡開著");
            Sim.Bus.Publish(new StepStarted { title = "降弓並確認", index = 1, total = 25, side = "" });
            Sim.State.SetBool("training.running", true);
            yield return new WaitForSecondsRealtime(3.2f);   // 短暫提示本來就停 3 秒
            Assert.IsTrue(Sim.Registry.TryGetTransform("xr.panel.board", out var board));
            Assert.IsTrue(board.Find("StepPanel").gameObject.activeSelf, "提示板要有內容");
            Assert.IsTrue(PanelLogic.InView(Rig.Head.position, Rig.Head.forward, board.position, 60f, 2.5f), "選好模式後提示板在眼前");
            foreach (Transform c in Dialog())
                if (c.name != "BackPlate") Assert.IsFalse(c.gameObject.activeSelf, c.name + "：開始後不能留著");
            Assert.IsFalse(plate.gameObject.activeSelf, "底板收掉");
            Assert.IsFalse(handle.activeSelf, "把手收掉");

            Sim.State.SetBool("xr.help_open", true);   // 之後要看再叫：Y
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(plate.gameObject.activeSelf, "叫出說明時底板回來");
            Sim.State.SetBool("xr.help_open", false);
        }

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
