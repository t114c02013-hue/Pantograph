# 裁定紀錄（decisions）

最終規則的裁定，不是待定。待定請寫在 StationSet.pendingRulings（每筆要有 ruleRef、decidedBy、releaseSegment）。

| 編號 | 裁定者 | 文件 | 發布段 | 內容 |
|---|---|---|---|---|
| D36／增補 6 第 7 點 | 軟體工程 | D36 和增補 6 第 7 點 | a6 | Walkable 內側挖除分兩種：不會動的底架、絕緣子只挖自己的頂點範圍（RoofSceneDef.staticCarveInflate＝0）；會動的臂、弓頭、連桿外擴升降全程範圍（movingCarveInflate＝0.15）。「離不可見牆 ≥ 0.15」只查外側邊與會動零件的挖除邊（Blocker_Static 不查）；不會動零件的淨空改由第 16 項 (i)：站點到底架、絕緣子頂點範圍 ≥ StationSet.staticPartClearance（0.25）。 |
| D37／增補 6 第 7 點 G | 鐵道 | D37 加增補 6 第 7 點 G | a6 | 站點離會動零件升降範圍的淨空依零件分開，資料 StationSet.partClearRadius：羊角（PanHead）0.2 只限車頂站點；其他會動零件 0.4；清單沒列的零件用 clearRadius（0.4）。理由：有人在車頂時弓已上鎖、斷電、接地，固定帶扣上，升弓會被擋並記 Critical；降弓淨空由第 16 項 (h) 驗過。 |
