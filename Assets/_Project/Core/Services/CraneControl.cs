namespace PantographSim.Core
{
    /// <summary>
    /// R1.5b b1：天車遙控器 env.crane_pendant 的唯一寫入口（VR 的遙控器、桌面的按鍵面板都經過這裡），只寫 ctrl.crane.* 與遙控器所在位置，
    /// 不碰 CraneMechanism（設計原則 1：和 ctrl.lift.cmd 同一個模式）。
    /// </summary>
    public static class CraneControl
    {
        public const string Docked = "Docked", Held = "Held";

        /// <summary>目前按住的方向命令（None／Up／Down／E／W／N／S，已由 CraneLogic.Resolve 處理同時按兩個）。</summary>
        public static void Command(ISimState s, string cmd)
        {
            if (s == null) return;
            if (s.Get(SimIds.CraneCmd) != cmd) s.Set(SimIds.CraneCmd, cmd);
        }

        /// <summary>
        /// 高速鍵 Btn_Speed2。
        /// shortcut: 規格是「和方向鍵一起按住」，但 VR 只有右手一個 poke／射線、桌面只有一個滑鼠，同時按兩顆做不到，所以改成按一下切換高速／微動, 換成雙鍵輸入（或左手拇指）時改回按住
        /// </summary>
        public static void ToggleFast(ISimState s) { if (s != null) s.SetBool(SimIds.CraneFast, !s.GetBool(SimIds.CraneFast)); }

        /// <summary>緊急停止：沒鎖時按下＝鎖住；鎖住時再按＝旋轉復歸（之後要重轉電源鑰匙，CraneMechanism 判定）。按下時方向命令一律清掉。</summary>
        public static void PressEStop(ISimState s)
        {
            if (s == null) return;
            s.Batch(() => { s.SetBool(SimIds.CraneEStop, !s.GetBool(SimIds.CraneEStop)); s.Set(SimIds.CraneCmd, "None"); });
        }

        /// <summary>電源鑰匙 Key_Power：轉一下切換開／關。</summary>
        public static void TurnKey(ISimState s)
        {
            if (s == null) return;
            s.Batch(() => { s.SetBool(SimIds.CranePower, !s.GetBool(SimIds.CranePower)); s.Set(SimIds.CraneCmd, "None"); });
        }

        /// <summary>拿起／掛回遙控器；掛回時放開所有按鍵。</summary>
        public static void SetPendant(ISimState s, bool held)
        {
            if (s == null) return;
            s.Batch(() => { s.Set(SimIds.CranePendantState, held ? Held : Docked); if (!held) s.Set(SimIds.CraneCmd, "None"); });
        }

        public static bool PendantHeld(ISimState s) => s != null && s.Get(SimIds.CranePendantState) == Held;
    }
}
