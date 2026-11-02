using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    public class DataTests
    {
        [Test]
        public void SerializeReference_KeepsSubclassData()
        {
            var step = ScriptableObject.CreateInstance<StepDefinition>();
            step.stepId = "STP_X";
            step.completion = new AllOf
            {
                rules = new CompletionRule[]
                {
                    new ValueInRange { key = "ip.carbon_strip_{side}.bolt_{n}.angle_deg", min = 150, max = 210, count = 4 },
                    new OrderedSequence { key = "ip.carbon_strip_{side}.loosen_order", expected = "1,2,3,4" }
                }
            };
            var json = EditorJsonUtility.ToJson(step);
            var copy = ScriptableObject.CreateInstance<StepDefinition>();
            EditorJsonUtility.FromJsonOverwrite(json, copy);

            Assert.IsInstanceOf<AllOf>(copy.completion);
            var all = (AllOf)copy.completion;
            Assert.AreEqual(2, all.rules.Length);
            var range = all.rules[0] as ValueInRange;
            Assert.IsNotNull(range);
            Assert.AreEqual(210f, range.max);
            Assert.AreEqual(4, range.count);
            Assert.AreEqual("1,2,3,4", ((OrderedSequence)all.rules[1]).expected);
        }

        [Test]
        public void JsonConverter_BuildsStepsRulesAndErrors()
        {
            const string json = @"{
              ""procedureId"": ""PRC_T"",
              ""repeatBlock"": { ""fromStepId"": ""B"", ""toStepId"": ""B"", ""sides"": [""L"",""R""], ""sideLabels"": [""肘側滑板"",""外側滑板""] },
              ""steps"": [
                { ""stepId"": ""A"", ""title"": ""t"", ""completion"": { ""type"": ""StateEquals"", ""key"": ""k"", ""value"": ""v"" },
                  ""errors"": [ { ""errorId"": ""E"", ""severity"": ""Critical"", ""trigger"": ""ChoiceEquals"", ""key"": ""c"", ""value"": ""x"", ""what"": ""w"", ""why"": ""y"", ""fix"": ""f"" } ] },
                { ""stepId"": ""B"", ""side"": ""{side}"", ""completion"": { ""type"": ""AllOf"", ""all"": [ { ""type"": ""ValueInRange"", ""key"": ""v"", ""min"": 1, ""max"": 2 }, { ""type"": ""Decision"", ""choiceKey"": ""d"", ""threshold"": 3.0 } ] } }
              ],
              ""safetyRules"": [ { ""ruleId"": ""SR_Global_NotLowered"", ""errorId"": ""ERR_Global_NotLowered"", ""kind"": ""NotLowered"", ""what"": ""a"", ""why"": ""b"", ""fix"": ""c"" } ],
              ""tools"": [ { ""toolId"": ""tool.caliper"", ""displayName"": ""卡尺"", ""hotkey"": 3, ""parameters"": [""noise=0.02""], ""pages"": [ { ""title"": ""1"", ""body"": ""b"" } ] } ]
            }";
            var b = ProcedureJsonConverter.Build(json);
            Assert.AreEqual(2, b.procedure.steps.Count);
            Assert.IsInstanceOf<StateEquals>(b.procedure.steps[0].completion);
            Assert.AreEqual(Severity.Critical, b.procedure.steps[0].errors[0].severity);
            Assert.AreEqual(ErrorTrigger.ChoiceEquals, b.procedure.steps[0].errors[0].trigger);
            var all = (AllOf)b.procedure.steps[1].completion;
            Assert.IsInstanceOf<Decision>(all.rules[1]);
            Assert.AreEqual(SafetyRuleKind.NotLowered, b.safetyRules[0].kind);
            Assert.AreEqual("0.02", b.tools[0].Param("noise"));
            Assert.AreEqual("外側滑板", b.procedure.repeatBlock.sideLabels[1]);
        }

        [Test]
        public void StateKeys_ExpandsSideAndBolt()
        {
            Assert.AreEqual("ip.carbon_strip_R.bolt_3.angle_deg", StateKeys.Expand("ip.carbon_strip_{side}.bolt_{n}.angle_deg", "R", 3));
            Assert.AreEqual("part.carbon_strip_L.true.mid", StateKeys.Expand("part.carbon_strip_{side}.true.{point}", "L", 0, "mid"));
        }

        [Test]
        public void ValueTags_Labels()
        {
            Assert.AreEqual("保養表值", ValueTags.Label(ValueTag.MaintenanceTable));
            Assert.AreEqual("示例值", ValueTags.Label(ValueTag.Example));
        }
    }
}
