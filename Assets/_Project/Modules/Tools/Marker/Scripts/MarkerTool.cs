using PantographSim.Core;

namespace PantographSim.Tools
{
    /// <summary>標記筆：點已鎖到扭力的螺栓，畫一條跨螺栓頭與托架的標記線。</summary>
    public class MarkerTool : ToolBase
    {
        public override void OnClick(ActionRequested e)
        {
            if (!e.targetId.Contains(".bolt_")) return;
            if (State.Get(e.targetId + ".state") != "Torqued") { State.Set(Key("message"), "螺栓要先鎖到規定扭力才畫標記"); return; }
            if (Ask(e.targetId, "mark")) Perform(e.targetId, "mark");
        }
    }
}
