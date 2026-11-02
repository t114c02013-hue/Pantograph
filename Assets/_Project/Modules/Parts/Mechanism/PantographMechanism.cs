using System;
using UnityEngine;

namespace PantographSim.Parts.Mechanism
{
    /// <summary>
    /// 集電弓升降機構：以一個升弓量 Lift（0=降弓、1=升弓）平滑驅動各關節。
    /// 模型網格是升弓姿態；每個關節記錄「降弓時相對升弓轉了幾度」，中間依 Lift 內插。
    /// 拉桿等兩端都有鉸接的連桿，用 AimLink 把另一端對準它所連接的零件。
    /// </summary>
    public class PantographMechanism : MonoBehaviour
    {
        [Serializable]
        public class Joint
        {
            public string name;
            public Transform segment;
            [Tooltip("轉軸（父物件座標）")] public Vector3 axis = Vector3.right;
            [Tooltip("降弓時相對升弓的角度")] public float loweredAngle;
            [Tooltip("降弓時沿轉軸的位移（父物件座標）")] public Vector3 loweredOffset;
            [Tooltip("升弓（靜止）姿態")] public Vector3 restPosition;
            public Quaternion restRotation = Quaternion.identity;
        }

        [Serializable]
        public class AimLink
        {
            public string name;
            public Transform segment;
            [Tooltip("連桿另一端的鉸接點（連桿本身座標）")] public Vector3 anchor;
            public Transform target;
            [Tooltip("同一個鉸接點（目標零件座標）")] public Vector3 targetAnchor;
            [Tooltip("轉軸（父物件座標）")] public Vector3 axis = Vector3.right;
        }

        public enum State { Raised, Lowered, Raising, Lowering }

        [SerializeField] Joint[] joints = Array.Empty<Joint>();
        [SerializeField] AimLink[] aimLinks = Array.Empty<AimLink>();

        [Header("動作")]
        [SerializeField] bool startRaised = false;
        [SerializeField, Min(0.1f)] float raiseDuration = 7f;
        [SerializeField, Min(0.1f)] float lowerDuration = 4.5f;
        [SerializeField] AnimationCurve easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        float progress;   // 0..1 的線性進度，經 easing 得到 Lift
        int direction;    // +1 升弓中、-1 降弓中、0 靜止

        public float Lift { get; private set; }
        public State CurrentState =>
            direction > 0 ? State.Raising : direction < 0 ? State.Lowering : progress >= 1f ? State.Raised : State.Lowered;
        public bool IsMoving => direction != 0;
        public event Action<State> StateChanged;

        public void Configure(Joint[] newJoints, AimLink[] newAimLinks)
        {
            joints = newJoints;
            aimLinks = newAimLinks;
        }

        void Awake()
        {
            progress = startRaised ? 1f : 0f;
            SetLift(progress);
        }

        public void Raise() => StartMove(+1);
        public void Lower() => StartMove(-1);

        /// <summary>升弓中/已升弓 → 降弓；反之 → 升弓。動作中呼叫會立即反向。</summary>
        public void Toggle()
        {
            bool goingUp = direction > 0 || (direction == 0 && progress >= 1f);
            StartMove(goingUp ? -1 : +1);
        }

        void StartMove(int dir)
        {
            if ((dir > 0 && progress >= 1f) || (dir < 0 && progress <= 0f)) return;
            direction = dir;
            StateChanged?.Invoke(CurrentState);
        }

        void Update()
        {
            if (direction == 0) return;
            float duration = direction > 0 ? raiseDuration : lowerDuration;
            progress = Mathf.Clamp01(progress + direction * Time.deltaTime / duration);
            SetLift(easing.Evaluate(progress));
            if (progress <= 0f || progress >= 1f)
            {
                direction = 0;
                StateChanged?.Invoke(CurrentState);
            }
        }

        /// <summary>直接設定姿態（0=降弓、1=升弓），不經動畫。</summary>
        public void SetLift(float lift)
        {
            Lift = Mathf.Clamp01(lift);
            float lowered = 1f - Lift;
            foreach (var j in joints)
            {
                if (j.segment == null) continue;
                j.segment.localPosition = j.restPosition + j.loweredOffset * lowered;
                j.segment.localRotation = j.restRotation * Quaternion.AngleAxis(j.loweredAngle * lowered, j.axis);
            }
            foreach (var a in aimLinks) ApplyAim(a);
        }

        static void ApplyAim(AimLink a)
        {
            if (a.segment == null || a.target == null) return;
            var parent = a.segment.parent;
            Vector3 axis = parent != null ? parent.TransformDirection(a.axis) : a.axis;
            Vector3 pivot = a.segment.position;
            Vector3 from = Vector3.ProjectOnPlane(a.segment.TransformPoint(a.anchor) - pivot, axis);
            Vector3 to = Vector3.ProjectOnPlane(a.target.TransformPoint(a.targetAnchor) - pivot, axis);
            if (from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f) return;
            float angle = Vector3.SignedAngle(from, to, axis);
            a.segment.rotation = Quaternion.AngleAxis(angle, axis) * a.segment.rotation;
        }

        /// <summary>給驗證工具用：某個連桿在目前姿態下兩端的落差（公尺）。</summary>
        public float AimGap(int index)
        {
            var a = aimLinks[index];
            return Vector3.Distance(a.segment.TransformPoint(a.anchor), a.target.TransformPoint(a.targetAnchor));
        }

        public int AimLinkCount => aimLinks.Length;
    }
}
