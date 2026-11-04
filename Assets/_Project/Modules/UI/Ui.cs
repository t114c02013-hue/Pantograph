using System;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PantographSim.UI
{
    /// <summary>uGUI 元件的建立工具與共用配色、字型（介面全部由程式建立，可重跑、好維護）。</summary>
    public static class Ui
    {
        public const string FontResource = "NotoSansTC";
        static Font font;
        /// <summary>內建中文字型 Noto Sans TC（Content/Fonts/Resources，SIL OFL）；不依賴電腦上裝了哪些字型。</summary>
        public static Font Font => font != null ? font : font = LoadFont();

        static Font LoadFont()
        {
            var f = Resources.Load<Font>(FontResource);
            return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        public static readonly Color PanelBg = new Color(0.07f, 0.08f, 0.1f, 0.92f);
        public static readonly Color PanelBorder = new Color(1f, 0.75f, 0.2f, 0.9f);
        public static readonly Color Accent = new Color(1f, 0.72f, 0.15f);
        public static readonly Color Ok = new Color(0.4f, 0.92f, 0.5f);
        public static readonly Color Bad = new Color(1f, 0.38f, 0.33f);
        public static readonly Color Muted = new Color(0.72f, 0.77f, 0.83f);
        public static readonly Color ButtonBg = new Color(0.2f, 0.24f, 0.3f, 1f);
        public static readonly Color ButtonOn = new Color(0.9f, 0.55f, 0.1f, 1f);

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>設定錨點：anchor 為 0–1 位置（例 (0,1) 左上），pivot 同，pos 為相對錨點的偏移（px）。</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color? color = null, bool border = true, bool raycast = true)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color ?? PanelBg;
            img.raycastTarget = raycast;
            if (border)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = PanelBorder; o.effectDistance = new Vector2(2, -2);
            }
            return img;
        }

        public static Text Text(Transform parent, string text, int size, Color? color = null, TextAnchor anchor = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font; t.fontSize = size; t.text = text; t.color = color ?? Color.white;
            t.alignment = anchor; t.fontStyle = style; t.supportRichText = true; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.15f;
            return t;
        }

        public static Button Button(Transform parent, string label, int size, Action onClick, Color? bg = null)
        {
            var img = Panel(parent, "Button_" + label, bg ?? ButtonBg, false);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors; colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f); colors.pressedColor = new Color(0.8f, 0.8f, 0.8f); b.colors = colors;
            var t = Text(img.transform, label, size, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(t.rectTransform, 6, 4, 6, 4);
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }

        public static Text LabelOf(Button b) => b.GetComponentInChildren<Text>();

        public static InputField Input(Transform parent, int size, string placeholder = "")
        {
            var img = Panel(parent, "Input", new Color(0.95f, 0.95f, 0.95f), false);
            var field = img.gameObject.AddComponent<InputField>();
            var t = Text(img.transform, "", size, Color.black, TextAnchor.MiddleLeft);
            t.supportRichText = false;
            Stretch(t.rectTransform, 8, 2, 8, 2);
            var ph = Text(img.transform, placeholder, size - 2, new Color(0.4f, 0.4f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(ph.rectTransform, 8, 2, 8, 2);
            field.textComponent = t; field.placeholder = ph;
            return field;
        }

        /// <summary>垂直排列並依內容自動調整高度。</summary>
        public static VerticalLayoutGroup VStack(GameObject go, int padding = 16, float spacing = 8, bool fitHeight = true)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.spacing = spacing; v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            if (fitHeight) go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return v;
        }

        public static HorizontalLayoutGroup HStack(GameObject go, float spacing = 8, int padding = 0)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.padding = new RectOffset(padding, padding, padding, padding);
            h.childControlHeight = true; h.childControlWidth = true; h.childForceExpandHeight = false; h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            return h;
        }

        public static LayoutElement Size(Component c, float width = -1, float height = -1, float flexW = -1)
        {
            var le = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (width >= 0) le.preferredWidth = width;
            if (height >= 0) le.preferredHeight = height;
            if (flexW >= 0) le.flexibleWidth = flexW;
            return le;
        }

        public static string Tint(string text, Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{text}</color>";

        /// <summary>
        /// 送出介面操作（徒手）。按鈕按下的是「介面功能」（重設工具、上一步、重做本步、作業單）時順便發 UiCommandIssued。
        /// 重播中只放行重播、紀錄、錄影、選單與關閉錯誤視窗的指令（看重播時按任何東西都不會改變場景）。
        /// </summary>
        public static void Send(string targetId, string text, string pointId = "")
        {
            if (SimSession.IsReplay && !AllowedInReplay(targetId, text)) return;
            if (targetId == "tool.belt" && text == "reset") UiCommands.Issue(UiCommands.ResetTool, "ui.button");
            else if (targetId == "procedure.sub_back") UiCommands.Issue(UiCommands.Undo, "ui.button");
            else if (targetId == "ui.error" && text == "restart") UiCommands.Issue(UiCommands.RestartStep, "ui.button");
            else if (targetId == SimIds.WorkOrder && text == "toggle") UiCommands.Issue(UiCommands.WorkOrderToggle, "ui.button");
            Sim.Bus?.Publish(new ActionRequested { targetId = targetId, text = text, pointId = pointId ?? "", toolId = "", time = SimClock.Now });
        }

        static bool AllowedInReplay(string targetId, string text) =>
            targetId == "replay" || targetId == "rec" || targetId == "rec.video" || targetId.StartsWith("app.") || targetId == "xr.menu"
            || targetId == "ui.error" || targetId == "ui.tutorial";

        /// <summary>介面功能（帶我過去、說明、提示板…）被按下：發 UiCommandIssued（紀錄與報表的「提示使用」）。</summary>
        public static void Command(string name, string source = "ui.button") => UiCommands.Issue(name, source);

        /// <summary>錄影中顯示的「● REC mm:ss」；沒在錄影回傳空字串。</summary>
        public static string RecText(ISimState s)
        {
            if (s == null || !s.GetBool("rec.video.active")) return "";
            int sec = Mathf.Max(0, (int)(Time.realtimeSinceStartup - s.GetFloat("rec.video.started")));
            return $"● REC {sec / 60:00}:{sec % 60:00}";
        }

        public static float Blink(float speed = 5f) => 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed);

        public static void SetActive(Component c, bool on) { if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on); }

        public static string ToolName(string toolId)
        {
            foreach (var entry in (Sim.State?.Get("tools.list") ?? "").Split(';'))
            {
                var f = entry.Split('|');
                if (f.Length >= 3 && f[1] == toolId) return f[2];
            }
            return string.IsNullOrEmpty(toolId) ? "徒手" : toolId;
        }

        /// <summary>工具平常放在哪裡：工具架、桿架或工具袋。</summary>
        public static string ToolPlace(string toolId)
        {
            foreach (var entry in (Sim.State?.Get(SimIds.ToolPlaces) ?? "").Split(';'))
            {
                var f = entry.Split('|');
                if (f.Length == 2 && f[0] == toolId) return f[1];
            }
            return "工具架";
        }

        public static string ToolKey(string toolId)
        {
            foreach (var entry in (Sim.State?.Get("tools.list") ?? "").Split(';'))
            {
                var f = entry.Split('|');
                if (f.Length >= 3 && f[1] == toolId) return f[0];
            }
            return "0";
        }
    }
}
