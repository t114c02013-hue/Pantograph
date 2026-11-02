using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Parts.Mechanism;

namespace PantographSim.Tests
{
    /// <summary>
    /// T7 升降：SI_02 之前 ▲▼ 都能用；上鎖、斷電、車頂有人時 ▲ 被安全聯鎖擋住並說明原因；預設完成步驟的狀態等流程走到才寫入。
    /// 桌面（升降面板按鈕、PageUp/PageDown）與 VR（升降面板、右扳機按住）都經過同一個入口 LiftControl.Send／Release，這裡直接用它。
    /// </summary>
    public class LiftTests
    {
        /// <summary>升降機構的數值模擬（與 LiftDriver 相同：讀 ctrl.lift.cmd 與安全聯鎖，LiftLogic 積分；距離＝高度×全程）。</summary>
        class LiftSim
        {
            public float value = 1f, velocity;
            readonly ISimState s;
            readonly LiftSettingsValues set = LiftSettingsValues.Default;
            public LiftSim(ISimState state) { s = state; }

            public void Run(float seconds)
            {
                const float dt = 0.02f;
                for (float t = 0; t < seconds; t += dt)
                {
                    int input = LiftDriver.InputOf(s.Get(SimIds.LiftCmd), LiftControl.UpLocked(s));
                    LiftLogic.Step(ref value, ref velocity, input, dt, set, s.GetFloat(SimIds.PanHeadLowerDistance, 999f));
                    float d = value * set.travelCm;
                    if (System.Math.Abs(s.GetFloat(SimIds.PanHeadLowerDistance) - d) >= 0.05f) s.SetFloat(SimIds.PanHeadLowerDistance, d);
                }
            }

            public void Hold(string cmd, float seconds) { LiftControl.Send(s, cmd); Run(seconds); LiftControl.Send(s, "Stop"); Run(1.5f); }
        }

        [Test]
        public void Opening_DownHalf_UpToTop_DownToGreen_AllMove_ThenInPosition()
        {
            var sim = new SliceDryRunTests.SliceSim();   // 開場：升弓、有電，在 SI_01
            var s = sim.s;
            Assert.AreEqual("STP_SI_01_ConfirmLowered", sim.Current);
            Assert.IsNull(LiftControl.UpBlockReason(s), "SI_02 之前 ▲ 可以用");
            var lift = new LiftSim(s);

            lift.Hold("Down", 7.5f);
            Assert.That(lift.value, Is.InRange(0.4f, 0.6f), "降一半");
            float half = lift.value;
            lift.Hold("Up", 12f);
            Assert.AreEqual(1f, lift.value, 1e-3f, "再升回頂（到頂就停）");
            Assert.Greater(lift.value, half);
            LiftControl.Send(s, "Down");
            for (int i = 0; i < 2000 && s.GetFloat(SimIds.PanHeadLowerDistance) > 0.8f; i++) lift.Run(0.02f);
            LiftControl.Release(s);   // 放開：最新距離與 Stop 同一批
            Assert.LessOrEqual(s.GetFloat(SimIds.PanHeadLowerDistance), 1.0f);
            Assert.AreEqual(1f, s.GetFloat("procedure.sub_index"), "SI_01 a：判定到位");
        }

        [Test]
        public void PresetSteps_StatesAbsentUntilFlowReachesThem()
        {
            var sim = new SliceDryRunTests.SliceSim();
            var s = sim.s;
            foreach (var key in new[] { "lockout.breaker", "lockout.raise_switch", "backpack.lockout_key", "harness.roof", "roof.adjacent_check" })
                Assert.IsFalse(s.Has(key), key + " 在 SI_01 時不應存在（預設完成步驟還沒走到）");
            Assert.IsFalse(s.GetBool("step.STP_SI_02_LockOut.done"));
            Assert.AreNotEqual("off", s.Get("cab.cylinder_power"));

            sim.RunUntil("STP_SI_04_VoltageTest");   // 走過 SI_02、SI_03
            Assert.AreEqual("locked", s.Get("lockout.raise_switch"));
            Assert.AreEqual("off", s.Get("cab.cylinder_power"));
            Assert.IsTrue(s.GetBool("step.STP_SI_02_LockOut.done") && s.GetBool("step.STP_SI_03_CylinderPowerOff.done"));
            Assert.IsFalse(s.Has("harness.roof"), "SI_06 還沒走到");
        }

        [Test]
        public void AfterLockout_UpHasNoEffect_WithReason_DownStillWorks()
        {
            var sim = new SliceDryRunTests.SliceSim();
            sim.RunUntil("STP_SI_04_VoltageTest");
            var s = sim.s;
            Assert.AreEqual("升弓開關已上鎖，不能升弓", LiftControl.UpBlockReason(s));
            var lift = new LiftSim(s) { value = 0.3f };
            lift.Hold("Up", 3f);
            Assert.AreEqual(0.3f, lift.value, 1e-4f, "上鎖後 ▲ 不動");
            lift.Hold("Down", 1f);
            Assert.Less(lift.value, 0.3f, "▼ 照現行規則");
        }

        [Test]
        public void PowerOff_UpHasNoEffect_WithReason()
        {
            var s = new SimState(new EventBus());
            s.Set("cab.cylinder_power", "off");
            Assert.AreEqual("電動缸電源已關閉，不能升弓", LiftControl.UpBlockReason(s));
            var lift = new LiftSim(s) { value = 0.5f };
            lift.Hold("Up", 3f);
            Assert.AreEqual(0.5f, lift.value, 1e-4f);
            s.Set("cab.cylinder_power", "on");   // WC_04 恢復電源
            Assert.IsNull(LiftControl.UpBlockReason(s));
            lift.Hold("Up", 2f);
            Assert.Greater(lift.value, 0.5f, "恢復電源後可以升");
        }

        [Test]
        public void RoofOccupied_UpBlocked_WithReason()
        {
            var s = new SimState(new EventBus());
            s.SetBool(LiftControl.RoofOpenKey, true);
            Assert.AreEqual("車頂有人作業，不能升弓", LiftControl.UpBlockReason(s));
            Assert.AreEqual(0, LiftDriver.InputOf("Up", LiftControl.UpLocked(s)));
            Assert.AreEqual(-1, LiftDriver.InputOf("Down", LiftControl.UpLocked(s)));
        }

        [Test]
        public void EndOfProcedure_WC04_UnlocksAndRestoresPower_UpWorksAgain()
        {
            var sim = new SliceDryRunTests.SliceSim();
            sim.RunUntil(null);
            Assert.IsTrue(sim.completed);
            Assert.AreEqual("open", sim.s.Get("lockout.raise_switch"));
            Assert.AreEqual("on", sim.s.Get("cab.cylinder_power"));
            Assert.IsNull(LiftControl.UpBlockReason(sim.s), "WC_04 解鎖、恢復電源後可以升");
        }
    }
}
