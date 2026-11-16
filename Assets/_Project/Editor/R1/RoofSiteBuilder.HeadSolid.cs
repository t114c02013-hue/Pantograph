using System.Collections.Generic;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Data;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PantographSim.Editor
{
    /// <summary>
    /// terry 186（RA-21、RA-25、RA-26）：照 HeadSolidDef.solids 替實心物件加 HeadSolid 碰撞框（子物件 HeadSolid_&lt;原名&gt;，原物件不動）；
    /// HeadSolid 和任何層都不互撞；駕駛室房間寫進 excludeZones（頭在裡面不判）。FBX 本身不帶 collider。
    /// </summary>
    public static partial class RoofSiteBuilder
    {
        public const string HeadSolidPrefix = "HeadSolid_";

        static bool ExcludedPart(Transform t, HeadSolidDef def) => (def.excludes ?? new string[0]).Any(p => HeadSolidDef.Wild(t.name, p));

        /// <summary>物件（含子節點，扣掉排除的）在自己局部座標的外框。</summary>
        static Bounds? LocalBounds(Transform root, IEnumerable<MeshFilter> parts)
        {
            Bounds? b = null;
            foreach (var mf in parts)
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = root.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (b.HasValue) { var x = b.Value; x.Encapsulate(p); b = x; } else b = new Bounds(p, Vector3.zero);
                }
            }
            return b;
        }

        static GameObject HeadSolidChild(Transform owner, int layer)
        {
            var g = new GameObject(HeadSolidPrefix + owner.name);
            g.transform.SetParent(owner, false);
            g.layer = layer;
            return g;
        }

        static IEnumerable<MeshFilter> Parts(Transform t, HeadSolidDef def, bool deep) =>
            (deep ? t.GetComponentsInChildren<MeshFilter>(true) : t.GetComponents<MeshFilter>())
                .Where(mf => !mf.GetComponentsInParent<Transform>(true).TakeWhile(x => x != t.parent).Any(x => ExcludedPart(x, def) || x.name.StartsWith(HeadSolidPrefix)));

        public static void BuildHeadSolids(Transform env, StringBuilder sb)
        {
            var def = R1Data.HeadSolid;
            R1Layers.Ensure(); int layer = LayerMask.NameToLayer(SceneLayers.HeadSolid);
            if (layer < 0) { sb.AppendLine("✘ 頭穿透：沒有 HeadSolid 層（先跑 R1Layers.Ensure）"); return; }
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(layer, i, true);   // 只拿來查詢，不參與物理
            var all = env.GetComponentsInChildren<Transform>(true).Where(t => !t.name.StartsWith(HeadSolidPrefix)).ToList();
            foreach (var row in def.solids ?? new HeadSolidDef.Solid[0])
            {
                var hits = all.Where(t => HeadSolidDef.Match(t, row.pattern) && !ExcludedPart(t, def)).ToList();
                hits = hits.Where(t => !hits.Any(o => o != t && t.IsChildOf(o))).ToList();   // 外層和 FBX 根同名（Worksite/ENV_WorkBench/Visual/ENV_WorkBench）：只做外層，不重複
                if (hits.Count == 0)
                {
                    sb.AppendLine(row.optional ? $"・頭穿透 {row.pattern}：略過（教學箱 FBX 未交付）" : $"✘ 頭穿透 {row.pattern}：場景裡找不到");
                    continue;
                }
                int boxes = 0;
                foreach (var t in hits)
                {
                    switch (row.kind)
                    {
                        case HeadSolidDef.Kind.Mesh:
                            var mf = t.GetComponent<MeshFilter>();
                            if (mf == null || mf.sharedMesh == null) break;
                            HeadSolidChild(t, layer).AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; boxes++;
                            break;
                        case HeadSolidDef.Kind.Box:
                            var lb = LocalBounds(t, Parts(t, def, true));
                            if (!lb.HasValue) break;
                            var bc = HeadSolidChild(t, layer).AddComponent<BoxCollider>(); bc.center = lb.Value.center; bc.size = lb.Value.size; boxes++;
                            break;
                        case HeadSolidDef.Kind.BoxPerPart:
                            foreach (var part in Parts(t, def, true).Where(p => p.sharedMesh != null && !def.solids.Any(r => r != row && r.kind == HeadSolidDef.Kind.Mesh && HeadSolidDef.Match(p.transform, r.pattern))).ToList())   // 別列已用網格的部分不再包框
                            {
                                var pc = HeadSolidChild(part.transform, layer).AddComponent<BoxCollider>(); pc.center = part.sharedMesh.bounds.center; pc.size = part.sharedMesh.bounds.size; boxes++;
                            }
                            break;
                    }
                }
                sb.AppendLine($"✔ 頭穿透 {row.pattern}（{row.kind}）：{hits.Count} 個物件、{boxes} 個碰撞框");
            }
            // 駕駛室房間：頭在裡面不判（只包房間內部，不能超出車頭外殼）
            var zones = new List<Bounds>();
            foreach (var cab in all.Where(t => t.name == "ENV_CabFloor"))
            {
                var walls = cab.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("Wall") || r.name == "Floor" || r.name == "Ceiling").ToList();
                if (walls.Count == 0) continue;
                var b = walls.Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                zones.Add(b);
                var shell = all.Where(t => t.name.StartsWith("Module_A") && t.parent != null && t.parent.name == "LRV").Select(t => t.GetComponent<Renderer>()).Where(r => r != null).Select(r => r.bounds).Where(x => x.Intersects(b)).ToList();
                bool inside = shell.Count > 0 && shell.Aggregate((x, y) => { x.Encapsulate(y); return x; }) is var s && s.Contains(b.min) && s.Contains(b.max);
                sb.AppendLine($"{(inside ? "✔" : "✘")} 頭穿透不判範圍：駕駛室 {b.min:F2}…{b.max:F2}（{(inside ? "在車頭外殼內" : "超出車頭外殼")}）");
            }
            def.excludeZones = zones.ToArray();
            EditorUtility.SetDirty(def);
            sb.AppendLine($"頭穿透：碰撞框 {env.GetComponentsInChildren<Collider>(true).Count(c => c.gameObject.layer == layer)} 個、HeadSolid 和所有層都不互撞；駕駛室 {zones.Count} 間（只有一端有駕駛室房間就只做那一端）");
        }
    }
}
