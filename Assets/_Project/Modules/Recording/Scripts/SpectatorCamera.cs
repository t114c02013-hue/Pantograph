using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Recording
{
    /// <summary>
    /// 錄影用的旁觀鏡頭（VR）：非立體（stereoTargetEye=None），畫到 1920×1080 RenderTexture，只在錄影時啟用，每秒算 30 次、不算陰影。
    /// ・第三人稱（預設）：學員斜後上方約 1.2 m，看雙手、工具與目標零件；位置與旋轉做 0.5 s 低通平滑，水平線固定。
    /// ・頭盔視角（穩定化）：位置跟著頭，旋轉做 0.3 s 低通平滑並去掉側傾。
    /// 不畫 HmdOnly 圖層（頭盔視野角落的 REC 紅點），影片裡不會出現紅點。桌面模式直接錄主相機，不用這台。
    /// </summary>
    public class SpectatorCamera : MonoBehaviour
    {
        /// <summary>只給頭盔看的圖層（REC 紅點）。</summary>
        public const int HmdOnlyLayer = 30;
        public const int Width = 1920, Height = 1080;
        public const float RenderRate = 30f;
        public const float ThirdPersonTau = 0.5f, HeadTau = 0.3f;

        [SerializeField] float distance = 1.2f;
        [SerializeField] float fieldOfView = 60f;

        Camera cam;
        RenderTexture rt;
        float nextRender;
        bool hasPose;
        Vector3 smoothPos;
        Quaternion smoothRot = Quaternion.identity;

        public static SpectatorCamera Instance { get; private set; }
        public RenderTexture Texture => EnsureTexture();
        public Camera Camera => cam;
        /// <summary>錄影中由後端打開；關閉時完全不算。</summary>
        public bool Capturing { get; set; }

        public void Setup(float dist) => distance = dist;

        void Awake()
        {
            Instance = this;
            cam = GetComponent<Camera>();
            if (cam == null) cam = gameObject.AddComponent<Camera>();
            cam.enabled = false;              // 手動 Render（30 Hz）
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.fieldOfView = fieldOfView;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
            cam.cullingMask = ~(1 << HmdOnlyLayer);
            cam.allowHDR = false; cam.allowMSAA = false;
            var listener = GetComponent<AudioListener>();
            if (listener != null) Destroy(listener);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (rt != null) { rt.Release(); Destroy(rt); }
        }

        RenderTexture EnsureTexture()
        {
            if (rt != null) return rt;
            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "SpectatorRT", antiAliasing = 1 };
            rt.Create();
            cam.targetTexture = rt;
            return rt;
        }

        void LateUpdate()
        {
            if (!Capturing) { hasPose = false; return; }
            var pose = ServiceLocator.Get<IPoseSource>();
            if (pose == null || !pose.PoseAvailable) return;
            bool head = Sim.State != null && Sim.State.Get(VideoCapture.ViewKey, "third") == "head";
            Target(pose, head, out var pos, out var rot);
            float tau = head ? HeadTau : ThirdPersonTau;
            if (!hasPose) { smoothPos = pos; smoothRot = rot; hasPose = true; }
            else
            {
                float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / tau);
                smoothPos = Vector3.Lerp(smoothPos, pos, k);
                smoothRot = Quaternion.Slerp(smoothRot, rot, k);
            }
            // 水平線固定：去掉側傾
            var f = smoothRot * Vector3.forward;
            transform.SetPositionAndRotation(smoothPos, Quaternion.LookRotation(f, Vector3.up));
            if (Time.unscaledTime < nextRender) return;
            nextRender = Time.unscaledTime + 1f / RenderRate;
            RenderNow();
        }

        /// <summary>理想位置：第三人稱＝頭的斜後上方 distance，看向雙手中點前方；頭盔＝頭的位置，只取偏航與俯仰。</summary>
        void Target(IPoseSource pose, bool headView, out Vector3 pos, out Quaternion rot)
        {
            var headPos = pose.Head.position;
            var fwd = pose.Head.rotation * Vector3.forward;
            if (headView)
            {
                pos = headPos;
                rot = Quaternion.LookRotation(fwd, Vector3.up);   // 去掉側傾
                return;
            }
            var flat = new Vector3(fwd.x, 0f, fwd.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();
            var right = Vector3.Cross(Vector3.up, flat);
            // 斜後上方：往後 0.75、往上 0.7、往右 0.6（合計約 1.2 m），看得到雙手、工具與前方零件
            float s = distance / 1.2f;
            pos = headPos - flat * (0.75f * s) + Vector3.up * (0.7f * s) + right * (0.6f * s);
            var look = pose.HasHands ? (pose.LeftHand.position + pose.RightHand.position) * 0.5f + flat * 0.35f : headPos + fwd * 0.8f;
            rot = Quaternion.LookRotation(look - pos, Vector3.up);
        }

        /// <summary>算一張（不算陰影）。</summary>
        public void RenderNow()
        {
            EnsureTexture();
            float shadow = QualitySettings.shadowDistance;
            QualitySettings.shadowDistance = 0f;
            try { cam.Render(); }
            finally { QualitySettings.shadowDistance = shadow; }
        }
    }
}
