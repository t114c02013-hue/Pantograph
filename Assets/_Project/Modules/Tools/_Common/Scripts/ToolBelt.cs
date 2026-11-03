using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Tools
{
    /// <summary>
    /// 手上的工具（步驟計畫 v5）：只能到工具架點掛位拿起／歸還，手上一次一件；
    /// 數字鍵 1–9 只高亮對應掛位（ui.rack_hint），不直接切換工具。
    /// 指令（ActionRequested 目標 tool.belt）：rack:&lt;k&gt; 點掛位、hint:&lt;k&gt; 高亮掛位、deploy 工具留在場上（接地棒）。
    /// 黑板：tool.active、rack.slot_&lt;k&gt;、tools.all_returned；場景點擊轉給手上的工具。
    /// </summary>
    public class ToolBelt : MonoBehaviour
    {
        readonly List<ToolBase> tools = new List<ToolBase>();
        [Tooltip("R1.5b b3：掛位表（homeStorageId、作業期間在腰包）")] [SerializeField] ToolSlotTable slotTable;
        [Tooltip("R1.5b b3：車頂放開工具沒收回工具袋（ERR_Tool_NotStowedOnRoof，Major）")] [SerializeField] SafetyRuleDefinition notStowedRule;

        public void Setup(ToolSlotTable table, SafetyRuleDefinition notStowed) { slotTable = table; notStowedRule = notStowed; }
        ToolBase active;
        bool bound;
        /// <summary>R1.5b b4：放開握把的規則（Resources/ToolRules；測試可換）。</summary>
        public ToolRulesDef Rules { get { if (!rulesLoaded) { rules = ToolRulesDef.Load(); rulesLoaded = true; } return rules; } set { rules = value; rulesLoaded = true; } }
        ToolRulesDef rules; bool rulesLoaded;   // Resources.Load 不能在 MonoBehaviour 的欄位初始值裡呼叫
        readonly ToolReleaseLogic releaseLogic = new ToolReleaseLogic();

        public IReadOnlyList<ToolBase> Tools => tools;
        public ToolBase Active => active;

        void Awake() => tools.AddRange(GetComponentsInChildren<ToolBase>(true).OrderBy(t => t.Hotkey));

        void Start() => Bind();

        void Bind()
        {
            if (bound || !Sim.Ready) return;
            Sim.Bus.Subscribe<ActionRequested>(OnRequest);
            Sim.Bus.Subscribe<StateChanged>(OnState);
            Sim.Bus.Subscribe<ToolPickRequested>(OnPick);
            Sim.Bus.Subscribe<ToolReturnRequested>(OnReturn);
            bound = true;
            var s = Sim.State;
            s.Set(SimIds.ActiveTool, "");
            s.SetBool("tools.all_returned", true);
            foreach (var t in tools) RackLogic.Initialize(s, t.Hotkey);
            s.Set("tools.list", string.Join(";", tools.Select(t => $"{t.Hotkey}|{t.ToolId}|{t.DisplayName}")));
            // 一句用途（工具教學的「用途」頁），給資訊卡顯示
            foreach (var t in tools)
            {
                var page = t.Definition?.tutorial?.pages?.FirstOrDefault(p => p.title == "用途");
                if (page != null) s.Set("tools.purpose." + t.ToolId, page.body);
            }
        }

        void OnDestroy()
        {
            if (!bound || Sim.Bus == null) return;
            Sim.Bus.Unsubscribe<ActionRequested>(OnRequest);
            Sim.Bus.Unsubscribe<StateChanged>(OnState);
            Sim.Bus.Unsubscribe<ToolPickRequested>(OnPick);
            Sim.Bus.Unsubscribe<ToolReturnRequested>(OnReturn);
        }

        // 工具架掛位（Worksite）發出的事件；兩者都走 ClickSlot，由目前手上與掛位狀態決定拿或還
        void OnPick(ToolPickRequested e) => ClickSlot(Mathf.RoundToInt(e.value));
        void OnReturn(ToolReturnRequested e) => ClickSlot(Mathf.RoundToInt(e.value));

        ToolBase BySlot(int slot) => tools.FirstOrDefault(t => t.Hotkey == slot);
        ToolBase ById(string id) => tools.FirstOrDefault(t => t.ToolId == id);

        void OnRequest(ActionRequested e)
        {
            if (e.targetId == "tool.belt")
            {
                var parts = (e.text ?? "").Split(':');
                int.TryParse(parts.Length > 1 ? parts[1] : "", out int slot);
                switch (parts[0])
                {
                    case "rack": ClickSlot(slot); break;
                    case "hint": Hint(slot); break;
                    case "deploy": if (active != null) { var t = active; Sim.State.Batch(() => { Activate(null); RackLogic.Deploy(Sim.State, t.Hotkey); }); } break;
                    case "put_away": if (active != null) ClickSlot(active.Hotkey); break;
                    case "reset": ResetActive(); break;
                    case "release": Release(parts.Length > 1 && parts[1] == "roof"); break;
                }
                return;
            }
            if (e.targetId == ToolSlotTable.WaistPouch && string.IsNullOrEmpty(e.toolId) || e.targetId == ToolSlotTable.WaistPouch && active != null && e.toolId == active.ToolId) { Sim.State.Batch(Pouch); return; }
            if (e.targetId != null && e.targetId.StartsWith("tool."))
            {
                // 工具列按鈕或舊的「select」：只高亮掛位；其他是工具自己的介面指令
                if (e.text == "select") { var t = ById(e.targetId); if (t != null) { if (t == active) ResetActive(); else Hint(t.Hotkey); } }
                else { var t = ById(e.targetId); if (t != null) Sim.State.Batch(() => t.OnCommand(e.text)); }
                return;
            }
            // 工具的一次動作＝一批：工具自己的鍵、零件因 ActionPerformed 寫的鍵（螺栓、接地、量測讀數…）一起生效
            if (active != null && e.toolId == active.ToolId) { var t = active; Sim.State.Batch(() => t.OnClick(e)); }
        }

        void OnState(StateChanged e)
        {
            // 狀態被還原（從這一步重做）時，手上的工具跟著黑板
            if (!e.TryGet(SimIds.ActiveTool, out var c)) return;
            var want = ById(c.newValue ?? "");
            if (want != active) Activate(want);
        }

        /// <summary>點工具架掛位：手上拿著這件 → 歸還；手是空的 → 拿起；拿著別件 → 提示先放回。</summary>
        public void ClickSlot(int slot)
        {
            if (BySlot(slot) == null) return;
            // 一批：tool.active、掛位狀態、tools.all_returned（與提示）同時生效
            Sim.State.Batch(() => ClickSlotInBatch(slot));
        }

        void ClickSlotInBatch(int slot)
        {
            var tool = BySlot(slot);
            var s = Sim.State;
            if (active == tool || s.Get(RackLogic.SlotKey(slot)) == RackLogic.InUse)
            {
                bool canReturnInUse = tool.ToolId == SimIds.ToolGroundingRod ? !s.GetBool(SimIds.HeadClamped) && !s.GetBool(SimIds.TerminalClamped)
                    : tool.ToolId != GroundingLeadLogic.ToolId || !GroundingLeadLogic.Installed(s);   // 短接地線還接在車頂上不能收回
                var msg = RackLogic.TryReturn(s, slot, tool.ToolId, canReturnInUse);
                if (msg == null && tool.ToolId == GroundingLeadLogic.ToolId) s.SetBool(GroundingLeadLogic.Carried, false);   // 掛回工具架就不在身上
                if (msg != null) Toast(msg);
                else { if (active == tool) Activate(null); Toast($"已把「{tool.DisplayName}」放回工具架"); }
                return;
            }
            if (s.Get(RackLogic.SlotKey(slot)) == RackLogic.InPouch)
            {
                // D30：WC_06 c 交接時把個人上鎖包從腰包掛回工具架（手要空著）
                if (active != null) { Toast($"手上已經拿著「{active.DisplayName}」，先把它放好"); return; }
                s.Set(RackLogic.SlotKey(slot), RackLogic.OnRack); s.SetBool("tools.all_returned", true);
                Toast($"已把「{tool.DisplayName}」從腰包掛回工具架"); return;
            }
            var err = RackLogic.TryPick(s, slot, tool.ToolId, tool.DisplayName, active != null ? active.DisplayName : "");
            if (err != null) { Toast(err); return; }
            releaseLogic.Regrab(tool.ToolId);   // 寬限內抓回不算
            if (slotTable != null && slotTable.Find(slot) is ToolSlotDef def && def.workInPouch)
            {
                RackLogic.StowToPouch(s, slot, tool.ToolId);   // D30：SI_00 f 領取個人上鎖包，直接掛在腰間
                Toast($"「{tool.DisplayName}」已放進腰包");
                return;
            }
            Activate(tool);
        }

        /// <summary>D30：點腰包＝手上的上鎖掛牌組收進腰包，或（手空著）從腰包拿出。</summary>
        void Pouch()
        {
            var s = Sim.State;
            if (slotTable == null) return;
            if (active != null)
            {
                var def = slotTable.Find(active.Hotkey);
                if (def == null || !def.workInPouch) { Toast($"「{active.DisplayName}」不放腰包"); return; }
                var t = active; RackLogic.StowToPouch(s, t.Hotkey, t.ToolId); Activate(null); Toast($"已把「{t.DisplayName}」收進腰包");
                return;
            }
            foreach (var def in slotTable.slots.Where(x => x != null && x.workInPouch))
            {
                var tool = BySlot(def.slot);
                if (tool != null && RackLogic.TakeFromPouch(s, def.slot, tool.ToolId) == null) { Activate(tool); return; }
            }
            Toast("腰包是空的");
        }

        /// <summary>
        /// 放開手上的工具、沒有收回存放處（防墜繫繩，crane-procedure 6.3；ToolRulesDef）：在車頂＝掛在繫繩上垂著，regrabGraceSec 內抓回不算，
        /// 超過時練習、考核記 Major ERR_Tool_NotStowedOnRoof（教學模式只提示，見 TickRelease）；在地面工作區＝落在地面或台架上，不記錯（WC_01 清點時要收回）。
        /// gripReleaseDrops=false：工具留在手上，只提示。
        /// </summary>
        public void Release(bool onRoof)
        {
            if (active == null) return;
            var t = active;
            if (!releaseLogic.Release(Rules, t.ToolId, onRoof, SimClock.Now)) { Toast(Rules.keepHint); return; }
            Sim.State.Batch(() =>
            {
                RackLogic.Release(Sim.State, t.Hotkey, onRoof);
                Activate(null);
                Toast(onRoof ? $"「{t.DisplayName}」{Rules?.hangingHint ?? "掛在防墜繫繩上垂著：用完要收回工具袋"}" : $"「{t.DisplayName}」放在地上了，清點時要收回");
            });
        }

        /// <summary>寬限到了：記錯（練習、考核）或只提示（教學）。Update 每幀呼叫；EditMode 測試直接呼叫。</summary>
        public void TickRelease(float now)
        {
            var errors = new List<string>(); var hints = new List<string>();
            releaseLogic.Due(Rules, now, Sim.State.Get("procedure.current"), Sim.State.Get("training.mode"), errors, hints);
            foreach (var id in hints) Toast($"「{ById(id)?.DisplayName ?? id}」{Rules?.hangingHint}");
            foreach (var id in errors)
                Sim.Bus.Publish(new ErrorDetected
                {
                    errorId = notStowedRule != null ? notStowedRule.errorId : "ERR_Tool_NotStowedOnRoof", severity = notStowedRule != null ? notStowedRule.severity : Severity.Major,
                    what = notStowedRule?.what ?? "", why = notStowedRule?.why ?? "", fix = notStowedRule?.fix ?? "",
                    stepId = Sim.State.Get("procedure.current"), toolId = id, targetId = id, time = now
                });
        }

        void Activate(ToolBase next)
        {
            if (next == active) return;
            if (active != null) { active.IsActive = false; active.OnDeselected(); }
            active = next;
            if (active != null) { active.IsActive = true; active.OnSelected(); }
            if (Sim.State.Get(SimIds.ActiveTool) != (active != null ? active.ToolId : "")) Sim.State.Set(SimIds.ActiveTool, active != null ? active.ToolId : "");
            Sim.Bus.Publish(new ToolSelected { toolId = active != null ? active.ToolId : "", text = active != null ? active.DisplayName : "徒手", time = SimClock.Now });
        }

        /// <summary>重設手上的工具（再點一次同一工具、↺ 按鈕、Backspace）。</summary>
        public void ResetActive()
        {
            if (active == null) return;
            Sim.State.Batch(() =>   // 工具的設定值一起重設（一批）
            {
                active.OnReset();
                Toast($"已重設「{active.DisplayName}」，可以重新操作");
            });
        }

        void Hint(int slot)
        {
            var tool = BySlot(slot);
            if (tool == null) return;
            if (tool == active) { ResetActive(); return; }
            Sim.State.Set("ui.rack_hint", RackLogic.SlotId(slot));
            Sim.State.SetFloat("ui.rack_hint_time", Time.time);
            Toast($"「{tool.DisplayName}」在工具架第 {slot} 格，點掛位拿起");
        }

        static void Toast(string text) { Sim.State.Set("ui.toast", text); Sim.State.SetFloat("ui.toast_time", Time.time); }

        void Update()
        {
            Bind();
            if (!bound || !Sim.Ready) return;   // 結束 Play／場景卸載時服務已清掉
            if (Sim.State.GetBool("training.paused")) { PointerState.ScrollCaptured = false; return; }
            TickRelease(SimClock.Now);
            if (!PointerState.TypingInUi)
                for (int k = 1; k <= 9; k++)
                    if (Input.GetKeyDown(KeyCode.Alpha0 + k) || Input.GetKeyDown(KeyCode.Keypad0 + k)) Hint(k);
            if (!PointerState.TypingInUi && Input.GetKeyDown(KeyCode.Backspace)) ResetActive();
            PointerState.ScrollCaptured = active != null && active.CapturesScroll && !PointerState.OverUi;
            if (active != null) { var t = active; Sim.State.Batch(t.Tick); }   // 每幀的工具操作（滾輪、按住）也一批生效
        }
    }
}
