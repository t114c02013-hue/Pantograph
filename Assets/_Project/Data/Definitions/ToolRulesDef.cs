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
        [Tooltip("放開後多久之內再抓回不算（秒）")] public float regrabGraceSec = 1.5f;
        [Tooltip("只提示、不記錯的模式（training.mode）")] public string[] hintOnlyModes = { "tutorial" };
        public string keepHint = "把手伸到工具架／工具袋";
        public string hangingHint = "工具掛在防墜繫繩上垂著：用完要收回工具袋";
        [Tooltip("R1.5b b4 RT_02 絕緣測試（示例值）：讀值（MΩ）、接地還掛著時的讀值、放電要接觸多久（秒）")] public float meggerReadingMOhm = 500f, meggerGroundedReadingMOhm = 0f, dischargeSeconds = 3f;

        public static ToolRulesDef Load() => Resources.Load<ToolRulesDef>(ResourcePath);
    }
}
