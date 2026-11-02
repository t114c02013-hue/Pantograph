using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// 暈動遮罩：攝影機前的圓形透明、四周變暗的方塊（危險區時改成紅色）。
    /// shortcut: 專案沒有 XRI 的 Tunneling Vignette 範例（shader 與 prefab），先用執行時產生的徑向貼圖；要換成 TunnelingVignetteController 時只換這個元件。
    /// </summary>
    public class XrVignette : MonoBehaviour
    {
        [SerializeField] Renderer quad;
        float alpha;
        MaterialPropertyBlock block;
        static Texture2D tex;

        public void Setup(Renderer r) { quad = r; }
        public float Alpha => alpha;

        void Awake()
        {
            if (quad == null) return;
            if (tex == null)
            {
                const int n = 128;
                tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float r = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                        tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.95f, r))));
                    }
                tex.Apply();
            }
            Show(0f, false);
        }

        public void Show(float a, bool red)
        {
            alpha = Mathf.MoveTowards(alpha, Mathf.Clamp01(a), Time.unscaledDeltaTime * 4f);
            if (quad == null) return;
            quad.enabled = alpha > 0.01f;
            if (block == null) block = new MaterialPropertyBlock();
            if (tex != null) block.SetTexture("_MainTex", tex);
            block.SetColor("_Color", red ? new Color(0.8f, 0f, 0f, alpha) : new Color(0f, 0f, 0f, alpha));
            quad.SetPropertyBlock(block);
        }
    }
}
