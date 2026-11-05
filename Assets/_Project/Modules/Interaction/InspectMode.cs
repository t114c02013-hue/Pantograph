using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Interaction
{
    /// <summary>
    /// 檢視模式：滑鼠指著物件按 F → 攝影機拉近到該物件；Esc → 回到預設視角。
    /// （第一人稱 WASD 移動延後實作。）
    /// </summary>
    [RequireComponent(typeof(Picker))]
    public class InspectMode : MonoBehaviour
    {
        [SerializeField] OrbitCameraController orbit;
        [SerializeField] float inspectSize = 0.35f;
        Picker picker;

        public void Setup(OrbitCameraController o) => orbit = o;

        void Awake() => picker = GetComponent<Picker>();

        void Update()
        {
            if (orbit == null || InputMode.XrActive || Sim.State == null || PointerState.TypingInUi) return;
            if (Input.GetKeyDown(KeyCode.F) && picker.Hovered is Component c)
            {
                orbit.Retarget(new Bounds(c.transform.position, Vector3.one * inspectSize));
                Sim.State.SetBool("ui.inspecting", true);
            }
            else if (Input.GetKeyDown(KeyCode.Escape) && Sim.State.GetBool("ui.inspecting"))
            {
                orbit.ResetView();
                Sim.State.SetBool("ui.inspecting", false);
            }
        }
    }
}
