using System.Linq;
using System.Text;
using NUnit.Framework;
using PantographSim.Data;
using PantographSim.Editor;
using PantographSim.Parts.Common;
using PantographSim.XR;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>
    /// R1.5b b4（D43）：絕緣子髒汙殼（16×6 格）從 r3f 指定的子站點（桌面：站點眼高的攝影機）看得到幾成——
    /// 每格中心從眼睛打射線，第一個打到的是這支的 COL_Dirt 才算擦得到。結果寫 Logs/InsulatorReach.txt（< 95 % 要回報，不降門檻）。
    /// VR 可以伸手繞到背面，這裡只量桌面的下限。
    /// </summary>
    public class InsulatorReachTests
    {
        [Test]
        public void InsulatorDirt_DesktopReachFromAssignedStation_Measured()
        {
            var prc = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath("periodic"));
            var step = prc.steps.First(s => s.stepId == "STP_RT_02_InsulationTest");
            var subs = step.subActions.Where(a => a.subId.StartsWith("r3f")).ToList();
            Assert.AreEqual(4, subs.Count, "r3f 展開成 4 支");
            float eye = AssetDatabase.LoadAssetAtPath<StationSet>("Assets/_Project/Modules/Worksite/Data/StationSet.asset").eyeHeight;
            var sb = new StringBuilder("D43 絕緣子髒汙殼：從 r3f 指定子站點（眼高 " + eye + " m）看得到的格子比例（桌面下限；VR 可伸手繞背面）\n");
            StationTests.WithScene(scene =>
            {
                Physics.SyncTransforms();
                var pts = Object.FindObjectsOfType<XrTeleportPoint>(true).Where(p => p.gameObject.scene == scene).ToDictionary(p => p.PointId, p => p.transform);
                var masks = Object.FindObjectsOfType<DirtMask>(true).Where(m => m.gameObject.scene == scene && m.Id.StartsWith("ip.roof.insulator_")).ToDictionary(m => m.Id);
                foreach (var a in subs)
                {
                    Assert.IsTrue(masks.TryGetValue(a.targetId, out var mask), a.targetId + " 要在場景");
                    Assert.IsTrue(pts.TryGetValue(a.standPointId, out var st), a.standPointId + " 要在場景");
                    var col = mask.GetComponentInChildren<CapsuleCollider>(true);
                    bool wasOn = col.enabled; col.enabled = true;
                    var origin = st.position + Vector3.up * eye;
                    int seen = 0, total = 0;
                    var firstHits = new System.Collections.Generic.Dictionary<string, int>();
                    for (int r = 0; r < 6; r++)
                        for (int c = 0; c < DirtMask.Cols; c++)
                        {
                            float ang = ((c + 0.5f) / DirtMask.Cols - 0.5f) * 2f * Mathf.PI, y = ((r + 0.5f) / 6f - 0.5f) * 2f;
                            var cell = mask.transform.TransformPoint(new Vector3(Mathf.Cos(ang) * 0.5f, y, Mathf.Sin(ang) * 0.5f));
                            total++;
                            var dir = cell - origin;
                            bool any = Physics.Raycast(origin, dir.normalized, out var hit, dir.magnitude + 0.01f, PantographSim.Core.SceneLayers.InteractMask, QueryTriggerInteraction.Collide);   // 和清潔布同一個射線設定
                            if (any && hit.collider == col && (hit.point - cell).magnitude < 0.02f) seen++;
                            else { string k = any ? hit.collider.name + "@" + LayerMask.LayerToName(hit.collider.gameObject.layer) : "（沒打到）"; firstHits[k] = firstHits.TryGetValue(k, out var nn) ? nn + 1 : 1; }
                        }
                    col.enabled = wasOn;
                    float frac = seen / (float)total;
                    sb.AppendLine($"{a.targetId}（{a.subId}）從 {a.standPointId}：{seen}/{total} 格＝{frac:P0}{(frac < 0.95f ? "　＜ 95 %：桌面從這一站擦不到門檻" : "")}；擋住的：{string.Join("、", firstHits.OrderByDescending(x => x.Value).Take(4).Select(x => x.Key + "×" + x.Value))}");
                }
            });
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/InsulatorReach.txt", sb.ToString());
            Debug.Log(sb.ToString());
        }
    }
}
