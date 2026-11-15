using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Worksite;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>terry 2026-11-15（150A、151A、154–166）：教學箱開蓋 LidToggle、實體按鈕 PressFollow、指示燈點光源 LampLight。</summary>
    public class TeachingBoxTests
    {
        EventBus bus; SimState s;
        readonly List<GameObject> made = new List<GameObject>();
        readonly List<object> performed = new List<object>();

        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
            bus = new EventBus(); s = new SimState(bus);
            ServiceLocator.Register<IEventBus>(bus); ServiceLocator.Register<ISimState>(s);
            bus.Subscribe<ActionPerformed>(e => performed.Add(e));
            bus.Subscribe<ErrorDetected>(e => performed.Add(e));
        }

        [TearDown]
        public void TearDown() { foreach (var g in made) Object.DestroyImmediate(g); made.Clear(); performed.Clear(); ServiceLocator.Clear(); }

        static void Start(Component c) => c.GetType().GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(c, null);

        (LidToggle[] lids, GameObject inside) Box(int n)
        {
            var root = new GameObject("Box"); made.Add(root);
            var inside = new GameObject("Inside_1"); inside.transform.SetParent(root.transform);
            var lids = new LidToggle[n];
            for (int i = 0; i < n; i++)
            {
                var g = new GameObject("Lid_" + (i + 1)); g.transform.SetParent(root.transform);
                lids[i] = g.AddComponent<LidToggle>();
                lids[i].Configure($"env.roof_box.hvac.lid_{i + 1}", "蓋子");
            }
            foreach (var l in lids) { l.Setup(100f, 0.6f, lids, new[] { inside }); Start(l); }
            inside.SetActive(false);
            return (lids, inside);
        }

        static void Run(IEnumerable<LidToggle> lids, float seconds) { foreach (var l in lids) l.Tick(seconds); }

        [Test]
        public void Lid_OpenTurnsToOpenDeg_ShowsInside_HidesOnlyWhenAllClosed()
        {
            var (lids, inside) = Box(2);
            s.Set(lids[0].StateKey, LidToggle.Open); s.Set(lids[1].StateKey, LidToggle.Open);
            Run(lids, 1f);
            Assert.AreEqual(100f, lids[0].Angle, 1e-3f, "開到 lidOpenDeg");
            Assert.IsTrue(inside.activeSelf, "開著要看得到內部");
            s.Set(lids[0].StateKey, LidToggle.Closed);
            Run(lids, 1f);
            Assert.IsTrue(inside.activeSelf, "還有一片開著，內部不隱藏");
            s.Set(lids[1].StateKey, LidToggle.Closed);
            Run(lids, 0.1f);
            Assert.IsTrue(inside.activeSelf, "還在轉，內部先不隱藏");
            Run(lids, 1f);
            Assert.AreEqual(0f, lids[1].Angle, 1e-3f);
            Assert.IsFalse(inside.activeSelf, "全部關好才隱藏");
        }

        [Test]
        public void Lid_XrPointChange_ClosesAll()
        {
            var (lids, _) = Box(3);
            foreach (var l in lids) s.Set(l.StateKey, LidToggle.Open);
            s.Set(LidToggle.PointKey, "tp.stand.box_w");
            foreach (var l in lids) Assert.AreEqual(LidToggle.Closed, s.Get(l.StateKey), l.Id);
        }

        [Test]
        public void Lid_Click_TogglesEvenWithTool_NoActionPerformedOrError()
        {
            var (lids, _) = Box(1);
            bus.Publish(new ActionRequested { targetId = lids[0].Id });
            Assert.AreEqual(LidToggle.Open, s.Get(lids[0].StateKey));
            bus.Publish(new ActionRequested { targetId = lids[0].Id, toolId = "tool.caliper" });
            Assert.AreEqual(LidToggle.Closed, s.Get(lids[0].StateKey), "拿著工具點照樣切換");
            Assert.IsEmpty(performed, "不送 ActionPerformed、不產生 ErrorDetected");
        }

        PressFollow Button(string key, string value, bool hold)
        {
            var g = new GameObject("Btn"); made.Add(g);
            var p = g.AddComponent<PressFollow>(); p.Setup(key, value, hold, 0.005f, 0.15f); Start(p); p.Tick(0f);
            return p;
        }

        [Test]
        public void Buttons_HoldWhileLiftCmd_SelfTestPressesOnce()
        {
            var up = Button("ctrl.lift.cmd", "Up", true); var down = Button("ctrl.lift.cmd", "Down", true); var test = Button("tool.voltage_detector.selftest_time", "", false);
            s.Set("ctrl.lift.cmd", "Up"); up.Tick(0.016f); down.Tick(0.016f);
            Assert.IsTrue(up.Pressed); Assert.IsFalse(down.Pressed);
            Assert.AreEqual(-0.005f, up.transform.localPosition.y, 1e-5f, "陷 btnPressMm");
            Assert.AreEqual(0f, down.transform.localPosition.y, 1e-5f);
            s.Set("ctrl.lift.cmd", "Stop"); up.Tick(0.016f);
            Assert.IsFalse(up.Pressed); Assert.AreEqual(0f, up.transform.localPosition.y, 1e-5f, "放開彈回");
            s.SetFloat("tool.voltage_detector.selftest_time", 3.2f); test.Tick(0.016f);
            Assert.IsTrue(test.Pressed, "自檢陷一下");
            test.Tick(0.2f); test.Tick(0.016f);
            Assert.IsFalse(test.Pressed, "btnPressSeconds 後彈回");
            Assert.AreEqual(0f, test.transform.localPosition.y, 1e-5f);
        }

        [Test]
        public void Carve_BoxesPlusNarrowGapsOnly()   // 教學箱規格 4.4：縫 ≤ 0.6 m 且 Z 重疊才挖；1.6 m 走道不挖
        {
            var a = Rect.MinMaxRect(0f, 0f, 1f, 1f);
            var b = Rect.MinMaxRect(1.5f, 0.5f, 2.5f, 2f);    // 縫 0.5，Z 重疊 0.5…1
            var c = Rect.MinMaxRect(4.1f, 0f, 5f, 1f);        // 和 b 縫 1.6 → 不挖
            var list = PantographSim.Editor.RoofSiteBuilder.CarveRects(new[] { a, b, c }, 0.6f);
            Assert.AreEqual(4, list.Count);
            Assert.Contains(Rect.MinMaxRect(1f, 0.5f, 1.5f, 1f), list);
        }

        [Test]
        public void LampLight_Off_IsDimmedNotDark()
        {
            var g = new GameObject("Lamp"); made.Add(g);
            var l = new GameObject("LampLight").AddComponent<Light>(); l.transform.SetParent(g.transform);
            var lamp = g.AddComponent<LampLight>(); lamp.Setup(l, 0.8f, 0.1f);
            lamp.Set(true, Color.green);
            Assert.AreEqual(0.8f, l.intensity, 1e-5f); Assert.AreEqual(Color.green, l.color);
            lamp.Set(false, Color.red);
            Assert.AreEqual(0.08f, l.intensity, 1e-5f, "暗＝lampLightIntensity × lampDimRatio");
        }
    }
}
