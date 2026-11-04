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

        /// <summary>R1.5b b4（D43）：可選故障（教官在任務卡開關；預設值在資料）。</summary>
        [Serializable]
        public class FaultOption
        {
            [Tooltip("黑板鍵，例 fault.insulator_dirty")] public string key;
            public string title;
            public bool defaultOn;
            [Tooltip("v3.10：故障處理時間從這個小動作啟用起、到這個小動作完成止（不算進時間判定，報表另列；序號展開的 s1i2 算 s1）")] public string timeFrom, timeTo;
        }

        public string courseId, title;
        [Tooltip("D43：可選故障（只列在有的課程，例 定期的絕緣子髒汙）")] public FaultOption[] faults = Array.Empty<FaultOption>();
        public ProcedureDefinition procedure;
        public Placement[] placements = Array.Empty<Placement>();
        public JumpRule[] onPass = Array.Empty<JumpRule>();
        [Tooltip("D23、D41：升弓上限＝接觸線，從 O 點往上量（m）；0＝不覆寫（定期檢修）")] public float liftCapAboveO;
        [Tooltip("D41：滑板頂面停在接觸線下方多少（m，避免閃爍的視覺間距）")] public float contactGapM;
        [Tooltip("R1.5b b4：開場狀態（鍵=值），例 cab.breaker=on（主斷路器、電動缸電源開著，學員自己切 OFF）")] public string[] initialStates = Array.Empty<string>();
        [Tooltip("D23：天車停駐（停到 Crane_ParkPose、電源固定關、命令無效、遙控器拿不起來、tp.platform.crane 不開放）")] public bool craneParked;
    }
}
