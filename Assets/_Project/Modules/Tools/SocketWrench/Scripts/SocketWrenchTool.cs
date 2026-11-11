using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Tools
{
    /// <summary>
    /// 套筒扳手：套筒 10/13/17 mm、方向「鬆／鎖」（介面切換）。點螺栓套上後，用滾輪（每格 45°）或按住左鍵左右拖曳旋轉，累計角度。
    /// 螺栓已脫出時點它＝放進零件盒；方向是「鎖」且孔位空著時點它＝從零件盒取螺栓放回孔位。
    /// </summary>
    public class SocketWrenchTool : ToolBase
    {
        const float XrDegreesPerSecond = 90f;
        string engaged = "";
        bool dragging;
        float lastX;

        public override bool CapturesScroll => !string.IsNullOrEmpty(engaged);

        public override void OnSelected()
        {
            if (!State.Has(Key("size"))) State.Set(Key("size"), "10");
            if (!State.Has(Key("dir"))) State.Set(Key("dir"), "loosen");
        }

        public override void OnDeselected() { Release(); }

        public override void OnReset()
        {
            Release();
            ToolResetLogic.Reset(State, ToolId);
        }

        public override void OnCommand(string command)
        {
            if (command.StartsWith("size:")) State.Set(Key("size"), command.Substring(5));
            else if (command.StartsWith("dir:")) State.Set(Key("dir"), command.Substring(4));
            else if (command == "release") Release();
        }

        void Release() { engaged = ""; dragging = false; State?.Set(Key("engaged"), ""); }

        public override void OnClick(ActionRequested e)
        {
            if (e.targetId.StartsWith("env.bolt_tray_"))   // 點盤子＝把這一側脫出的螺栓收進去（步驟文字、帶我過去都指盤子）
            {
                string outBolt = SimIds.StripBoltOut(State, e.targetId.Substring("env.bolt_tray_".Length));
                if (outBolt.Length > 0 && Ask(outBolt, "take")) Perform(outBolt, "take");
                return;
            }
            if (!SimIds.IsBoltId(e.targetId)) return;
            string st = State.Get(e.targetId + ".state", "Tight");
            string dir = State.Get(Key("dir"), "loosen");
            if (st == "Out") { if (Ask(e.targetId, "take")) Perform(e.targetId, "take"); return; }
            if (st == "InTray")
            {
                // 從零件盒取螺栓放回孔位（v5：先放回四支，再把方向切到「鎖」預鎖）
                if (SimIds.CanPlaceBolt(State, e.targetId) && Ask(e.targetId, "place")) Perform(e.targetId, "place");
                return;
            }
            if (!Ask(e.targetId, "engage")) return;
            engaged = e.targetId;
            State.Set(Key("engaged"), engaged);
            dragging = true;
            lastX = Input.mousePosition.x;
        }

        public override void Tick()
        {
            if (string.IsNullOrEmpty(engaged)) return;
            float deg = Mathf.Abs(ScrollNotches()) * SocketLogic.DegreesPerNotch;
            if (dragging)
            {
                if (Input.GetMouseButton(0)) { deg += Mathf.Abs(Input.mousePosition.x - lastX) * SocketLogic.DegreesPerPixel; lastX = Input.mousePosition.x; }
                else if (PointerState.XrTriggerHeld) deg += XrDegreesPerSecond * Time.deltaTime;   // VR：按住扳機朝設定方向轉
                else dragging = false;
            }
            if (deg <= 0f) return;
            if (!Ask(engaged, "rotate")) { Release(); return; }
            Perform(engaged, $"rotate:{State.Get(Key("size"), "10")}:{State.Get(Key("dir"), "loosen")}", deg);
            string st = State.Get(engaged + ".state");
            if (st == "Out" || st == "InTray") Release();
        }
    }
}
