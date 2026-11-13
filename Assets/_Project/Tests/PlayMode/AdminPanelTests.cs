using System.Collections;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.UI;
using PantographSim.XR;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PantographSim.Tests
{
    /// <summary>後台控制面板：選第 N 步 → 系統代做前面的步驟 → 交接時停在第 N 步、手上沒工具；頭盔相機看不到面板。</summary>
    public class AdminPanelTests
    {
        const string Scene = "SC_CarbonStripSlice";
        const int LongRun = 420000;

        [TearDown]
        public void TearDown()
        {
            System.Environment.SetEnvironmentVariable("PANTO_DESKTOP", null);
            XrStartupSequencer.ProbeOverride = null;
            XrModeSwitch.SimulateRequested = false;
        }

        static IEnumerator LoadDesktop()
        {
            System.Environment.SetEnvironmentVariable("PANTO_DESKTOP", "1");
            SceneManager.LoadScene(Scene);
            for (int i = 0; i < 10; i++) yield return null;
        }

        static void Jump(string mode, string step) =>
            Sim.Bus.Publish(new ActionRequested { targetId = "app.jump", text = mode + ":" + step, time = SimClock.Now });

        static IEnumerator WaitHandOff(string step, float seconds)
        {
            yield return null;
            for (float t = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t < seconds;)
            {
                if (Sim.Ready && !Sim.State.GetBool(SimIds.AutoRunActive) && Sim.State.Get("procedure.current") == step && Sim.State.GetBool("training.running")) break;
                yield return null;
            }
            Assert.AreEqual("", Sim.State.Get(SimIds.AutoRunStatus));
            Assert.AreEqual(step, Sim.State.Get("procedure.current"), "交接時停在選的步驟");
        }

        static void AssertHandedOff(string step)
        {
            Assert.IsFalse(Sim.State.GetBool(SimIds.AutoRunActive), "遮罩收掉");
            Assert.AreEqual("", Sim.State.Get(SimIds.ActiveTool), "手上沒有工具");
            var ids = AdminPanel.Parse(Sim.State.Get(SimIds.StepsList)).Select(x => x.id).ToList();
            foreach (var id in ids.Take(ids.IndexOf(step)))
                Assert.IsTrue(Sim.State.GetBool("step." + id + ".done"), id + " 要先做完");
            Assert.IsFalse(Sim.State.GetBool("step." + step + ".done"), "這一步留給人做");
            Assert.AreEqual(0f, Sim.State.GetFloat("score.minor") + Sim.State.GetFloat("score.major") + Sim.State.GetFloat("score.critical"), "代做不留錯誤");
            Assert.AreEqual(1f, Sim.State.GetFloat(SimIds.AutoRunProgress), "進度條滿格");
            StringAssert.StartsWith("✔", Sim.State.Get(SimIds.AutoRunProgressText), "後台面板顯示已到");
        }

        /// <summary>terry 2026-11-13：代做要看得到進度，也要能停。</summary>
        [UnityTest, Timeout(LongRun)]
        public IEnumerator Progress_Advances_ThenStopButtonStops()
        {
            yield return LoadDesktop();
            Jump(AdminPanel.Tutorial, "STP_WC_03_RemoveGround");
            for (float t = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t < 120f && Sim.State.GetFloat(SimIds.AutoRunProgress) <= 0f;) yield return null;
            Assert.Greater(Sim.State.GetFloat(SimIds.AutoRunProgress), 0f, "做完第一步進度就往前");
            StringAssert.StartsWith("代做中", Sim.State.Get(SimIds.AutoRunProgressText));
            Sim.Bus.Publish(new ActionRequested { targetId = "app.jump_stop", time = SimClock.Now });
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsFalse(Sim.State.GetBool(SimIds.AutoRunActive), "停下來、遮罩收掉");
            StringAssert.StartsWith("■", Sim.State.Get(SimIds.AutoRunStatus), "面板顯示停在哪一步");
            Assert.AreEqual("Stop", Sim.State.Get(SimIds.LiftCmd, "Stop"), "升降沒有一直按著");
        }

        [UnityTest, Timeout(LongRun)]
        public IEnumerator Practice_LastHumanStep_EverythingBeforeIsDone()
        {
            yield return LoadDesktop();
            var list = AdminPanel.Parse(Sim.State.Get(SimIds.StepsList));
            Assert.IsTrue(list.Exists(x => x.id == "STP_CSR_06_Loosen_L") && list.Exists(x => x.id == "STP_CSR_06_Loosen_R"), "兩側都選得到");
            Assert.IsFalse(list.Exists(x => x.id == "STP_SI_02_LockOut"), "預設完成的步驟不列");
            const string step = "STP_WC_03_RemoveGround";
            Jump(AdminPanel.Practice, step);
            yield return WaitHandOff(step, 400f);
            AssertHandedOff(step);
            Assert.AreEqual("practice", Sim.State.Get("training.mode"));
        }

        /// <summary>CSR_01 開作業單就過：腳本不能跑過頭。訓練中再選別步：重新載入後從頭代做。</summary>
        [UnityTest, Timeout(LongRun)]
        public IEnumerator Tutorial_StopsExactlyAtStep_ThenJumpAgainReloads()
        {
            yield return LoadDesktop();
            const string first = "STP_CSR_01_CheckIsolation", second = "STP_SI_04_VoltageTest";
            Jump(AdminPanel.Tutorial, first);
            yield return WaitHandOff(first, 200f);
            AssertHandedOff(first);
            Assert.AreEqual("tutorial", Sim.State.Get("training.mode"));
            var before = Sim.State;
            Jump(AdminPanel.Practice, second);
            for (float t = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t < 20f && Sim.State == before;) yield return null;
            Assert.AreNotSame(before, Sim.State, "場景重新載入");
            for (int i = 0; i < 60; i++) yield return null;
            yield return WaitHandOff(second, 120f);
            AssertHandedOff(second);
        }

        static Color32[] Render(Camera cam, string png = null)
        {
            var rt = new RenderTexture(256, 256, 24);
            var keep = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = keep;
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(256, 256, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            var pixels = tex.GetPixels32();
            if (png != null) System.IO.File.WriteAllBytes(png, tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
            return pixels;
        }

        static float Brightness(Color32[] p) => (float)p.Average(c => c.r + c.g + c.b) / 3f;

        [UnityTest, Timeout(LongRun)]
        public IEnumerator Headset_NoPanelInEye_MaskCoversUntilHandOff()
        {
            XrStartupSequencer.ProbeOverride = () => (true, 1.63f);
            XrModeSwitch.SimulateRequested = true;
            SceneManager.LoadScene(Scene);
            for (int i = 0; i < 10; i++) yield return null;
            for (float t = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t < 10f && !Sim.State.GetBool(XrStartupSequencer.ReadyKey);) yield return null;
            Assert.IsTrue(InputMode.XrActive);
            var eye = Object.FindObjectOfType<XrRig>(true).Eye;
            var panel = Object.FindObjectOfType<AdminPanel>(true);
            Assert.IsNotNull(panel, "VR 模式下面板還在（畫在電腦螢幕）");
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, panel.Canvas.renderMode, "只畫在電腦螢幕");
            Assert.IsNull(panel.Canvas.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>(), "控制器射線點不到");
            yield return null;
            var with = Render(eye);
            panel.Canvas.enabled = false;
            var without = Render(eye);
            panel.Canvas.enabled = true;
            Assert.Less(with.Zip(without, (a, b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b)).Average(), 2.0, "面板開關，頭盔畫面一樣");

            const string step = "STP_SI_04_VoltageTest";
            Jump(AdminPanel.Tutorial, step);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsTrue(Sim.State.GetBool(SimIds.AutoRunActive));
            StringAssert.Contains("第 4 步", Sim.State.Get(SimIds.AutoRunText));
            yield return null;
            float masked = Brightness(Render(eye, "Logs/rig_autorun_mask.png"));
            Assert.Less(masked, 12f, "代做期間頭盔裡看不到場景");
            yield return WaitHandOff(step, 120f);
            AssertHandedOff(step);
            for (float t = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t < 2f;) yield return null;
            yield return null;
            Assert.Greater(Brightness(Render(eye, "Logs/rig_autorun_after.png")), masked + 10f, "交接後淡回");
        }
    }
}
