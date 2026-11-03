using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Data;
using PantographSim.Procedures;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// Tools/Export Step Table：R1.5b b4 起匯出兩門課（匯入產生的 PRC_periodic／PRC_daily）到同一份 Logs/StepTable.csv（UTF-8 with BOM），
    /// course 欄區分；zoneId 讀站點所屬作業區。唯讀，不改任何資料。
    /// </summary>
    public static class StepTableMenu
    {
        public const string JsonPath = "Assets/_Project/Modules/Procedures/Data/carbon_strip_slice.json";
        public const string CsvPath = "Logs/StepTable.csv";

        public static string BuildCourses()
        {
            var courses = StepsCsvImporter.Courses.Select(c => (c, AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(c)))).Where(x => x.Item2 != null).ToList();
            return StepTableExport.Build(courses, ZoneOf);
        }

        public static string ZoneOf(string standPointId) => R1Data.Stations.stations.FirstOrDefault(s => s != null && s.id == standPointId)?.zoneId ?? "";

        [MenuItem("Tools/Export Step Table")]
        public static void Export()
        {
            Directory.CreateDirectory("Logs");
            var csv = BuildCourses();
            File.WriteAllText(CsvPath, csv, new UTF8Encoding(true));
            Debug.Log($"[StepTable] 已匯出 {csv.Split(new[] { "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries).Length - 1} 個小動作（兩門課）→ {CsvPath}");
        }
    }
}
