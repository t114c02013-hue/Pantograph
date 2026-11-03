using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Tools
{
    /// <summary>
    /// 上鎖掛牌組（簡化）：點主斷路器或升弓開關，切換「已上鎖＋掛牌」；鑰匙記在玩家身上。
    /// R1.5b b4：主斷路器要先切到 OFF 才能鎖（CabControlLogic）；WC_04 c 解除主斷路器的鎖後，掛牌組自動收回腰包（D30，WC_06 c 才歸還工具架）。
    /// </summary>
    public class LockoutTagTool : ToolBase
    {
        public override void OnClick(ActionRequested e)
        {
            if (e.targetId != "ip.cab.breaker" && e.targetId != "ip.cab.raise_switch") return;
            if (!Ask(e.targetId, "lockout")) return;
            string key = "lockout." + e.targetId.Substring(7);
            bool locked = State.Get(key) == "locked";
            var block = !locked && e.targetId == CabControlLogic.BreakerId ? CabControlLogic.CanLockBreaker(State) : null;
            if (block != null) { State.Set("ui.toast", block); return; }
            State.Batch(() =>   // 一批：上鎖掛牌狀態與鑰匙持有者同時生效
            {
                State.Set(key, locked ? "open" : "locked");
                State.Set("lockout.key_holder", locked ? "" : "trainee");
                Perform(e.targetId, locked ? "unlock" : "lock");
                if (locked && e.targetId == CabControlLogic.BreakerId) RackLogic.StowToPouch(State, Hotkey, ToolId);   // 放開後收回腰包
            });
        }
    }
}
