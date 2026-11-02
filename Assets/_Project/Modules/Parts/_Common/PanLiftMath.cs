using System;
using System.Collections.Generic;
using UnityEngine;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// R1.5a a5（建模 B 第 12 節 F1–F4）：集電弓整組吊運的純計算（EditMode 測試用）。
    /// 吊法：吊鉤在重心 COG 正上方；吊梁沿 Z 水平掛在吊鉤下，梁中點在 COG 正上方，兩端吊耳（每個 Z 側一個）各用兩條吊帶接那一側的兩個吊點。
    /// 吊帶長度依 COG 分邊計算，吊起後才會水平。
    /// </summary>
    public static class PanLiftMath
    {
        [Serializable]
        public class Group { public string joint; public float massKg; }

        /// <summary>資料（Assets/_Project/Parts/PantographAssembly/Data/pan_mass_table.json）。數值 EX。</summary>
        [Serializable]
        public class Table
        {
            public Group[] groups = Array.Empty<Group>();
            [Tooltip("吊梁兩端吊耳到梁中點的距離（沿 Z）")] public float beamHalfLength = 1.0f;
            [Tooltip("吊梁比吊點高多少")] public float beamAboveLiftPoints = 0.75f;
            [Tooltip("吊鉤掛點比吊梁高多少")] public float hookAboveBeam = 0.9f;
            public float maxTiltDeg = 2f;
        }

        /// <summary>依質量加權的重心（F1）。</summary>
        public static Vector3 WeightedCenter(IList<(Vector3 center, float mass)> parts)
        {
            Vector3 sum = Vector3.zero; float m = 0f;
            foreach (var (c, w) in parts) { sum += c * w; m += w; }
            return m > 0f ? sum / m : Vector3.zero;
        }

        /// <summary>吊梁兩端吊耳的位置（+Z 側、−Z 側）：梁中點在 COG 正上方。</summary>
        public static (Vector3 plusZ, Vector3 minusZ) BeamEyes(Vector3 cog, float liftPointY, Table t)
        {
            float y = liftPointY + t.beamAboveLiftPoints;
            return (new Vector3(cog.x, y, cog.z + t.beamHalfLength), new Vector3(cog.x, y, cog.z - t.beamHalfLength));
        }

        /// <summary>吊鉤掛點（F3）：COG 正上方。</summary>
        public static Vector3 HookPoint(Vector3 cog, float liftPointY, Table t) => new Vector3(cog.x, liftPointY + t.beamAboveLiftPoints + t.hookAboveBeam, cog.z);

        /// <summary>每個 Z 側的吊帶長度（F2）：吊耳到該側吊點的距離（兩個吊點取平均）。liftPoints 依 z 分邊（z 大於 COG 的是 +Z 側）。</summary>
        public static (float plusZ, float minusZ) SlingLengths(Vector3 cog, IList<Vector3> liftPoints, Table t)
        {
            float y = 0f; foreach (var p in liftPoints) y += p.y; y /= Mathf.Max(1, liftPoints.Count);
            var (ep, em) = BeamEyes(cog, y, t);
            float sp = 0f, sm = 0f; int np = 0, nm = 0;
            foreach (var p in liftPoints)
                if (p.z >= cog.z) { sp += Vector3.Distance(ep, p); np++; } else { sm += Vector3.Distance(em, p); nm++; }
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
        public static float HangPlaneTiltDeg(Vector3 cog, IList<Vector3> liftPoints, (float plusZ, float minusZ) len, Table t)
        {
            float y = 0f; foreach (var p in liftPoints) y += p.y; y /= Mathf.Max(1, liftPoints.Count);
            var (ep, em) = BeamEyes(cog, y, t);
            var ends = new List<Vector3>();
            foreach (var p in liftPoints)
            {
                bool plus = p.z >= cog.z;
                var eye = plus ? ep : em;
                ends.Add(eye + (p - eye).normalized * (plus ? len.plusZ : len.minusZ));
            }
            if (ends.Count < 3) return 0f;
            var n = Vector3.Cross(ends[1] - ends[0], ends[2] - ends[0]);
            if (n.y < 0f) n = -n;
            return Vector3.Angle(n, Vector3.up);
        }
    }
}
