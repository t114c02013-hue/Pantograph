using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Parts.CarbonStrip
{
    /// <summary>滑板量測的狀態邏輯（純函式；元件與測試共用）。</summary>
    public static class StripMeasureLogic
    {
        public static readonly string[] Points = { "left", "mid", "right" };
        static string Side(string stripId) => stripId.Substring(stripId.Length - 1);
        public static string PointId(string stripId, string p) => "ip.carbon_strip_" + Side(stripId) + ".measure_" + p;

        /// <summary>把 part.carbon_strip_X.true.&lt;p&gt; 鏡射到量測點的 &lt;point&gt;.true（卡尺讀這個）。</summary>
        public static void Mirror(ISimState s, string stripId, string p)
        {
            var key = stripId + ".true." + p;
            if (s.Has(key)) s.SetFloat(PointId(stripId, p) + ".true", s.GetFloat(key));
        }

        public static void MirrorAll(ISimState s, string stripId) { foreach (var p in Points) Mirror(s, stripId, p); }

        /// <summary>記錄卡尺讀數。回傳這個點是否屬於這條滑板。</summary>
        public static bool Record(ISimState s, string stripId, string pointId, float value) =>
            s.BatchResult(() => RecordInBatch(s, stripId, pointId, value));   // 讀數與已量測一批生效

        static bool RecordInBatch(ISimState s, string stripId, string pointId, float value)
        {
            foreach (var p in Points)
            {
                if (pointId != PointId(stripId, p)) continue;
                s.SetFloat(pointId + ".reading", Mathf.Round(value * 100f) / 100f);
                s.SetBool(pointId + ".measured", true);
                if (s.Get(stripId + ".state") == "NewMounted") s.SetBool(pointId + ".new_measured", true);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 碳滑板的量測：真實厚度來自情境（part.carbon_strip_L.true.left/mid/right），
    /// 鏡射到各量測點的 &lt;point&gt;.true 給卡尺讀；卡尺量完（MeasurementTaken）就寫 &lt;point&gt;.reading 與 .measured。
    /// 新滑板裝上後量中點，另外記 .new_measured（CSR_15 用）。
    /// </summary>
    public class CarbonStripBehaviour : MonoBehaviour, IMeasurable
    {
        [SerializeField] string stripId = "part.carbon_strip_L";
        bool bound;

        public string Id => stripId;
        public void Setup(string id) => stripId = id;

        void Start() => Bind();
        void OnEnable() => Bind();

        void Bind()
        {
            if (bound || !Sim.Ready) return;
            Sim.Bus.Subscribe<MeasurementTaken>(OnMeasured);
            Sim.Bus.Subscribe<StateChanged>(OnState);
            bound = true;
            StripMeasureLogic.MirrorAll(Sim.State, stripId);
        }

        void OnDisable()
        {
            if (!bound || Sim.Bus == null) return;
            Sim.Bus.Unsubscribe<MeasurementTaken>(OnMeasured);
            Sim.Bus.Unsubscribe<StateChanged>(OnState);
            bound = false;
        }

        void OnState(StateChanged e)
        {
            // 一批鏡射：三個量測點一起更新（步驟切換時的開始快照不會只鏡射到一半）
            if (!StripMeasureLogic.Points.Any(p => e.Has(stripId + ".true." + p))) return;
            Sim.State.Batch(() =>
            {
                foreach (var p in StripMeasureLogic.Points)
                    if (e.Has(stripId + ".true." + p)) StripMeasureLogic.Mirror(Sim.State, stripId, p);
            });
        }

        public bool TryGetTrueValue(string pointId, out float valueMm)
        {
            valueMm = Sim.State.GetFloat(pointId + ".true");
            return Sim.State.Has(pointId + ".true");
        }

        void OnMeasured(MeasurementTaken e) => StripMeasureLogic.Record(Sim.State, stripId, e.targetId, e.value);
    }
}
