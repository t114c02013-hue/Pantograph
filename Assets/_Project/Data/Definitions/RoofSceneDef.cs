using System;
using UnityEngine;

namespace PantographSim.Data
{
    // R1：座標系＝原點在弓頭中心正下方的車頂地板點，+Y 上、+Z 朝肘側滑板、平台在 −X 側，單位 m；標 EX 的是示例值。

    // ---------- R1：真實高度場景、站點、車頂開放、移動、高度（全部是資料；建置腳本與執行期只讀這些資產） ----------
    // 座標系：原點 O＝弓頭中心正下方的車頂地板點；+Y 朝上；+Z 朝肘側滑板；平台在 −X 側；單位 m。標 EX 的是示例值。

    /// <summary>車頂、檢修平台、駕駛室、背景的尺寸與各物件位置（EX）。改尺寸只改這份資料、重跑建置。</summary>
    [CreateAssetMenu(menuName = "PantographSim/R1/Roof Scene")]
    public class RoofSceneDef : ScriptableObject
    {
        [Header("車頂")]
        public float carBodyHalfWidth = 1.325f;   // terry 2026-11-13：輕軌車寬 2.65（淡海輕軌公開資料）
        [Tooltip("可走範圍離車頂邊至少多遠。terry 2026-11-13 輕軌車頂窄（半寬 1.325），兩側都有平台＋1.1 m 欄杆＋補隙踏板，從 0.5 改 0.3（待鐵道確認）")] public float roofEdgeNoWalk = 0.30f;
        public float roofSectionZMin = -4.0f, roofSectionZMax = 4.0f;
        public float roofFloorY = 0f;
        [Tooltip("車頂弧面兩側往下彎的量")] public float roofCurveDrop = 0.15f;
        [Tooltip("集電弓底架下的絕緣子高度（降弓後滑板頂面約在車頂上 0.6–0.7 m）")] public float pantographBaseHeight = 0.30f;
        [Tooltip("不可走物件（絕緣子、母線、底架、升降聯集）投影往外擴")] public float obstacleMargin = 0.15f;
        [Tooltip("車頂不可見邊界牆高度")] public float roofWallHeight = 1.2f;
        [Tooltip("車頂電纜／母線：沿 Z 從弓座往後的一條（中心 X、寬度）")] public float busbarX = 0.30f, busbarWidth = 0.20f;

        [Header("檢修平台")]
        public float platformGap = 0.12f;
        [Tooltip("terry 2026-11-13 換輕軌：車變窄後平台不內移，靠車側加補隙踏板（可走）。平台靠車側邊＝車側＋platformGap＋這個寬度")] public float platformBoard = 0.365f;
        public float platformWidth = 1.50f;
        public float platformZMin = -4.0f, platformZMax = 4.0f;
        public float platformFloorY = 0f;
        public float guardrailHeight = 1.10f;
        [Tooltip("試玩修正 第 1 點（規格 v5）：A 車頂矮欄（跪著作業的地方）、B 一般護欄的高度（m）；collider 和看得見的網格一樣高")] public float roofEdgeRailHeight = 0.45f, gapRailHeight = 1.0f;
        public float gateCenterZ = -1.00f, gateWidth = 0.80f;

        [Header("其他樓層")]
        public float cabFloorY = -2.35f;   // D27：軌面 −3.65 ＋ 車廂地板 1.30
        public Vector3 cabCenter = new Vector3(0f, -2.35f, -30.0f);   // D28：相鄰端車車頭的駕駛室（本節車 Z −5.0…+20.0 之外），只能傳送進出
        public float cabSize = 1.5f, cabWallHeight = 2.2f;
        public float groundY = -3.65f;

        [Header("平台上的設備（EX）")]
        public Vector3 liftPanel = new Vector3(-1.60f, 1.10f, 0.00f);
        public Vector3 groundBusbar = new Vector3(-1.60f, 1.00f, 0.60f);
        public Vector3 testerSelfCheck = new Vector3(-1.60f, 1.10f, 0.30f);
        public Vector3 harnessPoint = new Vector3(-1.60f, 1.50f, -1.55f);
        [Tooltip("平台工具架（掛位 1–4），面朝 −X；y＝掛鉤最低高度")] public Vector3 platformToolRack = new Vector3(-1.60f, 0.90f, -2.50f);
        public float toolRackHookTop = 1.60f;

        [Header("車頂上的設備（EX）")]
        [Tooltip("車頂工具箱（掛位 5–9＋套筒座），面朝 −X")] public Vector3 roofToolbox = new Vector3(0.10f, 0.90f, -2.50f);
        public Vector3 partsTray = new Vector3(-0.30f, 0.90f, -1.90f);
        public Vector3 spareShelf = new Vector3(-0.30f, 0.90f, -3.10f);
        [Tooltip("螺栓盤：相對共用車頂站點的 X 偏移（R 在 +X、L 在 −X）與高度")] public float boltTrayOffset = 0.40f, boltTrayHeight = 0.90f;
        [Tooltip("車頂安全帶掛點")] public Vector3 roofAnchor = new Vector3(-0.60f, 0.10f, -1.60f);

        [Header("駕駛室（相對駕駛室中心）")]
        [Tooltip("控制盤在駕駛室 −X 牆、盤面朝 +X；高 0.9–1.2 m（相對駕駛室地板）")] public float cabPanelHeight = 1.05f;

        [Header("背景")]
        public int backdropMaxTriangles = 5000;

        // ---------- R1.5a：700T 車廂（推估值，拿到圖面後只改資料；Blender 只讀匯出的 car_params.json） ----------
        [Header("R1.5a 車廂（推估值）")]
        public float carLength = 25.0f;
        public int carCount = 3;
        [Tooltip("車頂邊相對中央平段的下降量")] public float roofEdgeDrop = 0.30f;
        [Tooltip("地板面到軌面的高度")] public float floorAboveRail = 1.30f;
        public float bogieCenterDistance = 17.5f;
        [Tooltip("集電弓到車端的距離")] public float pantographFromCarEnd = 5.0f;
        [Tooltip("車頂可走範圍 |X| ≤ 這個值")] public float roofWalkHalfWidth = 0.80f;
        public float insulatorHeight = 0.30f;
        [Tooltip("平台靠車側的通道寬")] public float platformAisle = 1.00f;
        [Tooltip("平台外側工具架帶的深度")] public float rackBandDepth = 0.50f;
        [Tooltip("車頂斷面：中央平段寬、兩側圓弧半徑")] public float roofFlatWidth = 1.6f, roofArcRadius = 1.2f;
        public Color liveryWhite = new Color32(0xF4, 0xF4, 0xF2, 0xFF), liveryOrange = new Color32(0xE8, 0x6A, 0x1E, 0xFF), liveryDark = new Color32(0x2B, 0x2B, 0x2B, 0xFF);
        [Tooltip("建置時量到的絕緣子位置（a3 由 PantographAssemblyBuilder 的 BaseHole 回寫）")] public Vector3[] insulatorPositions = new Vector3[4];

        [Header("R1.5a 平台設備（x＝相對平台靠車側邊 PlatformInnerX 或外側邊 PlatformOuterX 的偏移；y、z 是絕對值）")]
        public Vector3 liftPanelFromInner = new Vector3(0f, 1.10f, 0f);
        public Vector3 groundBusbarFromInner = new Vector3(0f, 1.00f, 0.35f);
        public Vector3 testerFromInner = new Vector3(0f, 1.10f, 0.85f);
        [Tooltip("平台安全帶掛點（ip.depot.platform_anchor），D27：車側護欄立柱 (−1.86, 1.20, −2.10)")] public Vector3 platformAnchorFromInner = new Vector3(-0.05f, 1.20f, -2.10f);
        [Tooltip("防墜器（門架頂）")] public Vector3 fallArresterFromInner = new Vector3(0f, 2.60f, -1.55f);
        [Tooltip("防墜器掛鉤（ip.roof.anchor 的互動點）")] public Vector3 roofAnchorHookFromInner = new Vector3(-0.14f, 1.40f, -1.55f);
        [Tooltip("防墜器門架柱（離站點至少 0.4 m）")] public Vector3 fallArresterPostFromInner = new Vector3(-0.05f, 0f, -2.20f);
        [Tooltip("工具架（掛位 1、4），靠外護欄、面向 +X")] public Vector3 toolRackFromOuter = new Vector3(0.25f, 1.00f, -2.80f);
        [Tooltip("長桿桿架 env.pole_rack（掛位 2 驗電器、3 接地棒，D25），靠外護欄")] public Vector3 poleRackFromOuter = new Vector3(0.15f, 0f, 0.60f);
        [Tooltip("遙控器掛座 PendantDock")] public Vector3 pendantDockFromOuter = new Vector3(0f, 1.10f, -3.50f);
        [Tooltip("車體接地座 Roof_CarBodyGround（互動點 Clamp_Point）")] public Vector3 carBodyGround = new Vector3(-0.70f, 0.05f, 1.20f);
        [Tooltip("避雷器")] public Vector3 arrester = new Vector3(0.55f, 0f, 2.60f);
        [Tooltip("控制線接頭 Car_Roof_Work/CtrlCable_End")] public Vector3 ctrlCableEnd = new Vector3(-0.35f, 0.05f, 2.10f);
        [Tooltip("三節車之間的間隙")] public float carGap = 0.6f;
        [Tooltip("terry 2026-11-13 輕軌：各模組長（m，頭車在兩端、集電弓在中間那個）。7.875／5.5／4.1 是同車系文件的推估")] public float[] lrvModules = { 7.875f, 5.5f, 4.1f, 5.5f, 7.875f };
        [Tooltip("模組之間的鉸接通道長（推估）")] public float lrvArticulation = 0.9f;
        [Tooltip("輕軌車身的水藍色帶")] public Color liveryAqua = new Color32(0x2E, 0xB0, 0xC8, 0xFF);
        [Tooltip("廠房屋頂桁架下緣高度")] public float shedTrussY = 8.0f;
        [Tooltip("terry 2026-11-13 室內日光燈感：主光（由上往下）強度、俯角（度）、顏色")] public float lightIntensity = 0.75f, lightPitchDeg = 75f;
        public Color lightColor = new Color(0.96f, 0.98f, 1f);
        [Tooltip("環境光（整體亮而平均）")] public Color ambientColor = new Color(0.68f, 0.70f, 0.72f);

        [Header("R1.5a 地面維修區（a4）")]
        public Vector2 maintMinXZ = new Vector2(-13.5f, -5f), maintMaxXZ = new Vector2(-5.5f, 5f);
        [Tooltip("台架 env.panto_stand 中心（XZ）")] public Vector2 standCenterXZ = new Vector2(-9.5f, 0f);
        [Tooltip("台架外形：X、Z")] public Vector2 standSizeXZ = new Vector2(2.0f, 2.6f);
        [Tooltip("R1.5b b4：定期檢修的螺栓盤在台架外框頂面：離中心的 X、從靠 tp.stand.s 的邊往內的 Z")] public float boltTrayStandOffsetX = 0.40f, boltTrayStandInsetZ = 0.15f;
        [Header("R1.5b b4 4c 架空線暫代物（建模 A model-spec v3.10 §8.3b；接觸線高度在 Courses.json 的 liftCapAboveO，D41）")]
        [Tooltip("接觸線、承力索沿 Z 從 O −值 到 O ＋值")] public float catenaryHalfLengthZ = 12.5f;
        [Tooltip("承力索在 O 點上方（m）")] public float catenaryMessengerAboveO = 2.75f;
        public float catenaryWireDiameter = 0.02f;
        [Tooltip("支柱：X（相對 O）、邊長、Y 從 O −3.65 到 O +3.30")] public float catenaryMastX = 3.10f, catenaryMastSize = 0.30f, catenaryMastYMin = -3.65f, catenaryMastYMax = 3.30f;
        [Tooltip("接地器 #E1／#E2：Z ±值（相對 O）、從接觸線斜拉到 +X 鋼軌（X、Y 相對 O）、桿徑、Inspect 方塊邊長")] public float earthingDeviceZ = 10f;
        public Vector2 earthingRailXY = new Vector2(0.72f, -3.65f);
        public float earthingDiameter = 0.04f, earthingInspectBox = 0.3f;
        [Tooltip("D23 天車駐車位 Crane_ParkPose：吊車台 X、橋架 Z（吊鉤 Y 讀 CraneProfile.hookYMax）")] public Vector2 craneParkXZ = new Vector2(-14.0f, 14.0f);
        [Tooltip("台架頂面（支撐座、定位銷）離地")] public float standTopAboveGround = 0.75f;
        [Tooltip("F6：台架外框（支撐座四周與四角立柱）頂面離地，低於支撐座，懸出的臂和吊帶不會掃到")] public float standFrameTopAboveGround = 0.45f;
        [Tooltip("a6 G2：台架外框＝降弓整組 LoweredTightBounds 兩端各外擴這個值（standSizeXZ 由建置寫入）")] public float standFrameMargin = 0.15f;
        [Tooltip("a6 增補 4：外框中心相對台架中心（＝底架中心）的偏移；LoweredTightBounds 前後不對稱，建置時寫入")] public Vector2 standFrameOffsetXZ;
        [Tooltip("增補 7 E3(1)：降弓整組 LoweredTightBounds（PAN_Root 局部、頂點算法、排除可拆件；建置時寫入）")] public Bounds panLoweredTightBounds;
        [Tooltip("增補 7 第 8 點（組長）：活動欄開口（車頂側）最小淨寬")] public float gateMinWidth = 0.60f;
        [Tooltip("R1.5b F1（資深軟體 B）：活動欄開口側擋欄 ENV_GateSliceGuard 的高度與第 22 項的容許差（高度、寬度都用這個容差）")] public float gateGuardHeight = 1.00f, gateGuardTolerance = 0.01f;
        [Tooltip("a6 G2：台架支撐座＝BaseHole 的 X、Z 間距各加這個值")] public Vector2 standSeatMarginXZ = new Vector2(0.13f, 0.18f);
        [Tooltip("台架四周淨空（地面黃色走道線畫在這裡）")] public float standClearance = 1.2f;
        [Tooltip("tp.stand.* 離台架邊")] public float standStationOffset = 0.6f;
        [Tooltip("工作檯中心（XZ），檯面沿 Z 擺")] public Vector2 workbenchCenterXZ = new Vector2(-12.5f, 0f);
        [Tooltip("零件盒在地面維修區的位置（R1.5b 才把 env.parts_tray 移過來；這輪只放空物件 Anchor_PartsTray_Maint）")] public Vector2 partsTrayMaintXZ = new Vector2(-12.5f, -1.4f);

        [Header("R1.5a 天車、吊具、工具袋（a5，靜態）")]
        [Tooltip("terry 手調：架上驗電桿、接地桿的長度縮放與離工具模型的高度（x＝驗電桿、y＝接地桿）")] public Vector2 rackPoleScale = new Vector2(0.12305149f, 0.1400058f), rackPoleLocalY = new Vector2(-0.118f, -0.151f);
        [Tooltip("D24：驗電器（掛位 2）與接地棒（掛位 3）的伸縮絕緣桿長度（EX）；接地站到弓頭接地掛點的上限＝這個 − 0.5")] public float insulatedPoleLength = 2.5f;
        [Tooltip("天車走行軌道 X（東、西）")] public float craneRunwayEastX = 3.0f, craneRunwayWestX = -16.0f;
        [Tooltip("走行軌道 Z 範圍 ±")] public float craneRunwayHalfLength = 15f;
        [Tooltip("走行軌道頂面高度（小車頂要在屋頂桁架下緣 8.0 以下）")] public float craneRailY = 6.3f;
        [Tooltip("本輪天車停的位置：橋 Z、小車 X")] public float craneParkZ = 12f, craneParkTrolleyX = 0f;
        [Tooltip("吊鉤最高點（離吊車橋的距離）")] public float hookTopBelowBridge = 1.2f;
        [Tooltip("吊具架（維修區地面）")] public Vector2 slingRackXZ = new Vector2(-6.5f, -3.8f);
        [Tooltip("v3.10（D44、model-spec v3.17）：平台吊具架 env.rigging_rack 中心（只在定期課；備用吊帶在架中間、停用牌在架左側）、紅框停用箱 env.sling_reject_bin 底面中心")] public Vector3 riggingRackPos = new Vector3(-2.30f, 1.20f, -3.92f), slingRejectBinPos = new Vector3(-3.10f, 0f, -3.85f);

        [Header("R1.5a a6（試玩回饋、D30、D32、日常滑板、車頂拆裝座）")]
        [Tooltip("「去駕駛室」地面光圈 mk.platform.to_cab（XZ、半徑）；標記，不是站點")] public Vector2 toCabMarkerXZ = new Vector2(-2.20f, -0.45f);
        public float toCabMarkerRadius = 0.25f;
        [Tooltip("日常課程備品架擺放點 Anchor_SpareShelf_Daily（中心；沿 Z 長 dailyRackLength）")] public Vector3 spareShelfDaily = new Vector3(-3.06f, 1.0f, 1.45f);
        [Tooltip("日常課程待報廢箱擺放點 Anchor_ScrapBox_Daily")] public Vector3 scrapBoxDaily = new Vector3(-3.06f, 0.5f, 2.85f);
        public float dailyRackLength = 1.3f;
        [Tooltip("車頂拆裝座：底架兩側局部放寬的可走範圍 |X| ≤ 這個值（D36：1.00）；Z 從 roofBaseWalkZMin 到底架後緣外擴 roofBaseWalkZMargin")] public float roofBaseWalkHalfWidth = 1.00f, roofBaseWalkZMargin = 0.15f;
        [Tooltip("D36：放寬帶前端 Z（往 −Z 延伸，讓 base_*.s 避開降弓羊角）")] public float roofBaseWalkZMin = -0.75f;
        [Tooltip("放寬帶後端 Z（底架後緣＋roofBaseWalkZMargin，建置時寫入）")] public float roofBaseWalkZMax = 1.9f;
        /// <summary>增補 6 第 3 點：站點在底架兩側放寬帶的 Z 範圍內就用 roofBaseWalkHalfWidth，否則 roofWalkHalfWidth。</summary>
        public float RoofWalkLimitAt(float z) => z >= roofBaseWalkZMin - 1e-4f && z <= roofBaseWalkZMax + 1e-4f ? roofBaseWalkHalfWidth : roofWalkHalfWidth;
        [Tooltip("底架兩側的警示紅線 |X|（鐵道規定，和 Walkable 寬度分開）")] public float roofBaseWarningX = 1.0f;
        [Tooltip("D36：底架站點的 |X|（離 Walkable 邊 invisibleWallClearance）；base_w／base_e 的 Z＝底架中心、.n 的 Z＝螺栓 3／4 實測 Z（建置時推導）、.s 的 Z＝roofBaseSouthZ")] public float roofBaseStationX = 0.85f, roofBaseSouthZ = -0.55f;
        [Tooltip("增補 6 第 7 點 A：Walkable 內側挖除的外擴量——不會動的底架與絕緣子（只挖自己的頂點範圍）、會動的臂／弓頭／連桿（升降全程）")] public float staticCarveInflate = 0f, movingCarveInflate = 0.15f;
        [Tooltip("車頂暫放墊 env.roof_strip_mat（model-spec v2.3 §8.6）：中心、長（X）× 寬（Z）、墊厚、邊框寬、邊框高出墊面、上方判定 trigger 高")]
        public Vector3 roofStripMatCenter = new Vector3(0f, 0f, -1.10f);   // 底面中心（FBX 原點），墊面在 Y＝thickness
        public Vector2 roofStripMatSize = new Vector2(1.20f, 0.30f);
        public float roofStripMatThickness = 0.01f, roofStripMatRimWidth = 0.02f, roofStripMatRimHeight = 0.02f, roofStripMatTriggerHeight = 0.10f;
        [Tooltip("a6 v4.3 第 11 點（車頂座標，鐵道修正）：控制線接頭 ip.pan_assy.ctrl_connector 的 XZ（Y＝底架頂面）、降弓固定帶綁點 HoldDown_TiePoint 的 XZ（Y＝下臂頂面）")]
        public Vector2 ctrlConnectorXZ = new Vector2(0.50f, 0.95f), holdDownTieXZ = new Vector2(0.30f, 1.20f);
        [Tooltip("增補 2：PAN_CtrlConnector 參數（座 X×Y×Z、插座直徑／長、蓋直徑／長）")] public Vector3 ctrlConnectorBodySize = new Vector3(0.10f, 0.08f, 0.08f);
        public float ctrlSocketDiameter = 0.040f, ctrlSocketLength = 0.050f, ctrlCapDiameter = 0.046f, ctrlCapLength = 0.020f;
        [Tooltip("增補 2：PAN_HoldDownStrap 參數；armHalfWidth＝實測下臂在綁點 Z 的 X 半寬＋0.005（建置時寫入）")] public float strapWidth = 0.050f, strapThickness = 0.003f, strapArmHalfWidth = 0.035f, strapRollDiameter = 0.07f;
        public Vector3 strapBuckleSize = new Vector3(0.06f, 0.02f, 0.08f);
        [Tooltip("D30：腰包 env.waist_pouch（相對 XR Origin；x＞0＝右腰）")] public Vector3 waistPouchLocal = new Vector3(0.18f, 0.95f, 0.05f);
        [Tooltip("R1.5b b3：隨身容器相對頭部的位置（頭下方 0.65 m；工具袋左腰、腰包右腰，選單可對調；只跟著頭的 yaw 轉）")] public Vector3 toolBagHeadOffset = new Vector3(-0.2f, -0.65f, 0.1f), waistPouchHeadOffset = new Vector3(0.2f, -0.65f, 0.05f);
        [Tooltip("R1.5b b3：右手在隨身容器這個範圍（m）內＝袋子打開、顯示每格名稱")] public float carryOpenRange = 0.25f;
        [Tooltip("R1.5b b4（D31）：安全帶胸前收納扣環（掛鉤收起／取下）、安全帶工具環（短接地線掛上／拿下）相對頭部的位置（只跟著頭的 yaw 轉），和腰包、工具袋至少隔 0.15 m")]
        public Vector3 chestRingHeadOffset = new Vector3(0f, -0.35f, 0.15f), toolRingHeadOffset = new Vector3(0.12f, -0.50f, 0.15f);
        [Tooltip("D31：兩個操作點的大小（m，方塊邊長）、手在這個半徑內按握把才算（m）")] public float bodyRingSize = 0.05f, bodyRingRadius = 0.08f;
        [Tooltip("D30：防墜器繩子的視覺長度（不改判定）")] public float fallArresterLineLength = 6.0f;   // 增補 3：捲收器標示 6 m；單位長 1 m 的 Ø10 mm 細圓柱縮放到 D 環
        public float fallArresterLineDiameter = 0.010f;
        [Tooltip("D32：遙控器掛座固定在外護欄立柱的橫臂長度")] public float pendantDockArmLength = 0.25f;

        [Tooltip("增補 3（D33）：車頂防墜器背部 D 環離車頂面的高度（跪姿、站姿）")] public float dRingKneelHeight = 0.90f, dRingStandHeight = 1.35f;
        [Tooltip("增補 3（D34）：車體接地座焊接小平台邊長、接地螺樁高")] public float carBodyGroundPadSize = 0.10f, carBodyGroundStudHeight = 0.03f;

        public float StandTopY => groundY + standTopAboveGround;
        /// <summary>增補 7 第 8 點：底架放寬帶前端伸進活動欄開口的那一段（EdgeWall_-X_GateSlice）的長度；0＝沒有伸進去。</summary>
        public float GateSliceWidth => Mathf.Max(0f, gateCenterZ + gateWidth / 2f - roofBaseWalkZMin);
        /// <summary>台架外框中心（XZ）：台架中心＋standFrameOffsetXZ。tp.stand.* 和外框的距離都量到這個框。</summary>
        public Vector2 StandFrameCenterXZ => standCenterXZ + standFrameOffsetXZ;

        /// <summary>車頂斷面在 x 的高度（和 Tools/Blender/gen_700t/car_body.py 的 profile 同一條式子：中央平段，兩側圓弧縮放到車頂邊下降 roofEdgeDrop）。</summary>
        public float RoofSurfaceY(float x)
        {
            float flat = roofFlatWidth / 2f, ax = Mathf.Abs(x);
            if (ax <= flat) return roofFloorY;
            float Raw(float v) => -roofArcRadius + Mathf.Sqrt(Mathf.Max(roofArcRadius * roofArcRadius - (v - flat) * (v - flat), 0f));
            float edge = Raw(carBodyHalfWidth) < 0f ? Raw(carBodyHalfWidth) : -1e-3f;
            return roofFloorY + roofEdgeDrop * Raw(Mathf.Min(ax, carBodyHalfWidth)) / -edge;
        }

        public Vector3 FromInner(Vector3 v) => new Vector3(PlatformInnerX + v.x, v.y, v.z);
        public Vector3 FromOuter(Vector3 v) => new Vector3(PlatformOuterX + v.x, v.y, v.z);
        /// <summary>中間那節（有集電弓）的車長中點 Z：集電弓在 Z 0、離 −Z 車端 pantographFromCarEnd。</summary>
        public float MiddleCarCenterZ => -pantographFromCarEnd + carLength / 2f;

        // ---------- 推導值（建置腳本計算，不手填；車寬改了位置自動跟著變） ----------
        public float RoofEdgeX => carBodyHalfWidth;
        public float PlatformInnerX => -carBodyHalfWidth - platformGap - platformBoard;
        /// <summary>補隙踏板靠車側的邊（踏板 x 從 PlatformInnerX 到這裡）。</summary>
        public float PlatformBoardEdgeX => -carBodyHalfWidth - platformGap;
        public float PlatformOuterX => PlatformInnerX - platformWidth;
        public float RackBandMinX => PlatformOuterX;
        public float RackBandMaxX => PlatformOuterX + rackBandDepth;
        public float RackBandCenterX => PlatformOuterX + rackBandDepth / 2f;
        public float GateBridgeMinX => PlatformInnerX;
        public float GateBridgeMaxX => -roofWalkHalfWidth;

        /// <summary>給 Blender 的參數（car_params.json，鍵名固定、數字用不變文化格式）。</summary>
        public string ToParamsJson()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string F(float v) => v.ToString("0.####", ci);
            string C(Color c) => "\"#" + ColorUtility.ToHtmlStringRGB(c) + "\"";
            var body = string.Join(",\n  ", new[]
            {
                $"\"carBodyHalfWidth\": {F(carBodyHalfWidth)}", $"\"carLength\": {F(carLength)}", $"\"carCount\": {carCount}", $"\"groundY\": {F(groundY)}",
                $"\"roofFloorY\": {F(roofFloorY)}", $"\"roofEdgeDrop\": {F(roofEdgeDrop)}", $"\"floorAboveRail\": {F(floorAboveRail)}", $"\"bogieCenterDistance\": {F(bogieCenterDistance)}",
                $"\"pantographFromCarEnd\": {F(pantographFromCarEnd)}", $"\"roofWalkHalfWidth\": {F(roofWalkHalfWidth)}", $"\"insulatorHeight\": {F(insulatorHeight)}",
                $"\"platformGap\": {F(platformGap)}", $"\"platformWidth\": {F(platformWidth)}", $"\"platformAisle\": {F(platformAisle)}", $"\"rackBandDepth\": {F(rackBandDepth)}",
                $"\"platformZMin\": {F(platformZMin)}", $"\"platformZMax\": {F(platformZMax)}", $"\"guardrailHeight\": {F(guardrailHeight)}",
                $"\"gateCenterZ\": {F(gateCenterZ)}", $"\"gateWidth\": {F(gateWidth)}", $"\"roofFlatWidth\": {F(roofFlatWidth)}", $"\"roofArcRadius\": {F(roofArcRadius)}",
                $"\"insulatedPoleLength\": {F(insulatedPoleLength)}",
                $"\"standTopAboveGround\": {F(standTopAboveGround)}", $"\"standFrameTopAboveGround\": {F(standFrameTopAboveGround)}",
                $"\"liveryWhite\": {C(liveryWhite)}", $"\"liveryOrange\": {C(liveryOrange)}", $"\"liveryDark\": {C(liveryDark)}", $"\"liveryAqua\": {C(liveryAqua)}",
                $"\"platformBoard\": {F(platformBoard)}", $"\"lrvArticulation\": {F(lrvArticulation)}", $"\"roofSectionZMax\": {F(roofSectionZMax)}",
                $"\"lrvModules\": [{string.Join(", ", System.Array.ConvertAll(lrvModules ?? new float[0], F))}]",
                $"\"roofStripMat\": {{ \"center\": [{F(roofStripMatCenter.x)}, {F(roofStripMatCenter.y)}, {F(roofStripMatCenter.z)}], \"size\": [{F(roofStripMatSize.x)}, {F(roofStripMatSize.y)}], " +
                $"\"thickness\": {F(roofStripMatThickness)}, \"rimWidth\": {F(roofStripMatRimWidth)}, \"rimHeight\": {F(roofStripMatRimHeight)} }}",
                $"\"ctrlConnector\": {{ \"bodySize\": [{F(ctrlConnectorBodySize.x)}, {F(ctrlConnectorBodySize.y)}, {F(ctrlConnectorBodySize.z)}], \"socketDiameter\": {F(ctrlSocketDiameter)}, \"socketLength\": {F(ctrlSocketLength)}, " +
                $"\"capDiameter\": {F(ctrlCapDiameter)}, \"capLength\": {F(ctrlCapLength)} }}",
                $"\"gateSliceGuard\": {{ \"width\": {F(GateSliceWidth)}, \"height\": {F(gateGuardHeight)} }}",
                $"\"holdDownStrap\": {{ \"strapWidth\": {F(strapWidth)}, \"strapThickness\": {F(strapThickness)}, \"armHalfWidth\": {F(strapArmHalfWidth)}, " +
                $"\"buckleSize\": [{F(strapBuckleSize.x)}, {F(strapBuckleSize.y)}, {F(strapBuckleSize.z)}], \"rollDiameter\": {F(strapRollDiameter)} }}",
            });
            return "{\n  " + body + "\n}";
        }

        /// <summary>參數的雜湊（SHA-256 前 16 碼）：改了資料卻忘了重跑 Blender 時測試會抓到。</summary>
        public string ParamsHash()
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return System.BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(ToParamsJson()))).Replace("-", "").Substring(0, 16);
        }
    }
}
