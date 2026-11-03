using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.Procedures;
using PantographSim.Worksite;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>
    /// R1.5b b4 第 4 部分 CourseOverrideTests（第 2 部分 3.5）：每門課的小動作集合＝SubActions.csv 依 courses 篩選；課程擺放（位置、顯示）；
    /// onPass；日常 CSR_08 判暫放墊；備品架 L、R 排列（負責人裁定：兩側都只有正確且無裂紋的那一條能選，L＝slot_2、R＝slot_3）。D23 的 5 個（4c）在最後。
    /// </summary>
    public class CourseOverrideTests
    {
        static CourseDef Course(string c) => AssetDatabase.LoadAssetAtPath<CourseDef>(StepsCsvImporter.CourseDefPath(c));

        [Test]
        public void SubActionSets_EqualCsvFilteredByCourse()
        {
            var ex = StepsCsvImporter.Expand(StepsCsvImporter.LoadSubActionRows(), new List<string>());
            foreach (var c in StepsCsvImporter.Courses)
            {
                var want = ex.Where(e => e.course == c).Select(e => e.stepId + "|" + e.subId).OrderBy(x => x).ToList();
                var have = Course(c).procedure.steps.SelectMany(s => s.subActions.Select(a => s.stepId + "|" + a.subId)).OrderBy(x => x).ToList();
                CollectionAssert.AreEqual(want, have, c + "：小動作集合只由 CSV 的 courses 欄決定");
            }
        }

        [TestCase("periodic")]
        [TestCase("daily")]
        public void Placements_ActiveAndAtAnchor(string c)
        {
            StationTests.WithScene(scene =>
            {
                var problems = CourseApplier.Apply(Course(c));
                Assert.IsEmpty(problems, string.Join("\n", problems));
                var ids = CourseApplier.SceneIds();
                foreach (var p in Course(c).placements)
                {
                    var t = p.id.StartsWith("name:") ? CourseApplier.FindByName(p.id.Substring(5)) : ids[p.id];
                    Assert.AreEqual(p.active, t.gameObject.activeSelf, $"{c} {p.id}：顯示與否");
                    if (!string.IsNullOrEmpty(p.anchor))
                        Assert.Less(Vector3.Distance(CourseApplier.FindByName(p.anchor).position, t.position), 0.01f, $"{c} {p.id}：在 {p.anchor}");
                }
            });
        }

        [Test]
        public void OnPass_Periodic_ToIN01_Daily_ToWC01a()
        {
            Assert.AreEqual("STP_IN_01_RigOnStand", Course("periodic").procedure.onPass.Single(j => j.stepId == "STP_CSR_05_Decide").jumpTo);
            Assert.AreEqual("STP_WC_01a_ToolCountRoof", Course("daily").procedure.onPass.Single(j => j.stepId == "STP_CSR_05_Decide").jumpTo);
        }

        [Test]
        public void CSR08_Daily_OnMat_Periodic_InTray()
        {
            var s = new SimState(new EventBus());
            s.Set("part.carbon_strip_L.removal", "vertical");   // 定期沿用 R1：垂直拆下（另一個條件）
            foreach (var (c, value, done) in new[] { ("daily", "OnMat", true), ("daily", "InTray", false), ("periodic", "InTray", true), ("periodic", "OnMat", false) })
            {
                var step = Course(c).procedure.steps.First(x => x.stepId == "STP_CSR_08_RemoveStrip_L");
                s.Set("part.carbon_strip_L.state", value);
                Assert.AreEqual(done, RuleEvaluators.Evaluate(step.completion, new EvalContext { state = s, side = "L" }), $"{c}：舊滑板 {value}");
            }
        }

        /// <summary>負責人裁定：兩側都只有正確且無裂紋的那一條能通過 CSR_10 f（L＝slot_2、R＝slot_3）；拿錯型號記 WrongType，拿到有裂紋的記 Cracked。取代「每一側 spare.{side}.n 都找得到」。</summary>
        [TestCase("L", 2)]
        [TestCase("R", 3)]
        public void SpareArrangement_OnlyCorrectUncrackedPasses_WrongTypeAndCrackedRecorded(string side, int good)
        {
            var arr = AssetDatabase.LoadAssetAtPath<SpareArrangementDef>(StepsCsvImporter.SpareArrangementPath);
            Assert.IsNotNull(arr, "要先跑 Tools/Import Steps");
            var src = Course("daily").procedure.steps.First(x => x.stepId == "STP_CSR_10_CheckNew_" + side);
            for (int slot = 1; slot <= 3; slot++)
            {
                var e = arr.Find(side, slot);
                var prc = ScriptableObject.CreateInstance<ProcedureDefinition>();
                prc.procedureId = "PRC_Spare"; prc.steps = new List<StepDefinition> { src };
                var bus = new EventBus(); var s = new SimState(bus);
                var runner = new ProcedureRunner(prc, bus, s);
                var det = new ErrorDetector(runner, bus, s);
                var errors = new List<string>();
                bus.Subscribe<ErrorDetected>(x => errors.Add(x.errorId));
                runner.Start();
                try
                {
                    string id = "env.spare_shelf.slot_" + slot;
                    SpareShelf.Stock(s, id, e.model, SpareShelf.Nominal);
                    SpareShelf.RecordMeasurement(s, id, SpareShelf.Nominal);
                    bool picked = SpareShelf.Pick(s, id, side, e.model, e.cracked);
                    Assert.AreEqual(slot == good, picked, $"{side} slot_{slot}（{e.model}{(e.cracked ? "、有裂紋" : "")}）");
                    Assert.AreEqual(slot == good, s.GetBool($"spare.{side}.picked"), "CSR_10 f 的條件");
                    if (e.model != SpareShelf.CorrectModel) CollectionAssert.Contains(errors, "ERR_CSR10_WrongType");
                    else if (e.cracked) CollectionAssert.Contains(errors, "ERR_CSR10_Cracked");
                    else Assert.IsEmpty(errors);
                }
                finally { det.Dispose(); runner.Dispose(); Object.DestroyImmediate(prc); }
            }
        }

        // ---------- D23 的 5 個（4c） ----------

        static float HoldUp(SimState s, float seconds)
        {
            var settings = LiftSettingsValues.From(AssetDatabase.LoadAssetAtPath<LiftSettings>(StepJsonImporter.LiftSettingsPath));
            float v = 0f, vel = 0f;
            for (float t = 0f; t < seconds; t += 0.02f)
            {
                LiftLogic.Step(ref v, ref vel, 1, 0.02f, settings);
                PantographSim.Parts.Mechanism.LiftDriver.ApplyCap(s, ref v, ref vel);
            }
            return v;
        }

        /// <summary>D23、D41：日常更換按住升弓 30 s，滑板頂面停在 O 點 + 1.35（減視覺間距）、判定已接觸；定期檢修升到原本的上限。</summary>
        [Test]
        public void DailyLiftCapStopsAtContactHeight()
        {
            StationTests.WithScene(scene =>
            {
                var mech = Object.FindObjectOfType<PantographSim.Parts.Mechanism.PantographMechanism>(true);
                float keep = mech.Lift;
                try
                {
                    var o = CourseApplier.FindByName("Anchor_O").position;
                    var s = new SimState(new EventBus());
                    CourseApplier.Apply(Course("daily"), s);
                    float v = HoldUp(s, 30f);
                    float top = CourseSceneBuilder.StripTopY(mech, v);
                    float wire = o.y + Course("daily").liftCapAboveO;
                    Assert.AreEqual(wire - Course("daily").contactGapM, top, 0.01f, $"日常：滑板頂面停在接觸線 Y {wire:0.000}（減 {Course("daily").contactGapM} m 間距）");
                    Assert.LessOrEqual(top, wire + 1e-3f, "不穿過接觸線");
                    Assert.IsTrue(s.GetBool(SimIds.PanAtContactWire), "到上限＝判定已接觸");
                    var p = new SimState(new EventBus());
                    CourseApplier.Apply(Course("periodic"), p);
                    float vp = HoldUp(p, 30f);
                    Assert.AreEqual(1f, vp, 1e-4f, "定期：升到 LiftProfile 原本的上限");
                    Assert.AreEqual(CourseSceneBuilder.StripTopY(mech, 1f), CourseSceneBuilder.StripTopY(mech, vp), 0.01f);
                    Assert.IsFalse(p.GetBool(SimIds.PanAtContactWire));
                }
                finally { mech.SetLift(keep); }
            });
        }

        [Test]
        public void DailyCraneParkedAtParkPose()
        {
            StationTests.WithScene(scene =>
            {
                var crane = Object.FindObjectOfType<PantographSim.Parts.Mechanism.CraneMechanism>(true);
                Assert.IsNotNull(crane.ParkPose, "Crane_ParkPose 節點");
                var s = new SimState(new EventBus());
                CourseApplier.Apply(Course("daily"), s);
                crane.Tick(s, 0.02f);
                Assert.Less(Vector3.Distance(crane.ParkPose.position, crane.AxisPositions), 0.01f, "橋架、吊車台、吊鉤和 Crane_ParkPose 相差 ≤ 1 cm");
            });
        }

        [Test]
        public void DailyCraneCommandsIgnored()
        {
            StationTests.WithScene(scene =>
            {
                var crane = Object.FindObjectOfType<PantographSim.Parts.Mechanism.CraneMechanism>(true);
                var s = new SimState(new EventBus());
                CourseApplier.Apply(Course("daily"), s);
                crane.Tick(s, 0.02f);
                var start = crane.AxisPositions;
                foreach (var cmd in new[] { CraneLogic.Up, CraneLogic.Down, CraneLogic.East, CraneLogic.West, CraneLogic.North, CraneLogic.South })
                {
                    s.SetBool(SimIds.CranePower, true);   // 想開電源也沒用
                    s.Set(SimIds.CraneCmd, cmd);
                    for (int i = 0; i < 50; i++) crane.Tick(s, 0.02f);
                    Assert.Less(Vector3.Distance(start, crane.AxisPositions), 1e-4f, cmd + "：三軸位置都不變");
                    Assert.IsFalse(s.GetBool(SimIds.CranePower), "ctrl.crane.power 仍是 false");
                }
            });
        }

        [Test]
        public void DailyPendantNotGrabbable()
        {
            var s = new SimState(new EventBus());
            CraneControl.SetPendant(s, false);
            s.SetBool(SimIds.CourseCraneParked, true);   // CourseApplier 套用日常課程時寫入
            CraneControl.SetPendant(s, true);   // 左握把在掛座 0.3 m 內按下（XrCranePendant.Toggle）、桌面點遙控器都走這裡
            Assert.AreEqual(CraneControl.Docked, s.Get(SimIds.CranePendantState), "遙控器仍在掛座，不會隨身");
            s.SetBool(SimIds.CourseCraneParked, false);
            CraneControl.SetPendant(s, true);
            Assert.AreEqual(CraneControl.Held, s.Get(SimIds.CranePendantState), "定期檢修照常拿得起來");
        }

        [Test]
        public void DailyCraneStationClosed()
        {
            StationTests.WithScene(scene =>
            {
                var rule = AssetDatabase.LoadAssetAtPath<TeleportRule>("Assets/_Project/Modules/Worksite/Data/TeleportRule.asset");
                var s = new SimState(new EventBus());
                CourseApplier.Apply(Course("daily"), s);
                Assert.IsTrue(rule.Hidden(s, "tp.platform.crane"), "傳送點清單沒有 tp.platform.crane");
                Assert.IsNotNull(rule.BlockReason(s, "tp.platform.crane"));
                var keep = PantographSim.XR.XrTeleportPoint.Rule;
                PantographSim.XR.XrTeleportPoint.Rule = rule;
                try
                {
                    var pts = Object.FindObjectsOfType<PantographSim.XR.XrTeleportPoint>(true);
                    var target = pts.First(p => p.PointId == "tp.platform.crane");
                    var current = pts.First(p => p.PointId == "tp.platform.rack");
                    Assert.AreEqual(2, PantographSim.XR.XrTeleportPoint.Decide(target, current, s), "直接要求傳送過去：擋下（Locked），人不動");
                    var p2 = new SimState(new EventBus());
                    CourseApplier.Apply(Course("periodic"), p2);
                    Assert.IsFalse(rule.Hidden(p2, "tp.platform.crane"), "定期檢修開放");
                }
                finally { PantographSim.XR.XrTeleportPoint.Rule = keep; }
            });
        }
    }
}
