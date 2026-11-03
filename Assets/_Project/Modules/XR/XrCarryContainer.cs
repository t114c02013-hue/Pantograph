using PantographSim.Core;
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
                container.position = WaistPosition(h.position, h.rotation, waistOffset, s != null && s.GetBool(SwapKey));
                container.rotation = Quaternion.Euler(0f, h.rotation.eulerAngles.y, 0f);   // pivot 在背面中心、開口朝 +Y
            }
            Open = rig.RightHand != null && (rig.RightHand.position - container.position).magnitude <= range;
            foreach (var l in labels) if (l != null && l.activeSelf != Open) l.SetActive(Open);
        }
    }
}
