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
    }
}
