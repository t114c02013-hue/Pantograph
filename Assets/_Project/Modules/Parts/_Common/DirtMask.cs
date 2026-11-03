using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// 托架接觸面的碳粉遮罩（16×4 格）。清潔布拖過時送來 clean:u,v，清掉附近的格子；
    /// 黑板：ip.carbon_strip_L.bracket.dirt（64 個 0/1）、.clean_ratio。只在滑板拆下時顯示。
    /// R1.5b b4（D43）：絕緣子髒汙也用這個（16×6 格、貼傘裙外緣的圓筒殼、圓柱 UV），visibleKey（fault.insulator_dirty）開著才顯示。
    /// </summary>
    public class DirtMask : PartBehaviour
    {
        public const int Cols = 16, Rows = 4;
        [SerializeField] Renderer overlay;
        [SerializeField] string stripId;
        [Tooltip("R1.5b b4：格子列數（托架 4、絕緣子 6）")] [SerializeField] int rows = Rows;
        [Tooltip("R1.5b b4：這個鍵成立才顯示（空白＝照滑板拆下才顯示）")] [SerializeField] string visibleKey = "";
        [Tooltip("R1.5b b4：u 繞一圈頭尾相接（圓筒殼）")] [SerializeField] bool wrapU;
        Texture2D tex;

        public void Setup(Renderer r, string strip) { overlay = r; stripId = strip; }
        public void SetupSurface(Renderer r, int rowCount, string showWhen) { overlay = r; stripId = ""; rows = rowCount; visibleKey = showWhen; wrapU = true; }

        protected override void OnBound()
        {
            if (!State.Has(Id + ".dirt")) { State.Set(Id + ".dirt", new string('1', Cols * rows)); State.SetFloat(Id + ".clean_ratio", 0f); }
        }

        protected override bool WatchesKey(string key) => key.StartsWith(Id + ".") || key == stripId + ".state" || (visibleKey.Length > 0 && key == visibleKey);

        protected override void OnToolAction(ActionPerformed e)
        {
            if (e.text == null || !e.text.StartsWith("clean:")) return;
            var uv = e.text.Substring(6).Split(',');
            if (uv.Length != 2) return;
            float.TryParse(uv[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float u);
            float.TryParse(uv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v);
            Clean(State, Id, u, v, rows, wrapU);
        }

        /// <summary>清掉以 (u,v) 為中心、半徑約 1 格的格子，更新清除率。</summary>
        public static void Clean(ISimState s, string id, float u, float v) => Clean(s, id, u, v, Rows, false);

        /// <summary>R1.5b b4：指定列數（絕緣子 6 列）；wrapU＝u 繞圓周時頭尾相接。</summary>
        public static void Clean(ISimState s, string id, float u, float v, int rowCount, bool wrapU)
        {
            int Rows = rowCount;
            var cells = s.Get(id + ".dirt", new string('1', Cols * Rows)).ToCharArray();
            float cu = Mathf.Clamp01(u) * Cols - 0.5f, cv = Mathf.Clamp01(v) * Rows - 0.5f;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    if (Mathf.Min(Mathf.Abs(c - cu), wrapU ? Cols - Mathf.Abs(c - cu) : 99f) <= 1.2f && Mathf.Abs(r - cv) <= 1.0f) cells[r * Cols + c] = '0';
            var str = new string(cells);
            s.Batch(() =>   // 髒污格子與清除率一批生效
            {
                s.Set(id + ".dirt", str);
                s.SetFloat(id + ".clean_ratio", str.Count(ch => ch == '0') / (float)(Cols * Rows));
                // R1.5b b4（D43）：圓筒殼每一行（繞圓周 16 格）都擦到過＝繞看了一圈（COL_Dirt 是 trigger，點選打不到，用擦拭的範圍判）
                if (wrapU && !s.GetBool(id + ".inspected") && Enumerable.Range(0, Cols).All(c => Enumerable.Range(0, Rows).Any(r => str[r * Cols + c] == '0'))) s.SetBool(id + ".inspected", true);
            });
        }

        protected override void RefreshVisual()
        {
            if (overlay == null) return;
            string st = State.Get(stripId + ".state", "OldMounted");
            bool exposed = visibleKey.Length > 0 ? State.GetBool(visibleKey) : st == "InTray" || st == "Carried";
            overlay.enabled = exposed;
            foreach (var c in GetComponents<Collider>()) c.enabled = exposed;
            if (tex == null)
            {
                tex = new Texture2D(Cols, rows, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                overlay.material.mainTexture = tex;
            }
            var cells = State.Get(Id + ".dirt", new string('1', Cols * rows));
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < Cols; c++)
                    tex.SetPixel(c, r, cells[r * Cols + c] == '1' ? new Color(0.08f, 0.08f, 0.08f, 0.9f) : new Color(0.75f, 0.78f, 0.8f, 0.35f));
            tex.Apply();
        }
    }
}
