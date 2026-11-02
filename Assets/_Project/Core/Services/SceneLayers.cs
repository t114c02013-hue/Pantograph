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

        /// <summary>點選、指物件用：排除 Boundary 與 LocomotionBlocker。</summary>
        public static int InteractMask => ~LayerMask.GetMask(Boundary, LocomotionBlocker);

        /// <summary>傳送拋物線：Walkable＋Boundary（沒有這些圖層時打全部）。</summary>
        public static int TeleportMask { get { int m = LayerMask.GetMask(Walkable, Boundary); return m == 0 ? ~0 : m; } }

        /// <summary>站點落點、目前樓層判斷：只打 Walkable。</summary>
        public static int WalkableMask { get { int m = LayerMask.GetMask(Walkable); return m == 0 ? ~0 : m; } }
    }
}
