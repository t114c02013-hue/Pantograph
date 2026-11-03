using System;
using System.Linq;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4 第 4 部分：一門課（定期檢修／日常更換）。兩門課的差異只放兩處：SubActions.csv（小動作、站點、*_daily 欄）和這裡的 overrides
    /// （物件擺放、onPass 跳轉；架空線、升弓上限、天車停駐在 4c 加）。由匯入器從 Data/Source/Courses.json 產生。
    /// </summary>
    public class CourseDef : ScriptableObject
    {
        [Serializable]
        public class Placement
        {
            [Tooltip("場景 ISimObject 的 ID，或「name:物件名稱」")] public string id;
            [Tooltip("場景錨點名稱；空白＝不移動")] public string anchor;
            public bool active = true;
        }

        public string courseId, title;
        public ProcedureDefinition procedure;
        public Placement[] placements = Array.Empty<Placement>();
        public JumpRule[] onPass = Array.Empty<JumpRule>();
    }
}
