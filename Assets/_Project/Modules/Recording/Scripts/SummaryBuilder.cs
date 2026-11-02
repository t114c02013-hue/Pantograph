using System;
using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;

namespace PantographSim.Recording
{
    /// <summary>
    /// 從 meta.json＋session.jsonl 算出摘要（summary.json 與報表共用）。分數與錯誤數只取 SessionResult 那一行，不自己重算；
    /// 沒有 SessionResult（中斷）就標「未完成（中斷）」，分數留空。
    /// </summary>
    public static class SummaryBuilder
    {
        public const string StatusDone = "完成", StatusInterrupted = "未完成（中斷）";
        /// <summary>平滑移動總距離（m）與總時間（s）：取 xr.move.distance／xr.move.seconds 最後一次的值。</summary>
        static Dictionary<string, object> SmoothMove(IReadOnlyList<RecordLine> lines)
        {
            double Last(string key) { var l = lines.LastOrDefault(x => x.type == "StateChanged" && x.S("key") == key); return l == null ? 0 : double.TryParse(l.S("value"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0; }
            return new Dictionary<string, object> { { "distanceMeters", Last("xr.move.distance") }, { "seconds", Last("xr.move.seconds") } };
        }
        static readonly string[] OrderKeys = { "loosen_order", "pretighten_order", "torque_order", "mark_order" };
        static readonly Dictionary<string, string> OrderNames = new Dictionary<string, string>
        {
            { "loosen_order", "預鬆螺栓順序" }, { "pretighten_order", "預鎖順序" }, { "torque_order", "鎖扭力順序" }, { "mark_order", "防鬆標記順序" }
        };
        static readonly HashSet<string> NoisyValueKeys = new HashSet<string> { "tool.torque_wrench.current", SimIds.PanHeadLowerDistance };

        public static Dictionary<string, object> Build(Dictionary<string, object> meta, IReadOnlyList<RecordLine> lines, bool rebuiltAfterCrash)
        {
            meta = meta ?? new Dictionary<string, object>();
            var cat = StepCatalog.FromMeta(meta);
            var resultLine = lines.LastOrDefault(l => l.type == "SessionResult");
            bool finished = resultLine != null;
            double end = lines.Count > 0 ? lines.Max(l => l.t) : 0;
            string mode = meta.Str("mode", "教學");

            var s = new Dictionary<string, object>
            {
                { "schemaVersion", RecordFiles.SchemaVersion }, { "trainee", meta.Str("trainee") }, { "mode", mode }, { "input", meta.Str("input") },
                { "scenarioId", meta.Str("scenarioId") }, { "scenarioTitle", meta.Str("scenarioTitle") }, { "startedAt", meta.Str("startedAt") },
                { "appVersion", meta.Str("appVersion") }, { "status", finished ? StatusDone : StatusInterrupted }, { "rebuilt", rebuiltAfterCrash },
                { "sessionSeconds", end }, { "lineCount", lines.Count },
            };
            if (finished)
            {
                var d = resultLine.data;
                s["result"] = new Dictionary<string, object>
                {
                    { "passed", d.Bool("passed") }, { "score", d.Num("score") }, { "totalSeconds", d.Num("totalSeconds") },
                    { "minor", (int)d.Num("minor") }, { "major", (int)d.Num("major") }, { "critical", (int)d.Num("critical") },
                    { "errorTotal", (int)(d.Num("minor") + d.Num("major") + d.Num("critical")) }, { "incompleteSteps", d.StrArr("incompleteSteps") }
                };
            }
            else s["result"] = null;

            s["steps"] = Steps(cat, lines, finished, end, mode);
            s["errors"] = lines.Where(l => l.type == "ErrorDetected").Select(l => (object)new Dictionary<string, object>
            {
                { "t", l.t }, { "step", l.step }, { "stepName", cat.NameOf(l.step) }, { "errorId", l.S("errorId") }, { "name", l.S("name", ErrorNames.Of(l.S("errorId"), l.S("what"))) },
                { "severity", l.S("severity") }, { "what", l.S("what") }, { "why", l.S("why") }, { "fix", l.S("fix") }
            }).ToList();
            var sev = lines.Where(l => l.type == "ErrorDetected").GroupBy(l => l.S("severity")).ToDictionary(g => g.Key, g => g.Count());
            s["errorEvents"] = new Dictionary<string, object>
            {
                { "minor", sev.TryGetValue("Minor", out var a) ? a : 0 }, { "major", sev.TryGetValue("Major", out var b) ? b : 0 }, { "critical", sev.TryGetValue("Critical", out var c) ? c : 0 }
            };
            var incomplete = ((List<object>)s["steps"]).OfType<Dictionary<string, object>>().Where(x => x.Str("status") == "未完成").Select(x => x.Str("name")).ToList();
            s["incompleteStepNames"] = finished
                ? ((Dictionary<string, object>)s["result"]).StrArr("incompleteSteps").Select(cat.NameOf).Cast<object>().ToList()
                : incomplete.Cast<object>().ToList();
            s["tools"] = Tools(meta, lines, end);
            s["smoothMove"] = SmoothMove(lines);   // R1 T6：右搖桿平滑移動總距離與總時間
            s["hints"] = Hints(cat, lines);
            // 卡關（防護網）：不扣分，報告列出
            s["stalls"] = lines.Where(l => l.type == "StepStallDetected").Select(l => (object)new Dictionary<string, object>
            {
                { "t", l.t }, { "step", l.step }, { "stepName", cat.NameOf(l.step) }, { "kind", l.S("kind") }, { "conditionIds", l.S("conditionIds") },
                { "values", l.S("values") }, { "explain", l.S("explain") }
            }).ToList();
            s["values"] = Values(cat, lines);
            s["order"] = Order(cat, lines, finished);
            s["videos"] = lines.Where(l => l.type == "VideoStarted").Select(l => (object)new Dictionary<string, object>
            {
                { "file", l.S("file") }, { "sessionTime", l.N("sessionTime", l.t) }
            }).ToList();
            s["counts"] = new Dictionary<string, object>
            {
                { "actions", lines.Count(l => l.type == "ActionRequested") }, { "blocked", lines.Count(l => l.type == "ActionBlocked") },
                { "measurements", lines.Count(l => l.type == "MeasurementTaken") }, { "uiCommands", lines.Count(l => l.type == "UiCommandIssued") }
            };
            return s;
        }

        static List<object> Steps(StepCatalog cat, IReadOnlyList<RecordLine> lines, bool finished, double end, string mode)
        {
            var list = new List<object>();
            foreach (var st in cat.steps)
            {
                var started = lines.Where(l => l.type == "StepStarted" && l.step == st.runId).ToList();
                var done = lines.LastOrDefault(l => l.type == "StepCompleted" && l.step == st.runId);
                string status;
                double seconds = 0;
                int redo = Math.Max(0, started.Count - 1);
                if (st.presetDone) status = "預設完成";
                else if (done != null) { status = redo > 0 ? "重做" : "完成"; seconds = done.N("seconds"); }
                else if (started.Count > 0) { status = "未完成"; seconds = end - started[0].t; }
                else status = finished ? "略過" : "未完成";
                float reference = Labels.RefSeconds(st, mode);
                bool over = reference > 0 && seconds > 2 * reference && !st.presetDone;
                list.Add(new Dictionary<string, object>
                {
                    { "runId", st.runId }, { "name", st.DisplayName }, { "order", st.order }, { "status", status }, { "seconds", seconds }, { "redoCount", redo },
                    { "refSeconds", reference }, { "over2x", over }, { "startedAt", started.Count > 0 ? (object)started[0].t : null }
                });
            }
            return list;
        }

        static List<object> Tools(Dictionary<string, object> meta, IReadOnlyList<RecordLine> lines, double end)
        {
            var names = new Dictionary<string, string>();
            foreach (var entry in meta.Str("tools").Split(';'))
            {
                var f = entry.Split('|');
                if (f.Length >= 3) names[f[1]] = f[2];
            }
            var bySlot = new SortedDictionary<int, Dictionary<string, object>>();
            foreach (var l in lines.Where(l => l.type == "ToolRackState"))
            {
                int slot = (int)l.N("slot");
                if (!bySlot.TryGetValue(slot, out var t))
                {
                    string id = l.S("tool");
                    bySlot[slot] = t = new Dictionary<string, object>
                    {
                        { "slot", slot }, { "tool", id }, { "name", names.TryGetValue(id, out var n) ? n : id },
                        { "picked", new List<object>() }, { "returned", new List<object>() }, { "deployed", new List<object>() }, { "last", "on_rack" }
                    };
                }
                string st = l.S("state");
                if (st == "in_hand") ((List<object>)t["picked"]).Add(l.t);
                else if (st == "on_rack") ((List<object>)t["returned"]).Add(l.t);
                else if (st == "in_use") ((List<object>)t["deployed"]).Add(l.t);
                t["last"] = st;
            }
            foreach (var t in bySlot.Values) t["leftover"] = t.Str("last") != "on_rack";
            return bySlot.Values.Cast<object>().ToList();
        }

        static Dictionary<string, object> Hints(StepCatalog cat, IReadOnlyList<RecordLine> lines)
        {
            var h = new Dictionary<string, object>();
            foreach (var name in UiCommands.All)
            {
                var used = lines.Where(l => l.type == "UiCommandIssued" && l.S("name") == name).ToList();
                h[name] = new Dictionary<string, object>
                {
                    { "count", used.Count },
                    { "steps", used.GroupBy(l => l.step).Select(g => (object)new Dictionary<string, object> { { "step", g.Key }, { "stepName", cat.NameOf(g.Key) }, { "count", g.Count() } }).ToList() }
                };
            }
            return h;
        }

        static List<object> Values(StepCatalog cat, IReadOnlyList<RecordLine> lines)
        {
            var list = new List<object>();
            var lastPerKey = new Dictionary<string, RecordLine>();
            foreach (var l in lines)
            {
                switch (l.type)
                {
                    case "MeasurementTaken":
                        list.Add(Row(cat, l, "讀數：" + TargetName(l.S("target")), l.S("target"), l.N("value").ToString("0.00"), "mm", "保養表值", "≤ 3.0 mm 更換", l.B("zeroed") ? "已歸零" : "未歸零"));
                        break;
                    case "Judgement":
                        list.Add(Row(cat, l, JudgementName(l.S("key")), l.S("key"), l.S("value"), "", l.S("key") == "workorder.decision" ? "保養表值" : "示例值",
                            l.S("key") == "workorder.decision" ? "任一點 ≤ 3.0 mm 兩條都換" : l.S("key") == "workorder.height_diff" ? "≤ 1.0 mm" : "", ""));
                        break;
                    case "LiftReleased":
                        list.Add(Row(cat, l, "放開升降鈕時檢測點距離", SimIds.PanHeadLowerDistance, SimIds.CmText((float)l.N("distanceCm")), "cm", "示例值", $"≤ {l.N("toleranceCm", 1):0.#} cm", ""));
                        break;
                    case "StateChanged":
                        if (l.data.Has("tag") && !NoisyValueKeys.Contains(l.S("key"))) lastPerKey[l.step + "|" + l.S("key")] = l;
                        break;
                }
            }
            foreach (var l in lastPerKey.Values.OrderBy(x => x.seq))
            {
                var rule = ValueTagRules.Find(l.S("key"));
                list.Add(Row(cat, l, (rule?.label ?? l.S("label")) + "：" + TargetName(l.S("key")), l.S("key"), l.S("value"), l.S("unit"), l.S("tag"), rule?.standard ?? "", "（該步驟結束時的值）"));
            }
            return list.OrderBy(x => ((Dictionary<string, object>)x).Num("t")).ToList();
        }

        static Dictionary<string, object> Row(StepCatalog cat, RecordLine l, string label, string key, string value, string unit, string tag, string standard, string note) =>
            new Dictionary<string, object>
            {
                { "t", l.t }, { "step", l.step }, { "stepName", cat.NameOf(l.step) }, { "label", label }, { "key", key }, { "value", value ?? "" },
                { "unit", unit ?? "" }, { "tag", tag ?? "" }, { "standard", standard ?? "" }, { "note", note ?? "" }
            };

        static string JudgementName(string key)
        {
            if (key == "workorder.decision") return "判定（作業單）";
            if (key == "workorder.height_diff") return "新滑板高低差";
            if (key == "workorder.last_error") return "作業單提示";
            if (key.EndsWith(".mismatch")) return "作業單抄寫比對：" + key.Replace("workorder.", "").Replace(".mismatch", "");
            return key;
        }

        /// <summary>ip.carbon_strip_L.bolt_3.torque_Nm → 肘側螺栓 3。</summary>
        public static string TargetName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string side = SimIds.SideOf(id);
            string sideName = side == "L" ? "肘側" : side == "R" ? "外側" : "";
            int b = id.IndexOf(".bolt_", StringComparison.Ordinal);
            if (b >= 0) { var rest = id.Substring(b + 6); int dot = rest.IndexOf('.'); return $"{sideName}螺栓 {(dot >= 0 ? rest.Substring(0, dot) : rest)}"; }
            int m = id.IndexOf(".measure_", StringComparison.Ordinal);
            if (m >= 0) { var p = id.Substring(m + 9).Split('.')[0]; return $"{sideName}滑板 {(p == "left" ? "左" : p == "mid" ? "中" : p == "right" ? "右" : p)}"; }
            if (id.StartsWith("env.spare_shelf.slot_")) return "備品 " + id.Substring("env.spare_shelf.slot_".Length);
            if (id.StartsWith("tool.torque_wrench")) return "扭力扳手";
            if (id.StartsWith("tool.socket_wrench")) return "套筒扳手";
            if (id.StartsWith("tool.caliper")) return "卡尺";
            if (id.Contains(".bracket")) return sideName + "托架";
            return id;
        }

        static List<object> Order(StepCatalog cat, IReadOnlyList<RecordLine> lines, bool finished)
        {
            var diffs = new List<object>();
            void Add(double t, string step, string what, string standard, string actual) =>
                diffs.Add(new Dictionary<string, object> { { "t", t }, { "step", step }, { "stepName", cat.NameOf(step) }, { "what", what }, { "standard", standard }, { "actual", actual } });

            // 步驟：重做、略過
            var seen = new HashSet<string>();
            foreach (var l in lines.Where(l => l.type == "StepStarted"))
                if (!seen.Add(l.step)) Add(l.t, l.step, "從這一步重做", "每步做一次", "重新開始這一步");
            if (finished)
                foreach (var st in cat.steps.Where(x => !x.presetDone && !lines.Any(l => l.type == "StepStarted" && l.step == x.runId)))
                    Add(0, st.runId, "略過這一步", "照標準順序執行", "判定合格後跳到收尾，沒有執行");

            // 小動作：退回上一步
            foreach (var l in lines.Where(l => l.type == "SubActionUndone"))
                Add(l.t, l.step, "退回上一個小動作", "依序完成小動作", $"退回到第 {l.N("index"):0} 個：{l.S("text")}");

            // 螺栓順序：1→2→3→4
            foreach (var side in new[] { "L", "R" })
                foreach (var key in OrderKeys)
                {
                    string full = $"ip.carbon_strip_{side}.{key}";
                    var last = lines.LastOrDefault(l => l.type == "StateChanged" && l.S("key") == full);
                    if (last == null) continue;
                    string v = last.S("value");
                    if (v != "1,2,3,4" && v.Length > 0 && v.Split(',').Length >= 2 && !"1,2,3,4".StartsWith(v))
                        Add(last.t, last.step, $"{(side == "L" ? "肘側" : "外側")}{OrderNames[key]}", "1→2→3→4", v.Replace(",", "→"));
                }

            // 接地：先夾平台端子再掛弓頭；拆除相反
            var ground = lines.Where(l => (l.type == "ActionRequested" || l.type == "ActionBlocked") && l.S("tool") == SimIds.ToolGroundingRod
                                          && (l.S("target") == SimIds.GroundTerminal || l.S("target") == SimIds.GroundClamp)).ToList();
            // 被擋下的「先掛弓頭」也算嘗試（E3 就是被擋下的）
            var firstHead = ground.FirstOrDefault(l => l.S("target") == SimIds.GroundClamp);
            var firstTerm = ground.FirstOrDefault(l => l.S("target") == SimIds.GroundTerminal && l.type == "ActionRequested");
            if (firstHead != null && (firstTerm == null || firstHead.seq < firstTerm.seq))
                Add(firstHead.t, firstHead.step, "接地棒先掛弓頭", "先夾平台接地端子 → 拉線確認 → 掛弓頭", "先掛弓頭");
            foreach (var l in lines.Where(l => l.type == "ActionBlocked" && l.S("rule") == "ERR_Ground_RemoveOrder"))
                Add(l.t, l.step, "拆接地順序", "先拆弓頭端 → 再拆平台端子", "先拆平台端子");
            return diffs.OrderBy(x => ((Dictionary<string, object>)x).Num("t")).ToList();
        }
    }
}
