using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>terry 2026-11-15：指示燈罩上的點光源，只管光（材質照舊由各燈自己換）。亮＝intensity，暗＝intensity × dimRatio（留弱光）；光色跟燈色。</summary>
    public class LampLight : MonoBehaviour
    {
        [SerializeField] Light lamp;
        [SerializeField] float intensity = 0.8f, dimRatio = 0.1f;

        public Light Light => lamp;

        public void Setup(Light l, float lit, float dim) { lamp = l; intensity = lit; dimRatio = dim; }

        public void Set(bool on, Color color)
        {
            if (lamp == null) return;
            lamp.color = color;
            lamp.intensity = on ? intensity : intensity * dimRatio;
        }

        /// <summary>有掛 LampLight 就設定（呼叫端一行）。</summary>
        public static void SetOn(Component c, bool on, Color color) { if (c != null && c.TryGetComponent(out LampLight l)) l.Set(on, color); }
    }
}
