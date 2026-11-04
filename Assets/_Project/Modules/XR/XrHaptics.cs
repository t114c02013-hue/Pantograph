using UnityEngine;
using UnityEngine.XR;

namespace PantographSim.XR
{
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
