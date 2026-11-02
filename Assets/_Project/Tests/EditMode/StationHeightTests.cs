using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.XR;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>R1 T1 高度：站點落在地板上、Floor 追蹤、平台與車頂高度、滑板頂面高度、Raised／Real 與蹲姿輔助的視點偏移。</summary>
    public class StationHeightTests
    {
        const string Folder = "Assets/_Project/Modules/Worksite/Data";
        static HeightProfile Profile => AssetDatabase.LoadAssetAtPath<HeightProfile>(Folder + "/HeightProfile.asset");
        static RoofSceneDef Roof => AssetDatabase.LoadAssetAtPath<RoofSceneDef>(Folder + "/RoofSceneDef.asset");

        [Test]
        public void EveryStation_StandsOnWalkable_Within2cm_AndXrOriginIsFloorWithZeroOffset()
        {
            StationTests.WithScene(scene =>
            {
                Physics.SyncTransforms();
                var pts = Object.FindObjectsOfType<XrTeleportPoint>(true).Where(p => p.gameObject.scene == scene).ToList();
                Assert.AreEqual(AssetDatabase.LoadAssetAtPath<StationSet>(Folder + "/StationSet.asset").stations.Length, pts.Count, "每個站點資料都有場景站點");   // R1.5a：8 → 9（新增 tp.platform.crane）
                foreach (var p in pts)
                {
                    var pos = p.transform.position;
                    Assert.IsTrue(Physics.Raycast(new Vector3(pos.x, 5f, pos.z), Vector3.down, out var hit, 20f, SceneLayers.WalkableMask, QueryTriggerInteraction.Ignore), p.PointId + " 下方沒有 Walkable");
                    Assert.AreEqual(hit.point.y, pos.y, 0.02f, p.PointId);
                }
                var origin = Object.FindObjectsOfType<MonoBehaviour>(true).First(o => o.gameObject.scene == scene && o.GetType().Name == "XROrigin");
                var so = new SerializedObject(origin);
                Assert.AreEqual("Floor", so.FindProperty("m_RequestedTrackingOriginMode").enumNames[so.FindProperty("m_RequestedTrackingOriginMode").enumValueIndex]);
                Assert.AreEqual(0f, so.FindProperty("m_CameraYOffset").floatValue, 1e-4f);
                Assert.IsFalse(scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Any(t => t.name == "PlatformRail"), "舊的浮空平台還在");
            });
        }

        [Test]
        public void PlatformAndRoof_Within15cm_StripTop_0p4To0p8AboveRoof()
        {
            Assert.LessOrEqual(Mathf.Abs(Roof.platformFloorY - Roof.roofFloorY), 0.15f);
            Assert.That(Profile.measuredStripTop, Is.InRange(0.4f, 0.8f), "降弓後滑板頂面在車頂上方 0.4–0.8 m");
        }

        [Test]
        public void HeightProfile_DefaultRaised_RoofOffsetGivesRaisedStripTop_RealAndOffRoofAreZero()
        {
            var p = Profile;
            Assert.AreEqual(HeightMode.Raised, p.mode);
            Assert.IsFalse(p.crouchAssistDefaultOn, "蹲姿輔助預設關");
            float o = XrViewHeight.OffsetFor("Roof", HeightMode.Raised, p.measuredStripTop, p.raisedStripTop, false, p.crouchOffset);
            Assert.AreEqual(p.raisedStripTop, p.measuredStripTop - o, 0.05f, "車頂上滑板頂面相對視點＝raisedStripTop");
            Assert.AreEqual(0f, XrViewHeight.OffsetFor("Roof", HeightMode.Real, p.measuredStripTop, p.raisedStripTop, false, p.crouchOffset));
            Assert.AreEqual(0f, XrViewHeight.OffsetFor("Platform", HeightMode.Raised, p.measuredStripTop, p.raisedStripTop, true, p.crouchOffset));
            Assert.AreEqual(0f, XrViewHeight.OffsetFor("Cab", HeightMode.Raised, p.measuredStripTop, p.raisedStripTop, false, p.crouchOffset));
            Assert.AreEqual(o + p.crouchOffset, XrViewHeight.OffsetFor("Roof", HeightMode.Raised, p.measuredStripTop, p.raisedStripTop, true, p.crouchOffset), 1e-4f);
        }

        [Test]
        public void SharedRoofStation_ClearsPanHead_AtLeast15cm()
        {
            StationTests.WithScene(scene =>
            {
                var ids = StationTests.SceneIds(scene);
                var head = ids["part.pan_head"].GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                var roof = Object.FindObjectsOfType<XrTeleportPoint>(true).First(p => p.gameObject.scene == scene && p.PointId == "tp.roof.inner").transform.position;
                var closest = new Vector2(Mathf.Clamp(roof.x, head.min.x, head.max.x), Mathf.Clamp(roof.z, head.min.z, head.max.z));
                // 身體膠囊半徑 0.25 m ＋ 淨距 0.15 m
                Assert.GreaterOrEqual(Vector2.Distance(new Vector2(roof.x, roof.z), closest), 0.25f + 0.15f - 1e-3f);
            });
        }
    }
}
