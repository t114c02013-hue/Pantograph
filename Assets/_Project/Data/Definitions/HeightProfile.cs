using System;
using UnityEngine;

namespace PantographSim.Data
{
    // R1：座標系＝原點在弓頭中心正下方的車頂地板點，+Y 上、+Z 朝肘側滑板、平台在 −X 側，單位 m；標 EX 的是示例值。

    public enum HeightMode { Real, Raised }

    /// <summary>
    /// 視點高度：Real＝真實高度（降弓後滑板頂面約在車頂上 0.6–0.7 m，要彎腰或蹲下）；Raised＝只在人站在車頂時調整 XR Origin 的 Camera Floor Offset，
    /// 偏移量＝measuredStripTop − raisedStripTop（例 0.69 − 1.2＝−0.51 m，視點下移，滑板看起來在腳下 1.2 m＝站著就能作業）。
    /// 場景不移動；平台與駕駛室偏移為 0；切換樓層時 0.2 s 淡黑。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/R1/Height Profile")]
    public class HeightProfile : ScriptableObject
    {
        public HeightMode mode = HeightMode.Raised;
        [Tooltip("第 8 點：175 cm 的舒適工作高度，要在 PostureDef.stripWorkHeight ± stripWorkTolerance 內")] public float raisedStripTop = 1.2f;
        public bool allowToggleInMenu = true;
        [Tooltip("建置時量到的降弓後滑板頂面高度（相對車頂地板）")] public float measuredStripTop = 0.69f;
        [Tooltip("蹲姿輔助：只在車頂有效的視點下移量")] public float crouchOffset = -0.5f;
        public bool crouchAssistDefaultOn = false;

        public float floorChangeFadeSeconds = 0.2f;

        /// <summary>人在車頂時的 Camera Floor Offset（m）；Real 時為 0。</summary>
        public float RoofOffset => mode == HeightMode.Raised ? measuredStripTop - raisedStripTop : 0f;
    }
}
