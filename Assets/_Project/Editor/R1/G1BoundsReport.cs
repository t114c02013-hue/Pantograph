using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Parts.Common;
using PantographSim.Parts.Mechanism;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// a6 第七節 4（建模 B pantograph-rigging.md 12.7）：降弓最低點的查法，一次跑完寫 Logs/G1BoundsReport.txt 與 Logs/g1_side.png。
    /// (1) 直接 SetLift(0)（不經升降動畫）；(2) 每個關節兩種最低點：Renderer.bounds、頂點；(3) 原始 FBX 降弓那組網格的高度（標準答案）；(4) 從 +X 看的正交側視圖。
    /// 場景建好之後呼叫（SliceSceneBuilder）；會把姿態設回原值。
    /// </summary>
    public static class G1BoundsReport
    {
        public const string ReportPath = "Logs/G1BoundsReport.txt", ShotPath = "Logs/g1_side.png";

        public static string Run(Transform root)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"G1 降弓 Bounds 查核（a6 第七節 4／建模 B 12.7，{System.DateTime.Now:yyyy-MM-dd HH:mm}）　量測座標：PAN_Root 局部（原點＝降弓底架底面中心）");
            var mech = root.GetComponentInChildren<PantographMechanism>(true);
            float lift = mech.Lift;
            mech.SetLift(0f);   // (1) 直接套用 Lowered 端點值
            var rig = mech.transform;
            sb.AppendLine($"\n(1) 姿態：PantographMechanism.SetLift(0)（Lowered 端點值，EditMode 直接套用，等待 0 幀）");
            foreach (var j in new[] { "LowerArm", "UpperArm" })
                sb.AppendLine($"    {j}.localEulerAngles＝{PanGeometry.Joint(rig, j).localEulerAngles:F3}");

            sb.AppendLine("\n(2) 各關節最低點（只算這個關節自己的網格）");
            sb.AppendLine("    關節        (a) Renderer.bounds.min.y   最低的 Renderer          (b) 頂點最低 y   頂點最低的網格        差 (a)−(b)");
            float worstA = float.MaxValue, worstB = float.MaxValue;
            foreach (var j in PanGeometry.JointNames)
            {
                var rs = PanGeometry.OwnRenderers(rig, j);
                if (rs.Count == 0) { sb.AppendLine($"    {j}：找不到網格"); continue; }
                var lowA = rs.OrderBy(r => r.bounds.min.y).First();
                float a = lowA.bounds.min.y - root.position.y;
                var vb = rs.Select(r => (r, b: PanGeometry.VertexBounds(new[] { r }, root))).Where(x => x.b.HasValue).OrderBy(x => x.b.Value.min.y).First();
                float b = vb.b.Value.min.y;
                worstA = Mathf.Min(worstA, a); worstB = Mathf.Min(worstB, b);
                sb.AppendLine($"    {j,-10}  {a,10:0.000}                {lowA.name,-22}  {b,10:0.000}     {vb.r.name,-20}  {a - b,8:0.000}");
            }
            var tight = PantographAssemblyBuilder.Tight;
            sb.AppendLine($"    整組 LoweredTightBounds（頂點，含 PAN_Root 底下所有顯示中的網格）：{tight.min:F3}–{tight.max:F3}");

            // (3) 原始 FBX 降弓那組網格（標準答案）
            sb.AppendLine("\n(3) 原始 FBX 降弓網格（頂點算法；相對它自己的底架底面）");
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(PanRiggingProbe.FbxPath);
            GameObject inst = null;
            float refMin = 0f, refMax = 0f; Bounds refAll = default; Vector3 refBase = default;
            if (fbx == null) sb.AppendLine("    找不到 " + PanRiggingProbe.FbxPath);
            else
            {
                inst = (GameObject)Object.Instantiate(fbx);
                inst.hideFlags = HideFlags.HideAndDontSave;
                var grouping = PoseGrouping.Compute(inst.GetComponentsInChildren<Renderer>(true));
                var lowered = grouping.Lowered;
                foreach (var r in grouping.Raised) r.enabled = false;
                // FBX 的網格沒有名稱（GUID），底架底面取降弓那組的最低點（規格：降弓最低點不低於底架底面）；水平位置取整組中心
                refAll = PanGeometry.VertexBounds(lowered).Value;
                refMin = 0f; refMax = refAll.size.y;
                refBase = new Vector3(refAll.center.x, refAll.min.y, refAll.center.z);
                sb.AppendLine($"    降弓網格 {lowered.Length} 個（分組可靠：{grouping.Reliable}）；FBX 網格沒有名稱，底架底面＝這組的最低點 Y {refAll.min.y:0.000}（FBX 座標），水平範圍 X {refAll.size.x:0.000} × Z {refAll.size.z:0.000} m");
                sb.AppendLine($"    最低點 {refMin:0.000} m、最高點 {refMax:0.000} m（相對底架底面；預期高度約 0.38 m，最低點不低於底架底面）");
            }
            var ours = PanGeometry.VertexBounds(rig.GetComponentsInChildren<Renderer>().Where(r => !(r is LineRenderer)), root).Value;
            sb.AppendLine($"    我們的降弓（機構網格，頂點）：最低 {ours.min.y:0.000}、最高 {ours.max.y:0.000} m；和原始 FBX 差 最低 {ours.min.y - refMin:+0.000;-0.000}、最高 {ours.max.y - refMax:+0.000;-0.000} m");

            // 判斷
            string verdict;
            if (worstB >= -0.005f && worstA < worstB - 0.01f) verdict = "C1：(b) 頂點正常、(a) Renderer.bounds 偏低（關節旋轉後網格 AABB 變鬆）。建置改用頂點算的 LoweredTightBounds（pan_lowered_tight_bounds.json）。";
            else if (worstB < -0.005f && Mathf.Abs(ours.min.y - refMin) > 0.02f) verdict = "C3：(b) 頂點也偏低，而且和原始 FBX 降弓差很多 → 先回報，關節角度等建模 B 決定。";
            else verdict = "(a)、(b) 都正常（差 ≤ 1 cm）。";
            sb.AppendLine("\n判斷：" + verdict + "（C2 不適用：EditMode 直接套用端點值，沒有等待幀）");

            // (4) 側視截圖
            try { Shot(root, rig, inst, refBase, sb); }
            catch (System.Exception e) { sb.AppendLine("(4) 截圖失敗：" + e.Message); }
            if (inst != null) Object.DestroyImmediate(inst);
            mech.SetLift(lift);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(true));
            return verdict;
        }

        /// <summary>(4) 正交相機從 +X 看：車頂線 Y 0（綠）、底架底面線（藍）、我們的降弓、原始 FBX 降弓（平移到同一個底架位置、半透明紅）。</summary>
        static void Shot(Transform root, Transform rig, GameObject fbxInst, Vector3 refBase, StringBuilder sb)
        {
            var temp = new List<Object>();
            var lineMat = new Material(Shader.Find("Sprites/Default")); temp.Add(lineMat);
            GameObject Line(string n, float y, Color c, Bounds span)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); temp.Add(g); g.name = n;
                g.transform.position = new Vector3(span.max.x + 0.05f, y, span.center.z); g.transform.localScale = new Vector3(0.01f, 0.008f, span.size.z + 1f);   // 畫在集電弓前面，不會被車頂擋住
                var m = new Material(lineMat) { color = c }; temp.Add(m); g.GetComponent<Renderer>().sharedMaterial = m;
                return g;
            }
            var ours = PanGeometry.VertexBounds(rig.GetComponentsInChildren<Renderer>().Where(r => !(r is LineRenderer)));
            var span = ours.Value;
            Line("G1_RoofLine", R1Data.RoofScene.roofFloorY, Color.green, span);
            Line("G1_BaseBottom", root.position.y, Color.blue, span);
            if (fbxInst != null)
            {
                var copy = (GameObject)Object.Instantiate(fbxInst); temp.Add(copy);
                copy.hideFlags = HideFlags.None;
                var oursAll = PanGeometry.VertexBounds(rig.GetComponentsInChildren<Renderer>().Where(r => !(r is LineRenderer))).Value;
                copy.transform.position += new Vector3(oursAll.center.x, root.position.y, oursAll.center.z) - refBase;
                var ghost = new Material(lineMat) { color = new Color(1f, 0.2f, 0.2f, 0.35f) }; temp.Add(ghost);
                foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) if (r.enabled) r.sharedMaterials = Enumerable.Repeat(ghost, r.sharedMaterials.Length).ToArray();
            }
            var camGo = new GameObject("G1_Cam"); temp.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = Mathf.Max(span.size.y, span.size.z / 1.78f) * 0.6f + 0.1f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.white;
            camGo.transform.position = new Vector3(span.max.x + 0.5f, span.center.y, span.center.z); camGo.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            cam.nearClipPlane = 0.3f; cam.farClipPlane = span.size.x + 0.8f;   // 只照集電弓那一片（後面的平台、護欄不入鏡）
            var rt = new RenderTexture(1600, 900, 24); temp.Add(rt);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false); temp.Add(tex);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            File.WriteAllBytes(ShotPath, tex.EncodeToPNG());
            sb.AppendLine($"(4) 側視截圖：{ShotPath}（正交、從 +X 看；綠＝車頂 Y 0、藍＝底架底面、半透明紅＝原始 FBX 降弓平移到同一個底架）");
            foreach (var o in temp) if (o != null) Object.DestroyImmediate(o);
        }
    }
}
