# R1.5b 待做機制清單（b4c-stubs）

b4 第 2 部分把完成條件對應到黑板鍵；下面這些鍵目前沒有機制會寫，要由後面的段落做。b4 審查條件 2：測試用寫黑板代替這些機制時，都要在這份清單上；
ImporterTests.PendingMechanisms_ListedInStubsDoc_EachMapsToItem 檢查這份清單和 Assets/_Project/Data/Source/ConditionTables.json 的 owner 欄一致，而且每一列都對到下面的一個項目。
機制做好時：把 ConditionTables.json 那一條的 owner 清空，再從這裡刪掉對應的列。b4c 做完時，b4c 的列要全部清空。

## 項目

| owner | 項目 |
|---|---|
| b4c:holdpoint | HoldPointDef 停留點與 NPC（b4c） |
| b4c:torque_socket | 扭力扳手選套筒（b4c，軟體工程裁定） |
| R2 | 本版不適用，排 R2（驗收組長裁定） |
| n/a | 本版不適用或已併入其他錯誤（驗收組長裁定，ID 保留） |
| b4c:ppe | 個人防護具與手套模式（b4c） |
| b4c:ground_register | 接地登記欄（b4c） |
| b4c:lockout | 一人一鎖（b4c） |
| b4c:si00 | SI_00 限制（b4c） |
| b4c:strip_carry | 日常滑板上下車頂（b4c） |
| b4c:spare_target | 選定備品／手上新滑板的目標 ID（b4c） |
| b4d | 車頂邊兩級判定與移動限制（b4d） |
| b4:p5 | 兩門課跑到結算（b4 第 5 部分；v3.10 做完時結案） |
| v310 | steps-v6 v3.10（吊帶割傷故障、D44 平台查看；這一段做完時清空） |

## 小動作（共 51 列）

| 步驟 | 小動作 | 課程 | 完成條件 | owner |
|---|---|---|---|---|
| STP_SI_06_RoofEntry | s2i1 | periodic | `rigging.cut_reported` | b4c:holdpoint |
| STP_SI_06_RoofEntry | s2i2 | periodic | `rigging.cut_reported` | b4c:holdpoint |
| STP_SI_06_RoofEntry | s2i3 | periodic | `rigging.cut_reported` | b4c:holdpoint |
| STP_SI_06_RoofEntry | s2i4 | periodic | `rigging.cut_reported` | b4c:holdpoint |
| STP_SI_06_RoofEntry | c | daily | `env.work_order.roof_entry_check.checked_count>=9&&hold.1.released&&workorder.sign_start` | b4c:holdpoint |
| STP_SI_06_RoofEntry | c | periodic | `env.work_order.roof_entry_check.checked_count>=11&&hold.1.released&&workorder.sign_start` | b4c:holdpoint |
| STP_LF_01_PreLift | e | periodic | `hold.2.checklist_done&&hold.2.released` | b4c:holdpoint |
| STP_IN_01_RigOnStand | s2i1 | periodic | `rigging.cut_reported` | b4c:holdpoint |
| STP_IN_01_RigOnStand | s2i2 | periodic | `rigging.cut_reported` | b4c:holdpoint |
| STP_IN_01_RigOnStand | s2i3 | periodic | `rigging.cut_reported` | b4c:holdpoint |
| STP_IN_01_RigOnStand | s2i4 | periodic | `rigging.cut_reported` | b4c:holdpoint |
| STP_IN_01_RigOnStand | d | periodic | `hold.3.checklist_done&&hold.3.released` | b4c:holdpoint |
| STP_RT_02_InsulationTest | r1 | periodic | `workorder.ir_reported` | b4c:holdpoint |
| STP_RT_02_InsulationTest | r3b | periodic | `hold.1.checklist_done&&hold.1.released` | b4c:holdpoint |
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
| STP_RT_02_InsulationTest | r2i | periodic | `ground_register.1=registered&&ground_register.1.registered_count>=3` | b4c:ground_register |
| STP_RT_02_InsulationTest | r4d | periodic | `ground_register.1=cancelled&&ground_register.1.cancelled_count>=3` | b4c:ground_register |
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
| STP_WC_01a_ToolCountRoof | b | daily | `env.bolt_tray_L.bolts<=0&&env.bolt_tray_R.bolts<=0&&env.tool_bag.bolt_pouch.count<=0&&env.roof_strip_mat.count<=0&&roof.leftover_count<=0` | b4c:strip_carry |
| STP_RM_07_LeaveRoof | b | periodic | `harness.platform&&harness.roof=false&&player.zone=platform` | b4d |
| STP_WC_02_LeaveRoof | c | daily | `player.zone=platform&&harness.hooks_stowed&&rack.slot_4=in_use` | b4d |
| STP_WC_02_LeaveRoof | c | periodic | `player.zone=platform&&harness.hooks_stowed&&rack.slot_4=in_use` | b4d |
| STP_RT_02_InsulationTest | r3h | periodic | `player.zone=platform&&harness.hooks_stowed` | b4d |

## 場景裡還沒有的目標（ConditionTables.json 的 pendingTargets）

| 目標 | owner |
|---|---|
| npc.work_leader | b4c:holdpoint |
| npc.guard | b4c:holdpoint |
| npc.rigging_lead | b4c:holdpoint |
| npc.inspector | b4c:holdpoint |
| env.ppe_station | b4c:ppe |
| consumable.carbon_strip | b4c:spare_target |
| consumable.carbon_strip.measure_mid | b4c:spare_target |

## 錯誤規則的觸發條件還沒做（Assets/_Project/Data/Source/ErrorRules.json 的 owner，共 22 條）

ErrorRuleTests.PendingTriggers_ListedInStubsDoc 檢查兩邊一致。觸發做好時把 owner 清空，再刪掉這裡那一列。

| 錯誤 | owner | 嚴重度 |
|---|---|---|
| ERR_Global_GroundNotCleared | b4c:ground_register | Critical |
| ERR_Global_GroundNotRegistered | b4c:ground_register | Major |
| ERR_Global_HoldPointRejected | b4c:holdpoint | Major |
| ERR_RT02_NoReport | b4c:holdpoint | Major |
| ERR_Global_KeyLeftUnattended | b4c:lockout | Minor |
| ERR_Global_NoHelmet | b4c:ppe | Critical |
| ERR_Global_NoInsulatingGloves | b4c:ppe | Critical |
| ERR_Global_PPEMissing | b4c:ppe | Major |
| ERR_Global_StartWithoutPermit | b4c:si00 | Critical |
| ERR_Global_StripReleasedOnRoof | b4c:strip_carry | Major |
| ERR_WC01a_StripOnMat | b4c:strip_carry | Major |
| ERR_CSR13_WrongSocket | b4c:torque_socket | Minor |
| ERR_IN07_WrongSocket | b4c:torque_socket | Minor |
| ERR_IN09_WrongSocket | b4c:torque_socket | Minor |
| ERR_Global_TeleportWhileAnchored | b4d | Minor |
| ERR_SI03_CylinderPowerOn | b4d | Critical |
| ERR_Global_RaiseWithHoldDown | n/a | Critical |
| ERR_SI02_RaiseSwitchUnlocked | n/a | Critical |
| ERR_Crane_RiggedOnArm | R2 | Critical |
| ERR_IN09_Misaligned | R2 | Major |

## 扭力扳手選套筒（b4c:torque_socket，軟體工程裁定 2026-11-03）

- 三支扳手（套筒扳手、扭力扳手、大扭力扳手）共用 ToolSocketMount，讀 SocketCatalog 和 ToolDefinition.driveIn。
- VR：拿扳手頭碰工具袋 SocketCase 的 TGT_Socket_*，停 0.15 s 換上，舊的回盒；方頭不合不吸附，提示「方頭尺寸不符」，不記分。
- 桌面：點 SocketCase 的格子換上；R1 的套筒選單保留當備援。
- 接合螺栓時比對套筒 sizeMm 和 FastenerDef.sizeMm：不同就擋下，提示「套筒尺寸不對」，記該步的 WrongSocket（Minor），同一步只記一次（ERR_IN07／IN09／CSR13_WrongSocket）。
- 教學模式 SocketCase 標尺寸。
- R1 的換套筒測試改成三支扳手各跑一次。

## 等後面段落才能測的（不是機制）

| 項目 | 由哪一段做 | 說明 |
|---|---|---|
| IN_04b h 分段起點的實體狀態 | b4b | 軟體工程裁定（補充 2b）：從 IN 段的 PhaseStartSnapshot 開始時，#1 弓頭端掛鉤要是「已取下」（isolation.head_clamped=false）。檢查的是還原有沒有把實體狀態設對，不是 version；PhaseStartSnapshot 在 b4b 才有 |
| 日常 CSR_09_R d 改走機制 | b4c:strip_carry | 步驟計畫裁定（2026-11-03）：條件照 CSV 判「待報廢箱裡有該側那一條舊滑板，而且暫放墊是空的」，不能只看數量（做 R 側時箱裡已經有 L 側那一條）。CourseFlowTests 的腳本到時要走 strip_carry 機制 |

## b4:p4 收尾的處理（資深軟體工程師 B 審查 F2，2026-11-03）

| 項目 | 處理 |
|---|---|
| RT_01 a part.crane.hook.parked | 做了：CraneMechanism 每 tick 判吊鉤在不在停放區（CraneProfile.parkZoneRadius／parkZoneBelowY，以 Crane_ParkPose 為中心） |
| CSR_10 g part_trace.matches_spare_L／R（兩門課） | 做了：作業單「新品料號／序號：抄自備品 n 的標籤」，WorkOrder 比對選用的那一條（spare.<side>.picked_slot） |
| env.spare_shelf | 做了：備品架本體加 ID（三格各自的 ID 不變） |
| env.roof_strip_mat.slot_old | 做了：暫放墊既有的 StripMat_Slot_Old 節點加 ID |
| consumable.carbon_strip、consumable.carbon_strip.measure_mid | 改 b4c:spare_target：這兩個 ID 指的是「選定的那一條備品」（CSR_10 d）和「手上的新滑板」（CSR_11 b），執行期才知道是哪一個物件，要做動態登錄（選定時把那一條登錄成這個 ID、拿起後換成手上的滑板）；完成條件已經讀 spare.<side>.selected／part.carbon_strip_<side>，只差高亮和箭頭 |

## 下一輪（資深軟體工程師 B 審查的非阻擋建議）

- MeggerLogic、InsulatorsClean 寫死的數量改讀資料。
- FreshWriteGate.Kinds 在測試 TearDown 還原。
- SimState 刪除不存在的鍵時不加 version。
- 常數搬到設定（軟體工程已裁定 BlackboardKeyDef 加 Constant 種類）。
- 每幀寫的量測鍵會讓 FreshWriteGate 失效（Event 鍵每幀被寫就永遠算「啟用後寫過」）：列出這些鍵，改成值變才寫或改 LiveState。
- PanelViews 作業單「抄自備品 n 的標籤」按鈕寫死 1、2、3，改讀備品架資料（SpareArrangementDef／場景格數）。
- MarkerView 每幀重新解析 rack.slots，改成值變了才重新解析（快取）。
- 提醒：吊鉤開場就在停放區時，RT_01 a 一啟用就完成（part.crane.hook.parked 是 LiveState，照規則可以）。
- b5：考核每段與全程的 1.5 倍時間判定要先扣掉故障處理時間（summary 的 faultSeconds、steps[].faultSeconds；v3.10 第 8 項，FaultClock 已記）。
