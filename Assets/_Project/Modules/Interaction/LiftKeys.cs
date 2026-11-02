using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Interaction
{
    /// <summary>
    /// 升降控制器的鍵盤：按住 PageUp／↑ 寫 ctrl.lift.cmd=Up，按住 PageDown／↓ 寫 Down，放開寫 Stop。
    /// 只寫黑板，不認識機構；介面按鈕寫同一個鍵。輸入框打字時不作用。
    /// </summary>
    public class LiftKeys : MonoBehaviour
    {
        const string CmdKey = SimIds.LiftCmd;
        string lastKeyCmd = "";

        void Update()
        {
            var s = Sim.State;
            if (s == null || !s.GetBool("training.running")) return;
            string cmd = "";
            if (!PointerState.TypingInUi)
            {
                if (Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.UpArrow)) cmd = "Up";
                if (Input.GetKey(KeyCode.PageDown) || Input.GetKey(KeyCode.DownArrow)) cmd = "Down";
            }
            if (cmd == lastKeyCmd) return;
            // 放開按鍵：只有在目前指令是鍵盤下的時候才停（不干擾正在按住的介面按鈕）
            if (cmd != "") LiftControl.Send(s, cmd);
            else if (s.Get(CmdKey) == lastKeyCmd) LiftControl.Release(s);   // 放開：最新距離與 Stop 同一批
            lastKeyCmd = cmd;
        }
    }
}
