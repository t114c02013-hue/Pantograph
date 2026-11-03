using System.Globalization;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Tools
{
    /// <summary>
    /// 扭力扳手（喀聲式）：設定值 ±0.5 或直接輸入（5–50 N·m）。在已貼合的螺栓上按住左鍵，扭力每秒 +8 N·m；
    /// 到達設定值時喀一聲、扳手閃色、顯示「到達設定值」；放開就記錄實際扭力。過了還按住會繼續上升（會過鎖）。
    /// </summary>
    public class TorqueWrenchTool : ToolBase
    {
        string target = "";
        bool holding;
        float current;

        public override void OnReset()
        {
            holding = false; target = ""; current = 0f;   // 取消按住中的施力
            ToolResetLogic.Reset(State, ToolId);
        }

        public override void OnSelected()
        {
            if (!State.Has(Key("setting"))) State.SetFloat(Key("setting"), 20f);
            State.SetFloat(Key("current"), 0f);
        }

        public override void OnCommand(string command)
        {
            if (!command.StartsWith("set:")) return;
            float setting = State.GetFloat(Key("setting"), 20f);
            string v = command.Substring(4);
            if (v == "+") setting = TorqueLogic.Adjust(setting, TorqueLogic.Step);
            else if (v == "-") setting = TorqueLogic.Adjust(setting, -TorqueLogic.Step);
            else if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) setting = TorqueLogic.Adjust(f, 0f);
            State.SetFloat(Key("setting"), setting);
        }

        /// <summary>K3：這一步剛好到扭力就播一次喀聲（和閃色、震動同一個「到扭力」事件，不另寫判定）；回傳是否剛到。</summary>
        public static bool ReachStep(float before, float current, float setting, AudioClip clip)
        {
            if (!TorqueLogic.JustReached(before, current, setting)) return false;
            ToolInventory.PlayClick(clip);
            return true;
        }

        public override void OnClick(ActionRequested e)
        {
            if (!e.targetId.Contains(".bolt_")) return;
            string st = State.Get(e.targetId + ".state");
            if (st != "Snug" && st != "Torqued") { State.Set(Key("message"), "這支螺栓還沒預鎖到貼合，先用套筒扳手預鎖"); return; }
            if (!Ask(e.targetId, "torque")) return;
            target = e.targetId;
            holding = true;
            current = st == "Torqued" ? State.GetFloat(e.targetId + ".torque_Nm") : TorqueLogic.SnugTorque;
            State.SetBool(Key("reached"), false);
            State.Set(Key("target"), target);
        }

        public override void Tick()
        {
            if (!holding) return;
            if (PrimaryHeld)
            {
                float setting = State.GetFloat(Key("setting"), 20f);
                float before = current;
                current = TorqueLogic.Hold(current, Time.deltaTime);
                State.SetFloat(Key("current"), Mathf.Round(current * 10f) / 10f);
                if (ReachStep(before, current, setting, Definition != null ? Definition.clickClip : null))
                {
                    State.SetBool(Key("reached"), true);
                    State.SetFloat(Key("flash_time"), Time.time);
                }
                return;
            }
            holding = false;
            float settingNow = State.GetFloat(Key("setting"), 20f);
            Perform(target, "torque:" + settingNow.ToString(CultureInfo.InvariantCulture), Mathf.Round(current * 10f) / 10f);
            State.Set(Key("target"), "");
        }
    }
}
