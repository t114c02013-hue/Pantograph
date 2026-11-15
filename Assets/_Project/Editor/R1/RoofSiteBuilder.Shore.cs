using System.Text;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Worksite;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// terry 175B（2026-11-15）：日常課程外接電源箱 ENV_ShoreBox（Courses.json 只在日常顯示）。FBX 交付前用方塊，節點名照建模師：
    /// Lamp_SupplyOn／Lamp_SupplyOff、Switch_Supply、Hook_Lock_Supply、PlugHolster/PlugSeat、ShorePlug（GrabPoint、CableAnchor）、ShoreSocket/PlugSeat。
    /// 箱前地面加一塊 Walkable（shoreWalkMin／Max，X 停在檢修坑外）。數值都在 RoofSceneDef／ToolRulesDef。
    /// </summary>
    public static partial class RoofSiteBuilder
    {
        static void BuildShoreBox(Result res, RoofSceneDef d, StringBuilder sb)
        {
            var p = Piece(res.env, "ENV_ShoreBox");
            var o = d.shoreBoxPos;   // 正面朝 +X（車）
            var mBox = Mat("shoreBox", new Color(0.55f, 0.58f, 0.6f)); var mDark = Mat("shoreDark", new Color(0.12f, 0.12f, 0.13f));
            Box("Post", p.visual, o + new Vector3(0f, 0.7f, 0f), new Vector3(0.08f, 1.4f, 0.08f), mDark, collider: false);
            var body = Box("ShoreBox_Body", p.visual, o + new Vector3(0.06f, 1.1f, 0f), new Vector3(0.12f, 0.45f, 0.35f), mBox, collider: false);
            float fx = o.x + 0.125f;   // 箱子正面
            var lampOn = SliceModelBuilder.Prim(PrimitiveType.Sphere, "Lamp_SupplyOn", p.visual, Vector3.zero, Vector3.one * 0.04f, mBox);
            lampOn.transform.position = new Vector3(fx, o.y + 1.27f, o.z - 0.08f);
            var lampOff = SliceModelBuilder.Prim(PrimitiveType.Sphere, "Lamp_SupplyOff", p.visual, Vector3.zero, Vector3.one * 0.04f, mBox);
            lampOff.transform.position = new Vector3(fx, o.y + 1.27f, o.z + 0.08f);
            Object.DestroyImmediate(lampOff.GetComponent<Collider>());
            var sw = Box("Switch_Supply", p.visual, new Vector3(fx + 0.01f, o.y + 1.12f, o.z), new Vector3(0.03f, 0.08f, 0.03f), mDark);
            Box("Hook_Lock_Supply", p.visual, new Vector3(fx + 0.01f, o.y + 1.12f, o.z + 0.05f), new Vector3(0.02f, 0.03f, 0.01f), mDark, collider: false);
            var holster = Box("PlugHolster", p.visual, new Vector3(fx + 0.02f, o.y + 0.95f, o.z), new Vector3(0.04f, 0.06f, 0.08f), mDark, collider: false);
            var holderSeat = Child(holster.transform, "PlugSeat"); holderSeat.position = new Vector3(fx + 0.07f, o.y + 0.95f, o.z);
            var outlet = Child(body.transform, "CableOut"); outlet.position = new Vector3(o.x + 0.06f, o.y + 0.86f, o.z);
            var s = d.carShoreSocketPos;
            var socket = Box("ShoreSocket", p.visual, s + new Vector3(-0.015f, 0f, 0f), new Vector3(0.03f, 0.1f, 0.1f), mDark, collider: false);
            var carSeat = Child(socket.transform, "PlugSeat"); carSeat.position = s + new Vector3(-0.07f, 0f, 0f);
            var plug = Box("ShorePlug", p.visual, carSeat.position, new Vector3(0.1f, 0.06f, 0.06f), Mat("shorePlug", new Color(0.95f, 0.75f, 0.1f)));
            Child(plug.transform, "GrabPoint");
            var anchor = Child(plug.transform, "CableAnchor"); anchor.localPosition = new Vector3(-0.5f, 0f, 0f);
            var line = Child(p.visual, "Cable").gameObject.AddComponent<LineRenderer>();
            line.positionCount = 2; line.widthMultiplier = 0.02f; line.sharedMaterial = mDark; line.useWorldSpace = true;

            var box = lampOn.AddComponent<ShoreBox>();
            box.Configure("ip.shore_box.lamp", "供電中紅燈");
            box.Setup(lampOn.GetComponent<Renderer>(), lampOff.GetComponent<Renderer>(), sw.transform, outlet, anchor, line);
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
