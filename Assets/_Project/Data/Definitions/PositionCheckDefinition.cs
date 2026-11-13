using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// 位置檢測（PC_*）：零件上的檢測點要到達指定目標點，距離在容許誤差內才算到位。
    /// 命名規則（步驟計畫 v4）：互動點 ip.&lt;零件&gt;.&lt;用途&gt;_check／_target，黑板 part.&lt;零件&gt;.&lt;用途&gt;_distance_cm 與 _delta_cm（到位與否由規則判定）。
    /// 例：弓頭頂面中心 → 降弓停放位置（零件 pan_head、用途 lower），容許 1 cm（示例值）。
    /// 檢測點與目標點座標由場景建置工具量測後寫回（checkPointLocal 相對零件，targetLocal 相對整組機構根節點）。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/Position Check")]
    public class PositionCheckDefinition : ScriptableObject
    {
        public string checkId = "PC_PanHeadLower";
        public string part = "pan_head";
        public string purpose = "lower";
        public string targetName = "降弓位置";
        [Tooltip("容許誤差（cm），示例值")] public float toleranceCm = 1f;
        [Tooltip("檢測點：相對零件（例 PanHead）的位置")] public Vector3 checkPointLocal;
        [Tooltip("目標點：相對整組機構根節點的位置")] public Vector3 targetLocal;
        [Tooltip("terry 2026-11-13：檢查點和目標框只在教學模式、目前小動作的目標含其中一個 ID（逗號分隔）時顯示（空＝一律顯示）")] public string showWhenSubTarget = "";

        public string DistanceKey => $"part.{part}.{purpose}_distance_cm";
        /// <summary>帶正負號的垂直差（cm）：檢測點比目標高為正（要往下）。</summary>
        public string DeltaKey => $"part.{part}.{purpose}_delta_cm";
        /// <summary>容許值（cm）：由規則的擁有者寫入（降弓＝SR_Global_NotLowered）；沒有時顯示用 toleranceCm。</summary>
        public string ToleranceKey => $"part.{part}.{purpose}_tolerance_cm";
        public string CheckMarkerId => $"ip.{part}.{purpose}_check";
        public string TargetMarkerId => $"ip.{part}.{purpose}_target";
    }

    /// <summary>位置判定（純函式）。</summary>
    public static class PositionCheckLogic
    {
        public static float DistanceCm(Vector3 point, Vector3 target) => Vector3.Distance(point, target) * 100f;
        public static bool InPosition(float distanceCm, float toleranceCm) => distanceCm <= toleranceCm + 1e-4f;
        public static float VerticalDeltaCm(Vector3 point, Vector3 target) => (point.y - target.y) * 100f;
        /// <summary>檢查點、目標框要不要畫（判定照常算，只是不畫）。</summary>
        public static bool MarkerVisible(string trainingMode, string subTarget, string showWhenSubTarget) =>
            string.IsNullOrEmpty(showWhenSubTarget) || (trainingMode == "tutorial" && !string.IsNullOrEmpty(subTarget) && System.Array.Exists(showWhenSubTarget.Split(','), id => id.Length > 0 && subTarget.Contains(id.Trim())));
    }
}
