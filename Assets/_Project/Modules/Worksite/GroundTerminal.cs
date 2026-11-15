using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// 接地設備外觀：平台接地端子（黃綠）與弓頭接地掛點。端子夾上、弓頭掛上時顯示接地夾，兩端都接上時畫出接地線。
    /// 同一元件放在端子與掛點兩個物件上，各自設定 ID；接地線由端子那一個負責畫。
    /// </summary>
    public class GroundTerminal : SiteObject
    {
        [SerializeField] GameObject clampVisual;
        [SerializeField] Transform otherEnd;
        [SerializeField] LineRenderer cable;

        public void Setup(GameObject clamp, Transform other, LineRenderer line) { clampVisual = clamp; otherEnd = other; cable = line; }

        string FlagKey => id == SimIds.GroundTerminal ? SimIds.TerminalClamped : SimIds.HeadClamped;

        protected override bool Watches(string key) => key == SimIds.TerminalClamped || key == SimIds.HeadClamped;

        /// <summary>
        /// 接地棒掛好後留在場上（工具架掛位「使用中」），拆除時徒手點：先點弓頭端取下掛鉤，再點端子鬆開夾子。
        /// 順序錯誤（先拆端子）由 SafetyGuard 擋下（E16）。掛接時則要拿著接地棒，由工具處理。
        /// </summary>
        protected override void OnBareHand(ActionRequested e)
        {
            bool deployed = State.Get(RackLogic.SlotKey(3)) == RackLogic.InUse;
            bool clamped = State.GetBool(FlagKey);
            if (!clamped || !deployed)
            {
                Toast(clamped ? "接地桿在使用中" : "要掛接地，先到桿架拿接地桿（第 3 格）");
                return;
            }
            if (!Sim.Ask(SimIds.ToolGroundingRod, id, "ground")) return;
            string result = id == SimIds.GroundTerminal ? IsolationLogic.ToggleTerminal(State) : IsolationLogic.ToggleHead(State);
            Sim.Perform(SimIds.ToolGroundingRod, id, result);
            Toast(id == SimIds.GroundTerminal ? "已鬆開接地端子的夾子" : "已取下弓頭端掛鉤");
        }

        protected override void Refresh()
        {
            if (clampVisual != null) clampVisual.SetActive(State.GetBool(FlagKey));
            if (cable != null)
            {
                bool both = State.GetBool(SimIds.TerminalClamped) && State.GetBool(SimIds.HeadClamped);
                bool any = State.GetBool(SimIds.TerminalClamped);
                cable.enabled = any;
                if (any && otherEnd != null)
                {
                    var a = transform.position + Vector3.up * 0.05f;
                    var b = both ? otherEnd.position : a + new Vector3(0.3f, -0.2f, 0.1f);
                    cable.positionCount = 3;
                    cable.SetPosition(0, a);
                    cable.SetPosition(1, (a + b) * 0.5f + Vector3.down * 0.25f);
                    cable.SetPosition(2, b);
                }
            }
        }

        void LateUpdate()
        {
            // 弓頭會隨升降移動：接地線端點跟著更新
            if (cable != null && cable.enabled && State != null && State.GetBool(SimIds.HeadClamped)) Refresh();
        }
    }
}
