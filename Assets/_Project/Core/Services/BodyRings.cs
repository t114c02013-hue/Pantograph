namespace PantographSim.Core
{
    /// <summary>
    /// R1.5b b4（D31）：身上的兩個操作點——安全帶胸前收納扣環（掛鉤收起／取下）、安全帶工具環（短接地線掛上／拿下）。
    /// 桌面版工具列按鈕和 VR 伸手按握把都送出這裡算出的同一個請求（工具自己的 OnCommand 處理；狀態邏輯在 Data 的 HarnessLogic、GroundingLeadLogic）。UI 只能引用 Core，所以 ID 放這裡。
    /// </summary>
    public static class BodyRings
    {
        public const string ChestRing = "tool.safety_harness.chest_ring", ToolRing = "tool.safety_harness.tool_ring";
        public const string HooksStowed = "harness.hooks_stowed", ToolGroundingLead = "tool.grounding_lead", LeadCarried = "tool.grounding_lead.carried";

        /// <summary>胸前扣環：掛鉤收著＝取下，否則＝收起。</summary>
        public static (string target, string text) ChestRingRequest(ISimState s) => (SimIds.ToolHarness, s.GetBool(HooksStowed) ? "take" : "stow");

        /// <summary>工具環：手上拿著短接地線＝掛上，否則＝拿下（沒掛著時由工具提示）。</summary>
        public static (string target, string text) ToolRingRequest(ISimState s) => (ToolGroundingLead, s.Get(SimIds.ActiveTool) == ToolGroundingLead ? "ring" : "take");
    }
}
