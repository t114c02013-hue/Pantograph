using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>按鍵對照表（資料：Resources/xr_bindings.json）。</summary>
    [Serializable]
    public class XrBindingTable
    {
        [Serializable]
        public class Button { public string button; public string command; public string[] paths = new string[0]; }

        [Serializable]
        public class Stick { public string tool; public string dir; public string target; public string text; public float scroll; }

        public Button[] buttons = new Button[0];
        public string[] leftStickPaths = new string[0], rightStickPaths = new string[0];
        public string leftStickLeft = "TurnLeft", leftStickRight = "TurnRight", leftStickForward = "TeleportAim";
        public Stick[] rightStick = new Stick[0];
        public string socketSizes = "10,13,17";
        public float turnDegrees = 45f, deadzone = 0.6f, release = 0.3f, repeatDelay = 0.45f, repeatInterval = 0.15f;

        public const string ResourceName = "xr_bindings";

        public static XrBindingTable FromJson(string json) => JsonUtility.FromJson<XrBindingTable>(json);

        public static XrBindingTable LoadDefault()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            return asset != null ? FromJson(asset.text) : new XrBindingTable();
        }

        public string CommandOf(string button) => buttons.FirstOrDefault(b => b.button == button)?.command ?? "";
    }

    /// <summary>一幀的控制器輸入（XrControllerInput 從 Input System 讀；測試或截圖腳本可直接給）。</summary>
    public struct XrFrame
    {
        public bool rightTrigger, rightGrip, a, b, x, y, menu, leftStickClick;
        public Vector2 leftStick, rightStick;

        public bool Get(string button)
        {
            switch (button)
            {
                case "RightTrigger": return rightTrigger;
                case "RightGrip": return rightGrip;
                case "A": return a;
                case "B": return b;
                case "X": return x;
                case "Y": return y;
                case "Menu": return menu;
                case "LeftStickClick": return leftStickClick;
                default: return false;
            }
        }
    }

    /// <summary>對照時需要的狀態（手上的工具、是否拿著滑板、是否有彈窗、目前套筒尺寸）。</summary>
    public struct XrContext
    {
        public string heldTool;
        public bool carrying;
        public bool dialogOpen;
        public string socketSize;
        public bool errorOpen, tutorial;

        /// <summary>按鍵表的情境（優先順序：錯誤視窗 → 其他彈窗 → 控制器教學 → 手上工具 → 空手）。</summary>
        public string BindingContext =>
            errorOpen ? Data.InputBindingTable.ErrorOpen : dialogOpen ? Data.InputBindingTable.PanelOpen : tutorial ? Data.InputBindingTable.Tutorial
            : !string.IsNullOrEmpty(heldTool) ? Data.InputBindingTable.ToolPrefix + heldTool : Data.InputBindingTable.Empty;
    }

    /// <summary>
    /// 對照結果。command：Use／Grab（pressed＝按下或放開）、ResetTool、FocusTarget、ToggleWorkOrder、ToggleHelp、Menu、
    /// TurnLeft／TurnRight（degrees）、TeleportAim（pressed＝開始瞄準或放開傳送）、ToolAdjust（target＋text 送給工具，或 scroll 當滾輪）。
    /// </summary>
    public struct XrCommand
    {
        public string command;
        public bool pressed;
        public string target, text;
        public float scroll, degrees;

        public override string ToString() => command + (pressed ? "↓" : "") + (string.IsNullOrEmpty(text) ? "" : " " + target + " " + text) + (scroll != 0 ? " scroll " + scroll : "");
    }

    /// <summary>
    /// 控制器按鍵 → 指令（純 C#，不碰場景，可在 EditMode 測試）。只認「按下／放開」的邊緣；
    /// 右搖桿依手上工具查表（上下按住會連發），左搖桿左右＝分段轉向、往前推＝傳送瞄準（放開才傳送）。
    /// A（重設工具）在彈窗開著或空手時不作用。
    /// </summary>
    public class XRInputMapper
    {
        readonly XrBindingTable table;
        readonly Data.InputBindingTable bindings;
        XrFrame prev;
        bool aiming;
        int turnLatch;
        string rightDir = "";
        float nextRepeat;

        public XrBindingTable Table => table;
        public Data.InputBindingTable Bindings => bindings;

        /// <summary>table＝實體按鍵路徑與搖桿參數（xr_bindings.json）；bindings＝按鍵表（功能與各情境是否啟用，唯一來源）。</summary>
        public XRInputMapper(XrBindingTable table, Data.InputBindingTable bindings = null)
        {
            this.table = table ?? new XrBindingTable();
            this.bindings = bindings ?? Data.InputBindingTable.LoadDefault();
        }

        public List<XrCommand> Map(XrFrame f, XrContext ctx, float now)
        {
            var list = new List<XrCommand>();
            string context = ctx.BindingContext;
            foreach (var b in table.buttons)
            {
                bool cur = f.Get(b.button), was = prev.Get(b.button);
                if (cur == was) continue;
                string command = bindings.FunctionOf(b.button);   // 功能由按鍵表決定（同一顆鍵在所有情境都一樣）
                if (string.IsNullOrEmpty(command) || command == "None") continue;
                if (command == "Use" || command == "Grab") { if (!cur || bindings.Enabled(context, b.button)) list.Add(new XrCommand { command = command, pressed = cur }); }   // 放開一定送，避免卡住
                else if (cur && bindings.Enabled(context, b.button)) list.Add(new XrCommand { command = command, pressed = true });
            }
            MapLeftStick(f.leftStick, list);
            MapRightStick(f.rightStick, ctx, now, list);
            prev = f;
            return list;
        }

        void MapLeftStick(Vector2 v, List<XrCommand> list)
        {
            if (aiming)
            {
                if (v.y < table.release) { aiming = false; list.Add(new XrCommand { command = table.leftStickForward, pressed = false }); }
                return;
            }
            if (v.y > table.deadzone && v.y > Mathf.Abs(v.x))
            {
                aiming = true; turnLatch = 0;
                list.Add(new XrCommand { command = table.leftStickForward, pressed = true });
                return;
            }
            if (turnLatch != 0) { if (Mathf.Abs(v.x) < table.release) turnLatch = 0; return; }
            if (Mathf.Abs(v.x) > table.deadzone && Mathf.Abs(v.x) > Mathf.Abs(v.y))
            {
                turnLatch = v.x > 0 ? 1 : -1;
                list.Add(new XrCommand { command = v.x > 0 ? table.leftStickRight : table.leftStickLeft, pressed = true, degrees = v.x > 0 ? table.turnDegrees : -table.turnDegrees });
            }
        }

        void MapRightStick(Vector2 v, XrContext ctx, float now, List<XrCommand> list)
        {
            string dir = rightDir;
            if (v.magnitude < table.release) dir = "";
            else if (v.magnitude > table.deadzone)
                dir = Mathf.Abs(v.y) >= Mathf.Abs(v.x) ? (v.y > 0 ? "up" : "down") : (v.x > 0 ? "right" : "left");
            bool edge = dir != rightDir;
            rightDir = dir;
            if (dir == "") return;
            bool vertical = dir == "up" || dir == "down";
            if (edge) nextRepeat = now + table.repeatDelay;
            else if (vertical && now >= nextRepeat) nextRepeat = now + table.repeatInterval;
            else return;

            string tool = ctx.carrying ? "carried" : ctx.heldTool ?? "";
            var entry = table.rightStick.FirstOrDefault(e => e.tool == tool && e.dir == dir);
            if (entry == null) return;
            if (!edge && !vertical) return;
            var cmd = new XrCommand { command = "ToolAdjust", pressed = true, target = entry.target ?? "", text = entry.text ?? "", scroll = entry.scroll };
            if (cmd.text == "size:next" || cmd.text == "size:prev") cmd.text = "size:" + CycleSize(ctx.socketSize, cmd.text == "size:next" ? 1 : -1);
            list.Add(cmd);
        }

        static readonly System.Collections.Generic.Dictionary<string, string> ControlNames = new System.Collections.Generic.Dictionary<string, string>
        {
            { "RightTrigger", "右扳機" }, { "RightGrip", "右握把" }, { "RightStick", "右搖桿" }, { "RightStickClick", "右搖桿按下" }, { "A", "A" }, { "B", "B" },
            { "LeftStick", "左搖桿" }, { "LeftStickClick", "左搖桿按下" }, { "LeftGrip", "左握把" }, { "LeftPoke", "左手尖端碰" }, { "X", "X" }, { "Y", "Y" },
            { "Menu", "≡" }, { "LeftWrist", "左手腕面板" },
            { "LeftTrigger", "左扳機" }, { "RightPoke", "右手尖端碰" }, { "RightWrist", "右手腕面板" },
        };

        /// <summary>Y 說明卡的按鍵表（從按鍵表產生，只列這個情境啟用的鍵；左撇子時左右對調）。</summary>
        public static string HelpText(Data.InputBindingTable t, string context, bool leftHanded = false) =>
            string.Join("\n", t.rows.Where(r => r.context == context && r.enabled && r.functionId != "None")
                .Select(r => Data.HandednessMap.Map(r, leftHanded))
                .Select(r => $"<b>{(ControlNames.TryGetValue(r.control, out var n) ? n : r.control)}</b>　{r.labelKey}"));

        /// <summary>套筒尺寸循環（10 → 13 → 17 → 10）。</summary>
        public string CycleSize(string current, int step)
        {
            var sizes = (table.socketSizes ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            if (sizes.Length == 0) return current ?? "";
            int i = Array.IndexOf(sizes, current ?? "");
            if (i < 0) i = 0;
            return sizes[((i + step) % sizes.Length + sizes.Length) % sizes.Length];
        }
    }
}
