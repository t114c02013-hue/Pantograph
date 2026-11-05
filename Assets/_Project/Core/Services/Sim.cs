using System.Collections.Generic;
using UnityEngine;

namespace PantographSim.Core
{
    /// <summary>執行期取用共用服務的捷徑（由 App 註冊）。</summary>
    public static class Sim
    {
        public static IEventBus Bus => ServiceLocator.Get<IEventBus>();
        public static ISimState State => ServiceLocator.Get<ISimState>();
        public static IActionGuard Guard => ServiceLocator.Get<IActionGuard>();
        public static SceneRegistry Registry => ServiceLocator.Get<SceneRegistry>();
        public static bool Ready => Bus != null && State != null;

        /// <summary>問守門者；沒有守門者時一律放行。</summary>
        public static bool Ask(string toolId, string targetId, string verb, string pointId = "")
        {
            var g = Guard;
            return g == null || g.Check(new ActionRequest(toolId, targetId, verb, pointId)).allowed;
        }

        public static void Perform(string toolId, string targetId, string text, float value = 0f, string pointId = "") =>
            Bus?.Publish(new ActionPerformed { toolId = toolId ?? "", targetId = targetId, text = text, value = value, pointId = pointId ?? "", time = SimClock.Now });
    }

    /// <summary>ID → 場景物件位置。互動點啟用時自行註冊（給提示疊圖、攝影機對焦用）。</summary>
    public class SceneRegistry : ISceneLocator
    {
        readonly Dictionary<string, Transform> map = new Dictionary<string, Transform>();
        readonly Dictionary<string, Transform> points = new Dictionary<string, Transform>();
        public void Register(string id, Transform t) { if (!string.IsNullOrEmpty(id) && t != null) map[id] = t; }
        public void Unregister(string id, Transform t) { if (map.TryGetValue(id, out var cur) && cur == t) { map.Remove(id); points.Remove(id); } }
        /// <summary>箭頭和鏡頭對準的點跟物件中心不同時用；名稱、元件還是讀 Register 的物件。</summary>
        public void RegisterPoint(string id, Transform t) { if (!string.IsNullOrEmpty(id) && t != null) points[id] = t; }

        public bool TryGetPosition(string id, out Vector3 position)
        {
            if (map.TryGetValue(id, out var t) && t != null && t.gameObject.activeInHierarchy) { position = points.TryGetValue(id, out var p) && p != null ? p.position : t.position; return true; }
            position = default;
            return false;
        }

        public IEnumerable<string> Ids => map.Keys;

        public bool TryGetTransform(string id, out Transform t) => map.TryGetValue(id, out t) && t != null;

        /// <summary>物件的顯示名稱（取自同物件上的 IInteractable）。</summary>
        public string NameOf(string id) =>
            map.TryGetValue(id, out var t) && t != null && t.GetComponent<IInteractable>() is IInteractable i ? i.DisplayName : id;
    }
}
