using System.Globalization;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace PantographSim.XR
{
    /// <summary>
    /// VR 的玩家輸入：右手射線指物件（資訊卡、外框高亮）、依按鍵對照表把控制器動作轉成既有的 Core 事件——
    /// 扳機＝點擊（ActionRequested，與滑鼠左鍵相同路徑）、握把＝拿／還工具（ToolPickRequested／ToolReturnRequested）與放下滑板、
    /// A＝重設工具、B＝帶我到目標（FocusRequested）、X＝作業單、Y＝說明、≡＝暫停選單、搖桿＝轉向／傳送／工具微調。
    /// 不認識任何零件或工具類別；只寫黑板、發事件。另負責震動回饋與「失去追蹤就停止升降」。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class XrPlayer : MonoBehaviour
    {
        [SerializeField] XrRig rig;
        [SerializeField] XROrigin origin;
        [SerializeField] XRRayInteractor rightRay;
        [SerializeField] XRBaseController rightController;
        [SerializeField] XrTeleport teleport;
        [SerializeField] XrCoach coach;
        [SerializeField] float maxDistance = 30f, grabRadius = 0.15f, trayRadius = 0.3f, pouchRadius = 0.15f;
        Data.ToolPouchDef pouch;
        [Tooltip("D31：胸前扣環、工具環的握把半徑（RoofSceneDef.bodyRingRadius，建置時寫入）")] [SerializeField] float bodyRingRadius = 0.08f;

        /// <summary>R1.5b b4（D31）：建置時寫入胸前扣環、工具環的握把半徑。</summary>
        public void SetBodyRingRadius(float r) => bodyRingRadius = r;

        XRInputMapper mapper;
        XrControllerInput input;
        IInteractable hovered;
        string hoveredId = "";
        Vector3 hoverPoint;
        bool bound, triggerHeld, grabPickedThisPress;
        string helpContext = "";
        GameObject heldModel;

        /// <summary>截圖或測試腳本：有值時取代控制器輸入。</summary>
        public static XrFrame? ScriptedFrame;
        /// <summary>模擬模式（沒有頭盔的 Editor 截圖）：不讀追蹤狀態。</summary>
        public static bool Simulated;

        public string HoveredId => hoveredId;
        public XRInputMapper Mapper => mapper;

        public void Setup(XrRig r, XROrigin o, XRRayInteractor ray, XRBaseController right, XrTeleport tp, XrCoach c)
        {
            rig = r; origin = o; rightRay = ray; rightController = right; teleport = tp; coach = c;
        }

        void Awake()
        {
            mapper = new XRInputMapper(XrBindingTable.LoadDefault());
            input = new XrControllerInput(mapper.Table);
        }

        void OnDestroy()
        {
            input?.Dispose();
            if (bound && Sim.Bus != null)
            {
                Sim.Bus.Unsubscribe<ToolSelected>(OnToolSelected);
                Sim.Bus.Unsubscribe<StateChanged>(OnState);
                Sim.Bus.Unsubscribe<ActionRequested>(OnRequest);
                Sim.Bus.Unsubscribe<FocusRequested>(OnFocus);
                Sim.Bus.Unsubscribe<MeasurementTaken>(OnMeasured);
                Sim.Bus.Unsubscribe<VideoStarted>(OnVideoStarted);
                Sim.Bus.Unsubscribe<VideoStopped>(OnVideoStopped);
            }
            PointerState.XrTriggerHeld = false;
        }

        void Bind()
        {
            if (bound || !Sim.Ready) return;
            Sim.Bus.Subscribe<ToolSelected>(OnToolSelected);
            Sim.Bus.Subscribe<StateChanged>(OnState);
            Sim.Bus.Subscribe<ActionRequested>(OnRequest);
            Sim.Bus.Subscribe<FocusRequested>(OnFocus);
            Sim.Bus.Subscribe<MeasurementTaken>(OnMeasured);
            Sim.Bus.Subscribe<VideoStarted>(OnVideoStarted);
            Sim.Bus.Subscribe<VideoStopped>(OnVideoStopped);
            bound = true;
            // 右手射線指到介面時的圓點、變色與按下震動（只有右手）
            if (rightRay != null && rightRay.GetComponent<XrUiPointer>() == null) rightRay.gameObject.AddComponent<XrUiPointer>().Setup(rightRay, rightController);
        }

        ISimState S => Sim.State;

        void Update()
        {
            Bind();
            if (pouch == null) pouch = Data.ToolPouchDef.Load();
            if (!bound || !InputMode.XrActive || !S.GetBool(XrStartupSequencer.ReadyKey)) return;   // 開場擺好人和介面才開放輸入
            UpdatePointer();
            var frame = ScriptedFrame ?? input.Read();
            var ctx = Context();
            if (ctx.BindingContext != helpContext) { helpContext = ctx.BindingContext; S.Set("xr.help.keys", XRInputMapper.HelpText(mapper.Bindings, helpContext)); S.Set("xr.keys.card", XRInputMapper.KeyCard(mapper.Bindings, helpContext)); }   // Y 說明卡的按鍵表
            foreach (var cmd in mapper.Map(frame, ctx, Time.unscaledTime)) Execute(cmd);
            if (!Simulated && !XrControllerInput.RightHandTracked() && (S.Get(SimIds.LiftCmd) == "Up" || S.Get(SimIds.LiftCmd) == "Down"))
                LiftControl.Release(S);   // 失去追蹤：升降一律停
        }

        XrContext Context() => new XrContext
        {
            heldTool = S.Get(SimIds.ActiveTool),
            carrying = !string.IsNullOrEmpty(S.Get("hand.item")) || CarriedStrip() != "",
            dialogOpen = DialogOpen(),
            socketSize = S.Get(SimIds.ToolSocket + ".size", "10"),
            errorOpen = S.GetBool("ui.error.active"),
            tutorial = S.GetBool("xr.tutorial.active"),
        };

        bool DialogOpen() =>
            S.GetBool("ui.error.active") || !string.IsNullOrEmpty(S.Get("ui.tutorial.tool")) || S.GetBool("xr.menu_open") || S.GetBool("xr.help_open")
            || !string.IsNullOrEmpty(S.Get("ui.spare_inspect")) || S.GetBool("ui.panel_open") || !S.GetBool("training.running") || S.GetBool("score.finished");

        string CarriedStrip()
        {
            foreach (var side in new[] { "L", "R" }) if (S.Get(SimIds.Strip(side) + ".state") == "Carried") return SimIds.Strip(side);
            return "";
        }

        // ---------- 右手射線 ----------
        void UpdatePointer()
        {
            var o = rig != null ? rig.RightRayOrigin : transform;
            var ray = new Ray(o.position, o.forward);
            bool hit3d = Physics.Raycast(ray, out var hit, maxDistance, SceneLayers.InteractMask, QueryTriggerInteraction.Ignore);   // 不打不可見的邊界牆
            bool overUi = false;
            if (rightRay != null && rightRay.TryGetCurrentUIRaycastResult(out var ui) && ui.isValid)
                overUi = !hit3d || ui.distance <= hit.distance + 0.01f;
            PointerState.Ray = ray;
            PointerState.HasRay = !overUi;
            PointerState.OverUi = overUi;

            IInteractable next = null;
            string nextId = "";
            if (hit3d && !overUi)
            {
                var bridge = hit.collider.GetComponentInParent<XRInteractableBridge>();
                next = bridge != null ? bridge.Target : null;
                if (next == null) next = hit.collider.GetComponentInParent<IInteractable>();
                if (next != null && !SimIds.Clickable(S, next.Id)) next = null;
                nextId = next == null ? "" : bridge != null && !string.IsNullOrEmpty(bridge.SimId) ? bridge.SimId : next.Id;
                hoverPoint = hit.point;
            }
            if (nextId == hoveredId) return;
            if (hovered as Object) hovered.SetHovered(false);
            hovered = next; hoveredId = nextId;
            if (hovered != null) hovered.SetHovered(true);
            S.Set("ui.hover_name", hovered != null ? hovered.DisplayName : "");
            S.Set("ui.hover_id", hoveredId);
            Sim.Bus.Publish(new HoverChanged { targetId = hoveredId, text = "xr.right", time = SimClock.Now });
        }

        // ---------- 指令 ----------
        /// <summary>重播中只保留看的功能（傳送、轉向、說明、選單）；會改變場景的按鍵一律不作用。</summary>
        static bool BlockedInReplay(string command) =>
            SimSession.IsReplay && (command == "Use" || command == "Grab" || command == "ResetTool" || command == "ToolAdjust" || command == "ToolSecondary" || command == "ToggleWorkOrder" || command == "FocusTarget");

        void Execute(XrCommand c)
        {
            coach?.Notify(c);
            if (BlockedInReplay(c.command)) { PointerState.XrTriggerHeld = false; return; }
            switch (c.command)
            {
                case "Use":
                    triggerHeld = c.pressed && !PointerState.OverUi;
                    PointerState.XrTriggerHeld = triggerHeld;
                    if (c.pressed && !PointerState.OverUi) Click();
                    break;
                case "Grab":
                    if (c.pressed) GrabStart(); else GrabEnd();
                    break;
                case "PanelGrab":   // 左握把：抓面板把手由 XRI 處理；手在工具袋前就拿、放工具
                    leftHandActing = true;
                    if (c.pressed) { if (BodyItemNear()) GrabStart(); }
                    else if (grabPickedThisPress) grabPickedThisPress = false;
                    else if (!string.IsNullOrEmpty(S.Get(SimIds.ActiveTool)) && BodyItemNear()) GrabEnd();
                    leftHandActing = false;
                    break;
                case "ResetTool":
                    UiCommands.Issue(UiCommands.ResetTool, "xr.A");
                    Sim.Bus.Publish(new ActionRequested { targetId = "tool.belt", text = "reset", toolId = "", time = SimClock.Now });
                    Haptic(0.25f, 0.06f);
                    break;
                case "FocusTarget":
                    UiCommands.Issue(UiCommands.FocusTarget, "xr.B");
                    Sim.Bus.Publish(new FocusRequested { time = SimClock.Now });
                    break;
                case "ToggleWorkOrder":
                    if (S.GetBool("training.running")) { UiCommands.Issue(UiCommands.WorkOrderToggle, "xr.X"); Send(SimIds.WorkOrder, "toggle"); }
                    break;
                case "ToggleHelp":
                    UiCommands.Issue(UiCommands.Help, "xr.Y");
                    S.SetBool("xr.help_open", !S.GetBool("xr.help_open"));
                    break;
                case "Menu":
                    S.SetBool("xr.menu_open", !S.GetBool("xr.menu_open"));
                    break;
                case "Crouch":   // 左搖桿按下＝蹲低視角（只在車頂有效）
                    ToggleCrouch();
                    break;
                case "TurnLeft":
                case "TurnRight":
                    if (origin != null && !(teleport != null && teleport.Aiming))
                    {
                        origin.RotateAroundCameraUsingOriginUp(c.degrees != 0 ? c.degrees : (c.command == "TurnRight" ? 45f : -45f));
                        S.SetFloat("xr.recenter", Time.time);
                    }
                    break;
                case "TeleportAim":
                    if (teleport == null) break;
                    if (c.pressed) teleport.BeginAim(); else teleport.EndAim();
                    break;
                case "ToolSecondary":   // 第 6 點：右搖桿按下＝手上工具的第二功能
                    Send(S.Get(SimIds.ActiveTool), SimIds.ToolSecondaryCommand);
                    break;
                case "ToolAdjust":
                    if (c.scroll != 0) PointerState.InjectedScroll += c.scroll;
                    else if (!string.IsNullOrEmpty(c.target)) Send(c.target, c.text);
                    break;
            }
        }

        static void Send(string target, string text, string pointId = "") =>
            Sim.Bus.Publish(new ActionRequested { targetId = target, text = text, pointId = pointId, toolId = "", time = SimClock.Now });

        /// <summary>扳機＝點擊：和滑鼠左鍵相同（徒手用物件的預設動作；拿著工具＝click，由工具判斷）。</summary>
        void Click()
        {
            if (S.GetBool("training.paused") || !S.GetBool("training.running")) return;
            string tool = S.Get(SimIds.ActiveTool);
            // T2-a：拿著卡尺、沒指著東西按右扳機＝ZERO（和左手 poke Poke_Zero 送同一個事件）
            if (hovered == null && tool == SimIds.ToolCaliper) { Sim.Bus.Publish(XrToolWidgets.ZeroRequest()); return; }
            if (hovered == null) return;
            string clicked = SimIds.RayTarget(S, tool, hoveredId, hoverPoint);
            Sim.Bus.Publish(new ActionRequested
            {
                targetId = clicked, toolId = tool,
                text = !string.IsNullOrEmpty(tool) ? "click" : clicked == hoveredId ? hovered.DefaultVerb : "inspect",
                pointId = $"{hoverPoint.x.ToString("0.###", CultureInfo.InvariantCulture)},{hoverPoint.y.ToString("0.###", CultureInfo.InvariantCulture)},{hoverPoint.z.ToString("0.###", CultureInfo.InvariantCulture)}",
                time = SimClock.Now
            });
            S.Set("ui.last_clicked", clicked);
        }

        // ---------- 握把：拿／還工具、放下滑板 ----------
        static int SlotOf(string id) => id != null && id.StartsWith("env.tool_rack.slot_") && int.TryParse(id.Substring("env.tool_rack.slot_".Length), out int k) ? k : 0;

        int SlotOfTool(string toolId)
        {
            foreach (var entry in S.Get("tools.list").Split(';'))
            {
                var f = entry.Split('|');
                if (f.Length >= 2 && f[1] == toolId && int.TryParse(f[0], out int k)) return k;
            }
            return 0;
        }

        bool leftHandActing;   // 第 4 點：左握把也能拿、放工具袋的工具（工具模型仍在右手）
        Vector3 HandPos => rig == null ? transform.position : leftHandActing && rig.LeftHand != null ? rig.LeftHand.position : rig.RightHand != null ? rig.RightHand.position : transform.position;
        float SlotRadius => pouch != null ? pouch.slotRadius : grabRadius;

        bool Near(string id, float radius) => Sim.Registry != null && Sim.Registry.TryGetPosition(id, out var p) && Vector3.Distance(p, HandPos) <= radius;

        bool BodyItemNear() => SlotNearHand() > 0 || Near("env.waist_pouch", SlotRadius) || Near(BodyRings.ChestRing, bodyRingRadius) || Near(BodyRings.ToolRing, bodyRingRadius);

        int SlotNearHand()
        {
            return XrPouchHover.Pick(S, pouch != null ? pouch : Data.ToolPouchDef.Load(), new[] { HandPos }).slot;
        }

        void GrabStart()
        {
            grabPickedThisPress = false;
            if (!S.GetBool("training.running") || S.GetBool("training.paused")) return;
            string active = S.Get(SimIds.ActiveTool);
            // D31：手空著伸到胸前扣環按握把＝掛鉤收起／取下；伸到工具環＝拿下短接地線（和桌面版按鈕同一個請求）
            bool empty = string.IsNullOrEmpty(active) && string.IsNullOrEmpty(S.Get("hand.item"));
            if (empty && Near(BodyRings.ChestRing, bodyRingRadius)) { var (t, x) = BodyRings.ChestRingRequest(S); Send(t, x); grabPickedThisPress = true; return; }
            if (empty && S.GetBool(BodyRings.LeadCarried) && Near(BodyRings.ToolRing, bodyRingRadius)) { var (t, x) = BodyRings.ToolRingRequest(S); Send(t, x); grabPickedThisPress = true; return; }
            // D30：手伸到腰包按握把＝從腰包拿出上鎖掛牌組（手要空著）
            if (string.IsNullOrEmpty(active) && string.IsNullOrEmpty(S.Get("hand.item")) && Near("env.waist_pouch", SlotRadius)) { Send("env.waist_pouch", "click"); grabPickedThisPress = true; return; }
            int slot = SlotOf(hoveredId);
            if (slot == 0) slot = SlotNearHand();
            if (slot > 0 && string.IsNullOrEmpty(active) && string.IsNullOrEmpty(S.Get("hand.item")))
            {
                Sim.Bus.Publish(new ToolPickRequested { targetId = "env.tool_rack.slot_" + slot, value = slot, time = SimClock.Now });
                grabPickedThisPress = true;
                return;
            }
            if (string.IsNullOrEmpty(active) && hoveredId.StartsWith("env.spare_shelf.slot_")) { Send(hoveredId, "pick"); return; }
            if (string.IsNullOrEmpty(active) && hoveredId.StartsWith("part.carbon_strip_")) Click();
        }

        void GrabEnd()
        {
            if (grabPickedThisPress) { grabPickedThisPress = false; return; }   // 剛拿起的這一下不算放下
            if (!S.GetBool("training.running")) return;
            string active = S.Get(SimIds.ActiveTool);
            if (!string.IsNullOrEmpty(active))
            {
                int k = SlotOfTool(active);
                if (k > 0 && (SlotOf(hoveredId) == k || Near("env.tool_rack.slot_" + k, SlotRadius)))
                    Sim.Bus.Publish(new ToolReturnRequested { targetId = "env.tool_rack.slot_" + k, value = k, time = SimClock.Now });
                else if (active == BodyRings.ToolGroundingLead && Near(BodyRings.ToolRing, bodyRingRadius)) { var (t, x) = BodyRings.ToolRingRequest(S); Send(t, x); }   // D31：短接地線掛回安全帶工具環
                else if (Near("env.waist_pouch", SlotRadius)) Send("env.waist_pouch", "click");   // D30：上鎖掛牌組收進腰包（不是腰包的工具由 ToolBelt 提示）
                else Send("tool.belt", "release:" + (S.Get(XrViewHeight.FloorKey) == "Roof" ? "roof" : "ground"));   // 防墜繫繩：車頂垂掛並記 Major、地面放下（crane-procedure 6.3）
                return;
            }
            if (!string.IsNullOrEmpty(S.Get("hand.item")) && (hoveredId == SimIds.PartsTray || Near(SimIds.PartsTray, trayRadius)))
            {
                Send(SimIds.PartsTray, "click");   // 舊滑板收進零件盒
                return;
            }
            var carried = CarriedStrip();
            if (carried != "" && hoveredId.EndsWith(".bracket")) Click();   // 新滑板放到托架
        }

        static void Toast(string text) { Sim.State.Set("ui.toast", ""); Sim.State.Set("ui.toast", text); Sim.State.SetFloat("ui.toast_time", Time.time); }

        // ---------- 事件 ----------
        // 錄影開始／停止：輕震一下（提示音由紀錄模組播）
        void OnVideoStarted(VideoStarted e) => Haptic(0.35f, 0.07f);
        void OnVideoStopped(VideoStopped e) => Haptic(0.35f, 0.07f);

        void OnToolSelected(ToolSelected e)
        {
            ShowHeldModel(e.toolId);
            if (!string.IsNullOrEmpty(e.toolId)) Haptic(pouch != null ? pouch.grabAmplitude : 0.5f, pouch != null ? pouch.grabSeconds : 0.08f);   // 抓取工具
        }

        void OnState(StateChanged e)
        {
            foreach (var c in e.Changes)
            {
                if (c.key.StartsWith("part.carbon_strip_") && c.key.EndsWith(".state") && c.newValue == "NewMounted") Haptic(0.8f, 0.18f);   // 吸附成功
                else if (c.key == "tool.torque_wrench.reached" && c.newValue == "true") Haptic(1f, 0.04f);   // 扭力喀聲
                else if (c.key == SimIds.LiftCmd && c.newValue == "Up" && LiftControl.UpLocked(S)) Haptic(0.3f, 0.06f);   // ▲ 被安全聯鎖擋住：輕震（原因顯示在升降面板）
            }
        }

        void OnRequest(ActionRequested e)
        {
            if (e.targetId != "xr.menu") return;
            if (MenuSetting(e.text)) return;   // 設定類按鈕不關選單
            S.SetBool("xr.menu_open", false);
            switch (e.text)
            {
                case "redo": UiCommands.Issue(UiCommands.RestartStep, "xr.menu"); Sim.Bus.Publish(new RestartStepRequested { time = SimClock.Now }); break;
                case "mode": Send("app.restart", ""); break;
                case "recenter":
                    var seq = FindObjectOfType<XrStartupSequencer>();
                    if (seq != null) seq.Reseat(); else teleport?.Recenter();
                    break;
                case "recall": break;
                case "quit":
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.ExitPlaymode();
#else
                    Application.Quit();
#endif
                    break;
            }
        }

        /// <summary>≡ 選單的設定（T1 高度與蹲低視角、T6 平滑移動、速度、遮罩）。回傳是否處理了。</summary>
        bool MenuSetting(string text)
        {
            var vh = GetComponent<XrViewHeight>();
            switch (text)
            {
                case "toolbag_mount": S.Set(XrCarryContainer.MountKey, S.Get(XrCarryContainer.MountKey) == "wrist" ? "waist" : "wrist"); return true;   // R1.5b b3
                case "carry_swap": S.SetBool(XrCarryContainer.SwapKey, !S.GetBool(XrCarryContainer.SwapKey)); return true;
                case "height":
                    var mode = S.Get("xr.height.mode", "Raised") == "Real" ? HeightMode.Raised : HeightMode.Real;
                    S.Set("xr.height.mode", mode.ToString());
                    vh?.SetMode(mode);
                    return true;
                case "crouch": ToggleCrouch(); return true;
                case "recall": S.SetFloat(XrPanels.RecallKey, Time.time); return false;   // 面板叫回眼前（之後照常關選單）
            }
            return false;
        }

        /// <summary>蹲低視角（左搖桿按下或 ≡ 選單）：提示 1.5 s「蹲姿輔助：開／關」並讓左手短震一下。</summary>
        public void ToggleCrouch()
        {
            var vh = GetComponent<XrViewHeight>();
            if (vh == null) return;
            Toast(vh.ToggleCrouch());
            S.SetBool("xr.crouch", vh.Crouch);
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (dev.isValid) dev.SendHapticImpulse(0, 0.35f, 0.06f);
        }

        /// <summary>帶我到目標：目前步驟／小動作的目標（或指定 ID）→ 最近的傳送點並面向目標。</summary>
        void OnFocus(FocusRequested e)
        {
            if (teleport == null || Sim.Registry == null) return;
            // R1 T5：有站點時 B 鍵只傳到目前小動作的站點（空＝原地，不傳送）；沒有站點元件才用舊的「最近傳送點」
            if (string.IsNullOrEmpty(e.targetId) && GetComponent<XrStations>() is XrStations st) { st.Focus(); return; }
            string ids = !string.IsNullOrEmpty(e.targetId) ? e.targetId : S.Get("hint.sub_target");
            if (string.IsNullOrEmpty(ids)) ids = S.Get("hint.highlight");
            var reg = Sim.Registry;
            var pts = ids.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)
                .SelectMany(id => id.EndsWith("*") ? reg.Ids.Where(r => r.StartsWith(id.TrimEnd('*'))).ToList() : new[] { id }.ToList())
                .Select(id => reg.TryGetPosition(id, out var p) ? (Vector3?)p : null).Where(p => p.HasValue).Select(p => p.Value).ToList();
            if (pts.Count == 0) { Toast("目前沒有要去的目標"); return; }
            var center = pts.Aggregate(Vector3.zero, (a, b) => a + b) / pts.Count;
            teleport.FocusOn(center);
        }

        /// <summary>卡尺量完：手腕面板出現「填入作業單」，一按就寫進對應欄位。</summary>
        void OnMeasured(MeasurementTaken e)
        {
            const string prefix = "ip.carbon_strip_";
            if (e.targetId == null || !e.targetId.StartsWith(prefix) || !e.targetId.Contains(".measure_")) return;
            string side = SimIds.SideOf(e.targetId);
            string point = e.targetId.Substring(e.targetId.IndexOf(".measure_") + ".measure_".Length);
            bool newStrip = S.Get("procedure.current").StartsWith("STP_CSR_15");
            string field = newStrip ? "newmid." + side : $"reading.{side}.{point}";
            string label = (side == "L" ? "靠關節" : "外側") + (newStrip ? "新滑板中點" : point == "left" ? "左" : point == "mid" ? "中" : "右");
            S.Set("xr.fill.value", e.value.ToString("0.00", CultureInfo.InvariantCulture));
            S.Set("xr.fill.label", label);
            S.Set("xr.fill.field", field);
        }

        // ---------- 手上的工具模型 ----------
        void ShowHeldModel(string toolId)
        {
            if (heldModel != null) Destroy(heldModel);
            heldModel = null;
            if (string.IsNullOrEmpty(toolId) || rig == null || Sim.Registry == null) return;
            int k = SlotOfTool(toolId);
            if (!Sim.Registry.TryGetTransform("env.tool_rack.slot_" + k, out var slot)) return;
            var model = slot.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("Model_"));
            if (model == null) return;
            heldModel = Instantiate(model.gameObject, rig.HeldToolAnchor);
            heldModel.name = "Held_" + toolId;
            heldModel.SetActive(true);
            heldModel.transform.localPosition = Vector3.zero;
            heldModel.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            var parentScale = rig.HeldToolAnchor.lossyScale;
            var s = model.lossyScale;
            heldModel.transform.localScale = new Vector3(s.x / parentScale.x, s.y / parentScale.y, s.z / parentScale.z);
            foreach (var c in heldModel.GetComponentsInChildren<Collider>()) Destroy(c);
        }

        // ---------- 震動 ----------
        /// <summary>左手或右手震動。</summary>
        public void Haptic(bool left, float amplitude, float duration)
        {
            if (!left) { Haptic(amplitude, duration); return; }
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (dev.isValid) dev.SendHapticImpulse(0, amplitude, duration);
        }

        public void Haptic(float amplitude, float duration)
        {
            if (rightController != null && rightController.SendHapticImpulse(amplitude, duration)) return;
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (dev.isValid) dev.SendHapticImpulse(0, amplitude, duration);
        }
    }
}
