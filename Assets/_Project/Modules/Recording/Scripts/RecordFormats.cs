using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PantographSim.Recording
{
    /// <summary>紀錄資料夾內的檔名與版本。</summary>
    public static class RecordFiles
    {
        /// <summary>
        /// 2：位置檢測距離（*_distance_cm、*_delta_cm）改存原始值（不再四捨五入到 0.1 cm），數字用 "R" 格式寫出、讀回不變；
        /// 同一批黑板變更共用同一個 seq。重播只套用記錄下來的值、不重新計算，所以 1 與 2 都能重播。
        /// </summary>
        public const int SchemaVersion = 3;   // 3：新增 PanelMoved（R1 T3）；2 以前的檔照舊能讀、能重播
        public const string Meta = "meta.json";

        /// <summary>terry 2026-11-14（32A）：流程版本（meta.json 的 flowVersion）。1＝有「螺栓倒進盤子／拿回袋子」的流程；沒有這欄的是舊版流程。</summary>
        public const int FlowVersion = 1;
        public const string OldFlowLabel = "舊版流程";
        public static bool IsOldFlow(Dictionary<string, object> meta) => meta == null || !meta.ContainsKey("flowVersion");

        /// <summary>R1.5b b4：錄影的課程（meta.json 的 courseId；舊錄影沒有＝r1）。</summary>
        public static string CourseOf(string folder)
        {
            try
            {
                var path = System.IO.Path.Combine(folder ?? "", Meta);
                if (System.IO.File.Exists(path) && Json.Parse(System.IO.File.ReadAllText(path)) is System.Collections.Generic.Dictionary<string, object> m)
                {
                    var id = m.Str("courseId");
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
            catch (System.Exception e) { UnityEngine.Debug.LogWarning("[Recording] 讀不到 meta.json 的 courseId：" + e.Message); }
            return PantographSim.Core.SimIds.CourseR1;
        }
        public const string Session = "session.jsonl";
        public const string Poses = "poses.bin";
        public const string Snapshots = "snapshots.bin";
        public const string Summary = "summary.json";
        public const string Report = "report.html";
        public static readonly string[] Csvs = { "steps.csv", "errors.csv", "tools.csv", "values.csv", "hints.csv" };

        /// <summary>學員頭與雙手在位姿檔裡的 ID。</summary>
        public const string Head = "@head", LeftHand = "@left", RightHand = "@right";
        /// <summary>升降機構的位置（0–1），重播時寫回黑板 lift.value 讓機構跟著動。</summary>
        public const string LiftChannel = "lift.value";
    }

    /// <summary>session.jsonl 的一行：{"t":秒,"seq":序號,"type":"…","step":"STP_…","data":{…}}。</summary>
    public class RecordLine
    {
        public double t;
        public long seq;
        public string type = "";
        public string step = "";
        public Dictionary<string, object> data = new Dictionary<string, object>();

        public string ToJson()
        {
            var sb = new StringBuilder(160);
            sb.Append("{\"t\":").Append(Math.Round(t, 3).ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(",\"seq\":").Append(seq.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"type\":"); Json.Write(sb, type);
            sb.Append(",\"step\":"); Json.Write(sb, step ?? "");
            sb.Append(",\"data\":"); Json.Write(sb, data);
            sb.Append('}');
            return sb.ToString();
        }

        public static bool TryParse(string line, out RecordLine r)
        {
            r = null;
            if (string.IsNullOrWhiteSpace(line) || !Json.TryParse(line, out var o) || !(o is Dictionary<string, object> d)) return false;
            r = new RecordLine { t = d.Num("t"), seq = (long)d.Num("seq"), type = d.Str("type"), step = d.Str("step"), data = d.Obj("data") ?? new Dictionary<string, object>() };
            return r.type.Length > 0;
        }

        public string S(string key, string fallback = "") => data.Str(key, fallback);
        public double N(string key, double fallback = 0) => data.Num(key, fallback);
        public bool B(string key) => data.Bool(key);

        /// <summary>讀整個 session.jsonl；壞掉的行（例如被截斷在一半的最後一行）略過，badLines 回報略過幾行。</summary>
        public static List<RecordLine> ReadAll(string path, out int badLines)
        {
            var list = new List<RecordLine>();
            badLines = 0;
            if (!File.Exists(path)) return list;
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                text = sr.ReadToEnd();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                if (TryParse(line, out var r)) list.Add(r); else badLines++;
            }
            // 穩定排序：同一批黑板變更共用一個 seq，保留檔案中的先後
            return list.Select((l, i) => (l, i)).OrderBy(x => x.l.seq).ThenBy(x => x.i).Select(x => x.l).ToList();
        }
    }

    /// <summary>一筆位姿樣本（ReplayTracked 物件、頭、手），或一個數值通道（例 lift.value）。</summary>
    public struct PoseSample
    {
        public float t;
        public Vector3 pos;
        public Quaternion rot;
        public float value;
        public bool active;
    }

    /// <summary>
    /// poses.bin：檔頭 "PSPB" + int 版本，之後一連串紀錄（每筆以 byte 種類開頭）：
    /// 0＝定義 ID（ushort 編號, string）；1＝位姿（ushort, float t, 3 float 位置, 4 float 旋轉, byte 啟用）；2＝數值（ushort, float t, float 值）。
    /// 讀取時遇到不完整的最後一筆就停止（中斷也讀得到前面的部分）。
    /// </summary>
    public static class PoseFile
    {
        public const string Magic = "PSPB";
        public const int Version = 1;

        public static void WriteHeader(BinaryWriter w) { w.Write(Encoding.ASCII.GetBytes(Magic)); w.Write(Version); }
        public static void WriteDefine(BinaryWriter w, ushort index, string id) { w.Write((byte)0); w.Write(index); w.Write(id ?? ""); }

        public static void WritePose(BinaryWriter w, ushort index, float t, Vector3 p, Quaternion r, bool active)
        {
            w.Write((byte)1); w.Write(index); w.Write(t);
            w.Write(p.x); w.Write(p.y); w.Write(p.z);
            w.Write(r.x); w.Write(r.y); w.Write(r.z); w.Write(r.w);
            w.Write((byte)(active ? 1 : 0));
        }

        public static void WriteValue(BinaryWriter w, ushort index, float t, float v) { w.Write((byte)2); w.Write(index); w.Write(t); w.Write(v); }

        /// <summary>讀出每個 ID 的樣本（依時間排序）。</summary>
        public static Dictionary<string, List<PoseSample>> Read(string path)
        {
            var tracks = new Dictionary<string, List<PoseSample>>();
            if (!File.Exists(path)) return tracks;
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[fs.Length];
                int read = 0;
                while (read < bytes.Length) { int n = fs.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
            }
            var names = new Dictionary<ushort, string>();
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                try
                {
                    if (Encoding.ASCII.GetString(r.ReadBytes(4)) != Magic) return tracks;
                    r.ReadInt32();
                    while (r.BaseStream.Position < r.BaseStream.Length)
                    {
                        byte kind = r.ReadByte();
                        ushort idx = r.ReadUInt16();
                        if (kind == 0) { names[idx] = r.ReadString(); continue; }
                        float t = r.ReadSingle();
                        var s = new PoseSample { t = t, rot = Quaternion.identity, active = true };
                        if (kind == 1)
                        {
                            s.pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                            s.rot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                            s.active = r.ReadByte() != 0;
                        }
                        else if (kind == 2) s.value = r.ReadSingle();
                        else break;   // 不認識的種類：檔案壞了，停在這裡
                        if (!names.TryGetValue(idx, out var id)) continue;
                        if (!tracks.TryGetValue(id, out var list)) tracks[id] = list = new List<PoseSample>();
                        list.Add(s);
                    }
                }
                catch (EndOfStreamException) { }   // 最後一筆不完整（中斷）
            }
            foreach (var l in tracks.Values) l.Sort((a, b) => a.t.CompareTo(b.t));
            return tracks;
        }
    }

    /// <summary>一筆全場快照：時間、快照前最後一行紀錄的序號、黑板全量、所有 ReplayTracked 的位置。</summary>
    public class Snapshot
    {
        public float t;
        public long lastSeq;
        public Dictionary<string, string> state = new Dictionary<string, string>();
        public Dictionary<string, PoseSample> tracked = new Dictionary<string, PoseSample>();
    }

    /// <summary>
    /// snapshots.bin：檔頭 "PSSN" + int 版本；每筆快照前面是 int 位元組長度（讀到長度不足就停止）。
    /// 內容：float t, long lastSeq, int 鍵數 {string 鍵, bool 有值, string 值}, int 物件數 {string ID, 3 float, 4 float, byte 啟用}。
    /// </summary>
    public static class SnapshotFile
    {
        public const string Magic = "PSSN";
        public const int Version = 1;

        public static void WriteHeader(BinaryWriter w) { w.Write(Encoding.ASCII.GetBytes(Magic)); w.Write(Version); }

        public static byte[] Encode(Snapshot s)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(s.t); w.Write(s.lastSeq);
                w.Write(s.state.Count);
                foreach (var kv in s.state) { w.Write(kv.Key ?? ""); w.Write(kv.Value != null); if (kv.Value != null) w.Write(kv.Value); }
                w.Write(s.tracked.Count);
                foreach (var kv in s.tracked)
                {
                    w.Write(kv.Key ?? "");
                    var p = kv.Value;
                    w.Write(p.pos.x); w.Write(p.pos.y); w.Write(p.pos.z);
                    w.Write(p.rot.x); w.Write(p.rot.y); w.Write(p.rot.z); w.Write(p.rot.w);
                    w.Write((byte)(p.active ? 1 : 0));
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        public static void WriteRecord(BinaryWriter w, Snapshot s)
        {
            var body = Encode(s);
            w.Write(body.Length);
            w.Write(body);
        }

        public static List<Snapshot> Read(string path)
        {
            var list = new List<Snapshot>();
            if (!File.Exists(path)) return list;
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[fs.Length];
                int read = 0;
                while (read < bytes.Length) { int n = fs.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
            }
            using (var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
            {
                try
                {
                    if (Encoding.ASCII.GetString(r.ReadBytes(4)) != Magic) return list;
                    r.ReadInt32();
                    while (r.BaseStream.Position + 4 <= r.BaseStream.Length)
                    {
                        int len = r.ReadInt32();
                        if (len <= 0 || r.BaseStream.Position + len > r.BaseStream.Length) break;
                        var body = r.ReadBytes(len);
                        using (var br = new BinaryReader(new MemoryStream(body), Encoding.UTF8))
                        {
                            var s = new Snapshot { t = br.ReadSingle(), lastSeq = br.ReadInt64() };
                            int n = br.ReadInt32();
                            for (int i = 0; i < n; i++) { string k = br.ReadString(); bool has = br.ReadBoolean(); s.state[k] = has ? br.ReadString() : null; }
                            int m = br.ReadInt32();
                            for (int i = 0; i < m; i++)
                            {
                                string id = br.ReadString();
                                var p = new PoseSample
                                {
                                    t = s.t,
                                    pos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                                    rot = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle())
                                };
                                p.active = br.ReadByte() != 0;
                                s.tracked[id] = p;
                            }
                            list.Add(s);
                        }
                    }
                }
                catch (EndOfStreamException) { }
            }
            list.Sort((a, b) => a.t.CompareTo(b.t));
            return list;
        }
    }
}
