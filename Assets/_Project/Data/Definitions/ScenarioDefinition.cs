using System;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>情境（SCN_*）：初始讀數與起始狀態。</summary>
    [CreateAssetMenu(menuName = "PantographSim/Scenario Definition")]
    public class ScenarioDefinition : ScriptableObject
    {
        public string scenarioId;
        public string title;
        [TextArea] public string description;
        [Tooltip("起始狀態 key=value，例 part.carbon_strip_L.true.mid=2.8")] public string[] initialStates = Array.Empty<string>();
        public int randomSeed = 1;
        public bool startRaised;
        public bool practiceOnly;
    }
}
