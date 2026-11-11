using System.Collections.Generic;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace PantographSim.XR
{
    /// <summary>
    /// R1.5b b3 隨身容器（§3.4，D30）：工具袋 env.tool_bag、個人腰包 env.waist_pouch 共用同一個元件。
    /// ・位置由頭部推估：頭下方 waistOffset.y、左右 waistOffset.x（工具袋左腰、腰包右腰；xr.carry.swap＝true 時左右對調）、前方 waistOffset.z。
    ///   只跟著頭的水平方向轉（yaw），低頭、抬頭、歪頭時不動。xr.toolbag.mount＝wrist 時工具袋改掛左手腕。
    /// ・工具袋（terry 試玩：不要亂動）：傳送到站點時擺一次，在腰高、人的左前方，之後不跟頭也不跟身體；換站點或重設高度才重擺。不放文字，用工具外型分。
    /// ・terry 2026-11-11：左手握把抓袋子上緣的腰帶隧道（Belt_Tunnel，PanelHandle 層、只給左手），抓著只跟 yaw、保持直立，放開夾在 PostureDef 的範圍內停在那裡；
    ///   換站點、重設高度、對調左右回預設位置，抓著傳送先放開。放開發 PanelMoved（panelId＝toolbag）給紀錄。
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

        /// <summary>左手放下工具袋的夾限（純函式）：高度在樓層＋minAboveFloor 到樓層＋站姿眼高＋maxAboveEye，水平離站點不超過 maxReach。</summary>
        public static Vector3 ClampBagDrop(Vector3 pos, float floorY, Vector3 anchor, PostureDef p)
        {
            pos.y = Mathf.Clamp(pos.y, floorY + p.toolBagMinAboveFloor, floorY + p.standEye + p.toolBagMaxAboveEye);
            var flat = new Vector2(pos.x - anchor.x, pos.z - anchor.z);
            if (flat.magnitude > p.toolBagMaxReach) { flat = flat.normalized * p.toolBagMaxReach; pos.x = anchor.x + flat.x; pos.z = anchor.z + flat.y; }
            return pos;
        }

        /// <summary>換站點、重設高度或對調左右才重擺（傳送後頭要過幾幀才到位，所以連放幾幀）。左手放過就停在放下的地方（高度記成相對樓層）。</summary>
        void PlaceToolBag(ISimState s, Transform h)
        {
            bool swap = s != null && s.GetBool(SwapKey);
            string where = s == null ? "" : $"{s.Get("xr.point")}|{s.GetFloat("xr.recenter", -1f)}|{swap}";
            if (where != placedFor)
            {
                if (bagGrab != null && bagGrab.isSelected) bagGrab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)bagGrab);   // 抓著傳送：先放開（會觸發 Dropped，所以排在重設前）
                placedFor = where; settleFrames = SettleFrames; dropped = false;
            }
            if (posture == null) posture = PostureDef.Load();
            float floorY = h.parent != null ? h.parent.position.y : 0f;   // Camera Offset：含樓層與車頂的視角調整
            if (bagGrab == null) BuildBagHandle(h);
            if (bagGrab != null && bagGrab.isSelected)
            {
                var yawOnly = Quaternion.Euler(0f, bagGrab.transform.eulerAngles.y, 0f);
                container.SetPositionAndRotation(bagGrab.transform.position + yawOnly * grabOffset, yawOnly * grabRot);   // 只跟 yaw，袋子保持直立
                return;
            }
            if (dropped && settleFrames <= 0)
            {
                container.SetPositionAndRotation(new Vector3(placed.position.x, floorY + droppedHeight, placed.position.z), placed.rotation);
                FollowBagHandle();
                return;
            }
            if (settleFrames > 0)
            {
                settleFrames--;
                placed = ToolBagPose(h.position, h.rotation, floorY, swap, posture);
                placed.rotation *= Quaternion.Euler(0f, 180f, 0f);   // 腰包正面（+Z）朝學員：後排比前排高，背面朝人會擋住袋口
                container.SetPositionAndRotation(placed.position, placed.rotation);
                foreach (var l in labels) if (l != null && l.activeSelf) l.SetActive(false);   // terry：工具袋不放字，用外型分
            }
            container.SetPositionAndRotation(new Vector3(placed.position.x, floorY + posture.standEye - posture.toolBagBelowEye, placed.position.z), placed.rotation);
            FollowBagHandle();
        }

        /// <summary>抓的地方：袋子上緣的腰帶隧道。另開一個物件掛 XRGrabInteractable（不放在袋子底下，格子的碰撞體才不會被收進來變成可抓）。</summary>
        void BuildBagHandle(Transform head)
        {
            Transform tunnel = null;
            foreach (var t in container.GetComponentsInChildren<Transform>(true)) if (t.name == "Belt_Tunnel") { tunnel = t; break; }
            var tunnelR = tunnel != null ? tunnel.GetComponent<Renderer>() : null;
            if (tunnelR == null) return;
            bagRenderers = container.GetComponentsInChildren<Renderer>(true);
            var go = new GameObject("UI_ToolBagHandle");
            go.layer = 2;   // Ignore Raycast：右手射線點格子時不被把手擋住
            go.transform.SetParent(container.parent, false);
            var b = tunnelR.bounds;
            go.transform.SetPositionAndRotation(b.center, container.rotation);
            var col = go.AddComponent<BoxCollider>();
            col.size = go.transform.InverseTransformVector(b.size + Vector3.one * 0.02f);
            col.size = new Vector3(Mathf.Abs(col.size.x), Mathf.Abs(col.size.y), Mathf.Abs(col.size.z));
            handleOffset = container.InverseTransformPoint(b.center);
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            var grab = go.AddComponent<LeftHandOnlyGrab>();
            grab.hand = rig.LeftHand;
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.throwOnDetach = false;
            grab.useDynamicAttach = true;
            grab.interactionLayers = InteractionLayerMask.GetMask(XrPanels.HandleLayerName);
            grab.hoverEntered.AddListener(_ => Tint(true));
            grab.hoverExited.AddListener(_ => Tint(false));
            grab.selectEntered.AddListener(a =>
            {
                BagHeld = true;
                var yawOnly = Quaternion.Euler(0f, grab.transform.eulerAngles.y, 0f);
                grabOffset = Quaternion.Inverse(yawOnly) * (container.position - grab.transform.position);
                grabRot = Quaternion.Inverse(yawOnly) * container.rotation;
                var p = pouch != null ? pouch : (pouch = ToolPouchDef.Load());
                if (a.interactorObject is XRBaseControllerInteractor ci && ci.xrController != null) ci.xrController.SendHapticImpulse(p.grabAmplitude, p.grabSeconds);
            });
            grab.selectExited.AddListener(_ => Dropped(head));
            bagGrab = grab;
        }

        void Dropped(Transform head)
        {
            BagHeld = false;
            if (posture == null) posture = PostureDef.Load();
            float floorY = head.parent != null ? head.parent.position.y : 0f;
            var anchor = head.parent != null ? head.parent.position : container.position;   // Camera Offset 的水平位置＝站點
            var pos = ClampBagDrop(container.position, floorY, anchor, posture);
            placed = new Pose(pos, Quaternion.Euler(0f, container.eulerAngles.y, 0f));
            droppedHeight = pos.y - floorY;
            dropped = true;
            container.SetPositionAndRotation(placed.position, placed.rotation);
            FollowBagHandle();
            Sim.Bus?.Publish(new PanelMoved { panelId = "toolbag", standPointId = Sim.State?.Get("xr.point") ?? "", x = pos.x, y = pos.y, z = pos.z, yaw = placed.rotation.eulerAngles.y, time = SimClock.Now });
        }

        /// <summary>沒抓著時把手跟著袋子。</summary>
        void FollowBagHandle()
        {
            if (bagGrab != null && !bagGrab.isSelected) bagGrab.transform.SetPositionAndRotation(container.TransformPoint(handleOffset), container.rotation);
        }

        /// <summary>左手靠近外框：袋身變亮（格子裡的工具不變）。</summary>
        void Tint(bool on)
        {
            var block = new MaterialPropertyBlock();
            foreach (var r in bagRenderers)
            {
                if (r == null || IsToolModel(r.transform)) continue;
                if (on) { block.Clear(); block.SetColor("_Color", HoverColor); r.SetPropertyBlock(block); }
                else r.SetPropertyBlock(null);
            }
        }

        static readonly Color HoverColor = new Color(1f, 0.85f, 0.55f);
        static bool IsToolModel(Transform t) { for (; t != null; t = t.parent) if (t.name.StartsWith("Model_")) return true; return false; }

        /// <summary>只有左手抓得到（右手的 PanelGrab 也在 PanelHandle 層，右握把是拿工具），而且手不在任何格子前。</summary>
        class LeftHandOnlyGrab : XRGrabInteractable
        {
            public Transform hand;
            // 手在格子前（ToolPouchDef.slotRadius，不用教學的 targetRadius，不然教學時袋子抓不起來）＝拿放工具，不抓袋子
            bool Left(IXRInteractor i) => hand != null && i.transform.IsChildOf(hand) && (isSelected || !SlotUnderHand(hand.position));   // 抓著時不再判（格子跟著袋子走）
            static bool SlotUnderHand(Vector3 p) => Sim.State != null && XrPouchHover.Nearest(SimIds.RackSlotNumbers(Sim.State), k => Sim.Registry != null && Sim.Registry.TryGetPosition("env.tool_rack.slot_" + k, out var q) ? q : (Vector3?)null, new[] { p }, ToolPouchDef.Load().slotRadius).slot > 0;
            public override bool IsHoverableBy(IXRHoverInteractor interactor) => base.IsHoverableBy(interactor) && Left(interactor);
            public override bool IsSelectableBy(IXRSelectInteractor interactor) => base.IsSelectableBy(interactor) && Left(interactor);
        }

        /// <summary>左手抓著工具袋（XrPouchHover 這時不震動）。</summary>
        public static bool BagHeld { get; private set; }
        /// <summary>左手碰到工具袋的抓取區（XrPlayer 的左握把這時抓袋子，不拿格子裡的工具）。</summary>
        public static bool BagHovered => instance != null && instance.bagGrab != null && instance.bagGrab.isHovered;
        static XrCarryContainer instance;

        void OnDestroy() { if (instance == this) { instance = null; BagHeld = false; } }   // 換場景時抓著也不留在「抓著」

        /// <summary>terry：工具袋用外型分。工具在袋裡就顯示格子裡的模型，拿出來（在手上或掛在繩上）就藏起來。</summary>
        void ShowBagTools(ISimState s)
        {
            if (bagTools == null)
            {
                bagTools = new List<(int, GameObject)>();
                foreach (var t in container.GetComponentsInChildren<Transform>(true))
                {
                    var pocket = t.parent != null && t.parent.name.StartsWith("Stow_") ? t.parent.parent : t.parent;   // 模型放在格子的 Stow 節點上
                    if (!t.name.StartsWith("Model_") || pocket == null || !pocket.name.StartsWith("Pocket_")) continue;
                    if (!int.TryParse(pocket.name.Substring("Pocket_".Length), out int k)) continue;
                    foreach (var c in t.GetComponentsInChildren<Collider>(true)) Destroy(c);   // 射線和手要碰到的是格子，不是模型
                    bagTools.Add((k, t.gameObject));
                }
            }
            foreach (var (k, model) in bagTools)
            {
                bool inBag = s.Get("rack.slot_" + k, "on_rack") == "on_rack";
                if (model.activeSelf != inBag) model.SetActive(inBag);
            }
        }

        List<(int slot, GameObject model)> bagTools;
        XRGrabInteractable bagGrab;
        Renderer[] bagRenderers = new Renderer[0];
        Vector3 handleOffset, grabOffset;
        Quaternion grabRot = Quaternion.identity;
        bool dropped;
        float droppedHeight;
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
            else if (canMountOnWrist) { instance = this; PlaceToolBag(s, rig.Head); }   // 只有工具袋能掛手腕
            else
            {
                var h = rig.Head;
                if (pouch == null) pouch = ToolPouchDef.Load();
                yaw = FollowYaw(yaw, h.rotation.eulerAngles.y, ref turning, pouch.followAngleDeg, pouch.followSpeedDeg * Time.deltaTime);
                var turned = Quaternion.Euler(0f, yaw, 0f);
                container.position = WaistPosition(h.position, turned, waistOffset, s != null && s.GetBool(SwapKey));
                container.rotation = turned;   // pivot 在背面中心、開口朝 +Y
            }
            if (canMountOnWrist && s != null) ShowBagTools(s);
            // 手指到的那一格、教學模式這一步要拿的那一格由 XrPouchHover 讓格子變色
            Open = s != null && (int)s.GetFloat(XrPouchHover.SlotKey) != 0;
        }
    }
}
