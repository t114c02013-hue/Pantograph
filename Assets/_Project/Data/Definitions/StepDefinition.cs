using System;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>一個步驟（STP_*）。</summary>
    [CreateAssetMenu(menuName = "PantographSim/Step Definition")]
    public class StepDefinition : ScriptableObject
    {
        public string stepId;
        public string title;
        [TextArea] public string instruction;
        public string toolHint;
        public string targetHint;
        public string[] targetIds = Array.Empty<string>();
        public string requiredToolId;
        [SerializeReference] public CompletionRule completion;
        public ErrorRule[] errors = Array.Empty<ErrorRule>();
        public HintSet hints = new HintSet();
        [Tooltip("小動作序列（教學模式一次顯示一個）")] public SubAction[] subActions = Array.Empty<SubAction>();
        public bool presetDone;
        [Tooltip("L / R / 空白")] public string side;
        [Tooltip("預設完成時寫入的狀態 key=value")] public string[] presetStates = Array.Empty<string>();
        [Tooltip("畫面數值與來源標籤，例 3.0 mm=保養表值")] public string[] valueTags = Array.Empty<string>();
        [Tooltip("參考時間（秒，示例值）：教學模式；重複區塊為單側秒數。報表用來標記超過 2 倍的步驟，0＝沒有資料")] public float refSecondsTutorial;
        [Tooltip("參考時間（秒，示例值）：考核模式；重複區塊為單側秒數")] public float refSecondsExam;
    }
}
