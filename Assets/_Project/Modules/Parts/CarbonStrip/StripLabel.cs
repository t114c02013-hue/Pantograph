using UnityEngine;

namespace PantographSim.Parts.CarbonStrip
{
    /// <summary>滑板標籤：型號與是否有裂紋（裂紋在背面，翻面才看得到）。</summary>
    public class StripLabel : MonoBehaviour
    {
        public string modelCode = "CS-1100-74A";
        public bool cracked;
        [Tooltip("示例值")] public float nominalThicknessMm = 12.0f;
    }
}
