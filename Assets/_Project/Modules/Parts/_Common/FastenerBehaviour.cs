using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// 螺栓（ip.carbon_strip_L.bolt_1）。邏輯在 FastenerLogic；這裡負責接收工具動作與更新外觀：
    /// 鬆開時螺栓頭隨角度升起、放進零件盒時隱藏、畫了標記線就顯示紅線。Collider 一直保留，空孔也點得到。
    /// </summary>
    public class FastenerBehaviour : PartBehaviour
    {
        [SerializeField] Transform visual;
        [SerializeField] GameObject markLine;
        [SerializeField] float riseAtOut = 0.025f;

        protected override void OnBound() => FastenerLogic.Initialize(State, Id);

        protected override void OnToolAction(ActionPerformed e) => FastenerLogic.Apply(State, Bus, Id, e.text, e.value);

        protected override void RefreshVisual()
        {
            if (visual == null) return;
            string st = State.Get(Id + ".state", FastenerLogic.Tight);
            bool shown = st != FastenerLogic.InTray;
            visual.gameObject.SetActive(shown);
            float rise = 0f;
            if (st == FastenerLogic.Tight || st == FastenerLogic.Loosened || st == FastenerLogic.Out)
                rise = Mathf.Clamp01(State.GetFloat(Id + ".angle_deg") / FastenerLogic.OutAt) * riseAtOut;
            else if (st == FastenerLogic.Placed)
                rise = (1f - Mathf.Clamp01(State.GetFloat(Id + ".lock_deg") / FastenerLogic.SnugAt)) * riseAtOut;
            visual.localPosition = new Vector3(0f, rise, 0f);
            visual.localRotation = Quaternion.Euler(0f, -(State.GetFloat(Id + ".angle_deg") - State.GetFloat(Id + ".lock_deg")), 0f);
            if (markLine != null) markLine.SetActive(shown && State.GetBool(Id + ".marked"));
        }

        public void SetVisual(Transform v, GameObject mark) { visual = v; markLine = mark; }
    }
}
