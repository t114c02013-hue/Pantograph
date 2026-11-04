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
        public const string SlotKey = "xr.pouch.slot";

        [SerializeField] XrRig rig;
        [SerializeField] XrPlayer player;

        ToolPouchDef def;
        int current;

        /// <summary>測試用：指到新的一格幾次（每次一個輕震）。</summary>
        public int HoverCount { get; private set; }

        public void Setup(XrRig r, XrPlayer p) { rig = r; player = p; }

        void Awake() => def = ToolPouchDef.Load();

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
            var (slot, hand) = Nearest(SimIds.RackSlotNumbers(s), k => Sim.Registry.TryGetPosition(Id(k), out var p) ? p : (Vector3?)null,
                new[] { rig.RightHand.position, rig.LeftHand.position }, def.slotRadius);
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
