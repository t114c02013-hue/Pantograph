using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Tools;
using PantographSim.Worksite;

namespace PantographSim.Tests
{
    /// <summary>terry 175B（2026-11-15）：日常課程外接電源箱——燈、開關上鎖、插頭抓放（ShorePlugLogic）、上鎖掛牌組對照表。</summary>
    public class ShorePowerTests
    {
        static SimState New() { var s = new SimState(new EventBus()); s.Set(CabControlLogic.ShoreSwitch, CabControlLogic.On); s.Set(ShorePlugLogic.Plug, ShorePlugLogic.Car); return s; }

        [Test]
        public void Lamps_OnRedOffGreen()   // P1
        {
            var s = New();
            Assert.AreEqual((true, false), ShoreBox.Lamps(s));
            Assert.IsNull(CabControlLogic.ToggleShore(s));
            Assert.AreEqual((false, true), ShoreBox.Lamps(s));
        }

        [Test]
        public void Switch_LockedCannotTurnOn_LockNeedsOff()   // P2
        {
            var s = New();
            Assert.IsNotNull(CabControlLogic.CanLockShore(s), "ON 不能上鎖");
            CabControlLogic.ToggleShore(s);
            Assert.IsNull(CabControlLogic.CanLockShore(s));
            s.Set("lockout.shore_switch", "locked");
            Assert.IsNotNull(CabControlLogic.ToggleShore(s));
            Assert.AreEqual(CabControlLogic.Off, s.Get(CabControlLogic.ShoreSwitch), "上鎖以後切不回 ON");
        }

        [Test]
        public void Plug_ReleaseNearHolderOnly()   // P4
        {
            float snap = ToolRulesDef.Load().shorePlugSnapDist;
            Assert.AreEqual(0.15f, snap, 1e-5f);
            var s = New(); CabControlLogic.ToggleShore(s);
            Assert.IsNull(ShorePlugLogic.Grab(s));
            Assert.AreEqual(ShorePlugLogic.HolderText, ShorePlugLogic.Release(s, 0.16f <= snap));
            Assert.AreEqual(ShorePlugLogic.Car, s.Get(ShorePlugLogic.Plug), "0.16 m：回車上插座");
            Assert.IsFalse(s.GetBool(ShorePlugLogic.UnplugLive), "不記錯");
            Assert.IsNull(ShorePlugLogic.Grab(s));
            Assert.IsNull(ShorePlugLogic.Release(s, 0.14f <= snap));
            Assert.AreEqual(ShorePlugLogic.Holder, s.Get(ShorePlugLogic.Plug), "0.14 m：掛上插頭座");
        }

        [Test]
        public void Plug_InHolderCannotGrab()   // P5
        {
            var s = New(); CabControlLogic.ToggleShore(s); s.Set(ShorePlugLogic.Plug, ShorePlugLogic.Holder);
            Assert.IsNull(ShorePlugLogic.Grab(s));
            Assert.AreEqual("", s.Get(ShorePlugLogic.HandItem));
        }

        [Test]
        public void Plug_DesktopClick_BlockedWhenOn_ToHolderWhenOff()   // P6
        {
            var s = New();
            Assert.AreEqual(ShorePlugLogic.LiveText, ShorePlugLogic.Click(s));
            Assert.AreEqual(ShorePlugLogic.Car, s.Get(ShorePlugLogic.Plug));
            CabControlLogic.ToggleShore(s);
            Assert.IsNull(ShorePlugLogic.Click(s));
            Assert.AreEqual(ShorePlugLogic.Holder, s.Get(ShorePlugLogic.Plug));
            Assert.AreEqual("", s.Get(ShorePlugLogic.HandItem));
        }

        [Test]
        public void LockNames_ExistingKeysUnchanged_ShoreAdded()
        {
            Assert.AreEqual("breaker", LockoutTagTool.LockNames["ip.cab.breaker"]);
            Assert.AreEqual("raise_switch", LockoutTagTool.LockNames["ip.cab.raise_switch"]);
            Assert.AreEqual("shore_switch", LockoutTagTool.LockNames[CabControlLogic.ShoreSwitchId]);
        }
    }
}
