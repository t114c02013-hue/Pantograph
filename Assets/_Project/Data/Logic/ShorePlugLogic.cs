using PantographSim.Core;

namespace PantographSim.Data
{
    /// <summary>
    /// terry 175B（2026-11-15）：日常課程外接電源插頭 ip.shore_box.plug（shore.plug＝car／holder）。純函式，ShorePlug 元件與測試共用；回傳 null＝成功，否則是提示。
    /// 供電開關還是 ON 就抓：所有模式都擋下（插頭不離開插座），每抓一次寫一次 shore.unplug_live（ErrorRules 的 ERR_SI02b_UnplugLive，教學只提示不記錯）。
    /// </summary>
    public static class ShorePlugLogic
    {
        public const string Id = "ip.shore_box.plug", Plug = "shore.plug", Car = "car", Holder = "holder", UnplugLive = "shore.unplug_live", HandItem = "hand.item";
        public const string LiveText = "先把開關切到 OFF", HolderText = "把插頭掛到箱上的插頭座", FullText = "手上已經拿著東西";

        public static bool InHolder(ISimState s) => s.Get(Plug, Car) == Holder;
        public static bool Held(ISimState s) => s.Get(HandItem) == Id;

        /// <summary>握把抓插頭。插頭座上的抓不起來（沒反應）。</summary>
        public static string Grab(ISimState s)
        {
            if (InHolder(s) || Held(s)) return null;
            if (s.Get(CabControlLogic.ShoreSwitch, CabControlLogic.On) == CabControlLogic.On) { s.SetBool(UnplugLive, true); return LiveText; }
            if (!string.IsNullOrEmpty(s.Get(HandItem))) return FullText;   // 上鎖掛牌組拿在手上也能抓（另一隻手）
            s.Set(HandItem, Id);
            return null;
        }

        /// <summary>放開：離插頭座夠近＝掛好（holder）；否則回到車上的插座（car 不變），提示、不記錯。</summary>
        public static string Release(ISimState s, bool nearHolder)
        {
            if (!Held(s)) return null;
            s.Batch(() => { s.Set(HandItem, ""); if (nearHolder) s.Set(Plug, Holder); });
            return nearHolder ? null : HolderText;
        }

        /// <summary>桌機：點插頭＝抓起來直接掛到插頭座（開關 ON 時一樣擋下）。</summary>
        public static string Click(ISimState s)
        {
            if (InHolder(s)) return null;
            var msg = Grab(s);
            return msg ?? Release(s, true);
        }
    }
}
