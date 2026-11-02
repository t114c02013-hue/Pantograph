using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Recording;
using PantographSim.XR;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>R1 T3：面板固定在世界、左握把抓把手移動、各站點記位置、叫回眼前、紀錄與重播。</summary>
    public class PanelLayoutTests
    {
        [Test]
        public void Release_PanelUpright_FacingPlayer_Within5Deg()
        {
            var head = new Vector3(0.3f, 1.6f, -0.2f);
            var panel = new Vector3(-0.8f, 1.1f, 0.9f);
            var rot = PanelLogic.FaceHead(panel, head);
            Assert.Less(Vector3.Angle(rot * Vector3.up, Vector3.up), 5f, "直立");
            var toPanel = panel - head; toPanel.y = 0f;
            Assert.Less(Vector3.Angle(rot * Vector3.forward, toPanel), 5f, "面向玩家（Canvas +Z 背對頭）");
        }

        [Test]
        public void StationA_Saved_SwitchToB_BackToA_StillThere()
        {
            var a = new GameObject("A").transform; var b = new GameObject("B").transform;
            try
            {
                a.SetPositionAndRotation(new Vector3(-2f, 0f, 0f), Quaternion.Euler(0, 90, 0));
                b.SetPositionAndRotation(new Vector3(0f, 0f, -0.65f), Quaternion.Euler(0, 0, 0));
                var mem = new PanelMemory();
                var placed = new Pose(new Vector3(-1.5f, 1.3f, 0.6f), Quaternion.Euler(0, 75, 0));
                var (local, yaw) = PanelLogic.ToLocal(a, placed.position, placed.rotation);
                mem.Save("board", "A", local, yaw);
                Assert.IsFalse(mem.TryGet("board", "B", out _, out _), "B 沒存過＝用預設");
                Assert.IsTrue(mem.TryGet("board", "A", out var l2, out var y2));
                var back = PanelLogic.FromLocal(a, l2, y2);
                Assert.Less(Vector3.Distance(back.position, placed.position), 1e-4f);
                Assert.Less(Quaternion.Angle(back.rotation, placed.rotation), 0.01f);
            }
            finally { Object.DestroyImmediate(a.gameObject); Object.DestroyImmediate(b.gameObject); }
        }

        [Test]
        public void Recall_InFront_0p8To1p5m_Within30Deg()
        {
            var layout = AssetDatabase.LoadAssetAtPath<PanelLayout>("Assets/_Project/Modules/XR/Resources/PanelLayout.asset") ?? ScriptableObject.CreateInstance<PanelLayout>();
            var head = new Vector3(1f, 1.6f, 2f); var fwd = Quaternion.Euler(0, 40, 0) * Vector3.forward;
            for (int i = 0; i < 3; i++)
            {
                var p = PanelLogic.Recall(head, fwd, i, layout.recallDistance, layout.recallMaxAngle);
                float d = Vector3.Distance(p.position, head);
                Assert.That(d, Is.InRange(0.8f, 1.5f), "距頭 0.8–1.5 m");
                var flat = p.position - head; flat.y = 0f;
                Assert.LessOrEqual(Vector3.Angle(fwd, flat), 30f, "視線 30° 內");
            }
        }

        [Test]
        public void OnlyLeftDirectGrabsHandles_LiftPanelAndWorkOrderHaveNoHandle()
        {
            var layout = AssetDatabase.LoadAssetAtPath<PanelLayout>("Assets/_Project/Modules/XR/Resources/PanelLayout.asset");
            Assert.IsNotNull(layout);
            CollectionAssert.AreEquivalent(new[] { "board", "dialog" }, layout.panels.Select(p => p.panelId), "升降操作盒、作業單沒有把手");
            Assert.IsTrue(layout.Find("dialog").openInFront, "錯誤視窗一定開在頭前");
            StationTests.WithScene(scene =>
            {
                var all = Object.FindObjectsOfType<MonoBehaviour>(true).Where(m => m.gameObject.scene == scene).ToList();
                int handle = UnityEngine.XR.Interaction.Toolkit.InteractionLayerMask.GetMask(XrPanels.HandleLayerName);
                Assert.AreNotEqual(0, handle, "Interaction Layer PanelHandle 要存在");
                var direct = all.OfType<UnityEngine.XR.Interaction.Toolkit.XRDirectInteractor>().ToList();
                Assert.AreEqual(1, direct.Count, "只有左手一個 XRDirectInteractor");
                Assert.AreEqual(handle, direct[0].interactionLayers.value, "左手只抓把手");
                Assert.IsTrue(direct[0].GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.ActionBasedController>(true).name.StartsWith("Left"));
                foreach (var ray in all.OfType<UnityEngine.XR.Interaction.Toolkit.XRRayInteractor>())
                    Assert.AreEqual(0, ray.interactionLayers.value & handle, "右手射線抓不到把手");
                Assert.IsTrue(all.Any(m => m is XrPanels));
            });
        }

        [Test]
        public void PanelMoved_Recorded_AndReplayRestoresLastPose()
        {
            Assert.AreEqual(3, RecordFiles.SchemaVersion);
            var lines = new System.Collections.Generic.List<RecordLine>
            {
                new RecordLine { t = 1, type = "PanelMoved", data = new System.Collections.Generic.Dictionary<string, object> { { "panelId", "board" }, { "standPointId", "tp.platform.lift" }, { "x", -0.5 }, { "y", 1.3 }, { "z", 1.0 }, { "yaw", 10.0 } } },
                new RecordLine { t = 5, type = "PanelMoved", data = new System.Collections.Generic.Dictionary<string, object> { { "panelId", "board" }, { "standPointId", "tp.roof.inner" }, { "x", 0.2 }, { "y", 1.2 }, { "z", 0.9 }, { "yaw", -5.0 } } },
            };
            var tl = new ReplayTimeline(new System.Collections.Generic.Dictionary<string, object>(), lines, new System.Collections.Generic.List<Snapshot>(), new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<PoseSample>>());
            Assert.AreEqual("tp.platform.lift|-0.5,1.3,1,10", tl.PanelPosesAt(3)["board"]);
            Assert.AreEqual("tp.roof.inner|0.2,1.2,0.9,-5", tl.PanelPosesAt(6)["board"]);
            Assert.IsEmpty(new ReplayTimeline(new System.Collections.Generic.Dictionary<string, object>(), new System.Collections.Generic.List<RecordLine>(), new System.Collections.Generic.List<Snapshot>(), new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<PoseSample>>()).PanelPosesAt(10), "舊檔沒有 PanelMoved 照樣能重播");
        }
    }
}
