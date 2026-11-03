using System;
using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Parts.Mechanism
{
    /// <summary>
    /// R1.5b b2 集電弓整組的吊運（crane-software v0.5 §2.3、§3.1；掛在 PAN_Root）。
    /// part.pan_assy.state：Mounted → Released（固定件都拆了）→ Rigged（4 個吊點都掛上）→ Lifted → OnStand → Lifted → Mounted → Fastened（4 支底座螺栓都 Torqued）。
    /// ・吊點 ip.pan_assy.lift_point_n、吊鉤舌片 part.crane.hook、台架夾具 env.panto_stand.clamp_n：徒手 Operate 切換狀態。
    /// ・起吊：按住上升時，起吊條件（RiggingDef）全部成立 → 集電弓變成吊梁的子物件（kinematic，跟著吊鉤走）；缺一項就寫 part.crane.hoist_up_blocked
    ///   （天車不往上，吊帶繃緊但集電弓不動）和 part.pan_assy.lift_block（還缺什麼）。
    /// ・下降：吊物底面到落點（台架 StandSeat_n／車頂 InsulatorTop_n）時，4 個孔都在 snapRadius 內才吸附（對齊到 0）→ OnStand／Mounted；
    ///   沒對準就寫 part.crane.hoist_down_blocked 擋住下降。對位燈讀吸附判定同一份距離。對位用 PAN_Root 底下的孔，不用 COG（建模 B F5）。
    /// ・吊物探測 collider（LoadProbe 層、trigger）只在 Lifted 時開啟；天車碰撞停止讀它的範圍。
    /// </summary>
    public class PanAssyRigging : MonoBehaviour
    {
        public const string StateKey = "part.pan_assy.state";
        public const string Mounted = "Mounted", Released = "Released", Rigged = "Rigged", Lifted = "Lifted", OnStand = "OnStand", Fastened = "Fastened";
        public const string HookId = "part.crane.hook", LatchedKey = "part.crane.hook.latched";
        public const string UpBlockedKey = "part.crane.hoist_up_blocked", DownBlockedKey = "part.crane.hoist_down_blocked", DropDistanceKey = "part.crane.drop_distance";

        [SerializeField] RiggingDef def;
        [SerializeField] CraneMechanism crane;
        [SerializeField] Transform[] holes = new Transform[0];          // PAN_Base/BaseHole_1..4（PAN_Root 底下）
        [SerializeField] Transform[] standPins = new Transform[0];      // StandSeat_1..4
        [SerializeField] Transform[] roofTops = new Transform[0];       // InsulatorTop_1..4
        [SerializeField] Renderer[] standLights = new Renderer[0], roofLights = new Renderer[0];
        [SerializeField] BoxCollider probe;
        [SerializeField] Transform homeParent;   // 放下（吸附）後回到原本的父物件
        [Tooltip("R1.5b b4：車頂斷面（吊梁離車頂高度用）")] [SerializeField] RoofSceneDef roofScene;

        public void SetRoof(RoofSceneDef r) => roofScene = r;
        [SerializeField] Color lightOk = new Color(0.2f, 0.9f, 0.3f), lightBad = new Color(0.95f, 0.2f, 0.15f);

        bool subscribed;
        MaterialPropertyBlock mpb;

        public void Setup(RiggingDef d, CraneMechanism c, Transform[] holeT, Transform[] standPinT, Transform[] roofTopT, Renderer[] standLightR, Renderer[] roofLightR, BoxCollider loadProbe)
        {
            homeParent = transform.parent;
            def = d; crane = c; holes = holeT; standPins = standPinT; roofTops = roofTopT; standLights = standLightR ?? new Renderer[0]; roofLights = roofLightR ?? new Renderer[0]; probe = loadProbe;
        }

        public string State(ISimState s) => s.Get(StateKey, Mounted);

        void OnEnable() => Subscribe();
        void OnDisable()
        {
            if (subscribed && Sim.Bus != null) Sim.Bus.Unsubscribe<ActionRequested>(OnRequest);
            subscribed = false;
        }

        void Subscribe()
        {
            if (subscribed || !Sim.Ready) return;
            Sim.Bus.Subscribe<ActionRequested>(OnRequest);
            subscribed = true;
            if (!Sim.State.Has(StateKey)) Sim.State.Set(StateKey, Mounted);
            RigHeightDef.Load()?.Publish(Sim.State);   // D42：IN_01 a 吊梁高度範圍（建置量測的拉直高度 − slackMarginM）
        }

        /// <summary>徒手 Operate：吊點掛／卸吊帶、扣／開吊鉤舌片、鎖／開台架夾具。</summary>
        void OnRequest(ActionRequested e) { if (string.IsNullOrEmpty(e.toolId)) Operate(Sim.State, e.targetId); }

        public bool Operate(ISimState s, string targetId)
        {
            if (s == null || string.IsNullOrEmpty(targetId)) return false;
            if (targetId.StartsWith("ip.pan_assy.lift_point_"))
            {
                if (State(s) == Lifted) return false;   // 懸空時不能卸吊帶
                var k = targetId + ".state";
                bool rig = s.Get(k) != "Rigged";
                s.Batch(() =>
                {
                    s.Set(k, rig ? "Rigged" : "Free");
                    // R1.5b b4（ERR_Crane_RiggingNotInspected）：掛上判為不合格的吊帶（吊耳 n 配吊帶 n）
                    if (rig && s.Get("env.sling_" + targetId.Substring(targetId.LastIndexOf('_') + 1) + ".verdict") == "ng") s.SetBool("part.crane.failed_sling_rigged", true);
                });
                return true;
            }
            if (targetId == HookId) { s.SetBool(LatchedKey, !s.GetBool(LatchedKey)); return true; }
            if (targetId.StartsWith("env.panto_stand.clamp_"))
            {
                var k = targetId + ".state";
                s.Set(k, s.Get(k) == "Locked" ? "Open" : "Locked");
                return true;
            }
            return false;
        }

        void Update()
        {
            Subscribe();
            var s = Sim.State;
            if (s == null || !s.GetBool("training.running")) return;
            Tick(s);
        }

        static List<Vector3> Pos(Transform[] ts) => ts.Where(t => t != null).Select(t => t.position).ToList();

        /// <summary>目前的落點（台架或車頂，看哪個比較近）與 4 支的水平距離；吊物底面到落點頂面的高度。</summary>
        public (Transform[] pins, float[] dist, float height, bool roof) Target()
        {
            var h = Pos(holes);
            float Height(Transform[] p) => p.Length == 0 || p[0] == null ? float.PositiveInfinity : h.Count == 0 ? float.PositiveInfinity : h.Average(v => v.y) - p.Average(t => t.position.y);
            float Flat(Transform[] p) => p.Length == 0 || p[0] == null ? float.PositiveInfinity : RiggingLogic.PinDistances(h, Pos(p)).DefaultIfEmpty(float.PositiveInfinity).Max();
            bool roof = Flat(roofTops) + Mathf.Abs(Height(roofTops)) < Flat(standPins) + Mathf.Abs(Height(standPins));
            var pins = roof ? roofTops : standPins;
            return (pins, RiggingLogic.PinDistances(h, Pos(pins)), Height(pins), roof);
        }

        /// <summary>一幀（EditMode 測試直接呼叫）。</summary>
        public void Tick(ISimState s) => Tick(s, Time.deltaTime);

        float pullFixed;
        bool measureRefs;
        Transform cogT, attachT;
        Renderer[] beamR = new Renderer[0], headR = new Renderer[0];

        /// <summary>一幀；dt＝這一幀的秒數（拉著固定件起吊的累計秒數用）。</summary>
        public void Tick(ISimState s, float dt)
        {
            if (def == null) return;
            string st = State(s);
            string cmd = s.Get(SimIds.CraneCmd, "None");
            bool up = cmd == "Up", down = cmd == "Down";
            // 狀態推進（固定件拆好、吊帶掛好、底座螺栓上好扭力）
            bool allRigged = Enumerable.Range(1, 4).All(n => s.Get($"ip.pan_assy.lift_point_{n}.state") == "Rigged");
            bool reseated = s.GetBool("pan_assy.reseated");   // 吊回車頂之後：Mounted 只會往 Fastened 走，不再回到 Released／Rigged
            if (st == Mounted && !reseated && FixingsReleased(s)) st = Released;
            if (st == Released && allRigged && !reseated) st = Rigged;
            if (st == Rigged && !allRigged) st = Released;
            if (st == Mounted && reseated && Enumerable.Range(1, 4).All(n => s.Get($"part.pan_base.bolt_{n}.state") == "Torqued")) st = Fastened;

            string block = "";
            bool upBlocked = false, downBlocked = false;
            bool anyRigged = Enumerable.Range(1, 4).Any(n => s.Get($"ip.pan_assy.lift_point_{n}.state") == "Rigged");
            if (up && anyRigged && st != Lifted && st != Fastened)   // 有吊帶連著弓時按上升：條件不足就繃緊不動，並說明還缺什麼
            {
                var missing = RiggingDef.Missing(s, st == OnStand ? def.standLiftConditions : def.liftConditions);
                if (missing.Count == 0 && (st == Rigged || st == OnStand) && crane != null) { crane.Attach(transform, probe != null ? new Bounds(probe.center, probe.size) : new Bounds()); st = Lifted; s.SetBool("pan_assy.reseated", false); }
                else { upBlocked = true; block = string.Join("、", missing); }
            }
            // R1.5b b4（ERR_Crane_LiftWhileFixed）：固定件（底座螺栓、高壓電纜、控制線）還沒拆就按上升的累計秒數
            pullFixed = up && anyRigged && upBlocked && !FixingsReleased(s) && !reseatedOrFastened(st) ? pullFixed + dt : 0f;
            float pf = Mathf.Floor(pullFixed * 10f) / 10f;
            if (!s.Has("part.crane.pull_fixed_s") || Mathf.Abs(s.GetFloat("part.crane.pull_fixed_s") - pf) >= 0.1f - 1e-4f) s.SetFloat("part.crane.pull_fixed_s", pf);
            var (pins, dist, height, roof) = Target();
            WriteMeasures(s, st, dist, roof);
            if (st == Lifted)
            {
                s.SetFloat(DropDistanceKey, Mathf.Round(height * 1000f) / 1000f);
                // R1.5b b4（ERR_Crane_NoTrialLift）：離座 trialDrop 範圍內停住 trialStillSeconds＝試吊過（落地後清掉，下一次起吊要再試吊）
                if (!s.GetBool("part.crane.trial_ok") && height >= def.trialDropMin - 1e-4f && height <= def.trialDropMax + 1e-4f && s.GetFloat("part.crane.still_s") >= def.trialStillSeconds - 1e-4f) s.SetBool("part.crane.trial_ok", true);
                if (down && height <= def.seatTolerance)   // 往下放到落點才算放下（剛吊起時底面也貼著支撐）
                {
                    if (RiggingLogic.AllWithin(dist, def.snapRadius)) { Seat(pins); st = roof ? Mounted : OnStand; if (roof) s.SetBool("pan_assy.reseated", true); }
                    else downBlocked = true;   // 沒對準：定位銷頂住，不能再往下
                }
            }
            else if (s.Has(DropDistanceKey)) s.Set(DropDistanceKey, "");
            if (st != Lifted && s.Get("part.crane.trial_ok") != "") s.Set("part.crane.trial_ok", "");
            if (probe != null) probe.enabled = st == Lifted;
            Lights(st == Lifted && height <= def.indicatorRange, roof, dist);
            s.Batch(() =>
            {
                s.Set(StateKey, st);
                s.SetBool(UpBlockedKey, upBlocked);
                s.SetBool(DownBlockedKey, downBlocked);
                s.Set("part.pan_assy.lift_block", block);
            });
        }

        /// <summary>
        /// R1.5b b4：吊運的量測（只在值變動超過顯示精度時寫）：吊鉤掛點與重心水平差 hook_cog_offset_cm、吊梁底面離車頂 beam_above_roof_m／離弓頂 beam_above_pan_m、
        /// 吊起時傾斜 tilt_deg、PAN_Root 原點到台架中心 stand_offset_cm／到 4 個絕緣子中心 roof_offset_cm、放下後孔位到落點的最大差 seat_error_cm。
        /// </summary>
        void WriteMeasures(ISimState s, string st, float[] dist, bool roof)
        {
            void F(string key, float v, float step) { v = Mathf.Round(v / step) * step; if (!s.Has(key) || Mathf.Abs(s.GetFloat(key) - v) >= step - 1e-5f) s.SetFloat(key, v); }
            float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
            Vector3 Avg(Transform[] ts) { var p = Pos(ts); return p.Count == 0 ? transform.position : p.Aggregate(Vector3.zero, (a, b) => a + b) / p.Count; }
            if (!measureRefs)   // 每幀都會跑：節點與 Renderer 只找一次（VR 90 fps 不要每幀配置陣列）
            {
                measureRefs = true;
                cogT = transform.Find("PAN_Base/COG");
                attachT = crane != null && crane.HookBlock != null ? crane.HookBlock.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Hook_AttachPoint") : null;
                beamR = crane != null && crane.Beam != null ? crane.Beam.GetComponentsInChildren<Renderer>() : new Renderer[0];
                headR = (transform.Find("PAN_Head") ?? transform).GetComponentsInChildren<Renderer>();
            }
            var cog = cogT; var attach = attachT;
            if (cog != null && attach != null) F("part.crane.hook_cog_offset_cm", Flat(attach.position, cog.position) * 100f, 0.1f);
            if (crane != null && crane.Beam != null)
            {
                var rs = beamR;
                if (rs.Length > 0)
                {
                    float beamBottom = rs.Min(r => r.bounds.min.y);
                    var hp = crane.HookBlock.position;
                    F("part.crane.beam_above_roof_m", beamBottom - RoofY(hp.x), 0.01f);
                    // 吊梁離作業面（吊鉤在台架附近＝台架頂面，否則＝車頂）：ERR_Crane_FastLowerEmptyHook
                    bool overStand = roofScene != null && new Vector2(hp.x - roofScene.standCenterXZ.x, hp.z - roofScene.standCenterXZ.y).magnitude <= def.standSurfaceRadius;
                    F("part.crane.beam_above_surface_m", beamBottom - (overStand ? roofScene.StandTopY : RoofY(hp.x)), 0.01f);
                    var hr = headR;
                    if (hr.Length > 0) F("part.crane.beam_above_pan_m", beamBottom - hr.Max(r => r.bounds.max.y), 0.01f);
                }
            }
            if (st == Lifted) F("part.pan_assy.tilt_deg", Vector3.Angle(transform.up, Vector3.up), 0.1f);
            if (standPins.Length > 0 && standPins[0] != null) F("part.pan_assy.stand_offset_cm", Flat(transform.position, Avg(standPins)) * 100f, 0.1f);
            if (roofTops.Length > 0 && roofTops[0] != null) F("part.pan_assy.roof_offset_cm", Flat(transform.position, Avg(roofTops)) * 100f, 0.1f);
            if ((st == Mounted || st == OnStand) && dist.Length > 0) F("part.pan_assy.seat_error_cm", dist.Max() * 100f, 0.1f);
        }

        /// <summary>車頂面高度（RoofSceneDef 的車頂斷面）。</summary>
        float RoofY(float x) => roofScene != null ? roofScene.RoofSurfaceY(x) : 0f;

        static bool reseatedOrFastened(string st) => st == Fastened || st == Lifted;

        static bool FixingsReleased(ISimState s) =>
            s.Get("ip.pan_assy.ctrl_connector.state") == "Removed" && s.Get("ip.pan_assy.hv_terminal.state") == "Removed" &&
            Enumerable.Range(1, 4).All(n => s.Get($"part.pan_base.bolt_{n}.state") == "Out");

        /// <summary>吸附：離開吊梁，水平對齊到孔＝落點（平均差歸零），底面貼到落點頂面。</summary>
        void Seat(Transform[] pins)
        {
            var h = Pos(holes); var p = Pos(pins);
            var off = RiggingLogic.SnapOffset(h, p);
            off.y = p.Average(v => v.y) - h.Average(v => v.y);
            crane?.Attach(null, new Bounds());
            transform.SetParent(homeParent, true);
            transform.position += off;
        }

        void Lights(bool show, bool roof, float[] dist)
        {
            var on = roof ? roofLights : standLights; var off = roof ? standLights : roofLights;
            foreach (var r in off) if (r != null) r.enabled = false;
            mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < on.Length; i++)
            {
                if (on[i] == null) continue;
                on[i].enabled = show;
                if (!show) continue;
                bool ok = i < dist.Length && dist[i] <= def.snapRadius + 1e-5f;
                mpb.SetColor("_Color", ok ? lightOk : lightBad); mpb.SetColor("_BaseColor", ok ? lightOk : lightBad);
                on[i].SetPropertyBlock(mpb);
            }
        }

        /// <summary>對位燈 n 目前是不是綠的（測試用）。</summary>
        public bool LightGreen(int n, bool roof)
        {
            var arr = roof ? roofLights : standLights;
            if (n < 1 || n > arr.Length || arr[n - 1] == null || !arr[n - 1].enabled) return false;
            arr[n - 1].GetPropertyBlock(mpb ??= new MaterialPropertyBlock());
            return mpb.GetColor("_Color") == lightOk;
        }
    }
}
