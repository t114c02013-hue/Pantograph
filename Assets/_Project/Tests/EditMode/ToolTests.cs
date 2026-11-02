using System.IO;
using System.Linq;
using NUnit.Framework;
using PantographSim.Data;
using PantographSim.Tools;

namespace PantographSim.Tests
{
    public class ToolTests
    {
        [Test]
        public void Caliper_ZeroedReadingWithinNoise_UnzeroedPlus030()
        {
            var rng = new System.Random(7);
            for (int i = 0; i < 200; i++)
            {
                float r = CaliperLogic.Reading(2.80f, true, rng);
                Assert.That(r, Is.InRange(2.78f - 1e-4f, 2.82f + 1e-4f));
                Assert.AreEqual(r, (float)System.Math.Round(r, 2), 1e-5f, "解析度 0.01 mm");
            }
            Assert.AreEqual(3.10f, CaliperLogic.Reading(2.80f, false, null), 1e-4f);
        }

        [Test]
        public void Torque_RisesEightPerSecond_ReachesAndClamps()
        {
            float t = TorqueLogic.SnugTorque;
            for (int i = 0; i < 25; i++) t = TorqueLogic.Hold(t, 0.1f);
            Assert.AreEqual(25f, t, 1e-3f);
            Assert.IsTrue(TorqueLogic.JustReached(24.9f, 25.1f, 25f));
            Assert.IsFalse(TorqueLogic.JustReached(25.1f, 25.9f, 25f));
            Assert.AreEqual(25.5f, TorqueLogic.Adjust(25f, 0.5f));
            Assert.AreEqual(50f, TorqueLogic.Adjust(49.8f, 5f));
            Assert.AreEqual(5f, TorqueLogic.Adjust(5f, -0.5f));
        }

        [Test]
        public void Socket_HasThreeSizesIncluding13()
        {
            CollectionAssert.AreEqual(new[] { "10", "13", "17" }, SocketLogic.Sizes);
        }

        [Test]
        public void EveryToolHasTutorial_3to5Pages()
        {
            const string path = "Assets/_Project/Modules/Procedures/Data/carbon_strip_slice.json";
            Assert.IsTrue(File.Exists(path), "步驟資料不存在：" + path);
            var bundle = ProcedureJsonConverter.Build(File.ReadAllText(path));
            var expected = new[] { "tool.voltage_detector", "tool.grounding_rod", "tool.caliper", "tool.socket_wrench", "tool.torque_wrench", "tool.cleaning_cloth", "tool.marker", "tool.lockout_tag", "tool.safety_harness" };
            CollectionAssert.AreEquivalent(expected, bundle.tools.Select(t => t.toolId).ToArray());
            foreach (var t in bundle.tools)
            {
                Assert.That(t.tutorial.pages.Length, Is.InRange(3, 5), t.toolId + " 教學頁數");
                Assert.IsTrue(t.tutorial.pages.All(p => !string.IsNullOrWhiteSpace(p.title) && !string.IsNullOrWhiteSpace(p.body)), t.toolId);
                Assert.That(t.hotkey, Is.InRange(1, 9));
            }
            Assert.AreEqual(9, bundle.tools.Select(t => t.hotkey).Distinct().Count(), "熱鍵不重複");
        }
    }
}
