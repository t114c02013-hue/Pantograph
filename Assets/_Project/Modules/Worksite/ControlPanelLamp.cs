using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>控制盤上的弓位指示燈（ip.cab.pan_indicator）：點它＝查看指示燈。到降弓位置亮綠燈，否則紅燈。</summary>
    public class ControlPanelLamp : SiteObject
    {
        [SerializeField] Renderer lamp;
        public void Setup(Renderer r) => lamp = r;

        protected override bool Watches(string key) => key == SimIds.PanHeadLowerDistance;

        protected override void OnBareHand(ActionRequested e)
        {
            State.SetBool(id + ".checked", true);
            bool lowered = SimIds.PanHeadLowered(State);
            State.Set(id + ".shows", lowered ? "降弓" : "升弓");
            Toast(lowered ? "弓位指示燈：降弓（綠燈）" : "弓位指示燈：未降弓（紅燈）");
        }

        protected override void Refresh()
        {
            if (lamp == null) return;
            var block = new MaterialPropertyBlock();
            bool ok = SimIds.PanHeadLowered(State);
            block.SetColor("_Color", ok ? new Color(0.1f, 1f, 0.25f) : new Color(1f, 0.15f, 0.1f));
            block.SetColor("_EmissionColor", ok ? new Color(0.1f, 0.8f, 0.2f) : new Color(0.8f, 0.1f, 0.05f));
            lamp.SetPropertyBlock(block);
            LampLight.SetOn(lamp, true, ok ? new Color(0.1f, 1f, 0.25f) : new Color(1f, 0.15f, 0.1f));   // 點光源跟燈色（一直亮）
        }
    }
}
