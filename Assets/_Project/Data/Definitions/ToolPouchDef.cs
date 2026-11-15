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
        [Tooltip("教學模式：這一步要拿的工具在手離它這個距離內就算拿到（格子只隔 6 cm，低頭也不好對準）")] public float targetRadius = 0.30f;
        public string notStartedHint = "請先在任務卡選擇課程並開始";
        [Tooltip("教學模式：這一步要拿工具袋裡的工具時的提示，{0}＝工具名稱")] public string takeHint = "手伸到工具袋，按握把拿「{0}」";

        public static ToolPouchDef Load()
        {
            var d = Resources.Load<ToolPouchDef>(ResourceName);
            return d != null ? d : CreateInstance<ToolPouchDef>();
        }
    }
}
