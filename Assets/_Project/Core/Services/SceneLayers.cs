using UnityEngine;

namespace PantographSim.Core
{
    /// <summary>
    /// R1 場景圖層：Walkable（可走地板）、Boundary（護欄、支架、不可見邊界牆）、LocomotionBlocker（只擋角色控制器的不可見牆）。
    /// 點選與指物件的射線不打不可見的牆；傳送拋物線只打 Walkable 與 Boundary。圖層不存在時（舊場景）退回全部圖層。
    /// </summary>
    public static class SceneLayers
    {
        public const string Walkable = "Walkable", Boundary = "Boundary", LocomotionBlocker = "LocomotionBlocker";
        /// <summary>R1.5b：車頂可走範圍的不可見邊牆（EdgeWall_*、EndWall_*）。和 Boundary 一樣擋角色控制器與傳送射線，但不是實體障礙，天車碰撞停止不算它（軟體工程裁定：用 layer 排除，不用名稱比對）。</summary>
        public const string WalkLimit = "WalkLimit";
        /// <summary>R1.5b b2：吊物探測 collider（PAN_Root 的整體 Box，只在 Lifted 時開）。</summary>
        public const string LoadProbe = "LoadProbe";
        /// <summary>R1.5b D40：天車本體（橋架、台車、捲揚機、軌道）的 collider，只給吊鉤正上方的 BoxCast 用。</summary>
        public const string Crane = "Crane";
        /// <summary>terry 186：頭穿透判定用的碰撞框（只拿來查詢，和任何層都不互撞；所有射線都要排除它）。</summary>
        public const string HeadSolid = "HeadSolid";
        public static int HeadSolidMask => LayerMask.GetMask(HeadSolid);

        /// <summary>VR 試玩修正 第 1 點：擋手、工具和傳送指向線的實體邊界：護欄、圍牆、車頂邊（頭由 HeadBoundaryPusher 的水平範圍擋）。</summary>
        public static int BoundaryMask => LayerMask.GetMask(Boundary, WalkLimit);

        /// <summary>點選、指物件用：排除 Boundary 與 LocomotionBlocker。</summary>
        public static int InteractMask => ~LayerMask.GetMask(Boundary, LocomotionBlocker, HeadSolid);

        /// <summary>傳送拋物線：Walkable＋Boundary（沒有這些圖層時打全部）。</summary>
        public static int TeleportMask { get { int m = LayerMask.GetMask(Walkable, Boundary, WalkLimit); return m == 0 ? ~0 : m; } }

        /// <summary>站點落點、目前樓層判斷：只打 Walkable。</summary>
        public static int WalkableMask { get { int m = LayerMask.GetMask(Walkable); return m == 0 ? ~0 : m; } }
    }
}
