using System.Globalization;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Tools
{
    /// <summary>清潔布：按住左鍵在托架上拖曳，清除碳粉遮罩（送 clean:u,v 給托架）。R1.5b b4：也能擦絕緣子髒汙殼（圓柱 UV）。</summary>
    public class CleaningClothTool : ToolBase
    {
        string strokeTarget = "";

        public override void OnSelected() { PointerState.DragCaptured = true; }   // 拖曳是擦拭，不轉鏡頭：從哪裡開始拖都可以
        public override void OnDeselected() { PointerState.DragCaptured = false; }

        public override void Tick()
        {
            if (!PrimaryHeld || PointerState.OverUi) { strokeTarget = ""; return; }
            var cam = Camera.main;
            // VR：用右手射線掃（XR 模組每幀寫 PointerState.Ray）；桌面：滑鼠位置
            Ray ray;
            if (InputMode.XrActive) ray = PointerState.Ray;
            else if (cam != null) ray = cam.ScreenPointToRay(Input.mousePosition);
            else return;
            // 不打不可見的邊界牆；絕緣子的 COL_Dirt 是 trigger。擦拭只認托架和絕緣子：線上其他東西不擋布
            var hits = Physics.RaycastAll(ray, 50f, SceneLayers.InteractMask, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            RaycastHit hit = default; ISimObject obj = null;
            foreach (var h in hits)
            {
                var o = h.collider.GetComponentInParent<ISimObject>();
                if (o != null && (o.Id.EndsWith(".bracket") || o.Id.StartsWith("ip.roof.insulator_"))) { hit = h; obj = o; break; }
            }
            if (obj == null) return;
            bool insulator = obj.Id.StartsWith("ip.roof.insulator_");   // R1.5b b4（D43）：絕緣子髒汙殼
            if (strokeTarget != obj.Id)
            {
                if (!Ask(obj.Id, "clean")) { strokeTarget = ""; return; }
                strokeTarget = obj.Id;
            }
            var local = hit.collider.transform.InverseTransformPoint(hit.point);
            float u = Mathf.Clamp01(local.x + 0.5f), v = Mathf.Clamp01(local.y + 0.5f);
            if (insulator) { u = Mathf.Repeat(Mathf.Atan2(local.z, local.x) / (2f * Mathf.PI) + 0.5f, 1f); v = Mathf.Clamp01(local.y * 0.5f + 0.5f); }   // 內建圓柱：Y 在 ±1，繞 Y 軸展開
            Perform(obj.Id, "clean:" + u.ToString("0.###", CultureInfo.InvariantCulture) + "," + v.ToString("0.###", CultureInfo.InvariantCulture));
        }
    }
}
