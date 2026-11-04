using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.XR;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>2026-11-04 頭盔試玩修正（Docs/playtest/2026-11-04-vr-fixes-software.md）：不用跑 Play 就能驗的部分。</summary>
    public class XrPlaytestFixTests
    {
        /// <summary>第 7 點：天車遙控器每一顆 Btn_* 和 Key_Power 都有 Collider（trigger）。</summary>
        [Test]
        public void Pendant_EveryControlHasTriggerCollider()
        {
            StationTests.WithScene(scene =>
            {
                var remote = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).First(t => t.name == "ENV_CraneRemote");
                var controls = remote.GetComponentsInChildren<Transform>(true).Where(RoofSiteBuilder.IsPendantControl).ToList();
                Assert.Greater(controls.Count, 5, "遙控器要找得到按鍵");
                foreach (var t in controls)
                {
                    var c = t.GetComponent<Collider>();
                    Assert.IsTrue(c != null, t.name + " 要有 Collider");
                    Assert.IsTrue(c.isTrigger, t.name + " 是 trigger");
                }
            });
        }

        static BoundaryDef Def => AssetDatabase.LoadAssetAtPath<BoundaryDef>("Assets/_Project/Modules/XR/Resources/BoundaryDef.asset");
        static RoofSceneDef Roof => AssetDatabase.LoadAssetAtPath<RoofSceneDef>("Assets/_Project/Modules/Worksite/Data/RoofSceneDef.asset");

        /// <summary>第 1 點：內縮的算法：L 形內縮、洞不內縮、窄通道還連著。</summary>
        [Test]
        public void Erode_ShrinksOuterEdge_FillsHoles()
        {
            var one = BoundaryLogic.Erode(new[] { Rect.MinMaxRect(0, 0, 2, 2) }, 0.15f);
            Assert.AreEqual(1, one.Length);
            Assert.AreEqual(0.15f, one[0].xMin, 1e-4f); Assert.AreEqual(1.85f, one[0].yMax, 1e-4f);
            var ring = new[] { Rect.MinMaxRect(0, 0, 3, 1), Rect.MinMaxRect(0, 2, 3, 3), Rect.MinMaxRect(0, 1, 1, 2), Rect.MinMaxRect(2, 1, 3, 2) };   // 中間有洞
            var area = BoundaryLogic.Erode(ring, 0.15f);
            Assert.AreEqual(new Vector2(1.5f, 1.5f), BoundaryLogic.Clamp(area, new Vector2(1.5f, 1.5f)), "洞（絕緣子、底架）不內縮");
            var corridor = BoundaryLogic.Erode(new[] { Rect.MinMaxRect(0, 0, 2, 2), Rect.MinMaxRect(2, 0.5f, 3, 1.5f), Rect.MinMaxRect(3, 0, 5, 2) }, 0.15f);
            Assert.AreEqual(new Vector2(2.5f, 1f), BoundaryLogic.Clamp(corridor, new Vector2(2.5f, 1f)), "通道還連著");
            Assert.AreEqual(1.35f, BoundaryLogic.Clamp(corridor, new Vector2(2.5f, 1.9f)).y, 1e-4f);
        }

        /// <summary>規格 v5 測試 4：BoundaryDef 和場景的 Walkable 往內縮 0.15 一致。</summary>
        [Test]
        public void BoundaryDef_MatchesWalkableInset()
        {
            Assert.AreEqual(0.15f, Def.inset, 1e-4f);
            StationTests.WithScene(scene =>
            {
                var walk = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BoxCollider>(true)).Where(c => c.gameObject.layer == LayerMask.NameToLayer(SceneLayers.Walkable)).ToList();
                Assert.Greater(walk.Count, 3);
                var want = BoundaryLogic.Erode(walk.Select(BoundaryLogic.WalkRect).ToList(), Def.inset);
                Assert.AreEqual(want.Length, Def.open.Length);
                for (int i = 0; i < want.Length; i++) Assert.Less((want[i].min - Def.open[i].min).magnitude + (want[i].max - Def.open[i].max).magnitude, 1e-3f);
                Assert.Less(Def.closed.Sum(r => r.width * r.height), Def.open.Sum(r => r.width * r.height), "閘門關時沒有跨板");
            });
        }

        /// <summary>規格 v5 測試 1、2：頭推出護欄 0.3 m 推回邊上；跪著（頭低）一樣推回；只推水平。</summary>
        [Test]
        public void HeadOutside_PushedBackToEdge_AtAnyHeight()
        {
            float edge = Roof.PlatformOuterX + Def.inset;
            foreach (float y in new[] { 1.63f, 1.20f, 0.95f })
            {
                var head = new Vector3(Roof.PlatformOuterX - 0.3f, y, 0f);
                var push = HeadBoundaryPusher.Push(Def.closed, head);
                Assert.AreEqual(edge, head.x + push.x, 1e-3f, "推回邊上（頭高 " + y + "）");
                Assert.AreEqual(0f, push.y);
            }
            float kz = Roof.roofBaseWalkZMin - 0.3f, roofEdge = Roof.RoofWalkLimitAt(kz) - Def.inset;
            var kneel = new Vector3(roofEdge + 0.4f, Roof.roofFloorY + 0.95f, kz);
            Assert.AreEqual(roofEdge, kneel.x + HeadBoundaryPusher.Push(Def.closed, kneel).x, 1e-3f, "車頂矮欄旁跪著伸出去也推回");
            Assert.AreEqual(Vector3.zero, HeadBoundaryPusher.Push(Def.closed, new Vector3(edge + 0.2f, 1.6f, 0f)), "範圍內不推");
        }

        /// <summary>規格 v5 測試 3：不能有隱形牆：每個邊界 collider 都在看得見的 Renderer 裡（容差 0.01）。</summary>
        [Test]
        public void NoInvisibleWalls()
        {
            StationTests.WithScene(scene =>
            {
                Physics.SyncTransforms();
                var roots = scene.GetRootGameObjects();
                var renderers = roots.SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)).Where(r => r.enabled).ToList();
                var bad = new List<string>();
                int mask = SceneLayers.BoundaryMask;
                foreach (var c in roots.SelectMany(g => g.GetComponentsInChildren<Collider>(true)).Where(c => (mask & (1 << c.gameObject.layer)) != 0))
                {
                    var b = BoundsOf(c);
                    var seen = renderers.Where(r => r.bounds.Intersects(b)).Select(r => r.bounds).ToList();
                    if (seen.Count == 0) { bad.Add(c.name + "：沒有看得見的模型"); continue; }
                    var all = seen[0]; foreach (var r in seen) all.Encapsulate(r);
                    all.Expand(0.02f);
                    if (!all.Contains(b.min) || !all.Contains(b.max)) bad.Add($"{c.name}：collider {b.min:F2}–{b.max:F2} 超出模型 {all.min:F2}–{all.max:F2}");
                }
                Assert.IsEmpty(bad, string.Join("\n", bad));
            });
        }

        /// <summary>collider 停用或物件沒啟用時 bounds 是空的。</summary>
        static Bounds BoundsOf(Collider c)
        {
            if (!(c is BoxCollider b)) return c.bounds;
            var w = new Bounds(c.transform.TransformPoint(b.center), Vector3.zero);
            foreach (var sx in new[] { -0.5f, 0.5f }) foreach (var sy in new[] { -0.5f, 0.5f }) foreach (var sz in new[] { -0.5f, 0.5f })
                w.Encapsulate(c.transform.TransformPoint(b.center + Vector3.Scale(b.size, new Vector3(sx, sy, sz))));
            return w;
        }

        /// <summary>規格 v5 第 4 點：撞到邊界沒有目標；別的區域選不到；車頂開放時平台和車頂相連。</summary>
        [Test]
        public void Teleport_NotAcrossRail_OnlyConnectedAreas()
        {
            Assert.IsTrue(XrTeleport.Connected("Platform", "Platform", false));
            Assert.IsFalse(XrTeleport.Connected("Platform", "Roof", false), "車頂沒開放");
            Assert.IsTrue(XrTeleport.Connected("Platform", "Roof", true));
            Assert.IsFalse(XrTeleport.Connected("Platform", "Ground", true), "地面不相連");
            StationTests.WithScene(scene =>
            {
                Physics.SyncTransforms();
                var pts = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<XrTeleportPoint>(true)).ToList();
                var lift = pts.First(p => p.PointId == "tp.platform.lift");
                var landing = lift.transform.position;
                Assert.IsNull(XrTeleport.PickTarget(pts, landing, true, 1.6f, "Platform", true), "指向線撞到邊界就沒有目標");
                var got = XrTeleport.PickTarget(pts, landing, false, 1.6f, "Platform", false);
                Assert.AreEqual("Platform", got.Floor);
                var list = new List<Vector3>();
                var from = new Vector3(Roof.PlatformOuterX + 0.5f, 0.8f, 0f);
                TeleportArc.Trace(from, new Vector3(-7f, 0f, 0f), -10f, SceneLayers.TeleportMask, list, out bool blocked);
                Assert.IsTrue(blocked, "朝護欄外打，指向線被護欄擋住");
            });
        }

        /// <summary>第 1e 點：每個站點都在頭的活動範圍裡（站上去不會被推），並列出邊界段落給建模 A（Logs/BoundaryReport.txt）。</summary>
        [Test]
        public void Stations_InsideArea_ReportSegments()
        {
            StationTests.WithScene(scene =>
            {
                var sb = new StringBuilder("站點離活動範圍（負值＝在範圍內；正值＝站上去會被推的距離 m）\n");
                var far = new List<string>();
                foreach (var p in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<XrTeleportPoint>(true)).OrderBy(p => p.PointId))
                {
                    float d = HeadBoundaryPusher.Push(Def.open, p.transform.position).magnitude;
                    sb.AppendLine($"{p.PointId}\t{d:0.000}");
                    if (d > 0.02f) far.Add($"{p.PointId} {d:0.000}");
                }
                sb.AppendLine("\n邊界段落（名稱、中心 X,Z、長度 m、高度 m、建議型式）");
                foreach (var c in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BoxCollider>(true)).Where(c => c.gameObject.layer == LayerMask.NameToLayer(SceneLayers.WalkLimit)).OrderBy(c => c.name))
                {
                    var b = BoundsOf(c);
                    string type = c.name.Contains("_Base") || c.name.Contains("_Step") ? "A 矮欄" : "B 一般護欄";
                    if (Mathf.Max(b.size.x, b.size.z) <= 0.15f) type = "C 端擋板";
                    sb.AppendLine($"{c.name}\t{b.center.x:0.00},{b.center.z:0.00}\t{Mathf.Max(b.size.x, b.size.z):0.00}\t{b.size.y:0.00}\t{type}");
                }
                System.IO.File.WriteAllText("Logs/BoundaryReport.txt", sb.ToString());
                Assert.IsEmpty(far, "站點在活動範圍外：\n" + string.Join("\n", far));
            });
        }

        /// <summary>第 5 點：按鍵動畫的 t（WebXR 對應）。</summary>
        [Test]
        public void Controller_PressAmount()
        {
            var b = new XrControllerVisual.Buttons { trigger = .3f, grip = .6f, primary = true, stick = new Vector2(1f, -1f) };
            Assert.AreEqual(.3f, XrControllerVisual.PressAmount("xr_standard_trigger_pressed_value", b), 1e-4f);
            Assert.AreEqual(.6f, XrControllerVisual.PressAmount("xr_standard_squeeze_pressed_value", b), 1e-4f);
            Assert.AreEqual(1f, XrControllerVisual.PressAmount("xr_standard_thumbstick_xaxis_pressed_value", b), 1e-4f);
            Assert.AreEqual(0f, XrControllerVisual.PressAmount("xr_standard_thumbstick_yaxis_pressed_value", b), 1e-4f);
            Assert.AreEqual(0f, XrControllerVisual.PressAmount("xr_standard_thumbstick_pressed_value", b), 1e-4f);
            Assert.AreEqual(1f, XrControllerVisual.PressAmount("x_button_pressed_value", b), 1e-4f);
            Assert.AreEqual(0f, XrControllerVisual.PressAmount("b_button_pressed_value", b), 1e-4f);
            Assert.AreEqual(0f, XrControllerVisual.PressAmount("thumbrest_pressed_value", b), 1e-4f);
        }

        /// <summary>第 5 點：標示讀按鍵表：教學一直顯示、練習按住 Y；只顯示這個情境有作用的按鍵。</summary>
        [Test]
        public void Controller_LabelsFollowBindingTable()
        {
            Assert.IsTrue(XrControllerVisual.ShowLabels("tutorial", false));
            Assert.IsFalse(XrControllerVisual.ShowLabels("practice", false));
            Assert.IsTrue(XrControllerVisual.ShowLabels("practice", true));
            Assert.AreEqual("RightTrigger", XrControllerVisual.ControlOf("LBL_Trigger", false));
            Assert.AreEqual("LeftGrip", XrControllerVisual.ControlOf("LBL_Grip", true));
            Assert.AreEqual("A", XrControllerVisual.ControlOf("LBL_A", false));
            var t = InputBindingTable.LoadDefault();
            Assert.AreEqual("", XrControllerVisual.LabelText(t, InputBindingTable.Empty, "A"), "空手 A 沒作用");
            Assert.AreEqual("", XrControllerVisual.LabelText(t, InputBindingTable.Empty, "LeftTrigger"), "左扳機不在表上");
            Assert.AreEqual("點／使用", XrControllerVisual.LabelText(t, InputBindingTable.Empty, "RightTrigger"), "只取冒號前");
        }

        /// <summary>第 4 點：取最近的一格；半徑外沒有；左右手都算。</summary>
        [Test]
        public void Pouch_NearestSlot_EitherHand()
        {
            var pos = new Dictionary<int, Vector3> { { 5, new Vector3(0, 1, 0) }, { 6, new Vector3(0.1f, 1, 0) } };
            var hands = new[] { new Vector3(5, 0, 0), new Vector3(0.08f, 1, 0) };
            var (slot, hand) = XrPouchHover.Nearest(pos.Keys, k => pos[k], hands, 0.12f);
            Assert.AreEqual(6, slot, "取最近"); Assert.AreEqual(1, hand, "左手也算");
            Assert.AreEqual(0, XrPouchHover.Nearest(pos.Keys, k => pos[k], new[] { Vector3.zero }, 0.12f).slot);
        }

        /// <summary>第 4 點：轉身超過 60° 才跟過來，平常不動。</summary>
        [Test]
        public void Pouch_FollowsYawOnlyPastLimit()
        {
            bool turning = false;
            Assert.AreEqual(0f, XrCarryContainer.FollowYaw(0f, 50f, ref turning, 60f, 5f), 1e-4f, "60° 內不動");
            Assert.AreEqual(5f, XrCarryContainer.FollowYaw(0f, 70f, ref turning, 60f, 5f), 1e-4f, "超過開始跟");
            Assert.IsTrue(turning);
            Assert.AreEqual(10f, XrCarryContainer.FollowYaw(5f, 50f, ref turning, 60f, 5f), 1e-4f, "開始跟了就跟到對齊");
        }

        /// <summary>第 0 點：追蹤有效才往下走；頭太低不算；逾時用預設高度。</summary>
        [Test]
        public void StartupGate_WaitsForTracking_ThenTimesOut()
        {
            var l = ScriptableObject.CreateInstance<PanelLayout>();
            Assert.AreEqual(XrStartupSequencer.Gate.Wait, XrStartupSequencer.Decide(false, 1.6f, 1f, l));
            Assert.AreEqual(XrStartupSequencer.Gate.Wait, XrStartupSequencer.Decide(true, 0.2f, 1f, l), "頭太低還不算追蹤有效");
            Assert.AreEqual(XrStartupSequencer.Gate.Go, XrStartupSequencer.Decide(true, 1.6f, 1f, l));
            Assert.AreEqual(XrStartupSequencer.Gate.Timeout, XrStartupSequencer.Decide(false, 0f, l.startupTimeoutSec, l));
            Object.DestroyImmediate(l);
        }
    }
}
