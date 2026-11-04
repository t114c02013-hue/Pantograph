using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Interaction
{
    /// <summary>
    /// 攝影機帶到目前的目標：教學模式下每個小動作的目標改變時自動平滑移過去；
    /// 任何模式按 C 或介面「帶我過去」（FocusRequested）也會移過去。目標支援 * 萬用字元，位置由 SceneRegistry 提供。
    /// </summary>
    public class StepFocus : MonoBehaviour
    {
        [SerializeField] float viewHeight = 1.2f, boltViewHeight = 1f, boltViewBack = 0.3f, boltViewSize = 0.2f, bagViewBack = 0.05f;
        [SerializeField] OrbitCameraController orbit;
        [SerializeField] float minSize = 0.45f;
        string currentTargets = "", stepTargets = "";
        bool bound, firstStep = true;

        public void Setup(OrbitCameraController o) => orbit = o;

        void Update()
        {
            if (!bound && Sim.Ready)
            {
                Sim.Bus.Subscribe<StepStarted>(OnStep);
                Sim.Bus.Subscribe<HighlightRequested>(OnHighlight);
                Sim.Bus.Subscribe<ActionRequested>(OnRequest);
                Sim.Bus.Subscribe<FocusRequested>(OnFocus);
                bound = true;
            }
            if (bound && !PointerState.TypingInUi && Input.GetKeyDown(KeyCode.C)) { UiCommands.Issue(UiCommands.FocusTarget, "key.C"); FocusCurrent(); }
        }

        void OnDestroy()
        {
            if (!bound || Sim.Bus == null) return;
            Sim.Bus.Unsubscribe<StepStarted>(OnStep);
            Sim.Bus.Unsubscribe<HighlightRequested>(OnHighlight);
            Sim.Bus.Unsubscribe<ActionRequested>(OnRequest);
            Sim.Bus.Unsubscribe<FocusRequested>(OnFocus);
        }

        void OnStep(StepStarted e)
        {
            stepTargets = !string.IsNullOrEmpty(e.highlightIds) ? e.highlightIds : e.targetIds != null ? string.Join(",", e.targetIds) : "";
            if (string.IsNullOrEmpty(currentTargets) || Sim.State.Get("training.mode") == "practice") currentTargets = stepTargets;
            if (firstStep) FocusCurrent();
            firstStep = false;
        }

        void OnHighlight(HighlightRequested e)
        {
            if (string.IsNullOrEmpty(e.text) || e.text == currentTargets) return;
            currentTargets = e.text;
            if (Sim.State.Get("training.mode") != "practice") FocusCurrent();
        }

        void OnRequest(ActionRequested e)
        {
            if (e.targetId == "camera.home" && orbit != null) orbit.ResetView();
        }

        void OnFocus(FocusRequested e)
        {
            if (!string.IsNullOrEmpty(e.targetId)) currentTargets = e.targetId;
            FocusCurrent();
        }

        public void FocusCurrent()
        {
            if (orbit == null || Sim.Registry == null) return;
            var points = Resolve(string.IsNullOrEmpty(currentTargets) ? stepTargets : currentTargets).ToList();
            if (points.Count == 0) return;
            var b = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) b.Encapsulate(p);
            b.size = Vector3.Max(b.size, Vector3.one * (SimIds.IsBoltId(Sim.State.Get("hint.sub_target")) ? boltViewSize : minSize));   // 螺栓很小，拉近一點
            var stand = GameObject.Find("StationAnchor_" + Sim.State.Get("hint.sub_stand"));
            if (stand != null) orbit.Retarget(b, ViewFrom(stand.transform.position, Sim.State.Get("hint.sub_target"), b.center, InBag(Sim.State.Get("hint.sub_target")))); else orbit.Retarget(b);
        }

        /// <summary>鏡頭從哪個方向看目標：一般從站點的眼高；螺栓在弓頭上面，從它正上方偏站點一點往下看（側面會被弓角和框架擋住）。</summary>
        public Vector3 ViewFrom(Vector3 stand, string targetId, Vector3 target, bool inBag = false)
        {
            // 掛位排成一列：斜看會被隔壁的工具擋住，從架子正前方看（架子都靠外側護欄，面向 +X）
            if (!inBag && (targetId ?? "").StartsWith("env.tool_rack.slot_")) return target + Vector3.right + Vector3.up * 0.2f;
            if (!inBag && !SimIds.IsBoltId(targetId)) return stand + Vector3.up * viewHeight;   // 工具袋和螺栓一樣從上面看：開口朝上，側面被零件袋擋住
            var back = stand - target; back.y = 0f;
            return target + Vector3.up * boltViewHeight + back.normalized * (inBag ? bagViewBack : boltViewBack);
        }

        static bool InBag(string id) => id.StartsWith("env.tool_rack.slot_") && Sim.State.Get(SimIds.BagSlots).Contains("," + id.Substring(id.LastIndexOf('_') + 1) + ",");

        static IEnumerable<Vector3> Resolve(string ids)
        {
            var reg = Sim.Registry;
            foreach (var raw in ids.Split(','))
            {
                var id = raw.Trim();
                if (id.Length == 0) continue;
                if (id.EndsWith("*"))
                {
                    foreach (var match in reg.Ids.Where(x => x.StartsWith(id.TrimEnd('*'))).ToList())
                        if (reg.TryGetPosition(match, out var p)) yield return p;
                }
                else if (reg.TryGetPosition(id, out var q)) yield return q;
            }
        }
    }
}
