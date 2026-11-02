using PantographSim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PantographSim.UI
{
    /// <summary>
    /// 右側升降控制器（像千斤頂）：「▲ 上升（按住）」「▼ 下降（按住）」，放開、指標移出按鈕或視窗失焦就停；
    /// 0–100% 高度條（綠色＝降弓位置），目前高度與距降弓位置的距離與方向，到位時顯示「✔ 已到降弓位置」。
    /// 按鈕只寫黑板 ctrl.lift.cmd（Up / Down / Stop），由機構端讀取驅動；鍵盤 PageUp／PageDown、↑／↓ 由 Interaction 寫同一個鍵。
    /// </summary>
    public class LiftPanelView : View
    {
        public const string CmdKey = SimIds.LiftCmd;
        RectTransform root;
        Image fill, zone, upBg, downBg;
        Text height, status, keys, lampLabel;
        Button lamp;
        Image lampImg;
        static readonly Color ButtonBlue = new Color(0.15f, 0.42f, 0.7f);

        protected override void OnBuild(RectTransform safe)
        {
            var img = Ui.Panel(safe, "LiftPanel");
            root = Ui.Place(img.rectTransform, new Vector2(1, 0.5f), new Vector2(0, 30), new Vector2(320, 0));
            Ui.VStack(img.gameObject, 18, 10);
            Ui.Text(root, "升降控制器", 28, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
            upBg = HoldButton("▲ 上升（按住）", "Up");
            var barRow = Ui.Rect(root, "Bar");
            Ui.Size(barRow, -1, 34);
            var bar = Ui.Panel(barRow, "BarBg", new Color(0.2f, 0.22f, 0.25f), true, false);
            Ui.Stretch(bar.rectTransform);
            zone = Ui.Panel(bar.transform, "LoweredZone", new Color(0.2f, 0.85f, 0.35f, 0.75f), false, false);
            zone.rectTransform.anchorMin = Vector2.zero; zone.rectTransform.anchorMax = new Vector2(0.04f, 1);
            zone.rectTransform.offsetMin = zone.rectTransform.offsetMax = Vector2.zero;
            fill = Ui.Panel(bar.transform, "Fill", new Color(1f, 0.72f, 0.15f, 0.85f), false, false);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            Ui.Text(root, "◀ 降弓位置（綠）　　　升到頂 ▶", 17, Ui.Muted, TextAnchor.UpperCenter);
            height = Ui.Text(root, "", 22, Color.white, TextAnchor.UpperCenter);
            status = Ui.Text(root, "", 26, Ui.Ok, TextAnchor.UpperCenter, FontStyle.Bold);
            downBg = HoldButton("▼ 下降（按住）", "Down");
            keys = Ui.Text(root, "鍵盤：PageUp／PageDown 或 ↑／↓ 按住", 18, Ui.Muted, TextAnchor.UpperCenter);
            // VR：弓位指示燈在同一塊面板上，SI_01 的確認＝扳機點這顆燈（桌面在駕駛室控制盤面板查看）
            lamp = Ui.Button(root, "", 24, () => Ui.Send("ip.cab.pan_indicator", "check"));
            Ui.Size(lamp, -1, 80);
            lampImg = lamp.GetComponent<Image>();
            lampLabel = Ui.LabelOf(lamp);
        }

        Image HoldButton(string label, string cmd)
        {
            var b = Ui.Button(root, label, 26, null, ButtonBlue);
            Ui.Size(b, -1, 84);
            var trigger = b.gameObject.AddComponent<EventTrigger>();
            void Add(EventTriggerType type, string value)
            {
                var entry = new EventTrigger.Entry { eventID = type };
                entry.callback.AddListener(_ => LiftControl.Send(Sim.State, value));   // Stop＝最新距離與 Stop 同一批
                trigger.triggers.Add(entry);
            }
            Add(EventTriggerType.PointerDown, cmd);
            Add(EventTriggerType.PointerUp, "Stop");
            Add(EventTriggerType.PointerExit, "Stop");
            return b.GetComponent<Image>();
        }

        public override void Refresh(ISimState s)
        {
            bool running = s.GetBool("training.running") && !s.GetBool("procedure.finished");
            bool ok = SimIds.PanHeadLowered(s);
            // 降弓那一步、沒在降弓位置、或正在升降時才顯示（降好後收起，避免和工具面板重疊；鍵盤 PageUp/PageDown 仍可用）
            bool show = running && !s.GetBool("ui.workorder_open")
                        && (s.Get("procedure.current") == "STP_SI_01_ConfirmLowered" || !ok || s.GetBool("lift.moving"));
            Ui.SetActive(root, show);
            if (!show) return;
            bool xr = InputMode.XrActive;
            keys.text = xr ? "射線指著 ▲／▼ 按住扳機；放開就停" : "鍵盤：PageUp／PageDown 或 ↑／↓ 按住";
            Ui.SetActive(lamp, xr);
            if (xr)
            {
                bool checkedLamp = s.GetBool("ip.cab.pan_indicator.checked");
                lampImg.color = ok ? new Color(0.1f, 0.55f, 0.2f) : new Color(0.6f, 0.12f, 0.1f);
                lampLabel.text = (ok ? "● 弓位指示燈：降弓" : "● 弓位指示燈：升弓") + (checkedLamp ? "　✔ 已查看" : "\n扳機點這裡＝查看指示燈");
            }
            float value = s.GetFloat(SimIds.LiftValue);
            float travel = Mathf.Max(1f, s.GetFloat("lift.travel_cm", 183f));
            float dist = s.GetFloat(SimIds.PanHeadLowerDistance);
            float delta = s.GetFloat(SimIds.PanHeadLowerDelta);
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
            zone.rectTransform.anchorMax = new Vector2(Mathf.Max(0.03f, s.GetFloat(SimIds.PanHeadLowerTolerance, 1f) / travel), 1);
            string dir = ok ? "" : delta > 0 ? "\n要往下 ▼" : "\n要往上 ▲";
            height.text = $"高度 {value * 100f:0}%（約 {value * travel:0} cm）\n距降弓位置 {SimIds.CmText(dist)} cm{dir}";
            status.text = ok ? "✔ 已到降弓位置" : value > 0.999f ? "弓在升起位置" : "未到降弓位置";
            status.color = ok ? Ui.Ok : Ui.Bad;
            string cmd = s.Get(CmdKey);
            float blink = Ui.Blink(6f);
            bool needDown = !ok && s.Get("procedure.current") == "STP_SI_01_ConfirmLowered";
            downBg.color = cmd == "Down" ? Ui.ButtonOn : needDown ? Color.Lerp(ButtonBlue, new Color(1f, 0.85f, 0.1f), blink) : ButtonBlue;
            // 上升被安全聯鎖擋住（車頂有人／升弓開關上鎖／電動缸斷電）：按鈕變灰；按下時狀態列寫出原因
            bool upLocked = LiftControl.UpLocked(s);
            upBg.color = upLocked ? new Color(0.3f, 0.3f, 0.32f) : cmd == "Up" ? Ui.ButtonOn : ButtonBlue;
            if (upLocked && cmd == "Up") { status.text = LiftControl.UpBlockReason(s); status.color = Ui.Bad; }
        }
    }
}
