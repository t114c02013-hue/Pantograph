using System;
using System.Linq;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4（D23、D41）：各課程的升弓上限，由場景建置量測寫回：接觸線世界 Y＝O 點世界 Y＋CourseDef.liftCapAboveO，
    /// 滑板頂面到「接觸線 − contactGapM」時的升降值 liftMax。CourseApplier 載入課程時寫到黑板 lift.max。放在 Resources/LiftCaps。
    /// </summary>
    public class LiftCapsDef : ScriptableObject
    {
        public const string ResourcePath = "LiftCaps";

        [Serializable] public class Entry { public string courseId; public float contactWireY, stopTopY, liftMax; }

        public Entry[] entries = Array.Empty<Entry>();

        public Entry Find(string courseId) => entries.FirstOrDefault(e => e.courseId == courseId);

        public static LiftCapsDef Load() => Resources.Load<LiftCapsDef>(ResourcePath);
    }
}
