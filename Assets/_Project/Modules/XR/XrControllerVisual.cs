using System.Collections.Generic;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;
using UnityEngine.XR;

namespace PantographSim.XR
{
    /// <summary>
    /// VR 試玩修正 第 5 點：原廠控制器模型。按鍵跟著手上的實體按鍵動（*_pressed_value 節點＝Lerp/Slerp(min, max, t)），
    /// 標示掛在 LBL_*，文字讀 InputBindingTable，只顯示這個情境有作用的按鍵；教學模式一直顯示，其他模式按住 Y 才顯示。
    /// </summary>
    public class XrControllerVisual : MonoBehaviour
    {
        public struct Buttons { public float trigger, grip; public bool primary, secondary, stickClick; public Vector2 stick; }

        /// <summary>測試用：不讀實體控制器。</summary>
        public static Buttons? Scripted;

        struct Node { public Transform value, min, max; public string name; }

        [SerializeField] bool left;
        [SerializeField] XrRig rig;
        [SerializeField] XrPlayer player;

        readonly List<Node> nodes = new List<Node>();
        readonly List<TextMesh> labels = new List<TextMesh>();
        InputBindingTable table;
        string shown;

        public void Setup(bool isLeft) => left = isLeft;
        public void Setup(XrRig r, XrPlayer p) { rig = r; player = p; }

        void Awake()
        {
            table = InputBindingTable.LoadDefault();
            var all = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) all[t.name] = t;
            foreach (var kv in all)
            {
                if (!kv.Key.EndsWith("_value")) continue;
                string stem = kv.Key.Substring(0, kv.Key.Length - "_value".Length);
                if (all.TryGetValue(stem + "_min", out var min) && all.TryGetValue(stem + "_max", out var max))
                    nodes.Add(new Node { value = kv.Value, min = min, max = max, name = kv.Key });
            }
            labels.AddRange(GetComponentsInChildren<TextMesh>(true));
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

        /// <summary>教學模式一直顯示，其他模式按住 Y 才顯示。</summary>
        public static bool ShowLabels(string mode, bool holdingY) => mode == "tutorial" || holdingY;

        /// <summary>LBL_* 對應的按鍵表控制項。</summary>
        public static string ControlOf(string anchor, bool isLeft)
        {
            string c = anchor.Substring("LBL_".Length);
            return c == "Trigger" || c == "Grip" || c == "Stick" ? (isLeft ? "Left" : "Right") + c : c;
        }

        /// <summary>標示文字：這個情境沒作用回傳空字串；只取冒號前的短名稱。</summary>
        public static string LabelText(InputBindingTable table, string context, string control)
        {
            var row = table.Find(context, control);
            return row == null || !row.enabled || row.functionId == "None" ? "" : row.labelKey.Split('：')[0];
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
            UpdateLabels();
        }

        void UpdateLabels()
        {
            var s = Sim.State;
            if (s == null || player == null) return;
            bool show = ShowLabels(s.Get("training.mode"), player.LastFrame.y);
            string key = show ? player.BindingContext : "";
            if (key != shown)
            {
                shown = key;
                foreach (var l in labels)
                {
                    l.text = show ? LabelText(table, key, ControlOf(l.transform.parent.name, left)) : "";
                    l.gameObject.SetActive(l.text != "");
                }
            }
            if (rig == null || rig.Head == null) return;
            foreach (var l in labels) if (l.gameObject.activeSelf) l.transform.rotation = Quaternion.LookRotation(l.transform.position - rig.Head.position);
        }
    }
}
