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
    /// R1.5b b4 RigHeightTests（鐵道 D42、軟體工程）：IN_01 a 吊梁在弓頂上方 lowerM～（拉直高度 − slackMarginM）。拉直高度由建置量測寫回資料，
    /// 和場景重量一致；建置檢查（上限 &gt; 下限 + 0.10、&lt; 1.0 − 0.10）；吊梁停在 1.0 m 進入 IN_01 不完成、降到範圍中間才完成、上限 + 0.02 不完成。
    /// </summary>
    public class RigHeightTests
    {
        static RigHeightDef Def => AssetDatabase.LoadAssetAtPath<RigHeightDef>(RigHeightMeasure.AssetPath);

        [Test]
        public void StraightHeight_StoredMatchesScene_CheckPasses()
        {
            Assert.IsNotNull(Def, "要先建置場景（建置時量測寫回）");
            StationTests.WithScene(scene =>
            {
                var m = RigHeightMeasure.Measure(Def.topBasis);
                Assert.AreEqual(m.straight, Def.straightHeightM, 0.005f, "資料裡的拉直高度和場景重量一致：" + m);
            });
            Assert.IsNull(Def.Check(), Def.Check());
            var bad = Object.Instantiate(Def);
            try { bad.straightHeightM = bad.lowerM + 0.10f + bad.slackMarginM - 0.001f; Assert.IsNotNull(bad.Check(), "上限沒有比下限高 0.10 以上：建置失敗"); }
            finally { Object.DestroyImmediate(bad); }
        }

        static (ProcedureRunner runner, SimState s, ProcedureDefinition prc) Rig()
        {
            var src = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath("periodic"));
            var prc = ScriptableObject.CreateInstance<ProcedureDefinition>();
            prc.procedureId = "PRC_Rig"; prc.steps = new List<StepDefinition> { src.steps.First(x => x.stepId == "STP_IN_01_RigOnStand") }; prc.requireFreshWrite = true;
            var bus = new EventBus(); var s = new SimState(bus);
            Def.Publish(s);
            s.Batch(() => { s.SetFloat("part.crane.hook_cog_offset_cm", 0f); s.SetFloat("part.crane.beam_above_pan_m", 1.0f); });   // LF_06 c 結束時吊梁在弓頂上方 1.0 m
            var runner = new ProcedureRunner(prc, bus, s);
            runner.Start();
            return (runner, s, prc);
        }

        [Test]
        public void IN01a_At1m_NotDone_Mid_Done()
        {
            var (runner, s, prc) = Rig();
            try
            {
                // v3.10（D44）：a 之前先做空吊梁查看（a0–a4；沒開故障時 s1–s4 跳過）
                var subs = prc.steps[0].subActions;
                int ia = System.Array.FindIndex(subs, x => x.subId == "a");
                foreach (var pre in subs.Take(ia).Where(x => string.IsNullOrEmpty(x.when))) CourseFlowTests.Write(s, pre.condition);
                Assert.AreEqual(ia, (int)s.GetFloat("procedure.sub_index"), "吊梁停在 1.0 m 做到 IN_01 a：不完成（不和 LF_06 c 的 ≥ 1.0 m 重疊）");
                s.SetFloat("part.crane.beam_above_pan_m", (Def.lowerM + Def.UpperM) / 2f);
                Assert.AreEqual(ia + 1, (int)s.GetFloat("procedure.sub_index"), "降到範圍中間才完成");
            }
            finally { runner.Dispose(); Object.DestroyImmediate(prc); }
        }

        [Test]
        public void IN01a_AboveUpperBy2cm_NotDone()
        {
            var (runner, s, prc) = Rig();
            try
            {
                s.SetFloat("part.crane.beam_above_pan_m", Def.UpperM + 0.02f);
                Assert.AreEqual(0, (int)s.GetFloat("procedure.sub_index"), "上限 + 0.02 m：吊帶太緊掛不上，不完成");
                s.SetFloat("part.crane.beam_above_pan_m", Def.lowerM - 0.02f);
                Assert.AreEqual(0, (int)s.GetFloat("procedure.sub_index"), "低於下限也不完成");
            }
            finally { runner.Dispose(); Object.DestroyImmediate(prc); }
        }
    }
}
