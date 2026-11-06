using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4（負責人裁定）：放開握把時工具怎麼處理，放在 Resources/ToolRules，改資料不改程式。
    /// ・gripReleaseDrops＝true：放開就掉（車頂＝垂在防墜繫繩上）。regrabGraceSec 內再抓回不算；超過時教學模式只提示，
    ///   練習與考核記 Major ERR_Tool_NotStowedOnRoof；同一步同一件工具只記一次，撿回來也不消掉。
    /// ・false：舊做法，工具留在手上，提示 keepHint，不記錯。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/Tool Rules")]
    public class ToolRulesDef : ScriptableObject
    {
        public const string ResourcePath = "ToolRules";
        public bool gripReleaseDrops = true;
        [Tooltip("滑板放上托架：孔位差這個距離（cm）內就吸附")] public float stripSnapCm = 5f;
        [Tooltip("跳過步驟的說明，每則停留秒數")] public float noticeSeconds = 6f;
        [Tooltip("VR 射線指著掛架按握把：離擊中點這個距離（m）內最近的掛位算拿到")] public float rackGrabRadius = 0.1f;
        [Tooltip("編輯器測試驅動走過的那一段，在紀錄和報表上的標示")] public string testDriverTag = "測試驅動";
        [Tooltip("遊戲內錄影的鍵：不要用 F9，驗收用它控制 Unity Recorder")] public KeyCode videoHotkey = KeyCode.F8;
        [Tooltip("VR 第一次拿起工具時的提示")] public string toolDetailHint = "按左手扳機看用法";
        [Tooltip("VR 按住握把這麼久（秒）再放開才算放手；輕按一下是誤觸，工具留在手上")] public float releaseHoldSeconds = 0.6f;
        [Tooltip("放開後多久之內再抓回不算（秒）")] public float regrabGraceSec = 1.5f;
        [Tooltip("只提示、不記錯的模式（training.mode）")] public string[] hintOnlyModes = { "tutorial" };
        public string keepHint = "把手伸到工具架／工具袋";
        public string hangingHint = "工具掛在防墜繫繩上垂著：用完要收回工具袋";
        [Tooltip("R1.5b b4 RT_02 絕緣測試（示例值）：讀值（MΩ）、接地還掛著時的讀值、放電要接觸多久（秒，D43：5）")] public float meggerReadingMOhm = 500f, meggerGroundedReadingMOhm = 0f, dischargeSeconds = 5f;
        [Tooltip("D43：合格下限（MΩ，≥ 合格）、可選故障「絕緣子髒汙」開啟且還沒擦淨時的讀值、絕緣子擦淨的清除率")] public float irPassMinMOhm = 100f, meggerFaultReadingMOhm = 20f, insulatorCleanRatio = 0.95f;
        [Tooltip("D43：報表註明")] public string irPassNote = "100 MΩ 下限待高鐵 SOP 確認";
        [Tooltip("R1.5b b4：查看＝指著停多久（秒）；哪些東西用這種查看（吊鉤、吊梁、吊帶、吊具、集電弓、接地掛點）")] public float inspectDwellSeconds = 1.0f;
        public string[] dwellInspectIds = { "part.crane.hook", "env.lift_beam", "env.sling_1", "env.sling_2", "env.sling_3", "env.sling_4", "env.sling_spare", "env.rigging", "part.pan_assy", "ip.pan_head.ground_clamp" };
        [Tooltip("D43：讀值 0（接地還掛著）時三種模式都只提示、不記分")] public string groundedHint = "接地還掛著，請確認 #1、#2 都已拆除並銷號後再量";

        public static ToolRulesDef Load() => Resources.Load<ToolRulesDef>(ResourcePath);
    }
}
