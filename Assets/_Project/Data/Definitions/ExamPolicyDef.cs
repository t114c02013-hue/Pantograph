using System;
using System.Linq;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b K3（組長、驗收組長裁定）：考核政策，放在 Resources/ExamPolicy，數值和模式行為都由資料改，不改程式。
    /// ・k3Ready＝喀聲回饋已在頭盔試玩確認；false 時考核模式把有 severityBeforeK3 的錯誤（扭力不足）改記那個等級，報告註明。
    /// ・扭力不足（觸發 OutOfRangeOnLeave）：holdModes 的模式擋下、顯示 underHint，鎖到位才能往下做；recordModes 的模式放行並記錯；
    ///   deferModes 的模式進行中不跳提示，只進報表。模式名稱＝黑板 training.mode（tutorial／practice／exam）。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/Exam Policy")]
    public class ExamPolicyDef : ScriptableObject
    {
        public const string ResourcePath = "ExamPolicy";
        public const string K3Note = "（K3 完成後升為 Critical）";
        public bool k3Ready;
        public string[] holdModes = { "tutorial" };
        public string[] recordModes = { "practice", "exam" };
        public string[] deferModes = { "practice", "exam" };
        public string underHint = "扭力不足，請鎖到扳手響（或震動）為止";

        public static bool In(string[] modes, string mode) => (modes ?? Array.Empty<string>()).Contains(string.IsNullOrEmpty(mode) ? "tutorial" : mode);

        public static ExamPolicyDef Load() => Resources.Load<ExamPolicyDef>(ResourcePath);
    }
}
