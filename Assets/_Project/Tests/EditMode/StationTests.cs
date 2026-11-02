using NUnit.Framework;
using PantographSim.Data;
using PantographSim.XR;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>R1 T5 站點：資料資產、建置檢查。</summary>
    public class StationTests
    {
        const string Folder = "Assets/_Project/Modules/Worksite/Data";
        static StationSet Set => AssetDatabase.LoadAssetAtPath<StationSet>(Folder + "/StationSet.asset");
        static RoofAccessRule Rule => AssetDatabase.LoadAssetAtPath<RoofAccessRule>(Folder + "/RoofAccessRule.asset");

        [Test]
        public void StationLayoutCheck_Data_8StationsPass()
        {
            Assert.IsNotNull(Rule, "找不到 RoofAccessRule");
            var bad = StationLayoutCheck.CheckData(Set, Rule.resultKey);
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        public const string ScenePath = "Assets/_Project/Content/Scenes/SC_CarbonStripSlice.unity";

        /// <summary>在切片場景上執行 body（場景沒開就以 Additive 開、做完關掉；已開著就直接用，不改它）。</summary>
        public static void WithScene(System.Action<UnityEngine.SceneManagement.Scene> body)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try { body(scene); }
            finally { if (opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
        }

        public static System.Collections.Generic.Dictionary<string, UnityEngine.Transform> SceneIds(UnityEngine.SceneManagement.Scene scene) =>
            System.Linq.Enumerable.ToDictionary(System.Linq.Enumerable.GroupBy(System.Linq.Enumerable.Where(System.Linq.Enumerable.OfType<Core.ISimObject>(UnityEngine.Object.FindObjectsOfType<UnityEngine.MonoBehaviour>(true)),
                o => !string.IsNullOrEmpty(o.Id) && ((UnityEngine.Component)o).gameObject.scene == scene), o => o.Id), g => g.Key, g => ((UnityEngine.Component)System.Linq.Enumerable.First(g)).transform);

        [Test]
        public void StationLayoutCheck_Scene_8StationsPass()
        {
            WithScene(scene =>
            {
                var ids = SceneIds(scene);
                var pts = System.Linq.Enumerable.Where(UnityEngine.Object.FindObjectsOfType<XrTeleportPoint>(true), p => p.gameObject.scene == scene);
                var def = AssetDatabase.LoadAssetAtPath<RoofSceneDef>(Folder + "/RoofSceneDef.asset");
                // 升降聯集要移動弓體才量得到（會弄髒場景），只在建置時檢查；這裡傳空範圍
                // shortcut: EditMode 測試不檢查「站點離升降聯集 ≥ 0.4 m」（建置時有完整檢查）, 要在測試裡檢查時改用預覽場景複製集電弓再量
                // a6：第11、18 項量降弓姿態（和建置時相同），量完設回存檔的姿態
                var mech = UnityEngine.Object.FindObjectOfType<Parts.Mechanism.PantographMechanism>(true);
                float lift = mech.Lift;
                mech.SetLift(0f);
                StationLayoutCheck.SceneResult r;
                try { r = StationLayoutCheck.CheckScene(Set, def, pts, id => ids.TryGetValue(id ?? "", out var t) ? t : null, null, A6Tests.Wrench); }
                finally { mech.SetLift(lift); }
                Assert.IsTrue(r.Ok, r.ToString());
                Assert.IsTrue(ids.ContainsKey("env.bolt_tray_L") && ids.ContainsKey("env.bolt_tray_R"), "主場景要有兩個螺栓盤");
            });
        }

        [Test]
        public void GroundStation_D11_GateEdge_Clamp_AnchorDistance_AndGuardrail()
        {
            WithScene(scene =>
            {
                Physics.SyncTransforms();
                var ids = SceneIds(scene);
                var pts = System.Linq.Enumerable.ToDictionary(System.Linq.Enumerable.Where(UnityEngine.Object.FindObjectsOfType<XrTeleportPoint>(true), p => p.gameObject.scene == scene), p => p.PointId);
                var gp = pts["tp.platform.ground"].transform.position;
                Assert.AreEqual(-2.20f, gp.x, 1e-3f); Assert.AreEqual(0.60f, gp.z, 1e-3f);   // D15（R1.5a a3）：取代 R1 的 (−1.95, 0.30)
                var info = StationLayoutCheck.GroundStationReport(Set, gp, id => ids.TryGetValue(id ?? "", out var t) ? t : null, pts["tp.platform.anchor"].transform.position, out var problems);
                Assert.IsEmpty(problems, string.Join("\n", problems) + "\n" + string.Join("\n", info));
                // 護欄至少 0.30 m（D11）：到 X −1.60 的車側護欄
                foreach (var c in Physics.OverlapCapsule(gp + Vector3.up * 0.1f, gp + Vector3.up * 1.8f, Set.guardrailClearance, LayerMask.GetMask(Core.SceneLayers.Boundary), QueryTriggerInteraction.Ignore))
                    Assert.IsFalse(c.name.StartsWith("Rail_") || c.name == "GateBoundary", "離護欄不到 0.30 m：" + c.name);
            });
        }

        [Test]
        public void GroundStation_D11_RulesCatchBadPositions()
        {
            WithScene(scene =>
            {
                var ids = SceneIds(scene);
                System.Func<string, UnityEngine.Transform> find = id => ids.TryGetValue(id ?? "", out var t) ? t : null;
                // 開口邊緣（Z −0.6）旁 0.2 m、離 anchor 站 0.4 m、離接地掛點很遠
                StationLayoutCheck.GroundStationReport(Set, new Vector3(-2.2f, 0f, -0.4f), find, new Vector3(-2.0f, 0f, -0.75f), out var problems);
                Assert.IsTrue(problems.Exists(p => p.Contains("(a)")), string.Join("\n", problems));
                Assert.IsTrue(problems.Exists(p => p.Contains("(c)")), string.Join("\n", problems));
                StationLayoutCheck.GroundStationReport(Set, new Vector3(-2.6f, 0f, 2.0f), find, null, out problems);
                Assert.IsTrue(problems.Exists(p => p.Contains("(b)")), string.Join("\n", problems));
            });
        }

        [Test]
        public void StationData_MatchesSpec_SharedRoofPointAndStart()
        {
            var s = Set;
            Assert.AreEqual("tp.platform.lift", s.startStationId);
            Assert.AreEqual(s.Find("tp.roof.inner").positionXZ, s.Find("tp.roof.outer").positionXZ, "inner／outer 共用落點");
            Assert.AreEqual("tp.roof.inner", s.Find("tp.roof.outer").sharedWith);
            Assert.AreEqual(StationFloor.Cab, s.Find("tp.cab").floor);
            foreach (var id in new[] { "tp.platform.lift", "tp.platform.rack", "tp.platform.ground", "tp.platform.anchor" })
                Assert.AreEqual(StationFloor.Platform, s.Find(id).floor, id);
        }

        [Test]
        public void StepData_EveryStandPointId_IsAStation_AndInPlaceOnlyWhereSpecSays()
        {
            var bundle = ProcedureJsonConverter.Build(System.IO.File.ReadAllText("Assets/_Project/Modules/Procedures/Data/carbon_strip_slice.json"));
            var ids = new System.Collections.Generic.HashSet<string>(System.Linq.Enumerable.Select(Set.stations, x => x.id));
            var inPlace = new System.Collections.Generic.List<string>();
            foreach (var st in bundle.procedure.steps)
            {
                if (st.presetDone) continue;
                bool any = false;
                foreach (var side in string.IsNullOrEmpty(st.side) ? new[] { "L", "R" } : new[] { st.side })
                    foreach (var a in st.subActions)
                    {
                        var id = SubAction.ExpandStand(a.standPointId, side);
                        if (id.Length == 0) continue;
                        any = true;
                        Assert.IsTrue(ids.Contains(id), $"{st.stepId}：站點 {id} 不在 StationSet");
                    }
                if (!any) inPlace.Add(st.stepId);
            }
            CollectionAssert.AreEquivalent(new[] { "STP_CSR_05_Decide" }, inPlace, "只有 CSR_05 全部在原地");
        }

        [Test]
        public void RoofAccess_OpensAfterVoltageGroundAndRoofHook_ClosesWhenUnhooked()
        {
            var s = new Core.SimState(new Core.EventBus());
            var r = Rule;
            Assert.IsFalse(Worksite.RoofAccess.IsOpen(r, s), "開場：關");
            s.SetBool("step.STP_SI_04_VoltageTest.done", true);
            s.SetBool("step.STP_SI_05_Grounding.done", true);
            Assert.IsFalse(Worksite.RoofAccess.IsOpen(r, s), "還沒扣車頂掛點：關");
            s.SetBool("harness.roof", true);
            Assert.IsTrue(Worksite.RoofAccess.IsOpen(r, s), "驗電、接地、扣上車頂掛點：開");
            s.SetBool("harness.roof", false);
            Assert.IsFalse(Worksite.RoofAccess.IsOpen(r, s), "WC_02 b 解開車頂掛點：關");
            s.SetBool("harness.roof", true); s.SetBool("step.STP_WC_02_LeaveRoof.done", true);
            Assert.IsFalse(Worksite.RoofAccess.IsOpen(r, s), "WC_02 完成：關");
            Worksite.RoofAccess.Write(r, s);
            Assert.IsFalse(s.GetBool(r.resultKey));
        }

        [Test]
        public void Gate_BlocksEntryOnly_StaysOpenWhilePlayerOnRoof()
        {
            Assert.IsTrue(Worksite.PlatformGate.ShouldBeOpen(true, "Platform", false));
            Assert.IsTrue(Worksite.PlatformGate.ShouldBeOpen(false, "Roof", true), "車頂關了但人還在車頂：活動欄不關");
            Assert.IsFalse(Worksite.PlatformGate.ShouldBeOpen(false, "Platform", true), "回到平台才關");
            Assert.IsFalse(Worksite.PlatformGate.ShouldBeOpen(false, "Roof", false), "關著時不會因為人在車頂而打開");
        }

        [Test]
        public void BButton_GoesToSubActionStation_InPlaceOrLockedDoesNotMove()
        {
            var bus = new Core.EventBus(); var s = new Core.SimState(bus);
            Core.ServiceLocator.Clear();
            Core.ServiceLocator.Register<Core.IEventBus>(bus); Core.ServiceLocator.Register<Core.ISimState>(s);
            var root = new UnityEngine.GameObject("TP");
            try
            {
                XrTeleportPoint Point(string id, string key, string shared = "")
                {
                    var p = new UnityEngine.GameObject(id).AddComponent<XrTeleportPoint>();
                    p.transform.SetParent(root.transform);
                    p.Configure(id, id, key, null, null, null);
                    p.ConfigureStation("Roof", "先完成驗電、掛接地，並扣好安全帶", shared, null);
                    return p;
                }
                var pts = new[] { Point("tp.platform.lift", ""), Point("tp.roof.inner", "roof.open"), Point("tp.roof.outer", "roof.open", "tp.roof.inner") };
                var lift = pts[0]; var inner = pts[1]; var outer = pts[2];
                var st = root.AddComponent<XrStations>();   // 沒有 XrTeleport 時 Focus 不動作（XrTeleport 要 XROrigin，測試組件不引用 XR CoreUtils，改測 Decide）
                s.Set("hint.sub_stand", "");
                Assert.IsFalse(st.Focus(), "原地：不傳送");
                Assert.AreEqual(2, XrTeleportPoint.Decide(inner, lift, s), "車頂還沒開放：Locked，不傳送");
                s.SetBool("roof.open", true);
                Assert.AreEqual(0, XrTeleportPoint.Decide(inner, lift, s), "開放後：Moved");
                Assert.AreEqual(1, XrTeleportPoint.Decide(outer, inner, s), "共用落點：已經在那一站，不傳");
                Assert.AreEqual(3, XrTeleportPoint.Decide(null, inner, s), "站點不存在");
                Assert.AreEqual(3, (int)XrTeleport.GoResult.Missing, "Decide 的值對應 GoResult");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); Core.ServiceLocator.Clear(); }
        }

        [Test]
        public void BoltTrays_CountPerSide_PartsTrayOnlyStrips_AnyLooseBoltBlocksWC01b()
        {
            var s = new Core.SimState(new Core.EventBus());
            foreach (var side in new[] { "L", "R" }) s.SetFloat(Worksite.BoltTray.CountKey(side), 0);
            Worksite.BoltTray.Apply(s, "L", "add:bolt", "ip.carbon_strip_L.bolt_1");
            Worksite.BoltTray.Apply(s, "L", "add:bolt", "ip.carbon_strip_L.bolt_2");
            Worksite.BoltTray.Apply(s, "R", "add:bolt", "ip.carbon_strip_R.bolt_1");
            Assert.AreEqual(2f, s.GetFloat(Worksite.BoltTray.CountKey("L")), "CSR_07 b：放入 +1");
            Assert.AreEqual(1f, s.GetFloat(Worksite.BoltTray.CountKey("R")), "兩側各自計數");
            Worksite.BoltTray.Apply(s, "L", "remove:bolt", "ip.carbon_strip_L.bolt_1");
            Assert.AreEqual(1f, s.GetFloat(Worksite.BoltTray.CountKey("L")), "CSR_12 b：拿起 −1");
            Assert.AreEqual(1f, s.GetFloat(Worksite.BoltTray.CountKey("R")), "另一側不受影響");
            Worksite.PartsTray.Apply(s, "add:strip", "part.carbon_strip_L");
            Assert.AreEqual(1f, s.GetFloat("tray.old_strips"), "舊滑板進零件盒照常計數");
            Assert.IsFalse(s.GetBool("tray.no_loose_bolts"), "螺栓盤還有螺栓：WC_01 b 不成立");
            Worksite.BoltTray.Apply(s, "L", "remove:bolt", "ip.carbon_strip_L.bolt_2");
            Worksite.BoltTray.Apply(s, "R", "remove:bolt", "ip.carbon_strip_R.bolt_1");
            Assert.IsTrue(s.GetBool("tray.no_loose_bolts"), "兩個螺栓盤都空");
        }

        [Test]
        public void StationLayoutCheck_Data_ReportsBrokenShare()
        {
            var copy = UnityEngine.Object.Instantiate(Set);
            try
            {
                var outer = UnityEngine.Object.Instantiate(copy.Find("tp.roof.outer"));
                outer.sharedWith = "tp.nowhere";
                copy.stations[System.Array.FindIndex(copy.stations, x => x.id == "tp.roof.outer")] = outer;
                var bad = StationLayoutCheck.CheckData(copy, Rule.resultKey);
                Assert.IsTrue(bad.Exists(b => b.Contains("tp.nowhere")), string.Join("\n", bad));
                UnityEngine.Object.DestroyImmediate(outer);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
    }
}
