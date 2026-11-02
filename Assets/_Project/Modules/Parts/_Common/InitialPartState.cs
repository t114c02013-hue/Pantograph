using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// R1.5a a5：零件開場時的黑板值（例 part.pan_base.bolt_1.state＝Torqued、ip.pan_assy.hold_down.state＝Removed）。
    /// 和同物件上的 FastenerBehaviour／RemovableBehaviour 誰先綁定都一樣：它們只在沒有值時寫預設，這裡一律寫指定值。本輪這些零件都還沒接進步驟。
    /// </summary>
    public class InitialPartState : MonoBehaviour
    {
        [SerializeField] string key;
        [SerializeField] string value;
        bool done;

        public void Configure(string k, string v) { key = k; value = v; }

        void Update()
        {
            if (done || !Sim.Ready || string.IsNullOrEmpty(key)) return;
            done = true;
            if (Sim.State.Get(key) != value) Sim.State.Set(key, value);
        }
    }
}
