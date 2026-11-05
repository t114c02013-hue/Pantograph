using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using PantographSim.Core;

namespace PantographSim.Recording
{
    /// <summary>
    /// 報表：report.html（單一檔案、CSS 內嵌、UTF-8、附列印樣式——用瀏覽器「列印 → 另存成 PDF」）＋ steps／errors／tools／values／hints.csv（UTF-8 加 BOM，Excel 直接開）。
    /// 一律用中文步驟名與中文錯誤名；stepId、錯誤 ID 只當小字附註（CSV 另開欄位）。分數與錯誤數只取 SessionResult。
    /// </summary>
    public static class ReportBuilder
    {
        static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static readonly Dictionary<string, string> CommandNames = new Dictionary<string, string>
        {
            { UiCommands.FocusTarget, "帶我到目標" }, { UiCommands.Help, "說明" }, { UiCommands.Undo, "↶ 上一步" }, { UiCommands.ResetTool, "重設工具" },
            { UiCommands.RestartStep, "從這一步重做" }, { UiCommands.HintPanelToggle, "顯示／收起提示板" }, { UiCommands.WorkOrderToggle, "打開／收起作業單" }, { UiCommands.StallHint, "卡關提示（看還差什麼）" }
        };

        /// <summary>重新讀紀錄資料夾，產生報表與 CSV。回傳 report.html 的完整路徑。</summary>
        public static string Generate(string folder)
        {
            var meta = RecordingIndex.ReadJson(Path.Combine(folder, RecordFiles.Meta)) ?? new Dictionary<string, object>();
            var lines = RecordLine.ReadAll(Path.Combine(folder, RecordFiles.Session), out _);
            var old = RecordingIndex.ReadJson(Path.Combine(folder, RecordFiles.Summary));
            var summary = SummaryBuilder.Build(meta, lines, old != null && old.Bool("rebuilt"));
            var video = Directory.Exists(folder) ? Directory.GetFiles(folder, "video_*.mp4").Select(Path.GetFileName).OrderBy(x => x).ToList() : new List<string>();
            File.WriteAllText(Path.Combine(folder, RecordFiles.Report), Html(meta, summary, video, Path.GetFileName(folder)), Utf8);
            WriteCsvs(folder, summary);
            return Path.GetFullPath(Path.Combine(folder, RecordFiles.Report));
        }

        // ---------- 格式 ----------
        public static string Clock(double sec)
        {
            if (double.IsNaN(sec) || sec < 0) sec = 0;
            int s = (int)Math.Round(sec);
            return $"{s / 60:00}:{s % 60:00}";
        }

        static string Dur(double sec) { int s = (int)Math.Round(sec); return s >= 60 ? $"{s / 60} 分 {s % 60} 秒" : $"{s} 秒"; }
        /// <summary>只跳脫 HTML 特殊字元；中文保持原字（WebUtility.HtmlEncode 在 Unity 會把中文全轉成 &amp;#…;，無法搜尋）。</summary>
        static string H(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
        static string Small(string s) => string.IsNullOrEmpty(s) ? "" : $"<span class=\"id\">{H(s)}</span>";
        static IEnumerable<Dictionary<string, object>> Rows(Dictionary<string, object> d, string key) => d.Arr(key).OfType<Dictionary<string, object>>();
        static string SevClass(string sev) => sev == "Critical" ? "crit" : sev == "Major" ? "major" : "minor";

        static string FormatDate(string iso) =>
            DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : iso;

        // ---------- HTML ----------
        static string Html(Dictionary<string, object> meta, Dictionary<string, object> s, List<string> videoFiles, string folderName)
        {
            var result = s.Obj("result");
            bool finished = result != null;
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"zh-Hant\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append($"<title>訓練報表　{H(s.Str("trainee"))}　{H(FormatDate(s.Str("startedAt")))}</title><style>{Css}</style></head><body>");

            // ---- 第一頁 ----
            sb.Append("<section class=\"cover\">");
            sb.Append($"<h1>{H(meta.Str("procedureTitle", "集電弓碳滑板更換"))}　訓練報表</h1>");
            if (s.Str("testDriven").Length > 0) sb.Append($"<p><b>{H(s.Str("testDriven"))}</b>：前面的步驟由編輯器代做，這份只算接手之後，不是完整訓練成績。</p>");
            if (!finished) sb.Append("<p class=\"banner\">這一筆是「未完成（中斷）」：沒有結算，所以沒有分數；以下是中斷前已記下的內容。</p>");
            string passText = finished ? (result.Bool("passed") ? "<b class=\"ok\">通過</b>" : "<b class=\"bad\">未通過</b>") : "—（中斷）";
            var incomplete = s.StrArr("incompleteStepNames");
            sb.Append("<table class=\"kv\">");
            void KV(string k, string v) => sb.Append($"<tr><th>{k}</th><td>{v}</td></tr>");
            KV("學員", H(s.Str("trainee")));
            KV("日期", H(FormatDate(s.Str("startedAt"))));
            KV("模式", H(s.Str("mode")));
            KV("輸入方式", H(s.Str("input")));
            KV("情境", H(s.Str("scenarioTitle")) + Small(s.Str("scenarioId")));
            KV("是否通過", passText);
            KV("總分", finished ? $"<b class=\"big\">{result.Num("score"):0}</b> 分 <span class=\"note\">（計分方式為示例值）</span>" : "—");
            KV("總時間", finished ? Dur(result.Num("totalSeconds")) : Dur(s.Num("sessionSeconds")) + "（到中斷為止）");
            KV("Critical（嚴重）次數", finished ? $"<b class=\"{(result.Num("critical") > 0 ? "bad" : "ok")}\">{result.Num("critical"):0}</b>" : "—");
            KV("錯誤總數", finished ? $"{result.Num("errorTotal"):0}（嚴重 {result.Num("critical"):0}、錯誤 {result.Num("major"):0}、提醒 {result.Num("minor"):0}）" : "—");
            KV("未完成的步驟", incomplete.Length == 0 ? "無" : string.Join("、", incomplete.Select(H)));
            sb.Append("</table>");
            sb.Append($"<p class=\"note\">紀錄資料夾：{H(folderName)}　程式版本 {H(s.Str("appVersion"))}　分數與錯誤數取自結算（SessionResult）；同一步同一錯誤只算一次，下方明細列出每一次發生。</p>");
            sb.Append("</section>");

            // ---- 錯誤明細 ----
            var errors = Rows(s, "errors").ToList();
            var crit = errors.Where(e => e.Str("severity") == "Critical").ToList();
            sb.Append("<section><h2 class=\"crit-title\">嚴重錯誤（Critical）</h2>");
            if (crit.Count == 0) sb.Append("<p>沒有嚴重錯誤。</p>");
            else ErrorTable(sb, crit, true);
            sb.Append("</section>");
            sb.Append("<section><h2>錯誤明細</h2>");
            if (errors.Count == 0) sb.Append("<p>沒有錯誤，做得好！</p>");
            else ErrorTable(sb, errors, false);
            sb.Append("</section>");

            // ---- 故障處理時間（v3.10 第 8 項：不算進時間判定）----
            var faultTimes = Rows(s, "faultTimes").ToList();
            if (faultTimes.Count > 0)
            {
                sb.Append("<section><h2>故障處理時間</h2><p class=\"note\">這段時間另列，不算進步驟時間的判定。</p><table><thead><tr><th>步驟</th><th>故障</th><th>花費</th></tr></thead><tbody>");
                foreach (var f in faultTimes) sb.Append($"<tr><td>{H(f.Str("stepName"))}{Small(f.Str("step"))}</td><td>{H(f.Str("fault"))}</td><td>{Dur(f.Num("seconds"))}</td></tr>");
                sb.Append("</tbody></table></section>");
            }

            // ---- 步驟時間 ----
            sb.Append("<section><h2>步驟時間</h2><p class=\"note\">參考時間為示例值（");
            sb.Append(Labels.RefSecondsKind(s.Str("mode")));
            sb.Append(s.Str("mode") == "練習" ? "：練習沒有小動作提示，和考核用同一組" : "");
            sb.Append("；兩側各做一次的步驟為單側秒數），超過 2 倍標 ▲。缺參考時間的步驟不標記。</p>");
            sb.Append("<table><thead><tr><th>#</th><th>步驟</th><th>狀態</th><th>花費</th><th>參考（示例值）</th><th>標記</th></tr></thead><tbody>");
            foreach (var r in Rows(s, "steps"))
            {
                bool over = r.Bool("over2x");
                string st = r.Str("status");
                double refSec = r.Num("refSeconds");
                sb.Append($"<tr class=\"{(over ? "over" : "")}\"><td>{r.Num("order"):0}</td><td>{H(r.Str("name"))}{Small(r.Str("runId"))}</td>");
                sb.Append($"<td class=\"{(st == "未完成" ? "bad" : st == "重做" ? "warn" : "")}\">{H(st)}{(r.Num("redoCount") > 0 ? $"（重做 {r.Num("redoCount"):0} 次）" : "")}</td>");
                sb.Append($"<td>{(st == "預設完成" || st == "略過" ? "—" : Dur(r.Num("seconds")))}</td><td>{(refSec > 0 ? refSec.ToString("0") + " 秒" : "—")}</td><td>{(over ? "<b class=\"bad\">▲ 超過 2 倍</b>" : "")}</td></tr>");
            }
            sb.Append("</tbody></table></section>");

            // ---- 數值 ----
            sb.Append("<section><h2>數值（讀數、判定、扭力、角度）</h2><table><thead><tr><th>時間</th><th>步驟</th><th>項目</th><th>數值</th><th>來源標籤</th><th>標準</th></tr></thead><tbody>");
            var values = Rows(s, "values").ToList();
            if (values.Count == 0) sb.Append("<tr><td colspan=\"6\">沒有記到數值。</td></tr>");
            foreach (var v in values)
            {
                string tag = v.Str("tag");
                sb.Append($"<tr><td>{Clock(v.Num("t"))}</td><td>{H(v.Str("stepName"))}{Small(v.Str("step"))}</td><td>{H(v.Str("label"))}{(v.Str("note").Length > 0 ? $" <span class=\"note\">{H(v.Str("note"))}</span>" : "")}</td>");
                sb.Append($"<td><b>{H(v.Str("value"))}</b> {H(v.Str("unit"))}</td><td><span class=\"tag {(tag == "保養表值" ? "tag-mt" : "tag-ex")}\">{H(tag)}</span></td><td>{H(v.Str("standard"))}</td></tr>");
            }
            sb.Append("</tbody></table></section>");

            // ---- 工具 ----
            sb.Append("<section><h2>工具（拿起與歸還）</h2><table><thead><tr><th>掛位</th><th>工具</th><th>拿起</th><th>歸還</th><th>使用中（留在場上）</th><th>結束時</th></tr></thead><tbody>");
            var tools = Rows(s, "tools").ToList();
            if (tools.Count == 0) sb.Append("<tr><td colspan=\"6\">沒有拿過工具。</td></tr>");
            foreach (var t in tools)
            {
                bool left = t.Bool("leftover");
                string Times(string k) { var l = t.Arr(k).OfType<double>().Select(Clock).ToList(); return l.Count == 0 ? "—" : string.Join("、", l); }
                sb.Append($"<tr class=\"{(left ? "leftover" : "")}\"><td>{t.Num("slot"):0}</td><td>{H(t.Str("name"))}{Small(t.Str("tool"))}</td><td>{Times("picked")}</td><td>{Times("returned")}</td><td>{Times("deployed")}</td>");
                sb.Append($"<td>{(left ? "<b class=\"bad\">遺留（沒有掛回工具架）</b>" : "已掛回")}</td></tr>");
            }
            sb.Append("</tbody></table></section>");

            // ---- 提示使用 ----
            sb.Append("<section><h2>提示使用</h2><table><thead><tr><th>功能</th><th>次數</th><th>用在哪一步</th></tr></thead><tbody>");
            var hints = s.Obj("hints") ?? new Dictionary<string, object>();
            foreach (var name in UiCommands.All)
            {
                var h = hints.Obj(name);
                int count = h != null ? (int)h.Num("count") : 0;
                var steps = h != null ? Rows(h, "steps").Select(x => $"{H(x.Str("stepName"))}×{x.Num("count"):0}").ToList() : new List<string>();
                sb.Append($"<tr><td>{H(CommandNames[name])}{Small(name)}</td><td>{count}</td><td>{(steps.Count == 0 ? "—" : string.Join("、", steps))}</td></tr>");
            }
            sb.Append("</tbody></table></section>");

            // ---- 卡關（防護網偵測；不扣分） ----
            var stalls = Rows(s, "stalls").ToList();
            if (stalls.Count > 0)
            {
                sb.Append("<section><h2>卡關紀錄（不扣分）</h2><p class=\"note\">rule_mismatch＝小動作都完成但完成條件超過 3 秒不成立；missed_commit＝條件都成立但步驟沒推進（程式判定問題）。</p>");
                sb.Append("<table><thead><tr><th>時間</th><th>步驟</th><th>種類</th><th>還差什麼</th><th>未成立的條件</th><th>當時的值</th></tr></thead><tbody>");
                foreach (var r in stalls)
                    sb.Append($"<tr><td>{Clock(r.Num("t"))}</td><td>{H(r.Str("stepName"))}</td><td>{H(r.Str("kind"))}</td><td>{H(r.Str("explain"))}</td><td>{H(r.Str("conditionIds")).Replace("\n", "<br>")}</td><td>{H(r.Str("values")).Replace("\n", "<br>")}</td></tr>");
                sb.Append("</tbody></table></section>");
            }

            // ---- 順序對照 ----
            sb.Append("<section><h2>順序對照（和 PRC 標準順序比較）</h2>");
            var diffs = Rows(s, "order").ToList();
            if (diffs.Count == 0) sb.Append("<p>實際的步驟與小動作順序和標準順序相同。</p>");
            else
            {
                sb.Append("<table><thead><tr><th>時間</th><th>步驟</th><th>不同處</th><th>標準</th><th>實際</th></tr></thead><tbody>");
                foreach (var d in diffs)
                    sb.Append($"<tr><td>{Clock(d.Num("t"))}</td><td>{H(d.Str("stepName"))}{Small(d.Str("step"))}</td><td><b>{H(d.Str("what"))}</b></td><td>{H(d.Str("standard"))}</td><td class=\"bad\">{H(d.Str("actual"))}</td></tr>");
                sb.Append("</tbody></table>");
            }
            sb.Append("</section>");

            // ---- 影片 ----
            sb.Append("<section><h2>影片</h2>");
            var videos = Rows(s, "videos").ToList();
            if (videos.Count == 0 && videoFiles.Count == 0) sb.Append("<p>這一場沒有錄影。</p>");
            else
            {
                sb.Append("<table><thead><tr><th>檔名</th><th>影片 0 秒＝紀錄的</th></tr></thead><tbody>");
                foreach (var v in videos) sb.Append($"<tr><td>{H(v.Str("file"))}</td><td>{Clock(v.Num("sessionTime"))}（第 {v.Num("sessionTime"):0.0} 秒）</td></tr>");
                foreach (var f in videoFiles.Where(f => videos.All(v => v.Str("file") != f))) sb.Append($"<tr><td>{H(f)}</td><td>—</td></tr>");
                sb.Append("</tbody></table>");
            }
            sb.Append("</section>");
            sb.Append($"<footer>產生時間 {DateTime.Now:yyyy-MM-dd HH:mm}　數值標示「保養表值」者依保養表，「示例值」為教學用示例。</footer>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        static void ErrorTable(StringBuilder sb, List<Dictionary<string, object>> errors, bool critical)
        {
            sb.Append($"<table class=\"{(critical ? "crit-table" : "")}\"><thead><tr><th>時間</th><th>步驟</th><th>錯誤</th><th>嚴重度</th><th>做錯什麼</th><th>為什麼</th><th>怎麼改</th></tr></thead><tbody>");
            foreach (var e in errors)
            {
                string sev = e.Str("severity");
                sb.Append($"<tr class=\"{SevClass(sev)}\"><td>{Clock(e.Num("t"))}</td><td>{H(e.Str("stepName"))}{Small(e.Str("step"))}</td><td><b>{H(e.Str("name"))}</b>{Small(e.Str("errorId"))}</td>");
                sb.Append($"<td><span class=\"sev {SevClass(sev)}\">{H(ErrorNames.SeverityLabel(sev))}</span></td><td>{H(e.Str("what"))}</td><td>{H(e.Str("why"))}</td><td>{H(e.Str("fix"))}</td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        const string Css = @"
:root{--ink:#1d232b;--muted:#5b6672;--line:#d5dbe2;--crit:#c0271d;--major:#d9730d;--minor:#b59a00;--ok:#1f8a3b;--bg:#fff}
*{box-sizing:border-box}body{font-family:'Noto Sans TC','Microsoft JhengHei','PingFang TC',sans-serif;color:var(--ink);background:var(--bg);margin:24px auto;max-width:1100px;padding:0 16px;line-height:1.5;font-size:14px}
h1{font-size:26px;margin:0 0 16px}h2{font-size:19px;margin:28px 0 8px;padding-bottom:4px;border-bottom:2px solid var(--line)}
.crit-title{color:var(--crit);border-color:var(--crit)}
table{width:100%;border-collapse:collapse;margin:6px 0 4px}th,td{border:1px solid var(--line);padding:5px 7px;text-align:left;vertical-align:top}
thead th{background:#eef1f5}.kv th{width:200px;background:#f4f6f9}.kv td{font-size:16px}
.id{display:block;color:var(--muted);font-size:10.5px;font-family:Consolas,monospace}
.note{color:var(--muted);font-size:12px}.big{font-size:28px}.ok{color:var(--ok)}.bad{color:var(--crit)}.warn{color:var(--major)}
.banner{background:#fff4e5;border:1px solid var(--major);padding:8px 12px;border-radius:6px}
tr.crit td{background:#fdeceb}tr.major td{background:#fff5ea}tr.minor td{background:#fffbe6}
.crit-table{border:2px solid var(--crit)}.crit-table thead th{background:var(--crit);color:#fff}
.sev{display:inline-block;padding:1px 8px;border-radius:10px;color:#fff;font-size:12px;white-space:nowrap}.sev.crit{background:var(--crit)}.sev.major{background:var(--major)}.sev.minor{background:var(--minor)}
tr.over td{background:#fff5ea}tr.leftover td{background:#fdeceb;color:var(--crit)}
.tag{display:inline-block;padding:1px 6px;border-radius:4px;font-size:12px;border:1px solid}.tag-mt{border-color:#2a6ebb;color:#2a6ebb}.tag-ex{border-color:#8a8f96;color:#5b6672}
footer{margin:28px 0 8px;color:var(--muted);font-size:12px}
@page{size:A4;margin:14mm 12mm}
@media print{body{margin:0;max-width:none;font-size:11.5px}.cover{page-break-after:always}section{page-break-inside:auto}tr{page-break-inside:avoid}thead{display:table-header-group}
*{-webkit-print-color-adjust:exact;print-color-adjust:exact}h2{page-break-after:avoid}}
";

        // ---------- CSV ----------
        static void WriteCsvs(string folder, Dictionary<string, object> s)
        {
            var steps = new StringBuilder();
            Csv(steps, "順序", "步驟", "stepId", "狀態", "花費秒", "參考秒（示例值）", "超過2倍", "重做次數");
            foreach (var r in Rows(s, "steps"))
                Csv(steps, r.Num("order").ToString("0"), r.Str("name"), r.Str("runId"), r.Str("status"), r.Num("seconds").ToString("0.0", CultureInfo.InvariantCulture),
                    r.Num("refSeconds") > 0 ? r.Num("refSeconds").ToString("0") : "", r.Bool("over2x") ? "是" : "", r.Num("redoCount").ToString("0"));
            Save(folder, "steps.csv", steps);

            var errors = new StringBuilder();
            Csv(errors, "時間（秒）", "時間", "步驟", "stepId", "錯誤", "錯誤ID", "嚴重度", "做錯什麼", "為什麼", "怎麼改");
            foreach (var e in Rows(s, "errors"))
                Csv(errors, e.Num("t").ToString("0.0", CultureInfo.InvariantCulture), Clock(e.Num("t")), e.Str("stepName"), e.Str("step"), e.Str("name"), e.Str("errorId"),
                    ErrorNames.SeverityLabel(e.Str("severity")) + "（" + e.Str("severity") + "）", e.Str("what"), e.Str("why"), e.Str("fix"));
            Save(folder, "errors.csv", errors);

            var tools = new StringBuilder();
            Csv(tools, "掛位", "工具", "toolId", "拿起時間", "歸還時間", "使用中（留在場上）", "遺留");
            foreach (var t in Rows(s, "tools"))
            {
                string Times(string k) => string.Join(" ", t.Arr(k).OfType<double>().Select(Clock));
                Csv(tools, t.Num("slot").ToString("0"), t.Str("name"), t.Str("tool"), Times("picked"), Times("returned"), Times("deployed"), t.Bool("leftover") ? "是" : "");
            }
            Save(folder, "tools.csv", tools);

            var values = new StringBuilder();
            Csv(values, "時間", "步驟", "stepId", "項目", "鍵", "數值", "單位", "來源標籤", "標準", "備註");
            foreach (var v in Rows(s, "values"))
                Csv(values, Clock(v.Num("t")), v.Str("stepName"), v.Str("step"), v.Str("label"), v.Str("key"), v.Str("value"), v.Str("unit"), v.Str("tag"), v.Str("standard"), v.Str("note"));
            Save(folder, "values.csv", values);

            var hints = new StringBuilder();
            Csv(hints, "功能", "名稱", "總次數", "步驟", "stepId", "該步次數");
            var h = s.Obj("hints") ?? new Dictionary<string, object>();
            foreach (var name in UiCommands.All)
            {
                var o = h.Obj(name);
                int count = o != null ? (int)o.Num("count") : 0;
                var per = o != null ? Rows(o, "steps").ToList() : new List<Dictionary<string, object>>();
                if (per.Count == 0) Csv(hints, CommandNames[name], name, count.ToString(), "", "", "");
                foreach (var p in per) Csv(hints, CommandNames[name], name, count.ToString(), p.Str("stepName"), p.Str("step"), p.Num("count").ToString("0"));
            }
            Save(folder, "hints.csv", hints);
        }

        static void Csv(StringBuilder sb, params string[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(',');
                string c = cells[i] ?? "";
                if (c.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) c = "\"" + c.Replace("\"", "\"\"") + "\"";
                sb.Append(c);
            }
            sb.Append("\r\n");
        }

        static void Save(string folder, string name, StringBuilder sb) => File.WriteAllText(Path.Combine(folder, name), sb.ToString(), Utf8Bom);
    }
}
