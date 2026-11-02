using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace PantographSim.XR
{
    /// <summary>
    /// T6 右搖桿平滑移動的設定與監看（移動本身是 XRI ActionBasedContinuousMoveProvider＋CharacterController）：
    /// ・開關 xr.move.on（預設 LocomotionSettings.smoothMoveDefaultOn）、速度 xr.move.speed（0.6–1.5，車頂上限 roofMaxSpeed）。
    /// ・遮罩強度 xr.vignette（0＝關）；移動時開啟。
    /// ・累計平滑移動距離與時間寫 xr.move.distance／xr.move.seconds（每秒一次，給紀錄的 summary.json）。
    /// ・房間規模走路會繞過 CharacterController：每幀看頭的位置，腳下沒有 Walkable、或車頂沒開放卻在車頂 → 四周變紅並提示，只提示不記分。
    /// </summary>
    public class XrLocomotion : MonoBehaviour
    {
        public const string OnKey = "xr.move.on", SpeedKey = "xr.move.speed", VignetteKey = "xr.vignette";
        public const string DistanceKey = "xr.move.distance", SecondsKey = "xr.move.seconds", DangerKey = "xr.danger";
        public const string DangerMessage = "已進入危險區域，請退回";

        [SerializeField] ContinuousMoveProviderBase move;
        [SerializeField] LocomotionSettings settings;
        [SerializeField] Transform head, origin;
        [SerializeField] XrVignette vignette;

        Vector3 lastPos;
        float distance, seconds, nextWrite;
        bool init, danger;

        public void Setup(ContinuousMoveProviderBase m, LocomotionSettings s, Transform headT, Transform originT, XrVignette v)
        {
            move = m; settings = s; head = headT; origin = originT; vignette = v;
        }

        /// <summary>這一刻的移動速度（純函式）：車頂上限 roofMax，其他夾在 min–max。</summary>
        public static float SpeedFor(float wanted, string floor, LocomotionSettings s)
        {
            float v = Mathf.Clamp(wanted, s.minSpeed, s.maxSpeed);
            return floor == "Roof" ? Mathf.Min(v, s.roofMaxSpeed) : v;
        }

        /// <summary>頭在危險區（純函式）：腳下沒有可走地板，或車頂沒開放卻站在車頂。</summary>
        public static bool IsDanger(bool overWalkable, string floorUnderHead, bool roofOpen) => !overWalkable || (floorUnderHead == "Roof" && !roofOpen);

        void Update()
        {
            var s = Sim.State;
            if (s == null || settings == null) return;
            if (!init)
            {
                init = true;
                if (!s.Has(OnKey)) s.SetBool(OnKey, settings.smoothMoveDefaultOn);
                if (!s.Has(SpeedKey)) s.SetFloat(SpeedKey, settings.speed);
                if (!s.Has(VignetteKey)) s.SetFloat(VignetteKey, settings.vignetteDefaultOn ? settings.vignetteStrength : 0f);
                distance = s.GetFloat(DistanceKey); seconds = s.GetFloat(SecondsKey);
                if (origin != null) lastPos = origin.position;
            }
            string floor = s.Get(XrViewHeight.FloorKey);
            bool on = s.GetBool(OnKey) && !SimSession.IsReplay;
            if (move != null)
            {
                if (move.enabled != on) move.enabled = on;
                move.moveSpeed = SpeedFor(s.GetFloat(SpeedKey, settings.speed), floor, settings);
            }

            // 只算搖桿推動時的水平位移（傳送不算）
            bool moving = false;
            if (origin != null)
            {
                var d = origin.position - lastPos; d.y = 0f;
                lastPos = origin.position;
                if (on && move != null && move.locomotionPhase == LocomotionPhase.Moving && d.magnitude < 0.5f)
                {
                    moving = d.sqrMagnitude > 1e-8f;
                    distance += d.magnitude; seconds += Time.deltaTime;
                }
            }
            if (Time.time >= nextWrite && (distance > 0f || seconds > 0f))
            {
                nextWrite = Time.time + 1f;
                s.Batch(() => { s.SetFloat(DistanceKey, Mathf.Round(distance * 100f) / 100f); s.SetFloat(SecondsKey, Mathf.Round(seconds * 10f) / 10f); });
            }

            // 危險區（只提示）
            if (head != null)
            {
                bool over = Physics.Raycast(head.position + Vector3.up * 0.1f, Vector3.down, out var hit, 4f, SceneLayers.WalkableMask, QueryTriggerInteraction.Ignore);
                bool now = IsDanger(over, over ? XrViewHeight.FloorOf(hit.collider.transform) : "", s.GetBool(LiftControl.RoofOpenKey));
                if (now != danger)
                {
                    danger = now;
                    s.SetBool(DangerKey, danger);
                    if (danger) { s.Set("ui.toast", ""); s.Set("ui.toast", DangerMessage); s.SetFloat("ui.toast_time", Time.time); }
                }
            }
            if (vignette != null) vignette.Show(danger ? 0.85f : moving ? s.GetFloat(VignetteKey) : 0f, danger);
        }
    }
}
