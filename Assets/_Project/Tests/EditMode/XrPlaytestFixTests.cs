using System.Linq;
using NUnit.Framework;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.XR;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>2026-11-04 頭盔試玩修正（Docs/playtest/2026-11-04-vr-fixes-software.md）：不用跑 Play 就能驗的部分。</summary>
    public class XrPlaytestFixTests
    {
        /// <summary>第 7 點：天車遙控器每一顆 Btn_* 和 Key_Power 都有 Collider（trigger）。</summary>
        [Test]
        public void Pendant_EveryControlHasTriggerCollider()
        {
            StationTests.WithScene(scene =>
            {
                var remote = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).First(t => t.name == "ENV_CraneRemote");
                var controls = remote.GetComponentsInChildren<Transform>(true).Where(RoofSiteBuilder.IsPendantControl).ToList();
                Assert.Greater(controls.Count, 5, "遙控器要找得到按鍵");
                foreach (var t in controls)
                {
                    var c = t.GetComponent<Collider>();
                    Assert.IsTrue(c != null, t.name + " 要有 Collider");
                    Assert.IsTrue(c.isTrigger, t.name + " 是 trigger");
                }
            });
        }

        /// <summary>第 0 點：追蹤有效才往下走；頭太低不算；逾時用預設高度。</summary>
        [Test]
        public void StartupGate_WaitsForTracking_ThenTimesOut()
        {
            var l = ScriptableObject.CreateInstance<PanelLayout>();
            Assert.AreEqual(XrStartupSequencer.Gate.Wait, XrStartupSequencer.Decide(false, 1.6f, 1f, l));
            Assert.AreEqual(XrStartupSequencer.Gate.Wait, XrStartupSequencer.Decide(true, 0.2f, 1f, l), "頭太低還不算追蹤有效");
            Assert.AreEqual(XrStartupSequencer.Gate.Go, XrStartupSequencer.Decide(true, 1.6f, 1f, l));
            Assert.AreEqual(XrStartupSequencer.Gate.Timeout, XrStartupSequencer.Decide(false, 0f, l.startupTimeoutSec, l));
            Object.DestroyImmediate(l);
        }
    }
}
