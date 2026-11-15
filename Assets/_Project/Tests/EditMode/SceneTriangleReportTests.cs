using NUnit.Framework;
using PantographSim.Editor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>terry 2026-11-15（協調者第 153 題 A）：建置報告寫三角面數。方塊 12 面。</summary>
    public class SceneTriangleReportTests
    {
        static GameObject Cube(string name, Transform parent)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name; g.transform.SetParent(parent, false);
            return g;
        }

        [Test]
        public void Build_SplitsCarBodyRoofBoxesPlatform_AndTotalIncludesInactive()
        {
            var env = new GameObject("ENV");
            try
            {
                var lrv = Cube(SceneTriangleReport.LrvName, env.transform);
                Cube("Roof_HVAC_B1", lrv.transform);
                Cube("Roof_Battery_A2", lrv.transform).SetActive(false);
                var plat = new GameObject(SceneTriangleReport.PlatformName); plat.transform.SetParent(env.transform, false);
                Cube("Deck", plat.transform);
                Cube("Other", env.transform);
                Assert.AreEqual("三角面數：輕軌車體 12、車頂箱體 24、平台設備（ENV_InspectionPlatform）12、全場合計 60（含隱藏物件）",
                    SceneTriangleReport.Build(new[] { env }));
            }
            finally { Object.DestroyImmediate(env); }
        }

        [Test]
        public void Build_MissingGroups_ReportZero()
        {
            var g = new GameObject("Empty");
            try { StringAssert.Contains("輕軌車體 0、車頂箱體 0、平台設備（ENV_InspectionPlatform）0、全場合計 0", SceneTriangleReport.Build(new[] { g })); }
            finally { Object.DestroyImmediate(g); }
        }
    }
}
