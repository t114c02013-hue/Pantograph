using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.UI;
using PantographSim.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PantographSim.Editor.XR
{
    /// <summary>
    /// 沒有頭盔也能檢查 VR 畫面：進入 Play、用模擬模式啟用 XR Origin（頭與手由這裡擺放），
    /// 從 XR Origin 的攝影機輸出 1920×1080 截圖：
    /// Logs/rig_xr_ui.png（提示板、手腕面板與手勢小卡，含「鬆、歸、鎖」）、rig_xr_lift.png（升降面板＋弓頭＋綠框）、rig_xr_hover.png（備品資訊卡）。
    /// 觸發：Logs/xr-capture.request 或選單 PantographSim/XR/Capture VR Screenshots。記錄寫到 Logs/xr-capture.txt。
    /// </summary>
    [InitializeOnLoad]
    public static class XrCaptureDriver
    {
        const string RequestPath = "Logs/xr-capture.request";
        const string SessionKey = "PantographSim.XrCapturePending";
        static IEnumerator routine;
        static double nextPoll;
        static readonly StringBuilder log = new StringBuilder();

        static XrCaptureDriver()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(SessionKey, false)) XrModeSwitch.SimulateRequested = true;
                if (s == PlayModeStateChange.ExitingPlayMode) { XrModeSwitch.SimulateRequested = false; XrPlayer.ScriptedFrame = null; }
            };
        }

        [MenuItem("PantographSim/XR/Capture VR Screenshots")]
        public static void Request()
        {
            if (EditorApplication.isPlaying) return;
            if (EditorSceneManager.GetActiveScene().path != SliceSceneBuilder.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(SliceSceneBuilder.ScenePath);
            }
            SessionState.SetBool(SessionKey, true);
            XrModeSwitch.SimulateRequested = true;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.timeSinceStartup > nextPoll)
            {
                nextPoll = EditorApplication.timeSinceStartup + 2.0;
                if (File.Exists(RequestPath) && !EditorApplication.isCompiling)
                {
                    File.Delete(RequestPath);
                    var active = EditorSceneManager.GetActiveScene();
                    if (active.isDirty && !string.IsNullOrEmpty(active.path)) EditorSceneManager.SaveScene(active);
                    if (active.path != SliceSceneBuilder.ScenePath) EditorSceneManager.OpenScene(SliceSceneBuilder.ScenePath);
                    SessionState.SetBool(SessionKey, true);
                    XrModeSwitch.SimulateRequested = true;
                    EditorApplication.EnterPlaymode();
                }
                return;
            }
            if (!EditorApplication.isPlaying) return;
            if (routine == null && SessionState.GetBool(SessionKey, false))
            {
                SessionState.SetBool(SessionKey, false);
                XrModeSwitch.SimulateRequested = true;
                log.Clear();
                routine = Run();
            }
            if (routine == null) return;
            try { if (!routine.MoveNext()) Finish(); }
            catch (Exception e) { log.AppendLine("例外：" + e); Finish(); }
        }

        static void Finish()
        {
            routine = null;
            XrModeSwitch.SimulateRequested = false;
            XrPlayer.ScriptedFrame = null;
            if (Sim.State != null) LiftControl.Release(Sim.State);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/xr-capture.txt", log.ToString());
            Debug.Log("[XrCaptureDriver] 完成\n" + log);
            EditorApplication.ExitPlaymode();
        }

        static ISimState S => Sim.State;
        static void Send(string target, string text, string pointId = "") =>
            Sim.Bus.Publish(new ActionRequested { targetId = target, text = text, pointId = pointId, toolId = "", time = SimClock.Now });

        static IEnumerable Wait(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        static XrRig Rig => ServiceLocator.Get<IXrRig>() as XrRig;

        /// <summary>頭在 XR Origin 上方 1.6 m，看向世界座標 look。</summary>
        static void Look(Vector3 look)
        {
            var head = Rig.Head;
            head.parent.localPosition = Vector3.zero;   // Camera Offset（Floor 模式＝0）
            head.localPosition = new Vector3(0, 1.6f, 0);
            head.rotation = Quaternion.LookRotation(look - head.position, Vector3.up);
        }

        /// <summary>手放在頭的相對位置（右、上、前，公尺），右手指向 aim。</summary>
        static void Hands(Vector3 leftLocal, Vector3? rightLocal = null, Vector3? aim = null)
        {
            var head = Rig.Head;
            var yaw = Quaternion.Euler(0, head.eulerAngles.y, 0);
            Rig.LeftHand.position = head.position + yaw * leftLocal;
            Rig.LeftHand.rotation = Quaternion.LookRotation(yaw * new Vector3(0.15f, -0.55f, 1f), yaw * Vector3.up);
            if (rightLocal.HasValue)
            {
                Rig.RightHand.position = head.position + yaw * rightLocal.Value;
                Rig.RightHand.rotation = aim.HasValue ? Quaternion.LookRotation(aim.Value - Rig.RightHand.position, Vector3.up) : yaw;
            }
        }

        static IEnumerator Run()
        {
            while (!Sim.Ready || !InputMode.XrActive || Rig == null) yield return null;
            var tp = UnityEngine.Object.FindObjectOfType<XrTeleport>();
            log.AppendLine("XR 狀態：" + XrModeSwitch.Status);
            Rig.Head.localPosition = new Vector3(0, 1.6f, 0);
            tp.PlaceAtStart();
            foreach (var _ in Wait(0.5f)) yield return null;
            log.AppendLine("Canvas：" + string.Join("、", UnityEngine.Object.FindObjectsOfType<Canvas>(true).Where(c => c.isRootCanvas).Select(c => $"{c.name}({c.renderMode})")));
            log.AppendLine("控制器教學：" + (S.GetBool("xr.tutorial.active") ? "顯示中" : "未顯示"));
            Send("xr.tutorial", "skip");
            Send("app.start", "tutorial:SCN_Default");
            foreach (var _ in Wait(1.5f)) yield return null;

            // ---- 升降面板＋弓頭＋綠框：一邊按住▼一邊拍 ----
            Sim.Registry.TryGetPosition("ip.pan_head.lower_target", out var green);
            Sim.Registry.TryGetTransform(XrPanelLayout.LiftMountId, out var mount);
            float t0 = Time.realtimeSinceStartup;
            while (S.GetFloat(SimIds.LiftValue, 1f) > 0.3f && Time.realtimeSinceStartup - t0 < 30f) { LiftControl.Send(S, "Down"); yield return null; }
            LiftControl.Release(S);
            Look(Vector3.Lerp(green, mount.position, 0.35f) + Vector3.down * 0.1f);
            Hands(new Vector3(-0.35f, -0.7f, 0.15f), new Vector3(0.3f, -0.55f, 0.25f), mount.position + Vector3.down * 0.15f);
            foreach (var _ in Wait(0.6f)) yield return null;
            Shot("rig_xr_lift");
            log.AppendLine($"升降：lift={S.GetFloat(SimIds.LiftValue):0.00}　距綠框 {S.GetFloat(SimIds.PanHeadLowerDistance):0.0} cm　步驟 {S.Get("procedure.current")}");

            // ---- 介面：拿套筒扳手（手腕面板＋手勢小卡），等提示板移到左前方 ----
            Sim.Bus.Publish(new ToolPickRequested { targetId = "env.tool_rack.slot_6", value = 6, time = SimClock.Now });
            foreach (var _ in Wait(0.3f)) yield return null;
            Send("ui.tutorial", "close");
            Send(SimIds.ToolSocket, "size:13");
            foreach (var _ in Wait(5.5f)) yield return null;
            Look(Rig.Head.position + Quaternion.Euler(0, -15f, 0) * Flat(tp.Find("tp.platform.lift").transform.forward) * 2f + Vector3.down * 1.1f);
            Hands(new Vector3(-0.05f, -0.42f, 0.36f), new Vector3(0.3f, -0.6f, 0.2f));
            foreach (var _ in Wait(0.5f)) yield return null;
            Shot("rig_xr_ui");
            log.AppendLine("手上：" + S.Get(SimIds.ActiveTool) + "　手勢小卡：" + S.Get("xr.gesture.tool"));

            // ---- 作業單：X 鍵打開，在左手夾板上（不是正前方中央）；選中一格＝高亮 ----
            Send(SimIds.WorkOrder, "toggle");
            S.Set(WorkOrderView.SelectedKey, "reading.L.mid");
            Look(Rig.Head.position + Flat(Rig.Head.forward) * 2f + Vector3.down * 1.2f);   // 先看前下方，手放在胸前
            Hands(new Vector3(0.12f, -0.36f, 0.45f), new Vector3(0.32f, -0.6f, 0.2f));
            foreach (var _ in Wait(0.4f)) yield return null;
            var board = GameObject.Find("XR_Clipboard");
            if (board != null) Look(Vector3.Lerp(board.transform.position, Rig.LeftHand.position, 0.35f));   // 轉頭看夾板與手腕面板（手不跟著動）
            foreach (var _ in Wait(0.3f)) yield return null;
            Shot("rig_xr_workorder");
            log.AppendLine("作業單：open=" + S.Get("ui.workorder_open") + "　選中 " + S.Get(WorkOrderView.SelectedKey) + "　暫停=" + S.Get("training.paused", "false"));

            // ---- 右手射線指到夾板上的按鈕：圓點、射線變藍 ----
            var target = board != null ? board.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(b => b.GetComponentInChildren<UnityEngine.UI.Text>()?.text == "兩條都換") : null;
            if (target != null)
            {
                var tp0 = target.transform.position;
                Sim.Bus.Publish(new ToolReturnRequested { targetId = "env.tool_rack.slot_6", value = 6, time = SimClock.Now });   // 先放回扳手：手上的工具模型不擋鏡頭
                Hands(new Vector3(0.12f, -0.36f, 0.45f), new Vector3(0.18f, -0.12f, 0.22f), tp0);   // 右手在夾板正面上方往下指（介面背面不接受射線）
                for (int i = 0; i < 6; i++) yield return null;
                Look(Vector3.Lerp(tp0, Rig.RightHand.position, 0.25f));
                for (int i = 0; i < 4; i++) yield return null;
                Shot("rig_xr_reticle");
                var pointer = UnityEngine.Object.FindObjectOfType<XrUiPointer>();
                log.AppendLine($"射線指介面：OnUi={(pointer != null && pointer.OnUi)}　圓點直徑 {(pointer != null ? pointer.DotDiameter * 100f : 0f):0.00} cm　指到「兩條都換」");
            }
            else log.AppendLine("找不到夾板上的按鈕，略過 reticle 截圖");
            Send(SimIds.WorkOrder, "toggle");

            // ---- 資訊卡：走到備品架前，右手射線指第 2 條備品 ----
            Sim.Bus.Publish(new ToolReturnRequested { targetId = "env.tool_rack.slot_6", value = 6, time = SimClock.Now });
            Sim.Registry.TryGetPosition("env.spare_shelf.slot_2", out var spare);
            var standAt = new Vector3(spare.x + 0.35f, 0f, spare.z + 0.95f);
            tp.TeleportTo(standAt, Quaternion.LookRotation(Flat(spare - standAt)), null, immediate: true);
            foreach (var _ in Wait(0.3f)) yield return null;
            Look(spare + Vector3.up * 0.15f);
            Hands(new Vector3(-0.35f, -0.8f, 0.1f), new Vector3(0.25f, -0.5f, 0.25f), spare);
            foreach (var _ in Wait(1.0f)) yield return null;
            Shot("rig_xr_hover");
            log.AppendLine("指到：" + S.Get("ui.hover_id") + "　資訊卡：" + InfoCardText.Build(S, Sim.Registry, "env.spare_shelf.slot_2").Replace("\n", " / "));

            // ---- R1：各站點眼高 1.6 m 看面向目標（rig_xr_r1_<站點>）；車頂鎖住、活動欄、面板把手、工具節點 ----
            Sim.Bus.Publish(new ToolReturnRequested { targetId = "env.tool_rack.slot_6", value = 6, time = SimClock.Now });
            S.SetBool("ui.board_hidden", false);
            foreach (var p in tp.Points)
            {
                tp.TeleportTo(p.transform.position, p.transform.rotation, p, immediate: true);
                foreach (var _ in Wait(0.4f)) yield return null;
                Look(Rig.Head.position + Flat(p.transform.forward) * 2f + Vector3.down * 0.9f);
                Hands(new Vector3(-0.35f, -0.8f, 0.1f), new Vector3(0.3f, -0.8f, 0.1f));
                foreach (var _ in Wait(0.3f)) yield return null;
                Shot("rig_xr_r1_" + p.PointId);
                log.AppendLine($"站點 {p.PointId}：{p.transform.position:F3}　視點 {Rig.Head.position:F3}　樓層 {S.Get("xr.floor")}");
            }
            var anchor = tp.Find("tp.platform.anchor");
            tp.TeleportTo(anchor.transform.position, anchor.transform.rotation, anchor, immediate: true);
            S.SetBool("roof.open", false);
            foreach (var _ in Wait(0.4f)) yield return null;
            Look(tp.Find("tp.roof.inner").transform.position + Vector3.up * 0.3f);
            foreach (var _ in Wait(0.3f)) yield return null;
            Shot("rig_xr_r1_roof_locked");
            var gate = GameObject.Find("ENV_PlatformGate");
            if (gate != null) { Look(gate.transform.position + Vector3.up * 0.6f + Vector3.forward * -1.0f); foreach (var _ in Wait(0.3f)) yield return null; Shot("rig_xr_r1_gate"); }
            var handle = GameObject.Find("UI_PanelHandle_board");
            if (handle != null)
            {
                Look(handle.transform.position + Vector3.down * 0.15f);
                Hands(Rig.Head.InverseTransformDirection(handle.transform.position - Rig.Head.position) * 0.8f, new Vector3(0.3f, -0.8f, 0.1f));
                foreach (var _ in Wait(0.3f)) yield return null;
                Shot("rig_xr_r1_panel_handle");
            }
            log.AppendLine("面板把手：" + (handle != null ? handle.transform.position.ToString("F2") : "沒有"));
            Sim.Bus.Publish(new ToolPickRequested { targetId = "env.tool_rack.slot_7", value = 7, time = SimClock.Now });
            foreach (var _ in Wait(0.4f)) yield return null;
            Send("ui.tutorial", "close");
            Hands(new Vector3(-0.12f, -0.35f, 0.38f), new Vector3(0.05f, -0.3f, 0.4f));
            Look(Rig.RightHand.position);
            foreach (var _ in Wait(0.3f)) yield return null;
            Shot("rig_xr_r1_toolwidgets");
            log.AppendLine("工具節點：手上 " + S.Get(SimIds.ActiveTool));
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        static void Shot(string name)
        {
            var cam = Rig.Eye;
            var rt = new RenderTexture(1920, 1080, 24);
            float fov = cam.fieldOfView;
            cam.fieldOfView = 75f;
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            cam.fieldOfView = fov;
            File.WriteAllBytes($"Logs/{name}.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            log.AppendLine("截圖：Logs/" + name + ".png");
        }
    }
}
