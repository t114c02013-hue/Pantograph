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
                s.Set(k, s.Get(k) == "Rigged" ? "Free" : "Rigged");
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
        public void Tick(ISimState s)
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
            var (pins, dist, height, roof) = Target();
            if (st == Lifted)
            {
                s.SetFloat(DropDistanceKey, Mathf.Round(height * 1000f) / 1000f);
                if (down && height <= def.seatTolerance)   // 往下放到落點才算放下（剛吊起時底面也貼著支撐）
                {
                    if (RiggingLogic.AllWithin(dist, def.snapRadius)) { Seat(pins); st = roof ? Mounted : OnStand; if (roof) s.SetBool("pan_assy.reseated", true); }
                    else downBlocked = true;   // 沒對準：定位銷頂住，不能再往下
                }
            }
            else if (s.Has(DropDistanceKey)) s.Set(DropDistanceKey, "");
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
