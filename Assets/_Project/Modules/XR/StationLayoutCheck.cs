using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// 站點建置檢查（R1 3.5）。建置腳本與 EditMode 測試共用；回傳不合格項目（空＝通過），每項寫出站點與原因。
    /// CheckData 只看資料欄位；場景擺放的檢查在 CheckScene。
    /// </summary>
    public static partial class StationLayoutCheck
    {
        public static List<string> CheckData(StationSet set, string roofKey)
        {
            var bad = new List<string>();
            if (set == null) { bad.Add("沒有 StationSet"); return bad; }
            var st = set.stations ?? new StationDef[0];
            foreach (var must in new[] { "tp.cab", "tp.platform.lift", "tp.platform.rack", "tp.platform.ground", "tp.platform.anchor", "tp.roof.inner", "tp.roof.outer", "tp.roof.shelf",
                "tp.platform.parts", "tp.platform.scrap", "tp.roof.base_w", "tp.roof.base_e", "tp.roof.base_w.s", "tp.roof.base_e.s", "tp.roof.base_e.n", "tp.roof.base_w.n" })
                if (!st.Any(s => s != null && s.id == must)) bad.Add($"缺少 R1 站點 {must}");
            if (st.Any(s => s == null)) { bad.Add("StationSet 有空的站點"); return bad; }
            foreach (var dup in st.GroupBy(s => s.id).Where(g => g.Count() > 1)) bad.Add($"{dup.Key}：ID 重複");
            foreach (var s in st)
            {
                if (string.IsNullOrEmpty(s.id) || !s.id.StartsWith("tp.")) bad.Add($"{s.name}：ID 要以 tp. 開頭");
                if (string.IsNullOrEmpty(s.label)) bad.Add($"{s.id}：沒有名稱");
                if (string.IsNullOrEmpty(s.facingTargetId) && s.facingDirection.sqrMagnitude < 1e-6f) bad.Add($"{s.id}：沒有面向目標，也沒有固定朝向");
                if (s.floor == StationFloor.Roof && (s.unlockKey != roofKey || string.IsNullOrEmpty(s.lockedMessage)))
                    bad.Add($"{s.id}：車頂站點要用 {roofKey} 開放，並寫鎖住原因");
                if (s.floor != StationFloor.Roof && !string.IsNullOrEmpty(s.unlockKey)) bad.Add($"{s.id}：平台與駕駛室站點要隨時開放");
                if (!string.IsNullOrEmpty(s.sharedWith))
                {
                    var o = set.Find(s.sharedWith);
                    if (o == null) bad.Add($"{s.id}：共用的站點 {s.sharedWith} 不存在");
                    else if (Vector2.Distance(o.positionXZ, s.positionXZ) > Eps) bad.Add($"{s.id}：和 {s.sharedWith} 共用落點，座標要相同");
                }
            }
            if (set.Find(set.startStationId) == null) bad.Add($"起始站點 {set.startStationId} 不存在");
            if (set.targetMinDistance >= set.targetMaxDistance || set.boltTrayMin >= set.boltTrayMax) bad.Add("距離範圍的下限要小於上限");
            return bad;
        }

        public class SceneResult
        {
            public readonly List<string> bad = new List<string>(), warn = new List<string>(), info = new List<string>();
            /// <summary>合格的規則行（✔）、◇ 待定（有裁定紀錄的已知不合格，不擋建置）。</summary>
            public readonly List<string> passed = new List<string>(), pending = new List<string>();
            /// <summary>不合格項目的（檢查項, 物件 ID, 文字）：待定比對用（增補 6 第 1 點：檢查項＋物件 ID 完全相等）。</summary>
            public readonly List<(string item, string obj, string text)> failures = new List<(string, string, string)>();
            public bool Ok => bad.Count == 0;
            public void Fail(string item, string obj, string text) { bad.Add(text); failures.Add((item, obj, text)); }
            /// <summary>摘要「N 過／M 待定／K 不合格」；有待定或不合格就不寫「全過」。</summary>
            public string Summary => $"{passed.Count} 過／{pending.Count} 待定／{bad.Count} 不合格";
            public override string ToString() => string.Join("\n", bad.Select(b => "✘ " + b).Concat(pending.Select(p => "◇ 待定 " + p)).Concat(warn.Select(w => "⚠ " + w)).Concat(info.Select(i => "・" + i)).Concat(passed.Select(p => "✔ " + p)));
        }

        static bool IsTarget(Collider c) => c.GetComponentInParent<Core.ISimObject>() != null;
        /// <summary>目標的中心：只算屬於這個 ID 的碰撞體（子物件另有 ID 的不算，例 台架上的工具盤）。</summary>
        static Vector3 Center(Transform t)
        {
            var own = t.GetComponentInParent<ISimObject>();
            var cols = t.GetComponentsInChildren<Collider>().Where(c => c.GetComponentInParent<ISimObject>() == own).ToList();
            if (cols.Count == 0) return t.position;
            var b = cols[0].bounds; foreach (var c in cols) b.Encapsulate(c.bounds);
            return b.center;
        }
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// <summary>
        /// 場景擺放檢查（B 段 3.5）。points＝場景裡的站點；find＝互動點 ID → 物件；panFootprints＝會動的關節在升降全程的頂點範圍（a6，取代整組 AABB 聯集；null＝不查）。
        /// 「0.4 m 內沒有實體碰撞體」只算障礙物：站點要操作的互動物件（ISimObject）不算，螺栓盤另有 0.30–0.50 m 的規則。
        /// </summary>
        public static SceneResult CheckScene(StationSet set, RoofSceneDef d, IEnumerable<XrTeleportPoint> points, System.Func<string, Transform> find, IDictionary<string, Bounds> panFootprints, WrenchSweepInput wrench = null)
        {
            var r = new SceneResult();
            Physics.SyncTransforms();
            int walk = SceneLayers.WalkableMask;
            int invisible = LayerMask.GetMask(SceneLayers.Boundary, SceneLayers.LocomotionBlocker);
            int solid = ~(walk | invisible | LayerMask.GetMask("UI", "Ignore Raycast"));
            var pts = points.ToDictionary(p => p.PointId);
            foreach (var s in set.stations.Where(x => x != null))
            {
                if (!pts.TryGetValue(s.id, out var tp)) { r.Fail("core", s.id, $"{s.id}：場景裡沒有這個站點"); continue; }
                var p = tp.transform.position;
                // 集電弓升降範圍（R1.5b 軟體工程裁定）：和「下方有沒有 Walkable」各自獨立，距離 < 該零件的 clearRadius 就不合格（站在範圍內＝距離 0 也算），不靠 Walkable 挖洞保證
                if (s.floor == StationFloor.Roof && panFootprints != null)
                    foreach (var kv in panFootprints)
                    {
                        var fp = kv.Value; float need = PanClearRadius(set, kv.Key, s.floor);   // D37＋增補 6 第 7 點 G：依零件與站點樓層（StationSet.partClearRadius）
                        float dd = Flat(p, fp.ClosestPoint(new Vector3(p.x, fp.center.y, p.z)));
                        if (dd < need) r.Fail("core", s.id, $"{s.id}：離集電弓升降範圍（{kv.Key}）X {fp.min.x:0.00}…{fp.max.x:0.00}、Z {fp.min.z:0.00}…{fp.max.z:0.00} {dd:0.000} m，不到 {need} m");
                        else if (kv.Key == "PanHead" && s.id.StartsWith("tp.roof.base")) r.info.Add($"{s.id}：到羊角升降範圍 {dd:0.000} m（≥ {need}，D37）");
                    }
                // 站在 Walkable 上
                if (!Physics.Raycast(new Vector3(p.x, 5f, p.z), Vector3.down, out var hit, 20f, walk, QueryTriggerInteraction.Ignore)) { r.Fail("core", s.id, $"{s.id}：({p.x:0.00}, {p.z:0.00}) 下方沒有 Walkable"); continue; }
                if (Mathf.Abs(hit.point.y - p.y) > 0.02f) r.Fail("core", s.id, $"{s.id}：y {p.y:0.000} 和地板 {hit.point.y:0.000} 差超過 0.02 m");
                r.info.Add($"{s.id}：({p.x:0.000}, {p.y:0.000}, {p.z:0.000})");
                // 0.4 m 內沒有障礙物（身體高度 0.1–1.8 m）；不可見牆至少 0.15 m
                foreach (var c in Physics.OverlapCapsule(p + Vector3.up * 0.1f, p + Vector3.up * 1.8f, set.clearRadius, solid, QueryTriggerInteraction.Ignore))
                    if (!IsTarget(c)) r.Fail("core", s.id, $"{s.id}：{set.clearRadius} m 內有障礙物 {c.name}");
                // 不可見牆只查外側邊界與會動零件的挖除邊（Blocker_Static＝不會動的底架、絕緣子，改由第16項 (i) 保證，增補 6 第 7 點 B）；剛好等於門檻算合格（wallClearanceTolerance）
                foreach (var c in Physics.OverlapCapsule(p + Vector3.up * 0.1f, p + Vector3.up * 1.8f, set.invisibleWallClearance - set.wallClearanceTolerance, invisible, QueryTriggerInteraction.Ignore))
                    if (c.GetComponent<Renderer>() == null && c.name != "Blocker_Static") r.Fail("core", s.id, $"{s.id}：離不可見牆 {c.name} 不到 {set.invisibleWallClearance} m");
                // D11：平台護欄（封閉 1.1 m 護欄、活動欄）至少 guardrailClearance（身體膠囊 0.25＋0.05）
                foreach (var c in Physics.OverlapCapsule(p + Vector3.up * 0.1f, p + Vector3.up * 1.8f, set.guardrailClearance, invisible, QueryTriggerInteraction.Ignore))
                    if (IsGuardrail(c)) r.Fail("core", s.id, $"{s.id}：離護欄 {c.name} {Flat(p, c.ClosestPoint(p + Vector3.up * 0.5f)):0.00} m，不到 {set.guardrailClearance} m");
                float walkLimit = d.RoofWalkLimitAt(p.z);   // 增補 6 第 3 點：在底架兩側放寬帶的 Z 範圍內才用 roofBaseWalkHalfWidth
                if (s.floor == StationFloor.Roof && Mathf.Abs(p.x) > walkLimit + Eps) r.Fail("core", s.id, $"{s.id}：不在車頂可走範圍 |X| ≤ {walkLimit}");
                if (string.IsNullOrEmpty(s.facingTargetId)) { if (Vector3.Angle(tp.transform.forward, s.facingDirection) > 1f) r.Fail("core", s.id, $"{s.id}：朝向不是 {s.facingDirection}"); continue; }
                // 面向目標：眼高看得到、水平 ±30°、距離
                var target = find(s.facingTargetId);
                if (target == null) { r.Fail("core", s.id, $"{s.id}：找不到面向目標 {s.facingTargetId}"); continue; }
                var eye = p + Vector3.up * set.eyeHeight;
                var to = Center(target);
                var dir = to - p; dir.y = 0f;
                if (Vector3.Angle(tp.transform.forward, dir) > set.facingToleranceDeg) r.Fail("core", s.id, $"{s.id}：目標不在正前方 ±{set.facingToleranceDeg}°");
                if (!Visible(eye, to, target)) r.Fail("core", s.id, $"{s.id}：從眼高看 {s.facingTargetId} 被擋住");
                float dist = Flat(p, to), max = s.maxTargetDistance > 0 ? s.maxTargetDistance : set.targetMaxDistance, min = s.maxTargetDistance > 0 ? 0f : set.targetMinDistance;
                if (dist < min - Eps || dist > max + Eps) r.Fail("core", s.id, $"{s.id}：到 {s.facingTargetId} 水平 {dist:0.00} m（要 {min:0.0}–{max:0.0} m）");
            }
            // 個別站點規則
            Transform T(string id) => find(id);
            if (pts.TryGetValue("tp.platform.ground", out var g))
            {
                var gp = g.transform.position;
                var lines = GroundStationReport(set, gp, T, pts.TryGetValue("tp.platform.anchor", out var an) ? an.transform.position : (Vector3?)null, out var problems);
                r.info.AddRange(lines);
                r.bad.AddRange(problems);
            }
            if (pts.TryGetValue("tp.cab", out var cab))   // D27：相鄰車的駕駛室
            {
                var c = cab.transform.position;
                if (Mathf.Abs(c.x) > d.RoofEdgeX + Eps) r.bad.Add($"tp.cab：X {c.x:0.00} 超出車寬 ±{d.RoofEdgeX}");
                if (c.z > set.cabMaxZ + Eps) r.bad.Add($"tp.cab：Z {c.z:0.00} 要 ≤ {set.cabMaxZ}");
                if (c.x >= d.maintMinXZ.x && c.x <= d.maintMaxXZ.x && c.z >= d.maintMinXZ.y && c.z <= d.maintMaxXZ.y) r.bad.Add("tp.cab：落在地面維修區裡");
                r.info.Add($"tp.cab（相鄰車駕駛室）：{c:F3}");
            }
            if (pts.TryGetValue("tp.platform.lift", out var lift))
            {
                var eye = lift.transform.position + Vector3.up * set.eyeHeight;
                foreach (var id in new[] { "part.pan_head", "ip.pan_head.lower_target" })
                    if (T(id) is Transform t && !Visible(eye, Center(t), t)) r.bad.Add($"tp.platform.lift：看不到 {id}");
            }
            if (pts.TryGetValue("tp.roof.inner", out var roof))
                foreach (var side in new[] { "L", "R" })
                {
                    var tray = T("env.bolt_tray_" + side);
                    if (tray == null) { r.bad.Add($"主場景缺螺栓盤 env.bolt_tray_{side}"); continue; }
                    float dd = Flat(roof.transform.position, Center(tray));
                    if (dd < set.boltTrayMin - Eps || dd > set.boltTrayMax + Eps) r.bad.Add($"tp.roof.inner：到 env.bolt_tray_{side} {dd:0.00} m（要 {set.boltTrayMin}–{set.boltTrayMax} m）");
                }
            if (set.measuredFarBoltL > set.roofBoltMaxDistance) r.warn.Add($"肘側最遠螺栓 {set.measuredFarBoltL:0.00} m 超過 {set.roofBoltMaxDistance} m：啟用側移提示");
            if (Mathf.Abs(d.platformFloorY - d.roofFloorY) > set.maxFloorStep) r.bad.Add($"平台與車頂高度差 {Mathf.Abs(d.platformFloorY - d.roofFloorY):0.00} m 超過 {set.maxFloorStep} m");
            if (d.platformGap > set.maxPlatformGap) r.bad.Add($"平台到車頂間隙 {d.platformGap:0.00} m 超過 {set.maxPlatformGap} m");
            CheckA6(set, d, pts, find, r, wrench);
            ApplyPending(set, r);
            return r;
        }

        /// <summary>◇ 待定（增補 6 第 1 點）：不合格項目的（檢查項, 物件 ID）和 PendingRuling 完全相等才改列待定；缺裁定紀錄的那筆本身就算不合格。</summary>
        public static void ApplyPending(StationSet set, SceneResult r)
        {
            foreach (var rule in set.pendingRulings ?? new PendingRuling[0])
            {
                if (rule == null) continue;
                if (!rule.HasRecord) { r.Fail("pending", rule.objectId ?? "", $"待定 第{rule.checkItem}項 {rule.objectId} 缺裁定紀錄（ruleRef／decidedBy／releaseSegment）"); continue; }
                foreach (var f in r.failures.Where(f => rule.Matches(f.item, f.obj)).ToList())
                {
                    r.bad.Remove(f.text); r.failures.Remove(f);
                    r.pending.Add($"{f.text}　←{rule}");
                }
            }
        }

        /// <summary>D37（鐵道）＋增補 6 第 7 點 G：站點離各段升降範圍的淨空，讀 StationSet.partClearRadius（沒列的零件用 clearRadius）。</summary>
        public static float PanClearRadius(StationSet set, string segment, StationFloor floor) => set.ClearRadiusFor(segment, floor);

        static bool IsGuardrail(Collider c) => c.name.StartsWith("Rail_") || c.name == "GateBoundary";

        /// <summary>
        /// 接地站（D11）：(a) 沿 Z 離活動欄開口邊緣 ≥ groundGateMinZ；(b) 到弓頭接地掛點 ≤ groundClampMaxDistance；(c) 和 tp.platform.anchor ≥ groundAnchorMinDistance。
        /// 回傳實際座標與數值（寫進建置報告），不合格項目放 problems。
        /// </summary>
        public static List<string> GroundStationReport(StationSet set, Vector3 gp, System.Func<string, Transform> find, Vector3? anchorStation, out List<string> problems)
        {
            problems = new List<string>();
            var info = new List<string>();
            string P(Vector3 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000})";
            info.Add($"tp.platform.ground：{P(gp)}，面向 {set.Find("tp.platform.ground")?.facingTargetId}");
            foreach (var n in new[] { "Anchor_GroundBusbar", "Anchor_TesterSelfCheck" })
                if (GameObject.Find(n) is GameObject a) info.Add($"{n}：{P(a.transform.position)}，水平距離 {Flat(gp, a.transform.position):0.00} m");
            foreach (var id in new[] { SimIds.GroundTerminal, SimIds.VoltageTester })
                if (find(id) is Transform t) info.Add($"{id}（物件中心）：{P(Center(t))}，水平距離 {Flat(gp, Center(t)):0.00} m");
            var clamp = find("ip.pan_head.ground_clamp");
            if (clamp == null) problems.Add("tp.platform.ground：找不到弓頭接地掛點");
            else
            {
                float dc = Flat(gp, clamp.position);   // D24：量到互動點（不是物件中心）
                info.Add($"弓頭接地掛點互動點：{P(clamp.position)}；(b) 到接地站 {dc:0.00} m（上限 {set.groundClampMaxDistance} m＝絕緣桿長 − 0.5）");
                if (dc > set.groundClampMaxDistance + Eps) problems.Add($"tp.platform.ground：(b) 到弓頭接地掛點 {dc:0.00} m（上限 {set.groundClampMaxDistance} m）");
            }
            foreach (var n in new[] { "Rail_CarSide_A", "Rail_CarSide_B" })
                if (GameObject.Find(n) is GameObject rail && rail.GetComponent<Collider>() is Collider rc)
                    info.Add($"車側護欄 {n}：範圍 {P(rc.bounds.min)}–{P(rc.bounds.max)}，到接地站 {Flat(gp, rc.ClosestPoint(gp + Vector3.up * 0.5f)):0.00} m");
            var gate = GameObject.Find("GateBoundary")?.GetComponent<Collider>();
            if (gate == null) problems.Add("找不到活動欄 GateBoundary");
            else
            {
                var b = gate.bounds;
                float dz = gp.z >= b.min.z && gp.z <= b.max.z ? 0f : Mathf.Min(Mathf.Abs(gp.z - b.min.z), Mathf.Abs(gp.z - b.max.z));
                info.Add($"活動欄開口：Z {b.min.z:0.000}–{b.max.z:0.000}（X {b.center.x:0.000}）；(a) 接地站沿 Z 離開口邊緣 {dz:0.00} m（至少 {set.groundGateMinZ} m）");
                if (dz < set.groundGateMinZ - Eps) problems.Add($"tp.platform.ground：(a) 沿 Z 離活動欄開口邊緣 {dz:0.00} m（至少 {set.groundGateMinZ} m）");
            }
            if (anchorStation.HasValue)
            {
                float da = Flat(gp, anchorStation.Value);
                info.Add($"(c) 和 tp.platform.anchor {P(anchorStation.Value)} 相距 {da:0.00} m（至少 {set.groundAnchorMinDistance} m）");
                if (da < set.groundAnchorMinDistance - Eps) problems.Add($"tp.platform.ground：(c) 和 tp.platform.anchor 只相距 {da:0.00} m（至少 {set.groundAnchorMinDistance} m）");
            }
            return info;
        }

        static bool Visible(Vector3 eye, Vector3 to, Transform target)
        {
            int mask = ~(SceneLayers.WalkableMask | LayerMask.GetMask(SceneLayers.Boundary, SceneLayers.LocomotionBlocker, "UI", "Ignore Raycast"));
            foreach (var h in Physics.RaycastAll(eye, (to - eye).normalized, Vector3.Distance(eye, to) - 0.02f, mask, QueryTriggerInteraction.Ignore))
                if (!h.transform.IsChildOf(target) && !target.IsChildOf(h.transform) && !IsTarget(h.collider)) return false;   // 互動物件（例：工具箱的掛位）不算遮擋
            return true;
        }
    }
}
