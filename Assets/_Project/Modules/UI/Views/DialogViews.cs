using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PantographSim.UI
{
    /// <summary>開場任務卡：目標、工具、安全重點、操作鍵；大的置中「開始」按鈕；下方練習情境。</summary>
    public class TaskCardView : View
    {
        RectTransform dim, practiceRow, nameDesktop, nameXr, courseRow, faultRow;
        string faultsBuiltFor = "";
        readonly List<(string key, Image bg)> faultButtons = new List<(string, Image)>();
        string coursesBuiltFor = "";
        readonly List<(string id, Image bg)> courseButtons = new List<(string, Image)>();
        Text title, goal, tools, safety, controls, xrName;
        InputField nameField;
        string builtFor = "";
        int nameIndex;
        const string DesktopControls = "<b>操作</b>：到工具架點掛位拿起／歸還工具（手上一次一件；數字鍵 1–9 會高亮掛位）　左鍵點互動點　右鍵拖曳轉視角　滾輪縮放　F 拉近檢視　C 帶我到目標　R 轉動手上的滑板　Backspace 重設工具　H 完整說明　{錄影鍵} 開始／停止錄影";
        const string XrControls = "<b>操作（VR）</b>：扳機＝使用／點擊　握把＝拿工具（在掛位前放開＝歸還）　左搖桿＝傳送／轉向　工具數值＝看左手開工具面板　A 重設工具　B 帶我到目標　X 作業單　Y 說明　≡ 暫停選單（含「● 開始錄影」）";

        protected override void OnBuild(RectTransform safe)
        {
            var (d, card) = Modal.Create(safe, "TaskCard", new Vector2(1180, 1000));
            dim = d;
            Ui.VStack(card.gameObject, 30, 10, false);
            title = Ui.Text(card, "", 38, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
            // R1.5b b4：課程選擇（換課程會重新載入場景）
            courseRow = Ui.Rect(card, "CourseRow"); Ui.HStack(courseRow.gameObject, 12).childAlignment = TextAnchor.MiddleCenter;
            faultRow = Ui.Rect(card, "FaultRow"); Ui.HStack(faultRow.gameObject, 12).childAlignment = TextAnchor.MiddleCenter;   // D43：可選故障（教官開關）
            goal = Ui.Text(card, "", 24);
            tools = Ui.Text(card, "", 23);
            safety = Ui.Text(card, "", 22);
            controls = Ui.Text(card, DesktopControls, 22, Ui.Muted);
            Ui.Text(card, "6.0 mm 更換基準為保養表值；其他數值（扭力 25 N·m、新品 18.0 mm 等）為示例值。", 20, Ui.Muted);

            // 學員名稱：桌面打字（留空＝「學員」＋日期時間）；VR 不打字，沿用上次的名稱或從用過的名稱中選
            nameDesktop = Ui.Rect(card, "TraineeDesktop"); Ui.HStack(nameDesktop.gameObject, 12).childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(Ui.Text(nameDesktop, "學員名稱", 24, Ui.Accent, TextAnchor.MiddleRight, FontStyle.Bold), 130, 52);
            nameField = Ui.Input(nameDesktop, 24, "留空＝「學員」＋日期時間");
            Ui.Size(nameField, 420, 52);
            var list = Ui.Button(nameDesktop, "紀錄清單", 22, () => Ui.Send("rec", "list.open"), new Color(0.15f, 0.45f, 0.75f)); Ui.Size(list, 170, 52);
            nameXr = Ui.Rect(card, "TraineeXr"); Ui.HStack(nameXr.gameObject, 12).childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(Ui.Text(nameXr, "學員", 24, Ui.Accent, TextAnchor.MiddleRight, FontStyle.Bold), 80, 56);
            var prev = Ui.Button(nameXr, "◀", 26, () => nameIndex--); Ui.Size(prev, 70, 56);
            xrName = Ui.Text(nameXr, "", 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold); Ui.Size(xrName, 380, 56);
            var next = Ui.Button(nameXr, "▶", 26, () => nameIndex++); Ui.Size(next, 70, 56);
            var listXr = Ui.Button(nameXr, "紀錄清單", 22, () => Ui.Send("rec", "list.open"), new Color(0.15f, 0.45f, 0.75f)); Ui.Size(listXr, 170, 56);

            var startRow = Ui.Rect(card, "StartRow"); var h = Ui.HStack(startRow.gameObject, 0); h.childAlignment = TextAnchor.MiddleCenter;
            var start = Ui.Button(startRow, "▶  開始（教學模式）", 34, () => Start("tutorial:SCN_Default"), new Color(0.2f, 0.6f, 0.25f));
            Ui.Size(start, 560, 96);
            Ui.Text(card, "練習模式（沒有高亮與箭頭，自己判斷）：", 22, Ui.Accent, TextAnchor.UpperCenter);
            practiceRow = Ui.Rect(card, "Practice"); var ph = Ui.HStack(practiceRow.gameObject, 12); ph.childAlignment = TextAnchor.MiddleCenter;
        }

        public override void Refresh(ISimState s)
        {
            // VR：開場控制器教學做完才出現任務卡（扳機點「開始」）
            bool show = !s.GetBool("training.running") && (!InputMode.XrActive || s.GetBool("xr.tutorial.done")) && !s.GetBool("rec.list_open");
            Ui.SetActive(dim, show);
            if (!show) return;
            controls.text = InputMode.XrActive ? XrControls : DesktopControls.Replace("{錄影鍵}", s.Get("ui.video_key"));
            Ui.SetActive(nameDesktop, !InputMode.XrActive);
            Ui.SetActive(nameXr, InputMode.XrActive);
            if (InputMode.XrActive) xrName.text = XrNames(s)[Wrap(nameIndex, XrNames(s).Count)];
            title.text = "任務卡　" + s.Get("task.title");
            goal.text = "<b>目標</b>：" + s.Get("task.goal");
            tools.text = "<b>工具</b>：" + s.Get("task.tools");
            safety.text = "<b>安全重點</b>\n・" + s.Get("task.safety").Replace("\n", "\n・");
            var courses = s.Get("app.courses.list");
            if (courses != coursesBuiltFor)
            {
                foreach (Transform c in courseRow) Object.Destroy(c.gameObject);
                courseButtons.Clear();
                foreach (var entry in courses.Split(';'))
                {
                    var f = entry.Split('|');
                    if (f.Length < 2) continue;
                    string id = f[0], label = f[1] + (f.Length > 2 && f[2].Length > 0 ? "（" + f[2] + "）" : "");
                    var cb = Ui.Button(courseRow, label, 21, () => Ui.Send("app.course", id));
                    Ui.Size(cb, 330, 56);
                    courseButtons.Add((id, cb.GetComponent<Image>()));
                }
                coursesBuiltFor = courses;
            }
            foreach (var (id, bg) in courseButtons) bg.color = id == s.Get(SimIds.CourseId) ? Ui.ButtonOn : Ui.ButtonBg;
            var faults = s.Get("app.faults.list");
            if (faults != faultsBuiltFor)
            {
                foreach (Transform c in faultRow) Object.Destroy(c.gameObject);
                faultButtons.Clear();
                foreach (var entry in faults.Split(';'))
                {
                    var f = entry.Split('|');
                    if (f.Length < 2) continue;
                    string key = f[0];
                    var fb = Ui.Button(faultRow, f[1], 20, () => Ui.Send("app.fault", key));
                    Ui.Size(fb, 330, 52);
                    faultButtons.Add((key, fb.GetComponent<Image>()));
                }
                faultsBuiltFor = faults;
            }
            foreach (var (key, bg) in faultButtons) bg.color = s.GetBool(key) ? Ui.ButtonOn : Ui.ButtonBg;   // 開著＝亮
            var list = s.Get("scenarios.list");
            if (list != builtFor)
            {
                foreach (Transform c in practiceRow) Object.Destroy(c.gameObject);
                foreach (var entry in list.Split(';'))
                {
                    var f = entry.Split('|');
                    if (f.Length < 2) continue;
                    string id = f[0];
                    var b = Ui.Button(practiceRow, f[1], 21, () => Start("practice:" + id));
                    Ui.Size(b, 250, 60);
                }
                builtFor = list;
            }
        }

        const string NewNameOption = "（新學員：學員＋日期時間）";

        /// <summary>VR 名稱清單：曾經用過的名稱（第一個是上次用的），最後一個選項是自動命名。</summary>
        static List<string> XrNames(ISimState s)
        {
            var names = s.Get("rec.names").Split(';').Where(x => x.Length > 0).ToList();
            names.Add(NewNameOption);
            return names;
        }

        static int Wrap(int i, int n) => n <= 0 ? 0 : ((i % n) + n) % n;

        /// <summary>按開始：先告訴紀錄服務學員名稱，再開始訓練（紀錄會自動開始）。</summary>
        void Start(string modeAndScenario)
        {
            string name;
            if (InputMode.XrActive)
            {
                var names = XrNames(Sim.State);
                name = names[Wrap(nameIndex, names.Count)];
                if (name == NewNameOption) name = "";
            }
            else name = nameField != null ? nameField.text.Trim() : "";
            Ui.Send("rec", "trainee:" + name);
            Ui.Send("app.start", modeAndScenario);
        }
    }

    /// <summary>第一次拿起工具時的教學卡：文字＋操作鍵，「我知道了」關閉。</summary>
    public class TutorialView : View
    {
        RectTransform dim;
        Text title, heading, body;
        Button prev, next;
        RectTransform buttons;
        int page; string shownTool;

        protected override void OnBuild(RectTransform safe)
        {
            var (d, card) = Modal.Create(safe, "Tutorial", new Vector2(860, 480), new Color(0.3f, 0.7f, 1f));
            dim = d;
            Ui.VStack(card.gameObject, 28, 12, false);
            title = Ui.Text(card, "", 30, new Color(0.5f, 0.85f, 1f), TextAnchor.UpperLeft, FontStyle.Bold);
            heading = Ui.Text(card, "", 27, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            body = Ui.Text(card, "", 24);
            Ui.Size(body, -1, 200);
            var row = Ui.Rect(card, "Buttons"); var h = Ui.HStack(row.gameObject, 12); h.childAlignment = TextAnchor.MiddleRight;
            buttons = row;
            prev = Ui.Button(row, "上一頁", 22, () => page--); Ui.Size(prev, 150, 56);
            next = Ui.Button(row, "下一頁", 22, () => page++); Ui.Size(next, 150, 56);
            var ok = Ui.Button(row, "我知道了", 24, () => Ui.Send("ui.tutorial", "close"), Ui.ButtonOn); Ui.Size(ok, 200, 56);
        }

        public override void Refresh(ISimState s)
        {
            string tool = s.Get("ui.tutorial.tool");
            bool show = !string.IsNullOrEmpty(tool) && !s.GetBool("ui.error.active") && s.GetBool("training.running");
            Ui.SetActive(dim, show);
            if (!show) return;
            if (tool != shownTool) { shownTool = tool; page = 0; }
            var pages = s.Get("ui.tutorial.pages").Split('\u001e');
            Ui.SetActive(buttons, !InputMode.XrActive);
            if (InputMode.XrActive) { RefreshToolCard(s, tool, pages); return; }
            page = Mathf.Clamp(page, 0, pages.Length - 1);
            var f = pages[page].Split('\u001f');
            title.text = $"工具教學：{s.Get("ui.tutorial.title")}　<size=22>（{page + 1}/{pages.Length}，{Ui.ToolSpot(tool)}）</size>";
            heading.text = f.Length > 0 ? f[0] : "";
            body.text = f.Length > 1 ? f[1] : "";
            if (InputMode.XrActive && XrGestures.For(tool).Length > 0) body.text += "\n" + Ui.Tint("VR：" + XrGestures.For(tool), Ui.Accent);
            prev.interactable = page > 0;
            next.interactable = page < pages.Length - 1;
        }

        /// <summary>VR：工具旁邊的小視窗沒有按鈕（工具在右手，射線點不到自己旁邊），全部頁面一次列出，放回工具就收起。</summary>
        void RefreshToolCard(ISimState s, string tool, string[] pages)
        {
            title.text = s.Get("ui.tutorial.title");
            heading.text = "";
            var lines = new List<string>();
            foreach (var p in pages)
            {
                var f = p.Split('\u001f');
                lines.Add(f.Length > 1 ? Ui.Tint(f[0], Ui.Accent) + "　" + f[1] : p);
            }
            if (XrGestures.For(tool).Length > 0) lines.Add(Ui.Tint("VR：" + XrGestures.For(tool), Ui.Accent));
            body.text = string.Join("\n", lines);
            body.resizeTextForBestFit = true;
            body.resizeTextMaxSize = 22; body.resizeTextMinSize = 14;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            Ui.Size(body, -1, 400);
        }
    }

    /// <summary>錯誤對話框：紅色標題，「做錯什麼／為什麼／怎麼改」三段；按鈕「從這一步重做」「繼續」。不顯示內部代碼。</summary>
    public class ErrorDialogView : View
    {
        RectTransform dim;
        Text title, what, why, fix;
        Image titleBar;
        Button cont;
        Text contLabel;

        protected override void OnBuild(RectTransform safe)
        {
            var (d, card) = Modal.Create(safe, "Error", new Vector2(880, 0), Ui.Bad);
            dim = d;
            Ui.VStack(card.gameObject, 0, 0);
            titleBar = Ui.Panel(card, "TitleBar", new Color(0.75f, 0.12f, 0.1f), false, false);
            Ui.VStack(titleBar.gameObject, 18, 0);
            title = Ui.Text(titleBar.transform, "", 32, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            var content = Ui.Rect(card, "Content"); Ui.VStack(content.gameObject, 24, 8);
            Ui.Text(content, "做錯什麼", 22, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold); what = Ui.Text(content, "", 25);
            Ui.Text(content, "為什麼", 22, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold); why = Ui.Text(content, "", 25);
            Ui.Text(content, "怎麼改", 22, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold); fix = Ui.Text(content, "", 25, Ui.Ok);
            var row = Ui.Rect(content, "Buttons"); var h = Ui.HStack(row.gameObject, 14); h.childAlignment = TextAnchor.MiddleCenter;
            var redo = Ui.Button(row, "從這一步重做", 25, () => Ui.Send("ui.error", "restart"), Ui.ButtonOn); Ui.Size(redo, 300, 64);
            cont = Ui.Button(row, "繼續", 25, () => Ui.Send("ui.error", "dismiss")); Ui.Size(cont, 300, 64);
            contLabel = Ui.LabelOf(cont);
        }

        public override void Refresh(ISimState s)
        {
            bool show = s.GetBool("ui.error.active");
            Ui.SetActive(dim, show);
            if (!show) return;
            string sev = s.Get("ui.error.severity");
            bool blocking = s.GetBool("ui.error.blocking");
            title.text = "⚠ " + (sev == "Critical" ? "嚴重錯誤：安全或品質問題，必須重做" : sev == "Major" ? "錯誤：這樣做不對" : "小提醒");
            titleBar.color = sev == "Critical" ? new Color(0.75f, 0.12f, 0.1f) : sev == "Major" ? new Color(0.8f, 0.42f, 0.05f) : new Color(0.55f, 0.5f, 0.1f);
            what.text = s.Get("ui.error.what");
            why.text = s.Get("ui.error.why");
            fix.text = s.Get("ui.error.fix");
            cont.interactable = !blocking;
            contLabel.text = blocking ? "繼續（嚴重錯誤需重做）" : "繼續";
        }
    }

    /// <summary>結算：分數、是否通過、各級錯誤數、用時、錯誤紀錄。</summary>
    public class ResultView : View
    {
        RectTransform dim;
        Text score, detail, presetNote, log, folder;

        protected override void OnBuild(RectTransform safe)
        {
            var (d, card) = Modal.Create(safe, "Result", new Vector2(980, 960));
            dim = d;
            Ui.VStack(card.gameObject, 30, 12, false);
            Ui.Text(card, "結算　碳滑板更換", 36, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
            score = Ui.Text(card, "", 44, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
            detail = Ui.Text(card, "", 24, Ui.Muted, TextAnchor.UpperCenter);
            presetNote = Ui.Text(card, "", 20, Ui.Muted, TextAnchor.UpperCenter);
            Ui.Text(card, "錯誤紀錄", 24, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            log = Ui.Text(card, "", 21);
            Ui.Size(log, -1, 360);
            folder = Ui.Text(card, "", 18, Ui.Muted);
            var row = Ui.Rect(card, "RecordRow"); var h = Ui.HStack(row.gameObject, 12); h.childAlignment = TextAnchor.MiddleCenter;
            var replay = Ui.Button(row, "重播這次操作", 24, () => Ui.Send("rec", "replay:"), new Color(0.15f, 0.45f, 0.75f)); Ui.Size(replay, 260, 64);
            var report = Ui.Button(row, "開啟報表", 24, () => Ui.Send("rec", "report:")); Ui.Size(report, 200, 64);
            var open = Ui.Button(row, "開啟資料夾", 24, () => Ui.Send("rec", "open:")); Ui.Size(open, 200, 64);
            var list = Ui.Button(row, "紀錄清單", 24, () => Ui.Send("rec", "list.open")); Ui.Size(list, 180, 64);
            var again = Ui.Button(card, "再做一次", 28, () => Ui.Send("app.restart", ""), Ui.ButtonOn); Ui.Size(again, -1, 72);
        }

        public override void Refresh(ISimState s)
        {
            bool show = s.GetBool("score.finished") && !s.GetBool("rec.list_open") && !s.GetBool("replay.active");
            Ui.SetActive(dim, show);
            if (!show) return;
            string rf = s.Get("rec.folder");
            folder.text = rf.Length == 0 ? "（這一場沒有紀錄）"
                : "紀錄已存在：" + rf + (s.Get("rec.report").Length > 0 ? "\n報表：report.html（可用瀏覽器「列印 → 另存成 PDF」）" : "\n報表產生中…");
            bool passed = s.GetBool("score.passed");
            score.text = $"{s.GetFloat("score.total"):0} 分　" + (passed ? Ui.Tint("通過", Ui.Ok) : Ui.Tint("未通過", Ui.Bad));
            float sec = s.GetFloat("score.elapsed");
            detail.text = $"用時 {(int)(sec / 60)} 分 {(int)(sec % 60)} 秒　嚴重 {s.GetFloat("score.critical"):0}　錯誤 {s.GetFloat("score.major"):0}　提醒 {s.GetFloat("score.minor"):0}　（計分方式為示例值）";
            presetNote.text = s.Get("procedure.preset_note");
            Ui.SetActive(presetNote, presetNote.text.Length > 0);
            var l = s.Get("score.log");
            log.text = string.IsNullOrEmpty(l) ? "沒有錯誤，做得好！" : StripCodes(l);
        }

        static string StripCodes(string log) => log.Replace("[Critical]", "【嚴重】").Replace("[Major]", "【錯誤】").Replace("[Minor]", "【提醒】");
    }

    /// <summary>完整說明（H）。</summary>
    public class HelpView : View
    {
        RectTransform dim; Text body;

        protected override void OnBuild(RectTransform safe)
        {
            var (d, card) = Modal.Create(safe, "Help", new Vector2(1000, 840), new Color(0.3f, 0.7f, 1f));
            dim = d;
            Ui.VStack(card.gameObject, 30, 10, false);
            Ui.Text(card, "操作說明", 34, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            body = Ui.Text(card,
                "<b>看哪裡</b>：左側面板寫著「現在要做、為什麼、需要工具、點哪裡」，下面的清單會一項一項打勾。\n" +
                "場景中閃爍發黃的就是要點的東西，上方有「▼ 點這裡」標籤。看不到時按「帶我過去」或 C。\n\n" +
                "<b>工具</b>：到工具架點掛位拿起，用完點同一格歸還；手上一次只能拿一件。數字鍵 1–9 只會高亮掛位。第一次拿起會跳出教學。\n" +
                "  1 上鎖掛牌組　2 驗電器　3 接地桿　4 安全帶　5 卡尺　6 套筒扳手　7 扭力扳手　8 清潔布　9 標記筆\n  手上那件做錯想重來：再按一次它的數字鍵、按「↺ 重設工具」或 Backspace。\n\n" +
                "<b>滑鼠</b>：左鍵點互動點；右鍵拖曳轉視角；滾輪縮放（拿卡尺／套筒時滾輪改成操作工具）；中鍵拖曳平移。\n" +
                "<b>鍵盤</b>：F 拉近檢視滑鼠下的物件、Esc 返回；R 轉動手上的新滑板；C 帶我到目標；H 開關本說明。\n\n" +
                "<b>作業單</b>：右上「作業單」按鈕。量測讀數要自己抄，送出後系統會比對。\n" +
                "<b>做錯時</b>：會跳出說明「做錯什麼、為什麼、怎麼改」。嚴重錯誤要按「從這一步重做」。\n" +
                "<b>紀錄與錄影</b>：按「開始」就自動記錄（左上角「● 紀錄中」）；{錄影鍵} 開始／停止錄影，影片存在這一場的紀錄資料夾。", 23);
            var close = Ui.Button(card, "關閉（H）", 24, () => Sim.State.SetBool("ui.help_open", false), Ui.ButtonOn); Ui.Size(close, -1, 60);
        }

        public override void Refresh(ISimState s)
        {
            bool open = s.GetBool("ui.help_open") && !InputMode.XrActive;
            Ui.SetActive(dim, open);
            if (open) body.text = body.text.Replace("{錄影鍵}", s.Get("ui.video_key"));
        }
    }

    /// <summary>小視窗：舊滑板取出方式、備品檢視、零件盒內容，以及短暫提示。</summary>
    public class PromptViews : View
    {
        static string toast = "";
        static float toastUntil;
        static readonly Queue<string> notices = new Queue<string>();
        RectTransform removalDim, spareDim, trayPanel, toastPanel;
        Text spareModel, spareCrack, spareThick, trayText, toastText;
        string removalSide = "L";

        public static void OnStateChanged(StateChanged e)
        {
            foreach (var c in e.Changes) OnChange(c.key, c.newValue);
        }

        static void OnChange(string key, string text)
        {
            if (key.EndsWith(".remove_blocked") && text == "bolts") Show("螺栓還沒全部取出，不能拿下滑板");
            else if (key.EndsWith(".pick_result") && text == "not_measured") Show("選用前先用卡尺量這條備品的中點厚度");
            else if (key == "workorder.last_error" && !string.IsNullOrEmpty(text)) Show(text);
            else if (key == "ui.toast" && !string.IsNullOrEmpty(text)) Show(text);
            else if (key == "ui.notice" && !string.IsNullOrEmpty(text)) foreach (var line in text.Split('\n')) notices.Enqueue(line);
            else if (key == "part.pan_head.inspected" && text == "true") Show("已親眼確認弓頭");
        }

        static void Show(string text) { toast = text; toastUntil = Time.realtimeSinceStartup + 3f; }

        /// <summary>現在該顯示的提示：一般提示優先，空出來就換下一則說明，一則接一則。</summary>
        public static string ToastAt(float now, float noticeSeconds)
        {
            if (now >= toastUntil && notices.Count > 0) { toast = notices.Dequeue(); toastUntil = now + noticeSeconds; }
            return now < toastUntil ? toast : "";
        }

        public static void AddNotices(string text) => OnChange("ui.notice", text);
        public static void ClearToasts() { notices.Clear(); toast = ""; toastUntil = 0f; }

        protected override void OnBuild(RectTransform safe)
        {
            var (rd, rc) = Modal.Create(safe, "Removal", new Vector2(720, 280));
            removalDim = rd;
            Ui.VStack(rc.gameObject, 28, 14, false);
            Ui.Text(rc, "要怎麼取出舊滑板？", 30, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            var row = Ui.Rect(rc, "Row"); var h = Ui.HStack(row.gameObject, 16); h.childAlignment = TextAnchor.MiddleCenter;
            var v = Ui.Button(row, "垂直取出", 26, () => Ui.Send("part.carbon_strip_" + removalSide, "remove:vertical"), Ui.ButtonOn); Ui.Size(v, 300, 72);
            var p = Ui.Button(row, "用起子撬起", 26, () => Ui.Send("part.carbon_strip_" + removalSide, "remove:pry")); Ui.Size(p, 300, 72);

            var (sd, sc) = Modal.Create(safe, "Spare", new Vector2(760, 420));
            spareDim = sd;
            Ui.VStack(sc.gameObject, 28, 10, false);
            Ui.Text(sc, "檢視備品滑板", 30, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            spareModel = Ui.Text(sc, "", 26);
            Ui.Text(sc, $"作業單要求：型號 CS-1100-74A、新品厚度 {Sim.State?.Get("strip.new_text", "18.0 ± 0.2 mm") ?? "18.0 ± 0.2 mm"}（示例值）", 21, Ui.Muted);
            spareCrack = Ui.Text(sc, "", 25);
            spareThick = Ui.Text(sc, "", 25);
            var srow = Ui.Rect(sc, "Row"); var sh = Ui.HStack(srow.gameObject, 12); sh.childAlignment = TextAnchor.MiddleCenter;
            var flip = Ui.Button(srow, "翻面查看", 23, () => Ui.Send(Sim.State.Get("ui.spare_inspect"), "flip")); Ui.Size(flip, 200, 60);
            var pick = Ui.Button(srow, "選用此備品", 23, () => Ui.Send(Sim.State.Get("ui.spare_inspect"), "pick"), Ui.ButtonOn); Ui.Size(pick, 220, 60);
            var close = Ui.Button(srow, "關閉", 23, () => Ui.Send(Sim.State.Get("ui.spare_inspect"), "close")); Ui.Size(close, 160, 60);

            var tp = Ui.Panel(safe, "Tray");
            trayPanel = Ui.Place(tp.rectTransform, new Vector2(1, 1), new Vector2(-490, -66), new Vector2(340, 0));
            Ui.VStack(tp.gameObject, 16, 6);
            Ui.Text(trayPanel, "零件盒", 26, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            trayText = Ui.Text(trayPanel, "", 22);

            var tt = Ui.Panel(safe, "Toast", new Color(0.1f, 0.3f, 0.5f, 0.95f));
            toastPanel = Ui.Place(tt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 220), new Vector2(720, 64));
            toastText = Ui.Text(toastPanel, "", 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(toastText.rectTransform, 10, 6, 10, 6);
        }

        public override void Refresh(ISimState s)
        {
            bool running = s.GetBool("training.running");
            removalSide = s.Get("procedure.side", "L");
            if (string.IsNullOrEmpty(removalSide)) removalSide = "L";
            Ui.SetActive(removalDim, running && s.GetBool($"part.carbon_strip_{removalSide}.removal_prompt") && s.Get($"part.carbon_strip_{removalSide}.state") == "OldMounted");

            string slot = s.Get("ui.spare_inspect");
            Ui.SetActive(spareDim, running && !string.IsNullOrEmpty(slot));
            if (!string.IsNullOrEmpty(slot))
            {
                spareModel.text = "標籤型號：<b>" + s.Get("ui.spare_inspect.model") + "</b>";
                spareCrack.text = s.GetBool(slot + ".flipped")
                    ? (s.GetBool("ui.spare_inspect.crack_visible") ? Ui.Tint("背面：發現裂紋！不能使用", Ui.Bad) : Ui.Tint("背面：沒有裂紋", Ui.Ok))
                    : "背面：還沒檢查（按「翻面查看」）";
                spareThick.text = s.GetBool(slot + ".measured") ? $"中點厚度：{s.GetFloat(slot + ".reading"):0.00} mm" : "中點厚度：還沒量（按 3 拿卡尺點這條備品）";
            }

            Ui.SetActive(trayPanel, running && s.GetBool("ui.tray_open"));
            trayText.text = $"螺栓：{s.GetFloat("tray.bolts"):0} 支\n舊滑板：{s.GetFloat("tray.old_strips"):0} 條\n裝回螺栓：套筒方向切到「鎖」，點空的螺栓孔。";

            if (!running) notices.Clear();
            string shown = ToastAt(Time.realtimeSinceStartup, s.GetFloat("ui.notice_seconds", 6f));
            bool toastOn = running && !string.IsNullOrEmpty(shown);
            Ui.SetActive(toastPanel, toastOn);
            if (toastOn) toastText.text = shown;
        }
    }
}
