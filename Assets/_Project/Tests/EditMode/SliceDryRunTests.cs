using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Parts.CarbonStrip;
using PantographSim.Parts.Common;
using PantographSim.Procedures;
using PantographSim.Safety;
using PantographSim.Tools;
using PantographSim.Training;
using PantographSim.Worksite;

namespace PantographSim.Tests
{
    /// <summary>
    /// W14：用真正的步驟資料（carbon_strip_slice.json）、ProcedureRunner、ErrorDetector、SafetyGuard，
    /// 以及工具／零件共用的狀態邏輯，模擬玩家操作跑完整個流程與各錯誤情境（不需要場景與 Play）。
    /// </summary>
    public class SliceDryRunTests
    {
        // ---------- 模擬器 ----------
        public class SliceSim
        {
            public readonly EventBus bus = new EventBus();
            public readonly SimState s;
            public readonly ProcedureRunner runner;
            readonly CompositeActionGuard guard = new CompositeActionGuard();
            public readonly List<ErrorDetected> errors = new List<ErrorDetected>();
            public readonly List<ActionBlocked> blocked = new List<ActionBlocked>();
            public bool completed;
            readonly System.Random rng = new System.Random(1);
            readonly Dictionary<string, int> toolSlots = new Dictionary<string, int>();
            readonly Dictionary<string, (string model, bool cracked)> spares = new Dictionary<string, (string, bool)>
            {
                { "env.spare_shelf.slot_1", ("CS-1100-64B", false) }, { "env.spare_shelf.slot_2", (SpareShelf.CorrectModel, false) }, { "env.spare_shelf.slot_3", (SpareShelf.CorrectModel, true) }
            };

            public readonly ProcedureBundle bundle;

            /// <summary>beforeStart：流程開始（第一個 StepStarted）之前呼叫，例如讓紀錄器先訂閱事件。</summary>
            public SliceSim(string scenarioId = "SCN_Default", bool tutorial = false, System.Action<SliceSim> beforeStart = null)
            {
                s = new SimState(bus);
                bundle = ProcedureJsonConverter.Build(File.ReadAllText("Assets/_Project/Modules/Procedures/Data/carbon_strip_slice.json"));
                var safety = new SafetyGuard(bus, s, bundle.safetyRules);
                var gates = new CompositeStepGate(); gates.Add(safety);
                runner = new ProcedureRunner(bundle.procedure, bus, s, gates);
                var detector = new ErrorDetector(runner, bus, s);
                guard.Add(safety); guard.Add(detector);

                bus.Subscribe<ErrorDetected>(e => errors.Add(e));
                bus.Subscribe<ActionBlocked>(e => blocked.Add(e));
                bus.Subscribe<ProcedureCompleted>(e => completed = true);
                bus.Subscribe<ActionPerformed>(e =>
                {
                    if (e.targetId == SimIds.PartsTray) PartsTray.Apply(s, e.text, e.pointId);
                    else if (e.targetId == SimIds.BoltTray("L") || e.targetId == SimIds.BoltTray("R")) BoltTray.Apply(s, SimIds.SideOf(e.pointId), e.text, e.pointId);   // 螺栓盤（R1）
                });
                bus.Subscribe<StateChanged>(e => s.Batch(() =>   // CarbonStripBehaviour：三個量測點一批鏡射
                {
                    foreach (var side in new[] { "L", "R" })
                        foreach (var p in StripMeasureLogic.Points)
                            if (e.Has($"part.carbon_strip_{side}.true.{p}")) StripMeasureLogic.Mirror(s, "part.carbon_strip_" + side, p);   // CarbonStripBehaviour 會做的（一批可能有多個鍵）
                }));

                // 元件 OnBound 會寫的初始狀態
                foreach (var side in new[] { "L", "R" })
                {
                    StripLogic.Initialize(s, "part.carbon_strip_" + side);
                    for (int n = 1; n <= 4; n++) FastenerLogic.Initialize(s, $"ip.carbon_strip_{side}.bolt_{n}");
                    s.Set($"ip.carbon_strip_{side}.bracket.dirt", new string('1', 64));
                    s.SetFloat($"ip.carbon_strip_{side}.bracket.clean_ratio", 0);
                }
                s.SetFloat("tray.bolts", 0); s.SetFloat("tray.old_strips", 0); s.SetBool("tray.no_loose_bolts", true);
                foreach (var side in new[] { "L", "R" }) s.SetFloat(BoltTray.CountKey(side), 0);   // BoltTray.OnBound 會寫的
                foreach (var kv in spares) SpareShelf.Stock(s, kv.Key, kv.Value.model, SpareShelf.Nominal);
                s.SetBool("tools.all_returned", true);

                s.Set("training.mode", tutorial ? "tutorial" : "practice");   // 小動作所有模式都要求，教學模式才顯示
                for (int k = 1; k <= 9; k++) RackLogic.Initialize(s, k);
                foreach (var t in bundle.tools) toolSlots[t.toolId] = t.hotkey;
                s.SetFloat(SimIds.PanHeadLowerTolerance, bundle.safetyRules.First(r => r.kind == SafetyRuleKind.NotLowered).toleranceCm);
                // 情境（ModeController 會做的事）
                var scn = bundle.scenarios.First(x => x.scenarioId == scenarioId);
                KeyValueList.Apply(scn.initialStates, s);
                s.Set(SimIds.PanHeadPose, scn.startRaised ? SimIds.PoseRaised : SimIds.PoseLowered);
                
                s.SetFloat(SimIds.PanHeadLowerDistance, scn.startRaised ? 183f : 0.2f);
                beforeStart?.Invoke(this);
                runner.Start();
            }

            public string Current => runner.Current?.RunId;
            string Side => runner.Current?.side ?? "L";
            bool Ask(string tool, string target, string verb) => guard.Check(new ActionRequest(tool, target, verb)).allowed;
            static string Bolt(string side, int n) => $"ip.carbon_strip_{side}.bolt_{n}";

            // ---- 玩家動作（與工具／零件相同的狀態邏輯，動作前都問守門者） ----
            /// <summary>用升降控制器把檢測點降到綠框（PositionCheck 會寫的結果）。</summary>
            public void LowerToTarget() { s.SetFloat(SimIds.PanHeadLowerDistance, 0.4f); s.Set(SimIds.PanHeadPose, SimIds.PoseLowered); }

            public bool InspectPanHead() { if (!Ask("", SimIds.PanHead, "inspect")) return false; s.SetBool("part.pan_head.inspected", true); return true; }
            public void CheckIndicator() => s.SetBool("ip.cab.pan_indicator.checked", true);
            public bool SelfTest() { if (!Ask(SimIds.ToolVoltageDetector, SimIds.VoltageTester, "selftest")) return false; IsolationLogic.SelfTest(s, 0); return true; }
            public bool MeasureVoltage() { if (!Ask(SimIds.ToolVoltageDetector, SimIds.GroundClamp, "test")) return false; IsolationLogic.MeasureHead(s); return true; }
            public bool Terminal() { if (!Ask(SimIds.ToolGroundingRod, SimIds.GroundTerminal, "ground")) return false; IsolationLogic.ToggleTerminal(s); return true; }
            public bool PullTest() { if (!Ask(SimIds.ToolGroundingRod, SimIds.GroundTerminal, "pulltest")) return false; return IsolationLogic.PullTest(s); }
            public bool Head()
            {
                if (!Ask(SimIds.ToolGroundingRod, SimIds.GroundClamp, "ground")) return false;
                IsolationLogic.ToggleHead(s);
                // GroundingRodTool：掛上弓頭後接地棒留在場上（使用中），手空出來
                if (s.GetBool(SimIds.HeadClamped) && s.Get(SimIds.ActiveTool) == SimIds.ToolGroundingRod) RackLogic.Deploy(s, toolSlots[SimIds.ToolGroundingRod]);
                return true;
            }
            public void Write(string field, string value) => WorkOrder.Write(s, bus, field, value);
            public void ZeroCaliper() { s.SetFloat("tool.caliper.jaw", 0); s.SetBool("tool.caliper.zeroed", true); }

            public float? Measure(string pointId, string tool = SimIds.ToolCaliper)
            {
                if (!Ask(tool, pointId, "measure")) return null;
                float reading = CaliperLogic.Reading(s.GetFloat(pointId + ".true"), s.GetBool("tool.caliper.zeroed"), rng);
                s.SetBool("tool.caliper.measured_any", true);
                bus.Publish(new MeasurementTaken { targetId = pointId, toolId = tool, value = reading });
                if (pointId.StartsWith("env.spare_shelf.")) SpareShelf.RecordMeasurement(s, pointId, reading);
                else
                {
                    string strip = "part.carbon_strip_" + SimIds.SideOf(pointId);
                    StripMeasureLogic.Record(s, strip, pointId, reading);
                    s.SetBool(pointId + ".measured", true);   // CarbonStripBehaviour 會寫的
                    if (s.Get(strip + ".state") == "NewMounted") s.SetBool(pointId + ".new_measured", true);
                }
                return reading;
            }

            public void MeasureAndRecord(string side, params string[] points)
            {
                if (points.Length == 0) points = StripMeasureLogic.Points;
                foreach (var p in points)
                {
                    var r = Measure($"ip.carbon_strip_{side}.measure_{p}");
                    if (r.HasValue) Write($"reading.{side}.{p}", r.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                }
                Write("submit." + side, "true");
            }

            public bool Rotate(string bolt, float deg, string dir, string size = "13", string tool = SimIds.ToolSocket)
            {
                if (!Ask(tool, bolt, "rotate")) return false;
                return FastenerLogic.Apply(s, bus, bolt, $"rotate:{size}:{dir}", deg);
            }
            public bool Take(string bolt) => Ask(SimIds.ToolSocket, bolt, "take") && FastenerLogic.Apply(s, bus, bolt, "take", 0);
            public bool Place(string bolt) => Ask(SimIds.ToolSocket, bolt, "place") && FastenerLogic.Apply(s, bus, bolt, "place", 0);
            public bool Torque(string bolt, float nm, float setting = 25f) =>
                Ask(SimIds.ToolTorque, bolt, "torque") && FastenerLogic.Apply(s, bus, bolt, "torque:" + setting.ToString(System.Globalization.CultureInfo.InvariantCulture), nm);
            public bool Mark(string bolt) => Ask(SimIds.ToolMarker, bolt, "mark") && FastenerLogic.Apply(s, bus, bolt, "mark", 0);
            public bool Remove(string side, string method) => Ask("", "part.carbon_strip_" + side, "remove") && StripLogic.Remove(s, bus, "part.carbon_strip_" + side, method);
            public bool ToTray(string side) => StripLogic.ToTray(s, bus, "part.carbon_strip_" + side);

            public bool Clean(string side)
            {
                string id = $"ip.carbon_strip_{side}.bracket";
                if (!Ask(SimIds.ToolCloth, id, "clean")) return false;
                for (float u = 0; u <= 1.001f; u += 0.05f) { DirtMask.Clean(s, id, u, 0.2f); DirtMask.Clean(s, id, u, 0.8f); }
                return true;
            }

            public bool Pick(string slot)
            {
                var spec = spares[slot];
                // 與 SpareShelf＋RemovableBehaviour 相同：選用成功與拿到手上同一批
                return s.BatchResult(() =>
                {
                    bool ok = SpareShelf.Pick(s, slot, Side, spec.model, spec.cracked);
                    if (ok) StripLogic.Carry(s, "part.carbon_strip_" + Side);
                    return ok;
                });
            }

            public void RotateCarried(string side) => StripLogic.Rotate(s, "part.carbon_strip_" + side);
            public bool Install(string side)
            {
                // PositionCheck 在同一批寫入吸附後的距離（RemovableBehaviour 經 Sensors.Flush 做的事）
                return Ask("", $"ip.carbon_strip_{side}.bracket", "install")
                       && StripLogic.Install(s, "part.carbon_strip_" + side, 0.002f, () => s.SetFloat($"part.carbon_strip_{side}.install_distance_cm", 0.2f, true));
            }

            // ---- 小動作：工具架拿／還、工具設定、查看類動作（場景元件或介面會寫的狀態） ----
            public string PickTool(string toolId) => RackLogic.TryPick(s, toolSlots[toolId], toolId, toolId, s.Get(SimIds.ActiveTool));
            public string ReturnTool()
            {
                string active = s.Get(SimIds.ActiveTool);
                return string.IsNullOrEmpty(active) ? null : RackLogic.TryReturn(s, toolSlots[active], active, true);
            }

            static string KeyOf(string condition) => condition.Split('=', '>', '<')[0];

            /// <summary>不是「零件操作」本身的小動作：拿／還工具、工具設定、查看、移動滑鼠到托架上方。</summary>
            static bool IsSetup(string condition)
            {
                string k = KeyOf(condition);
                return k == SimIds.ActiveTool || k.StartsWith("rack.") || k.StartsWith("ui.")
                       || k == "tool.socket_wrench.size" || k == "tool.socket_wrench.dir" || k == "tool.torque_wrench.setting"
                       || k == "tool.caliper.jaw" || k == "tool.caliper.zeroed"
                       || k.EndsWith(".viewed_count") || k.EndsWith(".flipped") || k.EndsWith(".over_bracket");
            }

            void DoSetup(string condition)
            {
                string k = KeyOf(condition);
                if (k == SimIds.ActiveTool)
                {
                    string want = condition.Substring(k.Length + 1);
                    var msg = want == "" ? ReturnTool() : (ReturnTool() ?? PickTool(want));
                    Assert.IsNull(msg, $"{Current}：{condition} → {msg}");
                }
                else if (k.StartsWith("rack.slot_"))
                {
                    int slot = int.Parse(k.Substring("rack.slot_".Length));
                    Assert.IsNull(RackLogic.TryReturn(s, slot, toolSlots.First(t => t.Value == slot).Key, true), condition);
                }
                else if (condition.Contains(">=")) s.SetFloat(k, float.Parse(condition.Substring(condition.IndexOf(">=") + 2), System.Globalization.CultureInfo.InvariantCulture));
                else if (condition.Contains("=")) s.Set(k, condition.Substring(k.Length + 1));
                else s.SetBool(k, true);
            }

            /// <summary>以正確方式完成目前這一步：依序做小動作（拿工具、設定、零件操作、放回工具架）。</summary>
            public void DoStep()
            {
                string runId = Current;
                bool acted = false;
                for (int guardCount = 0; guardCount < 40 && Current == runId && !completed; guardCount++)
                {
                    var subs = ProcedureRunner.SubActions(runner.Current);
                    int idx = (int)s.GetFloat("procedure.sub_index");
                    if (idx < subs.Length && IsSetup(subs[idx].condition)) { DoSetup(subs[idx].condition); continue; }
                    if (acted) break;
                    acted = true;
                    DoActions();
                }
            }

            /// <summary>這一步的零件操作本身（與工具／零件相同的狀態邏輯）。</summary>
            void DoActions()
            {
                string side = Side;
                switch (runner.Current.def.stepId)
                {
                    case "STP_SI_01_ConfirmLowered": LowerToTarget(); InspectPanHead(); CheckIndicator(); break;
                    case "STP_SI_04_VoltageTest": SelfTest(); MeasureVoltage(); break;
                    case "STP_SI_05_Grounding": Terminal(); PullTest(); Head(); break;
                    case "STP_CSR_01_CheckIsolation": s.SetBool("ui.workorder_open", true); break;
                    case "STP_CSR_02_ZeroCaliper": ZeroCaliper(); break;
                    case "STP_CSR_03_Measure_L": case "STP_CSR_04_Measure_R": MeasureAndRecord(side); break;
                    case "STP_CSR_05_Decide": Write("decision", "both"); break;
                    case "STP_CSR_06_Loosen": for (int n = 1; n <= 4; n++) Rotate(Bolt(side, n), 180, "loosen"); break;
                    case "STP_CSR_07_RemoveBolts": for (int n = 1; n <= 4; n++) { Rotate(Bolt(side, n), 600, "loosen"); Take(Bolt(side, n)); } break;
                    case "STP_CSR_08_RemoveStrip": Remove(side, "vertical"); ToTray(side); break;
                    case "STP_CSR_09_CleanBracket": Clean(side); break;
                    case "STP_CSR_10_CheckNew": Measure("env.spare_shelf.slot_2"); ReturnTool(); Pick("env.spare_shelf.slot_2"); break;   // 拿新滑板前手要空
                    case "STP_CSR_11_Install": RotateCarried(side); Install(side); break;
                    case "STP_CSR_12_PreTighten":
                        for (int n = 1; n <= 4; n++) Place(Bolt(side, n));
                        for (int n = 1; n <= 4; n++) Rotate(Bolt(side, n), 360, "lock");
                        break;
                    case "STP_CSR_13_Torque": for (int n = 1; n <= 4; n++) Torque(Bolt(side, n), 25.2f); break;
                    case "STP_CSR_14_Mark": for (int n = 1; n <= 4; n++) Mark(Bolt(side, n)); break;
                    case "STP_CSR_15_HeightCheck":
                        foreach (var sd in new[] { "L", "R" })
                        {
                            var r = Measure($"ip.carbon_strip_{sd}.measure_mid");
                            Write("newmid." + sd, r.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                        }
                        break;
                    case "STP_WC_01_ToolCount": Write("toolcount_confirmed", "true"); break;
                    case "STP_WC_03_RemoveGround": Head(); Terminal(); break;
                    default: Assert.Fail("沒有預期要操作的步驟：" + runner.Current.RunId); break;
                }
            }

            public void RunUntil(string runId)
            {
                int guardCount = 0;
                while (!completed && Current != runId)
                {
                    var before = Current;
                    DoStep();
                    Assert.AreNotEqual(before, Current, $"步驟 {before} 照正確方式操作後沒有完成。錯誤：{string.Join(", ", errors.Select(e => e.errorId))}");
                    Assert.Less(++guardCount, 60);
                }
                if (runId != null) Assert.AreEqual(runId, Current);
            }

            public string Describe() =>
                $"index={runner.CurrentIndex} finished={runner.Finished} completed={completed} done=[{string.Join(",", runner.Steps.Where(x => s.GetBool(x.DoneKey)).Select(x => x.RunId))}] " +
                $"errors=[{string.Join(",", errors.Select(e => e.errorId))}] gate={s.Get("procedure.gate_reason")} " +
                $"sub={s.GetFloat("procedure.sub_index")}/{s.GetFloat("procedure.sub_count")} tool.active={s.Get(SimIds.ActiveTool)} " +
                $"all_returned={s.Get("tools.all_returned")} no_loose_bolts={s.Get("tray.no_loose_bolts")} confirmed={s.Get("workorder.toolcount_confirmed")} hand={s.Get("hand.item")}";

            public ErrorDetected Last(string errorId) => errors.LastOrDefault(e => e.errorId == errorId);
        }

        static void AssertError(SliceSim sim, string errorId, Severity severity)
        {
            var e = sim.Last(errorId);
            Assert.IsNotNull(e.errorId, $"應出現 {errorId}；實際：{string.Join(", ", sim.errors.Select(x => x.errorId))}");
            Assert.AreEqual(severity, e.severity, errorId + " 嚴重度");
            Assert.IsFalse(string.IsNullOrWhiteSpace(e.what) || string.IsNullOrWhiteSpace(e.why) || string.IsNullOrWhiteSpace(e.fix), errorId + " 需要三段說明");
        }

        // ---------- 正常路徑 ----------
        [Test]
        public void NormalPath_All25StepsPass_NoErrors()
        {
            var sim = new SliceSim();
            sim.RunUntil(null);
            Assert.IsTrue(sim.completed, "流程應完成");
            Assert.AreEqual(34, sim.runner.Steps.Count, "25 個步驟定義，CSR_06–14 兩側展開後共 34 個執行步驟");
            Assert.AreEqual(25, sim.runner.Procedure.steps.Count);
            Assert.IsTrue(sim.runner.Steps.All(st => sim.s.GetBool(st.DoneKey)), "每個執行步驟都完成");
            Assert.AreEqual(0, sim.errors.Count, "正常路徑不應有錯誤：" + string.Join(", ", sim.errors.Select(e => e.errorId)));
            Assert.AreEqual(0, sim.blocked.Count);
            Assert.AreEqual("NewMounted", sim.s.Get("part.carbon_strip_L.state"));
            Assert.AreEqual("NewMounted", sim.s.Get("part.carbon_strip_R.state"));
            Assert.AreEqual(2f, sim.s.GetFloat("tray.old_strips"));
            Assert.AreEqual("open", sim.s.Get("lockout.breaker"), "收尾解鎖（預設完成）在最後才寫入");
        }

        [Test]
        public void BothOkScenario_PassJumpsToClosing_AndCompletes()
        {
            var sim = new SliceSim("SCN_BothOK");
            sim.RunUntil("STP_CSR_05_Decide");
            sim.Write("decision", "pass");
            Assert.AreEqual("STP_WC_01_ToolCount", sim.Current, "選合格要跳到收尾");
            sim.RunUntil(null);
            Assert.IsTrue(sim.completed);
            Assert.AreEqual("OldMounted", sim.s.Get("part.carbon_strip_L.state"));
        }

        // ---------- 安全類：一定擋下並說明 ----------
        [Test]
        public void E1_NotLowered_BlocksTouchingPanBody()
        {
            var sim = new SliceSim();   // 所有情境都從升弓開始
            Assert.IsTrue(sim.InspectPanHead(), "親眼查看弓頭（不碰觸）允許");
            Assert.IsFalse(sim.Rotate("ip.carbon_strip_L.bolt_1", 180, "loosen"));
            AssertError(sim, "ERR_Global_NotLowered", Severity.Critical);
            Assert.AreEqual("Tight", sim.s.Get("ip.carbon_strip_L.bolt_1.state"), "被擋下的動作不執行");
            sim.CheckIndicator();
            Assert.AreEqual("STP_SI_01_ConfirmLowered", sim.Current, "還在升弓，不能完成確認降弓");
            AssertError(sim, "ERR_SI01_NotInPosition", Severity.Major);
            sim.LowerToTarget();
            Assert.AreNotEqual("STP_SI_01_ConfirmLowered", sim.Current);
        }

        [Test]
        public void E2_NotGrounded_BlocksTouchingPanBody()
        {
            var sim = new SliceSim("SCN_NotGrounded");
            sim.RunUntil("STP_SI_04_VoltageTest");
            Assert.IsFalse(sim.Measure("ip.carbon_strip_L.measure_mid").HasValue);
            AssertError(sim, "ERR_Global_NotGrounded", Severity.Critical);
            Assert.IsFalse(sim.Remove("L", "vertical"));
            Assert.AreEqual("OldMounted", sim.s.Get("part.carbon_strip_L.state"));
        }

        [Test]
        public void E3_HeadBeforeTerminal_Blocked()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_SI_05_Grounding");
            Assert.IsFalse(sim.Head());
            AssertError(sim, "ERR_Ground_Order", Severity.Critical);
            Assert.IsFalse(sim.s.GetBool(SimIds.HeadClamped));
        }

        [Test]
        public void E13_GroundingBeforeVoltageTest_Blocked()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_SI_04_VoltageTest");
            Assert.IsFalse(sim.Terminal());
            AssertError(sim, "ERR_Ground_NoVoltageTest", Severity.Critical);
            Assert.IsFalse(sim.s.GetBool(SimIds.TerminalClamped));
        }

        [Test]
        public void E16_RemoveTerminalBeforeHead_Blocked()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_WC_03_RemoveGround");
            Assert.IsFalse(sim.Terminal());
            AssertError(sim, "ERR_Ground_RemoveOrder", Severity.Critical);
            Assert.IsTrue(sim.s.GetBool(SimIds.TerminalClamped));
            Assert.IsTrue(sim.Head()); Assert.IsTrue(sim.Terminal());
            Assert.AreEqual("STP_WC_03_RemoveGround", sim.Current, "接地棒還沒掛回工具架");
            sim.RunUntil(null);   // 最後一個小動作：把接地棒放回工具架
            Assert.IsTrue(sim.completed);
        }

        // ---------- 不能矇混過關 ----------
        [Test]
        public void E5_OnlyMid_FeedbackAndCannotPass()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_03_Measure_L");
            sim.MeasureAndRecord("L", "mid");
            AssertError(sim, "ERR_CSR03_OnlyMid", Severity.Major);
            Assert.AreEqual("STP_CSR_03_Measure_L", sim.Current, sim.Describe());
        }

        [Test]
        public void E6_ShouldReplaceButPass_CriticalAndCannotPass_ThenRedo()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_05_Decide");
            sim.Write("decision", "pass");
            AssertError(sim, "ERR_CSR05_ShouldReplace", Severity.Critical);
            Assert.IsTrue(sim.Last("ERR_CSR05_ShouldReplace").blocks);
            Assert.AreEqual("STP_CSR_05_Decide", sim.Current, "不能跳到收尾");
            sim.bus.Publish(new RestartStepRequested());
            sim.Write("decision", "both");
            Assert.AreEqual("STP_CSR_06_Loosen_L", sim.Current);
        }

        [Test]
        public void E6b_OnlyOne_Major()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_05_Decide");
            sim.Write("decision", "L");
            AssertError(sim, "ERR_CSR05_OnlyOne", Severity.Major);
            Assert.AreEqual("STP_CSR_05_Decide", sim.Current);
        }

        [Test]
        public void E6c_UnneededReplace_Minor()
        {
            var sim = new SliceSim("SCN_BothOK");
            sim.RunUntil("STP_CSR_05_Decide");
            sim.Write("decision", "both");
            AssertError(sim, "ERR_CSR05_Unneeded", Severity.Minor);
            Assert.AreEqual("STP_CSR_05_Decide", sim.Current);
        }

        [Test]
        public void E8_E9_TorqueUnderAndOver_FeedbackAndCannotPass()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_13_Torque_L");
            for (int n = 1; n <= 4; n++) sim.Torque($"ip.carbon_strip_L.bolt_{n}", 20f);
            AssertError(sim, "ERR_CSR13_Under", Severity.Major);
            Assert.AreEqual("STP_CSR_13_Torque_L", sim.Current);

            sim.bus.Publish(new RestartStepRequested());
            for (int n = 1; n <= 4; n++) sim.Torque($"ip.carbon_strip_L.bolt_{n}", 30f);
            AssertError(sim, "ERR_CSR13_Over", Severity.Major);
            Assert.AreEqual("STP_CSR_13_Torque_L", sim.Current);

            sim.bus.Publish(new RestartStepRequested());
            for (int n = 1; n <= 4; n++) sim.Torque($"ip.carbon_strip_L.bolt_{n}", 25f, 20f);
            AssertError(sim, "ERR_CSR13_Setting", Severity.Major);
        }

        /// <summary>第 10 輪：新品量測另外記，舊滑板讀數不被蓋掉；作業單比對新品讀數。</summary>
        [Test]
        public void NewStripReading_DoesNotOverwriteOldReading()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_15_HeightCheck");
            string mid = "ip.carbon_strip_L.measure_mid";
            float old = sim.s.GetFloat(mid + ".reading");
            var r = sim.Measure(mid);
            Assert.AreEqual(old, sim.s.GetFloat(mid + ".reading"), "舊滑板讀數留著");
            Assert.AreEqual(r.Value, sim.s.GetFloat(mid + ".new_reading"), 0.01f);
            Assert.IsTrue(sim.s.GetBool(mid + ".new_measured"));
            Assert.Greater(sim.s.GetFloat(mid + ".new_reading"), old + 5f, "新品比舊滑板厚");
        }

        /// <summary>第 10 輪：第 23 步是預設完成，編號跳過時要寫出那一步在做什麼，下一步再清掉。</summary>
        [Test]
        public void PresetStep_LeavesNote_ForTheNextStep()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_WC_03_RemoveGround");
            StringAssert.Contains("第 23 步 下車頂", sim.s.Get("procedure.preset_note"));
            StringAssert.Contains("第 23 步", sim.s.Get("ui.toast"));
            var early = new SliceSim();
            early.RunUntil("STP_SI_04_VoltageTest");
            StringAssert.Contains("第 2 步", early.s.Get("procedure.preset_note"));
            StringAssert.Contains("第 3 步", early.s.Get("procedure.preset_note"));
            early.RunUntil("STP_SI_05_Grounding");
            Assert.AreEqual("", early.s.Get("procedure.preset_note"), "下一步清掉");
        }

        /// <summary>第 10 輪：第 17 步放不下去要說為什麼；手上有滑板時在托架上方就算點到托架。</summary>
        [Test]
        public void Install_Fail_SaysWhy_AndClickOverBracketCounts()
        {
            var s = new SimState(new EventBus());
            string strip = "part.carbon_strip_L", bracket = "ip.carbon_strip_L.bracket";
            Assert.AreEqual("", SimIds.CarriedBracket(s));
            s.Set("procedure.side", "L"); s.Set(strip + ".state", "Carried");
            Assert.AreEqual(bracket, SimIds.CarriedBracket(s));
            Assert.IsTrue(SimIds.ClicksBracket(s, bracket, bracket));
            Assert.IsFalse(SimIds.ClicksBracket(s, bracket, null), "不在托架上方：不算");
            s.SetBool(strip + ".over_bracket", true);
            Assert.IsTrue(SimIds.ClicksBracket(s, bracket, null), "射線沒打到托架也算");
            s.Set(strip + ".new_orientation", "in");
            Assert.IsFalse(StripLogic.Install(s, strip, 0f));
            StringAssert.Contains("按 R", StripLogic.InstallFailText(s, strip, 0f));
            s.Set(strip + ".new_orientation", "out");
            Assert.IsFalse(StripLogic.Install(s, strip, 0.2f));
            StringAssert.Contains("偏 20.0 cm", StripLogic.InstallFailText(s, strip, 0.2f));
        }

        /// <summary>第 10 輪：備品架換側。第 10 步量外側換到 R，回到 L 要換回 L 的排列；兩側之間沒有側別不算換側（呼叫端略過）。</summary>
        [Test]
        public void SpareShelf_SideChanged_BothWays()
        {
            Assert.IsFalse(SpareShelf.SideChanged(null, "L"), "開場已經是 L 的排列");
            Assert.IsTrue(SpareShelf.SideChanged("L", "R"));
            Assert.IsTrue(SpareShelf.SideChanged("R", "L"));
            Assert.IsFalse(SpareShelf.SideChanged("L", "L"));
        }

        /// <summary>第 10 輪：作業單按了清點但前面還沒做，要說還差什麼，不能沒反應。</summary>
        [Test]
        public void ToolCount_PressedEarly_SaysWhatIsLeft()
        {
            var bus = new EventBus(); var s = new SimState(bus);
            s.Set("training.mode", "tutorial");
            s.Set("hint.sub_target", "env.tool_rack"); s.Set("hint.sub_text", "清點平台桿架與工具袋");
            Assert.IsTrue(WorkOrder.Write(s, bus, "toolcount_confirmed", "true"));
            StringAssert.Contains("清點平台桿架與工具袋", s.Get("ui.toast"));
            s.Set("ui.toast", ""); s.Set("hint.sub_target", SimIds.WorkOrder);
            WorkOrder.Write(s, bus, "toolcount_confirmed", "true");
            Assert.AreEqual("", s.Get("ui.toast"), "輪到作業單就不用提示");
        }

        /// <summary>驗收組長第 9 輪：第 19 步 2 號只鎖到 22.9，按「繼續」後引導留在 2 號，重鎖到合格後走完。</summary>
        [Test]
        public void Step19_UnderTorque_Continue_StaysOnBolt_ReTorque_Completes()
        {
            var sim = new SliceSim();
            sim.s.Set("training.mode", "tutorial");
            using var hints = new HintService(sim.bus, sim.s);
            using var feedback = new ErrorFeedback(sim.bus, sim.s);
            sim.RunUntil("STP_CSR_13_Torque_L");
            string Bolt(int n) => $"ip.carbon_strip_L.bolt_{n}";
            sim.s.Set(SimIds.ActiveTool, SimIds.ToolTorque);
            sim.s.SetFloat("tool.torque_wrench.setting", 25f);
            Assert.IsTrue(sim.Torque(Bolt(1), 25f));
            Assert.IsTrue(sim.Torque(Bolt(2), 22.9f));
            AssertError(sim, "ERR_CSR13_Under", Severity.Major);
            sim.bus.Publish(new ActionRequested { targetId = "ui.error", text = "dismiss" });
            Assert.AreEqual(Bolt(2), sim.s.Get("hint.sub_target"), "按繼續後引導留在 2 號");
            StringAssert.Contains("再按住施力", sim.s.Get("procedure.gate_reason"));
            Assert.IsTrue(sim.Torque(Bolt(2), 25f), "能重鎖");
            Assert.AreEqual(Bolt(3), sim.s.Get("hint.sub_target"), "合格才往下");
            Assert.AreEqual("", sim.s.Get("procedure.gate_reason"));
            sim.Torque(Bolt(3), 25f); sim.Torque(Bolt(4), 25f);
            sim.s.Set(SimIds.ActiveTool, "");
            Assert.AreNotEqual("STP_CSR_13_Torque_L", sim.Current, "步驟完成");
        }

        /// <summary>練習模式放行：小動作走完還差 2 號，還差的目標要寫出來。教學模式提示帶到那個目標，不說「這一步完成了」。</summary>
        [Test]
        public void Stall_NamesMissingTarget_HintFollows()
        {
            var sim = new SliceSim();
            sim.s.Set("training.mode", "practice");
            sim.RunUntil("STP_CSR_13_Torque_L");
            string bolt2 = "ip.carbon_strip_L.bolt_2";
            sim.s.Set(SimIds.ActiveTool, SimIds.ToolTorque);
            sim.s.SetFloat("tool.torque_wrench.setting", 25f);
            for (int n = 1; n <= 4; n++) sim.Torque($"ip.carbon_strip_L.bolt_{n}", n == 2 ? 22.9f : 25f);
            sim.s.Set(SimIds.ActiveTool, "");
            sim.runner.Poll(1000f); sim.runner.Poll(1004f);
            Assert.IsTrue(sim.s.GetBool("procedure.stalled"));
            Assert.AreEqual(bolt2, sim.s.Get("procedure.stall_target"));

            var bus = new EventBus(); var s = new SimState(bus);
            using var hints = new HintService(bus, s);
            s.Set("training.mode", "tutorial");
            bus.Publish(new StepStarted { subActions = "拿\u001ftool", highlightIds = "all" });
            s.SetFloat("procedure.sub_index", 1);
            Assert.AreEqual("", s.Get("hint.sub_target"));
            StringAssert.DoesNotContain("完成了", s.Get("hint.sub_text"), "完成條件還沒成立");
            s.Batch(() => { s.SetBool("procedure.stalled", true); s.Set("procedure.stall_text", "鎖 2 號"); s.Set("procedure.stall_target", bolt2); s.Set("procedure.stall_stand", "tp.roof.L"); });
            Assert.AreEqual(bolt2, s.Get("hint.sub_target"), "C 和帶我過去讀這個");
            Assert.AreEqual("tp.roof.L", s.Get("hint.sub_stand"));
            Assert.AreEqual(bolt2, s.Get("hint.highlight"));
            Assert.AreEqual("還差：鎖 2 號", s.Get("hint.sub_text"));
        }

        [Test]
        public void E10_Reversed_NeverSnaps_Critical()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_11_Install_L");
            Assert.IsFalse(sim.Install("L"));
            AssertError(sim, "ERR_CSR11_Reversed", Severity.Critical);
            Assert.AreEqual("Carried", sim.s.Get("part.carbon_strip_L.state"));
            Assert.AreEqual("STP_CSR_11_Install_L", sim.Current);
        }

        // ---------- 其餘錯誤都有 ErrorDetected ----------
        [Test]
        public void SI01_ConfirmBeforeInPosition_ShowsDistance_ThenPassesWhenLowered()
        {
            var sim = new SliceSim();   // 教學預設：開場升弓中
            sim.s.SetFloat(SimIds.PanHeadLowerDistance, 37.4f);
            Assert.IsTrue(sim.InspectPanHead(), "徒手點弓頭不可觸發 E1");
            Assert.IsNull(sim.Last("ERR_Global_NotLowered").errorId);
            AssertError(sim, "ERR_SI01_NotInPosition", Severity.Major);
            StringAssert.Contains("37.4", sim.Last("ERR_SI01_NotInPosition").what, "說明要顯示還差幾 cm");
            sim.CheckIndicator();
            Assert.AreEqual("STP_SI_01_ConfirmLowered", sim.Current, "未到位不能通過");
            sim.s.SetFloat(SimIds.PanHeadLowerDistance, 1.6f);   // 還差 0.6 cm 超出容許 1 cm
            Assert.AreEqual("STP_SI_01_ConfirmLowered", sim.Current);
            sim.LowerToTarget();
            Assert.AreEqual("STP_SI_04_VoltageTest", sim.Current, "到位後 SI_01 通過");
        }

        [Test]
        public void SI01_LightOnly_Major()
        {
            var sim = new SliceSim();
            sim.LowerToTarget();
            sim.CheckIndicator();
            AssertError(sim, "ERR_SI01_LightOnly", Severity.Major);
        }

        [Test]
        public void SI04_NoSelfTest_Critical()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_SI_04_VoltageTest");
            sim.MeasureVoltage();
            AssertError(sim, "ERR_SI04_NoSelfTest", Severity.Critical);
            Assert.AreEqual("STP_SI_04_VoltageTest", sim.Current);
        }

        [Test]
        public void SI05_NoPullTest_Major_CannotPass()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_SI_05_Grounding");
            sim.Terminal(); sim.Head();
            AssertError(sim, "ERR_SI05_NoPullTest", Severity.Major);
            Assert.AreEqual("STP_SI_05_Grounding", sim.Current);
        }

        [Test]
        public void CSR02_MeasureBeforeZero_Major()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_02_ZeroCaliper");
            sim.Measure("ip.carbon_strip_L.measure_mid");
            AssertError(sim, "ERR_CSR02_MeasureBeforeZero", Severity.Major);
        }

        [Test]
        public void CSR03_CopyError_Minor_CannotPass()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_03_Measure_L");
            foreach (var p in StripMeasureLogic.Points) sim.Measure($"ip.carbon_strip_L.measure_{p}");
            sim.Write("reading.L.left", "9.99"); sim.Write("reading.L.mid", "9.99"); sim.Write("reading.L.right", "9.99"); sim.Write("submit.L", "true");
            AssertError(sim, "ERR_CSR03_CopyError", Severity.Minor);
            Assert.AreEqual("STP_CSR_03_Measure_L", sim.Current);
        }

        [Test]
        public void GenericWrongTool_Minor_AndNoStateChange()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_06_Loosen_L");
            Assert.IsFalse(sim.Rotate("ip.carbon_strip_L.bolt_1", 180, "loosen", "13", SimIds.ToolCaliper));
            AssertError(sim, ErrorDetector.WrongToolId, Severity.Minor);
            Assert.AreEqual("Tight", sim.s.Get("ip.carbon_strip_L.bolt_1.state"));
        }

        [Test]
        public void CSR06_WrongSocket_Order_FullTurn()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_06_Loosen_L");
            sim.Rotate("ip.carbon_strip_L.bolt_1", 180, "loosen", "17");
            AssertError(sim, "ERR_CSR06_WrongSocket", Severity.Minor);
            sim.Rotate("ip.carbon_strip_L.bolt_2", 180, "loosen");
            AssertError(sim, "ERR_CSR06_Order", Severity.Minor);
            sim.Rotate("ip.carbon_strip_L.bolt_3", 400, "loosen");
            AssertError(sim, "ERR_CSR06_FullTurn", Severity.Major);
            Assert.AreEqual("STP_CSR_06_Loosen_L", sim.Current);
        }

        [Test]
        public void CSR08_Pry_Major()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_08_RemoveStrip_L");
            Assert.IsFalse(sim.Remove("L", "pry"));
            AssertError(sim, "ERR_CSR08_Pry", Severity.Major);
            Assert.AreEqual("OldMounted", sim.s.Get("part.carbon_strip_L.state"));
        }

        [Test]
        public void CSR09_PickBeforeClean_Major()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_09_CleanBracket_L");
            sim.Measure("env.spare_shelf.slot_2", SimIds.ToolCaliper);
            sim.Pick("env.spare_shelf.slot_2");
            AssertError(sim, "ERR_CSR09_Dirty", Severity.Major);
        }

        [Test]
        public void CSR10_WrongTypeAndCracked_Critical()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_10_CheckNew_L");
            Assert.IsFalse(sim.Pick("env.spare_shelf.slot_1"));
            AssertError(sim, "ERR_CSR10_WrongType", Severity.Critical);
            sim.bus.Publish(new RestartStepRequested());
            Assert.IsFalse(sim.Pick("env.spare_shelf.slot_3"));
            AssertError(sim, "ERR_CSR10_Cracked", Severity.Critical);
            Assert.AreEqual("STP_CSR_10_CheckNew_L", sim.Current);
        }

        [Test]
        public void CSR12_OvertightFirst_Major()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_12_PreTighten_L");
            for (int n = 1; n <= 4; n++) sim.Place($"ip.carbon_strip_L.bolt_{n}");
            sim.Rotate("ip.carbon_strip_L.bolt_1", 720, "lock");
            AssertError(sim, "ERR_CSR12_Overtight", Severity.Major);
        }

        [Test]
        public void CSR15_HeightDiffExceeded_Major()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_CSR_15_HeightCheck");
            sim.s.SetFloat("part.carbon_strip_R.true.mid", 10.5f);   // 模擬裝錯/墊高造成的高低差
            foreach (var sd in new[] { "L", "R" })
            {
                var r = sim.Measure($"ip.carbon_strip_{sd}.measure_mid");
                sim.Write("newmid." + sd, r.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            }
            AssertError(sim, "ERR_CSR15_Exceeds", Severity.Major);
            Assert.AreEqual("STP_CSR_15_HeightCheck", sim.Current);
        }

        [Test]
        public void WC01_Leftover_Critical()
        {
            var sim = new SliceSim();
            sim.RunUntil("STP_WC_01_ToolCount");
            sim.s.SetBool("tools.all_returned", false);   // 手上還拿著工具
            sim.Write("toolcount_confirmed", "true");
            AssertError(sim, "ERR_WC01_Leftover", Severity.Critical);
            Assert.AreEqual("STP_WC_01_ToolCount", sim.Current);
        }
    }
}
