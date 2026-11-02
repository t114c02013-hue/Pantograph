using System.Collections.Generic;
using NUnit.Framework;
using PantographSim.Core;

namespace PantographSim.Tests
{
    public class CoreTests
    {
        [Test]
        public void EventBus_PublishesToSubscribers_AndUnsubscribes()
        {
            var bus = new EventBus();
            int count = 0;
            void Handler(ToolSelected e) => count++;
            bus.Subscribe<ToolSelected>(Handler);
            bus.Publish(new ToolSelected { toolId = "tool.caliper" });
            bus.Unsubscribe<ToolSelected>(Handler);
            bus.Publish(new ToolSelected { toolId = "tool.caliper" });
            Assert.AreEqual(1, count);
        }

        [Test]
        public void SimState_SetPublishesOnlyOnChange()
        {
            var bus = new EventBus();
            var keys = new List<string>();
            bus.Subscribe<StateChanged>(e => keys.Add(e.targetId + "=" + e.text));
            var s = new SimState(bus);
            s.Set("a", "1");
            s.Set("a", "1");
            s.SetFloat("b", 2.5f);
            s.SetBool("c", true);
            CollectionAssert.AreEqual(new[] { "a=1", "b=2.5", "c=true" }, keys);
            Assert.AreEqual(2.5f, s.GetFloat("b"), 1e-6);
            Assert.IsTrue(s.GetBool("c"));
        }

        [Test]
        public void SimState_SnapshotRestore_RevertsAndNotifiesDiffOnly()
        {
            var bus = new EventBus();
            var s = new SimState(bus);
            s.Set("keep", "x");
            s.Set("change", "1");
            var snap = s.Snapshot();
            s.Set("change", "2");
            s.Set("added", "y");
            var keys = new List<string>();
            bus.Subscribe<StateChanged>(e => keys.Add(e.targetId));
            s.Restore(snap);
            Assert.AreEqual("1", s.Get("change"));
            Assert.IsFalse(s.Has("added"));
            Assert.AreEqual("x", s.Get("keep"));
            CollectionAssert.AreEquivalent(new[] { "change", "added" }, keys);
        }

        [Test]
        public void CompositeGuard_FirstBlockWins()
        {
            var g = new CompositeActionGuard();
            g.Add(new FixedGuard(GuardResult.Allow));
            g.Add(new FixedGuard(GuardResult.Block("E1")));
            g.Add(new FixedGuard(GuardResult.Block("E2")));
            var r = g.Check(new ActionRequest("tool.x", "part.y", "click"));
            Assert.IsFalse(r.allowed);
            Assert.AreEqual("E1", r.errorId);
        }

        [Test]
        public void ServiceLocator_RegisterAndGet()
        {
            ServiceLocator.Clear();
            var bus = new EventBus();
            ServiceLocator.Register<IEventBus>(bus);
            Assert.AreSame(bus, ServiceLocator.Get<IEventBus>());
            ServiceLocator.Clear();
            Assert.IsNull(ServiceLocator.Get<IEventBus>());
        }

        class FixedGuard : IActionGuard
        {
            readonly GuardResult r;
            public FixedGuard(GuardResult r) { this.r = r; }
            public GuardResult Check(ActionRequest request) => r;
        }
    }
}
