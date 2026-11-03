using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1.5b b4 步驟匯入器（唯一來源：Assets/_Project/Data/Source/SubActions.csv、Steps.csv，附錄 C 原樣存檔）。
    /// 這一部分只做解析與展開（純函式，ImporterTests 直接測）：templateN（n=1→4、n=1,3,2,4、side=L,R、interleave=a+b，可用分號組合）、
    /// {side}／{n} 代換、課程（both／periodic／daily）與 *_daily 欄、依 n 或側別的站點對照表。完成條件對應與資產產生在後面的部分。
    /// </summary>
    public static partial class StepsCsvImporter
    {
        public const string SourceFolder = "Assets/_Project/Data/Source";
        public static string SubActionsPath => SourceFolder + "/SubActions.csv";
        public static string StepsPath => SourceFolder + "/Steps.csv";
        public static readonly string[] Courses = { "periodic", "daily" };

        // ---------- CSV ----------

        /// <summary>RFC 4180：逗號分隔、雙引號包住含逗號／換行的欄位、"" 是一個引號。回傳每列的欄位（含表頭）。</summary>
        public static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>(); var row = new List<string>(); var f = new StringBuilder(); bool q = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (q) { if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { f.Append('"'); i++; } else q = false; } else f.Append(c); continue; }
                if (c == '"') q = true;
                else if (c == ',') { row.Add(f.ToString()); f.Clear(); }
                else if (c == '\n') { row.Add(f.ToString()); f.Clear(); rows.Add(row.ToArray()); row.Clear(); }
                else if (c != '\r') f.Append(c);
            }
            if (f.Length > 0 || row.Count > 0) { row.Add(f.ToString()); rows.Add(row.ToArray()); }
            return rows.Where(r => r.Any(x => x.Length > 0)).ToList();
        }

        /// <summary>CSV 的一列（以表頭欄名取值；行號＝檔案裡的第幾行，給錯誤訊息用）。</summary>
        public class Row
        {
            public int line;
            public Dictionary<string, string> v = new Dictionary<string, string>();
            public string this[string k] => v.TryGetValue(k, out var s) ? s ?? "" : "";
            public override string ToString() => $"第 {line} 行（{this["stepId"]} {this["subId"]}）";
        }

        public static List<Row> Rows(string csvText)
        {
            var all = ParseCsv(csvText);
            var head = all[0];
            return all.Skip(1).Select((r, i) => new Row { line = i + 2, v = head.Select((h, k) => (h, k)).ToDictionary(x => x.h, x => x.k < r.Length ? r[x.k] : "") }).ToList();
        }

        // ---------- templateN ----------

        public class Template
        {
            public List<int> n = new List<int>();
            public List<string> sides = new List<string>();
            public (string a, string b)? interleave;
            /// <summary>R1.5b b4（D43）：when=條件（例 fault.insulator_dirty）：只在條件成立時才做的分支小動作。</summary>
            public string when = "";
        }

        /// <summary>templateN 欄：n=1→4、n=1,3,2,4、side=L,R、interleave=a+b、when=條件（分支小動作），可用分號組合。格式錯了丟 FormatException。</summary>
        public static Template ParseTemplate(string s)
        {
            var t = new Template();
            foreach (var part in (s ?? "").Split(';').Select(p => p.Trim()).Where(p => p.Length > 0))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) throw new FormatException("templateN 看不懂：" + part);
                string k = part.Substring(0, eq), val = part.Substring(eq + 1);
                if (k == "n")
                {
                    if (val.Contains("→")) { var ab = val.Split('→'); int a = int.Parse(ab[0]), b = int.Parse(ab[1]); for (int i = a; i <= b; i++) t.n.Add(i); }
                    else t.n.AddRange(val.Split(',').Select(x => int.Parse(x.Trim())));
                }
                else if (k == "side") t.sides.AddRange(val.Split(',').Select(x => x.Trim()));
                else if (k == "interleave") { var ab = val.Split('+'); t.interleave = (ab[0].Trim(), ab[1].Trim()); }
                else if (k == "when") t.when = val.Trim();
                else throw new FormatException("templateN 不認得的鍵：" + k);
            }
            return t;
        }

        // ---------- 展開 ----------

        /// <summary>展開後的一個小動作（某一門課）。</summary>
        public class Expanded
        {
            public Row row;
            public string course, stepId, side, subId, text, textDesktop, targetId, toolId, condition, standPointId, errorIds, when = "";
            public int n, order;
            /// <summary>把只在某一側展開的列（side=L）也算進去的順序：第 1 層用它對 R1 的位置（R 側少了 CSR_06 b 也不會錯位）。</summary>
            public int fullOrder;
            public override string ToString() => $"{course} {stepId} {subId}（{row}）";
        }

        static string Sub(string s, string side, int n) => (s ?? "").Replace("{side}", side ?? "").Replace("{n}", n > 0 ? n.ToString() : "{n}");

        /// <summary>站點欄：單一站點、依 n 的對照表（n1:站點;n2:…）、依側別的對照表（L:站點;R:…）。回傳這個小動作的站點，或錯誤。</summary>
        public static string ResolveStand(string field, Template t, string side, int n, out string error)
        {
            error = null;
            var f = (field ?? "").Trim();
            if (!f.Contains(":")) return f;
            var map = f.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).Select(p => { int i = p.IndexOf(':'); return (k: p.Substring(0, i).Trim(), v: p.Substring(i + 1).Trim()); }).ToDictionary(x => x.k, x => x.v);
            if (map.Keys.All(k => k.StartsWith("n")))
            {
                if (t.n.Count == 0) { error = "沒有 templateN 的 n 卻寫了依 n 的站點對照表"; return ""; }
                var missing = t.n.Where(x => !map.ContainsKey("n" + x)).ToList();
                if (missing.Count > 0) { error = "站點對照表沒有涵蓋 n＝" + string.Join(",", missing); return ""; }
                return map["n" + n];
            }
            if (map.Keys.All(k => k == "L" || k == "R"))
            {
                if (t.sides.Count == 0) { error = "沒有 side=L,R 卻寫了依側別的站點對照表"; return ""; }
                if (!map.TryGetValue(side ?? "", out var v)) { error = $"站點對照表沒有側別 {side}"; return ""; }
                return v;
            }
            error = "站點對照表的鍵要全是 n1…或全是 L／R：" + f;
            return "";
        }

        public static bool InCourse(string courses, string course) => courses == "both" || courses == course;

        /// <summary>
        /// 把 SubActions.csv 展開成兩門課的小動作：每一步（{side} 換成 L、R）依 order 排，範本 d 展開成 d1–d4，interleave=a+b 交錯成 a1 b1 a2 b2…；
        /// 不在該課程的列不存在（不算完成、不計時）；日常的文字、目標、條件有 *_daily 就用它。errors 收集所有格式問題（行號＋內容）。
        /// </summary>
        public static List<Expanded> Expand(List<Row> rows, List<string> errors)
        {
            var list = new List<Expanded>();
            foreach (var g in rows.GroupBy(r => r["stepId"]))
            {
                var rs = g.OrderBy(r => int.TryParse(r["order"], out var o) ? o : 0).ToList();
                var tmpl = new Dictionary<Row, Template>();
                foreach (var r in rs) { try { tmpl[r] = ParseTemplate(r["templateN"]); } catch (Exception e) { errors.Add($"{r}：{e.Message}"); tmpl[r] = new Template(); } }
                bool sided = g.Key.Contains("{side}");
                var sides = sided ? rs.SelectMany(r => tmpl[r].sides).Distinct().ToList() : new List<string> { "" };
                if (sided && sides.Count == 0) { errors.Add($"{rs[0]}：步驟 ID 有 {{side}}，但沒有 side=L,R 範本"); continue; }
                foreach (var r in rs.Where(r => !sided && tmpl[r].sides.Count > 0)) errors.Add($"{r}：有 side=L,R 範本，但步驟 ID 沒有 {{side}}");
                foreach (var course in Courses)
                    foreach (var side in sides)
                    {
                        string stepId = g.Key.Replace("{side}", side);
                        int order = 0, skipped = 0;
                        var consumed = new HashSet<Row>();
                        foreach (var r in rs)
                        {
                            if (consumed.Contains(r) || !InCourse(r["courses"], course)) continue;
                            var t = tmpl[r];
                            if (sided && t.sides.Count > 0 && !t.sides.Contains(side)) { skipped += Math.Max(1, t.n.Count) * (t.interleave.HasValue ? 2 : 1); continue; }   // 只在某一側展開（例：CSR_06 b side=L）
                            var pair = t.interleave.HasValue && t.interleave.Value.a == r["subId"] ? rs.FirstOrDefault(x => x["subId"] == t.interleave.Value.b) : null;
                            if (t.interleave.HasValue && pair == null && t.interleave.Value.b != r["subId"]) errors.Add($"{r}：interleave={t.interleave.Value.a}+{t.interleave.Value.b} 找不到另一列");
                            if (pair != null) consumed.Add(pair);
                            var seq = t.n.Count > 0 ? t.n : new List<int> { 0 };
                            foreach (int n in seq)
                                foreach (var src in pair != null ? new[] { r, pair } : new[] { r })
                                {
                                    if (!InCourse(src["courses"], course)) continue;
                                    bool daily = course == "daily";
                                    string Pick(string col) => daily && src[col + "_daily"].Length > 0 ? src[col + "_daily"] : src[col];
                                    var standField = daily ? src["standPointId_daily"] : src["standPointId_periodic"];
                                    var stand = Sub(ResolveStand(standField, tmpl[src], side, n, out var se), side, n);
                                    if (se != null) errors.Add($"{src}（{course}）：{se}");
                                    var e = new Expanded
                                    {
                                        row = src, course = course, stepId = stepId, side = side, n = n, order = ++order, fullOrder = order + skipped,
                                        subId = src["subId"] + (n > 0 ? n.ToString() : ""),
                                        text = Sub(Pick("instruction"), side, n), textDesktop = Sub(src["instructionDesktop"], side, n),
                                        targetId = Sub(Pick("targetId"), side, n), toolId = Sub(src["toolId"], side, n), condition = Sub(Pick("condition"), side, n),
                                        standPointId = stand, errorIds = src["errorIds"], when = tmpl[src].when,
                                    };
                                    if (string.IsNullOrEmpty(e.textDesktop)) e.textDesktop = e.text;
                                    list.Add(e);
                                }
                        }
                    }
            }
            return list;
        }

        /// <summary>Steps.csv 的一列：步驟的標題、階段、課程、兩門課的順序、參考時間（教學／考核 × 定期／日常）、錯誤 ID 與嚴重度。</summary>
        public class StepMeta
        {
            public Row row;
            public string stepId, title, phase, courses;
            public int orderPeriodic, orderDaily;
            public float teachP, examP, teachD, examD;
            public List<(string id, string severity)> errors = new List<(string, string)>();
        }

        public static List<StepMeta> Steps(List<Row> rows, List<string> errors)
        {
            float F(Row r, string k) => float.TryParse(r[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
            int I(Row r, string k) => int.TryParse(r[k], out var v) ? v : 0;
            return rows.Select(r =>
            {
                var m = new StepMeta
                {
                    row = r, stepId = r["stepId"], title = r["title"], phase = r["phase"], courses = r["courses"], orderPeriodic = I(r, "orderPeriodic"), orderDaily = I(r, "orderDaily"),
                    teachP = F(r, "refTeachS_periodic"), examP = F(r, "refExamS_periodic"), teachD = F(r, "refTeachS_daily"), examD = F(r, "refExamS_daily"),
                };
                m.errors = ParseErrorRefs(r["errorIds"], s => errors.Add($"{r}：{s}"));
                return m;
            }).ToList();
        }

        /// <summary>「ID:嚴重度」以分號分隔。</summary>
        public static List<(string id, string severity)> ParseErrorRefs(string s, Action<string> fail)
        {
            var list = new List<(string, string)>();
            foreach (var p in (s ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                int i = p.IndexOf(':');
                if (i <= 0) { fail("錯誤引用要寫成「ID:嚴重度」：" + p); continue; }
                list.Add((p.Substring(0, i), p.Substring(i + 1)));
            }
            return list;
        }

        /// <summary>展開後的數量與參考時間加總（只寫進 Logs/R1.5b-assembly.txt 對照，不寫進測試）。</summary>
        public static string CountsReport()
        {
            var errs = new List<string>();
            var ex = Expand(LoadSubActionRows(), errs); var st = Steps(LoadStepRows(), errs);
            var sb = new StringBuilder();
            foreach (var c in Courses)
            {
                var inC = st.Where(s => InCourse(s.courses, c)).ToList();
                sb.AppendLine($"{c}：步驟 {inC.Count}、小動作 {ex.Count(e => e.course == c)}、參考時間 教學 {inC.Sum(s => c == "periodic" ? s.teachP : s.teachD):0} s／考核 {inC.Sum(s => c == "periodic" ? s.examP : s.examD):0} s");
            }
            if (errs.Count > 0) sb.AppendLine("格式問題：" + string.Join("；", errs));
            return sb.ToString();
        }

        public static List<Row> LoadSubActionRows() => Rows(File.ReadAllText(SubActionsPath, Encoding.UTF8));
        public static List<Row> LoadStepRows() => Rows(File.ReadAllText(StepsPath, Encoding.UTF8));
    }
}
