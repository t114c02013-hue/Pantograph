using NUnit.Framework;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>降弓位置檢測與升降控制器的數值測試。</summary>
    public class PositionLiftTests
    {
        [Test]
        public void Distance_IsInCentimetres_AndToleranceInclusive()
        {
            Assert.AreEqual(5f, PositionCheckLogic.DistanceCm(new Vector3(0, 0.05f, 0), Vector3.zero), 1e-3f);
            Assert.IsTrue(PositionCheckLogic.InPosition(1.0f, 1f));
            Assert.IsTrue(PositionCheckLogic.InPosition(0.2f, 1f));
            Assert.IsFalse(PositionCheckLogic.InPosition(1.2f, 1f));
        }

        [Test]
        public void Definition_UsesV4Naming()
        {
            var d = ScriptableObject.CreateInstance<PositionCheckDefinition>();
            d.part = "pan_head"; d.purpose = "lower";
            Assert.AreEqual("part.pan_head.lower_distance_cm", d.DistanceKey);
            Assert.AreEqual("part.pan_head.lower_delta_cm", d.DeltaKey);
            Assert.AreEqual("part.pan_head.lower_tolerance_cm", d.ToleranceKey);
            Assert.AreEqual("ip.pan_head.lower_check", d.CheckMarkerId);
            Assert.AreEqual("ip.pan_head.lower_target", d.TargetMarkerId);
        }

        /// <summary>terry 2026-11-13：綠色目標框、檢查點只在教學模式、按 ▼ 那一個小動作出現（判定照常算）。</summary>
        [Test]
        public void LowerMarkers_OnlyInTutorialLiftSubAction()
        {
            const string when = "ip.pan_head.lower_check,ui.lift_controller";
            Assert.IsTrue(PositionCheckLogic.MarkerVisible("tutorial", "ui.lift_controller", when));
            Assert.IsTrue(PositionCheckLogic.MarkerVisible("tutorial", "ip.pan_head.lower_check", when), "R1 第 1 步");
            Assert.IsFalse(PositionCheckLogic.MarkerVisible("practice", "ui.lift_controller", when), "練習模式不畫");
            Assert.IsFalse(PositionCheckLogic.MarkerVisible("tutorial", "part.pan_head", when), "別的小動作不畫");
            Assert.IsTrue(PositionCheckLogic.MarkerVisible("practice", "", ""), "沒設定＝照舊一律畫");
            Assert.AreEqual(when, UnityEditor.AssetDatabase.LoadAssetAtPath<PositionCheckDefinition>("Assets/_Project/Modules/Parts/_Common/Data/PC_PanHeadLower.asset").showWhenSubTarget, "資料有設定");
        }

        static void Hold(ref float v, ref float vel, int input, float seconds, LiftSettingsValues s, float distanceCm = 999f)
        {
            const float dt = 0.02f;
            for (float t = 0; t < seconds; t += dt) LiftLogic.Step(ref v, ref vel, input, dt, s, distanceCm);
        }

        [Test]
        public void HoldDown_FullTravelAboutFifteenSeconds()
        {
            var s = LiftSettingsValues.Default;
            float v = 1f, vel = 0f;
            Hold(ref v, ref vel, -1, 7.5f, s);
            Assert.That(v, Is.InRange(0.5f, 0.54f), "按住下降 7.5 秒約到一半（含加速時間）");
            Hold(ref v, ref vel, -1, 8f, s);
            Assert.AreEqual(0f, v, 1e-4f, "約 15 秒降到底");
            Assert.AreEqual(0f, vel, 1e-4f);
        }

        [Test]
        public void Release_StopsAtCurrentHeight()
        {
            var s = LiftSettingsValues.Default;
            float v = 1f, vel = 0f;
            Hold(ref v, ref vel, -1, 3f, s);
            Hold(ref v, ref vel, 0, 2f, s);
            float stopped = v;
            Hold(ref v, ref vel, 0, 3f, s);
            Assert.AreEqual(stopped, v, 1e-5f, "放開後停住不動");
            Assert.Greater(v, 0.75f);
            Assert.Less(v, 0.85f);
        }

        [Test]
        public void SlowZone_LimitsSpeedToFiveCmPerSecond()
        {
            var s = LiftSettingsValues.Default;   // 全程 183 cm、20 cm 內 5 cm/s
            float v = 0.1f, vel = 0f;
            Hold(ref v, ref vel, -1, 1f, s, 15f);
            float before = v;
            Hold(ref v, ref vel, -1, 1f, s, 15f);
            float cmPerSec = (before - v) * s.travelCm;
            Assert.AreEqual(5f, cmPerSec, 0.3f);
        }

        [Test]
        public void LiftCommand_UpIgnoredWhenCylinderPowerOff()
        {
            Assert.AreEqual(-1, PantographSim.Parts.Mechanism.LiftDriver.InputOf("Down", true));
            Assert.AreEqual(1, PantographSim.Parts.Mechanism.LiftDriver.InputOf("Up", false));
            Assert.AreEqual(0, PantographSim.Parts.Mechanism.LiftDriver.InputOf("Up", true), "升弓氣缸電源已關：不能上升");
            Assert.AreEqual(0, PantographSim.Parts.Mechanism.LiftDriver.InputOf("Stop", false));
            Assert.AreEqual(0, PantographSim.Parts.Mechanism.LiftDriver.InputOf("", false));
        }

        [Test]
        public void ChecklistEval_LessOrEqual_NumberOrKey()
        {
            var s = new PantographSim.Core.SimState(new PantographSim.Core.EventBus());
            s.SetFloat("d", 0.8f); s.SetFloat("tol", 1f);
            Assert.IsTrue(PantographSim.Core.ChecklistEval.Done(s, "d<=1"));
            Assert.IsTrue(PantographSim.Core.ChecklistEval.Done(s, "d<=tol"));
            s.SetFloat("d", 1.3f);
            Assert.IsFalse(PantographSim.Core.ChecklistEval.Done(s, "d<=tol"));
            Assert.IsFalse(PantographSim.Core.ChecklistEval.Done(s, "missing<=1"));
            s.Set("st", "NewMounted"); s.SetFloat("d", 0.3f);
            Assert.IsTrue(PantographSim.Core.ChecklistEval.Done(s, "st=NewMounted && d<=0.5"));
            s.Set("st", "Carried");
            Assert.IsFalse(PantographSim.Core.ChecklistEval.Done(s, "st=NewMounted && d<=0.5"), "距離到了但還拿在手上，不算裝好");
        }

        [Test]
        public void HoldUp_RaisesAndClampsAtTop()
        {
            var s = LiftSettingsValues.Default;
            float v = 0f, vel = 0f;
            Hold(ref v, ref vel, 1, 20f, s);
            Assert.AreEqual(1f, v, 1e-4f);
        }
    }
}
