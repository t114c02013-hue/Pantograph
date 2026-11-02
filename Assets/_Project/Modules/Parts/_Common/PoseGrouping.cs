using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PantographSim.Parts.Common
{
    /// <summary>以網格中心點沿某一軸的最大間隙，把網格分成兩組。</summary>
    public class PoseGrouping
    {
        public Renderer[] Raised = Array.Empty<Renderer>();
        public Renderer[] Lowered = Array.Empty<Renderer>();
        public bool Reliable;
        public string Report = "";

        const float MinGapRatio = 0.1f;      // 間隙至少佔總寬的 10%
        const float MinGroupShare = 0.3f;    // 較小的一組至少佔 30% 網格

        public static PoseGrouping Compute(Renderer[] renderers)
        {
            var result = new PoseGrouping();
            var sb = new StringBuilder();
            int n = renderers.Length;
            sb.AppendLine($"網格數：{n}");
            if (n < 2) { result.Report = sb.ToString(); return result; }

            int bestAxis = -1, bestSplit = -1;
            float bestRatio = 0f;
            Renderer[] bestOrder = null;
            string[] axisNames = { "X", "Y", "Z" };

            for (int axis = 0; axis < 3; axis++)
            {
                var order = renderers.OrderBy(r => r.bounds.center[axis]).ToArray();
                float range = order[n - 1].bounds.center[axis] - order[0].bounds.center[axis];
                if (range < 1e-4f) continue;
                for (int i = 0; i < n - 1; i++)
                {
                    float gap = order[i + 1].bounds.center[axis] - order[i].bounds.center[axis];
                    float ratio = gap / range;
                    if (ratio > bestRatio)
                    {
                        bestRatio = ratio; bestAxis = axis; bestSplit = i + 1; bestOrder = order;
                    }
                }
            }

            if (bestOrder == null) { sb.AppendLine("所有網格中心重合，無法分組"); result.Report = sb.ToString(); return result; }

            var a = bestOrder.Take(bestSplit).ToArray();
            var b = bestOrder.Skip(bestSplit).ToArray();
            var boundsA = Encapsulate(a.Select(r => r.bounds));
            var boundsB = Encapsulate(b.Select(r => r.bounds));
            bool aIsRaised = boundsA.size.y >= boundsB.size.y;
            result.Raised = aIsRaised ? a : b;
            result.Lowered = aIsRaised ? b : a;
            result.Reliable = bestRatio >= MinGapRatio && Mathf.Min(a.Length, b.Length) >= n * MinGroupShare;

            var raisedB = aIsRaised ? boundsA : boundsB;
            var loweredB = aIsRaised ? boundsB : boundsA;
            sb.AppendLine($"分組軸：{axisNames[bestAxis]}，間隙比例：{bestRatio:P0}，可靠：{result.Reliable}");
            sb.AppendLine($"升弓組：{result.Raised.Length} 個網格，範圍 {raisedB.size}（高 {raisedB.size.y:F2} m）");
            sb.AppendLine($"降弓組：{result.Lowered.Length} 個網格，範圍 {loweredB.size}（高 {loweredB.size.y:F2} m）");

            // 與「依子物件順序前半/後半」的分法比對，供人工確認
            var siblingOrder = renderers.OrderBy(r => r.transform.GetSiblingIndex()).ToArray();
            int half = n / 2;
            var firstHalf = new HashSet<Renderer>(siblingOrder.Take(half));
            int matchRaised = result.Raised.Count(firstHalf.Contains);
            sb.AppendLine($"與順序分法比對：升弓組中有 {matchRaised}/{result.Raised.Length} 個位於前 {half} 個子物件");

            result.Report = sb.ToString();
            return result;
        }

        public static Bounds Encapsulate(IEnumerable<Bounds> all)
        {
            bool any = false;
            var total = new Bounds();
            foreach (var bb in all)
            {
                if (!any) { total = bb; any = true; }
                else total.Encapsulate(bb);
            }
            return any ? total : new Bounds(Vector3.zero, Vector3.one);
        }
    }
}
