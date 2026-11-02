using System.Collections.Generic;
using UnityEngine;

namespace PantographSim.XR
{
    public static class TeleportArc
    {
        /// <summary>拋物線取樣（純函式，測試用）：碰到 mask 的碰撞體（地板、護欄）就停在命中點；否則落到 floor 高度。points 收集取樣點。</summary>
        public static Vector3 Trace(Vector3 p, Vector3 v, float floor, int mask, List<Vector3> points)
        {
            points.Clear();
            const float dt = 0.03f;
            points.Add(p);
            for (int i = 0; i < 90; i++)
            {
                var next = p + v * dt + 0.5f * Physics.gravity * dt * dt;
                v += Physics.gravity * dt;
                if (Physics.Linecast(p, next, out var hit, mask, QueryTriggerInteraction.Ignore)) { points.Add(hit.point); return hit.point; }   // 只打地板與護欄
                if (next.y <= floor) { var q = Vector3.Lerp(p, next, Mathf.InverseLerp(p.y, next.y, floor)); points.Add(q); return q; }
                points.Add(next);
                p = next;
            }
            return p;
        }
    }
}
