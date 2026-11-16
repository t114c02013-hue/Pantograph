using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.XR;
using PantographSim.Worksite;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>建模師 B gen_roof_boxes v1.1 交付後：地面教學箱（箱子＋鋼架 FBX）在場景裡的擺法、蓋子開法、字牌。</summary>
    public class TrainingBoxSceneTests
    {
        static RoofSceneDef Def => AssetDatabase.LoadAssetAtPath<RoofSceneDef>("Assets/_Project/Modules/Worksite/Data/RoofSceneDef.asset");

        static Bounds RB(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponent<TextMesh>() == null).ToList();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        [Test]
        public void Boxes_OnStands_FacingStations_LabelsFromData()
        {
            StationTests.WithScene(scene =>
            {
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                foreach (var tb in Def.trainingBoxes)
                {
                    var box = all.Single(t => t.name == "TrainingBox_" + tb.kind);
                    var stand = box.Find("Stand");
                    Assert.IsNotNull(stand, tb.kind + " 有鋼架 FBX");
                    Assert.AreEqual(Def.groundY, RB(stand).min.y, 0.01f, tb.kind + " 鋼架腳貼地");
                    Assert.AreEqual(Def.groundY + tb.standHeight, box.GetComponentsInChildren<Renderer>(true).First(r => r.name == "Body").bounds.min.y, 0.01f, tb.kind + " 箱底在鋼架頂");
                    var label = box.GetComponentsInChildren<Transform>(true).First(t => t.name == "Label_Name");
                    Assert.AreEqual(tb.facingNorth ? 1f : -1f, Mathf.Sign(label.position.z - tb.centerXZ.y), tb.kind + " 正面（銘牌）朝" + (tb.facingNorth ? "北" : "南"));
                    Assert.AreEqual(tb.displayName, label.GetComponentInChildren<TextMesh>().text, tb.kind + " 箱名字牌");
                    foreach (var n in new[] { "Label_Warn", "Lid_2_Label_Warn" })
                    {
                        var w = box.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                        if (string.IsNullOrEmpty(tb.warnText)) Assert.IsTrue(w == null || w.GetComponentInChildren<TextMesh>() == null, tb.kind + " 沒有警示牌字");
                        else Assert.AreEqual(tb.warnText, w.GetComponentInChildren<TextMesh>().text, tb.kind + " " + n);
                    }
                }
            });
        }

        [Test]
        public void HeadSolid_ClosedBoxSolid_OpenLidPeekNotDark_WallStillDark()   // 4c：Body 非凸網格＋蓋子全關時的箱內實心框
        {
            StationTests.WithScene(scene =>
            {
                var hs = HeadSolidDef.Load();
                ServiceLocator.Clear();
                var st = new SimState(new EventBus()); ServiceLocator.Register<ISimState>(st);
                try
                {
                    foreach (var box in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(t => t.name.StartsWith("TrainingBox_")).ToList())
                    {
                        var body = box.GetComponentsInChildren<Renderer>(true).First(r => r.name == "Body").bounds;
                        var peek = new Vector3(body.center.x, body.max.y - 0.15f, body.center.z);
                        var wall = new Vector3(body.center.x, body.center.y, body.max.z - 0.01f);
                        Physics.SyncTransforms();
                        Assert.IsTrue(StationLayoutCheck.HeadInSolid(peek, hs), box.name + "：蓋子全關，頭從上面壓進箱內要變暗");
                        Assert.IsTrue(StationLayoutCheck.HeadInSolid(wall, hs), box.name + "：穿過箱壁要變暗");
                        var lids = box.GetComponentsInChildren<LidToggle>(true);
                        foreach (var l in lids) st.Set(l.StateKey, LidToggle.Open);
                        foreach (var l in lids) l.Tick(5f);
                        Physics.SyncTransforms();
                        Assert.IsFalse(StationLayoutCheck.HeadInSolid(peek, hs), box.name + "：蓋子打開，從蓋口往下 0.15 m 探頭不變暗");
                        Assert.IsTrue(StationLayoutCheck.HeadInSolid(wall, hs), box.name + "：蓋子打開時穿過箱壁仍變暗");
                        foreach (var l in lids) st.Set(l.StateKey, LidToggle.Closed);
                        foreach (var l in lids) l.Tick(5f);
                    }
                }
                finally { ServiceLocator.Clear(); }
            });
        }

        [Test]
        public void Stations_FaceTheirBox_EveryHandleWithinReach()   // RA-31：站點在各箱正面、面向箱子；每片蓋子的把手離最近的教學箱站點水平 ≤ armReachHorizontal
        {
            StationTests.WithScene(scene =>
            {
                float reach = AssetDatabase.LoadAssetAtPath<StationSet>("Assets/_Project/Modules/Worksite/Data/StationSet.asset").armReachHorizontal;
                var pts = Object.FindObjectsOfType<XrTeleportPoint>(true).Where(p => p.gameObject.scene == scene && p.PointId.StartsWith("tp.maint.box_")).ToList();
                CollectionAssert.AreEquivalent(Def.trainingBoxStations.Select(s => s.id), pts.Select(p => p.PointId), "教學箱站點（box_c 拆成 box_cs、box_cn）");
                foreach (var l in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LidToggle>(true)))
                {
                    var h = l.GetComponentsInChildren<Renderer>(true).First(r => r.name.EndsWith("_Handle")).bounds.center;
                    var p = pts.OrderBy(x => Vector2.Distance(new Vector2(x.transform.position.x, x.transform.position.z), new Vector2(h.x, h.z))).First();
                    Assert.LessOrEqual(Vector2.Distance(new Vector2(p.transform.position.x, p.transform.position.z), new Vector2(h.x, h.z)), reach + 1e-4f, l.Id + " 把手搆得到（" + p.PointId + "）");
                    var to = h - p.transform.position; to.y = 0f;
                    Assert.Less(Vector3.Angle(p.transform.forward, to), 60f, p.PointId + " 面向箱子（" + l.Id + "）");
                }
            });
        }

        [Test]
        public void Lids_OpenUpward_ToOpenDeg_NoClashWithOtherBoxesOrOpenLids()
        {
            StationTests.WithScene(scene =>
            {
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                var boxes = all.Where(t => t.name.StartsWith("TrainingBox_")).ToList();
                var closed = boxes.ToDictionary(b => b, RB);
                var deg = typeof(LidToggle).GetField("openDeg", BindingFlags.NonPublic | BindingFlags.Instance);
                var lids = all.Select(t => t.GetComponent<LidToggle>()).Where(l => l != null).ToList();
                Assert.AreEqual(11, lids.Count, "空調、電池箱、變流器各 3 片，電阻箱 2 片");
                var open = new System.Collections.Generic.List<(LidToggle lid, Transform box, Bounds b)>();
                foreach (var l in lids)
                {
                    var box = l.GetComponentsInParent<Transform>(true).First(p => p.name.StartsWith("TrainingBox_"));
                    var tb = Def.trainingBoxes.Single(x => "TrainingBox_" + x.kind == box.name);
                    float want = tb.overrideLid > 0 && l.name == "Lid_" + tb.overrideLid ? tb.lidOpenDegOverride : Def.lidOpenDeg;
                    Assert.AreEqual(want, (float)deg.GetValue(l), 1e-3f, box.name + "/" + l.name + " 最大開啟角度");
                    var rest = l.transform.localRotation; float y0 = RB(l.transform).center.y;
                    l.transform.localRotation = rest * Quaternion.Euler(want, 0f, 0f);
                    var ob = RB(l.transform);
                    Assert.Greater(ob.center.y, y0 + 0.1f, box.name + "/" + l.name + " 繞局部 +X 正轉＝往上打開");
                    foreach (var o in boxes.Where(o => o != box)) Assert.IsFalse(closed[o].Intersects(ob), $"{box.name}/{l.name} 打開後碰到 {o.name}");
                    open.Add((l, box, ob));
                    l.transform.localRotation = rest;
                }
                foreach (var a in open) foreach (var b in open.Where(b => b.box != a.box)) Assert.IsFalse(a.b.Intersects(b.b), $"{a.box.name}/{a.lid.name} 和 {b.box.name}/{b.lid.name} 同時打開會互撞");
            });
        }
    }
}
