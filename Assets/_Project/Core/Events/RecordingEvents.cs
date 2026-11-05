namespace PantographSim.Core
{
    /// <summary>介面功能名稱（UiCommandIssued.name）。桌面按鈕／快捷鍵與 VR 按鍵按下同一功能時都發。</summary>
    public static class UiCommands
    {
        public const string FocusTarget = "FocusTarget";
        public const string Help = "Help";
        public const string Undo = "Undo";
        public const string ResetTool = "ResetTool";
        public const string RestartStep = "RestartStep";
        public const string HintPanelToggle = "HintPanelToggle";
        public const string WorkOrderToggle = "WorkOrderToggle";
        /// <summary>練習模式卡關時按「提示」看還差什麼（計入報表的提示次數）。</summary>
        public const string StallHint = "StallHint";

        public static readonly string[] All = { FocusTarget, Help, Undo, ResetTool, RestartStep, HintPanelToggle, WorkOrderToggle, StallHint };

        /// <summary>發出 UiCommandIssued（stepId＝目前步驟；text＝來源，例 ui.button / key.H / xr.B）。</summary>
        public static void Issue(string name, string source)
        {
            var bus = Sim.Bus;
            if (bus == null) return;
            bus.Publish(new UiCommandIssued { name = name, stepId = Sim.State?.Get("procedure.current") ?? "", text = source ?? "", time = SimClock.Now });
        }
    }

    /// <summary>
    /// R1 T3：學員用左握把把面板移到新位置（放開時發一次）。panelId＝board／dialog；standPointId＝當時所在的站點；
    /// x,y,z,yaw＝相對該站點的位置與水平轉向（重播照這個還原面板位置）。
    /// </summary>
    public struct PanelMoved
    {
        public string panelId, standPointId; public float x, y, z, yaw; public float time;
    }
    /// <summary>學員按了介面功能（帶我過去、說明、上一步、重設工具、重做本步、收起提示板、作業單）。</summary>
    public struct UiCommandIssued
    {
        public string name, stepId, text; public float time; public bool isReplay;
    }

    /// <summary>
    /// 結算結果：Training 計分完成時發一次，數字和結算畫面顯示的完全相同（分數與錯誤數的唯一來源）。
    /// incompleteSteps＝沒有完成也沒有被跳過的執行步驟（RunId）。
    /// </summary>
    public struct SessionResult
    {
        public bool passed;
        public float score, totalSeconds;
        public int minor, major, critical;
        public string[] incompleteSteps;
        public float time; public bool isReplay;
    }

    /// <summary>編輯器的測試驅動走到指定步驟、交給人接手：這之前的操作不是學員做的，不計成績、不進報表。tag＝紀錄上的標示。</summary>
    public struct TestDriverHandoff
    {
        public string tag; public float time;
    }

    /// <summary>要求開始／停止錄影。text：toggle（預設）／start／stop；targetId＝來源（key.F9、xr.menu、xr.wrist）。</summary>
    public struct VideoCaptureToggled
    {
        public string text, targetId; public float time; public bool isReplay;
    }

    /// <summary>影片開始寫檔。file＝完整路徑；t＝開始時的 SimClock 時間（紀錄用它換算「影片 0 秒＝紀錄第幾秒」）。</summary>
    public struct VideoStarted
    {
        public string file; public float t; public float time; public bool isReplay;
    }

    /// <summary>影片停止並收尾（reason：user／play_exit／editor_quit／split）。</summary>
    public struct VideoStopped
    {
        public string file, reason; public float t; public float time; public bool isReplay;
    }
}
