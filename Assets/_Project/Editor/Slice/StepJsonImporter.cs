using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Data;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// 把 carbon_strip_slice.json 轉成資產：STP_*（步驟）、PRC_CarbonStripSlice（程序）、SR_*（安全規則）、SCN_*（情境）、TL_*（工具）。
    /// 已存在的資產以 CopySerialized 覆寫，GUID 不變，場景引用不會斷。可重跑。
    /// </summary>
    public static class StepJsonImporter
    {
        public const string JsonPath = "Assets/_Project/Modules/Procedures/Data/carbon_strip_slice.json";
        public const string StepFolder = "Assets/_Project/Modules/Procedures/Data/Steps";
        public const string ProcedurePath = "Assets/_Project/Modules/Procedures/Data/PRC_CarbonStripSlice.asset";
        public const string SafetyFolder = "Assets/_Project/Modules/Safety/Data";
        public const string ScenarioFolder = "Assets/_Project/Data/Scenarios";
        const string ToolsRoot = "Assets/_Project/Modules/Tools";
        public const string PositionCheckFolder = "Assets/_Project/Modules/Parts/_Common/Data";
        public const string LiftSettingsPath = "Assets/_Project/Modules/Worksite/Data/LiftSettings.asset";

        static readonly Dictionary<string, string> ToolFolders = new Dictionary<string, string>
        {
            { "tool.voltage_detector", "VoltageDetector" }, { "tool.grounding_rod", "GroundingRod" }, { "tool.caliper", "Caliper" },
            { "tool.socket_wrench", "SocketWrench" }, { "tool.torque_wrench", "TorqueWrench" }, { "tool.cleaning_cloth", "CleaningCloth" },
            { "tool.marker", "Marker" }, { "tool.lockout_tag", "LockoutTag" }, { "tool.safety_harness", "SafetyHarness" }
        };

        public static string ToolAssetPath(string toolId) =>
            $"{ToolsRoot}/{(ToolFolders.TryGetValue(toolId, out var f) ? f : "_Common")}/Data/TL_{toolId.Replace("tool.", "")}.asset";

        [MenuItem("PantographSim/Slice/Import Steps")]
        public static void ImportMenu()
        {
            var report = Import();
            Debug.Log("[StepJsonImporter]\n" + report);
        }

        public static string Import()
        {
            var sb = new StringBuilder();
            var bundle = ProcedureJsonConverter.Build(File.ReadAllText(JsonPath));

            var savedSteps = new List<StepDefinition>();
            foreach (var step in bundle.procedure.steps)
                savedSteps.Add(Save(step, $"{StepFolder}/{step.stepId}.asset"));
            sb.AppendLine($"步驟 {savedSteps.Count} 個 → {StepFolder}");

            bundle.procedure.steps = savedSteps;
            Save(bundle.procedure, ProcedurePath);
            sb.AppendLine("程序 → " + ProcedurePath);

            foreach (var r in bundle.safetyRules) Save(r, $"{SafetyFolder}/{r.ruleId}.asset");
            sb.AppendLine($"安全規則 {bundle.safetyRules.Count} 條 → {SafetyFolder}");
            foreach (var c in bundle.scenarios) Save(c, $"{ScenarioFolder}/{c.scenarioId}.asset");
            sb.AppendLine($"情境 {bundle.scenarios.Count} 個 → {ScenarioFolder}");
            foreach (var t in bundle.tools) Save(t, ToolAssetPath(t.toolId));
            sb.AppendLine($"工具 {bundle.tools.Count} 個 → Modules/Tools/*/Data");

            // 位置檢測與升降參數：保留場景建置工具量測寫回的座標與行程
            foreach (var pc in bundle.positionChecks)
            {
                string path = $"{PositionCheckFolder}/{pc.checkId}.asset";
                var old = AssetDatabase.LoadAssetAtPath<PositionCheckDefinition>(path);
                if (old != null) { pc.checkPointLocal = old.checkPointLocal; pc.targetLocal = old.targetLocal; }
                Save(pc, path);
            }
            sb.AppendLine($"位置檢測 {bundle.positionChecks.Count} 個 → {PositionCheckFolder}");
            var oldLift = AssetDatabase.LoadAssetAtPath<LiftSettings>(LiftSettingsPath);
            if (oldLift != null) bundle.lift.travelCm = oldLift.travelCm;
            Save(bundle.lift, LiftSettingsPath);
            sb.AppendLine("升降參數 → " + LiftSettingsPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return sb.ToString();
        }

        static T Save<T>(T obj, string path) where T : ScriptableObject
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                // a6 增補 6 第 6 點：內容沒變就不寫（SerializeReference 的 rid 每次新建都不同，比對時拿掉；不然每次重建 25 個 STP_*.asset 都會變）
                string Norm(Object o) => System.Text.RegularExpressions.Regex.Replace(EditorJsonUtility.ToJson(o), "\"(rid|m_Name)\":\\s*(-?\\d+|\"[^\"]*\")", "");
                if (Norm(obj) == Norm(existing)) { Object.DestroyImmediate(obj); return existing; }
                EditorUtility.CopySerialized(obj, existing);
                existing.name = Path.GetFileNameWithoutExtension(path);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(obj);
                return existing;
            }
            obj.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }

        public static ProcedureDefinition LoadProcedure() => AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(ProcedurePath);

        public static List<T> LoadAll<T>(string folder) where T : ScriptableObject =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder }).Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x != null).ToList();

        public static List<ToolDefinition> LoadTools() => LoadAll<ToolDefinition>(ToolsRoot).OrderBy(t => t.hotkey).ToList();
    }
}
