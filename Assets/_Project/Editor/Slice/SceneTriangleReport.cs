using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>terry 2026-11-15（協調者第 153 題 A）：重建場景時把三角面數寫進建置報告。含隱藏物件（日常／定期課程開關的物件也算）。</summary>
    public static class SceneTriangleReport
    {
        public const string LrvName = "LRV", RoofBoxPrefix = "Roof_", PlatformName = "ENV_InspectionPlatform";

        public static int Tris(Transform root) => root == null ? 0
            : root.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh != null ? f.sharedMesh.triangles.Length / 3 : 0)
            + root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(s => s.sharedMesh != null ? s.sharedMesh.triangles.Length / 3 : 0);

        static Transform Find(IEnumerable<GameObject> roots, string name) =>
            roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == name);

        public static string Build(IEnumerable<GameObject> roots)
        {
            var list = roots.ToList();
            var lrv = Find(list, LrvName);
            int boxes = lrv == null ? 0 : lrv.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(RoofBoxPrefix)).Sum(Tris);
            int car = Tris(lrv) - boxes, plat = Tris(Find(list, PlatformName)), total = list.Sum(r => Tris(r.transform));
            return $"三角面數：輕軌車體 {car}、車頂箱體 {boxes}、平台設備（{PlatformName}）{plat}、全場合計 {total}（含隱藏物件）";
        }
    }
}
