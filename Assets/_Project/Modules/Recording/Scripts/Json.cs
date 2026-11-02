using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PantographSim.Recording
{
    /// <summary>
    /// 小型 JSON 讀寫（紀錄檔用；JsonUtility 讀不了任意物件）。
    /// 寫：string／數字／bool／null／IDictionary&lt;string,object&gt;／IEnumerable。讀：回傳 Dictionary&lt;string,object&gt;、List&lt;object&gt;、double、bool、string、null。
    /// </summary>
    public static class Json
    {
        // ---------- 寫 ----------
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        public static void Write(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: Str(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case float f: NumF(sb, f); break;
                case double d: Num(sb, d); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> dict:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Str(sb, kv.Key); sb.Append(':'); Write(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                }
                case IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    break;
                }
                default: Str(sb, Convert.ToString(v, CultureInfo.InvariantCulture)); break;
            }
        }

        /// <summary>數字用 "R"（可完整讀回的最短表示），不四捨五入：距離等原始值寫入再讀回不變（schemaVersion 2 起）。</summary>
        static void Num(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>float 用 float 的 "R"（例 0.94f 寫成 0.94，不是 0.939999997615814）。</summary>
        static void NumF(StringBuilder sb, float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) { sb.Append("null"); return; }
            sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
        }

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------- 讀 ----------
        /// <summary>解析失敗（例如被截斷的一行）時丟 FormatException。</summary>
        public static object Parse(string text)
        {
            int i = 0;
            var v = Value(text, ref i);
            Skip(text, ref i);
            if (i != text.Length) throw new FormatException("JSON 後面有多餘內容");
            return v;
        }

        public static bool TryParse(string text, out object value)
        {
            try { value = Parse(text); return true; }
            catch (Exception) { value = null; return false; }
        }

        static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 不完整");
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return ReadStr(s, ref i);
            if (Lit(s, ref i, "true")) return true;
            if (Lit(s, ref i, "false")) return false;
            if (Lit(s, ref i, "null")) return null;
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException("JSON 無法辨識的字元：" + c);
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static bool Lit(string s, ref int i, string lit)
        {
            if (string.CompareOrdinal(s, i, lit, 0, lit.Length) != 0) return false;
            i += lit.Length;
            return true;
        }

        static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            Skip(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                Skip(s, ref i);
                string key = ReadStr(s, ref i);
                Skip(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON 缺少冒號");
                i++;
                d[key] = Value(s, ref i);
                Skip(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 物件沒有結束");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException("JSON 物件格式錯誤");
            }
        }

        static List<object> Arr(string s, ref int i)
        {
            var list = new List<object>();
            i++;
            Skip(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(Value(s, ref i));
                Skip(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 陣列沒有結束");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("JSON 陣列格式錯誤");
            }
        }

        static string ReadStr(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException("JSON 應該是字串");
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON 字串沒有結束");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("JSON 跳脫字元不完整");
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("JSON \\u 不完整");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
        }

        // ---------- 取值小工具 ----------
        public static string Str(this IDictionary<string, object> d, string key, string fallback = "") =>
            d != null && d.TryGetValue(key, out var v) && v != null ? (v is string s ? s : Convert.ToString(v, CultureInfo.InvariantCulture)) : fallback;

        /// <summary>讀回的數字是 double；記憶體中的紀錄行（還沒寫成 JSON）可能是 float／int／long。</summary>
        public static double Num(this IDictionary<string, object> d, string key, double fallback = 0)
        {
            if (d == null || !d.TryGetValue(key, out var v)) return fallback;
            switch (v)
            {
                case double x: return x;
                case float f: return f;
                case int i: return i;
                case long l: return l;
                default: return fallback;
            }
        }

        public static bool Bool(this IDictionary<string, object> d, string key, bool fallback = false) =>
            d != null && d.TryGetValue(key, out var v) && v is bool b ? b : fallback;

        public static bool Has(this IDictionary<string, object> d, string key) => d != null && d.ContainsKey(key) && d[key] != null;

        public static Dictionary<string, object> Obj(this IDictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        /// <summary>讀回的陣列是 List&lt;object&gt;；記憶體中的紀錄行可能是 string[] 等其他集合。</summary>
        public static List<object> Arr(this IDictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null || v is string) return new List<object>();
            if (v is List<object> l) return l;
            if (v is IEnumerable e) { var r = new List<object>(); foreach (var x in e) r.Add(x); return r; }
            return new List<object>();
        }

        public static string[] StrArr(this IDictionary<string, object> d, string key)
        {
            var l = Arr(d, key);
            var r = new string[l.Count];
            for (int k = 0; k < l.Count; k++) r[k] = l[k] as string ?? Convert.ToString(l[k], CultureInfo.InvariantCulture);
            return r;
        }
    }
}
