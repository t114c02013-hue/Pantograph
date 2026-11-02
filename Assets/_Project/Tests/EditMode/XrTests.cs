using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.UI;
using PantographSim.XR;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace PantographSim.Tests
{
    /// <summary>VR 第一批：按鍵對照表、資訊卡、世界空間介面、沒有 OnGUI、1 unit = 1 m。</summary>
    public class XrTests
    {
        const string ScenePath = "Assets/_Project/Content/Scenes/SC_CarbonStripSlice.unity";

        static XRInputMapper NewMapper() => new XRInputMapper(XrBindingTable.LoadDefault());
        static XrContext Hand(string tool = "", bool dialog = false, bool carrying = false, string size = "10") =>
            new XrContext { heldTool = tool, dialogOpen = dialog, carrying = carrying, socketSize = size };

        // ---------- 按鍵對照表 ----------
        [Test]
        public void Bindings_AreData_WithAllButtons()
        {
            var t = XrBindingTable.LoadDefault();
            Assert.AreEqual("Use", t.CommandOf("RightTrigger"));
            Assert.AreEqual("Grab", t.CommandOf("RightGrip"));
            Assert.AreEqual("ResetTool", t.CommandOf("A"));
            Assert.AreEqual("FocusTarget", t.CommandOf("B"));
            Assert.AreEqual("ToggleWorkOrder", t.CommandOf("X"));
            Assert.AreEqual("ToggleHelp", t.CommandOf("Y"));
            Assert.AreEqual("Menu", t.CommandOf("Menu"));
            Assert.AreEqual(45f, t.turnDegrees);
            Assert.IsTrue(t.buttons.All(b => b.paths.Length > 0), "每個按鍵都要有 Input System 綁定路徑");
        }

        [Test]
        public void Trigger_PressAndRelease_IsUse()
        {
            var m = NewMapper();
            var down = m.Map(new XrFrame { rightTrigger = true }, Hand(), 0f);
            var up = m.Map(new XrFrame(), Hand(), 0.1f);
            Assert.AreEqual(1, down.Count); Assert.AreEqual("Use", down[0].command); Assert.IsTrue(down[0].pressed);
            Assert.AreEqual(1, up.Count); Assert.AreEqual("Use", up[0].command); Assert.IsFalse(up[0].pressed);
            Assert.AreEqual(0, m.Map(new XrFrame(), Hand(), 0.2f).Count, "沒有變化就沒有指令");
        }

        [Test]
        public void A_ResetsOnlyWithToolInHand_AndNoDialog()
        {
            var m = NewMapper();
            Assert.IsEmpty(m.Map(new XrFrame { a = true }, Hand(), 0f), "空手時 A 不作用");
            m.Map(new XrFrame(), Hand(), 0.1f);
            Assert.IsEmpty(m.Map(new XrFrame { a = true }, Hand(SimIds.ToolCaliper, dialog: true), 0.2f), "彈窗開著時 A 不作用");
            m.Map(new XrFrame(), Hand(), 0.3f);
            var r = m.Map(new XrFrame { a = true }, Hand(SimIds.ToolCaliper), 0.4f);
            Assert.AreEqual("ResetTool", r.Single().command);
        }

        [Test]
        public void Buttons_B_X_Y_Menu()
        {
            var m = NewMapper();
            Assert.AreEqual("FocusTarget", m.Map(new XrFrame { b = true }, Hand(), 0f).Single().command);
            Assert.AreEqual("ToggleWorkOrder", m.Map(new XrFrame { x = true }, Hand(), 0.1f).Single().command);
            Assert.AreEqual("ToggleHelp", m.Map(new XrFrame { y = true }, Hand(), 0.2f).Single().command);
            Assert.AreEqual("Menu", m.Map(new XrFrame { menu = true }, Hand(), 0.3f).Single().command);
        }

        [Test]
        public void RightStick_AnyToolOrCarrying_NoToolAdjust()
        {
            // R1：右搖桿讓給平滑移動（T6），工具數值改用手調（T2-a）；原本的 RightStick_Torque_*、RightStick_Socket_*、RightStick_CarriedStrip_* 改寫成這個測試
            var m = NewMapper();
            float t = 0f;
            foreach (var ctx in new[] { Hand(SimIds.ToolTorque), Hand(SimIds.ToolSocket), Hand(SimIds.ToolCaliper), Hand(carrying: true), Hand() })
                foreach (var v in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
                {
                    Assert.IsFalse(m.Map(new XrFrame { rightStick = v }, ctx, t += 0.5f).Any(c => c.command == "ToolAdjust"), $"{ctx.heldTool} {v}");
                    m.Map(new XrFrame(), ctx, t += 0.5f);
                }
        }
        [Test]
        public void LeftStick_Snap45_AndTeleportAimOnlyOnRelease()
        {
            var m = NewMapper();
            var turn = m.Map(new XrFrame { leftStick = Vector2.left }, Hand(), 0f).Single();
            Assert.AreEqual("TurnLeft", turn.command); Assert.AreEqual(-45f, turn.degrees);
            Assert.IsEmpty(m.Map(new XrFrame { leftStick = Vector2.left }, Hand(), 0.5f), "推著不放只轉一次");
            m.Map(new XrFrame(), Hand(), 0.6f);
            Assert.AreEqual(45f, m.Map(new XrFrame { leftStick = Vector2.right }, Hand(), 0.7f).Single().degrees);
            m.Map(new XrFrame(), Hand(), 0.8f);
            var aim = m.Map(new XrFrame { leftStick = Vector2.up }, Hand(), 0.9f).Single();
            Assert.AreEqual("TeleportAim", aim.command); Assert.IsTrue(aim.pressed);
            Assert.IsEmpty(m.Map(new XrFrame { leftStick = new Vector2(0.9f, 0.7f) }, Hand(), 1.0f), "瞄準中不轉向");
            var go = m.Map(new XrFrame(), Hand(), 1.1f).Single();
            Assert.AreEqual("TeleportAim", go.command); Assert.IsFalse(go.pressed);
        }

        // ---------- 資訊卡 ----------
        [Test]
        public void HoverDwell_Shows200ms_Hides300ms_OneAtATime()
        {
            var d = new HoverDwell();
            Assert.AreEqual("", d.Update("a", 0f));
            Assert.AreEqual("", d.Update("a", 0.15f));
            Assert.AreEqual("a", d.Update("a", 0.2f));
            Assert.AreEqual("a", d.Update("b", 0.25f), "換指 b 還沒停 0.2 秒，仍顯示 a");
            Assert.AreEqual("b", d.Update("b", 0.46f));
            Assert.AreEqual("b", d.Update("", 0.5f));
            Assert.AreEqual("b", d.Update("", 0.75f));
            Assert.AreEqual("", d.Update("", 0.81f));
        }

        [Test]
        public void InfoCard_Spare_NoCrackNoJudgement_ShowsNotMeasured()
        {
            var s = new SimState(new EventBus());
            s.Set("training.mode", "tutorial");
            s.Set("spare.required_model", "CS-1100-74A");
            s.Set("env.spare_shelf.slot_3.model", "CS-1100-74A");
            s.SetFloat("env.spare_shelf.slot_3.true", 12.0f);
            var text = InfoCardText.Build(s, null, "env.spare_shelf.slot_3");
            StringAssert.Contains("碳滑板（備品）", text);
            StringAssert.Contains("還沒量", text);
            StringAssert.Contains("作業單要求型號", text);
            StringAssert.DoesNotContain("裂", text);
            StringAssert.DoesNotContain("12.0", text, "沒量過不能顯示真實厚度");
            s.Set("training.mode", "practice");
            StringAssert.DoesNotContain("作業單要求型號", InfoCardText.Build(s, null, "env.spare_shelf.slot_3"), "考核（練習）模式不顯示要求型號");
            s.SetFloat("env.spare_shelf.slot_3.reading", 12.03f); s.SetBool("env.spare_shelf.slot_3.measured", true);
            StringAssert.Contains("12.03", InfoCardText.Build(s, null, "env.spare_shelf.slot_3"));
        }

        [Test]
        public void InfoCard_Bolt_ShowsStateButNotTorque()
        {
            var s = new SimState(new EventBus());
            s.Set("ip.carbon_strip_L.bolt_2.state", "Torqued");
            s.SetFloat("ip.carbon_strip_L.bolt_2.torque_Nm", 25.4f);
            var text = InfoCardText.Build(s, null, "ip.carbon_strip_L.bolt_2");
            StringAssert.Contains("螺栓 2", text);
            StringAssert.Contains("M8×25（13 mm）", text);
            StringAssert.Contains("已鎖到扭力", text);
            StringAssert.Contains("沒有防鬆標記", text);
            StringAssert.DoesNotContain("25.4", text);
        }

        // ---------- 介面與場景 ----------
        [Test]
        public void Runtime_HasNoOnGUI()
        {
            var offenders = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name.StartsWith("PantographSim.") && !a.GetName().Name.Contains("Editor") && !a.GetName().Name.Contains("Tests"))
                .SelectMany(a => a.GetTypes())
                .Where(t => t.GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null)
                .Select(t => t.FullName).ToList();
            Assert.IsEmpty(offenders, "執行期不能有 OnGUI：" + string.Join(", ", offenders));
        }

        [Test]
        public void SliceScene_HasNoScreenSpaceCanvas_AndHasXrRig()
        {
            Assert.IsTrue(File.Exists(ScenePath), "找不到切片場景");
            var yaml = File.ReadAllText(ScenePath);
            var canvases = Docs(yaml).Where(d => d.StartsWith("223 ")).ToList();   // 223＝Canvas
            foreach (var d in canvases) StringAssert.Contains("m_RenderMode: 2", d, "場景裡不能有 Screen Space Canvas");
            StringAssert.Contains("m_Name: XR Origin (XR Rig)", yaml);
            // R1：舊的 6 個固定傳送點改成 StationSet 的站點（R1 8 個；R1.5a 加 tp.platform.crane＝9 個）
            int stations = UnityEditor.AssetDatabase.LoadAssetAtPath<PantographSim.Data.StationSet>("Assets/_Project/Modules/Worksite/Data/StationSet.asset").stations.Length;   // R1.5a 起跟著站點資料（a3 9 個、a4 13 個）
            Assert.AreEqual(stations, Regex.Matches(yaml, @"pointId: tp\.").Count, "每個站點資料都有場景站點");
            foreach (var old in new[] { "tp.platform\n", "tp.control_panel", "tp.tool_rack", "tp.ground_terminal", "tp.roof_left", "tp.roof_right" })
                Assert.IsFalse(yaml.Contains("pointId: " + old), "舊傳送點還在：" + old.Trim());
        }

        class FakeRig : IXrRig
        {
            public Camera Eye { get; set; }
            public Transform Head => Eye.transform;
            public Transform LeftHand { get; set; }
            public Transform RightHand { get; set; }
        }

        [Test]
        public void XrLayout_AllCanvasesAreWorldSpace_WithTextAbout2cm()
        {
            var root = new GameObject("TestXrUi");
            var cam = new GameObject("Eye").AddComponent<Camera>();
            var rig = new FakeRig { Eye = cam, LeftHand = new GameObject("L").transform, RightHand = new GameObject("R").transform };
            try
            {
                var ui = root.AddComponent<SliceUI>();
                ui.Build();
                ui.UseXrLayout(rig);
                var canvases = Object.FindObjectsOfType<Canvas>(true).Where(c => c.isRootCanvas).ToList();
                Assert.IsTrue(canvases.Count >= 6);
                foreach (var c in canvases) Assert.AreEqual(RenderMode.WorldSpace, c.renderMode, c.name + " 不是 World Space");
                // 步驟面板 22 級字 ≈ 2 cm（1 unit = 1 m，canvas 比例 0.0009 m/unit）
                Assert.AreEqual(0.02f, 22 * XrPanelLayout.BoardScale, 0.003f);
            }
            finally
            {
                foreach (var es in Object.FindObjectsOfType<EventSystem>()) Object.DestroyImmediate(es.gameObject);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cam.gameObject);
                Object.DestroyImmediate(rig.LeftHand.gameObject);
                Object.DestroyImmediate(rig.RightHand.gameObject);
            }
        }

        [Test]
        public void Scale_OneUnitIsOneMeter()
        {
            var strip = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Modules/Parts/CarbonStrip/Prefabs/PF_CarbonStrip_L.prefab");
            var bolt = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Modules/Parts/CarbonStrip/Prefabs/PF_StripBolt.prefab");
            Assert.IsNotNull(strip); Assert.IsNotNull(bolt);
            var mesh = strip.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.AreEqual(1.10f, mesh.bounds.size.x * strip.GetComponentInChildren<MeshFilter>().transform.lossyScale.x, 0.05f, "碳滑板長約 1.1 m");
            var docs = Docs(File.ReadAllText(ScenePath));
            var go = docs.FirstOrDefault(d => d.StartsWith("1 ") && d.Contains("m_Name: XR Origin (XR Rig)"));
            Assert.IsNotNull(go, "找不到 XR Origin");
            string id = Regex.Match(go, @"^1 &(\d+)").Groups[1].Value;
            var tr = docs.FirstOrDefault(d => d.StartsWith("4 ") && d.Contains("m_GameObject: {fileID: " + id + "}"));
            Assert.IsNotNull(tr);
            StringAssert.Contains("m_LocalScale: {x: 1, y: 1, z: 1}", tr, "XR Origin 不可縮放（1 unit = 1 m）");
        }

        /// <summary>把 .unity 檔拆成物件（每段以「類別編號 &amp;fileID」開頭）。</summary>
        static string[] Docs(string yaml) => yaml.Split(new[] { "--- !u!" }, StringSplitOptions.RemoveEmptyEntries);
    }
}
