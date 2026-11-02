using System.Collections;
using PantographSim.Core;
using PantographSim.Data;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// 樓層與視點高度（T1）：
    /// ・每幀從頭的位置往下打 Walkable，判斷目前樓層（Cab／Platform／Roof），寫到 xr.floor（活動欄「只擋進入、不擋離開」用）。
    /// ・HeightProfile＝Raised 時，只在車頂調整 Camera Floor Offset（＝measuredStripTop − raisedStripTop）；平台、駕駛室為 0；切換時 0.2 s 淡黑。
    /// ・蹲姿輔助（左搖桿按下／≡ 選單）：只在車頂有效，視點再下移 crouchOffset；離開車頂自動關。切換時視野下方提示 1.5 s 並輕震。
    /// 人站的地板不變，碰撞、手部判定照常。
    /// </summary>
    public class XrViewHeight : MonoBehaviour
    {
        public const string FloorKey = "xr.floor";
        [SerializeField] XROrigin origin;
        [SerializeField] HeightProfile profile;
        [SerializeField] LocomotionSettings locomotion;
        [SerializeField] XrFader fader;
        HeightMode mode;
        bool crouch, changing;
        string floor = "";
        float applied;

        public string Floor => floor;
        public bool Crouch => crouch;
        public HeightMode Mode => mode;
        public float AppliedOffset => applied;
        public event System.Action<string> Notice;

        public void Setup(XROrigin o, HeightProfile p, LocomotionSettings l, XrFader f)
        {
            origin = o; profile = p; locomotion = l; fader = f;
        }

        void Awake()
        {
            mode = profile != null ? profile.mode : HeightMode.Raised;
            crouch = profile != null && profile.crouchAssistDefaultOn;
        }

        /// <summary>樓層與設定 → Camera Floor Offset（純函式，測試用）。</summary>
        public static float OffsetFor(string floor, HeightMode mode, float stripTop, float raisedStripTop, bool crouch, float crouchOffset)
        {
            if (floor != "Roof") return 0f;
            float o = mode == HeightMode.Raised ? stripTop - raisedStripTop : 0f;
            return o + (crouch ? crouchOffset : 0f);
        }

        /// <summary>腳下的 Walkable 屬於哪一層（依場景件名稱：ENV_RoofSection／跨板＝Roof，ENV_CabFloor＝Cab，其他＝Platform）。</summary>
        public static string FloorOf(Transform hit)
        {
            for (var t = hit; t != null; t = t.parent)
            {
                if (t.name == "Walkable_Bridge" || t.name == "ENV_RoofSection") return "Roof";
                if (t.name == "ENV_CabFloor") return "Cab";
                if (t.name == "ENV_InspectionPlatform") return "Platform";
                if (t.name == "ENV_MaintenanceArea") return "Ground";   // R1.5a a4：地面維修區（視點偏移 0）
            }
            return "Platform";
        }

        float Desired => profile == null ? 0f : OffsetFor(floor, mode, profile.measuredStripTop, profile.raisedStripTop, crouch, profile.crouchOffset);

        void Update()
        {
            if (origin == null || origin.Camera == null) return;
            var head = origin.Camera.transform.position;
            if (Physics.Raycast(head + Vector3.up * 0.1f, Vector3.down, out var hit, 4f, SceneLayers.WalkableMask, QueryTriggerInteraction.Ignore))
            {
                string f = FloorOf(hit.collider.transform);
                if (f != floor)
                {
                    bool first = floor == "";
                    if (f != "Roof" && crouch) crouch = false;   // 離開車頂：蹲姿輔助自動關
                    floor = f;
                    Sim.State?.Set(FloorKey, floor);
                    if (first) ApplyNow(); else if (!changing) StartCoroutine(Change());
                }
            }
            // XROrigin 在追蹤原點改變時會把偏移歸零：每幀確認一次
            var offObj = origin.CameraFloorOffsetObject;
            if (!changing && offObj != null && Mathf.Abs(offObj.transform.localPosition.y - Desired) > 1e-4f) ApplyNow();
        }

        IEnumerator Change()
        {
            changing = true;
            if (fader != null && Mathf.Abs(applied - Desired) > 1e-4f) { fader.Seconds = profile != null ? profile.floorChangeFadeSeconds : 0.2f; yield return fader.FadeTo(1f); }
            ApplyNow();
            if (fader != null && fader.Alpha > 0f) yield return fader.FadeTo(0f);
            changing = false;
        }

        void ApplyNow()
        {
            applied = Desired;
            var off = origin.CameraFloorOffsetObject;
            if (off != null) off.transform.localPosition = new Vector3(0f, applied, 0f);
        }

        /// <summary>蹲姿輔助開關；只在車頂有效。回傳提示文字。</summary>
        public string ToggleCrouch()
        {
            string text;
            if (floor != "Roof") text = "蹲姿輔助只在車頂有效";
            else { crouch = !crouch; text = crouch ? "蹲姿輔助：開" : "蹲姿輔助：關"; }
            Notice?.Invoke(text);
            return text;
        }

        public void SetMode(HeightMode m) { mode = m; }
    }
}
