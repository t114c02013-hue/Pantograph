using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PantographSim.Data
{
    public enum BlackboardKeyKind { Event, LiveState }

    /// <summary>
    /// R1.5b b4（軟體工程裁定 2026-11-03，補充 2b）：黑板鍵的種類。LiveState＝場景裡現在看得到的實體狀態，Event＝做過某件事留下的紀錄；
    /// 沒列的鍵一律當 Event。小動作只讀 LiveState 鍵時條件成立就完成，讀到 Event 鍵時那個鍵要在啟用後被寫過（FreshWriteGate）。
    /// 由匯入器從 Data/Source/BlackboardKeys.json 產生，放在 Resources/BlackboardKeys。
    /// </summary>
    public class BlackboardKeyDef : ScriptableObject
    {
        public const string ResourcePath = "BlackboardKeys";

        [Serializable]
        public class Entry
        {
            [Tooltip("鍵；* 代表一段任意字元（例 rack.slot_*）")] public string pattern;
            public BlackboardKeyKind kind;
            public string note;
        }

        public Entry[] keys = Array.Empty<Entry>();

        Regex[] compiled;

        public static bool Matches(string pattern, string key) =>
            Regex.IsMatch(key, "^" + string.Join(".*", (pattern ?? "").Split('*').Select(Regex.Escape)) + "$");

        /// <summary>這個鍵的種類（沒列的＝Event）。</summary>
        public BlackboardKeyKind Kind(string key)
        {
            if (compiled == null || compiled.Length != keys.Length)
                compiled = keys.Select(e => new Regex("^" + string.Join(".*", (e.pattern ?? "").Split('*').Select(Regex.Escape)) + "$")).ToArray();
            for (int i = 0; i < keys.Length; i++) if (compiled[i].IsMatch(key)) return keys[i].kind;
            return BlackboardKeyKind.Event;
        }

        static BlackboardKeyDef cached;
        public static BlackboardKeyDef Load() => cached != null ? cached : cached = Resources.Load<BlackboardKeyDef>(ResourcePath);
    }
}
