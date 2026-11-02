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
        public float carBodyHalfWidth = 1.69f;   // R1.5a：700T 車寬 3.38（推估）
        public float roofEdgeNoWalk = 0.50f;
        public float roofSectionZMin = -4.0f, roofSectionZMax = 4.0f;
        public float roofFloorY = 0f;
        [Tooltip("車頂弧面兩側往下彎的量")] public float roofCurveDrop = 0.15f;
        [Tooltip("集電弓底架下的絕緣子高度（降弓後滑板頂面約在車頂上 0.6–0.7 m）")] public float pantographBaseHeight = 0.30f;
        [Tooltip("不可走物件（絕緣子、母線、底架、升降聯集）投影往外擴")] public float obstacleMargin = 0.15f;
        [Tooltip("車頂不可見邊界牆高度")] public float roofWallHeight = 1.2f;
        [Tooltip("車頂電纜／母線：沿 Z 從弓座往後的一條（中心 X、寬度）")] public float busbarX = 0.30f, busbarWidth = 0.20f;

        [Header("檢修平台")]
        public float platformGap = 0.12f;
        public float platformWidth = 1.50f;
        public float platformZMin = -4.0f, platformZMax = 4.0f;
        public float platformFloorY = 0f;
        public float guardrailHeight = 1.10f;
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
        [Tooltip("廠房屋頂桁架下緣高度")] public float shedTrussY = 8.0f;

        [Header("R1.5a 地面維修區（a4）")]
        public Vector2 maintMinXZ = new Vector2(-13.5f, -5f), maintMaxXZ = new Vector2(-5.5f, 5f);
        [Tooltip("台架 env.panto_stand 中心（XZ）")] public Vector2 standCenterXZ = new Vector2(-9.5f, 0f);
        [Tooltip("台架外形：X、Z")] public Vector2 standSizeXZ = new Vector2(2.0f, 2.6f);
        [Tooltip("台架頂面（支撐座、定位銷）離地")] public float standTopAboveGround = 0.75f;
        [Tooltip("F6：台架外框（支撐座四周與四角立柱）頂面離地，低於支撐座，懸出的臂和吊帶不會掃到")] public float standFrameTopAboveGround = 0.45f;
        [Tooltip("台架四周淨空（地面黃色走道線畫在這裡）")] public float standClearance = 1.2f;
        [Tooltip("tp.stand.* 離台架邊")] public float standStationOffset = 0.6f;
        [Tooltip("工作檯中心（XZ），檯面沿 Z 擺")] public Vector2 workbenchCenterXZ = new Vector2(-12.5f, 0f);
        [Tooltip("零件盒在地面維修區的位置（R1.5b 才把 env.parts_tray 移過來；這輪只放空物件 Anchor_PartsTray_Maint）")] public Vector2 partsTrayMaintXZ = new Vector2(-12.5f, -1.4f);

        [Header("R1.5a 天車、吊具、工具袋（a5，靜態）")]
        [Tooltip("D24：驗電器（掛位 2）與接地棒（掛位 3）的伸縮絕緣桿長度（EX）；接地站到弓頭接地掛點的上限＝這個 − 0.5")] public float insulatedPoleLength = 2.5f;
        [Tooltip("天車走行軌道 X（東、西）")] public float craneRunwayEastX = 3.0f, craneRunwayWestX = -16.0f;
        [Tooltip("走行軌道 Z 範圍 ±")] public float craneRunwayHalfLength = 15f;
        [Tooltip("走行軌道頂面高度（小車頂要在屋頂桁架下緣 8.0 以下）")] public float craneRailY = 6.3f;
        [Tooltip("本輪天車停的位置：橋 Z、小車 X")] public float craneParkZ = 12f, craneParkTrolleyX = 0f;
        [Tooltip("吊鉤最高點（離吊車橋的距離）")] public float hookTopBelowBridge = 1.2f;
        [Tooltip("吊具架（維修區地面）")] public Vector2 slingRackXZ = new Vector2(-6.5f, -3.8f);

        public float StandTopY => groundY + standTopAboveGround;

        public Vector3 FromInner(Vector3 v) => new Vector3(PlatformInnerX + v.x, v.y, v.z);
        public Vector3 FromOuter(Vector3 v) => new Vector3(PlatformOuterX + v.x, v.y, v.z);
        /// <summary>中間那節（有集電弓）的車長中點 Z：集電弓在 Z 0、離 −Z 車端 pantographFromCarEnd。</summary>
        public float MiddleCarCenterZ => -pantographFromCarEnd + carLength / 2f;

        // ---------- 推導值（建置腳本計算，不手填；車寬改了位置自動跟著變） ----------
        public float RoofEdgeX => carBodyHalfWidth;
        public float PlatformInnerX => -carBodyHalfWidth - platformGap;
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
                $"\"liveryWhite\": {C(liveryWhite)}", $"\"liveryOrange\": {C(liveryOrange)}", $"\"liveryDark\": {C(liveryDark)}",
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
