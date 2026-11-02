using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Recording
{
    /// <summary>
    /// 要記錄並重播位置的物件（工具、螺栓、滑板、弓頭、接地棒）。只放 ID，ID 取自同物件或父物件上的 ISimObject（由 Editor 建置腳本加上）。
    /// 紀錄：15 Hz、移動超過 1 mm 或轉動超過 0.5° 才記；重播：依時間擺回世界座標。
    /// </summary>
    [DisallowMultipleComponent]
    public class ReplayTracked : MonoBehaviour
    {
        [SerializeField] string id;
        [Tooltip("同一個 ISimObject 底下有多個被追蹤物件時加在 ID 後面（例 .model）")] [SerializeField] string suffix;

        public string Id
        {
            get
            {
                if (!string.IsNullOrEmpty(id)) return id;
                var sim = GetComponentsInParent<MonoBehaviour>(true).OfType<ISimObject>().FirstOrDefault(o => !string.IsNullOrEmpty(o.Id));
                return sim != null ? sim.Id + (suffix ?? "") : name;
            }
        }

        /// <summary>建置腳本呼叫：ID 寫死在元件上（執行期不必再找父物件）。</summary>
        public void Configure(string simId, string idSuffix = "") { id = simId + (idSuffix ?? ""); suffix = idSuffix; }

        /// <summary>場景中所有（含停用的）被追蹤物件，依階層深度排序（重播時先擺父物件再擺子物件）。</summary>
        public static List<ReplayTracked> FindAll() =>
            FindObjectsOfType<ReplayTracked>(true).OrderBy(Depth).ThenBy(t => t.Id).ToList();

        static int Depth(ReplayTracked t) { int d = 0; for (var p = t.transform; p != null; p = p.parent) d++; return d; }

        public PoseSample Sample() => new PoseSample { pos = transform.position, rot = transform.rotation, active = gameObject.activeInHierarchy };
    }
}
