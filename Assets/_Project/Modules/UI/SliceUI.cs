using System.Collections.Generic;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PantographSim.UI
{
    /// <summary>
    /// 碳滑板切片的介面（uGUI）。桌面：Screen Space Overlay＋CanvasScaler（1920×1080、match 0.5），所有面板在 24 px 安全邊距內；VR：各面板改成 World Space（XrPanelLayout）；
    /// Editor 的 Game 視窗放大時（ViewportInfo.Zoom）自動把面板縮進可見範圍。UI 只讀黑板、訂閱事件、送 ActionRequested。
    /// 也提供 BeginCapture/EndCapture 讓 Editor 腳本把整個畫面（含介面）輸出成 1920×1080 截圖做自我檢查。
    /// </summary>
    public class SliceUI : MonoBehaviour
    {
        public const float Margin = 24f;
        public static SliceUI Instance { get; private set; }

        Canvas canvas;
        RectTransform canvasRect, safe;
        readonly List<View> views = new List<View>();
        StepPanelView stepPanel;
        MarkerView markers;
        bool bound;
        Camera captureCamera, captureUiCamera;
        int canvasLayer;

        public Canvas Canvas => canvas;
        public RectTransform Safe => safe;

        // ---------- VR：各面板搬到世界空間（XrPanelLayout） ----------
        XrPanelLayout xrLayout;
        HoverCardView hoverCard;
        IXrRig xrRig;
        bool built;
        public bool WorldSpace => xrLayout != null;
        public XrPanelLayout XrLayout => xrLayout;

        /// <summary>
        /// VR 模式：提示板、對話框、手腕面板、升降面板、資訊卡…各自一塊 World Space Canvas（1 unit = 1 m），
        /// 桌面專用的面板收起。控制器點介面用的 TrackedDeviceGraphicRaycaster 由 XR 模組加在每個 Canvas 上。
        /// </summary>
        public void UseXrLayout(IXrRig rig)
        {
            if (xrLayout != null) return;
            xrRig = rig;
            xrLayout = gameObject.AddComponent<XrPanelLayout>();
            xrLayout.Setup(canvas, safe, rig, hoverCard);
        }

        void OnInputModeChanged()
        {
            if (InputMode.XrActive) UseXrLayout(ServiceLocator.Get<IXrRig>());
        }

        void Awake() => Build();

        /// <summary>建立整個介面（Awake 呼叫；EditMode 測試也直接呼叫）。</summary>
        public void Build()
        {
            if (built) return;
            built = true;
            Instance = this;
            InputMode.Changed += OnInputModeChanged;
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var go = new GameObject("SliceCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            canvasRect = (RectTransform)go.transform;

            safe = Ui.Rect(canvasRect, "SafeArea");
            safe.anchorMin = safe.anchorMax = safe.pivot = new Vector2(0.5f, 0.5f);

            markers = new MarkerView();
            stepPanel = new StepPanelView();
            hoverCard = new HoverCardView();
            views.Add(markers);                 // 最底層：場景標籤與目標箭頭
            views.Add(hoverCard);               // VR 資訊卡（桌面用 MarkerView 的提示框）
            views.Add(stepPanel);
            views.Add(new ToolBarView());
            views.Add(new ToolPanelView());
            views.Add(new LiftPanelView());
            views.Add(new XrWristView());       // VR：左手腕面板、手勢小卡、右控制器按鍵標籤
            views.Add(new XrGestureView());
            views.Add(new ControllerLabelsView());
            views.Add(new TopBarView());
            views.Add(new ControlsStripView());
            views.Add(new WorkOrderView());     // 對話框：遮罩蓋住其他面板
            views.Add(new ControlPanelView());
            views.Add(new PromptViews());
            views.Add(new TaskCardView());
            views.Add(new TutorialView());
            views.Add(new ResultView());
            views.Add(new HelpView());
            views.Add(new XrTutorialView());    // VR：控制器教學、≡ 選單、Y 說明
            views.Add(new XrMenuView());
            views.Add(new XrHelpView());
            views.Add(new ReplayView());        // 重播：控制列與時間軸
            views.Add(new RecordsListView());   // 紀錄清單
            views.Add(new VideoNoticeView());   // 影片存檔路徑
            views.Add(new ErrorDialogView());   // 最上層
            UpdateSafeArea();
            foreach (var v in views) v.Build(safe, this);
        }

        void OnDestroy()
        {
            InputMode.Changed -= OnInputModeChanged;
            if (Instance == this) Instance = null;
            if (!bound || Sim.Bus == null) return;
            Sim.Bus.Unsubscribe<StepStarted>(OnStep);
            Sim.Bus.Unsubscribe<StateChanged>(PromptViews.OnStateChanged);
        }

        void OnStep(StepStarted e) { stepPanel.OnStep(e); markers.OnStep(e); }

        void Update()
        {
            if (!Sim.Ready) return;
            if (!bound)
            {
                Sim.Bus.Subscribe<StepStarted>(OnStep);
                Sim.Bus.Subscribe<StateChanged>(PromptViews.OnStateChanged);
                bound = true;
            }
            if (InputMode.XrActive && xrLayout == null) UseXrLayout(ServiceLocator.Get<IXrRig>());
            var es = EventSystem.current;
            if (!InputMode.XrActive) PointerState.OverUi = es != null && es.IsPointerOverGameObject();   // VR 由 XR 模組依右手射線寫入
            var sel = es != null ? es.currentSelectedGameObject : null;
            var field = sel != null ? sel.GetComponent<InputField>() : null;
            PointerState.TypingInUi = field != null && field.isFocused;
            if (!InputMode.XrActive && Input.GetKeyDown(KeyCode.H) && !PointerState.TypingInUi)
            {
                Ui.Command(UiCommands.Help, "key.H");
                Sim.State.SetBool("ui.help_open", !Sim.State.GetBool("ui.help_open"));
            }
            // Backspace 由工具列重設手上工具；這裡只記下「學員用了重設工具」
            if (!InputMode.XrActive && Input.GetKeyDown(KeyCode.Backspace) && !PointerState.TypingInUi && !SimSession.IsReplay
                && !string.IsNullOrEmpty(Sim.State.Get(SimIds.ActiveTool)) && !Sim.State.GetBool("training.paused"))
                Ui.Command(UiCommands.ResetTool, "key.Backspace");
        }

        void LateUpdate()
        {
            if (!Sim.Ready) return;
            UpdateSafeArea();
            RefreshAll();
        }

        public void RefreshAll()
        {
            var s = Sim.State;
            foreach (var v in views) v.Refresh(s);
        }

        void UpdateSafeArea()
        {
            float zoom = Mathf.Max(1f, ViewportInfo.Zoom);
            var size = canvasRect.rect.size / zoom - new Vector2(Margin * 2, Margin * 2);
            safe.sizeDelta = size;
            safe.anchoredPosition = Vector2.zero;
        }

        /// <summary>世界座標 → 安全區域內的座標；在攝影機後方回傳 false。</summary>
        public bool WorldToSafe(Vector3 world, out Vector2 local, out bool inside)
        {
            local = default; inside = false;
            var cam = captureCamera != null ? captureCamera : Camera.main;
            if (cam == null) return false;
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0.01f) return false;
            var uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(safe, sp, uiCam, out local)) return false;
            var r = safe.rect;
            inside = r.Contains(local);
            return true;
        }

        public Vector2 MouseInSafe()
        {
            var uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safe, Input.mousePosition, uiCam, out var local);
            return local;
        }

        // ---------- 自我檢查用截圖 ----------
        /// <summary>截圖：場景用主攝影機畫到 rt，介面用只看 UI 圖層的另一台攝影機疊上去（不會被貼近鏡頭的模型穿過）。</summary>
        public void BeginCapture(Camera cam, RenderTexture rt)
        {
            const int UiLayer = 5;
            captureCamera = cam;
            cam.targetTexture = rt;
            captureUiCamera = new GameObject("CaptureUICamera").AddComponent<Camera>();
            captureUiCamera.enabled = false;
            captureUiCamera.clearFlags = CameraClearFlags.Depth;
            captureUiCamera.cullingMask = 1 << UiLayer;
            captureUiCamera.transform.position = new Vector3(0, -1000f, 0);
            captureUiCamera.targetTexture = rt;
            canvasLayer = canvas.gameObject.layer;
            canvas.gameObject.layer = UiLayer;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = captureUiCamera;
            canvas.planeDistance = 1f;
        }

        /// <summary>依序畫場景與介面到截圖目標。</summary>
        public void RenderCapture()
        {
            if (captureCamera != null) captureCamera.Render();
            if (captureUiCamera != null) captureUiCamera.Render();
        }

        public void EndCapture()
        {
            if (captureCamera != null) captureCamera.targetTexture = null;
            canvas.renderMode = xrLayout != null ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = xrLayout != null && xrRig != null ? xrRig.Eye : null;
            canvas.gameObject.layer = canvasLayer;
            if (captureUiCamera != null) Destroy(captureUiCamera.gameObject);
            captureCamera = null; captureUiCamera = null;
        }
    }

    /// <summary>介面的一個區塊：Build 建立元件，Refresh 每幀依黑板更新。</summary>
    public abstract class View
    {
        protected SliceUI ui;
        public void Build(RectTransform safe, SliceUI owner) { ui = owner; OnBuild(safe); }
        protected abstract void OnBuild(RectTransform safe);
        public abstract void Refresh(ISimState s);
    }
}
