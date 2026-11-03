using System;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>工具定義（TL_*）。</summary>
    [CreateAssetMenu(menuName = "PantographSim/Tool Definition")]
    public class ToolDefinition : ScriptableObject
    {
        public string toolId;
        public string displayName;
        [Tooltip("數字鍵 1–9")] public int hotkey;
        public ToolTutorialDefinition tutorial = new ToolTutorialDefinition();
        [Tooltip("參數 key=value，例 sockets=10,13,17")] public string[] parameters = Array.Empty<string>();

        [Header("a6 增補 7（D39，torque-wrench-sockets.md v1.3 §6.5、§7.3）：扳手與套筒；套筒數值在 SocketCatalog")]
        public string prefab;
        [Tooltip("方頭（吋）：0.375＝3/8\"、0.5＝1/2\"；0＝不是扳手")] public float driveIn;
        [Tooltip("扭力量程 N·m（最小, 最大）；0＝沒有量程")] public Vector2 rangeNm;
        [Tooltip("把手中心到 Socket_Mount 的距離（m），第 21 項的扇形半徑")] public float handleLengthM;
        public string socketMountNode;
        public string defaultSocketId;
        [Tooltip("可以裝的套筒（SocketCatalog 的 socketId）")] public string[] socketIds = Array.Empty<string>();
        [Tooltip("目前裝的套筒的黑板鍵（例 tool.torque_wrench.socket）")] public string socketStateKey;
        public ToolCalibration calibration = new ToolCalibration();
        [Tooltip("R1.5b b3（K3）：到扭力時的喀聲；空白＝內建短音效")] public AudioClip clickClip;

        public string Param(string name, string fallback = "")
        {
            foreach (var p in parameters)
            {
                int i = p.IndexOf('=');
                if (i > 0 && p.Substring(0, i) == name) return p.Substring(i + 1);
            }
            return fallback;
        }
    }

    /// <summary>校正標籤（§6.3）：在 Unity 用共用標籤底圖加文字，hover 資訊卡顯示同樣內容；過期目前不判定。</summary>
    [Serializable]
    public class ToolCalibration
    {
        public string labelId, dueDate, source;
        public bool IsSet => !string.IsNullOrEmpty(labelId) && !string.IsNullOrEmpty(dueDate);
    }
}
