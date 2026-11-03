using System.IO;
using System.Linq;
using PantographSim.Data;
using PantographSim.Parts.Common;
using PantographSim.Parts.Mechanism;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1.5b b4（鐵道 D42、軟體工程）：在場景裡量「拉直高度」——吊梁在弓頂正上方（梁中點在 COG 正上方）、吊帶 slingLength 從吊耳拉直到 4 個吊點時，
    /// 吊梁底面離弓頂多高。弓頂＝吊梁正下方（吊梁 XZ 範圍內）、降弓整組的最高點（鐵道 D42）。用網格頂點算（和 LoweredTightBounds 相同；Renderer.bounds 在關節轉動後會變鬆，G1）。
    /// 建置時寫回 RigHeightDef.straightHeightM，上限＝拉直高度 − slackMarginM。
    /// </summary>
    public static class RigHeightMeasure
    {
        public const string AssetPath = "Assets/_Project/Modules/Parts/Mechanism/Resources/RigHeight.asset";

        public struct Result
        {
            public float straight, eyeAboveLiftPoints, eyeBelowBeamBottom;
            public float topUnderBeamLocal, headTopLocal, upperArmTopLocal;   // PAN_Root 局部（降弓）
            public string topUnderBeamPart, basis;
            public override string ToString() =>
                $"拉直高度 {straight:0.000} m（弓頂取 {basis}；PAN_Root 局部，降弓）：吊梁正下方最高點 {topUnderBeamLocal:0.000}（{topUnderBeamPart}）、弓頭頂 {headTopLocal:0.000}、上臂頂 {upperArmTopLocal:0.000}；" +
                $"吊耳在吊點上方 {eyeAboveLiftPoints:0.000}、吊耳比吊梁底面低 {eyeBelowBeamBottom:0.000}";
        }

        public static Result Measure(string topBasis = "head")
        {
            var root = GameObject.Find(PantographAssemblyBuilder.RootName).transform;
            var mech = Object.FindObjectOfType<PantographMechanism>(true);
            float lift = mech != null ? mech.Lift : 0f;
            if (mech != null) mech.SetLift(0f);   // 台架上、車頂上吊掛時都是降弓
            try
            {
                var pts = Enumerable.Range(1, 4).Select(i => root.Find("PAN_LiftPoints/ip.pan_assy.lift_point_" + i).position).ToList();
                var cog = root.Find("PAN_Base/COG").position;
                var r = new Result { eyeAboveLiftPoints = PanLiftMath.BeamAbove(cog, pts, PanLiftMath.LoadDefault()) };
                float eyeY = pts.Average(p => p.y) + r.eyeAboveLiftPoints;
                var beam = GameObject.Find("ENV_LiftingBeam");
                var eye = beam.transform.Find("Beam_SlingEye_L") ?? beam.transform.parent.Find("Beam_SlingEye_L");
                var beamWorld = PanGeometry.VertexBounds(new[] { beam.GetComponent<Renderer>() }).Value;
                r.eyeBelowBeamBottom = beamWorld.min.y - eye.position.y;
                // 吊梁放在 COG 正上方時的 XZ 範圍（吊梁本身的 X、Z 尺寸）
                var half = new Vector2(beamWorld.size.x / 2f, beamWorld.size.z / 2f);
                var rs = root.GetComponentsInChildren<Renderer>().Where(x => x.enabled && x.gameObject.activeInHierarchy && !(x is LineRenderer)).ToList();
                float top = float.MinValue; string part = "";
                foreach (var rr in rs)
                    foreach (var w in PanGeometry.WorldVertices(rr))
                        if (Mathf.Abs(w.x - cog.x) <= half.x && Mathf.Abs(w.z - cog.z) <= half.y && w.y > top) { top = w.y; part = rr.name; }
                r.topUnderBeamLocal = root.InverseTransformPoint(new Vector3(cog.x, top, cog.z)).y; r.topUnderBeamPart = part;
                var head = Object.FindObjectsOfType<MonoBehaviour>(true).OfType<PantographSim.Core.ISimObject>().First(o => o.Id == PantographSim.Core.SimIds.PanHead);
                r.headTopLocal = PanGeometry.VertexBounds(((Component)head).GetComponentsInChildren<Renderer>(), root)?.max.y ?? 0f;
                r.upperArmTopLocal = PanGeometry.VertexBounds(rs.Where(x => x.name.Contains("UpperArm")), root)?.max.y ?? 0f;
                float baseTop = topBasis == "underBeam" ? top : root.TransformPoint(new Vector3(0f, r.headTopLocal, 0f)).y;
                r.basis = topBasis;
                r.straight = eyeY + r.eyeBelowBeamBottom - baseTop;
                return r;
            }
            finally { if (mech != null) mech.SetLift(lift); }
        }

        public static float StraightHeight(string topBasis = "head") => Measure(topBasis).straight;

        /// <summary>建置時：量測、寫回、檢查（不通過就丟例外讓建置失敗）。回傳報告的一行。</summary>
        public static string MeasureAndStore()
        {
            var def = AssetDatabase.LoadAssetAtPath<RigHeightDef>(AssetPath);
            if (def == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
                def = ScriptableObject.CreateInstance<RigHeightDef>();
                AssetDatabase.CreateAsset(def, AssetPath);
            }
            var m = Measure(def.topBasis);
            def.straightHeightM = Mathf.Round(m.straight * 1000f) / 1000f;
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            string line = $"D42 IN_01 a：{m}；上限 {def.UpperM:0.000} m（− slackMarginM {def.slackMarginM:0.00}），下限 {def.lowerM:0.00} m";
            var why = def.Check();
            if (why != null) throw new System.Exception("建置檢查失敗：" + why + "；" + m);
            return line;
        }
    }
}
