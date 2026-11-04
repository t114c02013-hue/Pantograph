using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// 移動靠站點傳送加真實走動（試玩修正 第 1 點拿掉右搖桿平滑移動；撞邊界由 HeadBoundaryPusher 擋）。這裡只留：
    /// ・車頂沒開放卻在車頂、腳下沒有可走地板 → 提示（不變暗、不拉回）。
    /// ・安全網：XR Origin 低於目前站點地板 recoverBelowFloorM → 放回站點並寫 Log；有實體邊界之後正常不會發生。不記錯，原本該記的錯誤照舊。
    /// </summary>
    public class XrLocomotion : MonoBehaviour
    {
        public const string OnKey = "xr.move.on", SpeedKey = "xr.move.speed", DistanceKey = "xr.move.distance", SecondsKey = "xr.move.seconds", DangerKey = "xr.danger";
        public const string DangerMessage = "已進入危險區域，請退回", RecoveredMessage = "已回到站點";

        [SerializeField] LocomotionSettings settings;
        [SerializeField] Transform head, origin;
        [SerializeField] XrTeleport teleport;

        bool danger;

        public void Setup(LocomotionSettings s, Transform headT, Transform originT, XrTeleport tp)
        {
            settings = s; head = headT; origin = originT; teleport = tp;
        }

        /// <summary>shortcut: 平滑移動拿掉後沒有人呼叫, R1.5b 收尾和 smoothMoveDefaultOn 一起刪。這一刻的移動速度（純函式）：車頂上限 roofMax，其他夾在 min–max。</summary>
        public static float SpeedFor(float wanted, string floor, LocomotionSettings s)
        {
            float v = Mathf.Clamp(wanted, s.minSpeed, s.maxSpeed);
            return floor == "Roof" ? Mathf.Min(v, s.roofMaxSpeed) : v;
        }

        /// <summary>頭在危險區（純函式）：腳下沒有可走地板，或車頂沒開放卻站在車頂。</summary>
        public static bool IsDanger(bool overWalkable, string floorUnderHead, bool roofOpen) => !overWalkable || (floorUnderHead == "Roof" && !roofOpen);

        /// <summary>安全網（純函式）：Origin 比站點地板低超過 limit。</summary>
        public static bool BelowFloor(float originY, float stationY, float limit) => originY < stationY - limit;

        void Update()
        {
            var s = Sim.State;
            if (s == null || settings == null || head == null || !s.GetBool(XrStartupSequencer.ReadyKey)) return;

            var at = teleport != null ? teleport.Current : null;
            if (at != null && origin != null && BelowFloor(origin.position.y, at.transform.position.y, settings.recoverBelowFloorM))
            {
                Debug.LogWarning($"[XR] 安全網：XR Origin Y {origin.position.y:0.00} 低於站點 {at.PointId} 地板，放回站點");
                teleport.TeleportTo(at.transform.position, at.transform.rotation, at, immediate: true);
                s.Set("ui.toast", ""); s.Set("ui.toast", RecoveredMessage); s.SetFloat("ui.toast_time", Time.time);
            }

            // 危險區（只提示）
            bool over = Physics.Raycast(head.position + Vector3.up * 0.1f, Vector3.down, out var hit, 4f, SceneLayers.WalkableMask, QueryTriggerInteraction.Ignore);
            bool now = IsDanger(over, over ? XrViewHeight.FloorOf(hit.collider.transform) : "", s.GetBool(LiftControl.RoofOpenKey));
            if (now != danger)
            {
                danger = now;
                s.SetBool(DangerKey, danger);
                if (danger) { s.Set("ui.toast", ""); s.Set("ui.toast", DangerMessage); s.SetFloat("ui.toast_time", Time.time); }
            }
        }
    }
}
