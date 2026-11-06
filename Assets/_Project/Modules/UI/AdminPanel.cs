using System.Collections.Generic;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PantographSim.UI
{
    /// <summary>
    /// 後台控制面板（教官用）：選課程、模式、從第幾步開始，按下去系統代做前面的步驟（app.jump）。
    /// 用 IMGUI：只畫在電腦螢幕（Game 視窗／Link 鏡像），不經過任何攝影機，頭盔裡看不到；
    /// 也不是 Canvas，XR 的版面搬移、射線都碰不到它，切到 VR 後的 EventSystem 也不影響滑鼠。固定在螢幕左上角。
    /// </summary>
    public class AdminPanel : MonoBehaviour
    {
        const float Width = 400, Row = 28, Gap = 4, Pad = 10, DesignHeight = 1080;
        const int FontSize = 16;
        public const string Tutorial = "tutorial", Practice = "practice";

        static string mode = Tutorial, step = "";   // 重新載入場景後保留，同一步可以連續重練
        static Rect screenRect;
        bool collapsed;
        GUIStyle label, button;
        float y;

        /// <summary>滑鼠在面板上（桌面模式這時不點場景）。</summary>
        public static bool Hover
        {
            get { var m = Input.mousePosition; return screenRect.Contains(new Vector2(m.x, Screen.height - m.y)); }
        }

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

        void OnDestroy() => screenRect = default;

        void OnGUI()
        {
            var s = Sim.State;
            if (s == null || SimSession.IsReplay) { screenRect = default; return; }
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { font = Ui.Font, fontSize = FontSize, alignment = TextAnchor.MiddleLeft, wordWrap = true };
                button = new GUIStyle(GUI.skin.button) { font = Ui.Font, fontSize = FontSize, alignment = TextAnchor.MiddleLeft };
            }
            // Game 視窗放大時（ViewportInfo.Zoom）畫面只看得到中間一塊：對齊看得到的左上角；縮放跟著視窗高度，清單不用捲動
            float zoom = Mathf.Max(1f, ViewportInfo.Zoom), scale = Screen.height / zoom / DesignHeight;
            var origin = new Vector2(Screen.width, Screen.height) * (1f - 1f / zoom) / 2f;
            GUI.matrix = Matrix4x4.TRS(origin, Quaternion.identity, new Vector3(scale, scale, 1f));

            var steps = Parse(s.Get(SimIds.StepsList));
            var courses = Parse(s.Get("app.courses.list"));
            float height = Pad * 2 + (Row + Gap) * (collapsed ? 2 : 6 + courses.Count + steps.Count);
            var color = GUI.color;
            GUI.color = Ui.PanelBg;
            GUI.DrawTexture(new Rect(0, 0, Width, height), Texture2D.whiteTexture);
            GUI.color = color;
            screenRect = new Rect(origin, new Vector2(Width, height) * scale);

            y = Pad;
            bool running = s.GetBool(SimIds.AutoRunActive);
            GUI.Label(Line(Width - Pad * 2 - 70), "後台控制（只有這台電腦看得到）", label);
            y -= Row + Gap;
            if (GUI.Button(new Rect(Width - Pad - 66, y, 66, Row), collapsed ? "展開" : "收合", button)) collapsed = !collapsed;
            y += Row + Gap;
            if (!collapsed) DrawBody(s, courses, steps, running);
            GUI.Label(Line(), running ? s.Get(SimIds.AutoRunText) : s.Get(SimIds.AutoRunStatus), label);
            GUI.matrix = Matrix4x4.identity;
        }

        void DrawBody(ISimState s, List<(string id, string label)> courses, List<(string id, string label)> steps, bool running)
        {
            GUI.enabled = !running;
            GUI.Label(Line(), "課程版本", label);
            foreach (var (id, title) in courses)
            {
                bool ready = id == SimIds.CourseR1;   // 代做腳本目前只有 R1
                GUI.enabled = !running && ready;
                if (Choice(Line(), ready ? title : title + "（尚未提供）", id == s.Get(SimIds.CourseId)) && id != s.Get(SimIds.CourseId)) Ui.Send("app.course", id);
            }
            GUI.enabled = !running;
            GUI.Label(Line(), "模式", label);
            var row = Line();
            row.width = (row.width - Gap) / 2f;
            if (Choice(row, "教學", mode == Tutorial)) mode = Tutorial;
            row.x += row.width + Gap;
            if (Choice(row, "練習（考核）", mode == Practice)) mode = Practice;
            GUI.Label(Line(), "從第幾步開始", label);
            foreach (var (id, title) in steps)
                if (Choice(Line(), title, id == step)) step = id;
            GUI.enabled = !running && steps.Exists(x => x.id == step);
            if (Choice(Line(), "▶ 從這一步開始", GUI.enabled))
            {
                Ui.Send("app.jump", mode + ":" + step);
                collapsed = true;   // 桌面的提示板在左邊，別蓋住
            }
            GUI.enabled = true;
        }

        bool Choice(Rect r, string text, bool on)
        {
            var bg = GUI.backgroundColor;
            GUI.backgroundColor = on ? Ui.ButtonOn : Ui.ButtonBg;
            bool hit = GUI.Button(r, text, button);
            GUI.backgroundColor = bg;
            return hit;
        }

        Rect Line(float width = Width - Pad * 2)
        {
            var r = new Rect(Pad, y, width, Row);
            y += Row + Gap;
            return r;
        }
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
