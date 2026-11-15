using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// terry 2026-11-15（150A、151A）：地面教學箱的蓋子（env.roof_box.&lt;種類&gt;.lid_&lt;n&gt;）。點一下開／關，拿著工具也可以；不送 ActionPerformed、不計分。
    /// 角度只照黑板鍵 &lt;id&gt;.state（open／closed）決定，重播與還原也跟著動。任一片蓋子開著或還在轉，就顯示這個箱子的 Inside_*。
    /// VR 換站點（xr.point 變了）時把開著的蓋子關上；桌機沒有 xr.point，不自動關。
    /// </summary>
    public class LidToggle : SiteObject
    {
        public const string Open = "open", Closed = "closed", PointKey = "xr.point";

        [SerializeField] float openDeg = 100f, seconds = 0.6f;
        [SerializeField] LidToggle[] siblings = new LidToggle[0];   // 同一個箱子的所有蓋子（含自己）
        [SerializeField] GameObject[] insides = new GameObject[0];
        Quaternion closedRot;
        bool hasRest;
        float angle;

        public string StateKey => id + ".state";
        public float Angle => angle;
        public float TargetDeg => State != null && State.Get(StateKey) == Open ? openDeg : 0f;
        /// <summary>開著或還在轉：箱子內部要顯示。</summary>
        public bool ShowsInside => angle > 0.01f || TargetDeg > 0f;

        public void Setup(float openDegrees, float openSeconds, LidToggle[] boxLids, GameObject[] boxInsides)
        {
            openDeg = openDegrees; seconds = openSeconds; siblings = boxLids; insides = boxInsides;
        }

        protected override bool AcceptsAnyTool => true;

        protected override void OnBound()
        {
            Rest();
            Bus.Subscribe<StateChanged>(OnPoint);
        }

        protected override void OnDestroy()
        {
            if (bound && Bus != null) Bus.Unsubscribe<StateChanged>(OnPoint);
            base.OnDestroy();
        }

        void Rest() { if (!hasRest) { closedRot = transform.localRotation; hasRest = true; } }

        protected override void OnBareHand(ActionRequested e) => State.Set(StateKey, State.Get(StateKey) == Open ? Closed : Open);

        void OnPoint(StateChanged e)
        {
            if (e.restored || e.isReplay || !e.Has(PointKey) || State.Get(StateKey) != Open) return;
            State.Set(StateKey, Closed);
        }

        void Update() => Tick(Time.deltaTime);

        /// <summary>往目標角度轉（lidOpenSeconds 轉完全程），再更新箱子內部顯示。</summary>
        public void Tick(float dt)
        {
            Rest();
            angle = Mathf.MoveTowards(angle, TargetDeg, openDeg / Mathf.Max(seconds, 1e-3f) * dt);
            transform.localRotation = closedRot * Quaternion.Euler(angle, 0f, 0f);   // 原點在鉸鏈、繞 +X 正轉＝打開
            bool show = siblings.Any(l => l != null && l.ShowsInside);
            foreach (var g in insides) if (g != null && g.activeSelf != show) g.SetActive(show);
        }
    }
}
