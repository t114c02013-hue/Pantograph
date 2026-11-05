using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// 站點的地面標記與 B 鍵「帶我到目標」（T5）：
    /// ・教學模式：這一步用到的站點（小動作的 standPointId）變綠並脈動，地上有箭頭指向目前小動作的站點；其他已開放的站點暗藍；鎖住＝灰＋鎖頭。
    /// ・練習／考核模式：不分目前步驟，一律一般外觀。
    /// ・B 鍵：傳到目前小動作的站點（hint.sub_stand）並面向小動作的目標；已經在那一站就不傳；還沒開放只顯示原因。
    /// </summary>
    public class XrStations : MonoBehaviour
    {
        [SerializeField] XrTeleport teleport;
        [SerializeField] Transform arrow;
        [SerializeField] Transform head;
        readonly List<string> stepStations = new List<string>();
        bool bound;

        public IReadOnlyList<string> StepStations => stepStations;

        public void Setup(XrTeleport tp, Transform arrowRoot, Transform headT) { teleport = tp; arrow = arrowRoot; head = headT; }

        void Bind()
        {
            if (bound || !Sim.Ready) return;
            Sim.Bus.Subscribe<StepStarted>(OnStep);
            bound = true;
        }

        void OnDestroy() { if (bound && Sim.Bus != null) Sim.Bus.Unsubscribe<StepStarted>(OnStep); }

        /// <summary>小動作字串（StepStarted.subActions）裡的站點，依序去重。</summary>
        public static List<string> StationsOf(string subActions) =>
            (subActions ?? "").Split('\n').Where(x => x.Length > 0).Select(x => x.Split('\u001f')).Where(f => f.Length > 5 && f[5].Length > 0).Select(f => f[5]).Distinct().ToList();

        void OnStep(StepStarted e)
        {
            stepStations.Clear();
            stepStations.AddRange(StationsOf(e.subActions));
        }

        /// <summary>這一站的外觀（純函式）。</summary>
        public static XrTeleportPoint.Look LookOf(bool unlocked, bool tutorial, bool inStep) =>
            !unlocked ? XrTeleportPoint.Look.Locked : tutorial && inStep ? XrTeleportPoint.Look.Current : XrTeleportPoint.Look.Available;

        void Update()
        {
            Bind();
            var s = Sim.State;
            if (s == null || teleport == null) return;
            bool tutorial = s.Get("training.mode") != "practice" && s.Get("training.mode") != "exam";
            foreach (var p in teleport.Points)
            {
                if (!p.HasMarker) continue;
                bool inStep = stepStations.Any(id => p.Same(id) || teleport.Points.Any(o => o.PointId == id && o.Same(p.PointId)));
                p.SetLook(LookOf(p.Unlocked(s), tutorial, inStep), Time.time);
            }
            UpdateArrow(s, tutorial);
        }

        /// <summary>教學模式：腳下 0.6 m 處的地面箭頭指向目前小動作的站點（已經在那一站就收起）。</summary>
        void UpdateArrow(ISimState s, bool tutorial)
        {
            if (arrow == null || head == null) return;
            var next = teleport.Find(s.Get("hint.sub_stand"));
            var cur = teleport.Current;
            bool show = tutorial && next != null && !(cur != null && (cur.Same(next.PointId) || next.Same(cur.PointId)));
            if (show)
            {
                var feet = new Vector3(head.position.x, teleport.transform.position.y + 0.01f, head.position.z);
                var dir = next.transform.position - feet; dir.y = 0f;
                if (dir.magnitude < 0.5f) show = false;
                else
                {
                    arrow.position = feet + dir.normalized * 0.6f;
                    arrow.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                }
            }
            if (arrow.gameObject.activeSelf != show) arrow.gameObject.SetActive(show);
        }

        /// <summary>B 鍵：傳到目前小動作的站點並面向目標。回傳是否已處理（沒有站點資料時回傳 false，由舊行為處理）。</summary>
        public bool Focus()
        {
            var s = Sim.State;
            string stand = s?.Get("hint.sub_stand") ?? "";
            if (teleport == null) return false;
            if (string.IsNullOrEmpty(stand) || teleport.Find(stand) == null) { SayTarget(s); return false; }
            Vector3? look = null;
            string target = s.Get("hint.sub_target");
            if (Sim.Registry != null && !string.IsNullOrEmpty(target))
            {
                var ids = target.EndsWith("*") ? Sim.Registry.Ids.Where(r => r.StartsWith(target.TrimEnd('*'))).ToList() : new List<string> { target };
                var pts = ids.Select(id => Sim.Registry.TryGetPosition(id, out var p) ? (Vector3?)p : null).Where(p => p.HasValue).Select(p => p.Value).ToList();
                if (pts.Count > 0) look = pts.Aggregate(Vector3.zero, (a, b) => a + b) / pts.Count;
            }
            if (teleport.GoToStation(stand, look) == XrTeleport.GoResult.AlreadyThere)
            {
                teleport.FaceFromHere(look);   // 同一站也要轉向目標，不然背對著按了沒反應
                SayTarget(s);
            }
            return true;
        }

        /// <summary>已經在這一站：按「找目標」要有反應，提示要做什麼並震一下。</summary>
        static void SayTarget(ISimState s)
        {
            if (s == null) return;
            s.Set("ui.toast", ""); s.Set("ui.toast", "就在這一站：" + s.Get("hint.sub_text")); s.SetFloat("ui.toast_time", Time.time);
            XrHaptics.Pulse(UnityEngine.XR.XRNode.RightHand, 0.3f, 0.06f);
        }
    }
}
