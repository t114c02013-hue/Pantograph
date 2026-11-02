using System.Collections.Generic;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Parts.Common;

namespace PantographSim.Tests
{
    public class PartsTests
    {
        EventBus bus; SimState s; List<ActionPerformed> trayEvents;
        const string B1 = "ip.carbon_strip_L.bolt_1";
        const string Strip = "part.carbon_strip_L";

        [SetUp]
        public void SetUp()
        {
            bus = new EventBus(); s = new SimState(bus); trayEvents = new List<ActionPerformed>();
            // R1：拆下的螺栓進該側螺栓盤（BoltTray.OnBound 會寫計數鍵），不再進零件盒
            bus.Subscribe<ActionPerformed>(e => { if (e.targetId == SimIds.BoltTray("L")) trayEvents.Add(e); });
            s.SetFloat(SimIds.BoltTray("L") + ".bolts", 0);
            for (int n = 1; n <= 4; n++) FastenerLogic.Initialize(s, "ip.carbon_strip_L.bolt_" + n);
            StripLogic.Initialize(s, Strip);
        }

        void Rotate(string bolt, float deg, string dir = "loosen", string size = "13") => FastenerLogic.Apply(s, bus, bolt, $"rotate:{size}:{dir}", deg);

        [Test]
        public void Fastener_WrongSocket_DoesNothing()
        {
            Rotate(B1, 180, "loosen", "17");
            Assert.AreEqual("Tight", s.Get(B1 + ".state"));
            Assert.IsTrue(s.GetBool(B1 + ".wrong_socket"));
            Assert.IsFalse(s.Has(B1 + ".angle_deg"));
        }

        [Test]
        public void Fastener_HalfTurnLoosens_ThenOut_Tray_Place_Snug_Torque_Mark()
        {
            Rotate(B1, 90); Rotate(B1, 90);
            Assert.AreEqual("Loosened", s.Get(B1 + ".state"));
            Assert.AreEqual("1", s.Get("ip.carbon_strip_L.loosen_order"));
            Rotate(B1, 600);
            Assert.AreEqual("Out", s.Get(B1 + ".state"));
            FastenerLogic.Apply(s, bus, B1, "take", 0);
            Assert.AreEqual("InTray", s.Get(B1 + ".state"));
            s.SetFloat("tray.bolts", 1);
            FastenerLogic.Apply(s, bus, B1, "place", 0);
            Assert.AreEqual("Placed", s.Get(B1 + ".state"));
            Rotate(B1, 360, "lock");
            Assert.AreEqual("Snug", s.Get(B1 + ".state"));
            FastenerLogic.Apply(s, bus, B1, "torque:25", 25.3f);
            Assert.AreEqual("Torqued", s.Get(B1 + ".state"));
            Assert.AreEqual(25.3f, s.GetFloat(B1 + ".torque_Nm"), 1e-3);
            Assert.AreEqual(25f, s.GetFloat(B1 + ".torque_setting"), 1e-3);
            FastenerLogic.Apply(s, bus, B1, "mark", 0);
            Assert.IsTrue(s.GetBool(B1 + ".marked"));
            Assert.AreEqual(2, trayEvents.Count);
        }

        [Test]
        public void Fastener_NoBoltTrayInScene_LogsError_AndNeverFallsBackToPartsTray()
        {
            var s2 = new SimState(bus);
            var toParts = new List<ActionPerformed>();
            bus.Subscribe<ActionPerformed>(e => { if (e.targetId == SimIds.PartsTray) toParts.Add(e); });
            FastenerLogic.Initialize(s2, B1);
            s2.Set(B1 + ".state", FastenerLogic.Out);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("沒有螺栓盤"));
            FastenerLogic.Apply(s2, bus, B1, "take", 0);
            Assert.IsEmpty(toParts, "螺栓不能進零件盒");
        }

        [Test]
        public void Fastener_OvertightFirst_IsFlagged()
        {
            for (int n = 1; n <= 4; n++) s.Set("ip.carbon_strip_L.bolt_" + n + ".state", "Placed");
            Rotate(B1, 600, "lock");
            Assert.IsTrue(s.GetBool(B1 + ".overtight_first"));
        }

        [Test]
        public void Fastener_LockDirectionOnTightBolt_Ignored()
        {
            Rotate(B1, 180, "lock");
            Assert.AreEqual("Tight", s.Get(B1 + ".state"));
        }

        [Test]
        public void Strip_CannotRemoveWithBolts_PryIsRejected_VerticalWorks()
        {
            Assert.IsFalse(StripLogic.Remove(s, bus, Strip, "vertical"));
            Assert.AreEqual("bolts", s.Get(Strip + ".remove_blocked"));
            for (int n = 1; n <= 4; n++) s.Set("ip.carbon_strip_L.bolt_" + n + ".state", "InTray");
            Assert.IsFalse(StripLogic.Remove(s, bus, Strip, "pry"));
            Assert.AreEqual("pry", s.Get(Strip + ".removal"));
            Assert.AreEqual("OldMounted", s.Get(Strip + ".state"));
            Assert.IsTrue(StripLogic.Remove(s, bus, Strip, "vertical"));
            Assert.AreEqual("Removed", s.Get(Strip + ".state"), "垂直取出後拿在手上");
            Assert.AreEqual(Strip, s.Get("hand.item"));
            Assert.IsTrue(StripLogic.ToTray(s, bus, Strip));
            Assert.AreEqual("InTray", s.Get(Strip + ".state"));
            Assert.AreEqual("", s.Get("hand.item"));
        }

        [Test]
        public void Strip_InstallSnap_RequiresOrientationAndAlignment()
        {
            s.Set(Strip + ".state", "InTray");
            Assert.IsTrue(StripLogic.Carry(s, Strip));
            Assert.AreEqual("in", s.Get(Strip + ".new_orientation"));
            Assert.IsFalse(StripLogic.Install(s, Strip, 0f));
            Assert.AreEqual("WrongOrientation", s.Get(Strip + ".install_result"));
            StripLogic.Rotate(s, Strip);
            Assert.IsFalse(StripLogic.Install(s, Strip, 0.08f));
            Assert.AreEqual("NotAligned", s.Get(Strip + ".install_result"));
            Assert.IsTrue(StripLogic.Install(s, Strip, 0.004f));
            Assert.AreEqual("NewMounted", s.Get(Strip + ".state"));
            Assert.AreEqual(12f, s.GetFloat(Strip + ".true.mid"), 1e-3);
        }

        [Test]
        public void DirtMask_CleanAcrossRaisesRatio()
        {
            const string id = "ip.carbon_strip_L.bracket";
            for (float u = 0; u <= 1.001f; u += 0.05f) { DirtMask.Clean(s, id, u, 0.2f); DirtMask.Clean(s, id, u, 0.8f); }
            Assert.GreaterOrEqual(s.GetFloat(id + ".clean_ratio"), 0.95f);
        }
    }
}
