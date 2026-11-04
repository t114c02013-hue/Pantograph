using System.Collections;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.XR;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PantographSim.Tests
{
    /// <summary>2026-11-04 試玩修正 第 0 點：VR 開場時序（XrStartupSequencer）。用模擬的追蹤（ProbeOverride）跑真場景。</summary>
    public class XrStartupTests
    {
        const string Scene = "SC_CarbonStripSlice";
        bool tracked; float headY;

        [SetUp]
        public void SetUp()
        {
            tracked = false; headY = 0f;
            XrStartupSequencer.ProbeOverride = () => (tracked, headY);
            XrModeSwitch.SimulateRequested = true;
        }

        [TearDown]
        public void TearDown()
        {
            XrStartupSequencer.ProbeOverride = null;
            XrModeSwitch.SimulateRequested = false;
        }

        static XrRig Rig => Object.FindObjectOfType<XrRig>(true);
        static Vector3 Point(string id) => Object.FindObjectsOfType<XrTeleportPoint>(true).First(p => p.PointId == id).transform.position;
        static PanelLayout Layout => Resources.Load<PanelLayout>(PanelLayout.ResourceName);

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        IEnumerator Boot(Vector3 headInRoom, string startStation = null)
        {
            SceneManager.LoadScene(Scene);
            for (int i = 0; i < 10; i++) yield return null;
            if (startStation != null) Sim.State.Set(SimIds.CourseStartStation, startStation);
            Rig.Head.localPosition = headInRoom;
            headY = headInRoom.y;
        }

        IEnumerator WaitReady()
        {
            for (float t = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t < Layout.startupTimeoutSec + 3f && !Sim.State.GetBool(XrStartupSequencer.ReadyKey);) yield return null;
            Assert.IsTrue(Sim.State.GetBool(XrStartupSequencer.ReadyKey), "開場要走完");
            yield return null;
        }

        static Transform Dialog() { Assert.IsTrue(Sim.Registry.TryGetTransform("xr.panel.dialog", out var t), "對話框要登記"); return t; }

        [UnityTest]
        public IEnumerator TrackingLate_NothingPlacedBefore_HeadAtStartAndCardInFrontAfter()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f));
            for (float t = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t < 2f;) yield return null;
            Assert.IsFalse(InputMode.XrActive, "追蹤有效之前不切 VR 模式");
            Assert.IsFalse(Sim.State.GetBool(XrStartupSequencer.ReadyKey));
            Assert.AreEqual("", Sim.State.Get("xr.point"), "追蹤有效之前不擺人");
            tracked = true;
            yield return WaitReady();
            Assert.IsTrue(InputMode.XrActive);
            Assert.AreEqual("tp.platform.lift", Sim.State.Get("xr.point"));
            Assert.Less(Flat(Rig.Head.position, Point("tp.platform.lift")), 0.05f, "頭在開場站點中心");
            Assert.AreEqual(Layout.taskCardDistance, Flat(Dialog().position, Rig.Head.position), 0.05f, "任務卡在頭前");
            Assert.AreEqual(1.0f, Layout.taskCardDistance, 1e-4f, "8.5：眼前 1.0 m");
            Assert.AreEqual(0.15f, Layout.taskCardBelowEye, 1e-4f, "8.5：眼下 0.15 m");
            var to = Dialog().position - Rig.Head.position; to.y = 0f;
            var f = Rig.Head.forward; f.y = 0f;
            Assert.Less(Vector3.Angle(f, to), 3f, "任務卡在視線正前方");
            Assert.AreEqual(Rig.Head.position.y - Layout.taskCardBelowEye, Dialog().position.y, 0.05f, "比眼睛低 0.15 m");

            var before = Dialog().position;
            Rig.Head.Rotate(0f, 90f, 0f);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.Less((Dialog().position - before).magnitude, 1e-3f, "頭轉了，任務卡固定不動");
        }

        [UnityTest]
        public IEnumerator HeadOffCentre_StillLandsOnStation()
        {
            yield return Boot(new Vector3(0.8f, 1.6f, 0.5f));
            tracked = true;
            yield return WaitReady();
            Assert.Less(Flat(Rig.Head.position, Point("tp.platform.lift")), 0.05f, "扣掉頭離房間中心的偏移");
        }

        [UnityTest]
        public IEnumerator StartStationFromData()
        {
            yield return Boot(new Vector3(0f, 1.6f, 0f), "tp.platform.rack");
            tracked = true;
            yield return WaitReady();
            Assert.AreEqual("tp.platform.rack", Sim.State.Get("xr.point"));
            Assert.Less(Flat(Rig.Head.position, Point("tp.platform.rack")), 0.05f);
        }

        [UnityTest]
        public IEnumerator Timeout_UsesFallbackHeight_WithWarning_ThenReplacesWhenTrackingArrives()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("頭盔追蹤沒有生效"));
            yield return Boot(Vector3.zero);
            yield return WaitReady();   // 等滿 startupTimeoutSec
            for (int i = 0; i < 5; i++) yield return null;   // 開場教學的對話框打開之後才擺回預設眼高
            var floor = Point("tp.platform.lift").y;
            Assert.AreEqual(floor + Layout.fallbackHeadHeight - Layout.taskCardBelowEye, Dialog().position.y, 0.05f, "用預設眼高擺");

            Rig.Head.localPosition = new Vector3(0.5f, 1.7f, 0f); headY = 1.7f; tracked = true;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.Less(Flat(Rig.Head.position, Point("tp.platform.lift")), 0.05f, "追蹤回來後重新擺人");
            Assert.AreEqual(Rig.Head.position.y - Layout.taskCardBelowEye, Dialog().position.y, 0.05f, "追蹤回來後重新擺任務卡");
        }
    }
}
