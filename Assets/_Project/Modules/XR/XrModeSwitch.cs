using System.Collections;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Management;

namespace PantographSim.XR
{
    /// <summary>
    /// 開場判斷用 VR 還是桌面：有 XR loader（OpenXR 已啟動、頭盔接上）→ 啟用 XR Origin、註冊 IXrRig、
    /// 把 EventSystem 換成 XRUIInputModule、每個 Canvas 加 TrackedDeviceGraphicRaycaster，最後 InputMode.SetXr(true)
    /// （App 停用桌面相機、OrbitCamera、Picker；UI 改成世界空間）。沒有頭盔或命令列 -desktop → 什麼都不做，照舊桌面。
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public class XrModeSwitch : MonoBehaviour
    {
        [SerializeField] GameObject rigRoot;
        [SerializeField] XrRig rig;
        [SerializeField] XrTeleport teleport;
        [SerializeField] XrCoach coach;
        [SerializeField] float waitSeconds = 4f;

        /// <summary>Editor 截圖用：沒有頭盔也切到 VR 版面（攝影機與手由腳本擺放）。</summary>
        public static bool SimulateRequested;
        public static bool Simulated { get; private set; }
        public static string Status { get; private set; } = "";
        /// <summary>XRI 的 XR Device Simulator 開著、沒有真的頭盔：鍵鼠模擬裝置，手和頭照真的走 Input System。開關是 XR Device Simulator 的設定（Project Settings）。</summary>
        public static bool DeviceSimulated { get; private set; }

        /// <summary>沒有 loader、等完 wait 秒之後怎麼走（純函式）。</summary>
        public static bool UseDeviceSimulator(bool simulatorPresent, bool forceDesktop) => simulatorPresent && !forceDesktop;

        /// <summary>測試用：取代真的頭盔追蹤。</summary>
        public static System.Func<bool> HeadTrackedOverride;

        static bool HeadTracked()
        {
            if (HeadTrackedOverride != null) return HeadTrackedOverride();
            var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
            return head.isValid && head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) && tracked;
        }

        static void RemoveSimulator() { if (XRDeviceSimulator.instance != null) Destroy(XRDeviceSimulator.instance.gameObject); }

        public void Setup(GameObject root, XrRig r, XrTeleport tp, XrCoach c) { rigRoot = root; rig = r; teleport = tp; coach = c; }

        IEnumerator Start()
        {
            Simulated = false; DeviceSimulated = false;
            if (rigRoot != null) rigRoot.SetActive(false);
            if (InputMode.ForceDesktop) { RemoveSimulator(); Status = "命令列 -desktop：桌面模式"; Debug.Log("[XR] " + Status); yield break; }
            float t0 = Time.realtimeSinceStartup;
            while (true)
            {
                if (SimulateRequested) { RemoveSimulator(); Simulated = true; break; }
                var m = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
                bool loaderUp = m != null && m.isInitializationComplete && m.activeLoader != null;
                bool simulator = XRDeviceSimulator.instance != null;
                // OpenXR 只要 Meta 執行環境開著就會起來，沒戴頭盔也一樣：開著模擬器時要頭真的有追蹤才算實機
                if (loaderUp && (!simulator || HeadTracked())) { RemoveSimulator(); break; }
                if (Time.realtimeSinceStartup - t0 > waitSeconds)
                {
                    if (UseDeviceSimulator(simulator, InputMode.ForceDesktop))
                    {
                        if (loaderUp) { m.StopSubsystems(); m.DeinitializeLoader(); }   // 不然畫面還是送給沒人戴的頭盔
                        DeviceSimulated = true;
                        break;
                    }
                    Status = "沒有偵測到頭盔（OpenXR 沒有啟動），使用桌面模式";
                    Debug.Log("[XR] " + Status);
                    yield break;
                }
                yield return null;
            }
            Activate();
        }

        /// <summary>啟用 XR Origin 讓頭盔開始追蹤；擺人、擺介面、切 VR 模式的順序由 XrStartupSequencer 負責。</summary>
        void Activate()
        {
            if (rigRoot != null) rigRoot.SetActive(true);
            var ears = rig != null && rig.Eye != null ? rig.Eye.GetComponent<AudioListener>() : null;
            if (ears != null) ears.enabled = false;   // 切到 VR 之前桌面相機的 AudioListener 還在
            XrPlayer.Simulated = Simulated;
            if (Simulated)
            {
                // 沒有裝置：關掉追蹤，讓腳本擺放頭與手
                foreach (var d in rigRoot.GetComponentsInChildren<TrackedPoseDriver>(true)) d.enabled = false;
                foreach (var c in rigRoot.GetComponentsInChildren<XRBaseController>(true)) c.enabled = false;
            }
            ServiceLocator.Register<IXrRig>(rig);
            var sequencer = gameObject.AddComponent<XrStartupSequencer>();
            StartCoroutine(sequencer.Run(rig, teleport, Simulated || DeviceSimulated, () =>
            {
                if (ears != null) ears.enabled = true;
                SetupEventSystem();
                InputMode.SetXr(true);
                AddRaycasters();
                Status = Simulated ? "VR 版面（模擬，沒有頭盔）" : DeviceSimulated ? "VR 模式：XR Device Simulator（鍵鼠）" : "VR 模式：" + (XRGeneralSettings.Instance?.Manager?.activeLoader?.name ?? "");
                Debug.Log("[XR] " + Status);
                StartCoroutine(KeepRaycasters());
            }, () => { if (coach != null) coach.Begin(); }));
        }

        static void SetupEventSystem()
        {
            var es = EventSystem.current != null ? EventSystem.current : FindObjectOfType<EventSystem>();
            if (es == null) es = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            var old = es.GetComponent<StandaloneInputModule>();
            if (old != null) DestroyImmediate(old);
            if (es.GetComponent<XRUIInputModule>() == null) es.gameObject.AddComponent<XRUIInputModule>();
        }

        /// <summary>每個根 Canvas（World Space 介面）都要有 TrackedDeviceGraphicRaycaster，右手射線才點得到。</summary>
        public const string ClipboardCanvas = "XR_Clipboard";

        public static void AddRaycasters()
        {
            foreach (var c in FindObjectsOfType<Canvas>(true))
            {
                if (!c.isRootCanvas) continue;
                var r = c.GetComponent<TrackedDeviceGraphicRaycaster>();
                if (r == null) r = c.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                // 左手夾板（作業單）會隨手腕翻轉：背面也接住射線，從下面往上指也點得到
                if (c.name == ClipboardCanvas) r.ignoreReversedGraphics = false;
            }
        }

        IEnumerator KeepRaycasters()
        {
            var wait = new WaitForSecondsRealtime(1f);
            while (true) { yield return wait; AddRaycasters(); }
        }
    }
}
