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
    }
}
