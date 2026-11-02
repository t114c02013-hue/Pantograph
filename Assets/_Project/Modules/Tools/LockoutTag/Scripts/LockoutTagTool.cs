using PantographSim.Core;

namespace PantographSim.Tools
{
    /// <summary>上鎖掛牌組（簡化）：點主斷路器或升弓開關，切換「已上鎖＋掛牌」；鑰匙記在玩家身上。</summary>
    public class LockoutTagTool : ToolBase
    {
        public override void OnClick(ActionRequested e)
        {
            if (e.targetId != "ip.cab.breaker" && e.targetId != "ip.cab.raise_switch") return;
            if (!Ask(e.targetId, "lockout")) return;
            string key = "lockout." + e.targetId.Substring(7);
            bool locked = State.Get(key) == "locked";
            State.Batch(() =>   // 一批：上鎖掛牌狀態與鑰匙持有者同時生效
            {
                State.Set(key, locked ? "open" : "locked");
                State.Set("lockout.key_holder", locked ? "" : "trainee");
                Perform(e.targetId, locked ? "unlock" : "lock");
            });
        }
    }
}
