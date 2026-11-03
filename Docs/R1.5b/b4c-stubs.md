# R1.5b 待做機制清單（b4c-stubs）

b4 第 2 部分把完成條件對應到黑板鍵；下面這些鍵目前沒有機制會寫，要由後面的段落做。b4 審查條件 2：測試用寫黑板代替這些機制時，都要在這份清單上；
ImporterTests.PendingMechanisms_ListedInStubsDoc_EachMapsToItem 檢查這份清單和 Assets/_Project/Data/Source/ConditionTables.json 的 owner 欄一致，而且每一列都對到下面的一個項目。
機制做好時：把 ConditionTables.json 那一條的 owner 清空，再從這裡刪掉對應的列。b4c 做完時，b4c 的列要全部清空。

## 項目

| owner | 項目 |
|---|---|
| b4c:holdpoint | HoldPointDef 停留點與 NPC（b4c） |
| b4c:ppe | 個人防護具與手套模式（b4c） |
| b4c:ground_register | 接地登記欄（b4c） |
| b4c:lockout | 一人一鎖（b4c） |
| b4c:si00 | SI_00 限制（b4c） |
| b4c:strip_carry | 日常滑板上下車頂（b4c） |
| b4d | 車頂邊兩級判定與移動限制（b4d） |
| b4:p3 | ErrorRule（b4 第 3 部分） |
| b4:p4 | CourseDef 覆寫（b4 第 4 部分） |
| b4:p5 | 兩門課跑到結算（b4 第 5 部分；機制沒有指定段落，做的時候回報） |
| b4:p3b | 扭力類錯誤與 K3 降級（b4 第 3b 部分） |

## 小動作（共 86 列）

| 步驟 | 小動作 | 課程 | 完成條件 | owner |
|---|---|---|---|---|
| STP_SI_06_RoofEntry | c | daily | `env.work_order.roof_entry_check.checked_count>=9&&hold.1.released&&workorder.sign_start` | b4c:holdpoint |
| STP_SI_06_RoofEntry | c | periodic | `env.work_order.roof_entry_check.checked_count>=11&&hold.1.released&&workorder.sign_start` | b4c:holdpoint |
| STP_LF_01_PreLift | e | periodic | `hold.2.checklist_done&&hold.2.released` | b4c:holdpoint |
| STP_IN_01_RigOnStand | d | periodic | `hold.3.checklist_done&&hold.3.released` | b4c:holdpoint |
| STP_WC_04_Unlock | a0 | daily | `hold.4.checklist_done&&hold.4.released` | b4c:holdpoint |
| STP_WC_04_Unlock | a0 | periodic | `hold.4.checklist_done&&hold.4.released` | b4c:holdpoint |
| STP_WC_06_Handover | a | daily | `workorder.all_filled&&npc.work_leader.signed` | b4c:holdpoint |
| STP_WC_06_Handover | a | periodic | `workorder.all_filled&&npc.work_leader.signed` | b4c:holdpoint |
| STP_WC_06_Handover | b | daily | `npc.inspector.verdict=pass` | b4c:holdpoint |
| STP_WC_06_Handover | b | periodic | `npc.inspector.verdict=pass` | b4c:holdpoint |
| STP_SI_00_PreWork | d | daily | `ppe.helmet=worn&&ppe.helmet_strap=fastened&&ppe.safety_shoes=worn&&ppe.work_gloves=worn&&ppe.insulating_gloves=carried&&ppe.insulating_gloves.inflate_checked` | b4c:ppe |
| STP_SI_00_PreWork | d | periodic | `ppe.helmet=worn&&ppe.helmet_strap=fastened&&ppe.safety_shoes=worn&&ppe.work_gloves=worn&&ppe.insulating_gloves=carried&&ppe.insulating_gloves.inflate_checked` | b4c:ppe |
| STP_SI_05_Grounding | e | daily | `ground_register.1=registered` | b4c:ground_register |
| STP_SI_05_Grounding | e | periodic | `ground_register.1=registered` | b4c:ground_register |
| STP_RM_00_SecondGround | d | periodic | `ground_register.2=registered` | b4c:ground_register |
| STP_LF_01_PreLift | c2 | periodic | `ground_register.1=cancelled` | b4c:ground_register |
| STP_IN_04b_RegroundPan | i | periodic | `ground_register.1=registered&&ground_register.1.registered_count>=2` | b4c:ground_register |
| STP_WC_03a_RemoveGroundRoof | d | periodic | `ground_register.2=cancelled` | b4c:ground_register |
| STP_WC_03_RemoveGround | d | daily | `ground_register.1=cancelled` | b4c:ground_register |
| STP_WC_03b_RemoveGroundPlatform | d | periodic | `ground_register.1=cancelled` | b4c:ground_register |
| STP_WC_01b_ToolCountPlatform | b | daily | `ground_register.1.confirmed&&ground_register.1=cancelled&&rack.slot_3=on_rack` | b4c:ground_register |
| STP_WC_01b_ToolCountPlatform | b | periodic | `ground_register.1.confirmed&&ground_register.2.confirmed&&ground_register.1=cancelled&&ground_register.2=cancelled` | b4c:ground_register |
| STP_SI_00_PreWork | a | daily | `env.work_order.anti_roll_check.checked_count>=3` | b4c:si00 |
| STP_SI_00_PreWork | a | periodic | `env.work_order.anti_roll_check.checked_count>=3` | b4c:si00 |
| STP_SI_00_PreWork | b | daily | `env.work_order.permit.viewed_count>=5` | b4c:si00 |
| STP_SI_00_PreWork | b | periodic | `env.work_order.permit.viewed_count>=5` | b4c:si00 |
| STP_SI_00_PreWork | c | daily | `env.work_order.tbm.checked_count>=5&&env.work_order.tbm.signed` | b4c:si00 |
| STP_SI_00_PreWork | c | periodic | `env.work_order.tbm.checked_count>=5&&env.work_order.tbm.signed` | b4c:si00 |
| STP_SI_00_PreWork | e | daily | `env.work_order.calibration_check.valid_count>=2` | b4c:si00 |
| STP_SI_00_PreWork | e | periodic | `env.work_order.calibration_check.valid_count>=3` | b4c:si00 |
| STP_CSR_08_RemoveStrip_L | b | daily | `part.carbon_strip_L.state=OnMat` | b4c:strip_carry |
| STP_CSR_08_RemoveStrip_R | b | daily | `part.carbon_strip_R.state=OnMat` | b4c:strip_carry |
| STP_CSR_09_CleanBracket_L | d | daily | `part.carbon_strip_L.state=InTray&&env.roof_strip_mat.count<=0` | b4c:strip_carry |
| STP_CSR_09_CleanBracket_R | d | daily | `part.carbon_strip_R.state=InTray&&env.roof_strip_mat.count<=0` | b4c:strip_carry |
| STP_RM_07_LeaveRoof | b | periodic | `harness.platform&&harness.roof=false&&player.zone=platform` | b4d |
| STP_WC_02_LeaveRoof | c | daily | `player.zone=platform&&harness.hooks_stowed&&rack.slot_4=in_use` | b4d |
| STP_WC_02_LeaveRoof | c | periodic | `player.zone=platform&&harness.hooks_stowed&&rack.slot_4=in_use` | b4d |
| STP_CSR_01_CheckIsolation | a | daily | `workorder.si_all_done` | b4:p3 |
| STP_CSR_10_CheckNew_L | g | daily | `env.work_order.part_trace.matches_spare_L` | b4:p4 |
| STP_CSR_10_CheckNew_R | g | daily | `env.work_order.part_trace.matches_spare_R` | b4:p4 |
| STP_CSR_10_CheckNew_L | g | periodic | `env.work_order.part_trace.matches_spare_L` | b4:p4 |
| STP_CSR_10_CheckNew_R | g | periodic | `env.work_order.part_trace.matches_spare_R` | b4:p4 |
| STP_RT_01_CraneStow | a | periodic | `part.crane.hook.parked` | b4:p4 |
| STP_RM_01_HoldDown | c | periodic | `ip.pan_assy.hold_down.pull_tested` | b4:p5 |
| STP_RM_02_CtrlDisconnect | b | periodic | `ip.pan_assy.ctrl_connector.cap=on` | b4:p5 |
| STP_RM_03_HVDisconnect | a | periodic | `ip.pan_head.ground_clamp.inspected&&isolation.head_clamped&&ip.roof.hv_cable_ground.clamped` | b4:p5 |
| STP_RM_03_HVDisconnect | h1 | periodic | `part.pan_assy.hv_bolt_1.state=InTray` | b4:p5 |
| STP_RM_03_HVDisconnect | h2 | periodic | `part.pan_assy.hv_bolt_2.state=InTray` | b4:p5 |
| STP_RM_04_BaseUnbolt | c1 | periodic | `part.pan_base.bolt_1.state=InTray` | b4:p5 |
| STP_RM_04_BaseUnbolt | c2 | periodic | `part.pan_base.bolt_2.state=InTray` | b4:p5 |
| STP_RM_04_BaseUnbolt | c3 | periodic | `part.pan_base.bolt_3.state=InTray` | b4:p5 |
| STP_RM_04_BaseUnbolt | c4 | periodic | `part.pan_base.bolt_4.state=InTray` | b4:p5 |
| STP_RM_05_CraneRiggingCheck | b | periodic | `ctrl.crane.estop=false&&ctrl.crane.estop_tested` | b4:p5 |
| STP_RM_05_CraneRiggingCheck | c | periodic | `part.crane.hook_cog_offset_cm<=5&&part.crane.beam_above_roof_m>=1.2&&part.crane.beam_above_roof_m<=1.6&&part.crane.person_clear` | b4:p5 |
| STP_RM_05_CraneRiggingCheck | d | periodic | `part.crane.hook.inspected&&env.lift_beam.inspected` | b4:p5 |
| STP_RM_05_CraneRiggingCheck | f1 | periodic | `env.sling_1.inspected&&env.sling_1.verdict_correct` | b4:p5 |
| STP_RM_05_CraneRiggingCheck | f2 | periodic | `env.sling_2.inspected&&env.sling_2.verdict_correct` | b4:p5 |
| STP_RM_05_CraneRiggingCheck | f3 | periodic | `env.sling_3.inspected&&env.sling_3.verdict_correct` | b4:p5 |
| STP_RM_05_CraneRiggingCheck | f4 | periodic | `env.sling_4.inspected&&env.sling_4.verdict_correct` | b4:p5 |
| STP_RM_06_Rig | b | periodic | `env.rigging.inspected` | b4:p5 |
| STP_LF_01_PreLift | d | periodic | `part.crane.hook.inspected&&part.crane.hook_cog_offset_cm<=5` | b4:p5 |
| STP_LF_02_TrialLift | a | periodic | `part.crane.drop_distance>=0.08&&part.crane.drop_distance<=0.15&&part.crane.still_s>=3` | b4:p5 |
| STP_LF_02_TrialLift | b | periodic | `part.pan_assy.inspected&&part.pan_assy.tilt_deg<=3` | b4:p5 |
| STP_LF_04_Traverse | a | periodic | `part.pan_assy.stand_offset_cm<=5` | b4:p5 |
| STP_LF_05_LowerToStand | a | periodic | `part.crane.drop_distance>=0.05&&part.crane.drop_distance<=0.15&&part.crane.still_s>=1` | b4:p5 |
| STP_LF_06_SecureOnStand | c | periodic | `part.crane.beam_above_pan_m>=1.0` | b4:p5 |
| STP_CSR_01_CheckIsolation | b | daily | `env.work_order.anomaly.checked` | b4:p5 |
| STP_CSR_01_CheckIsolation | b | periodic | `env.work_order.anomaly.checked` | b4:p5 |
| STP_CSR_15_HeightCheck | f | periodic | `rack.slot_5=on_rack&&rack.slot_6=on_rack&&rack.slot_7=on_rack&&rack.slot_8=on_rack&&rack.slot_9=on_rack&&env.panto_stand.tool_tray.count<=0` | b4:p5 |
| STP_IN_01_RigOnStand | a | periodic | `part.crane.hook_cog_offset_cm<=5&&part.crane.beam_above_pan_m>=crane.rig_height_lower_m&&part.crane.beam_above_pan_m<=crane.rig_height_upper_m` | b4:p5 |
| STP_IN_02_TrialLift | a | periodic | `part.crane.drop_distance>=0.1&&part.crane.still_s>=3` | b4:p5 |
| STP_IN_02_TrialLift | b | periodic | `part.pan_assy.inspected&&part.pan_assy.tilt_deg<=3` | b4:p5 |
| STP_IN_03_Transfer | b | periodic | `part.pan_assy.roof_offset_cm<=5` | b4:p5 |
| STP_IN_04_LowerToRoof | a | periodic | `part.crane.drop_distance>=0.05&&part.crane.drop_distance<=0.15&&part.crane.still_s>=1` | b4:p5 |
| STP_IN_04_LowerToRoof | b | periodic | `part.pan_assy.state=Mounted&&part.pan_assy.seat_error_cm<=1` | b4:p5 |
| STP_IN_10_CtrlReconnect | a | periodic | `ip.pan_assy.ctrl_connector.cap=off` | b4:p5 |
| STP_WC_01a_ToolCountRoof | a | daily | `env.tool_bag.slot_5.viewed&&env.tool_bag.slot_6.viewed&&env.tool_bag.slot_7.viewed&&env.tool_bag.slot_8.viewed&&env.tool_bag.slot_9.viewed&&rack.slot_5=on_rack&&rack.slot_6=on_rack&&rack.slot_7=on_rack&&rack.slot_8=on_rack&&rack.slot_9=on_rack` | b4:p5 |
| STP_WC_01a_ToolCountRoof | a | periodic | `env.tool_bag.slot_5.viewed&&env.tool_bag.slot_6.viewed&&env.tool_bag.slot_7.viewed&&env.tool_bag.slot_8.viewed&&env.tool_bag.slot_9.viewed&&env.tool_bag.slot_10.viewed&&env.tool_bag.slot_13.viewed&&rack.slot_5=on_rack&&rack.slot_6=on_rack&&rack.slot_7=on_rack&&rack.slot_8=on_rack&&rack.slot_9=on_rack&&rack.slot_13=on_rack&&rack.slot_10=on_rack` | b4:p5 |
| STP_WC_01a_ToolCountRoof | b | daily | `env.bolt_tray_L.count<=0&&env.bolt_tray_R.count<=0&&env.tool_bag.bolt_pouch.count<=0&&env.roof_strip_mat.count<=0&&roof.leftover_count<=0` | b4:p5 |
| STP_WC_01a_ToolCountRoof | b | periodic | `env.tool_bag.bolt_pouch.count<=0&&env.bolt_tray_L.count<=0&&env.bolt_tray_R.count<=0&&env.parts_tray.strip_count_ok&&roof.leftover_count<=0` | b4:p5 |
| STP_WC_01b_ToolCountPlatform | c | daily | `workorder.toolcount_platform_confirmed` | b4:p5 |
| STP_WC_01b_ToolCountPlatform | c | periodic | `workorder.toolcount_platform_confirmed` | b4:p5 |
| STP_RT_02_InsulationTest | b | periodic | `tool.megger.connected_count>=2` | b4:p5 |
| STP_RT_02_InsulationTest | c | periodic | `workorder.ir_reading>=100` | b4:p5 |
| STP_RT_02_InsulationTest | e | periodic | `ip.pan_head.ground_clamp.discharge_s>=3` | b4:p5 |
| STP_WC_05_RaiseTest | a | periodic | `part.crane.hook.inspected&&part.pan_assy.inspected` | b4:p5 |

## 場景裡還沒有的目標（ConditionTables.json 的 pendingTargets）

| 目標 | owner |
|---|---|
| npc.work_leader | b4c:holdpoint |
| npc.guard | b4c:holdpoint |
| npc.rigging_lead | b4c:holdpoint |
| npc.inspector | b4c:holdpoint |
| env.ppe_station | b4c:ppe |
| env.lift_beam | b4:p5 |
| env.rigging | b4:p5 |
| Crane_HookBlock/Hook_Latch | b4:p5 |
| env.roof_strip_mat.slot_old | b4:p4 |
| env.spare_shelf | b4:p4 |
| consumable.carbon_strip | b4:p4 |
| consumable.carbon_strip.measure_mid | b4:p4 |

## 錯誤規則的觸發條件還沒做（Assets/_Project/Data/Source/ErrorRules.json 的 owner，共 43 條）

ErrorRuleTests.PendingTriggers_ListedInStubsDoc 檢查兩邊一致。觸發做好時把 owner 清空，再刪掉這裡那一列。

| 錯誤 | owner | 嚴重度 |
|---|---|---|
| ERR_CSR01_IsolationIncomplete | b4:p4 | Major |
| ERR_Crane_FastLowerEmptyHook | b4:p5 | Minor |
| ERR_Crane_LiftWhileFixed | b4:p5 | Major |
| ERR_Crane_NoTrialLift | b4:p5 | Major |
| ERR_Crane_RiggedOnArm | b4:p5 | Critical |
| ERR_Crane_RiggingNotInspected | b4:p5 | Critical |
| ERR_Crane_SideLoad | b4:p5 | Major |
| ERR_CSR13_WrenchRange | b4:p5 | Minor |
| ERR_CSR13_WrongSocket | b4:p5 | Minor |
| ERR_Global_GroundHangOrder | b4:p5 | Critical |
| ERR_Global_GroundWithoutVoltageTest | b4:p5 | Critical |
| ERR_Global_HarnessUnclipped | b4:p5 | Critical |
| ERR_Global_HVDisconnectWithoutSecondGround | b4:p5 | Critical |
| ERR_Global_NoDischargeAfterIR | b4:p5 | Major |
| ERR_Global_RaiseUnderHook | b4:p5 | Critical |
| ERR_Global_RaiseWithHoldDown | b4:p5 | Critical |
| ERR_IN06_TightenBeforeAllThreaded | b4:p5 | Major |
| ERR_IN06_WrongSocket | b4:p5 | Minor |
| ERR_IN07_WrenchRange | b4:p5 | Minor |
| ERR_IN07_WrongSocket | b4:p5 | Minor |
| ERR_IN09_Misaligned | b4:p5 | Major |
| ERR_IN09_WrenchRange | b4:p5 | Minor |
| ERR_IN09_WrongSocket | b4:p5 | Minor |
| ERR_IN10_NotConnected | b4:p5 | Major |
| ERR_RM03_WrongSocket | b4:p5 | Minor |
| ERR_RM04_WrongSocket | b4:p5 | Minor |
| ERR_RT02_IRFail | b4:p5 | Critical |
| ERR_SI02_RaiseSwitchUnlocked | b4:p5 | Critical |
| ERR_SI02_TagWithoutLock | b4:p5 | Critical |
| ERR_SI03_CylinderPowerOn | b4:p5 | Critical |
| ERR_Global_GroundNotCleared | b4c:ground_register | Critical |
| ERR_Global_GroundNotRegistered | b4c:ground_register | Major |
| ERR_Global_HoldPointRejected | b4c:holdpoint | Major |
| ERR_Global_HoldPointSkipped | b4c:holdpoint | Critical |
| ERR_Global_KeyLeftUnattended | b4c:lockout | Minor |
| ERR_Global_NoHelmet | b4c:ppe | Critical |
| ERR_Global_NoInsulatingGloves | b4c:ppe | Critical |
| ERR_Global_PPEMissing | b4c:ppe | Major |
| ERR_Global_StartWithoutPermit | b4c:si00 | Critical |
| ERR_Global_StripReleasedOnRoof | b4c:strip_carry | Major |
| ERR_WC01a_StripOnMat | b4c:strip_carry | Major |
| ERR_Global_RoofFallZone | b4d | Major |
| ERR_Global_TeleportWhileAnchored | b4d | Minor |

## 等後面段落才能測的（不是機制）

| 項目 | 由哪一段做 | 說明 |
|---|---|---|
| IN_04b h 分段起點的實體狀態 | b4b | 軟體工程裁定（補充 2b）：從 IN 段的 PhaseStartSnapshot 開始時，#1 弓頭端掛鉤要是「已取下」（isolation.head_clamped=false）。檢查的是還原有沒有把實體狀態設對，不是 version；PhaseStartSnapshot 在 b4b 才有 |
| 日常 CSR_09_R d 改走機制 | b4c:strip_carry | 步驟計畫裁定（2026-11-03）：條件照 CSV 判「待報廢箱裡有該側那一條舊滑板，而且暫放墊是空的」，不能只看數量（做 R 側時箱裡已經有 L 側那一條）。CourseFlowTests 的腳本到時要走 strip_carry 機制 |
