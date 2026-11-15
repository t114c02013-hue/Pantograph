using System.Collections;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>頭盔前的淡黑轉場（傳送、切換樓層）：攝影機前 0.08 m 的黑色方塊，FadeTo 在 seconds 內改變透明度。</summary>
    public class XrFader : MonoBehaviour
    {
        [SerializeField] Renderer quad;
        [SerializeField] float seconds = 0.2f;
        float alpha, overlay;
        MaterialPropertyBlock block;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public float Alpha => alpha;
        /// <summary>terry 186：頭穿進實心物體的淡黑（HeadSolidFade 寫）。畫面顯示 max(轉場, overlay)：轉場淡到全黑不會被拉回。</summary>
        public float Overlay { get => overlay; set { float v = Mathf.Clamp01(value); if (v == overlay) return; overlay = v; Apply(alpha); } }
        /// <summary>實際顯示的透明度。</summary>
        public float Shown => Mathf.Max(alpha, overlay);
        public float Seconds { get => seconds; set => seconds = value; }

        public void Setup(Renderer r, float s) { quad = r; seconds = s; Apply(0f); }

        public IEnumerator FadeTo(float target)
        {
            float start = alpha, t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                Apply(Mathf.Lerp(start, target, seconds <= 0f ? 1f : t / seconds));
                yield return null;
            }
            Apply(target);
        }

        public void Apply(float a)
        {
            alpha = Mathf.Clamp01(a);
            if (quad == null) return;
            float shown = Shown;
            quad.enabled = shown > 0.001f;
            if (block == null) block = new MaterialPropertyBlock();
            block.SetColor(ColorId, new Color(0, 0, 0, shown));
            quad.SetPropertyBlock(block);
        }
    }
}
