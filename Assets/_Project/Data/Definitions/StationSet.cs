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
        [Tooltip("D37＋增補 6 第 7 點 G：站點離各會動零件升降範圍的淨空（partGroup＝段名或名稱開頭，例 PanHead、Rod）；清單沒列的零件用 clearRadius")]
        public PartClearRadius[] partClearRadius = Array.Empty<PartClearRadius>();

        /// <summary>站點（樓層 floor）離會動零件 segment 升降範圍要留的淨空：清單裡第一筆名稱相符、範圍適用的；沒有就用 clearRadius。</summary>
        public float ClearRadiusFor(string segment, StationFloor floor)
        {
            foreach (var p in partClearRadius ?? Array.Empty<PartClearRadius>())
                if (p != null && !string.IsNullOrEmpty(p.partGroup) && (segment == p.partGroup || segment.StartsWith(p.partGroup)) && (p.scope != PartClearScope.RoofOnly || floor == StationFloor.Roof))
                    return p.radius;
            return clearRadius;
        }
        public float eyeHeight = 1.60f, facingToleranceDeg = 30f;
        public float targetMinDistance = 0.40f, targetMaxDistance = 0.90f;
        [Tooltip("接地站到弓頭接地掛點互動點的水平距離上限＝絕緣桿長 − 0.5 m（D24：2.5 m 桿 → 2.0）")] public float groundClampMaxDistance = 2.0f;
        [Tooltip("D11：站點到平台護欄（封閉 1.1 m 護欄）的最低距離＝身體膠囊 0.25 m＋0.05 m")] public float guardrailClearance = 0.30f;
        [Tooltip("D11：接地站沿 Z 離活動欄開口邊緣的最低距離")] public float groundGateMinZ = 0.50f;
        [Tooltip("D11：接地站和 tp.platform.anchor 的最低距離")] public float groundAnchorMinDistance = 0.60f;
        [Tooltip("terry 2026-11-13 輕軌：駕駛室站點 tp.cab 的 Z 上限（這列車 −Z 端的頭車駕駛室）")] public float cabMaxZ = -15.5f;
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

        [Header("a6（a6 補充定案後的門檻）")]
        [Tooltip("a6 第1項：平台站點兩兩水平距離下限（sharedWith 互連的不算）")] public float platformStationMinSpacing = 0.60f;
        [Tooltip("a6 第2項：站點離活動欄開口（GateBoundary）的水平距離下限")] public float gateFrontClearance = 0.40f;
        [Tooltip("a6 第4項(d)（D25）：長桿桿架 env.pole_rack 到 tp.platform.ground 的水平距離上限")] public float poleRackGroundMaxDistance = 1.00f;
        [Tooltip("a6 第5項：吊運路徑（弓座 (0,0) 到台架中心）兩側各加寬")] public float liftCorridorHalfWidth = 1.00f;
        [Tooltip("a6 第5項：tp.platform.crane 離吊運路徑的水平距離下限")] public float craneCorridorMinDistance = 2.00f;
        [Tooltip("a6 第6項：tp.stand.* 的 Y 和地面 groundY 的容許差")] public float standStationYTolerance = 0.02f;
        [Tooltip("a6 第6項：tp.stand.* 到台架邊距離和 standStationOffset 的容許差")] public float standStationOffsetTolerance = 0.05f;
        [Tooltip("a6 第8項：車體接地座到 tp.roof.base_w 的水平距離上限")] public float carBodyGroundMaxDistance = 0.80f;
        [Tooltip("R1.5b b4（D43，鐵道）：接地 #1 的接地線到 1 號絕緣子至少多遠（m）")] public float groundLineInsulatorClearance = 0.10f;
        [Tooltip("a6 第9項：tp.roof.base_e 到 HV_Cable_End 的水平距離上限")] public float baseECableMaxDistance = 1.30f;
        [Tooltip("a6 第10項(b)：平台鋼梯 ENV_PlatformStair 的 Z 範圍")] public float stairMinZ = 4.0f, stairMaxZ = 10.0f;
        [Tooltip("a6 第11項：降弓整組放上台架後（底架底面除外）到台架外框頂面的垂直距離下限")] public float standFrameMinClearance = 0.30f;

        [Header("a6 補充 v4.3／增補 1（定案門檻）")]
        [Tooltip("第10b項：鋼梯和扶手可以伸進平台上方的容差（接口碰到平台護欄是正常的）")] public float stairOverlapTolerance = 0.02f;
        [Tooltip("第13項：活動欄開口中心到防墜器掛鉤、桿架、工具架的水平距離上限")] public float roofEntryMaxDistance = 2.0f;
        [Tooltip("第14項：去駕駛室光圈到 tp.platform.lift 的水平距離上限")] public float toCabLiftMaxDistance = 0.50f;
        [Tooltip("第15項：NPC 平時站位到設備與站點的最低距離、離活動欄開口前方的最低距離、吊運中三人彼此與到工作檯／台架淨空帶／鋼梯的最低距離")]
        public float npcClearance = 0.60f, npcGateFront = 0.40f;
        [Tooltip("吊物禁區（XZ；平台側 Z ±2.25，已從吊物外緣外擴 1.0 m，不要再外推）")] public Vector2 liftZoneMinXZ = new Vector2(-12.0f, -2.25f), liftZoneMaxXZ = new Vector2(1.69f, 2.25f);
        [Tooltip("第16項：車頂拆裝座到同側最遠底座螺栓的上限")] public float baseBoltMaxDistance = 1.0f;
        // shortcut: headMaxAbsX 已經沒有檢查用到（伸手估計改用 reachHeadMaxAbsX），留著是為了不動資產序列化, R1.5b 收尾時刪除
        [Tooltip("未使用，已由 reachHeadMaxAbsX 取代（D36）")] public float headMaxAbsX = 1.10f;
        [Tooltip("第17項：暫放墊要在 |X| ≤ 這個值內、前緣離最近車頂站點的下限")] public float stripMatMaxAbsX = 0.80f, stripMatStationMin = 0.25f;
        [Tooltip("第18項：弓側節點到 tp.roof.base_e 的上限、接頭到 HV_Cable_End 的下限")] public float sideNodeBaseEMax = 0.80f, connectorCableMin = 0.30f;
        [Tooltip("第19項：PendantDock 到 tp.platform.crane 的上限、和工具架 Bounds 的間距下限與浮點容差")] public float pendantCraneMax = 1.0f, pendantRackGap = 0.15f, pendantRackGapTolerance = 0.005f;
        [Tooltip("第16項（D33）：子站點到自己那支螺栓的水平距離上限")] public float subStationBoltMax = 0.60f;
        [Tooltip("D34：車體接地座 X 上限、站點立足圓半徑、跪姿區（站點面向方向往前的深度與半寬）")] public float carBodyGroundMaxX = -1.05f, stationFootRadius = 0.25f, kneelZoneDepth = 0.60f, kneelZoneHalfWidth = 0.25f;
        [Tooltip("第七節 1：吊物底面到路徑下方固定物頂面的最低餘裕")] public float liftObstacleMargin = 0.50f;
        [Tooltip("第七節 1（鐵道 6.1）：吊鉤在上限位時吊物底面的最低高度 Y")] public float liftBottomMinY = 3.1f;
        [Tooltip("◇ 待定（增補 4 第 9 點、增補 6 第 1 點）：不合格的（檢查項＋物件 ID）完全相等時改列「待定」，不讓建置失敗；每一筆都要有 ruleRef、decidedBy、releaseSegment，缺一項就建置失敗")]
        public PendingRuling[] pendingRulings = Array.Empty<PendingRuling>();
        [Tooltip("增補 6 第 2 點：離不可見牆的量測容差（剛好等於門檻算合格；OverlapCapsule 接觸也算重疊）")] public float wallClearanceTolerance = 0.001f;
        [Tooltip("增補 6 第 7 點 B：站點中心到不會動的底架、絕緣子頂點範圍的水平距離下限（身體半徑 0.2＋0.05）")] public float staticPartClearance = 0.25f;
        [Tooltip("第16項 (h)：站點和降弓羊角頂點範圍之間要留的身體半徑")] public float hornBodyRadius = 0.20f;
        [Tooltip("第16項 (j) 等伸手估計：伸手時頭部 |X| 上限、站姿手臂可及的水平距離、短絕緣桿的水平可及（EX，R1.5b 拿到桿長再改）")] public float reachHeadMaxAbsX = 1.0f, armReachHorizontal = 0.55f, shortPoleReachHorizontal = 0.80f;
        [Tooltip("第21項（D36 補充三）：扳手從起始方向往兩邊各擺的角度、取樣步距（度）")] public float wrenchSwingDeg = 45f, wrenchSampleDeg = 5f;
        [Tooltip("第21項（D38）：扳手把手擺動範圍的最小淨空；半徑方向的取樣步距（m）")] public float wrenchSweepMinClearance = 0.02f, wrenchRadialStep = 0.05f;
        [Tooltip("第21項（增補 7 第 3 點）：要算擺動淨空的扳手（ToolDefinition.toolId；底座螺栓 CSV v2.9：RM_04 b、IN_06 d 用套筒扳手，IN_07 d 用大扭力扳手）")] public string[] wrenchSweepTools = Array.Empty<string>();
        [Tooltip("第19項：到 tp.platform.crane 的量法＝站點到掛座 Bounds 中心的水平距離（增補 4 第 2 點）")] public bool pendantCraneToCenter = true;
        [Tooltip("第15項：NPC 各階段站位（平時／吊運中）")] public NpcStandPose[] npcPoses = Array.Empty<NpcStandPose>();

        public StationDef Find(string id) => Array.Find(stations, s => s != null && s.id == id);
    }

    /// <summary>◇ 待定的裁定紀錄（增補 6 第 1 點）：checkItem（例 "15"）＋objectId（例 "tp.roof.base_w.s"）和不合格項目完全相等才算。</summary>
    [Serializable]
    public class PendingRuling
    {
        public string checkItem, objectId, ruleRef, decidedBy, releaseSegment;
        public bool HasRecord => !string.IsNullOrWhiteSpace(checkItem) && !string.IsNullOrWhiteSpace(objectId) && !string.IsNullOrWhiteSpace(ruleRef) && !string.IsNullOrWhiteSpace(decidedBy) && !string.IsNullOrWhiteSpace(releaseSegment);
        public bool Matches(string item, string obj) => checkItem == item && objectId == obj;
        public override string ToString() => $"第{checkItem}項 {objectId}（{ruleRef}，{decidedBy}，預計 {releaseSegment} 解除）";
    }

    public enum PartClearScope { AllStations, RoofOnly }

    /// <summary>D37：零件別的站點淨空（例 羊角 0.2 只限車頂站點）。</summary>
    [Serializable]
    public class PartClearRadius
    {
        public string partGroup;
        public float radius;
        public PartClearScope scope;
    }

    public static class NpcPhase { public const string Normal = "平時", Lifting = "吊運中"; }

    /// <summary>a6 第15項：NPC 站位預留（NPC 本身 R1.5b 才建）。phase＝NpcPhase；吊運中＝停留點 2 放行到 LF_06、停留點 3 放行到 IN_04（只有定期課程）。
    /// shortcut: 規格寫在 HoldPointDef 的 NPC 資料，專案還沒有 HoldPointDef, R1.5b 建停留點資料時搬過去。</summary>
    [Serializable]
    public class NpcStandPose
    {
        public string npcId, phase;
        public Vector3 position;
    }
}
