using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// 場景中可點的互動點／零件。帶 ID（例 ip.carbon_strip_L.bolt_1），啟用時登錄位置，
    /// 滑過時通知同物件上的 Highlightable。需要 Collider 才點得到。
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractionPointMarker : MonoBehaviour, IInteractable
    {
        [SerializeField] protected string id;
        [SerializeField] protected string displayName;
        [SerializeField] protected string defaultVerb = "click";

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public string DefaultVerb => defaultVerb;

        public void Configure(string newId, string newName, string verb = "click")
        {
            id = newId; displayName = newName; defaultVerb = verb;
        }

        protected virtual void OnEnable()
        {
            Sim.Registry?.Register(id, transform);
        }

        protected virtual void OnDisable()
        {
            Sim.Registry?.Unregister(id, transform);
        }

        public void SetHovered(bool hovered)
        {
            var h = GetComponentInParent<Highlightable>();
            if (h != null) h.SetHovered(hovered);
        }
    }
}
