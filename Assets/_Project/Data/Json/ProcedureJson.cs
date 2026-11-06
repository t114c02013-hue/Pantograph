using System;
using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Data
{
    // carbon_strip_slice.json 的資料格式（JsonUtility 可讀）。轉換成 ScriptableObject 由 ProcedureJsonConverter 負責。

    [Serializable] public class RuleLeafJson
    {
        public string type;              // StateEquals / ChecklistAll / OrderedSequence / ValueInRange / Decision / ReadingMatches / ProcedureDone
        public string key, value, expected, choiceKey;
        public string[] keys = Array.Empty<string>();
        public string[] recordKeys = Array.Empty<string>();
        public string[] readingKeys = Array.Empty<string>();
        public float min, max, threshold = 3.0f, tolerance = 0.05f;
        public int count;
    }

    [Serializable] public class RuleJson : RuleLeafJson
    {
        public RuleLeafJson[] all = Array.Empty<RuleLeafJson>();   // type = AllOf
    }

    [Serializable] public class ErrorJson
    {
        public string errorId, severity, trigger, what, why, fix, key, key2, value, whenAnswer, expected;
        public string[] keys = Array.Empty<string>();
        public float min, max;
        public int count;
        public bool blocks;
        public bool oncePerRun;   // R1.5b b4 3d
        public bool tutorialHintOnly;
    }

    [Serializable] public class HintJson
    {
        public string text, why;
        public string[] checklist = Array.Empty<string>();
        public string[] highlightIds = Array.Empty<string>();
        public bool showBoltNumbers, showOutwardArrow;
    }

    [Serializable] public class SubActionJson
    {
        public string textDesktop, hint;   // text＝VR 文字；桌面照舊用 textDesktop
        public string text, why, target, tool, done;
        /// <summary>這個小動作的站點 ID（R1）；tp.roof.{side} 依側別展開成 tp.roof.inner／tp.roof.outer。</summary>
        public string stand;
    }

    [Serializable] public class StepJson
    {
        public SubActionJson[] subActions = Array.Empty<SubActionJson>();
        public string stepId, title, instruction, instructionXR, toolHint, targetHint, requiredToolId, side;
        public string[] targetIds = Array.Empty<string>();
        public string[] presetStates = Array.Empty<string>();
        public string[] valueTags = Array.Empty<string>();
        public bool presetDone;
        /// <summary>參考時間 [教學秒, 考核秒]（示例值）；重複區塊為單側秒數。</summary>
        public float[] refSeconds = Array.Empty<float>();
        public HintJson hints = new HintJson();
        public RuleJson completion = new RuleJson();
        public ErrorJson[] errors = Array.Empty<ErrorJson>();
    }

    [Serializable] public class SafetyRuleJson { public string ruleId, errorId, kind, what, why, fix; public float toleranceCm = 1f; public string severity = "Critical"; public bool deprecated; public string replacedBy = ""; }   // R1.5b b3：severity

    [Serializable] public class ScenarioJson
    {
        public string scenarioId, title, description;
        public string[] initialStates = Array.Empty<string>();
        public int randomSeed = 1;
        public bool startRaised, practiceOnly;
    }

    [Serializable] public class ToolJson
    {
        public string toolId, displayName;
        public int hotkey;
        public string[] parameters = Array.Empty<string>();
        public string secondaryAction;
        public TutorialPage[] pages = Array.Empty<TutorialPage>();
    }

    [Serializable] public class PositionCheckJson
    {
        public string checkId, part = "pan_head", purpose = "lower", targetName;
        public float toleranceCm = 1f;
    }

    [Serializable] public class LiftJson
    {
        public float fullTravelSeconds = 15f, accelSeconds = 0.6f, slowZoneCm = 20f, slowSpeedCmPerSec = 5f;
    }

    [Serializable] public class ProcedureJson
    {
        public PositionCheckJson[] positionChecks = Array.Empty<PositionCheckJson>();
        public LiftJson lift = new LiftJson();
        public string procedureId, title, goal;
        public string[] safetyPoints = Array.Empty<string>();
        public RepeatBlock repeatBlock = new RepeatBlock();
        public JumpRule[] onPass = Array.Empty<JumpRule>();
        public StepJson[] steps = Array.Empty<StepJson>();
        public SafetyRuleJson[] safetyRules = Array.Empty<SafetyRuleJson>();
        public ScenarioJson[] scenarios = Array.Empty<ScenarioJson>();
        public ToolJson[] tools = Array.Empty<ToolJson>();
    }

    /// <summary>JSON 轉出的整包資料（尚未存成資產）。</summary>
    public class ProcedureBundle
    {
        public ProcedureDefinition procedure;
        public List<SafetyRuleDefinition> safetyRules = new List<SafetyRuleDefinition>();
        public List<ScenarioDefinition> scenarios = new List<ScenarioDefinition>();
        public List<ToolDefinition> tools = new List<ToolDefinition>();
        public List<PositionCheckDefinition> positionChecks = new List<PositionCheckDefinition>();
        public LiftSettings lift;
    }

    public static class ProcedureJsonConverter
    {
        public static ProcedureBundle Build(string json)
        {
            var src = JsonUtility.FromJson<ProcedureJson>(json);
            var bundle = new ProcedureBundle();

            var prc = ScriptableObject.CreateInstance<ProcedureDefinition>();
            prc.name = src.procedureId;
            prc.procedureId = src.procedureId;
            prc.title = src.title;
            prc.goal = src.goal;
            prc.safetyPoints = src.safetyPoints;
            prc.repeatBlock = src.repeatBlock;
            prc.onPass = src.onPass;
            foreach (var s in src.steps) prc.steps.Add(BuildStep(s));
            bundle.procedure = prc;

            foreach (var r in src.safetyRules)
            {
                var sr = ScriptableObject.CreateInstance<SafetyRuleDefinition>();
                sr.name = r.ruleId; sr.ruleId = r.ruleId; sr.errorId = r.errorId;
                sr.kind = (SafetyRuleKind)Enum.Parse(typeof(SafetyRuleKind), r.kind);
                sr.what = r.what; sr.why = r.why; sr.fix = r.fix; sr.toleranceCm = r.toleranceCm; sr.deprecated = r.deprecated; sr.replacedBy = r.replacedBy ?? "";
                sr.severity = (PantographSim.Core.Severity)Enum.Parse(typeof(PantographSim.Core.Severity), string.IsNullOrEmpty(r.severity) ? "Critical" : r.severity);
                bundle.safetyRules.Add(sr);
            }
            foreach (var c in src.scenarios)
            {
                var sc = ScriptableObject.CreateInstance<ScenarioDefinition>();
                sc.name = c.scenarioId; sc.scenarioId = c.scenarioId; sc.title = c.title; sc.description = c.description;
                sc.initialStates = c.initialStates; sc.randomSeed = c.randomSeed; sc.startRaised = c.startRaised; sc.practiceOnly = c.practiceOnly;
                bundle.scenarios.Add(sc);
            }
            foreach (var t in src.tools)
            {
                var td = ScriptableObject.CreateInstance<ToolDefinition>();
                td.name = "TL_" + t.toolId.Replace("tool.", "");
                td.toolId = t.toolId; td.displayName = t.displayName; td.hotkey = t.hotkey; td.parameters = t.parameters; td.secondaryAction = t.secondaryAction;
                td.tutorial = new ToolTutorialDefinition { pages = t.pages };
                bundle.tools.Add(td);
            }
            foreach (var p in src.positionChecks ?? Array.Empty<PositionCheckJson>())
            {
                var pc = ScriptableObject.CreateInstance<PositionCheckDefinition>();
                pc.name = p.checkId; pc.checkId = p.checkId; pc.part = p.part; pc.purpose = p.purpose;
                pc.targetName = p.targetName; pc.toleranceCm = p.toleranceCm;
                bundle.positionChecks.Add(pc);
            }
            bundle.lift = ScriptableObject.CreateInstance<LiftSettings>();
            bundle.lift.name = "LiftSettings";
            bundle.lift.fullTravelSeconds = src.lift?.fullTravelSeconds ?? 15f;
            bundle.lift.accelSeconds = src.lift?.accelSeconds ?? 0.6f;
            bundle.lift.slowZoneCm = src.lift?.slowZoneCm ?? 20f;
            bundle.lift.slowSpeedCmPerSec = src.lift?.slowSpeedCmPerSec ?? 5f;
            return bundle;
        }

        public static StepDefinition BuildStep(StepJson s)
        {
            var st = ScriptableObject.CreateInstance<StepDefinition>();
            st.name = s.stepId;
            st.stepId = s.stepId; st.title = s.title; st.instruction = s.instruction; st.instructionXR = s.instructionXR;
            st.toolHint = s.toolHint; st.targetHint = s.targetHint; st.targetIds = s.targetIds;
            st.requiredToolId = s.requiredToolId ?? ""; st.side = s.side ?? "";
            st.presetDone = s.presetDone; st.presetStates = s.presetStates; st.valueTags = s.valueTags;
            st.refSecondsTutorial = s.refSeconds != null && s.refSeconds.Length > 0 ? s.refSeconds[0] : 0f;
            st.refSecondsExam = s.refSeconds != null && s.refSeconds.Length > 1 ? s.refSeconds[1] : 0f;
            st.hints = new HintSet { text = s.hints?.text, why = s.hints?.why, checklist = s.hints?.checklist ?? Array.Empty<string>(), highlightIds = s.hints?.highlightIds ?? Array.Empty<string>(), showBoltNumbers = s.hints?.showBoltNumbers ?? false, showOutwardArrow = s.hints?.showOutwardArrow ?? false };
            st.subActions = (s.subActions ?? Array.Empty<SubActionJson>())
                .Select(a => new SubAction { text = a.text, textDesktop = a.textDesktop ?? "", hint = a.hint ?? "", why = a.why ?? "", targetId = a.target ?? "", toolId = a.tool ?? "", condition = a.done ?? "", standPointId = a.stand ?? "" }).ToArray();
            st.completion = BuildRule(s.completion);
            st.errors = s.errors.Select(BuildError).ToArray();
            return st;
        }

        public static CompletionRule BuildRule(RuleLeafJson r)
        {
            if (r == null || string.IsNullOrEmpty(r.type)) return null;
            switch (r.type)
            {
                case "StateEquals": return new StateEquals { key = r.key, value = r.value };
                case "ChecklistAll": return new ChecklistAll { keys = r.keys };
                case "OrderedSequence": return new OrderedSequence { key = r.key, expected = r.expected };
                case "ValueInRange": return new ValueInRange { key = r.key, min = r.min, max = r.max, count = r.count };
                case "Decision": return new Decision { choiceKey = r.choiceKey, threshold = r.threshold };
                case "ReadingMatches": return new ReadingMatches { recordKeys = r.recordKeys, readingKeys = r.readingKeys, tolerance = r.tolerance };
                case "ProcedureDone": return new ProcedureDone();
                case "AllOf":
                    var all = (r as RuleJson)?.all ?? Array.Empty<RuleLeafJson>();
                    return new AllOf { rules = all.Select(BuildRule).Where(x => x != null).ToArray() };
                default: throw new ArgumentException("未知的完成條件類型：" + r.type);
            }
        }

        public static ErrorRule BuildError(ErrorJson e) => new ErrorRule
        {
            errorId = e.errorId,
            severity = (Severity)Enum.Parse(typeof(Severity), e.severity),
            trigger = (ErrorTrigger)Enum.Parse(typeof(ErrorTrigger), e.trigger),
            what = e.what, why = e.why, fix = e.fix,
            key = e.key, key2 = e.key2, keys = e.keys ?? Array.Empty<string>(), value = e.value, whenAnswer = e.whenAnswer,
            min = e.min, max = e.max, expected = e.expected, count = e.count, blocks = e.blocks, oncePerRun = e.oncePerRun, tutorialHintOnly = e.tutorialHintOnly
        };
    }
}
