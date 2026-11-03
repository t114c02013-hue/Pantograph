using PantographSim.Core;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4：絕緣測試（RT_02）。高阻計 tool.megger 的兩條測試線接弓頭接地掛點、車體接地點（connected_count），按測試鍵（DC 1000 V）得到讀值
    /// （ToolRulesDef：有接地還掛著＝短路讀值，否則＝絕緣讀值，示例值），測完弓體帶電（charged）；接地桿碰弓頭接地掛點放電，接觸時間累計在 discharge_s，
    /// 到 ToolRulesDef.dischargeSeconds 才算放完。純函式，工具與測試共用；回傳 null＝成功，否則是提示。
    /// </summary>
    public static class MeggerLogic
    {
        public const string ToolId = "tool.megger", Count = "tool.megger.connected_count", Reading = "tool.megger.reading", Charged = "tool.megger.charged";
        public const string PanPoint = SimIds.GroundClamp, BodyPoint = "ip.roof.carbody_ground";
        public const string DischargeKey = PanPoint + ".discharge_s", ContactKey = PanPoint + ".discharge_contact", ContactSinceKey = ContactKey + "_since";
        static string LeadKey(string point) => "tool.megger.lead." + point;
        public static bool IsPoint(string targetId) => targetId == PanPoint || targetId == BodyPoint;

        /// <summary>測試線接上（或取下）一個點。</summary>
        public static void ToggleLead(ISimState s, string point) => s.Batch(() =>
        {
            bool on = !s.GetBool(LeadKey(point));
            s.SetBool(LeadKey(point), on);
            s.SetFloat(Count, (s.GetBool(LeadKey(PanPoint)) ? 1 : 0) + (s.GetBool(LeadKey(BodyPoint)) ? 1 : 0));
        });

        /// <summary>收起測試線（高阻計放下時）。</summary>
        public static void ClearLeads(ISimState s)
        {
            if (s.GetFloat(Count) <= 0) return;
            s.Batch(() => { s.SetBool(LeadKey(PanPoint), false); s.SetBool(LeadKey(BodyPoint), false); s.SetFloat(Count, 0); });
        }

        /// <summary>按測試鍵。</summary>
        public static string Test(ISimState s, ToolRulesDef rules)
        {
            if (s.GetFloat(Count) < 2) return "先把兩條測試線接上弓頭接地掛點和車體接地點";
            bool grounded = s.GetBool(SimIds.HeadClamped) || s.GetBool(SimIds.TerminalClamped);
            s.Batch(() =>
            {
                s.SetFloat(Reading, grounded ? rules.meggerGroundedReadingMOhm : rules.meggerReadingMOhm);
                s.SetBool(Charged, true);
                s.SetFloat(DischargeKey, 0);
            });
            return null;
        }

        /// <summary>接地桿碰弓頭接地掛點（或移開）；帶電或正在接觸時才算放電。回傳是否處理了（否＝照接地桿原本的掛接）。</summary>
        public static bool ToggleDischarge(ISimState s, float now)
        {
            if (!s.GetBool(Charged) && !s.GetBool(ContactKey)) return false;
            bool on = !s.GetBool(ContactKey);
            s.Batch(() => { s.SetBool(ContactKey, on); if (on) s.SetFloat(ContactSinceKey, now - s.GetFloat(DischargeKey)); });
            return true;
        }

        /// <summary>接觸中每幀：累計放電時間（0.1 s），到 dischargeSeconds 就放完。</summary>
        public static void Tick(ISimState s, float now, ToolRulesDef rules)
        {
            if (!s.GetBool(ContactKey)) return;
            float t = UnityEngine.Mathf.Floor((now - s.GetFloat(ContactSinceKey)) * 10f) / 10f;
            if (t != s.GetFloat(DischargeKey)) s.SetFloat(DischargeKey, t);
            if (t >= rules.dischargeSeconds && s.GetBool(Charged)) s.SetBool(Charged, false);
        }
    }
}
