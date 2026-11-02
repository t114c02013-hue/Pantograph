using System.Collections.Generic;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.XR;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>傳送拋物線碰到 Walkable 就停（之前一行註解把「命中就停」蓋掉，拋物線會穿過地板一路落到原點高度）。</summary>
    public class TeleportArcTests
    {
        [Test]
        public void Arc_StopsOnWalkableFloor_NotAtOriginFloorHeight()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                floor.name = "TestWalkable";
                int layer = LayerMask.NameToLayer(SceneLayers.Walkable);
                if (layer >= 0) floor.layer = layer;
                floor.transform.position = new Vector3(0f, 0.95f, 2f);   // 頂面 y＝1.0
                floor.transform.localScale = new Vector3(4f, 0.1f, 4f);
                Physics.SyncTransforms();
                var pts = new List<Vector3>();
                var landing = TeleportArc.Trace(new Vector3(0f, 2.5f, 0f), new Vector3(0f, 0f, 3f), -5f, SceneLayers.TeleportMask, pts);
                Assert.AreEqual(1.0f, landing.y, 0.02f, "落點要在地板頂面，不能穿過去");
                Assert.AreEqual(landing, pts[pts.Count - 1], "最後一個取樣點＝命中點");
                Assert.Less(pts.Count, 90, "命中後就停止取樣");
            }
            finally { Object.DestroyImmediate(floor); }
        }

        [Test]
        public void Arc_NoCollider_FallsToFloorHeight()
        {
            var pts = new List<Vector3>();
            var landing = TeleportArc.Trace(new Vector3(500f, 2f, 500f), new Vector3(0f, 0f, 2f), 0f, SceneLayers.TeleportMask, pts);
            Assert.AreEqual(0f, landing.y, 1e-3f);
        }
    }
}
