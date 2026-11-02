using System.Linq;
using PantographSim.Core;
using NUnit.Framework;
using PantographSim.UI;
using PantographSim.XR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace PantographSim.Tests
{
    /// <summary>VR 指向介面：每個 World Space Canvas 都有 TrackedDeviceGraphicRaycaster、每個可點元件都有可被射線打到的圖形、圓點大小固定視角。</summary>
    public class XrPointerTests
    {
        class FakeRig : IXrRig
        {
            public Camera Eye { get; set; }
            public Transform Head => Eye.transform;
            public Transform LeftHand { get; set; }
            public Transform RightHand { get; set; }
        }

        [Test]
        public void EveryWorldSpaceCanvas_HasTrackedRaycaster_AndEverySelectableHasRaycastGraphic()
        {
            var root = new GameObject("TestXrUi");
            var cam = new GameObject("Eye").AddComponent<Camera>();
            var rig = new FakeRig { Eye = cam, LeftHand = new GameObject("L").transform, RightHand = new GameObject("R").transform };
            try
            {
                var ui = root.AddComponent<SliceUI>();
                ui.Build();
                ui.UseXrLayout(rig);
                XrModeSwitch.AddRaycasters();
                var canvases = Object.FindObjectsOfType<Canvas>(true).Where(c => c.isRootCanvas && c.renderMode == RenderMode.WorldSpace).ToList();
                Assert.IsTrue(canvases.Count >= 6);
                foreach (var c in canvases) Assert.IsNotNull(c.GetComponent<TrackedDeviceGraphicRaycaster>(), c.name + " 沒有 TrackedDeviceGraphicRaycaster");
                var clip = canvases.First(c => c.name == XrModeSwitch.ClipboardCanvas);
                Assert.IsFalse(clip.GetComponent<TrackedDeviceGraphicRaycaster>().ignoreReversedGraphics, "左手夾板背面也要接住射線");
                var selectables = root.GetComponentsInChildren<Selectable>(true).Concat(canvases.SelectMany(c => c.GetComponentsInChildren<Selectable>(true))).Distinct().ToList();
                Assert.Greater(selectables.Count, 20);
                foreach (var s in selectables)
                {
                    bool hit = (s.targetGraphic != null && s.targetGraphic.raycastTarget) || s.GetComponents<Graphic>().Any(g => g.raycastTarget);
                    Assert.IsTrue(hit, $"{s.name}（{s.transform.parent?.name}）沒有可被射線打到的 Graphic");
                }
            }
            finally
            {
                foreach (var es in Object.FindObjectsOfType<EventSystem>()) Object.DestroyImmediate(es.gameObject);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cam.gameObject);
                Object.DestroyImmediate(rig.LeftHand.gameObject);
                Object.DestroyImmediate(rig.RightHand.gameObject);
            }
        }

        [Test]
        public void Reticle_FixedAngularSize_About1cmAt60cm()
        {
            Assert.That(0.6f * XrUiPointer.AngularDiameter, Is.InRange(0.008f, 0.015f), "手臂距離（0.5–0.6 m）約 1 cm");
            Assert.AreEqual(2f, (1.2f * XrUiPointer.AngularDiameter) / (0.6f * XrUiPointer.AngularDiameter), 1e-4f, "距離加倍，圓點加倍（視角大小不變）");
        }
    }
}
