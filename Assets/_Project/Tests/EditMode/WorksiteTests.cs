using System.Collections.Generic;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Worksite;

namespace PantographSim.Tests
{
    public class WorksiteTests
    {
        EventBus bus; SimState s; List<RecordWritten> records;

        [SetUp]
        public void SetUp()
        {
            bus = new EventBus(); s = new SimState(bus); records = new List<RecordWritten>();
            bus.Subscribe<RecordWritten>(e => records.Add(e));
        }

        [Test]
        public void WorkOrder_ReadingWithinTolerance_NoMismatch_AndPublishes()
        {
            s.SetFloat("ip.carbon_strip_L.measure_mid.reading", 2.81f);
            Assert.IsTrue(WorkOrder.Write(s, bus, "reading.L.mid", "2.80"));
            Assert.AreEqual("2.80", s.Get("workorder.reading.L.mid"));
            Assert.IsFalse(s.GetBool("workorder.reading.L.mid.mismatch"));
            Assert.AreEqual("reading.L.mid", records[0].pointId);
        }

        [Test]
        public void WorkOrder_WrongCopy_FlagsMismatch_NonNumberRejected()
        {
            s.SetFloat("ip.carbon_strip_L.measure_left.reading", 3.40f);
            WorkOrder.Write(s, bus, "reading.L.left", "3.50");
            Assert.IsTrue(s.GetBool("workorder.reading.L.left.mismatch"));
            Assert.IsFalse(WorkOrder.Write(s, bus, "reading.L.right", "abc"));
        }

        [Test]
        public void WorkOrder_HeightDiff_Computed()
        {
            WorkOrder.Write(s, bus, "newmid.L", "12.01");
            WorkOrder.Write(s, bus, "newmid.R", "11.98");
            Assert.AreEqual(0.03f, s.GetFloat("workorder.height_diff"), 1e-3f);
        }

        /// <summary>2026-11-11 頭盔紀錄：重做本步還原成換側前（R）的排列，元件欄位還是 L，箭頭指的那條判成有裂紋。</summary>
        [Test]
        public void SpareShelf_AfterRestoreToOtherSide_ModelAndCrackFollowBlackboard()
        {
            const string slot1 = "env.spare_shelf.slot_1";
            SpareShelf.Stock(s, slot1, SpareShelf.CorrectModel, SpareShelf.Nominal);   // R 排列：1 號 74A 有裂紋
            s.SetBool(slot1 + ".cracked", true);
            var snapR = s.Snapshot();
            SpareShelf.Stock(s, slot1, "CS-1100-64B", SpareShelf.Nominal);             // 換到 L：1 號 64B 完好
            s.SetBool(slot1 + ".cracked", false);
            s.Restore(snapR);                                                            // 重做本步

            var (model, cracked) = SpareShelf.Current(s, slot1, "CS-1100-64B", false);  // 元件欄位還是 L
            Assert.AreEqual(SpareShelf.CorrectModel, model);
            Assert.IsTrue(cracked, "外觀、標籤和判定都要用還原後黑板上的那一條");
            SpareShelf.RecordMeasurement(s, slot1, SpareShelf.Nominal);
            Assert.IsFalse(SpareShelf.Pick(s, slot1, "L", model, cracked));
            Assert.AreEqual("cracked", s.Get("spare.L.pick_result"));
            Assert.AreEqual(("CS-1100-64B", false), SpareShelf.Current(new SimState(bus), slot1, "CS-1100-64B", false), "黑板沒寫就用場景設定");
        }

        [Test]
        public void SpareShelf_PickRules()
        {
            Assert.IsFalse(SpareShelf.Pick(s, "env.spare_shelf.slot_1", "L", "CS-1100-64B", false));
            Assert.AreEqual("wrong_type", s.Get("spare.L.pick_result"));
            Assert.IsFalse(SpareShelf.Pick(s, "env.spare_shelf.slot_3", "L", SpareShelf.CorrectModel, true));
            Assert.AreEqual("cracked", s.Get("spare.L.pick_result"));
            Assert.IsFalse(SpareShelf.Pick(s, "env.spare_shelf.slot_2", "L", SpareShelf.CorrectModel, false));
            Assert.AreEqual("not_measured", s.Get("spare.L.pick_result"));
            s.SetBool("env.spare_shelf.slot_2.measured", true);
            s.SetFloat("env.spare_shelf.slot_2.reading", 12.01f);
            Assert.IsTrue(SpareShelf.Pick(s, "env.spare_shelf.slot_2", "L", SpareShelf.CorrectModel, false));
            Assert.IsTrue(s.GetBool("spare.L.picked"));
            Assert.IsTrue(s.GetBool("spare.L.measured_ok"));
        }

        [Test]
        public void PartsTray_CountsBoltsAndStrips()
        {
            PartsTray.Apply(s, "add:bolt", "ip.carbon_strip_L.bolt_1");
            PartsTray.Apply(s, "add:bolt", "ip.carbon_strip_L.bolt_2");
            PartsTray.Apply(s, "remove:bolt", "ip.carbon_strip_L.bolt_1");
            PartsTray.Apply(s, "add:strip", "part.carbon_strip_L");
            Assert.AreEqual(1f, s.GetFloat("tray.bolts"));
            Assert.AreEqual(1f, s.GetFloat("tray.old_strips"));
            Assert.AreEqual("ip.carbon_strip_L.bolt_2,part.carbon_strip_L", s.Get("tray.items"));
        }
    }
}
