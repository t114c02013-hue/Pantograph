using System.Collections.Generic;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.XR;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>
    /// PoleNoCollisionTests（試玩 P2）：手上的長桿不和場景碰撞（HeldPole 層只和 ContactTarget 互動）、位置等於手的目標位置、
    /// 穿過場景時同一個物件 0.3 s 內只輕震一次、放回後 layer 換回原值；雙手握沿後手→前手，單手下垂。
    /// </summary>
    public class PoleNoCollisionTests
    {
        [Test]
        public void HeldPoleLayer_IgnoresScene_OnlyContactTargets()
        {
            int pole = LayerMask.NameToLayer(XrHeldPole.LayerName);
            Assert.GreaterOrEqual(pole, 0, "有 HeldPole 層");
            foreach (var n in new[] { "Default", SceneLayers.Boundary, SceneLayers.Walkable, SceneLayers.LocomotionBlocker, SceneLayers.WalkLimit })
                Assert.IsTrue(Physics.GetIgnoreLayerCollision(pole, LayerMask.NameToLayer(n)), "不和 " + n + " 碰撞（護欄、門架、防墜器柱都在這些層）");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(pole, LayerMask.NameToLayer("ContactTarget")), "只碰互動點的觸發器");
        }

        [Test]
        public void Pose_FollowsHandExactly_TwoHandAlongLine_OneHandDroops()
        {
            var rear = new Vector3(1f, 1.2f, 0.5f);
            var (tail, dir) = XrHeldPole.Pose(rear, Quaternion.identity, rear + new Vector3(0f, 0.3f, 0.6f), 15f);
            Assert.Less(Vector3.Distance(tail, rear), 1e-3f, "桿尾就在後手（沒有被推開）");
            Assert.Less(Vector3.Angle(dir, new Vector3(0f, 0.3f, 0.6f)), 0.05f, "雙手握：沿後手→前手");
            var (_, droop) = XrHeldPole.Pose(rear, Quaternion.identity, null, 15f);
            Assert.AreEqual(15f, Vector3.Angle(droop, Vector3.forward), 0.01f, "單手：往下垂 15°");
            Assert.Less(droop.y, 0f);
            Assert.IsTrue(XrHeldPole.CanBeFrontHand(rear, Vector3.forward, 1.6f, 0.12f, rear + new Vector3(0.05f, 0f, 1.0f)), "護環以下可以當前手");
            Assert.IsFalse(XrHeldPole.CanBeFrontHand(rear, Vector3.forward, 1.6f, 0.12f, rear + new Vector3(0f, 0f, 2.0f)), "護環以上不行");
        }

        [Test]
        public void Haptic_OncePerObjectWithinCooldown()
        {
            var d = new HapticDebounce();
            var rail = new GameObject("Rail"); var post = new GameObject("Post");
            try
            {
                Assert.IsTrue(d.Hit(rail, 0f, 0.3f), "進入時震一次");
                Assert.IsFalse(d.Hit(rail, 0.1f, 0.3f), "0.3 s 內再進入不重複");
                Assert.IsTrue(d.Hit(post, 0.1f, 0.3f), "別的物件照震");
                Assert.IsTrue(d.Hit(rail, 0.5f, 0.3f), "離開 0.3 s 後再進入才震");
            }
            finally { Object.DestroyImmediate(rail); Object.DestroyImmediate(post); }
        }

        [Test]
        public void LayerRestoredAfterReturn_ShaftIsTriggerOnly()
        {
            var go = new GameObject("Held_Pole"); var child = new GameObject("Part"); child.transform.SetParent(go.transform);
            child.layer = LayerMask.NameToLayer(SceneLayers.Boundary); child.AddComponent<BoxCollider>();
            try
            {
                var keep = new Dictionary<GameObject, int>();
                XrHeldPole.SetHeldLayer(go, keep, true);
                Assert.AreEqual(LayerMask.NameToLayer(XrHeldPole.LayerName), child.layer, "拿著時換到 HeldPole");
                XrHeldPole.SetHeldLayer(go, keep, false);
                Assert.AreEqual(LayerMask.NameToLayer(SceneLayers.Boundary), child.layer, "放回桿架後換回原本的層");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
