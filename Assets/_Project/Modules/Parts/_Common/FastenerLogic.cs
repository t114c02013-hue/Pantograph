using System.Globalization;
using System.Linq;
using PantographSim.Core;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// 螺栓狀態機（純邏輯，方便測試）：Tight→Loosened→Out→InTray→Placed→Snug→Torqued。
    /// 黑板鍵（B＝ip.carbon_strip_L.bolt_1）：B.state、B.angle_deg（鬆開累計角度）、B.lock_deg（鎖緊累計角度）、
    /// B.torque_Nm、B.torque_setting、B.marked、B.wrong_socket、B.overtight_first。
    /// 同一條滑板的順序：ip.carbon_strip_L.loosen_order / pretighten_order / torque_order / mark_order（例 "1,2,3,4"）。
    /// </summary>
    public static class FastenerLogic
    {
        public const string Tight = "Tight", Loosened = "Loosened", Out = "Out", InTray = "InTray", Placed = "Placed", Snug = "Snug", Torqued = "Torqued";
        public const float LoosenedAt = 150f, OutAt = 720f, SnugAt = 360f, OvertightAt = 540f;
        public const string CorrectSocket = "13";
        /// <summary>R1.5b b4：集電弓底座、高壓端子螺栓收進工具袋的零件袋（碳滑板螺栓照舊進該側螺栓盤）。</summary>
        public const string BoltPouch = "env.tool_bag.bolt_pouch", BoltPouchCount = BoltPouch + ".count";

        public static string StripPrefix(string boltId) => boltId.Substring(0, boltId.LastIndexOf('.'));
        public static string Number(string boltId) => boltId.Substring(boltId.LastIndexOf('_') + 1);

        public static void Initialize(ISimState s, string boltId)
        {
            if (!s.Has(boltId + ".state")) s.Set(boltId + ".state", Tight);
        }

        /// <summary>
        /// 處理工具動作。text：rotate:&lt;套筒&gt;:&lt;loosen|lock&gt;、torque:&lt;設定值&gt;、mark、take、place。回傳是否改變了狀態。
        /// 一批寫入：state、angle_deg、torque_Nm、marked 與順序鍵同時生效（不會出現扭力已到、狀態還不是 Torqued 的中間狀態）。
        /// </summary>
        public static bool Apply(ISimState s, IEventBus bus, string boltId, string text, float value) => Apply(s, bus, boltId, text, value, CorrectSocket);

        /// <summary>R1.5b b4：correctSocket＝這支螺栓要用的套筒（SocketCatalog，建置時寫進 FastenerBehaviour；底座 24、高壓端子 19、碳滑板 13）。</summary>
        public static bool Apply(ISimState s, IEventBus bus, string boltId, string text, float value, string correctSocket) =>
            s.BatchResult(() => ApplyInBatch(s, bus, boltId, text, value, correctSocket));

        static bool ApplyInBatch(ISimState s, IEventBus bus, string boltId, string text, float value, string correctSocket)
        {
            var parts = (text ?? "").Split(':');
            string st = s.Get(boltId + ".state", Tight);
            switch (parts[0])
            {
                case "rotate":
                {
                    string size = parts.Length > 1 ? parts[1] : "";
                    string dir = parts.Length > 2 ? parts[2] : "loosen";
                    if (size != correctSocket) { s.SetBool(boltId + ".wrong_socket", true); return false; }
                    float deg = System.Math.Abs(value);
                    bool tight = st == Tight || (st == Torqued && !IsStripBolt(boltId));   // R1.5b b4：集電弓底座、高壓端子螺栓開場是已上扭力，拆的時候照鎖緊的鬆
                    if (dir == "loosen" && (tight || st == Loosened || st == Out))
                    {
                        float a = s.GetFloat(boltId + ".angle_deg") + deg;
                        s.SetFloat(boltId + ".angle_deg", a);
                        if (a >= OutAt) s.Set(boltId + ".state", Out);
                        else if (a >= LoosenedAt && tight) { s.Set(boltId + ".state", Loosened); AppendOrder(s, boltId, "loosen_order"); }
                        return true;
                    }
                    if (dir == "lock" && (st == Placed || st == Snug))
                    {
                        if (!IsStripBolt(boltId) && !OthersAtLeast(s, boltId, Placed) && !s.GetBool(boltId + ".tighten_before_all")) s.SetBool(boltId + ".tighten_before_all", true);   // R1.5b b4（IN_06）：四支還沒都放回就開始鎖
                        float a = s.GetFloat(boltId + ".lock_deg") + deg;
                        s.SetFloat(boltId + ".lock_deg", a);
                        if (a >= SnugAt && st == Placed) { s.Set(boltId + ".state", Snug); AppendOrder(s, boltId, "pretighten_order"); }
                        if (a > OvertightAt && !OthersAtLeast(s, boltId, Snug)) s.SetBool(boltId + ".overtight_first", true);
                        return true;
                    }
                    return false;
                }
                case "torque":
                {
                    if (st != Snug && st != Torqued) return false;
                    float.TryParse(parts.Length > 1 ? parts[1] : "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var setting);
                    s.SetFloat(boltId + ".torque_setting", setting);
                    s.SetFloat(boltId + ".torque_Nm", value);
                    if (st == Snug) { s.Set(boltId + ".state", Torqued); AppendOrder(s, boltId, "torque_order"); }
                    return true;
                }
                case "mark":
                    if (st != Torqued || s.GetBool(boltId + ".marked")) return false;
                    s.SetBool(boltId + ".marked", true);
                    AppendOrder(s, boltId, "mark_order");
                    return true;
                case "take":
                    if (st != Out) return false;
                    s.Set(boltId + ".state", InTray);
                    bus?.Publish(new ActionPerformed { targetId = TrayFor(s, boltId), text = "add:bolt", pointId = boltId, time = SimClock.Now });   // 放進該側的螺栓盤
                    return true;
                case "place":
                    if (st != InTray || s.GetFloat(IsStripBolt(boltId) ? "tray.bolts" : BoltPouchCount) < 1) return false;
                    s.Set(boltId + ".state", Placed);
                    s.SetFloat(boltId + ".lock_deg", 0);
                    bus?.Publish(new ActionPerformed { targetId = TrayFor(s, boltId), text = "remove:bolt", pointId = boltId, time = SimClock.Now });   // 從該側的螺栓盤拿
                    return true;
            }
            return false;
        }

        static bool IsStripBolt(string boltId) => boltId.StartsWith("ip.carbon_strip_");

        // 碳滑板螺栓進該側螺栓盤（BoltTray.OnBound 會寫 env.bolt_tray_<側>.bolts）；場景缺螺栓盤時記 Error，不改送零件盒。集電弓的螺栓進工具袋的零件袋
        static string TrayFor(ISimState s, string boltId)
        {
            if (!IsStripBolt(boltId)) return BoltPouch;
            string tray = SimIds.BoltTray(SimIds.SideOf(boltId));
            if (!s.Has(tray + ".bolts")) UnityEngine.Debug.LogError($"[FastenerLogic] 場景沒有螺栓盤 {tray}，{boltId} 無法計數");
            return tray;
        }

        static void AppendOrder(ISimState s, string boltId, string orderName)
        {
            string key = StripPrefix(boltId) + "." + orderName;
            string cur = s.Get(key);
            s.Set(key, string.IsNullOrEmpty(cur) ? Number(boltId) : cur + "," + Number(boltId));
        }

        static readonly string[] Rank = { Tight, Loosened, Out, InTray, Placed, Snug, Torqued };

        static bool OthersAtLeast(ISimState s, string boltId, string minState)
        {
            string prefix = StripPrefix(boltId);
            int min = System.Array.IndexOf(Rank, minState);
            return Enumerable.Range(1, 4).Select(n => prefix + ".bolt_" + n).Where(b => b != boltId)
                .All(b => System.Array.IndexOf(Rank, s.Get(b + ".state", Tight)) >= min);
        }
    }
}
