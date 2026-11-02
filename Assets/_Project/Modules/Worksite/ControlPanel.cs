using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// 駕駛室控制盤（場景中是一個方塊，點擊開 2D 面板）。面板上的弓位指示燈點過會記 ip.cab.pan_indicator.checked；
    /// 主斷路器、升弓開關、升弓氣缸電源的狀態顯示在面板（本切片預設已上鎖／斷電）。
    /// </summary>
    public class ControlPanel : SiteObject
    {
        protected override void OnBareHand(ActionRequested e) => State.SetBool("ui.panel_open", e.text != "close");
    }
}
