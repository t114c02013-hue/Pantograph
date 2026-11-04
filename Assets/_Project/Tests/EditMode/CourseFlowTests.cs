using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.Parts.Common;
using PantographSim.Procedures;
using UnityEditor;

namespace PantographSim.Tests
{
    /// <summary>
    /// R1.5b b4（軟體工程裁定 2026-11-03）CourseFlowTests：用匯入產生的兩門課（ProcedureRunner＋requireFreshWrite），
    /// 腳本照正確順序一個 tick 寫一個小動作的條件鍵，把整門課跑完。有小動作在啟用的同一個 tick 就完成（不在 autoCompleteAllowed）就失敗。
    /// 還沒有機制的鍵由腳本直接寫黑板，這些都列在 Docs/R1.5b/b4c-stubs.md（ImporterTests 檢查一致）。
    /// 步驟完成條件（R1 沿用的 ValueInRange、OrderedSequence、Decision…）也由腳本依規則型別寫入。
    /// </summary>
    public class CourseFlowTests
    {
        /// <summary>
        /// R1.5b b4（5b）：腳本走真的機制、不直接寫黑板的鍵（這些都是 LiveState，一成立就完成，所以要靠機制連帶的變化才不會啟用時就成立）：
        /// 工具拿放 tool.active、rack.slot_N（ToolBelt：拿起＝in_hand、收回＝on_rack、上鎖包進腰包）；吊物底面 part.crane.load_bottom_y（CraneMechanism，吊物懸空時才寫）；
        /// 離座高度 part.crane.drop_distance：天車真的把吊物移到「座面（吊物原本放著的高度）＋該高度」，同時寫 part.pan_assy.state＝Lifted
        /// （場景裡由 PanAssyRigging 寫，RiggingTests 的車頂↔台架往返驗證過；drop_distance 本身仍是 b4:p5 暫代）；降弓檢測點 part.pan_head.lower_distance_cm（PositionCheck＋LiftControl 放開時強制量一次）。
        /// </summary>
        sealed class Mechanisms : System.IDisposable
        {
            readonly SimState s;
            readonly BeltRig belt;
            readonly CraneRig crane;
            readonly UnityEngine.GameObject pcRoot;
            readonly PantographSim.Parts.Common.PositionCheck pc;
            readonly UnityEngine.Transform check;
            readonly float restBottomY;
            static readonly System.Text.RegularExpressions.Regex Slot = new System.Text.RegularExpressions.Regex(@"^rack\.slot_(\d+)=(on_rack|in_hand|in_use|in_pouch)$");

            public Mechanisms(EventBus bus, SimState state)
            {
                s = state;
                oldClock = SimClock.Source; SimClock.Source = () => now;   // 放電計時用腳本自己的時鐘
                crane = new CraneRig(true, bus, s);
                belt = new BeltRig(bus, s);
                restBottomY = Bottom;
                pcRoot = new UnityEngine.GameObject("TestPanHeadCheck");
                check = new UnityEngine.GameObject("Check").transform; check.SetParent(pcRoot.transform);
                pc = pcRoot.AddComponent<PantographSim.Parts.Common.PositionCheck>();
                pc.Setup(AssetDatabase.LoadAssetAtPath<PositionCheckDefinition>("Assets/_Project/Modules/Parts/_Common/Data/PC_PanHeadLower.asset"), check, null, null, null, null);
                check.position = pc.TargetWorld + UnityEngine.Vector3.up;   // 開場升弓
                Sensors.Register(pc);   // EditMode 不會呼叫 OnEnable
                var notLowered = StepJsonImporter.LoadAll<SafetyRuleDefinition>(StepJsonImporter.SafetyFolder).First(r => r.kind == SafetyRuleKind.NotLowered);
                s.SetFloat(SimIds.PanHeadLowerTolerance, notLowered.toleranceCm);   // App 開場寫的（容許值只有一個來源）
                foreach (var side in new[] { "L", "R" }) s.SetFloat(SimIds.BoltTray(side) + ".bolts", 0);   // BoltTray、BoltPouch 的 OnBound
                s.SetFloat(FastenerLogic.BoltPouchCount, 0);
                bus.Subscribe<StateChanged>(e =>   // 工具袋元件（ToolBag）做的：相關鍵一變就重算清點
                {
                    if (counting) return;
                    counting = true;
                    try { ToolCountLogic.Update(s, BeltRig.Table); } finally { counting = false; }
                });
                bus.Subscribe<ActionPerformed>(e =>
                {
                    if (e.targetId == FastenerLogic.BoltPouch) PantographSim.Worksite.BoltPouch.Apply(s, e.text);
                    else if (e.targetId == SimIds.BoltTray("L") || e.targetId == SimIds.BoltTray("R")) PantographSim.Worksite.BoltTray.Apply(s, SimIds.SideOf(e.pointId), e.text, e.pointId);
                });
            }

            float Bottom => crane.load.position.y - crane.load.localScale.y / 2f;
            int SlotOf(string toolId) => BeltRig.Table.FindTool(toolId).slot;
            string SlotState(int n) => s.Get(RackLogic.SlotKey(n), RackLogic.OnRack);

            void Hold(string toolId)
            {
                string active = s.Get(SimIds.ActiveTool);
                if (active == toolId) return;
                if (active != "") belt.belt.ClickSlot(SlotOf(active));   // 先放回手上那件
                if (toolId == "") return;
                int n = SlotOf(toolId);
                if (SlotState(n) != RackLogic.InPouch) belt.belt.ClickSlot(n);
                if (SlotState(n) == RackLogic.InPouch) Pouch();   // 上鎖包：從腰包拿出來
            }

            void Pouch() => Sim.Bus.Publish(new ActionRequested { targetId = ToolSlotTable.WaistPouch, text = "", toolId = "" });

            void Rack(int n, string want)
            {
                if (SlotState(n) == want) return;
                var tool = BeltRig.Table.Find(n).toolId;
                if (want == RackLogic.OnRack)
                {
                    if (s.Get(SimIds.ActiveTool) != tool) Hold("");   // 收回腰包、使用中的工具：手先空出來
                    belt.belt.ClickSlot(n);
                }
                else
                {
                    Hold(tool);
                    if (want == RackLogic.InPouch) { Pouch(); return; }
                    if (want == RackLogic.InUse) Sim.Bus.Publish(new ActionRequested { targetId = "tool.belt", text = "deploy", toolId = "" });
                }
            }

            /// <summary>吊物底面移到 y（上或下）。</summary>
            void HoistTo(float y)
            {
                if (Bottom < y) Hoist("Up", b => b >= y - 1e-3f); else if (Bottom > y + 1e-3f) Hoist("Down", b => b <= y + 1e-3f);
            }

            void Hoist(string cmd, System.Func<float, bool> until)
            {
                crane.Cmd(cmd, true);
                for (int i = 0; i < 20000 && !until(Bottom); i++) crane.Run(0.05f, 0.05f);
                crane.Cmd("Stop"); crane.Run(0.2f, 0.05f);
                Assert.IsTrue(until(Bottom), $"天車 {cmd} 到不了（吊物底 {Bottom:0.000}、狀態 {s.Get(SimIds.CraneState)}）");
            }

            const string RaisedPose = "part.pan_head.pose=Raised";

            void RaisePan()
            {
                check.position = pc.TargetWorld + UnityEngine.Vector3.up;   // 檢測點離開降弓位置
                LiftControl.Send(s, "Up");
                LiftControl.Send(s, "Stop");
            }

            void LowerPan()
            {
                check.position = pc.TargetWorld;   // 弓頭降到降弓位置
                LiftControl.Send(s, "Down");
                LiftControl.Send(s, "Stop");       // 放開：PositionCheck 強制量一次
            }

            static IEnumerable<string> Parts(string condition) => condition.Split(new[] { "&&" }, System.StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim());
            static bool IsLeadPart(string part) => part.StartsWith(GroundingLeadLogic.Carried) || part.StartsWith(GroundingLeadLogic.CarBody + ".") || part.StartsWith(GroundingLeadLogic.Cable + ".");
            static void Command(string target, string text, string toolId = "") => Sim.Bus.Publish(new ActionRequested { targetId = target, text = text, toolId = toolId });
            int LeadSlot => SlotOf(GroundingLeadLogic.ToolId);

            const string CtrlConnector = "ip.pan_assy.ctrl_connector";
            static readonly System.Text.RegularExpressions.Regex InsulatorClean = new System.Text.RegularExpressions.Regex(@"^(ip\.roof\.insulator_\d+)\.clean_ratio>=");

            /// <summary>D43 v3.12：清潔布擦絕緣子面向站點的計分格（DirtMask 收到 clean:u,v 時呼叫的同一段；計分格達門檻整支變乾淨）。</summary>
            void WipeInsulator(string id)
            {
                var def = InsulatorDirtDef.Load();
                int k = int.Parse(id.Substring(id.LastIndexOf('_') + 1)) - 1;
                var score = def.scoreMask[k];
                for (int i = 0; i < score.Length && !s.GetBool(id + ".wrapped"); i++)
                    if (score[i] == '1') DirtMask.Clean(s, id, (i % def.cols + 0.5f) / def.cols, (i / def.cols + 0.5f) / def.rows, def.rows, true, score, def.faceMask[k], def.cleanRatio);
            }
            static readonly System.Text.RegularExpressions.Regex SlingVerdict = new System.Text.RegularExpressions.Regex(@"^(env\.sling_(?:\d|spare))\.verdict(?:=ok)?$");
            static readonly System.Text.RegularExpressions.Regex SlingReplaced = new System.Text.RegularExpressions.Regex(@"^env\.sling_(\d)\.replaced$");
            static readonly System.Text.RegularExpressions.Regex ViewedSlot = new System.Text.RegularExpressions.Regex(@"^env\.tool_bag\.slot_(\d+)\.viewed$");
            static readonly string[] Derived = { ToolCountLogic.TrayCount, ToolCountLogic.Leftover, ToolCountLogic.StripCountOk, FastenerLogic.BoltPouchCount, "env.bolt_tray_L.bolts", "env.bolt_tray_R.bolts" };
            bool counting;
            static readonly System.Text.RegularExpressions.Regex BoltState = new System.Text.RegularExpressions.Regex(@"^(.+\.bolt_\d+)\.state=(Out|InTray|Placed)$");
            readonly SocketCatalog sockets = AssetDatabase.LoadAssetAtPath<SocketCatalog>("Assets/_Project/Tools/Data/SocketCatalog.asset");

            /// <summary>螺栓轉到脫出（先到鬆開、再到脫出，鬆開順序照記）、收進螺栓盤／零件袋、裝回時從那裡拿：FastenerLogic（FastenerBehaviour 收到工具動作時呼叫的同一段），套筒讀 SocketCatalog。</summary>
            void Bolt(string part)
            {
                var m = BoltState.Match(part);
                string id = m.Groups[1].Value, want = m.Groups[2].Value, size = sockets.SizeFor(id).ToString();
                if (s.Get(id + ".state", FastenerLogic.Tight) == want) return;
                if (want == FastenerLogic.Out)
                {
                    FastenerLogic.Apply(s, Sim.Bus, id, $"rotate:{size}:loosen", FastenerLogic.LoosenedAt, size);
                    FastenerLogic.Apply(s, Sim.Bus, id, $"rotate:{size}:loosen", FastenerLogic.OutAt, size);
                }
                else Assert.IsTrue(FastenerLogic.Apply(s, Sim.Bus, id, want == FastenerLogic.InTray ? "take" : "place", 0, size), part);   // 裝回：從螺栓盤／零件袋拿
                Assert.AreEqual(want, s.Get(id + ".state"), part);
            }

            float now;
            readonly System.Func<float> oldClock;

            /// <summary>高阻計：兩條測試線接上弓頭接地掛點、車體接地點（MeggerTool）。</summary>
            void Megger()
            {
                Hold(MeggerLogic.ToolId);
                foreach (var p in new[] { MeggerLogic.PanPoint, MeggerLogic.BodyPoint })
                    if (!s.GetBool("tool.megger.lead." + p)) Command(p, "click", MeggerLogic.ToolId);
            }

            /// <summary>接地桿碰弓頭接地掛點放電：碰上、時間過 dischargeSeconds（工具每幀 Tick 累計）、移開。</summary>
            void Discharge()
            {
                Hold(SimIds.ToolGroundingRod);
                Command(MeggerLogic.PanPoint, "click", SimIds.ToolGroundingRod);
                now += ToolRulesDef.Load().dischargeSeconds + 0.1f;
                belt.belt.Active.Tick();
                Command(MeggerLogic.PanPoint, "click", SimIds.ToolGroundingRod);
            }

            /// <summary>上鎖掛牌：手上拿著上鎖掛牌組（從腰包拿出）點主斷路器／升弓開關（LockoutTagTool）。</summary>
            void Lockout(string part)
            {
                if (part.EndsWith(".tagged"))   // v3.10：已上鎖後換「牌」點同一個開關
                {
                    if (s.GetBool(part)) return;
                    Hold(SimIds.ToolLockout);
                    Command(SimIds.ToolLockout, "part:tag");
                    Command("ip.cab." + part.Substring("lockout.".Length, part.Length - "lockout.".Length - ".tagged".Length), "click", SimIds.ToolLockout);
                    Command(SimIds.ToolLockout, "part:lock");
                    return;
                }
                int eq = part.IndexOf('=');
                string target = "ip.cab." + part.Substring("lockout.".Length, eq - "lockout.".Length), want = part.Substring(eq + 1);
                if (s.Get(part.Substring(0, eq), "open") == want) return;
                Hold(SimIds.ToolLockout);
                Command(target, "click", SimIds.ToolLockout);
            }

            /// <summary>短接地線拿到手上（前一個動作：工具環或接著的位置拿下來、或從工具架拿），ToolBelt 在這一批生效後才換手上的工具。</summary>
            void EnsureLead()
            {
                if (s.Get(SimIds.ActiveTool) == GroundingLeadLogic.ToolId) return;
                if (SlotState(LeadSlot) == RackLogic.InUse) { Hold(""); Command(GroundingLeadLogic.ToolId, "take"); }
                else Hold(GroundingLeadLogic.ToolId);
            }

            /// <summary>短接地線的條件：掛到工具環、接上或取下兩端、拉線確認（GroundingLeadTool）。</summary>
            void Lead(string part)
            {
                if (part == GroundingLeadLogic.Carried) { if (!s.GetBool(part)) Command(GroundingLeadLogic.ToolId, "ring"); return; }
                if (part == GroundingLeadLogic.PullTestedKey) { Command(GroundingLeadLogic.ToolId, "pulltest"); return; }
                bool want = !part.EndsWith("=false");
                string point = part.StartsWith(GroundingLeadLogic.CarBody) ? GroundingLeadLogic.CarBody : GroundingLeadLogic.Cable;
                if (s.GetBool(GroundingLeadLogic.ClampedKey(point)) != want) Command(point, "click", GroundingLeadLogic.ToolId);
            }

            /// <summary>一個 tick：機制的動作和其他鍵一批生效（和原本的 Write 一樣）。回傳有沒有走機制。</summary>
            public bool Write(string condition)
            {
                if (Parts(condition).Any(IsLeadPart)) EnsureLead();
                foreach (var m in Parts(condition).Select(p => ViewedSlot.Match(p)).Where(m => m.Success))
                    s.Set(ToolCountLogic.HoverKey, "env.tool_rack.slot_" + m.Groups[1].Value);   // 逐格指著工具袋（一格一個畫面；ToolCountLogic 記注視）
                bool used = false; s.Batch(() => used = WriteInBatch(condition)); return used;
            }

            bool WriteInBatch(string condition)
            {
                var rest = new List<string>();
                float? dropLo = null, dropHi = null;
                foreach (var part in Parts(condition).OrderBy(p => IsLeadPart(p) ? 0 : 1))   // 短接地線先（掛到工具環後掛位 12 才是使用中）
                {
                    var m = Slot.Match(part);
                    if (IsLeadPart(part)) Lead(part);
                    else if (part.StartsWith("lockout.key_holder")) { }   // 上鎖時 LockoutTagTool 一起寫
                    else if (part.StartsWith(MeggerLogic.Count + ">=")) Megger();
                    else if (BoltState.IsMatch(part)) Bolt(part);
                    else if (ViewedSlot.IsMatch(part)) { }   // 已在批次前逐格指過
                    else if (Derived.Any(part.StartsWith)) { }   // 清點的件數由機制算（ToolBag／BoltPouch／BoltTray），腳本不寫
                    else if (part == "ip.pan_assy.hold_down.pull_tested") DetachableLogic.PullTest(s, "ip.pan_assy.hold_down");   // DetachablePart：裝著空手拉一下
                    else if (part.StartsWith(DetachableLogic.CapKey(CtrlConnector) + "=")) { if (s.Get(DetachableLogic.CapKey(CtrlConnector)) != part.Substring(part.IndexOf('=') + 1)) Assert.IsTrue(DetachableLogic.CapClick(s, CtrlConnector), part); }
                    else if (part == MeggerLogic.Measured) Command(MeggerLogic.ToolId, "test");   // D43：按測試鍵（MeggerTool）
                    else if (InsulatorClean.Match(part) is var ic && ic.Success) WipeInsulator(ic.Groups[1].Value);
                    else if (part.StartsWith(MeggerLogic.Reading + ">=")) { }   // 讀值由高阻計算
                    else if (part.StartsWith("workorder.ir_reading>="))   // 照高阻計讀值填作業單（WorkOrder）
                        Assert.IsTrue(PantographSim.Worksite.WorkOrder.Write(s, Sim.Bus, PantographSim.Worksite.WorkOrder.IrReadingField, s.GetFloat(MeggerLogic.Reading).ToString(CultureInfo.InvariantCulture)), part);
                    else if (part == "workorder.ir_judged" || part == "workorder.ir_judgement=pass")   // 照讀值勾合格／不合格
                        Assert.IsTrue(PantographSim.Worksite.WorkOrder.Write(s, Sim.Bus, PantographSim.Worksite.WorkOrder.IrJudgementField, MeggerLogic.Pass(s, ToolRulesDef.Load()) ? "pass" : "fail"), part);
                    else if (part.StartsWith(MeggerLogic.DischargeKey + ">=") || part == MeggerLogic.Discharged) Discharge();
                    else if (part == MeggerLogic.Count + "<=0")   // 空手拆兩條測試線（MeggerLeads）
                        foreach (var p in new[] { MeggerLogic.PanPoint, MeggerLogic.BodyPoint }) Assert.IsNull(MeggerLogic.RemoveLead(s, p, ToolRulesDef.Load()), part);
                    else if (part == "env.work_order.anomaly.checked") PantographSim.Worksite.WorkOrder.Write(s, Sim.Bus, PantographSim.Worksite.WorkOrder.AnomalyField, "none");
                    else if (part.StartsWith("env.work_order.part_trace.matches_spare_"))   // CSR_10 g：抄選用那一條備品的標籤（WorkOrder）
                    {
                        string side = part.Substring(part.Length - 1);
                        Assert.IsTrue(PantographSim.Worksite.WorkOrder.Write(s, Sim.Bus, PantographSim.Worksite.WorkOrder.PartTracePrefix + side, s.Get("spare." + side + ".picked_slot")), part);
                    }
                    else if (part == "workorder.toolcount_platform_confirmed") PantographSim.Worksite.WorkOrder.Write(s, Sim.Bus, PantographSim.Worksite.WorkOrder.PlatformCountField, "true");
                    else if (part.StartsWith("lockout.")) Lockout(part);
                    else if (part.StartsWith(CabControlLogic.Breaker + "=")) { if (s.Get(CabControlLogic.Breaker, CabControlLogic.On) != part.Substring(CabControlLogic.Breaker.Length + 1)) Assert.IsNull(CabControlLogic.ToggleBreaker(s), part); }
                    else if (part.StartsWith(CabControlLogic.CylinderPower + "=")) { if (s.Get(CabControlLogic.CylinderPower, CabControlLogic.On) != part.Substring(CabControlLogic.CylinderPower.Length + 1)) Assert.IsNull(CabControlLogic.ToggleCylinderPower(s), part); }
                    else if (part == CabControlLogic.RaiseTestDone) CabControlLogic.PressRaise(s);   // 控制盤 CabSwitch 呼叫的同一段邏輯
                    else if (SlingVerdict.Match(part) is var sv && sv.Success)   // v3.10：查看後判可用／不可用（SlingLogic，割傷那一條判不可用）
                    {
                        string id = sv.Groups[1].Value; int n = SlingLogic.Number(id);
                        Assert.IsNull(SlingLogic.Verdict(s, id, n == 0 || !s.GetBool(SlingLogic.CutKey(n)), null), part);
                    }
                    else if (part == SlingLogic.Rejected + ".tagged")   // s1：取下割傷吊帶、拿停用牌、掛上（SlingRig 呼叫的同一段）
                    {
                        int n = (int)s.GetFloat(SlingLogic.Index);
                        Assert.IsNull(SlingLogic.Detach(s, n), part); Assert.IsNull(SlingLogic.TakeTag(s), part); Assert.IsNull(SlingLogic.HangTag(s, n), part);
                    }
                    else if (part == SlingLogic.Rejected + ".state=" + SlingLogic.InBin) Assert.IsNull(SlingLogic.PutInBin(s), part);
                    else if (part == SlingLogic.Spare + ".inspected") { s.SetBool(part, true); Assert.IsNull(SlingLogic.TakeSpare(s), part); }   // s3：查看並拿備用吊帶
                    else if (SlingReplaced.Match(part) is var sr && sr.Success) Assert.IsNull(SlingLogic.HangSpare(s, int.Parse(sr.Groups[1].Value)), part);   // s4
                    else if (part == HarnessLogic.Stowed) { if (!s.GetBool(part)) Command(SimIds.ToolHarness, "stow"); }   // D31 胸前扣環
                    else if (part == HarnessLogic.Stowed + "=false") Command(SimIds.ToolHarness, "take");
                    else if (part.StartsWith(SimIds.ActiveTool + "=")) Hold(part.Substring(SimIds.ActiveTool.Length + 1));
                    else if (m.Success) Rack(int.Parse(m.Groups[1].Value), m.Groups[2].Value);
                    else if (part.StartsWith(SimIds.CraneLoadBottomY + ">="))
                    {
                        float y = float.Parse(part.Substring(SimIds.CraneLoadBottomY.Length + 2), CultureInfo.InvariantCulture);
                        Hoist("Up", b => b >= y);
                    }
                    else if (part.StartsWith(SimIds.PanHeadLowerDistance + "<=")) LowerPan();
                    else if (part == RaisedPose) { RaisePan(); rest.Add(part); }   // 弓頭升到頂（姿態鍵在場景裡由 PantographMechanism 寫）
                    else if (part.StartsWith(PantographSim.Parts.Mechanism.PanAssyRigging.DropDistanceKey))
                    {
                        var key = PantographSim.Parts.Mechanism.PanAssyRigging.DropDistanceKey;
                        float v = float.Parse(part.Substring(key.Length + 2), CultureInfo.InvariantCulture);
                        if (part.Substring(key.Length, 2) == ">=") dropLo = v; else dropHi = v;
                        rest.Add(part);
                    }
                    else rest.Add(part);
                }
                if (dropLo.HasValue || dropHi.HasValue)
                {
                    s.Set(PantographSim.Parts.Mechanism.PanAssyRigging.StateKey, PantographSim.Parts.Mechanism.PanAssyRigging.Lifted);   // 吊物懸空（天車才寫吊物底面）
                    float drop = dropLo.HasValue && dropHi.HasValue ? (dropLo.Value + dropHi.Value) / 2f : dropLo ?? dropHi.Value;
                    HoistTo(restBottomY + drop);   // 試吊離座、放到定位銷／絕緣子上方
                }
                if (rest.Count > 0) CourseFlowTests.Write(s, string.Join("&&", rest));
                return rest.Count < Parts(condition).Count() || dropLo.HasValue || dropHi.HasValue;
            }

            public void Dispose()
            {
                SimClock.Source = oldClock;
                Sensors.Unregister(pc);
                UnityEngine.Object.DestroyImmediate(pcRoot);
                belt.Dispose(); crane.Dispose();
            }
        }

        class Flow
        {
            public readonly EventBus bus = new EventBus();
            public readonly SimState s;
            public readonly ProcedureRunner runner;
            public readonly ProcedureDefinition prc;
            readonly Mechanisms mech;
            public readonly List<string> autoCompleted = new List<string>();   // 啟用同一個 tick 就完成（違規）
            public readonly List<string> wouldAuto = new List<string>();       // 啟用時條件已成立（沒有 version 規則就會自動完成）
            public readonly HashSet<string> driven = new HashSet<string>();     // 走真的機制的小動作（步驟 小動作）

            public Flow(string course, params string[] faults)
            {
                prc = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(course));
                Assert.IsNotNull(prc, course + "：要先跑 Tools/Import Steps");
                Assert.IsTrue(prc.requireFreshWrite, course + "：匯入的課程要開 requireFreshWrite");
                s = new SimState(bus);
                mech = new Mechanisms(bus, s);   // 先接機制（開場狀態），再建流程
                PantographSim.Worksite.CourseApplier.ApplyInitialStates(AssetDatabase.LoadAssetAtPath<CourseDef>(StepsCsvImporter.CourseDefPath(course)), s);   // 課程的開場狀態（主斷路器、電動缸電源開著）
                foreach (var k in faults) { int eq = k.IndexOf('='); if (eq > 0) s.Set(k.Substring(0, eq), k.Substring(eq + 1)); else s.SetBool(k, true); }   // D43：教官打開的可選故障（v3.10：「鍵=值」預設故障參數）
                SlingLogic.Setup(s, new System.Random(1));   // v3.10：吊帶割傷故障（App 在開始訓練時呼叫的同一段）
                bus.Subscribe<StepStarted>(e => SlingLogic.OnStepStarted(s, e.stepId));
                runner = new ProcedureRunner(prc, bus, s);
            }

            public void Dispose() { runner.Dispose(); mech.Dispose(); }

            int Sub => (int)s.GetFloat("procedure.sub_index");

            /// <summary>跑到 stop(步驟, 小動作) 成立為止（null＝跑完整門課）。回傳是否停在 stop。</summary>
            public bool Run(System.Func<string, string, bool> stop = null)
            {
                runner.Start();
                for (int guard = 0; guard < 5000 && !runner.Finished; guard++)
                {
                    var st = runner.Current;
                    var subs = ProcedureRunner.SubActions(st);
                    int idx = Sub;
                    if (idx < subs.Length)
                    {
                        if (stop != null && stop(st.def.stepId, subs[idx].subId)) return true;
                        if (ChecklistEval.Done(s, subs[idx].condition)) wouldAuto.Add($"{st.RunId} {subs[idx].subId}：{subs[idx].condition}{(FreshWriteGate.EventKeys(subs[idx].condition).Any() ? "" : "　〔只讀 LiveState：照規則一出現就完成〕")}");
                        if (mech.Write(subs[idx].condition)) driven.Add(st.def.stepId + " " + subs[idx].subId);   // 一個 tick（工具拿放、天車、降弓走真的機制）
                        if (runner.Current == st && Sub == idx) Assert.Fail($"{st.RunId} {subs[idx].subId}：寫了條件鍵還是沒完成（{subs[idx].condition}；目前 {string.Join("、", RuleExplainer.KeysOf(subs[idx].condition).Select(k => k + "=" + s.Get(k)))}；提示 {s.Get("ui.toast")}）");
                        CheckNoAuto(st, idx);
                    }
                    else
                    {
                        s.Batch(() => Satisfy(st.def.completion, new EvalContext { state = s, side = st.side }));
                        if (runner.Current == st && !runner.Finished) Assert.Fail($"{st.RunId}：小動作都完成、完成條件也寫了，步驟還是沒完成");
                        CheckNoAuto(st, idx);
                    }
                }
                Assert.IsTrue(runner.Finished, "整門課要跑完");
                return false;
            }

            /// <summary>這一個 tick 之後：同一步最多前進一個；換到新的一步時，新步驟的第一個小動作不能已經完成。</summary>
            void CheckNoAuto(RuntimeStep before, int idx)
            {
                if (runner.Finished) return;
                var now = runner.Current;
                var subs = ProcedureRunner.SubActions(now);
                int first = now == before ? idx + 1 : 0;
                for (int i = first; i < System.Math.Min(Sub, subs.Length); i++)
                    if (!runner.Skipped(subs[i]) && !FreshWriteGate.Allowed(prc, now.def.stepId, subs[i].subId) && FreshWriteGate.EventKeys(subs[i].condition).Any())   // 只讀 LiveState 鍵的照規則可以一出現就完成
                        autoCompleted.Add($"{now.RunId} {subs[i].subId}：啟用的同一個 tick 就完成（鍵 {string.Join(",", RuleExplainer.KeysOf(subs[i].condition))}）");
            }
        }

        /// <summary>寫入讓條件成立的值（ChecklistEval 格式）。</summary>
        internal static void Write(ISimState s, string condition) => s.Batch(() =>
        {
            foreach (var part in condition.Split(new[] { "&&" }, System.StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()))
            {
                int ge = part.IndexOf(">="), le = part.IndexOf("<="), eq = part.IndexOf('=');
                int op = ge > 0 ? ge : le > 0 ? le : -1;
                if (op > 0)
                {
                    string key = part.Substring(0, op), rhs = part.Substring(op + 2);
                    if (!float.TryParse(rhs, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { if (!s.Has(rhs)) s.SetFloat(rhs, 1f); v = s.GetFloat(rhs); }
                    s.SetFloat(key, v);
                }
                else if (eq > 0) s.Set(part.Substring(0, eq), part.Substring(eq + 1));
                else s.SetBool(part, true);
            }
        });

        /// <summary>寫入讓步驟完成條件成立的值（依規則型別）。</summary>
        static void Satisfy(CompletionRule r, EvalContext c)
        {
            switch (r)
            {
                case AllOf a: foreach (var x in a.rules) Satisfy(x, c); break;
                case StateEquals se: c.state.Set(c.K(se.key), se.value); break;
                case ChecklistAll cl: foreach (var k in cl.keys) c.state.SetBool(c.K(k), true); break;
                case OrderedSequence os: c.state.Set(c.K(os.key), os.expected); break;
                case ValueInRange vr: foreach (var k in ValueInRangeEvaluator.Expand(vr.key, vr.count, c)) c.state.SetFloat(k, (vr.min + vr.max) / 2f); break;
                case Decision d: c.state.Set(c.K(d.choiceKey), DecisionEvaluator.Answer(d, c.state)); break;
                case ReadingMatches rm:
                    for (int i = 0; i < rm.recordKeys.Length; i++)
                    {
                        var read = c.K(rm.readingKeys[i]);
                        if (!c.state.Has(read)) c.state.SetFloat(read, 1f);
                        c.state.SetFloat(c.K(rm.recordKeys[i]), c.state.GetFloat(read));
                    }
                    break;
            }
        }

        /// <summary>D43：定期課打開可選故障「絕緣子髒汙」也能跑完：讀值 20 MΩ 勾不合格、走分支（放電拆線、回報、驗電後掛回 #1、上車頂擦 4 支、下車頂拆 #1、重量 500 MΩ 勾合格）。</summary>
        [Test]
        public void Periodic_FaultInsulatorDirty_RunsBranchToEnd()
        {
            var f = new Flow("periodic", MeggerLogic.FaultInsulatorDirty);
            try
            {
                Assert.IsTrue(f.Run((st, sub) => st == "STP_RT_02_InsulationTest" && sub == "c3"));
                Assert.AreEqual(ToolRulesDef.Load().meggerFaultReadingMOhm, f.s.GetFloat(MeggerLogic.Reading), "故障開啟：20 MΩ");
            }
            finally { f.Dispose(); }
            var g = new Flow("periodic", MeggerLogic.FaultInsulatorDirty);
            try
            {
                g.Run();
                Assert.IsEmpty(g.autoCompleted, string.Join("\n", g.autoCompleted));
                for (int n = 1; n <= 4; n++) Assert.GreaterOrEqual(g.s.GetFloat(MeggerLogic.InsulatorId(n) + ".clean_ratio"), ToolRulesDef.Load().insulatorCleanRatio, "絕緣子 " + n + " 擦淨");
                Assert.AreEqual("pass", g.s.Get("workorder.ir_judgement"), "重量後勾合格");
                Assert.AreEqual(ToolRulesDef.Load().meggerReadingMOhm, g.s.GetFloat(MeggerLogic.Reading));
            }
            finally { g.Dispose(); }
        }

        /// <summary>v3.10：定期課打開「吊帶割傷」，在 RM（SI_06 平台查看）或 IN（IN_01 台架旁查看）各跑一次到結算：只做抽到那一條的分支（s1–s4），割傷吊帶停用進停用箱、備用吊帶接手，正確操作不記吊帶類錯誤。</summary>
        [TestCase("RM", 2)]
        [TestCase("IN", 3)]
        public void Periodic_FaultSlingCut_RunsBranchToSettlement(string where, int index)
        {
            var f = new Flow("periodic", SlingLogic.Fault, SlingLogic.Where + "=" + where, SlingLogic.Index + "=" + index);
            var det = new ErrorDetector(f.runner, f.bus, f.s);
            var scoring = new PantographSim.Training.Scoring(f.bus, f.s);
            var errors = new List<ErrorDetected>(); SessionResult? got = null;
            f.bus.Subscribe<ErrorDetected>(e => errors.Add(e)); f.bus.Subscribe<SessionResult>(r => got = r);
            try
            {
                Assert.AreEqual(where == "RM", f.s.GetBool(SlingLogic.CutKey(index)), "RM 開場就割傷，IN 到 IN_01 才有");
                scoring.Start(0f);
                f.Run();
                Assert.IsEmpty(f.autoCompleted, string.Join("\n", f.autoCompleted));
                Assert.IsTrue(got.HasValue, "要發結算");
                string step = where == "RM" ? "STP_SI_06_RoofEntry" : "STP_IN_01_RigOnStand", other = where == "RM" ? "STP_IN_01_RigOnStand" : "STP_SI_06_RoofEntry";
                foreach (var sub in new[] { "s1i" + index, "s4i" + index }) Assert.IsTrue(f.driven.Contains(step + " " + sub), $"{step} {sub} 要走 SlingLogic");
                Assert.IsFalse(f.driven.Any(x => x.StartsWith(other + " s")), "另一段的分支跳過");
                Assert.IsFalse(f.driven.Any(x => x.StartsWith(step + " s1i") && x != step + " s1i" + index), "只做抽到的那一條");
                Assert.AreEqual(SlingLogic.InBin, f.s.Get(SlingLogic.Rejected + ".state"));
                Assert.IsTrue(f.s.GetBool(SlingLogic.ReplacedKey(index)));
                Assert.IsFalse(f.s.GetBool(SlingLogic.CutInUse), "割傷吊帶不在吊梁上");
                Assert.IsEmpty(errors.Where(e => e.errorId.StartsWith("ERR_Sling_")).Select(e => e.errorId).ToList(), "正確操作不記吊帶類錯誤");
            }
            finally { scoring.Dispose(); det.Dispose(); f.Dispose(); }
        }

        [TestCase("periodic")]
        [TestCase("daily")]
        public void Course_RunsToEnd_NoSubActionCompletesOnActivation(string course)
        {
            var f = new Flow(course);
            try
            {
                f.Run();
                Assert.IsEmpty(f.autoCompleted, string.Join("\n", f.autoCompleted));
                // 第 5 部分：這幾個先前只是腳本沒模擬機制連帶的變化，現在要走真的機制（不加例外）
                if (course == "periodic")
                    foreach (var sub in new[] { "STP_IN_03_Transfer a", "STP_IN_07_BaseTorque a", "STP_RT_02_InsulationTest f", "STP_WC_05_RaiseTest d" })
                        Assert.IsTrue(f.driven.Contains(sub), sub + " 要走真的機制");
                System.IO.Directory.CreateDirectory("Logs");
                System.IO.File.WriteAllText($"Logs/CourseFlow_{course}.txt",
                    $"{course}：{f.runner.Finished}，啟用時條件已成立、靠 version 規則擋下（沒有這條規則就會自動完成）的小動作 {f.wouldAuto.Count} 個：\n" + string.Join("\n", f.wouldAuto) + "\n");
            }
            finally { f.Dispose(); }
        }

        /// <summary>R1.5b b4（5a）：兩門課從頭跑到結算（ErrorDetector＋Scoring），結算畫面要的分數、Critical／Major／Minor 數都產生出來。</summary>
        [TestCase("periodic")]
        [TestCase("daily")]
        public void Course_RunsToSettlement_ScoreAndCountsProduced(string course)
        {
            var f = new Flow(course);
            var det = new ErrorDetector(f.runner, f.bus, f.s);
            var scoring = new PantographSim.Training.Scoring(f.bus, f.s);
            SessionResult? got = null;
            f.bus.Subscribe<SessionResult>(r => got = r);
            try
            {
                scoring.Start(0f);
                f.Run();
                Assert.IsTrue(got.HasValue, "要發結算（SessionResult）");
                var result = got.Value;
                Assert.IsTrue(f.s.GetBool("score.finished"));
                Assert.AreEqual(f.s.GetFloat("score.total"), result.score);
                Assert.AreEqual(System.Math.Max(0f, 100f - 2f * result.minor - 5f * result.major - 15f * result.critical), result.score, 1e-4f);
                System.IO.File.AppendAllText($"Logs/CourseFlow_{course}.txt",
                    $"結算：分數 {result.score}、Critical {result.critical}、Major {result.major}、Minor {result.minor}、通過 {result.passed}\n");
            }
            finally { scoring.Dispose(); det.Dispose(); f.Dispose(); }
        }

        /// <summary>反例：在 SI_04 b 做過自檢後，IN_04b b 啟用時 selftest 已經是 true，不能自動完成；要再點一次自檢器。</summary>
        [Test]
        public void IN04b_SelfTest_NeedsNewSelfTest()
        {
            AssertNeedsAction("STP_IN_04b_RegroundPan", "b", "tool.voltage_detector.selftest");
        }

        /// <summary>反例：LF_02 b 查看過，IN_02 b 啟用時 inspected 已經是 true，不能自動完成。</summary>
        [Test]
        public void IN02_Inspect_NeedsNewInspection()
        {
            AssertNeedsAction("STP_IN_02_TrialLift", "b", "part.pan_assy.inspected");
        }

        /// <summary>D31：RM_07 c 把掛鉤收到胸前扣環後 harness.platform＝false，IN_05 b（扣平台掛點）啟用時不成立，要真的扣上。</summary>
        [Test]
        public void IN05b_AfterStowingHooks_PlatformNotHooked()
        {
            AssertNotHeldAtActivation("STP_IN_05_RoofEntry", "b", "harness.platform");
        }

        /// <summary>RM_00 把短接地線夾上車體接地座後就不在身上（carried＝false），WC_03a c（掛回工具環）啟用時不成立，要真的掛回去。</summary>
        [Test]
        public void WC03a_c_AfterClampingLead_NotCarried()
        {
            AssertNotHeldAtActivation("STP_WC_03a_RemoveGroundRoof", "c", GroundingLeadLogic.Carried);
        }

        static void AssertNotHeldAtActivation(string stepId, string subId, string key)
        {
            var f = new Flow("periodic");
            try
            {
                Assert.IsTrue(f.Run((st, sub) => st == stepId && sub == subId), $"要跑到 {stepId} {subId}");
                Assert.IsFalse(f.s.GetBool(key), $"{stepId} {subId} 啟用時 {key} 不能已經成立");
            }
            finally { f.Dispose(); }
        }

        static void AssertNeedsAction(string stepId, string subId, string actionKey)
        {
            var f = new Flow("periodic");
            try
            {
                Assert.IsTrue(f.Run((st, sub) => st == stepId && sub == subId), $"要跑到 {stepId} {subId}");
                var cond = ProcedureRunner.SubActions(f.runner.Current)[(int)f.s.GetFloat("procedure.sub_index")].condition;
                Assert.IsTrue(ChecklistEval.Done(f.s, cond), $"前提：{stepId} {subId} 啟用時條件已經成立（{cond}）");
                f.s.Set("test.unrelated", "1");   // 別的鍵變動：照樣判定一次
                Assert.AreEqual(stepId, f.runner.Current.def.stepId);
                Assert.AreEqual(subId, ProcedureRunner.SubActions(f.runner.Current)[(int)f.s.GetFloat("procedure.sub_index")].subId, "沒有再做動作就不能完成");
                f.s.SetBool(actionKey, true);   // 再做一次（值本來就是 true，也要寫入）
                Assert.IsTrue(f.runner.Current.def.stepId != stepId || ProcedureRunner.SubActions(f.runner.Current)[(int)f.s.GetFloat("procedure.sub_index")].subId != subId, "再做一次就完成（比對步驟＋小動作：下一步的第一個可能接著完成）");
            }
            finally { f.Dispose(); }
        }

        [Test]
        public void SimState_VersionCountsEveryWrite_SameValueToo_DiscardedBatchNot()
        {
            var s = new SimState(new EventBus());
            Assert.AreEqual(0, s.Version("k"));
            s.SetBool("k", true); s.SetBool("k", true);
            Assert.AreEqual(2, s.Version("k"), "值相同也要加");
            using (s.BeginBatch()) { s.Set("k", "x"); s.Set("k", "y"); }
            Assert.AreEqual(3, s.Version("k"), "一批只算一次生效");
            var old = SimState.ErrorLog; SimState.ErrorLog = _ => { };
            try { Assert.Throws<System.Exception>(() => s.Batch(() => { s.Set("k", "z"); throw new System.Exception("放棄"); })); }
            finally { SimState.ErrorLog = old; }
            Assert.AreEqual(3, s.Version("k"), "放棄的批次不算");
            Assert.AreEqual("y", s.Get("k"));
        }

        [Test]
        public void AutoCompleteAllowed_IsEmpty()
        {
            foreach (var c in StepsCsvImporter.Courses)
                Assert.IsEmpty(AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(c)).autoCompleteAllowed, c + "：例外清單目前應該是空的（要加先回報）");
        }
    }
}
