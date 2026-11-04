using System.Collections.Generic;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PantographSim.UI
{
    // VR 才顯示的面板（桌面模式一律隱藏）。內容只讀黑板、送 ActionRequested；位置由 XrPanelLayout 擺到世界空間。
    // XR 模組寫的黑板鍵：xr.tutorial.*（控制器教學）、xr.menu_open、xr.help_open、xr.fill.*（卡尺讀數待填入）、xr.gesture.tool（手勢小卡）。

    /// <summary>VR 各工具的手勢說明（手勢小卡與 Y 說明共用）。</summary>
    public static class XrGestures
    {
        public const string Common = "手到掛位按一下握把＝歸還　A＝重設手上工具";

        public static string For(string toolId)
        {
            switch (toolId)
            {
                case SimIds.ToolVoltageDetector: return "扳機點驗電器自檢器（綠燈）→ 扳機點弓頭接地掛點";
                case SimIds.ToolGroundingRod: return "扳機點平台接地端子 → 看左手開工具面板點「拉線確認」→ 扳機點弓頭接地掛點";
                case SimIds.ToolCaliper: return "看左手開工具面板：合攏量爪、ZERO，也可用左手指尖碰量爪\n扳機點量測點＝量；工具面板「填入作業單」一按就填";
                case SimIds.ToolSocket: return "扳機點螺栓套上，按住扳機＝轉（每秒 90°）\n看左手開工具面板選鬆／鎖和套筒，也可用左手指尖碰橘色撥桿；扳手頭碰套筒座＝換套筒";
                case SimIds.ToolTorque: return "看左手開工具面板點 +0.5／−0.5，也可用左手指尖碰綠、紅小方塊\n扳機點螺栓後按住扳機＝加力，喀一聲就放開";
                case SimIds.ToolCloth: return "按住扳機，用射線掃過托架上的黑色碳粉";
                case SimIds.ToolMarker: return "扳機點已鎖到扭力的螺栓，畫防鬆標記";
                case "carried": return "射線移到托架上方，扳機點托架＝放下\n轉動手腕超過 90°＝翻轉 180°";
                default: return "";
            }
        }

        public const string KeyTable =
            "<b>扳機</b>　使用／點擊（指著不按＝只看資訊卡）\n" +
            "<b>握把</b>　抓取／放下工具與滑板（在掛位前放開＝歸還）\n" +
            "<b>左搖桿</b>　往前推＝傳送　左右撥＝轉 45°\n" +
            "<b>右搖桿按下</b>　手上工具的第二功能　<b>工具數值</b>　看左手開工具面板\n" +
            "<b>A</b>　重設手上工具　<b>B</b>　帶我到目標\n" +
            "<b>X</b>　作業單（用握把抓把手移動）　<b>Y</b>　說明\n" +
            "<b>≡</b>　暫停選單（重做本步、切換模式、視角置中、錄影、離開）\n" +
            "<b>錄影</b>　≡ 暫停選單或左手腕面板按「● 開始錄影／■ 停止錄影」（不佔用 A、B、X、Y）";
    }

    /// <summary>資訊卡：指到物件 0.2 秒後顯示（一次一張），離開 0.3 秒後收起。VR 只認右手射線。</summary>
    public class HoverCardView : View
    {
        RectTransform root;
        Text text;
        readonly HoverDwell dwell = new HoverDwell();
        string current = "";
        bool bound;

        public string ShownId => dwell.Shown;
        public RectTransform Root => root;

        protected override void OnBuild(RectTransform safe)
        {
            var img = Ui.Panel(safe, "HoverCard", new Color(0.06f, 0.1f, 0.16f, 0.94f), true, false);
            root = Ui.Place(img.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440, 0));
            Ui.VStack(img.gameObject, 16, 4);
            text = Ui.Text(root, "", 24, Color.white);
        }

        void OnHover(HoverChanged e)
        {
            if (InputMode.XrActive && e.text != "xr.right") return;
            current = e.targetId ?? "";
        }

        public override void Refresh(ISimState s)
        {
            if (!bound && Sim.Bus != null) { Sim.Bus.Subscribe<HoverChanged>(OnHover); bound = true; }
            string id = dwell.Update(InputMode.XrActive && s.GetBool("training.running") ? current : "", Time.unscaledTime);
            bool show = id.Length > 0 && Sim.Registry != null && Sim.Registry.TryGetPosition(id, out _);
            Ui.SetActive(root, show);
            if (show) text.text = InfoCardText.Build(s, Sim.Registry, id);
        }
    }

    /// <summary>左手腕面板：手上工具與設定值、錯誤數、↶ 上一步（教學模式）、顯示／收起提示板、卡尺讀數「填入作業單」。</summary>
    public class XrWristView : View
    {
        RectTransform root;
        Text toolLine, valueLine, errors;
        Button back, fill, board;
        Text fillLabel, boardLabel;

        public RectTransform Root => root;

        protected override void OnBuild(RectTransform safe)
        {
            var img = Ui.Panel(safe, "XrWrist");
            root = Ui.Place(img.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(700, 0));
            Ui.VStack(img.gameObject, 16, 8);
            toolLine = Ui.Text(root, "", 30, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            valueLine = Ui.Text(root, "", 26, Ui.Ok);
            errors = Ui.Text(root, "", 22, Ui.Muted);
            fill = Ui.Button(root, "", 24, () =>
            {
                // 填進作業單上選中的格子（沒選就用這次量測對應的格子），填完選中下一格
                var st = Sim.State;
                string target = FillTarget(st);
                if (string.IsNullOrEmpty(target)) { WorkOrderView.Notice("先在作業單上選一格（用右手射線點格子）"); return; }
                Ui.Send(SimIds.WorkOrder, st.Get("xr.fill.value"), target);
                st.Batch(() => { st.Set("xr.fill.field", ""); st.Set(WorkOrderView.SelectedKey, WorkOrderView.NextField(target)); });
            }, new Color(0.2f, 0.55f, 0.3f));
            Ui.Size(fill, -1, 64); fillLabel = Ui.LabelOf(fill);
            var row = Ui.Rect(root, "Row"); Ui.HStack(row.gameObject, 10);
            back = Ui.Button(row, "↶ 上一步", 22, () => Ui.Send("procedure.sub_back", "back")); Ui.Size(back, 200, 60);
            board = Ui.Button(row, "", 22, () => { Ui.Command(UiCommands.HintPanelToggle, "xr.wrist"); Sim.State.SetBool("ui.board_hidden", !Sim.State.GetBool("ui.board_hidden")); }); Ui.Size(board, 230, 60);
            boardLabel = Ui.LabelOf(board);
            var wo = Ui.Button(row, "作業單（X）", 22, () => Ui.Send(SimIds.WorkOrder, "toggle"), Ui.ButtonOn); Ui.Size(wo, 210, 60);
            var recallRow = Ui.Rect(root, "RecallRow"); Ui.HStack(recallRow.gameObject, 10);
            var recall = Ui.Button(recallRow, "面板叫回眼前", 22, () => Ui.Send("xr.menu", "recall")); Ui.Size(recall, 300, 60);
            var vrow = Ui.Rect(root, "VideoRow"); Ui.HStack(vrow.gameObject, 10);
            video = Ui.Button(vrow, "", 22, () => Ui.Send("rec.video", "toggle", "xr.wrist"), new Color(0.6f, 0.12f, 0.1f)); Ui.Size(video, 300, 60);
            videoLabel = Ui.LabelOf(video);
            rec = Ui.Text(vrow, "", 24, new Color(1f, 0.3f, 0.3f), TextAnchor.MiddleLeft, FontStyle.Bold); Ui.Size(rec, 340, 60);
        }

        Button video;
        Text videoLabel, rec;

        public override void Refresh(ISimState s)
        {
            bool show = InputMode.XrActive && s.GetBool("training.running") && !s.GetBool("score.finished");
            Ui.SetActive(root, show);
            if (!show) return;
            string active = s.Get(SimIds.ActiveTool);
            bool carrying = !string.IsNullOrEmpty(s.Get("hand.item")) || IsCarryingNew(s);
            toolLine.text = "手上：" + (string.IsNullOrEmpty(active) ? (carrying ? "碳滑板" : "空") : Ui.ToolName(active));
            valueLine.text = ValueText(s, active);
            Ui.SetActive(valueLine, valueLine.text.Length > 0);
            float minor = s.GetFloat("score.minor"), major = s.GetFloat("score.major"), critical = s.GetFloat("score.critical");
            errors.text = $"錯誤 {minor + major + critical:0}" + (critical > 0 ? $"（嚴重 {critical:0}）" : "");
            AutoFill(s);
            string field = s.Get("xr.fill.field");
            Ui.SetActive(fill, field.Length > 0);
            if (field.Length > 0) fillLabel.text = $"填入作業單：{FieldLabel(FillTarget(s))} {s.Get("xr.fill.value")} mm";
            bool tutorial = s.Get("training.mode") != "practice";
            Ui.SetActive(back, tutorial);
            back.interactable = s.GetBool("procedure.can_back");
            boardLabel.text = s.GetBool("ui.board_hidden") ? "顯示提示板" : "收起提示板";
            bool capturing = s.GetBool("rec.video.active");
            videoLabel.text = capturing ? "■ 停止錄影" : "● 開始錄影";
            rec.text = Ui.RecText(s) + (s.GetBool("rec.active") ? (capturing ? "" : "● 紀錄中") : "");
        }

        /// <summary>
        /// T2-a 卡尺讀數穩定 0.5 s（XR 寫 xr.fill.auto＝量測點對應的格子）：填進那一格（不看目前選中哪格），選中移到下一個沒填的格子。
        /// </summary>
        public static void AutoFill(ISimState s)
        {
            string target = s.Get("xr.fill.auto");
            if (string.IsNullOrEmpty(target)) return;
            s.Set("xr.fill.auto", "");
            Ui.Send(SimIds.WorkOrder, s.Get("xr.fill.value"), target);
            s.Batch(() => { s.Set("xr.fill.field", ""); s.Set(WorkOrderView.SelectedKey, NextEmpty(s, target)); });
        }

        /// <summary>FieldOrder 裡 field 之後第一個還沒填（workorder.&lt;欄位&gt; 是空的）的格子；都填了回傳空字串。</summary>
        public static string NextEmpty(ISimState s, string field)
        {
            int i = System.Array.IndexOf(WorkOrderView.FieldOrder, field);
            for (int k = i + 1; k < WorkOrderView.FieldOrder.Length; k++)
                if (string.IsNullOrEmpty(s.Get("workorder." + WorkOrderView.FieldOrder[k]))) return WorkOrderView.FieldOrder[k];
            return "";
        }

        /// <summary>一按填入的目標：作業單上選中的數值格；沒有選中就用量測對應的格子（xr.fill.field）。</summary>
        public static string FillTarget(ISimState s)
        {
            string sel = s.Get(WorkOrderView.SelectedKey);
            return System.Array.IndexOf(WorkOrderView.FieldOrder, sel) >= 0 ? sel : s.Get("xr.fill.field");
        }

        static string FieldLabel(string field)
        {
            var p = field.Split('.');
            string side = p.Length > 1 ? (p[1] == "L" ? "靠關節" : "外側") : "";
            if (p[0] == "newmid") return side + "新滑板中點";
            return p.Length > 2 ? side + (p[2] == "left" ? "左" : p[2] == "mid" ? "中" : "右") : field;
        }

        static bool IsCarryingNew(ISimState s) => s.Get("part.carbon_strip_L.state") == "Carried" || s.Get("part.carbon_strip_R.state") == "Carried";

        static string ValueText(ISimState s, string tool)
        {
            switch (tool)
            {
                case SimIds.ToolCaliper: return $"讀數 {s.Get("tool.caliper.display", "--.-- mm")}　" + (s.GetBool("tool.caliper.zeroed") ? "已歸零" : Ui.Tint("未歸零", Ui.Bad));
                case SimIds.ToolTorque: return $"設定 {s.GetFloat("tool.torque_wrench.setting", 20f):0.0} N·m　目前 {s.GetFloat("tool.torque_wrench.current"):0.0}";
                case SimIds.ToolSocket:
                    bool loosen = s.Get("tool.socket_wrench.dir", "loosen") == "loosen";
                    return $"套筒 {s.Get("tool.socket_wrench.size", "10")} mm　方向 " + (loosen ? "↺ 鬆" : "↻ 鎖");
                case SimIds.ToolVoltageDetector: return (s.Get("tool.voltage_detector.led") == "green" ? "自檢正常　" : "尚未自檢　") + s.Get("tool.voltage_detector.display", "");
                case SimIds.ToolGroundingRod:
                    return (s.GetBool(SimIds.TerminalClamped) ? "● 端子" : "○ 端子") + "　" + (s.GetBool(SimIds.PullTested) ? "● 拉線" : "○ 拉線") + "　" + (s.GetBool(SimIds.HeadClamped) ? "● 弓頭" : "○ 弓頭");
                case SimIds.ToolCloth:
                    string side = s.Get("procedure.side", "L");
                    return $"托架清除率 {s.GetFloat($"ip.carbon_strip_{side}.bracket.clean_ratio") * 100f:0}%";
                default: return "";
            }
        }
    }

    /// <summary>手勢小卡（手腕面板上方）：第一次拿起工具時出現，學員第一次完成主要動作後才淡出（XR 模組控制，至少 6 秒）。</summary>
    public class XrGestureView : View
    {
        RectTransform root;
        Text title, body;
        CanvasGroup group;

        public RectTransform Root => root;

        protected override void OnBuild(RectTransform safe)
        {
            var img = Ui.Panel(safe, "XrGesture", new Color(0.08f, 0.2f, 0.32f, 0.95f), true, false);
            root = Ui.Place(img.rectTransform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(700, 0));
            Ui.VStack(img.gameObject, 16, 6);
            group = img.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            title = Ui.Text(root, "", 26, new Color(0.55f, 0.85f, 1f), TextAnchor.UpperLeft, FontStyle.Bold);
            body = Ui.Text(root, "", 24);
        }

        public override void Refresh(ISimState s)
        {
            string tool = s.Get("xr.gesture.tool");
            float fade = s.GetFloat("xr.gesture.fade", 1f);
            bool show = InputMode.XrActive && s.GetBool("training.running") && tool.Length > 0;
            Ui.SetActive(root, show);
            if (!show) return;
            group.alpha = Mathf.Clamp01(fade);
            title.text = "手勢：" + (tool == "carried" ? "碳滑板" : Ui.ToolName(tool));
            body.text = XrGestures.For(tool) + "\n" + Ui.Tint(XrGestures.Common, Ui.Muted);
        }
    }

    /// <summary>≡ 暫停選單：重做本步、切換模式（回任務卡）、重設高度與視角置中、離開。</summary>
    public class XrMenuView : View
    {
        RectTransform dim;

        protected override void OnBuild(RectTransform safe)
        {
            var (d, card) = Modal.Create(safe, "XrMenu", new Vector2(640, 0), new Color(0.3f, 0.7f, 1f));
            dim = d;
            Ui.VStack(card.gameObject, 28, 14);
            Ui.Text(card, "暫停選單", 34, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
            void Item(string label, string cmd, Color? c = null) { var b = Ui.Button(card, label, 28, () => Ui.Send("xr.menu", cmd), c); Ui.Size(b, -1, 78); }
            Item("從這一步重做", "redo", Ui.ButtonOn);
            Item("切換模式（回任務卡）", "mode");
            Item("面板叫回眼前", "recall");
            Item("重設高度與視角置中", "recenter");
            Ui.Text(card, "若覺得陷進地板或浮起來，請先在 Quest 重新設定地板", 20, Ui.Muted);
            // R1：高度、蹲低視角（T1）與平滑移動、速度、遮罩（T6）；這些設定按了不關選單
            Text Setting(string cmd) { var b = Ui.Button(card, "", 26, () => Ui.Send("xr.menu", cmd)); Ui.Size(b, -1, 66); return Ui.LabelOf(b); }
            heightLabel = Setting("height"); crouchLabel = Setting("crouch"); 
            bagLabel = Setting("toolbag_mount"); swapLabel = Setting("carry_swap");   // R1.5b b3：工具袋掛左手腕、工具袋與腰包左右對調
            var v = Ui.Button(card, "", 28, () => Ui.Send("rec.video", "toggle", "xr.menu"), new Color(0.6f, 0.12f, 0.1f)); Ui.Size(v, -1, 78);
            videoLabel = Ui.LabelOf(v);
            var cam = Ui.Button(card, "", 24, () => Ui.Send("rec.video", "view:toggle")); Ui.Size(cam, -1, 66);
            camLabel = Ui.LabelOf(cam);
            Item("離開", "quit", new Color(0.55f, 0.15f, 0.12f));
            Item("關閉選單（≡）", "close");
        }

        Text videoLabel, camLabel, heightLabel, crouchLabel, bagLabel, swapLabel;

        public override void Refresh(ISimState s)
        {
            bool show = InputMode.XrActive && s.GetBool("xr.menu_open");
            Ui.SetActive(dim, show);
            if (!show) return;
            heightLabel.text = "車頂視點：" + (s.Get("xr.height.mode", "Raised") == "Real" ? "真實高度" : "抬高（平視滑板）") + "　<size=20>（點一下切換）</size>";
            crouchLabel.text = "蹲低視角：" + (s.GetBool("xr.crouch") ? "開" : "關") + "　<size=20>（只在車頂有效，也可按左搖桿）</size>";
            bagLabel.text = "工具袋位置：" + (s.Get("xr.toolbag.mount") == "wrist" ? "左手腕" : "腰間") + "　<size=20>（點一下切換）</size>";
            swapLabel.text = "工具袋／腰包：" + (s.GetBool("xr.carry.swap") ? "工具袋右腰、腰包左腰" : "工具袋左腰、腰包右腰");
            string rec = Ui.RecText(s);
            videoLabel.text = s.GetBool("rec.video.active") ? $"■ 停止錄影　<size=22>{rec}</size>" : "● 開始錄影";
            camLabel.text = "旁觀鏡頭：" + (s.Get("rec.video.view", "third") == "head" ? "頭盔視角（穩定化）" : "第三人稱") + "　<size=20>（點一下切換）</size>";
        }
    }

    /// <summary>Y 說明：按鍵表＋手上工具的手勢。</summary>
    public class XrHelpView : View
    {
        RectTransform dim;
        Text gesture, keys;

        protected override void OnBuild(RectTransform safe)
        {
            var (d, card) = Modal.Create(safe, "XrHelp", new Vector2(1000, 0), new Color(0.3f, 0.7f, 1f));
            dim = d;
            Ui.VStack(card.gameObject, 28, 10);
            Ui.Text(card, "按鍵表", 34, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            keys = Ui.Text(card, XrGestures.KeyTable, 25);
            gesture = Ui.Text(card, "", 24, new Color(0.55f, 0.85f, 1f));
            var close = Ui.Button(card, "關閉（Y）", 26, () => Sim.State.SetBool("xr.help_open", false), Ui.ButtonOn); Ui.Size(close, -1, 70);
        }

        public override void Refresh(ISimState s)
        {
            bool show = InputMode.XrActive && s.GetBool("xr.help_open");
            Ui.SetActive(dim, show);
            if (!show) return;
            string table = s.Get("xr.help.keys");   // XR 依按鍵表（InputBindingTable）與目前情境產生
            keys.text = string.IsNullOrEmpty(table) ? XrGestures.KeyTable : table;
            string active = s.Get(SimIds.ActiveTool);
            gesture.text = string.IsNullOrEmpty(active) ? "手上沒有工具：到工具架、桿架或工具袋用握把拿起。" : $"<b>{Ui.ToolName(active)}</b>　" + XrGestures.For(active);
        }
    }

    /// <summary>開場約 30 秒的控制器教學：扳機、握把、傳送、轉向、手腕面板、Y 說明；完成後出現任務卡，扳機點「開始」。</summary>
    public class XrTutorialView : View
    {
        public static readonly string[] Steps =
        {
            "按一下右手<b>扳機</b>（食指）＝使用／點擊",
            "按一下右手<b>握把</b>（中指）＝抓取工具",
            "<b>左搖桿往前推</b>，瞄準地上的圓圈，放開＝傳送",
            "<b>左搖桿往前推</b>，傳送到地上的圈",
            "用<b>左握把</b>抓住面板上緣的橘色把手，移一下再放開",
            "<b>左搖桿往左或右撥</b>＝轉 45°",
            "抬起左手，看<b>手腕面板</b>（工具狀態、上一步）",
            "按 <b>Y</b>＝說明（按鍵表），再按一次 Y 關閉",
        };

        RectTransform dim;
        readonly List<Text> rows = new List<Text>();

        protected override void OnBuild(RectTransform safe)
        {
            var (d, card) = Modal.Create(safe, "XrTutorial", new Vector2(980, 0), new Color(0.3f, 0.7f, 1f));
            dim = d;
            Ui.VStack(card.gameObject, 30, 12);
            Ui.Text(card, "控制器教學（約 30 秒）", 36, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            Ui.Text(card, "照著做一次，做到的會打勾。", 24, Ui.Muted);
            foreach (var _ in Steps) rows.Add(Ui.Text(card, "", 28));
            var skip = Ui.Button(card, "略過教學", 24, () => Ui.Send("xr.tutorial", "skip")); Ui.Size(skip, -1, 64);
        }

        public override void Refresh(ISimState s)
        {
            bool show = InputMode.XrActive && s.GetBool("xr.tutorial.active") && !s.GetBool("xr.help_open");
            Ui.SetActive(dim, show);
            if (!show) return;
            int step = (int)s.GetFloat("xr.tutorial.step");
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].text = (i < step ? Ui.Tint("✔ ", Ui.Ok) : i == step ? Ui.Tint("► ", Ui.Accent) : "　 ") + Steps[i];
                rows[i].color = i == step ? Color.white : i < step ? Ui.Muted : new Color(0.55f, 0.6f, 0.66f);
            }
        }
    }

    /// <summary>右控制器按鍵旁的提示標籤（低頭看控制器時出現，取代桌面底部操作列）。</summary>
    public class ControllerLabelsView : View
    {
        RectTransform root;
        Text keys;
        public RectTransform Root => root;

        protected override void OnBuild(RectTransform safe)
        {
            var img = Ui.Panel(safe, "XrLabels", new Color(0.04f, 0.05f, 0.07f, 0.88f), true, false);
            root = Ui.Place(img.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420, 0));
            Ui.VStack(img.gameObject, 14, 4);
            keys = Ui.Text(root, "", 22);
        }

        public override void Refresh(ISimState s)
        {
            Ui.SetActive(root, InputMode.XrActive);
            keys.text = s.Get("xr.keys.card");   // XR 依按鍵表與目前情境產生
        }
    }
}
