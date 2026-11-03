using PantographSim.Core;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4（D31）：安全帶全程穿著，掛鉤三種狀態——扣在車頂防墜器（harness.roof）、扣在平台掛點（harness.platform）、收在胸前扣環（harness.hooks_stowed）。
    /// 胸前扣環是 tool.safety_harness 的可互動點（RM_07 c、IN_05 a、WC_02 c，站點 tp.platform.anchor）。純函式，工具與測試共用；每個動作一批寫入。
    /// </summary>
    public static class HarnessLogic
    {
        public const string Roof = "harness.roof", Platform = "harness.platform", Stowed = BodyRings.HooksStowed, Count = "harness.hooks_count";

        /// <summary>解開掛鉤收到胸前扣環（在平台）。還扣在車頂防墜器上不能收（車頂上要一直扣著）。回傳 null＝成功，否則是提示。</summary>
        public static string StowHooks(ISimState s)
        {
            if (s.GetBool(Roof)) return "還扣在車頂防墜器上，先回到平台";
            if (s.GetBool(Stowed)) return null;
            s.Batch(() =>
            {
                s.SetBool(Platform, false);
                s.SetFloat(Count, 0);
                s.SetBool(Stowed, true);
            });
            return null;
        }

        /// <summary>從胸前扣環取下掛鉤，準備扣上掛點。</summary>
        public static void TakeHooks(ISimState s) { if (s.GetBool(Stowed)) s.SetBool(Stowed, false); }

        /// <summary>掛鉤收在胸前時不能直接扣掛點（要先取下）。</summary>
        public static string CanHook(ISimState s) => s.GetBool(Stowed) ? "掛鉤收在胸前扣環，先取下掛鉤" : null;
    }
}
