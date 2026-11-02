using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Recording
{
    /// <summary>
    /// 重播（SimMode.Replay 才由 App 加上）：依時間擺放 ReplayTracked 物件、套用黑板差異、重新發出介面需要的事件（isReplay=true）。
    /// 控制（ActionRequested 目標 replay）：play／pause／toggle、speed:0.5|1|2|4、seek:秒、error:序號（跳到該錯誤前 5 s）、step:序號、view:trainee|free、exit。
    /// 介面讀黑板 replay.*（時間、長度、速度、錯誤標記、步驟清單）。
    /// 視角：預設第三人稱自由視角（沿用 OrbitCamera），場景中半透明的頭盔、雙手和射線代表學員；「學員視角」位置跟著當時的頭、旋轉 0.3 s 低通並去掉側傾。
    /// VR 裡看重播時不會把紀錄的頭位姿套到觀看者身上（學員視角只在桌面可用）。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class ReplayPlayer : MonoBehaviour
    {
        public const float JumpBeforeError = 5f;
        public static readonly float[] Speeds = { 0.5f, 1f, 2f, 4f };

        ReplayTimeline timeline;
        double time;
        float speed = 1f;
        bool playing = true, bound, traineeView;
        float lastPublished = -1f;
        List<ReplayTracked> tracked = new List<ReplayTracked>();
        List<RecordLine> errors = new List<RecordLine>(), steps = new List<RecordLine>();

        // 學員影子
        GameObject ghost;
        Transform ghostHead, ghostLeft, ghostRight;
        LineRenderer ghostRay;
        GameObject heldModel;
        string heldTool = "";
        Quaternion viewRot = Quaternion.identity;
        bool viewInit;

        public static ReplayPlayer Instance { get; private set; }
        public ReplayTimeline Timeline => timeline;
        public double CurrentTime => time;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (bound && Sim.Bus != null) Sim.Bus.Unsubscribe<ActionRequested>(OnRequest);
        }

        void Start()
        {
            string folder = SimSession.ReplayFolder;
            if (!Directory.Exists(folder)) { Debug.LogError("[Replay] 找不到紀錄資料夾：" + folder); return; }
            timeline = ReplayTimeline.Load(folder);
            timeline.Attach(Sim.Bus, Sim.State);
            tracked = ReplayTracked.FindAll();
            errors = timeline.Errors;
            steps = timeline.StepStarts;
            Sim.Bus.Subscribe<ActionRequested>(OnRequest);
            bound = true;
            gameObject.AddComponent<ReplayPoseApplier>().player = this;
            BuildGhost();
            var s = Sim.State;
            var m = timeline.meta;
            s.SetBool("replay.active", true);
            s.Set("replay.folder", folder);
            s.Set("replay.title", $"{m.Str("trainee")}　{FormatDate(m.Str("startedAt"))}　{m.Str("mode")}／{m.Str("input")}");
            s.SetFloat("replay.duration", (float)timeline.Duration);
            s.Set("replay.markers", string.Join("\n", errors.Select(e => string.Join("|", e.t.ToString("0.###", CultureInfo.InvariantCulture), e.S("severity"),
                Clean(e.S("name")), Clean(timeline.catalog.NameOf(e.step))))));
            s.Set("replay.steps", string.Join("\n", steps.Select(st => st.t.ToString("0.###", CultureInfo.InvariantCulture) + "|" + Clean(timeline.catalog.NameOf(st.step)))));
            timeline.Seek(0);
            time = 0;
            Publish(true);
            Debug.Log($"[Replay] 載入 {folder}：{timeline.lines.Count} 行、{timeline.snapshots.Count} 筆快照、{timeline.poses.Count} 條位姿、長度 {timeline.Duration:0.0} s");
        }

        static string Clean(string s) => (s ?? "").Replace("|", "／").Replace("\n", " ");
        static string FormatDate(string iso) => System.DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d.ToString("yyyy-MM-dd HH:mm") : iso;

        void OnRequest(ActionRequested e)
        {
            if (e.targetId != "replay" || timeline == null) return;
            string t = e.text ?? "";
            int colon = t.IndexOf(':');
            string cmd = colon >= 0 ? t.Substring(0, colon) : t, arg = colon >= 0 ? t.Substring(colon + 1) : "";
            float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float f);
            int.TryParse(arg, out int i);
            switch (cmd)
            {
                case "play": playing = true; if (time >= timeline.Duration) SeekTo(0); break;
                case "pause": playing = false; break;
                case "toggle": playing = !playing; if (playing && time >= timeline.Duration) SeekTo(0); break;
                case "speed": if (f > 0) speed = f; break;
                case "seek": SeekTo(f); break;
                case "seek_ratio": SeekTo(f * timeline.Duration); break;
                case "error": if (i >= 0 && i < errors.Count) { SeekTo(errors[i].t - JumpBeforeError); playing = true; } break;
                case "step": if (i >= 0 && i < steps.Count) { SeekTo(steps[i].t); playing = true; } break;
                case "view":
                    traineeView = arg == "trainee" && !InputMode.XrActive;
                    if (arg == "trainee" && InputMode.XrActive) Sim.State.Set("ui.toast", "VR 中看重播不套用學員的頭部位置；學員視角只在桌面可用");
                    viewInit = false;
                    break;
                case "exit": Sim.Bus.Publish(new ActionRequested { targetId = "app.replay_exit", toolId = "", time = SimClock.Now }); break;
            }
            Publish(true);
        }

        public void SeekTo(double t)
        {
            time = System.Math.Max(0, System.Math.Min(timeline.Duration, t));
            timeline.Seek(time);
            ApplyLift();
            Publish(true);
        }

        void Update()
        {
            if (timeline == null) return;
            if (playing)
            {
                time += Time.unscaledDeltaTime * speed;
                if (time >= timeline.Duration) { time = timeline.Duration; playing = false; }
                timeline.AdvanceTo(time);
            }
            ApplyLift();
            Publish(false);
        }

        /// <summary>升降機構：寫回 lift.value，PantographPoseReporter 會照著擺姿態。</summary>
        void ApplyLift()
        {
            if (timeline.TryGetValue(RecordFiles.LiftChannel, time, out var lift)) Sim.State.SetFloat(SimIds.LiftValue, lift);
        }

        void Publish(bool force)
        {
            var s = Sim.State;
            float t = Mathf.Round((float)time * 20f) / 20f;
            if (force || Mathf.Abs(t - lastPublished) >= 0.05f) { s.SetFloat("replay.time", t); lastPublished = t; }
            s.SetBool("replay.playing", playing);
            s.SetFloat("replay.speed", speed);
            s.SetBool("replay.trainee_view", traineeView);
            foreach (var kv in timeline.PanelPosesAt(time))   // R1 T3：面板位置（XrPanels 照 xr.panel.replay.<id> 擺）
                if (s.Get("xr.panel.replay." + kv.Key) != kv.Value) s.Set("xr.panel.replay." + kv.Key, kv.Value);
        }

        // ---------- 學員影子與視角（LateUpdate，在零件自己的擺放之後） ----------
        void BuildGhost()
        {
            var mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.35f, 0.8f, 1f, 0.38f) };
            ghost = new GameObject("ReplayGhost");
            GameObject Part(string n, PrimitiveType type, Vector3 scale, Transform parent, Vector3 local = default)
            {
                var g = GameObject.CreatePrimitive(type);
                g.name = n;
                Destroy(g.GetComponent<Collider>());
                g.GetComponent<Renderer>().sharedMaterial = mat;
                g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                g.transform.SetParent(parent, false);
                g.transform.localPosition = local;
                g.transform.localScale = scale;
                return g;
            }
            ghostHead = new GameObject("Head").transform; ghostHead.SetParent(ghost.transform, false);
            Part("Helmet", PrimitiveType.Sphere, new Vector3(0.2f, 0.22f, 0.24f), ghostHead);
            Part("Visor", PrimitiveType.Cube, new Vector3(0.17f, 0.07f, 0.06f), ghostHead, new Vector3(0, 0.0f, 0.1f));
            ghostLeft = new GameObject("LeftHand").transform; ghostLeft.SetParent(ghost.transform, false);
            Part("Model", PrimitiveType.Cube, new Vector3(0.05f, 0.04f, 0.12f), ghostLeft);
            ghostRight = new GameObject("RightHand").transform; ghostRight.SetParent(ghost.transform, false);
            Part("Model", PrimitiveType.Cube, new Vector3(0.05f, 0.04f, 0.12f), ghostRight);
            ghostRay = new GameObject("Ray").AddComponent<LineRenderer>();
            ghostRay.transform.SetParent(ghost.transform, false);
            ghostRay.sharedMaterial = mat; ghostRay.widthMultiplier = 0.006f; ghostRay.positionCount = 2;
            ghostRay.startColor = new Color(0.4f, 0.9f, 1f, 0.7f); ghostRay.endColor = new Color(0.4f, 0.9f, 1f, 0.05f);
        }

        internal void ApplyPoses()
        {
            if (timeline == null) return;
            foreach (var t in tracked)
            {
                if (t == null) continue;
                string id = t.Id;
                if (timeline.TryGetPose(id, time, out var p) || timeline.TryGetSnapshotPose(id, time, out p))
                    t.transform.SetPositionAndRotation(p.pos, p.rot.normalized);
            }
            bool hasHead = timeline.TryGetPose(RecordFiles.Head, time, out var head);
            bool hasL = timeline.TryGetPose(RecordFiles.LeftHand, time, out var left);
            bool hasR = timeline.TryGetPose(RecordFiles.RightHand, time, out var right);
            if (ghost != null)
            {
                ghostHead.gameObject.SetActive(hasHead && !traineeView);
                if (hasHead) ghostHead.SetPositionAndRotation(head.pos, head.rot);
                ghostLeft.gameObject.SetActive(hasL);
                if (hasL) ghostLeft.SetPositionAndRotation(left.pos, left.rot);
                ghostRight.gameObject.SetActive(hasR);
                ghostRay.enabled = hasR;
                if (hasR)
                {
                    ghostRight.SetPositionAndRotation(right.pos, right.rot);
                    ghostRay.SetPosition(0, right.pos);
                    ghostRay.SetPosition(1, right.pos + right.rot * Vector3.forward * 1.5f);
                }
                UpdateHeldModel(hasR ? ghostRight : hasHead ? ghostHead : null);
            }
            if (traineeView && hasHead && !InputMode.XrActive && Camera.main != null)
            {
                var flatRot = Quaternion.LookRotation(head.rot * Vector3.forward, Vector3.up);   // 去掉側傾
                if (!viewInit) { viewRot = flatRot; viewInit = true; }
                else viewRot = Quaternion.Slerp(viewRot, flatRot, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.3f));
                Camera.main.transform.SetPositionAndRotation(head.pos, Quaternion.LookRotation(viewRot * Vector3.forward, Vector3.up));
            }
        }

        /// <summary>影子的右手拿著當時手上的工具（工具架模型的複製品）。</summary>
        void UpdateHeldModel(Transform hand)
        {
            string tool = Sim.State.Get(SimIds.ActiveTool);
            if (tool == heldTool && (heldModel != null || string.IsNullOrEmpty(tool))) return;
            heldTool = tool;
            if (heldModel != null) Destroy(heldModel);
            heldModel = null;
            if (string.IsNullOrEmpty(tool) || hand == null || Sim.Registry == null) return;
            string slot = "";
            foreach (var entry in Sim.State.Get("tools.list").Split(';'))
            {
                var f = entry.Split('|');
                if (f.Length >= 2 && f[1] == tool) slot = f[0];
            }
            if (!Sim.Registry.TryGetTransform("env.tool_rack.slot_" + slot, out var slotT)) return;
            var model = slotT.Cast<Transform>().FirstOrDefault(c => c.name.StartsWith("Model_"));
            if (model == null) return;
            heldModel = Instantiate(model.gameObject, hand, false);
            foreach (var tr in heldModel.GetComponentsInChildren<ReplayTracked>(true)) Destroy(tr);
            heldModel.SetActive(true);
            heldModel.transform.localPosition = new Vector3(0, 0, 0.08f);
            heldModel.transform.localRotation = Quaternion.Euler(70f, 0, 0);
            var s = model.lossyScale; var ps = hand.lossyScale;
            heldModel.transform.localScale = new Vector3(s.x / ps.x, s.y / ps.y, s.z / ps.z);
        }
    }

    /// <summary>在所有零件的 LateUpdate 之後擺放被追蹤物件與學員影子。</summary>
    [DefaultExecutionOrder(1000)]
    public class ReplayPoseApplier : MonoBehaviour
    {
        public ReplayPlayer player;
        void LateUpdate() { if (player != null) player.ApplyPoses(); }
    }

    /// <summary>重播模式的守門者：所有會改變狀態的動作一律不執行（不發錯誤、不計分）。</summary>
    public class ReplayGuard : IActionGuard
    {
        public GuardResult Check(ActionRequest request) =>
            request.kind == ActionKind.Inspect ? GuardResult.Allow : GuardResult.Block("REPLAY");
    }
}
