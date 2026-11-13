using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// 隔離管制點的狀態燈箱（terry 2026-11-13，只有外觀）：只讀車頂開放鍵 roof.open（RoofAccessRule 寫，SI_04、SI_05、SI_06 d 完成後開放、WC_02 b 之後關閉），
    /// 開放＝綠燈「隔離完成　准許登車頂」，否則紅燈「隔離未完成　禁止登車頂」。不寫任何狀態。
    /// </summary>
    public class IsolationStatusLight : MonoBehaviour
    {
        [SerializeField] Renderer green, red;
        [SerializeField] TextMesh label;
        [SerializeField] Renderer board;
        [SerializeField] Material on, off, boardGreen, boardRed;
        [SerializeField] string openText = "隔離完成　准許登車頂", closedText = "隔離未完成　禁止登車頂";
        bool? shown;

        public void Setup(Renderer g, Renderer r, TextMesh t, Renderer b, Material lit, Material dark, Material bg, Material br)
        {
            green = g; red = r; label = t; board = b; on = lit; off = dark; boardGreen = bg; boardRed = br;
        }

        /// <summary>燈號文字（純函式）。</summary>
        public static string Text(bool roofOpen, string open, string closed) => roofOpen ? open : closed;

        void Update()
        {
            var s = Sim.State;
            bool open = s != null && s.GetBool(LiftControl.RoofOpenKey);
            if (shown == open) return;
            shown = open;
            if (green != null) green.sharedMaterial = open ? on : off;
            if (red != null) red.sharedMaterial = open ? off : on;
            if (board != null) board.sharedMaterial = open ? boardGreen : boardRed;
            if (label != null) label.text = Text(open, openText, closedText);
        }
    }
}
