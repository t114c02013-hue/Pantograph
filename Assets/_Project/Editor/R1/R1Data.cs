using System.IO;
using System.Linq;
using PantographSim.Data;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1 的資料資產（不存在就用預設值建立；已存在就照用，不覆寫）：RoofSceneDef、StationSet＋8 個 StationDef、RoofAccessRule、HeightProfile、LocomotionSettings。
    /// 數值都是 EX，之後只改資產。
    /// </summary>
    public static class R1Data
    {
        public const string Folder = "Assets/_Project/Modules/Worksite/Data";
        public const string StationFolder = Folder + "/Stations";
        public static string RoofScenePath => Folder + "/RoofSceneDef.asset";
        public static string StationSetPath => Folder + "/StationSet.asset";
        public static string RoofAccessPath => Folder + "/RoofAccessRule.asset";
        public static string HeightPath => Folder + "/HeightProfile.asset";
        public static string LocomotionPath => Folder + "/LocomotionSettings.asset";

        public static RoofSceneDef RoofScene => Ensure<RoofSceneDef>(RoofScenePath);
        public static RoofAccessRule RoofAccess => Ensure<RoofAccessRule>(RoofAccessPath);
        public static HeightProfile Height => Ensure<HeightProfile>(HeightPath);
        public static LocomotionSettings Locomotion => Ensure<LocomotionSettings>(LocomotionPath);

        /// <summary>按鍵表（Resources，執行期 InputBindingTable.LoadDefault 讀它）；不存在或空的就用預設表建立，已存在就照用。</summary>
        public const string InputBindingsPath = "Assets/_Project/Modules/XR/Resources/InputBindingTable.asset";
        public static InputBindingTable InputBindings
        {
            get
            {
                var t = Ensure<InputBindingTable>(InputBindingsPath);
                if (t.rows != null && t.rows.Length > 0) return t;
                t.rows = InputBindingTable.CreateDefault().rows;
                EditorUtility.SetDirty(t);
                AssetDatabase.SaveAssets();
                return t;
            }
        }

        /// <summary>T3 可移動面板的預設位置（Resources，執行期 PanelLayout.LoadDefault 讀它；已存在就照用）。</summary>
        public const string PanelLayoutPath = "Assets/_Project/Modules/XR/Resources/PanelLayout.asset";
        public static PanelLayout Panels => Ensure<PanelLayout>(PanelLayoutPath);

        static T Ensure<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            AssetDatabase.SaveAssets();
            return a;
        }

        /// <summary>8 個站點（EX）。tp.roof.inner／outer 共用 (0, −0.65)：兩條滑板都從外側操作（單臂弓的底架在肘側）。</summary>
        public static StationSet Stations
        {
            get
            {
                var set = Ensure<StationSet>(StationSetPath);
                if (set.stations != null && set.stations.Length > 0 && set.stations.All(s => s != null)) return set;
                Directory.CreateDirectory(StationFolder);
                const string roofLock = "先完成驗電、掛接地，並扣好安全帶";
                set.stations = new[]
                {
                    Station("tp.cab", "駕駛室控制盤前", StationFloor.Cab, new Vector2(-6.20f, -3.00f), "ip.cab.panel", 2.5f),
                    Station("tp.platform.lift", "升降操作盒前", StationFloor.Platform, new Vector2(-2.00f, 0.00f), "part.pan_head", 2.5f),
                    Station("tp.platform.rack", "平台工具架前", StationFloor.Platform, new Vector2(-2.20f, -2.50f), "env.tool_rack"),
                    Station("tp.platform.ground", "接地端子旁", StationFloor.Platform, new Vector2(-1.95f, 0.30f), "ip.depot.ground_terminal"),
                    Station("tp.platform.anchor", "活動欄與平台掛點", StationFloor.Platform, new Vector2(-2.00f, -1.00f), "ip.depot.platform_anchor"),
                    Station("tp.roof.inner", "車頂（肘側滑板）", StationFloor.Roof, new Vector2(0.00f, -0.65f), "part.carbon_strip_L", 0f, "roof.open", roofLock),
                    Station("tp.roof.outer", "車頂（外側滑板）", StationFloor.Roof, new Vector2(0.00f, -0.65f), "part.carbon_strip_R", 0f, "roof.open", roofLock, "tp.roof.inner"),
                    Station("tp.roof.shelf", "車頂工具箱與零件盒", StationFloor.Roof, new Vector2(-0.60f, -2.50f), "env.roof_toolbox", 0f, "roof.open", roofLock),
                };
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssets();
                return set;
            }
        }

        /// <summary>
        /// R1.5a a3：平台站點改成相對平台靠車側邊（車寬 3.38 時 PlatformInnerX＝−1.81），駕駛室移到股道 +X 側，新增 tp.platform.crane（面向 +X，遙控器掛座旁）。
        /// 可以重跑（每次寫同樣的值）。D15：tp.platform.ground＝(−2.20, +0.60)。
        /// </summary>
        public static StationSet ApplyR15a()
        {
            var set = Stations;
            void Rel(string id, float dx, float z) { var s = set.Find(id); s.relativeToPlatformInner = true; s.positionXZ = new Vector2(dx, z); EditorUtility.SetDirty(s); }
            float inner = RoofScene.PlatformInnerX;
            Rel("tp.platform.rack", -2.41f - inner, -2.80f);
            Rel("tp.platform.lift", -2.20f - inner, -0.05f);
            Rel("tp.platform.ground", -2.20f - inner, 0.60f);
            Rel("tp.platform.anchor", -2.20f - inner, -1.80f);
            var cab = set.Find("tp.cab");
            var cc = RoofScene.cabCenter;
            cab.positionXZ = new Vector2(cc.x - RoofScene.cabSize / 2f + 0.06f + 0.5f, cc.z);   // 控制盤前 0.5 m
            EditorUtility.SetDirty(cab);
            if (set.Find("tp.platform.crane") == null)
            {
                var crane = Station("tp.platform.crane", "天車遙控器掛座旁", StationFloor.Platform, Vector2.zero, "");
                set.stations = set.stations.Concat(new[] { crane }).ToArray();
            }
            var cr = set.Find("tp.platform.crane");
            cr.relativeToPlatformInner = true; cr.positionXZ = new Vector2(-2.40f - inner, -3.50f); cr.facingDirection = Vector3.right; cr.facingTargetId = "";
            EditorUtility.SetDirty(cr);

            StandStations(set);
            foreach (var s in set.stations.Where(x => x != null && x.zoneId != ZoneDef.Maintenance)) { s.zoneId = ZoneDef.Arrival; EditorUtility.SetDirty(s); }
            Zones();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return set;
        }

        /// <summary>a4：地面維修區的台架四邊站點（台架邊中點外 standStationOffset，面向台架；teleport 進出維修區）。a6 G2 台架外框改由建置推導後重跑。</summary>
        public static void StandStations(StationSet set)
        {
            var d = RoofScene;
            var c = d.StandFrameCenterXZ;   // a6 增補 4：量到外框（外框中心可能偏離台架中心）
            float hx = d.standSizeXZ.x / 2f + d.standStationOffset, hz = d.standSizeXZ.y / 2f + d.standStationOffset;
            foreach (var (id, label, xz) in new[] { ("tp.stand.n", "台架北側", new Vector2(c.x, c.y + hz)), ("tp.stand.e", "台架東側", new Vector2(c.x + hx, c.y)),
                                                    ("tp.stand.s", "台架南側", new Vector2(c.x, c.y - hz)), ("tp.stand.w", "台架西側", new Vector2(c.x - hx, c.y)) })
            {
                if (set.Find(id) == null) set.stations = set.stations.Concat(new[] { Station(id, label, StationFloor.Ground, xz, "env.panto_stand", 2.5f) }).ToArray();
                var s = set.Find(id);
                s.floor = StationFloor.Ground; s.positionXZ = xz; s.facingTargetId = "env.panto_stand"; s.maxTargetDistance = 2.5f; s.zoneId = ZoneDef.Maintenance;
                EditorUtility.SetDirty(s);
            }
            EditorUtility.SetDirty(set);
        }

        /// <summary>
        /// R1.5a a6（補充 v4.3、增補 1）：平台擺放（桿架、工具架、遙控器掛座）、車體接地座、新站點（tp.platform.parts／scrap、tp.roof.base_w／base_e）、
        /// 定案門檻、NPC 站位。可以重跑（每次寫同樣的值）。base_w／base_e 的 Z 在建置時由 RoofSiteBuilder 從底架模型推導後寫回。
        /// </summary>
        public static StationSet ApplyA6()
        {
            var set = ApplyR15a();
            var d = RoofScene;
            d.poleRackFromOuter = new Vector3(-2.85f - d.PlatformOuterX, d.poleRackFromOuter.y, 0.60f);
            d.toolRackFromOuter = new Vector3(d.toolRackFromOuter.x, d.toolRackFromOuter.y, -2.40f);
            d.pendantDockFromOuter = new Vector3(-3.06f - d.PlatformOuterX, 1.20f, -2.95f);   // D32
            d.carBodyGround = new Vector3(-1.15f, d.carBodyGround.y, 0.43f);   // D34：外肩（Y 由車頂斷面算）
            // D36（增補 5）＋增補 6 第 7 點 A：底架兩側 Walkable 改回 1.00、前端到 −0.75；底架站點 |X| 0.85；不會動的零件不外擴
            d.roofBaseWalkHalfWidth = 1.00f; d.roofBaseWarningX = 1.00f; d.roofBaseWalkZMin = -0.75f; d.roofBaseStationX = 0.85f; d.roofBaseSouthZ = -0.55f;
            d.staticCarveInflate = 0f; d.movingCarveInflate = 0.15f;
            EditorUtility.SetDirty(d);

            float inner = d.PlatformInnerX;
            var rack = set.Find("tp.platform.rack"); rack.positionXZ = new Vector2(-2.41f - inner, -2.40f); EditorUtility.SetDirty(rack);
            var crane = set.Find("tp.platform.crane"); crane.allowedWhileLifted = true; EditorUtility.SetDirty(crane);
            string roofLock = set.Find("tp.roof.inner").lockedMessage;
            StationDef Add(string id, string label, StationFloor floor)
            {
                if (set.Find(id) == null) set.stations = set.stations.Concat(new[] { Station(id, label, floor, Vector2.zero, "") }).ToArray();
                return set.Find(id);
            }
            foreach (var (id, label, z) in new[] { ("tp.platform.parts", "備品架前（日常）", 1.60f), ("tp.platform.scrap", "待報廢箱前（日常）", 2.85f) })
            {
                var s = Add(id, label, StationFloor.Platform);
                s.relativeToPlatformInner = true; s.positionXZ = new Vector2(-2.41f - inner, z); s.facingTargetId = ""; s.facingDirection = Vector3.left; s.zoneId = ZoneDef.Arrival;
                EditorUtility.SetDirty(s);
            }
            // D33／D36：base_w／base_e（面向 ±X；接地座、吊梁類／控制線接頭、固定帶扣座）＋四個子站點（面向自己那支底座螺栓；.n 的 Z 建置時取螺栓實測 Z，.s 固定 roofBaseSouthZ）
            // 子站點的螺栓距離上限寫在 maxTargetDistance（.s 斜向 0.70、.n 0.60）；扳手起始方向 wrenchStartDir（D36 補充三）
            foreach (var (id, label, sgn, bolt, maxD, wrench) in new[] {
                ("tp.roof.base_w", "車頂拆裝座（−X 側）", -1f, 0, 0f, Vector2.zero), ("tp.roof.base_e", "車頂拆裝座（+X 側）", 1f, 0, 0f, Vector2.zero),
                ("tp.roof.base_w.s", "底座螺栓 1 前", -1f, 1, 0.70f, new Vector2(1f, -1f).normalized), ("tp.roof.base_e.s", "底座螺栓 2 前", 1f, 2, 0.70f, new Vector2(1f, 1f).normalized),
                ("tp.roof.base_e.n", "底座螺栓 3 前", 1f, 3, 0.60f, new Vector2(0f, 1f)), ("tp.roof.base_w.n", "底座螺栓 4 前", -1f, 4, 0.60f, new Vector2(0f, -1f)) })
            {
                var s = Add(id, label, StationFloor.Roof);
                s.positionXZ = new Vector2(sgn * d.roofBaseStationX, id.EndsWith(".s") ? d.roofBaseSouthZ : s.positionXZ.y);   // base_w／e、.n 的 Z 由建置腳本寫回
                s.facingTargetId = bolt > 0 ? "part.pan_base.bolt_" + bolt : "";
                s.facingDirection = bolt > 0 ? Vector3.zero : new Vector3(-sgn, 0f, 0f);
                s.maxTargetDistance = maxD; s.wrenchStartDir = wrench; s.targetId = bolt > 0 ? "part.pan_base.bolt_" + bolt : "";   // 增補 7 E3(4)
                s.unlockKey = "roof.open"; s.lockedMessage = roofLock; s.zoneId = ZoneDef.Arrival;
                EditorUtility.SetDirty(s);
            }
            set.poleRackGroundMaxDistance = 0.70f;
            // 增補 4／5 之後第 15、19 項與 D36 的站點都已裁定：待定清單是空的（有新的待定才加，每筆要有 ruleRef、decidedBy、releaseSegment）
            set.pendingRulings = new PendingRuling[0];
            // D37（鐵道）＋增補 6 第 7 點 G：羊角對車頂站點 0.2（身體半徑），其他會動零件 0.4；沒列的零件用 clearRadius
            // 增補 7（D38、D39）：第 21 項門檻與要算的扳手（底座螺栓 CSV v2.9：RM_04 b、IN_06 d 套筒扳手，IN_07 d 大扭力扳手）
            set.wrenchSweepMinClearance = 0.02f; set.wrenchRadialStep = 0.05f;
            set.wrenchSweepTools = new[] { "tool.socket_wrench", "tool.torque_wrench_l" };
            d.craneHookTopTolerance = 0.02f; d.gateMinWidth = 0.60f; EditorUtility.SetDirty(d);
            set.partClearRadius = new[]
            {
                new PartClearRadius { partGroup = "PanHead", radius = 0.20f, scope = PartClearScope.RoofOnly },
                new PartClearRadius { partGroup = "LowerArm", radius = 0.40f, scope = PartClearScope.AllStations },
                new PartClearRadius { partGroup = "UpperArm", radius = 0.40f, scope = PartClearScope.AllStations },
                new PartClearRadius { partGroup = "Rod", radius = 0.40f, scope = PartClearScope.AllStations },
            };
            set.npcPoses = new[]
            {
                new NpcStandPose { npcId = "npc.guard", phase = NpcPhase.Normal, position = new Vector3(-3.00f, 0f, -1.00f) },
                new NpcStandPose { npcId = "npc.work_leader", phase = NpcPhase.Normal, position = new Vector3(-3.10f, 0f, -0.30f) },   // D36
                new NpcStandPose { npcId = "npc.rigging_lead", phase = NpcPhase.Lifting, position = new Vector3(-5.0f, d.groundY, -3.5f) },
                new NpcStandPose { npcId = "npc.guard", phase = NpcPhase.Lifting, position = new Vector3(-6.0f, d.groundY, -4.3f) },
                new NpcStandPose { npcId = "npc.work_leader", phase = NpcPhase.Lifting, position = new Vector3(-7.0f, d.groundY, -4.3f) },
            };
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return set;
        }

        public const string ZoneFolder = Folder + "/Zones";

        /// <summary>R1.5a a4：zone.arrival（進站區：股道、平台、車頂、駕駛室）與 zone.maintenance（地面維修區）。可以重跑。</summary>
        public static ZoneDef[] Zones()
        {
            Directory.CreateDirectory(ZoneFolder);
            var d = RoofScene;
            ZoneDef Z(string id, string label, Vector2 min, Vector2 max, float y)
            {
                var z = Ensure<ZoneDef>($"{ZoneFolder}/{id}.asset");
                z.id = id; z.label = label; z.minXZ = min; z.maxXZ = max; z.floorY = y;
                EditorUtility.SetDirty(z);
                return z;
            }
            var zones = new[]
            {
                Z(ZoneDef.Arrival, "進站區", new Vector2(d.PlatformOuterX, d.MiddleCarCenterZ - 1.5f * d.carLength - d.carGap), new Vector2(d.cabCenter.x + d.cabSize, d.MiddleCarCenterZ + 1.5f * d.carLength + d.carGap), d.roofFloorY),
                Z(ZoneDef.Maintenance, "地面維修區", d.maintMinXZ, d.maintMaxXZ, d.groundY),
            };
            AssetDatabase.SaveAssets();
            return zones;
        }

        static StationDef Station(string id, string label, StationFloor floor, Vector2 xz, string facing, float maxDist = 0f, string unlock = "", string locked = "", string shared = "")
        {
            string path = $"{StationFolder}/{id}.asset";
            var s = AssetDatabase.LoadAssetAtPath<StationDef>(path);
            if (s == null) { s = ScriptableObject.CreateInstance<StationDef>(); AssetDatabase.CreateAsset(s, path); }
            s.id = id; s.label = label; s.floor = floor; s.positionXZ = xz; s.facingTargetId = facing; s.maxTargetDistance = maxDist;
            s.unlockKey = unlock; s.lockedMessage = locked; s.sharedWith = shared;
            EditorUtility.SetDirty(s);
            return s;
        }
    }
}
