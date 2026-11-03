using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Parts.Mechanism;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PantographSim.Tests
{
    /// <summary>R1.5b b1：測試用的小天車（橋架、小車、吊鉤、繩、吊梁、方塊吊物），不接流程（除錯情境：吊一個方塊）。</summary>
    sealed class CraneRig : System.IDisposable
    {
        public readonly GameObject root;
        public readonly CraneMechanism mech;
        public readonly CraneProfile profile;
        public readonly SimState state;
        public readonly List<ErrorDetected> errors = new List<ErrorDetected>();
        public readonly Transform hook, load;
        Vector3? head;

        public CraneRig(bool withLoad = true)
        {
            ServiceLocator.Clear();
            var bus = new EventBus();
            state = new SimState(bus);
            ServiceLocator.Register<IEventBus>(bus);
            ServiceLocator.Register<ISimState>(state);
            bus.Subscribe<ErrorDetected>(e => errors.Add(e));
            profile = ScriptableObject.CreateInstance<CraneProfile>();
            profile.hookYMax = 6.0f; profile.hookYMin = 0f;
            root = new GameObject("TestCrane");
            var bridge = new GameObject("Crane_Bridge").transform; bridge.SetParent(root.transform); bridge.position = new Vector3(0, 7f, 0);
            var trolley = new GameObject("Crane_Trolley").transform; trolley.SetParent(bridge); trolley.position = new Vector3(0, 7f, 0);
            var anchor = new GameObject("Crane_RopeAnchor").transform; anchor.SetParent(trolley); anchor.position = new Vector3(0, 6.5f, 0);
            hook = new GameObject("Crane_HookBlock").transform; hook.SetParent(root.transform); hook.position = new Vector3(0, 4f, 0);
            var beam = new GameObject("ENV_LiftingBeam").transform; beam.SetParent(hook, false); beam.localPosition = new Vector3(0, -0.5f, 0);
            mech = root.AddComponent<CraneMechanism>();
            var rules = new[] { Rule(SafetyRuleKind.CranePersonUnderLoad, "ERR_Crane_PersonUnderLoad"), Rule(SafetyRuleKind.CraneCollision, "ERR_Crane_Collision") };
            mech.Setup(profile, bridge, trolley, hook, anchor, null, beam, rules);
            if (withLoad)
            {
                load = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;   // 1 m 方塊
                load.name = "DebugLoad"; load.position = new Vector3(0, 3f, 0);
                mech.Attach(load, new Bounds(Vector3.zero, Vector3.one));
            }
            CraneMechanism.HeadSource = () => head;
            state.SetBool(SimIds.CranePower, true);
        }

        static SafetyRuleDefinition Rule(SafetyRuleKind k, string id)
        {
            var r = ScriptableObject.CreateInstance<SafetyRuleDefinition>(); r.kind = k; r.errorId = id; r.ruleId = id; return r;
        }

        public void Head(Vector3? p) => head = p;
        public void Cmd(string c, bool fast = false) { state.Set(SimIds.CraneCmd, c); state.SetBool(SimIds.CraneFast, fast); }
        public void Run(float seconds, float dt = 0.02f) { for (float t = 0; t < seconds - 1e-6f; t += dt) mech.Tick(state, dt); Physics.SyncTransforms(); }
        public Vector3 Hook => hook.position;

        public void Dispose()
        {
            CraneMechanism.HeadSource = () => Camera.main != null ? Camera.main.transform.position : (Vector3?)null;
            Object.DestroyImmediate(root); if (load != null) Object.DestroyImmediate(load.gameObject);
            Object.DestroyImmediate(profile);
            ServiceLocator.Clear();
        }
    }

    /// <summary>CraneMechanismTests（crane-software v0.5 §5）：按住才動、放開就停；同時按兩個方向就停；高速與微動；緊急停止鎖住要復歸；上限位；碰撞停車並記錯。</summary>
    public class CraneMechanismTests
    {
        [Test]
        public void Profile_DefaultsMatchCraneProcedure()
        {
            var p = ScriptableObject.CreateInstance<CraneProfile>();
            Assert.AreEqual((5f, 0.5f, 15f, 1.5f, 20f, 2f), (p.hoistFast, p.hoistFine, p.traverseFast, p.traverseFine, p.travelFast, p.travelFine));
            Assert.AreEqual((-15f, 2f, -15f, 15f, 0.3f, 1.0f), (p.trolleyXMin, p.trolleyXMax, p.bridgeZMin, p.bridgeZMax, p.accelSeconds, p.redZoneMargin));
            Object.DestroyImmediate(p);
        }

        [Test]
        public void HoldMoves_ReleaseStops()
        {
            using (var c = new CraneRig(false))
            {
                var y0 = c.Hook.y;
                c.Cmd("Up"); c.Run(2f);
                Assert.Greater(c.Hook.y, y0 + 0.01f, "按住上升會動");
                Assert.AreEqual("Moving", c.state.Get(SimIds.CraneState));
                c.Cmd("None"); c.Run(c.profile.accelSeconds + 0.1f);
                var y1 = c.Hook.y; c.Run(1f);
                Assert.AreEqual(y1, c.Hook.y, 1e-5f, "放開就停");
                Assert.AreEqual("Idle", c.state.Get(SimIds.CraneState));
                Assert.AreEqual(y1, float.Parse(c.state.Get(SimIds.CraneHookPos).Split(',')[1], System.Globalization.CultureInfo.InvariantCulture), 1e-3f, "放開時強制寫入吊鉤位置");
            }
        }

        [Test]
        public void TwoDirectionsAtOnce_Stops()
        {
            Assert.AreEqual("Up", CraneLogic.Resolve(new[] { "Btn_Up" }));
            Assert.AreEqual("None", CraneLogic.Resolve(new[] { "Btn_Up", "Btn_East" }), "同時按兩個方向＝None");
            Assert.AreEqual("E", CraneLogic.Resolve(new[] { "Btn_East", "Btn_Speed2" }), "高速鍵不算方向");
            using (var c = new CraneRig(false))
            {
                var p0 = c.Hook;
                c.Cmd(CraneLogic.Resolve(new[] { "Btn_North", "Btn_South" })); c.Run(1f);
                Assert.AreEqual(p0, c.Hook);
            }
        }

        [TestCase("Up", 1)] [TestCase("E", 0)] [TestCase("N", 2)]
        public void FastAndFineSpeeds(string cmd, int axis)
        {
            foreach (var fast in new[] { false, true })
                using (var c = new CraneRig(false))
                {
                    c.Cmd(cmd, fast); c.Run(c.profile.accelSeconds + 0.1f);
                    var a = c.Hook; c.Run(0.5f); var b = c.Hook;
                    float v = (b - a)[axis == 0 ? 0 : axis == 1 ? 1 : 2] / 0.5f;
                    var ax = axis == 0 ? CraneAxis.Traverse : axis == 1 ? CraneAxis.Hoist : CraneAxis.Travel;
                    Assert.AreEqual(CraneLogic.Speed(c.profile, ax, fast), v, 1e-3f, $"{cmd} {(fast ? "高速" : "微動")}");
                }
        }

        [Test]
        public void EStop_Latches_NeedsResetAndPowerCycle()
        {
            using (var c = new CraneRig(false))
            {
                var y0 = c.Hook.y;
                CraneControl.PressEStop(c.state);
                c.Cmd("Up"); c.Run(1f);
                Assert.AreEqual(y0, c.Hook.y, 1e-6f); Assert.AreEqual("EStop", c.state.Get(SimIds.CraneState));
                CraneControl.PressEStop(c.state);   // 旋轉復歸
                c.Cmd("Up"); c.Run(1f);
                Assert.AreEqual(y0, c.Hook.y, 1e-6f, "復歸後還沒重轉電源，命令仍無效");
                CraneControl.TurnKey(c.state); c.Run(0.1f); CraneControl.TurnKey(c.state);   // 關再開
                c.Cmd("Up"); c.Run(1f);
                Assert.Greater(c.Hook.y, y0, "重轉電源後可以動");
            }
        }

        [Test]
        public void UpperLimit_StopsAndReportsLimitUp()
        {
            using (var c = new CraneRig(false))
            {
                c.hook.position = new Vector3(0, c.profile.hookYMax - 0.02f, 0);
                c.Cmd("Up", true); c.Run(2f);
                Assert.AreEqual(c.profile.hookYMax, c.Hook.y, 1e-4f);
                Assert.AreEqual("LimitUp", c.state.Get(SimIds.CraneState));
            }
        }

        [Test]
        public void Collision_StopsAndLogsCritical()
        {
            using (var c = new CraneRig())
            {
                var obstacle = new GameObject("Wall"); var group = new GameObject("Collision"); obstacle.transform.SetParent(group.transform);
                var bc = obstacle.AddComponent<BoxCollider>(); bc.size = new Vector3(0.2f, 3f, 3f); obstacle.transform.position = new Vector3(1.0f, 3f, 0f);
                try
                {
                    Physics.SyncTransforms();
                    c.Cmd("E", true); c.Run(3f);
                    Assert.AreEqual("Collision", c.state.Get(SimIds.CraneState));
                    Assert.Less(c.load.position.x + 0.5f, 0.9f + 1e-3f, "吊物停在牆前，不穿過");
                    var e = c.errors.Single();
                    Assert.AreEqual(("ERR_Crane_Collision", Severity.Critical), (e.errorId, e.severity));
                    c.Cmd("W"); c.Run(1f);
                    Assert.AreNotEqual("Collision", c.state.Get(SimIds.CraneState), "放開、往反方向可以移開");
                }
                finally { Object.DestroyImmediate(group); }
            }
        }
    }

    /// <summary>CraneRedZoneTests（D18、D19）：紅區＝吊物投影外擴 1.0 m；進入時停車並記 Critical；在紅區內按鍵無效；離開後可以再操作。</summary>
    public class CraneRedZoneTests
    {
        [Test]
        public void RedZone_ExtentIsLoadPlusMargin_IgnoresHeight()
        {
            var b = new Bounds(Vector3.zero, new Vector3(2f, 1f, 1f));
            float m = 1.0f;
            Assert.IsTrue(CraneLogic.InRedZone(b, m, new Vector3(1f + m - 0.01f, -10f, 0f)));
            Assert.IsFalse(CraneLogic.InRedZone(b, m, new Vector3(1f + m + 0.01f, 0f, 0f)));
            Assert.IsTrue(CraneLogic.InRedZone(b, m, new Vector3(0f, 50f, 0.5f + m - 0.01f)));
            Assert.IsFalse(CraneLogic.InRedZone(b, m, new Vector3(0f, 0f, -(0.5f + m + 0.01f))));
        }

        [Test]
        public void EnterRedZone_StopsSameFrame_LogsCriticalOnce_KeysIgnored_LeaveResumes()
        {
            using (var c = new CraneRig())
            {
                c.Head(new Vector3(5f, 1.6f, 0f));
                c.Cmd("N", true); c.Run(1f);
                Assert.IsEmpty(c.errors);
                c.Head(new Vector3(0.5f + 1.0f - 0.05f, 1.6f, c.load.position.z));   // 進到紅區邊上
                var before = c.Hook; c.Run(0.02f);
                Assert.AreEqual("RedZoneStop", c.state.Get(SimIds.CraneState));
                Assert.AreEqual(before, c.Hook, "同一幀就停");
                c.Run(2f);
                Assert.AreEqual(before, c.Hook, "紅區內按鍵無效");
                var e = c.errors.Single();
                Assert.AreEqual(("ERR_Crane_PersonUnderLoad", Severity.Critical), (e.errorId, e.severity));
                c.Head(new Vector3(5f, 1.6f, 0f)); c.Run(1f);
                Assert.AreNotEqual(before, c.Hook, "離開紅區後可以再操作");
                Assert.AreEqual(1, c.errors.Count);
            }
        }
    }

    /// <summary>CraneInputTests：遙控器只寫 ctrl.crane.*、不引用 CraneMechanism；InputBindingTable 和 R1 相同（一個按鍵都不新增）。</summary>
    public class CraneInputTests
    {
        [Test]
        public void PendantAndPanel_DoNotReferenceMechanism()
        {
            var mechAsm = typeof(CraneMechanism).Assembly.GetName().Name;
            foreach (var t in new[] { typeof(PantographSim.XR.XrCranePendant), typeof(PantographSim.UI.CranePanelView), typeof(CraneControl) })
                CollectionAssert.DoesNotContain(t.Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList(), mechAsm, $"{t.Name} 的組件不可以引用天車機構");
        }

        [Test]
        public void CraneControl_WritesOnlyCraneKeys()
        {
            var bus = new EventBus(); var s = new SimState(bus); var keys = new HashSet<string>();
            bus.Subscribe<StateChanged>(e => keys.Add(e.targetId));
            CraneControl.Command(s, "Up"); CraneControl.Command(s, "None"); CraneControl.ToggleFast(s); CraneControl.PressEStop(s); CraneControl.TurnKey(s);
            CraneControl.SetPendant(s, true); CraneControl.SetPendant(s, false);
            Assert.IsNotEmpty(keys);
            foreach (var k in keys) Assert.IsTrue(k.StartsWith("ctrl.crane.") || k == SimIds.CranePendantState, k);
        }

        /// <summary>R1.5a 收尾時的按鍵表（列內容的雜湊）；R1.5b 不新增控制器按鍵（設計原則 2）。</summary>
        const string R1BindingHash = "229DA96BD2B8E306";   // 182 列；git 上 8eea3cc（R1.5a 基線）以來這個表沒變過

        [Test]
        public void InputBindingTable_SameAsR1()
        {
            var table = InputBindingTable.LoadDefault();
            var text = string.Join("\n", table.rows.Select(r => $"{r.context}|{r.hand}|{r.control}|{r.functionId}|{r.labelKey}|{r.enabled}"));
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create()) hash = System.BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 16);
            Assert.AreEqual(R1BindingHash, hash, $"按鍵表 {table.rows.Length} 列");
            Assert.IsFalse(table.rows.Any(r => (r.functionId ?? "").ToLowerInvariant().Contains("crane")), "沒有天車專用按鍵");
        }
    }
}
