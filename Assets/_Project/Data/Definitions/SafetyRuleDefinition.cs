using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>安全規則（SR_*）。</summary>
    [CreateAssetMenu(menuName = "PantographSim/Safety Rule")]
    public class SafetyRuleDefinition : ScriptableObject
    {
        public string ruleId;
        public string errorId;
        public SafetyRuleKind kind;
        [TextArea] public string what;
        [TextArea] public string why;
        [TextArea] public string fix;
        [Tooltip("NotLowered：降弓位置容許誤差（cm），檢測點距離 ≤ 此值才算降弓到位（示例值）")] public float toleranceCm = 1f;
    }
}
