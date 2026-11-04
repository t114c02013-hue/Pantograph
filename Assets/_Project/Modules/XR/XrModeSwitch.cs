using System.Collections;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit;
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

        public void Setup(GameObject root, XrRig r, XrTeleport tp, XrCoach c) { rigRoot = root; rig = r; teleport = tp; coach = c; }

        IEnumerator Start()
        {
            Simulated = false;
            if (rigRoot != null) rigRoot.SetActive(false);
            if (InputMode.ForceDesktop) { Status = "命令列 -desktop：桌面模式"; Debug.Log("[XR] " + Status); yield break; }
            float t0 = Time.realtimeSinceStartup;
            while (true)
            {
                if (SimulateRequested) { Simulated = true; break; }
                var m = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
                if (m != null && m.isInitializationComplete && m.activeLoader != null) break;
                if (Time.realtimeSinceStartup - t0 > waitSeconds)
                {
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
            StartCoroutine(sequencer.Run(rig, teleport, Simulated, () =>
            {
                if (ears != null) ears.enabled = true;
                SetupEventSystem();
                InputMode.SetXr(true);
                AddRaycasters();
                Status = Simulated ? "VR 版面（模擬，沒有頭盔）" : "VR 模式：" + (XRGeneralSettings.Instance?.Manager?.activeLoader?.name ?? "");
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
