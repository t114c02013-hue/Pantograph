using PantographSim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace PantographSim.XR
{
    /// <summary>
    /// 右手射線指到 World Space 介面（作業單夾板、手腕面板、升降面板、選單、資訊卡）時的指示：
    /// ・擊中點顯示白色帶黑邊的圓點（reticle），依距離縮放、保持固定的視角大小（約 1 cm＠0.6 m），永遠畫在介面最上層；
    /// ・射線打到介面時變藍，長度到擊中點為止（XRInteractorLineVisual 停在第一個擊中點）；
    /// ・介面優先：指到介面時 XrPlayer 不送場景點擊（PointerState.OverUi）；
    /// ・只在按下（扳機）時短震動，hover 不震動。按鈕的 hover 高亮與按下效果由 Selectable 的顏色過渡處理。
    /// 射線沒打到介面時不顯示圓點，射線維持原樣。只有右手有。
    /// </summary>
    public class XrUiPointer : MonoBehaviour
    {
        /// <summary>圓點直徑相對距離的比例（視角大小）：0.5 m 處約 1 cm、0.6 m 處 1.2 cm；最小 0.8 cm。</summary>
        public const float AngularDiameter = 0.02f, MinDiameter = 0.008f;
        public static readonly Color UiRayColor = new Color(0.25f, 0.65f, 1f, 1f);

        XRRayInteractor ray;
        XRInteractorLineVisual line;
        XRBaseController controller;
        Canvas reticleCanvas;
        RectTransform dot;
        Gradient validOriginal, invalidOriginal, uiGradient;
        bool lastSelect;

        public bool OnUi { get; private set; }
        public Vector3 HitPoint { get; private set; }
        public float DotDiameter { get; private set; }
        public RectTransform Dot => dot;

        public void Setup(XRRayInteractor r, XRBaseController c)
        {
            ray = r; controller = c;
            line = r != null ? r.GetComponent<XRInteractorLineVisual>() : null;
            if (line != null) { validOriginal = line.validColorGradient; invalidOriginal = line.invalidColorGradient; }
            uiGradient = new Gradient();
            uiGradient.SetKeys(new[] { new GradientColorKey(UiRayColor, 0f), new GradientColorKey(UiRayColor, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            if (reticleCanvas == null) BuildReticle();
        }

        void BuildReticle()
        {
            var go = new GameObject("XR_Reticle", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            reticleCanvas = go.AddComponent<Canvas>();
            reticleCanvas.renderMode = RenderMode.WorldSpace;
            reticleCanvas.sortingOrder = 100;   // 比所有介面面板（10）晚畫：永遠在最上層
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(100, 100);
            var img = new GameObject("Dot", typeof(RectTransform)).AddComponent<Image>();
            img.transform.SetParent(go.transform, false);
            img.sprite = CircleSprite();
            img.raycastTarget = false;   // 圓點本身不擋射線
            dot = img.rectTransform;
            dot.anchorMin = Vector2.zero; dot.anchorMax = Vector2.one; dot.offsetMin = dot.offsetMax = Vector2.zero;
            go.SetActive(false);
        }

        /// <summary>白色實心圓＋黑色外框（程式產生，不需要圖檔）。</summary>
        static Sprite CircleSprite()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float c = (n - 1) / 2f, outer = n / 2f - 1f, inner = outer - n * 0.14f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(outer - d + 0.5f);
                    px[y * n + x] = d <= inner ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        void LateUpdate()
        {
            if (ray == null) return;
            bool hasUi = ray.TryGetCurrentUIRaycastResult(out RaycastResult ui) && ui.isValid;
            // 與 XrPlayer 相同的規則：介面比場景物件近（或沒打到物件）時，介面優先
            OnUi = hasUi && PointerState.OverUi;
            if (OnUi) Place(ui);
            if (reticleCanvas != null && reticleCanvas.gameObject.activeSelf != OnUi) reticleCanvas.gameObject.SetActive(OnUi);
            if (line != null)
            {
                line.validColorGradient = OnUi ? uiGradient : validOriginal;
                line.invalidColorGradient = OnUi ? uiGradient : invalidOriginal;
            }
            // 按下（扳機）瞬間指在介面上：短震動一次
            bool select = ray.TryGetUIModel(out TrackedDeviceModel model) && model.select;
            if (select && !lastSelect && OnUi && !SimSession.IsReplay) controller?.SendHapticImpulse(0.35f, 0.04f);
            lastSelect = select;
        }

        void Place(RaycastResult ui)
        {
            var origin = ray.rayOriginTransform != null ? ray.rayOriginTransform : ray.transform;
            Vector3 dir = (ui.worldPosition - origin.position);
            float dist = dir.magnitude;
            dir = dist > 1e-4f ? dir / dist : origin.forward;
            HitPoint = ui.worldPosition;
            DotDiameter = Mathf.Max(MinDiameter, dist * AngularDiameter);
            if (reticleCanvas == null) return;
            var canvasT = ui.gameObject != null ? ui.gameObject.GetComponentInParent<Canvas>()?.rootCanvas.transform : null;
            var t = reticleCanvas.transform;
            t.position = ui.worldPosition - dir * 0.003f;   // 往射線起點退 3 mm，不會和面板互相遮擋
            t.rotation = canvasT != null ? canvasT.rotation : Quaternion.LookRotation(dir, Vector3.up);
            t.localScale = Vector3.one * (DotDiameter / 100f) / Mathf.Max(1e-6f, transform.lossyScale.x);
        }
    }
}
