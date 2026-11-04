using PantographSim.Core;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// 開場約 30 秒的控制器教學（扳機 → 握把 → 傳送 → 轉向 → 看手腕面板 → Y 說明），做到就打勾，完成後介面出現任務卡（扳機點「開始」）。
    /// 黑板：xr.tutorial.active、xr.tutorial.step（0–6）、xr.tutorial.done。同一次執行（含「再做一次」重載場景）只教一次。
    /// 也管手勢小卡：第一次拿起某工具時顯示 xr.gesture.tool；學員第一次用它完成主要動作、且至少顯示 6 秒後才淡出。
    /// </summary>
    public class XrCoach : MonoBehaviour
    {
        public const int StepCount = 8;
        const float MinGestureSeconds = 6f, FadeSeconds = 1f;
        static bool doneThisRun;

        [SerializeField] XrRig rig;
        float wristSince = -1f;
        bool bound;

        string gestureTool = "";
        float gestureShownAt, gestureDoneAt = -1f;
        readonly System.Collections.Generic.HashSet<string> gestureSeen = new System.Collections.Generic.HashSet<string>();

        public void Setup(XrRig r) => rig = r;

        ISimState S => Sim.State;
        int Step => (int)S.GetFloat("xr.tutorial.step");

        public void Begin()
        {
            if (S == null) return;
            if (doneThisRun || SimSession.IsReplay) { Finish(); return; }   // 看重播不需要控制器教學
            S.SetFloat("xr.tutorial.step", 0);
            S.SetBool("xr.tutorial.done", false);
            S.SetBool("xr.tutorial.active", true);
        }

        void Finish()
        {
            doneThisRun = true;
            S.SetBool("xr.tutorial.active", false);
            S.SetBool("xr.tutorial.done", true);
        }

        void Advance(int from)
        {
            if (!S.GetBool("xr.tutorial.active") || Step != from) return;
            S.SetFloat("xr.tutorial.step", from + 1);
            if (from + 1 >= StepCount) Finish();
        }

        /// <summary>XrPlayer 每執行一個指令就通知一次。</summary>
        public void Notify(XrCommand c)
        {
            if (S == null) return;
            switch (c.command)
            {
                case "Use": if (c.pressed) Advance(0); break;
                case "Grab": if (c.pressed) Advance(1); break;
                case "TurnLeft": case "TurnRight": Advance(5); break;
                case "ToggleHelp": Advance(7); break;
            }
        }

        void OnTeleported(XrTeleportPoint p) { Advance(3); Advance(2); }   // 先判第 4 項：同一次傳送不會連過兩項
        void OnPanelMoved(PanelMoved e) => Advance(4);   // T3：用左握把抓把手移過面板

        void Bind()
        {
            if (bound || !Sim.Ready) return;
            Sim.Bus.Subscribe<ActionRequested>(OnRequest);
            Sim.Bus.Subscribe<ToolSelected>(OnToolSelected);
            Sim.Bus.Subscribe<ActionPerformed>(OnPerformed);
            Sim.Bus.Subscribe<MeasurementTaken>(OnMeasured);
            Sim.Bus.Subscribe<StateChanged>(OnState);
            Sim.Bus.Subscribe<PanelMoved>(OnPanelMoved);
            var tp = GetComponent<XrTeleport>();
            if (tp != null) tp.Teleported += OnTeleported;
            bound = true;
        }

        void OnDestroy()
        {
            if (!bound || Sim.Bus == null) return;
            Sim.Bus.Unsubscribe<ActionRequested>(OnRequest);
            Sim.Bus.Unsubscribe<ToolSelected>(OnToolSelected);
            Sim.Bus.Unsubscribe<ActionPerformed>(OnPerformed);
            Sim.Bus.Unsubscribe<MeasurementTaken>(OnMeasured);
            Sim.Bus.Unsubscribe<StateChanged>(OnState);
            Sim.Bus.Unsubscribe<PanelMoved>(OnPanelMoved);
        }

        void OnRequest(ActionRequested e)
        {
            if (e.targetId == "xr.tutorial" && e.text == "skip") Finish();
        }

        // ---------- 手勢小卡 ----------
        void ShowGesture(string key)
        {
            if (gestureSeen.Contains(key)) return;
            gestureSeen.Add(key);
            gestureTool = key; gestureShownAt = Time.time; gestureDoneAt = -1f;
            S.SetFloat("xr.gesture.fade", 1f);
            S.Set("xr.gesture.tool", key);
        }

        void OnToolSelected(ToolSelected e)
        {
            if (!string.IsNullOrEmpty(e.toolId)) ShowGesture(e.toolId);
            else if (gestureTool != "carried" && gestureTool.Length > 0 && gestureDoneAt < 0) { gestureTool = ""; S.Set("xr.gesture.tool", ""); }
        }

        void MainActionDone(string key) { if (key == gestureTool && gestureDoneAt < 0) gestureDoneAt = Time.time; }

        void OnPerformed(ActionPerformed e) { if (!string.IsNullOrEmpty(e.toolId)) MainActionDone(e.toolId); }
        void OnMeasured(MeasurementTaken e) => MainActionDone(e.toolId);

        void OnState(StateChanged e)
        {
            foreach (var c in e.Changes)
            {
                if (!c.key.StartsWith("part.carbon_strip_") || !c.key.EndsWith(".state")) continue;
                if (c.newValue == "Carried") ShowGesture("carried");
                else if (c.newValue == "NewMounted") MainActionDone("carried");
            }
        }

        void Update()
        {
            Bind();
            if (!bound || !InputMode.XrActive) return;
            // 第 6 項：抬起左手看手腕面板（左手在視線中央附近、0.75 m 內，停 0.4 秒）
            if (S.GetBool("xr.tutorial.active") && Step == 6 && rig != null && rig.Head != null && rig.LeftHand != null)
            {
                var to = rig.LeftHand.position - rig.Head.position;
                bool look = to.magnitude < 0.75f && Vector3.Dot(rig.Head.forward, to.normalized) > 0.85f;
                if (!look) wristSince = -1f;
                else if (wristSince < 0f) wristSince = Time.time;
                else if (Time.time - wristSince > 0.4f) Advance(6);
            }
            if (gestureTool.Length > 0 && gestureDoneAt >= 0f)
            {
                float start = Mathf.Max(gestureDoneAt, gestureShownAt + MinGestureSeconds);
                float fade = 1f - Mathf.Clamp01((Time.time - start) / FadeSeconds);
                S.SetFloat("xr.gesture.fade", Mathf.Round(fade * 20f) / 20f);
                if (fade <= 0f) { gestureTool = ""; S.Set("xr.gesture.tool", ""); }
            }
        }
    }
}
