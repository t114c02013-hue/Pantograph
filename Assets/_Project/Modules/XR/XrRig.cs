using System.Collections.Generic;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PantographSim.XR
{
    /// <summary>XR Origin 上的頭與雙手位置（註冊成 Core 的 IXrRig，給介面擺放世界空間面板）。</summary>
    public class XrRig : MonoBehaviour, IXrRig, IPoseSource
    {
        // ---------- IPoseSource：頭盔與雙手（紀錄、旁觀鏡頭用） ----------
        string IPoseSource.SourceName => "xr";
        bool IPoseSource.PoseAvailable => isActiveAndEnabled && eye != null;
        Pose IPoseSource.Head => eye != null ? new Pose(eye.transform.position, eye.transform.rotation) : Pose.identity;
        bool IPoseSource.HasHands => leftHand != null && rightHand != null;
        Pose IPoseSource.LeftHand => leftHand != null ? new Pose(leftHand.position, leftHand.rotation) : Pose.identity;
        Pose IPoseSource.RightHand => RightRayOrigin != null ? new Pose(RightRayOrigin.position, RightRayOrigin.rotation) : Pose.identity;

        [SerializeField] Camera eye;
        [SerializeField] Transform leftHand, rightHand, rightRayOrigin, heldToolAnchor;

        public Camera Eye => eye;
        public Transform Head => eye != null ? eye.transform : null;
        public Transform LeftHand => leftHand;
        public Transform RightHand => rightHand;
        public Transform RightRayOrigin => rightRayOrigin != null ? rightRayOrigin : rightHand;
        public Transform HeldToolAnchor => heldToolAnchor != null ? heldToolAnchor : rightHand;

        public void Setup(Camera cam, Transform left, Transform right, Transform rayOrigin, Transform toolAnchor)
        {
            eye = cam; leftHand = left; rightHand = right; rightRayOrigin = rayOrigin; heldToolAnchor = toolAnchor;
        }
    }

    /// <summary>
    /// 從 Input System 讀控制器按鍵（綁定路徑來自 xr_bindings.json），組成 XrFrame。
    /// 另外回報右手控制器是否在追蹤中（失去追蹤時升降要停）。
    /// </summary>
    public class XrControllerInput
    {
        readonly Dictionary<string, InputAction> buttons = new Dictionary<string, InputAction>();
        InputAction leftStick, rightStick;

        public XrControllerInput(XrBindingTable table)
        {
            foreach (var b in table.buttons)
            {
                var a = new InputAction(b.button, InputActionType.Button);
                foreach (var p in b.paths) a.AddBinding(p);
                a.Enable();
                buttons[b.button] = a;
            }
            leftStick = Stick("LeftStick", table.leftStickPaths);
            rightStick = Stick("RightStick", table.rightStickPaths);
        }

        static InputAction Stick(string name, string[] paths)
        {
            var a = new InputAction(name, InputActionType.Value, expectedControlType: "Vector2");
            foreach (var p in paths) a.AddBinding(p);
            a.Enable();
            return a;
        }

        bool Pressed(string name) => buttons.TryGetValue(name, out var a) && a.IsPressed();

        public XrFrame Read() => new XrFrame
        {
            rightTrigger = Pressed("RightTrigger"), rightGrip = Pressed("RightGrip"), leftGrip = Pressed("LeftGrip"),
            a = Pressed("A"), b = Pressed("B"), x = Pressed("X"), y = Pressed("Y"), menu = Pressed("Menu"), leftStickClick = Pressed("LeftStickClick"), rightStickClick = Pressed("RightStickClick"),
            leftStick = leftStick.ReadValue<Vector2>(), rightStick = rightStick.ReadValue<Vector2>()
        };

        public static bool RightHandTracked()
        {
            var dev = InputSystem.GetDevice<UnityEngine.InputSystem.XR.XRController>(UnityEngine.InputSystem.CommonUsages.RightHand);
            return dev != null && dev.isTracked.isPressed;
        }

        public void Dispose()
        {
            foreach (var a in buttons.Values) a.Dispose();
            leftStick?.Dispose(); rightStick?.Dispose();
        }
    }
}
