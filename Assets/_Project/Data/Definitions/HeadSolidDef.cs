using System;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// terry 186（RA-21、RA-25、RA-26）：頭穿進實心物體時畫面變暗並提示「請退後」（Resources/HeadSolidDef）。
    /// solids：哪些物件要加 HeadSolid 碰撞框（名稱樣式「名稱」或「父/名稱」，* 是任意字元）；excludes：要排除的子節點樣式；
    /// excludeZones：頭在裡面時不判（駕駛室房間，建置時由 ENV_CabFloor 推導寫入）。
    /// </summary>
    public class HeadSolidDef : ScriptableObject
    {
        public const string ResourceName = "HeadSolidDef";
        public enum Kind { Box, Mesh, BoxPerPart }

        [Serializable] public class Solid
        {
            public string pattern; public Kind kind;
            [Tooltip("找不到也不算失敗（FBX 未交付的教學箱）")] public bool optional;
        }
        [Serializable] public class TopCheck { public string stationPrefix, objectName; }
        [Serializable] public class LeanCheck { public string stationId; public float headX; }
        [Serializable] public class BowSkip { public string stationId; public string reason; }

        [Tooltip("判定用的頭半徑（m）")] public float headSolidRadius = 0.10f;
        [Tooltip("進去後多久變到最暗、出來後多久恢復（s）")] public float fadeInSec = 0.25f, fadeOutSec = 0.20f;
        [Tooltip("最暗的黑（0～1）：還看得到周圍才知道往哪退")] public float maxAlpha = 0.85f;
        [Tooltip("傳送、淡出推回、掉落救回後多久內不判（s）")] public float graceSec = 0.5f;
        public string hintText = "請退後";

        public Solid[] solids =
        {
            new Solid { pattern = "LRV/Module_*", kind = Kind.Mesh },
            new Solid { pattern = "LRV/Articulation_*", kind = Kind.Mesh },
            new Solid { pattern = "LRV/Roof_*", kind = Kind.Box },
            new Solid { pattern = "LRV/Bogie_*", kind = Kind.Box },
            new Solid { pattern = "TrainingBox_*/Body", kind = Kind.Mesh },          // 4c：箱體、外觀細節用非凸網格（內部是空的，打開蓋子探頭看不變暗；穿過箱壁會變暗）
            new Solid { pattern = "TrainingBox_*/Detail_Body", kind = Kind.Mesh },
            new Solid { pattern = "TrainingBox_*", kind = Kind.BoxPerPart },   // gen_roof_boxes v1.1 已交付（含鋼架）；上面兩列用網格的部分不再包框
            new Solid { pattern = "ENV_PantographStand", kind = Kind.BoxPerPart },
            new Solid { pattern = "ENV_WorkBench", kind = Kind.BoxPerPart },
            new Solid { pattern = "SpareShelf", kind = Kind.BoxPerPart },
            new Solid { pattern = "ENV_SlingRack", kind = Kind.Box },
            new Solid { pattern = "ENV_RiggingRack", kind = Kind.Box },
            new Solid { pattern = "ToolRack_Platform", kind = Kind.Box },
            new Solid { pattern = "ENV_PoleRack", kind = Kind.Box },
            new Solid { pattern = "LiftControlBox", kind = Kind.Box },
            new Solid { pattern = "DetectorTester", kind = Kind.Box },
            new Solid { pattern = "ShorePowerBox", kind = Kind.BoxPerPart },   // gen_depot_props v1.0（立柱、箱體、雨蓋…各一框）
            new Solid { pattern = "IsolationStatusLight", kind = Kind.BoxPerPart },
            new Solid { pattern = "LotoBoard", kind = Kind.Box },
            new Solid { pattern = "PlatformShelf", kind = Kind.BoxPerPart },
            new Solid { pattern = "Visual/Column", kind = Kind.Box },
            new Solid { pattern = "Visual/Wall", kind = Kind.Box },
            new Solid { pattern = "Visual/EndWall", kind = Kind.Box },
            new Solid { pattern = "WallEquipment/SafetyBoard", kind = Kind.Box },
            new Solid { pattern = "WallEquipment/HelmetHarnessRack", kind = Kind.Box },
            new Solid { pattern = "WallEquipment/ToolCart_*", kind = Kind.Box },
            new Solid { pattern = "WallEquipment/CarbonStripRack", kind = Kind.Box },
            new Solid { pattern = "WallEquipment/WasteBin", kind = Kind.Box },
            new Solid { pattern = "WallEquipment/FireExtinguisher", kind = Kind.Box },
            new Solid { pattern = "WallEquipment/SocketBox", kind = Kind.Box },
            new Solid { pattern = "WallEquipment/AirHoseReel", kind = Kind.Box },
        };
        [Tooltip("solids 範圍內要排除的子節點樣式（燈、標牌、文字、夾板、可以拿的東西、內部）")] public string[] excludes = { "AlignLight_*", "Clipboard", "Inside_*", "*Label*", "Text", "LampLight", "HeadSolid_*", "NewStrip_*", "BoltTray_Square_*" };
        [Tooltip("頭在裡面時不判（建置時由駕駛室房間推導寫入）")] public Bounds[] excludeZones = new Bounds[0];

        [Tooltip("站點檢查：彎腰時頭往前、往下（m）")] public float bowForward = 0.3f, bowDown = 0.3f;
        [Tooltip("站點檢查：頭在檯面（箱頂）上方多少不觸發（m）")] public float aboveTopCheck = 0.2f;
        [Tooltip("站點檢查：這些站點再加「頭伸到物件頂面上方」")] public TopCheck[] topChecks =
        {
            new TopCheck { stationPrefix = "tp.stand.", objectName = "ENV_PantographStand" },
            new TopCheck { stationPrefix = "tp.platform.parts", objectName = "SpareShelf" },
            new TopCheck { stationPrefix = "tp.maint.box_", objectName = "TrainingBox_*" },
        };
        [Tooltip("站點檢查：前傾到某個頭 X（RA-22 拔外接電源插頭）")] public LeanCheck[] leanChecks = { new LeanCheck { stationId = "tp.cab.shore", headX = -1.9f } };
        [Tooltip("站點檢查：不做彎腰頭的站點（面前就是要操作的盒子，實際彎腰是低頭看面板，不會再往前壓 bowForward／bowDown）")] public BowSkip[] bowSkips =
            { new BowSkip { stationId = "tp.platform.lift", reason = "升降盒在面前 0.2 m、頂面比眼睛低約 0.4 m：彎腰頭會碰到盒頂邊（頭真的壓進盒子時頭盔照樣變暗）" } };

        public static HeadSolidDef Load() => Resources.Load<HeadSolidDef>(ResourceName);

        public bool Excluded(Vector3 head)
        {
            foreach (var z in excludeZones ?? new Bounds[0]) if (z.Contains(head)) return true;
            return false;
        }

        /// <summary>名稱樣式比對：* 是任意字元；「父/名稱」比對直接上層。</summary>
        public static bool Match(Transform t, string pattern)
        {
            int slash = pattern.IndexOf('/');
            if (slash < 0) return Wild(t.name, pattern);
            return t.parent != null && Wild(t.parent.name, pattern.Substring(0, slash)) && Wild(t.name, pattern.Substring(slash + 1));
        }

        public static bool Wild(string s, string p) => System.Text.RegularExpressions.Regex.IsMatch(s, "^" + System.Text.RegularExpressions.Regex.Escape(p).Replace("\\*", ".*") + "$");
    }
}
