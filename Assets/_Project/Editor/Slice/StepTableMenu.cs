using System.IO;
using System.Text;
using PantographSim.Data;
using PantographSim.Procedures;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>Tools/Export Step Table：步驟資料（carbon_strip_slice.json）→ Logs/StepTable.csv（UTF-8 with BOM）。唯讀，不改任何資料。</summary>
    public static class StepTableMenu
    {
        public const string JsonPath = "Assets/_Project/Modules/Procedures/Data/carbon_strip_slice.json";
        public const string CsvPath = "Logs/StepTable.csv";

        [MenuItem("Tools/Export Step Table")]
        public static void Export()
        {
            var bundle = ProcedureJsonConverter.Build(File.ReadAllText(JsonPath));
            Directory.CreateDirectory("Logs");
            File.WriteAllText(CsvPath, StepTableExport.Build(bundle.procedure), new UTF8Encoding(true));
            Debug.Log($"[StepTable] 已匯出 {StepTableExport.SubActionCount(bundle.procedure)} 個小動作 → {CsvPath}");
        }
    }
}
