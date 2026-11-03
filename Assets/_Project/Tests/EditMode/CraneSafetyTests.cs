using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.Procedures;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>
    /// R1.5b b4 3c CraneSafetyTests（第 2 部分 2.6）：讀 ErrorRules.json 的天車錯誤逐條測（附錄 A 1.3）。
    /// 全程監看的條件（WhenCondition）每條各一個觸發情境與一個不觸發情境（腳本寫黑板，還沒有寫入機制的鍵列在 b4c-stubs.md）；
    /// 紅區、碰撞兩條由天車機構直接記，已由 CraneRedZoneTests、CraneCollisionTests 測。資料新增天車錯誤卻沒有情境，這裡就失敗。
    /// </summary>
    public class CraneSafetyTests
    {
        const string L = "part.pan_assy.state=Lifted";

        /// <summary>id → (觸發：依序寫入的條件, 不觸發)。每個字串是一個 tick。</summary>
        static readonly Dictionary<string, (string[] hit, string[] miss)> Cases = new Dictionary<string, (string[], string[])>
        {
            { "ERR_Crane_PersonOnRoofDuringLift", (new[] { "harness.roof", L }, new[] { "harness.roof=false", L }) },
            { "ERR_Crane_RiggingNotInspected", (new[] { L }, new[] { "step.STP_RM_05_CraneRiggingCheck.done", L }) },
            { "ERR_Crane_HoldDownMissing", (new[] { "ip.pan_assy.hold_down.state=Removed", "ip.pan_assy.lift_point_1.state=Rigged" }, new[] { "ip.pan_assy.hold_down.state=Installed", "ip.pan_assy.lift_point_1.state=Rigged" }) },
            { "ERR_Crane_RiggedOnArm", (new[] { "part.pan_assy.rig_misplaced" }, new[] { "ip.pan_assy.lift_point_1.state=Rigged" }) },
            { "ERR_Crane_LowClearance", (new[] { L, "part.crane.load_bottom_y=3.0", "ctrl.crane.cmd=E" }, new[] { L, "part.crane.load_bottom_y=3.2", "ctrl.crane.cmd=E" }) },
            { "ERR_Crane_OperatorTooClose", (new[] { L, "part.crane.operator_distance_m=1.5" }, new[] { L, "part.crane.operator_distance_m=2.5" }) },
            { "ERR_Crane_NoTrialLift", (new[] { L, "part.crane.drop_distance=0.4" }, new[] { "part.crane.trial_ok", L, "part.crane.drop_distance=0.4" }) },
            { "ERR_Crane_SideLoad", (new[] { "part.crane.hook_cog_offset_cm=8", L }, new[] { "part.crane.hook_cog_offset_cm=2", L }) },
            { "ERR_Crane_LiftWhileFixed", (new[] { "part.crane.pull_fixed_s=3.5" }, new[] { "part.crane.pull_fixed_s=2" }) },
            { "ERR_Crane_UnrigBeforeClamp", (new[] { "part.pan_assy.state=OnStand", "ip.pan_assy.lift_point_1.state=Free" },
                new[] { "env.panto_stand.clamp_1.state=Locked", "env.panto_stand.clamp_2.state=Locked", "part.pan_assy.state=OnStand", "ip.pan_assy.lift_point_1.state=Free" }) },
            { "ERR_Crane_UnderMovingHook", (new[] { "part.crane.head_to_hook_m=0.3", "part.crane.empty_hook_moving" }, new[] { "part.crane.head_to_hook_m=0.8", "part.crane.empty_hook_moving" }) },
            { "ERR_Crane_FastLowerEmptyHook", (new[] { "part.crane.beam_above_surface_m=1.5", "ctrl.crane.fast", "ctrl.crane.cmd=Down", "part.crane.empty_hook_moving" },
                new[] { "part.crane.beam_above_surface_m=3", "ctrl.crane.fast", "ctrl.crane.cmd=Down", "part.crane.empty_hook_moving" }) },
        };

        /// <summary>天車機構直接記的兩條（不是 WhenCondition）：對應的既有測試。</summary>
        static readonly Dictionary<string, (System.Type type, string method)> Mechanism = new Dictionary<string, (System.Type, string)>
        {
            { "ERR_Crane_PersonUnderLoad", (typeof(CraneRedZoneTests), "EnterRedZone_StopsSameFrame_LogsCriticalOnce_KeysIgnored_LeaveResumes") },
            { "ERR_Crane_Collision", (typeof(CraneMechanismTests), "Collision_StopsAndLogsCritical") },
        };

        static IEnumerable<string> CraneIds() => StepsCsvImporter.LoadErrorRules().Keys.Where(k => k.StartsWith("ERR_Crane_")).OrderBy(k => k);

        [Test]
        public void EveryCraneRuleInData_HasScenario()
        {
            var defs = StepsCsvImporter.LoadErrorRules();
            foreach (var id in CraneIds())
            {
                if (Mechanism.TryGetValue(id, out var m)) { Assert.IsNotNull(m.type.GetMethod(m.method), $"{id}：{m.type.Name}.{m.method} 要存在"); continue; }
                Assert.IsTrue(Cases.ContainsKey(id), $"{id}：ErrorRules.json 有這條天車錯誤，這裡沒有測試情境");
                Assert.AreEqual("WhenCondition", defs[id].rule?.trigger, id + "：要寫成全程監看的條件");
                Assert.AreEqual("global", defs[id].scope);
            }
            Assert.IsEmpty(Cases.Keys.Except(CraneIds()).ToList(), "情境對應的 ID 在資料裡要存在");
        }

        static List<ErrorDetected> Play(string[] writes)
        {
            var src = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath("periodic"));
            var prc = ScriptableObject.CreateInstance<ProcedureDefinition>();
            prc.procedureId = "PRC_Crane"; prc.steps = new List<StepDefinition> { src.steps.First(s => s.stepId == "STP_LF_03_Clearance") };
            prc.globalErrors = src.globalErrors;
            var bus = new EventBus(); var s = new SimState(bus);
            s.Set("training.mode", "practice");
            var runner = new ProcedureRunner(prc, bus, s);
            var det = new ErrorDetector(runner, bus, s);
            var got = new List<ErrorDetected>();
            bus.Subscribe<ErrorDetected>(e => got.Add(e));
            runner.Start();
            try { foreach (var w in writes) CourseFlowTests.Write(s, w); }
            finally { det.Dispose(); runner.Dispose(); Object.DestroyImmediate(prc); }
            return got;
        }

        static IEnumerable<string> ConditionIds() => Cases.Keys.OrderBy(k => k);

        [TestCaseSource(nameof(ConditionIds))]
        public void Rule_TriggersInScenario_NotOtherwise(string id)
        {
            var defs = StepsCsvImporter.LoadErrorRules();
            var hit = Play(Cases[id].hit).Where(e => e.errorId == id).ToList();
            Assert.AreEqual(1, hit.Count, $"{id}：觸發情境要記一次（{string.Join(" → ", Cases[id].hit)}）");
            Assert.AreEqual(defs[id].severity, hit[0].severity.ToString(), id + "：嚴重度照 ErrorRules.json");
            Assert.IsEmpty(Play(Cases[id].miss).Where(e => e.errorId == id).ToList(), $"{id}：不觸發情境不可以記（{string.Join(" → ", Cases[id].miss)}）");
        }

        [Test]
        public void DailyCourse_HasNoCraneRules()
        {
            var daily = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath("daily"));
            Assert.IsFalse(daily.globalErrors.Any(e => e.errorId.StartsWith("ERR_Crane_")), "日常更換不用天車");
            var periodic = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath("periodic"));
            CollectionAssert.IsSubsetOf(ConditionIds().ToList(), periodic.globalErrors.Select(e => e.errorId).ToList(), "定期檢修全程監看這些天車錯誤");
        }

        [Test]
        public void HorizontalDistance_ToLoadProjection()
        {
            var b = new Bounds(Vector3.zero, new Vector3(2f, 1f, 2f));   // XZ −1..1
            Assert.AreEqual(0f, CraneLogic.HorizontalDistance(b, new Vector3(0.5f, 5f, 0.5f)), 1e-5f, "在投影內＝0（不看高度）");
            Assert.AreEqual(1.5f, CraneLogic.HorizontalDistance(b, new Vector3(2.5f, 0f, 0f)), 1e-5f);
            Assert.AreEqual(Mathf.Sqrt(2f), CraneLogic.HorizontalDistance(b, new Vector3(2f, 0f, 2f)), 1e-5f);
        }
    }
}
