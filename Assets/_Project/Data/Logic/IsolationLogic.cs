using PantographSim.Core;

namespace PantographSim.Data
{
    /// <summary>
    /// 驗電與接地的狀態邏輯（純函式；工具與測試共用）。守門（順序是否允許）由 SafetyGuard 負責。
    /// 每個動作一批寫入：例如掛上弓頭時 head_clamped、grounded 與順序紀錄同時生效，安全規則與步驟判定不會讀到中間狀態。
    /// </summary>
    public static class IsolationLogic
    {
        public static void SelfTest(ISimState s, float now) => s.Batch(() =>
        {
            s.SetBool("tool.voltage_detector.selftest", true);
            s.SetFloat("tool.voltage_detector.selftest_time", now);
            s.Set("tool.voltage_detector.led", "green");
            s.Set("tool.voltage_detector.display", "自檢正常（綠燈）");
        });

        /// <summary>量弓頭接地掛點；回傳是否算驗電完成（有自檢才算）。</summary>
        public static bool MeasureHead(ISimState s) => s.BatchResult(() =>
        {
            bool tested = s.GetBool("tool.voltage_detector.selftest");
            s.SetBool("isolation.voltage_measured", true);
            if (tested) { s.SetBool(SimIds.VoltageTested, true); s.Set("tool.voltage_detector.display", "無電"); }
            else s.Set("tool.voltage_detector.display", "未自檢，結果不可信");
            return tested;
        });

        /// <summary>點平台端子：沒夾就夾上、夾著就拆下。回傳動作文字。</summary>
        public static string ToggleTerminal(ISimState s) => s.BatchResult(() =>
        {
            bool clamped = s.GetBool(SimIds.TerminalClamped);
            s.SetBool(SimIds.TerminalClamped, !clamped);
            if (clamped) { s.SetBool(SimIds.PullTested, false); Append(s, "isolation.removal_order", "terminal"); return "unclamp"; }
            Append(s, "isolation.ground_order", "terminal");
            return "clamp";
        });

        public static bool PullTest(ISimState s) => s.BatchResult(() =>
        {
            if (!s.GetBool(SimIds.TerminalClamped)) return false;
            s.SetBool(SimIds.PullTested, true);
            Append(s, "isolation.ground_order", "pull");
            return true;
        });

        /// <summary>點弓頭接地掛點：沒掛就掛上（沒拉線確認會記錄）、掛著就取下。</summary>
        public static string ToggleHead(ISimState s) => s.BatchResult(() =>
        {
            if (!s.GetBool(SimIds.HeadClamped))
            {
                if (!s.GetBool(SimIds.PullTested)) s.SetBool("isolation.clamped_without_pulltest", true);
                s.SetBool(SimIds.HeadClamped, true);
                s.SetBool(SimIds.Grounded, s.GetBool(SimIds.TerminalClamped));
                Append(s, "isolation.ground_order", "head");
                return "hang";
            }
            s.SetBool(SimIds.HeadClamped, false);
            s.SetBool(SimIds.Grounded, false);
            Append(s, "isolation.removal_order", "head");
            return "unhang";
        });

        static void Append(ISimState s, string key, string item)
        {
            var cur = s.Get(key);
            s.Set(key, string.IsNullOrEmpty(cur) ? item : cur + "," + item);
        }
    }
}
