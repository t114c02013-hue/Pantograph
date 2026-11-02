# ExternalModels 外部模型設計區

外部模型進專案的唯一入口。執行期程式與正式場景不得引用這裡的資產。

1. 原始檔（.blend、.max、.step）放在專案根目錄的 ArtSource/。
2. 匯出的 FBX 先放 _Inbox/，檢查單位（公尺）、軸向、面數、材質與授權。
3. 檢查後依來源移到 Vendor/<來源>/、CAD/ 或 Scans/，在 Sandbox/ 試做。
4. 採用後把減面、定好軸心的版本複製到 Modules/Parts/<零件>/Models/，改用正式命名。

檔名規則：EXT_<來源>_<名稱>_v<版本>，例如 EXT_VendorA_PanHead_v02.fbx

## 模型清單
- Vendor/3d66/EXT_3d66_JJI54552119072_v01.fbx（原檔名 3d66.com_JJI54552119072.fbx，授權待確認）
- Sandbox/PantographRig/EXT_3d66_Pantograph_Rigged_v01.prefab：由上面的 FBX 自動產生的可動版本（選單 Tools/集電弓/重建可動模型），結果報告在 Logs/PantographRigReport.txt
