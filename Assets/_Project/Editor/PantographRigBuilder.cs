using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Parts.Common;
using PantographSim.Parts.Mechanism;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PantographSim.Editor
{
    /// <summary>
    /// 從外部模型（同一 FBX 內含升弓、降弓兩組依材質分的網格）自動做出可動的集電弓：
    /// 1. 升弓/降弓網格一一配對，拆成連通塊，求每塊從升弓到降弓的剛體運動。
    /// 2. 運動相同的連通塊歸為同一剛體段（底架、下臂、上臂、弓頭、拉桿…）。
    /// 3. 由兩段之間的相對轉動求出鉸接軸，組成關節階層；有兩端鉸接的連桿用 AimLink 對準。
    /// 4. 輸出網格與 Prefab 到 ExternalModels/Sandbox（原始 FBX 不動），並驗證降弓姿態誤差。
    /// </summary>
    public static class PantographRigBuilder
    {
        public const string SourcePath = "Assets/_Project/ExternalModels/Vendor/3d66/EXT_3d66_JJI54552119072_v01.fbx";
        public const string OutputFolder = "Assets/_Project/ExternalModels/Sandbox/PantographRig";
        public const string PrefabPath = OutputFolder + "/EXT_3d66_Pantograph_Rigged_v01.prefab";
        const string MeshPath = OutputFolder + "/EXT_3d66_Pantograph_Rigged_v01_Meshes.asset";
        const string ReportPath = "Logs/PantographRigReport.txt";

        const float HingeTolerance = 0.05f;   // 鉸接軸距離零件 5 cm 內才算可靠
        const string VersionTag = "PantographRigBuilder v4";   // 建模演算法改版時遞增，舊輸出會自動重建

        /// <summary>可動模型不存在或是舊版演算法產生的。</summary>
        public static bool NeedsBuild()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) return true;
            return AssetImporter.GetAtPath(PrefabPath)?.userData != VersionTag;
        }

        class Island
        {
            public Renderer source; public int[] verts; public Vector3[] a, b; public Vector3 ca;
            public Quaternion q; public Vector3 t; public float residual; public float spread; public int segment = -1;
            public Vector3 Apply(Vector3 p) => q * p + t;
        }

        class Segment
        {
            public int index; public string name; public Quaternion q; public Vector3 t;
            public List<Island> islands = new List<Island>();
            public Vector3[] points; public Vector3 centroid;
            public int parent = -1; public float hingeScore; public Vector3 pivot; public Vector3 axis = Vector3.right; public float angle; public Vector3 shift;
            public int aimTarget = -1; public Vector3 aimPoint; public float aimScore;
            public Transform transform;
        }

        struct Hinge { public bool valid; public bool rigid; public Vector3 point; public Vector3 axis; public float angle; public Vector3 shift; }

        [MenuItem("Tools/集電弓/重建可動模型")]
        public static void BuildFromMenu()
        {
            if (Build()) EditorUtility.DisplayDialog("集電弓", "可動模型已建立，詳細結果見 " + ReportPath, "OK");
        }

        public static bool Build()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            if (asset == null) { Debug.LogError($"[PantographRigBuilder] 找不到模型 {SourcePath}"); return false; }

            var sb = new StringBuilder();
            var temp = Object.Instantiate(asset);
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                // 1. 配對與連通塊
                var grouping = PoseGrouping.Compute(temp.GetComponentsInChildren<Renderer>(true));
                sb.AppendLine(grouping.Report);
                if (!grouping.Reliable) { sb.AppendLine("升降弓分組不可靠，停止。"); return Finish(sb, false); }

                var lowered = grouping.Lowered.ToList();
                var islands = new List<Island>();
                foreach (var r in grouping.Raised)
                {
                    var m = r.GetComponent<MeshFilter>().sharedMesh;
                    var tris = m.triangles;
                    var match = lowered.FirstOrDefault(l => l.sharedMaterial == r.sharedMaterial
                        && l.GetComponent<MeshFilter>().sharedMesh.vertexCount == m.vertexCount
                        && l.GetComponent<MeshFilter>().sharedMesh.triangles.SequenceEqual(tris));
                    if (match == null) { sb.AppendLine($"找不到對應的降弓網格：{r.name}"); return Finish(sb, false); }
                    lowered.Remove(match);
                    var va = m.vertices.Select(v => r.transform.TransformPoint(v)).ToArray();
                    var vb = match.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => match.transform.TransformPoint(v)).ToArray();
                    foreach (var verts in SplitIslands(tris, va))
                    {
                        var isl = new Island { source = r, verts = verts, a = verts.Select(i => va[i]).ToArray(), b = verts.Select(i => vb[i]).ToArray() };
                        Fit(isl);
                        islands.Add(isl);
                    }
                }
                sb.AppendLine($"連通塊：{islands.Count}");

                // 2. 依剛體運動分段
                var segments = new List<Segment>();
                foreach (var isl in islands.Where(i => i.spread > 0.02f && i.residual < 0.003f).OrderByDescending(i => i.verts.Length))
                {
                    var best = segments.Select(s => (s, d: Mismatch(s, isl))).OrderBy(x => x.d).FirstOrDefault();
                    if (best.s != null && best.d < 0.01f) best.s.islands.Add(isl);
                    else segments.Add(new Segment { index = segments.Count, q = isl.q, t = isl.t, islands = { isl } });
                }
                foreach (var isl in islands.Where(i => !segments.Any(s => s.islands.Contains(i))))
                {
                    var best = segments.OrderBy(s => MaxError(s, isl)).First();
                    isl.residual = MaxError(best, isl);
                    best.islands.Add(isl);
                }
                foreach (var s in segments)
                {
                    s.points = s.islands.SelectMany(i => i.a).ToArray();
                    s.centroid = s.points.Aggregate(Vector3.zero, (acc, p) => acc + p) / s.points.Length;
                }

                var root = segments.OrderByDescending(s => s.points.Length).First();
                sb.AppendLine($"剛體段：{segments.Count}，底架為最大的一段（{root.points.Length} 頂點）");

                // 3. 關節階層：從底架往外（廣度優先），鉸接軸同時貼近兩個零件才算真的鉸接
                var placed = new List<Segment> { root };
                var queue = new Queue<Segment>(); queue.Enqueue(root);
                while (queue.Count > 0)
                {
                    var p = queue.Dequeue();
                    var children = new List<Segment>();
                    foreach (var s in segments.Except(placed))
                    {
                        var h = RelativeHinge(p, s);
                        float score = HingeScore(p, s, h);
                        if (score > HingeTolerance) continue;
                        Attach(s, p, h, score);
                        children.Add(s);
                    }
                    placed.AddRange(children);
                    foreach (var c in children.OrderByDescending(c => c.points.Length)) queue.Enqueue(c);
                }
                // 找不到可靠鉸接的段：接到最接近的一段並標示
                while (placed.Count < segments.Count)
                {
                    var best = segments.Except(placed).SelectMany(s => placed.Select(p => (s, p, h: RelativeHinge(p, s))))
                        .Select(x => (x.s, x.p, x.h, score: HingeScore(x.p, x.s, x.h))).OrderBy(x => x.score).First();
                    if (float.IsInfinity(best.score)) { sb.AppendLine("有剛體段找不到任何鉸接關係，停止。"); return Finish(sb, false); }
                    Attach(best.s, best.p, best.h, best.score);
                    placed.Add(best.s);
                }
                root.pivot = root.centroid;

                // 命名：弓頭＝最高且不轉動；下臂＝接在底架上最大的一段；上臂＝接在下臂上最大的一段
                root.name = "Base";
                var panHead = segments.Where(s => s != root && Mathf.Abs(Absolute(root, s)) < 0.5f).OrderByDescending(s => s.centroid.y).FirstOrDefault();
                var lowerArm = segments.Where(s => s.parent == root.index).OrderByDescending(s => s.points.Length).FirstOrDefault();
                var upperArm = lowerArm == null ? null : segments.Where(s => s.parent == lowerArm.index).OrderByDescending(s => s.points.Length).FirstOrDefault();
                if (lowerArm != null) lowerArm.name = "LowerArm";
                if (upperArm != null && upperArm != panHead) upperArm.name = "UpperArm";
                if (panHead != null) panHead.name = "PanHead";
                int linkNo = 1;
                foreach (var s in segments.Where(s => s.name == null).OrderBy(s => s.centroid.y)) s.name = $"Rod{linkNo++}";
                var chain = new[] { root, lowerArm, upperArm, panHead }.Where(s => s != null).ToList();

                if (panHead != null)
                {
                    sb.AppendLine("PanHead 連通塊：");
                    int k = 0;
                    foreach (var isl in panHead.islands.OrderBy(i => i.ca.x))
                    {
                        var b = new Bounds(isl.a[0], Vector3.zero); foreach (var p in isl.a) b.Encapsulate(p);
                        var mat = isl.source.sharedMaterial;
                        string color = mat != null && mat.HasProperty("_Color") ? ColorUtility.ToHtmlStringRGB(mat.color) : "-";
                        string tex = mat != null && mat.mainTexture != null ? mat.mainTexture.name : "-";
                        sb.AppendLine($"  [{k++,2}] 頂點={isl.verts.Length,5} 中心={b.center:F3} 尺寸={b.size:F3} 色={color} 貼圖={tex} 材質={mat?.name}");
                    }
                }

                // 主鏈外的葉節點（拉桿、平衡桿）：另一端也有鉸接就加 AimLink
                var leaves = segments.Where(s => s != root && !chain.Contains(s) && segments.All(o => o.parent != s.index)).ToList();
                foreach (var s in leaves)
                {
                    s.aimScore = float.MaxValue;
                    foreach (var q in segments.Where(o => o != s && o.index != s.parent && !leaves.Contains(o)))
                    {
                        var h = RelativeHinge(q, s);
                        if (!h.valid || h.rigid) continue;
                        var point = ClosestOnAxis(s.centroid, h.point, h.axis);
                        if (Vector3.Distance(point, s.pivot) < 0.1f) continue;   // 太靠近主鉸接點，不是另一端
                        float score = HingeScore(q, s, h);
                        if (score < s.aimScore) { s.aimScore = score; s.aimTarget = q.index; s.aimPoint = point; }
                    }
                    if (s.aimScore > HingeTolerance) s.aimTarget = -1;
                }

                // 4. 建立網格與 Prefab
                Directory.CreateDirectory(OutputFolder);
                if (AssetDatabase.LoadAssetAtPath<Object>(MeshPath) != null) AssetDatabase.DeleteAsset(MeshPath);
                var rigRoot = new GameObject("EXT_3d66_Pantograph_Rigged_v01");
                try
                {
                    bool firstMesh = true;
                    void AddMesh(GameObject target, IEnumerable<Island> parts, Vector3 pivot)
                    {
                        var mesh = BuildMesh(parts, pivot, out var materials);
                        mesh.name = target.name;
                        if (firstMesh) { AssetDatabase.CreateAsset(mesh, MeshPath); firstMesh = false; }
                        else AssetDatabase.AddObjectToAsset(mesh, MeshPath);
                        target.AddComponent<MeshFilter>().sharedMesh = mesh;
                        target.AddComponent<MeshRenderer>().sharedMaterials = materials;
                    }

                    foreach (var s in segments.OrderBy(s => Depth(segments, s)))
                    {
                        var go = new GameObject(s.name);
                        s.transform = go.transform;
                        var parentT = s.parent >= 0 ? segments[s.parent].transform : rigRoot.transform;
                        go.transform.SetParent(parentT, false);
                        go.transform.position = s.pivot;

                        // 弓頭再拆出可維修的零件（碳滑板、托架含羊角、懸吊固定件），其餘留在本體
                        var subParts = s == panHead ? ClassifyPanHead(s, sb) : new List<(string name, List<Island> islands)>();
                        var used = new HashSet<Island>(subParts.SelectMany(p => p.islands));
                        AddMesh(go, s.islands.Where(i => !used.Contains(i)), s.pivot);
                        foreach (var (partName, partIslands) in subParts)
                        {
                            var child = new GameObject(partName);
                            child.transform.SetParent(go.transform, false);
                            var center = Encapsulate(partIslands).center;
                            child.transform.position = center;
                            AddMesh(child, partIslands, center);
                        }
                    }

                    var mech = rigRoot.AddComponent<PantographMechanism>();
                    var joints = segments.Where(s => s.parent >= 0).OrderBy(s => Depth(segments, s)).Select(s => new PantographMechanism.Joint
                    {
                        name = s.name, segment = s.transform, axis = s.axis, loweredAngle = s.angle, loweredOffset = s.shift,
                        restPosition = s.transform.localPosition, restRotation = Quaternion.identity
                    }).ToArray();
                    var aims = segments.Where(s => s.aimTarget >= 0).Select(s => new PantographMechanism.AimLink
                    {
                        name = s.name, segment = s.transform, anchor = s.aimPoint - s.pivot,
                        target = segments[s.aimTarget].transform, targetAnchor = s.aimPoint - segments[s.aimTarget].pivot, axis = s.axis
                    }).ToArray();
                    mech.Configure(joints, aims);

                    // 報告
                    foreach (var s in segments.OrderBy(s => Depth(segments, s)))
                    {
                        string parent = s.parent >= 0 ? segments[s.parent].name : "-";
                        string reliable = s.parent < 0 ? "" : s.hingeScore <= HingeTolerance ? "可靠" : "⚠ 不可靠";
                        sb.AppendLine($"{s.name,-9} 父={parent,-9} 頂點={s.points.Length,6} 連通塊={s.islands.Count,3} 降弓角={s.angle,7:F2}° 軸={s.axis} 鉸接點={s.pivot} 鉸接距離={s.hingeScore * 1000:F1}mm {reliable}");
                        if (s.aimTarget >= 0) sb.AppendLine($"          另一端連到 {segments[s.aimTarget].name}，鉸接點={s.aimPoint}，距離={s.aimScore * 1000:F1}mm");
                    }

                    // 驗證：降弓姿態應與原降弓網格（扣掉底架位移）重合；中間姿態檢查連桿兩端落差
                    Verify(sb, segments, root, mech);

                    PrefabUtility.SaveAsPrefabAsset(rigRoot, PrefabPath);
                    var importer = AssetImporter.GetAtPath(PrefabPath);
                    importer.userData = VersionTag;
                    importer.SaveAndReimport();
                }
                finally
                {
                    Object.DestroyImmediate(rigRoot);
                }
                AssetDatabase.SaveAssets();
                sb.AppendLine($"已輸出 {PrefabPath}");
                RenderPreviews(sb);
                return Finish(sb, true);
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        static bool Finish(StringBuilder sb, bool ok)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, sb.ToString());
            if (ok) Debug.Log("[PantographRigBuilder] 可動模型建立完成\n" + sb);
            else Debug.LogError("[PantographRigBuilder] 可動模型建立失敗\n" + sb);
            return ok;
        }

        static void Verify(StringBuilder sb, List<Segment> segments, Segment root, PantographMechanism mech)
        {
            foreach (var s in segments.Where(s => s.parent >= 0)) s.transform.localRotation = Quaternion.identity;

            mech.SetLift(0f);
            var baseInv = Quaternion.Inverse(root.q);
            float worst = 0f;
            foreach (var s in segments)
            {
                float err = 0f;
                foreach (var isl in s.islands)
                    for (int k = 0; k < isl.a.Length; k += Mathf.Max(1, isl.a.Length / 50))
                    {
                        var expected = baseInv * (isl.b[k] - root.t);
                        var actual = s.transform.TransformPoint(isl.a[k] - s.pivot);
                        err = Mathf.Max(err, (expected - actual).magnitude);
                    }
                worst = Mathf.Max(worst, err);
                sb.AppendLine($"驗證 降弓姿態 {s.name,-9} 最大誤差 {err * 1000:F1} mm");
            }
            // 所有「不在階層上」的鉸接（四連桿閉合處），檢查動作中是否分離
            var closures = new List<(Segment s, Segment q, Vector3 point)>();
            foreach (var s in segments)
                foreach (var q in segments)
                {
                    if (s.index >= q.index || s.parent == q.index || q.parent == s.index) continue;
                    var h = RelativeHinge(q, s);
                    if (!h.valid || h.rigid || HingeScore(q, s, h) > HingeTolerance) continue;
                    closures.Add((s, q, ClosestOnAxis(s.centroid, h.point, h.axis)));
                }
            for (float lift = 0.25f; lift < 1f; lift += 0.25f)
            {
                mech.SetLift(lift);
                foreach (var (s, q, point) in closures)
                {
                    float gap = Vector3.Distance(s.transform.TransformPoint(point - s.pivot), q.transform.TransformPoint(point - q.pivot));
                    sb.AppendLine($"驗證 升弓量 {lift:F2} {s.name}–{q.name} 鉸接處落差 {gap * 1000:F1} mm");
                }
            }
            mech.SetLift(1f);
            sb.AppendLine(worst < 0.005f ? "驗證結果：降弓姿態與原模型吻合" : $"⚠ 降弓姿態誤差 {worst * 1000:F0} mm");
        }

        static void RenderPreviews(StringBuilder sb)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var mech = inst.GetComponent<PantographMechanism>();
                var lightGo = new GameObject("Light");
                SceneManager_Move(lightGo, scene);
                var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
                var camGo = new GameObject("Cam");
                SceneManager_Move(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.85f, 0.88f, 0.92f);
                cam.fieldOfView = 35f;
                var rt = new RenderTexture(640, 480, 24);
                cam.targetTexture = rt;

                mech.SetLift(1f);
                var bounds = inst.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                var center = bounds.center; float dist = bounds.extents.magnitude * 3.2f;
                var views = new[] { ("side", Quaternion.Euler(5, 90, 0)), ("persp", Quaternion.Euler(20, 50, 0)) };
                foreach (var lift in new[] { 0f, 0.5f, 1f })
                {
                    mech.SetLift(lift);
                    foreach (var (vname, rot) in views)
                    {
                        cam.transform.SetPositionAndRotation(center - rot * Vector3.forward * dist, rot);
                        cam.Render();
                        RenderTexture.active = rt;
                        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                        tex.Apply();
                        RenderTexture.active = null;
                        File.WriteAllBytes($"Logs/rig_{vname}_{lift:F1}.png", tex.EncodeToPNG());
                        Object.DestroyImmediate(tex);
                    }
                }
                // 弓頭特寫：拆出的零件上色
                mech.SetLift(1f);
                var head = inst.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "PanHead");
                if (head != null)
                {
                    var palette = new Dictionary<string, Color>
                    {
                        { "CarbonStrip", new Color(0.1f, 0.6f, 1f) }, { "StripCarrier", new Color(1f, 0.75f, 0.1f) },
                        { "_Bolt", new Color(1f, 0.2f, 0.2f) }, { "_Washer", new Color(0.2f, 1f, 0.3f) },
                        { "_Nut", new Color(1f, 0.3f, 1f) }, { "_Sleeve", new Color(0.3f, 1f, 1f) },
                    };
                    var shader = Shader.Find("Unlit/Color");
                    var temps = new List<Material>();
                    foreach (Transform child in head)
                    {
                        var key = palette.Keys.FirstOrDefault(k => child.name.Contains(k));
                        var rend = child.GetComponent<Renderer>();
                        if (key == null || rend == null) continue;
                        var m = new Material(shader) { color = palette[key] }; temps.Add(m);
                        rend.sharedMaterials = Enumerable.Repeat(m, rend.sharedMaterials.Length).ToArray();
                    }
                    var hb = head.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                    foreach (var (vname, rot, scale) in new[] { ("panhead_top", Quaternion.Euler(55, 30, 0), 1.6f), ("panhead_end", Quaternion.Euler(20, 100, 0), 0.6f) })
                    {
                        var focus = vname == "panhead_end" ? head.Find("Fastener_L_Bolt") != null ? head.Find("Fastener_L_Bolt").position : hb.center : hb.center;
                        cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * hb.extents.magnitude * scale * 2f, rot);
                        cam.Render();
                        RenderTexture.active = rt;
                        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                        tex.Apply();
                        RenderTexture.active = null;
                        File.WriteAllBytes($"Logs/rig_{vname}.png", tex.EncodeToPNG());
                        Object.DestroyImmediate(tex);
                    }
                    foreach (var m in temps) Object.DestroyImmediate(m);
                }
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                sb.AppendLine("預覽圖：Logs/rig_*.png（弓頭特寫：藍＝碳滑板、黃＝托架含羊角、紅＝螺桿、綠＝墊圈、紫＝螺帽、青＝套筒）");
            }
            catch (System.Exception e) { sb.AppendLine("預覽圖失敗：" + e.Message); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

        // ---------- 幾何 ----------

        static Bounds Encapsulate(IEnumerable<Island> islands)
        {
            bool any = false; var b = new Bounds();
            foreach (var p in islands.SelectMany(i => i.a))
            {
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
            return b;
        }

        /// <summary>
        /// 依幾何特徵把弓頭的連通塊分成可維修零件（座標：X＝橫向、Y＝上、Z＝行車方向）。
        /// 碳滑板：最高、長度超過弓頭寬一半、厚度 &lt; 3 cm 的長條。
        /// 托架（含羊角）：長度接近整個弓頭寬的長條。
        /// 懸吊固定件：細長直立的螺桿，以及同一軸線上的墊圈（扁平）、螺帽（在墊圈下）、套筒（在墊圈上）。
        /// </summary>
        static List<(string name, List<Island> islands)> ClassifyPanHead(Segment panHead, StringBuilder sb)
        {
            var result = new List<(string, List<Island>)>();
            var info = panHead.islands.Select(i => (isl: i, b: Encapsulate(new[] { i }))).ToList();
            var whole = Encapsulate(panHead.islands);
            float width = whole.size.x;

            var strips = info.Where(x => x.b.size.x > width * 0.5f && x.b.size.y < 0.03f && x.b.size.x < width * 0.9f)
                             .OrderByDescending(x => x.b.center.y).Take(2).OrderBy(x => x.b.center.z).ToList();
            for (int i = 0; i < strips.Count; i++) result.Add(($"CarbonStrip_{i + 1}", new List<Island> { strips[i].isl }));

            var carriers = info.Where(x => x.b.size.x >= width * 0.9f).OrderBy(x => x.b.center.z).ToList();
            for (int i = 0; i < carriers.Count; i++) result.Add(($"StripCarrier_{i + 1}", new List<Island> { carriers[i].isl }));

            var bolts = info.Where(x => x.b.size.y > 0.08f && x.b.size.x < 0.015f && x.b.size.z < 0.015f).OrderBy(x => x.b.center.x).ToList();
            var taken = new HashSet<Island>(strips.Concat(carriers).Concat(bolts).Select(x => x.isl));
            for (int i = 0; i < bolts.Count; i++)
            {
                string side = bolts[i].b.center.x < whole.center.x ? "L" : "R";
                var axisX = bolts[i].b.center.x; var axisZ = bolts[i].b.center.z;
                var near = info.Where(x => !taken.Contains(x.isl) && Mathf.Abs(x.b.center.x - axisX) < 0.005f && Mathf.Abs(x.b.center.z - axisZ) < 0.005f
                                           && Mathf.Max(x.b.size.x, x.b.size.z) < 0.05f).ToList();
                result.Add(($"Fastener_{side}_Bolt", new List<Island> { bolts[i].isl }));
                var washer = near.Where(x => x.b.size.y < 0.006f).OrderBy(x => x.b.size.y).FirstOrDefault();
                if (washer.isl != null)
                {
                    result.Add(($"Fastener_{side}_Washer", new List<Island> { washer.isl }));
                    taken.Add(washer.isl);
                    var nut = near.Where(x => x.isl != washer.isl && x.b.center.y < washer.b.center.y).OrderByDescending(x => x.b.center.y).FirstOrDefault();
                    if (nut.isl != null) { result.Add(($"Fastener_{side}_Nut", new List<Island> { nut.isl })); taken.Add(nut.isl); }
                    var sleeve = near.Where(x => x.isl != washer.isl && x.b.center.y > washer.b.center.y).OrderBy(x => x.b.center.y).FirstOrDefault();
                    if (sleeve.isl != null) { result.Add(($"Fastener_{side}_Sleeve", new List<Island> { sleeve.isl })); taken.Add(sleeve.isl); }
                }
            }

            sb.AppendLine("弓頭拆分：");
            foreach (var (name, isls) in result)
            {
                var b = Encapsulate(isls);
                sb.AppendLine($"  {name,-20} 中心={b.center:F3} 尺寸(m)={b.size:F3}");
            }
            int rest = panHead.islands.Count - result.Sum(r => r.Item2.Count);
            sb.AppendLine($"  其餘 {rest} 塊留在 PanHead 本體（橫管、支架、懸吊樑、彈簧盒）");
            return result;
        }

        static Mesh BuildMesh(IEnumerable<Island> islands, Vector3 pivot, out Material[] materials)
        {
            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var tangents = new List<Vector4>(); var uvs = new List<Vector2>();
            var trisByMat = new Dictionary<Material, List<int>>();
            foreach (var bySource in islands.GroupBy(i => i.source))
            {
                var r = bySource.Key; var m = r.GetComponent<MeshFilter>().sharedMesh;
                var sv = m.vertices; var sn = m.normals; var st = m.tangents; var su = m.uv;
                var remap = new Dictionary<int, int>();
                foreach (var i in bySource.SelectMany(isl => isl.verts))
                {
                    remap[i] = verts.Count;
                    verts.Add(r.transform.TransformPoint(sv[i]) - pivot);
                    if (sn.Length > 0) normals.Add(r.transform.TransformDirection(sn[i]).normalized);
                    if (st.Length > 0) { var d = r.transform.TransformDirection(st[i]).normalized; tangents.Add(new Vector4(d.x, d.y, d.z, st[i].w)); }
                    if (su.Length > 0) uvs.Add(su[i]);
                }
                var mats = r.sharedMaterials;
                for (int sub = 0; sub < m.subMeshCount; sub++)
                {
                    var mat = mats[Mathf.Min(sub, mats.Length - 1)];
                    if (!trisByMat.TryGetValue(mat, out var list)) trisByMat[mat] = list = new List<int>();
                    var tris = m.GetTriangles(sub);
                    for (int k = 0; k < tris.Length; k += 3)
                        if (remap.ContainsKey(tris[k]))
                        { list.Add(remap[tris[k]]); list.Add(remap[tris[k + 1]]); list.Add(remap[tris[k + 2]]); }
                }
            }
            var mesh = new Mesh { indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            if (normals.Count == verts.Count) mesh.SetNormals(normals); else mesh.RecalculateNormals();
            if (tangents.Count == verts.Count) mesh.SetTangents(tangents);
            if (uvs.Count == verts.Count) mesh.SetUVs(0, uvs);
            var entries = trisByMat.Where(kv => kv.Value.Count > 0).ToList();
            mesh.subMeshCount = entries.Count;
            for (int i = 0; i < entries.Count; i++) mesh.SetTriangles(entries[i].Value, i);
            mesh.RecalculateBounds();
            materials = entries.Select(e => e.Key).ToArray();
            return mesh;
        }

        static List<int[]> SplitIslands(int[] tris, Vector3[] world)
        {
            int n = world.Length;
            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int x, int y) { x = Find(x); y = Find(y); if (x != y) parent[x] = y; }
            for (int i = 0; i < tris.Length; i += 3) { Union(tris[i], tris[i + 1]); Union(tris[i], tris[i + 2]); }
            var byPos = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < n; i++)
            {
                var key = Vector3Int.RoundToInt(world[i] * 10000f);
                if (byPos.TryGetValue(key, out int j)) Union(i, j); else byPos[key] = i;
            }
            return Enumerable.Range(0, n).GroupBy(Find).Select(g => g.ToArray()).ToList();
        }

        static float Mismatch(Segment s, Island i)
        {
            float d = 0;
            foreach (var p in new[] { i.ca, s.islands[0].ca, i.a[0], i.a[i.a.Length - 1] })
                d = Mathf.Max(d, ((s.q * p + s.t) - i.Apply(p)).magnitude);
            return d;
        }

        static float MaxError(Segment s, Island isl)
        {
            float e = 0;
            for (int k = 0; k < isl.a.Length; k++) e = Mathf.Max(e, ((s.q * isl.a[k] + s.t) - isl.b[k]).magnitude);
            return e;
        }

        /// <summary>child 相對 parent 的運動（升弓座標）：轉軸、軸上一點、角度。</summary>
        static Hinge RelativeHinge(Segment parent, Segment child)
        {
            var pInv = Quaternion.Inverse(parent.q);
            var q = pInv * child.q;
            var t = pInv * (child.t - parent.t);
            q.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) { angle -= 360f; }
            if (Mathf.Abs(angle) < 0.3f)
                return new Hinge { valid = t.magnitude < 0.002f, rigid = true, axis = Vector3.right };
            axis.Normalize();
            var shift = Vector3.Dot(t, axis) * axis;   // 沿轉軸的位移（通常只有幾 mm）
            var tPerp = t - shift;
            float half = angle * 0.5f * Mathf.Deg2Rad;
            var point = 0.5f * (tPerp + Vector3.Cross(axis, tPerp) / Mathf.Tan(half));
            return new Hinge { valid = true, point = point, axis = axis, angle = angle, shift = shift };
        }

        static Vector3 ClosestOnAxis(Vector3 p, Vector3 point, Vector3 axis) => point + Vector3.Dot(p - point, axis) * axis;

        static float DistanceToAxis(Vector3[] pts, Vector3 point, Vector3 axis)
        {
            float best = float.MaxValue;
            for (int i = 0; i < pts.Length; i++)
            {
                var d = pts[i] - point;
                best = Mathf.Min(best, (d - Vector3.Dot(d, axis) * axis).sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }

        static int Depth(List<Segment> all, Segment s) => s.parent < 0 ? 0 : 1 + Depth(all, all[s.parent]);

        /// <summary>鉸接軸到兩個零件的最近距離（取較大者）；真正的鉸接兩邊都會貼著軸。</summary>
        static float HingeScore(Segment parent, Segment child, Hinge h)
        {
            if (!h.valid) return float.PositiveInfinity;
            if (h.rigid) return 0f;
            return Mathf.Max(DistanceToAxis(child.points, h.point, h.axis), DistanceToAxis(parent.points, h.point, h.axis));
        }

        static void Attach(Segment s, Segment parent, Hinge h, float score)
        {
            s.parent = parent.index; s.hingeScore = score;
            s.axis = h.axis; s.angle = h.angle; s.shift = h.shift;
            s.pivot = h.rigid ? s.centroid : ClosestOnAxis(s.centroid, h.point, h.axis);
        }

        /// <summary>相對底架的轉角（度）。</summary>
        static float Absolute(Segment root, Segment s)
        {
            var h = RelativeHinge(root, s);
            return h.valid && !h.rigid ? h.angle : 0f;
        }

        // Horn 四元數法：最佳剛體轉換 a -> b
        static void Fit(Island isl)
        {
            int n = isl.a.Length;
            Vector3 ca = Vector3.zero, cb = Vector3.zero;
            for (int i = 0; i < n; i++) { ca += isl.a[i]; cb += isl.b[i]; }
            ca /= n; cb /= n; isl.ca = ca;
            double sxx = 0, sxy = 0, sxz = 0, syx = 0, syy = 0, syz = 0, szx = 0, szy = 0, szz = 0; float spread = 0;
            for (int i = 0; i < n; i++)
            {
                var p = isl.a[i] - ca; var q = isl.b[i] - cb; spread = Mathf.Max(spread, p.magnitude);
                sxx += p.x * q.x; sxy += p.x * q.y; sxz += p.x * q.z; syx += p.y * q.x; syy += p.y * q.y; syz += p.y * q.z; szx += p.z * q.x; szy += p.z * q.y; szz += p.z * q.z;
            }
            isl.spread = spread;
            var N = new double[4, 4]
            {
                { sxx + syy + szz, syz - szy, szx - sxz, sxy - syx },
                { syz - szy, sxx - syy - szz, sxy + syx, szx + sxz },
                { szx - sxz, sxy + syx, -sxx + syy - szz, syz + szy },
                { sxy - syx, szx + sxz, syz + szy, -sxx - syy + szz }
            };
            var v = LargestEigenvector(N);
            isl.q = new Quaternion((float)v[1], (float)v[2], (float)v[3], (float)v[0]).normalized;
            isl.t = cb - isl.q * ca;
            float res = 0; for (int i = 0; i < n; i++) res = Mathf.Max(res, (isl.Apply(isl.a[i]) - isl.b[i]).magnitude);
            isl.residual = res;
        }

        static double[] LargestEigenvector(double[,] a)
        {
            var A = (double[,])a.Clone(); var V = new double[4, 4]; for (int i = 0; i < 4; i++) V[i, i] = 1;
            for (int sweep = 0; sweep < 50; sweep++)
            {
                double off = 0; for (int p = 0; p < 4; p++) for (int q = p + 1; q < 4; q++) off += A[p, q] * A[p, q];
                if (off < 1e-20) break;
                for (int p = 0; p < 4; p++) for (int q = p + 1; q < 4; q++)
                {
                    if (System.Math.Abs(A[p, q]) < 1e-30) continue;
                    double theta = (A[q, q] - A[p, p]) / (2 * A[p, q]);
                    double t = theta == 0 ? 1 : System.Math.Sign(theta) / (System.Math.Abs(theta) + System.Math.Sqrt(theta * theta + 1));
                    double c = 1 / System.Math.Sqrt(t * t + 1), s = t * c;
                    for (int k = 0; k < 4; k++) { double akp = A[k, p], akq = A[k, q]; A[k, p] = c * akp - s * akq; A[k, q] = s * akp + c * akq; }
                    for (int k = 0; k < 4; k++) { double apk = A[p, k], aqk = A[q, k]; A[p, k] = c * apk - s * aqk; A[q, k] = s * apk + c * aqk; }
                    for (int k = 0; k < 4; k++) { double vkp = V[k, p], vkq = V[k, q]; V[k, p] = c * vkp - s * vkq; V[k, q] = s * vkp + c * vkq; }
                }
            }
            int best = 0; for (int i = 1; i < 4; i++) if (A[i, i] > A[best, best]) best = i;
            return new[] { V[0, best], V[1, best], V[2, best], V[3, best] };
        }
    }
}
