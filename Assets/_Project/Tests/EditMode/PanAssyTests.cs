using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Parts.Common;
using PantographSim.Parts.Mechanism;
using UnityEngine;

namespace PantographSim.Tests
{
    static class PanAssy
    {
        public static Transform Root(UnityEngine.SceneManagement.Scene s) =>
            s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "PAN_Root");

        /// <summary>所有和位置有關的判定值（cm）：每個 PositionCheck 的檢測點到目標距離，在降弓、半升、升弓三種姿態下各量一次。</summary>
        public static List<float> Measure(Transform root)
        {
            var mech = root.GetComponentInChildren<PantographMechanism>(true);
            float lift = mech.Lift;
            var list = new List<float>();
            foreach (var l in new[] { 0f, 0.5f, 1f })
            {
                mech.SetLift(l);
                foreach (var pc in root.GetComponentsInChildren<PositionCheck>(true).OrderBy(p => p.name))
                {
                    var check = (Transform)typeof(PositionCheck).GetField("checkPoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(pc);
                    list.Add(PositionCheckLogic.DistanceCm(check.position, pc.TargetWorld));
                }
            }
            mech.SetLift(lift);
            return list;
        }

        public static List<string> Ids(Transform root) =>
            root.GetComponentsInChildren<Transform>(true).Select(t => t.name + "|" + (t.GetComponent<ISimObject>()?.Id ?? "")).OrderBy(x => x).ToList();
    }

    /// <summary>R1.5a a1：整組平移 (+3, −2, +1)、繞 Y 轉 90° 後，和位置有關的判定值都和沒移動時相同。</summary>
    public class PanAssyFrameTests
    {
        [Test]
        public void MoveAndRotatePanRoot_PositionJudgementsUnchanged()
        {
            StationTests.WithScene(scene =>
            {
                var root = PanAssy.Root(scene);
                Assert.IsNotNull(root, "場景要有 PAN_Root");
                var pos = root.position; var rot = root.rotation;
                var before = PanAssy.Measure(root);
                try
                {
                    root.SetPositionAndRotation(pos + new Vector3(3f, -2f, 1f), Quaternion.Euler(0f, 90f, 0f) * rot);
                    var after = PanAssy.Measure(root);
                    Assert.AreEqual(before.Count, after.Count);
                    Assert.Greater(before.Count, 0);
                    for (int i = 0; i < before.Count; i++) Assert.AreEqual(before[i], after[i], 1e-3f, $"第 {i} 個判定距離（cm）");
                }
                finally { root.SetPositionAndRotation(pos, rot); }
            });
        }
    }

    /// <summary>R1.5a a1：所有集電弓的零件、互動點與網格都在 PAN_Root 底下。</summary>
    public class PanAssyHierarchyTests
    {
        [Test]
        public void AllPantographPartsAndMeshes_UnderPanRoot()
        {
            StationTests.WithScene(scene =>
            {
                var root = PanAssy.Root(scene);
                Assert.IsNotNull(root);
                foreach (var n in new[] { "PAN_Visual", "PAN_Mechanism", "PAN_Parts", "PAN_Base", "PAN_LiftPoints", "PAN_Connections", "PAN_HoldDown" })
                    Assert.IsNotNull(root.Find(n), "缺 " + n);
                for (int i = 1; i <= 4; i++)
                {
                    Assert.IsNotNull(root.Find("PAN_Base/BaseHole_" + i)); Assert.IsNotNull(root.Find("PAN_Base/LocatingHole_" + i));
                    Assert.IsNotNull(root.Find("PAN_LiftPoints/ip.pan_assy.lift_point_" + i));
                }
                Assert.AreEqual("part.pan_assy", root.GetComponent<ISimObject>()?.Id);
                var sims = Object.FindObjectsOfType<MonoBehaviour>(true).OfType<ISimObject>().Where(o => ((Component)o).gameObject.scene == scene)
                    .Where(o => o.Id.StartsWith("part.") || o.Id.StartsWith("ip.pan_") || o.Id.StartsWith("ip.carbon_strip_")).ToList();
                Assert.Greater(sims.Count, 30);
                foreach (var o in sims) Assert.IsTrue(((Component)o).transform.IsChildOf(root), o.Id + " 不在 PAN_Root 底下");
                var mech = root.GetComponentInChildren<PantographMechanism>(true);
                Assert.IsNotNull(mech);
                Assert.IsTrue(mech.transform.IsChildOf(root.Find("PAN_Mechanism")));
                foreach (var r in Object.FindObjectsOfType<Renderer>(true).Where(r => r.gameObject.scene == scene && r.transform.IsChildOf(mech.transform)))
                    Assert.IsTrue(r.transform.IsChildOf(root));
                Assert.IsFalse(scene.GetRootGameObjects().Any(g => g.GetComponent<PantographMechanism>() != null), "集電弓不能留在 PAN_Root 外面");
            });
        }

        [Test]
        public void PanRootOrigin_IsLoweredBaseUndersideCenter()
        {
            StationTests.WithScene(scene =>
            {
                var root = PanAssy.Root(scene);
                var mech = root.GetComponentInChildren<PantographMechanism>(true);
                float lift = mech.Lift;
                mech.SetLift(0f);
                var b = mech.transform.Find("Base").GetComponent<Renderer>().bounds;
                mech.SetLift(lift);
                Assert.Less(Vector3.Distance(root.position, new Vector3(b.center.x, b.min.y, b.center.z)), 1e-3f);
                Assert.Less(Quaternion.Angle(root.rotation, Quaternion.identity), 0.01f, "+Z 朝肘側、+Y 朝上");
            });
        }
    }

    /// <summary>R1.5a a1：移動前後所有物件的名稱與 ID 清單完全相同。</summary>
    public class PanAssyIdUnchangedTests
    {
        [Test]
        public void NamesAndIds_SameBeforeAndAfterMove()
        {
            StationTests.WithScene(scene =>
            {
                var root = PanAssy.Root(scene);
                var pos = root.position; var rot = root.rotation;
                var before = PanAssy.Ids(root);
                try
                {
                    root.SetPositionAndRotation(pos + new Vector3(3f, -2f, 1f), Quaternion.Euler(0f, 90f, 0f) * rot);
                    CollectionAssert.AreEqual(before, PanAssy.Ids(root));
                }
                finally { root.SetPositionAndRotation(pos, rot); }
                foreach (var id in new[] { "Bolt_L_1", "CarbonStrip_L", "PanHead", "LowerTarget", "GroundClampPoint" })
                    Assert.IsTrue(before.Any(x => x.StartsWith(id + "|")), id + " 名稱不能改");
            });
        }
    }
}
