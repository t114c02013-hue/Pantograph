using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace PantographSim.XR
{
    /// <summary>
    /// VR 試玩修正 第 5 點：原廠控制器模型。按鍵跟著手上的實體按鍵動（*_pressed_value 節點＝Lerp/Slerp(min, max, t)），
    /// 按鍵說明不放在模型上，低頭看控制器時的小卡顯示（ControllerLabelsView，XrPlayer 寫 xr.keys.card）。
    /// </summary>
    public class XrControllerVisual : MonoBehaviour
    {
        public struct Buttons { public float trigger, grip; public bool primary, secondary, stickClick; public Vector2 stick; }

        /// <summary>測試用：不讀實體控制器。</summary>
        public static Buttons? Scripted;

        struct Node { public Transform value, min, max; public string name; }

        [SerializeField] bool left;

        readonly List<Node> nodes = new List<Node>();

        public void Setup(bool isLeft) => left = isLeft;

        void Awake()
        {
            var all = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) all[t.name] = t;
            foreach (var kv in all)
            {
                if (!kv.Key.EndsWith("_value")) continue;
                string stem = kv.Key.Substring(0, kv.Key.Length - "_value".Length);
                if (all.TryGetValue(stem + "_min", out var min) && all.TryGetValue(stem + "_max", out var max))
                    nodes.Add(new Node { value = kv.Value, min = min, max = max, name = kv.Key });
            }
        }

        /// <summary>節點的 t（WebXR motion-controllers 的對應）；沒有對應的回傳 0。</summary>
        public static float PressAmount(string node, Buttons b)
        {
            if (node.Contains("trigger")) return b.trigger;
            if (node.Contains("squeeze")) return b.grip;
            if (node.Contains("xaxis")) return (b.stick.x + 1f) / 2f;
            if (node.Contains("yaxis")) return (b.stick.y + 1f) / 2f;
            if (node.Contains("thumbstick")) return b.stickClick ? 1f : 0f;
            if (node.StartsWith("a_button") || node.StartsWith("x_button")) return b.primary ? 1f : 0f;
            if (node.StartsWith("b_button") || node.StartsWith("y_button")) return b.secondary ? 1f : 0f;
            return 0f;
        }

        Buttons Read()
        {
            if (Scripted != null) return Scripted.Value;
            var d = InputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);
            var b = new Buttons();
            if (!d.isValid) return b;
            d.TryGetFeatureValue(CommonUsages.trigger, out b.trigger);
            d.TryGetFeatureValue(CommonUsages.grip, out b.grip);
            d.TryGetFeatureValue(CommonUsages.primaryButton, out b.primary);
            d.TryGetFeatureValue(CommonUsages.secondaryButton, out b.secondary);
            d.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out b.stickClick);
            d.TryGetFeatureValue(CommonUsages.primary2DAxis, out b.stick);
            return b;
        }

        void LateUpdate()
        {
            var b = Read();
            foreach (var n in nodes)
            {
                float t = PressAmount(n.name, b);
                n.value.localPosition = Vector3.Lerp(n.min.localPosition, n.max.localPosition, t);
                n.value.localRotation = Quaternion.Slerp(n.min.localRotation, n.max.localRotation, t);
            }
        }
    }
}
