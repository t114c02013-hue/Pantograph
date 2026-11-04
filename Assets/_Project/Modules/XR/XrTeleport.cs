using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

namespace PantographSim.XR
{
    /// <summary>
    /// 傳送（左搖桿往前推＝瞄準拋物線，放開＝傳送到最近的固定傳送點）與「帶我到目標」（FocusRequested）。
    /// 只做瞬間移動（XRI TeleportationProvider），絕不平滑移動或轉動視角。車頂兩點在 harness.roof（SI_06 d：安全帶扣上車頂掛點）之前鎖住：
    /// 拋物線變紅並顯示「先扣好安全帶」。
    /// </summary>
    public class XrTeleport : MonoBehaviour
    {
        [SerializeField] XROrigin origin;
        [SerializeField] TeleportationProvider provider;
        [SerializeField] Transform aimOrigin;
        [SerializeField] LineRenderer arc;
        [SerializeField] XrTeleportPoint[] points = new XrTeleportPoint[0];
        [SerializeField] string startPointId = "tp.platform.lift";
        [SerializeField] XrFader fader;
        [SerializeField] float launchSpeed = 7f, snapRadius = 1.6f;
        [Tooltip("R1.5b b2：傳送限制（吊物懸空一律不能傳送）")] [SerializeField] PantographSim.Data.TeleportRule rule;

        public void SetRule(PantographSim.Data.TeleportRule r) { rule = r; XrTeleportPoint.Rule = r; }

        bool aiming;
        XrTeleportPoint target;
        readonly List<Vector3> arcPoints = new List<Vector3>();

        public IReadOnlyList<XrTeleportPoint> Points => points;
        public bool Aiming => aiming;
        public XrTeleportPoint Target => target;
        public event System.Action<XrTeleportPoint> Teleported;

        public void Setup(XROrigin o, TeleportationProvider p, Transform aim, LineRenderer line, XrTeleportPoint[] pts, string startId = null, XrFader f = null)
        {
            origin = o; provider = p; aimOrigin = aim; arc = line; points = pts; fader = f;
            if (!string.IsNullOrEmpty(startId)) startPointId = startId;
        }

        /// <summary>目前所在的站點（傳送後記在 xr.point；共用同一點的兩站都算）。</summary>
        public XrTeleportPoint Current => Find(Sim.State?.Get("xr.point") ?? "");

        Transform Head => origin != null && origin.Camera != null ? origin.Camera.transform : null;

        void Start()
        {
            XrTeleportPoint.Rule = rule;
            if (arc != null) arc.enabled = false;
            foreach (var p in points) p.ShowMarker(false, false, false, null);   // 名稱只在瞄準時顯示；地上的標記由 XrStations 每幀上色
        }

        public XrTeleportPoint Find(string id) => points.FirstOrDefault(p => p.PointId == id);

        /// <summary>開場站點：課程資料（course.start_station），R1 沒有課程就用 StationSet.startStationId。</summary>
        public XrTeleportPoint StartPoint => Find(Sim.State?.Get(SimIds.CourseStartStation) ?? "") ?? Find(startPointId) ?? points.FirstOrDefault();

        /// <summary>頭離 XR Origin 的高度（追蹤有效時約等於眼高）。</summary>
        public float HeadHeight => origin != null ? origin.CameraInOriginSpaceHeight : 0f;

        public void PlaceAtStart()
        {
            var p = StartPoint;
            if (p != null) TeleportTo(p.transform.position, p.transform.rotation, p, immediate: true);
        }

        public void BeginAim() { aiming = true; if (arc != null) arc.enabled = true; }

        public void EndAim()
        {
            if (!aiming) return;
            aiming = false;
            if (arc != null) arc.enabled = false;
            foreach (var p in points) p.ShowMarker(false, false, false, null);
            if (target == null) return;
            if (!target.Unlocked(Sim.State)) { Toast(target.LockedMessage); target = null; return; }
            TeleportTo(target.transform.position, target.transform.rotation, target);
            target = null;
        }

        void Update()
        {
            if (!aiming || aimOrigin == null) return;
            var landing = TraceArc(out bool blocked);
            target = PickTarget(points, landing, blocked, snapRadius, Current?.Floor, Sim.State?.GetBool(LiftControl.RoofOpenKey) ?? false);
            bool locked = target != null && !target.Unlocked(Sim.State);
            foreach (var p in points) p.ShowMarker(true, p == target, !p.Unlocked(Sim.State), Head);
            if (arc != null)
            {
                var c = target == null ? new Color(0.8f, 0.8f, 0.8f) : locked ? new Color(1f, 0.25f, 0.2f) : new Color(0.25f, 1f, 0.45f);
                arc.startColor = arc.endColor = c;
                if (target != null) arcPoints[arcPoints.Count - 1] = target.transform.position + Vector3.up * 0.02f;
                arc.positionCount = arcPoints.Count;
                arc.SetPositions(arcPoints.ToArray());
            }
        }

        /// <summary>沿拋物線取樣到碰到東西或落到地面高度，回傳落點。</summary>
        Vector3 TraceArc(out bool blocked)
        {
            float floor = origin != null ? origin.transform.position.y : 0f;
            return TeleportArc.Trace(aimOrigin.position, aimOrigin.forward * launchSpeed, floor, SceneLayers.TeleportMask, arcPoints, out blocked);
        }

        /// <summary>試玩修正 第 1 點：拋物線撞到邊界就沒有目標；目標要在目前的區域，或車頂開放時平台和車頂互通（不能隔著欄杆傳送到別區）。currentFloor 空白＝還不在任何站點。</summary>
        public static XrTeleportPoint PickTarget(IEnumerable<XrTeleportPoint> pts, Vector3 landing, bool blocked, float snapRadius, string currentFloor, bool roofOpen)
        {
            if (blocked) return null;
            return pts.Where(p => Flat(p.transform.position - landing).magnitude <= snapRadius && Connected(currentFloor, p.Floor, roofOpen))
                      .OrderBy(p => Flat(p.transform.position - landing).magnitude).FirstOrDefault();
        }

        /// <summary>兩個區域有沒有相連（純函式）。</summary>
        public static bool Connected(string from, string to, bool roofOpen) =>
            string.IsNullOrEmpty(from) || from == to || (roofOpen && (from == "Platform" && to == "Roof" || from == "Roof" && to == "Platform"));



        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>帶我到目標（舊行為的備援）：移到離目標最近（且已解鎖）的站點，並面向目標。</summary>
        public void FocusOn(Vector3 targetPos)
        {
            var s = Sim.State;
            var best = points.Where(p => p.Unlocked(s)).OrderBy(p => Flat(p.transform.position - targetPos).magnitude).FirstOrDefault();
            if (best == null) return;
            GoTo(best, targetPos);
        }

        /// <summary>
        /// B 鍵：傳到指定站點並面向目標。結果：Moved（已傳送）、AlreadyThere（已經在這站，不傳）、Locked（還沒開放，只顯示原因）、Missing。
        /// </summary>
        public enum GoResult { Moved, AlreadyThere, Locked, Missing }

        public GoResult GoToStation(string stationId, Vector3? lookAt)
        {
            var p = Find(stationId);
            var r = (GoResult)XrTeleportPoint.Decide(p, Current, Sim.State);
            if (r == GoResult.Locked) Toast(p.LockedMessage);
            if (r == GoResult.Moved) GoTo(p, lookAt);
            return r;
        }

        void GoTo(XrTeleportPoint p, Vector3? lookAt)
        {
            var dir = lookAt.HasValue ? Flat(lookAt.Value - p.transform.position) : Vector3.zero;
            var rot = dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir, Vector3.up) : p.transform.rotation;
            TeleportTo(p.transform.position, rot, p);
        }

        /// <summary>重設視角：回到目前最近的傳送點中心、面向該點的方向。</summary>
        public void Recenter()
        {
            if (Head == null) return;
            var near = points.OrderBy(p => Flat(p.transform.position - Head.position).magnitude).FirstOrDefault();
            if (near != null) TeleportTo(near.transform.position, near.transform.rotation, near, immediate: true);
        }

        public void TeleportTo(Vector3 pos, Quaternion rot, XrTeleportPoint point, bool immediate = false)
        {
            if (origin == null) return;
            // 淡黑轉場：0.2 s 淡出 → 傳送 → 0.2 s 淡入（開場擺位不淡出）
            if (!immediate && fader != null && isActiveAndEnabled) { StartCoroutine(FadeTeleport(pos, rot, point)); return; }
            TeleportNow(pos, rot, point, immediate);
        }

        IEnumerator FadeTeleport(Vector3 pos, Quaternion rot, XrTeleportPoint point)
        {
            yield return fader.FadeTo(1f);
            TeleportNow(pos, rot, point, true);
            yield return null;
            yield return fader.FadeTo(0f);
            Teleported?.Invoke(point);
        }

        void TeleportNow(Vector3 pos, Quaternion rot, XrTeleportPoint point, bool immediate)
        {
            if (!immediate && provider != null && provider.system != null)
            {
                provider.QueueTeleportRequest(new TeleportRequest
                {
                    destinationPosition = pos, destinationRotation = rot, matchOrientation = MatchOrientation.TargetUpAndForward
                });
            }
            else
            {
                origin.MatchOriginUpCameraForward(rot * Vector3.up, rot * Vector3.forward);
                origin.MoveCameraToWorldLocation(pos + origin.Origin.transform.up * origin.CameraInOriginSpaceHeight);
            }
            if (Sim.State != null) Sim.State.Set("xr.point", point != null ? point.PointId : "");
            StartCoroutine(AfterTeleport(point, !immediate));
        }

        IEnumerator AfterTeleport(XrTeleportPoint point, bool byPlayer)
        {
            yield return null;
            yield return null;
            Sim.State?.SetFloat("xr.recenter", Time.time);
            if (byPlayer) Teleported?.Invoke(point);   // 開場擺位、置中不算玩家傳送
        }

        static void Toast(string text) { if (Sim.State == null) return; Sim.State.Set("ui.toast", ""); Sim.State.Set("ui.toast", text); Sim.State.SetFloat("ui.toast_time", Time.time); }
    }
}
