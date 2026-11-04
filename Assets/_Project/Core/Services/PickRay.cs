using UnityEngine;

namespace PantographSim.Core
{
    /// <summary>點選射線（桌面、VR 共用）：略過手上拿的零件和不是目前目標的檢測點，打到後面的東西。</summary>
    public static class PickRay
    {
        public static bool Cast(Ray ray, float maxDistance, int mask, ISimState s, out RaycastHit hit)
        {
            var hits = Physics.RaycastAll(ray, maxDistance, mask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var it = h.collider.GetComponentInParent<IInteractable>();
                if (it != null && SimIds.RayIgnores(s, it.Id)) continue;
                hit = h;
                return true;
            }
            hit = default;
            return false;
        }
    }
}
