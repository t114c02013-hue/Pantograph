# 後台面板 WIP 接續筆記（完成後刪除）

已存：ProcedureAutoRun 重構（2488eeb）、SimIds 鍵、SimSession.PendingJump、ToolRulesDef 兩欄、AdminPanel.cs（含 AutoRunMaskView）。

還沒做：
1. ToolRules.asset 補 autoRunMaskText、autoRunFadeSeconds。
2. SliceUI：views 最後加 AutoRunMaskView；Build 尾端 AddComponent AdminPanel；OverUi 加 AdminPanel.Hover。
3. XrPanelLayout：XR_AutoRunMask 掛 rig.Head，距離 0.3 m，比例 0.0005，6000×6000，sortingOrder 最大，材質 unity_GUIZTestMode=Always。
4. ProcedureAutoRun.GoTo(mode, stepId)：等 xr.ready → rec trainee → app.start → Procedure 每 yield 檢查 ReachedStep → HandOff；走不到寫 AutoRunStatus。HandOff 開頭清 ScriptedRay、XrTriggerHeld、LiftControl.Release。WaitStep 結尾加 yield return 1（否則同一次 MoveNext 會跑過目標步，例如 CSR_01）。
5. SliceBootstrap：Awake 寫 SimIds.StepsList（R1 才有，排除 presetDone，「RunId|defIndex 標題（側）」）；OnRequest 處理 app.jump（course==null、非 active；runner 已開始就存 PendingJump 後重載）；Start 消化 PendingJump；JumpTo 協程設遮罩鍵、跑 GoTo、關遮罩。
6. PlayMode 測試：選第 N 步交接、前面步驟完成、手上無工具；XR 模擬下頭盔相機 RT 看不到面板、遮罩全黑。asmdef 加 UI、Training 參照。

注意：遮罩鍵用 xr. 開頭，因為「重做本步」還原快照只保留 score.、xr.。
注意：模式只有 tutorial／practice；sending exam 會變教學，面板標「練習（考核）」，回報時說明。
