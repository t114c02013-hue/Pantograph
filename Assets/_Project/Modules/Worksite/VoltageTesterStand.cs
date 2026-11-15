using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>驗電器自檢器（ip.tool.voltage_tester）：自檢通過時 LED 亮綠燈。</summary>
    public class VoltageTesterStand : SiteObject
    {
        [SerializeField] Renderer led;
        public void Setup(Renderer ledRenderer) => led = ledRenderer;

        protected override bool Watches(string key) => key == "tool.voltage_detector.led";

        protected override void Refresh()
        {
            if (led == null) return;
            var block = new MaterialPropertyBlock();
            bool green = State.Get("tool.voltage_detector.led") == "green";
            block.SetColor("_Color", green ? new Color(0.1f, 1f, 0.2f) : new Color(0.25f, 0.05f, 0.05f));
            led.SetPropertyBlock(block);
            LampLight.SetOn(led, green, new Color(0.1f, 1f, 0.2f));   // 沒亮時留弱光
        }
    }
}
