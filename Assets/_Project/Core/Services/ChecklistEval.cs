using System.Globalization;

namespace PantographSim.Core
{
    /// <summary>
    /// 小步驟清單的條件判斷（UI 顯示 ☐/☑ 用）。條件格式：
    /// 「鍵」→ 值為 true 或非空且不是 false；「鍵=值」→ 相等（值可為空字串）；
    /// 「鍵>=數字」→ 數值不小於；「鍵&lt;=數字」→ 數值不大於。比較的右邊也可以是另一個鍵（例：距離&lt;=容許值鍵）。
    /// </summary>
    public static class ChecklistEval
    {
        public static bool Done(ISimState s, string condition)
        {
            if (string.IsNullOrEmpty(condition)) return false;
            if (condition.Contains("&&"))   // 「條件 && 條件」：全部成立
            {
                foreach (var part in condition.Split(new[] { "&&" }, System.StringSplitOptions.RemoveEmptyEntries))
                    if (!Done(s, part.Trim())) return false;
                return true;
            }
            int ge = condition.IndexOf(">=");
            if (ge > 0)
            {
                var key = condition.Substring(0, ge);
                return s.Has(key) && TryOperand(s, condition.Substring(ge + 2), out var min) && s.GetFloat(key) >= min - 1e-4f;
            }
            int le = condition.IndexOf("<=");
            if (le > 0)
            {
                var key = condition.Substring(0, le);
                return s.Has(key) && TryOperand(s, condition.Substring(le + 2), out var max) && s.GetFloat(key) <= max + 1e-4f;
            }
            int eq = condition.IndexOf('=');
            if (eq > 0) return s.Get(condition.Substring(0, eq)) == condition.Substring(eq + 1);
            var v = s.Get(condition);
            return !string.IsNullOrEmpty(v) && v != "false";
        }

        /// <summary>比較的右邊：數字，或黑板上已有的數值鍵。</summary>
        static bool TryOperand(ISimState s, string text, out float value)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            if (!s.Has(text)) return false;
            value = s.GetFloat(text);
            return true;
        }

        /// <summary>「文字|條件」→ (文字, 條件)</summary>
        public static (string text, string condition) Parse(string item)
        {
            int i = item.IndexOf('|');
            return i < 0 ? (item, "") : (item.Substring(0, i), item.Substring(i + 1));
        }
    }
}
