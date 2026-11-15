using PantographSim.Core;
using PantographSim.Data;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// terry 186（RA-21、RA-26）：頭穿進實心物體（HeadSolid 層）時，暗幕漸變到 maxAlpha 並在眼前顯示「請退後」；退出就恢復。只在 VR。
    /// 不記錯、不寫黑板鍵、不發事件、不移動人。暗幕和 XrFader 共用同一片（寫 Overlay，顯示取最大值）；提示文字在 HmdOnly 層，旁觀鏡頭看不到。
    /// 順序：頭部邊界推回（LateUpdate）、墜落區淡出推回之後才判斷；轉場淡黑或淡出推回進行中讓出暗幕，結束後 graceSec 內不判。
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class HeadSolidFade : MonoBehaviour
    {
        [SerializeField] XROrigin origin;
        [SerializeField] XrFader fader;
        [SerializeField] XrTeleport teleport;
        [SerializeField] RoofFallZoneMonitor fall;
        [SerializeField] TextMesh hint;
        HeadSolidDef def;
        HeadSolidLogic logic;
        Vector3? lastHead;
        const float JumpM = 1.0f;   // 一個 tick 頭移動超過這個距離＝被移過去（掉落救回等沒有事件的移動）

        public void Setup(XROrigin o, XrFader f, XrTeleport t, RoofFallZoneMonitor m, TextMesh h) { origin = o; fader = f; teleport = t; fall = m; hint = h; }

        void Awake() { def = HeadSolidDef.Load(); if (def != null) logic = new HeadSolidLogic(def); if (hint != null) hint.text = def != null ? def.hintText : ""; }
        void OnEnable() { if (teleport != null) teleport.Teleported += OnMoved; if (fall != null) fall.PushedBack += OnPushed; }
        void OnDisable() { if (teleport != null) teleport.Teleported -= OnMoved; if (fall != null) fall.PushedBack -= OnPushed; }
        void OnMoved(XrTeleportPoint p) => Reset();
        void OnPushed() => Reset();
        void Reset() { if (logic != null && origin != null && origin.Camera != null) logic.Moved(origin.Camera.transform.position, Time.time); }

        void LateUpdate()
        {
            if (logic == null || origin == null || origin.Camera == null) return;
            var s = Sim.State;
            var head = origin.Camera.transform.position;
            if (lastHead.HasValue && (head - lastHead.Value).magnitude > JumpM) logic.Moved(head, Time.time);
            lastHead = head;
            bool suppressed = s == null || !InputMode.XrActive || !s.GetBool(XrStartupSequencer.ReadyKey) || s.GetBool(SimIds.AutoRunActive) || SimSession.IsReplay;
            bool paused = (fader != null && fader.Alpha > 0.001f) || (fall != null && fall.Pushing);
            int mask = SceneLayers.HeadSolidMask;
            float a = logic.Tick(Time.time, Time.deltaTime, head, paused, suppressed,
                h => Physics.CheckSphere(h, def.headSolidRadius, mask, QueryTriggerInteraction.Collide),
                (from, to) => { var d = to - from; return d.sqrMagnitude < 1e-8f ? 0 : Physics.RaycastAll(from, d, d.magnitude, mask, QueryTriggerInteraction.Collide).Length; });
            if (fader != null) fader.Overlay = a;
            if (hint != null) hint.gameObject.SetActive(HeadSolidLogic.ShowHint(logic.Inside, fall != null ? fall.Zone : FallZone.Normal));
        }
    }
}
