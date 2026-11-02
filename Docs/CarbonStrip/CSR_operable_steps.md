# 碳滑板更換：可操作版步驟表與工具清單（步驟計畫 定稿 v3）

> 來源：專案負責人轉交（2026-10-13），含審查修正。標 EX 的數值是示例，只改資料不改程式。

## 0. 共通規則
- 操作（桌機）：工具列選工具（數字鍵 1–9 或點圖示），再點場景中的互動點；不需工具的步驟直接點零件或作業單。
- 每一步是一筆 StepDefinition：stepId、玩家動作、通過條件、錯誤（Minor/Major/Critical）。教學模式下的 Critical：提示並要求重做。
- 拿錯工具點零件：記 Minor「工具選擇錯誤」，不改變零件狀態。
- 雙側：CSR 06–14 先做肘側滑板（_L），再做外側滑板（_R），畫面顯示「肘側滑板」「外側滑板」。
- 全域安全規則（所有模式都生效）：SI_01 未完成（或弓頭狀態 = 升弓），或 SI_04/SI_05 未完成時，任何工具或滑鼠動作碰到弓體（part.carbon_strip_*、ip.carbon_strip_*.bolt_*、bracket、PanHead）一律擋下並記 Critical。畫面說明做錯什麼、為什麼危險、正確做法，並提供「從這一步重做」。錯誤 ID：ERR_Global_NotLowered、ERR_Global_NotGrounded。
- 介面：按 Play 先顯示任務卡（目標、工具清單、安全重點、操作鍵：數字鍵選工具、左鍵點互動點、右鍵轉視角）。每步都要顯示下一步做什麼、用什麼工具、點哪裡。每個錯誤回饋都要有做錯什麼、為什麼、怎麼改。
- 3.0 mm 更換基準來自保養表 m1-2，畫面標「保養表值」；其他 EX 值標「示例值」。
- 判定：任一條三點最小值 ≤ 3.0 mm 就兩條一起換（EX 政策）。

## 1. 互動件
- part.carbon_strip_L / _R：CarbonStrip_1/2（1.10 m × 7.4 cm × 1.6 cm）。靠肘關節的那條是 _L，W00 確認。
- ip.carbon_strip_{side}.measure_left / mid / right：頂面三點（左端起 EX 15 cm、中點、右端起 EX 15 cm），卡尺在這裡吸附。
- ip.carbon_strip_{side}.bolt_1…4：自行建模，EX M8×25 六角頭（13 mm 套筒，對邊 13 mm）加平墊圈。兩端各 2 支，距端 EX 3 cm、距中心線 ±2.5 cm（外排靠斜面），由上往下鎖進托架，頭朝上。
- ip.carbon_strip_{side}.bracket：StripCarrier 頂面接觸區（羊角和托架同一個 mesh，不拆），清潔用。
- 不建定位銷：螺栓孔對齊＋方向正確才吸附；方向反了不吸附並發 WrongOrientation。
- ip.pan_head.ground_clamp：弓頭接地掛點。
- ip.depot.ground_terminal：平台接地端子，黃綠標誌。
- ip.cab.breaker / raise_switch / cylinder_power：駕駛室控制盤（2D 面板）。
- env.work_order：作業單 UI。env.parts_tray：零件盒。env.spare_shelf：備品架 3 條（1 條正確型號）。
- 螺栓編號：1 左外、2 右內、3 右外、4 左內（外 = 靠斜面那排）。

## 2. 步驟表（stepId｜玩家動作｜工具｜通過條件｜錯誤）
### A 安全隔離
- STP_SI_01_ConfirmLowered｜點弓頭，再點控制盤的弓位指示燈｜無｜兩處都點過，且弓頭狀態 = 降弓｜只看燈沒看弓頭（Major）
- STP_SI_02_LockOut（預設完成）｜主斷路器 OFF，上鎖掛牌組點主斷路器和升弓開關｜tool.lockout_tag｜兩處都「已上鎖＋掛牌」，鑰匙在玩家身上｜只掛牌沒上鎖、漏鎖升弓開關（Critical）
- STP_SI_03_CylinderPowerOff（預設完成）｜cylinder_power 切 OFF，按一次升弓鈕確認弓體不動｜無｜電源 OFF 且做過不動確認｜沒斷電源（Critical）
- STP_SI_04_VoltageTest｜先點驗電器自檢器（綠燈＋嗶聲），再點弓頭接地掛點｜tool.voltage_detector｜先自檢後量測，顯示「無電」｜未自檢就量（Critical）
- STP_SI_05_Grounding｜先點平台接地端子，再「拉線確認」，最後點弓頭接地掛點｜tool.grounding_rod｜端子→拉線→弓頭｜先掛弓頭端（Critical）；沒拉線確認（Major）
- STP_SI_06_RoofEntry（預設完成）｜安全帶改扣，勾鄰線檢查 5 項，作業單簽名｜tool.safety_harness｜5 項全勾、始終至少一個掛點扣著、已簽名｜先解後扣（Critical）；漏勾（Major）
### B 量測與更換
- STP_CSR_01_CheckIsolation｜打開作業單看 SI 狀態欄｜無｜SI 01–06 全部完成
- STP_CSR_02_ZeroCaliper｜量爪合攏，按 ZERO｜tool.caliper｜合攏時顯示 0.00｜沒歸零就量（Major，之後讀數全部 +0.30）
- STP_CSR_03_Measure_L｜卡尺依序點 measure_left/mid/right，讀數填入作業單｜tool.caliper｜三點都量過，填入值與實際讀數差 ≤ 0.05｜只量中間（Major）；抄錯（Minor）
- STP_CSR_04_Measure_R｜同上
- STP_CSR_05_Decide｜作業單四選一：兩條都換／只換肘側／只換外側／合格｜無｜任一條最小值 ≤ 3.0 選「兩條都換」；都 > 3.0 選「合格」跳到 C｜該換未換或用平均值判定（E6，Critical）；只換一條（E6b，Major，m1-3 要求兩條無高低差）；不該換卻換（E6c，Minor）
- STP_CSR_06_Loosen_{side}｜套筒選對尺寸，方向「鬆」，依 1→2→3→4 每支轉半圈｜tool.socket_wrench｜每支 180°±30°，順序正確｜套筒錯（Minor）；順序錯（Minor）；一支轉到底（Major）
- STP_CSR_07_RemoveBolts_{side}｜轉到脫出，點螺栓放進零件盒｜tool.socket_wrench｜4 支都在零件盒｜沒收（Major）
- STP_CSR_08_RemoveStrip_{side}｜點滑板，選「垂直取出」（另一選項「撬起」），放進零件盒｜無｜選垂直取出，舊滑板在盒中｜撬起（Major）
- STP_CSR_09_CleanBracket_{side}｜清潔布在托架上拖曳｜tool.cleaning_cloth｜清除率 ≥ EX 95%｜沒擦淨就裝（Major）
- STP_CSR_10_CheckNew_{side}｜看備品架 3 條標籤，選型號相符的，卡尺量中點｜tool.caliper｜型號正確、厚度 EX 12.0±0.2、無裂紋｜選錯型號（Critical）；拿有裂紋的（Critical）
- STP_CSR_11_Install_{side}｜拖到托架，按 R 旋轉讓斜面朝外，對準螺栓孔放下｜無｜斜面朝外、4 孔對齊吸附｜裝反（Critical，所有模式都不吸附，並提示正確方向）
- STP_CSR_12_PreTighten_{side}｜從盒中取螺栓放回孔位，方向「鎖」，依 1→4 鎖到貼合｜tool.socket_wrench｜四支都貼合，順序正確｜第一支直接鎖死（Major）；順序錯（Major）
- STP_CSR_13_Torque_{side}｜扭力扳手設 25 N·m，依 1→4 按住到喀聲或「到達設定值」就放開｜tool.torque_wrench｜每支 EX 25±2，順序正確｜設定錯（Major）；超過 27（Major）；低於 23（Major）
- STP_CSR_14_Mark_{side}｜標記筆依序點四支｜tool.marker｜四支都有線｜漏畫（Minor）
- STP_CSR_15_HeightCheck｜兩側做完後，卡尺量兩條新滑板中點，填作業單｜tool.caliper｜差 ≤ EX 1.0（m1-3）｜沒比對或超限仍通過（Major）
### C 收尾
- STP_WC_01_ToolCount｜打開零件盒與工具清點畫面，按確認｜無｜無遺留工具與螺栓，盒中只剩舊滑板與舊螺栓｜遺留（Critical）
- STP_WC_02_LeaveRoof（預設完成）｜安全帶改扣平台｜tool.safety_harness｜始終扣著｜先解後扣（Critical）
- STP_WC_03_RemoveGround｜先點弓頭掛點取下，再點平台端子｜tool.grounding_rod｜弓頭→端子｜順序顛倒（Critical）
- STP_WC_04_Unlock（預設完成）｜解鎖、恢復電源、簽名｜tool.lockout_tag｜解鎖者 = 上鎖者，已簽名｜未簽（Minor）

初始數值：肘側 3.4/2.8/3.1，外側 4.2/3.8/4.0，新品 12.0，預期兩條一起換。E6b 嚴重度 Major。

## 3. 工具規格
- 上鎖掛牌組：點開關後變成「已上鎖＋掛牌」，鑰匙進背包，只有持鑰者能解鎖。
- 驗電器：點自檢器，LED 綠燈＋嗶聲，記下自檢時間；點目標顯示「有電／無電」。本步驟內沒自檢就量要記錯。
- 接地棒：夾端子，拉線確認，掛弓頭；拆除照反序。
- 安全帶：兩個掛鉤；改扣時任何時刻至少一個扣著。
- 數位卡尺：滾輪或拖曳開合量爪；ZERO 鍵；靠近量測點自動吸附，讀數 0.01 mm、±0.02 雜訊；未歸零 +0.30；讀數由玩家抄到作業單。
- 套筒扳手：套筒 10/13/17 mm（正確 13）；方向鬆／鎖；拖曳或滾輪旋轉，累計角度；螺栓狀態：鎖緊→鬆半圈→脫出→放回→貼合。
- 扭力扳手（喀聲式）：設定 ±0.5 或直接輸入，範圍 5–50；按住時扭力上升，到值時喀聲＋扳手閃色＋文字「到達設定值」；放開記錄實際值；過了還按住會繼續上升。
- 清潔布：拖曳清除碳粉遮罩，清除率 ≥ 95% 通過。
- 標記筆：點螺栓，畫一條線貼在螺栓頭與托架上。
- 零件盒：可放入、取出，顯示數量。
- 備品滑板 ×3：1 條正確、1 條型號不符、1 條有裂紋（要旋轉才看得到）。
- 作業單：兩條三點讀數、判定、兩條中點讀數、上鎖人、簽名。

## 驗收最低標準（未達到就不交付）
1. 開 SC_CarbonStripSlice 按 Play 不出現紅字，正常流程能從頭做到尾。
2. 只看畫面就知道下一步。
3. 驗電器、接地棒、卡尺、套筒扳手、扭力扳手都能實際操作。
4. 安全類 Critical 一定擋下並說明原因：未降弓、未接地、接地順序、未驗電、拆接地順序。
5. 只量一點、該換未換、扭力不足或過大、裝反，一定有回饋，而且不能矇混過關。
6. 錯誤回饋都要有「做錯什麼、為什麼、怎麼改」。
7. 數值都標「保養表值」或「示例值」。
