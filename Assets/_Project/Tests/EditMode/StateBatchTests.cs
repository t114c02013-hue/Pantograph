using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>黑板批次寫入（ISimState.BeginBatch／Batch）：Commit 前其他模組看不到、只發一次、可巢狀、例外整批放棄。</summary>
    public class StateBatchTests
    {
        EventBus bus;
        SimState s;
        List<StateChanged> events;

        // 測試用的批次錯誤記錄器：預期的錯誤由各測試斷言後清掉；TearDown 時還有剩（預期以外的錯誤）就讓測試失敗，並還原原本的記錄方式
        readonly List<string> logged = new List<string>();
        Action<string> originalLog;

        [TearDown]
        public void TearDown()
        {
            SimState.ErrorLog = originalLog;
            Assert.IsEmpty(logged, "收到預期以外的批次錯誤：\n" + string.Join("\n", logged));
        }

        [SetUp]
        public void SetUp()
        {
            logged.Clear();
            originalLog = SimState.ErrorLog;
            SimState.ErrorLog = m => logged.Add(m);
            bus = new EventBus();
            s = new SimState(bus);
            events = new List<StateChanged>();
            bus.Subscribe<StateChanged>(e => events.Add(e));
        }

        [Test]
        public void InsideBatch_NoNotificationBeforeCommit_ReadYourWrites()
        {
            s.Set("a", "0");
            events.Clear();
            using (s.BeginBatch())
            {
                s.Set("a", "1");
                s.SetFloat("b", 2.5f);
                Assert.AreEqual(0, events.Count, "Commit 前其他訂閱者收不到通知");
                Assert.AreEqual("1", s.Get("a"), "批次內讀得到自己剛寫的值");
                Assert.AreEqual(2.5f, s.GetFloat("b"), 1e-6f);
                Assert.IsTrue(s.InBatch);
                Assert.AreEqual("0", s.Snapshot()["a"], "快照只含已生效的狀態");
            }
            Assert.IsFalse(s.InBatch);
            Assert.AreEqual(1, events.Count);
        }

        [Test]
        public void Commit_PublishesOnce_WithAllChanges_OldAndNew()
        {
            s.Set("a", "0");
            s.Set("gone", "x");
            events.Clear();
            string seenByOther = null;
            bus.Subscribe<StateChanged>(e => seenByOther = s.Get("b") + "/" + s.Get("a"));
            s.Batch(() =>
            {
                s.Set("a", "1");
                s.Set("b", "2");
                s.Set("gone", null);
                s.Set("a", "3");   // 同一鍵寫兩次：只記最後的值
            });
            Assert.AreEqual(1, events.Count, "只發一次 StateChanged");
            var c = events[0].Changes;
            CollectionAssert.AreEqual(new[] { "a", "b", "gone" }, c.Select(x => x.key).ToArray());
            Assert.AreEqual("0", c[0].oldValue); Assert.AreEqual("3", c[0].newValue);
            Assert.IsNull(c[1].oldValue); Assert.AreEqual("2", c[1].newValue);
            Assert.AreEqual("x", c[2].oldValue); Assert.IsNull(c[2].newValue);
            Assert.AreEqual("a", events[0].targetId, "舊的單筆欄位＝第一筆");
            Assert.AreEqual("3", events[0].text);
            Assert.IsTrue(events[0].Has("b") && !events[0].Has("zzz"));
            Assert.AreEqual("2/3", seenByOther, "收到通知時黑板已全部寫好");
            Assert.IsFalse(s.Has("gone"));
        }

        [Test]
        public void Nested_CommitsOnlyAtOutermost()
        {
            using (s.BeginBatch())
            {
                s.Set("a", "1");
                using (s.BeginBatch())
                {
                    s.Set("b", "2");
                    s.Batch(() => s.Set("c", "3"));
                }
                Assert.AreEqual(0, events.Count, "內層結束不 Commit");
                Assert.AreEqual("3", s.Get("c"));
            }
            Assert.AreEqual(1, events.Count);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, events[0].Changes.Select(x => x.key).ToArray());
        }

        [Test]
        public void Exception_DiscardsWholeBatch_AndLogsError()
        {
            // 故意丟的例外：記錄改寫到清單（SetUp 換上、TearDown 還原；不出現在 Unity Console 的紅色錯誤），並檢查有記錄
            s.Set("a", "0");
            events.Clear();
            Assert.Throws<InvalidOperationException>(() =>
                s.Batch(() =>
                {
                    s.Set("a", "1");
                    s.Set("b", "2");
                    throw new InvalidOperationException("boom");
                }));
            Assert.AreEqual("0", s.Get("a"), "例外時不寫入任何鍵");
            Assert.IsFalse(s.Has("b"));
            Assert.AreEqual(0, events.Count);
            Assert.IsFalse(s.InBatch);

            // 內層拋出例外：外層也整批放棄
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (s.BeginBatch())
                {
                    s.Set("outer", "1");
                    s.Batch(() => throw new InvalidOperationException("inner"));
                }
            });
            Assert.IsFalse(s.Has("outer"));
            Assert.AreEqual(0, events.Count);

            s.Set("after", "ok");
            Assert.AreEqual(1, events.Count, "之後的寫入照常");
            Assert.AreEqual(2, logged.Count, "只收到兩次批次錯誤：" + string.Join(" / ", logged));
            Assert.AreEqual(2, logged.Count(m => Regex.IsMatch(m, "整批放棄.*boom|整批放棄.*inner")), "兩次預期的例外都有記錄");
            logged.Clear();   // 已確認收到；TearDown 檢查不會再有預期以外的錯誤
        }

        [Test]
        public void NoChanges_NoNotification()
        {
            s.Set("a", "1");
            events.Clear();
            s.Batch(() => { s.Set("a", "1"); });
            s.Batch(() => { });
            s.Batch(() => { s.Set("tmp", "x"); s.Set("tmp", null); });   // 新增又刪掉＝沒有變更
            s.Batch(() => { s.Set("a", "2"); s.Set("a", "1"); });           // 改了又改回＝沒有變更
            Assert.AreEqual(0, events.Count);
        }

        [Test]
        public void ForcedWrite_IsReportedEvenIfUnchanged()
        {
            s.SetFloat("d", 0.4f);
            events.Clear();
            s.Batch(() => { s.SetFloat("d", 0.4f, true); s.Set("cmd", "Stop"); });
            Assert.AreEqual(1, events.Count);
            Assert.IsTrue(events[0].TryGet("d", out var c) && c.forced && c.oldValue == c.newValue);
        }

        [Test]
        public void SingleSet_StillOneEventWithOneChange()
        {
            s.Set("a", "1");
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(1, events[0].Changes.Count);
            Assert.AreEqual("a", events[0].targetId);
            Assert.AreEqual("1", events[0].text);
        }

        [Test]
        public void ManualEvent_WithoutChanges_FallsBackToSingleFields()
        {
            var e = new StateChanged { targetId = "k", text = "v" };
            Assert.AreEqual(1, e.Changes.Count);
            Assert.AreEqual("k", e.Changes[0].key);
            Assert.IsTrue(e.Has("k"));
        }

        [Test]
        public void LiftRelease_ForcesLatestDistanceInSameBatchAsStop()
        {
            s.SetFloat(SimIds.PanHeadLowerDistance, 0.8f);
            LiftControl.Send(s, "Down");
            events.Clear();
            LiftControl.Release(s);
            Assert.AreEqual(1, events.Count, "放開＝一批");
            Assert.IsTrue(events[0].Has(SimIds.LiftCmd) && events[0].Has(SimIds.PanHeadLowerDistance));
            events.Clear();
            LiftControl.Release(s);
            Assert.AreEqual(0, events.Count, "已經停了不重複寫");
        }
    }
}
