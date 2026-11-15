using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Worksite;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// terry 175B（2026-11-15）：日常課程外接電源箱 ENV_ShoreBox（Courses.json 只在日常顯示）。外觀用建模師 A 的 FBX（gen_depot_props v1.0），節點：
    /// Lamp_SupplyOn／Lamp_SupplyOff、Switch_Supply、Hook_Lock_Supply、PlugHolster/PlugSeat、ShorePlug（GrabPoint、CableAnchor）、ShoreSocket/PlugSeat。
    /// 箱前地面加一塊 Walkable（shoreWalkMin／Max，X 停在檢修坑外）。數值都在 RoofSceneDef／ToolRulesDef。
    /// </summary>
    public static partial class RoofSiteBuilder
    {
        static void BuildShoreBox(Result res, RoofSceneDef d, StringBuilder sb)
        {
            var p = Piece(res.env, "ENV_ShoreBox");
            var o = d.shoreBoxPos;   // 正面朝 +X（車）
            var mDark = Mat("shoreDark", new Color(0.12f, 0.12f, 0.13f));
            // 建模師 A gen_depot_props v1.0：電源箱原點在立柱底板底面中心、正面局部 −Z（匯入後；轉成朝 +X 車）；插座原點在面板中心、正面局部 −Z（轉成朝 −X 車外）；插頭原點在前端、插入方向 −Z
            var boxVis = Fbx("ENV_ShorePowerBox", null, p.visual, o, yaw: -90f, rename: "ShorePowerBox");
            Transform N(GameObject root, string n) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
            Renderer Lens(Transform lamp) => lamp.GetComponentsInChildren<Renderer>(true).First(r => r.name.EndsWith("_Lens"));
            var lampOn = N(boxVis, "Lamp_SupplyOn").gameObject; var lampOff = N(boxVis, "Lamp_SupplyOff");
            var onCol = lampOn.AddComponent<SphereCollider>(); onCol.radius = 0.025f; onCol.center = lampOn.transform.InverseTransformPoint(Lens(lampOn.transform).bounds.center);   // 點紅燈確認
            var sw = N(boxVis, "Switch_Supply").gameObject;
            var handle = sw.GetComponentsInChildren<Renderer>(true).First(r => r.name == "Switch_Supply_Handle");
            var swCol = sw.AddComponent<BoxCollider>(); swCol.center = sw.transform.InverseTransformPoint(handle.bounds.center); swCol.size = new Vector3(0.05f, 0.14f, 0.05f);   // 把手（ON 直立）
            var holderSeat = N(boxVis, "PlugSeat");
            holderSeat.rotation = Quaternion.LookRotation(Vector3.up, Vector3.right);   // 掛在杯座：插入方向（插頭 −Z）朝下
            var outlet = N(boxVis, "CableAnchor");
            var s = d.carShoreSocketPos;
            var socket = Fbx("ENV_ShoreSocket", null, p.visual, s + new Vector3(-0.012f, 0f, 0f), yaw: 90f, rename: "ShoreSocket");   // 凸緣背面貼車側
            var carSeat = N(socket, "PlugSeat");
            carSeat.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);   // 插好：插頭 −Z（插入方向）朝 +X 插進車側、握柄朝車外
            var plug = Fbx("ENV_ShorePlug", null, p.visual, carSeat.position, rename: "ShorePlug");
            plug.transform.SetPositionAndRotation(carSeat.position, carSeat.rotation);
            var pb = plug.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
            var plugCol = plug.AddComponent<BoxCollider>(); plugCol.center = plug.transform.InverseTransformPoint(pb.center); plugCol.size = new Vector3(0.09f, 0.09f, 0.22f);
            var anchor = N(plug, "CableAnchor");
            var line = Child(p.visual, "Cable").gameObject.AddComponent<LineRenderer>();
            line.positionCount = 2; line.widthMultiplier = 0.02f; line.sharedMaterial = mDark; line.useWorldSpace = true;

            var box = lampOn.AddComponent<ShoreBox>();
            box.Configure("ip.shore_box.lamp", "供電中紅燈");
            box.Setup(Lens(lampOn.transform), Lens(lampOff), sw.transform, outlet, anchor, line);
            lampOn.AddComponent<Highlightable>();
            sw.AddComponent<CabSwitch>().Configure(CabControlLogic.ShoreSwitchId, "外接電源供電開關");
            sw.AddComponent<Highlightable>();
            plug.AddComponent<ShorePlug>().Configure(ShorePlugLogic.Id, "外接電源插頭");
            plug.GetComponent<ShorePlug>().Setup(carSeat, holderSeat);
            plug.AddComponent<Highlightable>();
            foreach (var (id, t) in new[] { ("ip.shore_box.lamp", lampOn.transform), (CabControlLogic.ShoreSwitchId, sw.transform), (ShorePlugLogic.Id, plug.transform) }) res.ids[id] = t;

            Vector2 a = d.shoreWalkMinXZ, b = d.shoreWalkMaxXZ;
            Col("Walkable_Shore", Child(res.env, "ENV_ShoreWalk"), new Vector3(a.x, d.groundY - 0.1f, a.y), new Vector3(b.x, d.groundY + 0.01f, b.y), SceneLayers.Walkable);   // 一直開著（站點高度、頭的邊界要打得到）
            res.equipment.Add(p.root);
            p.root.gameObject.SetActive(false);   // 存檔 inactive；日常課程由 Courses.json 顯示

            // 伸手估計（StationSet.armReachHorizontal）：頭最遠到 Walkable 內縮後的邊，到各互動點的水平距離
            var set = R1Data.Stations; float inset = R1Data.Boundary.inset, headX = b.x - inset;
            var st = new Vector2(d.shoreStationXZ.x, d.shoreStationXZ.y);
            foreach (var (label, pt) in new[] { ("紅燈", lampOn.transform.position), ("開關", sw.transform.position), ("插頭座", holderSeat.position), ("車側插座的插頭", carSeat.position) })
            {
                float fromStation = Vector2.Distance(st, new Vector2(pt.x, pt.z)), fromLean = Vector2.Distance(new Vector2(headX, st.y), new Vector2(pt.x, pt.z));
                bool ok = Mathf.Min(fromStation, fromLean) <= set.armReachHorizontal + 1e-4f;
                sb.AppendLine($"{(ok ? "✔" : "✘")} 外接電源 {label}：站點水平 {fromStation:0.000} m、頭前傾到 X {headX:0.00} 時 {fromLean:0.000} m（伸手 ≤ {set.armReachHorizontal}）");
            }
            sb.AppendLine($"外接電源箱：{o:F2} 朝 +X、車側插座 {s:F2}、Walkable X {a.x}…{b.x}、Z {a.y}…{b.y}（檢修坑 |X| ≤ {d.pitHalfWidth}）");
        }
    }
}
