using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Worksite
{
    /// <summary>
    /// R1.5b b4（D43，RT_02 e2）：絕緣測試線。接著的時候空手點弓頭接地掛點或車體接地點＝拆下那一條（MeggerLogic.RemoveLead：
    /// 放電未滿 dischargeSeconds，教學擋下並顯示倒數，練習／考核照拆並記錯）。場景上一個元件管兩個點。
    /// </summary>
    public class MeggerLeads : SiteObject
    {
        protected override bool Owns(string targetId) => MeggerLogic.IsPoint(targetId) && State != null && State.GetBool(MeggerLogic.LeadKey(targetId));

        protected override void OnBareHand(ActionRequested e)
        {
            if (e.text == "inspect") return;
            var msg = MeggerLogic.RemoveLead(State, e.targetId, ToolRulesDef.Load());
            if (msg != null) Toast(msg);
        }
    }
}
