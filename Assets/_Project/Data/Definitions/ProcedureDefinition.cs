using System;
using System.Collections.Generic;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>一份程序（PRC_*）。</summary>
    [CreateAssetMenu(menuName = "PantographSim/Procedure Definition")]
    public class ProcedureDefinition : ScriptableObject
    {
        public string procedureId;
        public string title;
        [TextArea] public string goal;
        public string[] safetyPoints = Array.Empty<string>();
        public List<StepDefinition> steps = new List<StepDefinition>();
        public RepeatBlock repeatBlock = new RepeatBlock();
        public JumpRule[] onPass = Array.Empty<JumpRule>();
        [Tooltip("R1.5b b4（軟體工程裁定 2026-11-03）：小動作要條件成立，而且啟用後條件讀的鍵至少被寫過一次才算完成（R1 的 25 步沒開）")]
        public bool requireFreshWrite;
        [Tooltip("例外：啟用時條件已成立就可以直接完成的小動作（每筆都要寫原因；目前應該是空的）")]
        public AutoCompleteAllowedDef[] autoCompleteAllowed = Array.Empty<AutoCompleteAllowedDef>();
    }

    [Serializable]
    public class AutoCompleteAllowedDef { public string stepId, subId, why; }
}
