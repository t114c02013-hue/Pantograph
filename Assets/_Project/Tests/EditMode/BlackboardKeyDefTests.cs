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
            // 錯誤規則的觸發條件（BlockWhen 是「目標|條件」）也算有人讀
            foreach (var r in StepsCsvImporter.LoadErrorRules().Values.Where(r => r.HasRule && !StepsCsvImporter.Disabled(r.status)))
                foreach (var c in r.rule.keys.Append(r.rule.key).Append(r.rule.key2).Where(c => !string.IsNullOrEmpty(c)))
                    foreach (var side in new[] { "L", "R" })
                        foreach (var k in FreshWriteGate.ConditionKeys(StateKeys.Expand(c.Substring(c.IndexOf('|') + 1), side))) set.Add(k);
            return set;
        }

        [System.Serializable] class ResetJson { public string pattern, kind, resetBy; }
        [System.Serializable] class ResetFile { public ResetJson[] keys = System.Array.Empty<ResetJson>(); }

        /// <summary>
        /// 軟體工程裁定 2026-11-03 2d：每個 LiveState 鍵都要有回到 false（或換成別的值）的路徑，寫在 resetBy；
        /// 寫的機制「類別.方法」要真的存在；寫不回來的標「n/a: 理由」，清單寫到 Logs/BlackboardKeys_ResetBy.txt 給軟體確認。
        /// </summary>
        [Test]
        public void EveryLiveState_HasResetPath()
        {
            var src = JsonUtility.FromJson<ResetFile>(System.IO.File.ReadAllText(StepsCsvImporter.BlackboardKeysPath, System.Text.Encoding.UTF8));
            var types = System.AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("PantographSim"))
                .SelectMany(a => a.GetTypes()).GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.ToList());
            var na = new List<string>();
            foreach (var k in src.keys.Where(k => k.kind == "LiveState"))
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(k.resetBy), $"LiveState {k.pattern}：resetBy 沒寫（由哪個機制寫回，或「n/a: 理由」）");
                if (k.resetBy.StartsWith("n/a:")) { na.Add(k.pattern + "：" + k.resetBy.Substring(4).Trim()); continue; }
                var refs = System.Text.RegularExpressions.Regex.Matches(k.resetBy, @"\b([A-Z]\w+)\.([A-Z]\w+)\b").Cast<System.Text.RegularExpressions.Match>().ToList();
                Assert.IsNotEmpty(refs, $"LiveState {k.pattern}：resetBy 要寫出「類別.方法」");
                foreach (var m in refs)
                    Assert.IsTrue(types.TryGetValue(m.Groups[1].Value, out var ts) && ts.Any(t => t.GetMember(m.Groups[2].Value, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static).Length > 0),
                        $"LiveState {k.pattern}：resetBy 寫的 {m.Value} 不存在");
            }
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/BlackboardKeys_ResetBy.txt", $"LiveState 鍵找不到寫回 false 路徑（n/a）的 {na.Count} 個：\n" + string.Join("\n", na) + "\n");
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
