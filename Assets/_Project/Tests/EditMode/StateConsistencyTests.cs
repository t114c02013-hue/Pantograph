using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Procedures;
using PantographSim.Recording;

namespace PantographSim.Tests
{
    /// <summary>
    /// 黑板一致性：訂閱 StateChanged，每收到一次就檢查不變條件；用 SliceDryRun 跑完整個正常流程（兩側）。
    /// 另含升降放開的最新距離、每一步開始快照的重做／↶ 上一步、WC_01 卡關重現、錄製同一批同一時間點。
    /// </summary>
    public class StateConsistencyTests
    {
        static readonly string[] Sides = { "L", "R" };

        /// <summary>每次 StateChanged 都檢查的不變條件；違反時記下 key 與當時的值。</summary>
        class InvariantChecker
        {
            readonly SimState s;
            readonly Dictionary<int, string> slotTool;
            public readonly List<string> violations = new List<string>();
            public int checkedEvents;

            public InvariantChecker(SliceDryRunTests.SliceSim sim)
            {
                s = sim.s;
                slotTool = sim.bundle.tools.ToDictionary(t => t.hotkey, t => t.toolId);
                sim.bus.Subscribe<StateChanged>(OnState);
            }

            void OnState(StateChanged e)
            {
                checkedEvents++;
                string changed = string.Join(", ", e.Changes.Select(c => c.ToString()));
                foreach (var v in Check(s, slotTool)) violations.Add($"[{s.Get("procedure.current")}] {v}\n    本次變更：{changed}");
                // 放開升降：Stop 與最新距離同一批（快照還原逐鍵通知的不算放開）
                if (!e.restored && e.TryGet(SimIds.LiftCmd, out var cmd) && cmd.newValue == "Stop" && cmd.oldValue != "Stop" && !e.Has(SimIds.PanHeadLowerDistance))
                    violations.Add($"[{s.Get("procedure.current")}] ctrl.lift.cmd 變成 Stop，同一批沒有 {SimIds.PanHeadLowerDistance}（本次變更：{changed}）");
            }

            public static IEnumerable<string> Check(ISimState s, Dictionary<int, string> slotTool)
            {
                // 1. 手上的工具與掛位
                string held = s.Get(SimIds.ActiveTool);
                foreach (var kv in slotTool)
                {
                    string st = s.Get(RackLogic.SlotKey(kv.Key), RackLogic.OnRack);
                    if (kv.Value == held && st != RackLogic.InHand)
                        yield return $"tool.active={held}，但 rack.slot_{kv.Key}={st}（應為 {RackLogic.InHand}）";
                    if (st == RackLogic.InHand && kv.Value != held)
                        yield return $"rack.slot_{kv.Key}={st}（{kv.Value}），但 tool.active='{held}'";
                    if (st == RackLogic.InUse && kv.Value != SimIds.ToolGroundingRod && kv.Value != SimIds.ToolHarness)
                        yield return $"rack.slot_{kv.Key}={st}，只有留在場上的接地棒與安全帶可以是使用中（{kv.Value}）";
                }
                if (s.Has("tools.all_returned") && s.GetBool("tools.all_returned") != string.IsNullOrEmpty(held))
                    yield return $"tools.all_returned={s.Get("tools.all_returned")}，但 tool.active='{held}'";

                foreach (var side in Sides)
                {
                    // 2. 滑板裝好（NewMounted）：方向正確、安裝距離 ≤ 容許值
                    string strip = SimIds.Strip(side);
                    if (s.Get(strip + ".state") == "NewMounted")
                    {
                        if (s.Get(strip + ".new_orientation") != "out") yield return $"{strip}.state=NewMounted，但 new_orientation={s.Get(strip + ".new_orientation")}";
                        string d = strip + ".install_distance_cm";
                        if (!s.Has(d) || s.GetFloat(d) > 0.5f + 1e-4f) yield return $"{strip}.state=NewMounted，但 {d}={s.Get(d, "∅")}（容許 0.5）";
                    }
                    // 3. 螺栓 Torqued：扭力 > 0
                    for (int n = 1; n <= 4; n++)
                    {
                        string b = $"ip.carbon_strip_{side}.bolt_{n}";
                        if (s.Get(b + ".state") == "Torqued" && !(s.GetFloat(b + ".torque_Nm") > 0f))
                            yield return $"{b}.state=Torqued，但 torque_Nm={s.Get(b + ".torque_Nm", "∅")}";
                    }
                }
                // 4. 已接地：接地棒兩端都連接
                if (s.GetBool(SimIds.Grounded) && !(s.GetBool(SimIds.TerminalClamped) && s.GetBool(SimIds.HeadClamped)))
                    yield return $"isolation.grounded=true，但 terminal_clamped={s.Get(SimIds.TerminalClamped, "∅")} head_clamped={s.Get(SimIds.HeadClamped, "∅")}";
                // 5. 車頂傳送點已解鎖（lockKey＝harness.roof）：車頂扣環扣著、扣著的數量 ≥ 1
                if (s.GetBool("harness.roof") && s.GetFloat("harness.hooks_count") < 1f)
                    yield return $"harness.roof=true（車頂傳送點解鎖），但 harness.hooks_count={s.Get("harness.hooks_count", "∅")}";
            }
        }

        static void AssertNoViolations(InvariantChecker c)
        {
            Assert.Greater(c.checkedEvents, 100, "應該檢查了很多次 StateChanged");
            Assert.IsEmpty(c.violations, $"不變條件違反 {c.violations.Count} 次：\n" + string.Join("\n", c.violations.Take(15)));
        }

        static Dictionary<int, string> SlotTools(SliceDryRunTests.SliceSim sim) => sim.bundle.tools.ToDictionary(t => t.hotkey, t => t.toolId);

        /// <summary>用升降控制器降到綠框並放開（取代直接寫距離）：每幀距離變化、放開時 Stop 與最新距離同一批。</summary>
        static void LowerWithLift(SliceDryRunTests.SliceSim sim)
        {
            LiftControl.Send(sim.s, "Down");
            for (float d = 30f; d > 0.4f; d -= 2.5f) sim.s.SetFloat(SimIds.PanHeadLowerDistance, d);
            sim.s.SetFloat(SimIds.PanHeadLowerDistance, 0.4f);
            LiftControl.Release(sim.s);
            sim.s.Set(SimIds.PanHeadPose, SimIds.PoseLowered);
        }

        // ---------- 1. 正常流程（兩側）全程不變條件 ----------
        [Test]
        public void NormalPath_BothSides_InvariantsHoldOnEveryStateChanged()
        {
            foreach (var mode in new[] { false, true })
            {
                InvariantChecker checker = null;
                var sim = new SliceDryRunTests.SliceSim("SCN_Default", mode, x => checker = new InvariantChecker(x));
                LowerWithLift(sim);
                sim.RunUntil(null);
                Assert.IsTrue(sim.completed, sim.Describe());
                Assert.AreEqual(0, sim.errors.Count, "正常路徑不應有錯誤：" + string.Join(", ", sim.errors.Select(e => e.errorId)));
                AssertNoViolations(checker);
            }
        }

        // ---------- 2. 升降放開：SI_01 的 a 用放開那一刻的最新距離 ----------
        class FakeLowerSensor : IStateSensor
        {
            public float liveCm;
            public string KeyPrefix => LiftControl.SensorPrefix;
            public void WriteNow(ISimState s, bool force)   // 與 PositionCheck 相同：寫原始距離（判定用原始值，顯示才四捨五入）
            {
                s.SetFloat(SimIds.PanHeadLowerDistance, liveCm, force);
                s.SetFloat(SimIds.PanHeadLowerDelta, liveCm, force);
            }
        }

        /// <param name="lastWrittenCm">弓頭緩慢下降時位置檢測最後一次寫進黑板的值（之後還沒到下一次寫入，黑板是舊值）</param>
        /// <param name="releaseCm">放開那一刻檢測點的實際距離</param>
        static (bool aDone, float used, List<StateChanged> releaseEvents) ReleaseAt(float lastWrittenCm, float releaseCm)
        {
            var sim = new SliceDryRunTests.SliceSim();
            var sensor = new FakeLowerSensor();
            Sensors.Register(sensor);
            try
            {
                LiftControl.Send(sim.s, "Down");
                for (float d = 3f; d > lastWrittenCm + 0.05f; d -= 0.05f) { sensor.liveCm = d; sim.s.SetFloat(SimIds.PanHeadLowerDistance, d); }   // 每變化 0.05 cm 寫一次（PositionCheck 的門檻）
                sim.s.SetFloat(SimIds.PanHeadLowerDistance, lastWrittenCm);   // 每幀寫入的最後一筆
                if (lastWrittenCm > 1f) Assert.AreEqual(0f, sim.s.GetFloat("procedure.sub_index"), "黑板上的舊值還沒到位");
                sensor.liveCm = releaseCm;   // 這一幀移動了，但放開發生在位置檢測寫入之前
                var events = new List<StateChanged>();
                sim.bus.Subscribe<StateChanged>(e => events.Add(e));
                LiftControl.Release(sim.s);
                Assert.AreEqual("STP_SI_01_ConfirmLowered", sim.Current);
                return (sim.s.GetFloat("procedure.sub_index") >= 1f, sim.s.GetFloat(SimIds.PanHeadLowerDistance), events);
            }
            finally { Sensors.Unregister(sensor); }
        }

        [Test]
        public void LiftRelease_SI01a_UsesLatestDistance_PassAt09_FailAt11()
        {
            // 0.9 cm：黑板上還是上一筆 1.1（未到位），放開時強制寫入最新 0.9 → a 通過
            var pass = ReleaseAt(1.1f, 0.9f);
            Assert.AreEqual(0.9f, pass.used, 1e-4f, "放開時用最新距離");
            Assert.IsTrue(pass.aDone, "0.9 cm ≤ 1 cm：SI_01 的 a 通過");
            Assert.AreEqual(1, pass.releaseEvents.Count(e => e.Has(SimIds.LiftCmd)), "放開＝一批");
            var batch = pass.releaseEvents.First(e => e.Has(SimIds.LiftCmd));
            Assert.IsTrue(batch.Has(SimIds.PanHeadLowerDistance) && batch.Has(SimIds.PanHeadLowerDelta), "距離、垂直差與 Stop 同一批");
            Assert.AreEqual(SimIds.PanHeadLowerDistance, batch.Changes[0].key, "先寫距離再寫 Stop");

            // 1.1 cm：黑板上一筆 1.14，放開時實際 1.1（變化不到 0.05 cm 的寫入門檻、還沒寫下一筆）→ a 不通過
            var fail = ReleaseAt(1.14f, 1.1f);
            Assert.AreEqual(1.1f, fail.used, 1e-4f);
            Assert.IsFalse(fail.aDone, "1.1 cm > 1 cm：SI_01 的 a 不通過");

            // 只差一點、正好跨過容許值：黑板舊值 1.03（未到位），放開時實際 0.99 → 用最新值才會通過；判定用原始值，不是四捨五入後的 1.0
            var edge = ReleaseAt(1.03f, 0.99f);
            Assert.IsTrue(edge.aDone, "0.99 cm：用放開那一刻的距離判定");
            Assert.AreEqual(0.99f, edge.used, 1e-4f, "黑板存原始距離");
            var over = ReleaseAt(1.08f, 1.04f);
            Assert.IsFalse(over.aDone, "1.04 cm 顯示成 1.0，但判定用原始值 1.04 > 1 cm，不通過");
            // 距離沒變也一定在同一批（強制寫入）
            var same = ReleaseAt(0.9f, 0.9f);
            Assert.IsTrue(same.aDone);
            var b2 = same.releaseEvents.First(e => e.Has(SimIds.LiftCmd));
            Assert.IsTrue(b2.TryGet(SimIds.PanHeadLowerDistance, out var c) && c.forced, "值沒變也強制寫入");
        }

        // ---------- 3. 每一步開始快照：重做（RestartCurrentStep）與 ↶ 上一步後都一致 ----------
        [Test]
        public void EveryStepStart_RestartAndStepBack_SnapshotsConsistent()
        {
            InvariantChecker checker = null;
            var sim = new SliceDryRunTests.SliceSim("SCN_Default", true, x => checker = new InvariantChecker(x));
            var slots = SlotTools(sim);
            LowerWithLift(sim);
            var restarted = new HashSet<string>();
            for (int guard = 0; guard < 80 && !sim.completed; guard++)
            {
                string runId = sim.Current;
                if (restarted.Add(runId))
                {
                    // 上一步的動作（8 處之一）完成後立刻重做這一步：還原的開始快照必須是一致的狀態
                    sim.runner.RestartCurrentStep();
                    var bad = InvariantChecker.Check(sim.s, slots).ToList();
                    Assert.IsEmpty(bad, $"{runId} 重做後的快照不一致：\n" + string.Join("\n", bad));
                    Assert.AreEqual(runId, sim.Current);
                }
                sim.DoStep();
                Assert.AreNotEqual(runId, sim.Current, $"{runId} 重做後照正確方式操作要能完成：" + sim.Describe() + "\n" + string.Join("\n", sim.s.Keys.Where(k => k.Contains("mid") || k.Contains("height") || k.Contains("true")).OrderBy(k => k).Select(k => k + "=" + sim.s.Get(k))));
            }
            Assert.IsTrue(sim.completed, sim.Describe());
            Assert.AreEqual(sim.runner.Steps.Count(st => !st.def.presetDone), restarted.Count, "每個要操作的步驟（兩側展開）都重做過一次");
            AssertNoViolations(checker);
        }

        [Test]
        public void ToolPickAndReturn_StepBack_RestoresConsistentPair()
        {
            InvariantChecker checker = null;
            var sim = new SliceDryRunTests.SliceSim("SCN_Default", true, x => checker = new InvariantChecker(x));
            sim.RunUntil("STP_CSR_06_Loosen_L");   // 第一個小動作：拿套筒扳手
            Assert.IsNull(sim.PickTool(SimIds.ToolSocket));
            Assert.AreEqual(1f, sim.s.GetFloat("procedure.sub_index"));
            Assert.AreEqual("已退回上一個小動作", sim.runner.StepBackSubAction());
            Assert.AreEqual("", sim.s.Get(SimIds.ActiveTool));
            Assert.AreEqual(RackLogic.OnRack, sim.s.Get("rack.slot_6"));
            Assert.IsTrue(sim.s.GetBool("tools.all_returned"), "退回後手上空、掛位在架、已收回三者一致");
            sim.RunUntil(null);
            Assert.IsTrue(sim.completed, sim.Describe());
            AssertNoViolations(checker);
        }

        // ---------- 4. WC_01 卡關重現：上一步最後一個動作是把工具放回工具架 ----------
        [Test]
        public void WC01_Repro_RestartAfterToolReturn_StillCompletes()
        {
            var sim = new SliceDryRunTests.SliceSim();
            sim.RunUntil("STP_WC_01_ToolCount");   // CSR_15 的最後一個小動作（放回卡尺）讓步驟切換到 WC_01 並拍下開始快照
            sim.runner.RestartCurrentStep();
            Assert.AreEqual("", sim.s.Get(SimIds.ActiveTool));
            Assert.IsTrue(sim.s.GetBool("tools.all_returned"), "快照不能是「手上空了、清點還是沒收回」的中間狀態");
            sim.DoStep();   // 照正確方式重做清點
            Assert.AreNotEqual("STP_WC_01_ToolCount", sim.Current, "重做後清點要能通過：" + sim.Describe());

            Assert.IsTrue(BadOrderInsideBatch_StepStartSeesFinalState(), "包在一批裡，下一步的開始快照一定是完整狀態");
        }

        /// <summary>上一輪出問題的寫入順序（先手上變空、再寫已收回），包在一批裡：下一步開始時與重做後都看到「已收回」。</summary>
        static bool BadOrderInsideBatch_StepStartSeesFinalState()
        {
            const string json = @"{ ""procedureId"": ""PRC_B"", ""steps"": [
                { ""stepId"": ""S1"", ""completion"": { ""type"": ""StateEquals"", ""key"": ""tool.active"", ""value"": """" } },
                { ""stepId"": ""S2"", ""completion"": { ""type"": ""StateEquals"", ""key"": ""x"", ""value"": ""y"" } } ] }";
            var bus = new EventBus();
            var s = new SimState(bus);
            s.Set(SimIds.ActiveTool, SimIds.ToolCaliper); s.Set("rack.slot_5", RackLogic.InHand); s.SetBool("tools.all_returned", false);
            var runner = new ProcedureRunner(ProcedureJsonConverter.Build(json).procedure, bus, s);
            runner.Start();
            string atStart = null;
            bus.Subscribe<StepStarted>(e => { if (e.stepId == "S2") atStart = s.Get("tools.all_returned"); });
            s.Batch(() => { s.Set(SimIds.ActiveTool, ""); s.Set("rack.slot_5", RackLogic.OnRack); s.SetBool("tools.all_returned", true); });
            if (runner.Current?.RunId != "S2" || atStart != "true") return false;
            runner.RestartCurrentStep();
            return s.GetBool("tools.all_returned") && s.Get(SimIds.ActiveTool) == "";
        }
        // ---------- 5. 錄製：同一批＝同一時間點、同一個 seq；重播結果與原本一致 ----------
        [Test]
        public void Recording_SameBatchSameSeq_AndReplayMatchesLive()
        {
            string root = Path.Combine(Path.GetTempPath(), "PantoBatchRec_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var oldClock = SimClock.Source;
            float clock = 100f;
            SimClock.Source = () => clock;
            SimSession.EnterTraining();
            try
            {
                string folder = Path.Combine(root, "20261020_120000_批次");
                RecordingWriter writer = null; SessionRecorder rec = null;
                var sim = new SliceDryRunTests.SliceSim("SCN_Default", false, x =>
                {
                    writer = new RecordingWriter(folder);
                    rec = new SessionRecorder(x.bus, x.s, writer, StepCatalog.From(x.bundle.procedure));
                    rec.Begin(new Dictionary<string, object> { { "trainee", "批次" }, { "startedAt", DateTime.Now.ToString("o") } });
                });
                sim.bus.Subscribe<StateChanged>(_ => { clock += 0.01f; rec.Tick(clock - rec.T0); });   // 每次通知時間往前（同一批內不前進）
                LowerWithLift(sim);
                sim.RunUntil(null);
                Assert.IsTrue(sim.completed);
                var live = sim.s.Snapshot();
                rec.Close("test");
                var lines = rec.Lines.ToList();
                rec.Dispose(); writer.Dispose();

                // 工具拿起：掛位與 tool.active 同一個 seq、同一個時間點
                var pick = lines.First(l => l.type == "ToolRackState" && l.S("state") == RackLogic.InHand);
                var same = lines.Where(l => l.seq == pick.seq).ToList();
                Assert.IsTrue(same.Any(l => l.type == "StateChanged" && l.S("key") == SimIds.ActiveTool), "拿起工具：tool.active 與掛位同一批");
                Assert.IsTrue(same.All(l => Math.Abs(l.t - pick.t) < 1e-6), "同一批同一時間點");
                // 螺栓鎖到扭力：state 與 torque_Nm 同一批
                var torqued = lines.First(l => l.type == "StateChanged" && l.S("key").EndsWith(".bolt_1.state") && l.S("value") == "Torqued");
                Assert.IsTrue(lines.Any(l => l.seq == torqued.seq && l.S("key") == torqued.S("key").Replace(".state", ".torque_Nm")));
                // 放開升降：Stop、距離、LiftReleased 同一批
                var stop = lines.First(l => l.type == "StateChanged" && l.S("key") == SimIds.LiftCmd && l.S("value") == "Stop");
                Assert.IsTrue(lines.Any(l => l.seq == stop.seq && l.S("key") == SimIds.PanHeadLowerDistance), "距離與 Stop 同一個 seq");
                var released = lines.First(l => l.type == "LiftReleased");
                Assert.AreEqual(stop.seq, released.seq);
                Assert.AreEqual(0.4, released.N("distanceCm"), 1e-4);
                // 不同批不共用 seq：seq 遞增
                for (int i = 1; i < lines.Count; i++) Assert.GreaterOrEqual(lines[i].seq, lines[i - 1].seq);

                // 讀回檔案（穩定排序保留同批的先後）並只用紀錄行（不用快照）重播：結果與原本一致
                var read = RecordLine.ReadAll(Path.Combine(folder, RecordFiles.Session), out int bad);
                Assert.AreEqual(0, bad);
                CollectionAssert.AreEqual(lines.Select(l => l.ToJson()).ToList(), read.Select(l => l.ToJson()).ToList());
                var bus = new EventBus();
                var st = new SimState(bus);
                var replayEvents = new List<StateChanged>();
                bus.Subscribe<StateChanged>(e => replayEvents.Add(e));
                var tl = new ReplayTimeline(new Dictionary<string, object>(), read, new List<Snapshot>(), null);
                tl.Attach(bus, st);
                tl.AdvanceTo(tl.Duration);
                Assert.IsTrue(replayEvents.Any(e => e.Changes.Any(c => c.key.StartsWith("rack.slot_")) && e.Has(SimIds.ActiveTool)), "重播時同一批也一次套用");
                var recordedKeys = new HashSet<string>(read.Where(l => l.type == "StateChanged" || l.type == "Judgement").Select(l => l.S("key")));
                var diff = recordedKeys.Where(k => st.Get(k, "∅") != (live.TryGetValue(k, out var v) ? v : "∅"))
                    .Select(k => $"{k}: 重播={st.Get(k, "∅")} 原本={(live.TryGetValue(k, out var v2) ? v2 : "∅")}").ToList();
                Assert.Greater(recordedKeys.Count, 50);
                Assert.IsEmpty(diff, "重播結果與原本不同：\n" + string.Join("\n", diff.Take(20)));
            }
            finally
            {
                SimClock.Source = oldClock;
                SimSession.EnterTraining();
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }
    }
}
