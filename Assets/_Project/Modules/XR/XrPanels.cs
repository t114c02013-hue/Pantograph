using System.Collections.Generic;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace PantographSim.XR
{
    /// <summary>
    /// T3 面板固定在世界中、用左握把抓把手移動（步驟提示面板 board、對話框／錯誤視窗 dialog）：
    /// ・介面（XrPanelLayout）把面板 Canvas 登記在 SceneRegistry：xr.panel.board、xr.panel.dialog；這裡在面板上緣加把手 UI_PanelHandle（亮橘、握紋、hover 發亮），背面加不透光底板。
    /// ・只有把手掛 XRGrabInteractable（Kinematic、不受重力、throwOnDetach 關），Interaction Layer＝PanelHandle，只有左手 XRDirectInteractor 抓得到。
    /// ・放開後只保留水平轉向並轉正面向頭；位置記成「相對目前站點」，每塊面板各站點各記一份（整場保留）；換站時套用那一站的位置，沒存過用 PanelLayout 的預設。
    /// ・xr.panels.recall 改變＝面板叫回眼前；放開時發 PanelMoved（紀錄、重播還原、控制器教學用）。重播時照 xr.panel.replay.&lt;id&gt; 擺。
    /// </summary>
    public class XrPanels : MonoBehaviour
    {
        public const string ManagedKey = "xr.panels.managed", RecallKey = "xr.panels.recall", ReplayPrefix = "xr.panel.replay.";
        public const string HandleLayerName = "PanelHandle";

        [SerializeField] PanelLayout layout;
        [SerializeField] Transform head;
        [SerializeField] XrTeleport teleport;

        class Entry
        {
            public string id; public Transform panel, handle, plate; public XRGrabInteractable grab; public Renderer handleRenderer;
            public bool grabbed; public Vector3 grabOffset; public Quaternion grabRot;
        }
        readonly List<Entry> entries = new List<Entry>();
        readonly PanelMemory memory = new PanelMemory();
        string station = "";
        float lastRecall = -1f;
        readonly Dictionary<string, string> replayApplied = new Dictionary<string, string>();

        public void Setup(PanelLayout l, Transform headT, XrTeleport tp) { layout = l; head = headT; teleport = tp; }

        void Update()
        {
            var s = Sim.State;
            if (s == null || Sim.Registry == null || layout == null || !InputMode.XrActive) return;
            if (entries.Count == 0) Build(s);
            if (entries.Count == 0) return;

            if (SimSession.IsReplay) { ApplyReplay(s); return; }

            string now = s.Get("xr.point");
            if (now != station) { station = now; foreach (var e in entries) if (!layout.Find(e.id).openInFront) PlaceForStation(e); }
            float recall = s.GetFloat(RecallKey, -1f);
            if (recall != lastRecall) { lastRecall = recall; Recall(); }

            foreach (var e in entries)
            {
                if (e.grabbed) e.panel.SetPositionAndRotation(e.handle.TransformPoint(e.grabOffset), e.handle.rotation * e.grabRot);
                else FollowHandle(e);
            }
        }

        /// <summary>開場（XrStartupSequencer 第 4 步）與重設高度：對話框（任務卡）擺到頭前、提示板擺到目前站點。之後固定不動。</summary>
        public void PlaceInFront(Vector3 eye, Vector3 forward)
        {
            if (entries.Count == 0 && Sim.State != null && Sim.Registry != null && layout != null) Build(Sim.State);
            station = Sim.State?.Get("xr.point") ?? station;
            foreach (var e in entries)
            {
                if (layout.Find(e.id).openInFront)
                {
                    var pose = PanelLogic.InFront(eye, forward, layout.taskCardDistance, layout.taskCardBelowEye);
                    e.panel.SetPositionAndRotation(pose.position, pose.rotation);
                }
                else PlaceForStation(e);
                FollowHandle(e);
            }
        }

        void Build(ISimState s)
        {
            foreach (var p in layout.panels)
            {
                if (!Sim.Registry.TryGetTransform("xr.panel." + p.panelId, out var panel) || panel == null) continue;
                var e = new Entry { id = p.panelId, panel = panel };
                var root = new GameObject("UI_PanelHandle_" + p.panelId);
                root.transform.SetParent(transform.parent, true);   // 不放在面板底下（抓的時候把手帶著面板走）
                var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
                vis.name = "Handle";
                vis.transform.SetParent(root.transform, false);
                vis.transform.localScale = layout.handleSize;
                e.handleRenderer = vis.GetComponent<Renderer>();
                Paint(e.handleRenderer, layout.handleColor);
                for (int k = -2; k <= 2; k++)   // 握紋
                {
                    var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(g.GetComponent<Collider>());
                    g.transform.SetParent(root.transform, false);
                    g.transform.localPosition = new Vector3(k * 0.03f, 0f, -layout.handleSize.z * 0.5f - 0.001f);
                    g.transform.localScale = new Vector3(0.004f, layout.handleSize.y * 0.8f, 0.002f);
                    Paint(g.GetComponent<Renderer>(), new Color(0.35f, 0.18f, 0.02f));
                }
                var rb = root.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
                var grab = root.AddComponent<XRGrabInteractable>();
                grab.colliders.Add(vis.GetComponent<Collider>());
                grab.movementType = XRBaseInteractable.MovementType.Kinematic;
                grab.throwOnDetach = false;
                grab.useDynamicAttach = true;
                grab.interactionLayers = InteractionLayerMask.GetMask(HandleLayerName);
                grab.hoverEntered.AddListener(_ => Paint(e.handleRenderer, Color.Lerp(layout.handleColor, Color.white, 0.5f)));
                grab.hoverExited.AddListener(_ => Paint(e.handleRenderer, layout.handleColor));
                grab.selectEntered.AddListener(_ => { e.grabbed = true; e.grabOffset = e.handle.InverseTransformPoint(e.panel.position); e.grabRot = Quaternion.Inverse(e.handle.rotation) * e.panel.rotation; });
                grab.selectExited.AddListener(_ => Released(e));
                e.handle = root.transform; e.grab = grab;
                e.plate = AddBackPlate(panel);
                entries.Add(e);
                FollowHandle(e);
            }
            if (entries.Count > 0) s.SetBool(ManagedKey, true);
        }

        static void Paint(Renderer r, Color c)
        {
            var b = new MaterialPropertyBlock(); b.SetColor("_Color", c); r.SetPropertyBlock(b);
        }

        /// <summary>面板背面的不透光底板（大小每幀跟著面板上顯示中的內容）。</summary>
        static Transform AddBackPlate(Transform panel)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "BackPlate";
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(panel, false);
            Paint(g.GetComponent<Renderer>(), new Color(0.12f, 0.13f, 0.15f));
            return g.transform;
        }

        /// <summary>面板上顯示中的內容範圍（面板本地座標；沒有內容回傳 false）。</summary>
        static bool ContentBounds(Transform panel, out Bounds b)
        {
            b = default; bool any = false;
            foreach (Transform c in panel)
            {
                if (!c.gameObject.activeSelf || !(c is RectTransform rc) || c.name == "BackPlate") continue;
                var cb = RectTransformUtility.CalculateRelativeRectTransformBounds(panel, rc);
                if (cb.size.x < 1f || cb.size.y < 1f) continue;
                if (!any) { b = cb; any = true; } else b.Encapsulate(cb);
            }
            return any;
        }

        /// <summary>把手放在內容上緣中央；底板蓋住內容背面。</summary>
        void FollowHandle(Entry e)
        {
            float sc = Mathf.Max(e.panel.lossyScale.y, 1e-5f);
            if (!ContentBounds(e.panel, out var b)) b = e.panel is RectTransform rt ? new Bounds(rt.rect.center, rt.rect.size) : new Bounds(Vector3.zero, Vector3.one * 100f);
            if (e.plate != null)
            {
                e.plate.localPosition = new Vector3(b.center.x, b.center.y, 0.006f / sc);
                e.plate.localScale = new Vector3(b.size.x, b.size.y, 0.004f / sc);
            }
            var top = e.panel.TransformPoint(new Vector3(b.center.x, b.max.y, 0f));
            e.handle.SetPositionAndRotation(top + e.panel.up * layout.handleSize.y, e.panel.rotation);
        }

        Transform StationT => teleport != null && teleport.Find(station) is XrTeleportPoint p ? p.transform : null;

        void PlaceForStation(Entry e)
        {
            var st = StationT;
            if (st == null) return;
            Pose pose;
            if (memory.TryGet(e.id, station, out var local, out var yaw)) pose = PanelLogic.FromLocal(st, local, yaw);
            else
            {
                var pos = PanelLogic.FromLocal(st, layout.Find(e.id).offset, 0f).position;
                pose = new Pose(pos, PanelLogic.FaceHead(pos, st.position + Vector3.up * 1.6f));
            }
            e.panel.SetPositionAndRotation(pose.position, pose.rotation);
        }

        void Released(Entry e)
        {
            e.grabbed = false;
            if (head != null) e.panel.rotation = PanelLogic.FaceHead(e.panel.position, head.position);   // 只留水平轉向、轉正面向頭
            Remember(e);
        }

        void Remember(Entry e)
        {
            var st = StationT;
            if (st == null) return;
            var (local, yaw) = PanelLogic.ToLocal(st, e.panel.position, e.panel.rotation);
            memory.Save(e.id, station, local, yaw);
            Sim.Bus.Publish(new PanelMoved { panelId = e.id, standPointId = station, x = local.x, y = local.y, z = local.z, yaw = yaw, time = SimClock.Now });
        }

        /// <summary>面板叫回眼前：頭前 recallDistance、視線 recallMaxAngle 內。</summary>
        public void Recall()
        {
            if (head == null) return;
            for (int i = 0; i < entries.Count; i++)
            {
                var pose = PanelLogic.Recall(head.position, head.forward, i, layout.recallDistance, layout.recallMaxAngle);
                entries[i].panel.SetPositionAndRotation(pose.position, pose.rotation);
                Remember(entries[i]);
            }
        }

        void ApplyReplay(ISimState s)
        {
            foreach (var e in entries)
            {
                string v = s.Get(ReplayPrefix + e.id);
                if (string.IsNullOrEmpty(v) || (replayApplied.TryGetValue(e.id, out var old) && old == v)) continue;
                replayApplied[e.id] = v;
                var f = v.Split('|');
                if (f.Length < 2 || teleport == null || !(teleport.Find(f[0]) is XrTeleportPoint st)) continue;
                var n = f[1].Split(',');
                if (n.Length < 4) continue;
                float P(int i) => float.Parse(n[i], System.Globalization.CultureInfo.InvariantCulture);
                var pose = PanelLogic.FromLocal(st.transform, new Vector3(P(0), P(1), P(2)), P(3));
                e.panel.SetPositionAndRotation(pose.position, pose.rotation);
                FollowHandle(e);
            }
        }
    }
}
