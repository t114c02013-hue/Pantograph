using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4（鐵道 D42、軟體工程）：IN_01 a 吊梁底面在弓頂上方的範圍。下限 lowerM；上限＝拉直高度 − slackMarginM（吊帶要鬆才掛得上吊耳）。
    /// 拉直高度由場景建置量測寫回（吊梁在弓頂正上方、吊帶 slingLength 拉直到吊耳時，吊梁底面離弓頂多高）。執行時寫到黑板
    /// crane.rig_height_lower_m／upper_m，IN_01 a 的完成條件讀這兩個鍵，不寫死數字。放在 Resources/RigHeight。
    /// </summary>
    public class RigHeightDef : ScriptableObject
    {
        public const string ResourcePath = "RigHeight";
        public const string LowerKey = "crane.rig_height_lower_m", UpperKey = "crane.rig_height_upper_m";

        public float lowerM = 0.55f;
        public float slackMarginM = 0.06f;
        [Tooltip("弓頂取哪一點：head＝降弓整組最高點（弓頭，含滑板）；underBeam＝吊梁 XZ 範圍正下方的最高點。等鐵道裁定，先用 head（較保守）")] public string topBasis = "head";
        [Tooltip("建置量測：吊帶拉直時吊梁底面離弓頂的高度（m）")] public float straightHeightM;

        public float UpperM => straightHeightM - slackMarginM;

        /// <summary>建置檢查：上限要比下限高 0.10 以上，而且比 LF_06 c 的 1.0 m 低 0.10 以上。回傳 null＝通過，否則是原因。</summary>
        public string Check(float lf06MinM = 1.0f, float gapM = 0.10f) =>
            UpperM > lowerM + gapM && UpperM < lf06MinM - gapM ? null
                : $"IN_01 a 上限 {UpperM:0.000} m（拉直高度 {straightHeightM:0.000} − {slackMarginM:0.000}）要大於 {lowerM + gapM:0.00}、小於 {lf06MinM - gapM:0.00}";

        public static RigHeightDef Load() => Resources.Load<RigHeightDef>(ResourcePath);

        /// <summary>執行時把範圍寫到黑板（PanAssyRigging 綁定時呼叫）。</summary>
        public void Publish(PantographSim.Core.ISimState s)
        {
            if (s == null) return;
            s.Batch(() => { s.SetFloat(LowerKey, lowerM); s.SetFloat(UpperKey, UpperM); });
        }
    }
}
