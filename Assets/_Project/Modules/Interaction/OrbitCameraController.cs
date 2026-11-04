using UnityEngine;

namespace PantographSim.Interaction
{
    /// <summary>
    /// 環繞式攝影機：左鍵或右鍵拖曳環繞、滾輪縮放、中鍵平移。
    /// 不處理任何訓練邏輯，其他模組透過 FrameBounds / Retarget / ResetView 控制。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class OrbitCameraController : MonoBehaviour, PantographSim.Core.IPoseSource
    {
        // ---------- IPoseSource：桌面模式以攝影機當學員的頭（沒有手） ----------
        public string SourceName => "desktop";
        public bool PoseAvailable => isActiveAndEnabled;
        public Pose Head => new Pose(transform.position, transform.rotation);
        public bool HasHands => false;
        public Pose LeftHand => Pose.identity;
        public Pose RightHand => Pose.identity;

        [Header("預設視角（ResetView 回到這裡）")]
        [SerializeField] Vector3 homeTarget;
        [SerializeField] float homeDistance = 5f;
        [SerializeField] float homeYaw = -45f;
        [SerializeField] float homePitch = 25f;

        [Header("操作手感")]
        [SerializeField] float rotateSpeed = 4f;
        [SerializeField] float panSpeed = 1f;
        [SerializeField] float zoomStep = 0.1f;
        [SerializeField] float minDistance = 0.3f;
        [SerializeField] float maxDistance = 50f;
        [SerializeField] float minPitch = -5f;
        [SerializeField] float maxPitch = 85f;
        [SerializeField] float smoothing = 12f;
        [Tooltip("左鍵也能環繞（預覽場景用）；操作切片時左鍵要留給點選，只用右鍵")]
        [SerializeField] bool rotateWithLeftButton = true;

        public void SetRotateWithLeftButton(bool value) => rotateWithLeftButton = value;

        Vector3 target, currentTarget;
        float distance, yaw, pitch;
        float currentDistance, currentYaw, currentPitch;

        /// <summary>為 true 時忽略滑鼠（例如滑鼠在 UI 上）。</summary>
        public bool InputBlocked { get; set; }

        void Awake()
        {
            ResetView(immediate: true);
        }

        /// <summary>依物件範圍設定預設視角並套用。</summary>
        public void FrameBounds(Bounds bounds, bool immediate = false)
        {
            homeTarget = bounds.center;
            homeDistance = DistanceToFit(bounds);
            ResetView(immediate);
        }

        /// <summary>只改變環繞中心與距離，保留目前角度。</summary>
        public void Retarget(Bounds bounds)
        {
            target = bounds.center;
            distance = DistanceToFit(bounds);
        }

        /// <summary>框住目標，並從 from 這個方向看（站點看得到面向目標，建置檢查保證）。</summary>
        public void Retarget(Bounds bounds, Vector3 from)
        {
            Retarget(bounds);
            distance = Mathf.Clamp(Mathf.Min(distance, Vector3.Distance(bounds.center, from)), minDistance, maxDistance);   // 不退到站點後面：護欄和擋板不會擋住
            var look = Quaternion.LookRotation(bounds.center - from, Vector3.up).eulerAngles;
            yaw = look.y;
            pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, look.x), minPitch, maxPitch);
        }

        /// <summary>Retarget 之後鏡頭會停的位置。</summary>
        public Vector3 PlannedPosition => target - Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward * distance;

        public void ResetView(bool immediate = false)
        {
            target = homeTarget;
            distance = Mathf.Clamp(homeDistance, minDistance, maxDistance);
            yaw = homeYaw;
            pitch = homePitch;
            if (immediate)
            {
                currentTarget = target;
                currentDistance = distance;
                currentYaw = yaw;
                currentPitch = pitch;
                Apply();
            }
        }

        float DistanceToFit(Bounds bounds)
        {
            var cam = GetComponent<Camera>();
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
            return Mathf.Clamp(radius / Mathf.Sin(halfFov) * 1.05f, minDistance, maxDistance);
        }

        void Update()
        {
            if (!InputBlocked && !PantographSim.Core.PointerState.OverUi)
            {
                if ((rotateWithLeftButton && !PantographSim.Core.PointerState.DragCaptured && Input.GetMouseButton(0)) || Input.GetMouseButton(1))
                {
                    yaw += Input.GetAxis("Mouse X") * rotateSpeed;
                    pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * rotateSpeed, minPitch, maxPitch);
                }

                if (Input.GetMouseButton(2))
                {
                    var rot = Quaternion.Euler(pitch, yaw, 0f);
                    float scale = distance * 0.05f * panSpeed;
                    target -= rot * new Vector3(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"), 0f) * scale;
                }

                float scroll = PantographSim.Core.PointerState.ScrollCaptured ? 0f : Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f)
                    distance = Mathf.Clamp(distance * Mathf.Pow(1f - zoomStep, scroll), minDistance, maxDistance);
            }

            float t = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            currentTarget = Vector3.Lerp(currentTarget, target, t);
            currentDistance = Mathf.Lerp(currentDistance, distance, t);
            currentYaw = Mathf.LerpAngle(currentYaw, yaw, t);
            currentPitch = Mathf.Lerp(currentPitch, pitch, t);
            Apply();
        }

        void Apply()
        {
            var rot = Quaternion.Euler(currentPitch, currentYaw, 0f);
            transform.SetPositionAndRotation(currentTarget - rot * Vector3.forward * currentDistance, rot);
        }
    }
}
