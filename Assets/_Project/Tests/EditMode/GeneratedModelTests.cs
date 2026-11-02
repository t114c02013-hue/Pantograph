using System.IO;
using System.Linq;
using NUnit.Framework;
using PantographSim.Data;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>R1.5a a2（4.4）：Blender 產生的 700T 車廂與設備模型。</summary>
    public class GeneratedModelTests
    {
        const string Generated = "Assets/_Project/Worksite/Models/Generated";
        const string Rigging = "Assets/_Project/Parts/PantographAssembly/Models";
        static RoofSceneDef Def => AssetDatabase.LoadAssetAtPath<RoofSceneDef>("Assets/_Project/Modules/Worksite/Data/RoofSceneDef.asset");
        static GameObject[] Models(string dir) => AssetDatabase.FindAssets("t:Model", new[] { dir }).Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).ToArray();

        static Bounds WorldBounds(GameObject prefab)
        {
            var go = (GameObject)Object.Instantiate(prefab);
            try { return go.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }); }
            finally { Object.DestroyImmediate(go); }
        }

        static int Tris(GameObject prefab) => prefab.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh != null ? (int)f.sharedMesh.triangles.Length / 3 : 0);

        [Test]
        public void CarBody_Width338_Length25_AlongZ()
        {
            var car = AssetDatabase.LoadAssetAtPath<GameObject>(Generated + "/ENV_CarBody.fbx");
            Assert.IsNotNull(car, "還沒產生 ENV_CarBody.fbx（跑 gen_700t.py）");
            var b = WorldBounds(car);
            Assert.AreEqual(Def.carBodyHalfWidth * 2f, b.size.x, 0.02f, "車寬");
            Assert.AreEqual(Def.carLength, b.size.z, 0.05f, "車長要在 Z 軸");
            Assert.Less(b.size.y, b.size.z);
        }

        [Test]
        public void Triangles_Within55k_Car35k_Others18k_AtMost7Materials()
        {
            var all = Models(Generated);
            Assert.Greater(all.Length, 10);
            int car = Tris(all.First(m => m.name == "ENV_CarBody"));
            int others = all.Where(m => m.name != "ENV_CarBody").Sum(Tris);
            Assert.LessOrEqual(car, 35000, "車廂三角面");
            Assert.LessOrEqual(others, 18000, "天車、台架等");
            Assert.LessOrEqual(car + others, 55000, "總三角面");
            var mats = all.Concat(Models(Rigging)).SelectMany(m => m.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Where(x => x != null).Select(x => x.name).Distinct().ToList();
            Assert.LessOrEqual(mats.Count, 7, string.Join(",", mats));
            Assert.IsTrue(mats.All(n => new[] { "Paint", "Roof", "Walkway", "Glass", "Dark", "Metal", "Insulator" }.Contains(n)), "要用外部材質 Materials/Worksite：" + string.Join(",", mats));
        }

        [Test]
        public void GeneratedFbx_NoColliders_UnitScale_ImportSettings()
        {
            foreach (var m in Models(Generated).Concat(Models(Rigging)))
            {
                Assert.IsEmpty(m.GetComponentsInChildren<Collider>(true), m.name + " 不能帶 collider");
                foreach (var t in m.GetComponentsInChildren<Transform>(true))
                    Assert.AreEqual(1f, t.localScale.x * t.localScale.y * t.localScale.z, 1e-3f, $"{m.name}/{t.name} 縮放要是 1");
                var imp = (ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(m));
                Assert.AreEqual(1f, imp.globalScale); Assert.IsTrue(imp.useFileScale); Assert.IsFalse(imp.addCollider); Assert.IsFalse(imp.isReadable);
            }
        }

        [Test]
        public void CarParams_MatchRoofSceneDef_Hash()
        {
            Assert.AreEqual(Def.ToParamsJson(), File.ReadAllText("Assets/_Project/Worksite/Data/car_params.json"), "改了 RoofSceneDef 要重新匯出（Tools/Worksite/Export Car Params）並重跑 Blender");
            Assert.AreEqual(Def.ParamsHash(), File.ReadAllText("Assets/_Project/Worksite/Data/car_params.hash.txt").Trim());
        }

        [Test]
        public void BlenderReports_Exist_WithVersionTrianglesBounds()
        {
            var car = File.ReadAllText("Logs/CarModelReport.json");
            StringAssert.Contains("\"blender\"", car); StringAssert.Contains("\"triangles\"", car); StringAssert.Contains("\"boundsMin\"", car); StringAssert.Contains("ENV_CarBody", car);
            var pan = File.ReadAllText("Logs/PanRiggingModelReport.json");
            StringAssert.Contains("\"triangles\"", pan);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(Rigging + "/PAN_Rigging.fbx"));
        }
    }
}
