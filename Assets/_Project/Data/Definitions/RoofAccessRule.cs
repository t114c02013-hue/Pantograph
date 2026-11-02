using System;
using UnityEngine;

namespace PantographSim.Data
{
    // R1：座標系＝原點在弓頭中心正下方的車頂地板點，+Y 上、+Z 朝肘側滑板、平台在 −X 側，單位 m；標 EX 的是示例值。

    /// <summary>
    /// 車頂開放規則（唯一來源：傳送、活動欄、升降提示都讀它）。open＝openCondition 成立且 closeCondition 不成立。
    /// 條件格式同 ChecklistEval（「鍵」「鍵=值」「&&」）。只擋進入、不擋離開（活動欄等學員回到平台才關）。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/R1/Roof Access Rule")]
    public class RoofAccessRule : ScriptableObject
    {
        [TextArea] public string openCondition = "step.STP_SI_04_VoltageTest.done && step.STP_SI_05_Grounding.done && harness.roof";
        [TextArea] public string closeCondition = "step.STP_WC_02_LeaveRoof.done";
        [TextArea] public string lockedMessage = "先完成驗電、掛接地，並扣好安全帶";
        [TextArea] public string leaveHookedMessage = "先解開車頂掛點";
        public string resultKey = "roof.open";
    }
}
