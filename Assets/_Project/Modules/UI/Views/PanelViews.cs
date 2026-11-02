using System.Linq;
using System.Collections.Generic;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PantographSim.UI
{
    /// <summary>彈出視窗的共用外框：半透明遮罩＋置中卡片。</summary>
    public static class Modal
    {
        public static (RectTransform dim, RectTransform card) Create(RectTransform safe, string name, Vector2 size, Color? border = null)
        {
            var dim = Ui.Panel(safe, name + "_Dim", new Color(0, 0, 0, 0.55f), false, true);
            Ui.Stretch(dim.rectTransform, -400, -400, -400, -400);
            var card = Ui.Panel(dim.transform, name, Ui.PanelBg, true, true);
            if (border.HasValue) card.GetComponent<Outline>().effectColor = border.Value;
            var rt = card.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            return (dim.rectTransform, rt);
        }
    }

    /// <summary>右上角：錯誤數、說明（H）、零件盒、作業單。</summary>
    public class TopBarView : View
    {
        RectTransform root;
        Text trayLabel, woLabel, errorLabel;
        Image errorBg;

        protected override void OnBuild(RectTransform safe)
        {
            root = Ui.Place(Ui.Rect(safe, "TopBar"), new Vector2(1, 1), Vector2.zero, new Vector2(760, 56));
            var h = Ui.HStack(root.gameObject, 10);
            h.childAlignment = TextAnchor.UpperRight;
            errorBg = Ui.Panel(root, "Errors", new Color(0.05f, 0.06f, 0.08f, 0.85f), true, false);
            Ui.Size(errorBg.rectTransform, 190, 52);
            errorLabel = Ui.Text(errorBg.rectTransform, "", 21, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(errorLabel.rectTransform);
            var help = Ui.Button(root, "說明（H）", 21, () => { Ui.Command(UiCommands.Help); Sim.State.SetBool("ui.help_open", !Sim.State.GetBool("ui.help_open")); }, new Color(0.15f, 0.45f, 0.75f));
            Ui.Size(help, 150, 52);
            var tray = Ui.Button(root, "零件盒", 21, () => Ui.Send(SimIds.PartsTray, "toggle"));
            Ui.Size(tray, 170, 52); trayLabel = Ui.LabelOf(tray);
            var wo = Ui.Button(root, "作業單", 21, () => Ui.Send(SimIds.WorkOrder, "toggle"), Ui.ButtonOn);
            Ui.Size(wo, 170, 52); woLabel = Ui.LabelOf(wo);
        }

        public override void Refresh(ISimState s)
        {
            Ui.SetActive(root, s.GetBool("training.running"));
            trayLabel.text = $"零件盒（{s.GetFloat("tray.bolts"):0}）";
            woLabel.text = s.GetBool("ui.workorder_open") ? "收起作業單" : "打開作業單";
            float minor = s.GetFloat("score.minor"), major = s.GetFloat("score.major"), critical = s.GetFloat("score.critical");
            float total = minor + major + critical;
            errorLabel.text = total <= 0 ? "錯誤 0" : critical > 0 ? $"錯誤 {total:0}（嚴重 {critical:0}）" : $"錯誤 {total:0}";
            errorBg.color = critical > 0 ? new Color(0.6f, 0.1f, 0.08f, 0.92f) : total > 0 ? new Color(0.6f, 0.4f, 0.05f, 0.92f) : new Color(0.05f, 0.06f, 0.08f, 0.85f);
        }
    }

    /// <summary>右下角常駐操作說明。</summary>
    public class ControlsStripView : View
    {
        RectTransform root;

        protected override void OnBuild(RectTransform safe)
        {
            var img = Ui.Panel(safe, "Controls", new Color(0.05f, 0.06f, 0.08f, 0.85f));
            root = Ui.Place(img.rectTransform, new Vector2(1, 0), Vector2.zero, new Vector2(440, 122));
            var t = Ui.Text(root, "<b>操作</b>　到工具架點掛位拿／還工具（1–9 高亮掛位）\n左鍵點　右鍵拖轉視角　滾輪縮放　Backspace 重設工具\nF 拉近　C 帶我到目標　H 完整說明", 18, Ui.Muted);
            Ui.Stretch(t.rectTransform, 14, 10, 14, 8);
        }

        // Game 視窗放大（可見範圍變小）時收起，避免蓋住工具列；完整說明按 H
        public override void Refresh(ISimState s) => Ui.SetActive(root, s.GetBool("training.running") && ViewportInfo.Zoom <= 1.2f && !s.GetBool("ui.workorder_open"));   // 作業單停靠右側時讓出右下角
    }

    /// <summary>
    /// 作業單（停靠在畫面右側的面板，約 30% 寬，不是強制回應視窗）：打開時不暫停、不擋 3D 場景，
    /// 只有點在面板範圍內才算點介面；可以一邊拿卡尺量、一邊看讀數抄寫。由上而下：卡尺讀數、安全隔離、兩條三點讀數、判定四選一、
    /// 新滑板中點、收尾清點、簽名。點一個數值格＝選中（高亮，黑板 ui.workorder.selected）；VR 的「一按填入」填進選中的格子，填完跳到下一格。
    /// VR 時整塊面板由 XrPanelLayout 搬到左手夾板（WorkOrder_Dim 根節點），X 鍵開關。
    /// </summary>
    public class WorkOrderView : View
    {
        public const float WidthFraction = 0.3f, MinWidth = 520f, Top = 66f;
        public const string SelectedKey = "ui.workorder.selected";
        /// <summary>數值格的順序（VR 填完自動跳下一格）。</summary>
        public static readonly string[] FieldOrder =
        {
            "reading.L.left", "reading.L.mid", "reading.L.right", "reading.R.left", "reading.R.mid", "reading.R.right", "newmid.L", "newmid.R"
        };

        RectTransform root, card;
        Text toolReading;
        readonly Dictionary<string, InputField> fields = new Dictionary<string, InputField>();
        readonly Dictionary<string, Text> saved = new Dictionary<string, Text>();
        readonly List<(string id, Text text, string label)> siRows = new List<(string, Text, string)>();
        readonly List<(string value, Image bg)> decisions = new List<(string, Image)>();
        Text diffText, countText, confirmLabel, signLabel, signHint;
        Button signButton;

        /// <summary>可以簽名的步驟：SI_06（開工）、WC_04（完工）。其他時候按鈕變灰、不能按，旁邊顯示「開工／完工時才能簽」。</summary>
        public static bool CanSign(ISimState s)
        {
            string cur = s.Get("procedure.current");
            return cur.StartsWith("STP_SI_06") || cur.StartsWith("STP_WC_04");
        }
        RectTransform siRoot;
        string siBuiltFor = "";
        static readonly string[] Points = { "left", "mid", "right" };
        static readonly string[] PointNames = { "左", "中", "右" };
        static readonly Color SelectedBg = new Color(1f, 0.93f, 0.55f), FieldBg = new Color(0.95f, 0.95f, 0.95f);

        public RectTransform Card => card;
        public IReadOnlyDictionary<string, InputField> Fields => fields;

        /// <summary>fieldId 之後的下一格（最後一格之後回傳空字串）。</summary>
        public static string NextField(string fieldId)
        {
            int i = System.Array.IndexOf(FieldOrder, fieldId);
            return i >= 0 && i + 1 < FieldOrder.Length ? FieldOrder[i + 1] : "";
        }

        protected override void OnBuild(RectTransform safe)
        {
            // 根節點只是容器（沒有 Image＝不擋點選）；名稱沿用 WorkOrder_Dim，VR 版面靠這個名字把它搬到左手夾板
            root = Ui.Rect(safe, "WorkOrder_Dim");
            Ui.Stretch(root);
            var img = Ui.Panel(root, "WorkOrder", Ui.PanelBg, true, true);
            card = img.rectTransform;
            Ui.VStack(card.gameObject, 16, 6);

            var titleRow = Ui.Rect(card, "Title"); Ui.HStack(titleRow.gameObject, 10);
            var title = Ui.Text(titleRow, "作業單　碳滑板更換", 26, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold); Ui.Size(title, -1, -1, 1);
            var close = Ui.Button(titleRow, "收起", 20, () => Ui.Send(SimIds.WorkOrder, "toggle"), Ui.ButtonOn); Ui.Size(close, 100, 42);
            toolReading = Ui.Text(card, "", 21, Ui.Ok, TextAnchor.UpperLeft, FontStyle.Bold);

            Ui.Text(card, "安全隔離", 19, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            siRoot = Ui.Rect(card, "SI"); Ui.VStack(siRoot.gameObject, 0, 0, false);

            foreach (var side in new[] { "L", "R" })
            {
                Ui.Text(card, (side == "L" ? "肘側滑板" : "外側滑板") + "　厚度（mm，照卡尺讀數抄）", 19, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
                var row = Ui.Rect(card, "Row_" + side); Ui.HStack(row.gameObject, 6);
                for (int i = 0; i < 3; i++)
                {
                    Ui.Text(row, PointNames[i], 20);
                    AddField(row, $"reading.{side}.{Points[i]}", 86);
                }
                string sd = side;
                var send = Ui.Button(row, "送出", 19, () =>
                {
                    // 沒填完不送出，畫面說明原因（以前會送出空的，之後步驟判定讀數不符卻看不出為什麼）
                    if (!AllFilled(Points.Select(p => $"reading.{sd}.{p}"))) { Notice("先把左、中、右三格讀數都填好再送出"); return; }
                    for (int i = 0; i < 3; i++) Submit($"reading.{sd}.{Points[i]}");
                    Ui.Send(SimIds.WorkOrder, "true", "submit." + sd);
                }); Ui.Size(send, 76, 40);
                saved["reading." + side] = Ui.Text(card, "", 17, Ui.Muted);
            }

            Ui.Text(card, "判定（任一點 ≤ 3.0 mm〔保養表值〕兩條都換）", 19, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            var d1 = Ui.Rect(card, "D1"); Ui.HStack(d1.gameObject, 6);
            var d2 = Ui.Rect(card, "D2"); Ui.HStack(d2.gameObject, 6);
            AddDecision(d1, "both", "兩條都換"); AddDecision(d1, "L", "只換肘側");
            AddDecision(d2, "R", "只換外側"); AddDecision(d2, "pass", "合格");

            Ui.Text(card, "新滑板中點（mm）　兩條差 ≤ 1.0（示例值）", 19, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            var nm = Ui.Rect(card, "NewMid"); Ui.HStack(nm.gameObject, 6);
            foreach (var side in new[] { "L", "R" })
            {
                Ui.Text(nm, side == "L" ? "肘側" : "外側", 20);
                AddField(nm, "newmid." + side, 92);
            }
            var sendNew = Ui.Button(nm, "送出", 19, () =>
            {
                if (!AllFilled(new[] { "newmid.L", "newmid.R" })) { Notice("先把肘側、外側兩格中點都填好再送出"); return; }
                Submit("newmid.L"); Submit("newmid.R");
            }); Ui.Size(sendNew, 76, 40);
            diffText = Ui.Text(card, "", 17, Ui.Muted);

            Ui.Text(card, "收尾清點", 19, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            countText = Ui.Text(card, "", 17, Ui.Muted);
            var confirm = Ui.Button(card, "確認工具與零件清點", 20, () => Ui.Send(SimIds.WorkOrder, "true", "toolcount_confirmed"));
            Ui.Size(confirm, -1, 42); confirmLabel = Ui.LabelOf(confirm);
            var signRow = Ui.Rect(card, "SignRow"); Ui.HStack(signRow.gameObject, 10);
            signButton = Ui.Button(signRow, "作業人員簽名", 20, () => Ui.Send(SimIds.WorkOrder, "true", "sign"));   // WorkOrder 依目前步驟寫 sign_start／sign_end
            Ui.Size(signButton, 220, 42); signLabel = Ui.LabelOf(signButton);
            signHint = Ui.Text(signRow, "開工／完工時才能簽", 16, Ui.Muted); Ui.Size(signHint, -1, -1, 1);
            PlaceDocked(safe);
        }

        void AddField(Transform row, string fieldId, float width)
        {
            var f = Ui.Input(row, 20); Ui.Size(f, width, 40);
            fields[fieldId] = f;
            // 點一下格子＝選中（VR「一按填入」填進這一格）
            var trigger = f.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.Select };
            entry.callback.AddListener(_ => Sim.State?.Set(SelectedKey, fieldId));
            trigger.triggers.Add(entry);
            var click = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick };
            click.callback.AddListener(_ => Sim.State?.Set(SelectedKey, fieldId));
            trigger.triggers.Add(click);
        }

        void AddDecision(Transform row, string value, string label)
        {
            var b = Ui.Button(row, label, 20, () => Ui.Send(SimIds.WorkOrder, value, "decision"));
            Ui.Size(b, 160, 42);
            decisions.Add((value, b.GetComponent<Image>()));
        }

        bool AllFilled(IEnumerable<string> ids) => ids.All(id => fields.TryGetValue(id, out var f) && !string.IsNullOrWhiteSpace(f.text));

        /// <summary>點擊沒被接受時的原因（底部提示列；先清空再寫，同一句話連按也會再出現）。</summary>
        public static void Notice(string text)
        {
            var s = Sim.State;
            if (s == null) return;
            s.Set("ui.toast", "");
            s.Set("ui.toast", text);
        }

        void Submit(string field)
        {
            if (fields.TryGetValue(field, out var f) && !string.IsNullOrWhiteSpace(f.text)) Ui.Send(SimIds.WorkOrder, f.text.Trim(), field);
        }

        /// <summary>桌面：停靠右側、頂部讓出右上角工具列；寬＝安全區 30%（最少 520）。VR：在夾板上置中。</summary>
        void PlaceDocked(RectTransform safe)
        {
            if (root.parent != safe)   // VR：XrPanelLayout 已把根節點搬到左手夾板
            {
                card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
                card.anchoredPosition = Vector2.zero;
                card.sizeDelta = new Vector2(1100, card.sizeDelta.y);
                return;
            }
            float width = Mathf.Max(MinWidth, safe.rect.width * WidthFraction);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(1f, 1f);
            card.anchoredPosition = new Vector2(0, -Top);
            card.sizeDelta = new Vector2(width, card.sizeDelta.y);
        }

        public override void Refresh(ISimState s)
        {
            bool show = s.GetBool("training.running") && s.GetBool("ui.workorder_open");
            Ui.SetActive(root, show);
            if (!show) return;
            PlaceDocked((RectTransform)root.parent);
            // 讀數顯示在標題下方，方便邊量邊抄（卡尺面板開著時也看得到）
            string active = s.Get(SimIds.ActiveTool);
            toolReading.text = active == SimIds.ToolCaliper ? $"卡尺讀數：{s.Get("tool.caliper.display")}"
                : string.IsNullOrEmpty(active) ? "手上：空" : $"手上：{Ui.ToolName(active)}";
            var si = s.Get("task.si_steps");
            if (si != siBuiltFor)
            {
                foreach (Transform c in siRoot) Object.Destroy(c.gameObject);
                siRows.Clear();
                foreach (var entry in si.Split(';'))
                {
                    var f = entry.Split('|');
                    if (f.Length >= 2) siRows.Add((f[0], Ui.Text(siRoot, f[1], 17), f[1]));
                }
                siBuiltFor = si;
            }
            foreach (var (id, text, label) in siRows)
            {
                bool done = s.GetBool("step." + id + ".done");
                text.text = (done ? Ui.Tint("✔ ", Ui.Ok) : Ui.Tint("✘ ", Ui.Bad)) + label;
            }
            string selected = s.Get(SelectedKey);
            foreach (var kv in fields)
            {
                var bg = kv.Value.GetComponent<Image>();
                if (bg != null) bg.color = kv.Key == selected ? SelectedBg : FieldBg;
            }
            foreach (var side in new[] { "L", "R" })
            {
                var parts = new List<string>();
                for (int i = 0; i < 3; i++)
                {
                    var v = s.Get($"workorder.reading.{side}.{Points[i]}");
                    if (v.Length > 0) parts.Add(PointNames[i] + " " + (s.GetBool($"workorder.reading.{side}.{Points[i]}.mismatch") ? Ui.Tint(v + "（和卡尺不符）", Ui.Bad) : Ui.Tint(v, Ui.Ok)));
                }
                saved["reading." + side].text = parts.Count > 0 ? "已記錄：" + string.Join("　", parts) : "";
            }
            string decision = s.Get("workorder.decision");
            foreach (var (value, bg) in decisions) bg.color = value == decision ? Ui.ButtonOn : Ui.ButtonBg;
            diffText.text = s.Has("workorder.height_diff") ? $"高低差：{s.GetFloat("workorder.height_diff"):0.00} mm" : "";
            countText.text = $"工具已收回：{(s.GetBool("tools.all_returned") ? Ui.Tint("是", Ui.Ok) : Ui.Tint("否（到工具架掛回）", Ui.Bad))}　零件盒剩螺栓：{s.GetFloat("tray.bolts"):0}　舊滑板：{s.GetFloat("tray.old_strips"):0}";
            confirmLabel.text = s.GetBool("workorder.toolcount_confirmed") ? "✔ 已確認清點" : "確認工具與零件清點";
            bool canSign = CanSign(s);
            signButton.interactable = canSign;
            signLabel.color = canSign ? Color.white : Ui.Muted;
            Ui.SetActive(signHint, !canSign);
            signLabel.text = "作業人員簽名";
            signHint.text = !canSign ? "開工／完工時才能簽" : "";
            if (s.GetBool("workorder.sign_start") || s.GetBool("workorder.sign_end"))
            {
                Ui.SetActive(signHint, true);
                signHint.text = (s.GetBool("workorder.sign_start") ? "✔ 開工已簽　" : "") + (s.GetBool("workorder.sign_end") ? "✔ 完工已簽" : "") + (!canSign ? "（開工／完工時才能簽）" : "");
            }
        }
    }

    /// <summary>駕駛室控制盤（點場景中的控制盤開啟）。</summary>
    public class ControlPanelView : View
    {
        RectTransform dim;
        Text indicator, rows;
        Button check;

        protected override void OnBuild(RectTransform safe)
        {
            var (d, card) = Modal.Create(safe, "ControlPanel", new Vector2(700, 440));
            dim = d;
            Ui.VStack(card.gameObject, 24, 10, false);
            Ui.Text(card, "駕駛室控制盤", 32, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            var row = Ui.Rect(card, "Indicator"); Ui.HStack(row.gameObject, 12);
            Ui.Text(row, "弓位指示燈：", 26);
            indicator = Ui.Text(row, "", 30, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            check = Ui.Button(row, "查看指示燈", 22, () => Ui.Send("ip.cab.pan_indicator", "check"), Ui.ButtonOn); Ui.Size(check, 180, 50);
            rows = Ui.Text(card, "", 23);
            Ui.Text(card, "只看指示燈不夠，還要親眼確認弓頭已降下（點弓頭）。", 21, Ui.Accent);
            var close = Ui.Button(card, "關閉", 22, () => Ui.Send("ip.cab.panel", "close")); Ui.Size(close, -1, 50);
        }

        public override void Refresh(ISimState s)
        {
            bool show = s.GetBool("ui.panel_open") && s.GetBool("training.running");
            Ui.SetActive(dim, show);
            if (!show) return;
            bool lowered = SimIds.PanHeadLowered(s);
            bool checkedLight = s.GetBool("ip.cab.pan_indicator.checked");
            indicator.text = checkedLight ? (lowered ? Ui.Tint("● 降弓", Ui.Ok) : Ui.Tint("● 升弓", Ui.Bad)) : "";
            Ui.SetActive(check, !checkedLight);
            rows.text = $"主斷路器：{(s.Get("lockout.breaker") == "locked" ? "OFF　已上鎖＋掛牌" : "未上鎖")}\n" +
                        $"升弓開關：{(s.Get("lockout.raise_switch") == "locked" ? "已上鎖＋掛牌" : "未上鎖")}\n" +
                        $"升弓氣缸電源：{(s.Get("cab.cylinder_power") == "off" ? "OFF（已確認按升弓鈕弓體不動）" : "ON")}";
        }
    }
}
