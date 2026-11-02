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
}
