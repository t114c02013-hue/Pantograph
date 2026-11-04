using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PantographSim.UI
{
    /// <summary>
    /// 畫在 Canvas 上、跟著 3D 物件移動的標示（永遠正向、不被模型擋住）：
    /// ・目前目標：上下跳動的「▼」＋「① 點這裡：名稱」；在畫面外時改成邊緣箭頭。
    /// ・主要場景物件與工具架掛位常駐小標籤（掛位顯示在架／手上／使用中）。
    /// ・數字鍵高亮的掛位：「▼ 在這裡」。（降弓距離只顯示在右側升降控制器，場景不再重複標示）
    /// ・裝新滑板時：「此側朝外」。・滑鼠移到可互動物件上：名稱提示框。
    /// </summary>
    public class MarkerView : View
    {
        static readonly (string id, string name)[] Persistent =
        {
            ("ip.cab.panel", "駕駛室控制盤"), ("ip.cab.pan_indicator", "弓位指示燈"), ("ip.depot.ground_terminal", "平台接地端子"),
            ("ip.pan_head.ground_clamp", "弓頭接地掛點"), ("env.parts_tray", "零件盒"), ("env.work_order", "作業單"),
            ("ip.depot.detector_tester", "驗電器自檢器"), ("env.tool_rack", "工具架"),
            ("part.carbon_strip_L", "靠關節滑板"), ("part.carbon_strip_R", "外側滑板")
            // 備品不常駐標示：滑鼠／控制器指到哪條才顯示那條的詳細資料（見 HoverText）
        };
        static readonly string[] Circled = { "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧", "⑨", "⑩" };
        static readonly Color LabelBg = new Color(0.05f, 0.06f, 0.08f, 0.85f);

        RectTransform layer;
        readonly Dictionary<string, (RectTransform rt, Text text)> labels = new Dictionary<string, (RectTransform, Text)>();
        readonly List<(RectTransform rt, Text arrow, Text text, Image bg)> targetPool = new List<(RectTransform, Text, Text, Image)>();
        (RectTransform rt, Text text) hover, outward;
        string[] targets = new string[0];

        public void OnStep(StepStarted e) { }

        protected override void OnBuild(RectTransform safe)
        {
            layer = Ui.Rect(safe, "Markers");
            Ui.Stretch(layer);
            foreach (var (id, name) in Persistent) labels[id] = MakeLabel(name, 20, LabelBg, Color.white);
            hover = MakeLabel("", 22, new Color(0.1f, 0.35f, 0.55f, 0.95f), Color.white);
            outward = MakeLabel("此側朝外 ➜", 26, new Color(0.9f, 0.5f, 0.05f, 0.95f), Color.white);        }

        (RectTransform, Text) MakeLabel(string text, int size, Color bg, Color fg)
        {
            var img = Ui.Panel(layer, "Label", bg, true, false);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            var h = Ui.HStack(img.gameObject, 0, 8); h.padding = new RectOffset(12, 12, 6, 6);
            var fit = img.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var t = Ui.Text(rt, text, size, fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return (rt, t);
        }

        (RectTransform, Text, Text, Image) MakeTarget()
        {
            var rt = Ui.Rect(layer, "Target");
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(10, 10);
            var arrow = Ui.Text(rt, "▼", 44, new Color(1f, 0.85f, 0.1f), TextAnchor.LowerCenter, FontStyle.Bold);
            arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            arrow.rectTransform.pivot = new Vector2(0.5f, 0f);
            arrow.rectTransform.sizeDelta = new Vector2(80, 50);
            arrow.gameObject.AddComponent<Outline>().effectColor = Color.black;
            var bg = Ui.Panel(rt, "Bg", new Color(0.95f, 0.75f, 0.05f, 0.97f), true, false);
            var brt = bg.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0, 52);
            var h = Ui.HStack(bg.gameObject, 0, 8); h.padding = new RectOffset(14, 14, 6, 6);
            var fit = bg.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var text = Ui.Text(brt, "", 24, Color.black, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return (rt, arrow, text, bg);
        }

        /// <summary>工具袋的格子標籤只有鏡頭在旁邊才顯示：袋子在遠處時投影到畫面會浮在別的東西上。</summary>
        static bool FarBagSlot(ISimState s, string id, Vector3 p) =>
            id.StartsWith("env.tool_rack.slot_") && s.Get(SimIds.BagSlots).Contains("," + id.Substring(id.LastIndexOf('_') + 1) + ",")
            && Camera.main != null && Vector3.Distance(Camera.main.transform.position, p) > 2f;

        static bool Close(Vector2 a, Vector2 b) => Mathf.Abs(a.x - b.x) < 170f && Mathf.Abs(a.y - b.y) < 40f;

        /// <summary>中心在 center、半寬 halfSize 的東西要移多少才不超出 ±limit。</summary>
        public static float EdgeShift(float center, float halfSize, float limit) =>
            Mathf.Clamp(center, -limit + halfSize, Mathf.Max(limit - halfSize, -limit + halfSize)) - center;

        /// <summary>放在 at 的標籤（底部中心對齊）會不會蓋到 q 的標記：標記從 q 往上約 100 px（箭頭加文字）、半寬約 120 px。</summary>
        static bool Covers(Vector2 q, Vector2 at, RectTransform rt) =>
            Mathf.Abs(q.x - at.x) < rt.rect.width * 0.5f + 120f && q.y + 100f > at.y && q.y < at.y + rt.rect.height;

        static bool RackHintActive(ISimState s) =>
            !string.IsNullOrEmpty(s.Get("ui.rack_hint")) && Time.time - s.GetFloat("ui.rack_hint_time") < 4f;

        public override void Refresh(ISimState s)
        {
            var reg = Sim.Registry;
            foreach (int k in SimIds.RackSlotNumbers(s))   // 掛位標籤：掛位清單讀 rack.slots（ToolSlotTable），不寫死 1–9
                if (!labels.ContainsKey("env.tool_rack.slot_" + k)) labels["env.tool_rack.slot_" + k] = MakeLabel("", 17, LabelBg, Color.white);
            bool running = s.GetBool("training.running") && !s.GetBool("procedure.finished");
            Ui.SetActive(layer, running && reg != null);
            if (!running || reg == null) return;

            // ---- 目標（教學模式的小動作目標，加上數字鍵高亮的掛位約 4 秒）----
            targets = Resolve(s.Get("hint.highlight"), reg);
            string rackHint = s.Get("ui.rack_hint");
            bool hintOn = RackHintActive(s);
            if (hintOn && !targets.Contains(rackHint)) targets = targets.Concat(new[] { rackHint }).ToArray();
            while (targetPool.Count < targets.Length) targetPool.Add(MakeTarget());
            float bob = Mathf.Sin(Time.unscaledTime * 5f) * 8f;
            var placed = new List<Vector2>();   // 這一幀「點這裡」標籤的位置：別的標籤要避開
            var half = ui.Safe.rect.size * 0.5f;
            for (int i = 0; i < targetPool.Count; i++)
            {
                var m = targetPool[i];
                bool use = i < targets.Length && reg.TryGetPosition(targets[i], out _);
                Ui.SetActive(m.rt, use);
                if (!use) continue;
                string id = targets[i];
                reg.TryGetPosition(id, out var pos);
                string name = ShortName(reg.NameOf(id));
                string num = Circled[Mathf.Min(i, Circled.Length - 1)];
                bool projected = ui.WorldToSafe(pos, out var local, out bool inView);
                if (projected && inView)
                {
                    // 同一端的兩支螺栓很近，標籤上下錯開
                    float stagger = SimIds.IsBoltId(id) && (id.EndsWith("_1") || id.EndsWith("_3")) ? 64f : 0f;
                    var at = local + new Vector2(0, 18 + stagger);
                    while (placed.Exists(q => Close(q, at))) at.y += 44f;   // 兩個目標很近時往上疊，不互相蓋住
                    placed.Add(at);
                    m.rt.anchoredPosition = at + new Vector2(0, bob);
                    Ui.SetActive(m.arrow, true);
                    m.text.text = hintOn && id == rackHint ? $"▼ 在這裡：{name}"
                        : id == SimIds.LowerCheckId ? (InputMode.XrActive ? $"{num} 用右側控制器把這個點降到綠框" : $"{num} 按住升降面板的 ▼ 把這個點降到綠框")
                        : $"{num} 點這裡：{name}";
                }
                else
                {
                    // 畫面外：貼在邊緣，箭頭指向目標方向
                    var dir = projected ? local : new Vector2(0, -half.y);
                    m.rt.anchoredPosition = new Vector2(Mathf.Clamp(dir.x, -half.x + 140, half.x - 140), Mathf.Clamp(dir.y, -half.y + 40, half.y - 110));
                    Ui.SetActive(m.arrow, false);
                    string arrow = Mathf.Abs(dir.x) > Mathf.Abs(dir.y) ? (dir.x > 0 ? "▶" : "◀") : (dir.y > 0 ? "▲" : "▼");
                    bool inBag = id.StartsWith("env.tool_rack.slot_") && s.Get(SimIds.BagSlots).Contains("," + id.Substring(id.LastIndexOf('_') + 1) + ",");
                    bool cGoesThere = s.Get("training.mode") != "practice" && (id != rackHint || id == s.Get("hint.sub_target"));   // 練習不帶路；數字鍵的掛位不是 C 的目標
                    m.text.text = inBag ? $"{arrow} {name}（點下方工具列的格子）" : cGoesThere ? $"{arrow} {name}（按「帶我過去」或 C）" : $"{arrow} {name}";
                }
                m.bg.color = Color.Lerp(new Color(1f, 0.85f, 0.1f), new Color(1f, 0.6f, 0.05f), Ui.Blink(5f));
                // 箭頭留在目標上，只把字推回畫面內
                var mp = m.rt.anchoredPosition;
                m.bg.rectTransform.anchoredPosition = new Vector2(EdgeShift(mp.x, m.bg.rectTransform.rect.width * 0.5f, half.x), 52f + EdgeShift(mp.y + 52f + m.bg.rectTransform.rect.height * 0.5f, m.bg.rectTransform.rect.height * 0.5f, half.y));
            }

            // ---- 常駐標籤 ----
            var targetSet = new HashSet<string>(targets);
            foreach (var kv in labels)
            {
                bool show = !targetSet.Contains(kv.Key) && reg.TryGetPosition(kv.Key, out var p)
                            && ui.WorldToSafe(p, out var lp, out bool inside) && inside && !placed.Exists(q => Close(q, lp + new Vector2(0, 26)))
                            && !FarBagSlot(s, kv.Key, p);
                Ui.SetActive(kv.Value.rt, show);
                if (!show) continue;
                reg.TryGetPosition(kv.Key, out var p2);
                ui.WorldToSafe(p2, out var lp2, out _);
                kv.Value.rt.anchoredPosition = lp2 + new Vector2(0, 26);
                placed.Add(lp2 + new Vector2(0, 26));
                if (kv.Key.StartsWith("env.tool_rack.slot_"))
                {
                    string st = s.Get("rack." + kv.Key.Substring("env.tool_rack.".Length), "on_rack");
                    kv.Value.text.text = kv.Key.Substring(kv.Key.Length - 1) + " " + ShortName(reg.NameOf(kv.Key)).Replace("工具架：", "")
                        + (st == "on_rack" ? "" : st == "in_hand" ? Ui.Tint("（手上）", Ui.Accent) : Ui.Tint("（使用中）", new Color(0.5f, 0.75f, 1f)));
                }
            }

            // ---- 此側朝外 ----
            string side = s.Get("hint.side");
            bool arrowOn = s.GetBool("hint.show_arrow") && !string.IsNullOrEmpty(side)
                           && reg.TryGetPosition($"ip.carbon_strip_{side}.outward", out var ap) && ui.WorldToSafe(ap, out _, out bool ain) && ain;
            Ui.SetActive(outward.rt, arrowOn);
            if (arrowOn)
            {
                reg.TryGetPosition($"ip.carbon_strip_{side}.outward", out var ap2);
                ui.WorldToSafe(ap2, out var al2, out _);
                var outAt = al2 + new Vector2(0, 30);
                while (placed.Exists(q => Covers(q, outAt, outward.rt))) outAt.y += 44f;
                placed.Add(outAt);
                outward.rt.anchoredPosition = outAt;
                outward.text.text = s.Get($"part.carbon_strip_{side}.new_orientation") == "out" ? "✔ 此側朝外（方向正確）" : "此側朝外 ➜　方向不對，按 R 轉 180°";
            }

            // ---- 指到的物件：名稱（備品顯示詳細資料），貼在物件上方（滑鼠或 VR 控制器射線都適用）----
            string hoverName = s.Get("ui.hover_name"), hoverId = s.Get("ui.hover_id");
            bool hoverOn = !string.IsNullOrEmpty(hoverName) && !PointerState.OverUi;
            Ui.SetActive(hover.rt, hoverOn);
            if (hoverOn)
            {
                hover.text.text = HoverText(s, hoverId, hoverName);
                hover.rt.anchoredPosition = reg.TryGetPosition(hoverId, out var hp) && ui.WorldToSafe(hp, out var hl, out bool hin) && hin
                    ? hl + new Vector2(0, 40)
                    : ui.MouseInSafe() + new Vector2(0, 28);
                var hoverAt = hover.rt.anchoredPosition;   // 不要和「點這裡」疊在一起
                while (placed.Exists(q => Covers(q, hoverAt, hover.rt))) hoverAt.y += 44f;
                hover.rt.anchoredPosition = hoverAt;
            }
        }

        /// <summary>指到物件時的資訊（與 VR 資訊卡相同內容，見 InfoCardText）。</summary>
        static string HoverText(ISimState s, string id, string name) =>
            string.IsNullOrEmpty(id) ? name : InfoCardText.Build(s, Sim.Registry, id);

        static string ShortName(string name)
        {
            int i = name.IndexOf('（');
            return i > 0 && name.EndsWith("）") && (name.Contains("點開") || name.Contains("點一下")) ? name.Substring(0, i) : name;
        }

        static string[] Resolve(string ids, SceneRegistry reg)
        {
            var result = new List<string>();
            foreach (var raw in (ids ?? "").Split(','))
            {
                var id = raw.Trim();
                if (id.Length == 0) continue;
                if (id.EndsWith("*")) result.AddRange(reg.Ids.Where(x => x.StartsWith(id.TrimEnd('*'))).OrderBy(x => x));
                else result.Add(id);
            }
            return result.Distinct().ToArray();
        }
    }
}
