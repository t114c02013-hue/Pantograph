using System.Collections;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>頭盔前的淡黑轉場（傳送、切換樓層）：攝影機前 0.08 m 的黑色方塊，FadeTo 在 seconds 內改變透明度。</summary>
    public class XrFader : MonoBehaviour
    {
        [SerializeField] Renderer quad;
        [SerializeField] float seconds = 0.2f;
        float alpha;
        MaterialPropertyBlock block;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public float Alpha => alpha;
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
            quad.enabled = alpha > 0.001f;
            if (block == null) block = new MaterialPropertyBlock();
            block.SetColor(ColorId, new Color(0, 0, 0, alpha));
            quad.SetPropertyBlock(block);
        }
    }
}
