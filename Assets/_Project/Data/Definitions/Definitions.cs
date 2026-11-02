using System;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Data
{
    [Serializable]
    public class RepeatBlock
    {
        public string fromStepId;
        public string toStepId;
        public string[] sides = { "L", "R" };
        public string[] sideLabels = { "肘側滑板", "外側滑板" };
    }

    /// <summary>某步驟完成時，若 key 等於 value 就跳到 jumpTo。</summary>
    [Serializable]
    public class JumpRule
    {
        public string stepId;
        public string key;
        public string value;
        public string jumpTo;
    }

    [Serializable]
    public class TutorialPage
    {
        public string title;
        [TextArea] public string body;
    }

    [Serializable]
    public class ToolTutorialDefinition
    {
        public TutorialPage[] pages = Array.Empty<TutorialPage>();
    }

    public enum SafetyRuleKind { NotLowered, NotGrounded, GroundOrder, NoVoltageTest, RemoveOrder }

    public static class KeyValueList
    {
        /// <summary>把 "key=value" 陣列寫入黑板（一批：例如上鎖掛牌、安全帶的預設狀態一起生效）。</summary>
        public static void Apply(string[] pairs, ISimState state)
        {
            if (pairs == null || pairs.Length == 0) return;
            state.Batch(() =>
            {
                foreach (var p in pairs)
                {
                    int i = p.IndexOf('=');
                    if (i > 0) state.Set(p.Substring(0, i).Trim(), p.Substring(i + 1).Trim());
                }
            });
        }
    }
}
