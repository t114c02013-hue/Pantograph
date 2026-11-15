using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.XR;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>terry 186（RA-21、RA-25、RA-26）：頭穿進實心物體變暗＋「請退後」。</summary>
    public class HeadSolidTests
    {
        static HeadSolidDef Def() => ScriptableObject.CreateInstance<HeadSolidDef>();
        static readonly Vector3 Out = new Vector3(0, 1.6f, 0), In = new Vector3(1, 1.6f, 0);

        [Test]
        public void Logic_TouchOrCross_Inside_ElseOutsideAndLastClearUpdated()   // 1
        {
            var l = new HeadSolidLogic(Def());
            l.Tick(0f, 0.01f, Out, false, false, h => false, (a, b) => 0);
            Assert.IsFalse(l.Inside); Assert.AreEqual(Out, l.LastClear);
            l.Tick(1f, 0.01f, In, false, false, h => true, (a, b) => 0);
            Assert.IsTrue(l.Inside, "球碰到表面");
            Assert.AreEqual(Out, l.LastClear, "在裡面不更新");
            l.Tick(2f, 0.01f, In, false, false, h => false, (a, b) => a == Out && b == In ? 1 : 0);
            Assert.IsTrue(l.Inside, "球碰不到表面但從最後在外面的位置走過來穿過表面（整顆頭在空心車體裡）");
            var far = new Vector3(5, 1.6f, 0);
            l.Moved(far, 3f);
            Assert.AreEqual(far, l.LastClear, "被移過去：直接設成新位置");
            l.Tick(4f, 0.01f, far, false, false, h => false, (a, b) => a != far ? 1 : 0);
            Assert.IsFalse(l.Inside, "不會誤判成穿過");
        }

        [Test]
        public void Logic_InOneSideOutAnother_NotStuckDark()   // 審查第 1 點：從側面伸進車體、再從車頂上方出來
        {
            var l = new HeadSolidLogic(Def());
            var side = new Vector3(-1.6f, 1.0f, 0f); var inBody = new Vector3(-1.3f, 1.0f, 0f); var aboveRoof = new Vector3(-1.3f, 1.6f, 0f);
            l.Tick(0f, 0.01f, side, false, false, h => false, (a, b) => 0);
            // 射線只打正面：從外面往裡打到側牆（進 1）；從車裡往外打不到（出 0）
            l.Tick(1f, 0.01f, inBody, false, false, h => false, (a, b) => a == side ? 1 : 0);
            Assert.IsTrue(l.Inside, "整顆頭在車體裡");
            // 頭從車頂出來：側面→車頂上方 穿過側牆正面（1），車頂上方→側面 打到車頂正面（1）
            l.Tick(2f, 0.01f, aboveRoof, false, false, h => false, (a, b) => 1);
            Assert.IsFalse(l.Inside, "進一面出一面＝出來了，不會一直暗");
            Assert.AreEqual(aboveRoof, l.LastClear);
        }

        [Test]
        public void Fade_RisesToMaxInFadeIn_BackToZeroInFadeOut_NoJump()   // 2
        {
            var d = Def(); var l = new HeadSolidLogic(d);
            float a = 0f;
            for (int i = 0; i < 25; i++) a = l.Tick(i * 0.01f, 0.01f, In, false, false, h => true, (x, y) => 0);
            Assert.AreEqual(d.maxAlpha, a, 1e-3f, "fadeInSec 後到 maxAlpha");
            float prev = a;
            for (int i = 0; i < 20; i++) { a = l.Tick(1f + i * 0.01f, 0.01f, Out, false, false, h => false, (x, y) => 0); Assert.LessOrEqual(prev - a, d.maxAlpha / d.fadeOutSec * 0.01f + 1e-4f, "不會跳"); prev = a; }
            Assert.AreEqual(0f, a, 1e-3f, "fadeOutSec 後回到 0");
            a = l.Tick(2f, 0.01f, In, false, false, h => true, (x, y) => 0);
            a = l.Tick(2.01f, 0.01f, Out, false, false, h => false, (x, y) => 0);
            Assert.Less(a, 0.1f, "很快進出不會跳到暗");
        }

        [Test]
        public void NotJudged_PausedGraceSuppressed()   // 3
        {
            var l = new HeadSolidLogic(Def());
            l.Moved(Out, 10f);
            Assert.AreEqual(0f, l.Tick(10.49f, 0.01f, In, false, false, h => true, (a, b) => 1), "傳送後 0.49 s");
            Assert.AreEqual(0f, l.Tick(11f, 0.01f, In, true, false, h => true, (a, b) => 1), "墜落區淡出推回／轉場進行中");
            Assert.AreEqual(0f, l.Tick(11.49f, 0.01f, In, false, false, h => true, (a, b) => 1), "讓出之後 0.49 s");
            Assert.AreEqual(0f, l.Tick(12f, 0.01f, In, false, true, h => true, (a, b) => 1), "代做、重播、桌機、開場還沒完成");
            Assert.Greater(l.Tick(13f, 0.01f, In, false, false, h => true, (a, b) => 1), 0f, "之後照判");
        }

        [Test]
        public void Hint_FallZonePriority()   // 4
        {
            Assert.IsTrue(HeadSolidLogic.ShowHint(true, FallZone.Normal));
            foreach (var z in new[] { FallZone.Warn, FallZone.OverPlatform, FallZone.FallZone, FallZone.Beyond })
                Assert.IsFalse(HeadSolidLogic.ShowHint(true, z), "墜落區提示優先：" + z);
            Assert.IsFalse(HeadSolidLogic.ShowHint(false, FallZone.Normal));
        }

        [Test]
        public void Masks_ExcludeHeadSolid()   // 6
        {
            int hs = SceneLayers.HeadSolidMask;
            Assert.AreNotEqual(0, hs, "專案要有 HeadSolid 層");
            foreach (var (name, m) in new[] { ("XR 指向線／Picker／清潔布（InteractMask）", SceneLayers.InteractMask), ("傳送線", SceneLayers.TeleportMask), ("天車遙控器按鈕", XrCranePendant.PressMask),
                                                ("天車碰撞", PantographSim.Parts.Mechanism.CraneMechanism.ObstacleMask), ("站點淨空", StationLayoutCheck.SolidMask), ("站點視線", StationLayoutCheck.SightMask), ("防墜繩路徑", StationLayoutCheck.LanyardMask) })
                Assert.AreEqual(0, m & hs, name + " 不打 HeadSolid");
            int layer = LayerMask.NameToLayer(SceneLayers.HeadSolid);
            for (int i = 0; i < 32; i++) Assert.IsTrue(Physics.GetIgnoreLayerCollision(layer, i), "HeadSolid 不和第 " + i + " 層互撞");
        }

        [Test]
        public void Scene_SolidsFound_LayerHeadSolid_NotOnPanInsulatorRailToolFloor()   // 5
        {
            StationTests.WithScene(scene =>
            {
                var def = HeadSolidDef.Load(); int layer = LayerMask.NameToLayer(SceneLayers.HeadSolid);
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                foreach (var row in def.solids.Where(x => !x.optional))
                {
                    var hits = all.Where(t => HeadSolidDef.Match(t, row.pattern) && !t.name.StartsWith(RoofSiteBuilder.HeadSolidPrefix)).ToList();
                    hits = hits.Where(t => !hits.Any(o => o != t && t.IsChildOf(o))).ToList();   // 外層和 FBX 根同名：碰撞框只做在外層
                    Assert.IsNotEmpty(hits, row.pattern + " 在場景裡");
                    foreach (var t in hits)
                        Assert.IsTrue(t.GetComponentsInChildren<Collider>(true).Any(c => c.gameObject.layer == layer && c.name.StartsWith(RoofSiteBuilder.HeadSolidPrefix)), t.name + " 有 HeadSolid_ 碰撞框");
                }
                foreach (var c in all.Where(t => t.gameObject.layer == layer)) StringAssert.StartsWith(RoofSiteBuilder.HeadSolidPrefix, c.name);
                foreach (var t in all) Assert.LessOrEqual(t.Cast<Transform>().Count(ch => ch.name.StartsWith(RoofSiteBuilder.HeadSolidPrefix)), 1, t.name + " 的碰撞框不重複（外層和 FBX 根同名）");
                foreach (var t in all.Where(t => t.gameObject.layer == layer))
                    foreach (var p in t.GetComponentsInParent<Transform>(true))
                        Assert.IsFalse(p.name.StartsWith("PAN_") || p.name == "Roof_Insulators" || p.name == "Roof_Busbar" || p.name.Contains("Rail") || p.name.StartsWith("Tool_") || p.name.StartsWith("NewStrip_") || p.name.StartsWith("Walkable") || p.name == "ENV_CabFloor",
                            $"{t.name} 不該掛在 {p.name}（受電弓、絕緣子、母線、欄杆、工具、地板、駕駛室不算實心）");
            });
        }

        [Test]
        public void Scene_StationItem24_CoversLeanKneelAndBowSkip()   // 站點檢查第 24 項真的有查：前傾拔插頭、車頂底架四個子站點跪姿、bowSkips
        {
            StationTests.WithScene(scene =>
            {
                Physics.SyncTransforms();
                var set = AssetDatabase.LoadAssetAtPath<StationSet>("Assets/_Project/Modules/Worksite/Data/StationSet.asset");
                var rdef = AssetDatabase.LoadAssetAtPath<RoofSceneDef>("Assets/_Project/Modules/Worksite/Data/RoofSceneDef.asset");
                var ids = StationTests.SceneIds(scene);
                var pts = Object.FindObjectsOfType<XrTeleportPoint>(true).Where(p => p.gameObject.scene == scene);
                var r = StationLayoutCheck.CheckScene(set, rdef, pts, id => ids.TryGetValue(id ?? "", out var t) ? t : null, null, A6Tests.Wrench);
                var item24 = r.passed.Concat(r.bad).Where(x => x.Contains("沒有穿進實心物體")).ToList();
                Assert.IsTrue(item24.Any(x => x.Contains("tp.cab.shore 前傾頭 (-1.900")), "tp.cab.shore 前傾頭到 X −1.9");
                foreach (var id in new[] { "tp.roof.base_w.s", "tp.roof.base_e.s", "tp.roof.base_w.n", "tp.roof.base_e.n" })
                    Assert.IsTrue(item24.Any(x => x.Contains(id + " 跪姿頭")), id + " 跪姿頭");
                Assert.IsFalse(item24.Any(x => x.Contains("tp.platform.lift 彎腰頭")), "bowSkips 的站點不做彎腰頭");
                Assert.IsTrue(item24.Any(x => x.Contains("tp.platform.lift 站姿頭")), "bowSkips 的站點仍做站姿頭");
                Assert.IsTrue(item24.Any(x => x.Contains("檯面上方")), "檯面上方的頭有查");
                Assert.IsEmpty(r.bad.Where(x => x.Contains("沒有穿進實心物體")), "第 24 項全過");
            });
        }

        [Test]
        public void Scene_HeadChecks_TopsCabShellFakeStation()   // 7＋駕駛室（RA-26 第 5 點）
        {
            StationTests.WithScene(scene =>
            {
                Physics.SyncTransforms();
                var def = HeadSolidDef.Load(); var posture = PostureDef.Load();
                foreach (var name in new[] { "ENV_WorkBench", "ENV_PantographStand", "SpareShelf" })
                {
                    var o = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).First(t => t.name == name);
                    var top = StationLayoutCheck.TopPoints(name, o.position, def).First();
                    Assert.IsFalse(StationLayoutCheck.HeadInSolid(top, def), name + "：檯面上方 0.2 m 不觸發");
                    // 穿進去：挑頂面最大的那片碰撞框，頭（含半徑）壓進它的頂面 0.05 m（台架是框架，站點旁的點下面可能是空的）
                    var slab = o.GetComponentsInChildren<Collider>(true).Where(c => c.gameObject.layer == LayerMask.NameToLayer(SceneLayers.HeadSolid)).OrderByDescending(c => c.bounds.size.x * c.bounds.size.z).First().bounds;
                    Assert.IsTrue(StationLayoutCheck.HeadInSolid(new Vector3(slab.center.x, slab.max.y + def.headSolidRadius - 0.05f, slab.center.z), def), name + "：頭（含半徑）穿進頂面 0.05 m 要觸發");
                }
                var box = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).First(r => r.name == "ShoreBox_Body");
                bool wasActive = box.gameObject.activeInHierarchy;
                foreach (var t in box.GetComponentsInParent<Transform>(true)) t.gameObject.SetActive(true);
                Physics.SyncTransforms();
                Assert.IsTrue(StationLayoutCheck.HeadInSolid(box.bounds.center, def), "故意擺在箱子裡的假站點要失敗");
                var cab = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).First(t => t.name == "StationAnchor_tp.cab");
                var floor = Physics.Raycast(cab.position + Vector3.up * 5f, Vector3.down, out var hit, 20f, SceneLayers.WalkableMask) ? hit.point.y : -3.3f;
                var stand = new Vector3(cab.position.x, floor + posture.standEye, cab.position.z);
                foreach (var off in new[] { Vector3.zero, Vector3.right * 0.3f, Vector3.left * 0.3f, Vector3.forward * 0.3f, Vector3.back * 0.3f })
                    Assert.IsFalse(StationLayoutCheck.HeadInSolid(stand + off + (off == Vector3.zero ? Vector3.zero : Vector3.down * def.bowDown), def), "駕駛室站姿／彎腰不變暗 " + off);
                var shell = new Vector3(-R1Data.RoofScene.carBodyHalfWidth + 0.03f, -2.0f, -14.0f);
                Assert.IsTrue(StationLayoutCheck.HeadInSolid(shell, def), "車頭外側把頭伸進車殼要變暗");
            });
        }
    }
}
