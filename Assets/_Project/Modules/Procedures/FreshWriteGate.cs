using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Procedures
{
    /// <summary>
    /// R1.5b b4（軟體工程裁定 2026-11-03）：小動作啟用時記下條件讀的 Event 鍵的 version；之後要「條件成立」而且「至少一個 Event 鍵的 version 變大」才算完成，
    /// 學員在這一步之後真的做了動作才過（例：IN_04b b 自檢，SI_04 做過也要再點一次）。黑板的值不清。
    /// 條件只讀 LiveState 鍵（手持、掛位、夾子、螺栓、吊物位置…，BlackboardKeyDef）時不看 version，條件成立就完成（例：進入時已拿著要用的工具）。
    /// 例外列在 ProcedureDefinition.autoCompleteAllowed（每筆寫原因）。
    /// shortcut: 機制每幀都寫的量測鍵（例：吊物高度）version 一直在加，對它們這條規則等於沒有作用；要擋時改成看「值有沒有變」或由機制只在動作時寫
    /// </summary>
    public class FreshWriteGate
    {
        readonly Dictionary<string, long> baseline = new Dictionary<string, long>();

        /// <summary>種類表（預設 Resources/BlackboardKeys；測試可換）。null＝全部當 Event。</summary>
        public static BlackboardKeyDef Kinds { get => kinds ?? (kinds = BlackboardKeyDef.Load()); set => kinds = value; }
        static BlackboardKeyDef kinds;

        /// <summary>條件讀的黑板鍵：「鍵=值」只取鍵（右邊是字面值，例 tool.active=tool.caliper），「鍵&gt;=、&lt;=」右邊不是數字時也是鍵。</summary>
        public static IEnumerable<string> ConditionKeys(string condition)
        {
            foreach (var raw in (condition ?? "").Split(new[] { "&&" }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var p = raw.Trim();
                int op = p.IndexOf(">=") > 0 ? p.IndexOf(">=") : p.IndexOf("<=");
                if (op > 0)
                {
                    yield return p.Substring(0, op).Trim();
                    var rhs = p.Substring(op + 2).Trim();
                    if (!float.TryParse(rhs, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) yield return rhs;
                }
                else { int eq = p.IndexOf('='); yield return (eq > 0 ? p.Substring(0, eq) : p).Trim(); }
            }
        }

        /// <summary>條件讀到的 Event 鍵（只有這些要看 version）。</summary>
        public static IEnumerable<string> EventKeys(string condition) =>
            ConditionKeys(condition).Distinct().Where(k => Kinds == null || Kinds.Kind(k) == BlackboardKeyKind.Event);

        /// <summary>啟用一個小動作：記下它條件裡每個 Event 鍵目前的 version。</summary>
        public void Arm(ISimState s, string condition)
        {
            baseline.Clear();
            foreach (var k in EventKeys(condition)) baseline[k] = s.Version(k);
        }

        /// <summary>啟用後被寫過的 Event 鍵（沒有任何鍵時回傳 null）。</summary>
        public string WrittenKey(ISimState s) => baseline.FirstOrDefault(kv => s.Version(kv.Key) > kv.Value).Key;

        /// <summary>條件成立，而且（允許一出現就完成，或只讀 LiveState 鍵，或啟用後有 Event 鍵被寫過）。</summary>
        public bool Done(ISimState s, string condition, bool allowAuto) =>
            ChecklistEval.Done(s, condition) && (allowAuto || baseline.Count == 0 || WrittenKey(s) != null);

        public static bool Allowed(ProcedureDefinition p, string stepId, string subId) =>
            p != null && (p.autoCompleteAllowed ?? System.Array.Empty<AutoCompleteAllowedDef>()).Any(a => a.stepId == stepId && a.subId == subId);
    }
}
