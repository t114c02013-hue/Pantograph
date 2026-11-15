using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// terry 175B（2026-11-15）：外接電源插頭 ip.shore_box.plug（邏輯在 ShorePlugLogic）。
    /// VR：握把抓（XrPlayer 送 grab，放開送 release；沿用車頂螺栓盤那條路，哪隻手抓就跟哪隻手）；放開時離插頭座 ≤ shorePlugSnapDist 就掛上。
    /// 桌機：點一下＝直接掛到插頭座。開關 ON 時都擋下。
    /// </summary>
    public class ShorePlug : SiteObject
    {
        [SerializeField] Transform carSeat, holderSeat;   // ShoreSocket/PlugSeat、PlugHolster/PlugSeat
        float snap = 0.15f;

        public void Setup(Transform car, Transform holder) { carSeat = car; holderSeat = holder; }

        protected override bool Watches(string key) => key == ShorePlugLogic.Plug || key == ShorePlugLogic.HandItem;

        protected override bool AcceptsAnyTool => true;   // 拿著上鎖掛牌組也能拔插頭

        protected override void OnBound() { snap = ToolRulesDef.Load().shorePlugSnapDist; }

        protected override void OnBareHand(ActionRequested e)
        {
            string msg = e.text == "grab" ? ShorePlugLogic.Grab(State)
                : e.text == "release" ? ShorePlugLogic.Release(State, holderSeat != null && Vector3.Distance(transform.position, holderSeat.position) <= snap)
                : ShorePlugLogic.Click(State);
            if (!string.IsNullOrEmpty(msg)) Toast(msg);
        }

        protected override void Refresh()
        {
            if (ShorePlugLogic.Held(State)) return;
            var seat = ShorePlugLogic.InHolder(State) ? holderSeat : carSeat;
            if (seat != null) transform.SetPositionAndRotation(seat.position, seat.rotation);
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = !ShorePlugLogic.InHolder(State);   // 進了插頭座就不能再抓
        }

        /// <summary>拿在手上：跟著抓的那隻手（桌機沒有抓的狀態）。</summary>
        void LateUpdate()
        {
            if (!bound || !ShorePlugLogic.Held(State)) return;
            var hand = PointerState.HoldHand;
            if (hand != null) transform.position = Vector3.Lerp(transform.position, hand.position, 1f - Mathf.Exp(-18f * Time.deltaTime));
        }
    }
}
