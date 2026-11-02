# 碳滑板更換切片：軟體工作項目（W00–W14）

> 來源：專案負責人轉交（2026-10-13 重送版為準）。步驟與數值見 CSR_operable_steps.md v3。

環境：Unity 2022.3.62f3、Built-in、舊 Input Manager。不能按 Play，所以每項都要能用 EditMode 測試、Editor 選單報告或渲染圖驗證。

**優先順序**：用量不足時，先做主線 W00–W07、W12、W13，加上 W10/W11 的提示、錯誤回饋、結算，以及 W08 的 IMGUI 最小版（作業單、零件盒、備品架）、W09 的最小版（OrbitCameraController 加點選、拖放，WASD 延後）。W14 先跑正常路徑和 E1/E2/E3/E5/E6/E8/E10/E13/E16。

**共通規則**：程式放在 Assets/_Project/，命名空間 = asmdef 名，Auto Referenced 關閉，只引用下層。功能層（Parts/Tools/Worksite/Procedures/Safety/Interaction）互不引用，只透過 Core 的 IEventBus、ISimState、IActionGuard 溝通；Training/UI 只訂閱事件；App 是唯一組裝點。場景與資產由 Editor 腳本產生。IMGUI 字型用 Font.CreateDynamicFontFromOSFont(new[]{"Microsoft JhengHei","Microsoft YaHei","Arial Unicode MS"},18)。每項做完：編譯 0 錯誤、驗證，寫入 Logs/SliceBuildReport.txt。

**審查修正（與上文不一致時以此為準）**：螺栓 M8×25、13 mm 套筒、每條 4 支、距端 3 cm、距中心線 ±2.5 cm、由上往下鎖；不建定位銷，InstallSnap 以孔位對齊＋方向正確判定，反向發 WrongOrientation；讀數肘側 3.4/2.8/3.1、外側 4.2/3.8/4.0、新品 12.0；E6b 為 Major；羊角不拆。

- **W00** 跑 ClassifyPanHead，確認 PantographRigReport.txt 有「弓頭拆分」段落、rig_panhead_*.png 存在。記錄肘側 _L／外側 _R、兩條座標與尺寸、斜面在哪一側（外）、StripCarrier 端點座標。
- **W01 Core**（PantographSim.Core，無依賴）：ISimObject、IInteractable、IMeasurable、IToolTarget、IActionGuard（Check(ActionRequest)→GuardResult{allowed,errorId}）、IStepGate；事件 struct（targetId/pointId/toolId/value/time）：ActionRequested、ActionPerformed、ActionBlocked、MeasurementTaken、RecordWritten、StateChanged、StepStarted、StepCompleted、ErrorDetected、ProcedureCompleted、ToolSelected；服務 EventBus、SimState（Snapshot/Restore、StateChanged）、ServiceLocator；CoreTests。
- **W02 Data**（→Core）：StepDefinition（stepId、title、instruction、toolHint、targetHint、targetIds[]、requiredToolId、[SerializeReference] CompletionRule、ErrorRule[]、HintSet、presetDone、side）；CompletionRule：StateEquals、ChecklistAll、OrderedSequence、ValueInRange（{side}、bolt_{n}；clean_ratio≥0.95；angle_deg 150–210）、Decision、ReadingMatches（±0.05）、ProcedureDone；ErrorRule（errorId、severity、what/why/fix、trigger：ValueOutOfRange/SequenceMismatch/ActionBefore/WrongTool/WrongTarget/Incomplete/ChoiceEquals、blocks）；ValueTag；ProcedureDefinition（repeatBlock、onPass）；ToolDefinition（3–5 頁教學）；ScenarioDefinition；DataTests。
- **W03 Procedures**（→Core、Data）：ProcedureRunner（repeatBlock 先 L 後 R、presetDone、合格跳到 C、Snapshot、RestartCurrentStep）、Evaluators、ErrorDetector（拿錯工具／點錯目標 Minor）、IStepGate；ProcedureTests。
- **W04 Safety**（→Core、Data）：SafetyGuard（SR_Global_NotLowered E1、SR_Global_NotGrounded E2、SR_Ground_Order E3、SR_Ground_NoVoltageTest E13、SR_Ground_RemoveOrder E16），被擋發 ActionBlocked＋Critical ErrorDetected，不評分；SafetyTests。
- **W05 模型**：SliceModelBuilder（PantographSim/Slice/Build Models）：PF_CarbonStrip_L/_R、PF_StripBolt、互動點、場景件、rig_slice_strip_L/bolts/scene.png。
- **W06 Parts**：PartBehaviour、InteractionPointMarker、FastenerBehaviour、RemovableBehaviour（InstallSnap）、DirtMask 16×4、Highlightable；CarbonStrip（IMeasurable、StripLabel）；Mechanism 包裝；PartsTests。
- **W07 Tools**：ToolBase、ToolBelt、ToolTutorialTrigger、ToolInventory；VoltageDetector、GroundingRod、Caliper、SocketWrench、TorqueWrench、CleaningCloth、Marker、LockoutTag、SafetyHarness；ToolTests。
- **W08 Worksite**：ControlPanel、GroundTerminal、PartsTray、SpareShelf、WorkOrder（發 RecordWritten）。
- **W09 Interaction**：Picker、DragPlace（R 轉 180°）、InspectMode、PlayerController（WASD 延後）。
- **W10 Training**：ModeController（正常／升弓未降／未接地／兩條都合格）、HintService、ErrorFeedback（三段說明、Critical 暫停＋從這一步重做）、Scoring。
- **W11 UI**（IMGUI）：TaskCard、StepPanel（第 n/25 步、大字顯示側別）、ToolBar、Readout、ErrorDialog、WorkOrder、ControlPanel、Tutorial、Result；數值標「保養表值／示例值」。
- **W12 步驟資料**：carbon_strip_slice.json（25 步、what/why/fix、預設完成 SI_02/03/06、WC_02/04）、StepJsonImporter（Import Steps）、DataValidator。
- **W13 App**：SliceBootstrap；SliceSceneBuilder（Build Scene）→ Assets/_Project/Content/Scenes/SC_CarbonStripSlice.unity（Build Settings 第 0 位）＋預覽圖。
- **W14** SliceDryRunTests：正常 25 步通過；E1/E2/E3/E13/E16 被擋且 Critical；E5/E6/E8/E9/E10 不能過關；其餘錯誤都有 ErrorDetected。
