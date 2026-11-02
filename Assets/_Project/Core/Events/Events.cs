namespace PantographSim.Core
{
    // 所有事件都帶 targetId / pointId / toolId / value / time，另有 text 放文字資料。
    // isReplay：重播中發出的事件（SimSession 為 Replay 時 EventBus 自動標上）；計分與紀錄一律略過 isReplay=true 的事件。

    public enum Severity { Minor, Major, Critical }

    /// <summary>玩家在場景或介面上對某目標發出的操作（點擊、按鈕）。</summary>
    public struct ActionRequested
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
    }

    /// <summary>工具或零件實際執行了動作（已通過守門）。text 放動作種類與參數，例："rotate:13"。</summary>
    public struct ActionPerformed
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
    }

    /// <summary>動作被守門者擋下。text＝錯誤 ID。</summary>
    public struct ActionBlocked
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
    }

    /// <summary>量測完成。value＝顯示讀數（mm）。</summary>
    public struct MeasurementTaken
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
    }

    /// <summary>作業單寫入。pointId＝欄位鍵，text＝填入值。</summary>
    public struct RecordWritten
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
    }

    /// <summary>黑板一個鍵的變更。newValue／oldValue 為 null＝不存在（刪除／新增）。forced＝值沒變但寫入者要求一定通知（例：放開升降時的最新距離）。</summary>
    public readonly struct StateChange
    {
        public readonly string key, oldValue, newValue;
        public readonly bool forced;

        public StateChange(string key, string oldValue, string newValue, bool forced = false)
        {
            this.key = key; this.oldValue = oldValue; this.newValue = newValue; this.forced = forced;
        }

        /// <summary>新值的數字（不是數字時為 0）。</summary>
        public float Value => float.TryParse(newValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f;
        public override string ToString() => $"{key}: {oldValue ?? "∅"} → {newValue ?? "∅"}";
    }

    /// <summary>
    /// 黑板值改變。一筆寫入或一整批（ISimState.BeginBatch）Commit 後只發一次，Changes 帶本批全部 (key, old, new)，發出時黑板已全部寫好。
    /// 舊的單筆欄位 targetId／text／value＝第一筆變更（只有一筆時與以前相同）；要看特定鍵請用 Has／TryGet／Any。
    /// restored＝來自 ISimState.Restore（快照還原：全部寫入後逐鍵通知）。
    /// </summary>
    public struct StateChanged
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
        public bool restored;
        System.Collections.Generic.IReadOnlyList<StateChange> changes;

        /// <summary>本次通知的全部變更（至少一筆）。手動建立、沒有指定時由 targetId／text 組成一筆。</summary>
        public System.Collections.Generic.IReadOnlyList<StateChange> Changes
        {
            get => changes ?? (targetId == null ? System.Array.Empty<StateChange>() : new[] { new StateChange(targetId, null, text) });
            set => changes = value;
        }

        public bool Has(string key) => TryGet(key, out _);

        public bool TryGet(string key, out StateChange change)
        {
            foreach (var c in Changes) if (c.key == key) { change = c; return true; }
            change = default;
            return false;
        }

        public bool Any(System.Func<string, bool> keyMatches)
        {
            foreach (var c in Changes) if (keyMatches(c.key)) return true;
            return false;
        }
    }

    public struct StepStarted
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
        public string stepId, title, instruction, toolHint, targetHint, side, sideLabel;
        public int index, total, runIndex, runTotal;
        public string[] targetIds;
        public bool showBoltNumbers, showOutwardArrow;
        public string highlightIds;   // 逗號分隔，已展開側別
        public string why;            // 為什麼（一句話）
        public string checklist;      // 小步驟，以換行分隔，每項「文字|條件」，已展開側別
        public string subActions;     // 小動作，以換行分隔，每項 文字\u001f目標\u001f工具\u001f條件\u001f為什麼，已展開側別
        public string valueTags;   // 例 "3.0 mm=保養表值;25 N·m=示例值"
    }

    public struct StepCompleted
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
        public string stepId, side;
    }

    public struct ErrorDetected
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
        public string errorId, stepId, what, why, fix;
        public Severity severity;
        public bool blocks;
    }

    /// <summary>
    /// 步驟卡關（防護網偵測，只記錄、不推進步驟、不扣分）。kind：
    /// missed_commit＝小動作與完成條件都成立、步驟卻沒推進（判定漏觸發，同時記一筆 Error）；
    /// rule_mismatch＝小動作全部完成，但完成條件超過 3 秒仍不成立。
    /// conditionIds＝未成立的條件（規則型別:鍵，以換行分隔），values＝當時的黑板值（鍵=值，以換行分隔）。
    /// </summary>
    public struct StepStallDetected
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
        public string stepId, side, kind, conditionIds, values, explain;
    }

    public struct ProcedureCompleted
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
        public string procedureId;
    }

    /// <summary>目前手上的工具改變。toolId 空字串＝徒手。</summary>
    public struct ToolSelected
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
    }

    /// <summary>要求重做目前步驟（錯誤對話框的「從這一步重做」）。</summary>
    public struct RestartStepRequested
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
    }

    /// <summary>提示要高亮的目標。text＝以逗號分隔的 ID。</summary>
    public struct HighlightRequested
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
    }

    /// <summary>要求攝影機帶到某目標（targetId 空白＝目前步驟／小動作的目標）。由 UI「帶我過去」或 C 鍵發出，Interaction 處理。</summary>
    public struct FocusRequested
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
    }

    /// <summary>指到的物件改變（桌面滑鼠或 VR 右手射線，只是指著、不算確認）。targetId＝新指到的 ID（空字串＝沒指到），text＝來源 mouse / xr.right。</summary>
    public struct HoverChanged
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
    }

    /// <summary>從工具架掛位拿起工具（value＝掛位編號）。由 Worksite 的掛位發出，工具列處理。</summary>
    public struct ToolPickRequested
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
    }

    /// <summary>把工具掛回工具架掛位（value＝掛位編號）。由 Worksite 的掛位發出，工具列處理。</summary>
    public struct ToolReturnRequested
    {
        public string targetId, pointId, toolId, text; public float value, time; public bool isReplay;
    }
}
