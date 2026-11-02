using System.Linq;
using System.Text;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// 場景擺放檢查：各場景件（工作桌、工具架、備品架、控制盤、接地端子）的 Bounds 不可與集電弓整體 Bounds（升降全程）相交；
    /// 工作桌與集電弓的水平間距至少 1 m。結果給建置報告與 EditMode 測試使用。
    /// </summary>
    public static class SceneLayoutCheck
    {
        public const float TableClearance = 1.0f;

        public struct Result
        {
            public bool ok;
            public string report;
        }

        public static Bounds BoundsOf(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true).Where(r => !(r is LineRenderer)).ToArray();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>兩個 Bounds 在水平面（XZ）上的最短距離；重疊時為 0。</summary>
        public static float HorizontalGap(Bounds a, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static Result Check(Bounds rig, (string name, Transform t)[] items)
        {
            var sb = new StringBuilder();
            bool ok = true;
            sb.AppendLine($"集電弓（升降全程）範圍：中心 {rig.center:F2}、尺寸 {rig.size:F2}");
            foreach (var (name, t) in items)
            {
                if (t == null) { sb.AppendLine($"✘ {name}：找不到物件"); ok = false; continue; }
                var b = BoundsOf(t);
                bool hit = b.Intersects(rig);
                float gap = HorizontalGap(b, rig);
                bool tooClose = name == "工作桌" && gap < TableClearance - 1e-3f;
                if (hit || tooClose) ok = false;
                sb.AppendLine($"{(hit || tooClose ? "✘" : "✔")} {name}：{(hit ? "與集電弓相交" : "不相交")}，水平間距 {gap:F2} m{(name == "工作桌" ? $"（需 ≥ {TableClearance:F1} m）" : "")}");
            }
            sb.Insert(0, ok ? "擺放檢查：通過\n" : "擺放檢查：有問題\n");
            return new Result { ok = ok, report = sb.ToString() };
        }
    }
}
