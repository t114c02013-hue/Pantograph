using System;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Data
{
    // ---------- 完成條件（只放資料；判定邏輯在 Procedures/Evaluators） ----------
    // 鍵可含 {side}（L/R）與 bolt_{n}（n=1..count 展開）。

    [Serializable] public abstract class CompletionRule { }

    /// <summary>黑板 key 等於 value。</summary>
    [Serializable] public class StateEquals : CompletionRule { public string key; public string value; }

    /// <summary>所有 keys 都是 "true"。</summary>
    [Serializable] public class ChecklistAll : CompletionRule { public string[] keys = Array.Empty<string>(); }

    /// <summary>key 記錄的順序字串（例 "1,2,3,4"）等於 expected。</summary>
    [Serializable] public class OrderedSequence : CompletionRule { public string key; public string expected; }

    /// <summary>key 的數值在 [min,max]；count&gt;0 時 bolt_{n} 依 1..count 展開，全部都要符合。</summary>
    [Serializable] public class ValueInRange : CompletionRule { public string key; public float min; public float max; public int count; }

    /// <summary>作業單的判定選項要等於由讀數算出的正確答案。</summary>
    [Serializable] public class Decision : CompletionRule
    {
        public string choiceKey;
        public string[] sides = { "L", "R" };
        [Tooltip("真實厚度鍵，{side} 與 {point} 展開")] public string trueKeyPattern = "part.carbon_strip_{side}.true.{point}";
        public string[] points = { "left", "mid", "right" };
        [Tooltip("更換基準（mm）")] public float threshold = 3.0f;
        public string replaceChoice = "both";
        public string passChoice = "pass";
    }

    /// <summary>作業單填入值與實際讀數差在容差內。recordKeys 與 readingKeys 一一對應。</summary>
    [Serializable] public class ReadingMatches : CompletionRule
    {
        public string[] recordKeys = Array.Empty<string>();
        public string[] readingKeys = Array.Empty<string>();
        public float tolerance = 0.05f;
    }

    /// <summary>前面所有步驟都已完成。</summary>
    [Serializable] public class ProcedureDone : CompletionRule { }

    /// <summary>全部子條件都成立。</summary>
    [Serializable] public class AllOf : CompletionRule
    {
        [SerializeReference] public CompletionRule[] rules = Array.Empty<CompletionRule>();
    }

    // ---------- 錯誤規則 ----------

    public enum ErrorTrigger
    {
        ValueOutOfRange,   // key 的數值變成在 [min,max] 之外
        SequenceMismatch,  // key 的順序字串不是 expected 的前綴
        ActionBefore,      // key 變成 true 時，前置條件 key2 還不是 true
        WrongTool,         // 對本步目標使用了不是 requiredToolId 的工具
        WrongTarget,       // 對不屬於本步的弓體目標動作
        Incomplete,        // key 變成 true 時，keys 中有尚未 true 的
        ChoiceEquals,      // key 的值等於 value（可限定正確答案為 whenAnswer）
        OutOfRangeOnLeave,        // R1.5b K3（驗收組長）：學員離開這顆螺栓時判一次（開始鎖下一顆、換工具、步驟完成）：key（bolt_{n} 依 count 展開）的最終值 < min；離開前補到位就不記
        UnsetAtCompletion,        // R1.5b K3：步驟完成時判一次：key2 的值在 [min,max] 之內、但 key 還不是 true（例：扭力合格卻沒畫標記）
        WhenCondition             // R1.5b b4 3c：全程監看（ProcedureDefinition.globalErrors）：key2（前提，可空白）成立且 keys 任一個條件（ChecklistEval）成立，由不成立變成立時記一次
    }

    [Serializable]
    public class ErrorRule
    {
        public string errorId;
        public Severity severity;
        [TextArea] public string what;
        [TextArea] public string why;
        [TextArea] public string fix;
        public ErrorTrigger trigger;
        public string key;
        public string key2;
        public string[] keys = Array.Empty<string>();
        public string value;
        public string whenAnswer;
        public float min;
        public float max;
        public string expected;
        public int count;
        [Tooltip("Critical 時要求從這一步重做")] public bool blocks;
        [Tooltip("R1.5b K3：考核模式且 ExamPolicyDef.k3Ready=false 時改記這個等級（空白＝不降級）")] public string severityBeforeK3 = "";
        [Tooltip("R1.5b K8：desktop＝只在桌面模式載入（VR 讀數自動填入，例 ERR_CSR03_CopyError）；空白＝兩種都載入")] public string inputMode = "";
    }

    /// <summary>
    /// 小動作：教學模式一次只顯示一個。依序完成（條件成立就鎖定為完成並換下一個），全部完成步驟才算完成。
    /// 條件格式同 ChecklistEval：「鍵」「鍵=值」「鍵&gt;=數字」；可含 {side}。
    /// </summary>
    [Serializable]
    public class SubAction
    {
        [Tooltip("一句短指令（≤ 20 字）")] public string text;
        [Tooltip("一行小字：為什麼")] public string why;
        [Tooltip("目標互動點 ID（高亮與箭頭）")] public string targetId;
        [Tooltip("需要的工具 ID（空白＝徒手或不限）")] public string toolId;
        [Tooltip("完成條件")] public string condition;
        [Tooltip("站點 ID（R1：B 鍵傳到這一站；tp.roof.{side} 依側別展開成 tp.roof.inner／tp.roof.outer）")] public string standPointId;
        [Tooltip("R1.5b：展開後的小動作 ID（a、d1、b2…；R1 的資料空白）")] public string subId = "";

        /// <summary>tp.roof.L → tp.roof.inner、tp.roof.R → tp.roof.outer（肘側／外側）。</summary>
        public static string ExpandStand(string stand, string side)
        {
            var s = StateKeys.Expand(stand ?? "", side);
            return s == "tp.roof.L" ? "tp.roof.inner" : s == "tp.roof.R" ? "tp.roof.outer" : s;
        }
    }

    [Serializable]
    public class HintSet
    {
        [TextArea] public string text;
        [Tooltip("為什麼要這樣做（一句話）")] [TextArea] public string why;
        [Tooltip("小步驟清單：顯示文字|黑板鍵（為 true／非空即完成）或 文字|鍵=值 或 文字|鍵>=數值")]
        public string[] checklist = Array.Empty<string>();
        public string[] highlightIds = Array.Empty<string>();
        public bool showBoltNumbers;
        public bool showOutwardArrow;
    }

    /// <summary>數值來源標籤。</summary>
    public enum ValueTag { None, MaintenanceTable, Example }

    public static class ValueTags
    {
        public static string Label(ValueTag tag) => tag == ValueTag.MaintenanceTable ? "保養表值" : tag == ValueTag.Example ? "示例值" : "";
        public static ValueTag Parse(string s) => s == "MaintenanceTable" || s == "保養表值" ? ValueTag.MaintenanceTable : s == "Example" || s == "示例值" ? ValueTag.Example : ValueTag.None;
    }

    /// <summary>黑板鍵展開工具。</summary>
    public static class StateKeys
    {
        public static string Expand(string pattern, string side, int n = 0, string point = null)
        {
            if (string.IsNullOrEmpty(pattern)) return pattern;
            var s = pattern.Replace("{side}", side ?? "");
            if (n > 0) s = s.Replace("bolt_{n}", "bolt_" + n).Replace("{n}", n.ToString());
            if (point != null) s = s.Replace("{point}", point);
            return s;
        }
    }
}
