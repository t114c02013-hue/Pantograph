using PantographSim.Core;
using PantographSim.Data;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// VR 試玩修正 第 1 點：頭伸不出活動範圍。collider 只擋手、工具和指向線；頭用水平範圍判斷（BoundaryDef，Walkable 外緣往內縮），和頭的高度無關，站著、跪著、彎腰都一樣。
    /// 頭超出時沿最近那條邊的法線把 XR Origin 推回邊上：只推水平，不改高度，不淡出、不延遲。活動欄打開時範圍包含跨板，平台和車頂相連。
    /// </summary>
    public class HeadBoundaryPusher : MonoBehaviour
    {
        [SerializeField] XROrigin origin;

        BoundaryDef def;

        public void Setup(XROrigin o) => origin = o;

        void Awake() => def = BoundaryDef.Load();

        /// <summary>頭在 head 時 XR Origin 要水平移動多少（純函式）。</summary>
        public static Vector3 Push(Rect[] area, Vector3 head)
        {
            var at = new Vector2(head.x, head.z);
            var d = BoundaryLogic.Clamp(area, at) - at;
            return new Vector3(d.x, 0f, d.y);
        }

        void LateUpdate()
        {
            var s = Sim.State;
            if (def == null || origin == null || origin.Camera == null || s == null || !InputMode.XrActive || !s.GetBool(XrStartupSequencer.ReadyKey)) return;
            bool bridge = s.GetBool(LiftControl.RoofOpenKey) || s.Get(XrViewHeight.FloorKey) == "Roof";
            origin.transform.position += Push(bridge ? def.open : def.closed, origin.Camera.transform.position);
        }
    }
}
