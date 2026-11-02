using System;

namespace PantographSim.Tools
{
    /// <summary>工具的數值邏輯（純函式，方便測試）。數值皆為示例值，可在 TL_ 參數調整。</summary>
    public static class CaliperLogic
    {
        public const float ResolutionMm = 0.01f;
        public const float NoiseMm = 0.02f;
        public const float UnzeroedOffsetMm = 0.30f;

        /// <summary>讀數＝真實值＋雜訊（±0.02）＋未歸零偏移（+0.30），四捨五入到 0.01 mm。</summary>
        public static float Reading(float trueMm, bool zeroed, Random rng, float noise = NoiseMm, float offset = UnzeroedOffsetMm)
        {
            float n = rng == null ? 0f : (float)(rng.NextDouble() * 2 - 1) * noise;
            float v = trueMm + n + (zeroed ? 0f : offset);
            return (float)Math.Round(v / ResolutionMm) * ResolutionMm;
        }

        public static string Display(float mm) => mm.ToString("0.00");
    }

    public static class TorqueLogic
    {
        public const float RisePerSecond = 8f;      // 按住時每秒 +8 N·m
        public const float Min = 5f, Max = 50f, Step = 0.5f;
        public const float SnugTorque = 5f;          // 貼合時的起始扭力

        public static float Adjust(float setting, float delta) => Clamp((float)Math.Round((setting + delta) / Step) * Step);
        public static float Clamp(float v) => Math.Max(Min, Math.Min(Max, v));
        public static float Hold(float current, float dt) => current + RisePerSecond * dt;
        public static bool JustReached(float before, float after, float setting) => before < setting && after >= setting;
    }

    /// <summary>
    /// 重設工具：只把工具自己的暫存操作狀態（設定值、方向、套筒、量爪、按住中的扭力）回到剛拿起時；
    /// 卡尺會變回「未歸零」；已經成為結果的狀態（驗電器已自檢、零件上的螺栓狀態、已記錄的錯誤）一律不動。
    /// </summary>
    public static class ToolResetLogic
    {
        public static void Reset(PantographSim.Core.ISimState s, string toolId) => s.Batch(() => ResetInBatch(s, toolId));   // 一批

        static void ResetInBatch(PantographSim.Core.ISimState s, string toolId)
        {
            switch (toolId)
            {
                case "tool.caliper":
                    // 重設卡尺＝回到剛拿起：量爪張開、未歸零（要重新合攏、按 ZERO）
                    s.SetFloat("tool.caliper.jaw", 25f);
                    s.SetBool("tool.caliper.zeroed", false);
                    s.Set("tool.caliper.snapped", "");
                    s.Set("tool.caliper.message", "");
                    s.Set("tool.caliper.display", CaliperLogic.Display(25f + CaliperLogic.UnzeroedOffsetMm) + " mm");
                    break;
                case "tool.torque_wrench":
                    s.SetFloat("tool.torque_wrench.setting", 20f);
                    s.SetFloat("tool.torque_wrench.current", 0f);
                    s.SetBool("tool.torque_wrench.reached", false);
                    s.Set("tool.torque_wrench.target", "");
                    s.Set("tool.torque_wrench.message", "");
                    break;
                case "tool.socket_wrench":
                    s.Set("tool.socket_wrench.size", "10");
                    s.Set("tool.socket_wrench.dir", "loosen");
                    s.Set("tool.socket_wrench.engaged", "");
                    s.Set("tool.socket_wrench.message", "");
                    break;
                case "tool.grounding_rod":
                    s.Set("tool.grounding_rod.message", "");
                    break;
            }
        }
    }

    public static class SocketLogic
    {
        public static readonly string[] Sizes = { "10", "13", "17" };
        public const float DegreesPerNotch = 45f;
        public const float DegreesPerPixel = 1.5f;
    }
}
