using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// terry 175B（2026-11-15）：外接電源箱（日常課程）。掛在「供電中」紅燈 Lamp_SupplyOn（ip.shore_box.lamp）：點紅燈＝確認（比照 SI_01 c 指弓位指示燈）。
    /// 一份元件管燈（shore.switch on＝紅亮綠暗，off 反過來）、開關把手角度（直立 ON、轉 90° OFF）和電纜（箱子出口到插頭，兩點直線）。
    /// </summary>
    public class ShoreBox : SiteObject
    {
        [SerializeField] Renderer lampOn, lampOff;
        [SerializeField] Transform switchHandle, cableOut, plugCableAnchor;
        [SerializeField] LineRenderer cable;
        Quaternion switchRest; bool hasRest;
        static readonly Color Red = new Color(1f, 0.15f, 0.1f), Green = new Color(0.1f, 1f, 0.25f), DarkRed = new Color(0.25f, 0.05f, 0.05f), DarkGreen = new Color(0.05f, 0.2f, 0.07f);

        public void Setup(Renderer on, Renderer off, Transform handle, Transform outlet, Transform plugAnchor, LineRenderer line)
        {
            lampOn = on; lampOff = off; switchHandle = handle; cableOut = outlet; plugCableAnchor = plugAnchor; cable = line;
        }

        /// <summary>燈號（純函式）：(紅燈亮, 綠燈亮)。</summary>
        public static (bool red, bool green) Lamps(ISimState s)
        {
            bool on = s == null || s.Get(CabControlLogic.ShoreSwitch, CabControlLogic.On) == CabControlLogic.On;
            return (on, !on);
        }

        protected override bool Watches(string key) => key == CabControlLogic.ShoreSwitch;

        protected override void OnBareHand(ActionRequested e) => State.SetBool(id + ".checked", true);

        protected override void Refresh()
        {
            var (red, green) = Lamps(State);
            Paint(lampOn, red ? Red : DarkRed); Paint(lampOff, green ? Green : DarkGreen);
            LampLight.SetOn(lampOn, red, Red); LampLight.SetOn(lampOff, green, Green);
            if (switchHandle != null)
            {
                if (!hasRest) { switchRest = switchHandle.localRotation; hasRest = true; }
                switchHandle.localRotation = switchRest * Quaternion.Euler(0f, 0f, red ? 0f : 90f);
            }
        }

        static void Paint(Renderer r, Color c)
        {
            if (r == null) return;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", c); block.SetColor("_EmissionColor", c * 0.8f);
            r.SetPropertyBlock(block);
        }

        void LateUpdate()
        {
            if (cable == null || cableOut == null || plugCableAnchor == null) return;
            cable.SetPosition(0, cableOut.position); cable.SetPosition(1, plugCableAnchor.position);
        }
    }
}
