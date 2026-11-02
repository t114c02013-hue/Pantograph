using System;
using UnityEngine;

namespace PantographSim.Data
{
    // R1：座標系＝原點在弓頭中心正下方的車頂地板點，+Y 上、+Z 朝肘側滑板、平台在 −X 側，單位 m；標 EX 的是示例值。

    /// <summary>右搖桿平滑移動與暈動遮罩（EX）。</summary>
    [CreateAssetMenu(menuName = "PantographSim/R1/Locomotion Settings")]
    public class LocomotionSettings : ScriptableObject
    {
        public bool smoothMoveDefaultOn = true;
        public float speed = 1.0f, minSpeed = 0.6f, maxSpeed = 1.5f, roofMaxSpeed = 1.0f;
        public float deadzone = 0.15f;
        public bool vignetteDefaultOn = true;
        [Range(0f, 1f)] public float vignetteStrength = 0.6f;
        public float capsuleRadius = 0.2f, stepOffset = 0.2f;
        [Tooltip("走到關閉的活動欄前多近時提示")] public float gateHintDistance = 0.3f;
        [Tooltip("蹲姿輔助切換提示顯示秒數")] public float crouchToastSeconds = 1.5f;
    }
}
