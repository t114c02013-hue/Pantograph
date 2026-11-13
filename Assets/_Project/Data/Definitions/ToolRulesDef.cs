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
        [Tooltip("測試驅動（編輯器工具、後台面板代做）走過的那一段，在紀錄和報表上的標示")] public string testDriverTag = "測試驅動";
        [Tooltip("後台面板代做時遮罩上的字（{0}＝步驟序號）")] public string autoRunMaskText = "正在前往第 {0} 步…";
        [Tooltip("代做做完後遮罩淡回的秒數")] public float autoRunFadeSeconds = 0.6f;
        [Tooltip("後台面板進度條：代做中（{0}＝已完成幾步、{1}＝要做幾步、{2}＝目前這一步）")] public string autoRunProgressText = "代做中 {0}／{1}：{2}";
        [Tooltip("後台面板進度條：做完（{0}＝目標步驟）")] public string autoRunDoneText = "✔ 已到{0}，可以開始";
        [Tooltip("後台面板進度條：同一步停超過這麼久（秒）就顯示卡住和原因")] public float autoRunStuckSeconds = 20f;
        [Tooltip("後台面板進度條：卡住（{0}＝目前這一步、{1}＝秒數、{2}＝原因）")] public string autoRunStuckText = "⚠ 停在{0}已 {1:0} 秒：{2}";
        [Tooltip("後台面板進度條：卡住但流程沒有寫原因時")] public string autoRunStuckUnknown = "原因不明，看 Console 的 [代做] 紀錄";
        [Tooltip("後台面板：按了停止代做（{0}＝停在哪一步）")] public string autoRunStoppedText = "■ 已停止代做，停在{0}";
        [Tooltip("terry 2026-11-13 選 A：碳滑板新品厚度（mm，淡海輕軌約 18）與允許誤差")] public float stripNewMm = 18f, stripNewTolMm = 0.2f;
        [Tooltip("terry 2026-11-13 選 A：碳滑板換新門檻（mm，三點最小值 ≤ 這個值就換；淡海輕軌約 6）")] public float stripLimitMm = 6f;
        [Tooltip("後台代做時升降速度的倍數（學員自己操作不變；放開那一刻的位置判定不變）")] public float autoRunLiftSpeed = 5f;
        [Tooltip("後台代做時扭力扳手上升速度的倍數（學員自己操作不變）")] public float autoRunTorqueSpeed = 5f;

        /// <summary>代做中用 multiplier 加快，其他時候 1（倍數小於 1 也當 1，不會變慢）。</summary>
        [Tooltip("terry 2026-11-13（21A）：哪些課程有「螺栓倒進平台螺栓盤清點／拿回螺栓袋」（只提示、不判定）")] public string[] boltPourCourses = { "r1", "daily" };
        [Tooltip("倒進盤子、拿回袋子的提示（{0}＝支數）")] public string boltPourText = "倒進螺栓盤清點：{0:0} 支", boltTakeBackText = "拿回螺栓袋：{0:0} 支";
        public bool BoltPourIn(string courseId) => System.Array.IndexOf(boltPourCourses, courseId) >= 0;

        public static float AutoRunScale(bool autoRun, float multiplier) => autoRun ? Mathf.Max(1f, multiplier) : 1f;
        [Tooltip("遊戲內錄影的鍵：不要用 F9，驗收用它控制 Unity Recorder")] public KeyCode videoHotkey = KeyCode.F8;
        [Tooltip("VR 第一次拿起工具時的提示")] public string toolDetailHint = "按左手扳機看用法";
        [Tooltip("VR 按住握把這麼久（秒）再放開才算放手；輕按一下是誤觸，工具留在手上")] public float releaseHoldSeconds = 0.6f;
        [Tooltip("放開後多久之內再抓回不算（秒）")] public float regrabGraceSec = 1.5f;
        [Tooltip("只提示、不記錯的模式（training.mode）")] public string[] hintOnlyModes = { "tutorial" };
        public string keepHint = "把手伸到工具架／工具袋";
        public string hangingHint = "工具掛在防墜繫繩上垂著：用完要收回工具袋";
        [Tooltip("R1.5b b4 RT_02 絕緣測試（示例值）：讀值（MΩ）、接地還掛著時的讀值、放電要接觸多久（秒，D43：5）")] public float meggerReadingMOhm = 500f, meggerGroundedReadingMOhm = 0f, dischargeSeconds = 5f;
        [Tooltip("D43：合格下限（MΩ，≥ 合格）、可選故障「絕緣子髒汙」開啟且還沒擦淨時的讀值、絕緣子擦淨的清除率")] public float irPassMinMOhm = 100f, meggerFaultReadingMOhm = 20f, insulatorCleanRatio = 0.95f;
        [Tooltip("D43：報表註明")] public string irPassNote = "100 MΩ 下限待營運單位 SOP 確認";
        [Tooltip("R1.5b b4：查看＝指著停多久（秒）；哪些東西用這種查看（吊鉤、吊梁、吊帶、吊具、集電弓、接地掛點）")] public float inspectDwellSeconds = 1.0f;
        public string[] dwellInspectIds = { "part.crane.hook", "env.lift_beam", "env.sling_1", "env.sling_2", "env.sling_3", "env.sling_4", "env.sling_spare", "env.rigging", "part.pan_assy", "ip.pan_head.ground_clamp" };
        [Tooltip("D43：讀值 0（接地還掛著）時三種模式都只提示、不記分")] public string groundedHint = "接地還掛著，請確認 #1、#2 都已拆除並銷號後再量";

        public static ToolRulesDef Load() => Resources.Load<ToolRulesDef>(ResourcePath);
    }
}
