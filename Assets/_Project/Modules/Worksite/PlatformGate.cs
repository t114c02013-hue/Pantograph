using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// 平台活動欄（ENV_PlatformGate）與跨板（ENV_GateBridge）：roof.open 時開（Boundary 停用、跨板 Walkable 啟用），否則關。
    /// 只擋進入、不擋離開：車頂關閉時若學員還在車頂（xr.floor＝Roof），等學員回到平台才關。
    /// </summary>
    public class PlatformGate : MonoBehaviour
    {
        public const string FloorKey = "xr.floor";
        [SerializeField] Collider gateCollider, bridgeCollider, gapBlocker;
        [SerializeField] GameObject gateVisual, bridgeVisual;
        [SerializeField] string openKey = LiftControl.RoofOpenKey;
        public bool IsOpen { get; private set; }

        public void Setup(Collider gate, GameObject gateVis, Collider bridge, GameObject bridgeVis, Collider gap)
        {
            gateCollider = gate; gateVisual = gateVis; bridgeCollider = bridge; bridgeVisual = bridgeVis; gapBlocker = gap;
            Apply(false);
        }

        /// <summary>活動欄要不要開（純函式）：車頂開放就開；車頂關閉但人還在車頂，維持開著讓學員離開。</summary>
        public static bool ShouldBeOpen(bool roofOpen, string playerFloor, bool currentlyOpen) =>
            roofOpen || (currentlyOpen && playerFloor == "Roof");

        void Update()
        {
            var s = Sim.State;
            if (s == null) return;
            bool want = ShouldBeOpen(s.GetBool(openKey), s.Get(FloorKey), IsOpen);
            if (want != IsOpen) Apply(want);
        }

        public void Apply(bool open)
        {
            IsOpen = open;
            if (gateCollider != null) gateCollider.enabled = !open;
            if (gateVisual != null) gateVisual.transform.localRotation = open ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;   // 開＝轉開 90°
            if (bridgeCollider != null) bridgeCollider.enabled = open;
            if (bridgeVisual != null) bridgeVisual.SetActive(open);
            if (gapBlocker != null) gapBlocker.enabled = !open;
        }
    }
}
