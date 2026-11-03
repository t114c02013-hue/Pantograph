using System.Collections.Generic;

namespace PantographSim.Core
{
    /// <summary>
    /// 連續量測的感測器（例：位置檢測 PositionCheck）。平常每幀依自己的門檻寫黑板；
    /// 動作需要「當下最新值」時（放開升降、滑板吸附），由動作在同一批寫入裡呼叫 WriteNow 立刻量一次並強制寫入。
    /// </summary>
    public interface IStateSensor
    {
        /// <summary>這個感測器寫的鍵的共同前綴，例 part.pan_head.lower。</summary>
        string KeyPrefix { get; }
        /// <summary>立刻量一次並寫入（force：值沒變也記成變更，讓同一批一定帶著這些鍵）。</summary>
        void WriteNow(ISimState s, bool force);
    }

    /// <summary>感測器登記處（Core 不知道感測器在哪個模組，只知道介面）。</summary>
    public static class Sensors
    {
        static readonly List<IStateSensor> all = new List<IStateSensor>();

        public static void Register(IStateSensor s) { if (s != null && !all.Contains(s)) all.Add(s); }
        public static void Unregister(IStateSensor s) => all.Remove(s);

        /// <summary>KeyPrefix 以 prefix 開頭的感測器立刻寫入最新值；回傳寫了幾個。</summary>
        public static int Flush(ISimState state, string prefix, bool force = true)
        {
            int n = 0;
            foreach (var s in all.ToArray())
                if (s.KeyPrefix != null && s.KeyPrefix.StartsWith(prefix)) { s.WriteNow(state, force); n++; }
            return n;
        }
    }

    /// <summary>
    /// 升降控制器指令（ctrl.lift.cmd）的唯一寫入口：介面按鈕、鍵盤、VR、驅動器都經過這裡。
    /// 放開（Stop）是一批：先強制寫入檢測點的最新距離（part.pan_head.lower_distance_cm、lower_delta_cm，不受每幀寫入門檻限制），
    /// 再寫 Stop——步驟判定（SI_01 的 a）讀到的一定是放開那一刻的距離。
    /// </summary>
    public static class LiftControl
    {
        public const string SensorPrefix = "part.pan_head.lower";

        public static void Send(ISimState s, string cmd)
        {
            if (s == null) return;
            if (cmd != "Stop") { s.Set(SimIds.LiftCmd, cmd); return; }
            Release(s);
        }

        /// <summary>車頂開放（有人可能在車頂作業）：由 RoofAccessRule 判定後寫在黑板，升降、傳送、活動欄都讀這個鍵。</summary>
        public const string RoofOpenKey = "roof.open";

        /// <summary>
        /// ▲ 上升被擋的原因（安全聯鎖）；null＝可以升。依序：車頂有人作業 → 升弓開關已上鎖（SI_02）→ 電動缸電源已關閉（SI_03）。
        /// 預設完成步驟的狀態要等流程走到那一步才寫入，所以 SI_02 之前 ▲ 可以用；WC_04 解鎖、恢復電源後又可以用。
        /// </summary>
        public static string UpBlockReason(ISimState s)
        {
            if (s == null) return null;
            if (s.GetBool(RoofOpenKey)) return "車頂有人作業，不能升弓";
            if (s.Get("lockout.raise_switch") == "locked") return "升弓開關已上鎖，不能升弓";
            if (s.Get("cab.cylinder_power") == "off") return "電動缸電源已關閉，不能升弓";
            if (s.Get("ip.pan_assy.hold_down.state") == "Installed") return "降弓固定帶未拆";   // R1.5b b4 3d（鐵道）：WC_05 升弓時的後備，ERR_IN11_StrapLeft
            return null;
        }

        public static bool UpLocked(ISimState s) => UpBlockReason(s) != null;
        public static void Release(ISimState s)
        {
            if (s == null || s.Get(SimIds.LiftCmd) == "Stop") return;   // 已經停了（例：滑鼠移出按鈕）不重複寫
            s.Batch(() =>
            {
                // 場景裡的位置檢測量當下距離；沒有感測器（測試、無場景）就把黑板上的值原樣強制寫一次
                if (Sensors.Flush(s, SensorPrefix) == 0)
                    foreach (var key in new[] { SimIds.PanHeadLowerDistance, SimIds.PanHeadLowerDelta })
                        if (s.Has(key)) s.Set(key, s.Get(key), true);
                s.Set(SimIds.LiftCmd, "Stop");
            });
        }
    }
}
