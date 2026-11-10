using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace PantographSim.XR
{
    /// <summary>
    /// T2-a 工具數值用手調（右搖桿讓給平滑移動）。只發既有事件、只寫既有黑板鍵：
    /// ・套筒尺寸：扳手頭（Socket_Mount）碰到套筒座 env.socket_seat_10／13／17 停留 0.15 s → tool.socket_wrench「size:n」。
    /// ・鬆／鎖：左手 Poke_Tip 碰 Poke_Ratchet，一次切換一次 →「dir:lock|loosen」。
    /// ・扭力：左手碰 Poke_Plus／Poke_Minus，一下 ±0.5；按住 0.4 s 後每秒 4 格 →「set:+|-」（範圍由 TorqueWrenchTool 夾在 5–50）。
    /// ・卡尺：量爪尖 3 cm 內有量測點就自動吸附量測（等同點擊量測點）；量到就由 XrPlayer 寫 xr.fill.auto（介面把讀數填進那個量測點的格子）；離開就合攏。
    ///   ZERO：左手碰 Poke_Zero →「zero」（右扳機在沒有指著東西時送同一個指令，見 XrPlayer）；左手碰量爪 FX_Jaw＝合攏。
    /// ・新滑板翻面：抓著滑板轉手腕超過 90° →「carried rotate180」。
    /// 只有右手正拿著的那件工具的節點會啟用；沒拿工具全停用。poke 到時左手輕震。
    /// shortcut: 節點掛在右手 Attach_Tool 上的小方塊（不是工具 prefab 裡），用距離判定碰觸，不經 XRI 的 poke 互動；R2 的 ContactRouter 再換成真正的接觸。
    /// 手上是建模師 B 的新模型（Held_*，有同名節點 Socket_Mount、Poke_*、FX_Jaw、Tip）時，小方塊跟著模型的節點走，卡尺的量爪 FX_Jaw 照讀數滑動；
    /// 判定方式和數值不變，只換位置。架上的舊模型沒有這些節點，照舊用下面寫死的位置。
    /// </summary>
    public class XrToolWidgets : MonoBehaviour
    {
        [SerializeField] Transform toolAnchor;      // 右手 Attach_Tool
        [SerializeField] Transform pokeTip;         // 左手 Poke_Tip
        [SerializeField] XRBaseController leftController;
        [SerializeField] float pokeRadius = 0.008f;

        public const string AutoFillKey = "xr.fill.auto";
        static readonly string[] Sizes = { "10", "13", "17" };

        class Widget { public string tool, name; public Transform t; public bool inside; }
        readonly List<Widget> widgets = new List<Widget>();
        Transform socketMount, jawTip, jaw;
        readonly HoldRepeater plus = new HoldRepeater(), minus = new HoldRepeater();
        readonly DwellTimer seatDwell = new DwellTimer(ToolWidgetLogic.SocketDwellSeconds);
        string snapped = "";
        bool carrying, flipped;
        float grabRoll;

        public void Setup(Transform anchor, Transform tip, XRBaseController left) { toolAnchor = anchor; pokeTip = tip; leftController = left; }

        void Awake() => BuildWidgets();

        /// <summary>在 Attach_Tool 下建立節點（只建一次；沒拿工具時全部停用）。</summary>
        public void BuildWidgets()
        {
            if (toolAnchor == null || widgets.Count > 0) return;
            Widget Box(string tool, string name, Vector3 pos, Vector3 size, Color c)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = name;
                var col = g.GetComponent<Collider>(); if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
                g.transform.SetParent(toolAnchor, false);
                g.transform.localPosition = pos; g.transform.localScale = size;
                var mpb = new MaterialPropertyBlock(); mpb.SetColor("_Color", c);   // 不用 renderer.material（Edit 模式會漏材質）
                g.GetComponent<Renderer>().SetPropertyBlock(mpb);
                var w = new Widget { tool = tool, name = name, t = g.transform };
                widgets.Add(w);
                return w;
            }
            var orange = new Color(1f, 0.55f, 0.1f);
            Box(SimIds.ToolSocket, "Poke_Ratchet", new Vector3(0f, 0.025f, 0.06f), new Vector3(0.02f, 0.01f, 0.01f), orange);
            Box(SimIds.ToolTorque, "Poke_Plus", new Vector3(0.02f, 0.025f, 0.05f), new Vector3(0.015f, 0.01f, 0.015f), new Color(0.3f, 0.9f, 0.4f));
            Box(SimIds.ToolTorque, "Poke_Minus", new Vector3(-0.02f, 0.025f, 0.05f), new Vector3(0.015f, 0.01f, 0.015f), new Color(0.9f, 0.3f, 0.3f));
            Box(SimIds.ToolCaliper, "Poke_Zero", new Vector3(0f, 0.02f, 0.03f), new Vector3(0.01f, 0.005f, 0.01f), orange);
            socketMount = Box(SimIds.ToolSocket, "Socket_Mount", new Vector3(0f, 0f, 0.14f), Vector3.one * 0.012f, Color.gray).t;
            jaw = Box(SimIds.ToolCaliper, "FX_Jaw", new Vector3(0f, 0f, 0.10f), new Vector3(0.004f, 0.03f, 0.004f), Color.gray).t;
            jawTip = jaw;
            SetActiveTool("");
        }

        void SetActiveTool(string tool)
        {
            foreach (var w in widgets) if (w.t.gameObject.activeSelf != (w.tool == tool)) w.t.gameObject.SetActive(w.tool == tool);
        }

        bool Touching(Widget w)
        {
            if (pokeTip == null || !w.t.gameObject.activeInHierarchy) return false;
            var local = w.t.InverseTransformPoint(pokeTip.position);
            var s = w.t.lossyScale;
            var r = new Vector3(pokeRadius / Mathf.Max(s.x, 1e-4f), pokeRadius / Mathf.Max(s.y, 1e-4f), pokeRadius / Mathf.Max(s.z, 1e-4f));
            return Mathf.Abs(local.x) <= 0.5f + r.x && Mathf.Abs(local.y) <= 0.5f + r.y && Mathf.Abs(local.z) <= 0.5f + r.z;
        }

        /// <summary>ZERO：左手 poke Poke_Zero 與右扳機（XrPlayer）送同一個事件。</summary>
        public static ActionRequested ZeroRequest() => new ActionRequested { targetId = SimIds.ToolCaliper, text = "zero", toolId = "", pointId = "", time = SimClock.Now };

        static void Send(string target, string text, string toolId = "") =>
            Sim.Bus.Publish(new ActionRequested { targetId = target, text = text, toolId = toolId, pointId = "", time = SimClock.Now });

        void Buzz()
        {
            if (leftController != null && leftController.SendHapticImpulse(0.3f, 0.03f)) return;
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (dev.isValid) dev.SendHapticImpulse(0, 0.3f, 0.03f);
        }

        void Update()
        {
            var s = Sim.State;
            if (s == null || !InputMode.XrActive || toolAnchor == null || SimSession.IsReplay) { SetActiveTool(""); return; }
            string tool = s.GetBool("training.running") ? s.Get(SimIds.ActiveTool) : "";
            SetActiveTool(tool);
            FindHeld(tool);
            FollowModel(tool);
            float dt = Time.unscaledDeltaTime;
            foreach (var w in widgets.Where(x => x.name.StartsWith("Poke_") || x.name == "FX_Jaw"))
            {
                bool now = Touching(w);
                bool enter = now && !w.inside;
                w.inside = now;
                if (enter) Buzz();
                if (w.name == "Poke_Ratchet" && enter) Send(SimIds.ToolSocket, "dir:" + ToolWidgetLogic.ToggleDir(s.Get(SimIds.ToolSocket + ".dir", "loosen")));
                else if (w.name == "Poke_Zero" && enter) Sim.Bus.Publish(ZeroRequest());
                else if (w.name == "FX_Jaw" && enter && snapped.Length == 0) Send(SimIds.ToolCaliper, "close");   // 左手捏量爪＝合攏
                else if (w.name == "Poke_Plus") for (int i = plus.Update(now, dt); i > 0; i--) Send(SimIds.ToolTorque, "set:+");
                else if (w.name == "Poke_Minus") for (int i = minus.Update(now, dt); i > 0; i--) Send(SimIds.ToolTorque, "set:-");
            }
            UpdateSocketSeat(s, tool, dt);
            UpdateCaliper(s, tool, dt);
            if (modelJaw != null) FollowModel(tool);   // 量爪剛滑動過，FX_Jaw 和 Poke_Zero 跟上
            UpdateFlip(s);
        }

        void UpdateSocketSeat(ISimState s, string tool, float dt)
        {
            string touching = "";
            if (tool == SimIds.ToolSocket && Sim.Registry != null && socketMount != null)
                touching = ToolWidgetLogic.Nearest(socketMount.position, Sizes.Select(n => new KeyValuePair<string, Vector3>(n,
                    Sim.Registry.TryGetPosition("env.socket_seat_" + n, out var p) ? p : Vector3.one * 1e4f)), ToolWidgetLogic.SocketTouchRadius);
            var size = seatDwell.Update(touching, dt);
            if (size != null && size != s.Get(SimIds.ToolSocket + ".size", "10")) { Send(SimIds.ToolSocket, "size:" + size); Buzz(); }
        }

        // ---- 手上模型的節點 ----
        Transform held, modelJaw, modelTip;
        string heldFor = "";
        Vector3 jawBase;
        readonly Dictionary<string, Transform> heldNodes = new Dictionary<string, Transform>();

        /// <summary>找手上的模型（XrPlayer 在 Attach_Tool 下建立 Held_工具）；拿起那一幀可能還沒建好，找到為止。</summary>
        void FindHeld(string tool)
        {
            if (tool == heldFor && (held != null || string.IsNullOrEmpty(tool))) return;
            if (tool != heldFor) { heldFor = tool; held = null; }
            heldNodes.Clear(); modelJaw = modelTip = null;
            if (string.IsNullOrEmpty(tool)) return;
            held = toolAnchor.Find("Held_" + tool);
            if (held == null) return;
            foreach (var t in held.GetComponentsInChildren<Transform>(true))
                if (!heldNodes.ContainsKey(t.name)) heldNodes[t.name] = t;
            heldNodes.TryGetValue("FX_Jaw", out modelJaw);
            heldNodes.TryGetValue("Tip", out modelTip);
            if (modelJaw != null) jawBase = modelJaw.localPosition;
        }

        /// <summary>小方塊放到手上模型的同名節點上（沒有同名節點就留在原位）。</summary>
        void FollowModel(string tool)
        {
            if (held == null) return;
            foreach (var w in widgets)
                if (w.tool == tool && heldNodes.TryGetValue(w.name, out var n) && n != null) w.t.SetPositionAndRotation(n.position, n.rotation);
        }

        void UpdateCaliper(ISimState s, string tool, float dt)
        {
            float mm = Mathf.Clamp(s.GetFloat(SimIds.ToolCaliper + ".jaw"), 0f, 150f);
            if (modelJaw != null && tool == SimIds.ToolCaliper) modelJaw.localPosition = jawBase + Vector3.right * (mm / 1000f);   // 新模型：量爪沿尺身 +X 滑動
            else if (jaw != null && tool == SimIds.ToolCaliper)   // 量爪沿 +X 開合 0–150 mm
                jaw.localPosition = new Vector3(mm / 1000f, 0f, 0.10f);
            string near = "";
            var tipT = modelTip != null && tool == SimIds.ToolCaliper ? modelTip : jawTip;   // 新模型：量爪尖
            if (tool == SimIds.ToolCaliper && Sim.Registry != null && tipT != null)
                near = ToolWidgetLogic.Nearest(tipT.position, Sim.Registry.Ids.Where(id => id.Contains(".measure_") && s.Has(id + ".true"))
                    .Select(id => new KeyValuePair<string, Vector3>(id, Sim.Registry.TryGetPosition(id, out var p) ? p : Vector3.one * 1e4f)), ToolWidgetLogic.CaliperSnapRadius);
            if (near != snapped)
            {
                if (near.Length > 0 && Sim.Ask(SimIds.ToolCaliper, near, "measure")) Send(near, "click", SimIds.ToolCaliper);   // 吸附量測（CaliperTool.OnClick）
                else if (snapped.Length > 0 && tool == SimIds.ToolCaliper) Send(SimIds.ToolCaliper, "close");   // 離開就合攏
                snapped = near;
            }
        }

        void UpdateFlip(ISimState s)
        {
            bool now = false;
            foreach (var side in new[] { "L", "R" }) if (s.Get(SimIds.Strip(side) + ".state") == "Carried") now = true;
            float roll = toolAnchor.parent != null ? toolAnchor.parent.localEulerAngles.z : 0f;
            if (now && !carrying) { grabRoll = roll; flipped = false; }
            carrying = now;
            if (!carrying) return;
            float d = Mathf.Abs(Mathf.DeltaAngle(grabRoll, roll));
            bool f = flipped ? d > 45f : d > 90f;   // 轉過 90° 翻面；轉回 45° 內才算回來
            if (f != flipped) { flipped = f; Send("carried", "rotate180"); }
        }
    }
}
