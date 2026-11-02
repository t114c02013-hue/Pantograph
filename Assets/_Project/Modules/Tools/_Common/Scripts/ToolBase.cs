using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Tools
{
    /// <summary>
    /// 工具基底。ToolBelt 決定目前拿哪一支；場景點擊（ActionRequested）會轉給目前工具的 OnClick，
    /// 介面按鈕（目標是工具 ID）轉給 OnCommand。改變任何東西前一定先 Ask（IActionGuard）。
    /// 工具把結果以 ActionPerformed 送給目標零件，由零件自己改狀態。
    /// </summary>
    public abstract class ToolBase : MonoBehaviour
    {
        [SerializeField] protected ToolDefinition definition;
        [SerializeField] string fallbackId;

        public ToolDefinition Definition => definition;
        public string ToolId => definition != null ? definition.toolId : fallbackId;
        public string DisplayName => definition != null ? definition.displayName : ToolId;
        public int Hotkey => definition != null ? definition.hotkey : 0;
        public bool IsActive { get; internal set; }

        protected ISimState State => Sim.State;
        protected IEventBus Bus => Sim.Bus;

        public void Setup(ToolDefinition def, string id) { definition = def; fallbackId = id; }

        /// <summary>拿著這支工具時要不要占用滑鼠滾輪（攝影機就不會縮放）。</summary>
        public virtual bool CapturesScroll => false;

        /// <summary>重新拿起／重設：工具的暫存操作狀態回到剛拿起時（設定值、方向、量爪、按住中的動作）；已寫進零件的結果不動。</summary>
        public virtual void OnReset() { }

        public virtual void OnSelected() { }
        public virtual void OnDeselected() { }
        public virtual void OnClick(ActionRequested e) { }
        public virtual void OnCommand(string command) { }
        public virtual void Tick() { }

        protected bool Ask(string targetId, string verb) => Sim.Ask(ToolId, targetId, verb);
        protected void Perform(string targetId, string text, float value = 0f) => Sim.Perform(ToolId, targetId, text, value);
        protected string Key(string suffix) => ToolId + "." + suffix;

        /// <summary>滾輪格數（滑鼠滾輪，或 VR 右搖桿上下）。</summary>
        protected static float ScrollNotches()
        {
            float y = Input.mouseScrollDelta.y + PointerState.ConsumeScroll();
            return Mathf.Abs(y) < 0.01f ? 0f : Mathf.Sign(y) * Mathf.Max(1f, Mathf.Round(Mathf.Abs(y)));
        }

        /// <summary>「按住」中（滑鼠左鍵，或 VR 右手扳機）。</summary>
        protected static bool PrimaryHeld => Input.GetMouseButton(0) || PointerState.XrTriggerHeld;
    }
}
