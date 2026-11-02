namespace PantographSim.Core
{
    /// <summary>共用的 ID 與黑板鍵常數（各模組透過字串溝通，集中在這裡避免拼錯）。</summary>
    public static class SimIds
    {
        public const string PanHead = "part.pan_head";
        public const string PanHeadPose = "part.pan_head.pose";
        public const string PoseLowered = "Lowered";
        public const string PoseRaised = "Raised";
        public const string GroundClamp = "ip.pan_head.ground_clamp";
        /// <summary>降弓位置檢測（步驟計畫 v4 命名）：距離（cm）與是否到位。</summary>
        public const string PanHeadLowerDistance = "part.pan_head.lower_distance_cm";
        public const string PanHeadLowerDelta = "part.pan_head.lower_delta_cm";
        /// <summary>降弓容許誤差（cm），由 App 從 SR_Global_NotLowered 資產寫入；介面顯示用。</summary>
        public const string PanHeadLowerTolerance = "part.pan_head.lower_tolerance_cm";

        /// <summary>檢測點是否在降弓位置（距離 ≤ 容許值）。</summary>
        public static bool PanHeadLowered(ISimState s) =>
            s.Has(PanHeadLowerDistance) && s.GetFloat(PanHeadLowerDistance) <= s.GetFloat(PanHeadLowerTolerance, 1f) + 1e-4f;
        /// <summary>升降指令（Up / Down / Stop）：介面按鈕與鍵盤寫入，機構端 LiftDriver 讀取。</summary>
        public const string LiftCmd = "ctrl.lift.cmd";
        public const string LiftValue = "lift.value";
        public const string GroundTerminal = "ip.depot.ground_terminal";
        public const string VoltageTester = "ip.depot.detector_tester";
        public const string WorkOrder = "env.work_order";
        public const string PartsTray = "env.parts_tray";
        /// <summary>R1：車頂的螺栓盤（磁性盤），每側一個；拆下的螺栓放這裡，零件盒只收舊滑板。</summary>
        public static string BoltTray(string side) => "env.bolt_tray_" + side;
        public const string RoofToolbox = "env.roof_toolbox";
        public const string SpareShelf = "env.spare_shelf";
        public const string ActiveTool = "tool.active";

        public const string VoltageTested = "isolation.voltage_tested";
        public const string TerminalClamped = "isolation.terminal_clamped";
        public const string PullTested = "isolation.pull_tested";
        public const string HeadClamped = "isolation.head_clamped";
        public const string Grounded = "isolation.grounded";

        public const string ToolGroundingRod = "tool.grounding_rod";
        public const string ToolVoltageDetector = "tool.voltage_detector";
        public const string ToolCaliper = "tool.caliper";
        public const string ToolSocket = "tool.socket_wrench";
        public const string ToolTorque = "tool.torque_wrench";
        public const string ToolCloth = "tool.cleaning_cloth";
        public const string ToolMarker = "tool.marker";
        public const string ToolLockout = "tool.lockout_tag";
        public const string ToolHarness = "tool.safety_harness";

        /// <summary>弓體：碳滑板、其螺栓／托架／量測點、弓頭本體（不含弓頭接地掛點）。</summary>
        public static bool IsPanBody(string id) =>
            !string.IsNullOrEmpty(id) && (id.StartsWith("part.carbon_strip_") || id.StartsWith("ip.carbon_strip_") || id == PanHead);

        public static string Strip(string side) => "part.carbon_strip_" + side;
        public static string Point(string side, string point) => "ip.carbon_strip_" + side + "." + point;

        /// <summary>
        /// 距離的顯示：往上進位到 0.1 cm（1.04 → 1.1），不會和判定矛盾——超過容許值的距離不會顯示成「1.0」或「還差 0.0」。
        /// 判定一律用黑板上的原始值，這裡只管顯示。先四捨五入到小數 3 位去掉浮點誤差（1.0000001 仍顯示 1.0）。
        /// </summary>
        public static float CeilTenthCm(float cm) => (float)System.Math.Ceiling(System.Math.Round(cm * 10.0, 3)) / 10f;
        public static string CmText(float cm) => CeilTenthCm(cm).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        public static bool IsDistanceKey(string key) => key != null && key.EndsWith("_distance_cm");

        /// <summary>從 ip.carbon_strip_L.bolt_3 這類 ID 取出側別 L/R；不是滑板相關回傳空字串。</summary>
        public static string SideOf(string id)
        {
            const string a = "ip.carbon_strip_", b = "part.carbon_strip_";
            if (id == null) return "";
            if (id.StartsWith(a) && id.Length > a.Length) return id.Substring(a.Length, 1);
            if (id.StartsWith(b) && id.Length > b.Length) return id.Substring(b.Length, 1);
            return "";
        }
    }
}
