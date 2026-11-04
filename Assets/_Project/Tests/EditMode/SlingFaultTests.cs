using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Tests
{
    /// <summary>steps-v6 v3.10（吊帶割傷故障、D44）：SlingLogic 的開場抽選、判定、碰吊具條件、停用流程、備用吊帶；教學擋下不記錯，練習／考核記錯。</summary>
    public class SlingFaultTests
    {
        static SimState New(string mode, string where = "RM", int index = 2, bool estop = true)
        {
            var s = new SimState(new EventBus());
            s.Set("training.mode", mode);
            s.SetBool(SlingLogic.Fault, true); s.Set(SlingLogic.Where, where); s.SetFloat(SlingLogic.Index, index);
            s.SetBool(SimIds.CraneEStop, estop);
            SlingLogic.Setup(s, new System.Random(1));
            return s;
        }

        /// <summary>脈衝鍵寫過幾次 true。</summary>
        static System.Func<int> Count(SimState s, IEventBus bus, string key)
        {
            int n = 0;
            bus.Subscribe<StateChanged>(e => { foreach (var c in e.Changes) if (c.key == key && c.newValue == "true") n++; });
            return () => n;
        }

        [Test]
        public void Setup_NoFault_NothingCut_RandomPicksValidWhereAndIndex()
        {
            var off = new SimState(new EventBus());
            SlingLogic.Setup(off, new System.Random(1));
            for (int n = 1; n <= SlingLogic.Count; n++) Assert.IsFalse(off.GetBool(SlingLogic.CutKey(n)));
            var s = new SimState(new EventBus());
            s.SetBool(SlingLogic.Fault, true); s.Set(SlingLogic.Where, "random");
            SlingLogic.Setup(s, new System.Random(7));
            CollectionAssert.Contains(new[] { "RM", "IN" }, s.Get(SlingLogic.Where));
            Assert.That((int)s.GetFloat(SlingLogic.Index), Is.InRange(1, SlingLogic.Count));
        }

        [Test]
        public void WhereIn_CutAppearsOnlyAtIn01()
        {
            var s = New("practice", "IN", 3);
            Assert.IsFalse(s.GetBool(SlingLogic.CutKey(3)), "SI_06 查看時四條都正常");
            Assert.IsFalse(s.GetBool(SlingLogic.CutInUse));
            SlingLogic.OnStepStarted(s, "STP_LF_01_PreLift");
            Assert.IsFalse(s.GetBool(SlingLogic.CutKey(3)));
            SlingLogic.OnStepStarted(s, SlingLogic.InStep);
            Assert.IsTrue(s.GetBool(SlingLogic.CutKey(3)));
            Assert.IsTrue(s.GetBool(SlingLogic.CutInUse));
        }

        [Test]
        public void Verdict_CutUsable_TutorialBlocks_PracticeRecords_FalseRejectMinor()
        {
            var t = New("tutorial");
            Assert.IsNotNull(SlingLogic.Verdict(t, SlingLogic.Sling(2), true, null), "教學擋下");
            Assert.IsFalse(t.Has(SlingLogic.Sling(2) + ".verdict"));
            Assert.IsFalse(t.Has(SlingLogic.JudgedUsableKey), "教學不記錯");

            var bus = new EventBus(); var s = new SimState(bus);
            var judged = Count(s, bus, SlingLogic.JudgedUsableKey); var reject = Count(s, bus, SlingLogic.FalseRejectKey);
            s.Set("training.mode", "practice"); s.SetBool(SlingLogic.Fault, true); s.Set(SlingLogic.Where, "RM"); s.SetFloat(SlingLogic.Index, 2);
            SlingLogic.Setup(s, new System.Random(1));
            SlingLogic.Verdict(s, SlingLogic.Sling(2), true, null);
            Assert.AreEqual(1, judged()); Assert.IsFalse(s.GetBool(SlingLogic.JudgedUsableKey), "脈衝寫回 false，下一次才是新的一次");
            Assert.IsFalse(s.GetBool(SlingLogic.Sling(2) + ".verdict_correct"));
            SlingLogic.Verdict(s, SlingLogic.Sling(1), false, null);
            Assert.AreEqual(1, reject());
            s.Batch(() => SlingLogic.Verdict(s, SlingLogic.Sling(2), true, null));
            Assert.AreEqual(2, judged(), "在一批裡（SiteObject 點擊）也要記到");
        }

        [Test]
        public void Touch_NeedsEStop_And_Clearance()
        {
            var t = New("tutorial", estop: false);
            StringAssert.Contains("緊急停止", SlingLogic.Touch(t));
            var bus = new EventBus(); var s = new SimState(bus); var n = Count(s, bus, SlingLogic.TouchNoEStopKey);
            s.Set("training.mode", "practice");
            Assert.IsNull(SlingLogic.Touch(s), "練習不擋");
            Assert.AreEqual(1, n());
            s.SetBool(SimIds.CraneEStop, true);
            Assert.IsNull(SlingLogic.Touch(s)); Assert.AreEqual(1, n(), "按著急停不記");
            s.SetBool(SlingLogic.HookClearKey, false);
            StringAssert.Contains("0.3", SlingLogic.Touch(s), "離吊鉤組太近一律擋");
            s.SetBool(SlingLogic.HookClearKey, true); s.SetBool(SlingLogic.UnderHookClearKey, false);
            Assert.IsNotNull(SlingLogic.Touch(s), "在吊鉤組正下方一律擋");
            Assert.IsTrue(SlingLogic.NearHook(SlingLogic.Sling(1)) && SlingLogic.NearHook(SlingLogic.Hook) && SlingLogic.NearHook(SlingLogic.BeamEye(2)));
            Assert.IsFalse(SlingLogic.NearHook(SlingLogic.Spare), "吊具架上的備用吊帶不在吊鉤組旁");
        }

        [Test]
        public void Reject_RackIsWrong_BinNeedsTag_SpareNeedsReportAndVerdict()
        {
            var bus = new EventBus(); var s = new SimState(bus);
            var rack = Count(s, bus, SlingLogic.ReturnedToRackKey); var noReport = Count(s, bus, SlingLogic.UnreportedKey);
            s.Set("training.mode", "practice"); s.SetBool(SlingLogic.Fault, true); s.Set(SlingLogic.Where, "RM"); s.SetFloat(SlingLogic.Index, 2); s.SetBool(SimIds.CraneEStop, true);
            SlingLogic.Setup(s, new System.Random(1));
            Assert.IsNotNull(SlingLogic.Detach(s, 1), "沒破損的不用拆");
            Assert.IsTrue(s.GetBool(SlingLogic.CutInUse));
            Assert.IsNull(SlingLogic.Detach(s, 2));
            Assert.IsFalse(s.GetBool(SlingLogic.CutInUse), "拆下後不在吊梁上");
            Assert.IsNotNull(SlingLogic.PutInBin(s), "沒掛停用牌不能放");
            Assert.IsNotNull(SlingLogic.HangTag(s, 2), "沒拿停用牌");
            SlingLogic.RackClick(s);
            Assert.AreEqual(SlingLogic.OnRack, s.Get(SlingLogic.Rejected + ".state")); Assert.AreEqual(1, rack());
            SlingLogic.RackClick(s);
            Assert.AreEqual(SlingLogic.InHand, s.Get(SlingLogic.Rejected + ".state"), "放錯可以拿回來");
            Assert.IsNull(SlingLogic.TakeTag(s)); Assert.IsNull(SlingLogic.HangTag(s, 2)); Assert.IsNull(SlingLogic.PutInBin(s));
            Assert.AreEqual(SlingLogic.InBin, s.Get(SlingLogic.Rejected + ".state"));

            Assert.IsNotNull(SlingLogic.HangSpare(s, 2), "還沒拿備用吊帶");
            Assert.IsNull(SlingLogic.TakeSpare(s)); Assert.AreEqual(1, noReport(), "沒回報就拿");
            Assert.IsNotNull(SlingLogic.HangSpare(s, 2), "還沒查看判可用");
            SlingLogic.Verdict(s, SlingLogic.Spare, true, null);
            Assert.IsNotNull(SlingLogic.HangSpare(s, 1), "要掛回拆下的那一號");
            Assert.IsNull(SlingLogic.HangSpare(s, 2));
            Assert.IsTrue(s.GetBool(SlingLogic.ReplacedKey(2))); Assert.IsFalse(s.GetBool(SlingLogic.CutKey(2)));

            var t = New("tutorial");
            Assert.IsNotNull(SlingLogic.TakeSpare(t), "教學：先回報");
            Assert.IsNull(SlingLogic.Detach(t, 2));
            Assert.IsNotNull(SlingLogic.RackClick(t), "教學：不能放回吊具架");
            Assert.AreEqual(SlingLogic.InHand, t.Get(SlingLogic.Rejected + ".state"));
        }
    }
}
