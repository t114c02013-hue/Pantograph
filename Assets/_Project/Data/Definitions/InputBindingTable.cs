using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// R1 按鍵表（唯一來源，B 段 6）：每列＝情境、手、控制項、功能、中文標示、是否啟用。
    /// 同一個控制項在所有情境的功能都一樣，只有啟用與否隨情境改變。XRInputMapper 依這張表送事件；Y 說明卡與手腕面板的按鍵說明也從這張表產生。
    /// 左撇子模式用 HandednessMap 對調左右手，不複製整張表。實體按鍵的 Input System 路徑仍在 Resources/xr_bindings.json。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/R1/Input Binding Table")]
    public class InputBindingTable : ScriptableObject
    {
        public const string ResourceName = "InputBindingTable";
        public const string Empty = "Empty", PanelOpen = "PanelOpen", ErrorOpen = "ErrorOpen", Tutorial = "Tutorial", ToolPrefix = "Tool:";

        [Serializable]
        public class Row
        {
            public string context, hand, control, functionId, labelKey;
            public bool enabled = true;
        }

        public Row[] rows = Array.Empty<Row>();

        /// <summary>情境清單（Tool:&lt;toolId&gt; 依工具展開）。</summary>
        public IEnumerable<string> Contexts => rows.Select(r => r.context).Distinct();

        public Row Find(string context, string control) => rows.FirstOrDefault(r => r.context == context && r.control == control);

        /// <summary>控制項的功能（所有情境相同；找不到回傳空字串）。</summary>
        public string FunctionOf(string control) => rows.FirstOrDefault(r => r.control == control)?.functionId ?? "";

        /// <summary>這個情境下控制項是否啟用（沒有列就當作停用）。</summary>
        public bool Enabled(string context, string control) => Find(context, control)?.enabled ?? false;

        public static InputBindingTable LoadDefault()
        {
            var t = Resources.Load<InputBindingTable>(ResourceName);
            return t != null && t.rows.Length > 0 ? t : CreateDefault();
        }

        /// <summary>預設表（B 段 6）。enabled 依情境：A 只有拿著工具且沒有彈窗時有作用；彈窗或錯誤視窗開著時只留點按、說明、選單與移動。</summary>
        public static InputBindingTable CreateDefault()
        {
            var t = CreateInstance<InputBindingTable>();
            t.rows = DefaultRows().ToArray();
            return t;
        }

        static readonly (string hand, string control, string fn, string label)[] Controls =
        {
            ("Right", "RightTrigger", "Use", "點／使用：空手點零件＝查看；指著面板或升降 ▲▼＝按下；拿著工具＝使用工具"),
            ("Right", "RightGrip", "Grab", "抓／放工具、滑板、螺栓"),
            ("Right", "RightStick", "None", "（無）"),
            ("Right", "RightStickClick", "ToolSecondary", "第二功能"),
            ("Right", "A", "ResetTool", "重設手上的工具"),
            ("Right", "B", "FocusTarget", "帶我到目標"),
            ("Left", "LeftStick", "TeleportTurn", "往前推＝傳送；左右撥＝轉 45°"),
            ("Left", "LeftStickClick", "Crouch", "蹲低視角（只在車頂有效）"),
            ("Left", "LeftGrip", "PanelGrab", "抓面板把手、拿放工具袋的工具"),
            ("Left", "LeftTrigger", "ToolDetail", "看手上工具的用法（再按一次關）"),
            ("Left", "LeftPoke", "ToolPoke", "按工具上的撥桿、± 鈕、ZERO 鍵"),
            ("Left", "X", "ToggleWorkOrder", "開關作業單"),
            ("Left", "Y", "ToggleHelp", "說明"),
            ("Left", "Menu", "Menu", "暫停選單：重做本步、面板叫回眼前、重設高度、真實高度／抬高、蹲低視角、離開"),
            ("Left", "LeftWrist", "WristPanel", "手腕面板：上一步（教學模式）、提示開關、面板叫回眼前、工具數值備援鈕"),
        };

        static readonly string[] Tools =
        {
            "tool.lockout_tag", "tool.voltage_detector", "tool.grounding_rod", "tool.safety_harness", "tool.caliper",
            "tool.socket_wrench", "tool.torque_wrench", "tool.cleaning_cloth", "tool.marker",
            "tool.megger", "tool.torque_wrench_l", "tool.hold_down_strap", "tool.grounding_lead",
        };

        static bool EnabledIn(string context, string control)
        {
            bool tool = context.StartsWith(ToolPrefix);
            switch (control)
            {
                case "RightStick": return false;   // 沒有功能
                case "RightStickClick": return context == ToolPrefix + "tool.lockout_tag" || context == ToolPrefix + "tool.megger";   // ToolDefinition.secondaryAction
                case "A": return tool;                  // 重設工具：彈窗開著（PanelOpen／ErrorOpen）或空手時沒作用
                case "LeftTrigger": return tool;        // 手上有工具才有用法可看
                case "LeftPoke": return tool;           // 只有拿著工具時，工具上的撥桿、± 鈕、ZERO 才啟用
                default: return true;                   // 其他鍵在所有情境都有作用
            }
        }

        static IEnumerable<Row> DefaultRows()
        {
            var contexts = new List<string> { Empty, PanelOpen, ErrorOpen, Tutorial };
            contexts.AddRange(Tools.Select(x => ToolPrefix + x));
            foreach (var c in contexts)
                foreach (var (hand, control, fn, label) in Controls)
                    yield return new Row { context = c, hand = hand, control = control, functionId = fn, labelKey = label, enabled = EnabledIn(c, control) };
        }
    }

    /// <summary>左撇子模式：把表上的左右手對調（控制項名稱 Right*／Left* 與 A／B ↔ X／Y 一起換），不複製整張表。</summary>
    // shortcut: 左撇子只對調表與說明文字，實體按鍵沒有切換開關、也不左右互換讀取, 要支援左撇子學員時在 XrControllerInput 依設定對調左右手的 InputAction
    public static class HandednessMap
    {
        static readonly Dictionary<string, string> Pairs = new Dictionary<string, string>
        {
            { "RightTrigger", "LeftTrigger" }, { "RightGrip", "LeftGrip" }, { "RightStick", "LeftStick" }, { "RightStickClick", "LeftStickClick" },
            { "A", "X" }, { "B", "Y" }, { "LeftPoke", "RightPoke" }, { "LeftWrist", "RightWrist" }, { "Menu", "Menu" },
        };

        public static string Hand(string hand, bool leftHanded) => !leftHanded ? hand : hand == "Right" ? "Left" : hand == "Left" ? "Right" : hand;

        public static string Control(string control, bool leftHanded)
        {
            if (!leftHanded) return control;
            if (Pairs.TryGetValue(control, out var v)) return v;
            foreach (var kv in Pairs) if (kv.Value == control) return kv.Key;
            return control;
        }

        public static InputBindingTable.Row Map(InputBindingTable.Row r, bool leftHanded) => new InputBindingTable.Row
        {
            context = r.context, hand = Hand(r.hand, leftHanded), control = Control(r.control, leftHanded), functionId = r.functionId, labelKey = r.labelKey, enabled = r.enabled
        };
    }
}
