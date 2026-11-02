using System.Collections.Generic;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Tools;
using PantographSim.UI;
using PantographSim.XR;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>R1 T2-a：工具數值改用手調（套筒座停留、撥桿、± 鈕、卡尺吸附與自動填入、ZERO、A 重設）。</summary>
    public class ToolWidgetTests
    {
        const float Dt = 0.02f;

        [Test]
        public void SocketSeat_Dwell015_Switches_Dwell010_DoesNot()
        {
            var t = new DwellTimer(ToolWidgetLogic.SocketDwellSeconds);
            string got = null;
            for (float x = 0; x < 0.10f; x += Dt) got = t.Update("13", Dt) ?? got;
            Assert.IsNull(got, "停留 0.1 s 不換");
            t.Update("", Dt);
            for (float x = 0; x < 0.16f + Dt; x += Dt) got = t.Update("13", Dt) ?? got;
            Assert.AreEqual("13", got, "停留 0.15 s 換成 13");
            Assert.IsNull(t.Update("13", Dt), "同一顆只換一次");
        }

        [Test]
        public void SocketSeat_TouchRadius_PicksNearestSeat()
        {
            var seats = new[] { new KeyValuePair<string, Vector3>("10", new Vector3(0, 0, 0)), new KeyValuePair<string, Vector3>("13", new Vector3(0, 0, 0.06f)) };
            Assert.AreEqual("13", ToolWidgetLogic.Nearest(new Vector3(0, 0, 0.05f), seats, ToolWidgetLogic.SocketTouchRadius));
            Assert.AreEqual("", ToolWidgetLogic.Nearest(new Vector3(0, 0.2f, 0), seats, ToolWidgetLogic.SocketTouchRadius), "沒碰到就不換");
        }

        [Test]
        public void Ratchet_PokeTwice_BackToOriginal()
        {
            string d = "loosen";
            d = ToolWidgetLogic.ToggleDir(d); Assert.AreEqual("lock", d);
            d = ToolWidgetLogic.ToggleDir(d); Assert.AreEqual("loosen", d);
        }

        [Test]
        public void Torque_PressOnce_Plus05_Hold14s_AboutPlus25_StopsAt50()
        {
            var h = new HoldRepeater();
            float setting = 20f;
            setting = TorqueLogic.Adjust(setting, h.Update(true, Dt) * TorqueLogic.Step);
            h.Update(false, Dt);
            Assert.AreEqual(20.5f, setting, 1e-4f, "按一下 +0.5");

            setting = 20f;
            for (float x = 0; x < 1.4f; x += Dt) setting = TorqueLogic.Adjust(setting, h.Update(true, Dt) * TorqueLogic.Step);
            h.Update(false, Dt);
            Assert.AreEqual(22.5f, setting, 0.5f, "按住 1.4 s 約 +2.5");

            setting = 49f;
            for (float x = 0; x < 3f; x += Dt) setting = TorqueLogic.Adjust(setting, h.Update(true, Dt) * TorqueLogic.Step);
            Assert.AreEqual(TorqueLogic.Max, setting, 1e-4f, "到 50 停住");
        }

        [Test]
        public void Widgets_AllDisabled_WhenNoToolInHand()
        {
            var rig = new GameObject("Rig");
            var anchor = new GameObject("Attach_Tool").transform; anchor.SetParent(rig.transform);
            try
            {
                var w = rig.AddComponent<XrToolWidgets>();
                w.Setup(anchor, null, null);
                w.BuildWidgets();   // 執行時由 Awake 呼叫；沒拿工具＝全部停用
                Assert.Greater(anchor.childCount, 0);
                foreach (Transform c in anchor) Assert.IsFalse(c.gameObject.activeSelf, c.name + " 沒拿工具時要停用");
            }
            finally { Object.DestroyImmediate(rig); }
        }

        [Test]
        public void Caliper_SnapsWithin3cm_ReadingFormulaUnchanged_FillsPointCellEvenIfOtherSelected()
        {
            var pts = new[] { new KeyValuePair<string, Vector3>("ip.carbon_strip_L.measure_mid", new Vector3(0, 0, 0)) };
            Assert.AreEqual("ip.carbon_strip_L.measure_mid", ToolWidgetLogic.Nearest(new Vector3(0.029f, 0, 0), pts, ToolWidgetLogic.CaliperSnapRadius));
            Assert.AreEqual("", ToolWidgetLogic.Nearest(new Vector3(0.031f, 0, 0), pts, ToolWidgetLogic.CaliperSnapRadius), "3 cm 外不吸附");
            Assert.AreEqual(12.30f, CaliperLogic.Reading(12.0f, false, null), 1e-4f, "讀數公式照舊：未歸零 +0.30");

            var stable = new DwellTimer(ToolWidgetLogic.CaliperStableSeconds);
            string fire = null;
            for (float x = 0; x < 0.45f; x += Dt) fire = stable.Update("ip.carbon_strip_L.measure_mid", Dt) ?? fire;
            Assert.IsNull(fire, "還沒穩定 0.5 s");
            for (float x = 0; x < 0.1f; x += Dt) fire = stable.Update("ip.carbon_strip_L.measure_mid", Dt) ?? fire;
            Assert.IsNotNull(fire, "穩定 0.5 s");

            var bus = new EventBus(); var s = new SimState(bus);
            ServiceLocator.Clear(); ServiceLocator.Register<IEventBus>(bus); ServiceLocator.Register<ISimState>(s);
            try
            {
                var sent = new List<ActionRequested>();
                bus.Subscribe<ActionRequested>(e => { if (e.targetId == SimIds.WorkOrder) sent.Add(e); });
                s.Set("xr.fill.value", "12.31");
                s.Set("xr.fill.field", "reading.L.mid");
                s.Set(WorkOrderView.SelectedKey, "reading.R.right");   // 選的是別格
                s.Set(XrToolWidgets.AutoFillKey, "reading.L.mid");
                XrWristView.AutoFill(s);
                Assert.AreEqual(1, sent.Count);
                Assert.AreEqual("reading.L.mid", sent[0].pointId, "填進量測點對應的格子");
                Assert.AreEqual("12.31", sent[0].text);
                Assert.AreEqual("reading.L.right", s.Get(WorkOrderView.SelectedKey), "選中移到下一個沒填的格子");
                Assert.AreEqual("", s.Get(XrToolWidgets.AutoFillKey));
            }
            finally { ServiceLocator.Clear(); }
        }

        [Test]
        public void Zero_PokeAndTrigger_SendSameEvent()
        {
            var a = XrToolWidgets.ZeroRequest(); var b = XrToolWidgets.ZeroRequest();   // XrPlayer 的右扳機與 Poke_Zero 都呼叫它
            Assert.AreEqual(SimIds.ToolCaliper, a.targetId); Assert.AreEqual("zero", a.text);
            Assert.AreEqual(a.targetId + a.text + a.toolId, b.targetId + b.text + b.toolId);
        }

        [Test]
        public void A_Reset_RestoresSocketSizeDirectionAndTorqueSetting()
        {
            var s = new SimState(new EventBus());
            s.Set("tool.socket_wrench.size", "13"); s.Set("tool.socket_wrench.dir", "lock");
            s.SetFloat("tool.torque_wrench.setting", 25f);
            ToolResetLogic.Reset(s, "tool.socket_wrench");
            ToolResetLogic.Reset(s, "tool.torque_wrench");
            Assert.AreEqual("10", s.Get("tool.socket_wrench.size"));
            Assert.AreEqual("loosen", s.Get("tool.socket_wrench.dir"));
            Assert.AreEqual(20f, s.GetFloat("tool.torque_wrench.setting"));
        }
    }
}
