using PantographSim.Interaction;
using PantographSim.Parts.Mechanism;
using PantographSim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PantographSim.App
{
    /// <summary>
    /// 預覽場景的進入點：把鍵盤輸入接到各模組，並在左上角顯示操作說明（uGUI）。
    /// 各模組彼此不認識，只在這裡組裝。
    /// </summary>
    public class PantographDemoController : MonoBehaviour
    {
        [SerializeField] OrbitCameraController orbitCamera;
        [SerializeField] PantographMechanism mechanism;
        [SerializeField] KeyCode togglePoseKey = KeyCode.Space;
        [SerializeField] KeyCode resetViewKey = KeyCode.F;

        Rect panelRect = new Rect(12, 12, 320, 230);
        Bounds raisedBounds;

        public void Bind(OrbitCameraController orbit, PantographMechanism mech)
        {
            orbitCamera = orbit;
            mechanism = mech;
        }

        void Start()
        {
            BuildPanel();
            if (mechanism == null) return;
            // 以升弓時的範圍取景，整個升降過程都在畫面內
            float lift = mechanism.Lift;
            mechanism.SetLift(1f);
            raisedBounds = RendererBounds(mechanism.gameObject);
            mechanism.SetLift(lift);
            if (orbitCamera != null) orbitCamera.FrameBounds(raisedBounds, immediate: true);
        }

        void Update()
        {
            if (orbitCamera != null)
                orbitCamera.InputBlocked = panelRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));

            if (Input.GetKeyDown(togglePoseKey) && mechanism != null) mechanism.Toggle();
            if (Input.GetKeyDown(resetViewKey) && orbitCamera != null) orbitCamera.FrameBounds(raisedBounds);
        }

        static Bounds RendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        static string StateText(PantographMechanism.State s) => s switch
        {
            PantographMechanism.State.Raised => "已升弓",
            PantographMechanism.State.Lowered => "已降弓",
            PantographMechanism.State.Raising => "升弓中…",
            _ => "降弓中…",
        };

        Text stateText, toggleLabel;

        /// <summary>左上角說明面板（uGUI；執行期不用 OnGUI）。</summary>
        void BuildPanel()
        {
            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
            var go = new GameObject("DemoCanvas", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            var panel = Ui.Panel(go.transform, "Help");
            Ui.Place(panel.rectTransform, new Vector2(0, 1), new Vector2(12, -12), new Vector2(panelRect.width, panelRect.height));
            Ui.VStack(panel.gameObject, 10, 4, false);
            Ui.Text(panel.transform, "集電弓預覽　操作說明", 16, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            Ui.Text(panel.transform, "左鍵 / 右鍵拖曳：環繞查看\n滾輪：縮放　　中鍵拖曳：平移\nF：回到預設視角", 14);
            stateText = Ui.Text(panel.transform, "", 14);
            if (mechanism != null)
            {
                var b = Ui.Button(panel.transform, "", 14, () => mechanism.Toggle());
                Ui.Size(b, -1, 28);
                toggleLabel = Ui.LabelOf(b);
            }
        }

        void LateUpdate()
        {
            if (stateText == null) return;
            if (mechanism == null) { stateText.text = "（找不到升降弓機構）"; return; }
            var state = mechanism.CurrentState;
            stateText.text = $"空白鍵：升弓 / 降弓（動作中按會反向）\n狀態：{StateText(state)}　升弓量 {mechanism.Lift * 100f:F0}%";
            bool goingUp = state == PantographMechanism.State.Raising || state == PantographMechanism.State.Raised;
            if (toggleLabel != null) toggleLabel.text = goingUp ? "降弓" : "升弓";
        }
    }
}