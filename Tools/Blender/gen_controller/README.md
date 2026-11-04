# Quest Touch Plus 控制器模型（資深建模師 B，v1.0，2026-11-04）

## 檔案

| 檔案 | SHA-256 | 放到專案的位置 |
|---|---|---|
| `CTRL_TouchPlus_L.fbx`（4,470 三角面，內嵌貼圖） | `323f7491e5a44c94e435498c4064a2c301945a5847f7d6620b72db05f27e8931` | `Assets/_Project/Interaction/XR/ControllerVisual/Models/` |
| `CTRL_TouchPlus_R.fbx`（4,470 三角面，內嵌貼圖） | `0d19df0313bf505e30a7f83c104841615767be25c30ef25ce04f7b4116dcca9e` | 同上 |
| `LICENSE.md`（MIT，Copyright 2019 Amazon） | — | `Assets/_Project/ThirdParty/WebXRInputProfiles/LICENSE.md` |
| `gen_controller_fbx.py`（轉檔腳本，可以重跑） | `91259d907cf4e26367edef7a38e379da39c28926048c98d003015a7b9ec9b776` | `Tools/Blender/gen_controller/` |
| `ControllerModelReport.json` | — | 轉檔報告（Blender 5.2.3 LTS，來源 GLB 的 SHA） |

- 來源：`immersive-web/webxr-input-profiles`，`packages/assets/profiles/meta-quest-touch-plus/left.glb`（SHA `41a35b80…82cb4`）與 `right.glb`（SHA `5126d638…44916`），2026-11-04 下載。
- 授權：MIT。模型照原樣顯示，不改造外觀，不用於宣傳（README 註明 MIT 不授予商標權）。
- 重跑方式：`blender -b --factory-startup -P gen_controller_fbx.py -- --in <glb 資料夾> --out <輸出資料夾>`。

## 座標與掛法

- 1 unit = 1 m。FBX 根節點的原點就是 **OpenXR grip pose**（掌心中心），已經換算成 Unity 座標：+Z 朝前（指向扳機前方）、+Y 朝上、+X 朝右。
- 掛法：把 FBX 實例放在 XRI 控制器（grip 姿態）的 Model Parent 底下，Transform 全部設 0。
- 如果頭盔裡看到的模型方向或位置和手上的實物不一致，**只調 prefab 的 `Visual_Offset` 空物件**（建議加在 FBX 實例的上一層），不要重跑腳本。轉檔時的推算是：GLB 的 root = WebXR grip space = OpenXR grip，OpenXR 轉 Unity 會讓 Z 取負號。這點已經用回讀 FBX 驗算過，但還沒有在頭盔裡實測。

## 節點（名稱照原檔，每個按鍵都可以動）

```
CTRL_TouchPlus_R
├─ controller_mesh                        機身（不動）
├─ xr_standard_trigger_pressed_value      ← 每幀設成 Lerp(min, max, 扳機值)
│   └─ trigger                            網格
├─ xr_standard_trigger_pressed_min / _max 參考姿態（空物件，不要刪）
├─ xr_standard_squeeze_pressed_value/min/max ─ squeeze
├─ xr_standard_thumbstick_pressed_value   ← 按壓（min/max）
│   └─ xr_standard_thumbstick_xaxis_pressed_value  ← X 軸傾斜（xaxis_pressed_min/max）
│       └─ xr_standard_thumbstick_yaxis_pressed_value ← Y 軸傾斜（yaxis_pressed_min/max）
│           └─ thumbstick
├─ a_button_pressed_value/min/max ─ a_button（左手：x_button）
├─ b_button_pressed_value/min/max ─ b_button（左手：y_button）
├─ thumbrest_pressed_value/min/max        （沒有網格，可以忽略）
├─ Attach_Tool       工具握持點（推估，見下表）
├─ Poke_Tip          控制器最前端（左手碰 Poke_* 用，SphereCollider r 0.8 cm，layer ToolTip）
└─ LBL_Trigger、LBL_Grip、LBL_Stick、LBL_A、LBL_B（左手：LBL_X、LBL_Y、LBL_Menu）
```

**按鍵動畫（給軟體）：** 每個 `*_pressed_value` 節點，每幀把 localPosition 和 localRotation 設成 `Lerp/Slerp(min, max, t)`。t 的來源：扳機用 trigger 0–1，握把用 grip 0–1，按鈕用 0 或 1，搖桿按壓用 0 或 1。搖桿 X 軸用 `(x+1)/2` 驅動 `xaxis` 那一組，Y 軸用 `(y+1)/2` 驅動 `yaxis` 那一組。這和 WebXR motion-controllers 的做法相同。

**按鍵發亮：** `InputBindingTable` 裡有作用的按鍵，把它的網格材質 Emission 打開（MaterialPropertyBlock），沒有作用的按鍵維持原樣。標示文字也讀同一張表，掛在對應的 `LBL_*` 底下。

## 錨點位置（Unity 座標，單位 mm，相對 grip 原點，EX，頭盔實測後再調）

| 錨點 | 左手 | 右手 | 說明 |
|---|---|---|---|
| `Attach_Tool` | (0, −20, 50) | (0, −20, 50) | 掌心往前 5 cm、往下 2 cm。工具的 `Attach_Grip` 對到這裡 |
| `Poke_Tip` | (4.3, −8.0, 51.9) | (−4.1, −8.0, 51.9) | 機身最前端（原本追蹤環的位置） |
| `LBL_Trigger` | (12.3, −48.3, 58.7) | (−12.3, −48.3, 58.7) | 扳機下前方 |
| `LBL_Grip` | (40.9, 13.2, 24.8) | (−40.9, 13.2, 24.8) | 握把鍵外側 |
| `LBL_Stick` | (−8.9, 26.3, 26.9) | (8.9, 26.3, 26.9) | 搖桿上方 3.5 cm |
| `LBL_X`／`LBL_A` | (2.0, 38.5, 18.3) | (−2.0, 38.5, 18.3) | 按鍵上方 3 cm |
| `LBL_Y`／`LBL_B` | 按鍵上方 3 cm | 按鍵上方 3 cm | 同上 |
| `LBL_Menu`（只有左手） | (9.1, 21.3, 4.9) | — | **原檔沒有選單鍵網格**，這是推估值，要在頭盔裡對照實物調整 |

## 已知限制

1. 原檔只有 Phong 材質和內嵌貼圖。匯入 Unity 後請改用 URP Lit（或專案目前用的 shader），貼圖沿用 `crystalControllers_*_BaseColor`。
2. 左手原檔沒有選單鍵（≡）網格，右手也沒有 Meta 鍵。≡ 只有標示錨點，沒有按壓動畫。
3. 所有錨點都是依網格推算的，還沒在頭盔裡實測。terry 第一次戴頭盔時，請看三件事：機身和手上的實物有沒有重疊；按扳機時模型的扳機有沒有跟著動；標示有沒有擋住按鍵。
4. 拿起工具時，控制器不要隱藏（D4）。XRI 的「選取時隱藏控制器模型」選項要關掉。
