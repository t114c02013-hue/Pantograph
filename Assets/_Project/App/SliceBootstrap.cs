using System.IO;
using System.Linq;
using PantographSim.Core;
using PantographSim.Recording;
using PantographSim.Data;
using PantographSim.Interaction;
using PantographSim.Procedures;
using PantographSim.Safety;
using PantographSim.Training;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PantographSim.App
{
    /// <summary>
    /// 碳滑板切片的唯一組裝點：建立 EventBus、SimState、場景登錄，註冊守門者（SafetyGuard → ErrorDetector）、
    /// 步驟閘門，載入程序（PRC）、安全規則（SR）、情境（SCN），建立訓練服務。
    /// 任務卡按下開始（ActionRequested app.start）後才套用情境並啟動 ProcedureRunner。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class SliceBootstrap : MonoBehaviour
    {
        [SerializeField] ProcedureDefinition procedure;
        [SerializeField] SafetyRuleDefinition[] safetyRules = new SafetyRuleDefinition[0];
        [SerializeField] ScenarioDefinition[] scenarios = new ScenarioDefinition[0];
        [SerializeField] ToolDefinition[] tools = new ToolDefinition[0];
        [Tooltip("R1.5b b4：可選的課程（日常／定期）；R1 原 25 步用上面的 procedure")] [SerializeField] CourseDef[] courses = new CourseDef[0];
        [Tooltip("預設課程（Courses.json 的 defaultCourse；b4c 做完前是 r1）")] [SerializeField] string defaultCourse = SimIds.CourseR1;
        [Tooltip("選單上 R1 的名稱、各課程後面的註記（例「開發中」），都讀 Courses.json")] [SerializeField] string r1Title = "", courseNotes = "";
        CourseDef course;
        [Tooltip("R1.5b b4：吊帶判定的答案（RiggingDef.defectiveSlings）")] [SerializeField] RiggingDef rigging;
        public void SetRigging(RiggingDef r) => rigging = r;

        EventBus bus;
        SimState state;
        ProcedureRunner runner;
        ErrorDetector detector;
        FaultClock faultClock;
        ModeController mode;
        Scoring scoring;
        HintService hints;
        ErrorFeedback feedback;
        bool starting;

        public ProcedureRunner Runner => runner;

        public void Configure(ProcedureDefinition prc, SafetyRuleDefinition[] rules, ScenarioDefinition[] scns, ToolDefinition[] toolDefs)
        {
            procedure = prc; safetyRules = rules; scenarios = scns; tools = toolDefs;
        }

        /// <summary>R1.5b b4：課程清單、預設課程、選單文字（場景建置時從 Courses.json 設定）。</summary>
        public void SetCourses(CourseDef[] list, string defaultId, string r1Name, string notes) { courses = list; defaultCourse = defaultId; r1Title = r1Name; courseNotes = notes; }

        /// <summary>這一場的課程：SimSession 選過的，否則資料的預設；r1＝原 25 步。</summary>
        public static string ResolveCourse(string chosen, string fallback) => string.IsNullOrEmpty(chosen) ? (string.IsNullOrEmpty(fallback) ? SimIds.CourseR1 : fallback) : chosen;

        void Awake()
        {
            ServiceLocator.Clear();
            // 輸入模式：預設桌面；XR 模組偵測到頭盔後切到 VR（-desktop 或 PANTO_DESKTOP=1 強制桌面）
            InputMode.ResetForScene();
            InputMode.ForceDesktop = System.Environment.GetCommandLineArgs().Contains("-desktop") || System.Environment.GetEnvironmentVariable("PANTO_DESKTOP") == "1";
            InputMode.Changed += OnInputModeChanged;
            SimClock.Source = () => Time.time;
            bus = new EventBus();
            state = new SimState(bus);
            var registry = new SceneRegistry();
            ServiceLocator.Register<IEventBus>(bus);
            ServiceLocator.Register<ISimState>(state);
            ServiceLocator.Register(registry);
            ServiceLocator.Register<ISceneLocator>(registry);

            string courseId = ResolveCourse(SimSession.CourseId, defaultCourse);
            course = courseId == SimIds.CourseR1 ? null : courses.FirstOrDefault(c => c != null && c.courseId == courseId);
            if (course != null) procedure = course.procedure;
            ServiceLocator.Register(procedure);   // 紀錄模組用來列出步驟名稱與參考時間
            var gates = new CompositeStepGate();
            var guard = new CompositeActionGuard();
            if (SimSession.IsReplay && !System.IO.Directory.Exists(SimSession.ReplayFolder)) SimSession.EnterTraining();
            if (SimSession.IsReplay)
            {
                // 重播：不啟動 ProcedureRunner／SafetyGuard／ErrorDetector；任何會改變狀態的動作一律擋下（不發錯誤、不計分）
                guard.Add(new ReplayGuard());
                runner = new ProcedureRunner(procedure, bus, state, gates);
            }
            else
            {
                var safety = new SafetyGuard(bus, state, safetyRules) { SkipDeprecated = course != null };
                gates.Add(safety);
                runner = new ProcedureRunner(procedure, bus, state, gates);
                detector = new ErrorDetector(runner, bus, state);
                faultClock = new FaultClock(runner, bus, state, course?.faults);   // v3.10 第 8 項：故障處理時間另列
                guard.Add(safety);      // 安全規則優先（Critical）
                guard.Add(detector);    // 再來是拿錯工具／點錯目標（Minor）
            }
            ServiceLocator.Register<IActionGuard>(guard);
            ServiceLocator.Register<IStepGate>(gates);

            mode = new ModeController(state);
            hints = new HintService(bus, state);
            feedback = new ErrorFeedback(bus, state);
            scoring = new Scoring(bus, state);
            scoring.IncompleteSteps = () => runner.Steps
                .Where(s => !state.GetBool(s.DoneKey) && !state.GetBool("step." + s.RunId + ".skipped")).Select(s => s.RunId).ToArray();

            WriteTaskCard();
            // 代做腳本目前只有 R1；預設完成的步驟不會停，不列
            state.Set(SimIds.StepsList, course != null ? "" : string.Join(";", runner.Steps.Where(s => !s.def.presetDone).Select(s =>
                s.RunId + "|" + s.defIndex + " " + s.def.title + (s.sideLabel.Length == 0 || s.def.title.Contains(s.sideLabel) ? "" : "（" + s.sideLabel + "）"))));
            var stripRules = ToolRulesDef.Load();   // terry 2026-11-13：碳滑板厚度基準給介面顯示（UI 不引用 Data）
            state.Set("strip.limit_text", $"{stripRules.stripLimitMm:0.0} mm");
            state.Set("strip.new_text", $"{stripRules.stripNewMm:0.0} ± {stripRules.stripNewTolMm:0.0} mm");
            state.Set(SimIds.CourseId, course != null ? course.courseId : SimIds.CourseR1);   // 錄影、成績都記 courseId
            state.Set("app.courses.list", string.Join(";", new[] { SimIds.CourseR1 + "|" + r1Title + "|" }.Concat(courses.Where(c => c != null).Select(c => c.courseId + "|" + c.title + "|" + NoteOf(c.courseId)))));
            if (course != null) PantographSim.Worksite.CourseApplier.Apply(course, state);   // 課程覆寫（擺放、天車停駐、升弓上限）
            if (course != null) state.Set(SimIds.CourseStartStation, course.startStationId);   // VR 開場站點
            var faults = course?.faults ?? new CourseDef.FaultOption[0];   // D43：可選故障，預設值讀資料
            foreach (var f in faults) state.SetBool(f.key, f.defaultOn);
            state.Set("app.faults.list", string.Join(";", faults.Select(f => f.key + "|" + f.title)));
            bus.Subscribe<ActionRequested>(OnRequest);
            bus.Subscribe<StepStarted>(e => { if (!e.isReplay) SlingLogic.OnStepStarted(state, e.stepId); });   // v3.10：where＝IN 的割傷到 IN_01 才出現
            if (SimSession.IsReplay) SetupReplay();
        }

        string NoteOf(string id) => (courseNotes ?? "").Split(';').Select(x => x.Split('|')).Where(x => x.Length == 2 && x[0] == id).Select(x => x[1]).FirstOrDefault() ?? "";

        void Start()
        {
            RegisterPoseSource();
            if (string.IsNullOrEmpty(SimSession.PendingJump)) return;
            string jump = SimSession.PendingJump;
            SimSession.PendingJump = "";
            bus.Publish(new ActionRequested { targetId = "app.jump", text = jump, time = SimClock.Now });
        }

        /// <summary>卡關偵測：每 0.5 s 只讀地檢查目前步驟（不推進步驟；見 ProcedureRunner.Poll）。重播時 runner 沒有開始，不作用。</summary>
        string dwellId = ""; float dwellT;
        ToolRulesDef toolRules;
        bool toolRulesLoaded;

        void Update()
        {
            runner?.Poll(Time.time);
            if (state == null) return;
            if (!toolRulesLoaded) { toolRules = ToolRulesDef.Load(); toolRulesLoaded = true; }
            (dwellId, dwellT) = InspectLogic.Dwell(state, dwellId, dwellT, Time.deltaTime, toolRules);   // R1.5b b4：指著停一下＝查看（桌面、VR 同一套）
        }

        /// <summary>學員位姿來源：VR＝XR Rig，桌面＝桌面攝影機（兩者都實作 IPoseSource）。</summary>
        void RegisterPoseSource()
        {
            var sources = FindObjectsOfType<MonoBehaviour>(true).OfType<IPoseSource>().ToList();
            var want = sources.FirstOrDefault(p => p.SourceName == (InputMode.XrActive ? "xr" : "desktop")) ?? sources.FirstOrDefault();
            if (want != null) ServiceLocator.Register<IPoseSource>(want);
        }

        /// <summary>重播模式：關掉判定以外的輸入（工具、點選、升降），保留環繞攝影機；加上 ReplayPlayer。</summary>
        void SetupReplay()
        {
            foreach (var b in FindObjectsOfType<Picker>(true)) b.enabled = false;
            foreach (var b in FindObjectsOfType<DragPlace>(true)) b.enabled = false;
            foreach (var b in FindObjectsOfType<InspectMode>(true)) b.enabled = false;
            foreach (var b in FindObjectsOfType<StepFocus>(true)) b.enabled = false;
            foreach (var b in FindObjectsOfType<LiftKeys>(true)) b.enabled = false;
            foreach (var b in FindObjectsOfType<PantographSim.Tools.ToolBelt>(true)) b.enabled = false;
            foreach (var b in FindObjectsOfType<PantographSim.Tools.ToolTutorialTrigger>(true)) b.enabled = false;
            foreach (var b in FindObjectsOfType<PantographSim.Parts.Mechanism.LiftDriver>(true)) b.enabled = false;
            state.SetBool("training.running", true);
            gameObject.AddComponent<ReplayPlayer>();
        }

        void WriteTaskCard()
        {
            state.Set("task.title", procedure.title);
            state.Set("task.goal", procedure.goal);
            state.Set("task.safety", string.Join("\n", procedure.safetyPoints));
            state.Set("task.tools", string.Join("、", tools.OrderBy(t => t.hotkey).Select(t => $"{t.hotkey} {t.displayName}")));
            state.Set("task.si_steps", string.Join(";", procedure.steps.Where(s => s.stepId.StartsWith("STP_SI_")).Select(s => s.stepId + "|" + s.title)));
            state.Set("scenarios.list", string.Join(";", scenarios.Select(s => s.scenarioId + "|" + s.title)));
            // 降弓容許值只有一個來源：安全規則 SR_Global_NotLowered（步驟條件、介面、檢測點顏色都讀這個鍵）
            var notLowered = safetyRules.FirstOrDefault(r => r != null && r.kind == SafetyRuleKind.NotLowered);
            state.SetFloat(SimIds.PanHeadLowerTolerance, notLowered != null ? notLowered.toleranceCm : 1f);
            state.SetBool("training.running", false);
        }

        void OnRequest(ActionRequested e)
        {
            if (e.targetId == "app.replay" && Directory.Exists(e.text ?? ""))
            {
                SimSession.EnterCourse(PantographSim.Recording.RecordFiles.CourseOf(e.text));   // 回放先載入錄影的課程（舊 R1 錄影沒有 courseId＝r1）
                SimSession.EnterReplay(e.text);
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }
            if (e.targetId == "app.replay_exit")
            {
                SimSession.EnterTraining();
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }
            if (SimSession.IsReplay) return;
            if (e.targetId == "app.start" && !runner.Started && !starting)
            {
                starting = true;
                var parts = (e.text ?? "tutorial:SCN_Default").Split(':');
                var scenario = scenarios.FirstOrDefault(s => s.scenarioId == (parts.Length > 1 ? parts[1] : "SCN_Default")) ?? scenarios.FirstOrDefault();
                mode.Begin(parts[0] == "practice" ? TrainingMode.Practice : TrainingMode.Tutorial, scenario);
                scoring.Start(Time.time);
                SlingLogic.Setup(state, new System.Random());   // v3.10：吊帶割傷故障（教官開了才抽 RM／IN 與第幾條）
                // 等機構套用情境姿態、檢測點寫出實際距離後才開始判定（否則第一個小動作會用到上一個姿態的距離）
                StartCoroutine(StartRunnerAfterPoseSettles());
            }
            else if (e.targetId.StartsWith(InspectLogic.SlingPrefix) && (e.text == "verdict:ok" || e.text == "verdict:ng"))
            {
                InspectLogic.SlingVerdict(state, e.targetId, e.text == "verdict:ok", rigging);   // R1.5b b4（RM_05 f）：吊帶可用／不可用
            }
            else if (e.targetId == "app.fault" && !runner.Started && (course?.faults ?? new CourseDef.FaultOption[0]).Any(f => f.key == e.text))
            {
                state.SetBool(e.text, !state.GetBool(e.text));   // D43：教官在任務卡開關可選故障（開始前）
            }
            else if (e.targetId == "app.course" && !runner.Started)
            {
                SimSession.EnterCourse(e.text);   // 換課程一律重新載入場景（軟體工程），不在同一場景裡切換
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
            else if (e.targetId == "app.jump" && course == null && !state.GetBool(SimIds.AutoRunActive))
            {
                if (!runner.Started && !starting) { StartCoroutine(JumpTo(e.text ?? "")); return; }
                SimSession.PendingJump = e.text;   // 訓練已經開始：重新載入場景再從頭代做
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
            else if (e.targetId == "app.jump_stop" && state.GetBool(SimIds.AutoRunActive))
            {
                jumpStop = true;   // 後台面板「停止代做」：JumpTo 下一幀停下
            }
            else if (e.targetId == "app.restart")
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        /// <summary>後台面板「從這一步開始」（text＝模式:步驟）：遮罩蓋住，代做前面的步驟，交給人後才淡回。</summary>
        System.Collections.IEnumerator JumpTo(string request)
        {
            var parts = request.Split(':');
            var target = parts.Length == 2 ? runner.Steps.FirstOrDefault(s => s.RunId == parts[1]) : null;
            if (target == null) yield break;
            var rules = ToolRulesDef.Load();
            state.Set(SimIds.AutoRunStatus, "");
            state.Set(SimIds.AutoRunText, string.Format(rules.autoRunMaskText, target.defIndex));
            state.SetFloat(SimIds.AutoRunFade, rules.autoRunFadeSeconds);
            state.SetBool(SimIds.AutoRunActive, true);
            var steps = runner.Steps.Where(s => !s.def.presetDone).ToList();
            int total = Mathf.Max(1, steps.IndexOf(target));
            string at = null; float since = Time.realtimeSinceStartup;
            float began = Time.realtimeSinceStartup;
            jumpStop = false;
            void Progress()
            {
                string cur = state.Get("procedure.current");
                if (cur != at) { at = cur; since = Time.realtimeSinceStartup; }
                int i = steps.FindIndex(s => s.RunId == cur);
                string why = state.Get("procedure.stall_text");
                if (why.Length == 0) why = state.Get("procedure.gate_reason");
                var (fraction, text) = AutoRunProgress(i, total, i >= 0 ? StepLabel(steps[i]) : "", Time.realtimeSinceStartup - since, why, rules);
                if (state.Get(SimIds.AutoRunProgressText) != text) state.Set(SimIds.AutoRunProgressText, text);
                if (Mathf.Abs(state.GetFloat(SimIds.AutoRunProgress) - fraction) > 1e-4f) state.SetFloat(SimIds.AutoRunProgress, fraction);
            }
            try
            {
                foreach (int frames in new ProcedureAutoRun { Log = l => Debug.Log("[代做] " + l) }.GoTo(parts[0], target.RunId))
                {
                    for (int i = 0; i < frames && !jumpStop; i++) { Progress(); yield return null; }
                    if (jumpStop) break;
                }
            }
            finally { state.SetBool(SimIds.AutoRunActive, false); }   // 腳本出錯也不能把人關在遮罩裡
            var now = runner.Steps.FirstOrDefault(s => s.RunId == state.Get("procedure.current"));
            Debug.Log($"[代做] {target.RunId}：{(jumpStop ? "停止" : state.Get(SimIds.AutoRunStatus).Length == 0 ? "到了" : "走不到")}，{Time.realtimeSinceStartup - began:0.0} 秒");
            if (jumpStop)
            {
                PointerState.ScriptedRay = null; PointerState.XrTriggerHeld = false;   // 腳本可能停在動作中間
                LiftControl.Release(state);
                state.Set(SimIds.AutoRunStatus, string.Format(rules.autoRunStoppedText, now != null ? StepLabel(now) : "開場"));
            }
            else if (state.Get(SimIds.AutoRunStatus).Length == 0 && now == target)
            {
                state.SetFloat(SimIds.AutoRunProgress, 1f);
                state.Set(SimIds.AutoRunProgressText, string.Format(rules.autoRunDoneText, StepLabel(target)));
            }
        }

        bool jumpStop;

        static string StepLabel(RuntimeStep s) => $"第 {s.defIndex} 步 {s.def.title}" + (s.sideLabel.Length == 0 || s.def.title.Contains(s.sideLabel) ? "" : "（" + s.sideLabel + "）");

        /// <summary>後台面板進度（純函式）：index＝目前這一步在可選步驟裡的位置（-1＝還在開場），total＝目標步驟的位置。同一步停超過 autoRunStuckSeconds 改顯示卡住和原因。</summary>
        public static (float fraction, string text) AutoRunProgress(int index, int total, string label, float stuckSeconds, string reason, ToolRulesDef r)
        {
            int done = Mathf.Clamp(index, 0, total);
            if (index < 0) label = "開場";
            string text = stuckSeconds >= r.autoRunStuckSeconds
                ? string.Format(r.autoRunStuckText, label, stuckSeconds, string.IsNullOrEmpty(reason) ? r.autoRunStuckUnknown : reason)
                : string.Format(r.autoRunProgressText, done, total, label);
            return ((float)done / Mathf.Max(1, total), text);
        }

        System.Collections.IEnumerator StartRunnerAfterPoseSettles()
        {
            for (int i = 0; i < 3; i++) yield return null;   // LiftDriver → PoseReporter → PositionCheck（LateUpdate）
            if (!runner.Started) runner.Start();
        }

        /// <summary>VR 模式：停用桌面相機、環繞攝影機與滑鼠點選（VR 由 XR 模組的控制器射線送同樣的事件）。</summary>
        void OnInputModeChanged()
        {
            RegisterPoseSource();
            if (!InputMode.XrActive) return;
            foreach (var orbit in FindObjectsOfType<OrbitCameraController>())
            {
                orbit.enabled = false;
                var cam = orbit.GetComponent<Camera>();
                if (cam != null) cam.enabled = false;
                var listener = orbit.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
                orbit.gameObject.tag = "Untagged";
            }
            foreach (var p in FindObjectsOfType<Picker>()) p.enabled = false;
            foreach (var i in FindObjectsOfType<InspectMode>()) i.enabled = false;
            foreach (var f in FindObjectsOfType<StepFocus>()) f.enabled = false;
        }

        void OnDestroy()
        {
            InputMode.Changed -= OnInputModeChanged;
            bus?.Unsubscribe<ActionRequested>(OnRequest);
            runner?.Dispose(); detector?.Dispose(); faultClock?.Dispose(); hints?.Dispose(); feedback?.Dispose(); scoring?.Dispose();
            PointerState.OverUi = false; PointerState.ScrollCaptured = false;
        }
    }
}
