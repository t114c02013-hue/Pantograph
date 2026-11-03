using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Procedures
{
    /// <summary>
    /// R1.5b b4（軟體工程裁定 2026-11-03）：小動作啟用時記下條件讀的鍵的 version；之後要「條件成立」而且「至少一個鍵的 version 變大」才算完成，
    /// 學員在這一步之後真的做了動作才過（例：IN_04b b 自檢，SI_04 做過也要再點一次）。黑板的值不清。
    /// 例外列在 ProcedureDefinition.autoCompleteAllowed（每筆寫原因）。
    /// shortcut: 機制每幀都寫的量測鍵（例：吊物高度）version 一直在加，對它們這條規則等於沒有作用；要擋時改成看「值有沒有變」或由機制只在動作時寫
    /// </summary>
    public class FreshWriteGate
    {
        readonly Dictionary<string, long> baseline = new Dictionary<string, long>();

        /// <summary>啟用一個小動作：記下它條件裡每個鍵目前的 version。</summary>
        public void Arm(ISimState s, string condition)
        {
            baseline.Clear();
            foreach (var k in RuleExplainer.KeysOf(condition).Distinct()) baseline[k] = s.Version(k);
        }

        /// <summary>啟用後被寫過的鍵（沒有任何鍵時回傳 null）。</summary>
        public string WrittenKey(ISimState s) => baseline.FirstOrDefault(kv => s.Version(kv.Key) > kv.Value).Key;

        /// <summary>條件成立，而且（允許一出現就完成，或啟用後有鍵被寫過；條件裡沒有鍵時不擋）。</summary>
        public bool Done(ISimState s, string condition, bool allowAuto) =>
            ChecklistEval.Done(s, condition) && (allowAuto || baseline.Count == 0 || WrittenKey(s) != null);

        public static bool Allowed(ProcedureDefinition p, string stepId, string subId) =>
            p != null && (p.autoCompleteAllowed ?? System.Array.Empty<AutoCompleteAllowedDef>()).Any(a => a.stepId == stepId && a.subId == subId);
    }
}
