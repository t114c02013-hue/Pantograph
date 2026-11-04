using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// 可拆裝的碳滑板（part.carbon_strip_L）。徒手點滑板 → 跳出「垂直取出／撬起」；取出後拿在手上跟著滑鼠，點零件盒放進去。
    /// 備品選定後新滑板拿在手上跟著滑鼠（在托架高度上方），R 轉向，移到托架上方點托架放下（InstallSnap）。
    /// 也處理自己托架（ip.carbon_strip_L.bracket）的徒手點擊＝放下滑板。
    /// </summary>
    public class RemovableBehaviour : PartBehaviour
    {
        [SerializeField] Transform[] holeMarkers = new Transform[0];   // 滑板上的 4 個螺栓孔（外觀的子物件）
        [SerializeField] Transform[] boltSlots = new Transform[0];     // 托架上的 4 個螺栓位置
        [SerializeField] Transform visual;                              // 外觀（轉向時旋轉這個）
        [SerializeField] float carryHeight = 0.06f;
        [SerializeField] float overBracketRadius = 0.12f;

        Vector3 mountPos; Quaternion mountRot, visualBase = Quaternion.identity; bool captured;
        string BracketId => "ip.carbon_strip_" + StripLogic.Side(Id) + ".bracket";
        string Side => StripLogic.Side(Id);

        public void Setup(Transform[] holes, Transform[] slots, Transform vis) { holeMarkers = holes; boltSlots = slots; visual = vis; }

        void Capture()
        {
            if (captured) return;
            mountPos = transform.localPosition; mountRot = transform.localRotation; captured = true;
            if (visual != null) visualBase = visual.localRotation;
        }

        protected override void Start() { Capture(); base.Start(); }

        protected override void OnBound()
        {
            Capture();
            StripLogic.Initialize(State, Id);
        }

        protected override bool Owns(string targetId) => targetId == Id || targetId == BracketId || targetId == "carried";

        protected override bool WatchesKey(string key) => key.StartsWith(Id + ".") || key == "spare." + Side + ".picked";

        protected override void OnBareHand(ActionRequested e)
        {
            string st = State.Get(Id + ".state");
            if (e.targetId == "carried")
            {
                if (e.text == "rotate180" && st == StripLogic.Carried) StripLogic.Rotate(State, Id);
                return;
            }
            if (e.targetId == BracketId)
            {
                if (st != StripLogic.Carried || !AskBare("install")) return;
                // 吸附成功：同一批裡把滑板放到托架位置，並讓安裝位置檢測立刻寫入吸附後的距離（install_distance_cm）
                float offset = HoleOffsetNow();
                if (!StripLogic.Install(State, Id, offset, () => { RefreshVisual(); Sensors.Flush(State, Id + ".install"); }))
                { State.Set("ui.toast", ""); State.Set("ui.toast", StripLogic.InstallFailText(State, Id, offset)); }
                return;
            }
            // 點滑板本身
            if (e.text != null && e.text.StartsWith("remove:"))
            {
                if (AskBare("remove")) StripLogic.Remove(State, Bus, Id, e.text.Substring(7));
                return;
            }
            if (st == StripLogic.OldMounted && AskBare("click"))
            {
                if (!StripLogic.BoltsRemoved(State, Id)) State.Set(Id + ".remove_blocked", "bolts");
                else State.SetBool(Id + ".removal_prompt", true);
            }
        }

        protected override void OnToolAction(ActionPerformed e)
        {
            if (e.text == "to_tray") StripLogic.ToTray(State, Bus, Id);
            else if (e.text == "carry") StripLogic.Carry(State, Id);   // 備品架選用成功（與選用同一批）
        }

        /// <summary>目前手上滑板的孔位與托架螺栓位置的最大偏差（m）。</summary>
        float HoleOffsetNow()
        {
            if (holeMarkers.Length != 4 || boltSlots.Length != 4) return HorizontalOffset();
            float worst = 0f;
            foreach (var slot in boltSlots)
            {
                float best = float.MaxValue;
                foreach (var h in holeMarkers)
                {
                    var d = h.position - slot.position; d.y = 0f;   // 放下前在上方，只比水平位置
                    best = Mathf.Min(best, d.magnitude);
                }
                worst = Mathf.Max(worst, best);
            }
            return worst;
        }

        Vector3 MountWorld => transform.parent != null ? transform.parent.TransformPoint(mountPos) : mountPos;

        float HorizontalOffset()
        {
            var d = transform.position - MountWorld; d.y = 0f;
            return d.magnitude;
        }

        protected override void RefreshVisual()
        {
            Capture();
            string st = State.Get(Id + ".state", StripLogic.OldMounted);
            bool inHand = st == StripLogic.Carried || st == StripLogic.Removed;
            bool shown = st == StripLogic.OldMounted || st == StripLogic.NewMounted || inHand;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = shown;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = shown && !inHand;
            if (!inHand) { transform.localPosition = mountPos; transform.localRotation = mountRot; }
            if (visual != null)
                visual.localRotation = st == StripLogic.Carried && State.Get(Id + ".new_orientation") != "out" ? visualBase * Quaternion.Euler(0, 180, 0) : visualBase;
        }

        void LateUpdate()
        {
            if (State == null) return;
            string st = State.Get(Id + ".state");
            // 備品架選定（Worksite 寫 spare.L.picked）→ 拿在手上
            if (st == StripLogic.InTray && State.GetBool("spare." + Side + ".picked")) { StripLogic.Carry(State, Id); return; }
            if (st != StripLogic.Carried && st != StripLogic.Removed) return;

            // 拿在手上：跟著滑鼠指到的托架高度那一點，抬高一點顯示（不能用抬高後的平面算：斜看會偏 6 cm 以上）
            var target = StripLogic.CarryPoint(PointerState.Ray, PointerState.HasRay, MountWorld, carryHeight);
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-18f * Time.deltaTime));
            transform.localRotation = mountRot;
            if (st == StripLogic.Carried)
            {
                bool over = HorizontalOffset() <= overBracketRadius;
                if (State.GetBool(Id + ".over_bracket") != over) State.SetBool(Id + ".over_bracket", over);
            }
        }
    }
}
