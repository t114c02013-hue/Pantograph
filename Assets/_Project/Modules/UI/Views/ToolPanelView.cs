using System;
using System.Collections.Generic;
using System.Globalization;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PantographSim.UI
{
    /// <summary>
    /// 目前工具的專用面板（底部工具列正上方、置中，最大寬 820）：
    /// 卡尺大字讀數＋歸零狀態；扭力扳手設定值、即時扭力條、23–27 合格區、到值「喀！到達設定值」；
    /// 套筒尺寸、方向、累計角度；驗電器燈號；接地棒狀態與拉線確認；清潔布清除率；標記筆說明。
    /// 可視寬度不夠（Game 視窗放大）時改成精簡版：只留讀數、設定值、合格區間與按鈕（標題與說明文字收起）；
    /// 每幀量一次位置，若壓到左上步驟面板或右側升降控制器，就往旁邊挪、必要時縮窄，三者不重疊。
    /// </summary>
    public class ToolPanelView : View
    {
        const float FullWidth = 820f, CompactWidth = 700f, MinWidth = 480f, Bottom = 116f, Gap = 16f;
        /// <summary>安全區寬度小於這個值就用精簡版（1x 約 1872，1.5x 約 1232）。</summary>
        const float CompactBelowWidth = 1500f;

        RectTransform root, safeArea;
        readonly Dictionary<string, RectTransform> panels = new Dictionary<string, RectTransform>();
        readonly List<Action<ISimState>> refreshers = new List<Action<ISimState>>();
        readonly List<Component> fullOnly = new List<Component>();   // 精簡版收起的標題與說明
        Text message;
        bool compact;

        public bool Compact => compact;

        /// <summary>只在完整版顯示的說明文字。</summary>
        Text Help(Transform parent, string text, int size = 20)
        {
            var t = Ui.Text(parent, text, size, Ui.Muted);
            fullOnly.Add(t);
            return t;
        }

        protected override void OnBuild(RectTransform safe)
        {
            safeArea = safe;
            var img = Ui.Panel(safe, "ToolPanel");
            root = Ui.Place(img.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, Bottom), new Vector2(FullWidth, 0));
            Ui.VStack(img.gameObject, 16, 8);
            BuildVoltage(); BuildGrounding(); BuildLead(); BuildMegger(); BuildLockout(); BuildCaliper(); BuildSocket(); BuildTorque(); BuildCloth(); BuildSimple(SimIds.ToolMarker, "標記筆", "依 1→2→3→4 點已鎖到扭力的螺栓，畫上跨螺栓頭與托架的紅色標記線。");
            BuildSimple(SimIds.ToolHarness, "安全帶", "本切片已預先完成安全帶改扣。");
            message = Ui.Text(root, "", 21, Ui.Accent);
            var resetRow = Ui.Rect(root, "ResetRow"); var rh = Ui.HStack(resetRow.gameObject, 10); rh.childAlignment = TextAnchor.MiddleRight;
            fullOnly.Add(Ui.Text(resetRow, "做錯想重來：", 19, Ui.Muted));
            var reset = Ui.Button(resetRow, "↺ 重設工具（Backspace）", 20, () => Ui.Send("tool.belt", "reset"), new Color(0.45f, 0.3f, 0.6f));
            Ui.Size(reset, 280, 46);
            var putBack = Ui.Button(resetRow, "放回工具架", 20, () => Ui.Send("tool.belt", "put_away"));
            Ui.Size(putBack, 150, 46);
        }

        RectTransform Panel(string toolId, string title)
        {
            var rt = Ui.Rect(root, toolId);
            Ui.VStack(rt.gameObject, 0, 6);
            fullOnly.Add(Ui.Text(rt, title, 26, Color.white, TextAnchor.UpperLeft, FontStyle.Bold));
            panels[toolId] = rt;
            return rt;
        }

        static RectTransform Row(Transform parent)
        {
            var r = Ui.Rect(parent, "Row");
            Ui.HStack(r.gameObject, 10);
            return r;
        }

        static Button Btn(Transform parent, string label, Action click, float w = 120) { var b = Ui.Button(parent, label, 20, click); Ui.Size(b, w, 48); return b; }

        void BuildVoltage()
        {
            var p = Panel(SimIds.ToolVoltageDetector, "驗電器");
            var row = Row(p);
            var led = Ui.Panel(row, "LED", Color.gray, true, false); Ui.Size(led, 40, 40);
            var txt = Ui.Text(row, "", 26); Ui.Size(txt, 600);
            Help(p, "先點工具台上的「驗電器自檢器」（綠燈＋嗶聲），再點弓頭接地掛點。");
            refreshers.Add(s =>
            {
                bool green = s.Get("tool.voltage_detector.led") == "green";
                led.color = green ? Ui.Ok : new Color(0.35f, 0.1f, 0.1f);
                txt.text = (green ? "自檢正常　" : "尚未自檢　") + "顯示：" + Ui.Tint(s.Get("tool.voltage_detector.display", "待自檢"), Color.white);
            });
        }

        void BuildGrounding()
        {
            var p = Panel(SimIds.ToolGroundingRod, "接地棒");
            var status = Ui.Text(p, "", 23);
            var row = Row(p);
            var pull = Btn(row, "拉線確認", () => Ui.Send(SimIds.ToolGroundingRod, "pulltest"), 160);
            Help(row, "掛接：端子 → 拉線 → 弓頭　拆除：弓頭 → 端子");
            refreshers.Add(s =>
            {
                string F(bool on, string t) => on ? Ui.Tint("● " + t, Ui.Ok) : Ui.Tint("○ " + t, Ui.Muted);
                status.text = $"{F(s.GetBool(SimIds.TerminalClamped), "平台端子已夾")}　{F(s.GetBool(SimIds.PullTested), "拉線已確認")}　{F(s.GetBool(SimIds.HeadClamped), "弓頭已掛")}"
                    + (s.Has("ui.discharge_countdown") ? $"\n放電倒數 {s.GetFloat("ui.discharge_countdown"):0.0} 秒" : "");   // D43：只有教學模式會寫倒數
                pull.interactable = s.GetBool(SimIds.TerminalClamped) && !s.GetBool(SimIds.PullTested);
            });
        }

        /// <summary>R1.5b b4：高阻計（RT_02）：兩條測試線接上後按測試鍵（DC 1000 V），讀值抄到作業單。</summary>
        /// <summary>v3.10（SI_02 c／c2、d／d2）：上鎖掛牌組——先選「鎖」點開關上鎖，再選「牌」點同一個開關掛牌。</summary>
        void BuildLockout()
        {
            var p = Panel(SimIds.ToolLockout, "上鎖掛牌組");
            var row = Row(p);
            Ui.Text(row, "手上拿", 22, Ui.Muted);
            var lockB = Btn(row, "鎖", () => Ui.Send(SimIds.ToolLockout, "part:lock"), 80);
            var tagB = Btn(row, "牌", () => Ui.Send(SimIds.ToolLockout, "part:tag"), 80);
            Help(p, "先上鎖、再掛牌：選「鎖」點主斷路器或升弓開關上鎖，再選「牌」點同一個開關掛牌。已上鎖時選「鎖」再點一次＝解鎖（牌一起取下）。");
            refreshers.Add(s =>
            {
                bool tag = s.Get("tool.lockout_tag.part", "lock") == "tag";
                lockB.GetComponent<Image>().color = tag ? Ui.ButtonBg : Ui.ButtonOn;
                tagB.GetComponent<Image>().color = tag ? Ui.ButtonOn : Ui.ButtonBg;
            });
        }

        void BuildMegger()
        {
            const string id = "tool.megger";
            var p = Panel(id, "高阻計");
            var status = Ui.Text(p, "", 23);
            var row = Row(p);
            var test = Btn(row, "測試鍵（DC 1000 V）", () => Ui.Send(id, "test"), 240);
            Help(row, "測試線：弓頭接地掛點、車體接地點　測完要用接地桿放電 5 秒才能空手拆線");
            refreshers.Add(s =>
            {
                int n = (int)s.GetFloat("tool.megger.connected_count");
                status.text = $"測試線 {n}／2　" + (s.Has("tool.megger.reading") ? $"讀值 {Ui.Tint(s.GetFloat("tool.megger.reading").ToString("0") + " MΩ", Color.white)}" : "尚未測試");
                test.interactable = n >= 2;
            });
        }

        /// <summary>R1.5b b4：攜帶式短接地線（#2）：拉線確認、掛回工具環。</summary>
        void BuildLead()
        {
            var p = Panel(BodyRings.ToolGroundingLead, "攜帶式短接地線");
            var status = Ui.Text(p, "", 23);
            var row = Row(p);
            var pull = Btn(row, "拉線確認", () => Ui.Send(BodyRings.ToolGroundingLead, "pulltest"), 160);
            Btn(row, "掛回安全帶工具環", () => Ui.Send(BodyRings.ToolGroundingLead, "ring"), 220);
            Help(row, "掛接：車體接地座 → 電纜端子 → 拉線　拆除：電纜端子 → 車體接地座");
            refreshers.Add(s =>
            {
                string F(bool on, string t) => on ? Ui.Tint("● " + t, Ui.Ok) : Ui.Tint("○ " + t, Ui.Muted);
                bool cable = s.GetBool("ip.roof.hv_cable_ground.clamped");
                status.text = $"{F(s.GetBool("ip.roof.carbody_ground.clamped"), "車體接地座已夾")}　{F(cable, "電纜端子已掛")}　{F(s.GetBool("ip.roof.hv_cable_ground.pull_tested"), "拉線已確認")}";
                pull.interactable = cable && !s.GetBool("ip.roof.hv_cable_ground.pull_tested");
            });
        }

        void BuildCaliper()
        {
            var p = Panel(SimIds.ToolCaliper, "游標卡尺");
            var row = Row(p);
            var screen = Ui.Panel(row, "Screen", new Color(0.05f, 0.12f, 0.08f), true, false);
            Ui.Size(screen, 260, 84);
            var digits = Ui.Text(screen.transform, "", 50, new Color(0.55f, 1f, 0.65f), TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(digits.rectTransform);
            var col = Ui.Rect(row, "Col"); Ui.VStack(col.gameObject, 0, 6, false); Ui.Size(col, 390, 90);
            var zero = Ui.Text(col, "", 26, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            var btns = Row(col);
            Btn(btns, "合攏量爪", () => Ui.Send(SimIds.ToolCaliper, "close"), 130);
            Btn(btns, "ZERO", () => Ui.Send(SimIds.ToolCaliper, "zero"), 100);
            Btn(btns, "張開", () => Ui.Send(SimIds.ToolCaliper, "open"), 90);
            Help(p, "滾輪開合量爪；點滑板上的藍色量測點會自動吸附讀數。讀數要自己抄到作業單。");
            refreshers.Add(s =>
            {
                digits.text = s.Get("tool.caliper.display", "--.-- mm");
                zero.text = s.GetBool("tool.caliper.zeroed") ? Ui.Tint("● 已歸零", Ui.Ok) : Ui.Tint("● 未歸零（多 0.30 mm）", Ui.Bad);
            });
        }

        void BuildSocket()
        {
            var p = Panel(SimIds.ToolSocket, "套筒扳手");
            var row = Row(p);
            Ui.Text(row, "套筒", 22, Ui.Muted);
            var sizes = new List<(string, Button)>();
            foreach (var sz in new[] { "10", "13", "17" }) sizes.Add((sz, Btn(row, sz + " mm", () => Ui.Send(SimIds.ToolSocket, "size:" + sz), 100)));
            Ui.Text(row, "　方向", 22, Ui.Muted);
            var loosen = Btn(row, "鬆", () => Ui.Send(SimIds.ToolSocket, "dir:loosen"), 80);
            var lockB = Btn(row, "鎖", () => Ui.Send(SimIds.ToolSocket, "dir:lock"), 80);
            var info = Ui.Text(p, "", 23);
            Help(p, "點螺栓套上，滾輪每格轉 45°（或按住左鍵左右拖）。脫出後再點一下＝收進零件盒；方向「鎖」時點空孔＝放回螺栓。");
            refreshers.Add(s =>
            {
                string size = s.Get("tool.socket_wrench.size", "10"), dir = s.Get("tool.socket_wrench.dir", "loosen");
                foreach (var (sz, b) in sizes) b.GetComponent<Image>().color = sz == size ? Ui.ButtonOn : Ui.ButtonBg;
                loosen.GetComponent<Image>().color = dir == "loosen" ? Ui.ButtonOn : Ui.ButtonBg;
                lockB.GetComponent<Image>().color = dir == "lock" ? Ui.ButtonOn : Ui.ButtonBg;
                string bolt = s.Get("tool.socket_wrench.engaged");
                info.text = string.IsNullOrEmpty(bolt) ? "尚未套上螺栓" :
                    $"已套上：{Sim.Registry?.NameOf(bolt)}　累計鬆開 {s.GetFloat(bolt + ".angle_deg"):0}°　鎖緊 {s.GetFloat(bolt + ".lock_deg"):0}°　狀態：{StateName(s.Get(bolt + ".state"))}";
            });
        }

        static string StateName(string st)
        {
            switch (st)
            {
                case "Tight": return "鎖緊"; case "Loosened": return "已鬆半圈"; case "Out": return "已脫出"; case "InTray": return "在零件盒";
                case "Placed": return "已放回"; case "Snug": return "已貼合"; case "Torqued": return "已鎖到扭力"; default: return st;
            }
        }

        void BuildTorque()
        {
            var p = Panel(SimIds.ToolTorque, "扭力扳手");
            var row = Row(p);
            var setting = Ui.Text(row, "", 26, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold); Ui.Size(setting, 250);
            Btn(row, "−0.5", () => Ui.Send(SimIds.ToolTorque, "set:-"), 90);
            Btn(row, "+0.5", () => Ui.Send(SimIds.ToolTorque, "set:+"), 90);
            var input = Ui.Input(row, 22, "數值"); Ui.Size(input, 100, 48);
            Btn(row, "設定", () => { if (float.TryParse(input.text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) Ui.Send(SimIds.ToolTorque, "set:" + input.text); }, 90);

            const float max = 40f;
            var bar = Ui.Panel(p, "Bar", new Color(0.2f, 0.22f, 0.25f), true, false); Ui.Size(bar, -1, 36);
            var zone = Ui.Panel(bar.transform, "Zone", new Color(0.2f, 0.7f, 0.3f, 0.6f), false, false);
            zone.rectTransform.anchorMin = new Vector2(23f / max, 0); zone.rectTransform.anchorMax = new Vector2(27f / max, 1);
            zone.rectTransform.offsetMin = zone.rectTransform.offsetMax = Vector2.zero;
            var fill = Ui.Panel(bar.transform, "Fill", Ui.Accent, false, false);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var tick = Ui.Panel(bar.transform, "Setting", Color.white, false, false);
            tick.rectTransform.offsetMin = tick.rectTransform.offsetMax = Vector2.zero;
            var current = Ui.Text(p, "", 24);
            var reached = Ui.Text(p, "", 44, Ui.Ok, TextAnchor.MiddleCenter, FontStyle.Bold);
            fullOnly.Add(reached);
            Help(p, "在已貼合的螺栓上按住左鍵施力（每秒 +8 N·m），聽到喀聲就放開。綠色區 23–27 N·m 為合格（示例值）。");
            refreshers.Add(s =>
            {
                float set = s.GetFloat("tool.torque_wrench.setting", 20f), cur = s.GetFloat("tool.torque_wrench.current");
                setting.text = $"設定 {set:0.0} N·m";
                fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(cur / max), 1);
                tick.rectTransform.anchorMin = new Vector2(Mathf.Clamp01(set / max) - 0.003f, 0); tick.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(set / max) + 0.003f, 1);
                bool inZone = cur >= 23f && cur <= 27f;
                bool flash = s.GetBool("tool.torque_wrench.reached") && Time.time - s.GetFloat("tool.torque_wrench.flash_time") < 1.2f;
                current.text = $"目前扭力 {cur:0.0} N·m　" + (cur <= 0 ? "" : inZone ? Ui.Tint("在合格區內", Ui.Ok) : cur > 27f ? Ui.Tint("過大！", Ui.Bad) : Ui.Tint("還不夠", Ui.Muted))
                               + Ui.Tint("　合格 23–27 N·m", Ui.Muted)
                               + (compact && flash ? Ui.Tint("　喀！到達設定值", Ui.Ok) : "");   // 精簡版把「喀」併在同一行
                reached.text = flash ? "喀！到達設定值" : "";
                Ui.SetActive(reached, flash && !compact);
            });
        }

        void BuildCloth()
        {
            var p = Panel(SimIds.ToolCloth, "清潔布");
            var bar = Ui.Panel(p, "Bar", new Color(0.2f, 0.22f, 0.25f), true, false); Ui.Size(bar, -1, 30);
            var fill = Ui.Panel(bar.transform, "Fill", Ui.Ok, false, false);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var txt = Ui.Text(p, "", 23);
            Help(p, "按住左鍵在托架黑色碳粉上來回拖，清除率要 ≥ 95%（示例值）。");
            refreshers.Add(s =>
            {
                string side = s.Get("procedure.side", "L");
                float r = s.GetFloat($"ip.carbon_strip_{side}.bracket.clean_ratio");
                fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(r), 1);
                txt.text = $"托架清除率 {r * 100f:0}%" + (r >= 0.95f ? Ui.Tint("　✔ 已擦乾淨", Ui.Ok) : "");
            });
        }

        void BuildSimple(string toolId, string title, string body)
        {
            var p = Panel(toolId, title);
            Ui.Text(p, body, 21, Ui.Muted);
        }

        public override void Refresh(ISimState s)
        {
            string active = s.Get(SimIds.ActiveTool);
            // 作業單停靠在右側，工具面板照常顯示（邊量邊抄）；Arrange 會避開作業單
            bool show = s.GetBool("training.running") && !string.IsNullOrEmpty(active) && panels.ContainsKey(active);
            Ui.SetActive(root, show);
            if (!show) return;
            bool docked = root.parent != safeArea;   // VR：掛在左手腕面板下
            compact = docked || safeArea.rect.width < CompactBelowWidth;
            foreach (var c in fullOnly) Ui.SetActive(c, !compact);
            foreach (var kv in panels) Ui.SetActive(kv.Value, kv.Key == active);
            foreach (var r in refreshers) r(s);
            var msg = s.Get(active + ".message");
            message.text = msg;
            Ui.SetActive(message, !string.IsNullOrEmpty(msg));
            if (!docked) Arrange();
        }

        /// <summary>
        /// 置中放在工具列上方；若和左上步驟面板、右側升降控制器或右側作業單重疊，往空的那一側挪，仍不夠就縮窄（最窄 480）。
        /// </summary>
        void Arrange()
        {
            float half = safeArea.rect.width / 2f;
            float width = Mathf.Min(compact ? CompactWidth : FullWidth, safeArea.rect.width - 2 * Gap);
            root.sizeDelta = new Vector2(width, root.sizeDelta.y);
            root.anchoredPosition = new Vector2(0, Bottom);
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);

            // 左右可用範圍：避開會在垂直方向重疊的面板
            float panelTop = Bottom - safeArea.rect.height / 2f + root.rect.height;   // 以安全區中心為原點
            float panelBottom = Bottom - safeArea.rect.height / 2f;
            float minX = -half, maxX = half;
            foreach (var name in new[] { "StepPanel", "LiftPanel", "WorkOrder_Dim/WorkOrder" })
            {
                var other = safeArea.Find(name) as RectTransform;
                if (other == null || !other.gameObject.activeInHierarchy) continue;
                var r = RectInSafe(other);
                if (r.yMin >= panelTop || r.yMax <= panelBottom) continue;   // 垂直方向不重疊
                if (r.center.x < 0) minX = Mathf.Max(minX, r.xMax + Gap);
                else maxX = Mathf.Min(maxX, r.xMin - Gap);
            }
            float avail = maxX - minX;
            if (avail < width) width = Mathf.Max(MinWidth, avail);
            float x = Mathf.Clamp(0f, minX + width / 2f, maxX - width / 2f);
            if (minX + width / 2f > maxX - width / 2f) x = (minX + maxX) / 2f;
            root.sizeDelta = new Vector2(width, root.sizeDelta.y);
            root.anchoredPosition = new Vector2(x, Bottom);
        }

        Rect RectInSafe(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector2 a = safeArea.InverseTransformPoint(corners[0]), b = safeArea.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }
    }
}
