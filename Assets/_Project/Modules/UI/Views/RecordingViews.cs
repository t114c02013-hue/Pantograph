using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PantographSim.UI
{
    // 紀錄、重播、錄影相關的介面。只讀黑板 rec.*／replay.*，送 ActionRequested（目標 rec／replay／rec.video），不引用紀錄模組。

    /// <summary>可捲動的清單（RectMask2D＋ScrollRect，內容由 VStack 排列）。</summary>
    public static class Scroll
    {
        public static RectTransform Create(Transform parent, string name, float height, out RectTransform content)
        {
            var bg = Ui.Panel(parent, name, new Color(0.04f, 0.05f, 0.07f, 0.6f), false, true);
            Ui.Size(bg, -1, height);
            var view = Ui.Rect(bg.transform, "Viewport");
            Ui.Stretch(view, 4, 4, 4, 4);
            view.gameObject.AddComponent<RectMask2D>();
            var img = view.gameObject.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0.01f);
            content = Ui.Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            Ui.VStack(content.gameObject, 4, 4);
            var sr = bg.gameObject.AddComponent<ScrollRect>();
            sr.viewport = view; sr.content = content; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30f;
            return bg.rectTransform;
        }

        public static void Clear(RectTransform content) { foreach (Transform c in content) Object.Destroy(c.gameObject); }
    }

    /// <summary>時間軸：點或拖曳＝跳到該時間（送 replay seek_ratio:0–1）。桌面滑鼠與 VR 射線都可以。</summary>
    public class TimelineScrub : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        float lastSent = -1f, lastTime;

        public void OnPointerDown(PointerEventData e) => Send(e, true);
        public void OnDrag(PointerEventData e) => Send(e, false);

        void Send(PointerEventData e, bool force)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local)) return;
            float ratio = Mathf.Clamp01((local.x - rt.rect.xMin) / Mathf.Max(1f, rt.rect.width));
            if (!force && (Mathf.Abs(ratio - lastSent) < 0.002f || Time.unscaledTime - lastTime < 0.05f)) return;
            lastSent = ratio; lastTime = Time.unscaledTime;
            Ui.Send("replay", "seek_ratio:" + ratio.ToString("0.####", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// 重播控制：播放／暫停、0.5×／1×／2×／4×、時間軸（錯誤標記：Critical 紅、Major 橘、Minor 黃；點標記跳到該錯誤前 5 s）、
    /// 學員視角開關、結束重播；右側錯誤清單與步驟清單（點一筆就跳過去）。
    /// </summary>
    public class ReplayView : View
    {
        public const float BarWidth = 1200f, TrackWidth = 1140f;
        RectTransform bar, lists, track, fill, markerRoot, errorContent, stepContent;
        Text title, clock, playLabel, viewLabel, errorHeader;
        readonly List<(float speed, Image bg)> speedButtons = new List<(float, Image)>();
        string builtMarkers = null, builtSteps = null;

        public static Color SeverityColor(string sev) =>
            sev == "Critical" ? new Color(0.9f, 0.15f, 0.12f) : sev == "Major" ? new Color(1f, 0.55f, 0.1f) : new Color(0.95f, 0.85f, 0.15f);

        protected override void OnBuild(RectTransform safe)
        {
            var bg = Ui.Panel(safe, "ReplayBar", new Color(0.05f, 0.07f, 0.1f, 0.95f));
            bar = Ui.Place(bg.rectTransform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(BarWidth, 0));
            Ui.VStack(bg.gameObject, 14, 8);
            var top = Ui.Rect(bar, "Top"); Ui.HStack(top.gameObject, 10);
            title = Ui.Text(top, "", 22, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold); Ui.Size(title, 470, 50);
            clock = Ui.Text(top, "", 24, Ui.Accent, TextAnchor.MiddleLeft, FontStyle.Bold); Ui.Size(clock, 170, 50);
            var play = Ui.Button(top, "", 24, () => Ui.Send("replay", "toggle"), Ui.ButtonOn); Ui.Size(play, 90, 50); playLabel = Ui.LabelOf(play);
            foreach (var sp in new[] { 0.5f, 1f, 2f, 4f })
            {
                float v = sp;
                var b = Ui.Button(top, sp.ToString("0.#", CultureInfo.InvariantCulture) + "×", 20, () => Ui.Send("replay", "speed:" + v.ToString(CultureInfo.InvariantCulture)));
                Ui.Size(b, 64, 50);
                speedButtons.Add((sp, b.GetComponent<Image>()));
            }
            var view = Ui.Button(top, "", 19, () => Ui.Send("replay", Sim.State.GetBool("replay.trainee_view") ? "view:free" : "view:trainee"), new Color(0.15f, 0.45f, 0.75f));
            Ui.Size(view, 130, 50); viewLabel = Ui.LabelOf(view);
            var exit = Ui.Button(top, "結束重播", 19, () => Ui.Send("replay", "exit"), new Color(0.55f, 0.15f, 0.12f)); Ui.Size(exit, 110, 50);

            // 時間軸
            var trackBg = Ui.Panel(bar, "Track", new Color(0.18f, 0.2f, 0.25f, 1f), false, true);
            track = trackBg.rectTransform;
            Ui.Size(trackBg, TrackWidth, 44);
            trackBg.gameObject.AddComponent<TimelineScrub>();
            var f = Ui.Panel(track, "Fill", new Color(0.3f, 0.6f, 0.9f, 0.55f), false, false);
            fill = f.rectTransform; fill.anchorMin = new Vector2(0, 0); fill.anchorMax = new Vector2(0, 1); fill.pivot = new Vector2(0, 0.5f); fill.anchoredPosition = Vector2.zero;
            markerRoot = Ui.Rect(track, "Markers"); Ui.Stretch(markerRoot);
            Ui.Text(bar, "時間軸：點或拖曳跳轉；彩色標記＝錯誤（紅 嚴重、橘 錯誤、黃 提醒），點標記跳到錯誤前 5 秒。", 17, Ui.Muted);

            // 右側：錯誤清單與步驟清單
            var lb = Ui.Panel(safe, "ReplayLists", new Color(0.05f, 0.07f, 0.1f, 0.93f));
            lists = Ui.Place(lb.rectTransform, new Vector2(1f, 0.5f), new Vector2(0, -40), new Vector2(400, 0));
            Ui.VStack(lb.gameObject, 12, 6);
            errorHeader = Ui.Text(lists, "錯誤", 21, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            Scroll.Create(lists, "Errors", 250, out errorContent);
            Ui.Text(lists, "步驟（點一下跳到開始）", 21, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            Scroll.Create(lists, "Steps", 300, out stepContent);
        }

        public override void Refresh(ISimState s)
        {
            bool show = s.GetBool("replay.active");
            Ui.SetActive(bar, show);
            Ui.SetActive(lists, show);
            if (!show) return;
            float t = s.GetFloat("replay.time"), dur = Mathf.Max(0.01f, s.GetFloat("replay.duration"));
            title.text = "重播　" + s.Get("replay.title");
            clock.text = $"{Clock(t)} / {Clock(dur)}";
            playLabel.text = s.GetBool("replay.playing") ? "Ⅱ" : "▶";
            float speed = s.GetFloat("replay.speed", 1f);
            foreach (var (sp, bg) in speedButtons) bg.color = Mathf.Abs(sp - speed) < 0.01f ? Ui.ButtonOn : Ui.ButtonBg;
            viewLabel.text = s.GetBool("replay.trainee_view") ? "自由視角" : "學員視角";
            fill.sizeDelta = new Vector2(TrackWidth * Mathf.Clamp01(t / dur), 0);
            string markers = s.Get("replay.markers");
            if (markers != builtMarkers) { BuildMarkers(markers, dur); builtMarkers = markers; }
            string steps = s.Get("replay.steps");
            if (steps != builtSteps) { BuildSteps(steps); builtSteps = steps; }
        }

        public static string Clock(float sec) { int x = Mathf.Max(0, (int)sec); return $"{x / 60:00}:{x % 60:00}"; }

        void BuildMarkers(string data, float dur)
        {
            foreach (Transform c in markerRoot) Object.Destroy(c.gameObject);
            Scroll.Clear(errorContent);
            var rows = data.Split('\n').Where(x => x.Length > 0).Select(x => x.Split('|')).ToList();
            errorHeader.text = $"錯誤（{rows.Count}）";
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Length < 4) continue;
                float.TryParse(r[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float et);
                int idx = i;
                var m = Ui.Button(markerRoot, "", 10, () => Ui.Send("replay", "error:" + idx), SeverityColor(r[1]));
                var rt = (RectTransform)m.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(Mathf.Clamp01(et / dur), 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(12, 44);
                var item = Ui.Button(errorContent, $"{Clock(et)}　{Ui.Tint("■", SeverityColor(r[1]))} {r[2]}\n<size=15>{r[3]}</size>", 17,
                    () => Ui.Send("replay", "error:" + idx));
                Ui.LabelOf(item).alignment = TextAnchor.MiddleLeft;
                Ui.LabelOf(item).fontStyle = FontStyle.Normal;
                Ui.Size(item, -1, 58);
            }
            if (rows.Count == 0) Ui.Text(errorContent, "這一場沒有錯誤。", 18, Ui.Muted);
        }

        void BuildSteps(string data)
        {
            Scroll.Clear(stepContent);
            var rows = data.Split('\n').Where(x => x.Length > 0).Select(x => x.Split('|')).ToList();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                float.TryParse(r[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float st);
                int idx = i;
                var item = Ui.Button(stepContent, $"{Clock(st)}　{(r.Length > 1 ? r[1] : "")}", 17, () => Ui.Send("replay", "step:" + idx));
                Ui.LabelOf(item).alignment = TextAnchor.MiddleLeft;
                Ui.LabelOf(item).fontStyle = FontStyle.Normal;
                Ui.Size(item, -1, 40);
            }
        }
    }

    /// <summary>
    /// 紀錄清單（任務卡「紀錄清單」或結算畫面開啟）：由新到舊，顯示學員、日期、模式、分數、是否完成與「未完成（中斷）」標記；
    /// 每筆有「重播」「報表」「開啟資料夾」；勾選兩筆以上並排比較分數、錯誤數、總時間。
    /// </summary>
    public class RecordsListView : View
    {
        RectTransform dim, content;
        Text compare, message, header;
        string builtFor = null;
        readonly HashSet<string> selected = new HashSet<string>();
        readonly Dictionary<string, Text> checkLabels = new Dictionary<string, Text>();
        List<string[]> rows = new List<string[]>();

        protected override void OnBuild(RectTransform safe)
        {
            var (d, card) = Modal.Create(safe, "RecordsList", new Vector2(1500, 900), new Color(0.3f, 0.7f, 1f));
            dim = d;
            Ui.VStack(card.gameObject, 24, 8, false);
            var top = Ui.Rect(card, "Top"); Ui.HStack(top.gameObject, 12);
            header = Ui.Text(top, "紀錄清單", 32, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold); Ui.Size(header, 1040, 56);
            var refresh = Ui.Button(top, "重新整理", 20, () => Ui.Send("rec", "list.refresh")); Ui.Size(refresh, 140, 50);
            var close = Ui.Button(top, "關閉", 22, () => Ui.Send("rec", "list.close"), Ui.ButtonOn); Ui.Size(close, 140, 50);
            Ui.Text(card, "　　　學員　　　　　　日期　　　　　　模式／輸入　　分數　　狀態　　　　　　　時間　　　　　　　　　　　　操作", 18, Ui.Muted);
            Scroll.Create(card, "Rows", 520, out content);
            var row = Ui.Rect(card, "Compare"); Ui.HStack(row.gameObject, 12);
            var cmp = Ui.Button(row, "比較勾選的紀錄", 20, UpdateCompare, new Color(0.15f, 0.45f, 0.75f)); Ui.Size(cmp, 220, 48);
            var clear = Ui.Button(row, "清除勾選", 20, () => { selected.Clear(); RefreshChecks(); compare.text = ""; }); Ui.Size(clear, 140, 48);
            message = Ui.Text(row, "", 17, Ui.Muted, TextAnchor.MiddleLeft); Ui.Size(message, 1000, 48);
            compare = Ui.Text(card, "", 19, Color.white);
        }

        public override void Refresh(ISimState s)
        {
            bool show = s.GetBool("rec.list_open");
            Ui.SetActive(dim, show);
            if (!show) return;
            string list = s.Get("rec.list");
            if (list != builtFor) { Rebuild(list); builtFor = list; }
            header.text = $"紀錄清單　<size=20>（{rows.Count} 筆，存在 {s.Get("rec.root")}）</size>";
            message.text = s.Get("rec.message");
        }

        void Rebuild(string list)
        {
            Scroll.Clear(content);
            checkLabels.Clear();
            rows = list.Split('\n').Where(x => x.Length > 0).Select(x => x.Split('\u001f')).Where(f => f.Length >= 13).ToList();
            selected.RemoveWhere(f => rows.All(r => r[0] != f));
            if (rows.Count == 0) Ui.Text(content, "還沒有紀錄。任務卡按「開始」就會自動開始記錄。", 20, Ui.Muted);
            foreach (var f in rows)
            {
                string folder = f[0];
                var line = Ui.Panel(content, "Row", new Color(0.12f, 0.15f, 0.2f, 0.9f), false, true);
                Ui.Size(line, -1, 56);
                var h = Ui.HStack(line.gameObject, 8, 4);
                var check = Ui.Button(line.transform, "□", 24, () => { if (!selected.Remove(folder)) selected.Add(folder); RefreshChecks(); }); Ui.Size(check, 50, 46);
                checkLabels[folder] = Ui.LabelOf(check);
                Cell(line.transform, f[1], 200, Color.white, FontStyle.Bold);
                Cell(line.transform, f[2], 190, Ui.Muted);
                Cell(line.transform, f[3] + "／" + f[4], 130, Ui.Muted);
                string score = f[5].Length > 0 ? f[5] + " 分" + (f[6] == "1" ? " " + Ui.Tint("通過", Ui.Ok) : f[6] == "0" ? " " + Ui.Tint("未通過", Ui.Bad) : "") : "—";
                Cell(line.transform, score, 150, Color.white);
                Cell(line.transform, f[8] == "1" ? Ui.Tint(f[7], Ui.Bad) : f[7], 190, f[7] == "完成" ? Ui.Ok : Ui.Accent);
                int.TryParse(f[11], out int sec);
                Cell(line.transform, $"{sec / 60} 分 {sec % 60} 秒" + (f[12] != "0" ? "　有影片" : ""), 140, Ui.Muted);
                var rp = Ui.Button(line.transform, "重播", 19, () => Ui.Send("rec", "replay:" + folder), Ui.ButtonOn); Ui.Size(rp, 90, 46);
                var rep = Ui.Button(line.transform, "報表", 19, () => Ui.Send("rec", "report:" + folder)); Ui.Size(rep, 90, 46);
                var op = Ui.Button(line.transform, "開啟資料夾", 19, () => Ui.Send("rec", "open:" + folder)); Ui.Size(op, 130, 46);
            }
            RefreshChecks();
        }

        static void Cell(Transform parent, string text, float width, Color color, FontStyle style = FontStyle.Normal)
        {
            var t = Ui.Text(parent, text, 19, color, TextAnchor.MiddleLeft, style);
            Ui.Size(t, width, 46);
        }

        void RefreshChecks()
        {
            foreach (var kv in checkLabels) kv.Value.text = selected.Contains(kv.Key) ? "■" : "□";
        }

        /// <summary>並排比較勾選的紀錄（分數、錯誤數、嚴重、總時間；數字取自各筆的結算）。</summary>
        void UpdateCompare()
        {
            var pick = rows.Where(r => selected.Contains(r[0])).ToList();
            if (pick.Count < 2) { compare.text = Ui.Tint("請勾選兩筆以上再比較。", Ui.Accent); return; }
            var sb = new System.Text.StringBuilder("<b>比較</b>\n");
            foreach (var r in pick)
            {
                int.TryParse(r[11], out int sec);
                sb.Append($"{r[1]}（{r[2]}，{r[3]}）　分數 {(r[5].Length > 0 ? r[5] : "—")}　錯誤 {(r[10].Length > 0 ? r[10] : "—")}　嚴重 {(r[9].Length > 0 ? r[9] : "—")}　總時間 {sec / 60} 分 {sec % 60} 秒　{r[7]}\n");
            }
            compare.text = sb.ToString();
        }
    }

    /// <summary>影片停止後顯示存檔路徑與「開啟資料夾」（約 10 秒）。</summary>
    public class VideoNoticeView : View
    {
        RectTransform root;
        Text text;

        protected override void OnBuild(RectTransform safe)
        {
            var img = Ui.Panel(safe, "VideoNotice", new Color(0.08f, 0.2f, 0.12f, 0.95f));
            root = Ui.Place(img.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -70), new Vector2(980, 0));
            var h = Ui.HStack(img.gameObject, 12, 12);
            text = Ui.Text(root, "", 19, Color.white, TextAnchor.MiddleLeft); Ui.Size(text, 760, 60);
            var open = Ui.Button(root, "開啟資料夾", 20, () => Ui.Send("rec.video", "open"), Ui.ButtonOn); Ui.Size(open, 160, 52);
        }

        public override void Refresh(ISimState s)
        {
            string last = s.Get("rec.video.last");
            bool show = last.Length > 0 && Time.realtimeSinceStartup - s.GetFloat("rec.video.stopped", -100f) < 10f;
            Ui.SetActive(root, show);
            if (show) text.text = "影片已存檔：\n<size=16>" + last + "</size>";
        }
    }
}
