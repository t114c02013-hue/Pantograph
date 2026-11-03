using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Tools
{
    /// <summary>
    /// 上鎖掛牌組（簡化）：點主斷路器或升弓開關。鑰匙記在玩家身上。
    /// R1.5b b4：主斷路器要先切到 OFF 才能鎖（CabControlLogic）；WC_04 c 解除主斷路器的鎖後，掛牌組自動收回腰包（D30，WC_06 c 才歸還工具架）。
    /// v3.10（SI_02 c／c2、d／d2，組長）：上鎖、掛牌拆成兩個動作——手上用哪一件由 part 切換（lock＝鎖、tag＝牌，桌面工具列、VR 右搖桿左右）。
    /// 鎖：沒鎖就上鎖（lockout.&lt;x&gt;＝locked），已鎖就解鎖（連牌一起取下）。牌：已上鎖才掛（lockout.&lt;x&gt;.tagged）；
    /// 沒上鎖就掛牌——教學擋下並提示「先上鎖再掛牌」，練習／考核照掛並記 lockout.&lt;x&gt;.tag_without_lock（ERR_SI02_TagWithoutLock）。
    /// </summary>
    public class LockoutTagTool : ToolBase
    {
        public const string PartKey = "tool.lockout_tag.part", Lock = "lock", Tag = "tag";

        public override void OnCommand(string command)
        {
            if (command == "part:" + Lock || command == "part:" + Tag) State.Set(PartKey, command.Substring(5));
        }

        public override void OnReset() => State.Set(PartKey, Lock);

        public override void OnClick(ActionRequested e)
        {
            if (e.targetId != "ip.cab.breaker" && e.targetId != "ip.cab.raise_switch") return;
            if (!Ask(e.targetId, "lockout")) return;
            string key = "lockout." + e.targetId.Substring(7);
            bool locked = State.Get(key) == "locked";
            if (State.Get(PartKey, Lock) == Tag) { HangTag(key, locked); return; }
            var block = !locked && e.targetId == CabControlLogic.BreakerId ? CabControlLogic.CanLockBreaker(State) : null;
            if (block != null) { State.Set("ui.toast", block); return; }
            State.Batch(() =>   // 一批：上鎖狀態、牌、鑰匙持有者同時生效
            {
                State.Set(key, locked ? "open" : "locked");
                if (locked) State.SetBool(key + ".tagged", false);   // 解鎖連牌一起取下
                State.Set("lockout.key_holder", locked ? "" : "trainee");
                Perform(e.targetId, locked ? "unlock" : "lock");
                if (locked && e.targetId == CabControlLogic.BreakerId) RackLogic.StowToPouch(State, Hotkey, ToolId);   // 放開後收回腰包
            });
        }

        void HangTag(string key, bool locked)
        {
            if (!locked && State.Get("training.mode") == "tutorial") { State.Set("ui.toast", "先上鎖再掛牌"); return; }
            State.Batch(() =>
            {
                State.SetBool(key + ".tagged", true);
                if (!locked) State.SetBool(key + ".tag_without_lock", true);
                Perform("ip.cab." + key.Substring(8), "tag");
            });
        }
    }
}
