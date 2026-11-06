using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PantographSim.Recording
{
    /// <summary>紀錄根目錄的位置（專案根目錄下的 Recordings/，可在 RecordingSettings 改）。</summary>
    public static class RecordingPaths
    {
        /// <summary>專案根目錄（Editor：Assets 的上一層；執行檔：exe 所在資料夾）。</summary>
        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>設定值可以是相對路徑（相對專案根目錄）或絕對路徑。</summary>
        public static string Resolve(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured)) configured = "Recordings";
            return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(ProjectRoot, configured));
        }

        /// <summary>資料夾名：yyyyMMdd_HHmmss_學員（去掉檔名不能用的字元）。</summary>
        public static string FolderName(DateTime when, string trainee)
        {
            var bad = Path.GetInvalidFileNameChars();
            var name = new string((trainee ?? "").Select(c => bad.Contains(c) || c == ' ' ? '_' : c).ToArray()).Trim('_', '.');
            if (name.Length == 0) name = "學員";
            if (name.Length > 40) name = name.Substring(0, 40);
            return when.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + name;
        }

        /// <summary>學員名稱留空時的預設：「學員」＋日期時間。</summary>
        public static string DefaultTrainee(DateTime when) => "學員" + when.ToString("MMdd_HHmm", CultureInfo.InvariantCulture);
    }

    /// <summary>紀錄清單的一筆。</summary>
    public class RecordEntry
    {
        public string folder = "", name = "", trainee = "", mode = "", input = "", startedAt = "", status = "";
        public bool interrupted, finished, passed, hiddenUntilFinished;
        public double score = double.NaN, totalSeconds, critical, errorTotal;
        public DateTime sortKey;
        public List<string> videos = new List<string>();
    }

    /// <summary>
    /// 掃描紀錄根目錄：讀每筆的 meta.json／summary.json；沒有 summary.json 的（中斷）就從 session.jsonl 重建摘要，標成「未完成（中斷）」。
    /// 考核模式還沒結束的紀錄不出現在清單。曾經用過的學員名稱存在 names.json。
    /// </summary>
    public static class RecordingIndex
    {
        public const string NamesFile = "names.json";

        public static Dictionary<string, object> ReadJson(string path)
        {
            try { return File.Exists(path) ? Json.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object> : null; }
            catch (Exception) { return null; }
        }

        /// <summary>從 session.jsonl 重建摘要並寫入 summary.json（標成中斷）。回傳摘要。</summary>
        public static Dictionary<string, object> RebuildSummary(string folder)
        {
            var meta = ReadJson(Path.Combine(folder, RecordFiles.Meta)) ?? new Dictionary<string, object>();
            var lines = RecordLine.ReadAll(Path.Combine(folder, RecordFiles.Session), out int bad);
            var summary = SummaryBuilder.Build(meta, lines, true);
            summary["badLines"] = bad;
            RecordingWriter.WriteAtomic(Path.Combine(folder, RecordFiles.Summary), Json.Write(summary));
            return summary;
        }

        /// <summary>啟動時呼叫：找出沒有 summary.json 的資料夾（不含正在寫的那一筆）並重建。回傳重建了幾筆。</summary>
        public static int RepairInterrupted(string root, string skipFolder = null)
        {
            if (!Directory.Exists(root)) return 0;
            int n = 0;
            foreach (var dir in Directory.GetDirectories(root))
            {
                if (skipFolder != null && Path.GetFullPath(dir) == Path.GetFullPath(skipFolder)) continue;
                if (!File.Exists(Path.Combine(dir, RecordFiles.Session)) || File.Exists(Path.Combine(dir, RecordFiles.Summary))) continue;
                try { RebuildSummary(dir); n++; }
                catch (Exception e) { Debug.LogWarning($"[Recording] 重建摘要失敗 {dir}：{e.Message}"); }
            }
            return n;
        }

        /// <summary>由新到舊的紀錄清單（考核未結束的不列出）。</summary>
        public static List<RecordEntry> List(string root, string activeFolder = null)
        {
            var list = new List<RecordEntry>();
            if (!Directory.Exists(root)) return list;
            foreach (var dir in Directory.GetDirectories(root))
            {
                var meta = ReadJson(Path.Combine(dir, RecordFiles.Meta));
                if (meta == null) continue;
                var summary = ReadJson(Path.Combine(dir, RecordFiles.Summary));
                bool active = activeFolder != null && Path.GetFullPath(dir) == Path.GetFullPath(activeFolder);
                var e = new RecordEntry
                {
                    folder = Path.GetFullPath(dir), name = Path.GetFileName(dir), trainee = meta.Str("trainee"), mode = meta.Str("mode"), input = meta.Str("input"),
                    startedAt = meta.Str("startedAt"), hiddenUntilFinished = meta.Str("mode") == "考核"
                };
                DateTime.TryParse(e.startedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out e.sortKey);
                if (e.sortKey == default) e.sortKey = Directory.GetCreationTime(dir);
                var result = summary?.Obj("result");
                e.finished = result != null;
                e.interrupted = !active && (summary == null || summary.Str("status") == SummaryBuilder.StatusInterrupted);
                e.status = active ? "紀錄中" : e.finished ? "完成" : SummaryBuilder.StatusInterrupted;
                if (result != null)
                {
                    e.score = result.Num("score"); e.passed = result.Bool("passed"); e.totalSeconds = result.Num("totalSeconds");
                    e.critical = result.Num("critical"); e.errorTotal = result.Num("errorTotal");
                }
                else if (summary != null) e.totalSeconds = summary.Num("sessionSeconds");
                foreach (var v in Directory.GetFiles(dir, "video_*.mp4").OrderBy(x => x)) e.videos.Add(Path.GetFileName(v));
                // 考核模式：考核結束（有結算）才顯示
                if (e.hiddenUntilFinished && !e.finished) continue;
                list.Add(e);
            }
            return list.OrderByDescending(x => x.sortKey).ThenByDescending(x => x.name).ToList();
        }

        // ---------- 學員名稱清單 ----------
        public static List<string> LoadNames(string root)
        {
            var d = ReadJson(Path.Combine(root, NamesFile));
            string driver = PantographSim.Data.ToolRulesDef.Load().testDriverTag;   // 跳步工具的驗收名不是學員，舊檔裡有也不沿用
            return d != null ? d.StrArr("names").Where(x => !string.IsNullOrWhiteSpace(x) && x != driver).ToList() : new List<string>();
        }

        /// <summary>把名稱放到清單最前面（上次用的就是第一個）。</summary>
        public static void RememberName(string root, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name == PantographSim.Data.ToolRulesDef.Load().testDriverTag) return;
            var names = LoadNames(root);
            names.Remove(name);
            names.Insert(0, name);
            if (names.Count > 30) names.RemoveRange(30, names.Count - 30);
            Directory.CreateDirectory(root);
            RecordingWriter.WriteAtomic(Path.Combine(root, NamesFile), Json.Write(new Dictionary<string, object> { { "names", names } }));
        }
    }
}
