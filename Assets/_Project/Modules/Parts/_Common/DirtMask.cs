using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// 托架接觸面的碳粉遮罩（16×4 格）。清潔布拖過時送來 clean:u,v，清掉附近的格子；
    /// 黑板：ip.carbon_strip_L.bracket.dirt（64 個 0/1）、.clean_ratio。只在滑板拆下時顯示。
    /// </summary>
    public class DirtMask : PartBehaviour
    {
        public const int Cols = 16, Rows = 4;
        [SerializeField] Renderer overlay;
        [SerializeField] string stripId;
        Texture2D tex;

        public void Setup(Renderer r, string strip) { overlay = r; stripId = strip; }

        protected override void OnBound()
        {
            if (!State.Has(Id + ".dirt")) { State.Set(Id + ".dirt", new string('1', Cols * Rows)); State.SetFloat(Id + ".clean_ratio", 0f); }
        }

        protected override bool WatchesKey(string key) => key.StartsWith(Id + ".") || key == stripId + ".state";

        protected override void OnToolAction(ActionPerformed e)
        {
            if (e.text == null || !e.text.StartsWith("clean:")) return;
            var uv = e.text.Substring(6).Split(',');
            if (uv.Length != 2) return;
            float.TryParse(uv[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float u);
            float.TryParse(uv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v);
            Clean(State, Id, u, v);
        }

        /// <summary>清掉以 (u,v) 為中心、半徑約 1 格的格子，更新清除率。</summary>
        public static void Clean(ISimState s, string id, float u, float v)
        {
            var cells = s.Get(id + ".dirt", new string('1', Cols * Rows)).ToCharArray();
            float cu = Mathf.Clamp01(u) * Cols - 0.5f, cv = Mathf.Clamp01(v) * Rows - 0.5f;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    if (Mathf.Abs(c - cu) <= 1.2f && Mathf.Abs(r - cv) <= 1.0f) cells[r * Cols + c] = '0';
            var str = new string(cells);
            s.Batch(() =>   // 髒污格子與清除率一批生效
            {
                s.Set(id + ".dirt", str);
                s.SetFloat(id + ".clean_ratio", str.Count(ch => ch == '0') / (float)(Cols * Rows));
            });
        }

        protected override void RefreshVisual()
        {
            if (overlay == null) return;
            string st = State.Get(stripId + ".state", "OldMounted");
            bool exposed = st == "InTray" || st == "Carried";
            overlay.enabled = exposed;
            foreach (var c in GetComponents<Collider>()) c.enabled = exposed;
            if (tex == null)
            {
                tex = new Texture2D(Cols, Rows, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                overlay.material.mainTexture = tex;
            }
            var cells = State.Get(Id + ".dirt", new string('1', Cols * Rows));
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    tex.SetPixel(c, r, cells[r * Cols + c] == '1' ? new Color(0.08f, 0.08f, 0.08f, 0.9f) : new Color(0.75f, 0.78f, 0.8f, 0.35f));
            tex.Apply();
        }
    }
}
