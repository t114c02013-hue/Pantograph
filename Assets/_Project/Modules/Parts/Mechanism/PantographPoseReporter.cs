using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Parts.Mechanism
{
    /// <summary>
    /// 包裝既有的 PantographMechanism：
    /// ・依黑板 lift.value（0＝降到底、1＝升到頂，由升降控制器寫入）驅動機構，取代原本一鍵升降；
    /// ・把姿態寫到黑板 part.pan_head.pose（Raised / Lowered / Moving）供控制盤指示燈使用。
    /// 是否「到降弓位置」由 PositionCheck 依檢測點判定，不在這裡判斷。
    /// </summary>
    [RequireComponent(typeof(PantographMechanism))]
    public class PantographPoseReporter : MonoBehaviour
    {
        PantographMechanism mech;
        float lastLift = -1f;

        void Awake() => mech = GetComponent<PantographMechanism>();

        void Update()
        {
            var s = Sim.State;
            if (s == null) return;
            if (s.Has(SimIds.LiftValue))
            {
                float lift = s.GetFloat(SimIds.LiftValue);
                if (Mathf.Abs(lift - lastLift) > 1e-5f) { mech.SetLift(lift); lastLift = lift; }
            }
            string pose = s.GetBool("lift.moving") ? "Moving" : mech.Lift <= 0.001f ? SimIds.PoseLowered : mech.Lift >= 0.999f ? SimIds.PoseRaised : "Partial";
            s.Set(SimIds.PanHeadPose, pose);
        }
    }
}
