using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>升降控制器（千斤頂式）的參數，皆為示例值。</summary>
    [CreateAssetMenu(menuName = "PantographSim/Lift Settings")]
    public class LiftSettings : ScriptableObject
    {
        [Tooltip("從降到底升到頂所需秒數")] public float fullTravelSeconds = 15f;
        [Tooltip("從靜止加速到全速（或減速到停）所需秒數")] public float accelSeconds = 0.6f;
        [Tooltip("下降時距目標多少 cm 內自動減速")] public float slowZoneCm = 20f;
        [Tooltip("減速區內的速度（cm/s）")] public float slowSpeedCmPerSec = 5f;
        [Tooltip("檢測點全程行程（cm），由場景建置工具量測寫回")] public float travelCm = 183f;
    }

    /// <summary>升降的數值積分（純函式）：按住時平滑加速到等速，放開平滑減速停在當下高度。value 0＝降到底、1＝升到頂。</summary>
    public static class LiftLogic
    {
        /// <param name="input">+1 上升、−1 下降、0 放開</param>
        /// <param name="distanceToTargetCm">下降時距目標的距離；在減速區內改用慢速</param>
        public static void Step(ref float value, ref float velocity, int input, float dt, LiftSettingsValues s, float distanceToTargetCm = float.MaxValue)
        {
            float fullSpeed = 1f / Mathf.Max(0.1f, s.fullTravelSeconds);
            float maxSpeed = fullSpeed;
            if (input < 0 && distanceToTargetCm < s.slowZoneCm && s.travelCm > 0) maxSpeed = Mathf.Min(fullSpeed, s.slowSpeedCmPerSec / s.travelCm);
            float accel = fullSpeed / Mathf.Max(0.01f, s.accelSeconds);
            velocity = Mathf.MoveTowards(velocity, input * maxSpeed, accel * dt);
            if (input < 0 && velocity < -maxSpeed) velocity = -maxSpeed;   // 進入減速區時立刻限速
            value += velocity * dt;
            if (value <= 0f) { value = 0f; if (velocity < 0) velocity = 0f; }
            if (value >= 1f) { value = 1f; if (velocity > 0) velocity = 0f; }
        }
    }

    /// <summary>LiftSettings 的數值複本（方便測試，不需建立資產）。</summary>
    public struct LiftSettingsValues
    {
        public float fullTravelSeconds, accelSeconds, slowZoneCm, slowSpeedCmPerSec, travelCm;
        public static LiftSettingsValues From(LiftSettings s) => s == null ? Default : new LiftSettingsValues
        {
            fullTravelSeconds = s.fullTravelSeconds, accelSeconds = s.accelSeconds, slowZoneCm = s.slowZoneCm, slowSpeedCmPerSec = s.slowSpeedCmPerSec, travelCm = s.travelCm
        };
        public static LiftSettingsValues Default => new LiftSettingsValues { fullTravelSeconds = 15f, accelSeconds = 0.6f, slowZoneCm = 20f, slowSpeedCmPerSec = 5f, travelCm = 183f };
    }
}
