using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Data;
using PantographSim.Parts.Common;
using PantographSim.Parts.Mechanism;
using PantographSim.XR;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>R1.5a a6 增補 4–6：待定紀錄、升降範圍和站點、LoweredTightBounds、台架外框推導、吊帶角度。</summary>
    public class A6SupplementTests
    {
        const string Folder = "Assets/_Project/Modules/Worksite/Data";
        static StationSet Set => AssetDatabase.LoadAssetAtPath<StationSet>(Folder + "/StationSet.asset");
        static RoofSceneDef Def => AssetDatabase.LoadAssetAtPath<RoofSceneDef>(Folder + "/RoofSceneDef.asset");

        /// <summary>增補 4 第 9 點／增補 6 第 1 點：每一筆待定都有 checkItem、objectId、ruleRef、decidedBy、releaseSegment；缺紀錄的待定在 CheckScene 裡算不合格。</summary>
        [Test]
        public void PendingRulings_AllHaveRecords_AndMissingRecordFailsTheBuild()
        {
            foreach (var p in Set.pendingRulings)
                Assert.IsTrue(p.HasRecord, $"待定 第{p.checkItem}項 {p.objectId} 缺 ruleRef／decidedBy／releaseSegment");
            var copy = Object.Instantiate(Set);
            try
            {
                copy.pendingRulings = new[] { new PendingRuling { checkItem = "16", objectId = "tp.roof.base_w.s" } };
                var r = new StationLayoutCheck.SceneResult();
                r.Fail("16", "tp.roof.base_w.s", "x");
                StationLayoutCheck.ApplyPending(copy, r);
                Assert.IsFalse(r.Ok, "缺裁定紀錄的待定要讓建置失敗");
                Assert.IsEmpty(r.pending);
                copy.pendingRulings[0].ruleRef = "D99"; copy.pendingRulings[0].decidedBy = "鐵道"; copy.pendingRulings[0].releaseSegment = "R1.5b";
                r = new StationLayoutCheck.SceneResult();
                r.Fail("16", "tp.roof.base_w.s", "x"); r.Fail("16", "tp.roof.base_e.s", "y"); r.Fail("15", "tp.roof.base_w.s", "z");
                StationLayoutCheck.ApplyPending(copy, r);
                Assert.AreEqual(1, r.pending.Count, "只有檢查項＋物件 ID 完全相等的那筆變成待定");
                CollectionAssert.AreEquivalent(new[] { "y", "z" }, r.bad, "同一物件的其他檢查項、其他物件照常不合格");
                StringAssert.Contains("1 待定", r.Summary);
            }
            finally { Object.DestroyImmediate(copy); }
        }

        /// <summary>增補 6 第 4 點（PanFootprintStationTest）：用真實的升降範圍，升弓 0、0.5、1.0 三個高度，會動的段（臂、弓頭、連桿）頂點範圍離車頂站點 ≥ D37 依零件分開的淨空（羊角 roofPanHeadClearRadius、其他 clearRadius；只算和身體同高的段）。</summary>
        [Test]
        public void PanFootprintStationTest_MovingSegmentsNeverOverlapRoofStations()
        {
            StationTests.WithScene(scene =>
            {
                var root = PanAssy.Root(scene);
                var mech = root.GetComponentInChildren<PantographMechanism>(true);
                var pts = Object.FindObjectsOfType<XrTeleportPoint>(true).Where(p => p.gameObject.scene == scene).ToDictionary(p => p.PointId);
                var roof = Set.stations.Where(s => s.floor == StationFloor.Roof).Select(s => pts[s.id]).ToList();
                var segs = new[] { "LowerArm", "UpperArm", "PanHead" }.Concat(mech.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Rod")).Select(t => t.name)).Distinct().ToList();
                float lift = mech.Lift;
                try
                {
                    foreach (float l in new[] { 0f, 0.5f, 1f })
                    {
                        mech.SetLift(l);
                        foreach (var seg in segs)
                        {
                            var b = StationLayoutCheck.VertexBounds(StationLayoutCheck.JointRenderers(root, seg)).Value;
                            foreach (var p in roof)
                            {
                                var q = p.transform.position;
                                bool sameHeight = b.min.y < q.y + 1.8f && b.max.y > q.y + 0.1f;   // 身體膠囊 0.1–1.8 m
                                float dx = Mathf.Max(b.min.x - q.x, 0f, q.x - b.max.x), dz = Mathf.Max(b.min.z - q.z, 0f, q.z - b.max.z);
                                float need = StationLayoutCheck.PanClearRadius(Set, seg, StationFloor.Roof);   // D37：讀 partClearRadius
                                Assert.IsFalse(sameHeight && Mathf.Sqrt(dx * dx + dz * dz) < need, $"升弓 {l}：{seg} 頂點範圍離 {p.PointId} 不到 {need} m");
                            }
                        }
                    }
                }
                finally { mech.SetLift(lift); }
            });
        }

        /// <summary>
        /// R1.5b 軟體工程裁定（panFootprints 觸發範圍）：升降範圍檢查和「下方有沒有 Walkable」各自獨立。
        /// (1) movingCarveInflate＝0 的 RoofSceneDef 副本、站點放進弓頭升降範圍內：升降範圍那一項不合格，而且「下方沒有 Walkable」也照樣列出；
        /// (2) 站點離範圍 0.10 m（小於挖除外擴 0.15）：不合格；(3) 離範圍 0.45 m（大於 0.40）：合格。
        /// </summary>
        [Test]
        public void PanFootprintStationTest_CounterExamples()
        {
            StationTests.WithScene(scene =>
            {
                var mech = Object.FindObjectOfType<PantographMechanism>(true);
                var fp = PantographSim.Editor.PanGeometry.SweptFootprints(mech)["PanHead"];
                var def = Object.Instantiate(Def);
                try
                {
                    def.movingCarveInflate = 0f;
                    bool FootFail(StationLayoutCheck.SceneResult r) => r.failures.Any(f => f.item == "core" && f.text.Contains("tp.roof.base_w.s") && f.text.Contains("升降範圍（PanHead）"));
                    var inside = A6WrenchGateTests.Run(scene, p => { var q = p["tp.roof.base_w.s"].transform; q.position = new Vector3(fp.center.x, q.position.y, fp.center.z); }, null, def);
                    Assert.IsTrue(FootFail(inside), "站在弓頭升降範圍內：升降範圍那一項不合格");
                    Assert.IsTrue(inside.failures.Any(f => f.text.Contains("tp.roof.base_w.s") && f.text.Contains("下方沒有 Walkable")), "兩項各報各的");
                    foreach (var (gap, fail) in new[] { (0.10f, true), (0.45f, false) })
                    {
                        var r = A6WrenchGateTests.Run(scene, p => { var q = p["tp.roof.base_w.s"].transform; q.position = new Vector3(q.position.x, q.position.y, fp.min.z - gap); }, null, def);
                        Assert.AreEqual(fail, FootFail(r), $"離範圍 {gap} m：" + string.Join("\n", r.failures.Select(f => f.text)));
                    }
                }
                finally { Object.DestroyImmediate(def); }
            });
        }

        /// <summary>增補 4 第 6 點（建模 B H1）＋增補 7 E3(1)：LoweredTightBounds 存在 RoofSceneDef，排除可拆件後 Y 最低點＝0（底架底面），PAN_Root 的 Box 一致；舊的 json 已刪。</summary>
        [Test]
        public void LoweredTightBounds_ExcludesDetachables_MinY0_MatchesBox()
        {
            var t = Tight();
            Assert.AreEqual(0f, t.min.y, 0.002f, "排除底座螺栓等可拆件後，最低點是底架底面");
            Assert.IsFalse(System.IO.File.Exists("Assets/_Project/Parts/PantographAssembly/Data/pan_lowered_tight_bounds.json"), "舊的 json 要刪掉");
            StationTests.WithScene(scene =>
            {
                var bc = PanAssy.Root(scene).GetComponent<BoxCollider>();
                Assert.Less(Vector3.Distance(bc.center, t.center), 1e-3f); Assert.Less(Vector3.Distance(bc.size, t.size), 1e-3f);
            });
        }

        /// <summary>增補 4 第 4 點：台架外框＝LoweredTightBounds 的 X、Z 範圍兩端各外擴 standFrameMargin（不對稱時外框中心跟著偏）。</summary>
        [Test]
        public void StandFrame_DerivedFromTightBounds_PlusMargin()
        {
            var t = PantographSim.Tests.A6SupplementTests.Tight();
            Assert.AreEqual(t.size.x + 2f * Def.standFrameMargin, Def.standSizeXZ.x, 0.002f);
            Assert.AreEqual(t.size.z + 2f * Def.standFrameMargin, Def.standSizeXZ.y, 0.002f);
            Assert.AreEqual(t.center.x, Def.standFrameOffsetXZ.x, 0.002f); Assert.AreEqual(t.center.z, Def.standFrameOffsetXZ.y, 0.002f);
            StationTests.WithScene(scene =>
            {
                var frame = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(true)).First(c => c.name == "StandFrame").bounds;
                Assert.AreEqual(Def.standSizeXZ.x, frame.size.x, 0.002f); Assert.AreEqual(Def.standSizeXZ.y, frame.size.z, 0.002f);
                Assert.AreEqual(Def.StandFrameCenterXZ.x, frame.center.x, 0.002f); Assert.AreEqual(Def.StandFrameCenterXZ.y, frame.center.z, 0.002f);
            });
        }
        static Bounds Tight() => Def.panLoweredTightBounds;

        /// <summary>增補 4 第 8 點（建模 B H3、D36）：吊帶長＝資料值 slingLength；每條和垂直線 ≤ 45°，同一端兩條之間 ≤ 90°。</summary>
        [Test]
        public void Slings_LengthFromData_AnglesWithinLimits()
        {
            var t = PanLiftMath.LoadDefault();
            StationTests.WithScene(scene =>
            {
                var root = PanAssy.Root(scene);
                var pts = Enumerable.Range(1, 4).Select(i => root.Find("PAN_LiftPoints/ip.pan_assy.lift_point_" + i).position).ToList();
                var cog = root.Find("PAN_Base/COG").position;
                var (ep, em) = PanLiftMath.BeamEyes(cog, pts, t);
                for (int i = 0; i < 4; i++)
                {
                    var eye = pts[i].x >= cog.x ? ep : em;
                    Assert.AreEqual(t.slingLength, Vector3.Distance(eye, pts[i]), 0.01f, $"吊帶 {i + 1} 長");
                    Assert.LessOrEqual(PanLiftMath.SlingAngleDeg(eye, pts[i]), t.maxSlingAngleDeg, $"吊帶 {i + 1} 和垂直線");
                }
                Assert.LessOrEqual(Vector3.Angle(pts[0] - em, pts[3] - em), t.maxSlingPairAngleDeg, "吊帶 1–4 夾角");
                Assert.LessOrEqual(Vector3.Angle(pts[1] - ep, pts[2] - ep), t.maxSlingPairAngleDeg, "吊帶 2–3 夾角");
            });
        }

        /// <summary>增補 6 第 3 點：可走寬度依區域（放寬帶 Z 範圍內用 roofBaseWalkHalfWidth，外面用 roofWalkHalfWidth），不看站點名稱。</summary>
        [Test]
        public void RoofWalkLimit_ByZone_NotByStationName()
        {
            var d = Def;
            Assert.AreEqual(d.roofBaseWalkHalfWidth, d.RoofWalkLimitAt((d.roofBaseWalkZMin + d.roofBaseWalkZMax) / 2f));
            Assert.AreEqual(d.roofWalkHalfWidth, d.RoofWalkLimitAt(d.roofBaseWalkZMin - 0.5f));
            Assert.AreEqual(d.roofWalkHalfWidth, d.RoofWalkLimitAt(d.roofBaseWalkZMax + 0.5f));
        }
    }
}

namespace PantographSim.Tests
{
    /// <summary>D37＋增補 6 第 7 點 G：零件別的站點淨空（StationSet.partClearRadius）。</summary>
    public class PartClearRadiusTest
    {
        static StationSet Set => UnityEditor.AssetDatabase.LoadAssetAtPath<StationSet>("Assets/_Project/Modules/Worksite/Data/StationSet.asset");

        [Test]
        public void RoofSubStation_AgainstHorn_Uses02()
        {
            Assert.AreEqual(0.20f, Set.ClearRadiusFor("PanHead", StationFloor.Roof), 1e-4f);
            // .s 到羊角升降範圍 0.363 m（Z −0.55 對 −0.187）＞ 0.2：合格（實際距離在建置報告）
            Assert.Greater(0.363f, Set.ClearRadiusFor("PanHead", StationFloor.Roof));
        }

        [Test]
        public void SameHorn_AgainstPlatformStation_Uses04() => Assert.AreEqual(Set.clearRadius, Set.ClearRadiusFor("PanHead", StationFloor.Platform), 1e-4f);

        [Test]
        public void UnlistedPart_UsesDefault04()
        {
            Assert.AreEqual(0.40f, Set.clearRadius, 1e-4f);
            Assert.AreEqual(Set.clearRadius, Set.ClearRadiusFor("SomethingNotListed", StationFloor.Roof), 1e-4f);
        }
    }
}
