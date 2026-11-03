using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Parts.Mechanism
{
    /// <summary>
    /// 升降驅動（像千斤頂）：讀黑板 ctrl.lift.cmd（Up / Down / Stop，由介面按鈕或鍵盤寫入），
    /// 依 LiftSettings 平滑加減速，寫 lift.value（0＝降到底、1＝升到頂）、lift.moving、lift.travel_cm；
    /// 上升被安全聯鎖擋住時（LiftControl.UpBlockReason）忽略 Up；暫停或視窗失焦時停止。開場若沒有電動缸電源狀態，寫入 on。
    /// 機構姿態由 PantographPoseReporter 依 lift.value 套用。
    /// </summary>
    public class LiftDriver : MonoBehaviour
    {
        public const string CmdKey = SimIds.LiftCmd;
        [SerializeField] LiftSettings settings;
        float value, velocity;
        bool initialized;

        public void Setup(LiftSettings s) => settings = s;

        /// <summary>指令字串 → 方向（純函式，方便測試）。</summary>
        public static int InputOf(string cmd, bool cylinderPowerOff)
        {
            if (cmd == "Down") return -1;
            if (cmd == "Up") return cylinderPowerOff ? 0 : 1;
            return 0;
        }

        /// <summary>
        /// R1.5b b4（D23、D41）：課程的升弓上限（lift.max，CourseApplier 依建置量測寫入）：到了就停，寫 part.pan_head.at_contact_wire＝已接觸。
        /// 升降判定本身不改，只改上限。沒有 lift.max（定期檢修、R1）時不影響。
        /// </summary>
        public static void ApplyCap(ISimState s, ref float value, ref float velocity)
        {
            if (s == null || !s.Has(SimIds.LiftMax)) return;
            float max = s.GetFloat(SimIds.LiftMax);
            if (value >= max) { value = max; if (velocity > 0f) velocity = 0f; }
            bool contact = value >= max - 1e-4f;
            if (s.GetBool(SimIds.PanAtContactWire) != contact) s.SetBool(SimIds.PanAtContactWire, contact);
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && Sim.State != null) LiftControl.Release(Sim.State);
        }

        void Update()
        {
            var s = Sim.State;
            if (s == null || !s.GetBool("training.running")) return;
            if (!initialized)
            {
                initialized = true;
                value = s.GetBool("scenario.start_raised") ? 1f : 0f;
                s.SetFloat("lift.travel_cm", settings != null ? settings.travelCm : LiftSettingsValues.Default.travelCm);
                if (!s.Has("cab.cylinder_power")) s.Set("cab.cylinder_power", "on");   // 初值：有電（沒寫入時不能當成關）
                LiftControl.Release(s);
            }
            int input = s.GetBool("training.paused") ? 0 : InputOf(s.Get(CmdKey), LiftControl.UpLocked(s));
            float distance = s.Has(SimIds.PanHeadLowerDistance) ? s.GetFloat(SimIds.PanHeadLowerDistance) : float.MaxValue;
            LiftLogic.Step(ref value, ref velocity, input, Time.deltaTime, LiftSettingsValues.From(settings), distance);
            ApplyCap(s, ref value, ref velocity);
            s.SetFloat(SimIds.LiftValue, Mathf.Round(value * 10000f) / 10000f);
            s.SetBool("lift.moving", Mathf.Abs(velocity) > 1e-5f);
        }
    }
}
