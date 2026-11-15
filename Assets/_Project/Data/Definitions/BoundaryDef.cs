using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// VR 試玩修正 第 1 點：頭的活動範圍（Resources/BoundaryDef）。建置時由 Walkable 自動產生：外緣往內縮 inset。
    /// open＝活動欄打開、跨板放下（平台和車頂相連）；closed＝沒有跨板。
    /// </summary>
    public class BoundaryDef : ScriptableObject
    {
        public const string ResourceName = "BoundaryDef";

        [Tooltip("Walkable 外緣往內縮（m）")] public float inset = 0.15f;
        public Rect[] closed = new Rect[0], open = new Rect[0];

        // ---------- terry 134B／185（2026-11-15）車頂墜落區：數值由建置時從 RoofSceneDef 推導寫入，執行期只讀這份 ----------
        [Tooltip("車頂邊 E（車體半寬）")] public float roofEdge = 1.325f;
        [Tooltip("警示帶寬（E − 這個值 ＜ |X| ≤ E）")] public float fallWarnBand = 0.3f;
        [Tooltip("沒有平台處的推回線在車頂邊外多遠")] public float fallPushOutsideEdge = 0.2f;
        [Tooltip("墜落區連續停留多久記錯（s）、傳送落地與淡出推回後多久內不判（s）")] public float fallDwellSec = 1.0f, teleportGraceSec = 0.5f;
        [Tooltip("淡出推回：淡出、淡入秒數")] public float fallFadeOutSec = 0.2f, fallFadeInSec = 0.2f;
        [Tooltip("進入警示帶時震一次：強度、秒數")] public float fallWarnHapticAmp = 0.5f, fallWarnHapticSec = 0.12f;
        [Tooltip("提示：警示帶、下方有平台、墜落區")] public string fallWarnText = "已靠近車頂邊，請往車頂中間站", offRoofText = "已離開車頂，請回到車頂中間再作業", fallZoneText = "這裡下方沒有平台，請立刻退回車頂中間";
        [Tooltip("車頂旁的平台範圍（XZ；−X 真平台、+X 外觀平台都算，含補隙踏板與間隙）")] public Rect[] platformRects = new Rect[0];
        [Tooltip("沒有平台的車頂邊外推回線範圍（越過就淡出推回）")] public Rect[] fallRects = new Rect[0];
        [Tooltip("淡出推回後頭回到的 |X|：車頂可走範圍邊（底架旁放寬段另算）")] public float roofWalkHalfWidth = 0.8f, roofBaseWalkHalfWidth = 1.0f, roofBaseWalkZMin, roofBaseWalkZMax;

        public float RoofWalkLimitAt(float z) => z >= roofBaseWalkZMin - 1e-4f && z <= roofBaseWalkZMax + 1e-4f ? roofBaseWalkHalfWidth : roofWalkHalfWidth;

        public static BoundaryDef Load() => Resources.Load<BoundaryDef>(ResourceName);
    }
}
