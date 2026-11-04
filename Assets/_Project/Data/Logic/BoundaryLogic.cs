using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// VR 試玩修正 第 1 點：頭的活動範圍。可走地板（XZ 的矩形）聯集後，外緣往內縮 inset；地板中間挖掉的洞（絕緣子、底架）先補起來，只縮外緣。
    /// 結果用一組矩形表示（直角多邊形拆成矩形）。純函式，建置與執行共用。
    /// </summary>
    public static class BoundaryLogic
    {
        const float Eps = 1e-4f;

        /// <summary>Walkable 的 XZ 範圍（collider 停用時 bounds 是空的，用 transform 算）。</summary>
        public static Rect WalkRect(BoxCollider c)
        {
            var p = c.transform.TransformPoint(c.center); var h = Vector3.Scale(c.size, c.transform.lossyScale) / 2f;
            return Rect.MinMaxRect(p.x - Mathf.Abs(h.x), p.z - Mathf.Abs(h.z), p.x + Mathf.Abs(h.x), p.z + Mathf.Abs(h.z));
        }

        /// <summary>矩形聯集（補洞）的外緣往內縮 inset，回傳矩形組。</summary>
        public static Rect[] Erode(IList<Rect> rects, float inset)
        {
            var xs = rects.SelectMany(r => new[] { r.xMin, r.xMax }).Distinct().OrderBy(v => v).ToArray();
            var ys = rects.SelectMany(r => new[] { r.yMin, r.yMax }).Distinct().OrderBy(v => v).ToArray();
            var solid = Filled(rects, xs, ys);
            bool Covered(float x0, float x1, float y0, float y1)
            {
                if (x0 < xs[0] - Eps || x1 > xs[xs.Length - 1] + Eps || y0 < ys[0] - Eps || y1 > ys[ys.Length - 1] + Eps) return false;
                for (int i = 0; i < xs.Length - 1; i++)
                {
                    if (xs[i + 1] <= x0 + Eps || xs[i] >= x1 - Eps) continue;
                    for (int j = 0; j < ys.Length - 1; j++)
                        if (ys[j + 1] > y0 + Eps && ys[j] < y1 - Eps && !solid[i, j]) return false;
                }
                return true;
            }
            var gx = xs.SelectMany(v => new[] { v - inset, v + inset }).Distinct().OrderBy(v => v).ToArray();
            var gy = ys.SelectMany(v => new[] { v - inset, v + inset }).Distinct().OrderBy(v => v).ToArray();
            var rows = new List<Rect>();
            for (int j = 0; j < gy.Length - 1; j++)
            {
                if (gy[j + 1] - gy[j] < Eps) continue;
                float my = (gy[j] + gy[j + 1]) / 2f; int start = -1;
                for (int i = 0; i <= gx.Length - 1; i++)
                {
                    bool inside = false;
                    if (i < gx.Length - 1 && gx[i + 1] - gx[i] >= Eps)
                    {
                        float mx = (gx[i] + gx[i + 1]) / 2f;
                        inside = Covered(mx - inset, mx + inset, my - inset, my + inset);
                    }
                    if (inside && start < 0) start = i;
                    if (!inside && start >= 0) { rows.Add(Rect.MinMaxRect(gx[start], gy[j], gx[i], gy[j + 1])); start = -1; }
                }
            }
            return MergeRows(rows);
        }

        /// <summary>每一格是不是地板；從外面灌水灌不到的格子是洞，算地板。</summary>
        static bool[,] Filled(IList<Rect> rects, float[] xs, float[] ys)
        {
            int w = xs.Length - 1, h = ys.Length - 1;
            var solid = new bool[w, h];
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    var m = new Vector2((xs[i] + xs[i + 1]) / 2f, (ys[j] + ys[j + 1]) / 2f);
                    solid[i, j] = rects.Any(r => r.Contains(m));
                }
            var outside = new bool[w, h];
            var todo = new Stack<(int, int)>();
            void Try(int i, int j) { if (i >= 0 && j >= 0 && i < w && j < h && !solid[i, j] && !outside[i, j]) { outside[i, j] = true; todo.Push((i, j)); } }
            for (int i = 0; i < w; i++) { Try(i, 0); Try(i, h - 1); }
            for (int j = 0; j < h; j++) { Try(0, j); Try(w - 1, j); }
            while (todo.Count > 0) { var (i, j) = todo.Pop(); Try(i + 1, j); Try(i - 1, j); Try(i, j + 1); Try(i, j - 1); }
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                    if (!outside[i, j]) solid[i, j] = true;
            return solid;
        }

        /// <summary>上下相鄰、左右一樣的矩形併成一塊。</summary>
        static Rect[] MergeRows(List<Rect> rows)
        {
            var done = new List<Rect>();
            foreach (var r in rows.OrderBy(r => r.yMin))
            {
                int k = done.FindIndex(d => Mathf.Abs(d.xMin - r.xMin) < Eps && Mathf.Abs(d.xMax - r.xMax) < Eps && Mathf.Abs(d.yMax - r.yMin) < Eps);
                if (k >= 0) done[k] = Rect.MinMaxRect(r.xMin, done[k].yMin, r.xMax, r.yMax);
                else done.Add(r);
            }
            return done.ToArray();
        }

        /// <summary>p 在範圍內回傳 p；在範圍外回傳最近的邊上的點（沿那條邊的法線推回來）。沒有範圍回傳 p。</summary>
        public static Vector2 Clamp(Rect[] area, Vector2 p)
        {
            if (area == null || area.Length == 0) return p;
            var best = p; float bestD = float.MaxValue;
            foreach (var r in area)
            {
                var q = new Vector2(Mathf.Clamp(p.x, r.xMin, r.xMax), Mathf.Clamp(p.y, r.yMin, r.yMax));
                float d = (q - p).sqrMagnitude;
                if (d <= Eps * Eps) return p;
                if (d < bestD) { bestD = d; best = q; }
            }
            return best;
        }
    }
}
