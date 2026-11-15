using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Recording;
using PantographSim.Training;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>
    /// W31–W34：紀錄寫入／讀回、中斷重建、報表與 CSV、重播（不產生非重播錯誤、拖曳與連續播放結果相同）、模組依賴。
    /// 用真的步驟資料與 SliceDryRun 的模擬器跑一場，寫到暫存資料夾。
    /// </summary>
    public class RecordingTests
    {
        string root;
        float clock;
        Func<float> oldClock;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "PantoRecTest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            oldClock = SimClock.Source;
            clock = 100f;
            SimClock.Source = () => clock;
            SimSession.EnterTraining();
        }

        [TearDown]
        public void TearDown()
        {
            SimClock.Source = oldClock;
            SimSession.EnterTraining();
            try { Directory.Delete(root, true); } catch (Exception) { }
        }

        class FakePose : IPoseSource
        {
            public Vector3 head = new Vector3(1, 1.6f, 2);
            public string SourceName => "xr";
            public bool PoseAvailable => true;
            public Pose Head => new Pose(head, Quaternion.Euler(10, 20, 0));
            public bool HasHands => true;
            public Pose LeftHand => new Pose(head + Vector3.left * 0.3f, Quaternion.identity);
            public Pose RightHand => new Pose(head + Vector3.right * 0.3f, Quaternion.identity);
        }

        // ---------- 跑一場（故意錯 E1、E3、E5、E8、E15） ----------
        class RecordedRun
        {
            public SliceDryRunTests.SliceSim sim;
            public SessionRecorder recorder;
            public RecordingWriter writer;
            public Scoring scoring;
            public string folder;
            public SessionResult? result;
            public readonly List<ErrorDetected> errors = new List<ErrorDetected>();
        }

        RecordedRun RunWithErrors()
        {
            var run = new RecordedRun { folder = Path.Combine(root, RecordingPaths.FolderName(DateTime.Now, "測試學員")) };
            run.sim = new SliceDryRunTests.SliceSim("SCN_Default", false, sim =>
            {
                run.writer = new RecordingWriter(run.folder);
                run.recorder = new SessionRecorder(sim.bus, sim.s, run.writer, StepCatalog.From(sim.bundle.procedure));
                run.scoring = new Scoring(sim.bus, sim.s);
                run.scoring.IncompleteSteps = () => Array.Empty<string>();
                run.scoring.Start(clock);
                sim.bus.Subscribe<SessionResult>(e => run.result = e);
                sim.bus.Subscribe<ErrorDetected>(e => run.errors.Add(e));
                sim.s.Set("tools.list", "1|tool.lockout_tag|上鎖掛牌組;2|tool.voltage_detector|驗電器;3|tool.grounding_rod|接地棒");
                run.recorder.Begin(new Dictionary<string, object>
                {
                    { "trainee", "測試學員" }, { "mode", "練習" }, { "modeRaw", "practice" }, { "input", "桌機" }, { "scenarioId", "SCN_Default" },
                    { "scenarioTitle", "正常（兩條都要換）" }, { "startedAt", DateTime.Now.ToString("o") }, { "appVersion", "test" }, { "tools", sim.s.Get("tools.list") }
                });
            });
            var s = run.sim;
            var pose = new FakePose();
            void Advance(float dt)
            {
                for (float t = 0; t < dt; t += 0.25f)
                {
                    clock += 0.25f;
                    pose.head += new Vector3(0.002f, 0, 0);
                    run.recorder.Sample(pose, new[] { new KeyValuePair<string, PoseSample>("part.carbon_strip_L", new PoseSample { pos = new Vector3(clock * 0.01f, 0, 0), rot = Quaternion.identity, active = true }) },
                        new[] { new KeyValuePair<string, float>(RecordFiles.LiftChannel, Mathf.Clamp01(1f - (clock - 100f) / 15f)) });
                }
            }
            void RunUntil(string runId)
            {
                for (int guard = 0; guard < 80 && !s.completed && s.Current != runId; guard++)
                {
                    var before = s.Current;
                    s.DoStep();
                    Advance(3f);
                    Assert.AreNotEqual(before, s.Current, "步驟沒有前進：" + s.Describe());
                }
            }

            // E1：還在升弓就碰弓體（Critical）
            Advance(1f);
            Assert.IsFalse(s.Rotate("ip.carbon_strip_L.bolt_1", 180, "loosen"));
            s.bus.Publish(new UiCommandIssued { name = UiCommands.Help, stepId = s.Current, text = "test", time = clock });
            Advance(2f);
            // E3：先掛弓頭（Critical）
            RunUntil("STP_SI_05_Grounding");
            Assert.IsFalse(s.Head());
            Advance(2f);
            // E5：只量中間（Major）
            RunUntil("STP_CSR_03_Measure_L");
            s.MeasureAndRecord("L", "mid");
            s.bus.Publish(new UiCommandIssued { name = UiCommands.FocusTarget, stepId = s.Current, text = "test", time = clock });
            Advance(2f);
            // E8：扭力不足（Major）→ 從這一步重做
            RunUntil("STP_CSR_13_Torque_L");
            for (int n = 1; n <= 4; n++) s.Torque($"ip.carbon_strip_L.bolt_{n}", 20f);
            Advance(2f);
            s.bus.Publish(new RestartStepRequested());
            Advance(1f);
            // E15：工具遺留（Critical）→ 從這一步重做
            RunUntil("STP_WC_01_ToolCount");
            s.s.SetBool("tools.all_returned", false);
            s.Write("toolcount_confirmed", "true");
            Advance(2f);
            s.bus.Publish(new RestartStepRequested());
            Advance(1f);
            RunUntil(null);
            Assert.IsTrue(s.completed, s.Describe());
            Assert.IsTrue(run.result.HasValue, "結算要發 SessionResult");
            run.recorder.Dispose();
            run.writer.Dispose();
            return run;
        }

        static readonly string[] ExpectedErrors = { "ERR_Global_NotLowered", "ERR_Ground_Order", "ERR_CSR03_OnlyMid", "ERR_CSR13_Under", "ERR_WC01_Leftover" };

        // ---------- 1. 寫入後讀回一致；截斷在一行中間仍能重建並標成中斷 ----------
        /// <summary>v3.10 第 8 項：故障處理時間從這一步的時間判定扣掉，別的步驟不受影響。</summary>
        [Test]
        public void FaultTime_SubtractedPerStep()
        {
            var lines = new List<RecordLine>
            {
                new RecordLine { type = "FaultTime", step = "A", data = new Dictionary<string, object> { { "fault", "fault.sling_cut" }, { "seconds", 40.0 } } },
                new RecordLine { type = "FaultTime", step = "A", data = new Dictionary<string, object> { { "fault", "fault.x" }, { "seconds", 2.5 } } },
            };
            Assert.AreEqual(42.5, SummaryBuilder.FaultSeconds(lines, "A"), 1e-6);
            Assert.AreEqual(0, SummaryBuilder.FaultSeconds(lines, "B"), 1e-6);
        }

        /// <summary>審查建議：舊錄影沒有 rack.slots（掛位清單），回放套快照時保留觀看者自己的（ToolBelt 從 ToolSlotTable 寫），掛位標籤和清點不會空白。</summary>
        [Test]
        public void OldRecording_WithoutRackSlots_KeepsViewerSlotList()
        {
            Assert.IsTrue(ReplayTimeline.ViewerOwnedKey(SimIds.RackSlots));
            var s = new SimState(new EventBus());
            s.Set(SimIds.RackSlots, "1,2,3,10,13");
            var merged = new System.Collections.Generic.Dictionary<string, string> { { "rack.slot_1", "on_rack" } };   // 舊錄影的快照：沒有 rack.slots
            foreach (var key in s.Keys.ToList()) if (ReplayTimeline.ViewerOwnedKey(key)) merged[key] = s.Get(key);
            s.Restore(merged);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 10, 13 }, SimIds.RackSlotNumbers(s));
        }

        [Test]
        public void WriteThenRead_RoundTrip_AndTruncatedLine_RebuildsAsInterrupted()
        {
            var bus = new EventBus();
            var state = new SimState(bus);
            string folder = Path.Combine(root, "20261019_120000_中斷測試");
            var writer = new RecordingWriter(folder);
            var rec = new SessionRecorder(bus, state, writer, new StepCatalog());
            var pose = new FakePose();
            rec.Begin(new Dictionary<string, object> { { "trainee", "中斷測試" }, { "mode", "教學" }, { "startedAt", DateTime.Now.ToString("o") } });
            for (int i = 0; i < 40; i++)
            {
                clock += 0.1f;
                pose.head += Vector3.forward * 0.01f;
                bus.Publish(new StepStarted { stepId = "STP_A_" + (i % 3), title = "測試步驟", subActions = "一\u001f\u001f\u001f\u001f\n二\u001f\u001f\u001f\u001f" });
                state.Set("part.test.value", i.ToString());
                bus.Publish(new ErrorDetected { errorId = "ERR_CSR13_Under", stepId = "STP_A_0", severity = Severity.Major, what = "扭力不足", why = "鬆脫", fix = "重鎖" });
                rec.Sample(pose, null, null);
            }
            rec.Tick(clock - rec.T0 + 11f);
            rec.Close("test");
            var written = rec.Lines.Select(l => l.ToJson()).ToList();
            rec.Dispose();
            writer.Dispose();

            // 讀回：每一行內容一致
            var read = RecordLine.ReadAll(Path.Combine(folder, RecordFiles.Session), out int bad);
            Assert.AreEqual(0, bad);
            Assert.AreEqual(written.Count, read.Count);
            for (int i = 0; i < read.Count; i++) Assert.AreEqual(written[i], read[i].ToJson(), "第 " + i + " 行");
            Assert.AreEqual("扭力不足", read.First(l => l.type == "ErrorDetected").S("what"));
            var poses = PoseFile.Read(Path.Combine(folder, RecordFiles.Poses));
            Assert.IsTrue(poses.ContainsKey(RecordFiles.Head) && poses[RecordFiles.Head].Count >= 2, "頭的位姿要寫進 poses.bin");
            Assert.AreEqual(0, Vector3.Distance(poses[RecordFiles.Head].Last().pos, pose.head), 0.02f);
            Assert.GreaterOrEqual(SnapshotFile.Read(Path.Combine(folder, RecordFiles.Snapshots)).Count, 2);

            // 截斷在最後一行中間
            string path = Path.Combine(folder, RecordFiles.Session);
            var bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, bytes.Take(bytes.Length - 25).ToArray());
            Assert.IsFalse(File.Exists(Path.Combine(folder, RecordFiles.Summary)));
            Assert.AreEqual(1, RecordingIndex.RepairInterrupted(root));
            var summary = RecordingIndex.ReadJson(Path.Combine(folder, RecordFiles.Summary));
            Assert.AreEqual(SummaryBuilder.StatusInterrupted, summary.Str("status"));
            Assert.IsNull(summary.Obj("result"), "中斷沒有分數");
            Assert.AreEqual(1, (int)summary.Num("badLines"), "被截斷的那一行略過");
            var entry = RecordingIndex.List(root).Single();
            Assert.IsTrue(entry.interrupted);
            Assert.AreEqual("未完成（中斷）", entry.status);
            StringAssert.Contains("未完成（中斷）", File.ReadAllText(ReportBuilder.Generate(folder), Encoding.UTF8));
        }

        // ---------- 2＆3. 五種錯誤都進報表與 errors.csv；分數與錯誤數等於 SessionResult；CSV 有 BOM ----------
        [Test]
        public void DryRun_FiveErrors_ReportAndCsv_MatchSessionResult()
        {
            var run = RunWithErrors();
            var r = run.result.Value;
            foreach (var id in ExpectedErrors) Assert.IsTrue(run.errors.Any(e => e.errorId == id), "模擬器應產生 " + id + "；實際 " + string.Join(",", run.errors.Select(e => e.errorId)));

            // 結算畫面的數字（黑板 score.*）＝ SessionResult
            Assert.AreEqual(run.sim.s.GetFloat("score.total"), r.score);
            Assert.AreEqual((int)run.sim.s.GetFloat("score.critical"), r.critical);
            Assert.AreEqual(run.sim.s.GetFloat("score.elapsed"), r.totalSeconds);

            string reportPath = ReportBuilder.Generate(run.folder);
            string html = File.ReadAllText(reportPath, Encoding.UTF8);
            var summary = RecordingIndex.ReadJson(Path.Combine(run.folder, RecordFiles.Summary));
            var result = summary.Obj("result");
            Assert.AreEqual(r.score, result.Num("score"), 1e-3);
            Assert.AreEqual(r.critical, (int)result.Num("critical"));
            Assert.AreEqual(r.major, (int)result.Num("major"));
            Assert.AreEqual(r.minor, (int)result.Num("minor"));
            Assert.AreEqual(r.minor + r.major + r.critical, (int)result.Num("errorTotal"));
            Assert.AreEqual(3, r.critical, "E1、E3、E15 三個 Critical（同一步同一錯誤只算一次）");
            StringAssert.Contains($"<b class=\"big\">{r.score:0}</b> 分", html, "第一頁總分取自 SessionResult");
            StringAssert.Contains($"<b class=\"bad\">{r.critical}</b>", html, "第一頁 Critical 次數");
            foreach (var name in new[] { "未降弓就碰弓體", "接地順序錯誤（先掛弓頭）", "靠關節滑板沒有三點都量", "扭力不足", "工具遺留" }) StringAssert.Contains(name, html);
            StringAssert.Contains("鎖到規定扭力（靠關節）", html, "中文步驟名");
            StringAssert.Contains("接地桿先掛弓頭", html, "順序對照");

            // errors.csv：UTF-8 BOM、每個錯誤都在、Critical（同步同錯只算一次）數量正確
            foreach (var csv in RecordFiles.Csvs)
            {
                var b = File.ReadAllBytes(Path.Combine(run.folder, csv));
                Assert.IsTrue(b.Length > 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF, csv + " 開頭要有 BOM");
            }
            var rows = File.ReadAllLines(Path.Combine(run.folder, "errors.csv"), Encoding.UTF8).Skip(1).ToList();
            foreach (var id in ExpectedErrors) Assert.IsTrue(rows.Any(x => x.Contains("," + id + ",")), "errors.csv 要有 " + id);
            int criticalUnique = rows.Where(x => x.Contains("（Critical）")).Select(x => x.Split(',')).Select(f => f[3] + "|" + f[5]).Distinct().Count();
            Assert.AreEqual(r.critical, criticalUnique, "errors.csv 的 Critical（每步每錯誤）數＝SessionResult.critical");
            StringAssert.Contains("鎖到規定扭力（靠關節）", File.ReadAllText(Path.Combine(run.folder, "errors.csv"), Encoding.UTF8));
            var hints = File.ReadAllText(Path.Combine(run.folder, "hints.csv"), Encoding.UTF8);
            StringAssert.Contains("說明,Help,1", hints);
            StringAssert.Contains("帶我到目標,FocusTarget,1", hints);
        }

        // ---------- 4. 重播：不發 isReplay=false 的錯誤；拖曳後的黑板＝連續播到同一時間 ----------
        [Test]
        public void Replay_NoLiveErrors_AndSeekEqualsContinuous()
        {
            var run = RunWithErrors();
            int recordedErrors = run.errors.Count;
            SimSession.EnterReplay(run.folder);

            (EventBus bus, SimState state, ReplayTimeline tl, List<ErrorDetected> errs) Env()
            {
                var b = new EventBus();
                var st = new SimState(b);
                new HintService(b, st); new ErrorFeedback(b, st);
                var sc = new Scoring(b, st); sc.Start(0);
                var list = new List<ErrorDetected>();
                b.Subscribe<ErrorDetected>(e => list.Add(e));
                var tl = ReplayTimeline.Load(run.folder);
                tl.Attach(b, st);
                return (b, st, tl, list);
            }

            var a = Env();
            Assert.Greater(a.tl.snapshots.Count, 3, "每 10 s 一筆快照");
            Assert.Greater(a.tl.Duration, 60);
            int sessionResults = 0;
            a.bus.Subscribe<SessionResult>(_ => sessionResults++);
            for (double t = 0; t <= a.tl.Duration + 0.5; t += 0.5) a.tl.AdvanceTo(t);
            Assert.AreEqual(recordedErrors, a.errs.Count, "重播要重發每一個錯誤（錯誤視窗）");
            Assert.IsTrue(a.errs.All(e => e.isReplay), "重播中不能發出 isReplay=false 的 ErrorDetected");
            Assert.AreEqual(0, sessionResults, "重播不產生分數");
            Assert.AreEqual(0f, a.state.GetFloat("score.total"));

            foreach (double frac in new[] { 0.13, 0.42, 0.77, 1.0 })
            {
                var cont = Env();
                var seek = Env();
                double T = Math.Round(cont.tl.Duration * frac, 2);
                for (double t = 0; t < T; t += 0.37) cont.tl.AdvanceTo(t);
                cont.tl.AdvanceTo(T);
                seek.tl.Seek(seek.tl.Duration);   // 先跳到最後再往回拖
                seek.tl.Seek(T);
                var x = Comparable(cont.state); var y = Comparable(seek.state);
                var diff = x.Keys.Union(y.Keys).Where(k => !(x.TryGetValue(k, out var v1) && y.TryGetValue(k, out var v2) && v1 == v2))
                    .Select(k => $"{k}: 連續={(x.TryGetValue(k, out var p) ? p : "∅")} 拖曳={(y.TryGetValue(k, out var q) ? q : "∅")}").ToList();
                Assert.IsEmpty(diff, $"t={T}：拖曳與連續播放的黑板不同\n" + string.Join("\n", diff.Take(20)));
                Assert.Greater(x.Count, 50);
            }

            // 位姿：頭與數值通道讀得到
            Assert.IsTrue(a.tl.TryGetPose(RecordFiles.Head, 10, out _));
            Assert.IsTrue(a.tl.TryGetValue(RecordFiles.LiftChannel, 5, out var lift) && lift < 1f);
        }

        static Dictionary<string, string> Comparable(ISimState s) =>
            s.Snapshot().Where(kv => !ReplayTimeline.ViewerOwnedKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        // ---------- 5. Recording 模組不引用任何功能層模組 ----------
        [Test]
        public void RecordingAsmdef_ReferencesOnlyCoreAndData()
        {
            string[] feature =
            {
                "PantographSim.Procedures", "PantographSim.Safety", "PantographSim.Parts", "PantographSim.Tools", "PantographSim.Worksite",
                "PantographSim.Interaction", "PantographSim.Training", "PantographSim.UI", "PantographSim.XR", "PantographSim.App"
            };
            var json = File.ReadAllText("Assets/_Project/Modules/Recording/PantographSim.Recording.asmdef");
            var refs = ((Dictionary<string, object>)Json.Parse(json)).StrArr("references");
            CollectionAssert.AreEquivalent(new[] { "PantographSim.Core", "PantographSim.Data" }, refs, "PantographSim.Recording 只能引用 Core 與 Data");
            string editorAsmdef = "Assets/_Project/Modules/Recording/Editor/PantographSim.Recording.Editor.asmdef";
            if (File.Exists(editorAsmdef))
            {
                var eRefs = ((Dictionary<string, object>)Json.Parse(File.ReadAllText(editorAsmdef))).StrArr("references");
                Assert.IsFalse(eRefs.Any(r => feature.Any(f => r.StartsWith(f))), "Recording.Editor 也不引用功能層：" + string.Join(",", eRefs));
            }
            // 程式碼也沒有 using 功能層命名空間
            foreach (var file in Directory.GetFiles("Assets/_Project/Modules/Recording", "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (var f in feature) Assert.IsFalse(text.Contains("using " + f), $"{Path.GetFileName(file)} 引用了 {f}");
            }
        }

        // ---------- 連續感測值節流：最多 15 Hz，最後一個值一定寫出 ----------
        [Test]
        public void ContinuousSensorKeys_Throttled_LastValueKept()
        {
            var bus = new EventBus();
            var state = new SimState(bus);
            var writer = new RecordingWriter(Path.Combine(root, "20261019_130000_節流"));
            var rec = new SessionRecorder(bus, state, writer, new StepCatalog());
            rec.Begin(new Dictionary<string, object> { { "trainee", "節流" } });
            // 1 秒內 500 幀，每幀距離都變
            for (int i = 0; i < 500; i++)
            {
                clock += 0.002f;
                state.SetFloat(SimIds.PanHeadLowerDistance, 100f - i * 0.1f);
                rec.Tick(clock - rec.T0);
            }
            state.SetFloat("part.test.value", 1);   // 一般鍵照常每次都記
            rec.Close("test");
            var dist = rec.Lines.Where(l => l.type == "StateChanged" && l.S("key") == SimIds.PanHeadLowerDistance).ToList();
            Assert.LessOrEqual(dist.Count, 18, "1 秒最多約 15 筆");
            Assert.GreaterOrEqual(dist.Count, 10);
            Assert.AreEqual(state.Get(SimIds.PanHeadLowerDistance), dist.Last().S("value"), "最後一個值一定要寫出");
            Assert.IsTrue(rec.Lines.Any(l => l.type == "StateChanged" && l.S("key") == "part.test.value"));
            rec.Dispose();
            writer.Dispose();
        }

        // ---------- 其他：資料夾名稱、ValueTag、考核模式隱藏 ----------
        [Test]
        public void FolderName_TraineeDefault_ValueTags_ExamHiddenUntilFinished()
        {
            Assert.AreEqual("20261019_153000_王小明", RecordingPaths.FolderName(new DateTime(2026, 10, 19, 15, 30, 0), "王小明"));
            Assert.AreEqual("20261019_153000_a_b", RecordingPaths.FolderName(new DateTime(2026, 10, 19, 15, 30, 0), "a/b"));
            StringAssert.StartsWith("學員", RecordingPaths.DefaultTrainee(DateTime.Now));
            Assert.AreEqual("示例值", ValueTagRules.Find("ip.carbon_strip_L.bolt_3.torque_Nm").tag);
            Assert.AreEqual("保養表值", ValueTagRules.Find("ip.carbon_strip_R.measure_mid.reading").tag);
            Assert.IsNull(ValueTagRules.Find("part.carbon_strip_L.state"));
            Assert.IsFalse(RecordingPaths.Resolve("Recordings").Replace('\\', '/').Contains("/Assets/"), "紀錄根目錄不能在 Assets 底下");

            // 參考時間：教學用教學秒數；練習（沒有小動作提示）與考核（預留）用考核秒數
            var info = new StepInfo { refTutorial = 45, refExam = 30 };
            Assert.AreEqual(45f, Labels.RefSeconds(info, "教學"));
            Assert.AreEqual(30f, Labels.RefSeconds(info, "練習"));
            Assert.AreEqual(30f, Labels.RefSeconds(info, "考核"));

            // 考核模式：沒結束不列出
            var exam = Path.Combine(root, "20261019_100000_考核學員");
            Directory.CreateDirectory(exam);
            File.WriteAllText(Path.Combine(exam, RecordFiles.Meta), Json.Write(new Dictionary<string, object> { { "trainee", "考核學員" }, { "mode", "考核" }, { "startedAt", "2026-10-19T10:00:00" } }));
            File.WriteAllText(Path.Combine(exam, RecordFiles.Session), "");
            Assert.IsEmpty(RecordingIndex.List(root));
            RecordingIndex.RememberName(root, "甲"); RecordingIndex.RememberName(root, "乙"); RecordingIndex.RememberName(root, "甲");
            CollectionAssert.AreEqual(new[] { "甲", "乙" }, RecordingIndex.LoadNames(root), "上次用的名稱排第一");
            string driver = PantographSim.Data.ToolRulesDef.Load().testDriverTag;
            RecordingIndex.RememberName(root, driver);
            CollectionAssert.AreEqual(new[] { "甲", "乙" }, RecordingIndex.LoadNames(root), "跳步工具的驗收名不記");
            File.WriteAllText(Path.Combine(root, RecordingIndex.NamesFile), "{\"names\":[\"" + driver + "\",\"甲\"]}", System.Text.Encoding.UTF8);
            CollectionAssert.AreEqual(new[] { "甲" }, RecordingIndex.LoadNames(root), "舊檔裡的驗收名不沿用");
        }
    }
}
