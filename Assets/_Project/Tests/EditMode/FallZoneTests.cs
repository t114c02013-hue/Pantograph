using System.Linq;
using NUnit.Framework;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.XR;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>terry 134B／185（2026-11-15）：車頂墜落區 FallZoneLogic、頭部邊界帶、提示不重複。</summary>
    public class FallZoneTests
    {
        static BoundaryDef Def(bool platform = true)
        {
            var d = ScriptableObject.CreateInstance<BoundaryDef>();
            var (_, _, plats) = BoundaryLogic.RoofFallStrips(R1Data.RoofScene, d);
            d.platformRects = platform ? plats : new Rect[0];
            d.roofWalkHalfWidth = 0.8f; d.roofBaseWalkHalfWidth = 1.0f; d.roofBaseWalkZMin = -0.75f; d.roofBaseWalkZMax = 1.0f;
            return d;
        }

        [Test]
        public void Classify_BoundaryValues_BothSides()   // 1
        {
            var d = Def();
            foreach (float s in new[] { -1f, 1f })
            {
                Assert.AreEqual(FallZone.Normal, FallZoneLogic.Classify(s * 1.025f, 0f, d));
                Assert.AreEqual(FallZone.Warn, FallZoneLogic.Classify(s * 1.026f, 0f, d));
                Assert.AreEqual(FallZone.Warn, FallZoneLogic.Classify(s * 1.325f, 0f, d));
                Assert.AreEqual(FallZone.OverPlatform, FallZoneLogic.Classify(s * 1.326f, 0f, d), "平台範圍內");
                Assert.AreEqual(FallZone.FallZone, FallZoneLogic.Classify(s * 1.326f, 5f, d), "平台範圍外");
                Assert.AreEqual(FallZone.FallZone, FallZoneLogic.Classify(s * 1.525f, 5f, d));
                Assert.AreEqual(FallZone.Beyond, FallZoneLogic.Classify(s * 1.526f, 5f, d));
            }
            Assert.AreEqual(FallZone.OverPlatform, FallZoneLogic.Classify(-1.4f, 0f, d), "車和踏板之間的間隙算平台");
        }

        [Test]
        public void Dwell_WritesOnceAfterOneSecond_ResetsWhenLeaving()   // 2
        {
            var f = new FallZoneLogic(Def(false));
            float t = 10f;
            bool Run(float seconds, float x) { bool w = false; for (int i = 0; i < Mathf.RoundToInt(seconds / 0.01f); i++) { t += 0.01f; w |= f.Tick(t, 0.01f, x, 0f, true, "S1", false).write; } return w; }
            Assert.IsFalse(Run(0.6f, 1.4f));
            Assert.IsFalse(Run(0.2f, 0f), "離開");
            Assert.IsFalse(Run(0.6f, 1.4f), "回來計時重算（0.6 ＜ 1）");
            Assert.IsFalse(Run(0.38f, 1.4f), "0.98 s");
            Assert.IsTrue(Run(0.03f, 1.4f), "到 1.0 s 寫一次");
            Assert.IsFalse(Run(5f, 1.4f), "5 秒還是一次");
        }

        [Test]
        public void SameStepOnce_StepChangeReopens()   // 3
        {
            var f = new FallZoneLogic(Def(false));
            Assert.IsTrue(Enumerable.Range(0, 110).Select(i => f.Tick(i * 0.01f, 0.01f, 1.4f, 0f, true, "S1", false).write).ToList().Any(w => w));
            var beyond = f.Tick(5f, 0.01f, 1.6f, 0f, true, "S1", false);
            Assert.IsFalse(beyond.write, "同一步不再寫"); Assert.IsTrue(beyond.pushBack);
            Assert.IsTrue(f.Tick(10f, 0.01f, 1.6f, 0f, true, "S2", false).write, "換步後可以再寫");
        }

        [Test]
        public void Beyond_WritesImmediately_PushBackToWalkEdge()   // 4
        {
            var d = Def(false);
            var r = new FallZoneLogic(d).Tick(1f, 0.01f, -1.6f, 0.5f, true, "S1", false);
            Assert.IsTrue(r.write); Assert.IsTrue(r.pushBack);
            var head = new Vector3(-1.6f, 1.2f, 0.5f);
            var moved = head + FallZoneLogic.PushBack(head, d);
            Assert.AreEqual(-d.RoofWalkLimitAt(0.5f), moved.x, 1e-5f); Assert.AreEqual(0.5f, moved.z, 1e-6f);
            head = new Vector3(1.6f, 1.2f, 3f);
            Assert.AreEqual(0.8f, (head + FallZoneLogic.PushBack(head, d)).x, 1e-5f, "底架旁以外是 0.8");
        }

        [Test]
        public void NotJudged_Grace_Suppressed_NotOnRoof()   // 5
        {
            var f = new FallZoneLogic(Def(false));
            f.Teleported(10f);
            Assert.IsFalse(f.Tick(10.49f, 0.01f, 1.6f, 0f, true, "S1", false).write, "傳送後 0.49 s");
            Assert.IsFalse(f.Tick(11f, 0.01f, 1.6f, 0f, true, "S1", true).write, "代做／重播／桌機");
            var off = f.Tick(12f, 0.01f, 1.6f, 0f, false, "S1", false);
            Assert.IsFalse(off.write); Assert.IsFalse(off.pushBack); Assert.IsNull(off.toast);
            for (int i = 0; i < 200; i++) Assert.IsFalse(f.Tick(13f + i * 0.01f, 0.01f, 1.4f, 0f, false, "S1", false).write, "不在車頂不計時");
        }

        [Test]
        public void Strips_PlatformSegmentTo166_NoFallSegmentNow()   // 7
        {
            var b = ScriptableObject.CreateInstance<BoundaryDef>();
            var (strips, fall, _) = BoundaryLogic.RoofFallStrips(R1Data.RoofScene, b);
            Assert.AreEqual(1.66f, strips.Max(r => r.xMax), 1e-4f);
            Assert.AreEqual(-1.66f, strips.Min(r => r.xMin), 1e-4f);
            Assert.IsEmpty(fall, "現在整段都有平台");
            Assert.AreEqual(R1Data.RoofScene.roofSectionZMin + b.inset, strips.Min(r => r.yMin), 1e-4f, "車頂兩端不變");
        }

        [Test]
        public void DangerToast_OnlyOffRoof()   // B
        {
            Assert.IsFalse(XrLocomotion.ShowDangerToast(true, true), "車頂上交給墜落區監看，不重複");
            Assert.IsTrue(XrLocomotion.ShowDangerToast(true, false), "非車頂照舊");
            Assert.IsFalse(XrLocomotion.ShowDangerToast(false, false));
        }
    }
}
