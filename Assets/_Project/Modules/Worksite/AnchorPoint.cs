using PantographSim.Core;

namespace PantographSim.Worksite
{
    /// <summary>安全帶掛點（ip.depot.platform_anchor、ip.roof.anchor）：本切片由預設步驟處理，只顯示與可查看。</summary>
    public class AnchorPoint : SiteObject
    {
        protected override void OnBareHand(ActionRequested e) =>
            Toast(DisplayName + (State.GetBool("harness." + (id.Contains("roof") ? "roof" : "platform")) ? "：安全帶已扣上" : "：未扣"));
    }
}
