using PantographSim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PantographSim.UI
{
    /// <summary>
    /// R1.5b b1 桌面版天車遙控器（crane-software v0.5 §2.1）：拿起遙控器之後才顯示，用滑鼠按住方向鍵；放開或指標移出就停。
    /// 高速、緊急停止、電源鑰匙按一下切換；「掛回掛座」放回遙控器。只經 CraneControl 寫 ctrl.crane.*。
    /// </summary>
    public class CranePanelView : View
    {
        RectTransform root;
        Text status;

        protected override void OnBuild(RectTransform safe)
        {
            var img = Ui.Panel(safe, "CranePanel");
            root = Ui.Place(img.rectTransform, new Vector2(1, 0.5f), new Vector2(-340, 30), new Vector2(300, 0));
            Ui.VStack(img.gameObject, 14, 8);
            Ui.Text(root, "天車遙控器", 26, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
            foreach (var (label, cmd) in new[] { ("▲ 上升", "Up"), ("▼ 下降", "Down"), ("橫行 +X（東）", "E"), ("橫行 −X（西）", "W"), ("走行 +Z（北）", "N"), ("走行 −Z（南）", "S") })
                HoldButton(label, cmd);
            Ui.Size(Ui.Button(root, "高速／微動（切換）", 20, () => CraneControl.ToggleFast(Sim.State)), -1, 52);
            Ui.Size(Ui.Button(root, "電源鑰匙", 20, () => CraneControl.TurnKey(Sim.State)), -1, 52);
            Ui.Size(Ui.Button(root, "緊急停止／復歸", 20, () => CraneControl.PressEStop(Sim.State), new Color(0.7f, 0.15f, 0.12f)), -1, 52);
            Ui.Size(Ui.Button(root, "掛回掛座", 20, () => CraneControl.SetPendant(Sim.State, false)), -1, 52);
            status = Ui.Text(root, "", 18, Ui.Muted, TextAnchor.UpperCenter);
        }

        void HoldButton(string label, string cmd)
        {
            var b = Ui.Button(root, label, 22, null);
            Ui.Size(b, -1, 56);
            var trigger = b.gameObject.AddComponent<EventTrigger>();
            void Add(EventTriggerType type, string value)
            {
                var entry = new EventTrigger.Entry { eventID = type };
                entry.callback.AddListener(_ => CraneControl.Command(Sim.State, value));
                trigger.triggers.Add(entry);
            }
            Add(EventTriggerType.PointerDown, cmd);
            Add(EventTriggerType.PointerUp, "None");
            Add(EventTriggerType.PointerExit, "None");
        }

        public override void Refresh(ISimState s)
        {
            bool show = !InputMode.XrActive && CraneControl.PendantHeld(s) && s.GetBool("training.running");
            Ui.SetActive(root, show);
            if (!show) return;
            status.text = $"電源 {(s.GetBool(SimIds.CranePower) ? "開" : "關")}　{(s.GetBool(SimIds.CraneFast) ? "高速" : "微動")}　{(s.GetBool(SimIds.CraneEStop) ? "緊急停止中" : "")}\n狀態 {s.Get(SimIds.CraneState, "Idle")}";
        }
    }
}
