using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// R1.5a a5／a6（建模 B 第 12 節 F1–F4、建模 A G2）：集電弓整組吊運的純計算（EditMode 測試用）。
    /// 吊法：吊鉤在重心 COG 正上方；吊梁沿 X 水平掛在吊鉤下，梁中點在 COG 正上方，兩端吊耳間距＝吊點的 X 間距（a6 G2），
    /// 每個 X 側的吊耳各用兩條吊帶接那一側的兩個吊點。吊帶長度依 COG 分邊計算，吊起後才會水平。
    /// </summary>
    public static class PanLiftMath
    {
        public const string MassTablePath = "Assets/_Project/Parts/PantographAssembly/Data/pan_mass_table.json";
        public const string LiftParamsPath = "Assets/_Project/Parts/PantographAssembly/Data/pan_lift_params.json";

        [Serializable]
        public class Group { public string joint; public float massKg; }

        /// <summary>吊具參數（pan_lift_params.json）＋質量（pan_mass_table.json，建模 B 12.6，總重 120 kg，推估）。</summary>
        [Serializable]
        public class Table
        {
            [NonSerialized] public Group[] groups = Array.Empty<Group>();
            [NonSerialized] public float total;
            [Tooltip("吊梁長＝吊耳間距＋這個值（a6 G2）")] public float beamExtraLength = 0.20f;
            [Tooltip("a6 增補 4（建模 B H3、D36）：4 條吊帶的長度；吊梁高度由這個長度推導")] public float slingLength = 1.25f;
            [Tooltip("吊鉤掛點比吊梁高多少")] public float hookAboveBeam = 0.9f;
            public float maxTiltDeg = 2f;
            [Tooltip("每條吊帶和垂直線的夾角上限、同一端兩條之間的夾角上限")] public float maxSlingAngleDeg = 45f, maxSlingPairAngleDeg = 90f;
        }

        /// <summary>讀兩個資料檔。質量表的 joints 是物件（JsonUtility 讀不了字典），只取「"名稱": 數字」。</summary>
        public static Table Load(string massJson, string paramsJson)
        {
            var t = JsonUtility.FromJson<Table>(paramsJson);
            var joints = Regex.Match(massJson, @"""joints""\s*:\s*\{([^}]*)\}");
            if (!joints.Success) throw new FormatException("pan_mass_table.json 沒有 joints");
            var list = new List<Group>();
            foreach (Match m in Regex.Matches(joints.Groups[1].Value, @"""([^""]+)""\s*:\s*([-0-9.eE]+)"))
                list.Add(new Group { joint = m.Groups[1].Value, massKg = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) });
            t.groups = list.ToArray();
            var total = Regex.Match(massJson, @"""total""\s*:\s*([-0-9.eE]+)");
            t.total = total.Success ? float.Parse(total.Groups[1].Value, CultureInfo.InvariantCulture) : 0f;
            return t;
        }

        public static Table LoadDefault() => Load(System.IO.File.ReadAllText(MassTablePath), System.IO.File.ReadAllText(LiftParamsPath));

        /// <summary>依質量加權的重心（F1）。</summary>
        public static Vector3 WeightedCenter(IList<(Vector3 center, float mass)> parts)
        {
            Vector3 sum = Vector3.zero; float m = 0f;
            foreach (var (c, w) in parts) { sum += c * w; m += w; }
            return m > 0f ? sum / m : Vector3.zero;
        }

        /// <summary>吊耳間距的一半＝吊點 X 間距的一半（G2）。</summary>
        public static float BeamHalfSpan(IList<Vector3> liftPoints)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var p in liftPoints) { min = Mathf.Min(min, p.x); max = Mathf.Max(max, p.x); }
            return liftPoints.Count > 0 ? (max - min) / 2f : 0f;
        }

        /// <summary>吊梁兩端吊耳的位置（+X 側、−X 側）：梁中點在 COG 正上方。</summary>
        public static (Vector3 plusX, Vector3 minusX) BeamEyes(Vector3 cog, IList<Vector3> liftPoints, Table t)
        {
            float y = AvgY(liftPoints) + BeamAbove(cog, liftPoints, t), h = BeamHalfSpan(liftPoints);
            return (new Vector3(cog.x + h, y, cog.z), new Vector3(cog.x - h, y, cog.z));
        }

        /// <summary>吊梁比吊點高多少：吊帶長 slingLength 拉直時的垂直高度（吊耳到各吊點的水平距離取平均）。</summary>
        public static float BeamAbove(Vector3 cog, IList<Vector3> liftPoints, Table t)
        {
            float h = BeamHalfSpan(liftPoints), d = 0f;
            foreach (var p in liftPoints) d += new Vector2(p.x - (p.x >= cog.x ? cog.x + h : cog.x - h), p.z - cog.z).magnitude;
            d /= Mathf.Max(1, liftPoints.Count);
            return Mathf.Sqrt(Mathf.Max(t.slingLength * t.slingLength - d * d, 0f));
        }

        /// <summary>吊帶（吊耳 eye → 吊點 p）和垂直線的夾角（度）。</summary>
        public static float SlingAngleDeg(Vector3 eye, Vector3 p) => Vector3.Angle(Vector3.down, p - eye);

        /// <summary>
        /// 建置檢查（建模 B H3、D36）：每條吊帶（吊點 1、4 接 −X 吊耳，2、3 接 +X）和垂直線 ≤ maxSlingAngleDeg、同一端兩條之間 ≤ maxSlingPairAngleDeg。
        /// 回傳各條角度的報告行；有任何一條不合格就丟例外（建置失敗）。liftPoints 依 1..4 排好。
        /// </summary>
        public static List<string> AssertSlingAngles(Vector3 cog, IList<Vector3> liftPoints, Table t)
        {
            var (ep, em) = BeamEyes(cog, liftPoints, t);
            var lines = new List<string>(); var bad = new List<string>();
            for (int i = 0; i < liftPoints.Count; i++)
            {
                float a = SlingAngleDeg(liftPoints[i].x >= cog.x ? ep : em, liftPoints[i]);
                lines.Add($"吊帶 {i + 1}：和垂直線 {a:0.0}°（≤ {t.maxSlingAngleDeg}°）");
                if (a > t.maxSlingAngleDeg + 1e-3f) bad.Add($"吊帶 {i + 1} 和垂直線 {a:0.0}° 超過 {t.maxSlingAngleDeg}°");
            }
            if (liftPoints.Count == 4)
                foreach (var (eye, a, b) in new[] { (em, 0, 3), (ep, 1, 2) })
                {
                    float pair = Vector3.Angle(liftPoints[a] - eye, liftPoints[b] - eye);
                    lines.Add($"吊帶 {a + 1}–{b + 1}（同一端）夾角 {pair:0.0}°（≤ {t.maxSlingPairAngleDeg}°）");
                    if (pair > t.maxSlingPairAngleDeg + 1e-3f) bad.Add($"吊帶 {a + 1}–{b + 1} 夾角 {pair:0.0}° 超過 {t.maxSlingPairAngleDeg}°");
                }
            if (bad.Count > 0) throw new InvalidOperationException("吊帶角度不合格：" + string.Join("；", bad));
            return lines;
        }

        /// <summary>吊鉤掛點（F3）：COG 正上方，吊梁上方 hookAboveBeam。</summary>
        public static Vector3 HookPoint(Vector3 cog, IList<Vector3> liftPoints, Table t) => new Vector3(cog.x, AvgY(liftPoints) + BeamAbove(cog, liftPoints, t) + t.hookAboveBeam, cog.z);

        /// <summary>每個 X 側的吊帶長度（F2）：吊耳到該側吊點的距離（兩個吊點取平均）。x 大於 COG 的是 +X 側。</summary>
        public static (float plusX, float minusX) SlingLengths(Vector3 cog, IList<Vector3> liftPoints, Table t)
        {
            var (ep, em) = BeamEyes(cog, liftPoints, t);
            float sp = 0f, sm = 0f; int np = 0, nm = 0;
            foreach (var p in liftPoints)
                if (p.x >= cog.x) { sp += Vector3.Distance(ep, p); np++; } else { sm += Vector3.Distance(em, p); nm++; }
            return (np > 0 ? sp / np : 0f, nm > 0 ? sm / nm : 0f);
        }

        /// <summary>吊起後的傾斜角（度）：剛體掛在單一吊鉤下會轉到 COG 在吊鉤正下方；傾斜＝吊鉤和 COG 的水平偏移對垂直距離的角度。</summary>
        public static float TiltDeg(Vector3 hook, Vector3 cog)
        {
            var d = hook - cog;
            float h = new Vector2(d.x, d.z).magnitude;
            return Mathf.Atan2(h, Mathf.Max(1e-6f, d.y)) * Mathf.Rad2Deg;
        }

        /// <summary>用分邊吊帶長度吊起來時，四條吊帶的下端（從吊耳往吊點方向量吊帶長度）圍出的平面和水平面的夾角（度）。</summary>
        public static float HangPlaneTiltDeg(Vector3 cog, IList<Vector3> liftPoints, (float plusX, float minusX) len, Table t)
        {
            var (ep, em) = BeamEyes(cog, liftPoints, t);
            var ends = new List<Vector3>();
            foreach (var p in liftPoints)
            {
                bool plus = p.x >= cog.x;
                var eye = plus ? ep : em;
                ends.Add(eye + (p - eye).normalized * (plus ? len.plusX : len.minusX));
            }
            if (ends.Count < 3) return 0f;
            var n = Vector3.Cross(ends[1] - ends[0], ends[2] - ends[0]);
            if (n.y < 0f) n = -n;
            return Vector3.Angle(n, Vector3.up);
        }

        static float AvgY(IList<Vector3> pts) { float y = 0f; foreach (var p in pts) y += p.y; return y / Mathf.Max(1, pts.Count); }
    }
}
