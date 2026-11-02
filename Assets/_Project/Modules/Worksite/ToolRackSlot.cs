using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// 工具架的一個掛位（env.tool_rack.slot_1..9）。點它：手空著＝拿起；拿著這件＝歸還；拿著別件＝提示先放回。
    /// 實際的拿／還由工具列處理（發 ToolPickRequested／ToolReturnRequested，value＝掛位），這裡負責外觀：在架顯示工具模型，
    /// 拿在手上（橘）或使用中（藍）時模型隱藏、框變色。
    /// </summary>
    public class ToolRackSlot : SiteObject
    {
        [SerializeField] int slot;
        [SerializeField] GameObject model;
        [SerializeField] Renderer frame;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        MaterialPropertyBlock block;

        public void Setup(int k, GameObject toolModel, Renderer frameRenderer) { slot = k; model = toolModel; frame = frameRenderer; }

        protected override bool AcceptsAnyTool => true;
        protected override bool Watches(string key) => key == RackLogic.SlotKey(slot);

        protected override void OnBareHand(ActionRequested e)
        {
            // 這格的工具不在架上（拿在手上或使用中）→ 要求歸還；在架上 → 要求拿起。是否允許由工具列判斷。
            if (State.Get(RackLogic.SlotKey(slot), RackLogic.OnRack) == RackLogic.OnRack)
                Bus.Publish(new ToolPickRequested { targetId = id, value = slot, time = SimClock.Now });
            else
                Bus.Publish(new ToolReturnRequested { targetId = id, value = slot, time = SimClock.Now });
        }

        protected override void Refresh()
        {
            string st = State.Get(RackLogic.SlotKey(slot), RackLogic.OnRack);
            if (model != null) model.SetActive(st == RackLogic.OnRack);
            if (frame == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            block.SetColor(ColorId, st == RackLogic.OnRack ? new Color(0.35f, 0.37f, 0.4f) : st == RackLogic.InHand ? new Color(1f, 0.6f, 0.1f) : new Color(0.2f, 0.5f, 0.9f));
            frame.SetPropertyBlock(block);
        }
    }
}
