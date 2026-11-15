using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.XR;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>R1.5a a4：地面維修區（作業區資料、台架、工作檯、站點、零件盒只標位置）與 D27 駕駛室。</summary>
    public class MaintenanceZoneTests
    {
        const string Folder = "Assets/_Project/Modules/Worksite/Data";
        static RoofSceneDef Def => AssetDatabase.LoadAssetAtPath<RoofSceneDef>(Folder + "/RoofSceneDef.asset");
        static StationSet Set => AssetDatabase.LoadAssetAtPath<StationSet>(Folder + "/StationSet.asset");

        static Bounds RB(Transform t) { var rs = t.GetComponentsInChildren<Renderer>(true); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }

        [Test]
        public void WorkBench_OnFloor_TopAtAnchor_StripsOnTop()   // RA-27：工作檯 FBX 原點在檯面，建置時抬到最低點貼地
        {
            StationTests.WithScene(scene =>
            {
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                var model = all.First(t => t.name == "ENV_WorkBench" && t.parent != null && t.parent.name == "Visual");
                var b = RB(model);
                Assert.AreEqual(Def.groundY, b.min.y, 0.02f, "工作檯最低點貼地");
                var anchor = all.First(t => t.name == "Anchor_BenchTop").position.y;
                float top = model.GetComponent<Renderer>().bounds.max.y;   // FBX 根節點的網格＝檯面板
                Assert.AreEqual(anchor, top, 0.02f, "檯面頂 ≈ Anchor_BenchTop");
                foreach (var s in all.Where(t => t.name.StartsWith("NewStrip_")))
                {
                    float gap = s.GetComponent<Renderer>().bounds.min.y - top;
                    Assert.That(gap, Is.InRange(-0.001f, 0.011f), s.name + " 底面在檯面頂上 0～0.01 m");
                }
            });
        }

        [Test]
        public void SlingRack_OnFloor_TopBarAtSlingHangHeight()   // RA-27：地面吊具架 FBX 原點在頂橫桿，建置時抬到最低點貼地
        {
            StationTests.WithScene(scene =>
            {
                var rack = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).First(t => t.name == "ENV_SlingRack");
                Assert.AreEqual(Def.groundY, RB(rack).min.y, 0.02f, "吊具架最低點貼地");
                Assert.AreEqual(Def.groundY + 1.2f, rack.GetComponent<Renderer>().bounds.max.y, 0.03f, "頂橫桿＝建置時吊帶掛高 g+1.2（架子站在地上）");
            });
        }

        [Test]
        public void FloorEquipment_NotSunkIntoFloor()   // RA-27 通用：用 Fbx() 擺在地面／平台地板的設備，外框最低點 ≥ 地板 −0.02 m
        {
            StationTests.WithScene(scene =>
            {
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                foreach (var (name, floor) in new[] { ("ENV_WorkBench", Def.groundY), ("ENV_PantographStand", Def.groundY), ("ENV_SlingRack", Def.groundY), ("ENV_PoleRack", Def.platformFloorY) })
                {
                    if (Def.floorSinkKnown.Contains(name)) continue;   // 已知清單（裁定後略過的）
                    var t = all.Where(x => x.name == name && x.GetComponentInChildren<Renderer>(true) != null).OrderBy(x => x.GetComponentsInParent<Transform>(true).Length).First();
                    Assert.GreaterOrEqual(RB(t).min.y, floor - 0.02f, name + " 沒有沉進地板");
                }
            });
        }

        [Test]
        public void Zones_ExistWithBounds_EveryStationHasZone()
        {
            var arrival = AssetDatabase.LoadAssetAtPath<ZoneDef>(Folder + "/Zones/zone.arrival.asset");
            var maint = AssetDatabase.LoadAssetAtPath<ZoneDef>(Folder + "/Zones/zone.maintenance.asset");
            Assert.IsNotNull(arrival); Assert.IsNotNull(maint);
            Assert.AreEqual(Def.groundY, maint.floorY, 1e-4f);
            Assert.AreEqual(new Vector2(-13.5f, -5f), maint.minXZ); Assert.AreEqual(new Vector2(-5.5f, 7f), maint.maxXZ);   // terry 181C：往北擴大 2 m
            foreach (var s in Set.stations) Assert.IsTrue(s.zoneId == ZoneDef.Arrival || s.zoneId == ZoneDef.Maintenance, s.id);
            foreach (var id in new[] { "tp.stand.n", "tp.stand.e", "tp.stand.s", "tp.stand.w" })
            {
                var s = Set.Find(id);
                Assert.IsNotNull(s, id);
                Assert.AreEqual(ZoneDef.Maintenance, s.zoneId);
                Assert.AreEqual("env.panto_stand", s.facingTargetId);
            }
            Assert.IsNull(Set.Find("tp.platform.bench"));   // a6 第一節 7：tp.platform.parts（日常課程備品架前）是平台站點，不在維修區
            Assert.AreEqual(ZoneDef.Arrival, Set.Find("tp.platform.parts")?.zoneId);
        }

        [Test]
        public void Scene_StandWorkbenchStations_ClearanceAndNodes()
        {
            StationTests.WithScene(scene =>
            {
                Physics.SyncTransforms();
                var ids = StationTests.SceneIds(scene);
                Assert.IsTrue(ids.ContainsKey("env.panto_stand") && ids.ContainsKey("env.panto_stand.tool_tray"));
                var stand = ids["env.panto_stand"];
                var names = stand.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
                foreach (var n in new[] { "StandSeat_1", "StandSeat_2", "StandSeat_3", "StandSeat_4", "StandClamp_1", "StandClamp_2", "AlignLight_1", "AlignLight_4", "BoltTray" })
                    Assert.Contains(n, names);
                var seats = Enumerable.Range(1, 4).Select(i => stand.GetComponentsInChildren<Transform>(true).First(t => t.name == "StandSeat_" + i).position).ToList();
                // a6 G2：定位銷間距＝底架 BaseHole 的實測間距（a5 以前是 0.80 × 1.00）
                var holes = Enumerable.Range(1, 4).Select(i => PanAssy.Root(scene).Find("PAN_Base/BaseHole_" + i).position).ToList();
                Assert.AreEqual(holes.Max(p => p.x) - holes.Min(p => p.x), seats.Max(p => p.x) - seats.Min(p => p.x), 0.01f, "定位銷 X 間距");
                Assert.AreEqual(holes.Max(p => p.z) - holes.Min(p => p.z), seats.Max(p => p.z) - seats.Min(p => p.z), 0.01f, "定位銷 Z 間距：" + string.Join(" ", seats.Select(s => s.ToString("F3"))));
                var top = seats[0].y;
                Assert.AreEqual(Def.StandTopY, top, 0.08f, "台架頂面 Y −2.90");
                // 四周 1.2 m 淨空：除了台架自己，沒有其他碰撞體
                var c = new Vector3(Def.StandFrameCenterXZ.x, Def.groundY + 0.6f, Def.StandFrameCenterXZ.y);
                var half = new Vector3(Def.standSizeXZ.x / 2f + Def.standClearance, 0.5f, Def.standSizeXZ.y / 2f + Def.standClearance);
                foreach (var col in Physics.OverlapBox(c, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    Assert.IsTrue(col.transform.IsChildOf(stand), "台架四周 1.2 m 內不能有 " + col.name);
                // 站點：維修區、離台架邊 0.6 m、面向台架
                var pts = Object.FindObjectsOfType<XrTeleportPoint>(true).Where(p => p.gameObject.scene == scene && StationLayoutCheck.IsStandEdge(p.PointId)).ToList();
                Assert.AreEqual(4, pts.Count);
                foreach (var p in pts)
                {
                    Assert.AreEqual(Def.groundY, p.transform.position.y, 0.02f, p.PointId);
                    var to = stand.position - p.transform.position; to.y = 0f;
                    Assert.Less(Vector3.Angle(p.transform.forward, to), 30f, p.PointId + " 面向台架");
                    float dx = Mathf.Abs(p.transform.position.x - Def.StandFrameCenterXZ.x) - Def.standSizeXZ.x / 2f, dz = Mathf.Abs(p.transform.position.z - Def.StandFrameCenterXZ.y) - Def.standSizeXZ.y / 2f;
                    Assert.AreEqual(Def.standStationOffset, Mathf.Max(dx, dz), 0.01f, p.PointId + " 離台架邊 0.6 m");
                }
                // 零件盒：場景裡只有一個 env.parts_tray（R1 位置），維修區只有空物件
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                Assert.AreEqual(1, Object.FindObjectsOfType<MonoBehaviour>(true).OfType<ISimObject>().Count(o => ((Component)o).gameObject.scene == scene && o.Id == SimIds.PartsTray));
                var anchor = all.FirstOrDefault(t => t.name == "Anchor_PartsTray_Maint");
                Assert.IsNotNull(anchor);
                Assert.AreEqual(0, anchor.GetComponents<Component>().Length - 1, "只是空物件");
                Assert.IsNotNull(all.FirstOrDefault(t => t.name == "ENV_WorkBench"));
            });
        }

        [Test]
        public void Cab_InLrvHeadCar_LowFloor()   // terry 2026-11-13：輕軌頭車駕駛室，低地板（推估軌面 ＋ 0.35）
        {
            Assert.AreEqual(Def.groundY + Def.floorAboveRail, Def.cabFloorY, 1e-4f, "軌面 ＋ 車廂地板");
            Assert.Greater(Def.cabCenter.z - Def.cabSize / 2f, -Def.carLength / 2f, "駕駛室在車頭裡");
            StationTests.WithScene(scene =>
            {
                var cab = Object.FindObjectsOfType<XrTeleportPoint>(true).First(p => p.gameObject.scene == scene && p.PointId == "tp.cab").transform.position;
                Assert.LessOrEqual(Mathf.Abs(cab.x), Def.RoofEdgeX);
                Assert.LessOrEqual(cab.z, Set.cabMaxZ);
                Assert.AreEqual(Def.cabFloorY, cab.y, 0.02f);
            });
        }
    }
}
