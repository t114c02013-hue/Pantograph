using System.Collections.Generic;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PantographSim.UI
{
    /// <summary>
    /// 後台控制面板（教官用）：選課程、模式、從第幾步開始，按下去系統代做前面的步驟（app.jump）。
    /// 自己一塊 Screen Space Overlay 的 Canvas：只畫在電腦螢幕（Game 視窗／Link 鏡像），頭盔的眼睛畫面不畫 Overlay，所以頭盔裡看不到；
    /// XR 版面不搬它、不加控制器射線（XrModeSwitch 跳過 Overlay），用滑鼠點。固定在螢幕左上角。
    /// </summary>
    public class AdminPanel : MonoBehaviour
    {
        public const string CanvasName = "AdminCanvas";
        public const string Tutorial = "tutorial", Practice = "practice";
        const float Width = 400, Row = 23;
        const int FontSize = 15;

        static string mode = Tutorial, step = "";   // 重新載入場景後保留，同一步可以連續重練

        Canvas canvas;
        RectTransform canvasRect, panel, body;
        RectTransform courseList, stepList;
        Text status, collapseLabel;
        Image tutorialBg, practiceBg;
        Button start, stop;
        RectTransform bar, barFill;
        bool collapsed;
        string coursesBuiltFor, stepsBuiltFor;
        readonly List<(string id, Image bg, Button b)> courseButtons = new List<(string, Image, Button)>();
        readonly List<(string id, Image bg)> stepButtons = new List<(string, Image)>();

        public Canvas Canvas => canvas;

        /// <summary>解析「id|標籤」用 ; 分隔的清單。</summary>
        public static List<(string id, string label)> Parse(string list)
        {
            var result = new List<(string, string)>();
            foreach (var entry in (list ?? "").Split(';'))
            {
                int bar = entry.IndexOf('|');
                if (bar > 0) result.Add((entry.Substring(0, bar), entry.Substring(bar + 1)));
            }
            return result;
        }

        void Awake()
        {
            var go = new GameObject(CanvasName, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;   // 在介面和代做遮罩上面
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;   // 跟著高度縮放：29 個步驟要整張放進畫面
            go.AddComponent<GraphicRaycaster>();
            canvasRect = (RectTransform)go.transform;

            var bg = Ui.Panel(canvasRect, "AdminPanel");
            panel = Ui.Place(bg.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(Width, 0));
            Ui.VStack(panel.gameObject, 8, 2);
            var head = Ui.Rect(panel, "Head"); Ui.HStack(head.gameObject, 6);
            Ui.Size(Ui.Text(head, "後台控制（只有這台電腦看得到）", FontSize, Ui.Accent, TextAnchor.MiddleLeft, FontStyle.Bold), Width - 96, Row);
            var collapse = Ui.Button(head, "收合", FontSize, () => collapsed = !collapsed); Ui.Size(collapse, 70, Row);
            collapseLabel = Ui.LabelOf(collapse);

            body = Ui.Rect(panel, "Body"); Ui.VStack(body.gameObject, 0, 2);
            Label(body, "課程版本");
            courseList = Ui.Rect(body, "Courses"); Ui.VStack(courseList.gameObject, 0, 2);
            Label(body, "模式");
            var modes = Ui.Rect(body, "Modes"); Ui.HStack(modes.gameObject, 4);
            tutorialBg = Choice(modes, "教學", () => mode = Tutorial, (Width - 20) / 2);
            practiceBg = Choice(modes, "練習（考核）", () => mode = Practice, (Width - 20) / 2);
            Label(body, "從第幾步開始");
            stepList = Ui.Rect(body, "Steps"); Ui.VStack(stepList.gameObject, 0, 2);
            start = Ui.Button(body, "▶ 從這一步開始", FontSize + 1, () =>
            {
                Ui.Send("app.jump", mode + ":" + step);
                collapsed = true;   // 桌面的提示板在左邊，別蓋住
            }, new Color(0.2f, 0.6f, 0.25f));
            Ui.Size(start, -1, Row + 6);
            status = Ui.Text(panel, "", FontSize, Color.white);
            bar = Ui.Panel(panel, "AutoRunBar", new Color(0.15f, 0.15f, 0.18f), false, false).rectTransform; Ui.Size(bar, -1, 12);
            barFill = Ui.Panel(bar, "Fill", new Color(0.2f, 0.6f, 0.25f), false, false).rectTransform;
            Ui.Stretch(barFill);
            stop = Ui.Button(panel, "■ 停止代做", FontSize, () => Ui.Send("app.jump_stop", ""), new Color(0.6f, 0.2f, 0.2f));
            Ui.Size(stop, -1, Row + 4);
        }

        static void Label(RectTransform parent, string text) => Ui.Size(Ui.Text(parent, text, FontSize, Ui.Muted, TextAnchor.MiddleLeft), -1, Row);

        static Image Choice(RectTransform parent, string label, System.Action onClick, float width = -1)
        {
            var b = Ui.Button(parent, label, FontSize, onClick);
            Ui.LabelOf(b).alignment = TextAnchor.MiddleLeft;
            Ui.Size(b, width, Row);
            return b.GetComponent<Image>();
        }

        void LateUpdate()
        {
            var s = Sim.State;
            bool show = s != null && !SimSession.IsReplay;
            if (canvas.enabled != show) canvas.enabled = show;
            if (!show) return;
            // Game 視窗放大時（ViewportInfo.Zoom）畫面只看得到中間一塊：對齊看得到的左上角
            float zoom = Mathf.Max(1f, ViewportInfo.Zoom);
            var size = canvasRect.rect.size;
            panel.anchoredPosition = new Vector2(size.x, -size.y) * (1f - 1f / zoom) / 2f;

            bool running = s.GetBool(SimIds.AutoRunActive);
            Ui.SetActive(body, !collapsed);
            collapseLabel.text = collapsed ? "展開" : "收合";
            // 代做中／做完：App 寫的進度那一行；走不到、按了停止：AutoRunStatus
            string why = s.Get(SimIds.AutoRunStatus);
            status.text = running || why.Length == 0 ? s.Get(SimIds.AutoRunProgressText) : why;
            Ui.SetActive(status, status.text.Length > 0);
            Ui.SetActive(bar, running || (why.Length == 0 && s.GetFloat(SimIds.AutoRunProgress) >= 1f));
            barFill.anchorMax = new Vector2(Mathf.Clamp01(s.GetFloat(SimIds.AutoRunProgress)), 1f);
            Ui.SetActive(stop, running);
            if (collapsed) return;

            string courses = s.Get("app.courses.list");
            if (courses != coursesBuiltFor)
            {
                coursesBuiltFor = courses;
                Clear(courseList); courseButtons.Clear();
                foreach (var (id, title) in Parse(courses))
                {
                    bool ready = id == SimIds.CourseR1;   // 代做腳本目前只有 R1
                    string name = title.Split('|')[0];   // 「名稱|註記」只顯示名稱
                    string courseId = id;
                    var bg = Choice(courseList, ready ? name : name + "（尚未提供）", () => { if (courseId != Sim.State.Get(SimIds.CourseId)) Ui.Send("app.course", courseId); });
                    courseButtons.Add((id, bg, bg.GetComponent<Button>()));
                }
            }
            string steps = s.Get(SimIds.StepsList);
            if (steps != stepsBuiltFor)
            {
                stepsBuiltFor = steps;
                Clear(stepList); stepButtons.Clear();
                foreach (var (id, title) in Parse(steps))
                {
                    string stepId = id;
                    stepButtons.Add((id, Choice(stepList, title, () => step = stepId)));
                }
            }
            foreach (var (id, bg, b) in courseButtons)
            {
                bg.color = id == s.Get(SimIds.CourseId) ? Ui.ButtonOn : Ui.ButtonBg;
                b.interactable = !running && id == SimIds.CourseR1;
            }
            foreach (var (id, bg) in stepButtons) bg.color = id == step ? Ui.ButtonOn : Ui.ButtonBg;
            tutorialBg.color = mode == Tutorial ? Ui.ButtonOn : Ui.ButtonBg;
            practiceBg.color = mode == Practice ? Ui.ButtonOn : Ui.ButtonBg;
            start.interactable = !running && stepButtons.Exists(x => x.id == step);
        }

        static void Clear(RectTransform list) { foreach (Transform c in list) Destroy(c.gameObject); }
    }

    /// <summary>
    /// 代做期間的遮罩：蓋住畫面、顯示「正在前往第 N 步…」，交接後淡回。桌面在主 Canvas 最上層；VR 由 XrPanelLayout 搬到跟著頭的 Canvas，鏡像畫面同樣看得到。
    /// </summary>
    public class AutoRunMaskView : View
    {
        public const string Name = "AutoRunMask";
        RectTransform root;
        CanvasGroup group;
        Text text;

        protected override void OnBuild(RectTransform safe)
        {
            root = Ui.Panel(safe, Name, Color.black, false).rectTransform;
            Ui.Stretch(root, -SliceUI.Margin, -SliceUI.Margin, -SliceUI.Margin, -SliceUI.Margin);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            text = Ui.Text(root, "", 48, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(text.rectTransform);
            root.gameObject.SetActive(false);
        }

        public override void Refresh(ISimState s)
        {
            bool on = s.GetBool(SimIds.AutoRunActive) && !SimSession.IsReplay;
            float seconds = Mathf.Max(0.01f, s.GetFloat(SimIds.AutoRunFade));
            group.alpha = on ? 1f : Mathf.MoveTowards(group.alpha, 0f, Time.unscaledDeltaTime / seconds);
            Ui.SetActive(root, group.alpha > 0f);
            if (on) text.text = s.Get(SimIds.AutoRunText);
        }
    }
}
