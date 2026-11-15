using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// a6（補充 v4.3、增補 1–6）的站點規則：合格寫進 SceneResult.passed（✔），不合格用 Fail(檢查項, 物件 ID, 文字)（建置失敗，除非 PendingRuling 有同一筆＝◇ 待定）。
    /// 門檻都在 StationSet／RoofSceneDef；缺物件寫「缺 xxx」並算不合格，不丟例外。純報告（不判定）的行用 Note（・）。
    /// </summary>
    public static partial class StationLayoutCheck
    {
        public static bool EnforceA6 = true;

        const string PanRootName = "PAN_Root";
        /// <summary>浮點比較容差（增補 7 E3(6)）：統一用這個，門檻值一律寫在資料。</summary>
        const float Eps = 1e-4f;
        static readonly string[] JointNames = { "Base", "LowerArm", "UpperArm", "PanHead" };

        static void Pend(SceneResult r, string item, string text, bool ok, string obj = "")
        {
            string line = $"【第{item}項】{text}";
            if (ok) r.passed.Add(line);
            else if (EnforceA6) r.Fail(item, obj, line);
            else r.warn.Add(line);
        }
        static void Note(SceneResult r, string item, string text) => r.info.Add($"【第{item}項】{text}");
        static void Miss(SceneResult r, string item, string what) => r.Fail(item, what, $"【第{item}項】缺 {what}，無法檢查");
        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
        static string P2(Vector2 v) => $"({v.x:0.00}, {v.y:0.00})";
        static string P3(Vector3 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000})";

        /// <summary>a6 全部規則（CheckScene 最後呼叫）。pts＝場景站點（ID → 傳送點）。</summary>
        public static void CheckA6(StationSet set, RoofSceneDef d, IDictionary<string, XrTeleportPoint> pts, System.Func<string, Transform> find, SceneResult r, WrenchSweepInput wrench = null)
        {
            Transform F(string id) => (find != null ? find(id) : null) ?? GameObject.Find(id)?.transform;
            var stations = (set.stations ?? new StationDef[0]).Where(s => s != null && pts.ContainsKey(s.id)).ToList();
            Vector3 Pos(StationDef s) => pts[s.id].transform.position;
            var root = GameObject.Find(PanRootName)?.transform;
            Bounds? tight = d.panLoweredTightBounds.size.sqrMagnitude > Eps ? d.panLoweredTightBounds : (Bounds?)null;   // LoweredTightBounds（PAN_Root 局部，RoofSceneDef，增補 7 E3(1)）
            var standOrigin = new Vector3(d.standCenterXZ.x, d.StandTopY, d.standCenterXZ.y);   // PAN_Root 原點放在台架中心的支撐座頂面

            A6PlatformSpacing(set, stations, Pos, r);
            A6GroundNotOverPit(d, stations, Pos, r);
            A6HeadSolid(stations, s => pts[s.id].transform, r);
            A6GateFront(set, d, stations, Pos, r);
            A6PoleRack(set, pts, F("env.pole_rack"), r);
            A6LiftCorridor(set, d, stations, Pos, r);
            A6LiftObstacles(set, d, root, tight, r);
            A6StandStations(set, d, stations, s => pts[s.id].transform, tight.HasValue ? new Bounds(tight.Value.center + standOrigin, tight.Value.size) : (Bounds?)null, r);
            A6RoofObstacles(d, stations, Pos, r);
            A6CarBodyGround(set, d, stations, s => pts[s.id].transform, F("ip.roof.carbody_ground"), r);
            A6RoofBase(set, pts, F, r);
            A6Stair(set, d, GameObject.Find("ENV_PlatformStair"), r);
            A6StandClearance(set, d, root, standOrigin, r);
            A6Slings(root, standOrigin, F, r);
            A6RoofEntry(set, d, F, r);
            A6ToCab(set, d, pts, r);
            A6Npc(set, d, stations, Pos, F, r);
            A6SubStations(set, d, pts, root, F, r);
            A6Wrench(set, pts, root, F, wrench, r);
            A6GateOpening(d, r);
            A6StripMat(set, d, stations, Pos, F("env.roof_strip_mat"), r);
            A6SideNodes(set, pts, root, F, r);
            A6PendantDock(set, pts, F, r);
            A6Lanyard(set, d, pts, root, r);
            A6GroundLineInsulator(set, F, r);
        }

        /// <summary>
        /// R1.5b b4（D43，鐵道）：接地 #1 的接地線（平台接地端子 ip.depot.ground_terminal → 弓頭接地掛點 ip.pan_head.ground_clamp，直線）
        /// 到 1 號絕緣子（ip.roof.insulator_1 的髒汙殼：軸線＋半徑）的距離 ≥ StationSet.groundLineInsulatorClearance。
        /// </summary>
        static void A6GroundLineInsulator(StationSet set, System.Func<string, Transform> F, SceneResult r)
        {
            Transform term = F("ip.depot.ground_terminal"), clamp = F(SimIds.GroundClamp), ins = F("ip.roof.insulator_1");
            if (term == null || clamp == null || ins == null) { Miss(r, "D43", term == null ? "ip.depot.ground_terminal" : clamp == null ? SimIds.GroundClamp : "ip.roof.insulator_1"); return; }
            var rd = ins.GetComponentInChildren<Renderer>(true);
            if (rd == null) { Miss(r, "D43", "ip.roof.insulator_1 的髒汙殼網格"); return; }
            var b = rd.bounds;
            float radius = Mathf.Max(b.extents.x, b.extents.z);
            var a0 = new Vector3(b.center.x, b.min.y, b.center.z); var a1 = new Vector3(b.center.x, b.max.y, b.center.z);
            float gap = SegmentDistance(term.position, clamp.position, a0, a1) - radius;
            Pend(r, "D43", $"接地 #1 的接地線到 1 號絕緣子 {gap:0.000} m（≥ {set.groundLineInsulatorClearance}）", gap >= set.groundLineInsulatorClearance - Eps);
        }

        /// <summary>兩條線段的最短距離。</summary>
        static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, rr = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, rr), s, t;
            if (a <= 1e-9f && e <= 1e-9f) return rr.magnitude;
            if (a <= 1e-9f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, rr);
                if (e <= 1e-9f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float bb = Vector3.Dot(d1, d2), den = a * e - bb * bb;
                    s = den > 1e-9f ? Mathf.Clamp01((bb * f - c * e) / den) : 0f;
                    t = (bb * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); } else if (t > 1f) { t = 1f; s = Mathf.Clamp01((bb - c) / a); }
                }
            }
            return ((p1 + d1 * s) - (p2 + d2 * t)).magnitude;
        }

        // ---------- 共用量測 ----------

        static Bounds? RenderBounds(Transform t, bool includeInactive = false)
        {
            if (t == null) return null;
            var rs = t.GetComponentsInChildren<Renderer>(includeInactive).Where(x => !(x is LineRenderer)).ToList();
            if (rs.Count == 0) return null;
            var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
            return b;
        }

        /// <summary>網格頂點範圍（世界；space 不為 null 時換到 space 局部）。Renderer.bounds 在網格旋轉後會變鬆（G1 C1），淨空與相交一律用這個。</summary>
        public static Bounds? VertexBounds(IEnumerable<Renderer> rs, Transform space = null)
        {
            Bounds? b = null;
            foreach (var x in rs)
            {
                Mesh mesh = null; var m = x.transform.localToWorldMatrix;
                if (x is SkinnedMeshRenderer sk) { mesh = new Mesh(); sk.BakeMesh(mesh); m = Matrix4x4.TRS(x.transform.position, x.transform.rotation, Vector3.one); }
                else if (x.TryGetComponent<MeshFilter>(out var mf)) mesh = mf.sharedMesh;
                if (mesh == null) continue;
                foreach (var v in mesh.vertices)
                {
                    var w = m.MultiplyPoint3x4(v); var p = space != null ? space.InverseTransformPoint(w) : w;
                    if (b == null) b = new Bounds(p, Vector3.zero); else { var e = b.Value; e.Encapsulate(p); b = e; }
                }
            }
            return b;
        }

        /// <summary>只屬於這個剛體段（Base／LowerArm／UpperArm／PanHead／Rod*）的網格（不屬於更深一層的段）。和 Editor 的 PanGeometry.OwnRenderers 同一套規則（XR 模組不引用 Editor）。</summary>
        public static List<Renderer> JointRenderers(Transform root, string joint)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            var j = all.FirstOrDefault(t => t.name == joint);
            if (j == null) return new List<Renderer>();
            var deeper = JointNames.Select(n => all.FirstOrDefault(t => t.name == n)).Concat(all.Where(t => t.name.StartsWith("Rod"))).Where(o => o != null && o != j && o.IsChildOf(j)).ToList();
            return j.GetComponentsInChildren<Renderer>(true).Where(x => !(x is LineRenderer) && deeper.All(o => !x.transform.IsChildOf(o))).ToList();
        }

        /// <summary>點到矩形（XZ）的水平距離（在裡面＝0）。</summary>
        static float RectDistance(Rect rc, Vector2 p) => new Vector2(Mathf.Max(rc.xMin - p.x, 0f, p.x - rc.xMax), Mathf.Max(rc.yMin - p.y, 0f, p.y - rc.yMax)).magnitude;
        static Rect RectXZ(Bounds b) => Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
        static float RectGap(Rect a, Rect b) => new Vector2(Mathf.Max(a.xMin - b.xMax, 0f, b.xMin - a.xMax), Mathf.Max(a.yMin - b.yMax, 0f, b.yMin - a.yMax)).magnitude;

        /// <summary>線段 a→b（XZ）有沒有穿過矩形。</summary>
        static bool SegmentHitsRect(Vector2 a, Vector2 b, Rect rc)
        {
            float t0 = 0f, t1 = 1f; var dir = b - a;
            for (int k = 0; k < 2; k++)
            {
                float p = k == 0 ? a.x : a.y, dv = k == 0 ? dir.x : dir.y, lo = k == 0 ? rc.xMin : rc.yMin, hi = k == 0 ? rc.xMax : rc.yMax;
                if (Mathf.Abs(dv) < 1e-6f) { if (p < lo || p > hi) return false; continue; }
                float ta = (lo - p) / dv, tb = (hi - p) / dv;
                if (ta > tb) (ta, tb) = (tb, ta);
                t0 = Mathf.Max(t0, ta); t1 = Mathf.Min(t1, tb);
                if (t0 > t1) return false;
            }
            return true;
        }

        static bool OnWalkable(Vector3 p) => Physics.Raycast(new Vector3(p.x, p.y + 1f, p.z), Vector3.down, 1.5f, SceneLayers.WalkableMask, QueryTriggerInteraction.Ignore);

        /// <summary>站點到 Walkable 邊的距離是否 ≥ r：半徑 r 的圓周上 16 個點都有 Walkable。</summary>
        static bool WalkableAround(Vector3 p, float r)
        {
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f;
                if (!OnWalkable(p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r))) return false;
            }
            return OnWalkable(p);
        }

        static float FacingAngle(Transform station, Vector3 target) { var dir = target - station.position; dir.y = 0f; return Vector3.Angle(station.forward, dir); }

        // ---------- 規則 ----------

        /// <summary>terry 186（RA-25、RA-26）：每個站點的站姿頭（跪姿站點用跪姿眼高）、彎腰頭（往朝向前 bowForward、下 bowDown）、
        /// 前傾拔插頭（leanChecks）、頭伸到檯面（箱頂）上方 aboveTopCheck（topChecks）都不能碰到 HeadSolid（含 headSolidRadius）；駕駛室不判範圍內的不算。</summary>
        static void A6HeadSolid(List<StationDef> st, System.Func<StationDef, Transform> tf, SceneResult r)
        {
            var def = HeadSolidDef.Load(); var posture = PostureDef.Load();
            if (def == null || posture == null || LayerMask.NameToLayer(SceneLayers.HeadSolid) < 0) return;
            foreach (var s in st)
            {
                var t = tf(s); var p = t.position; var f = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
                var stand = p + Vector3.up * posture.Eye(s.posture);
                var heads = new List<(string, Vector3)> { (s.posture == Posture.Kneel ? "跪姿" : s.posture == Posture.Crouch ? "蹲姿" : "站姿", stand) };
                if (!(def.bowSkips ?? new HeadSolidDef.BowSkip[0]).Any(x => x.stationId == s.id)) heads.Add(("彎腰", stand + f * def.bowForward + Vector3.down * def.bowDown));
                foreach (var l in (def.leanChecks ?? new HeadSolidDef.LeanCheck[0]).Where(x => x.stationId == s.id)) heads.Add(("前傾", new Vector3(l.headX, stand.y - def.bowDown, p.z)));
                foreach (var tc in (def.topChecks ?? new HeadSolidDef.TopCheck[0]).Where(x => s.id.StartsWith(x.stationPrefix)))
                    foreach (var top in TopPoints(tc.objectName, p, def)) heads.Add(("檯面上方", top));
                foreach (var (label, h) in heads) Pend(r, "24", $"{s.id} {label}頭 {P3(h)} 沒有穿進實心物體", !HeadInSolid(h, def), s.id);
            }
        }

        /// <summary>頭（含半徑）碰到 HeadSolid；駕駛室等不判範圍內不算。</summary>
        public static bool HeadInSolid(Vector3 head, HeadSolidDef def) => !def.Excluded(head) && Physics.CheckSphere(head, def.headSolidRadius, SceneLayers.HeadSolidMask, QueryTriggerInteraction.Collide);

        /// <summary>物件頂面上方 aboveTopCheck 的點：離站點最近的那一點（往內縮 0.05）。物件找不到（教學箱 FBX 未交付）就沒有。</summary>
        public static IEnumerable<Vector3> TopPoints(string objectName, Vector3 station, HeadSolidDef def)
        {
            foreach (var o in Resources.FindObjectsOfTypeAll<Transform>().Where(x => x.gameObject.scene.IsValid() && HeadSolidDef.Wild(x.name, objectName)))
            {
                var cols = o.GetComponentsInChildren<Collider>(true).Where(c => c.gameObject.layer == LayerMask.NameToLayer(SceneLayers.HeadSolid)).ToList();
                if (cols.Count == 0) continue;
                var b = cols.Select(c => c.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                float x0 = Mathf.Clamp(station.x, b.min.x + 0.05f, b.max.x - 0.05f), z0 = Mathf.Clamp(station.z, b.min.z + 0.05f, b.max.z - 0.05f);
                yield return new Vector3(x0, b.max.y + def.aboveTopCheck, z0);
            }
        }

        /// <summary>terry 175B：地面站點不能在檢修坑範圍內（|X| ≤ pitHalfWidth）。</summary>
        static void A6GroundNotOverPit(RoofSceneDef d, List<StationDef> st, System.Func<StationDef, Vector3> pos, SceneResult r)
        {
            foreach (var s in st.Where(x => x.floor == StationFloor.Ground))
            {
                var p = pos(s);
                Pend(r, "23", $"{s.id} X {p.x:0.00} 不在檢修坑上（|X| > {d.pitHalfWidth}）", Mathf.Abs(p.x) > d.pitHalfWidth + Eps);
            }
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
                    Pend(r, "1", $"{a.id}–{b.id} 水平 {dist:0.00} m（≥ {set.platformStationMinSpacing}）", dist >= set.platformStationMinSpacing - Eps);
                }
        }

        static (Vector2 a, Vector2 b) GateSegment(RoofSceneDef d)
        {
            var gate = GameObject.Find("GateBoundary")?.GetComponent<Collider>();
            if (gate != null && gate.enabled) { var b = gate.bounds; return (new Vector2(b.center.x, b.min.z), new Vector2(b.center.x, b.max.z)); }
            return (new Vector2(d.PlatformInnerX, d.gateCenterZ - d.gateWidth / 2f), new Vector2(d.PlatformInnerX, d.gateCenterZ + d.gateWidth / 2f));   // 欄開著（collider 停用）時用資料
        }

        /// <summary>第2項：站點離活動欄開口水平 ≥ gateFrontClearance。</summary>
        static void A6GateFront(StationSet set, RoofSceneDef d, List<StationDef> st, System.Func<StationDef, Vector3> pos, SceneResult r)
        {
            var (g0, g1) = GateSegment(d);
            int n = 0;
            foreach (var s in st)
            {
                float dist = CorridorDistance(g0, g1, 0f, XZ(pos(s)));
                if (dist < set.gateFrontClearance - Eps) { n++; Pend(r, "2", $"{s.id} 離活動欄開口 Z {g0.y:0.00}…{g1.y:0.00} 水平 {dist:0.00} m（≥ {set.gateFrontClearance}）", false); }
            }
            if (n == 0) Pend(r, "2", $"活動欄開口 Z {g0.y:0.00}…{g1.y:0.00} 前 {set.gateFrontClearance} m 內沒有站點", true);
        }

        /// <summary>第4項(d)（D25）：env.pole_rack 到 tp.platform.ground 水平 ≤ poleRackGroundMaxDistance。桿架不是站點。</summary>
        static void A6PoleRack(StationSet set, IDictionary<string, XrTeleportPoint> pts, Transform rack, SceneResult r)
        {
            if (rack == null) { Miss(r, "4d", "env.pole_rack"); return; }
            if (!pts.TryGetValue("tp.platform.ground", out var g)) { Miss(r, "4d", "tp.platform.ground"); return; }
            var c = Center(rack);
            float dist = Flat(c, g.transform.position);
            var anchor = GameObject.Find("Anchor_PoleRack")?.transform ?? rack;
            Pend(r, "4d", $"env.pole_rack 中心 {P2(XZ(c))}（錨點 Anchor_PoleRack {P2(XZ(anchor.position))}）到 tp.platform.ground 水平 {dist:0.00} m（≤ {set.poleRackGroundMaxDistance}）", dist <= set.poleRackGroundMaxDistance + Eps);
        }

        static (Vector2 a, Vector2 b) LiftPath(RoofSceneDef d) => (Vector2.zero, d.standCenterXZ);

        /// <summary>
        /// 第5項（第七節 1，鐵道 2026-11-02 確認）：吊運路徑 ±liftCorridorHalfWidth 只查吊物懸空時到得了的站點（allowedWhileLifted）與 NPC 吊運中站位；
        /// 其他站點在路徑內只列出「吊運時封閉」（TeleportRule：吊物懸空時擋傳送），不算不合格。
        /// </summary>
        static void A6LiftCorridor(StationSet set, RoofSceneDef d, List<StationDef> st, System.Func<StationDef, Vector3> pos, SceneResult r)
        {
            var (a, b) = LiftPath(d); float w = set.liftCorridorHalfWidth;
            var zone = Rect.MinMaxRect(set.liftZoneMinXZ.x, set.liftZoneMinXZ.y, set.liftZoneMaxXZ.x, set.liftZoneMaxXZ.y);
            foreach (var s in st)
            {
                var p = XZ(pos(s));
                float dist = CorridorDistance(a, b, w, p);
                if (s.allowedWhileLifted)
                    Pend(r, "5", $"{s.id} {P2(p)}（吊運時可到）離吊運路徑 ±{w} m {dist:0.00} m、{(zone.Contains(p) ? "在" : "不在")}吊物禁區內", dist > 0f && !zone.Contains(p));
                else if (dist <= 0f) Note(r, "5", $"{s.id} {P2(p)} 在吊運路徑內，吊運時封閉（吊物懸空時擋傳送）");
            }
            foreach (var n in (set.npcPoses ?? new NpcStandPose[0]).Where(x => x.phase == NpcPhase.Lifting))
            {
                var p = XZ(n.position);
                float dist = CorridorDistance(a, b, w, p);
                Pend(r, "5", $"{n.npcId}（吊運中）{P2(p)} 離吊運路徑 ±{w} m {dist:0.00} m、{(zone.Contains(p) ? "在" : "不在")}吊物禁區內", dist > 0f && !zone.Contains(p));
            }
        }

        /// <summary>第七節 1（越障驗算，鐵道）：吊鉤在最高點時，吊物底面（LoweredTightBounds 的底）≥ 路徑下方各固定物頂面＋liftObstacleMargin。</summary>
        static void A6LiftObstacles(StationSet set, RoofSceneDef d, Transform root, Bounds? tight, SceneResult r)
        {
            if (root == null || tight == null) { Miss(r, "5越障", "PAN_Root／LoweredTightBounds"); return; }
            var hookTarget = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Hook_AttachPoint_Target");
            var hook = GameObject.Find("Anchor_Hook_AttachPoint");
            if (hookTarget == null || hook == null) { Miss(r, "5越障", "Hook_AttachPoint_Target／天車吊鉤 Anchor_Hook_AttachPoint"); return; }
            float bottom = hook.transform.position.y - (root.InverseTransformPoint(hookTarget.position).y - tight.Value.min.y);
            Note(r, "5越障", $"吊鉤最高點 Y {hook.transform.position.y:0.000} → 吊物底面 Y {bottom:0.000}（掛點在 LoweredTightBounds 底面上 {root.InverseTransformPoint(hookTarget.position).y - tight.Value.min.y:0.000} m）");
            // D40：吊鉤最高點只有一個數值（CraneProfile.hookYMax，建置時量測寫回、場景的吊鉤就放在那裡），這裡不再和另一個規格值比對；間距 ≥ hookTopClearance 由建置檢查與 CraneHookTopTests 保證
            Pend(r, "5越障", $"吊鉤在上限位時吊物底面 Y {bottom:0.000}（≥ {set.liftBottomMinY}，鐵道 6.1）", bottom >= set.liftBottomMinY - Eps);
            var tops = new List<(string, float)>();
            void Top(string label, IEnumerable<Transform> ts)
            {
                var list = ts.Where(t => t != null).ToList();
                if (list.Count == 0) { Miss(r, "5越障", label); return; }
                float y = list.Select(t => RenderBounds(t, true) ?? (t.TryGetComponent<Collider>(out var c) ? c.bounds : new Bounds(t.position, Vector3.zero))).Max(b => b.max.y);
                tops.Add((label, y));
            }
            var all = Object.FindObjectsOfType<Transform>(true);
            Top("防墜器門架頂", all.Where(t => t.name == "FallArrester_Post" || t.name == "FallArrester_Arm" || t.name == "FallArrester"));
            Top("桿架上直立的長桿頂", all.Where(t => t.name == "InsulatedPole"));
            Top("平台護欄", all.Where(t => t.name.StartsWith("Rail_") && t.TryGetComponent<Collider>(out _)));
            Top("PendantDock", all.Where(t => t.name == "PendantDock"));
            foreach (var (label, y) in tops)
                Pend(r, "5越障", $"{label} 頂面 Y {y:0.000}，餘裕 {bottom - y:0.000} m（≥ {set.liftObstacleMargin}）", bottom - y >= set.liftObstacleMargin - Eps);
        }

        /// <summary>台架四邊的站點 tp.stand.n／e／s／w（v3.10 的 tp.stand.rig_e／rig_w 是空吊梁查看站，面向查看位置，不算）。</summary>
        public static bool IsStandEdge(string id) => id.StartsWith("tp.stand.") && !id.StartsWith("tp.stand.rig_");

        /// <summary>第6項：tp.stand.* 在維修區內、Y＝groundY ±容差、離台架邊 standStationOffset ±容差、面向台架、身體膠囊不碰放上台架的降弓整組（LoweredTightBounds）。</summary>
        static void A6StandStations(StationSet set, RoofSceneDef d, List<StationDef> st, System.Func<StationDef, Transform> tf, Bounds? onStand, SceneResult r)
        {
            var stands = st.Where(s => IsStandEdge(s.id)).ToList();
            if (stands.Count == 0) { Miss(r, "6", "tp.stand.* 站點"); return; }
            var c = d.StandFrameCenterXZ; var half = d.standSizeXZ / 2f;
            if (onStand == null) Miss(r, "6", "LoweredTightBounds（PAN_Root 的 BoxCollider）");
            foreach (var s in stands)
            {
                var t = tf(s); var p = t.position;
                bool inZone = p.x >= d.maintMinXZ.x && p.x <= d.maintMaxXZ.x && p.z >= d.maintMinXZ.y && p.z <= d.maintMaxXZ.y;
                Pend(r, "6", $"{s.id} {P2(XZ(p))} 在維修區 X {d.maintMinXZ.x}…{d.maintMaxXZ.x}、Z {d.maintMinXZ.y}…{d.maintMaxXZ.y}", inZone);
                float dy = Mathf.Abs(p.y - d.groundY);
                Pend(r, "6", $"{s.id} Y {p.y:0.000}，和地面 {d.groundY} 差 {dy:0.000} m（≤ {set.standStationYTolerance}）", dy <= set.standStationYTolerance + Eps);
                float edge = new Vector2(Mathf.Max(Mathf.Abs(p.x - c.x) - half.x, 0f), Mathf.Max(Mathf.Abs(p.z - c.y) - half.y, 0f)).magnitude;
                Pend(r, "6", $"{s.id} 離台架邊 {edge:0.00} m（{d.standStationOffset} ±{set.standStationOffsetTolerance}）", Mathf.Abs(edge - d.standStationOffset) <= set.standStationOffsetTolerance + Eps);
                float ang = Vector3.Angle(t.forward, new Vector3(c.x - p.x, 0f, c.y - p.z));
                Pend(r, "6", $"{s.id} 面向台架偏 {ang:0.0}°（≤ {set.facingToleranceDeg}°）", ang <= set.facingToleranceDeg);
                if (onStand == null) continue;
                var ob = onStand.Value;
                bool vOverlap = ob.min.y < p.y + 1.8f && ob.max.y > p.y + 0.1f;   // 身體膠囊 0.1–1.8 m，和 CheckScene 相同
                float hd = Flat(p, ob.ClosestPoint(new Vector3(p.x, ob.center.y, p.z)));
                Pend(r, "6", $"{s.id} 離台架上的降弓整組 水平 {hd:0.00} m（≥ {set.clearRadius}）", !vOverlap || hd >= set.clearRadius - Eps);
            }
        }

        /// <summary>第7項(b)：車頂站點不在母線帶（外擴 obstacleMargin）、絕緣子孔上。</summary>
        static void A6RoofObstacles(RoofSceneDef d, List<StationDef> st, System.Func<StationDef, Vector3> pos, SceneResult r)
        {
            var roof = st.Where(s => s.floor == StationFloor.Roof).ToList();
            if (roof.Count == 0) { Miss(r, "7b", "車頂站點"); return; }
            var bus = BusbarRect(d, d.obstacleMargin);
            var ins = Insulators();   // 絕緣子實際頂點範圍（不再抄 0.085 的常數）
            foreach (var s in roof)
            {
                var p = XZ(pos(s));
                var hits = new List<string>();
                if (bus.Contains(p)) hits.Add($"母線帶 X {bus.xMin:0.00}…{bus.xMax:0.00}");
                for (int i = 0; i < ins.Count; i++)
                {
                    float dd = RectDistance(RectXZ(ins[i]), p);
                    if (dd < d.obstacleMargin) hits.Add($"絕緣子 {i + 1}（{dd:0.00} m < {d.obstacleMargin}）");
                }
                Pend(r, "7b", $"{s.id} {P2(p)} " + (hits.Count == 0 ? "不在母線帶、絕緣子孔上" : "落在 " + string.Join("、", hits)), hits.Count == 0);
            }
        }

        /// <summary>第8項（增補 3，D34）：車體接地座在外肩：到 tp.roof.base_w ≤ carBodyGroundMaxDistance、X ≤ carBodyGroundMaxX、不在 Walkable 上、座面不碰任何站點的立足圓與跪姿區。</summary>
        static void A6CarBodyGround(StationSet set, RoofSceneDef d, List<StationDef> st, System.Func<StationDef, Transform> tf, Transform clamp, SceneResult r)
        {
            if (clamp == null) { Miss(r, "8", "車體接地座 ip.roof.carbody_ground"); return; }
            var p = clamp.position;
            Note(r, "8", $"車體接地座 Clamp_Point {P3(p)}（車頂斷面在 X {p.x:0.00} 的高度 {d.RoofSurfaceY(p.x):0.000}）");
            var bw = st.FirstOrDefault(s => s.id == "tp.roof.base_w");
            if (bw == null) Miss(r, "8", "tp.roof.base_w");
            else
            {
                float dist = Vector2.Distance(XZ(p), XZ(tf(bw).position));
                Pend(r, "8", $"車體接地座到 tp.roof.base_w {P2(XZ(tf(bw).position))} {dist:0.00} m（≤ {set.carBodyGroundMaxDistance}）", dist <= set.carBodyGroundMaxDistance + Eps);
            }
            Pend(r, "8", $"車體接地座 X {p.x:0.000}（≤ {set.carBodyGroundMaxX}）", p.x <= set.carBodyGroundMaxX + Eps);
            Pend(r, "8", $"車體接地座正下方{(OnWalkable(p) ? "有" : "沒有")} Walkable", !OnWalkable(p));
            float h = d.carBodyGroundPadSize / 2f;
            var pad = Rect.MinMaxRect(p.x - h, p.z - h, p.x + h, p.z + h);
            foreach (var s in st.Where(x => x.floor == StationFloor.Roof))
            {
                var t = tf(s).position; var fw = tf(s).forward; var f = new Vector2(fw.x, fw.z).normalized;   // 傳送點的實際朝向（面向目標的站點沒有 facingDirection）
                float foot = RectDistance(pad, XZ(t));
                // 跪姿區：站點面向方向往前 kneelZoneDepth、半寬 kneelZoneHalfWidth 的長方形（用中線距離近似）
                var a = XZ(t); var b = a + f * set.kneelZoneDepth;
                bool kneel = SegmentHitsRect(a, b, Rect.MinMaxRect(pad.xMin - set.kneelZoneHalfWidth, pad.yMin - set.kneelZoneHalfWidth, pad.xMax + set.kneelZoneHalfWidth, pad.yMax + set.kneelZoneHalfWidth));
                if (foot < set.stationFootRadius || kneel) Pend(r, "8", $"車體接地座碰到 {s.id} 的{(foot < set.stationFootRadius ? $"立足圓（{foot:0.00} m < {set.stationFootRadius}）" : "跪姿區")}", false);
            }
        }

        /// <summary>第9項：base_w 面向 +X、base_e 面向 −X；回報兩站到同側吊耳、控制線接頭、固定帶扣座的角度（不在 ±30° 內的只列出，鐵道裁定）。</summary>
        static void A6RoofBase(StationSet set, IDictionary<string, XrTeleportPoint> pts, System.Func<string, Transform> F, SceneResult r)
        {
            foreach (var (id, face, lugs) in new[] { ("tp.roof.base_w", Vector3.right, new[] { 1, 4 }), ("tp.roof.base_e", Vector3.left, new[] { 2, 3 }) })
            {
                if (!pts.TryGetValue(id, out var tp)) { Miss(r, "9", id); continue; }
                float ang = Vector3.Angle(tp.transform.forward, face);
                Pend(r, "9", $"{id} {P3(tp.transform.position)} 朝向偏 {(face.x > 0 ? "+X" : "−X")} {ang:0.0}°（≤ 1°）", ang <= 1f);
                foreach (int n in new[] { 1, 2, 3, 4 })
                    if (F("ip.pan_assy.lift_point_" + n) is Transform lp)
                    {
                        float a = FacingAngle(tp.transform, lp.position);
                        Note(r, "9", $"{id} → 吊耳 {n} {P2(XZ(lp.position))}：{Flat(tp.transform.position, lp.position):0.00} m、偏 {a:0.0}°{(lugs.Contains(n) ? (a <= set.facingToleranceDeg ? "（同側，在 ±30° 內）" : "（同側，不在 ±30° 內）") : "（對側）")}");
                    }
                foreach (var other in new[] { "ip.pan_assy.ctrl_connector", "HoldDown_BuckleBase_A", "HoldDown_BuckleBase_B" })
                    if (F(other) is Transform o) Note(r, "9", $"{id} → {other}：{Flat(tp.transform.position, o.position):0.00} m、偏 {FacingAngle(tp.transform, o.position):0.0}°");
            }
        }

        /// <summary>第10項(b)：鋼梯 Z 下限 ≥ stairMinZ − stairOverlapTolerance；鋼梯和扶手伸進平台上方的深度 ≤ stairOverlapTolerance（接口碰到平台護欄是正常的）。</summary>
        static void A6Stair(StationSet set, RoofSceneDef d, GameObject stair, SceneResult r)
        {
            var rs = stair != null ? stair.GetComponentsInChildren<Renderer>() : new Renderer[0];
            if (rs.Length == 0) { Miss(r, "10b", "ENV_PlatformStair（或它的 Renderer）"); return; }
            var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
            float tol = set.stairOverlapTolerance;
            Pend(r, "10b", $"ENV_PlatformStair Z {b.min.z:0.000}…{b.max.z:0.000}（{set.stairMinZ - tol:0.00}…{set.stairMaxZ}）", b.min.z >= set.stairMinZ - tol - Eps && b.max.z <= set.stairMaxZ + Eps);
            float depth = 0f;
            foreach (var x in rs)
            {
                var e = x.bounds;
                if (e.max.y <= d.platformFloorY || e.min.x >= d.PlatformInnerX || e.max.x <= d.PlatformOuterX) continue;
                depth = Mathf.Max(depth, d.platformZMax - e.min.z);
            }
            Pend(r, "10b", $"ENV_PlatformStair 伸進平台上方 {Mathf.Max(0f, depth):0.000} m（≤ {tol}）", depth <= tol + Eps);
        }

        /// <summary>第11項（G1 C1）：降弓整組放上台架（PAN_Root 原點在台架中心支撐座頂面）後，底架以外的頂點最低點到台架外框頂面 ≥ standFrameMinClearance。</summary>
        static void A6StandClearance(StationSet set, RoofSceneDef d, Transform root, Vector3 standOrigin, SceneResult r)
        {
            if (root == null) { Miss(r, "11", PanRootName); return; }
            var baseOwn = new HashSet<Renderer>(JointRenderers(root, "Base"));
            var pBase = root.Find("PAN_Base");
            var rs = root.GetComponentsInChildren<Renderer>().Where(x => !(x is LineRenderer) && !baseOwn.Contains(x) && (pBase == null || !x.transform.IsChildOf(pBase))).ToList();
            if (rs.Count == 0) { Miss(r, "11", "PAN_Root 底下的 Renderer"); return; }
            var low = rs.Select(x => (x, b: VertexBounds(new[] { x }, root))).Where(x => x.b.HasValue).OrderBy(x => x.b.Value.min.y).First();
            float lowest = standOrigin.y + low.b.Value.min.y;
            float frameTop = d.groundY + d.standFrameTopAboveGround, gap = lowest - frameTop;
            Pend(r, "11", $"台架上的降弓整組（底架除外，頂點算法）最低 Y {lowest:0.000}（{low.x.name}），到外框頂面 {frameTop:0.000} 垂直 {gap:0.000} m（≥ {set.standFrameMinClearance}）", gap >= set.standFrameMinClearance - Eps);
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

        /// <summary>第13項：活動欄開口中心到防墜器掛鉤、桿架、工具架的水平距離都 ≤ roofEntryMaxDistance。</summary>
        static void A6RoofEntry(StationSet set, RoofSceneDef d, System.Func<string, Transform> F, SceneResult r)
        {
            var (g0, g1) = GateSegment(d); var gc = (g0 + g1) / 2f;
            foreach (var (id, label) in new[] { ("ip.roof.anchor", "防墜器掛鉤"), ("env.pole_rack", "桿架"), ("env.tool_rack", "工具架") })
            {
                var t = F(id);
                if (t == null) { Miss(r, "13", id); continue; }
                var c = id == "ip.roof.anchor" ? t.position : Center(t);
                float dist = Vector2.Distance(gc, XZ(c));
                Pend(r, "13", $"活動欄開口中心 {P2(gc)} 到{label} {P2(XZ(c))} {dist:0.00} m（≤ {set.roofEntryMaxDistance}）", dist <= set.roofEntryMaxDistance + Eps);
            }
        }

        static Vector3? ToCabMarker() => (GameObject.Find("mk.platform.to_cab") ?? GameObject.Find("Anchor_ToCabMarker"))?.transform.position;

        /// <summary>第14項：去駕駛室光圈離 tp.platform.lift ≤ toCabLiftMaxDistance，而且在 Walkable 上（不受第1項站點間距限制）。</summary>
        static void A6ToCab(StationSet set, RoofSceneDef d, IDictionary<string, XrTeleportPoint> pts, SceneResult r)
        {
            var mk = ToCabMarker();
            if (mk == null) { Miss(r, "14", "mk.platform.to_cab"); return; }
            if (!pts.TryGetValue("tp.platform.lift", out var lift)) { Miss(r, "14", "tp.platform.lift"); return; }
            float dist = Flat(mk.Value, lift.transform.position);
            Pend(r, "14", $"mk.platform.to_cab {P3(mk.Value)} 到 tp.platform.lift {dist:0.00} m（≤ {set.toCabLiftMaxDistance}）", dist <= set.toCabLiftMaxDistance + Eps);
            Pend(r, "14", $"mk.platform.to_cab 正下方{(OnWalkable(mk.Value) ? "有" : "沒有")} Walkable", OnWalkable(mk.Value));
        }

        /// <summary>
        /// 第15項：NPC 站位依階段檢查（NPC 本身 R1.5b 才建）。平時：到平台設備與所有平台站點 ≥ npcClearance、不在活動欄開口前 npcGateFront 內。
        /// 吊運中：不在吊物禁區與吊運路徑兩側 liftCorridorHalfWidth 內（第5項已查），三人彼此、到工作檯、台架四周淨空帶、鋼梯都 ≥ npcClearance。不合格照實列出，不改座標。
        /// </summary>
        static void A6Npc(StationSet set, RoofSceneDef d, List<StationDef> st, System.Func<StationDef, Vector3> pos, System.Func<string, Transform> F, SceneResult r)
        {
            var poses = set.npcPoses ?? new NpcStandPose[0];
            if (poses.Length == 0) { Miss(r, "15", "NPC 站位資料（StationSet.npcPoses）"); return; }
            float c = set.npcClearance;
            var normalItems = new List<(string, Rect)>();
            void Item(string label, Transform t) { var b = t != null ? RenderBounds(t, true) : null; if (b == null) Miss(r, "15", label); else normalItems.Add((label, RectXZ(b.Value))); }
            Item("工具架", F("env.tool_rack")); Item("桿架", F("env.pole_rack")); Item("升降盒", GameObject.Find("LiftControlBox")?.transform); Item("PendantDock", GameObject.Find("PendantDock")?.transform);
            if (ToCabMarker() is Vector3 mk) normalItems.Add(("去駕駛室光圈", new Rect(mk.x - d.toCabMarkerRadius, mk.z - d.toCabMarkerRadius, 2 * d.toCabMarkerRadius, 2 * d.toCabMarkerRadius)));
            float hl = d.dailyRackLength / 2f;
            normalItems.Add(("備品架（日常擺放點）", Rect.MinMaxRect(d.spareShelfDaily.x - 0.2f, d.spareShelfDaily.z - hl, d.spareShelfDaily.x + 0.2f, d.spareShelfDaily.z + hl)));
            normalItems.Add(("待報廢箱（日常擺放點）", Rect.MinMaxRect(d.scrapBoxDaily.x - 0.2f, d.scrapBoxDaily.z - hl, d.scrapBoxDaily.x + 0.2f, d.scrapBoxDaily.z + hl)));
            var (g0, g1) = GateSegment(d);
            foreach (var n in poses.Where(x => x.phase == NpcPhase.Normal))
            {
                var p = XZ(n.position);
                var near = normalItems.Select(x => (x.Item1, dd: RectDistance(x.Item2, p))).OrderBy(x => x.dd).First();
                Pend(r, "15", $"{n.npcId}（平時）{P2(p)} 最近的設備 {near.Item1} {near.dd:0.00} m（≥ {c}）", near.dd >= c - Eps, n.npcId);
                foreach (var x in normalItems.Where(x => RectDistance(x.Item2, p) < c - Eps && x.Item1 != near.Item1)) Pend(r, "15", $"{n.npcId}（平時）到 {x.Item1} {RectDistance(x.Item2, p):0.00} m（≥ {c}）", false, n.npcId);
                var ps = st.Where(s => s.floor == StationFloor.Platform).Select(s => (s.id, dd: Vector2.Distance(p, XZ(pos(s))))).OrderBy(x => x.dd).First();
                Pend(r, "15", $"{n.npcId}（平時）最近的平台站點 {ps.id} {ps.dd:0.00} m（≥ {c}）", ps.dd >= c - Eps, n.npcId);
                float gd = CorridorDistance(g0, g1, 0f, p);
                Pend(r, "15", $"{n.npcId}（平時）離活動欄開口 {gd:0.00} m（≥ {set.npcGateFront}）", gd >= set.npcGateFront - Eps, n.npcId);
            }
            var lifting = poses.Where(x => x.phase == NpcPhase.Lifting).ToList();
            for (int i = 0; i < lifting.Count; i++)
                for (int j = i + 1; j < lifting.Count; j++)
                {
                    float dd = Vector2.Distance(XZ(lifting[i].position), XZ(lifting[j].position));
                    Pend(r, "15", $"{lifting[i].npcId}–{lifting[j].npcId}（吊運中）相距 {dd:0.00} m（≥ {c}）", dd >= c - Eps, lifting[i].npcId);
                }
            var liftItems = new List<(string, Rect)>();
            if (GameObject.Find("BenchBody")?.GetComponent<Collider>() is Collider bench) liftItems.Add(("工作檯", RectXZ(bench.bounds))); else Miss(r, "15", "工作檯 BenchBody");
            var sc = d.StandFrameCenterXZ; var band = d.standSizeXZ / 2f + Vector2.one * d.standClearance;
            liftItems.Add(("台架四周淨空帶", Rect.MinMaxRect(sc.x - band.x, sc.y - band.y, sc.x + band.x, sc.y + band.y)));
            if (RenderBounds(GameObject.Find("ENV_PlatformStair")?.transform) is Bounds sb) liftItems.Add(("鋼梯", RectXZ(sb))); else Miss(r, "15", "鋼梯");
            foreach (var n in lifting)
                foreach (var (label, rc) in liftItems)
                {
                    float dd = RectDistance(rc, XZ(n.position));
                    Pend(r, "15", $"{n.npcId}（吊運中）{P2(XZ(n.position))} 到{label} {dd:0.00} m（≥ {c}）", dd >= c - Eps, n.npcId);
                }
        }

        /// <summary>底座螺栓子站點：StationDef.targetId 是 part.pan_base.bolt_n 的車頂站點（增補 7 E3(4)，不再寫對照表）。n＝目標 ID 最後的數字（吊耳同號）。</summary>
        static List<(StationDef s, string bolt, int n)> BoltStations(StationSet set) =>
            (set.stations ?? new StationDef[0]).Where(s => s != null && s.floor == StationFloor.Roof && !string.IsNullOrEmpty(s.targetId) && s.targetId.StartsWith("part.pan_base.bolt_"))
                .Select(s => (s, s.targetId, int.TryParse(s.targetId.Substring(s.targetId.LastIndexOf('_') + 1), out var n) ? n : 0)).ToList();

        /// <summary>伸手估計：頭部水平位置從站點往目標移動「超出可及距離」的那一段（reach＝手臂或短絕緣桿的水平可及），回傳頭部 |X| 與水平距離。</summary>
        public static (float headAbsX, float dist) HeadReach(Vector3 station, Vector3 target, float reach)
        {
            var a = XZ(station); var d = XZ(target) - a; float dist = d.magnitude;
            var head = dist > reach ? a + d / dist * (dist - reach) : a;
            return (Mathf.Abs(head.x), dist);
        }

        /// <summary>車頂的絕緣子（Insulator_n 的視覺網格頂點範圍與 InsulatorCollision；增補 7 E3(2) 合成一個 helper）。</summary>
        public static List<Bounds> Insulators(Transform scope = null)
        {
            var all = scope != null ? scope.GetComponentsInChildren<Transform>(true) : Object.FindObjectsOfType<Transform>(true);
            return all.Where(t => t.name.StartsWith("Insulator_") && t.parent != null && t.parent.name == "Visual")
                .Select(t => VertexBounds(t.GetComponentsInChildren<Renderer>(true))).Where(b => b.HasValue).Select(b => b.Value).ToList();
        }
        static List<Collider> InsulatorColliders() => Object.FindObjectsOfType<Collider>(true).Where(c => c.name == "InsulatorCollision").ToList();

        /// <summary>
        /// 第16項（D33／D36 改寫，增補 6 第 7 點、增補 7 E3(4)）：(a) .n 螺栓在正前方 ≤ maxTargetDistance、±30°；(b) .s 斜向 ≤ maxTargetDistance、±30°；(c) 吊耳和螺栓同號、在 ±30° 內；
        /// (d) 高壓端子在 base_e.n 的 ±30° 內、≤ baseECableMaxDistance，base_e 到高壓電纜接地掛點 ≤ baseECableMaxDistance；(e) 六個底架站點都在 Walkable 上、離 Walkable 邊 ≥ invisibleWallClearance；
        /// (f) 車頂站點兩兩 ≥ platformStationMinSpacing；(g) 螺栓 |X| ＜ 站點 |X|；(h) 站點不在降弓羊角頂點範圍外擴 hornBodyRadius 內；(i) 站點到底架、絕緣子頂點範圍 ≥ staticPartClearance；
        /// (j) 從 base_e.n 伸手到控制線接頭、固定帶綁點（手）、高壓電纜接地掛點（短絕緣桿），頭部 |X| ≤ reachHeadMaxAbsX。
        /// </summary>
        static void A6SubStations(StationSet set, RoofSceneDef d, IDictionary<string, XrTeleportPoint> pts, Transform root, System.Func<string, Transform> F, SceneResult r)
        {
            float tol = set.facingToleranceDeg;
            var subs = BoltStations(set);
            if (subs.Count == 0) Miss(r, "16", "targetId＝part.pan_base.bolt_n 的車頂站點");
            foreach (var (def, boltId, n) in subs)
            {
                string id = def.id;
                if (!pts.TryGetValue(id, out var tp)) { Miss(r, "16", id); continue; }
                var bolt = F(boltId); var lug = F("ip.pan_assy.lift_point_" + n);
                if (bolt == null || lug == null) { Miss(r, "16", $"{boltId}／ip.pan_assy.lift_point_{n}"); continue; }
                var p = tp.transform.position;
                float max = def.maxTargetDistance > 0 ? def.maxTargetDistance : set.subStationBoltMax;
                float dist = Flat(p, bolt.position), ang = FacingAngle(tp.transform, bolt.position);
                string k = id.EndsWith(".s") ? "(b)" : "(a)";
                Pend(r, "16", $"{k} {id} {P3(p)} 面向 {P2(XZ(tp.transform.forward))} → 螺栓 {n} {P2(XZ(bolt.position))} {dist:0.000} m（≤ {max}）、偏 {ang:0.0}°（≤ {tol}°）", dist <= max + Eps && ang <= tol, id);
                float la = FacingAngle(tp.transform, lug.position);
                Pend(r, "16", $"(c) {id} → 吊耳 {n} {P2(XZ(lug.position))} {Flat(p, lug.position):0.000} m、偏 {la:0.0}°（≤ {tol}°）", la <= tol, id);
                Pend(r, "16", $"(g) {id} 螺栓 {n} |X| {Mathf.Abs(bolt.position.x):0.000} ＜ 站點 |X| {Mathf.Abs(p.x):0.000}", Mathf.Abs(bolt.position.x) < Mathf.Abs(p.x), id);
            }
            var cable = F("ip.roof.hv_cable_ground");   // 增補 7 E3(3)：只用 ID，找不到就算 Miss
            if (pts.TryGetValue("tp.roof.base_e.n", out var en) && F("ip.pan_assy.hv_terminal") is Transform hv)
            {
                float dd = Flat(en.transform.position, hv.position), a = FacingAngle(en.transform, hv.position);
                Pend(r, "16", $"(d) tp.roof.base_e.n → 高壓端子 {P2(XZ(hv.position))} {dd:0.000} m（≤ {set.baseECableMaxDistance}）、偏 {a:0.0}°（≤ {tol}°）", dd <= set.baseECableMaxDistance + Eps && a <= tol, "tp.roof.base_e.n");
            }
            else Miss(r, "16", "tp.roof.base_e.n／ip.pan_assy.hv_terminal");
            if (cable != null && pts.TryGetValue("tp.roof.base_e", out var be))
            {
                float dd = Flat(be.transform.position, cable.position);
                Pend(r, "16", $"(d) tp.roof.base_e 到 ip.roof.hv_cable_ground {P2(XZ(cable.position))} {dd:0.000} m（≤ {set.baseECableMaxDistance}）", dd <= set.baseECableMaxDistance + Eps, "tp.roof.base_e");
            }
            else Miss(r, "16", "ip.roof.hv_cable_ground／tp.roof.base_e");

            var bases = subs.Select(x => x.s.id).Concat(new[] { "tp.roof.base_w", "tp.roof.base_e" }).Where(pts.ContainsKey).ToList();
            var baseV = root != null ? VertexBounds(JointRenderers(root, "Base")) : null;
            var horn = root != null ? VertexBounds(JointRenderers(root, "PanHead")) : null;
            var insulators = Insulators();
            if (baseV == null || horn == null) Miss(r, "16", "底架／弓頭網格");
            foreach (var id in bases)
            {
                var p = pts[id].transform.position;
                Pend(r, "16", $"(e) {id} 在 Walkable 上、離 Walkable 邊 ≥ {set.invisibleWallClearance} m", WalkableAround(p, set.invisibleWallClearance - set.wallClearanceTolerance), id);
                if (horn.HasValue)
                {
                    float hd = RectDistance(RectXZ(horn.Value), XZ(p));
                    Pend(r, "16", $"(h) {id} 到降弓羊角頂點範圍 X {horn.Value.min.x:0.000}…{horn.Value.max.x:0.000}、Z {horn.Value.min.z:0.000}…{horn.Value.max.z:0.000} {hd:0.000} m（≥ 身體半徑 {set.hornBodyRadius}）", hd >= set.hornBodyRadius - Eps, id);
                }
                if (baseV.HasValue)
                {
                    var near = insulators.Select((b, i) => ($"絕緣子 {i + 1}", RectDistance(RectXZ(b), XZ(p)))).Append(("底架", RectDistance(RectXZ(baseV.Value), XZ(p)))).OrderBy(x => x.Item2).First();
                    Pend(r, "16", $"(i) {id} 到不會動零件最近的 {near.Item1} {near.Item2:0.000} m（≥ {set.staticPartClearance}）", near.Item2 >= set.staticPartClearance - Eps, id);
                }
            }
            var roof = (set.stations ?? new StationDef[0]).Where(s => s != null && s.floor == StationFloor.Roof && pts.ContainsKey(s.id) && string.IsNullOrEmpty(s.sharedWith)).ToList();
            foreach (var id in bases)
                foreach (var o in roof.Where(o => o.id != id && (!bases.Contains(o.id) || string.CompareOrdinal(o.id, id) > 0)))
                {
                    float dd = Flat(pts[id].transform.position, pts[o.id].transform.position);
                    Pend(r, "16", $"(f) {id}–{o.id} {dd:0.000} m（≥ {set.platformStationMinSpacing}）", dd >= set.platformStationMinSpacing - Eps, id);
                }
            // (j)：從 base_e.n 伸手（RM_01、RM_02、IN_10、IN_11、WC_03a a，鐵道 D36 補充四、五）
            if (pts.TryGetValue("tp.roof.base_e.n", out var n3))
                foreach (var (target, label, reach) in new[] { ("ip.pan_assy.ctrl_connector", "控制線接頭（手）", set.armReachHorizontal), ("HoldDown_TiePoint", "降弓固定帶綁點（手）", set.armReachHorizontal),
                                                               ("ip.roof.hv_cable_ground", "高壓電纜接地掛點（短絕緣桿桿頭）", set.shortPoleReachHorizontal) })
                {
                    var t = F(target);
                    if (t == null) { Miss(r, "16", target); continue; }
                    var (hx, dd) = HeadReach(n3.transform.position, t.position, reach);
                    Pend(r, "16", $"(j) tp.roof.base_e.n → {label} 水平 {dd:0.000} m，伸手時頭部 |X| 約 {hx:0.000}（≤ {set.reachHeadMaxAbsX}；可及 {reach} m）", hx <= set.reachHeadMaxAbsX + Eps, "tp.roof.base_e.n");
                }
        }

        /// <summary>增補 7 第 3 點：把手中心高＝螺栓頂點最高點（不是 BaseHole 的 Y）＋該螺栓套筒的 heightM。</summary>
        public static float WrenchHandleY(Transform bolt, SocketDef socket)
        {
            var vb = VertexBounds(bolt.GetComponentsInChildren<Renderer>(true));
            return (vb.HasValue ? vb.Value.max.y : bolt.position.y) + socket.heightM;
        }

        /// <summary>第21項的輸入：要算的扳手（StationSet.wrenchSweepTools 對到的 ToolDefinition）與套筒目錄。</summary>
        public class WrenchSweepInput
        {
            public IList<ToolDefinition> tools = new List<ToolDefinition>();
            public SocketCatalog catalog;
        }

        /// <summary>
        /// 扇形掃過的最小淨空（純計算，給第 21 項與反例測試直接呼叫）：以 bolt 為圓心、高度 y，從 baseDeg 起 fromDeg…toDeg、半徑 radialStep…radius 取樣，
        /// 量到 segs（頂點範圍）與 cols（collider，排除 exclude 底下的）的最近距離。回傳（最小淨空, 最近的名稱, 到 PanHead 的最小距離）。
        /// </summary>
        public static (float best, string what, float toHead) WrenchSweepClearance(Vector3 bolt, float y, float baseDeg, float fromDeg, float toDeg, float radius, float radialStep, float angleStep,
            IList<(string name, Bounds b)> segs, IList<Collider> cols, Transform exclude)
        {
            float best = float.PositiveInfinity, toHead = float.PositiveInfinity; string what = "";
            for (float a = fromDeg; a <= toDeg + Eps; a += angleStep)
                for (float rr = radialStep; rr <= radius + Eps; rr += radialStep)
                {
                    float rad = (baseDeg + a) * Mathf.Deg2Rad;
                    var q = new Vector3(bolt.x + Mathf.Cos(rad) * rr, y, bolt.z + Mathf.Sin(rad) * rr);
                    foreach (var (name, b) in segs) { float dd = Mathf.Sqrt(b.SqrDistance(q)); if (name == "PanHead") toHead = Mathf.Min(toHead, dd); if (dd < best) { best = dd; what = name; } }
                    foreach (var c in cols)
                    {
                        if (c == null || (exclude != null && (c.transform.IsChildOf(exclude) || exclude.IsChildOf(c.transform)))) continue;
                        float dd = Vector3.Distance(c.ClosestPoint(q), q);
                        if (dd < best) { best = dd; what = c.name; }
                    }
                }
            return (best, what, toHead);
        }

        /// <summary>
        /// 第21項（鐵道 D36 補充三、D38、增補 7 第 3、5 點）：每個底座螺栓子站點 × 鎖緊（起始方向往 + 擺）／起鬆（往 − 擺）× StationSet.wrenchSweepTools 的每一支扳手，
        /// 半徑＝那支扳手的 handleLengthM，把手中心高＝螺栓頂點最高點＋這支扳手 sockets[] 裡尺寸＝螺栓 sizeMm 的那顆套筒的 heightM（R1.5b b3）；
        /// 不碰 PAN_Root 底下的實體 collider（該螺栓本身除外）、絕緣子 collider、會動的段降弓頂點範圍。最小淨空 ≥ wrenchSweepMinClearance 才合格。
        /// shortcut: 「LoweredTightBounds」用各段自己的頂點範圍判（整組 AABB 包住底架四周，扇形一定在裡面）；底架本身不算（螺栓就鎖在底架上）, 要更準時改成三角面距離
        /// </summary>
        static void A6Wrench(StationSet set, IDictionary<string, XrTeleportPoint> pts, Transform root, System.Func<string, Transform> F, WrenchSweepInput input, SceneResult r)
        {
            if (input == null || input.catalog == null) { Miss(r, "21", "扳手資料（WrenchSweepInput：ToolDefinition＋SocketCatalog）"); return; }
            if (root == null) { Miss(r, "21", PanRootName); return; }
            var all = root.GetComponentsInChildren<Transform>(true);
            var segs = JointNames.Skip(1).Concat(all.Where(t => t.name.StartsWith("Rod")).Select(t => t.name)).Distinct()
                .Select(n => (n, b: VertexBounds(JointRenderers(root, n)))).Where(x => x.b.HasValue).Select(x => (x.n, x.b.Value)).ToList();
            // 只算實體 collider（互動點的 trigger 不算）；非凸 MeshCollider 不能 ClosestPoint，弓頭本體由 segs 的頂點範圍涵蓋
            var cols = root.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger && !(c is MeshCollider mc && !mc.convex)).Concat(InsulatorColliders()).ToList();
            foreach (var toolId in set.wrenchSweepTools ?? new string[0])
            {
                var tool = input.tools.FirstOrDefault(t => t != null && t.toolId == toolId);
                if (tool == null || tool.handleLengthM <= 0f) { Miss(r, "21", toolId + " 的 ToolDefinition／handleLengthM"); continue; }
                foreach (var (def, boltId, n) in BoltStations(set))
                {
                    var bolt = F(boltId);
                    var sock = input.catalog.ForTool(tool, input.catalog.SizeFor(boltId));
                    if (bolt == null || def.wrenchStartDir.sqrMagnitude < Eps) { Miss(r, "21", def.id + " 的螺栓或 wrenchStartDir"); continue; }
                    if (sock == null) { Miss(r, "21", $"{toolId} 的 sockets[] 裡沒有 {boltId} 要的 {input.catalog.SizeFor(boltId)} mm 套筒"); continue; }
                    float y = WrenchHandleY(bolt, sock);
                    float baseAng = Mathf.Atan2(def.wrenchStartDir.y, def.wrenchStartDir.x) * Mathf.Rad2Deg;
                    foreach (var (dir, from, to) in new[] { ("鎖緊", 0f, set.wrenchSwingDeg), ("起鬆", -set.wrenchSwingDeg, 0f) })
                    {
                        var (best, what, toHead) = WrenchSweepClearance(bolt.position, y, baseAng, from, to, tool.handleLengthM, set.wrenchRadialStep, set.wrenchSampleDeg, segs, cols, bolt);
                        Pend(r, "21", $"{def.id} × {dir} × {toolId}：螺栓 {n}（套筒 {sock.socketId} 高 {sock.heightM}）把手中心 Y {y:0.000}、半徑 {tool.handleLengthM} m，最小淨空 {best:0.000} m（最近 {what}）；到弓頭（含羊角）{toHead:0.000} m（≥ {set.wrenchSweepMinClearance}）",
                            best >= set.wrenchSweepMinClearance - Eps, def.id);
                    }
                }
            }
        }

        /// <summary>
        /// 第22項（增補 7 第 8 點，組長）：活動欄車頂側開口（gz0 到 EdgeWall_-X_GateSlice）淨寬 ≥ gateMinWidth；跨板在開口寬度方向（Z）的範圍完全落在開口內。
        /// 純計算給反例測試：回傳（開口寬, 跨板寬, 跨板兩邊到開口邊的距離, 合格）。
        /// </summary>
        public static (float open, float bridge, float gapMin, float gapMax, bool ok) GateOpening(float openMin, float openMax, float bridgeMin, float bridgeMax, float minWidth)
        {
            float open = openMax - openMin, gMin = bridgeMin - openMin, gMax = openMax - bridgeMax;
            return (open, bridgeMax - bridgeMin, gMin, gMax, open >= minWidth - Eps && gMin >= -Eps && gMax >= -Eps);
        }

        static void A6GateOpening(RoofSceneDef d, SceneResult r)
        {
            var slice = GameObject.Find("EdgeWall_-X_GateSlice")?.GetComponent<Collider>();
            // 跨板在活動欄關著時是隱藏的（Renderer.bounds 會是空的），用它的 Transform（單位方塊縮放）量範圍
            var plate = Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t => t.name == "BridgePlate");
            if (plate == null) { Miss(r, "22", "BridgePlate"); return; }
            var bridge = new Bounds(plate.position, plate.lossyScale);
            float openMin = d.gateCenterZ - d.gateWidth / 2f, openMax = slice != null ? slice.bounds.min.z : d.gateCenterZ + d.gateWidth / 2f;
            var (open, bw, g0, g1, ok) = GateOpening(openMin, openMax, bridge.min.z, bridge.max.z, d.gateMinWidth);
            Pend(r, "22", $"活動欄開口 Z {openMin:0.000}…{openMax:0.000} 淨寬 {open:0.000} m（≥ {d.gateMinWidth}）；跨板 Z {bridge.min.z:0.000}…{bridge.max.z:0.000} 寬 {bw:0.000} m，兩邊到開口邊 {g0:0.000}／{g1:0.000} m（跨板 ⊂ 開口）", ok, "EdgeWall_-X_GateSlice");
            var guard = GameObject.Find("ENV_GateSliceGuard");
            var gr = guard != null ? guard.GetComponentsInChildren<MeshRenderer>().Where(x => x.enabled).ToList() : new List<MeshRenderer>();
            if (slice == null) return;
            if (gr.Count == 0) { Miss(r, "22", "ENV_GateSliceGuard（看得見的擋欄）"); return; }
            var gb = gr[0].bounds; foreach (var x in gr) gb.Encapsulate(x.bounds);
            var sb = slice.bounds;
            float tol = d.gateGuardTolerance;
            bool covers = gb.min.z <= sb.min.z + tol && gb.max.z >= sb.max.z - tol && gb.min.z >= sb.min.z - tol;   // 寬和擋牆差 ≤ 容差、網格不比 collider 更往開口內伸
            Pend(r, "22", $"擋欄 ENV_GateSliceGuard 高 {gb.size.y:0.000}（{d.gateGuardHeight} ± {tol}）、Z {gb.min.z:0.000}…{gb.max.z:0.000}（擋牆 {sb.min.z:0.000}…{sb.max.z:0.000}）", Mathf.Abs(gb.size.y - d.gateGuardHeight) <= tol + Eps && covers, "EdgeWall_-X_GateSlice");
        }

        /// <summary>第17項：暫放墊整塊在 |X| ≤ stripMatMaxAbsX、離最近車頂站點 ≥ stripMatStationMin、不在活動欄開口到各車頂站點的直線動線上（範圍讀資料：物件存檔時是 inactive）。</summary>
        static void A6StripMat(StationSet set, RoofSceneDef d, List<StationDef> st, System.Func<StationDef, Vector3> pos, Transform mat, SceneResult r)
        {
            if (mat == null) { Miss(r, "17", "env.roof_strip_mat"); return; }
            var c = d.roofStripMatCenter; var h = d.roofStripMatSize / 2f;
            var rc = Rect.MinMaxRect(c.x - h.x, c.z - h.y, c.x + h.x, c.z + h.y);
            Pend(r, "17", $"暫放墊 X {rc.xMin:0.00}…{rc.xMax:0.00}、Z {rc.yMin:0.00}…{rc.yMax:0.00} 在 |X| ≤ {set.stripMatMaxAbsX} 內（存檔 {(mat.gameObject.activeSelf ? "active" : "inactive")}）", Mathf.Max(-rc.xMin, rc.xMax) <= set.stripMatMaxAbsX + Eps);
            var roof = st.Where(s => s.floor == StationFloor.Roof).ToList();
            if (roof.Count == 0) { Miss(r, "17", "車頂站點"); return; }
            var near = roof.Select(s => (s.id, dd: RectDistance(rc, XZ(pos(s))))).OrderBy(x => x.dd).First();
            Pend(r, "17", $"暫放墊到最近的車頂站點 {near.id} {near.dd:0.00} m（≥ {set.stripMatStationMin}）", near.dd >= set.stripMatStationMin - Eps);
            var (g0, g1) = GateSegment(d); var gc = (g0 + g1) / 2f;
            var hit = roof.Where(s => SegmentHitsRect(gc, XZ(pos(s)), rc)).Select(s => s.id).ToList();
            Pend(r, "17", hit.Count == 0 ? $"活動欄開口 {P2(gc)} 到各車頂站點的直線動線都不經過暫放墊" : "暫放墊在動線上：" + string.Join("、", hit), hit.Count == 0);
        }

        /// <summary>
        /// 第18項（v4.3、增補 3）：ctrl_connector、hold_down 到 tp.roof.base_e ≤ sideNodeBaseEMax；接頭到 HV_Cable_End ≥ connectorCableMin；兩個節點 PAN_Root 局部 X ＞ 0；
        /// 固定帶的鞍（HoldDown_Saddle）和上臂的頂點範圍相交；兩支腳長 0.05–1.00 m。
        /// </summary>
        static void A6SideNodes(StationSet set, IDictionary<string, XrTeleportPoint> pts, Transform root, System.Func<string, Transform> F, SceneResult r)
        {
            if (root == null) { Miss(r, "18", PanRootName); return; }
            pts.TryGetValue("tp.roof.base_e", out var be);
            if (be == null) Miss(r, "18", "tp.roof.base_e");
            var all = root.GetComponentsInChildren<Transform>(true);
            Transform Node(string n) => all.FirstOrDefault(t => t.name == n);
            foreach (var (id, label) in new[] { ("ip.pan_assy.ctrl_connector", "控制線接頭"), ("ip.pan_assy.hold_down", "降弓固定帶綁點") })
            {
                var t = id == "ip.pan_assy.hold_down" ? Node("HoldDown_TiePoint") : Node(id);
                if (t == null) { Miss(r, "18", id); continue; }
                var local = root.InverseTransformPoint(t.position);
                Pend(r, "18", $"{label} {P3(t.position)}（PAN_Root 局部 X {local.x:0.000} ＞ 0）", local.x > 0f);
                if (be != null) { float dd = Flat(t.position, be.transform.position); Pend(r, "18", $"{label}到 tp.roof.base_e {dd:0.00} m（≤ {set.sideNodeBaseEMax}）", dd <= set.sideNodeBaseEMax + Eps); }
            }
            var cable = F("ip.roof.hv_cable_ground");   // 增補 7 E3(3)：只用 ID
            if (Node("ip.pan_assy.ctrl_connector") is Transform cc && cable != null)
            {
                float dd = Flat(cc.position, cable.position);
                Pend(r, "18", $"控制線接頭到 HV_Cable_End 水平 {dd:0.00} m（≥ {set.connectorCableMin}）", dd >= set.connectorCableMin - Eps);
            }
            else if (cable == null) Miss(r, "18", "ip.roof.hv_cable_ground");
            var saddle = Node("HoldDown_Saddle");
            var arm = VertexBounds(JointRenderers(root, "UpperArm"));
            var sb = saddle != null ? VertexBounds(saddle.GetComponentsInChildren<Renderer>(true)) : null;
            if (sb == null || arm == null) Miss(r, "18", "HoldDown_Saddle 或 UpperArm 的網格");
            else Pend(r, "18", $"HoldDown_Saddle 頂點範圍 {P3(sb.Value.min)}–{P3(sb.Value.max)} 和上臂頂點範圍{(sb.Value.Intersects(arm.Value) ? "相交" : "不相交")}", sb.Value.Intersects(arm.Value));
            foreach (var leg in new[] { "HoldDown_Leg_A", "HoldDown_Leg_B" })
            {
                var t = Node(leg);
                var vb = t != null ? t.GetComponentsInChildren<Renderer>(true).FirstOrDefault() : null;
                if (vb == null || !vb.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) { Miss(r, "18", leg); continue; }
                float len = mf.sharedMesh.vertices.Max(v => Vector3.Distance(t.position, vb.transform.TransformPoint(v)));
                Pend(r, "18", $"{leg} 長 {len:0.000} m（0.05–1.00）", len >= 0.05f - Eps && len <= 1.00f + 0.01f);
            }
        }

        /// <summary>
        /// 第19項（增補 1，D32）：PendantDock 的 Bounds 不在吊物禁區內；到 tp.platform.crane ≤ pendantCraneMax；和工具架 Bounds 間距 ≥ pendantRackGap（容差 pendantRackGapTolerance），
        /// 不和掛位 1、4（以及 R1.5b b3 才建的 11、12）的掛位重疊。
        /// </summary>
        static void A6PendantDock(StationSet set, IDictionary<string, XrTeleportPoint> pts, System.Func<string, Transform> F, SceneResult r)
        {
            var dock = GameObject.Find("PendantDock")?.transform;
            var db = RenderBounds(dock);
            if (db == null) { Miss(r, "19", "PendantDock"); return; }
            var rc = RectXZ(db.Value);
            Note(r, "19", $"PendantDock Bounds {P3(db.Value.min)}–{P3(db.Value.max)}");
            var zone = Rect.MinMaxRect(set.liftZoneMinXZ.x, set.liftZoneMinXZ.y, set.liftZoneMaxXZ.x, set.liftZoneMaxXZ.y);
            Pend(r, "19", $"PendantDock {(rc.Overlaps(zone) ? "在" : "不在")}吊物禁區 Z {zone.yMin}…{zone.yMax} 內（離禁區 {Mathf.Max(0f, zone.yMin - rc.yMax, rc.yMin - zone.yMax):0.00} m）", !rc.Overlaps(zone));
            if (pts.TryGetValue("tp.platform.crane", out var cr)) { float dd = set.pendantCraneToCenter ? Vector2.Distance(rc.center, XZ(cr.transform.position)) : RectDistance(rc, XZ(cr.transform.position)); Pend(r, "19", $"PendantDock 中心 {P2(rc.center)} 到 tp.platform.crane {dd:0.000} m（量站點到掛座中心，≤ {set.pendantCraneMax}；a6 回報的 0.74 是量到掛座 Bounds 最近的邊）", dd <= set.pendantCraneMax + Eps, "PendantDock"); }
            else Miss(r, "19", "tp.platform.crane");
            var rack = F("env.tool_rack");
            var rb = rack != null ? RenderBounds(rack.parent != null && rack.name == "Board" ? rack.parent : rack) : null;
            if (rb == null) { Miss(r, "19", "工具架"); return; }
            float gap = RectGap(rc, RectXZ(rb.Value));
            Pend(r, "19", $"PendantDock 和工具架 Bounds Z {rb.Value.min.z:0.000}…{rb.Value.max.z:0.000} 間距 {gap:0.000} m（≥ {set.pendantRackGap} − 容差 {set.pendantRackGapTolerance}）", gap >= set.pendantRackGap - set.pendantRackGapTolerance - Eps, "PendantDock");
            // 2b（增補 4）：遙控器掛在掛座上時，本體 Bounds 的 Z 也要在掛座的 Z 範圍內，和工具架間距同樣 ≥ pendantRackGap
            var remote = RenderBounds(F("env.crane_pendant"));
            if (remote == null) Miss(r, "19", "env.crane_pendant");
            else
            {
                var rr = remote.Value;
                Pend(r, "19", $"遙控器 Bounds {P3(rr.min)}–{P3(rr.max)}：Z 在掛座 {rc.yMin:0.000}…{rc.yMax:0.000} 內", rr.min.z >= rc.yMin - set.pendantRackGapTolerance && rr.max.z <= rc.yMax + set.pendantRackGapTolerance, "env.crane_pendant");
                float rg = RectGap(RectXZ(rr), RectXZ(rb.Value));
                Pend(r, "19", $"遙控器和工具架間距 {rg:0.000} m（≥ {set.pendantRackGap} − {set.pendantRackGapTolerance}）", rg >= set.pendantRackGap - set.pendantRackGapTolerance - Eps, "env.crane_pendant");
            }
            foreach (int k in new[] { 1, 4, 11, 12 })
            {
                var slot = rack.parent != null ? rack.parent.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Slot_" + k) : null;
                if (slot == null) { Note(r, "19", $"工具架掛位 {k} 還沒建（{(k > 4 ? "R1.5b b3" : "找不到")}）"); continue; }
                var sb = RenderBounds(slot);
                float sg = sb.HasValue ? RectGap(rc, RectXZ(sb.Value)) : float.PositiveInfinity;
                Pend(r, "19", $"PendantDock 和掛位 {k} 間距 {sg:0.000} m（不重疊且 ≥ {set.pendantRackGap} − {set.pendantRackGapTolerance}）", sg >= set.pendantRackGap - set.pendantRackGapTolerance - Eps);
            }
        }

        /// <summary>第20項（增補 3，D33）：防墜器本體到四個子站點的 D 環（跪姿、站姿）8 條線段，不和 PAN_Root 底下的 collider、絕緣子的 collider 相交。</summary>
        static void A6Lanyard(StationSet set, RoofSceneDef d, IDictionary<string, XrTeleportPoint> pts, Transform root, SceneResult r)
        {
            var body = GameObject.Find("FallArrester");
            if (body == null) { Miss(r, "20", "防墜器本體 FallArrester"); return; }
            var a = body.transform.position;
            foreach (var id in new[] { "tp.roof.base_w.s", "tp.roof.base_e.s", "tp.roof.base_e.n", "tp.roof.base_w.n" })
            {
                if (!pts.TryGetValue(id, out var tp)) { Miss(r, "20", id); continue; }
                foreach (var (pose, hgt) in new[] { ("跪姿", d.dRingKneelHeight), ("站姿", d.dRingStandHeight) })
                {
                    var b = tp.transform.position + Vector3.up * hgt;
                    var hits = Physics.RaycastAll(a, (b - a).normalized, Vector3.Distance(a, b), LanyardMask, QueryTriggerInteraction.Collide)
                        .Select(h => h.collider).Where(c => (root != null && c.transform.IsChildOf(root)) || c.name == "InsulatorCollision").Select(c => c.name).Distinct().ToList();
                    Pend(r, "20", $"防墜器 → {id} D 環（{pose} {hgt} m）" + (hits.Count == 0 ? "不碰集電弓與絕緣子" : "碰到 " + string.Join("、", hits)), hits.Count == 0, id);
                }
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
