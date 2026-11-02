using System;
using UnityEngine;

namespace PantographSim.Data
{
    // R1：座標系＝原點在弓頭中心正下方的車頂地板點，+Y 上、+Z 朝肘側滑板、平台在 −X 側，單位 m；標 EX 的是示例值。

    public enum StationFloor { Cab, Platform, Roof, Ground }   // Ground＝R1.5a 地面維修區（新增在最後，舊資產的值不變）

    /// <summary>
    /// 一個站點（傳送點）。x、z 寫在資料裡（建置時產生 StationAnchor_&lt;id&gt; 節點）；y 由建置腳本往下打 Walkable 決定；
    /// 朝向在建置時對 facingTargetId 算出。unlockKey＝開放條件的黑板鍵（空＝隨時開放；車頂＝roof.open，由 RoofAccessRule 判定後寫入）。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/R1/Station")]
    public class StationDef : ScriptableObject
    {
        public string id;
        public string label;
        public StationFloor floor;
        public Vector2 positionXZ;
        public string facingTargetId;
        [Tooltip("水平距離上限（預設 0.4–0.9 m）；0＝用預設")] public float maxTargetDistance;
        public string unlockKey;
        [TextArea] public string lockedMessage;
        [Tooltip("和另一站共用同一點（地上只顯示一個標記）")] public string sharedWith;
        [Tooltip("T3：步驟提示面板相對站點的預設偏移")] public Vector3 panelOffset = new Vector3(-0.55f, 1.35f, 1.05f);
        [Tooltip("R1.5a：positionXZ.x 是相對平台靠車側邊（RoofSceneDef.PlatformInnerX）的偏移，車寬改了站點自動跟著變")] public bool relativeToPlatformInner;
        [Tooltip("R1.5a：固定朝向（不為 0 時取代 facingTargetId，例 tp.platform.crane 面向 +X）")] public Vector3 facingDirection;
        [Tooltip("R1.5a a4：所屬作業區（zone.arrival／zone.maintenance）")] public string zoneId = ZoneDef.Arrival;

        /// <summary>站點的世界 XZ（平台站點加上平台靠車側邊）。</summary>
        public Vector2 WorldXZ(RoofSceneDef d) => relativeToPlatformInner && d != null ? new Vector2(d.PlatformInnerX + positionXZ.x, positionXZ.y) : positionXZ;
    }
}
