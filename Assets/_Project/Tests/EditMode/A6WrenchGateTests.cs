using System;
using System.Linq;
using NUnit.Framework;
using PantographSim.Data;
using PantographSim.Parts.Common;
using PantographSim.Parts.Mechanism;
using PantographSim.XR;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PantographSim.Tests
{
    /// <summary>R1.5a a6 增補 7：扳手把手高度與擺動淨空（D38）、絕緣子 collider、套筒（D39）、活動欄開口（組長）、吊帶角度例外，以及第 16 項的反例（把站點移開就不合格）。</summary>
    public class A6WrenchGateTests
    {
        const string Folder = "Assets/_Project/Modules/Worksite/Data";
        static StationSet Set => AssetDatabase.LoadAssetAtPath<StationSet>(Folder + "/StationSet.asset");
        static RoofSceneDef Def => AssetDatabase.LoadAssetAtPath<RoofSceneDef>(Folder + "/RoofSceneDef.asset");
        static SocketCatalog Catalog => AssetDatabase.LoadAssetAtPath<SocketCatalog>("Assets/_Project/Tools/Data/SocketCatalog.asset");
        static Transform Named(UnityEngine.SceneManagement.Scene s, string n) => s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == n);

        /// <summary>在降弓姿態跑一次 CheckScene（mutate 可以先改場景，量完一律還原）。</summary>
        static StationLayoutCheck.SceneResult Run(UnityEngine.SceneManagement.Scene scene, Action<System.Collections.Generic.Dictionary<string, XrTeleportPoint>> mutate = null)
        {
            var ids = StationTests.SceneIds(scene);
            var pts = Object.FindObjectsOfType<XrTeleportPoint>(true).Where(p => p.gameObject.scene == scene).ToDictionary(p => p.PointId);
            var saved = pts.ToDictionary(kv => kv.Key, kv => (kv.Value.transform.position, kv.Value.transform.rotation));
            var mech = Object.FindObjectOfType<PantographMechanism>(true);
            float lift = mech.Lift;
            mech.SetLift(0f);
            try
            {
                mutate?.Invoke(pts);
                Physics.SyncTransforms();
                return StationLayoutCheck.CheckScene(Set, Def, pts.Values, id => ids.TryGetValue(id ?? "", out var t) ? t : null, null, A6Tests.Wrench);
            }
            finally
            {
                foreach (var kv in saved) pts[kv.Key].transform.SetPositionAndRotation(kv.Value.position, kv.Value.rotation);
                mech.SetLift(lift);
            }
        }

        // ---------- 第 3、6 點：把手高度、門檻、絕緣子 ----------

        [Test]
        public void WrenchHeightTest_Bolt1_HandleCenter_IsVertexTopPlusSocketHeight()
        {
            StationTests.WithScene(scene =>
            {
                var bolt = StationTests.SceneIds(scene)["part.pan_base.bolt_1"];
                var sock = Catalog.Find(Catalog.SocketFor("part.pan_base.bolt_1"));
                Assert.AreEqual("socket.24", sock.socketId, "底座 M16 用 24 mm（FastenerDef.socketId）");
                float top = StationLayoutCheck.VertexBounds(bolt.GetComponentsInChildren<Renderer>(true)).Value.max.y;
                Assert.AreEqual(top + 0.04f, StationLayoutCheck.WrenchHandleY(bolt, sock), 0.001f, "把手中心＝螺栓頂點最高點＋套筒高 0.04");
            });
        }

        [Test]
        public void WrenchSweepThresholdTest_005Fails_002Passes()
        {
            var set = Set; float keep = set.wrenchSweepMinClearance;
            try
            {
                StationTests.WithScene(scene =>
                {
                    set.wrenchSweepMinClearance = 0.05f;
                    Assert.IsTrue(Run(scene).failures.Any(f => f.item == "21"), "門檻 0.05 時第 21 項要不合格");
                    set.wrenchSweepMinClearance = 0.02f;
                    var r = Run(scene);
                    Assert.IsFalse(r.failures.Any(f => f.item == "21"), "門檻 0.02 時第 21 項要合格：" + string.Join("\n", r.failures.Where(f => f.item == "21").Select(f => f.text)));
                });
            }
            finally { set.wrenchSweepMinClearance = keep; }
            Assert.AreEqual(keep, Set.wrenchSweepMinClearance, "測完要還原資料");
        }

        [Test]
        public void InsulatorColliderTopTest_TopEqualsInsulatorTop()
        {
            StationTests.WithScene(scene =>
            {
                var cols = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(true)).Where(c => c.name == "InsulatorCollision").ToList();
                Assert.AreEqual(4, cols.Count);
                for (int n = 1; n <= 4; n++)
                {
                    var top = Named(scene, "InsulatorTop_" + n).position;
                    var c = cols.OrderBy(x => Vector2.Distance(new Vector2(x.bounds.center.x, x.bounds.center.z), new Vector2(top.x, top.z))).First();
                    Assert.AreEqual(top.y, c.bounds.max.y, 0.001f, $"絕緣子 {n} collider 頂面");
                }
            });
        }

        /// <summary>E1 反例：扇形旁邊放一個測試 collider，距離 0.01 m 不合格、0.03 m 合格（直接呼叫檢查函式）。</summary>
        [Test]
        public void WrenchSweepCounterTest_TestCollider_001Fails_003Passes()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                go.transform.localScale = new Vector3(0.2f, 0.1f, 0.2f);
                var segs = new System.Collections.Generic.List<(string, Bounds)>();
                foreach (var (gap, pass) in new[] { (0.01f, false), (0.03f, true) })
                {
                    go.transform.position = new Vector3(0.2f, gap + 0.05f, 0f);   // 方塊底面在扇形平面 y＝0 上方 gap
                    Physics.SyncTransforms();
                    var (best, what, _) = StationLayoutCheck.WrenchSweepClearance(Vector3.zero, 0f, 0f, 0f, Set.wrenchSwingDeg, 0.45f, Set.wrenchRadialStep, Set.wrenchSampleDeg, segs, new[] { go.GetComponent<Collider>() }, null);
                    Assert.AreEqual(gap, best, 0.002f, "最小淨空");
                    Assert.AreEqual(pass, best >= Set.wrenchSweepMinClearance, $"距離 {gap} m 應該{(pass ? "合格" : "不合格")}");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---------- 第 2 點：套筒 ----------

        [Test]
        public void Sockets_HeightDiameter_AndTipOnMountedWrench()
        {
            GameObject Pf(string p) { var g = AssetDatabase.LoadAssetAtPath<GameObject>(p); Assert.IsNotNull(g, p); return g; }
            Bounds B(GameObject g) { var i = (GameObject)PrefabUtility.InstantiatePrefab(g); try { var rs = i.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; } finally { Object.DestroyImmediate(i); } }
            var b24 = B(Pf("Assets/_Project/Tools/Sockets/Prefabs/TL_Socket_24.prefab"));
            Assert.AreEqual(0.040f, b24.size.y, 0.001f); Assert.AreEqual(0.033f, b24.size.x, 0.001f);
            Assert.AreEqual(0.035f, B(Pf("Assets/_Project/Tools/Sockets/Prefabs/TL_Socket_19.prefab")).size.y, 0.001f);
            var wrench = (GameObject)PrefabUtility.InstantiatePrefab(Pf("Assets/_Project/Tools/TorqueWrench/Prefabs/PF_TorqueWrench_L.prefab"));
            try
            {
                var mount = wrench.transform.Find("Socket_Mount");
                var sock = (GameObject)PrefabUtility.InstantiatePrefab(Pf("Assets/_Project/Tools/Sockets/Prefabs/TL_Socket_24.prefab"));
                sock.transform.SetParent(mount, false);
                Assert.AreEqual(Vector3.zero, sock.transform.localPosition);
                var tip = sock.transform.Find("Tip");
                Assert.AreEqual(0.040f, Vector3.Distance(tip.position, mount.position), 0.001f, "Tip 到 Socket_Mount＝套筒高");
                Assert.LessOrEqual(Vector3.Angle(tip.forward, -mount.up), 1f, "Tip +Z 和 Socket_Mount −Y 夾角");
            }
            finally { Object.DestroyImmediate(wrench); }
            foreach (var s in Catalog.sockets.Where(x => x.socketId == "socket.19" || x.socketId == "socket.24"))
                Assert.AreEqual(1, Catalog.sockets.Count(x => x.prefab == s.prefab), "19／24 在兩支扳手共用同一筆（數值只寫一次）");
        }

        // ---------- 第 8 點：活動欄開口 ----------

        [Test]
        public void GateOpeningTest_CounterExamples()
        {
            float o0 = -1.4f, o1 = -0.75f;
            Assert.IsTrue(StationLayoutCheck.GateOpening(o0, o1, o0, o1, 0.60f).ok, "現況：開口 0.65、跨板剛好落在開口內");
            Assert.IsFalse(StationLayoutCheck.GateOpening(o0, o1, o0 + 0.05f, o1 + 0.05f, 0.60f).ok, "跨板往擋牆方向移 0.05 m 要不合格");
            Assert.IsFalse(StationLayoutCheck.GateOpening(o0, o0 + 0.55f, o0, o0 + 0.55f, 0.60f).ok, "開口縮到 0.55 m 要不合格");
        }

        [Test]
        public void GateSliceGuard_Visible_CoversWall_Height1_OpeningAtLeast065()
        {
            StationTests.WithScene(scene =>
            {
                var guard = Named(scene, "ENV_GateSliceGuard");
                Assert.IsNotNull(guard, "要有看得見的擋欄");
                var rs = guard.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToList();
                Assert.IsNotEmpty(rs, "擋欄要有啟用中的 MeshRenderer");
                var gb = rs[0].bounds; foreach (var r in rs) gb.Encapsulate(r.bounds);
                var wall = Named(scene, "EdgeWall_-X_GateSlice").GetComponent<Collider>().bounds;
                Assert.AreEqual(1.00f, gb.size.y, 0.01f, "高 1.00");
                Assert.LessOrEqual(gb.min.z, wall.min.z + 0.01f); Assert.GreaterOrEqual(gb.max.z, wall.max.z - 0.01f);
                Assert.GreaterOrEqual(gb.min.z, wall.min.z - 0.01f, "網格不比 collider 更往開口內伸");
                Assert.GreaterOrEqual(wall.min.z - (Def.gateCenterZ - Def.gateWidth / 2f), 0.65f - 0.001f, "開口淨寬 ≥ 0.65");
            });
        }

        // ---------- E3(8)：吊帶角度不合格會丟例外 ----------

        [Test]
        public void SlingAngles_TooShortSling_Throws()
        {
            var t = PanLiftMath.LoadDefault();
            var pts = new[] { new Vector3(-0.5f, 0, -0.86f), new Vector3(0.5f, 0, -0.86f), new Vector3(0.5f, 0, 0.86f), new Vector3(-0.5f, 0, 0.86f) };
            Assert.DoesNotThrow(() => PanLiftMath.AssertSlingAngles(Vector3.zero, pts, t), "1.25 m 合格");
            t.slingLength = 0.95f;   // 和垂直線 ≈ 65°
            Assert.Throws<InvalidOperationException>(() => PanLiftMath.AssertSlingAngles(Vector3.zero, pts, t));
        }

        // ---------- E2：第 16 項的反例（把站點移開就要不合格） ----------

        static bool Fails(StationLayoutCheck.SceneResult r, string item, string contains) => r.failures.Any(f => f.item == item && f.text.Contains(contains));

        [Test]
        public void Item16_CounterExamples_MoveStationAway_Fails()
        {
            StationTests.WithScene(scene =>
            {
                Assert.IsFalse(Run(scene).failures.Any(f => f.item == "16"), "現況第 16 項全過");
                // (b) .s 往外移 0.3 m：到螺栓超過 0.70
                Assert.IsTrue(Fails(Run(scene, p => p["tp.roof.base_w.s"].transform.position += new Vector3(-0.3f, 0, 0)), "16", "(b) tp.roof.base_w.s"), "(b) 距離反例");
                // (a) .n 往外移 0.3 m：到螺栓超過 0.60
                Assert.IsTrue(Fails(Run(scene, p => p["tp.roof.base_e.n"].transform.position += new Vector3(0.3f, 0, 0)), "16", "(a) tp.roof.base_e.n"), "(a) 距離反例");
                // (c) 轉 40°：吊耳不在 ±30° 內
                Assert.IsTrue(Fails(Run(scene, p => p["tp.roof.base_e.n"].transform.rotation *= Quaternion.Euler(0, 40, 0)), "16", "(c) tp.roof.base_e.n"), "(c) 角度反例");
                // (h) .s 移到 Z −0.25：碰到羊角外擴身體半徑
                Assert.IsTrue(Fails(Run(scene, p => p["tp.roof.base_w.s"].transform.position = new Vector3(-0.85f, 0, -0.25f)), "16", "(h) tp.roof.base_w.s"), "(h) 羊角反例");
                // (j) base_e.n 移到 X 1.9：伸手時頭部 |X| 超過 1.0
                Assert.IsTrue(Fails(Run(scene, p => p["tp.roof.base_e.n"].transform.position += new Vector3(1.05f, 0, 0)), "16", "(j) tp.roof.base_e.n"), "(j) 頭部反例");
            });
        }

        /// <summary>D37（PartClearRadiusTest 的反例）：.s 到羊角升降範圍 0.363 m；同一個距離對平台站點（0.4）就不夠。</summary>
        [Test]
        public void PartClearRadius_CounterExample_SameDistanceFailsForPlatform()
        {
            float d = 0.363f;
            Assert.GreaterOrEqual(d, Set.ClearRadiusFor("PanHead", StationFloor.Roof), "車頂 .s 合格");
            Assert.Less(d, Set.ClearRadiusFor("PanHead", StationFloor.Platform), "同距離放在平台站點要不合格");
            Assert.Less(d, Set.ClearRadiusFor("UpperArm", StationFloor.Roof), "同距離對其他會動零件（0.4）要不合格");
        }
    }
}
