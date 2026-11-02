using System.Linq;
using NUnit.Framework;
using PantographSim.Parts.Common;
using PantographSim.Parts.Mechanism;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>R1.5a a5（建模 B 第 12 節 F4）：吊點在底架上；吊起後傾斜 ≤ 2°；吊帶長度＝建置腳本依 COG 算的值。</summary>
    public class LiftPointTests
    {
        static PanLiftMath.Table Table => PanLiftMath.LoadDefault();

        static Vector3[] LiftPoints(Transform root) => Enumerable.Range(1, 4).Select(i => root.Find("PAN_LiftPoints/ip.pan_assy.lift_point_" + i).position).ToArray();

        [Test]
        public void FourLiftPoints_AreOnTheBase()
        {
            StationTests.WithScene(scene =>
            {
                var root = PanAssy.Root(scene);
                var mech = root.GetComponentInChildren<PantographMechanism>(true);
                float lift = mech.Lift;
                mech.SetLift(0f);
                var b = mech.transform.Find("Base").GetComponent<Renderer>().bounds;
                mech.SetLift(lift);
                foreach (var p in LiftPoints(root))
                {
                    Assert.That(p.x, Is.InRange(b.min.x, b.max.x), "吊點要在底架範圍內（X）");
                    Assert.That(p.z, Is.InRange(b.min.z, b.max.z), "吊點要在底架範圍內（Z）");
                    Assert.AreEqual(b.max.y, p.y, 0.01f, "吊點在底架頂面");
                }
            });
        }

        [Test]
        public void AfterLifting_TiltWithin2Deg()
        {
            StationTests.WithScene(scene =>
            {
                var root = PanAssy.Root(scene);
                var cog = root.Find("PAN_Base/COG").position;
                var hook = root.Find("PAN_LiftPoints/Hook_AttachPoint_Target").position;
                var t = Table;
                Assert.LessOrEqual(PanLiftMath.TiltDeg(hook, cog), t.maxTiltDeg, "吊鉤要在 COG 正上方");
                var pts = LiftPoints(root);
                var len = PanLiftMath.SlingLengths(cog, pts, t);
                Assert.LessOrEqual(PanLiftMath.HangPlaneTiltDeg(cog, pts, len, t), t.maxTiltDeg, "用分邊吊帶吊起後要水平");
            });
        }

        [Test]
        public void SlingLengths_EqualScriptComputed_Within1cm()
        {
            StationTests.WithScene(scene =>
            {
                var root = PanAssy.Root(scene);
                var len = PanLiftMath.SlingLengths(root.Find("PAN_Base/COG").position, LiftPoints(root), Table);
                var ids = StationTests.SceneIds(scene);
                for (int i = 1; i <= 4; i++)
                {
                    var s = ids["env.sling_" + i];
                    float actual = s.GetComponent<Renderer>().bounds.size.y;
                    Assert.AreEqual(i == 2 || i == 3 ? len.plusX : len.minusX, actual, 0.01f, "env.sling_" + i);   // a6：吊梁沿 X，2、3 號接 +X 側
                }
            });
        }
    }
}
