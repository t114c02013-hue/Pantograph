using UnityEngine;

namespace PantographSim.Core
{
    /// <summary>
    /// 場景中的命名位置（例：xr.mount.lift_panel 升降面板掛在扶手上的位置）。啟用時登錄到 SceneRegistry，
    /// 介面用 Sim.Registry.TryGetTransform 取位置與方向。
    /// </summary>
    public class RegisteredPoint : MonoBehaviour
    {
        [SerializeField] string id;
        public string Id => id;
        public void Configure(string newId) => id = newId;
        void OnEnable() => Sim.Registry?.Register(id, transform);
        void Start() => Sim.Registry?.Register(id, transform);
        void OnDisable() => Sim.Registry?.Unregister(id, transform);
    }
}
