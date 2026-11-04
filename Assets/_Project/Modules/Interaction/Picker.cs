using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Interaction
{
    /// <summary>
    /// 滑鼠點選：射線找 IInteractable，滑過時高亮並顯示名稱（ui.hover_name），
    /// 左鍵按下送出 ActionRequested（帶目前工具 tool.active；徒手時用物件的預設動作，例如弓頭是「查看」）。
    /// 介面上、或錯誤對話框暫停中（training.paused）時不送。
    /// </summary>
    public class Picker : MonoBehaviour
    {
        [SerializeField] float maxDistance = 30f;
        IInteractable hovered;
        Camera cam;

        public IInteractable Hovered => hovered;

        /// <summary>點擊要不要送到場景：滑鼠在介面上（例：右側作業單面板）時不送，點擊不會穿過面板打到後面的物件。</summary>
        public static bool ShouldSendClick(bool overUi, bool paused, bool running) => !overUi && !paused && running;

        void Update()
        {
            var s = Sim.State;
            if (s == null) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            if (PointerState.ScriptedRay.HasValue) { PointerState.Ray = PointerState.ScriptedRay.Value; PointerState.HasRay = true; return; }
            PointerState.Ray = cam.ScreenPointToRay(Input.mousePosition);
            PointerState.HasRay = !PointerState.OverUi;
            IInteractable hit = null;
            RaycastHit info = default;
            if (!PointerState.OverUi && Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out info, maxDistance, SceneLayers.InteractMask, QueryTriggerInteraction.Ignore))   // 不打不可見的邊界牆
                hit = info.collider.GetComponentInParent<IInteractable>();
            if (hit != null && !SimIds.Clickable(s, hit.Id)) hit = null;

            if (!ReferenceEquals(hit, hovered))
            {
                if (hovered as Object) hovered.SetHovered(false);
                hovered = hit;
                if (hovered != null) hovered.SetHovered(true);
                s.Set("ui.hover_name", hovered != null ? hovered.DisplayName : "");
                s.Set("ui.hover_id", hovered != null ? hovered.Id : "");
                Sim.Bus.Publish(new HoverChanged { targetId = hovered != null ? hovered.Id : "", text = "mouse", time = SimClock.Now });
            }

            if (hovered == null || !ShouldSendClick(PointerState.OverUi, s.GetBool("training.paused"), s.GetBool("training.running"))) return;
            if (!Input.GetMouseButtonDown(0)) return;
            string tool = s.Get(SimIds.ActiveTool);
            string clicked = SimIds.RayTarget(s, tool, hovered.Id, info.point);
            Sim.Bus.Publish(new ActionRequested
            {
                targetId = clicked, toolId = tool,
                text = !string.IsNullOrEmpty(tool) ? "click" : clicked == hovered.Id ? hovered.DefaultVerb : "inspect",
                pointId = $"{info.point.x:0.###},{info.point.y:0.###},{info.point.z:0.###}", time = SimClock.Now
            });
            s.Set("ui.last_clicked", clicked);
        }
    }
}
