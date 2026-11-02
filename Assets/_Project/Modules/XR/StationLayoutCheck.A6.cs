using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// a6（規格 8.1 第 1–12 項）的站點規則：門檻暫定，量測值與判定寫進 SceneResult.pending（"◇ "）。
    /// EnforceA6 為 false 時不加進 bad，建置結果和以前相同。缺物件只寫「缺 xxx，無法檢查」，不丟例外。
    /// </summary>
    public static partial class StationLayoutCheck
    {
        // shortcut: a6 規則先只回報不擋建置, 收到 a6 補充（位置與門檻定案）後改 EnforceA6=true
        public static bool EnforceA6 = false;

        const string PanRootName = "PAN_Root";
        // shortcut: 絕緣子半寬 0.085、接地座挖孔半寬 0.15 抄 RoofSiteBuilder 的常數（RoofSceneDef 沒有這兩個欄位）, a6 補充給尺寸時改成資料欄位
        const float InsulatorHalf = 0.085f, CarBodyGroundHalf = 0.15f;

        static void Pend(SceneResult r, string item, string text, bool ok)
        {
            string line = $"【第{item}項】{text}";
            r.pending.Add(line + (ok ? "✔" : "✘"));
            if (!ok && EnforceA6) r.bad.Add(line);
        }
        static void Miss(SceneResult r, string item, string what) => r.pending.Add($"【第{item}項】缺 {what}，無法檢查");
        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
        static string P2(Vector2 v) => $"({v.x:0.00}, {v.y:0.00})";

        /// <summary>
        /// a6 全部規則（CheckScene 最後呼叫）。pts＝場景站點（ID → 傳送點）；loweredLocal＝降弓整組 Bounds（PAN_Root 局部，原點＝底架底面中心），
        /// null 時讀 PAN_Root 的 BoxCollider（PantographAssemblyBuilder 建的降弓範圍）。
        /// </summary>
        public static void CheckA6(StationSet set, RoofSceneDef d, IDictionary<string, XrTeleportPoint> pts, System.Func<string, Transform> find, SceneResult r, Bounds? loweredLocal = null)
        {
            Transform F(string id) => (find != null ? find(id) : null) ?? GameObject.Find(id)?.transform;
            var stations = (set.stations ?? new StationDef[0]).Where(s => s != null && pts.ContainsKey(s.id)).ToList();
            Vector3 Pos(StationDef s) => pts[s.id].transform.position;

            A6PlatformSpacing(set, stations, Pos, r);
            A6GateFront(set, d, stations, Pos, r);
            A6PoleRack(set, pts, F("env.pole_rack"), r);
            A6LiftCorridor(set, d, stations, Pos, r);

            var root = GameObject.Find(PanRootName)?.transform;
            if (loweredLocal == null && root != null && root.GetComponent<BoxCollider>() is BoxCollider bc) loweredLocal = new Bounds(bc.center, bc.size);
            var standOrigin = new Vector3(d.standCenterXZ.x, d.StandTopY, d.standCenterXZ.y);   // PAN_Root 原點放在台架中心的支撐座頂面
            Bounds? onStand = loweredLocal.HasValue ? new Bounds(loweredLocal.Value.center + standOrigin, loweredLocal.Value.size) : (Bounds?)null;
            A6StandStations(set, d, stations, s => pts[s.id].transform, onStand, r);

            Vector2 baseW = pts.TryGetValue("tp.roof.base_w", out var bw) ? XZ(bw.transform.position) : set.roofBaseWXZ;
            A6RoofObstacles(d, stations, Pos, F("ip.roof.carbody_ground"), r);
            A6CarBodyGround(set, d, F("ip.roof.carbody_ground") ?? F("Roof_CarBodyGround"), baseW, r);
            A6RoofBase(set, pts, F("ip.roof.hv_cable_ground") ?? F("HV_Cable_End"), r);
            A6Stair(set, d, GameObject.Find("ENV_PlatformStair"), r);
            A6StandClearance(set, d, root, standOrigin, r);
            A6Slings(root, standOrigin, F, r);
        }

        /// <summary>第1項：平台站點兩兩水平距離 ≥ platformStationMinSpacing（sharedWith 互連的不算）。</summary>
        static void A6PlatformSpacing(StationSet set, List<StationDef> st, System.Func<StationDef, Vector3> pos, SceneResult r)
        {
            var plat = st.Where(s => s.floor == StationFloor.Platform).ToList();
            for (int i = 0; i < plat.Count; i++)
                for (int j = i + 1; j < plat.Count; j++)
                {
                    var a = plat[i]; var b = plat[j];
                    if (a.sharedWith == b.id || b.sharedWith == a.id || (!string.IsNullOrEmpty(a.sharedWith) && a.sharedWith == b.sharedWith)) continue;
                    float dist = Flat(pos(a), pos(b));
                    Pend(r, "1", $"{a.id}–{b.id} 水平 {dist:0.00} m（暫定門檻 ≥ {set.platformStationMinSpacing}）", dist >= set.platformStationMinSpacing - 1e-4f);
                }
        }

        /// <summary>第2項：站點離活動欄開口（GateBoundary 的 Z 範圍，在開口 X 上的線段）水平 ≥ gateFrontClearance。</summary>
        static void A6GateFront(StationSet set, RoofSceneDef d, List<StationDef> st, System.Func<StationDef, Vector3> pos, SceneResult r)
        {
            var gate = GameObject.Find("GateBoundary")?.GetComponent<Collider>();
            Vector2 g0, g1;
            if (gate != null && gate.enabled) { var b = gate.bounds; g0 = new Vector2(b.center.x, b.min.z); g1 = new Vector2(b.center.x, b.max.z); }
            else { g0 = new Vector2(d.PlatformInnerX, d.gateCenterZ - d.gateWidth / 2f); g1 = new Vector2(d.PlatformInnerX, d.gateCenterZ + d.gateWidth / 2f); }   // 欄開著（collider 停用）時用資料
            int n = 0;
            foreach (var s in st)
            {
                float dist = CorridorDistance(g0, g1, 0f, XZ(pos(s)));
                if (dist < set.gateFrontClearance - 1e-4f) { n++; Pend(r, "2", $"{s.id} 離活動欄開口 Z {g0.y:0.00}…{g1.y:0.00} 水平 {dist:0.00} m（暫定門檻 ≥ {set.gateFrontClearance}）", false); }
            }
            if (n == 0) Pend(r, "2", $"活動欄開口 Z {g0.y:0.00}…{g1.y:0.00} 前 {set.gateFrontClearance} m 內沒有站點", true);
        }

        /// <summary>第4項(d)（D25）：env.pole_rack 到 tp.platform.ground 水平 ≤ poleRackGroundMaxDistance，且 X &lt; poleRackMaxX。桿架不是站點。</summary>
        static void A6PoleRack(StationSet set, IDictionary<string, XrTeleportPoint> pts, Transform rack, SceneResult r)
        {
            if (rack == null) { Miss(r, "4d", "env.pole_rack"); return; }
            if (!pts.TryGetValue("tp.platform.ground", out var g)) { Miss(r, "4d", "tp.platform.ground"); return; }
            var c = Center(rack);
            float dist = Flat(c, g.transform.position);
            Pend(r, "4d", $"env.pole_rack {P2(XZ(c))} 到 tp.platform.ground 水平 {dist:0.00} m（暫定門檻 ≤ {set.poleRackGroundMaxDistance}）", dist <= set.poleRackGroundMaxDistance + 1e-4f);
            Pend(r, "4d", $"env.pole_rack X {c.x:0.00}（暫定門檻 < {set.poleRackMaxX}）", c.x < set.poleRackMaxX);
        }

        /// <summary>第5項：吊運路徑（弓座 (0,0) 到台架中心，兩側各加寬 liftCorridorHalfWidth）內沒有站點；tp.platform.crane 離路徑 ≥ craneCorridorMinDistance。</summary>
        static void A6LiftCorridor(StationSet set, RoofSceneDef d, List<StationDef> st, System.Func<StationDef, Vector3> pos, SceneResult r)
        {
            var a = Vector2.zero; var b = d.standCenterXZ; float w = set.liftCorridorHalfWidth;
            int n = 0;
            foreach (var s in st)
                if (CorridorDistance(a, b, w, XZ(pos(s))) <= 0f) { n++; Pend(r, "5", $"{s.id} {P2(XZ(pos(s)))} 在吊運路徑 {P2(a)}→{P2(b)} ±{w} m 內", false); }
            if (n == 0) Pend(r, "5", $"吊運路徑 {P2(a)}→{P2(b)} ±{w} m 內沒有站點", true);
            var crane = st.FirstOrDefault(s => s.id == "tp.platform.crane");
            if (crane == null) { Miss(r, "5", "tp.platform.crane"); return; }
            float dist = CorridorDistance(a, b, w, XZ(pos(crane)));
            Pend(r, "5", $"tp.platform.crane 離吊運路徑 {dist:0.00} m（暫定門檻 ≥ {set.craneCorridorMinDistance}）", dist >= set.craneCorridorMinDistance - 1e-4f);
        }

        /// <summary>第6項：tp.stand.* 在維修區內、Y＝groundY ±容差、離台架邊 standStationOffset ±容差、面向台架、身體膠囊不碰放上台架的降弓整組。</summary>
        static void A6StandStations(StationSet set, RoofSceneDef d, List<StationDef> st, System.Func<StationDef, Transform> tf, Bounds? onStand, SceneResult r)
        {
            var stands = st.Where(s => s.id.StartsWith("tp.stand.")).ToList();
            if (stands.Count == 0) { Miss(r, "6", "tp.stand.* 站點"); return; }
            var c = d.standCenterXZ; var half = d.standSizeXZ / 2f;
            if (onStand == null) r.pending.Add("【第6項】未提供降弓 Bounds（找不到 PAN_Root 的 BoxCollider），站點和降弓整組的淨空無法檢查");
            foreach (var s in stands)
            {
                var t = tf(s); var p = t.position;
                bool inZone = p.x >= d.maintMinXZ.x && p.x <= d.maintMaxXZ.x && p.z >= d.maintMinXZ.y && p.z <= d.maintMaxXZ.y;
                Pend(r, "6", $"{s.id} {P2(XZ(p))} 在維修區 X {d.maintMinXZ.x}…{d.maintMaxXZ.x}、Z {d.maintMinXZ.y}…{d.maintMaxXZ.y}", inZone);
                float dy = Mathf.Abs(p.y - d.groundY);
                Pend(r, "6", $"{s.id} Y {p.y:0.000}，和地面 {d.groundY} 差 {dy:0.000} m（暫定門檻 ≤ {set.standStationYTolerance}）", dy <= set.standStationYTolerance + 1e-4f);
                float edge = new Vector2(Mathf.Max(Mathf.Abs(p.x - c.x) - half.x, 0f), Mathf.Max(Mathf.Abs(p.z - c.y) - half.y, 0f)).magnitude;
                Pend(r, "6", $"{s.id} 離台架邊 {edge:0.00} m（暫定門檻 {d.standStationOffset} ±{set.standStationOffsetTolerance}）", Mathf.Abs(edge - d.standStationOffset) <= set.standStationOffsetTolerance + 1e-4f);
                var dir = new Vector3(c.x - p.x, 0f, c.y - p.z);
                float ang = Vector3.Angle(t.forward, dir);
                Pend(r, "6", $"{s.id} 面向台架偏 {ang:0.0}°（暫定門檻 ≤ {set.facingToleranceDeg}°）", ang <= set.facingToleranceDeg);
                if (onStand == null) continue;
                var ob = onStand.Value;
                bool vOverlap = ob.min.y < p.y + 1.8f && ob.max.y > p.y + 0.1f;   // 身體膠囊 0.1–1.8 m，和 CheckScene 相同
                float hd = Flat(p, ob.ClosestPoint(new Vector3(p.x, ob.center.y, p.z)));
                Pend(r, "6", $"{s.id} 離台架上的降弓整組 水平 {hd:0.00} m（暫定門檻 ≥ {set.clearRadius}）", !vOverlap || hd >= set.clearRadius - 1e-4f);
            }
        }

        /// <summary>第7項(b)：車頂站點不在母線帶（外擴 obstacleMargin）、絕緣子孔、車體接地座上。</summary>
        static void A6RoofObstacles(RoofSceneDef d, List<StationDef> st, System.Func<StationDef, Vector3> pos, Transform seat, SceneResult r)
        {
            var roof = st.Where(s => s.floor == StationFloor.Roof).ToList();
            if (roof.Count == 0) { Miss(r, "7b", "車頂站點"); return; }
            var bus = BusbarRect(d, d.obstacleMargin);
            var seatXZ = seat != null ? XZ(seat.position) : new Vector2(d.carBodyGround.x, d.carBodyGround.z);
            var ins = d.insulatorPositions ?? new Vector3[0];
            float insR = InsulatorHalf + d.obstacleMargin;
            foreach (var s in roof)
            {
                var p = XZ(pos(s));
                var hits = new List<string>();
                if (bus.Contains(p)) hits.Add($"母線帶 X {bus.xMin:0.00}…{bus.xMax:0.00}");
                for (int i = 0; i < ins.Length; i++)
                    if (Vector2.Distance(p, XZ(ins[i])) < insR) hits.Add($"絕緣子 {i + 1}（{Vector2.Distance(p, XZ(ins[i])):0.00} m < {insR:0.00}）");
                if (Mathf.Abs(p.x - seatXZ.x) <= CarBodyGroundHalf && Mathf.Abs(p.y - seatXZ.y) <= CarBodyGroundHalf) hits.Add("車體接地座");
                Pend(r, "7b", $"{s.id} {P2(p)} " + (hits.Count == 0 ? "不在母線帶、絕緣子孔、車體接地座上" : "落在 " + string.Join("、", hits)), hits.Count == 0);
            }
        }

        /// <summary>第8項：車體接地座離 tp.roof.base_w ≤ carBodyGroundMaxDistance、不在 Walkable 上、不和母線帶重疊。</summary>
        static void A6CarBodyGround(StationSet set, RoofSceneDef d, Transform seat, Vector2 baseW, SceneResult r)
        {
            if (seat == null) { Miss(r, "8", "車體接地座 Roof_CarBodyGround"); return; }
            var p = seat.position;
            float dist = Vector2.Distance(XZ(p), baseW);
            Pend(r, "8", $"車體接地座 {P2(XZ(p))} 到 tp.roof.base_w {P2(baseW)} {dist:0.00} m（暫定門檻 ≤ {set.carBodyGroundMaxDistance}）", dist <= set.carBodyGroundMaxDistance + 1e-4f);
            bool onWalk = Physics.Raycast(new Vector3(p.x, p.y + 1f, p.z), Vector3.down, 1.5f, SceneLayers.WalkableMask, QueryTriggerInteraction.Ignore);
            Pend(r, "8", $"車體接地座正下方{(onWalk ? "有" : "沒有")} Walkable", !onWalk);
            var bus = BusbarRect(d, 0f);
            var seatR = new Rect(p.x - CarBodyGroundHalf, p.z - CarBodyGroundHalf, 2 * CarBodyGroundHalf, 2 * CarBodyGroundHalf);
            Pend(r, "8", $"車體接地座 X {seatR.xMin:0.00}…{seatR.xMax:0.00} 和母線帶 X {bus.xMin:0.00}…{bus.xMax:0.00}{(seatR.Overlaps(bus) ? "重疊" : "不重疊")}", !seatR.Overlaps(bus));
        }

        /// <summary>第9項：base_w 面向 +X、base_e 面向 −X；base_e 到 HV_Cable_End ≤ baseECableMaxDistance，超過就沿 +Z 搜尋。</summary>
        static void A6RoofBase(StationSet set, IDictionary<string, XrTeleportPoint> pts, Transform cable, SceneResult r)
        {
            foreach (var (id, face) in new[] { ("tp.roof.base_w", Vector3.right), ("tp.roof.base_e", Vector3.left) })
            {
                if (set.Find(id) == null || !pts.TryGetValue(id, out var tp)) { r.pending.Add($"【第9項】{id}：站點資料尚未建立"); continue; }
                float ang = Vector3.Angle(tp.transform.forward, face);
                Pend(r, "9", $"{id} 朝向偏 {(face.x > 0 ? "+X" : "−X")} {ang:0.0}°（暫定門檻 ≤ 1°）", ang <= 1f);
            }
            if (cable == null) { Miss(r, "9", "HV_Cable_End"); return; }
            var start = pts.TryGetValue("tp.roof.base_e", out var be) ? XZ(be.transform.position) : set.roofBaseEXZ;
            var (ok, pos, dist) = SearchBaseE(start, XZ(cable.position), set.baseECableMaxDistance, set.baseESearchStep, set.baseESearchMaxZ, 0.80f, set.busbarHoleMinX, set.busbarHoleMaxX);
            string moved = pos == start ? "" : $"（從 {P2(start)} 沿 +Z 移到 {P2(pos)}）";
            Pend(r, "9", ok ? $"tp.roof.base_e {P2(pos)} 到 HV_Cable_End {dist:0.00} m{moved}（暫定門檻 ≤ {set.baseECableMaxDistance}）"
                            : $"tp.roof.base_e 搜尋到 Z {set.baseESearchMaxZ} 仍不合格，最後試 {P2(pos)} 到 HV_Cable_End {dist:0.00} m（暫定門檻 ≤ {set.baseECableMaxDistance}）", ok);
        }

        /// <summary>
        /// 第9項的純計算：從 start 沿 +Z 每次 step，找第一個到 cableEnd 水平 ≤ maxDist 的位置（Z 不超過 maxZ；X 不變，要在 |X| ≤ maxAbsX 且不在母線孔帶 holeMinX…holeMaxX）。
        /// 找不到時 ok＝false，pos/dist＝最後試的位置與距離。
        /// </summary>
        public static (bool ok, Vector2 pos, float dist) SearchBaseE(Vector2 start, Vector2 cableEnd, float maxDist, float step, float maxZ, float maxAbsX, float holeMinX, float holeMaxX)
        {
            bool xOk = Mathf.Abs(start.x) <= maxAbsX + 1e-4f && (start.x < holeMinX - 1e-4f || start.x > holeMaxX + 1e-4f);
            var last = start; float lastD = Vector2.Distance(start, cableEnd);
            if (!xOk) return (false, last, lastD);
            for (int i = 0; i < 10000; i++)
            {
                var p = new Vector2(start.x, start.y + i * step);
                if (i > 0 && p.y > maxZ + 1e-4f) break;
                last = p; lastD = Vector2.Distance(p, cableEnd);
                if (lastD <= maxDist + 1e-4f) return (true, p, lastD);
                if (step <= 0f) break;
            }
            return (false, last, lastD);
        }

        /// <summary>第10項(b)：ENV_PlatformStair 的 Renderer Bounds Z 在 stairMinZ…stairMaxZ，且不在平台地板以上的平台範圍內。</summary>
        static void A6Stair(StationSet set, RoofSceneDef d, GameObject stair, SceneResult r)
        {
            var rs = stair != null ? stair.GetComponentsInChildren<Renderer>() : new Renderer[0];
            if (rs.Length == 0) { Miss(r, "10b", "ENV_PlatformStair（或它的 Renderer）"); return; }
            var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
            Pend(r, "10b", $"ENV_PlatformStair Z {b.min.z:0.00}…{b.max.z:0.00}（暫定門檻 {set.stairMinZ}…{set.stairMaxZ}）", b.min.z >= set.stairMinZ - 1e-3f && b.max.z <= set.stairMaxZ + 1e-3f);
            const float e = 1e-3f;
            bool over = b.max.y > d.platformFloorY + e && b.min.x < d.PlatformInnerX - e && b.max.x > d.PlatformOuterX + e && b.min.z < d.platformZMax - e && b.max.z > d.platformZMin + e;
            Pend(r, "10b", $"ENV_PlatformStair {(over ? "伸進" : "沒有伸進")}平台上方（平台 X {d.PlatformOuterX:0.00}…{d.PlatformInnerX:0.00}、Z {d.platformZMin}…{d.platformZMax}）", !over);
        }

        /// <summary>
        /// 第11項：降弓整組放上台架（PAN_Root 原點在台架中心支撐座頂面）後，除了底架（Base 網格與 PAN_Base 底下）以外最低點到台架外框頂面 ≥ standFrameMinClearance。
        /// shortcut: 用目前場景姿態的 Renderer（XrRigBuilder 呼叫 CheckScene 前已 SetLift(0)）、不看水平是否在外框上方, 要精確時改成降弓姿態網格逐一投影到外框
        /// </summary>
        static void A6StandClearance(StationSet set, RoofSceneDef d, Transform root, Vector3 standOrigin, SceneResult r)
        {
            if (root == null) { Miss(r, "11", PanRootName); return; }
            var pBase = root.Find("PAN_Base");
            var rs = root.GetComponentsInChildren<Renderer>().Where(x => x.enabled && x.name != "Base" && (pBase == null || !x.transform.IsChildOf(pBase))).ToList();
            if (rs.Count == 0) { Miss(r, "11", "PAN_Root 底下的 Renderer"); return; }
            var low = rs.OrderBy(x => x.bounds.min.y).First();
            float lowest = low.bounds.min.y + (standOrigin.y - root.position.y);
            float frameTop = d.groundY + d.standFrameTopAboveGround, gap = lowest - frameTop;
            Pend(r, "11", $"台架上的降弓整組（底架除外）最低 Y {lowest:0.000}（{low.name}），到外框頂面 {frameTop:0.000} 垂直 {gap:0.000} m（暫定門檻 ≥ {set.standFrameMinClearance}）", gap >= set.standFrameMinClearance - 1e-4f);
        }

        /// <summary>第12項：4 條吊帶（吊耳 → Hook_AttachPoint_Target，換到放上台架的姿態）不穿過台架外框 StandFrame。</summary>
        static void A6Slings(Transform root, Vector3 standOrigin, System.Func<string, Transform> find, SceneResult r)
        {
            if (root == null) { Miss(r, "12", PanRootName); return; }
            var hook = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Hook_AttachPoint_Target");
            if (hook == null) { Miss(r, "12", "Hook_AttachPoint_Target"); return; }
            var frame = GameObject.Find("StandFrame")?.GetComponent<Collider>();
            if (frame == null) { Miss(r, "12", "台架外框 StandFrame"); return; }
            var fb = frame.bounds;
            var delta = standOrigin - root.position;
            var h = hook.position + delta;
            for (int i = 1; i <= 4; i++)
            {
                var lp = find("ip.pan_assy.lift_point_" + i);
                if (lp == null) { Miss(r, "12", "ip.pan_assy.lift_point_" + i); continue; }
                var a = lp.position + delta;
                float len = Vector3.Distance(a, h);
                bool hit = fb.Contains(a) || fb.Contains(h) || (fb.IntersectRay(new Ray(a, h - a), out float t) && t <= len);
                Pend(r, "12", $"吊帶 {i}：{a:F2}→{h:F2} {(hit ? "穿過" : "不穿過")}台架外框（頂面 Y {fb.max.y:0.000}）", !hit);
            }
        }

        /// <summary>母線帶（XZ）：有 Busbar 網格就用它的 Bounds，沒有就用資料的 X 帶、Z 取車頂區段；外擴 margin。</summary>
        static Rect BusbarRect(RoofSceneDef d, float margin)
        {
            var bus = GameObject.Find("Busbar")?.GetComponentInChildren<Renderer>();
            Rect rc = bus != null ? Rect.MinMaxRect(bus.bounds.min.x, bus.bounds.min.z, bus.bounds.max.x, bus.bounds.max.z)
                                  : Rect.MinMaxRect(d.busbarX - d.busbarWidth / 2f, d.roofSectionZMin, d.busbarX + d.busbarWidth / 2f, d.roofSectionZMax);
            return Rect.MinMaxRect(rc.xMin - margin, rc.yMin - margin, rc.xMax + margin, rc.yMax + margin);
        }

        /// <summary>
        /// 點 p 到「線段 a→b 兩側各加寬 halfWidth 的長方形」的水平距離（XZ，0＝在裡面）。halfWidth＝0 時就是點到線段的距離。
        /// </summary>
        public static float CorridorDistance(Vector2 a, Vector2 b, float halfWidth, Vector2 p)
        {
            var ab = b - a; float len = ab.magnitude;
            if (len < 1e-6f) return Mathf.Max(0f, Vector2.Distance(a, p) - halfWidth);
            var u = ab / len; var ap = p - a;
            float along = Vector2.Dot(ap, u), perp = Mathf.Abs(u.x * ap.y - u.y * ap.x);
            float dx = Mathf.Max(0f, -along, along - len), dz = Mathf.Max(0f, perp - halfWidth);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
