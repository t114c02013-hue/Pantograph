using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Interaction
{
    /// <summary>
    /// 拿在手上的零件：按 R 旋轉 180°（送 ActionRequested(carried, rotate180)，由零件自己轉向）。
    /// 放下的方式是點目標位置（例如托架），由零件判斷能否吸附。
    /// </summary>
    public class DragPlace : MonoBehaviour
    {
        [SerializeField] KeyCode rotateKey = KeyCode.R;

        void Update()
        {
            var s = Sim.State;
            if (s == null || s.GetBool("training.paused") || PointerState.TypingInUi) return;
            if (Input.GetKeyDown(rotateKey))
                Sim.Bus.Publish(new ActionRequested { targetId = "carried", toolId = "", text = "rotate180", time = SimClock.Now });
        }
    }
}
