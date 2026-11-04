using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>VR 試玩修正 第 4 點：身上工具袋的判定與回饋（Resources/ToolPouchDef）。</summary>
    public class ToolPouchDef : ScriptableObject
    {
        public const string ResourceName = "ToolPouchDef";

        [Tooltip("每一格的判定半徑（m）：高亮、標籤、拿取、放回都用這一個")] public float slotRadius = 0.12f;
        [Tooltip("指到格子的輕震")] public float hoverAmplitude = 0.2f, hoverSeconds = 0.05f;
        [Tooltip("拿出、放回工具的震動")] public float grabAmplitude = 0.5f, grabSeconds = 0.08f;
        [Tooltip("轉身超過這個角度（度）工具袋才跟過來，平常不晃；跟過來的速度（度／秒）")] public float followAngleDeg = 60f, followSpeedDeg = 240f;
        public string notStartedHint = "請先在任務卡選擇課程並開始";

        public static ToolPouchDef Load()
        {
            var d = Resources.Load<ToolPouchDef>(ResourceName);
            return d != null ? d : CreateInstance<ToolPouchDef>();
        }
    }
}
