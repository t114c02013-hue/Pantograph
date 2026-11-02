using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// 讓 Editor 開著時也能從外部跑 EditMode 測試：建立 Logs/run-tests.request 後，
    /// 這裡會自動執行全部 EditMode 測試，把結果寫到 Logs/slice-tests.xml 與 Logs/slice-tests.txt。
    /// 也可從選單 PantographSim/Run EditMode Tests 手動執行。
    /// </summary>
    [InitializeOnLoad]
    public static class TestRunHook
    {
        const string RequestPath = "Logs/run-tests.request";
        const string RefreshPath = "Logs/refresh.request";
        const string BuildPath = "Logs/build-slice.request";
        const string ImportPath = "Logs/import-steps.request";
        const string ExportStepsPath = "Logs/export-steps.request";   // 匯出步驟表 Logs/StepTable.csv（唯讀）   // 只重新匯入步驟資料（JSON → Steps/*.asset），不重建場景
        const string SummaryPath = "Logs/slice-tests.txt";
        const string XmlPath = "Logs/slice-tests.xml";
        static double nextCheck;
        static bool running;

        static TestRunHook()
        {
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (running || EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + 2.0;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(RefreshPath))
            {
                // 外部修改了檔案：重新整理資產（會觸發重新編譯）；測試請求留到編譯完再處理
                File.Delete(RefreshPath);
                AssetDatabase.Refresh();
                return;
            }
            if (File.Exists(BuildPath))
            {
                // 外部要求重建切片場景（匯入步驟 → 模型 → 場景 → 檢查 → 預覽圖）
                File.Delete(BuildPath);
                try { SliceSceneBuilder.Build(); }
                catch (System.Exception e) { File.WriteAllText("Logs/SliceSceneReport.txt", "建置失敗：" + e); Debug.LogException(e); }
                return;
            }
            if (File.Exists(ImportPath))
            {
                File.Delete(ImportPath);
                try { File.WriteAllText("Logs/import-steps.txt", StepJsonImporter.Import()); }
                catch (System.Exception e) { File.WriteAllText("Logs/import-steps.txt", "匯入失敗：" + e); Debug.LogException(e); }
                return;
            }
            if (File.Exists("Logs/reimport-models.request"))   // R1.5a a2：重新匯入 Blender 產生的模型（先建外部材質）
            {
                File.Delete("Logs/reimport-models.request");
                try { GeneratedModelPostprocessor.ReimportGenerated(); }
                catch (System.Exception e) { Debug.LogException(e); }
                return;
            }
            if (File.Exists("Logs/export-car.request"))   // R1.5a a2：RoofSceneDef → car_params.json
            {
                File.Delete("Logs/export-car.request");
                try { CarParamsExport.Export(); }
                catch (System.Exception e) { Debug.LogException(e); }
                return;
            }
            if (File.Exists("Logs/pan-probe.request"))   // R1.5a a0：集電弓結構確認（只讀）
            {
                File.Delete("Logs/pan-probe.request");
                try { PanRiggingProbe.Run(); }
                catch (System.Exception e) { Debug.LogException(e); }
                return;
            }
            if (File.Exists(ExportStepsPath))
            {
                File.Delete(ExportStepsPath);
                try { StepTableMenu.Export(); }
                catch (System.Exception e) { Debug.LogException(e); }
                return;
            }
            if (File.Exists("Logs/r15a-report.request"))   // R1.5a a6：驗收報告 Logs/R1.5aReport.txt（唯讀）
            {
                File.Delete("Logs/r15a-report.request");
                try { R15aReport.Write(); }
                catch (System.Exception e) { Debug.LogException(e); }
                return;
            }
            if (!File.Exists(RequestPath)) return;
            File.Delete(RequestPath);
            Run();
        }

        [MenuItem("PantographSim/Run EditMode Tests")]
        public static void Run()
        {
            running = true;
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks(api));
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        class Callbacks : ICallbacks
        {
            readonly TestRunnerApi api;
            public Callbacks(TestRunnerApi api) { this.api = api; }

            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                running = false;
                var sb = new StringBuilder();
                sb.AppendLine($"結果：{result.ResultState}　通過 {result.PassCount}　失敗 {result.FailCount}　略過 {result.SkipCount}　耗時 {result.Duration:F1}s");
                foreach (var leaf in Leaves(result))
                {
                    string mark = leaf.TestStatus == TestStatus.Passed ? "PASS" : leaf.TestStatus == TestStatus.Failed ? "FAIL" : "SKIP";
                    sb.AppendLine($"[{mark}] {leaf.FullName}");
                    if (leaf.TestStatus == TestStatus.Failed) sb.AppendLine("       " + leaf.Message?.Trim().Replace("\n", "\n       "));
                }
                Directory.CreateDirectory("Logs");
                File.WriteAllText(SummaryPath, sb.ToString());
                File.WriteAllText(XmlPath, result.ToXml().OuterXml);
                Debug.Log("[TestRunHook] 測試完成\n" + sb);
                api.UnregisterCallbacks(this);
            }

            static System.Collections.Generic.IEnumerable<ITestResultAdaptor> Leaves(ITestResultAdaptor r) =>
                r.HasChildren ? r.Children.SelectMany(Leaves) : new[] { r };
        }
    }
}
