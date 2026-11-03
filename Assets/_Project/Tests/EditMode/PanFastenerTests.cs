using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Parts.Common;
using UnityEditor;

namespace PantographSim.Tests
{
    /// <summary>
    /// R1.5b b4（5h）：集電弓螺栓（底座 24 mm、高壓端子 19 mm，讀 SocketCatalog；開場已上扭力也能鬆）收進工具袋零件袋、裝回時從零件袋拿；
    /// 碳滑板螺栓照舊 13 mm、進螺栓盤、已上扭力不能鬆。控制線接頭防塵蓋、降弓固定帶拉線確認（DetachableLogic）。
    /// </summary>
    public class PanFastenerTests
    {
        static SocketCatalog Catalog => AssetDatabase.LoadAssetAtPath<SocketCatalog>("Assets/_Project/Tools/Data/SocketCatalog.asset");

        [Test]
        public void PanBolts_SocketFromCatalog_TorquedLoosens_GoesToPouch()
        {
            var bus = new EventBus(); var s = new SimState(bus);
            bus.Subscribe<ActionPerformed>(e => { if (e.targetId == FastenerLogic.BoltPouch) PantographSim.Worksite.BoltPouch.Apply(s, e.text); });
            const string b = "part.pan_base.bolt_1";
            string size = Catalog.SizeFor(b).ToString();
            Assert.AreEqual("24", size);
            Assert.AreEqual("19", Catalog.SizeFor("part.pan_assy.hv_bolt_1").ToString());
            s.Set(b + ".state", FastenerLogic.Torqued);
            Assert.IsFalse(FastenerLogic.Apply(s, bus, b, "rotate:13:loosen", 720, size), "套筒不對：轉不動");
            Assert.IsTrue(s.GetBool(b + ".wrong_socket"));
            FastenerLogic.Apply(s, bus, b, $"rotate:{size}:loosen", 720, size);
            Assert.AreEqual(FastenerLogic.Out, s.Get(b + ".state"), "開場已上扭力的底座螺栓：照鎖緊的鬆");
            Assert.IsTrue(FastenerLogic.Apply(s, bus, b, "take", 0, size));
            Assert.AreEqual(FastenerLogic.InTray, s.Get(b + ".state"));
            Assert.AreEqual(1f, s.GetFloat(FastenerLogic.BoltPouchCount), "收進工具袋的零件袋");
            Assert.IsTrue(FastenerLogic.Apply(s, bus, b, "place", 0, size), "裝回：從零件袋拿");
            Assert.AreEqual(0f, s.GetFloat(FastenerLogic.BoltPouchCount));
        }

        [Test]
        public void StripBolt_Torqued_StillCannotLoosen()
        {
            var s = new SimState(new EventBus());
            const string b = "ip.carbon_strip_L.bolt_1";
            s.Set(b + ".state", FastenerLogic.Torqued);
            Assert.IsFalse(FastenerLogic.Apply(s, null, b, "rotate:13:loosen", 720), "碳滑板螺栓上完扭力照舊不能鬆（R1 不變）");
        }

        [Test]
        public void DustCap_OnAfterRemove_OffThenReinstall_PullTestKeepsState()
        {
            var s = new SimState(new EventBus());
            const string c = "ip.pan_assy.ctrl_connector";
            Assert.IsTrue(DetachableLogic.CapClick(s, c), "RM_02 b：拔下後點＝蓋上");
            Assert.AreEqual(DetachableLogic.On, s.Get(DetachableLogic.CapKey(c)));
            Assert.IsTrue(DetachableLogic.CapClick(s, c), "IN_10 a：蓋著點＝取下");
            Assert.AreEqual(DetachableLogic.Off, s.Get(DetachableLogic.CapKey(c)));
            Assert.IsFalse(DetachableLogic.CapClick(s, c), "取下後再點＝插回（不再蓋）");
            DetachableLogic.ResetCap(s, c);
            Assert.IsTrue(DetachableLogic.CapClick(s, c), "插回後下次拔下又要先蓋");

            s.Set("ip.pan_assy.hold_down.state", "Installed");
            DetachableLogic.PullTest(s, "ip.pan_assy.hold_down");
            Assert.IsTrue(s.GetBool("ip.pan_assy.hold_down.pull_tested"));
            Assert.AreEqual("Installed", s.Get("ip.pan_assy.hold_down.state"), "拉一下：狀態不變");
        }
    }
}
