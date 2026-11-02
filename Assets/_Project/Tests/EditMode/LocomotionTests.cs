using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.XR;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>R1 T6 右搖桿平滑移動：預設值、車頂速度上限、不可走區都有碰撞體、Walkable 不和不可走區重疊、危險區只提示。</summary>
    public class LocomotionTests
    {
        static LocomotionSettings L => AssetDatabase.LoadAssetAtPath<LocomotionSettings>("Assets/_Project/Modules/Worksite/Data/LocomotionSettings.asset");

        [Test]
        public void Defaults_Speed1_VignetteOn_SmoothMoveOn_Deadzone015_Capsule02()
        {
            Assert.AreEqual(1.0f, L.speed, 1e-4f);
            Assert.IsTrue(L.vignetteDefaultOn);
            Assert.IsTrue(L.smoothMoveDefaultOn);
            Assert.AreEqual(0.15f, L.deadzone, 1e-4f);
            Assert.AreEqual(0.2f, L.capsuleRadius, 1e-4f);
            Assert.AreEqual(0.2f, L.stepOffset, 1e-4f);
        }

        [Test]
        public void RoofSpeedCap_1_OtherFloorsClampTo06_15()
        {
            Assert.AreEqual(1.0f, XrLocomotion.SpeedFor(1.5f, "Roof", L), 1e-4f, "車頂上限 1.0");
            Assert.AreEqual(1.5f, XrLocomotion.SpeedFor(1.5f, "Platform", L), 1e-4f);
            Assert.AreEqual(0.6f, XrLocomotion.SpeedFor(0.2f, "Platform", L), 1e-4f);
            Assert.AreEqual(1.5f, XrLocomotion.SpeedFor(3f, "Cab", L), 1e-4f);
        }

        [Test]
        public void Danger_OffWalkable_OrOnLockedRoof_OnlyHint()
        {
            Assert.IsTrue(XrLocomotion.IsDanger(false, "", true), "腳下沒有可走地板");
            Assert.IsTrue(XrLocomotion.IsDanger(true, "Roof", false), "車頂沒開放卻在車頂");
            Assert.IsFalse(XrLocomotion.IsDanger(true, "Roof", true));
            Assert.IsFalse(XrLocomotion.IsDanger(true, "Platform", false));
        }

        [Test]
        public void Scene_RigHasCharacterControllerAndMoveProvider_RightHandOnly()
        {
            StationTests.WithScene(scene =>
            {
                var all = Object.FindObjectsOfType<MonoBehaviour>(true).Where(m => m.gameObject.scene == scene).ToList();
                var move = all.FirstOrDefault(m => m.GetType().Name == "ActionBasedContinuousMoveProvider");
                Assert.IsNotNull(move, "XR Origin 要有連續移動");
                var cc = move.GetComponent<CharacterController>();
                Assert.IsNotNull(cc, "XR Origin 要有 CharacterController");
                Assert.AreEqual(0.2f, cc.radius, 1e-4f); Assert.AreEqual(0.2f, cc.stepOffset, 1e-4f);
                var so = new SerializedObject(move);
                Assert.IsTrue(so.FindProperty("m_EnableStrafe").boolValue, "可以橫移");
                Assert.IsFalse(so.FindProperty("m_EnableFly").boolValue);
                Assert.IsTrue(so.FindProperty("m_UseGravity").boolValue, "有重力、腳貼地");
                Assert.IsNotNull(all.FirstOrDefault(m => m is XrLocomotion));
                Assert.IsFalse(all.Any(m => m.GetType().Name.Contains("SnapTurnProvider") || m.GetType().Name.Contains("ContinuousTurnProvider")), "不做平滑轉向");
            });
        }

        [Test]
        public void Scene_WalkableDoesNotOverlapObstacles_AndRoofEdgeHasWalls()
        {
            StationTests.WithScene(scene =>
            {
                Physics.SyncTransforms();
                var def = AssetDatabase.LoadAssetAtPath<RoofSceneDef>("Assets/_Project/Modules/Worksite/Data/RoofSceneDef.asset");
                float walkHalf = def.carBodyHalfWidth - def.roofEdgeNoWalk;
                var cols = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(true)).ToList();
                var roofWalk = cols.Where(c => c.gameObject.layer == LayerMask.NameToLayer(SceneLayers.Walkable) && c.transform.parent != null && c.transform.parent.parent != null && c.transform.parent.parent.name == "ENV_RoofSection").ToList();
                Assert.IsNotEmpty(roofWalk);
                foreach (var w in roofWalk)
                    Assert.LessOrEqual(Mathf.Max(Mathf.Abs(w.bounds.min.x), Mathf.Abs(w.bounds.max.x)), walkHalf + 1e-3f, w.name + " 伸進車頂邊 0.5 m 內");
                var insulators = cols.Where(c => c.name == "InsulatorCollision").ToList();
                Assert.AreEqual(4, insulators.Count);
                foreach (var ins in insulators)
                    foreach (var w in roofWalk)
                    {
                        var a = ins.bounds; var b = w.bounds;
                        bool overlapXZ = a.min.x < b.max.x - 1e-3f && a.max.x > b.min.x + 1e-3f && a.min.z < b.max.z - 1e-3f && a.max.z > b.min.z + 1e-3f;
                        Assert.IsFalse(overlapXZ, $"{w.name} 和絕緣子重疊");
                    }
                Assert.IsTrue(cols.Any(c => c.name == "EdgeWall_+X") && cols.Any(c => c.name == "EdgeWall_-X_A"), "車頂邊要有邊界牆");
                Assert.IsTrue(cols.Any(c => c.name == "Blocker" && c.gameObject.layer == LayerMask.NameToLayer(SceneLayers.LocomotionBlocker)), "不可走處要有 LocomotionBlocker");
            });
        }
    }
}
