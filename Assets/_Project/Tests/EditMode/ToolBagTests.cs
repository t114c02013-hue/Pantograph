using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Procedures;
using PantographSim.Tools;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PantographSim.Tests
{
    /// <summary>R1.5b b3：用場景的工具資料建一條工具列（ToolBelt＋所有工具），不開場景。</summary>
    sealed class BeltRig : System.IDisposable
    {
        public readonly SimState s;
        public readonly ToolBelt belt;
        public readonly GameObject root;
        public readonly List<ErrorDetected> errors = new List<ErrorDetected>();
        public static ToolSlotTable Table => AssetDatabase.LoadAssetAtPath<ToolSlotTable>("Assets/_Project/Modules/Worksite/Data/ToolSlotTable.asset");

        /// <summary>bus、state：接到別的測試的黑板（CourseFlowTests 走真的拿放）；null＝自己建一個。</summary>
        public BeltRig(EventBus bus = null, SimState state = null)
        {
            ServiceLocator.Clear();
            bus = bus ?? new EventBus(); s = state ?? new SimState(bus);
            ServiceLocator.Register<IEventBus>(bus); ServiceLocator.Register<ISimState>(s);
            bus.Subscribe<ErrorDetected>(e => errors.Add(e));
            root = new GameObject("TestBelt"); root.SetActive(false);
            foreach (var def in AssetDatabase.FindAssets("t:ToolDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ToolDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null && d.hotkey > 0))
            {
                var go = new GameObject(def.toolId); go.transform.SetParent(root.transform);
                ((ToolBase)go.AddComponent(PantographSim.Editor.SliceSceneBuilder.ToolTypes.TryGetValue(def.toolId, out var type) ? type : typeof(CarryTool))).Setup(def, def.toolId);   // 和場景同一種工具元件
            }
            var rule = AssetDatabase.FindAssets("t:SafetyRuleDefinition").Select(g => AssetDatabase.LoadAssetAtPath<SafetyRuleDefinition>(AssetDatabase.GUIDToAssetPath(g))).First(r => r.kind == SafetyRuleKind.ToolNotStowedOnRoof);
            belt = root.AddComponent<ToolBelt>();
            belt.Setup(Table, rule);
            root.SetActive(true);
            typeof(ToolBelt).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(belt, null);   // EditMode 不會自動呼叫 Awake（收集工具）
            typeof(ToolBelt).GetMethod("Bind", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(belt, null);
        }

        public string Slot(int k) => s.Get(RackLogic.SlotKey(k), RackLogic.OnRack);
        public void Dispose() { Object.DestroyImmediate(root); ServiceLocator.Clear(); }
    }

    /// <summary>ToolBagTests（§5）：InRack 依 homeStorageId 判定；清點依資料算；車頂放開會垂掛並記 Major，地面放開不記錯；腰間位置只跟水平方向轉。</summary>
    public class ToolBagTests
    {
        [Test]
        public void InRack_FollowsHomeStorage_CountFromData()
        {
            using (var b = new BeltRig())
            {
                var bag = BeltRig.Table.At(ToolSlotTable.ToolBag, "periodic").Select(x => x.slot).ToList();
                Assert.IsEmpty(ToolStorageLogic.Missing(b.s, BeltRig.Table, ToolSlotTable.ToolBag, "periodic"), "開場：工具袋的掛位都在袋");
                b.belt.ClickSlot(bag[0]);
                Assert.AreEqual(RackLogic.InHand, b.Slot(bag[0]));
                CollectionAssert.AreEqual(new[] { bag[0] }, ToolStorageLogic.Missing(b.s, BeltRig.Table, ToolSlotTable.ToolBag, "periodic").Select(x => x.slot), "清點依資料：只少拿出來的那一件");
                b.belt.ClickSlot(bag[0]);
                Assert.IsTrue(ToolStorageLogic.InHome(b.s, bag[0]), "收回工具袋＝在它所屬的存放處");
                CollectionAssert.DoesNotContain(BeltRig.Table.At(ToolSlotTable.ToolBag, "daily").Select(x => x.slot).ToList(), 13, "大扭力扳手 slot_13 只有定期檢修");
            }
        }

        [Test]
        public void ReleaseOnRoof_HangsAndLogsMajor_OnGround_NoError()
        {
            using (var b = new BeltRig())
            {
                b.belt.Rules = ScriptableObject.CreateInstance<ToolRulesDef>();   // 預設：放開就掉、寬限 1.5 s
                b.s.Set("training.mode", "practice");
                b.belt.ClickSlot(6);
                b.belt.Release(true);
                Assert.AreEqual(RackLogic.Hanging, b.Slot(6), "車頂放開＝掛在防墜繫繩上垂著");
                Assert.AreEqual("", b.s.Get(SimIds.ActiveTool));
                Assert.IsEmpty(b.errors, "寬限內還不記（R1.5b b4 負責人裁定：1.5 s 內抓回不算）");
                b.belt.TickRelease(SimClock.Now + 2f);
                var e = b.errors.Single();
                Assert.AreEqual(("ERR_Tool_NotStowedOnRoof", Severity.Major), (e.errorId, e.severity));
                b.belt.ClickSlot(6);
                Assert.AreEqual(RackLogic.InHand, b.Slot(6), "垂著的工具可以撿回來");
                b.belt.Release(false);
                Assert.AreEqual(RackLogic.Dropped, b.Slot(6), "地面放開＝放在地上");
                Assert.AreEqual(1, b.errors.Count, "地面放開不記錯");
                Assert.AreEqual(6, ToolStorageLogic.Missing(b.s, BeltRig.Table, ToolSlotTable.ToolBag, "periodic").Single().slot, "清點時要收回");
            }
        }

        [Test]
        public void ToolBag_Pose_LeftFrontOfArrival_WaistHeight()
        {
            var p = ScriptableObject.CreateInstance<PantographSim.Data.PostureDef>();
            var pose = PantographSim.XR.XrCarryContainer.ToolBagPose(new Vector3(1f, 1.6f, 2f), Quaternion.Euler(0f, 90f, 0f), 0f, false, p);   // 面向 +x：左邊是 +z
            Assert.Less(Vector3.Distance(new Vector3(1.15f, p.standEye - p.toolBagBelowEye, 2.2f), pose.position), 1e-4f);
            var up = PantographSim.XR.XrCarryContainer.ToolBagPose(new Vector3(1f, 1.0f, 2f), Quaternion.Euler(50f, 90f, 0f), 2.5f, false, p);
            Assert.AreEqual(2.5f + p.standEye - p.toolBagBelowEye, up.position.y, 1e-4f, "高度只看樓層，到站時低頭、蹲著都一樣");
            Assert.Less(Vector3.Distance(new Vector3(1.15f, 0f, 1.8f), Vector3.Scale(PantographSim.XR.XrCarryContainer.ToolBagPose(new Vector3(1f, 1.6f, 2f), Quaternion.Euler(0f, 90f, 0f), 0f, true, p).position, new Vector3(1f, 0f, 1f))), 1e-4f, "選單對調左右");
            Object.DestroyImmediate(p);
        }

        [Test]
        public void GripTap_IsNotARelease()
        {
            var rules = PantographSim.Data.ToolRulesDef.Load();
            Assert.IsFalse(PantographSim.XR.XrPlayer.IsDeliberateRelease(rules.releaseHoldSeconds - 0.1f, rules), "輕按一下工具留在手上");
            Assert.IsTrue(PantographSim.XR.XrPlayer.IsDeliberateRelease(rules.releaseHoldSeconds, rules));
        }

        [Test]
        public void WaistPosition_FollowsYawOnly()
        {
            var off = new Vector3(-0.2f, -0.65f, 0.1f);
            var head = new Vector3(1f, 1.7f, 2f);
            var a = PantographSim.XR.XrCarryContainer.WaistPosition(head, Quaternion.Euler(0f, 90f, 0f), off, false);
            var b = PantographSim.XR.XrCarryContainer.WaistPosition(head, Quaternion.Euler(40f, 90f, 25f), off, false);
            Assert.Less(Vector3.Distance(a, b), 1e-4f, "低頭、歪頭不跟著動");
            Assert.AreEqual(head.y - 0.65f, a.y, 1e-4f, "頭下方 0.65 m");
            var c = PantographSim.XR.XrCarryContainer.WaistPosition(head, Quaternion.Euler(0f, 0f, 0f), off, false);
            Assert.AreEqual(-0.2f, c.x - head.x, 1e-4f, "工具袋在左腰");
            Assert.AreEqual(0.2f, PantographSim.XR.XrCarryContainer.WaistPosition(head, Quaternion.identity, off, true).x - head.x, 1e-4f, "選單對調後換到右腰");
        }
    }

    /// <summary>ToolHomeStorageTests：兩門課 slot_5–10 的 homeStorageId 都是 env.tool_bag；場景與資料都沒有 env.roof_toolbox、tp.roof.shelf；WC_01b 清點 4、11、12 和桿架；D40 結案：固定帶留在車頂時 WC_01a 清點不合格。</summary>
    public class ToolHomeStorageTests
    {
        [Test]
        public void Slots5to10_InToolBag_BothCourses()
        {
            var t = BeltRig.Table;
            foreach (var course in new[] { "periodic", "daily" })
                for (int k = 5; k <= 10; k++) { var d = t.Find(k); Assert.AreEqual(ToolSlotTable.ToolBag, d.homeStorageId, $"slot_{k}"); Assert.IsTrue(ToolSlotTable.InCourse(d, course)); }
            Assert.AreEqual(ToolSlotTable.PoleRack, t.Find(2).homeStorageId); Assert.AreEqual(ToolSlotTable.PoleRack, t.Find(3).homeStorageId);
        }

        [Test]
        public void NoRoofToolbox_NoRoofShelf_InSceneOrData()
        {
            var set = AssetDatabase.LoadAssetAtPath<StationSet>("Assets/_Project/Modules/Worksite/Data/StationSet.asset");
            Assert.IsNull(set.Find("tp.roof.shelf"), "StationSet 沒有 tp.roof.shelf");
            Assert.IsEmpty(AssetDatabase.FindAssets("tp.roof.shelf"), "沒有 tp.roof.shelf 資產");
            StationTests.WithScene(scene =>
            {
                var ids = StationTests.SceneIds(scene);
                Assert.IsFalse(ids.ContainsKey("env.roof_toolbox"), "場景沒有車頂工具箱");
                Assert.IsFalse(scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Any(x => x.name == "ToolBox_Roof"));
                for (int k = 5; k <= 10; k++) Assert.IsTrue(ids.ContainsKey("env.tool_rack.slot_" + k) && ids["env.tool_rack.slot_" + k].name == "Pocket_" + k, $"slot_{k} 在工具袋的 Pocket_{k}");
                foreach (var k in new[] { 1, 4, 11, 12 }) Assert.AreEqual("Slot_" + k, ids["env.tool_rack.slot_" + k].name, $"slot_{k} 在平台工具架");
            });
        }

        [Test]
        public void Wc01b_CountsRack4_11_12_AndPoleRack()
        {
            var t = BeltRig.Table;
            CollectionAssert.AreEquivalent(new[] { 4, 11, 12 }, t.At(ToolSlotTable.ToolRack, "periodic", includePouch: false).Select(x => x.slot), "D30：slot_1 作業期間在腰包，WC_01b 不清點");
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, t.At(ToolSlotTable.PoleRack, "periodic").Select(x => x.slot));
        }

        [Test]
        public void HoldDownStrapLeftOnRoof_Wc01aCountFails()
        {
            using (var b = new BeltRig())
            {
                b.belt.ClickSlot(10);                 // 拿出固定帶
                b.s.Set(RackLogic.SlotKey(10), RackLogic.InUse); b.s.Set(SimIds.ActiveTool, "");   // 綁在弓上（使用中），沒收回
                var missing = ToolStorageLogic.Missing(b.s, BeltRig.Table, ToolSlotTable.ToolBag, "periodic");
                Assert.AreEqual(10, missing.Single().slot, "固定帶沒收回工具袋，WC_01a 清點不合格（件數從資料算）");
            }
        }
    }

    /// <summary>WaistPouchTests（D30）：開始時 slot_1 在工具架；領取後「在腰包」；收回腰包算收好、不記錯；交接時掛回工具架。</summary>
    public class WaistPouchTests
    {
        [Test]
        public void LockoutKit_RackToPouch_StowedNoError_BackToRack()
        {
            using (var b = new BeltRig())
            {
                Assert.AreEqual(RackLogic.OnRack, b.Slot(1), "開始時在工具架");
                b.belt.ClickSlot(1);
                Assert.AreEqual(RackLogic.InPouch, b.Slot(1), "SI_00 f 領取後＝在腰包");
                Assert.AreEqual("", b.s.Get(SimIds.ActiveTool));
                b.s.Bus().Publish(new ActionRequested { targetId = ToolSlotTable.WaistPouch, toolId = "", text = "click" });
                Assert.AreEqual("tool.lockout_tag", b.s.Get(SimIds.ActiveTool), "SI_02 a 從腰包拿出");
                b.s.Bus().Publish(new ActionRequested { targetId = ToolSlotTable.WaistPouch, toolId = "tool.lockout_tag", text = "click" });
                Assert.AreEqual(RackLogic.InPouch, b.Slot(1), "SI_03 c 收回腰包");
                Assert.IsTrue(ToolStorageLogic.Stowed(b.s, 1), "在腰包算收好");
                Assert.IsEmpty(b.errors, "不記錯");
                b.belt.ClickSlot(1);
                Assert.AreEqual(RackLogic.OnRack, b.Slot(1), "WC_06 c 掛回工具架");
            }
        }
    }

    static class SimStateTestExtensions
    {
        public static IEventBus Bus(this SimState s) => ServiceLocator.Get<IEventBus>();
    }

    /// <summary>PreHeldToolTests（§3.5）：進入一步時已經拿著該步要的工具，「拿出」小動作在同一幀完成。</summary>
    public class PreHeldToolTests
    {
        const string Json = @"{ ""procedureId"": ""PRC_P"", ""steps"": [
            { ""stepId"": ""S1"", ""completion"": { ""type"": ""StateEquals"", ""key"": ""a"", ""value"": ""1"" } },
            { ""stepId"": ""S2"", ""completion"": { ""type"": ""StateEquals"", ""key"": ""b"", ""value"": ""1"" },
              ""subActions"": [ { ""text"": ""拿出卡尺"", ""done"": ""tool.active=tool.caliper"" }, { ""text"": ""量"", ""done"": ""b=1"" } ] } ] }";

        [Test]
        public void HoldingToolOnEnter_PickSubActionDoneSameFrame()
        {
            var bus = new EventBus(); var s = new SimState(bus);
            var runner = new ProcedureRunner(ProcedureJsonConverter.Build(Json).procedure, bus, s);
            runner.Start();
            s.Set(SimIds.ActiveTool, "tool.caliper");   // 上一步就拿著
            s.Set("a", "1");
            Assert.AreEqual("S2", runner.Current.RunId);
            Assert.AreEqual(1, (int)s.GetFloat("procedure.sub_index"), "「拿出」小動作在進入那一幀就完成");
        }

        /// <summary>R1.5b b4（軟體工程裁定，補充 2b）：version 規則開著，「拿出」只讀 LiveState 鍵（tool.active）：已拿著就同一個 tick 完成，拿的是別的工具不完成。R1 與日常課的 CSR_02 都跑。</summary>
        [TestCase("R1")]
        [TestCase("daily")]
        public void FreshRule_HoldingToolOnEnter_SameTick_OtherToolNot(string source)
        {
            foreach (var (held, done) in new[] { (SimIds.ToolCaliper, true), (SimIds.ToolSocket, false) })
            {
                var step = source == "R1"
                    ? ProcedureJsonConverter.BuildStep(PantographSim.Editor.StepsCsvImporter.LoadR1()["STP_CSR_02_ZeroCaliper"])
                    : AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(PantographSim.Editor.StepsCsvImporter.ProcedurePath("daily")).steps.First(x => x.stepId == "STP_CSR_02_ZeroCaliper");
                var prc = ScriptableObject.CreateInstance<ProcedureDefinition>();
                prc.procedureId = "PRC_PreHeld"; prc.steps = new List<StepDefinition> { step }; prc.requireFreshWrite = true;
                var bus = new EventBus(); var s = new SimState(bus);
                s.Set(SimIds.ActiveTool, held);   // 進入這一步之前就拿著
                var runner = new ProcedureRunner(prc, bus, s);
                try
                {
                    runner.Start();
                    Assert.AreEqual(done ? 1 : 0, (int)s.GetFloat("procedure.sub_index"), $"{source}：拿著 {held} 進入 CSR_02，「手持卡尺」{(done ? "同一個 tick 完成" : "不完成")}");
                }
                finally { runner.Dispose(); Object.DestroyImmediate(prc); if (source == "R1") Object.DestroyImmediate(step); }
            }
        }
    }

    /// <summary>TorqueClickTests（K3）：到扭力時播一次喀聲（替身記錄），沒到不播；資料沒指定 clip 就用內建音效。</summary>
    public class TorqueClickTests
    {
        [Test]
        public void ReachPlaysOnce_NotReachedSilent_BuiltinWhenNoClip()
        {
            var played = new List<AudioClip>();
            var keep = ToolInventory.ClickPlayer;
            ToolInventory.ClickPlayer = c => played.Add(c);
            try
            {
                Assert.IsFalse(PantographSim.Tools.TorqueWrenchTool.ReachStep(10f, 20f, 25f, null), "沒到扭力");
                Assert.IsEmpty(played, "沒到扭力不播");
                Assert.IsTrue(PantographSim.Tools.TorqueWrenchTool.ReachStep(24f, 25.2f, 25f, null));
                Assert.AreEqual(1, played.Count, "到扭力播一次");
                Assert.IsNotNull(played[0], "沒指定 clip 時用內建短音效，不可以沒聲音");
                Assert.AreSame(ToolInventory.BuiltinClick, played[0]);
                Assert.IsFalse(PantographSim.Tools.TorqueWrenchTool.ReachStep(25.2f, 26f, 25f, null), "過了以後繼續按不再播");
                var clip = AudioClip.Create("custom", 100, 1, 22050, false);
                PantographSim.Tools.TorqueWrenchTool.ReachStep(24f, 25.5f, 25f, clip);
                Assert.AreSame(clip, played[1], "資料指定的 clip");
                Object.DestroyImmediate(clip);
            }
            finally { ToolInventory.ClickPlayer = keep; }
        }
    }
}
