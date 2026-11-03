using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Recording
{
    /// <summary>
    /// 重播的時間軸（純 C#，EditMode 可測）：依時間把紀錄套回黑板、重新發出介面需要的事件（步驟、錯誤視窗、提示內容，全部 isReplay=true），
    /// 並提供 ReplayTracked 物件與學員頭手的位姿。
    /// 拖曳（Seek）＝從最近的快照開始往後套用；連續播放經過快照時也套用快照，所以兩種方式在同一時間點的黑板內容相同。
    /// </summary>
    public class ReplayTimeline
    {
        /// <summary>
        /// 套用快照時不動的鍵：重播自己的狀態、紀錄服務、觀看者自己的 VR 狀態、結算結果（總分、時間、是否通過——重播不產生分數；
        /// 錯誤次數 score.minor/major/critical 照常重播）。
        /// </summary>
        public static bool ViewerOwnedKey(string key) =>
            key.StartsWith("replay.") || key.StartsWith("rec.") || key.StartsWith("xr.") || key == "ui.hover_name" || key == "ui.hover_id" || key == "ui.toast"
            || key == "score.finished" || key == "score.total" || key == "score.elapsed" || key == "score.passed" || key == "score.start_time"
            || key == SimIds.RackSlots;   // 審查建議：掛位清單是這個版本的 ToolSlotTable（觀看者的 ToolBelt 開場寫），舊錄影沒有這個鍵也照常顯示標籤、清點

        public readonly Dictionary<string, object> meta;
        public readonly List<RecordLine> lines;
        public readonly List<Snapshot> snapshots;
        public readonly Dictionary<string, List<PoseSample>> poses;
        public readonly StepCatalog catalog;

        IEventBus bus;
        ISimState state;
        int cursor;              // 下一個要套用的行
        int snapCursor;          // 下一個要套用的快照
        RecordLine lastStepStarted;

        public double Duration { get; }
        public double Time { get; private set; }
        public int AppliedLines => cursor;

        public ReplayTimeline(Dictionary<string, object> meta, List<RecordLine> lines, List<Snapshot> snapshots, Dictionary<string, List<PoseSample>> poses)
        {
            this.meta = meta ?? new Dictionary<string, object>();
            this.lines = (lines ?? new List<RecordLine>()).OrderBy(l => l.seq).ToList();
            this.snapshots = (snapshots ?? new List<Snapshot>()).OrderBy(s => s.lastSeq).ThenBy(s => s.t).ToList();
            this.poses = poses ?? new Dictionary<string, List<PoseSample>>();
            catalog = StepCatalog.FromMeta(this.meta);
            double end = this.lines.Count > 0 ? this.lines.Max(l => l.t) : 0;
            foreach (var p in this.poses.Values) if (p.Count > 0) end = Math.Max(end, p[p.Count - 1].t);
            Duration = end;
        }

        public static ReplayTimeline Load(string folder) => new ReplayTimeline(
            RecordingIndex.ReadJson(Path.Combine(folder, RecordFiles.Meta)),
            RecordLine.ReadAll(Path.Combine(folder, RecordFiles.Session), out _),
            SnapshotFile.Read(Path.Combine(folder, RecordFiles.Snapshots)),
            PoseFile.Read(Path.Combine(folder, RecordFiles.Poses)));

        public void Attach(IEventBus b, ISimState s) { bus = b; state = s; }

        // ---------- 時間軸標記 ----------
        public List<RecordLine> Errors => lines.Where(l => l.type == "ErrorDetected").ToList();

        /// <summary>R1 T3：到時間 t 為止每塊面板最後一次的位置（"站點|x,y,z,yaw"）；schema 2 以前的檔沒有 PanelMoved，回傳空的。</summary>
        List<RecordLine> panelLines;
        public Dictionary<string, string> PanelPosesAt(double t)
        {
            var map = new Dictionary<string, string>();
            if (panelLines == null) panelLines = lines.Where(l => l.type == "PanelMoved").ToList();   // 每幀呼叫：只掃 PanelMoved
            foreach (var l in panelLines)
            {
                if (l.t > t) break;
                string F(string k) => l.data.Num(k).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
                map[l.data.Str("panelId")] = l.data.Str("standPointId") + "|" + F("x") + "," + F("y") + "," + F("z") + "," + F("yaw");
            }
            return map;
        }
        /// <summary>每一步第一次開始的時間。</summary>
        public List<RecordLine> StepStarts => lines.Where(l => l.type == "StepStarted").GroupBy(l => l.step).Select(g => g.First()).ToList();

        // ---------- 播放 ----------
        /// <summary>往後播到 t（t 比目前早就改用 Seek）。經過的快照也會套用。</summary>
        public void AdvanceTo(double t)
        {
            if (t < Time - 1e-6) { Seek(t); return; }
            t = Math.Min(Math.Max(0, t), Duration);
            while (true)
            {
                // 下一筆是快照還是紀錄行：快照的 lastSeq 之前的行先套用
                var snap = snapCursor < snapshots.Count ? snapshots[snapCursor] : null;
                var line = cursor < lines.Count ? lines[cursor] : null;
                if (snap != null && snap.t <= t + 1e-6 && (line == null || line.seq > snap.lastSeq))
                {
                    ApplySnapshot(snap);
                    snapCursor++;
                    continue;
                }
                if (line == null || line.t > t + 1e-6) break;
                cursor = ApplyGroup(cursor);
            }
            Time = t;
        }

        /// <summary>跳到 t：從 t 之前最近的快照開始，往後套用到 t。</summary>
        public void Seek(double t)
        {
            t = Math.Min(Math.Max(0, t), Duration);
            int si = -1;
            for (int i = 0; i < snapshots.Count; i++) if (snapshots[i].t <= t + 1e-6) si = i;
            if (si >= 0)
            {
                var snap = snapshots[si];
                snapCursor = si + 1;
                cursor = lines.FindIndex(l => l.seq > snap.lastSeq);
                if (cursor < 0) cursor = lines.Count;
                // 介面要知道目前在哪一步：先重發快照前最後一個 StepStarted，再套快照——
                // 與連續播放相同（StepStarted 在快照之前），提示服務因 StepStarted 寫的鍵由快照決定
                var step = lines.Take(cursor).LastOrDefault(l => l.type == "StepStarted");
                lastStepStarted = step;
                if (step != null) Publish(step, false);
                ApplySnapshot(snap);
            }
            else { cursor = 0; snapCursor = 0; lastStepStarted = null; }
            Time = snapshots.Count > 0 && si >= 0 ? snapshots[si].t : 0;
            // 從快照之後往前套用到 t（不再套用中間的快照以外的資料）
            while (cursor < lines.Count && lines[cursor].t <= t + 1e-6)
            {
                var snap = snapCursor < snapshots.Count ? snapshots[snapCursor] : null;
                if (snap != null && snap.t <= t + 1e-6 && lines[cursor].seq > snap.lastSeq) { ApplySnapshot(snap); snapCursor++; continue; }
                cursor = ApplyGroup(cursor);
            }
            while (snapCursor < snapshots.Count && snapshots[snapCursor].t <= t + 1e-6 && (cursor >= lines.Count || lines[cursor].seq > snapshots[snapCursor].lastSeq))
            {
                ApplySnapshot(snapshots[snapCursor]);
                snapCursor++;
            }
            Time = t;
        }

        /// <summary>把快照的黑板寫回（觀看者自己的鍵保留）。用 Restore：先全部寫入再通知，提示服務讀到的是完整狀態。</summary>
        void ApplySnapshot(Snapshot snap)
        {
            if (state == null) return;
            var merged = new Dictionary<string, string>();
            foreach (var kv in snap.state) if (kv.Value != null && !ViewerOwnedKey(kv.Key)) merged[kv.Key] = kv.Value;
            foreach (var key in state.Keys.ToList()) if (ViewerOwnedKey(key)) merged[key] = state.Get(key);
            state.Restore(merged);
        }

        /// <summary>套用第 i 行；與它同 seq 的行（錄製時的同一批黑板變更）一起套成一批。回傳下一個要套用的行。</summary>
        int ApplyGroup(int i)
        {
            int j = i + 1;
            while (j < lines.Count && lines[j].seq == lines[i].seq) j++;
            if (j - i == 1 || state == null) { Apply(lines[i]); return j; }
            using (state.BeginBatch())
                for (int k = i; k < j; k++) Apply(lines[k]);
            return j;
        }

        void Apply(RecordLine l)
        {
            if (state == null) return;
            switch (l.type)
            {
                case "StateChanged":
                case "ScoreChanged":
                case "Judgement":
                {
                    string key = l.S("key");
                    if (key.Length == 0 || ViewerOwnedKey(key)) break;
                    state.Set(key, l.data.TryGetValue("value", out var v) && v != null ? (v is string s ? s : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)) : null);
                    break;
                }
                case "ToolRackState":
                    state.Set("rack.slot_" + (int)l.N("slot"), l.S("state"));
                    break;
                case "StepStarted":
                    lastStepStarted = l;
                    Publish(l, true);
                    break;
                case "SubActionCompleted":
                    state.SetFloat("procedure.sub_index", (float)l.N("index"));
                    break;
                case "SubActionUndone":
                    state.SetFloat("procedure.sub_index", (float)l.N("index") - 1);
                    break;
                case "StepCompleted":
                    state.SetBool("step." + l.step + ".done", true);
                    bus?.Publish(new StepCompleted { stepId = l.step, side = l.S("side"), time = SimClock.Now, isReplay = true });
                    break;
                case "ErrorDetected":
                    state.Set("error.last", l.S("errorId"));
                    bus?.Publish(new ErrorDetected
                    {
                        errorId = l.S("errorId"), stepId = l.step, what = l.S("what"), why = l.S("why"), fix = l.S("fix"),
                        severity = Enum.TryParse<Severity>(l.S("severity"), out var sev) ? sev : Severity.Minor, blocks = l.B("blocks"),
                        targetId = l.S("target"), time = SimClock.Now, isReplay = true
                    });
                    break;
                case "ProcedureCompleted":
                    state.SetBool("procedure.finished", true);
                    break;
            }
        }

        /// <summary>重發 StepStarted。fresh＝這一步剛開始（同時把 procedure.* 設成開始時的值，與 ProcedureRunner 相同）。</summary>
        void Publish(RecordLine l, bool fresh)
        {
            var d = l.data;
            var subs = d.Str("subActions").Split('\n').Where(x => x.Length > 0).ToArray();
            if (fresh)
            {
                state.Set("procedure.current", l.step);
                state.Set("procedure.side", d.Str("side"));
                string label = d.Str("sideLabel");
                state.Set("procedure.current_title", string.IsNullOrEmpty(label) ? d.Str("title") : d.Str("title") + "（" + label + "）");
                state.SetFloat("procedure.sub_index", 0);
                state.SetFloat("procedure.sub_count", subs.Length);
            }
            bus?.Publish(new StepStarted
            {
                stepId = l.step, title = d.Str("title"), instruction = d.Str("instruction"), toolHint = d.Str("toolHint"), targetHint = d.Str("targetHint"),
                toolId = d.Str("toolId"), side = d.Str("side"), sideLabel = d.Str("sideLabel"),
                index = (int)d.Num("index"), total = (int)d.Num("total"), runIndex = (int)d.Num("runIndex"), runTotal = (int)d.Num("runTotal"),
                targetIds = d.StrArr("targetIds"), text = d.Str("text"), why = d.Str("why"), checklist = d.Str("checklist"), subActions = d.Str("subActions"),
                valueTags = d.Str("valueTags"), highlightIds = d.Str("highlightIds"), showBoltNumbers = d.Bool("showBoltNumbers"), showOutwardArrow = d.Bool("showOutwardArrow"),
                time = SimClock.Now, isReplay = true
            });
        }

        // ---------- 位姿 ----------
        /// <summary>某 ID 在 t 的位姿：取 t 之前最後一筆；下一筆在 0.1 s 內就內插（靜止的物件不會被內插成慢慢移動）。</summary>
        public bool TryGetPose(string id, double t, out PoseSample pose)
        {
            pose = default;
            if (!poses.TryGetValue(id, out var list) || list.Count == 0) return false;
            int i = LastIndexAtOrBefore(list, (float)t);
            if (i < 0) { pose = list[0]; return true; }
            pose = list[i];
            if (i + 1 < list.Count)
            {
                var b = list[i + 1];
                float gap = b.t - pose.t;
                if (gap > 1e-5f && gap <= 0.1f)
                {
                    float u = Mathf.Clamp01(((float)t - pose.t) / gap);
                    pose.pos = Vector3.Lerp(pose.pos, b.pos, u);
                    pose.rot = Quaternion.Slerp(pose.rot, b.rot, u);
                    pose.value = Mathf.Lerp(pose.value, b.value, u);
                }
            }
            return true;
        }

        /// <summary>數值通道（例 lift.value）：線性內插（跟著機構連續變化）。</summary>
        public bool TryGetValue(string id, double t, out float value)
        {
            value = 0f;
            if (!poses.TryGetValue(id, out var list) || list.Count == 0) return false;
            int i = LastIndexAtOrBefore(list, (float)t);
            if (i < 0) { value = list[0].value; return true; }
            value = list[i].value;
            if (i + 1 < list.Count && list[i + 1].t - list[i].t <= 0.25f)
                value = Mathf.Lerp(list[i].value, list[i + 1].value, Mathf.Clamp01(((float)t - list[i].t) / Mathf.Max(1e-5f, list[i + 1].t - list[i].t)));
            return true;
        }

        public IEnumerable<string> TrackedIds => poses.Keys.Where(k => !k.StartsWith("@") && k != RecordFiles.LiftChannel);

        static int LastIndexAtOrBefore(List<PoseSample> list, float t)
        {
            int lo = 0, hi = list.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (list[mid].t <= t) { best = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return best;
        }

        /// <summary>快照裡的物件位置（物件從沒移動、位姿檔沒有它時用）。</summary>
        public bool TryGetSnapshotPose(string id, double t, out PoseSample pose)
        {
            pose = default;
            for (int i = snapshots.Count - 1; i >= 0; i--)
                if (snapshots[i].t <= t + 1e-6 && snapshots[i].tracked.TryGetValue(id, out pose)) return true;
            return snapshots.Count > 0 && snapshots[0].tracked.TryGetValue(id, out pose);
        }
    }
}
