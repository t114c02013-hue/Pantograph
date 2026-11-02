using System.Collections.Generic;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PantographSim.UI
{
    /// <summary>
    /// 左上步驟面板（精簡版）：
    /// ・上方：「第 n/25 步：步驟名」＋小動作進度點（● ● ○）。
    /// ・教學模式：只顯示目前這一個小動作（大字短指令＋一行原因），完成時 ✔ 約 0.5 秒再換下一個；「↶ 上一步」可退回未改變零件的小動作。
    /// ・「詳細說明」按鈕展開完整內容（現在做、為什麼、需要工具、點哪裡＋帶我過去），預設收合。
    /// ・練習模式只顯示步驟名稱。
    /// </summary>
    public class StepPanelView : View
    {
        RectTransform root, subBox, details;
        Text header, dots, subText, subWhy, check, toolWarn, detailToggle, backReason, recLine;
        Text doText, whyText, toolText, whereText, tags, gate, goLabel;
        Button back, stallHint;
        RectTransform stallBox;
        Text stallText;
        public const string StallGeneric = "這一步還有條件沒完成，請再檢查";
        StepStarted step;
        bool hasStep, rebuild, showDetails;
        int lastSub = -1;
        float checkUntil;
        string checkedText = "";

        public void OnStep(StepStarted e) { step = e; hasStep = true; rebuild = true; lastSub = -1; checkUntil = 0f; }   // 換步驟時不閃上一步的 ✔

        protected override void OnBuild(RectTransform safe)
        {
            var panel = Ui.Panel(safe, "StepPanel");
            root = Ui.Place(panel.rectTransform, new Vector2(0, 1), Vector2.zero, new Vector2(500, 0));
            Ui.VStack(panel.gameObject, 18, 8);
            recLine = Ui.Text(root, "", 17, Ui.Muted, TextAnchor.UpperRight);   // 角落小字：● 紀錄中　● REC mm:ss
            header = Ui.Text(root, "", 26, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            dots = Ui.Text(root, "", 26, Ui.Accent);

            var sb = Ui.Panel(root, "SubAction", new Color(0.12f, 0.16f, 0.22f, 1f), true, false);
            subBox = sb.rectTransform;
            Ui.VStack(sb.gameObject, 16, 6);
            check = Ui.Text(subBox, "", 40, Ui.Ok, TextAnchor.UpperLeft, FontStyle.Bold);
            subText = Ui.Text(subBox, "", 34, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            subWhy = Ui.Text(subBox, "", 21, Ui.Muted);
            toolWarn = Ui.Text(subBox, "", 22, Ui.Bad, TextAnchor.UpperLeft, FontStyle.Bold);
            var row = Ui.Rect(subBox, "Row"); Ui.HStack(row.gameObject, 10);
            back = Ui.Button(row, "↶ 上一步", 20, () => Ui.Send("procedure.sub_back", "back")); Ui.Size(back, 130, 44);
            var go = Ui.Button(row, "帶我過去（C）", 20, () => { if (SimSession.IsReplay) return; Ui.Command(UiCommands.FocusTarget); Sim.Bus?.Publish(new FocusRequested { time = SimClock.Now }); }, new Color(0.15f, 0.45f, 0.75f)); Ui.Size(go, 170, 44);
            goLabel = Ui.LabelOf(go);
            backReason = Ui.Text(subBox, "", 18, Ui.Muted);

            var toggle = Ui.Button(root, "", 20, () => { showDetails = !showDetails; Ui.Command(UiCommands.HintPanelToggle, "ui.details"); }); Ui.Size(toggle, -1, 42);
            detailToggle = Ui.LabelOf(toggle);
            details = Ui.Rect(root, "Details"); Ui.VStack(details.gameObject, 0, 6);
            Section("現在要做"); doText = Ui.Text(details, "", 22);
            Section("為什麼"); whyText = Ui.Text(details, "", 21, Ui.Muted);
            Section("需要工具"); toolText = Ui.Text(details, "", 22);
            Section("點哪裡"); whereText = Ui.Text(details, "", 22);
            tags = Ui.Text(details, "", 18, Ui.Muted);
            gate = Ui.Text(root, "", 20, Ui.Bad);
            // 卡關提示：小動作都完成但完成條件不成立（ProcedureRunner.Poll 寫 procedure.stall_*）
            var st = Ui.Panel(root, "StallBox", new Color(0.35f, 0.22f, 0.05f, 1f), true, false);
            stallBox = st.rectTransform; Ui.VStack(st.gameObject, 12, 6);
            stallText = Ui.Text(stallBox, "", 22, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            stallHint = Ui.Button(stallBox, "提示：還差什麼？", 20, () =>
            {
                Ui.Command(UiCommands.StallHint, "ui.stall");   // 計入報表的提示次數
                Sim.State.SetBool("procedure.stall_hint_shown", true);
            }, new Color(0.15f, 0.45f, 0.75f));
            Ui.Size(stallHint, -1, 42);
        }

        void Section(string name) => Ui.Text(details, name, 20, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold);

        public override void Refresh(ISimState s)
        {
            bool show = hasStep && s.GetBool("training.running") && !s.GetBool("procedure.finished");
            Ui.SetActive(root, show);
            if (!show) return;
            if (rebuild) Rebuild();
            string rec = (s.GetBool("rec.active") ? Ui.Tint("● 紀錄中", new Color(1f, 0.45f, 0.4f)) : SimSession.IsReplay ? Ui.Tint("▶ 重播中", new Color(0.5f, 0.85f, 1f)) : "");
            string video = Ui.RecText(s);
            recLine.text = rec + (video.Length > 0 ? "　" + Ui.Tint(video, new Color(1f, 0.2f, 0.2f)) : "");
            Ui.SetActive(recLine, recLine.text.Length > 0);

            bool tutorial = s.Get("training.mode") != "practice";
            goLabel.text = InputMode.XrActive ? "帶我過去（B）" : "帶我過去（C）";
            string side = string.IsNullOrEmpty(step.sideLabel) ? "" : "（" + step.sideLabel + "）";
            header.text = $"第 {step.index}/{step.total} 步：{step.title}{side}";

            int count = (int)s.GetFloat("hint.sub_count");
            int idx = (int)s.GetFloat("procedure.sub_index");
            bool hasSubs = tutorial && count > 0;
            Ui.SetActive(dots, hasSubs);
            Ui.SetActive(subBox, hasSubs);
            if (hasSubs)
            {
                var d = new System.Text.StringBuilder();
                for (int i = 0; i < count; i++) d.Append(i < idx ? "● " : i == idx ? Ui.Tint("◉ ", Color.white) : "○ ");
                dots.text = d.ToString() + $"<size=18>{Mathf.Min(idx + 1, count)}/{count}</size>";

                if (lastSub >= 0 && idx > lastSub) { checkUntil = Time.unscaledTime + 0.6f; checkedText = "✔ " + prevText; }
                lastSub = idx;
                bool flash = Time.unscaledTime < checkUntil;
                Ui.SetActive(check, flash);
                check.text = checkedText;
                subText.text = s.Get("hint.sub_text");
                prevText = subText.text;
                var why = s.Get("hint.sub_why");
                subWhy.text = why; Ui.SetActive(subWhy, why.Length > 0);
                string need = s.Get("hint.sub_tool"), active = s.Get(SimIds.ActiveTool);
                bool wrong = !string.IsNullOrEmpty(need) && need != active && !subText.text.StartsWith("到工具架");
                toolWarn.text = wrong ? (string.IsNullOrEmpty(active) ? $"► 先到工具架拿「{Ui.ToolName(need)}」（第 {Ui.ToolKey(need)} 格）" : $"► 手上是「{Ui.ToolName(active)}」，要用「{Ui.ToolName(need)}」") : "";
                toolWarn.color = Color.Lerp(Ui.Bad, Color.white, Ui.Blink(6f) * 0.4f);
                Ui.SetActive(toolWarn, wrong);
                back.interactable = s.GetBool("procedure.can_back");
                var backWhy = s.Get("procedure.back_reason");
                backReason.text = back.interactable || string.IsNullOrEmpty(backWhy) ? "" : "↶ " + backWhy;
                Ui.SetActive(backReason, backReason.text.Length > 0 && idx > 0);
            }

            detailToggle.text = showDetails ? "▲ 收合詳細說明" : (tutorial ? "▼ 詳細說明" : "▼ 步驟說明");
            Ui.SetActive(details, showDetails);
            if (showDetails)
            {
                string required = step.toolId ?? "";
                toolText.text = string.IsNullOrEmpty(required) ? "不用工具（徒手）" : $"{Ui.ToolName(required)}（工具架第 {Ui.ToolKey(required)} 格）";
            }

            var reason = s.Get("procedure.gate_reason");
            gate.text = !string.IsNullOrEmpty(reason) && (step.stepId ?? "").StartsWith("STP_CSR") ? "尚不能完成：" + reason : "";
            Ui.SetActive(gate, gate.text.Length > 0);

            // 卡關：教學＝直接顯示第一個缺的小動作指令；練習＝通用句，按「提示」才顯示（計提示次數）；考核＝只顯示通用句
            bool stalled = s.GetBool("procedure.stalled");
            Ui.SetActive(stallBox, stalled);
            if (stalled)
            {
                string mode = s.Get("training.mode");
                string missing = s.Get("procedure.stall_text");
                bool reveal = mode == "tutorial" || mode == "" || (mode == "practice" && s.GetBool("procedure.stall_hint_shown"));
                stallText.text = reveal && missing.Length > 0 ? "還差：" + missing : StallGeneric;
                Ui.SetActive(stallHint, mode == "practice" && !s.GetBool("procedure.stall_hint_shown"));
            }
        }

        string prevText = "";

        void Rebuild()
        {
            rebuild = false;
            doText.text = string.IsNullOrEmpty(step.text) ? step.instruction : step.instruction + "\n" + Ui.Tint("提示：", Ui.Accent) + step.text;
            whyText.text = string.IsNullOrEmpty(step.why) ? "—" : step.why;
            whereText.text = step.targetHint;
            tags.text = string.IsNullOrEmpty(step.valueTags) ? "" : "數值：" + FormatTags(step.valueTags);
            Ui.SetActive(tags, tags.text.Length > 0);
            prevText = "";
        }

        /// <summary>"3.0 mm=保養表值;25 N·m=示例值" → "3.0 mm（保養表值）、25 N·m（示例值）"</summary>
        public static string FormatTags(string tags)
        {
            var parts = tags.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                int k = parts[i].IndexOf('=');
                if (k > 0) parts[i] = parts[i].Substring(0, k) + "（" + parts[i].Substring(k + 1) + "）";
            }
            return string.Join("、", parts);
        }
    }
}
