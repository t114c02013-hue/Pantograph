using PantographSim.Core;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4：可拆零件的兩個附加動作（DetachablePart 呼叫；純函式，元件與測試共用）。
    /// ・拉線確認（RM_01 c）：用工具拆裝的零件（降弓固定帶）裝著時空手點＝拉一下確認收緊（&lt;id&gt;.pull_tested），狀態不變。
    /// ・防塵蓋（RM_02 b、IN_10 a，控制線接頭）：拔下後點一下＝蓋上（&lt;id&gt;.cap＝on），蓋著點＝取下（off）；這次拔下已經蓋過、取下後再點＝插回。
    /// </summary>
    public static class DetachableLogic
    {
        public const string On = "on", Off = "off";
        public static string CapKey(string id) => id + ".cap";
        static string CapUsedKey(string id) => id + ".cap_used";

        public static void PullTest(ISimState s, string id) => s.SetBool(id + ".pull_tested", true);

        /// <summary>拔下狀態下點一下：處理防塵蓋就回傳 true；false＝要插回。</summary>
        public static bool CapClick(ISimState s, string id)
        {
            if (s.Get(CapKey(id)) == On) { s.Set(CapKey(id), Off); return true; }
            if (s.GetBool(CapUsedKey(id))) return false;
            s.Batch(() => { s.Set(CapKey(id), On); s.SetBool(CapUsedKey(id), true); });
            return true;
        }

        /// <summary>插回後重新開始（下次拔下又要先蓋）。</summary>
        public static void ResetCap(ISimState s, string id) { if (s.GetBool(CapUsedKey(id))) s.SetBool(CapUsedKey(id), false); }
    }
}
