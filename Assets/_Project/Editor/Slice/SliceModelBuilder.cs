using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Parts.CarbonStrip;
using PantographSim.Parts.Common;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// W05：碳滑板切片用的模型與 Prefab（全部由程式產生，可重跑）。
    /// ・PF_CarbonStrip_L/_R：取可動模型 PanHead 下的 CarbonStrip_2（肘側）/ CarbonStrip_1（外側）網格；軸心在中心，+X 沿長邊，+Z 朝外（斜面側）。
    ///   含 3 個量測點（距端 15 cm／中點）、4 個螺栓孔、「此側朝外」箭頭。
    /// ・PF_StripBolt：M8×25 六角頭（對邊 13 mm）＋平墊圈＋螺桿，primitive 與程式網格組成，含防鬆標記線。
    /// 數值皆為示例值。輸出報告 Logs/SliceModelReport.txt。
    /// </summary>
    public static class SliceModelBuilder
    {
        public const string PrefabFolder = "Assets/_Project/Modules/Parts/CarbonStrip/Prefabs";
        const string ModelFolder = "Assets/_Project/Modules/Parts/CarbonStrip/Models";
        public const string MaterialFolder = "Assets/_Project/Modules/Parts/CarbonStrip/Materials";
        public static string StripPrefabPath(string side) => $"{PrefabFolder}/PF_CarbonStrip_{side}.prefab";
        public const string BoltPrefabPath = PrefabFolder + "/PF_StripBolt.prefab";
        const string MeshAssetPath = ModelFolder + "/SliceMeshes.asset";

        // 螺栓配置（示例值）：距端 3 cm、距中心線 ±2.5 cm
        public const float BoltFromEnd = 0.03f, BoltOffset = 0.025f, MeasureFromEnd = 0.15f;

        /// <summary>螺栓編號 1 左外、2 右內、3 右外、4 左內（外＝+Z＝斜面側；左＝−X）。回傳滑板座標。</summary>
        public static Vector3 BoltLocal(int n, float halfLength, float topY)
        {
            float x = halfLength - BoltFromEnd;
            switch (n)
            {
                case 1: return new Vector3(-x, topY, +BoltOffset);
                case 2: return new Vector3(+x, topY, -BoltOffset);
                case 3: return new Vector3(+x, topY, +BoltOffset);
                default: return new Vector3(-x, topY, -BoltOffset);
            }
        }

        [MenuItem("PantographSim/Slice/Build Models")]
        public static void BuildMenu() => Debug.Log("[SliceModelBuilder]\n" + Build());

        public static string Build()
        {
            var sb = new StringBuilder();
            if (PantographRigBuilder.NeedsBuild()) PantographRigBuilder.Build();
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(PantographRigBuilder.PrefabPath);
            var panHead = rig.GetComponentsInChildren<Transform>(true).First(t => t.name == "PanHead");
            Directory.CreateDirectory(PrefabFolder); Directory.CreateDirectory(ModelFolder); Directory.CreateDirectory(MaterialFolder);

            var mats = Materials();
            var meshes = new List<Mesh>();
            var hex = HexPrism(0.013f, 0.0053f); hex.name = "M8_HexHead"; meshes.Add(hex);
            SaveMeshes(meshes);

            // ---- 碳滑板 ----
            foreach (var (side, source) in new[] { ("L", "CarbonStrip_2"), ("R", "CarbonStrip_1") })
            {
                var src = panHead.Find(source);
                var mesh = src.GetComponent<MeshFilter>().sharedMesh;
                var b = mesh.bounds;
                sb.AppendLine($"PF_CarbonStrip_{side} ← {source}：尺寸 {b.size.x:F3} × {b.size.y:F3} × {b.size.z:F3} m");
                sb.AppendLine("  截面：" + Profile(mesh));
                BuildStripPrefab(side, mesh, src.GetComponent<MeshRenderer>().sharedMaterials, mats, b);
            }

            // ---- 螺栓 ----
            BuildBoltPrefab(hex, mats);
            sb.AppendLine("PF_StripBolt：M8×25 六角頭 對邊 13 mm、高 5.3 mm；墊圈 Ø16×1.6 mm；螺桿 Ø8×25 mm（示例值）");
            sb.AppendLine($"螺栓位置：距端 {BoltFromEnd * 100:0} cm、距中心線 ±{BoltOffset * 100:0.0} cm，編號 1 左外、2 右內、3 右外、4 左內（外＝斜面側）");
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/SliceModelReport.txt", sb.ToString());
            return sb.ToString();
        }

        /// <summary>量網格頂面在 +Z 與 −Z 兩側邊緣的高度，判斷截面是否左右對稱（是否有斜面）。</summary>
        static string Profile(Mesh mesh)
        {
            var v = mesh.vertices;
            float zMin = v.Min(p => p.z), zMax = v.Max(p => p.z), w = zMax - zMin;
            float TopIn(System.Func<Vector3, bool> band) => v.Where(band).Select(p => p.y).DefaultIfEmpty(float.NaN).Max();
            float topNearMax = TopIn(p => p.z > zMax - w * 0.15f);
            float topNearMin = TopIn(p => p.z < zMin + w * 0.15f);
            float topCenter = TopIn(p => Mathf.Abs(p.z - (zMin + zMax) / 2) < w * 0.25f);
            bool symmetric = Mathf.Abs(topNearMax - topNearMin) < 0.0015f;
            string center = float.IsNaN(topCenter) ? "（中央無頂點）" : $"{topCenter * 1000:F1} mm";
            return $"頂點數 {v.Length}；頂面高度 −Z 邊 {topNearMin * 1000:F1} mm、中央 {center}、+Z 邊 {topNearMax * 1000:F1} mm → " +
                   (symmetric ? "左右對稱，無法從外形看出斜面，加貼「此側朝外」箭頭" : (topNearMax < topNearMin ? "+Z 側較低（斜面）" : "−Z 側較低（斜面）"));
        }

        static void BuildStripPrefab(string side, Mesh mesh, Material[] materials, Dictionary<string, Material> mats, Bounds b)
        {
            var root = new GameObject($"PF_CarbonStrip_{side}");
            try
            {
                float half = b.size.x / 2f, top = b.max.y;
                var visual = new GameObject("Visual").transform;
                visual.SetParent(root.transform, false);
                // 右側（外側滑板）在場景中整體轉 180°，讓 +Z 朝外；外觀反轉回去以保持原模型幾何
                if (side == "R") visual.localRotation = Quaternion.Euler(0, 180, 0);
                var meshGo = new GameObject("Mesh");
                meshGo.transform.SetParent(visual, false);
                meshGo.AddComponent<MeshFilter>().sharedMesh = mesh;
                meshGo.AddComponent<MeshRenderer>().sharedMaterials = materials;

                // 螺栓孔放在外觀底下：拿在手上轉向時孔位跟著轉，吸附判定用的就是當下的孔位
                var holesRoot = new GameObject("Holes").transform;
                holesRoot.SetParent(visual, false);
                var holes = new Transform[4];
                for (int n = 1; n <= 4; n++)
                {
                    var h = new GameObject("Hole_" + n).transform;
                    h.SetParent(holesRoot, false);
                    h.localPosition = BoltLocal(n, half, top);
                    holes[n - 1] = h;
                }

                // 此側朝外箭頭（+Z 側頂面，橘色三角＋桿）
                var arrow = new GameObject("OutwardArrow");
                arrow.transform.SetParent(root.transform, false);
                arrow.transform.localPosition = new Vector3(0, top + 0.0012f, b.max.z - 0.012f);
                var arrowMarker = arrow.AddComponent<InteractionPointMarker>();
                arrowMarker.Configure($"ip.carbon_strip_{side}.outward", "此側朝外");
                var shaft = Prim(PrimitiveType.Cube, "Shaft", arrow.transform, new Vector3(0, 0, -0.006f), new Vector3(0.006f, 0.001f, 0.014f), mats["arrow"]);
                var headL = Prim(PrimitiveType.Cube, "HeadL", arrow.transform, new Vector3(-0.004f, 0, 0.003f), new Vector3(0.003f, 0.001f, 0.012f), mats["arrow"]);
                headL.transform.localRotation = Quaternion.Euler(0, 40, 0);
                var headR = Prim(PrimitiveType.Cube, "HeadR", arrow.transform, new Vector3(0.004f, 0, 0.003f), new Vector3(0.003f, 0.001f, 0.012f), mats["arrow"]);
                headR.transform.localRotation = Quaternion.Euler(0, -40, 0);
                foreach (var c in arrow.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

                // 量測點
                foreach (var (p, x) in new[] { ("left", -half + MeasureFromEnd), ("mid", 0f), ("right", half - MeasureFromEnd) })
                {
                    var mp = Prim(PrimitiveType.Sphere, "measure_" + p, root.transform, new Vector3(x, top + 0.002f, 0), Vector3.one * 0.016f, mats["measure"]);
                    mp.AddComponent<InteractionPointMarker>().Configure($"ip.carbon_strip_{side}.measure_{p}", $"{(side == "L" ? "靠關節" : "外側")}滑板 量測點（{(p == "left" ? "左" : p == "mid" ? "中" : "右")}）");
                    mp.AddComponent<Highlightable>();
                }

                var col = root.AddComponent<BoxCollider>();
                col.center = b.center; col.size = b.size;
                var removable = root.AddComponent<RemovableBehaviour>();
                removable.Configure($"part.carbon_strip_{side}", side == "L" ? "靠關節碳滑板" : "外側碳滑板");
                removable.Setup(holes, new Transform[0], visual);
                root.AddComponent<CarbonStripBehaviour>().Setup($"part.carbon_strip_{side}");
                var label = root.AddComponent<StripLabel>();
                label.modelCode = "CS-1100-74A";
                root.AddComponent<Highlightable>();
                PrefabUtility.SaveAsPrefabAsset(root, StripPrefabPath(side));
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void BuildBoltPrefab(Mesh hex, Dictionary<string, Material> mats)
        {
            var root = new GameObject("PF_StripBolt");
            try
            {
                var col = root.AddComponent<BoxCollider>();
                col.center = new Vector3(0, 0.004f, 0); col.size = new Vector3(0.02f, 0.012f, 0.02f);
                var visual = new GameObject("Visual").transform;
                visual.SetParent(root.transform, false);
                var head = new GameObject("HexHead");
                head.transform.SetParent(visual, false);
                head.transform.localPosition = new Vector3(0, 0.0016f, 0);
                head.AddComponent<MeshFilter>().sharedMesh = hex;
                head.AddComponent<MeshRenderer>().sharedMaterial = mats["steel"];
                Prim(PrimitiveType.Cylinder, "Washer", visual, new Vector3(0, 0.0008f, 0), new Vector3(0.016f, 0.0008f, 0.016f), mats["washer"]);
                Prim(PrimitiveType.Cylinder, "Shank", visual, new Vector3(0, -0.0125f, 0), new Vector3(0.008f, 0.0125f, 0.008f), mats["steel"]);
                var mark = Prim(PrimitiveType.Cube, "MarkLine", visual, new Vector3(0.006f, 0.0075f, 0), new Vector3(0.016f, 0.0008f, 0.0015f), mats["mark"]);
                foreach (var c in visual.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                mark.SetActive(false);   // 畫了標記才顯示
                var fastener = root.AddComponent<FastenerBehaviour>();
                fastener.SetVisual(visual, mark);
                root.AddComponent<Highlightable>();
                PrefabUtility.SaveAsPrefabAsset(root, BoltPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>六角柱網格：對邊距離 acrossFlats、高度 height，底面在 y=0。</summary>
        static Mesh HexPrism(float acrossFlats, float height)
        {
            float r = acrossFlats / 2f / Mathf.Cos(Mathf.PI / 6f);
            var verts = new List<Vector3>(); var tris = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                float a0 = Mathf.PI / 3f * i, a1 = Mathf.PI / 3f * (i + 1);
                var p0 = new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r); var p1 = new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
                var up = Vector3.up * height;
                int s = verts.Count;
                verts.AddRange(new[] { p0, p1, p1 + up, p0 + up });
                tris.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
                int t = verts.Count;
                verts.AddRange(new[] { up, p0 + up, p1 + up });
                tris.AddRange(new[] { t, t + 2, t + 1 });
                int bt = verts.Count;
                verts.AddRange(new[] { Vector3.zero, p0, p1 });
                tris.AddRange(new[] { bt, bt + 1, bt + 2 });
            }
            var m = new Mesh();
            m.SetVertices(verts); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static void SaveMeshes(List<Mesh> meshes)
        {
            var existing = AssetDatabase.LoadAllAssetsAtPath(MeshAssetPath).OfType<Mesh>().ToDictionary(m => m.name);
            if (existing.Count == 0)
            {
                AssetDatabase.CreateAsset(meshes[0], MeshAssetPath);
                foreach (var m in meshes.Skip(1)) AssetDatabase.AddObjectToAsset(m, MeshAssetPath);
                return;
            }
            for (int i = 0; i < meshes.Count; i++)
            {
                if (existing.TryGetValue(meshes[i].name, out var old))
                {
                    old.Clear();
                    old.SetVertices(meshes[i].vertices); old.SetTriangles(meshes[i].triangles, 0);
                    old.RecalculateNormals(); old.RecalculateBounds();
                    meshes[i] = old;
                }
                else AssetDatabase.AddObjectToAsset(meshes[i], MeshAssetPath);
            }
        }

        public static Dictionary<string, Material> Materials()
        {
            var specs = new Dictionary<string, Color>
            {
                { "steel", new Color(0.72f, 0.74f, 0.78f) }, { "washer", new Color(0.82f, 0.82f, 0.8f) }, { "mark", new Color(0.9f, 0.1f, 0.08f) },
                { "arrow", new Color(1f, 0.55f, 0.05f) }, { "measure", new Color(0.2f, 0.75f, 1f) }, { "ground", new Color(0.25f, 0.7f, 0.2f) },
                { "groundStripe", new Color(0.95f, 0.85f, 0.1f) }, { "panel", new Color(0.25f, 0.28f, 0.33f) }, { "table", new Color(0.55f, 0.42f, 0.3f) },
                { "tray", new Color(0.15f, 0.35f, 0.6f) }, { "shelf", new Color(0.45f, 0.45f, 0.48f) }, { "roof", new Color(0.62f, 0.64f, 0.66f) },
                { "platform", new Color(0.5f, 0.52f, 0.55f) }, { "crack", new Color(0.05f, 0.05f, 0.05f) }, { "led", new Color(0.25f, 0.05f, 0.05f) },
                { "paper", new Color(0.95f, 0.95f, 0.9f) }, { "tool", new Color(0.9f, 0.6f, 0.1f) }
            };
            var result = new Dictionary<string, Material>();
            foreach (var kv in specs)
            {
                string path = $"{MaterialFolder}/MAT_Slice_{kv.Key}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
                m.color = kv.Value;
                if (kv.Key == "steel" || kv.Key == "washer") { m.SetFloat("_Metallic", 0.7f); m.SetFloat("_Glossiness", 0.6f); }
                EditorUtility.SetDirty(m);
                result[kv.Key] = m;
            }
            foreach (var (key, color) in new[] { ("checkPoint", new Color(1f, 0.2f, 0.15f, 1f)), ("targetZone", new Color(0.2f, 1f, 0.35f, 0.35f)), ("targetFrame", new Color(0.2f, 1f, 0.35f, 0.9f)) })
            {
                string path = $"{MaterialFolder}/MAT_Slice_{key}.mat";
                var shader = key == "targetZone" ? Shader.Find("Legacy Shaders/Transparent/Diffuse") : Shader.Find("Standard");
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
                m.shader = shader; m.color = color;
                if (shader.name == "Standard") { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * 0.6f); }
                EditorUtility.SetDirty(m);
                result[key] = m;
            }
            string dirtPath = $"{MaterialFolder}/MAT_Slice_dirt.mat";
            var dirt = AssetDatabase.LoadAssetAtPath<Material>(dirtPath);
            if (dirt == null) { dirt = new Material(Shader.Find("Unlit/Transparent")); AssetDatabase.CreateAsset(dirt, dirtPath); }
            result["dirt"] = dirt;
            return result;
        }
    }
}
