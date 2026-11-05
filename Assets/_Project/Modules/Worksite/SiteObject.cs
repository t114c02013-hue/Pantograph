using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// 作業現場的可點物件（控制盤、接地端子、零件盒、備品、作業單）。
    /// 與 Parts 的互動點同樣實作 Core 的 IInteractable，但 Worksite 不引用 Parts。
    /// </summary>
    public class SiteObject : MonoBehaviour, IInteractable
    {
        [SerializeField] protected string id;
        [SerializeField] protected string displayName;
        [SerializeField] protected string defaultVerb = "click";
        static readonly int ColorId = Shader.PropertyToID("_Color");
        MaterialPropertyBlock block;
        protected bool bound;

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public string DefaultVerb => defaultVerb;
        protected ISimState State => Sim.State;
        protected IEventBus Bus => Sim.Bus;

        public void Configure(string newId, string newName, string verb = "click") { id = newId; displayName = newName; defaultVerb = verb; }

        /// <summary>箭頭和鏡頭對準的點（沒設就是物件中心；工具架中心被掛位蓋住，要指在背板空的地方）。</summary>
        [SerializeField] Transform markerPoint;
        public void SetMarkerPoint(Transform t) => markerPoint = t;
        public Transform MarkerPoint => markerPoint != null ? markerPoint : transform;

        void RegisterSelf() { Sim.Registry?.Register(id, transform); Sim.Registry?.RegisterPoint(id, MarkerPoint); }
        protected virtual void OnEnable() { RegisterSelf(); }
        protected virtual void OnDisable() { Sim.Registry?.Unregister(id, transform); }

        protected virtual void Start()
        {
            if (!Sim.Ready) return;
            RegisterSelf();
            Bus.Subscribe<ActionRequested>(OnRequestInternal);
            Bus.Subscribe<ActionPerformed>(OnPerformedInternal);
            Bus.Subscribe<StateChanged>(OnStateInternal);
            Bus.Subscribe<MeasurementTaken>(OnMeasuredInternal);
            bound = true;
            OnBound();
            Refresh();
        }

        protected virtual void OnDestroy()
        {
            if (!bound || Bus == null) return;
            Bus.Unsubscribe<ActionRequested>(OnRequestInternal);
            Bus.Unsubscribe<ActionPerformed>(OnPerformedInternal);
            Bus.Unsubscribe<StateChanged>(OnStateInternal);
            Bus.Unsubscribe<MeasurementTaken>(OnMeasuredInternal);
        }

        void OnRequestInternal(ActionRequested e)
        {
            // 點一下現場物件＝一個動作＝一批寫入（例：備品選用的結果、次數與手上的滑板同時生效）
            if (Owns(e.targetId) && (AcceptsAnyTool || string.IsNullOrEmpty(e.toolId))) State.Batch(() => OnBareHand(e));
        }

        /// <summary>手上拿著工具點它也要處理（例：工具架掛位＝歸還）。</summary>
        protected virtual bool AcceptsAnyTool => false;

        protected static void Toast(string text) { Sim.State.Set("ui.toast", text); Sim.State.SetFloat("ui.toast_time", Time.time); }
        void OnPerformedInternal(ActionPerformed e) { if (Owns(e.targetId)) OnToolAction(e); }
        void OnStateInternal(StateChanged e) { if (e.Any(Watches)) Refresh(); }
        void OnMeasuredInternal(MeasurementTaken e) { if (Owns(e.targetId)) OnMeasured(e); }

        protected virtual bool Owns(string targetId) => targetId == id;
        protected virtual bool Watches(string key) => key.StartsWith(id + ".");
        protected virtual void OnBound() { }
        protected virtual void OnBareHand(ActionRequested e) { }
        protected virtual void OnToolAction(ActionPerformed e) { }
        protected virtual void OnMeasured(MeasurementTaken e) { }
        protected virtual void Refresh() { }

        public void SetHovered(bool hovered)
        {
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!hovered) { r.SetPropertyBlock(null); continue; }
                var baseColor = r.sharedMaterial != null && r.sharedMaterial.HasProperty(ColorId) ? r.sharedMaterial.color : Color.white;
                block.Clear();
                block.SetColor(ColorId, Color.Lerp(baseColor, new Color(0.4f, 0.95f, 1f), 0.5f));
                r.SetPropertyBlock(block);
            }
        }
    }
}
