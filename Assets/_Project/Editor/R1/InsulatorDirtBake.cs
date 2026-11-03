using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Parts.Common;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1.5b b4（D43，model-spec v3.12 §7.7.1c）：建置時烘焙絕緣子髒汙的計分格到 InsulatorDirtDef.scoreMask。
    /// 每支：子站點讀 PRC_periodic 的 RT_02 r3f（CSV，n1＝base_w.s、n2＝base_e.s、n3＝base_e.n、n4＝base_w.n），站點位置讀 StationDef；
    /// 16 行裡取面向子站點的 faceColumns 行；格子從跪姿肩點（站點上方 shoulderY）往外 reach 內的手部樣點打射線（和清潔布同一個射線設定），
    /// 第一個打到的是這支的 COL_Dirt 才算可達（沒被吊耳、高壓端子、底架擋住）；VR、桌面同一套規則，所以也要從桌面站點眼高（StationSet.eyeHeight）看得到。
    /// 每支不到 minScoreCells 就讓建置失敗。
    /// </summary>
    public static class InsulatorDirtBake
    {
        public const string AssetPath = "Assets/_Project/Modules/Parts/_Common/Resources/InsulatorDirt.asset";

        /// <summary>格子 (c, r) 在殼上的世界位置（和 CleaningClothTool 的圓柱 UV 相反）。</summary>
        public static Vector3 CellWorld(Transform shell, int c, int r, int cols, int rows)
        {
            float ang = ((c + 0.5f) / cols - 0.5f) * 2f * Mathf.PI, y = ((r + 0.5f) / rows - 0.5f) * 2f;
            return shell.TransformPoint(new Vector3(Mathf.Cos(ang) * 0.5f, y, Mathf.Sin(ang) * 0.5f));
        }

        public static void Build(StringBuilder sb)
        {
            var def = AssetDatabase.LoadAssetAtPath<InsulatorDirtDef>(AssetPath);
            if (def == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
                def = ScriptableObject.CreateInstance<InsulatorDirtDef>();
                AssetDatabase.CreateAsset(def, AssetPath);
            }
            var prc = AssetDatabase.LoadAssetAtPath<ProcedureDefinition>(StepsCsvImporter.ProcedurePath("periodic"));
            var subs = prc.steps.First(s => s.stepId == "STP_RT_02_InsulationTest").subActions.Where(a => a.subId.StartsWith("r3f")).ToList();
            var stations = AssetDatabase.FindAssets("t:StationDef").Select(g => AssetDatabase.LoadAssetAtPath<StationDef>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x != null).GroupBy(x => x.id).ToDictionary(g => g.Key, g => g.First());
            var masks = Object.FindObjectsOfType<DirtMask>(true).Where(m => m.Id.StartsWith("ip.roof.insulator_")).ToDictionary(m => m.Id);
            var d = R1Data.RoofScene;
            float eyeH = AssetDatabase.LoadAssetAtPath<StationSet>("Assets/_Project/Modules/Worksite/Data/StationSet.asset").eyeHeight;
            Physics.SyncTransforms();
            var score = new string[4]; var face = new string[4];
            for (int n = 1; n <= 4; n++)
            {
                string id = "ip.roof.insulator_" + n;
                var sub = subs.FirstOrDefault(a => a.targetId == id) ?? throw new System.Exception($"InsulatorDirtBake：RT_02 r3f 沒有 {id}");
                if (!masks.TryGetValue(id, out var mask)) throw new System.Exception($"InsulatorDirtBake：場景沒有 {id}");
                if (!stations.TryGetValue(sub.standPointId, out var st)) throw new System.Exception($"InsulatorDirtBake：沒有站點 {sub.standPointId}");
                var col = mask.GetComponentInChildren<CapsuleCollider>(true);
                bool wasOn = col.enabled; col.enabled = true; Physics.SyncTransforms();
                var shoulder = new Vector3(st.positionXZ.x, d.RoofSurfaceY(st.positionXZ.x) + def.shoulderY, st.positionXZ.y);
                var eye = new Vector3(shoulder.x, d.RoofSurfaceY(st.positionXZ.x) + eyeH, shoulder.z);
                var axis = mask.transform.position;
                var toward = Vector3.ProjectOnPlane(shoulder - axis, Vector3.up).normalized;
                var cols = Enumerable.Range(0, def.cols).OrderByDescending(c => Vector3.Dot(Vector3.ProjectOnPlane(CellWorld(mask.transform, c, def.rows / 2, def.cols, def.rows) - axis, Vector3.up).normalized, toward)).Take(def.faceColumns).OrderBy(c => c).ToArray();
                var m = new char[def.cols * def.rows];
                for (int i = 0; i < m.Length; i++) m[i] = '0';
                foreach (int c in cols)
                    for (int r = 0; r < def.rows; r++)
                    {
                        var cell = CellWorld(mask.transform, c, r, def.cols, def.rows);
                        if (Reachable(cell, shoulder, def.reach, col) && Hits(eye, cell, col)) m[r * def.cols + c] = '1';
                    }
                col.enabled = wasOn;
                score[n - 1] = new string(m); face[n - 1] = string.Join(",", cols);
                int count = m.Count(ch => ch == '1');
                sb.AppendLine($"絕緣子髒汙計分格（v3.12）{id}：子站點 {sub.standPointId}，面向的行 {face[n - 1]}，可達 {count} 格（至少 {def.minScoreCells}）");
                if (count < def.minScoreCells) throw new System.Exception($"InsulatorDirtBake：{id} 從 {sub.standPointId} 只有 {count} 個計分格（至少 {def.minScoreCells}）");
                mask.SetScore(def, n - 1, mask.transform.Find("WrapCloth"));
                EditorUtility.SetDirty(mask);
            }
            def.scoreMask = score; def.faceMask = face;
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
        }

        /// <summary>從肩點往外 reach 內取手部樣點（朝格子的方向、上下左右錯開），任一樣點打射線第一個打到的是這支的 COL_Dirt、而且就在這一格。</summary>
        static bool Reachable(Vector3 cell, Vector3 shoulder, float reach, Collider col)
        {
            var dir = (cell - shoulder).normalized;
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            float dist = (cell - shoulder).magnitude;
            foreach (float k in new[] { 0.4f, 0.6f, 0.8f })
                foreach (float a in new[] { 0f, -0.15f, 0.15f })
                    foreach (float b in new[] { 0f, -0.15f, 0.15f })
                    {
                        var h = shoulder + dir * Mathf.Min(k * reach, Mathf.Max(0f, dist - 0.05f)) + side * a + Vector3.up * b;
                        if ((h - shoulder).magnitude > reach + 1e-4f) continue;
                        if (Hits(h, cell, col)) return true;
                    }
            return false;
        }

        /// <summary>從 from 打向格子（和清潔布同一個射線設定），第一個打到的是這支的 COL_Dirt、而且就在這一格。</summary>
        public static bool Hits(Vector3 from, Vector3 cell, Collider col)
        {
            var ray = cell - from;
            return Physics.Raycast(from, ray.normalized, out var hit, ray.magnitude + 0.01f, SceneLayers.InteractMask, QueryTriggerInteraction.Collide) && hit.collider == col && (hit.point - cell).magnitude < 0.02f;
        }
    }
}
