using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.Procedures;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>
    /// R1.5b b4 BlackboardKeyDefTests（軟體工程裁定，補充 2b）：kind 只能是 Event、LiveState；LiveState 清單的每一項都對到真的有人讀的鍵；資產和 JSON 一致；
    /// 條件讀的鍵只取鍵、不把右邊的字面值（例 tool.active=tool.caliper 的 tool.caliper）當成鍵。
    /// </summary>
    public class BlackboardKeyDefTests
    {
        /// <summary>兩門課（匯入的資產）和 R1 的小動作條件讀到的鍵。</summary>
        static HashSet<string> ConditionKeysInUse()
        {
            var set = new HashSet<string>();
            foreach (var c in StepsCsvImporter.Courses)
                foreach (var st in AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath(c)).steps)
                    foreach (var a in st.subActions)
                        foreach (var side in string.IsNullOrEmpty(st.side) ? new[] { "L", "R" } : new[] { st.side })
                            foreach (var k in FreshWriteGate.ConditionKeys(StateKeys.Expand(a.condition, side))) set.Add(k);
            foreach (var st in StepsCsvImporter.LoadR1().Values)
                foreach (var a in st.subActions)
                    foreach (var side in new[] { "L", "R" })
                        foreach (var k in FreshWriteGate.ConditionKeys(StateKeys.Expand(a.done, side))) set.Add(k);
            return set;
        }

        [Test]
        public void Kind_OnlyEventOrLiveState_InvalidFailsImport()
        {
            var errs = new List<string>();
            var ok = StepsCsvImporter.LoadKeyDefSource(errs);
            try { Assert.IsEmpty(errs, string.Join("\n", errs)); }
            finally { Object.DestroyImmediate(ok); }
            var bad = StepsCsvImporter.BuildKeyDef("{\"keys\":[{\"pattern\":\"x.y\",\"kind\":\"Live\"}]}", errs);
            Object.DestroyImmediate(bad);
            Assert.IsTrue(errs.Any(e => e.Contains("x.y") && e.Contains("Event 或 LiveState")), "寫錯 kind 要失敗");
        }

        [Test]
        public void Asset_MatchesJson()
        {
            var errs = new List<string>();
            var src = StepsCsvImporter.LoadKeyDefSource(errs);
            var asset = AssetDatabase.LoadAssetAtPath<BlackboardKeyDef>(StepsCsvImporter.BlackboardKeysAssetPath);
            try
            {
                Assert.IsNotNull(asset, "要先跑 Tools/Import Steps");
                CollectionAssert.AreEqual(src.keys.Select(k => k.pattern + "|" + k.kind).ToArray(), asset.keys.Select(k => k.pattern + "|" + k.kind).ToArray(), "Resources/BlackboardKeys.asset 和 JSON 一致");
            }
            finally { Object.DestroyImmediate(src); }
        }

        [Test]
        public void EveryLiveStatePattern_MatchesAKeyInUse()
        {
            var inUse = ConditionKeysInUse().ToList();
            var asset = AssetDatabase.LoadAssetAtPath<BlackboardKeyDef>(StepsCsvImporter.BlackboardKeysAssetPath);
            foreach (var e in asset.keys.Where(k => k.kind == BlackboardKeyKind.LiveState))
                Assert.IsTrue(inUse.Any(k => BlackboardKeyDef.Matches(e.pattern, k)), $"LiveState {e.pattern}：沒有任何小動作條件讀這個鍵（不存在的鍵不要列）");
            // 沒列（＝Event）的鍵寫出來給軟體確認
            var events = inUse.Where(k => asset.Kind(k) == BlackboardKeyKind.Event).OrderBy(k => k).ToList();
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/BlackboardKeys_Event.txt", $"小動作條件讀到、沒列在 BlackboardKeys.json（一律當 Event）的鍵 {events.Count} 個：\n" + string.Join("\n", events) + "\n");
        }

        [Test]
        public void ConditionKeys_LeftSideOnly_ForEquals()
        {
            CollectionAssert.AreEqual(new[] { "tool.active" }, FreshWriteGate.ConditionKeys("tool.active=tool.caliper").ToArray(), "右邊是字面值");
            CollectionAssert.AreEqual(new[] { "a.b", "c.d" }, FreshWriteGate.ConditionKeys("a.b<=c.d").ToArray(), "<= 右邊不是數字＝鍵");
            CollectionAssert.AreEqual(new[] { "a.b", "x.y" }, FreshWriteGate.ConditionKeys("a.b>=0.5&&x.y").ToArray());
            var def = AssetDatabase.LoadAssetAtPath<BlackboardKeyDef>(StepsCsvImporter.BlackboardKeysAssetPath);
            Assert.AreEqual(BlackboardKeyKind.LiveState, def.Kind("rack.slot_3"));
            Assert.AreEqual(BlackboardKeyKind.Event, def.Kind("tool.voltage_detector.selftest"), "自檢是事件紀錄");
            Assert.AreEqual(BlackboardKeyKind.Event, def.Kind("never.listed"), "沒列的＝Event");
        }
    }
}
