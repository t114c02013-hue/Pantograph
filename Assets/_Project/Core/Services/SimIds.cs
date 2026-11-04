using System.Linq;

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
        /// <summary>R1.5b b1 天車（crane-software v0.5 §2.1、§2.2）：遙控器只寫 ctrl.crane.*，CraneMechanism 讀命令、寫 part.crane.*。</summary>
        public const string CraneCmd = "ctrl.crane.cmd", CraneFast = "ctrl.crane.fast", CraneEStop = "ctrl.crane.estop", CranePower = "ctrl.crane.power";
        public const string CraneHookPos = "part.crane.hook.pos", CraneLoadBottomY = "part.crane.load_bottom_y", CraneState = "part.crane.state";
        /// <summary>遙控器在掛座（Docked）或隨身（Held）。</summary>
        public const string CranePendantState = "env.crane_pendant.state";
        /// <summary>D30 個人腰包（和 ToolSlotTable.WaistPouch、RackLogic.InPouch 相同；UI 只能引用 Core）。</summary>
        public const string WaistPouch = "env.waist_pouch", RackInPouch = "in_pouch";
        /// <summary>審查建議：工具架／工具袋有哪些掛位（ToolBelt 開場從 ToolSlotTable 寫「1,2,…,13」；UI、VR 只能引用 Core，從這裡讀，不寫死 1–9）。</summary>
        public const string RackSlots = "rack.slots";
        /// <summary>工具放在哪裡：「toolId|工具架;…」（ToolBelt 開場寫；UI 只能引用 Core，從這裡讀）。</summary>
        public const string ToolPlaces = "tools.places";
        /// <summary>terry 試玩：拿驗電桿、接地桿時，射線指著弓頭、滑板、螺栓都算指著弓頭接地掛點（掛點太小，被它們擋住）。桌面和 VR 共用。</summary>
        public static string RayTarget(string tool, string hovered) =>
            (tool == ToolGroundingRod || tool == ToolVoltageDetector) && hovered != null && (hovered == "part.pan_head" || hovered.StartsWith("part.carbon_strip_") || hovered.StartsWith("ip.carbon_strip_") || hovered == LowerCheckId) ? GroundClamp : hovered;

        /// <summary>打到 hit 時實際算哪個目標：卡尺打到滑板或弓頭算量測點（教學＝這一步要量的，其他＝離 hit 最近的，量測點只有 1.6 cm）；徒手點檢測點算看弓頭。</summary>
        public static string RayTarget(ISimState s, string tool, string hovered, UnityEngine.Vector3 hit)
        {
            if (string.IsNullOrEmpty(tool) && hovered == LowerCheckId) return PanHead;
            if (tool != ToolCaliper || hovered == null || hovered.Contains(".measure_") || !(hovered == PanHead || hovered == LowerCheckId || hovered.StartsWith("part.carbon_strip_") || hovered.StartsWith("ip.carbon_strip_"))) return RayTarget(tool, hovered);
            string want = s.Get("hint.sub_target");
            if (s.Get("training.mode") == "tutorial" && want.Contains(".measure_")) return want;
            string best = hovered; float bestD = float.MaxValue;
            foreach (var id in Sim.Registry.Ids)
                if (id.Contains(".measure_") && Sim.Registry.TryGetPosition(id, out var p) && (p - hit).sqrMagnitude < bestD) { bestD = (p - hit).sqrMagnitude; best = id; }
            return best;
        }

        /// <summary>是不是一支螺栓（…bolt_ 後面接數字）；env.bolt_tray_L 是盤子，不算。</summary>
        public static bool IsBoltId(string id)
        {
            int i = id?.LastIndexOf(".bolt_") ?? -1;
            return i >= 0 && int.TryParse(id.Substring(i + 6), out _);
        }

        /// <summary>這一側已經脫出、還沒收進盤子的螺栓（沒有回傳空字串）。</summary>
        public static string StripBoltOut(ISimState s, string side)
        {
            for (int n = 1; n <= 4; n++)
                if (s.Get($"ip.carbon_strip_{side}.bolt_{n}.state") == "Out") return $"ip.carbon_strip_{side}.bolt_{n}";
            return "";
        }

        /// <summary>弓頭上的檢測點。</summary>
        public const string LowerCheckId = "ip.pan_head.lower_check";

        /// <summary>日常課和 R1 沒有吊車步驟：吊車不能點。</summary>
        public static bool Clickable(ISimState s, string id) =>
            id != "env.crane" || !(s.GetBool(CourseCraneParked) || string.IsNullOrEmpty(s.Get(CourseId)) || s.Get(CourseId) == CourseR1);

        /// <summary>第 6 點：送給手上工具的「第二功能」（ToolBelt 換成 ToolDefinition.secondaryAction）。</summary>
        public const string ToolSecondaryCommand = "secondary";

        public static int[] RackSlotNumbers(ISimState s) =>
            (s?.Get(RackSlots) ?? "").Split(',').Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).ToArray();
        /// <summary>R1.5b b4 第 4 部分（課程覆寫）：目前的課程、日常更換時天車停駐（D23）、升弓上限（lift 值）、弓頭到了接觸線（D41）。</summary>
        /// <summary>R1 原 25 步的課程 ID（沒有 courseId 的舊錄影也當這個）。</summary>
        public const string CourseR1 = "r1";
        /// <summary>VR 開場站點（CourseDef.startStationId；R1 沒有這個鍵，用 StationSet.startStationId）。</summary>
        public const string CourseStartStation = "course.start_station";
        public const string CourseId = "course.id", CourseCraneParked = "course.crane_parked", LiftMax = "lift.max", PanAtContactWire = "part.pan_head.at_contact_wire";
        public const string GroundTerminal = "ip.depot.ground_terminal";
        public const string VoltageTester = "ip.depot.detector_tester";
        public const string WorkOrder = "env.work_order";
        public const string PartsTray = "env.parts_tray";
        /// <summary>R1：車頂的螺栓盤（磁性盤），每側一個；拆下的螺栓放這裡，零件盒只收舊滑板。</summary>
        public static string BoltTray(string side) => "env.bolt_tray_" + side;
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
