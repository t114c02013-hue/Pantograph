using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Tools
{
    /// <summary>
    /// 第一次拿起某工具時顯示它的教學（ToolDefinition 的 3–5 頁）。內容寫進黑板 ui.tutorial.*，由 UI 顯示；
    /// 介面送 ActionRequested(ui.tutorial, close) 後記為已看過。
    /// </summary>
    public class ToolTutorialTrigger : MonoBehaviour
    {
        public const char PageSep = '\u001e', FieldSep = '\u001f';
        ToolBelt belt;
        bool bound;

        void Start()
        {
            belt = GetComponent<ToolBelt>();
            if (!Sim.Ready) return;
            Sim.Bus.Subscribe<ToolSelected>(OnSelected);
            Sim.Bus.Subscribe<ActionRequested>(OnRequest);
            bound = true;
        }

        void OnDestroy()
        {
            if (!bound || Sim.Bus == null) return;
            Sim.Bus.Unsubscribe<ToolSelected>(OnSelected);
            Sim.Bus.Unsubscribe<ActionRequested>(OnRequest);
        }

        void OnSelected(ToolSelected e)
        {
            if (string.IsNullOrEmpty(e.toolId) || Sim.State.GetBool(e.toolId + ".tutorial_seen")) return;
            if (InputMode.XrActive) return;   // terry 試玩：VR 的使用說明看工具旁的附記（XrGesture），不跳視窗
            Show(e.toolId);
        }

        public void Show(string toolId)
        {
            var tool = belt != null ? belt.Tools.FirstOrDefault(t => t.ToolId == toolId) : null;
            var pages = tool?.Definition?.tutorial?.pages;
            if (pages == null || pages.Length == 0) return;
            var s = Sim.State;
            s.Set("ui.tutorial.title", tool.DisplayName);
            s.Set("ui.tutorial.pages", string.Join(PageSep.ToString(), pages.Select(p => p.title + FieldSep + p.body)));
            s.Set("ui.tutorial.tool", toolId);
        }

        void OnRequest(ActionRequested e)
        {
            if (e.targetId != "ui.tutorial") return;
            if (e.text == "close")
            {
                var id = Sim.State.Get("ui.tutorial.tool");
                if (!string.IsNullOrEmpty(id)) Sim.State.SetBool(id + ".tutorial_seen", true);
                Sim.State.Set("ui.tutorial.tool", "");
            }
            else if (e.text != null && e.text.StartsWith("show:")) Show(e.text.Substring(5));
        }
    }
}
