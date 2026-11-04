using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// VR 試玩修正 第 1 點：頭的活動範圍（Resources/BoundaryDef）。建置時由 Walkable 自動產生：外緣往內縮 inset。
    /// open＝活動欄打開、跨板放下（平台和車頂相連）；closed＝沒有跨板。
    /// </summary>
    public class BoundaryDef : ScriptableObject
    {
        public const string ResourceName = "BoundaryDef";

        [Tooltip("Walkable 外緣往內縮（m）")] public float inset = 0.15f;
        public Rect[] closed = new Rect[0], open = new Rect[0];

        public static BoundaryDef Load() => Resources.Load<BoundaryDef>(ResourceName);
    }
}
