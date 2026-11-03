using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Parts.Common;
using PantographSim.Parts.Mechanism;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PantographSim.Tests
{
    /// <summary>R1.5b b2：切片場景裡的天車＋集電弓吊運（除錯情境：車頂 → 台架 → 車頂完整往返）。</summary>
    sealed class LiftScene
    {
        public readonly SimState s;
        public readonly CraneMechanism crane;
        public readonly PanAssyRigging rig;
        public readonly Transform root, bridge, trolley, hook;
        public readonly RiggingDef def;
        public readonly List<ErrorDetected> errors = new List<ErrorDetected>();

        public LiftScene(UnityEngine.SceneManagement.Scene scene)
        {
            ServiceLocator.Clear();
            var bus = new EventBus(); s = new SimState(bus);
            ServiceLocator.Register<IEventBus>(bus); ServiceLocator.Register<ISimState>(s);
            bus.Subscribe<ErrorDetected>(e => errors.Add(e));
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
            crane = all.Select(t => t.GetComponent<CraneMechanism>()).First(c => c != null);
            root = PanAssy.Root(scene);
            rig = root.GetComponent<PanAssyRigging>();
            def = AssetDatabase.LoadAssetAtPath<RiggingDef>("Assets/_Project/Modules/Worksite/Data/RiggingDef.asset");
            Transform N(string n) => crane.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
            bridge = N("Crane_Bridge"); trolley = N("Crane_Trolley"); hook = N("Crane_HookBlock");
            CraneMechanism.HeadSource = () => null;
            s.SetBool(SimIds.CranePower, true);
        }

        public void Release()
        {
            foreach (var (k, v) in new[] { ("ip.pan_assy.hold_down.state", "Installed"), ("ip.pan_assy.ctrl_connector.state", "Removed"), ("ip.pan_assy.hv_terminal.state", "Removed") }) s.Set(k, v);
            for (int n = 1; n <= 4; n++) s.Set($"part.pan_base.bolt_{n}.state", "Out");
        }

        public void RigAll() { for (int n = 1; n <= 4; n++) if (s.Get($"ip.pan_assy.lift_point_{n}.state") != "Rigged") rig.Operate(s, "ip.pan_assy.lift_point_" + n); }
        public void Latch() { if (!s.GetBool(PanAssyRigging.LatchedKey)) rig.Operate(s, PanAssyRigging.HookId); }

        /// <summary>空鉤移到弓的重心上方（測試直接擺 Transform，等同 RM_05 c／IN_01 a 已做完）。</summary>
        public void HookOverCog()
        {
            var cog = root.Find("PAN_Base/COG").position;
            var anchor = crane.GetComponentsInChildren<Transform>(true).First(t => t.name == "Crane_RopeAnchor");
            var d = anchor.position - trolley.position;
            bridge.position = new Vector3(bridge.position.x, bridge.position.y, cog.z - d.z);
            trolley.position = new Vector3(cog.x - d.x, trolley.position.y, cog.z - d.z);
            var t = PanLiftMath.LoadDefault();
            var pts = Enumerable.Range(1, 4).Select(i => root.Find("PAN_LiftPoints/ip.pan_assy.lift_point_" + i).position).ToList();
            var attach = hook.GetComponentsInChildren<Transform>(true).First(x => x.name == "Hook_AttachPoint");
            hook.position += PanLiftMath.HookPoint(cog, pts, t) - attach.position;
            Tick(0.02f, "None");
        }

        public void Tick(float dt, string cmd, bool fast = false)
        {
            s.Set(SimIds.CraneCmd, cmd); s.SetBool(SimIds.CraneFast, fast);
            rig.Tick(s); crane.Tick(s, dt); Physics.SyncTransforms();
        }

        /// <summary>按住 cmd 直到 until 成立（或逾時）；回傳是否成立。</summary>
        public bool Hold(string cmd, bool fast, System.Func<bool> until, float maxSeconds = 120f, float dt = 0.02f)
        {
            for (float t = 0; t < maxSeconds; t += dt) { if (until()) { Tick(dt, "None"); return true; } Tick(dt, cmd, fast); }
            Tick(dt, "None");
            return until();
        }

        public string State => s.Get(PanAssyRigging.StateKey);
        public string Diag => $"弓 {State}、天車 {s.Get(SimIds.CraneState)}、吊鉤 {s.Get(SimIds.CraneHookPos)}、吊物底 {s.Get(SimIds.CraneLoadBottomY)}、落點 {s.Get(PanAssyRigging.DropDistanceKey)}、缺 {s.Get("part.pan_assy.lift_block")}、錯誤 {string.Join("／", errors.Select(e => e.errorId + ":" + e.text))}、PAN_Root {root.position:F3}";
        public void Dispose() { CraneMechanism.HeadSource = () => Camera.main != null ? Camera.main.transform.position : (Vector3?)null; ServiceLocator.Clear(); }
    }

    /// <summary>RiggingTests（crane-software v0.5 §5）：起吊條件 6 項各自缺少都吊不起來並說明；全部成立吊得起來；吸附 2 cm 成立、2.1 cm 不成立；對位燈和吸附一致；COG 偏 0.3 m 時用 PAN_Root 原點對位；完整往返。</summary>
    public class RiggingTests
    {
        static RiggingDef Def => AssetDatabase.LoadAssetAtPath<RiggingDef>("Assets/_Project/Modules/Worksite/Data/RiggingDef.asset");

        [Test]
        public void LiftConditions_EachMissing_BlocksWithReason_AllMet_Lifts()
        {
            StationTests.WithScene(scene =>
            {
                var L = new LiftScene(scene);
                try
                {
                    L.HookOverCog();
                    var conds = Def.liftConditions;
                    Assert.AreEqual(6, conds.Length, "§3.1 表 6 項");
                    foreach (var c in conds)
                    {
                        L.Release(); L.RigAll(); L.Latch();
                        // 拿掉這一項（{n} 的拿掉第 1 個）
                        var k = c.condition.Replace("{n}", "1"); int eq = k.IndexOf('=');
                        if (eq > 0) L.s.Set(k.Substring(0, eq), "x"); else L.s.SetBool(k, false);
                        var y0 = L.root.position.y;
                        L.Tick(0.02f, "Up");
                        Assert.AreNotEqual("Lifted", L.State, c.condition);
                        StringAssert.Contains(c.label.Replace("{n}", "1"), L.s.Get("part.pan_assy.lift_block"), "說明缺什麼");
                        L.Hold("Up", true, () => false, 1f);
                        Assert.AreEqual(y0, L.root.position.y, 1e-5f, "集電弓不動");
                    }
                    L.Release(); L.RigAll(); L.Latch();
                    L.Tick(0.02f, "Up");
                    Assert.AreEqual("Lifted", L.State);
                    var y1 = L.root.position.y; L.Hold("Up", false, () => false, 2f);
                    Assert.Greater(L.root.position.y, y1, "全部成立時吊得起來");
                }
                finally { L.Dispose(); }
            });
        }

        [Test]
        public void Snap_2cmYes_2p1cmNo_LightsMatch()
        {
            var holes = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) };
            var pins = holes.Select(h => h + new Vector3(0.02f, 0f, 0f)).ToArray();
            var d = RiggingLogic.PinDistances(holes, pins);
            Assert.IsTrue(RiggingLogic.AllWithin(d, Def.snapRadius), "2 cm 成立");
            pins[2] += new Vector3(0.001f, 0f, 0f);
            d = RiggingLogic.PinDistances(holes, pins);
            Assert.IsFalse(RiggingLogic.AllWithin(d, Def.snapRadius), "有一支 2.1 cm 不成立");
            Assert.AreEqual(new[] { true, true, false, true }, d.Select(x => x <= Def.snapRadius + 1e-5f).ToArray(), "對位燈（每支一顆）和吸附讀同一份距離");
        }

        /// <summary>建模 B F5：COG 比底架中心偏 0.3 m；PAN_Root 原點對準台架中心就吸附，而且 4 支定位銷都對上。</summary>
        [Test]
        public void AlignByPanRootOrigin_NotCog()
        {
            StationTests.WithScene(scene =>
            {
                var root = PanAssy.Root(scene);
                var cog = root.Find("PAN_Base/COG").position;
                var holes = Enumerable.Range(1, 4).Select(i => root.Find("PAN_Base/BaseHole_" + i).position - root.position).ToArray();
                var standCenter = new Vector3(-9.5f, 0f, 0f);
                var cogOff = new Vector3(0f, 0f, 0.3f);   // 規格的情境：COG 比底架中心往弓頭偏 0.3 m（場景實測 COG 的水平偏移只有約 1 cm，所以用規格值）
                var pins = holes.Select(h => standCenter + new Vector3(h.x, 0, h.z)).ToArray();
                var byOrigin = holes.Select(h => standCenter + new Vector3(h.x, 0, h.z)).ToArray();
                Assert.IsTrue(RiggingLogic.AllWithin(RiggingLogic.PinDistances(byOrigin, pins), Def.snapRadius), "原點對準：4 支都對上");
                var byCog = holes.Select(h => standCenter - cogOff + new Vector3(h.x, 0, h.z)).ToArray();
                Assert.IsFalse(RiggingLogic.AllWithin(RiggingLogic.PinDistances(byCog, pins), Def.snapRadius), "用 COG 對位會錯開");
            });
        }

        /// <summary>R1.5b b4：吊運量測鍵（吊鉤與重心水平差、吊梁離車頂／弓頂、人員淨空、停住秒數、傾斜、對位偏差、座落誤差）由場景機制寫，車頂 → 台架 → 車頂一路驗。</summary>
        [Test]
        public void RoundTrip_Measures_HookCog_Beam_Still_Tilt_Offsets_SeatError()
        {
            StationTests.WithScene(scene =>
            {
                var L = new LiftScene(scene);
                try
                {
                    var p0 = L.root.position;
                    var rd = AssetDatabase.LoadAssetAtPath<RoofSceneDef>("Assets/_Project/Modules/Worksite/Data/RoofSceneDef.asset");
                    L.Release(); L.RigAll(); L.Latch(); L.HookOverCog();
                    L.rig.Tick(L.s);
                    Assert.LessOrEqual(L.s.GetFloat("part.crane.hook_cog_offset_cm", 99f), 5f, "吊鉤在重心正上方（RM_05 c、LF_01 d）");
                    Assert.Greater(L.s.GetFloat("part.crane.beam_above_roof_m"), 0f, "吊梁在車頂上方");
                    Assert.IsTrue(L.s.Has("part.crane.beam_above_pan_m"), "吊梁離弓頂");
                    Assert.IsTrue(L.s.GetBool("part.crane.person_clear"), "沒有人在吊鉤下方");
                    Assert.IsTrue(L.Hold("Up", false, () => L.s.GetFloat(PanAssyRigging.DropDistanceKey, 0f) >= 0.1f, 120f), "試吊離座 0.1 m：" + L.Diag);
                    for (int i = 0; i < 160; i++) L.Tick(0.02f, "None");   // 停住 3.2 s
                    Assert.GreaterOrEqual(L.s.GetFloat("part.crane.still_s"), 3f, "停住秒數（LF_02 a）");
                    Assert.LessOrEqual(L.s.GetFloat("part.pan_assy.tilt_deg", 99f), 3f, "吊起時傾斜（LF_02 b）");
                    Assert.IsTrue(L.Hold("Up", true, () => L.s.Get(SimIds.CraneState) == "LimitUp", 120f), L.Diag);
                    Move(L, new Vector3(rd.standCenterXZ.x, 0f, rd.standCenterXZ.y));
                    Assert.LessOrEqual(L.s.GetFloat("part.pan_assy.stand_offset_cm", 99f), 5f, "對準台架中心（LF_04 a）");
                    Assert.IsTrue(L.Hold("Down", true, () => L.s.GetFloat(PanAssyRigging.DropDistanceKey, 99f) < 0.25f, 120f), L.Diag);
                    Assert.IsTrue(L.Hold("Down", false, () => L.State == "OnStand", 60f), L.Diag);
                    Assert.LessOrEqual(L.s.GetFloat("part.pan_assy.seat_error_cm", 99f), 2f, "吸附到台架：孔位誤差 ≤ 吸附半徑");
                    for (int k = 1; k <= 2; k++) L.rig.Operate(L.s, "env.panto_stand.clamp_" + k);
                    for (int k = 1; k <= 2; k++) L.rig.Operate(L.s, "env.panto_stand.clamp_" + k);
                    L.Tick(0.02f, "None");
                    Assert.IsTrue(L.Hold("Up", true, () => L.s.Get(SimIds.CraneState) == "LimitUp", 120f), L.Diag);
                    Move(L, new Vector3(p0.x, 0f, p0.z));
                    Assert.LessOrEqual(L.s.GetFloat("part.pan_assy.roof_offset_cm", 99f), 5f, "對準 4 個絕緣子中心（IN_03 b）");
                    Assert.IsTrue(L.Hold("Down", true, () => L.s.GetFloat(PanAssyRigging.DropDistanceKey, 99f) < 0.25f, 120f), L.Diag);
                    Assert.IsTrue(L.Hold("Down", false, () => L.State == "Mounted", 60f), L.Diag);
                    Assert.LessOrEqual(L.s.GetFloat("part.pan_assy.seat_error_cm", 99f), 1f, "放到絕緣子上：BaseHole 與 InsulatorTop ≤ 1 cm（IN_04 b）");
                }
                finally { L.Dispose(); }
            });
        }

        [Test]
        public void EStop_PressThenReset_RecordsTested()
        {
            var s = new SimState(new EventBus());
            CraneControl.PressEStop(s);
            Assert.IsFalse(s.GetBool(CraneControl.EStopTestedKey), "只按下還不算");
            CraneControl.PressEStop(s);
            Assert.IsFalse(s.GetBool(SimIds.CraneEStop));
            Assert.IsTrue(s.GetBool(CraneControl.EStopTestedKey), "按下再復歸＝急停測試過（RM_05 b）");
        }

        [Test]
        public void Inspect_DwellThenRecorded_SlingVerdictCheckedAgainstData()
        {
            var s = new SimState(new EventBus());
            var rules = ToolRulesDef.Load();
            var def = ScriptableObject.CreateInstance<RiggingDef>(); def.defectiveSlings = new[] { 3 };
            (string id, float t) d = ("", 0f);
            s.Set(InspectLogic.HoverKey, "env.sling_3");
            d = InspectLogic.Dwell(s, d.id, d.t, 0.1f, rules);
            d = InspectLogic.Dwell(s, d.id, d.t, rules.inspectDwellSeconds / 2f, rules);
            Assert.IsFalse(s.GetBool("env.sling_3.inspected"), "還沒停夠久");
            d = InspectLogic.Dwell(s, d.id, d.t, rules.inspectDwellSeconds, rules);
            Assert.IsTrue(s.GetBool("env.sling_3.inspected"));
            Assert.AreEqual("env.sling_3", s.Get(InspectLogic.SlingVerdictUi), "看過吊帶：打開可用／不可用");
            InspectLogic.SlingVerdict(s, "env.sling_3", true, def);
            Assert.IsFalse(s.GetBool("env.sling_3.verdict_correct"), "有割傷判可用：不對");
            InspectLogic.SlingVerdict(s, "env.sling_3", false, def);
            Assert.IsTrue(s.GetBool("env.sling_3.verdict_correct"));
            s.Set(InspectLogic.HoverKey, "part.crane.hook");
            d = InspectLogic.Dwell(s, d.id, d.t, 0.1f, rules);
            d = InspectLogic.Dwell(s, d.id, d.t, rules.inspectDwellSeconds, rules);
            Assert.IsTrue(s.GetBool("part.crane.hook.inspected"), "吊鉤：指著停一下＝查看（不會切換舌片）");
            Assert.IsFalse(s.GetBool(PanAssyRigging.LatchedKey));
            Object.DestroyImmediate(def);
        }

        /// <summary>除錯情境：車頂 → 台架（吸附、鎖夾具、卸吊帶）→ 車頂（吸附、上扭力後 Fastened），全程沒有碰撞，最後回到原位。</summary>
        [Test]
        public void RoundTrip_RoofToStandAndBack()
        {
            StationTests.WithScene(scene =>
            {
                var L = new LiftScene(scene);
                try
                {
                    var p0 = L.root.position; var r0 = L.root.rotation;
                    var rd = AssetDatabase.LoadAssetAtPath<RoofSceneDef>("Assets/_Project/Modules/Worksite/Data/RoofSceneDef.asset");
                    var stand = new Vector3(rd.standCenterXZ.x, 0f, rd.standCenterXZ.y);
                    L.Release(); L.RigAll(); L.Latch(); L.HookOverCog();
                    L.rig.Tick(L.s); Assert.AreEqual("Rigged", L.State);
                    Assert.IsTrue(L.Hold("Up", true, () => L.s.Get(SimIds.CraneState) == "LimitUp", 120f), "升到上限位：" + L.Diag);
                    Assert.AreEqual("Lifted", L.State);
                    Move(L, stand);
                    Assert.IsTrue(L.Hold("Down", true, () => L.s.GetFloat(PanAssyRigging.DropDistanceKey, 99f) < 0.25f, 120f), "降到台架上方：" + L.Diag);
                    Assert.IsTrue(Enumerable.Range(1, 4).All(n => L.rig.LightGreen(n, false)), "4 顆對位燈都綠");
                    Assert.IsTrue(L.Hold("Down", false, () => L.State == "OnStand", 60f), "吸附到台架：" + L.Diag);
                    Assert.IsEmpty(L.errors.Select(e => e.errorId), "往台架的路上沒有碰撞或紅區");
                    for (int k = 1; k <= 2; k++) L.rig.Operate(L.s, "env.panto_stand.clamp_" + k);
                    for (int n = 1; n <= 4; n++) L.rig.Operate(L.s, "ip.pan_assy.lift_point_" + n);
                    L.Tick(0.02f, "None");
                    Assert.AreEqual("OnStand", L.State);
                    // 吊回
                    for (int k = 1; k <= 2; k++) L.rig.Operate(L.s, "env.panto_stand.clamp_" + k);
                    L.RigAll(); L.HookOverCog();
                    L.Tick(0.02f, "Up", true);
                    Assert.AreEqual("Lifted", L.State, "從台架起吊：" + L.Diag + "；舌片 " + L.s.Get(PanAssyRigging.LatchedKey) + "；夾具 " + L.s.Get("env.panto_stand.clamp_1.state") + "／" + L.s.Get("env.panto_stand.clamp_2.state") + "；吊點 " + L.s.Get("ip.pan_assy.lift_point_1.state") + "；擋上升 " + L.s.Get(PanAssyRigging.UpBlockedKey));
                    Assert.IsTrue(L.Hold("Up", true, () => L.s.Get(SimIds.CraneState) == "LimitUp", 120f), "從台架升到上限位：" + L.Diag);
                    Assert.AreEqual("Lifted", L.State);
                    Move(L, new Vector3(p0.x, 0f, p0.z));
                    Assert.IsTrue(L.Hold("Down", true, () => L.s.GetFloat(PanAssyRigging.DropDistanceKey, 99f) < 0.25f, 120f), "降到車頂上方：" + L.Diag);
                    Assert.IsTrue(L.Hold("Down", false, () => L.State == "Mounted", 60f), "吸附到絕緣子：" + L.Diag);
                    Assert.Less(Vector3.Distance(p0, L.root.position), 1e-3f, "回到原位");
                    Assert.Less(Quaternion.Angle(r0, L.root.rotation), 0.01f);
                    for (int n = 1; n <= 4; n++) L.s.Set($"part.pan_base.bolt_{n}.state", "Torqued");
                    L.Tick(0.02f, "None");
                    Assert.AreEqual("Fastened", L.State);
                    Assert.IsEmpty(L.errors.Select(e => e.errorId), "全程沒有錯誤");
                }
                finally { L.Dispose(); }
            });
        }

        /// <summary>水平移到 PAN_Root 原點對準 target（先高速、再微動），X 用小車、Z 用橋架。</summary>
        static void Move(LiftScene L, Vector3 target)
        {
            foreach (var (pos, neg, axis) in new[] { ("E", "W", 0), ("N", "S", 2) })
            {
                float Err() => target[axis] - L.root.position[axis];
                if (Mathf.Abs(Err()) > 0.08f) Assert.IsTrue(L.Hold(Err() > 0 ? pos : neg, true, () => Mathf.Abs(Err()) < 0.08f, 120f), "高速接近：" + L.Diag);
                for (int i = 0; i < 4 && Mathf.Abs(Err()) > 0.003f; i++) L.Hold(Err() > 0 ? pos : neg, false, () => Mathf.Abs(Err()) < 0.003f, 30f, 0.01f);
                Assert.Less(Mathf.Abs(Err()), 0.01f, "微動對準：" + L.Diag);
            }
        }
    }

    /// <summary>TeleportRuleTests（§2.5 表每一列各測一次允許、一次擋下）。</summary>
    public class TeleportRuleTests
    {
        static TeleportRule Rule => AssetDatabase.LoadAssetAtPath<TeleportRule>("Assets/_Project/Modules/Worksite/Data/TeleportRule.asset");

        [TestCase("tp.platform.lift")] [TestCase("tp.stand.w")] [TestCase("tp.platform.crane")] [TestCase("tp.stand.s")]
        public void LiftedBlocks_NotLiftedAllows(string station)
        {
            var s = new SimState(new EventBus());
            s.Set("part.pan_assy.state", "Lifted");
            Assert.IsNotNull(Rule.BlockReason(s, station), "吊物懸空一律不能傳送");
            foreach (var st in new[] { "Mounted", "Rigged", "OnStand" })
            {
                s.Set("part.pan_assy.state", st);
                Assert.IsNull(Rule.BlockReason(s, station), $"{st}：吊物沒懸空就開放（含剛放上台架、夾具還沒鎖的 LF_06、吊回時還沒離開台架的 tp.platform.crane）");
            }
        }

        [Test]
        public void PlatformToGround_TeleportOnly_StillBlockedWhileLifted()
        {
            var s = new SimState(new EventBus());
            Assert.IsNull(Rule.BlockReason(s, "tp.stand.n"), "平台 → 地面維修區只能用傳送（相當於走樓梯），平常開放");
            s.Set("part.pan_assy.state", "Lifted");
            Assert.IsNotNull(Rule.BlockReason(s, "tp.stand.n"));
        }
    }

    /// <summary>DetachablePartTests（a5 留下的第 1 件事）：Installed／Removed 寫黑板；requires 沒成立拿不下來並用 Explain() 說明；InstallSnap 2 cm 內吸附；碳滑板的 RemovableBehaviour 測試照舊（PartsTests）。</summary>
    public class DetachablePartTests
    {
        [Test]
        public void RemoveNeedsRequires_Explain_InstallSnapWithin2cm()
        {
            ServiceLocator.Clear();
            var bus = new EventBus(); var s = new SimState(bus);
            ServiceLocator.Register<IEventBus>(bus); ServiceLocator.Register<ISimState>(s);
            var go = new GameObject("ip.test.part"); var snap = new GameObject("snap").transform; snap.position = new Vector3(1, 2, 3);
            var on = new GameObject("On"); var off = new GameObject("Off");
            try
            {
                var p = go.AddComponent<DetachablePart>(); p.Configure("ip.test.part", "測試件");
                p.Setup(new[] { "bolt_1.state=Out", "bolt_2.state=Out" }, new[] { "螺栓 1 還沒脫出", "螺栓 2 還沒脫出" }, new[] { on }, new[] { off }, snap, 0.02f, DetachablePart.Installed);
                typeof(PartBehaviour).GetMethod("Start", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(p, null);
                Assert.AreEqual("Installed", s.Get("ip.test.part.state"));
                s.Set("bolt_1.state", "Out");
                Assert.IsFalse(p.Remove(), "還有一支沒脫出");
                Assert.AreEqual("還不能拆：螺栓 2 還沒脫出", p.Explain());
                StringAssert.Contains("螺栓 2", s.Get("ip.test.part.blocked"));
                s.Set("bolt_2.state", "Out");
                Assert.IsTrue(p.Remove()); Assert.AreEqual("Removed", s.Get("ip.test.part.state"));
                Assert.IsFalse(on.activeSelf); Assert.IsTrue(off.activeSelf, "子節點依狀態切換");
                Assert.IsFalse(p.Install(snap.position + new Vector3(0.021f, 0, 0)), "2.1 cm 不吸附");
                Assert.IsTrue(p.Install(snap.position + new Vector3(0.02f, 0, 0)), "2 cm 內吸附");
                Assert.AreEqual("Installed", s.Get("ip.test.part.state"));
                Assert.IsTrue(on.activeSelf); Assert.IsFalse(off.activeSelf);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(snap.gameObject); Object.DestroyImmediate(on); Object.DestroyImmediate(off); ServiceLocator.Clear(); }
        }

        [Test]
        public void SceneNodes_HaveIdsAndComponents()
        {
            StationTests.WithScene(scene =>
            {
                var ids = StationTests.SceneIds(scene);
                foreach (var id in Enumerable.Range(1, 4).Select(n => "part.pan_base.bolt_" + n)) Assert.IsNotNull(ids[id].GetComponent<FastenerBehaviour>(), id);
                foreach (var id in Enumerable.Range(1, 2).Select(n => "part.pan_assy.hv_bolt_" + n)) Assert.IsNotNull(ids[id].GetComponent<FastenerBehaviour>(), id);
                foreach (var id in Enumerable.Range(1, 4).Select(n => "ip.pan_assy.lift_point_" + n)) Assert.IsTrue(ids.ContainsKey(id), id);
                foreach (var id in new[] { "ip.pan_assy.hv_terminal", "ip.pan_assy.ctrl_connector", "ip.pan_assy.hold_down" }) Assert.IsNotNull(ids[id].GetComponent<DetachablePart>(), id);
                Assert.IsNull(ids["ip.pan_assy.hv_terminal"].GetComponent<RemovableBehaviour>(), "不掛碳滑板專用的 RemovableBehaviour");
            });
        }
    }

    /// <summary>LoadProbe（a5 留下的第 2 件事）：吊物探測 collider 只在 Lifted 時開；只和 Default、Boundary 互動，手、工具、清潔布（ContactTarget 等）在裡面移動不觸發。</summary>
    public class LoadProbeTests
    {
        [Test]
        public void Probe_OnlyWhenLifted_IgnoresHandsAndTools()
        {
            int probe = LayerMask.NameToLayer("LoadProbe");
            Assert.GreaterOrEqual(probe, 0, "有 LoadProbe 層");
            foreach (var other in new[] { "ContactTarget", "PanAssembly", "Walkable", "LocomotionBlocker", "UI", "Ignore Raycast" })
                Assert.IsTrue(Physics.GetIgnoreLayerCollision(probe, LayerMask.NameToLayer(other)), "不和 " + other + " 互動");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(probe, 0)); Assert.IsFalse(Physics.GetIgnoreLayerCollision(probe, LayerMask.NameToLayer("Boundary")));
            StationTests.WithScene(scene =>
            {
                var L = new LiftScene(scene);
                try
                {
                    var box = L.root.GetComponent<BoxCollider>();
                    Assert.AreEqual(probe, box.gameObject.layer); Assert.IsTrue(box.isTrigger);
                    L.Tick(0.02f, "None");
                    Assert.IsFalse(box.enabled, "沒懸空時關閉（不擋清潔布）");
                    L.Release(); L.RigAll(); L.Latch(); L.HookOverCog(); L.Tick(0.02f, "Up");
                    Assert.AreEqual("Lifted", L.State);
                    Assert.IsTrue(box.enabled, "懸空時開啟");
                }
                finally { L.Dispose(); }
            });
        }
    }
}
