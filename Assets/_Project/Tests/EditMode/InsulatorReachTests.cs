using System.Linq;
using System.Text;
using NUnit.Framework;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.Parts.Common;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>
    /// R1.5b b4（D43，model-spec v3.12 §7.7.1c）：絕緣子髒汙計分格（InsulatorDirtDef.scoreMask，建置時烘焙）。
    /// 驗證：每支計分格 ≥ minScoreCells；從 r3f 指定子站點（桌面：站點眼高）打射線（和清潔布同一個射線設定）打得到全部計分格（可見率 100 %）。
    /// 結果寫 Logs/InsulatorReach.txt。背面不計分，VR、桌面同一套規則。
    /// </summary>
    public class InsulatorReachTests
    {
        [Test]
        public void InsulatorDirt_ScoreCells_AtLeastMin_AllVisibleFromDesktopStation()
        {
            var def = InsulatorDirtDef.Load();
            Assert.IsNotNull(def, "要先建置場景（InsulatorDirtBake）");
            Assert.AreEqual(4, def.scoreMask.Length);
            var prc = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath("periodic"));
            var subs = prc.steps.First(s => s.stepId == "STP_RT_02_InsulationTest").subActions.Where(a => a.subId.StartsWith("r3f")).ToList();
            Assert.AreEqual(4, subs.Count, "r3f 展開成 4 支");
            var set = AssetDatabase.LoadAssetAtPath<StationSet>("Assets/_Project/Modules/Worksite/Data/StationSet.asset");
            var sb = new StringBuilder($"D43 絕緣子髒汙計分格（v3.12）：面向子站點的 {def.faceColumns} 行裡、跪姿肩點（站點上方 {def.shoulderY} m）{def.reach} m 內手搆得到的格；桌面從站點眼高 {set.eyeHeight} m 檢查看不看得到\n");
            var bad = new System.Collections.Generic.List<string>();
            StationTests.WithScene(scene =>
            {
                Physics.SyncTransforms();
                var pts = Object.FindObjectsOfType<PantographSim.XR.XrTeleportPoint>(true).Where(p => p.gameObject.scene == scene).ToDictionary(p => p.PointId, p => p.transform);
                var masks = Object.FindObjectsOfType<DirtMask>(true).Where(m => m.gameObject.scene == scene && m.Id.StartsWith("ip.roof.insulator_")).ToDictionary(m => m.Id);
                foreach (var a in subs)
                {
                    int n = int.Parse(a.targetId.Substring(a.targetId.LastIndexOf('_') + 1));
                    Assert.IsTrue(masks.TryGetValue(a.targetId, out var mask), a.targetId + " 要在場景");
                    Assert.IsTrue(pts.TryGetValue(a.standPointId, out var st), a.standPointId + " 要在場景");
                    Assert.AreEqual(def.scoreMask[n - 1], mask.ScoreMask, a.targetId + "：場景的計分格要和 InsulatorDirtDef 一致");
                    var score = def.scoreMask[n - 1];
                    int cells = score.Count(ch => ch == '1');
                    var col = mask.GetComponentInChildren<CapsuleCollider>(true);
                    bool wasOn = col.enabled; col.enabled = true; Physics.SyncTransforms();
                    var eyes = new[] { st.position + Vector3.up * PostureDef.Load().standEye, st.position + Vector3.up * PostureDef.Load().kneelEye };   // 第 8.3 點：站或跪任一個看得到
                    int seen = 0;
                    for (int i = 0; i < score.Length; i++)
                    {
                        if (score[i] != '1') continue;
                        var cell = InsulatorDirtBake.CellWorld(mask.transform, i % def.cols, i / def.cols, def.cols, def.rows);
                        bool anyEye = false;
                        foreach (var eye in eyes)
                        {
                            var dir = cell - eye;
                            if (Physics.Raycast(eye, dir.normalized, out var hit, dir.magnitude + 0.01f, PantographSim.Core.SceneLayers.InteractMask, QueryTriggerInteraction.Collide) && hit.collider == col && (hit.point - cell).magnitude < 0.02f) anyEye = true;
                        }
                        if (anyEye) seen++;
                    }
                    col.enabled = wasOn;
                    sb.AppendLine($"{a.targetId}（{a.subId}）從 {a.standPointId}：計分格 {cells} 格（面向的行 {def.faceMask[n - 1]}），桌面看得到 {seen}/{cells}＝{(cells > 0 ? seen / (float)cells : 0f):P0}");
                    if (cells < def.minScoreCells) bad.Add($"{a.targetId} 計分格 {cells} < {def.minScoreCells}");
                    if (seen < cells) bad.Add($"{a.targetId} 桌面只看得到 {seen}/{cells} 個計分格");
                }
            });
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/InsulatorReach.txt", (sb.ToString() + (bad.Count == 0 ? "全部通過\n" : "不通過：\n" + string.Join("\n", bad) + "\n")).Replace("\r\n", "\n"));
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
