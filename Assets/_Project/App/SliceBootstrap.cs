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

        EventBus bus;
        SimState state;
        ProcedureRunner runner;
        ErrorDetector detector;
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
                var safety = new SafetyGuard(bus, state, safetyRules);
                gates.Add(safety);
                runner = new ProcedureRunner(procedure, bus, state, gates);
                detector = new ErrorDetector(runner, bus, state);
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
            bus.Subscribe<ActionRequested>(OnRequest);
            if (SimSession.IsReplay) SetupReplay();
        }

        void Start() => RegisterPoseSource();

        /// <summary>卡關偵測：每 0.5 s 只讀地檢查目前步驟（不推進步驟；見 ProcedureRunner.Poll）。重播時 runner 沒有開始，不作用。</summary>
        void Update() => runner?.Poll(Time.time);

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
                // 等機構套用情境姿態、檢測點寫出實際距離後才開始判定（否則第一個小動作會用到上一個姿態的距離）
                StartCoroutine(StartRunnerAfterPoseSettles());
            }
            else if (e.targetId == "app.restart")
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
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
            runner?.Dispose(); detector?.Dispose(); hints?.Dispose(); feedback?.Dispose(); scoring?.Dispose();
            PointerState.OverUi = false; PointerState.ScrollCaptured = false;
        }
    }
}
