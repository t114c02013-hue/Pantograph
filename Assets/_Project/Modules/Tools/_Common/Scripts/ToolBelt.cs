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
        ToolBase active;
        bool bound;

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
                }
                return;
            }
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
                bool canReturnInUse = tool.ToolId != SimIds.ToolGroundingRod || (!s.GetBool(SimIds.HeadClamped) && !s.GetBool(SimIds.TerminalClamped));
                var msg = RackLogic.TryReturn(s, slot, tool.ToolId, canReturnInUse);
                if (msg != null) Toast(msg);
                else { if (active == tool) Activate(null); Toast($"已把「{tool.DisplayName}」放回工具架"); }
                return;
            }
            var err = RackLogic.TryPick(s, slot, tool.ToolId, tool.DisplayName, active != null ? active.DisplayName : "");
            if (err != null) { Toast(err); return; }
            Activate(tool);
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
            if (!PointerState.TypingInUi)
                for (int k = 1; k <= 9; k++)
                    if (Input.GetKeyDown(KeyCode.Alpha0 + k) || Input.GetKeyDown(KeyCode.Keypad0 + k)) Hint(k);
            if (!PointerState.TypingInUi && Input.GetKeyDown(KeyCode.Backspace)) ResetActive();
            PointerState.ScrollCaptured = active != null && active.CapturesScroll && !PointerState.OverUi;
            if (active != null) { var t = active; Sim.State.Batch(t.Tick); }   // 每幀的工具操作（滾輪、按住）也一批生效
        }
    }
}
