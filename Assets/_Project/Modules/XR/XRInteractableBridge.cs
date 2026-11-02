using System.Collections.Generic;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace PantographSim.XR
{
    /// <summary>
    /// 把場景的互動點（零件、互動點、工具架掛位、控制盤…）登記成 XRI 的可互動物件，讓右手射線指得到、線變色。
    /// 建置腳本加上，只收「最近的 IInteractable 就是自己」的 Collider（避免和子物件重複登記）。
    /// 點擊、抓取不走 XRI 的 select／activate：由 XrPlayer 依按鍵對照表送出既有的 Core 事件（ActionRequested、ToolPickRequested…）。
    /// </summary>
    public class XRInteractableBridge : XRSimpleInteractable
    {
        [SerializeField] string simId;

        public string SimId
        {
            get
            {
                if (string.IsNullOrEmpty(simId) && GetComponent<ISimObject>() is ISimObject o) simId = o.Id;
                return simId;
            }
        }

        public IInteractable Target => GetComponent<IInteractable>();

        public void Configure(string id, IEnumerable<Collider> own)
        {
            simId = id;
            colliders.Clear();
            colliders.AddRange(own);
        }
    }
}
