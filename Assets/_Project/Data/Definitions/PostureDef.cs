using UnityEngine;

namespace PantographSim.Data
{
    public enum Posture { Stand, Kneel, Crouch }

    /// <summary>VR 試玩修正 第 8 點：175 cm 身高基準（Resources/PostureDef）。只用在建置檢查、截圖和預設擺放，實際眼高由頭盔決定。</summary>
    public class PostureDef : ScriptableObject
    {
        public const string ResourceName = "PostureDef";

        public float standEye = 1.63f, kneelEye = 1.20f, crouchEye = 0.95f, standElbow = 1.09f, standShoulder = 1.43f;
        [Tooltip("terry 試玩：工具袋到站點才擺一次、之後不動：比站姿眼高低多少（m）")] public float toolBagBelowEye = 0.55f;
        [Tooltip("工具袋裡的工具模型：中心比格子高多少（m），上半截露出來看得到外型")] public float toolBagToolRise = 0.05f;
        [Tooltip("工具袋相對到站時的頭：x＝往右（負＝左）、y＝往前（m）")] public Vector2 toolBagOffset = new Vector2(-0.2f, 0.15f);

        [Header("StationLayoutCheck 第 23 項：離地高度（m）")]
        public Vector2 liftButtons = new Vector2(1.00f, 1.20f);
        public Vector2 liftLamps = new Vector2(1.40f, 1.55f);
        public Vector2 cabMonitor = new Vector2(1.45f, 1.55f);
        public Vector2 hooks = new Vector2(0.90f, 1.50f);
        public Vector2 frequentHooks = new Vector2(1.00f, 1.30f);
        [Tooltip("滑板工作面的舒適高度與容許差")] public float stripWorkHeight = 1.20f, stripWorkTolerance = 0.05f;
        [Tooltip("true＝第 23 項不合格就讓建置失敗。建模 A 改好高度之前是 false：只列在 Logs/HeightCheck23.txt")] public bool enforceHeights;

        public float Eye(Posture p) => p == Posture.Kneel ? kneelEye : p == Posture.Crouch ? crouchEye : standEye;

        public static PostureDef Load()
        {
            var d = Resources.Load<PostureDef>(ResourceName);
            return d != null ? d : CreateInstance<PostureDef>();
        }
    }
}
