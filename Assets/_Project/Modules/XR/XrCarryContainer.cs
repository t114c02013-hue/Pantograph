using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// R1.5b b3 隨身容器（§3.4，D30）：工具袋 env.tool_bag、個人腰包 env.waist_pouch 共用同一個元件。
    /// ・位置由頭部推估：頭下方 waistOffset.y、左右 waistOffset.x（工具袋左腰、腰包右腰；xr.carry.swap＝true 時左右對調）、前方 waistOffset.z。
    ///   只跟著頭的水平方向轉（yaw），低頭、抬頭、歪頭時不動。xr.toolbag.mount＝wrist 時工具袋改掛左手腕。
    /// ・右手在 range 內時袋子算「打開」：每格的名稱標籤（Label_k）顯示。拿取、放回由 XrPlayer 的握把邏輯處理（掛位位置就是袋子裡的 Pocket_k）。
    /// </summary>
    public class XrCarryContainer : MonoBehaviour
    {
        public const string MountKey = "xr.toolbag.mount", SwapKey = "xr.carry.swap";

        [SerializeField] XrRig rig;
        [SerializeField] Transform container;
        [SerializeField] Vector3 waistOffset = new Vector3(-0.2f, -0.65f, 0.1f);
        [SerializeField] Vector3 wristOffset = new Vector3(0f, -0.04f, -0.08f);
        [SerializeField] bool canMountOnWrist;
        [SerializeField] float range = 0.25f;
        [SerializeField] GameObject[] labels = new GameObject[0];

        public bool Open { get; private set; }

        public void Setup(XrRig r, Transform c, Vector3 waist, bool wristOption, float handRange, GameObject[] labelObjects)
        {
            rig = r; container = c; waistOffset = waist; canMountOnWrist = wristOption; range = handRange; labels = labelObjects ?? new GameObject[0];
        }

        /// <summary>腰間位置（純函式）：頭的位置＋只用頭 yaw 轉過的偏移。</summary>
        public static Vector3 WaistPosition(Vector3 head, Quaternion headRot, Vector3 offset, bool swap)
        {
            var yaw = Quaternion.Euler(0f, headRot.eulerAngles.y, 0f);
            if (swap) offset.x = -offset.x;
            return head + yaw * offset;
        }

        ToolPouchDef pouch;
        float yaw; bool turning;

        /// <summary>第 4 點（純函式）：轉身超過 limit 度才開始跟，跟到對齊才停，平常不晃。</summary>
        public static float FollowYaw(float current, float head, ref bool turning, float limit, float step)
        {
            float d = Mathf.Abs(Mathf.DeltaAngle(current, head));
            if (d > limit) turning = true;
            if (!turning) return current;
            float next = Mathf.MoveTowardsAngle(current, head, step);
            if (Mathf.Abs(Mathf.DeltaAngle(next, head)) < 1f) turning = false;
            return next;
        }

        void LateUpdate()
        {
            if (rig == null || container == null || rig.Head == null || !InputMode.XrActive) return;
            var s = Sim.State;
            bool wrist = canMountOnWrist && s != null && s.Get(MountKey) == "wrist" && rig.LeftHand != null;
            if (wrist)
            {
                container.SetPositionAndRotation(rig.LeftHand.TransformPoint(wristOffset), rig.LeftHand.rotation);
            }
            else
            {
                var h = rig.Head;
                if (pouch == null) pouch = ToolPouchDef.Load();
                yaw = FollowYaw(yaw, h.rotation.eulerAngles.y, ref turning, pouch.followAngleDeg, pouch.followSpeedDeg * Time.deltaTime);
                var turned = Quaternion.Euler(0f, yaw, 0f);
                container.position = WaistPosition(h.position, turned, waistOffset, s != null && s.GetBool(SwapKey));
                container.rotation = turned;   // pivot 在背面中心、開口朝 +Y
            }
            // 第 4 點：教學模式每一格都顯示名稱，練習、考核只顯示手指到的那一格（XrPouchHover）
            string mode = s != null ? s.Get("training.mode") : ""; bool all = mode != "practice" && mode != "exam";
            string pointed = s != null ? "Label_" + (int)s.GetFloat(XrPouchHover.SlotKey) : "";
            Open = pointed != "Label_0";
            foreach (var l in labels) { if (l == null) continue; bool show = all || l.name == pointed; if (l.activeSelf != show) l.SetActive(show); }
        }
    }
}
