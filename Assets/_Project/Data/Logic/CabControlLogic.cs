using PantographSim.Core;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4：駕駛室控制盤的開關（SI_02 b、SI_03、WC_04 d）。主斷路器 cab.breaker、電動缸電源 cab.cylinder_power 是 on／off；
    /// 開場狀態由課程的 CourseDef.initialStates 寫（Courses.json），R1 的 25 步沒有這些鍵、照舊。純函式，控制盤元件與測試共用；回傳 null＝成功，否則是提示。
    /// </summary>
    public static class CabControlLogic
    {
        public const string Breaker = "cab.breaker", CylinderPower = "cab.cylinder_power", RaiseTestDone = "cab.raise_test_done";
        public const string BreakerId = "ip.cab.breaker", CylinderPowerId = "ip.cab.cylinder_power", RaiseButtonId = "ip.cab.raise_button";
        public const string On = "on", Off = "off";

        static bool Locked(ISimState s, string what) => s.Get("lockout." + what) == "locked";

        /// <summary>主斷路器切換。上鎖掛牌時不能切。</summary>
        public static string ToggleBreaker(ISimState s)
        {
            if (Locked(s, "breaker")) return "主斷路器已上鎖掛牌，不能切換";
            s.Set(Breaker, s.Get(Breaker, On) == On ? Off : On);
            return null;
        }

        /// <summary>電動缸電源切換。要恢復（ON）時，主斷路器、升弓開關的鎖都要先解除。</summary>
        public static string ToggleCylinderPower(ISimState s)
        {
            bool turnOn = s.Get(CylinderPower, On) == Off;
            if (turnOn && (Locked(s, "breaker") || Locked(s, "raise_switch"))) return "還有鎖沒解除，不能恢復電動缸電源";
            s.Set(CylinderPower, turnOn ? On : Off);
            return null;
        }

        /// <summary>主斷路器上鎖前要先切到 OFF（課程才有 cab.breaker；R1 沒有這個鍵，照舊可以鎖）。</summary>
        public static string CanLockBreaker(ISimState s) => s.Get(Breaker) == On ? "先把主斷路器切到 OFF 再上鎖" : null;

        // terry 175B（2026-11-15）：日常課程外接電源箱的供電開關 shore.switch（開場 on），上鎖掛牌鍵 lockout.shore_switch
        public const string ShoreSwitch = "shore.switch", ShoreSwitchId = "ip.shore_box.switch";

        /// <summary>供電開關切換（比照主斷路器）：上鎖掛牌時不能切。</summary>
        public static string ToggleShore(ISimState s)
        {
            if (Locked(s, "shore_switch")) return "供電開關已上鎖掛牌，不能切換";
            s.Set(ShoreSwitch, s.Get(ShoreSwitch, On) == On ? Off : On);
            return null;
        }

        /// <summary>供電開關上鎖前要先切到 OFF。</summary>
        public static string CanLockShore(ISimState s) => s.Get(ShoreSwitch, On) == On ? "先把供電開關切到 OFF 再上鎖" : null;

        /// <summary>按升弓鈕（SI_03 b）：電動缸電源已關＝弓不動，記確認完成；電源還開著＝送升弓指令（弓會升起來）。</summary>
        public static void PressRaise(ISimState s)
        {
            if (s.Get(CylinderPower) == Off) s.SetBool(RaiseTestDone, true);
            else LiftControl.Send(s, "Up");
        }
    }
}
