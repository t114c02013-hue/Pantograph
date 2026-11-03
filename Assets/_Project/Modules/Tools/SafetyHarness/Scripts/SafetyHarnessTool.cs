using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Tools
{
    /// <summary>
    /// 安全帶（簡化）：點掛點切換扣環；改扣時任何時刻至少一個扣著，否則記 harness.unhooked。
    /// R1.5b b4（D31）：胸前扣環（介面指令 stow＝解開掛鉤收到扣環、take＝取下掛鉤），收在胸前時不能直接扣掛點。邏輯在 HarnessLogic。
    /// </summary>
    public class SafetyHarnessTool : ToolBase
    {
        public override void OnClick(ActionRequested e)
        {
            if (!e.targetId.StartsWith("ip.anchor.")) return;
            var block = HarnessLogic.CanHook(State);
            if (block != null) { State.Set("ui.toast", block); return; }
            if (!Ask(e.targetId, "hook")) return;
            string key = "harness." + e.targetId.Substring(10);
            bool hooked = State.GetBool(key);
            // 一批：扣環狀態（也是車頂傳送點的解鎖鍵 harness.roof）、扣著的數量、是否曾全部解開同時生效
            State.Batch(() =>
            {
                if (hooked && State.Get("harness.hooks_count") == "1") State.SetBool("harness.unhooked", true);
                State.SetBool(key, !hooked);
                int count = (int)State.GetFloat("harness.hooks_count") + (hooked ? -1 : 1);
                State.SetFloat("harness.hooks_count", count);
                Perform(e.targetId, hooked ? "unhook" : "hook");
            });
        }

        public override void OnCommand(string command)
        {
            if (command == "stow")
            {
                if (!Ask(ToolId, "stow")) return;
                var msg = HarnessLogic.StowHooks(State);
                if (msg != null) State.Set("ui.toast", msg); else Perform(ToolId, "stow");
            }
            else if (command == "take") { HarnessLogic.TakeHooks(State); Perform(ToolId, "take"); }
        }
    }
}
