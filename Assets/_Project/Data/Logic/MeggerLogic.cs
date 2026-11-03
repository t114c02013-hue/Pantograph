using System.Linq;
using PantographSim.Core;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4：絕緣測試（RT_02，D43）。數值都在 ToolRulesDef。
    /// ・高阻計 tool.megger 的兩條測試線接弓頭接地掛點、車體接地點（connected_count），按測試鍵（DC 1000 V）：
    ///   接地還掛著＝讀值 0，三種模式都只提示 groundedHint、不記分、要重量（measured 不成立）；
    ///   可選故障 fault.insulator_dirty 開著、4 支絕緣子還沒擦淨＝meggerFaultReadingMOhm；否則＝meggerReadingMOhm。測完弓體帶電（charged）。
    /// ・接地桿碰弓頭接地掛點放電：接觸時間累計在 discharge_s，到 dischargeSeconds 才算放完（discharged）。
    /// ・測試線要空手拆（RT_02 e2）：還沒放完電就拆，教學擋下並提示剩幾秒，練習／考核照拆並記 tool.megger.lead_removed_charged（ERR_RT02_LeadRemovedBeforeDischarge）。
    /// 純函式，工具、場景元件與測試共用；回傳 null＝成功，否則是提示。
    /// </summary>
    public static class MeggerLogic
    {
        public const string ToolId = "tool.megger", Count = "tool.megger.connected_count", Reading = "tool.megger.reading", Charged = "tool.megger.charged";
        public const string Measured = "tool.megger.measured", Discharged = "tool.megger.discharged", LeadRemovedCharged = "tool.megger.lead_removed_charged";
        public const string PanPoint = SimIds.GroundClamp, BodyPoint = "ip.roof.carbody_ground";
        public const string DischargeKey = PanPoint + ".discharge_s", ContactKey = PanPoint + ".discharge_contact", ContactSinceKey = ContactKey + "_since";
        public const string FaultInsulatorDirty = "fault.insulator_dirty";
        public static string InsulatorId(int n) => "ip.roof.insulator_" + n;
        public static string LeadKey(string point) => "tool.megger.lead." + point;
        public static bool IsPoint(string targetId) => targetId == PanPoint || targetId == BodyPoint;
        static bool Tutorial(ISimState s) => s.Get("training.mode") == "tutorial";

        static void Recount(ISimState s) => s.SetFloat(Count, (s.GetBool(LeadKey(PanPoint)) ? 1 : 0) + (s.GetBool(LeadKey(BodyPoint)) ? 1 : 0));

        /// <summary>拿著高阻計點一個點：接上測試線（已接的不動；拆線要空手，見 RemoveLead）。</summary>
        public static void ConnectLead(ISimState s, string point)
        {
            if (s.GetBool(LeadKey(point))) return;
            s.Batch(() => { s.SetBool(LeadKey(point), true); Recount(s); });
        }

        /// <summary>空手拆一條測試線。回傳 null＝拆下；否則是提示（教學：還沒放完電）。</summary>
        public static string RemoveLead(ISimState s, string point, ToolRulesDef rules)
        {
            if (!s.GetBool(LeadKey(point))) return null;
            bool charged = s.GetBool(Charged);
            if (charged && Tutorial(s)) return $"放電還沒滿 {rules.dischargeSeconds:0} 秒（剩 {System.Math.Max(0f, rules.dischargeSeconds - s.GetFloat(DischargeKey)):0.0} 秒），先用接地桿放電再拆線";
            s.Batch(() =>
            {
                s.SetBool(LeadKey(point), false);
                Recount(s);
                if (charged) s.SetBool(LeadRemovedCharged, true);
            });
            return null;
        }

        /// <summary>4 支絕緣子都擦淨了（清除率 ≥ insulatorCleanRatio）。</summary>
        public static bool InsulatorsClean(ISimState s, ToolRulesDef rules) => Enumerable.Range(1, 4).All(n => s.GetFloat(InsulatorId(n) + ".clean_ratio") >= rules.insulatorCleanRatio - 1e-4f);

        /// <summary>按測試鍵。</summary>
        public static string Test(ISimState s, ToolRulesDef rules)
        {
            if (s.GetFloat(Count) < 2) return "先把兩條測試線接上弓頭接地掛點和車體接地點";
            bool grounded = s.GetBool(SimIds.HeadClamped) || s.GetBool(SimIds.TerminalClamped) || s.GetBool("ip.roof.carbody_ground.clamped") || s.GetBool("ip.roof.hv_cable_ground.clamped");
            if (grounded)
            {
                s.Batch(() => { s.SetFloat(Reading, rules.meggerGroundedReadingMOhm); s.SetBool(Measured, false); });
                return rules.groundedHint;   // 三種模式都只提示，不記分
            }
            float reading = s.GetBool(FaultInsulatorDirty) && !InsulatorsClean(s, rules) ? rules.meggerFaultReadingMOhm : rules.meggerReadingMOhm;
            s.Batch(() =>
            {
                s.SetFloat(Reading, reading);
                s.SetBool(Measured, true);
                s.SetBool(Charged, true);
                s.SetBool(Discharged, false);
                s.SetFloat(DischargeKey, 0);
            });
            return null;
        }

        /// <summary>讀值合格（≥ irPassMinMOhm）。</summary>
        public static bool Pass(ISimState s, ToolRulesDef rules) => s.GetFloat(Reading) >= rules.irPassMinMOhm - 1e-4f;

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
            if (t >= rules.dischargeSeconds && s.GetBool(Charged)) s.Batch(() => { s.SetBool(Charged, false); s.SetBool(Discharged, true); });
        }

        /// <summary>教學模式的放電倒數（剩幾秒；不在放電或不是教學＝null）。練習／考核不顯示。</summary>
        public static float? TutorialCountdown(ISimState s, ToolRulesDef rules) =>
            Tutorial(s) && s.GetBool(Charged) ? System.Math.Max(0f, rules.dischargeSeconds - s.GetFloat(DischargeKey)) : (float?)null;
    }
}
