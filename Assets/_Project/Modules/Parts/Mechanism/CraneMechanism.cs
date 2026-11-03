using System;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Parts.Mechanism
{
    /// <summary>
    /// R1.5b b1 天車機構（crane-software v0.5 §2.2，和升降同一個模式）：只讀黑板 ctrl.crane.*（遙控器寫），依 CraneProfile 直接移動 Transform——
    /// Crane_Bridge 只沿 Z、Crane_Trolley 只沿 X、Crane_HookBlock 的 Y 由捲揚決定（XZ 跟著小車的 Crane_RopeAnchor）、Crane_Rope 依兩端距離縮放。
    /// 寫 part.crane.hook.pos（變化超過 hookPosWriteThreshold 或放開按鍵時）、part.crane.load_bottom_y、part.crane.state。
    /// 安全：上限位（LimitUp）、緊急停止鎖住（EStop，要復歸再重轉電源）、碰撞停止（Collision，記 ERR_Crane_Collision）、
    /// 紅區停車（RedZoneStop，頭部水平投影進入吊物投影外擴 redZoneMargin，記 ERR_Crane_PersonUnderLoad；離開紅區前命令無效）。
    /// 吊物 load（b1 除錯方塊、b2 起是 PAN_Root）是 kinematic 的子物件，晃動只轉吊梁的外觀，不寫黑板。
    /// </summary>
    public class CraneMechanism : MonoBehaviour
    {
        public enum CraneState { Idle, Moving, EStop, Collision, LimitUp, RedZoneStop }

        [SerializeField] CraneProfile profile;
        [SerializeField] Transform bridge, trolley, hookBlock, ropeAnchor, rope, swayPivot;
        [SerializeField] SafetyRuleDefinition[] rules = Array.Empty<SafetyRuleDefinition>();
        [Tooltip("吊鉤掛點下方的吊物（null＝空鉤）；loadLocalBounds 是吊物局部座標的緊包圍盒（PAN_Root 用 LoweredTightBounds）")]
        [SerializeField] Transform load;
        [SerializeField] Bounds loadLocalBounds;
        [Tooltip("R1.5b b2：下降對位時要排除的 collider（CraneCollisionIgnoreDef）")] [SerializeField] RiggingDef rigging;

        /// <summary>頭部位置來源（紅區判定）；預設是主攝影機。測試換成固定值。</summary>
        public static Func<Vector3?> HeadSource = () => Camera.main != null ? Camera.main.transform.position : (Vector3?)null;

        float vHoist, vTraverse, vTravel, ropeTopOffset;
        Vector3 lastWritten = new Vector3(float.NaN, 0f, 0f), lastHookVel;
        Vector2 sway;
        Quaternion swayBase = Quaternion.identity;
        bool needPowerCycle, inRedZone, collided, wasMoving;
        bool? wasAirborne;
        Vector3? liftOffHook;   // 3c：吊物離座時吊鉤的位置（算離座後水平移動了多少）
        string lastCmd = CraneLogic.None;

        public CraneProfile Profile => profile;
        /// <summary>上一次碰撞停止撞到的 collider 名稱（寫進 ErrorDetected.text，回放和報表看得到撞到什麼）。</summary>
        public string LastHit { get; private set; } = "";
        public Transform Load => load;
        /// <summary>R1.5b b4：吊鉤、吊梁（量吊鉤與重心水平差、吊梁離車頂／弓頂高度用）。</summary>
        public Transform HookBlock => hookBlock;
        public Transform Beam => swayPivot;
        float stillSeconds;
        public Vector3 HookPosition => hookBlock.position;

        public void Setup(CraneProfile p, Transform bridgeT, Transform trolleyT, Transform hookT, Transform anchorT, Transform ropeT, Transform swayT, SafetyRuleDefinition[] safety)
        {
            profile = p; bridge = bridgeT; trolley = trolleyT; hookBlock = hookT; ropeAnchor = anchorT; rope = ropeT; swayPivot = swayT; rules = safety ?? Array.Empty<SafetyRuleDefinition>();
            if (swayPivot != null) swayBase = swayPivot.localRotation;
            var rr = rope != null ? rope.GetComponent<Renderer>() : null;
            ropeTopOffset = rr != null && hookBlock != null ? rr.bounds.min.y - hookBlock.position.y : 0f;   // 繩子下端在吊鉤上方多少（建置時擺好的值）
        }

        public void SetRigging(RiggingDef r) => rigging = r;

        /// <summary>掛上吊物（null＝卸下）。吊物成為吊鉤（晃動支點）的子物件，跟著走。</summary>
        public void Attach(Transform loadT, Bounds localBounds)
        {
            load = loadT; loadLocalBounds = localBounds;
            if (load != null) load.SetParent(swayPivot != null ? swayPivot : hookBlock, true);
        }

        void Awake() { if (swayPivot != null) swayBase = swayPivot.localRotation; }

        void Update()
        {
            var s = Sim.State;
            if (s == null || !s.GetBool("training.running")) return;
            Tick(s, s.GetBool("training.paused") ? 0f : Time.deltaTime);
        }

        /// <summary>吊物的世界座標包圍盒（AABB；碰撞停止用旋轉後的 OverlapBox，見 LoadBox）。</summary>
        public Bounds? LoadWorldBounds()
        {
            if (load == null) return null;
            var (c, half, rot) = LoadBox();
            var b = new Bounds(c, Vector3.zero);
            for (int i = 0; i < 8; i++) b.Encapsulate(c + rot * Vector3.Scale(half, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
            return b;
        }

        (Vector3 center, Vector3 half, Quaternion rot) LoadBox() =>
            (load.TransformPoint(loadLocalBounds.center), Vector3.Scale(loadLocalBounds.extents, load.lossyScale), load.rotation);

        /// <summary>一幀（EditMode 測試直接呼叫）。</summary>
        [Tooltip("R1.5b b4（D23）：駐車位空節點 Crane_ParkPose（X＝吊車台、Y＝吊鉤、Z＝橋架）")] [SerializeField] Transform parkPose;
        public void SetParkPose(Transform pose) => parkPose = pose;
        public Transform ParkPose => parkPose;
        /// <summary>三軸目前位置：X＝吊車台、Y＝吊鉤、Z＝橋架（D23 測試比對駐車位）。</summary>
        public Vector3 AxisPositions => new Vector3(trolley.position.x, hookBlock.position.y, bridge.position.z);

        /// <summary>D23：日常更換時天車停在駐車位（位置從 Crane_ParkPose 節點讀），電源固定關、命令一律無效。</summary>
        public void Park(ISimState s)
        {
            if (parkPose != null) Apply(parkPose.position.x, parkPose.position.y, parkPose.position.z);
            vHoist = vTraverse = vTravel = 0f;
            if (s != null && s.GetBool(SimIds.CranePower)) s.SetBool(SimIds.CranePower, false);
        }

        public void Tick(ISimState s, float dt)
        {
            if (profile == null || bridge == null || trolley == null || hookBlock == null) return;
            if (s.GetBool(SimIds.CourseCraneParked)) { Park(s); return; }   // D23
            bool power = s.GetBool(SimIds.CranePower), estop = s.GetBool(SimIds.CraneEStop), fast = s.GetBool(SimIds.CraneFast);
            string cmd = s.Get(SimIds.CraneCmd, CraneLogic.None);
            if (estop) needPowerCycle = true;
            else if (!power) needPowerCycle = false;   // 復歸後要先關電源再開，才解除鎖住

            // 紅區、碰撞停止：吊物懸空時才有（part.pan_assy.state＝Lifted；b1 的除錯吊物沒有這個鍵＝掛上就算懸空）
            bool airborne = load != null && (!s.Has("part.pan_assy.state") || s.Get("part.pan_assy.state") == "Lifted");
            var lb = airborne ? LoadWorldBounds() : null;
            var head = HeadSource();
            bool red = lb.HasValue && head.HasValue && CraneLogic.InRedZone(lb.Value, profile.redZoneMargin, head.Value);
            if (red && !inRedZone) Fault(SafetyRuleKind.CranePersonUnderLoad, "ERR_Crane_PersonUnderLoad");
            // 高速回到微動（軟體工程裁定）：吊物狀態改變（Free↔Lifted）、進入紅區停車
            if (fast && ((red && !inRedZone) || (wasAirborne.HasValue && airborne != wasAirborne.Value))) { s.SetBool(SimIds.CraneFast, false); fast = false; }
            wasAirborne = airborne;
            inRedZone = red;
            if (cmd != lastCmd) { collided = false; lastCmd = cmd; }   // 放開（或換方向）後才重新允許移動

            var (axis, dir) = CraneLogic.Parse(cmd);
            bool allowed = power && !estop && !needPowerCycle && !red && !collided;
            if (!allowed) { axis = CraneAxis.None; dir = 0; }
            // b2：吊運條件不足（吊帶繃緊、集電弓不動）或定位銷頂住時，捲揚不動（PanAssyRigging 寫黑板，天車不引用它）
            bool hoistHeld = axis == CraneAxis.Hoist && (dir > 0 ? s.GetBool("part.crane.hoist_up_blocked") : s.GetBool("part.crane.hoist_down_blocked"));
            if (hoistHeld) { axis = CraneAxis.None; dir = 0; }
            bool hitLimit = false;
            float full = CraneLogic.Speed(profile, axis, fast);
            var hook = hookBlock.position; var br = bridge.position; var tr = trolley.position;
            float hy = hook.y, tx = tr.x, bz = br.z;
            var before = new Vector3(tx, hy, bz);
            hitLimit |= CraneLogic.StepAxis(ref hy, ref vHoist, axis == CraneAxis.Hoist ? dir * full : 0f, CraneLogic.Speed(profile, CraneAxis.Hoist, true), profile.accelSeconds, profile.hookYMin, profile.hookYMax, dt) && axis == CraneAxis.Hoist && dir > 0;
            CraneLogic.StepAxis(ref tx, ref vTraverse, axis == CraneAxis.Traverse ? dir * full : 0f, CraneLogic.Speed(profile, CraneAxis.Traverse, true), profile.accelSeconds, profile.trolleyXMin, profile.trolleyXMax, dt);
            CraneLogic.StepAxis(ref bz, ref vTravel, axis == CraneAxis.Travel ? dir * full : 0f, CraneLogic.Speed(profile, CraneAxis.Travel, true), profile.accelSeconds, profile.bridgeZMin, profile.bridgeZMax, dt);
            if (!allowed) { vHoist = vTraverse = vTravel = 0f; hy = before.y; tx = before.x; bz = before.z; }   // 不允許時立刻停（紅區、緊急停止不平滑）
            if (hoistHeld) { vHoist = 0f; hy = before.y; }

            Apply(tx, hy, bz);
            // 碰撞停止：移動後吊物和場景 Collision 群組重疊就退回這一幀之前的位置
            if (airborne && (tx != before.x || hy != before.y || bz != before.z) && Overlapping(s))
            {
                Apply(before.x, before.y, before.z);
                vHoist = vTraverse = vTravel = 0f; collided = true;
                Fault(SafetyRuleKind.CraneCollision, "ERR_Crane_Collision");
            }

            // 視覺晃動（只轉吊梁，不寫黑板）
            var hv = new Vector2(vTraverse, vTravel);
            var acc = dt > 0f ? (hv - new Vector2(lastHookVel.x, lastHookVel.z)) / dt : Vector2.zero;
            lastHookVel = new Vector3(hv.x, 0f, hv.y);
            sway = CraneLogic.StepSway(sway, acc, profile.swayMaxDeg, profile.swayDecaySeconds, dt);
            if (swayPivot != null) swayPivot.localRotation = swayBase * Quaternion.Euler(sway.y, 0f, -sway.x);

            bool moving = Mathf.Abs(vHoist) + Mathf.Abs(vTraverse) + Mathf.Abs(vTravel) > 1e-5f;
            CraneState state = estop || needPowerCycle && power ? CraneState.EStop : red ? CraneState.RedZoneStop : collided ? CraneState.Collision
                : hitLimit || (axis == CraneAxis.Hoist && dir > 0 && hy >= profile.hookYMax - 1e-4f) ? CraneState.LimitUp : moving ? CraneState.Moving : CraneState.Idle;
            s.Batch(() =>
            {
                s.Set(SimIds.CraneState, state.ToString());
                var hp = hookBlock.position;
                bool released = wasMoving && !moving;
                if (float.IsNaN(lastWritten.x) || released || (hp - lastWritten).magnitude > profile.hookPosWriteThreshold)
                {
                    s.Set(SimIds.CraneHookPos, $"{SimState.Format(hp.x)},{SimState.Format(hp.y)},{SimState.Format(hp.z)}", released);
                    lastWritten = hp;
                }
                if (lb.HasValue) s.SetFloat(SimIds.CraneLoadBottomY, Mathf.Round(LoadWorldBounds().Value.min.y * 1000f) / 1000f);
                WriteSafetyMeasures(s, airborne, moving, lb, head);
                stillSeconds = moving ? 0f : stillSeconds + dt;   // R1.5b b4：試吊、放下時停住幾秒（LF_02 a、LF_05 a、IN_02 a、IN_04 a）
                float still = Mathf.Floor(stillSeconds * 10f) / 10f;
                if (!s.Has("part.crane.still_s") || Mathf.Abs(s.GetFloat("part.crane.still_s") - still) >= 0.1f - 1e-4f) s.SetFloat("part.crane.still_s", still);
            });
            wasMoving = moving;
        }

        /// <summary>
        /// R1.5b b4 3c：天車安全錯誤（ErrorRules.json，全程監看）讀的量測鍵：頭盔到吊物投影的水平距離、到吊鉤的水平距離、空鉤是否在動、離座後水平移動距離。
        /// 只在值改變超過 5 mm（或布林改變）時寫，不會每幀都寫。
        /// </summary>
        void WriteSafetyMeasures(ISimState s, bool airborne, bool moving, Bounds? lb, Vector3? head)
        {
            void F(string key, float v) { v = Mathf.Round(v * 1000f) / 1000f; if (!s.Has(key) || Mathf.Abs(s.GetFloat(key) - v) > 0.005f) s.SetFloat(key, v); }
            void B(string key, bool v) { if (s.GetBool(key) != v) s.SetBool(key, v); }
            var hp = hookBlock.position;
            if (head.HasValue)
            {
                F("part.crane.head_to_hook_m", new Vector2(head.Value.x - hp.x, head.Value.z - hp.z).magnitude);
                if (lb.HasValue) F("part.crane.operator_distance_m", CraneLogic.HorizontalDistance(lb.Value, head.Value));
            }
            B("part.crane.empty_hook_moving", moving && !airborne);
            // R1.5b b4（RM_05 c）：吊鉤移動時人不在吊鉤下方（頭的水平投影離吊鉤超過紅區外擴）
            B("part.crane.person_clear", !head.HasValue || new Vector2(head.Value.x - hp.x, head.Value.z - hp.z).magnitude > profile.redZoneMargin);
            if (airborne && !liftOffHook.HasValue) liftOffHook = hp;
            if (!airborne) liftOffHook = null;
            F("part.crane.horizontal_since_lift_m", liftOffHook.HasValue ? new Vector2(hp.x - liftOffHook.Value.x, hp.z - liftOffHook.Value.z).magnitude : 0f);
        }

        void Apply(float trolleyX, float hookY, float bridgeZ)
        {
            bridge.position = new Vector3(bridge.position.x, bridge.position.y, bridgeZ);
            trolley.position = new Vector3(trolleyX, trolley.position.y, trolley.position.z);
            var a = ropeAnchor != null ? ropeAnchor.position : trolley.position;
            hookBlock.position = new Vector3(a.x, hookY, a.z);
            if (rope != null && ropeAnchor != null)
            {
                var top = ropeAnchor.position; var bottom = hookBlock.position + Vector3.up * ropeTopOffset;
                float len = Mathf.Max(0.01f, top.y - bottom.y);
                rope.position = (top + bottom) / 2f;
                var rend = rope.GetComponent<Renderer>();
                float now = rend != null ? rend.bounds.size.y : 0f;   // 目前世界長度（繩子垂直）
                if (now > 1e-6f)
                {
                    // 縮放和世界 +Y 最平行的那一個局部軸（FBX 匯入後繩子的長軸不一定是局部 Y）
                    var up = rope.InverseTransformDirection(Vector3.up); int k = Mathf.Abs(up.x) > Mathf.Abs(up.y) ? (Mathf.Abs(up.x) > Mathf.Abs(up.z) ? 0 : 2) : (Mathf.Abs(up.y) > Mathf.Abs(up.z) ? 1 : 2);
                    var ls = rope.localScale; ls[k] *= len / now; rope.localScale = ls;
                }
            }
        }

        bool Overlapping(ISimState s)
        {
            // 吊物底面離落點 ≤ ignoreDistance 時，定位銷與絕緣子本來就要接觸底架（CraneCollisionIgnoreDef，建模 B 12.9 H2）。
            // 規格只寫「下降時」；剛起吊離座的那 0.10 m 也一樣貼著支撐，不排除的話一起吊就碰撞停車，所以上下都排除
            bool ignoreSeats = rigging != null && float.TryParse(s.Get("part.crane.drop_distance"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var drop) && drop <= rigging.ignoreDistance;
            Physics.SyncTransforms();   // 這一幀剛移動過 Transform
            var (c, half, rot) = LoadBox();
            int mask = ~LayerMask.GetMask(SceneLayers.Walkable, SceneLayers.LocomotionBlocker, SceneLayers.WalkLimit, SceneLayers.LoadProbe, SceneLayers.Crane);   // 學員移動用的地板與不可見牆不是實體障礙（用 layer 排除）
            foreach (var col in Physics.OverlapBox(c, half, rot, mask, QueryTriggerInteraction.Ignore))
            {
                if (col.transform.IsChildOf(load) || col.transform.IsChildOf(transform)) continue;   // 吊物自己、天車自己
                if (ignoreSeats && Ignored(col.transform)) continue;
                if (InCollisionGroup(col.transform)) { LastHit = col.name; return true; }
            }
            return false;
        }

        bool Ignored(Transform t)
        {
            for (var p = t; p != null && p.name != "Collision"; p = p.parent)
                if (rigging.collisionIgnorePrefixes.Any(x => p.name.StartsWith(x))) return true;
            return false;
        }

        /// <summary>場景件的「Collision」群組底下的 collider 才算障礙（Walkable、互動點不算）。</summary>
        static bool InCollisionGroup(Transform t)
        {
            for (var p = t; p != null; p = p.parent) if (p.name == "Collision") return true;
            return false;
        }

        void Fault(SafetyRuleKind kind, string fallbackId)
        {
            var r = rules.FirstOrDefault(x => x != null && x.kind == kind);
            Sim.Bus?.Publish(new ErrorDetected
            {
                errorId = r != null ? r.errorId : fallbackId, severity = Severity.Critical, blocks = true,
                what = r?.what ?? "", why = r?.why ?? "", fix = r?.fix ?? "",
                stepId = Sim.State?.Get("procedure.current") ?? "", targetId = "env.crane", text = kind == SafetyRuleKind.CraneCollision ? LastHit : "", time = SimClock.Now
            });
        }
    }
}
