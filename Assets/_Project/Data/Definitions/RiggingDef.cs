using System;
using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>一條「要成立才吊得起來」的條件：ChecklistEval 條件＋不成立時的說明。</summary>
    [Serializable]
    public class RigCondition
    {
        public string condition, label;
        public RigCondition() { }
        public RigCondition(string c, string l) { condition = c; label = l; }
    }

    /// <summary>
    /// R1.5b b2 吊運資料（crane-software v0.5 §2.3、§3.1）：起吊條件（從車頂、從台架各一組）、吸附容差、對位燈顯示範圍、下降對位時天車碰撞要排除的 collider（CraneCollisionIgnoreDef）。
    /// 數值都是 EX；條件格式同 ChecklistEval，{n} 依 1..4 展開。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/Rigging Def")]
    public class RiggingDef : ScriptableObject
    {
        [Tooltip("從車頂起吊（state＝Rigged）的條件，§3.1 表")]
        public RigCondition[] liftConditions =
        {
            new RigCondition("ip.pan_assy.hold_down.state=Installed", "降弓固定帶還沒裝上（RM_01）"),
            new RigCondition("ip.pan_assy.ctrl_connector.state=Removed", "控制線還沒拆下（RM_02）"),
            new RigCondition("ip.pan_assy.hv_terminal.state=Removed", "高壓電纜端子還沒拆下（RM_03）"),
            new RigCondition("part.pan_base.bolt_{n}.state=Out", "底座螺栓 {n} 還沒取出（RM_04）"),
            new RigCondition("ip.pan_assy.lift_point_{n}.state=Rigged", "吊點 {n} 還沒掛上吊帶（RM_06）"),
            new RigCondition("part.crane.hook.latched", "吊鉤舌片還沒扣上（LF_01）"),
        };
        [Tooltip("從台架吊回（state＝OnStand）的條件")]
        public RigCondition[] standLiftConditions =
        {
            new RigCondition("ip.pan_assy.lift_point_{n}.state=Rigged", "吊點 {n} 還沒掛上吊帶（IN_01）"),
            new RigCondition("env.panto_stand.clamp_1.state=Open", "台架夾具 1 還沒解開（IN_01）"),
            new RigCondition("env.panto_stand.clamp_2.state=Open", "台架夾具 2 還沒解開（IN_01）"),
            new RigCondition("part.crane.hook.latched", "吊鉤舌片還沒扣上"),
        };
        [Tooltip("吸附：每支定位銷（或絕緣子頂）的水平距離 ≤ 這個值（m，第 2 部分 2.3：2 cm；鐵道確認定位銷有導角）")] public float snapRadius = 0.02f;
        [Tooltip("對位燈：吊物底面在落點上方這個高度（m）以內才亮")] public float indicatorRange = 0.3f;
        [Tooltip("CraneCollisionIgnoreDef：吊物下降、且底面離落點 ≤ ignoreDistance 時，天車碰撞停止不算這些名稱開頭的 collider（定位銷、絕緣子本來就要接觸底架）")]
        public string[] collisionIgnorePrefixes = { "StandSeat", "InsulatorTop_", "InsulatorCollision" };   // StandSeat＝定位銷 StandSeat_n 和支撐座 StandSeatBlock
        public float ignoreDistance = 0.10f;
        [Tooltip("吊物底面離落點這麼近（m）就算放下（吸附或擋住下降）")] public float seatTolerance = 0.005f;
        [Tooltip("R1.5b b4（RM_06 b）：「查看四條吊帶」的判定框大小與離吊梁的距離（m；只框吊帶中段，不擋下方吊耳）")] public Vector3 inspectBoxSize = new Vector3(1.2f, 0.4f, 1.2f);
        public float inspectBoxOffset = 0.05f;
        [Tooltip("R1.5b b4（ERR_Crane_NoTrialLift）：試吊＝離座這個範圍內（m）停住這麼久（s），示例值")] public float trialDropMin = 0.08f, trialDropMax = 0.15f, trialStillSeconds = 3f;
        [Tooltip("R1.5b b4（ERR_Crane_FastLowerEmptyHook）：吊鉤在台架中心這個水平距離（m）內，作業面算台架頂面")] public float standSurfaceRadius = 1.5f;
        [Tooltip("R1.5b b4（RM_05 f）：有割傷、要判「不合格」的吊帶編號（空＝四條都可用）")] public int[] defectiveSlings = new int[0];
        [Tooltip("v3.10（D44、model-spec v3.17 §7.10）：空吊梁查看位置 Crane_InspectPose_Platform／_Ground 的中心（吊鉤 XZ）")] public Vector2 inspectPosePlatformXZ = new Vector2(-2.56f, -3.55f), inspectPoseGroundXZ = new Vector2(-9.50f, -3.20f);
        [Tooltip("v3.10：查看位置吊梁底面離平台面／地面 EX 0.90 ± 0.10 m；中心水平容差（m，示例值：步驟表沒寫）")] public float inspectPoseBeamAboveM = 0.90f, inspectPoseHeightTol = 0.10f, inspectPoseXZTol = 0.15f;
        [Tooltip("v3.10：建置時用頂點量的吊鉤組水平半徑（m，以吊鉤節點為中心）；StationLayoutCheck 讀它檢查查看站離吊鉤組外緣 ≥ CraneProfile.inspectClearanceM")] public float hookBlockRadiusM = 0.17f;

        /// <summary>把 {n} 展開成 1..4。</summary>
        public static IEnumerable<RigCondition> Expand(IEnumerable<RigCondition> list)
        {
            foreach (var c in list)
            {
                if (c.condition.Contains("{n}")) for (int n = 1; n <= 4; n++) yield return new RigCondition(c.condition.Replace("{n}", n.ToString()), c.label.Replace("{n}", n.ToString()));
                else yield return c;
            }
        }

        /// <summary>還缺哪些條件（空＝全部成立）。</summary>
        public static List<string> Missing(ISimState s, IEnumerable<RigCondition> list) =>
            Expand(list).Where(c => !ChecklistEval.Done(s, c.condition)).Select(c => c.label).ToList();
    }

    /// <summary>吊運的純計算（RiggingTests 直接呼叫；對位燈和吸附讀同一份距離）。</summary>
    public static class RiggingLogic
    {
        /// <summary>每個孔對應落點的水平距離（孔 n 對落點 n）。</summary>
        public static float[] PinDistances(IList<Vector3> holes, IList<Vector3> pins)
        {
            var d = new float[Mathf.Min(holes.Count, pins.Count)];
            for (int i = 0; i < d.Length; i++) d[i] = new Vector2(holes[i].x - pins[i].x, holes[i].z - pins[i].z).magnitude;
            return d;
        }

        public static bool AllWithin(float[] d, float tol) => d.Length > 0 && d.All(x => x <= tol + 1e-5f);

        /// <summary>吸附後要平移多少（水平：孔平均到落點平均的差，讓所有孔對齊到 0）。</summary>
        public static Vector3 SnapOffset(IList<Vector3> holes, IList<Vector3> pins)
        {
            Vector3 a = Vector3.zero, b = Vector3.zero;
            for (int i = 0; i < holes.Count; i++) { a += holes[i]; b += pins[i]; }
            var off = (b - a) / Mathf.Max(1, holes.Count);
            off.y = 0f;
            return off;
        }
    }
}
