using System.Linq;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// R1.5b b3 隨身容器（§3.4，D30）：工具袋 env.tool_bag、個人腰包 env.waist_pouch 共用同一個元件。
    /// ・位置由頭部推估：頭下方 waistOffset.y、左右 waistOffset.x（工具袋左腰、腰包右腰；xr.carry.swap＝true 時左右對調）、前方 waistOffset.z。
    ///   只跟著頭的水平方向轉（yaw），低頭、抬頭、歪頭時不動。xr.toolbag.mount＝wrist 時工具袋改掛左手腕。
    /// ・工具袋（terry 試玩：不要亂動）：傳送到站點時擺一次，在腰高、人的左前方，之後不跟頭也不跟身體；換站點或重設高度才重擺。格子名稱一直顯示。
    /// ・右手在 range 內時袋子算「打開」。拿取、放回由 XrPlayer 的握把邏輯處理（掛位位置就是袋子裡的 Pocket_k）。
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

        /// <summary>工具袋在站點的固定位置（純函式）：到站時的頭的左前方，高度只看樓層與站姿眼高。</summary>
        public static Pose ToolBagPose(Vector3 head, Quaternion headRot, float floorY, bool swap, PostureDef p)
        {
            var yaw = Quaternion.Euler(0f, headRot.eulerAngles.y, 0f);
            var pos = head + yaw * new Vector3(swap ? -p.toolBagOffset.x : p.toolBagOffset.x, 0f, p.toolBagOffset.y);
            pos.y = floorY + p.standEye - p.toolBagBelowEye;
            return new Pose(pos, yaw);
        }

        /// <summary>換站點、重設高度或對調左右才重擺（傳送後頭要過幾幀才到位，所以連放幾幀）。</summary>
        void PlaceToolBag(ISimState s, Transform h)
        {
            bool swap = s != null && s.GetBool(SwapKey);
            string where = s == null ? "" : $"{s.Get("xr.point")}|{s.GetFloat("xr.recenter", -1f)}|{swap}";
            if (where != placedFor) { placedFor = where; settleFrames = SettleFrames; }
            if (posture == null) posture = PostureDef.Load();
            float floorY = h.parent != null ? h.parent.position.y : 0f;   // Camera Offset：含樓層與車頂的視角調整
            if (settleFrames > 0)
            {
                settleFrames--;
                placed = ToolBagPose(h.position, h.rotation, floorY, swap, posture);
                container.SetPositionAndRotation(placed.position, placed.rotation);
                ArrangeLabels(h.position);
            }
            container.SetPositionAndRotation(new Vector3(placed.position.x, floorY + posture.standEye - posture.toolBagBelowEye, placed.position.z), placed.rotation);
        }

        /// <summary>袋子旁一列清單「編號 工具名」，格子上放同一個編號（和提示的「工具袋第 N 格」一樣），都面向人。</summary>
        void ArrangeLabels(Vector3 head)
        {
            var sorted = labels.Where(l => l != null).OrderBy(l => Slot(l.name)).ToList();
            if (sorted.Count == 0) return;
            float edge = sorted.Min(l => Vector3.Dot(l.transform.parent.position - container.position, container.right));
            var foot = container.position + container.right * (edge - posture.toolBagListGap) + Vector3.up * posture.toolBagListRise;
            for (int i = 0; i < sorted.Count; i++)
            {
                var t = sorted[i].transform;
                t.position = foot + Vector3.up * (sorted.Count - 1 - i) * posture.toolBagListRow;
                t.rotation = Quaternion.LookRotation(t.position - head, Vector3.up);
                var mesh = sorted[i].GetComponent<TextMesh>();
                if (mesh == null) continue;
                mesh.anchor = TextAnchor.MiddleRight;
                string number = Slot(sorted[i].name).ToString();
                if (!mesh.text.StartsWith(number + " ")) mesh.text = number + " " + mesh.text;
                var num = PocketNumber(t, mesh, number);
                num.position = t.parent.position + Vector3.up * posture.toolBagListRise;
                num.rotation = Quaternion.LookRotation(num.position - head, Vector3.up);
            }
        }

        static Transform PocketNumber(Transform label, TextMesh from, string number)
        {
            var found = label.parent.Find("Num_" + number);
            if (found != null) return found;
            var mesh = new GameObject("Num_" + number).AddComponent<TextMesh>();
            mesh.transform.SetParent(label.parent, false);
            mesh.transform.localScale = label.localScale;
            mesh.font = from.font; mesh.GetComponent<MeshRenderer>().sharedMaterial = from.GetComponent<MeshRenderer>().sharedMaterial;
            mesh.characterSize = from.characterSize; mesh.fontSize = from.fontSize; mesh.anchor = TextAnchor.MiddleCenter; mesh.text = number; mesh.color = Color.white;
            return mesh.transform;
        }

        static int Slot(string labelName) => int.TryParse(labelName.Substring(labelName.IndexOf('_') + 1), out int k) ? k : 0;

        const int SettleFrames = 3;
        ToolPouchDef pouch;
        PostureDef posture;
        string placedFor;
        int settleFrames;
        Pose placed;
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
            else if (canMountOnWrist) PlaceToolBag(s, rig.Head);   // 只有工具袋能掛手腕
            else
            {
                var h = rig.Head;
                if (pouch == null) pouch = ToolPouchDef.Load();
                yaw = FollowYaw(yaw, h.rotation.eulerAngles.y, ref turning, pouch.followAngleDeg, pouch.followSpeedDeg * Time.deltaTime);
                var turned = Quaternion.Euler(0f, yaw, 0f);
                container.position = WaistPosition(h.position, turned, waistOffset, s != null && s.GetBool(SwapKey));
                container.rotation = turned;   // pivot 在背面中心、開口朝 +Y
            }
            // 名稱一直顯示；手指到的那一格和教學模式這一步要拿的那一格變色（XrPouchHover）
            string pointed = s != null ? "Label_" + (int)s.GetFloat(XrPouchHover.SlotKey) : "";
            string wanted = s != null && s.Get("training.mode") == "tutorial" ? "Label_" + (int)s.GetFloat(XrPouchHover.TargetKey) : "";
            Open = pointed != "Label_0";
            foreach (var l in labels)
            {
                if (l == null) continue;
                if (!l.activeSelf) l.SetActive(true);
                var mesh = l.GetComponent<TextMesh>();
                if (mesh != null) mesh.color = l.name == pointed || l.name == wanted ? new Color(1f, 0.72f, 0.15f) : Color.white;
            }
        }
    }
}
