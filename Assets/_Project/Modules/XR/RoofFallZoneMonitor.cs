using System.Collections;
using PantographSim.Core;
using PantographSim.Data;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// terry 134B／185（2026-11-15）：車頂墜落區（只在 VR）。每個 tick 把頭的位置、目前站點是不是車頂、目前步驟交給 FallZoneLogic；
    /// 要記錯就寫 xr.roof_fall_zone（先寫 false 再寫 true，WhenCondition 由假變真才記）；越過推回線就淡出推回。
    /// 車頂上的危險區提示由這裡統一出（XrLocomotion 只在不是車頂的地方提示）。桌機不判（沒有頭的位置）。
    /// </summary>
    public class RoofFallZoneMonitor : MonoBehaviour
    {
        public const string Key = "xr.roof_fall_zone", PointKey = "xr.point";

        [SerializeField] XROrigin origin;
        [SerializeField] XrPlayer player;
        [SerializeField] XrFader fader;
        [SerializeField] XrTeleport teleport;
        BoundaryDef def;
        FallZoneLogic logic;
        bool pushing;
        FallZone zone;
        /// <summary>terry 186：淡出推回進行中（HeadSolidFade 暫停）；目前區域（墜落區提示優先）。</summary>
        public bool Pushing => pushing;
        public FallZone Zone => zone;
        /// <summary>淡出推回剛結束（HeadSolidFade 用來重設最後在外面的位置）。</summary>
        public event System.Action PushedBack;

        public void Setup(XROrigin o, XrPlayer p, XrFader f, XrTeleport t) { origin = o; player = p; fader = f; teleport = t; }

        /// <summary>目前站點是車頂（只看站點，不看頭下方的地板）。</summary>
        public static bool OnRoof(ISimState s) => s != null && s.Get(PointKey).StartsWith("tp.roof.");

        /// <summary>記一次錯：先 false 再 true（分開兩次寫入）。</summary>
        public static void Write(ISimState s) { s.SetBool(Key, false); s.SetBool(Key, true); }

        void Awake() { def = BoundaryDef.Load(); if (def != null) logic = new FallZoneLogic(def); }
        void OnEnable() { if (teleport != null) teleport.Teleported += OnTeleported; }
        void OnDisable() { if (teleport != null) teleport.Teleported -= OnTeleported; }
        void OnTeleported(XrTeleportPoint p) => logic?.Teleported(Time.time);

        void Update()
        {
            var s = Sim.State;
            if (logic == null || s == null || origin == null || origin.Camera == null || !InputMode.XrActive || pushing) return;
            var h = origin.Camera.transform.position;
            bool suppressed = s.GetBool(SimIds.AutoRunActive) || SimSession.IsReplay;
            var r = logic.Tick(Time.time, Time.deltaTime, h.x, h.z, OnRoof(s), s.Get("procedure.current"), suppressed);
            zone = r.zone;
            Apply(s, r);
        }

        void Apply(ISimState s, FallZoneLogic.Result r)
        {
            if (!string.IsNullOrEmpty(r.toast)) { s.Set("ui.toast", ""); s.Set("ui.toast", r.toast); s.SetFloat("ui.toast_time", Time.time); }
            if (r.haptic && player != null) player.Haptic(def.fallWarnHapticAmp, def.fallWarnHapticSec);
            if (r.write) Write(s);
            if (r.pushBack) StartCoroutine(PushBack());
        }

        /// <summary>淡出 → 水平移動 XR Origin 讓頭回到車頂可走範圍邊（Z 不變）→ 淡入。</summary>
        IEnumerator PushBack()
        {
            pushing = true;
            float keep = fader != null ? fader.Seconds : 0f;
            if (fader != null) { fader.Seconds = def.fallFadeOutSec; yield return fader.FadeTo(1f); }
            origin.transform.position += FallZoneLogic.PushBack(origin.Camera.transform.position, def);
            if (fader != null) { fader.Seconds = def.fallFadeInSec; yield return fader.FadeTo(0f); fader.Seconds = keep; }
            logic.Teleported(Time.time);   // 推回後 teleportGraceSec 內不判
            pushing = false;
            PushedBack?.Invoke();
        }
    }
}
