using UnityEngine;

namespace PantographSim.Core
{
    /// <summary>模擬中有 ID 的物件（零件、互動點、場景件）。ID 例：part.carbon_strip_L、ip.carbon_strip_L.bolt_1。</summary>
    public interface ISimObject
    {
        string Id { get; }
    }

    /// <summary>可被滑鼠指到的物件：顯示名稱、滑過高亮。</summary>
    public interface IInteractable : ISimObject
    {
        string DisplayName { get; }
        /// <summary>徒手點擊時的動作，例 inspect（只看）或 click。</summary>
        string DefaultVerb { get; }
        void SetHovered(bool hovered);
    }

    /// <summary>可量測的零件：回傳某量測點的真實值（mm）。</summary>
    public interface IMeasurable : ISimObject
    {
        bool TryGetTrueValue(string pointId, out float valueMm);
    }

    /// <summary>可被工具作用的物件。</summary>
    public interface IToolTarget : ISimObject
    {
        bool Accepts(string toolId);
    }

    /// <summary>動作種類：Inspect＝只是看／點選（不改變零件狀態，安全規則一律放行）；Operate＝會改變狀態。</summary>
    public enum ActionKind { Inspect, Operate }

    /// <summary>動作請求：誰（工具）對哪個目標做什麼。</summary>
    public struct ActionRequest
    {
        public string toolId;    // 空字串＝徒手
        public string targetId;
        public string pointId;
        public string verb;      // click、rotate、torque、clean、inspect…
        public ActionKind kind;

        public ActionRequest(string toolId, string targetId, string verb, string pointId = "")
        {
            this.toolId = toolId ?? "";
            this.targetId = targetId ?? "";
            this.verb = verb ?? "";
            this.pointId = pointId ?? "";
            kind = KindOf(this.toolId, this.verb);
        }

        /// <summary>徒手的 inspect／click／select 屬於查看；拿著工具或其他動作都是操作。</summary>
        public static ActionKind KindOf(string toolId, string verb) =>
            string.IsNullOrEmpty(toolId) && (verb == "inspect" || verb == "click" || verb == "select") ? ActionKind.Inspect
            : verb == "inspect" || verb == "select" ? ActionKind.Inspect : ActionKind.Operate;
    }

    public struct GuardResult
    {
        public bool allowed;
        public string errorId;

        public static GuardResult Allow => new GuardResult { allowed = true, errorId = "" };
        public static GuardResult Block(string errorId) => new GuardResult { allowed = false, errorId = errorId };
    }

    /// <summary>工具或零件改變狀態前必須先問。被擋時由守門者自行發 ActionBlocked 與 ErrorDetected。</summary>
    public interface IActionGuard
    {
        GuardResult Check(ActionRequest request);
    }

    /// <summary>步驟判定完成前會先問所有 Gate。</summary>
    public interface IStepGate
    {
        bool CanComplete(string stepId, out string reason);
    }

    /// <summary>依 ID 查場景中物件的位置（給提示疊圖用）。</summary>
    public interface ISceneLocator
    {
        bool TryGetPosition(string id, out Vector3 position);
    }
}
