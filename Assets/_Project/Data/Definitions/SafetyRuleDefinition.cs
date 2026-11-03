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
        [Tooltip("R1.5b b3：嚴重度（預設 Critical；工具未收回工具袋是 Major）")] public PantographSim.Core.Severity severity = PantographSim.Core.Severity.Critical;
        [TextArea] public string what;
        [TextArea] public string why;
        [TextArea] public string fix;
        [Tooltip("NotLowered：降弓位置容許誤差（cm），檢測點距離 ≤ 此值才算降弓到位（示例值）")] public float toleranceCm = 1f;
        [Tooltip("R1.5b b4：已由新的錯誤規則取代（ID 不刪，舊錄影照樣讀得到）。載入課程時不判，R1 的 25 步照舊判")] public bool deprecated;
        [Tooltip("取代它的錯誤 ID")] public string replacedBy = "";
    }
}
