using System.Globalization;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Tools
{
    /// <summary>清潔布：按住左鍵在托架上拖曳，清除碳粉遮罩（送 clean:u,v 給托架）。</summary>
    public class CleaningClothTool : ToolBase
    {
        string strokeTarget = "";

        public override void Tick()
        {
            if (!PrimaryHeld || PointerState.OverUi) { strokeTarget = ""; return; }
            var cam = Camera.main;
            // VR：用右手射線掃（XR 模組每幀寫 PointerState.Ray）；桌面：滑鼠位置
            Ray ray;
            if (InputMode.XrActive) ray = PointerState.Ray;
            else if (cam != null) ray = cam.ScreenPointToRay(Input.mousePosition);
            else return;
            if (!Physics.Raycast(ray, out var hit, 50f)) return;
            var obj = hit.collider.GetComponentInParent<ISimObject>();
            if (obj == null || !obj.Id.EndsWith(".bracket")) return;
            if (strokeTarget != obj.Id)
            {
                if (!Ask(obj.Id, "clean")) { strokeTarget = ""; return; }
                strokeTarget = obj.Id;
            }
            var local = hit.collider.transform.InverseTransformPoint(hit.point);
            float u = Mathf.Clamp01(local.x + 0.5f), v = Mathf.Clamp01(local.y + 0.5f);
            Perform(obj.Id, "clean:" + u.ToString("0.###", CultureInfo.InvariantCulture) + "," + v.ToString("0.###", CultureInfo.InvariantCulture));
        }
    }
}
