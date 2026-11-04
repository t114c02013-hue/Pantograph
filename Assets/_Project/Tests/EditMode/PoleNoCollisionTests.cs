using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.XR;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>terry 裁定：驗電桿、接地桿拿在手上和架上同一支短桿（複製掛位的模型），不再有 2.5 m 長桿和雙手握持。</summary>
    public class PoleNoCollisionTests
    {
        [Test]
        public void HeldPoleLayer_StillConfigured() =>
            Assert.GreaterOrEqual(LayerMask.NameToLayer("HeldPole"), 0, "圖層留著，不動專案設定");

        [Test]
        public void NoLongPoleBehaviour() =>
            Assert.IsNull(typeof(XrPlayer).Assembly.GetType("PantographSim.XR.XrHeldPole"), "長桿雙手握持拿掉了");

        [Test]
        public void HeldModel_IsTheRackModel_BothPoles()
        {
            StationTests.WithScene(scene =>
            {
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                foreach (var id in new[] { "Model_tool.voltage_detector", "Model_tool.grounding_rod" })
                    Assert.IsTrue(all.First(t => t.name == id).Cast<Transform>().Any(c => c.name == "InsulatedPole"), id + "：手上複製這個模型，短桿要在裡面");
            });
        }

        [Test]
        public void RackPole_LowerEnd_Clickable()
        {
            StationTests.WithScene(scene =>
            {
                Physics.SyncTransforms();
                var poles = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(t => t.name == "InsulatedPole").ToList();
                foreach (var p in poles)
                {
                    var rs = p.GetComponentsInChildren<Renderer>(true);
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    var from = new Vector3(b.center.x + 1f, b.min.y + 0.02f, b.center.z);
                    Assert.IsTrue(Physics.Raycast(from, Vector3.left, out var hit, 3f, SceneLayers.InteractMask, QueryTriggerInteraction.Ignore), p.parent.name + "：下端點不到");
                    Assert.IsNotNull(hit.collider.GetComponentInParent<IInteractable>(), hit.collider.name);
                    StringAssert.StartsWith("env.tool_rack.slot_", hit.collider.GetComponentInParent<IInteractable>().Id, hit.collider.name);
                }
            });
        }
    }
}
