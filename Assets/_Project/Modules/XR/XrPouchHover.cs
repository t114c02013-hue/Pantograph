using System.Collections.Generic;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// VR 試玩修正 第 4 點：手伸到工具袋要有反應。左右手離哪一格最近（ToolPouchDef.slotRadius 內）就算指到那一格：高亮、顯示名稱（xr.pouch.slot，XrCarryContainer 讀）、輕震一次。
    /// 訓練還沒開始時一樣有反應，並提示要先開始。拿取、放回在 XrPlayer。
    /// </summary>
    public class XrPouchHover : MonoBehaviour
    {
        public const string SlotKey = "xr.pouch.slot", TargetKey = "xr.pouch.target";

        [SerializeField] XrRig rig;
        [SerializeField] XrPlayer player;

        ToolPouchDef def;
        int current;
        bool bound;

        /// <summary>測試用：指到新的一格幾次（每次一個輕震）。</summary>
        public int HoverCount { get; private set; }

        public void Setup(XrRig r, XrPlayer p) { rig = r; player = p; }

        void Awake() => def = ToolPouchDef.Load();

        void OnDestroy()
        {
            if (!bound || Sim.Bus == null) return;
            Sim.Bus.Unsubscribe<StepStarted>(OnStep);
            Sim.Bus.Unsubscribe<HighlightRequested>(OnHighlight);
        }

        /// <summary>步驟沒寫目標時不清掉：小動作的目標（HighlightRequested）比 StepStarted 先到，例 CSR_02 的卡尺格。</summary>
        void OnStep(StepStarted e)
        {
            string ids = !string.IsNullOrEmpty(e.highlightIds) ? e.highlightIds : e.targetIds != null ? string.Join(",", e.targetIds) : "";
            if (ids.Length > 0) SetTarget(ids);
        }
        void OnHighlight(HighlightRequested e) => SetTarget(e.text);

        /// <summary>這一步要拿的工具在工具袋裡：記下格子；教學模式提示怎麼拿。</summary>
        void SetTarget(string ids)
        {
            var s = Sim.State;
            if (s == null || !InputMode.XrActive) return;
            int slot = BagSlot(ids);
            if (slot == (int)s.GetFloat(TargetKey)) return;
            s.SetFloat(TargetKey, slot);
            if (slot == 0 || s.Get("training.mode") != "tutorial") return;
            var label = Sim.Registry.TryGetTransform(Id(slot), out var t) ? t.GetComponentInChildren<TextMesh>(true) : null;
            s.Set("ui.toast", ""); s.Set("ui.toast", string.Format(def.takeHint, label != null ? label.text : "")); s.SetFloat("ui.toast_time", Time.time);
        }

        static int BagSlot(string ids)
        {
            if (Sim.Registry == null || !Sim.Registry.TryGetTransform("env.tool_bag", out var bag) || bag == null) return 0;
            foreach (var id in (ids ?? "").Split(','))
                if (id.StartsWith("env.tool_rack.slot_") && int.TryParse(id.Substring("env.tool_rack.slot_".Length), out int k) && Sim.Registry.TryGetTransform(id, out var t) && t != null && t.IsChildOf(bag)) return k;
            return 0;
        }

        /// <summary>教學模式要拿的格子優先（手在 targetRadius 內），否則取最近的一格。</summary>
        public static (int slot, int hand) Pick(ISimState s, ToolPouchDef def, Vector3[] hands)
        {
            System.Func<int, Vector3?> at = k => Sim.Registry != null && Sim.Registry.TryGetPosition(Id(k), out var p) ? p : (Vector3?)null;
            int target = s.Get("training.mode") == "tutorial" ? (int)s.GetFloat(TargetKey) : 0;
            if (target > 0)
            {
                var hit = Nearest(new[] { target }, at, hands, def.targetRadius);
                if (hit.slot > 0) return hit;
            }
            return Nearest(SimIds.RackSlotNumbers(s), at, hands, def.slotRadius);
        }

        static string Id(int k) => "env.tool_rack.slot_" + k;

        /// <summary>離 hands 最近、在 radius 內的掛位（純函式）；沒有回傳 0。</summary>
        public static (int slot, int hand) Nearest(IEnumerable<int> slots, System.Func<int, Vector3?> position, Vector3[] hands, float radius)
        {
            int best = 0, bestHand = 0; float bestD = radius;
            foreach (int k in slots)
            {
                var p = position(k);
                if (p == null) continue;
                for (int h = 0; h < hands.Length; h++)
                {
                    float d = Vector3.Distance(p.Value, hands[h]);
                    if (d <= bestD) { bestD = d; best = k; bestHand = h; }
                }
            }
            return (best, bestHand);
        }

        void Update()
        {
            var s = Sim.State;
            if (s == null || Sim.Registry == null || rig == null || rig.LeftHand == null || rig.RightHand == null || !InputMode.XrActive || !s.GetBool(XrStartupSequencer.ReadyKey)) return;
            if (!bound && Sim.Bus != null) { Sim.Bus.Subscribe<StepStarted>(OnStep); Sim.Bus.Subscribe<HighlightRequested>(OnHighlight); bound = true; }
            var (slot, hand) = Pick(s, def, new[] { rig.RightHand.position, rig.LeftHand.position });
            if (slot == current) return;
            Highlight(current, false);
            current = slot;
            s.SetFloat(SlotKey, slot);
            if (slot == 0) return;
            Highlight(slot, true);
            HoverCount++;
            player?.Haptic(hand == 1, def.hoverAmplitude, def.hoverSeconds);
            if (!s.GetBool("training.running")) { s.Set("ui.toast", ""); s.Set("ui.toast", def.notStartedHint); s.SetFloat("ui.toast_time", Time.time); }
        }

        static void Highlight(int slot, bool on)
        {
            if (slot == 0 || !Sim.Registry.TryGetTransform(Id(slot), out var t) || t == null) return;
            t.GetComponent<IInteractable>()?.SetHovered(on);
        }
    }
}
