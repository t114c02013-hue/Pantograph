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

        /// <summary>第 8.1 點：五個值都在，程式裡沒有眼高的字面值（其他用途的 1.6 列在下面）。</summary>
        [Test]
        public void Posture_ValuesInData_NoEyeLiteral()
        {
            var p = PostureDef.Load();
            Assert.AreEqual(1.63f, p.standEye, 1e-4f); Assert.AreEqual(1.20f, p.kneelEye, 1e-4f); Assert.AreEqual(0.95f, p.crouchEye, 1e-4f);
            Assert.AreEqual(1.09f, p.standElbow, 1e-4f); Assert.AreEqual(1.43f, p.standShoulder, 1e-4f);
            Assert.AreEqual(p.kneelEye, p.Eye(Posture.Kneel)); Assert.AreEqual(p.crouchEye, p.Eye(Posture.Crouch));
            // 這些檔案的 1.6 不是眼高（車頂圓弧、桿長、傳送吸附、相機與模型擺位）
            var notEye = new[] { "PostureDef.cs", "RoofSceneDef.cs", "PantographRigBuilder.cs", "RoofSiteBuilder.cs", "SliceSceneBuilder.cs", "XrHeldPole.cs", "XrPlayer.cs", "XrTeleport.cs" };
            var found = System.IO.Directory.GetFiles("Assets/_Project", "*.cs", System.IO.SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/Tests/") && !notEye.Contains(System.IO.Path.GetFileName(f)))
                .Where(f => System.Text.RegularExpressions.Regex.IsMatch(System.IO.File.ReadAllText(f), @"(?<![\d.])1\.63?f")).ToList();
            CollectionAssert.IsEmpty(found);
        }

        /// <summary>第 8 點：滑板工作面舒適高度 1.20 m。</summary>
        [Test]
        public void HeightProfile_StripTop_ComfortHeight()
        {
            var h = UnityEditor.AssetDatabase.LoadAssetAtPath<HeightProfile>("Assets/_Project/Modules/Worksite/Data/HeightProfile.asset");
            var p = PostureDef.Load();
            Assert.AreEqual(p.stripWorkHeight, h.raisedStripTop, p.stripWorkTolerance);
        }

        /// <summary>第 23 項反例：按鈕放在 0.95 不合格；資料開關開著才讓建置失敗。</summary>
        [Test]
        public void HeightCheck_ButtonAt095_Fails()
        {
            var p = ScriptableObject.CreateInstance<PostureDef>();
            var r = new StationLayoutCheck.SceneResult();
            StationLayoutCheck.HeightBand(r, p, "按鈕", 1.10f, p.liftButtons);
            Assert.IsTrue(r.Ok); Assert.AreEqual(0, r.warn.Count);
            StationLayoutCheck.HeightBand(r, p, "按鈕", 0.95f, p.liftButtons);
            Assert.IsTrue(r.Ok, "開關關著只列警告"); Assert.AreEqual(1, r.warn.Count);
            p.enforceHeights = true;
            StringAssert.Contains("不合格", StationLayoutCheck.HeightBand(r, p, "按鈕", 0.95f, p.liftButtons));
            Assert.IsFalse(r.Ok, "開關開著建置失敗");
        }

        /// <summary>第 6 點：右搖桿按下只在有第二功能的工具上有作用；每支工具都在按鍵表上（拿著不在表上的工具扳機握把會失效）。</summary>
        [Test]
        public void RightStickClick_OnlyToolsWithSecondary_AllToolsInTable()
        {
            var t = InputBindingTable.LoadDefault();
            Assert.AreEqual("ToolSecondary", t.FunctionOf("RightStickClick"));
            foreach (var path in UnityEditor.AssetDatabase.FindAssets("t:ToolDefinition").Select(UnityEditor.AssetDatabase.GUIDToAssetPath))
            {
                var d = UnityEditor.AssetDatabase.LoadAssetAtPath<ToolDefinition>(path);
                string ctx = InputBindingTable.ToolPrefix + d.toolId;
                Assert.IsTrue(t.Enabled(ctx, "RightTrigger"), d.toolId + " 不在按鍵表上");
                Assert.AreEqual(!string.IsNullOrEmpty(d.secondaryAction), t.Enabled(ctx, "RightStickClick"), d.toolId);
            }
            Assert.AreEqual("part:toggle", UnityEditor.AssetDatabase.LoadAssetAtPath<ToolDefinition>("Assets/_Project/Modules/Tools/LockoutTag/Data/TL_lockout_tag.asset").secondaryAction);
            Assert.IsFalse(t.Enabled(InputBindingTable.Empty, "RightStickClick"), "空手沒作用");
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

        /// <summary>第 5 點：按鍵小卡讀按鍵表，只列這個情境有作用的按鍵。</summary>
        [Test]
        public void Controller_LabelsFollowBindingTable()
        {
            var t = InputBindingTable.LoadDefault();
            var empty = XRInputMapper.KeyCard(t, InputBindingTable.Empty).Split('\n');
            CollectionAssert.Contains(empty, "右扳機＝點／使用", "只取冒號前");
            Assert.IsFalse(empty.Any(l => l.StartsWith("A＝")), "空手 A 沒作用");
            Assert.IsTrue(XRInputMapper.KeyCard(t, InputBindingTable.ToolPrefix + "tool.megger").Contains("右搖桿按下＝第二功能"));
            Assert.IsFalse(empty.Any(l => l.StartsWith("右搖桿按下")), "空手右搖桿按下沒作用");
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
