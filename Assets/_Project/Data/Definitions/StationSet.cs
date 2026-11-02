using System;
using UnityEngine;

namespace PantographSim.Data
{
    // R1：座標系＝原點在弓頭中心正下方的車頂地板點，+Y 上、+Z 朝肘側滑板、平台在 −X 側，單位 m；標 EX 的是示例值。

    /// <summary>全部站點與建置檢查規則（EX）。</summary>
    [CreateAssetMenu(menuName = "PantographSim/R1/Station Set")]
    public class StationSet : ScriptableObject
    {
        public StationDef[] stations = Array.Empty<StationDef>();
        public string startStationId = "tp.platform.lift";

        [Header("建置檢查（StationLayoutCheck）")]
        public float clearRadius = 0.40f, invisibleWallClearance = 0.15f;
        public float eyeHeight = 1.60f, facingToleranceDeg = 30f;
        public float targetMinDistance = 0.40f, targetMaxDistance = 0.90f;
        [Tooltip("接地站到弓頭接地掛點互動點的水平距離上限＝絕緣桿長 − 0.5 m（D24：2.5 m 桿 → 2.0）")] public float groundClampMaxDistance = 2.0f;
        [Tooltip("D11：站點到平台護欄（封閉 1.1 m 護欄）的最低距離＝身體膠囊 0.25 m＋0.05 m")] public float guardrailClearance = 0.30f;
        [Tooltip("D11：接地站沿 Z 離活動欄開口邊緣的最低距離")] public float groundGateMinZ = 0.50f;
        [Tooltip("D11：接地站和 tp.platform.anchor 的最低距離")] public float groundAnchorMinDistance = 0.60f;
        [Tooltip("D28：駕駛室站點 tp.cab 的 Z 上限（相鄰端車車頭）")] public float cabMaxZ = -27f;
        [Tooltip("車頂站到兩側最遠一支螺栓的上限")] public float roofBoltMaxDistance = 1.00f;
        public float roofEdgeMinDistance = 0.50f;
        [Tooltip("車頂站點自己的螺栓盤距離範圍")] public float boltTrayMin = 0.30f, boltTrayMax = 0.50f;
        public float toolStorageMaxDistance = 0.80f;
        public float maxFloorStep = 0.15f, maxPlatformGap = 0.30f;

        [Header("共用車頂站點的移位（不合格時沿 +Z 每次 0.05 m，最多到 Z＝−0.50；仍不合格就顯示「側移一步」提示，不讓建置失敗）")]
        public float roofShiftStep = 0.05f, roofShiftMaxZ = -0.50f;
        [Tooltip("身體膠囊（半徑 0.2）和弓頭（含羊角）之間至少留的距離")] public float panHeadClearance = 0.15f;
        [Tooltip("建置時寫入：肘側最遠一支螺栓超過上限 → 教學模式啟用「往 n 號螺栓那側移一步」提示")] public bool sideStepHintEnabled;
        [Tooltip("建置時寫入的實測距離（m）")] public float measuredFarBoltL, measuredFarBoltR;

        public StationDef Find(string id) => Array.Find(stations, s => s != null && s.id == id);
    }
}
