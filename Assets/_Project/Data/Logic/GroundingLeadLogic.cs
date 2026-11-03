using PantographSim.Core;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4：攜帶式短接地線 tool.grounding_lead（slot_12，#2 接地）。一端夾車體接地座 ip.roof.carbody_ground，一端用短絕緣桿掛電纜端子 ip.roof.hv_cable_ground。
    /// 「隨身」＝掛在安全帶工具環上（tool.grounding_lead.carried，手空著，掛位 12 使用中）；拿下來用＝手上（in_hand）；兩端都接上、拉線確認後＝留在場上（使用中、手空出來）。
    /// 拆除順序（先電纜端子、後車體接地座）由 ErrorRules.json 的 ERR_WC03_RemoveOrder 在動作前擋下，這裡不判。純函式，工具與測試共用；每個動作一批寫入。
    /// </summary>
    public static class GroundingLeadLogic
    {
        public const string ToolId = BodyRings.ToolGroundingLead, Carried = BodyRings.LeadCarried;
        public const string CarBody = "ip.roof.carbody_ground", Cable = "ip.roof.hv_cable_ground";
        public static string ClampedKey(string point) => point + ".clamped";
        public static string PullTestedKey => Cable + ".pull_tested";
        public static bool IsPoint(string targetId) => targetId == CarBody || targetId == Cable;

        /// <summary>有任何一端還接著。</summary>
        public static bool Installed(ISimState s) => s.GetBool(ClampedKey(CarBody)) || s.GetBool(ClampedKey(Cable));

        /// <summary>手上的短接地線掛到安全帶工具環（隨身）。還接著不能掛。</summary>
        public static string HangOnRing(ISimState s, int slot)
        {
            if (s.Get(SimIds.ActiveTool) != ToolId) return "手上沒有拿著短接地線";
            if (Installed(s)) return "短接地線還接著，先拆下";
            s.Batch(() => { RackLogic.Deploy(s, slot); s.SetBool(Carried, true); });
            return null;
        }

        /// <summary>從工具環（或接著的位置）拿到手上。手要空著。</summary>
        public static string Take(ISimState s, int slot)
        {
            if (s.Get(SimIds.ActiveTool) == ToolId) return null;
            if (!string.IsNullOrEmpty(s.Get(SimIds.ActiveTool)) || !string.IsNullOrEmpty(s.Get("hand.item"))) return "手上已經拿著東西";
            if (s.Get(RackLogic.SlotKey(slot)) != RackLogic.InUse) return "短接地線不在身上";
            s.Batch(() =>
            {
                s.SetBool(Carried, false);
                s.SetBool("tools.all_returned", false);
                s.Set(RackLogic.SlotKey(slot), RackLogic.InHand);
                s.Set(SimIds.ActiveTool, ToolId);
            });
            return null;
        }

        /// <summary>點車體接地座或電纜端子：沒接就接上、接著就取下（手上要拿著短接地線）。回傳動作文字。</summary>
        public static string Toggle(ISimState s, string point) => s.BatchResult(() =>
        {
            bool on = !s.GetBool(ClampedKey(point));
            s.SetBool(ClampedKey(point), on);
            s.SetBool(Carried, false);
            if (!on && point == Cable) s.SetBool(PullTestedKey, false);
            return on ? "clamp" : "unclamp";
        });

        /// <summary>拉線確認（電纜端子掛上後，手上還拿著短絕緣桿）；確認後兩端都接著就留在場上、手空出來。</summary>
        public static string PullTest(ISimState s, int slot)
        {
            if (!s.GetBool(ClampedKey(Cable))) return "請先把短接地線掛上電纜端子";
            s.Batch(() =>
            {
                s.SetBool(PullTestedKey, true);
                if (s.GetBool(ClampedKey(CarBody)) && s.Get(SimIds.ActiveTool) == ToolId) RackLogic.Deploy(s, slot);
            });
            return null;
        }
    }
}
