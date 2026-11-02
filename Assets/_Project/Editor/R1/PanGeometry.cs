using System.Collections.Generic;
using System.Linq;
using PantographSim.Parts.Mechanism;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1.5a a6（建模 B 12.7 的頂點算法）：用網格頂點（不是 Renderer.bounds）量集電弓的範圍。
    /// Renderer.bounds 是「網格自己的 AABB 轉到世界後再取 AABB」，關節一轉就變鬆（G1 的 C1：降弓 UpperArm 的 bounds 最低 −0.68 m，頂點最低 0）。
    /// </summary>
    public static class PanGeometry
    {
        public static readonly string[] JointNames = { "Base", "LowerArm", "UpperArm", "PanHead" };

        /// <summary>關節（依名稱找最淺的一個）。</summary>
        public static Transform Joint(Transform rig, string name) => rig.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).OrderBy(Depth).FirstOrDefault();
        static int Depth(Transform t) { int d = 0; for (var p = t; p != null; p = p.parent) d++; return d; }

        /// <summary>機構的剛體段：四個關節＋連桿 Rod*（PantographRigBuilder 的 AimLink，會跟著升降轉）。</summary>
        public static List<Transform> Segments(Transform rig) =>
            JointNames.Select(n => Joint(rig, n)).Where(t => t != null).Concat(rig.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Rod"))).ToList();

        /// <summary>只屬於這個段的網格（不屬於更深一層的段）。</summary>
        public static List<Renderer> OwnRenderers(Transform rig, string joint)
        {
            var j = Joint(rig, joint);
            if (j == null) return new List<Renderer>();
            var deeper = Segments(rig).Where(o => o != j && o.IsChildOf(j)).ToList();
            return j.GetComponentsInChildren<Renderer>(true).Where(r => !(r is LineRenderer) && deeper.All(o => !r.transform.IsChildOf(o))).ToList();
        }

        /// <summary>網格和平面 z＝zWorld 的截面點（三角形邊和平面的交點，世界座標）：長管只有兩端有頂點時也量得到中段。</summary>
        public static List<Vector3> SectionZ(IEnumerable<Renderer> rs, float zWorld)
        {
            var pts = new List<Vector3>();
            foreach (var r in rs)
            {
                if (!r.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) continue;
                var m = r.transform.localToWorldMatrix;
                var v = mf.sharedMesh.vertices.Select(x => m.MultiplyPoint3x4(x)).ToArray();
                var tri = mf.sharedMesh.triangles;
                for (int i = 0; i < tri.Length; i += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        var a = v[tri[i + k]]; var b = v[tri[i + (k + 1) % 3]];
                        if ((a.z - zWorld) * (b.z - zWorld) > 0f || Mathf.Abs(a.z - b.z) < 1e-6f) continue;
                        pts.Add(Vector3.Lerp(a, b, (zWorld - a.z) / (b.z - a.z)));
                    }
            }
            return pts;
        }

        /// <summary>Renderer 的頂點（世界座標）；SkinnedMeshRenderer 先 BakeMesh。</summary>
        public static IEnumerable<Vector3> WorldVertices(Renderer r)
        {
            Mesh mesh = null; Matrix4x4 m = r.transform.localToWorldMatrix;
            if (r is SkinnedMeshRenderer sk) { mesh = new Mesh(); sk.BakeMesh(mesh); m = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one); }
            else if (r.TryGetComponent<MeshFilter>(out var mf)) mesh = mf.sharedMesh;
            if (mesh == null) yield break;
            foreach (var v in mesh.vertices) yield return m.MultiplyPoint3x4(v);
        }

        /// <summary>頂點範圍，換到 space 的局部座標（space＝null 時是世界）。沒有頂點時回傳 null。</summary>
        public static Bounds? VertexBounds(IEnumerable<Renderer> rs, Transform space = null)
        {
            Bounds? b = null;
            foreach (var r in rs)
                foreach (var w in WorldVertices(r))
                {
                    var p = space != null ? space.InverseTransformPoint(w) : w;
                    if (b == null) b = new Bounds(p, Vector3.zero); else { var x = b.Value; x.Encapsulate(p); b = x; }
                }
            return b;
        }

        /// <summary>頂點平均（世界）。</summary>
        public static Vector3 VertexMean(IEnumerable<Renderer> rs)
        {
            var sum = Vector3.zero; int n = 0;
            foreach (var r in rs) foreach (var w in WorldVertices(r)) { sum += w; n++; }
            return n > 0 ? sum / n : Vector3.zero;
        }

        /// <summary>
        /// 車頂不可走範圍用的升降掃掠範圍：每個會動的關節（底架以外）在升降 samples 個姿態的頂點範圍（世界）。
        /// 取代整組 AABB 聯集（X ±0.83 × Z −0.19…+2.12，連底架兩側都蓋掉）。最後會把姿態設回原值。
        /// shortcut: 只取 5 個姿態的 AABB, 弓頭在兩個取樣姿態之間掃到更遠的地方時加密取樣
        /// </summary>
        public static Dictionary<string, Bounds> SweptFootprints(PantographMechanism mech, int samples = 5, System.Text.StringBuilder log = null)
        {
            float lift = mech.Lift;
            var list = new Dictionary<string, Bounds>();   // 段名 → 範圍（D37：站點淨空依零件分開，讀 StationSet.partClearRadius）
            foreach (var seg in Segments(mech.transform).Where(s => s.name != "Base"))
            {
                Bounds? acc = null;
                for (int i = 0; i < samples; i++)
                {
                    mech.SetLift(i / (float)(samples - 1));
                    var b = VertexBounds(OwnRenderers(mech.transform, seg.name));
                    if (b == null) continue;
                    log?.AppendLine($"  升降 {mech.Lift:0.00} {seg.name,-9} X {b.Value.min.x:0.000}…{b.Value.max.x:0.000}、Y {b.Value.min.y:0.000}…{b.Value.max.y:0.000}、Z {b.Value.min.z:0.000}…{b.Value.max.z:0.000}");
                    if (acc == null) acc = b; else { var x = acc.Value; x.Encapsulate(b.Value); acc = x; }
                }
                if (acc != null) list[seg.name] = acc.Value;
            }
            mech.SetLift(lift);
            return list;
        }
    }
}
