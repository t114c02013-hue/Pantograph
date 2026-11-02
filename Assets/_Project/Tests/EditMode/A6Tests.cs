using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Parts.Common;
using PantographSim.Parts.Mechanism;
using PantographSim.XR;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>R1.5a a6：底座四角編號、吊運時傳送封鎖、車頂可走範圍與升降範圍、增補 2 的三個 FBX、G2 台架、存檔時的顯示狀態、質量表。</summary>
    public class A6Tests
    {
        const string Folder = "Assets/_Project/Modules/Worksite/Data";
        const string Generated = "Assets/_Project/Worksite/Models/Generated";
        static StationSet Set => AssetDatabase.LoadAssetAtPath<StationSet>(Folder + "/StationSet.asset");
        static RoofSceneDef Def => AssetDatabase.LoadAssetAtPath<RoofSceneDef>(Folder + "/RoofSceneDef.asset");
        /// <summary>第21項的扳手把手長（ToolDef tool.socket_wrench 的 handleLength）。</summary>
        public static float WrenchHandle => float.Parse(StepJsonImporterAccess.Tool("tool.socket_wrench").Param("handleLength", "0"), System.Globalization.CultureInfo.InvariantCulture);

        static int Corner(Vector3 local) => local.z < 0f ? (local.x < 0f ? 1 : 2) : (local.x < 0f ? 4 : 3);
        static Transform Named(UnityEngine.SceneManagement.Scene s, string n) => s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == n);

        /// <summary>model-spec v2.3 §7.7.1：每一類的 n 都符合正負號（1 −X−Z、2 +X−Z、3 +X+Z、4 −X+Z，PAN_Root 局部）；InsulatorTop_n 到 BaseHole_n ≤ 2 mm。</summary>
        [Test]
        public void BaseCorners_NumberedBySign_InsulatorTopsWithin2mm()
        {
            StationTests.WithScene(scene =>
            {
                var root = PanAssy.Root(scene);
                var ids = StationTests.SceneIds(scene);
                for (int n = 1; n <= 4; n++)
                {
                    var hole = root.Find("PAN_Base/BaseHole_" + n).position;
                    foreach (var (label, p) in new[] { ("BaseHole", hole), ("LocatingHole", root.Find("PAN_Base/LocatingHole_" + n).position),
                        ("lift_point", ids["ip.pan_assy.lift_point_" + n].position), ("part.pan_base.bolt", ids["part.pan_base.bolt_" + n].position),
                        ("InsulatorTop", Named(scene, "InsulatorTop_" + n).position), ("insulatorPositions", Def.insulatorPositions[n - 1]) })
                        Assert.AreEqual(n, Corner(root.InverseTransformPoint(p)), $"{label}_{n} 的正負號");
                    Assert.LessOrEqual(Vector3.Distance(Named(scene, "InsulatorTop_" + n).position, hole), 0.002f, $"InsulatorTop_{n} 到 BaseHole_{n}");
                    // 台架上的定位銷與對位燈：相對台架中心
                    var c = new Vector3(Def.standCenterXZ.x, 0f, Def.standCenterXZ.y);
                    foreach (var label in new[] { "StandSeat_", "AlignLight_" })
                    {
                        var t = Named(scene, label + n);
                        Assert.IsNotNull(t, label + n);
                        Assert.AreEqual(n, Corner(t.position - c), $"{label}{n} 的正負號");
                    }
                }
            });
        }

        /// <summary>G2：定位銷 StandSeat_n 在 BaseHole_n 的實測位置（PAN_Root 局部、底架中心對準台架中心），差 ≤ 1 mm；支撐座＝BaseHole 間距＋standSeatMarginXZ。</summary>
        [Test]
        public void StandPins_MatchBaseHoles_Within1mm_SeatSizeDerived()
        {
            StationTests.WithScene(scene =>
            {
                var root = PanAssy.Root(scene);
                var c = new Vector3(Def.standCenterXZ.x, 0f, Def.standCenterXZ.y);
                var holes = Enumerable.Range(1, 4).Select(i => root.InverseTransformPoint(root.Find("PAN_Base/BaseHole_" + i).position)).ToArray();
                for (int n = 1; n <= 4; n++)
                {
                    var pin = Named(scene, "StandSeat_" + n).position - c;
                    Assert.LessOrEqual(new Vector2(pin.x - holes[n - 1].x, pin.z - holes[n - 1].z).magnitude, 0.001f, "StandSeat_" + n);
                }
                var seat = Named(scene, "StandSeatBlock").GetComponent<Collider>().bounds;
                Assert.AreEqual(holes[1].x - holes[0].x + Def.standSeatMarginXZ.x, seat.size.x, 0.002f, "支撐座 X");
                Assert.AreEqual(holes[3].z - holes[0].z + Def.standSeatMarginXZ.y, seat.size.z, 0.002f, "支撐座 Z");
                Assert.AreEqual(Def.StandTopY, seat.max.y, 0.002f, "支撐座頂面不變");
            });
        }

        /// <summary>第七節 1：吊物懸空時，傳送到吊運路徑內的任何站點都被擋下；路徑外可到的只有 allowedWhileLifted。</summary>
        [Test]
        public void WhileLoadLifted_TeleportIntoLiftPath_IsBlocked()
        {
            var d = Def; var set = Set;
            int inside = 0;
            foreach (var s in set.stations.Where(x => x != null))
            {
                Assert.IsTrue(s.TeleportAllowed(false), s.id + "：吊物沒懸空時照常開放");
                bool inPath = StationLayoutCheck.CorridorDistance(Vector2.zero, d.standCenterXZ, set.liftCorridorHalfWidth, s.WorldXZ(d)) <= 0f;
                if (inPath) { inside++; Assert.IsFalse(s.TeleportAllowed(true), s.id + " 在吊運路徑內，吊物懸空時要擋下"); }
                if (s.TeleportAllowed(true)) Assert.IsFalse(inPath, s.id + " 吊物懸空時可到，不能在路徑內");
            }
            Assert.Greater(inside, 0, "至少有一站在路徑內（不然這個測試沒測到東西）");
            Assert.IsTrue(set.Find("tp.platform.crane").TeleportAllowed(true), "tp.platform.crane 吊運時要能到");
        }

        static List<Collider> RoofWalkables(UnityEngine.SceneManagement.Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(true))
            .Where(c => c.gameObject.layer == LayerMask.NameToLayer(SceneLayers.Walkable) && c.transform.parent != null && c.transform.parent.parent != null && c.transform.parent.parent.name == "ENV_RoofSection").ToList();

        static bool OverlapXZ(Bounds a, Bounds b) => a.min.x < b.max.x - 1e-3f && a.max.x > b.min.x + 1e-3f && a.min.z < b.max.z - 1e-3f && a.max.z > b.min.z + 1e-3f;

        /// <summary>升降範圍（組長條件）：(a) 5 個姿態下 Walkable 都不和會動的段（LowerArm／UpperArm／PanHead／Rod*）的頂點範圍相交；(c) 底架頂點範圍內沒有 Walkable。</summary>
        [Test]
        public void RoofWalkable_ClearOfJointVertexRanges_AllLiftSamples_AndNotOnBase()
        {
            StationTests.WithScene(scene =>
            {
                var root = PanAssy.Root(scene);
                var mech = root.GetComponentInChildren<PantographMechanism>(true);
                var walk = RoofWalkables(scene);
                Assert.IsNotEmpty(walk);
                float lift = mech.Lift;
                try
                {
                    var segs = new[] { "LowerArm", "UpperArm", "PanHead" }.Concat(mech.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Rod")).Select(t => t.name)).Distinct().ToList();
                    for (int i = 0; i < 5; i++)
                    {
                        mech.SetLift(i / 4f);
                        foreach (var seg in segs)
                        {
                            var b = StationLayoutCheck.VertexBounds(StationLayoutCheck.JointRenderers(root, seg));
                            Assert.IsTrue(b.HasValue, seg);
                            foreach (var w in walk) Assert.IsFalse(OverlapXZ(w.bounds, b.Value), $"升降 {i / 4f:0.00}：{w.name} 和 {seg} 的頂點範圍相交");
                        }
                    }
                    mech.SetLift(0f);
                    var baseV = StationLayoutCheck.VertexBounds(StationLayoutCheck.JointRenderers(root, "Base")).Value;
                    foreach (var w in walk) Assert.IsFalse(OverlapXZ(w.bounds, baseV), w.name + " 在底架頂點範圍內");
                }
                finally { mech.SetLift(lift); }
            });
        }

        /// <summary>(b) 四個子站點與 base_w／base_e 都在放寬後的 Walkable 上（等裁定的站點只警告）；放寬只在底架兩側，|X| ≤ roofBaseWalkHalfWidth。</summary>
        [Test]
        public void RoofBaseStations_OnWidenedWalkable()
        {
            StationTests.WithScene(scene =>
            {
                Physics.SyncTransforms();
                var pts = Object.FindObjectsOfType<XrTeleportPoint>(true).Where(p => p.gameObject.scene == scene).ToDictionary(p => p.PointId);
                foreach (var id in new[] { "tp.roof.base_w", "tp.roof.base_e", "tp.roof.base_w.s", "tp.roof.base_e.s", "tp.roof.base_e.n", "tp.roof.base_w.n" })
                {
                    Assert.IsTrue(pts.ContainsKey(id), id);
                    var p = pts[id].transform.position;
                    bool on = Physics.Raycast(p + Vector3.up, Vector3.down, 1.5f, SceneLayers.WalkableMask, QueryTriggerInteraction.Ignore);
                    if (Set.pendingRulings.Any(r => r.Matches("core", id))) { Assert.IsFalse(on, id + " 已經在 Walkable 上：請把這筆從 PendingRuling 刪掉"); continue; }
                    Assert.IsTrue(on, id + " 要在 Walkable 上");
                    Assert.LessOrEqual(Mathf.Abs(p.x), Def.roofBaseWalkHalfWidth - Set.invisibleWallClearance + 1e-3f, id + " 離放寬邊界 ≥ 0.15 m");
                }
                foreach (var w in RoofWalkables(scene)) Assert.LessOrEqual(Mathf.Max(Mathf.Abs(w.bounds.min.x), Mathf.Abs(w.bounds.max.x)), Def.roofBaseWalkHalfWidth + 1e-3f, w.name);
            });
        }

        /// <summary>增補 2 第一部分第 2 點：存檔時的顯示＝訓練開始時的現場狀態；暫放墊 inactive；腰包、去駕駛室光圈存在。</summary>
        [Test]
        public void SavedScene_InitialVisibility_MatchesTrainingStart()
        {
            StationTests.WithScene(scene =>
            {
                Assert.IsTrue(Named(scene, "DustCap_Off").gameObject.activeSelf, "接頭接著：DustCap_Off 顯示");
                Assert.IsFalse(Named(scene, "DustCap_On").gameObject.activeSelf);
                Assert.IsFalse(Named(scene, "HoldDown_Installed").gameObject.activeSelf, "固定帶在工具袋：Installed 隱藏");
                Assert.IsFalse(Named(scene, "HoldDown_Removed").gameObject.activeSelf, "固定帶在工具袋：Removed 隱藏");
                Assert.IsTrue(Named(scene, "HoldDown_BuckleBase_A").gameObject.activeSelf && Named(scene, "HoldDown_BuckleBase_B").gameObject.activeSelf, "扣座一直顯示");
                foreach (var n in new[] { "Ctrl_PlugPoint", "HoldDown_TiePoint", "StripMat_Surface", "StripMat_Slot_Old", "mk.platform.to_cab", "WaistPouch", "Anchor_SpareShelf_Daily", "Anchor_ScrapBox_Daily" })
                    Assert.IsNotNull(Named(scene, n), n);
                Assert.IsNull(Named(scene, "StripMat_Slot_New"), "沒有 Slot_New");
                var mat = Named(scene, "ENV_RoofStripMat");
                Assert.IsFalse(mat.gameObject.activeSelf, "暫放墊存檔時 inactive（日常課程才打開）");
                var ids = StationTests.SceneIds(scene);
                Assert.IsTrue(ids.ContainsKey("env.roof_strip_mat") && ids.ContainsKey("env.waist_pouch"));
                Assert.IsFalse(Set.stations.Any(s => s.id.StartsWith("mk.") || s.id == "env.waist_pouch"), "光圈、腰包不是站點");
            });
        }

        /// <summary>增補 2 第二部分第 4 節：三個新 FBX 的外形。</summary>
        [Test]
        public void NewFbx_CtrlConnector_HoldDownStrap_RoofStripMat_Shapes()
        {
            GameObject Load(string n) { var g = AssetDatabase.LoadAssetAtPath<GameObject>($"{Generated}/{n}.fbx"); Assert.IsNotNull(g, n + ".fbx（跑 gen_700t.py）"); return g; }
            Bounds VB(GameObject g, string child)
            {
                var go = (GameObject)Object.Instantiate(g);
                try
                {
                    var t = child == null ? go.transform : go.GetComponentsInChildren<Transform>(true).First(x => x.name == child);
                    return StationLayoutCheck.VertexBounds(t.GetComponentsInChildren<Renderer>(true)).Value;
                }
                finally { Object.DestroyImmediate(go); }
            }
            var cc = Load("PAN_CtrlConnector");
            Assert.IsEmpty(cc.GetComponentsInChildren<Collider>(true), "接頭 FBX 不帶 collider");
            Assert.AreEqual(Vector3.one, cc.transform.localScale);
            foreach (var n in new[] { "Connector_Body", "DustCap_On", "DustCap_Off" }) Assert.IsTrue(cc.GetComponentsInChildren<Transform>(true).Any(t => t.name == n), n);
            var body = VB(cc, "Connector_Body");
            Assert.AreEqual(0.15f, body.size.x, 0.01f, "座＋插座 X"); Assert.AreEqual(0.08f, body.size.y, 0.01f); Assert.AreEqual(0.08f, body.size.z, 0.01f);

            var hd = Load("PAN_HoldDownStrap");
            foreach (var n in new[] { "HoldDown_BuckleBase", "HoldDown_Saddle", "HoldDown_Leg", "HoldDown_Removed" }) Assert.IsTrue(hd.GetComponentsInChildren<Transform>(true).Any(t => t.name == n), n);
            Assert.AreEqual(1.00f, VB(hd, "HoldDown_Leg").size.y, 0.01f, "Leg 單位長");
            var saddle = VB(hd, "HoldDown_Saddle");
            Assert.AreEqual(Def.strapArmHalfWidth, (saddle.size.x - 2f * Def.strapThickness) / 2f, 0.002f, "Saddle 內徑＝armHalfWidth");

            var mat = Load("ENV_RoofStripMat");
            var mb = VB(mat, null);
            Assert.AreEqual(1.20f, mb.size.x, 0.005f); Assert.AreEqual(0.03f, mb.size.y, 0.005f); Assert.AreEqual(0.30f, mb.size.z, 0.005f);
            Assert.AreEqual(0.03f, mb.max.y, 0.002f, "邊框頂 Y 0.03");
            Assert.LessOrEqual(mat.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh.triangles.Length / 3), 200);
        }

        /// <summary>建模 B 12.6：質量表 total＝各關節加總（120 kg），欄位照規格；COG 由頂點平均算，吊鉤掛點在 COG 正上方。</summary>
        [Test]
        public void MassTable_ParsesJoints_TotalMatches()
        {
            var t = PanLiftMath.LoadDefault();
            CollectionAssert.AreEquivalent(new[] { "Base", "LowerArm", "UpperArm", "PanHead" }, t.groups.Select(g => g.joint));
            Assert.AreEqual(120f, t.total, 1e-3f);
            Assert.AreEqual(t.total, t.groups.Sum(g => g.massKg), 1e-3f);
            var parsed = PanLiftMath.Load("{\"total\":3,\"joints\":{\"A\":1.5,\"B\":1.5}}", "{\"maxTiltDeg\":2}");
            Assert.AreEqual(2, parsed.groups.Length); Assert.AreEqual(1.5f, parsed.groups[1].massKg, 1e-4f);
        }

        /// <summary>D30：上鎖掛牌組多一個位置「在腰包」（資料）；開始時照舊在工具架 slot_1。</summary>
        [Test]
        public void LockoutTag_HasWaistPouchLocation()
        {
            var tool = StepJsonImporterAccess.Tool("tool.lockout_tag");
            Assert.IsNotNull(tool);
            CollectionAssert.Contains(tool.Param("locations").Split(','), "在腰包");
            Assert.AreEqual(1, tool.hotkey, "開始時在 slot_1");
        }
    }

    static class StepJsonImporterAccess
    {
        public static ToolDefinition Tool(string id) => AssetDatabase.FindAssets("t:ToolDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ToolDefinition>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(t => t != null && t.toolId == id);
    }
}
