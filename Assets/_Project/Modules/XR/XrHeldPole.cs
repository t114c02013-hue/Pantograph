using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.XR;

namespace PantographSim.XR
{
    /// <summary>
    /// R1.5b b3（試玩 P2）2.5 m 驗電器、接地棒拿在手上時：
    /// ・雙手握：後手（右手）握住桿尾，左手在護環以下 frontGrabRadius 內按握把＝前手，桿身沿後手→前手；只有右手時桿身下垂 droopDeg（單手也能拿）。
    ///   位置每幀直接等於手的目標位置（等同 XRI 的 Instantaneous），不會被場景推開。
    /// ・桿身的 collider 換到 HeldPole 層（碰撞矩陣只和 ContactTarget 互動），放回桿架後換回原本的層。
    /// ・桿身穿過場景物件時，兩手各輕震一次（振幅、時間讀資料）；同一個物件 hapticCooldown 秒內不重複。不記錯、不顯示文字。互動判定照舊只看桿頭。
    /// shortcut: 雙手握持用自己算的姿態（後手→前手），沒有用 XRI XRGrabInteractable 多選取＋XRGeneralGrabTransformer（R1 的手持工具不是 XRI 互動物件，改成 XRI 要整個換掉工具握持方式）, 頭盔試玩覺得手感不對再改用 XRI
    /// </summary>
    public class XrHeldPole : MonoBehaviour
    {
        public const string LayerName = "HeldPole";
        /// <summary>左手正握著長桿（雙手握）：遙控器要隱藏（§2.1）。</summary>
        public static bool LeftHandHolding;

        [SerializeField] XrRig rig;
        [SerializeField] float length = 2.5f, guardFromTail = 1.6f, frontGrabRadius = 0.12f, droopDeg = 15f;
        [SerializeField] float hapticAmplitude = 0.2f, hapticSeconds = 0.05f, hapticCooldown = 0.3f;
        readonly Dictionary<GameObject, int> originalLayers = new Dictionary<GameObject, int>();
        readonly HapticDebounce debounce = new HapticDebounce();
        CapsuleCollider shaft;

        public void Setup(XrRig r, float poleLength, float guardRingFromTail) { rig = r; length = poleLength; guardFromTail = guardRingFromTail; }

        /// <summary>桿的姿態（純函式）：rear＝後手（桿尾），front＝前手（null＝單手）。回傳（桿尾位置, 方向）。</summary>
        public static (Vector3 tail, Vector3 dir) Pose(Vector3 rear, Quaternion rearRot, Vector3? front, float droop)
        {
            if (front.HasValue && (front.Value - rear).sqrMagnitude > 1e-4f) return (rear, (front.Value - rear).normalized);
            var fwd = rearRot * Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd).sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, fwd).normalized : rearRot * Vector3.right;
            return (rear, Quaternion.AngleAxis(droop, right) * fwd);   // 單手：桿身往下垂
        }

        /// <summary>左手在不在可以當前手的位置：在護環以下的桿身（桿尾起 guardFromTail 以內）frontGrabRadius 內。</summary>
        public static bool CanBeFrontHand(Vector3 tail, Vector3 dir, float guardFromTail, float radius, Vector3 hand)
        {
            float t = Mathf.Clamp(Vector3.Dot(hand - tail, dir), 0f, guardFromTail);
            return (tail + dir * t - hand).magnitude <= radius;
        }

        /// <summary>把 collider 換到 HeldPole 層（記下原本的層）／換回來。</summary>
        public static void SetHeldLayer(GameObject root, Dictionary<GameObject, int> keep, bool held)
        {
            int layer = LayerMask.NameToLayer(LayerName);
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (held) { if (!keep.ContainsKey(c.gameObject)) keep[c.gameObject] = c.gameObject.layer; if (layer >= 0) c.gameObject.layer = layer; }
                else if (keep.TryGetValue(c.gameObject, out var l)) c.gameObject.layer = l;
            }
            if (!held) keep.Clear();
        }

        void OnEnable()
        {
            shaft = gameObject.AddComponent<CapsuleCollider>();
            shaft.isTrigger = true; shaft.direction = 2; shaft.radius = 0.02f; shaft.height = length; shaft.center = new Vector3(0f, 0f, length / 2f);
            SetHeldLayer(gameObject, originalLayers, true);
        }

        void OnDisable()
        {
            SetHeldLayer(gameObject, originalLayers, false);
            if (shaft != null) Destroy(shaft);
            LeftHandHolding = false;
        }

        void LateUpdate()
        {
            if (rig == null || rig.RightHand == null) return;
            var rear = rig.RightHand;
            var (tail0, dir0) = Pose(rear.position, rear.rotation, null, droopDeg);
            bool grip = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).TryGetFeatureValue(CommonUsages.gripButton, out var g) && g;
            bool front = grip && rig.LeftHand != null && (LeftHandHolding || CanBeFrontHand(tail0, dir0, guardFromTail, frontGrabRadius, rig.LeftHand.position));
            LeftHandHolding = front;
            var (tail, dir) = Pose(rear.position, rear.rotation, front ? rig.LeftHand.position : (Vector3?)null, droopDeg);
            transform.SetPositionAndRotation(tail, Quaternion.LookRotation(dir, Vector3.up));
            // 桿身穿過場景：只查場景件（不含 HeldPole、手、互動點），兩手各輕震一次
            int mask = ~LayerMask.GetMask(LayerName, "ContactTarget", "UI", "Ignore Raycast", SceneLayers.Walkable, SceneLayers.LoadProbe);
            var hits = Physics.OverlapCapsule(tail, tail + dir * length, 0.02f, mask, QueryTriggerInteraction.Ignore).Where(c => !c.transform.IsChildOf(transform)).Select(c => c.gameObject);
            foreach (var h in hits)
                if (debounce.Hit(h, Time.time, hapticCooldown)) { XrHaptics.Pulse(XRNode.RightHand, hapticAmplitude, hapticSeconds); XrHaptics.Pulse(XRNode.LeftHand, hapticAmplitude, hapticSeconds); }
        }
    }

    /// <summary>同一個物件 cooldown 秒內只回傳一次 true（純邏輯，PoleNoCollisionTests 直接測）。</summary>
    public class HapticDebounce
    {
        readonly Dictionary<GameObject, float> last = new Dictionary<GameObject, float>();
        public bool Hit(GameObject o, float now, float cooldown)
        {
            if (last.TryGetValue(o, out var t) && now - t < cooldown) { last[o] = now; return false; }
            last[o] = now;
            return true;
        }
    }

    /// <summary>震動（OpenXR 裝置的 haptic impulse）。</summary>
    public static class XrHaptics
    {
        public static void Pulse(XRNode node, float amplitude, float seconds)
        {
            var d = InputDevices.GetDeviceAtXRNode(node);
            if (d.isValid && d.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse) d.SendHapticImpulse(0, amplitude, seconds);
        }
    }
}
