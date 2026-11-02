using System.Linq;
using UnityEngine;

namespace PantographSim.Core
{
    /// <summary>
    /// 高亮：滑鼠滑過（淡青）與步驟目標（黃色發光、持續閃爍）。執行時複製材質並開啟發光，不改動材質資產。
    /// 任何模組的物件都可以掛（Parts、Worksite 都只依賴 Core）。ids 為空時用同物件或子物件上所有 ISimObject 的 ID。
    /// </summary>
    public class Highlightable : MonoBehaviour
    {
        [SerializeField] string[] ids;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly Color HoverColor = new Color(0.4f, 0.95f, 1f);
        static readonly Color HintColor = new Color(1f, 0.82f, 0.05f);

        Renderer[] renderers;
        Material[][] mats;
        Color[][] baseColors;
        bool hovered, hinted, subscribed;

        public bool Hinted => hinted;

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer) && !(r is LineRenderer)).ToArray();
            if (ids == null || ids.Length == 0) ids = GetComponentsInChildren<ISimObject>(true).Select(s => s.Id).Where(s => !string.IsNullOrEmpty(s)).ToArray();
        }

        void EnsureMaterials()
        {
            if (mats != null || !Application.isPlaying) return;
            mats = renderers.Select(r => r.materials).ToArray();   // 執行時的材質複本
            baseColors = mats.Select(ms => ms.Select(m => m.HasProperty(ColorId) ? m.color : Color.white).ToArray()).ToArray();
            foreach (var ms in mats) foreach (var m in ms) if (m.HasProperty(EmissionId)) { m.EnableKeyword("_EMISSION"); m.SetColor(EmissionId, Color.black); }
        }

        void OnEnable() => TrySubscribe();
        void Start() => TrySubscribe();

        void TrySubscribe()
        {
            if (subscribed || !Sim.Ready) return;
            Sim.Bus.Subscribe<HighlightRequested>(OnHighlight);
            subscribed = true;
        }

        void OnDisable()
        {
            if (subscribed && Sim.Bus != null) Sim.Bus.Unsubscribe<HighlightRequested>(OnHighlight);
            subscribed = false;
        }

        public static bool Matches(string wanted, string id) =>
            wanted.Length > 0 && (wanted == id || (wanted.EndsWith("*") && id.StartsWith(wanted.TrimEnd('*'))));

        void OnHighlight(HighlightRequested e)
        {
            var wanted = (e.text ?? "").Split(',');
            hinted = ids.Any(id => wanted.Any(w => Matches(w, id)));
            Apply();
        }

        public void SetHovered(bool value)
        {
            if (hovered == value) return;
            hovered = value;
            Apply();
        }

        void Update()
        {
            if (hinted) Apply();
        }

        void Apply()
        {
            EnsureMaterials();
            if (mats == null) return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 6f);
            for (int r = 0; r < mats.Length; r++)
                for (int m = 0; m < mats[r].Length; m++)
                {
                    var mat = mats[r][m];
                    if (mat == null) continue;
                    Color baseC = baseColors[r][m];
                    Color c = baseC, emit = Color.black;
                    if (hovered) { c = Color.Lerp(baseC, HoverColor, 0.55f); emit = HoverColor * 0.35f; }
                    else if (hinted) { c = Color.Lerp(baseC, HintColor, 0.45f + 0.4f * pulse); emit = HintColor * (0.25f + 0.85f * pulse); }
                    if (mat.HasProperty(ColorId)) mat.color = c;
                    if (mat.HasProperty(EmissionId)) mat.SetColor(EmissionId, emit);
                }
        }
    }
}
