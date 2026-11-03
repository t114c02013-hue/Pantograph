using System.IO;
using System.Linq;
using NUnit.Framework;
using PantographSim.Data;
using PantographSim.Procedures;

namespace PantographSim.Tests
{
    /// <summary>步驟表匯出：每列一個小動作，資料列數＝從步驟資料算出的小動作總數（不寫死，步驟換版也不用改測試）。</summary>
    public class StepTableExportTests
    {
        const string JsonPath = "Assets/_Project/Modules/Procedures/Data/carbon_strip_slice.json";

        [Test]
        public void Csv_RowCount_EqualsSubActionTotal_FromStepData_AndHeaderMatches()
        {
            string json = File.ReadAllText(JsonPath);
            var bundle = ProcedureJsonConverter.Build(json);
            int expected = StepTableExport.SubActionCount(bundle.procedure);
            Assert.Greater(expected, 0);
            var csv = StepTableExport.Build(bundle.procedure);
            var rows = csv.Split(new[] { "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(StepTableExport.Header, rows[0]);
            Assert.AreEqual(expected, rows.Length - 1, "每列一個小動作");
            Assert.AreEqual(json, File.ReadAllText(JsonPath), "匯出是唯讀的，步驟資料不能被改");
            Assert.IsTrue(rows.Skip(1).Any(r => r.Contains("tp.roof.inner")) && rows.Skip(1).Any(r => r.Contains(":Critical")), "站點與錯誤嚴重度都有匯出");
        }

        /// <summary>R1.5b b4：兩門課在同一份表、course 欄區分；列數＝SubActions.csv 展開後的總數（讀資料算，不寫死）；textXR／textDesktop（K4）、zoneId、errorIds 都有。</summary>
        [Test]
        public void Courses_OneTable_RowsEqualCsvExpanded_TextXrDesktopZone()
        {
            var csv = PantographSim.Editor.StepTableMenu.BuildCourses();
            var rows = csv.Split(new[] { "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(StepTableExport.Header, rows[0]);
            int expanded = PantographSim.Editor.StepsCsvImporter.Expand(PantographSim.Editor.StepsCsvImporter.LoadSubActionRows(), new System.Collections.Generic.List<string>()).Count;
            Assert.AreEqual(expanded, rows.Length - 1, "列數＝CSV 展開後的總數");
            var cols = rows.Skip(1).Select(r => PantographSim.Editor.StepsCsvImporter.ParseCsv(r + "\n")[0]).ToList();
            CollectionAssert.AreEquivalent(new[] { "periodic", "daily" }, cols.Select(c => c[0]).Distinct().ToArray(), "course 欄");
            var csr11b = cols.First(c => c[0] == "daily" && c[2] == "STP_CSR_11_Install_L_b");
            StringAssert.Contains("按 R", csr11b[4], "桌面版指令");
            Assert.IsFalse(PantographSim.Editor.DataValidator.XrKeyboardWords.IsMatch(csr11b[3]), "VR 版指令沒有鍵盤字樣：" + csr11b[3]);
            Assert.IsTrue(cols.Where(c => c[9].Length > 0).All(c => c[10].StartsWith("zone.")), "每個有站點的小動作都有 zoneId");
            Assert.IsTrue(cols.Any(c => c[10] == "zone.maintenance") && cols.Any(c => c[10] == "zone.arrival"));
            Assert.IsTrue(cols.Any(c => c[6].Contains("ERR_CSR13_Under:Critical")), "errorIds 寫 ErrorRule 的 ID 與嚴重度");
        }
    }
}
