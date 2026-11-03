using System;
using System.Linq;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>R1.5b b4（負責人裁定 2026-11-03）：備品架一組 3 條，L、R 兩側各一種排列；換到 R 側補貨時換成 R 排列。放在 Resources/SpareArrangement。</summary>
    public class SpareArrangementDef : ScriptableObject
    {
        public const string ResourcePath = "SpareArrangement";

        [Serializable] public class Entry { public string side; public int slot; public string model; public bool cracked; }

        public Entry[] entries = Array.Empty<Entry>();

        public Entry Find(string side, int slot) => entries.FirstOrDefault(e => e.side == side && e.slot == slot);

        public static SpareArrangementDef Load() => Resources.Load<SpareArrangementDef>(ResourcePath);
    }
}
