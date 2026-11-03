using System.Linq;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// 托架接觸面的碳粉遮罩（16×4 格）。清潔布拖過時送來 clean:u,v，清掉附近的格子；
    /// 黑板：ip.carbon_strip_L.bracket.dirt（64 個 0/1）、.clean_ratio。只在滑板拆下時顯示。
    /// R1.5b b4（D43）：絕緣子髒汙也用這個（16×6 格、貼傘裙外緣的圓筒殼、圓柱 UV），visibleKey（fault.insulator_dirty）開著才顯示。
    /// 清除率只算 InsulatorDirtDef 的計分格（v3.12：面向子站點、手搆得到的格），達門檻時整支變乾淨並播拉一圈的動作；背面不計分。
    /// </summary>
    public class DirtMask : PartBehaviour
    {
        public const int Cols = 16, Rows = 4;
        [SerializeField] Renderer overlay;
        [SerializeField] string stripId;
        [Tooltip("R1.5b b4：格子列數（托架 4、絕緣子 6）")] [SerializeField] int rows = Rows;
        [Tooltip("R1.5b b4：這個鍵成立才顯示（空白＝照滑板拆下才顯示）")] [SerializeField] string visibleKey = "";
        [Tooltip("R1.5b b4：u 繞一圈頭尾相接（圓筒殼）")] [SerializeField] bool wrapU;
        [Tooltip("D43 v3.12：計分格（建置時烘焙）；null＝全部格子都算")] [SerializeField] InsulatorDirtDef score;
        [SerializeField] int scoreIndex = -1;
        [Tooltip("D43：達門檻時「用布包住絕緣子拉一圈」的布圈（子物件，平常隱藏）")] [SerializeField] Transform wrapCloth;
        Texture2D tex;
        float wrapT = -1f;
        bool wrapPlayed;

        public void SetScore(InsulatorDirtDef def, int index, Transform cloth) { score = def; scoreIndex = index; wrapCloth = cloth; }
        public string ScoreMask => score != null && scoreIndex >= 0 && scoreIndex < score.scoreMask.Length ? score.scoreMask[scoreIndex] : null;
        public string FaceMask => score != null && scoreIndex >= 0 && scoreIndex < score.faceMask.Length ? score.faceMask[scoreIndex] : null;

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
            Clean(State, Id, u, v, rows, wrapU, ScoreMask, FaceMask, score != null ? score.cleanRatio : 1f);
        }

        /// <summary>清掉以 (u,v) 為中心、半徑約 1 格的格子，更新清除率。</summary>
        public static void Clean(ISimState s, string id, float u, float v) => Clean(s, id, u, v, Rows, false);

        /// <summary>
        /// R1.5b b4：指定列數（絕緣子 6 列）；wrapU＝u 繞圓周時頭尾相接。D43 v3.12：scoreMask（'1'＝計分格）有給時清除率只算計分格，
        /// 達 threshold 就整支清乾淨（寫 &lt;id&gt;.wrapped，元件播拉一圈）；faceColumns（逗號分隔）有給時，那幾行每行至少擦到 1 格＝已目視繞看一圈。
        /// </summary>
        public static void Clean(ISimState s, string id, float u, float v, int rowCount, bool wrapU, string scoreMask = null, string faceColumns = null, float threshold = 1f)
        {
            int Rows = rowCount;
            var cells = s.Get(id + ".dirt", new string('1', Cols * Rows)).ToCharArray();
            float cu = Mathf.Clamp01(u) * Cols - 0.5f, cv = Mathf.Clamp01(v) * Rows - 0.5f;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    if (Mathf.Min(Mathf.Abs(c - cu), wrapU ? Cols - Mathf.Abs(c - cu) : 99f) <= 1.2f && Mathf.Abs(r - cv) <= 1.0f) cells[r * Cols + c] = '0';
            var str = new string(cells);
            float ratio = str.Count(ch => ch == '0') / (float)(Cols * Rows);
            bool wrapped = false;
            if (!string.IsNullOrEmpty(scoreMask) && scoreMask.Length == str.Length)
            {
                int scored = scoreMask.Count(ch => ch == '1'), cleaned = Enumerable.Range(0, str.Length).Count(i => scoreMask[i] == '1' && str[i] == '0');
                ratio = scored > 0 ? cleaned / (float)scored : 0f;
                if (ratio >= threshold - 1e-4f) { str = new string('0', str.Length); ratio = 1f; wrapped = true; }   // 背面不計分：計分格達門檻，整支擦淨
            }
            var cols = string.IsNullOrEmpty(faceColumns) ? (wrapU ? Enumerable.Range(0, Cols).ToArray() : new int[0]) : faceColumns.Split(',').Select(int.Parse).ToArray();
            s.Batch(() =>   // 髒污格子與清除率一批生效
            {
                s.Set(id + ".dirt", str);
                s.SetFloat(id + ".clean_ratio", ratio);
                if (wrapped && !s.GetBool(id + ".wrapped")) s.SetBool(id + ".wrapped", true);
                // D43 v3.12：面向子站點的那幾行每行至少擦到 1 格＝已目視繞看一圈（COL_Dirt 是 trigger，點選打不到，用擦拭的範圍判）
                if (cols.Length > 0 && !s.GetBool(id + ".inspected") && cols.All(c => Enumerable.Range(0, Rows).Any(r => str[r * Cols + c] == '0'))) s.SetBool(id + ".inspected", true);
            });
        }

        protected override void RefreshVisual()
        {
            if (overlay == null) return;
            string st = State.Get(stripId + ".state", "OldMounted");
            bool exposed = visibleKey.Length > 0 ? State.GetBool(visibleKey) : st == "InTray" || st == "Carried";
            overlay.enabled = exposed;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = exposed;   // 絕緣子的 COL_Dirt 在子物件
            if (wrapCloth != null && State.GetBool(Id + ".wrapped") && !wrapPlayed) { wrapPlayed = true; wrapT = 0f; wrapCloth.gameObject.SetActive(true); }
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

        /// <summary>D43：拉一圈的動作（布圈由下往上掃過整支，wrapSeconds 後收起）。</summary>
        void Update()
        {
            if (wrapT < 0f || wrapCloth == null) return;
            wrapT += Time.deltaTime;
            float dur = score != null ? score.wrapSeconds : 0.5f, k = Mathf.Clamp01(wrapT / dur);
            wrapCloth.localPosition = new Vector3(0f, Mathf.Lerp(-1f, 1f, k), 0f);
            if (k >= 1f) { wrapT = -1f; wrapCloth.gameObject.SetActive(false); }
        }
    }
}
