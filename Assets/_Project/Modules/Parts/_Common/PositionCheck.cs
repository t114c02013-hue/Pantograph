using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// 位置檢測：每幀量零件上的檢測點到目標點的距離，只寫黑板 part.&lt;零件&gt;.&lt;用途&gt;_distance_cm 與帶正負號的 _delta_cm
    /// （檢測點比目標高為正＝要往下）。是否「到位」不在這裡判定：由步驟規則（ValueInRange／條件）與安全規則決定，
    /// 容許值只有一個來源（降弓：SR_Global_NotLowered，由 App 寫入 _tolerance_cm）。
    /// 顏色顯示讀黑板的 _tolerance_cm（沒有時用 PC_* 的 toleranceCm）：目標綠框到位時更亮，檢測點到位綠色、未到位紅色。
    /// </summary>
    public class PositionCheck : MonoBehaviour, IStateSensor
    {
        [SerializeField] PositionCheckDefinition definition;
        [SerializeField] Transform checkPoint;
        [SerializeField] Transform reference;
        [SerializeField] Transform targetVisual;
        [SerializeField] Renderer checkRenderer;
        [SerializeField] Renderer targetRenderer;
        MaterialPropertyBlock block;

        public PositionCheckDefinition Definition => definition;

        public void Setup(PositionCheckDefinition def, Transform check, Transform referenceRoot, Transform target, Renderer checkR, Renderer targetR)
        {
            definition = def; checkPoint = check; reference = referenceRoot; targetVisual = target; checkRenderer = checkR; targetRenderer = targetR;
        }

        public Vector3 TargetWorld => reference != null ? reference.TransformPoint(definition.targetLocal) : definition.targetLocal;

        float Tolerance(ISimState s) => s != null && s.Has(definition.ToleranceKey) ? s.GetFloat(definition.ToleranceKey) : definition.toleranceCm;

        void OnEnable() => Sensors.Register(this);
        void OnDisable() => Sensors.Unregister(this);

        /// <summary>IStateSensor：例 part.pan_head.lower。</summary>
        public string KeyPrefix => definition != null ? $"part.{definition.part}.{definition.purpose}" : null;

        /// <summary>立刻量一次並寫入最新距離（不受每幀「變化 ≥ 0.05 cm 才寫」的門檻限制）；由動作在同一批寫入裡呼叫（放開升降、滑板吸附）。</summary>
        public void WriteNow(ISimState s, bool force)
        {
            if (s == null || definition == null || checkPoint == null) return;
            var target = TargetWorld;
            s.SetFloat(definition.DistanceKey, PositionCheckLogic.DistanceCm(checkPoint.position, target), force);
            s.SetFloat(definition.DeltaKey, PositionCheckLogic.VerticalDeltaCm(checkPoint.position, target), force);
        }

        void LateUpdate()
        {
            if (definition == null || checkPoint == null) return;
            var target = TargetWorld;
            if (targetVisual != null) targetVisual.position = target;
            float d = PositionCheckLogic.DistanceCm(checkPoint.position, target);
            float delta = PositionCheckLogic.VerticalDeltaCm(checkPoint.position, target);

            var s = Sim.State;
            if (s != null)
            {
                // 和黑板上的值比（不是和上次寫的比）：狀態被還原（從這一步重做）後也會立刻寫回實際距離
                Write(s, definition.DistanceKey, d);
                Write(s, definition.DeltaKey, delta);
            }
            bool ok = PositionCheckLogic.InPosition(d, Tolerance(s));
            bool show = s == null || PositionCheckLogic.MarkerVisible(s.Get("training.mode"), s.Get("hint.sub_target"), definition.showWhenSubTarget);
            if (checkRenderer != null && checkRenderer.enabled != show) checkRenderer.enabled = show;
            if (targetVisual != null && targetVisual.gameObject.activeSelf != show) targetVisual.gameObject.SetActive(show);
            Tint(checkRenderer, ok ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.2f, 0.15f), ok ? 0.8f : 0.6f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
            Tint(targetRenderer, new Color(0.2f, 1f, 0.35f, ok ? 0.65f : 0.3f + 0.2f * pulse), ok ? 0.9f : 0.4f);
        }

        /// <summary>
        /// 黑板存原始距離（SetFloat 保留到 0.001 cm），判定（容許值比較）一律用原始值；四捨五入到 0.1 cm 只用在顯示。
        /// 每幀寫入門檻不變：和黑板上的值差 ≥ 0.05 cm 才寫（放開升降、吸附時由 WriteNow 強制寫入當下值）。
        /// </summary>
        static void Write(ISimState s, string key, float value)
        {
            if (!s.Has(key) || Mathf.Abs(s.GetFloat(key) - value) >= 0.05f) s.SetFloat(key, value);
        }

        void Tint(Renderer r, Color c, float emission)
        {
            if (r == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_Color", c);
            block.SetColor("_EmissionColor", new Color(c.r, c.g, c.b) * emission);
            r.SetPropertyBlock(block);
        }

        void OnDrawGizmos()
        {
            if (definition == null || checkPoint == null) return;
            var target = TargetWorld;
            float tol = Application.isPlaying ? Tolerance(Sim.State) : definition.toleranceCm;
            Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(target, new Vector3(0.12f, 0.02f, 0.12f));
            Gizmos.DrawWireSphere(target, tol / 100f);
            bool ok = PositionCheckLogic.InPosition(PositionCheckLogic.DistanceCm(checkPoint.position, target), tol);
            Gizmos.color = ok ? Color.green : Color.red;
            Gizmos.DrawSphere(checkPoint.position, 0.012f);
            Gizmos.DrawLine(checkPoint.position, target);
        }
    }
}
