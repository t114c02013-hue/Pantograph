using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using PantographSim.Core;
using PantographSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// 介面自我檢查：自動進入 Play，照真實事件路徑（和玩家點擊相同）操作碳滑板切片（教學模式，每個小動作依序做），
    /// 在關鍵狀態輸出 1920×1080 截圖到 Logs/ui_*.png，並記錄每個檢查點的步驟、小動作與錯誤到 Logs/ui-capture.txt。
    /// 工具一律到工具架拿／還（ToolPickRequested／ToolReturnRequested），降弓用升降控制器（ctrl.lift.cmd）。
    /// 觸發：建立 Logs/ui-capture.request，或選單 PantographSim/Slice/Capture UI Screenshots。
    /// 另外：Play 時讀取 Game 視窗的放大倍率（Scale）寫入 ViewportInfo.Zoom，讓介面把面板縮進可見範圍。
    /// </summary>
    [InitializeOnLoad]
    public static class UiCaptureDriver
    {
        const string RequestPath = "Logs/ui-capture.request";
        const string SessionKey = "PantographSim.UiCapturePending";
        static IEnumerator routine;
        static int waitUntilFrame;
        static float zoomOverride = -1f;
        static double nextPoll;
        static readonly StringBuilder log = new StringBuilder();
        static readonly List<string> errors = new List<string>();

        static UiCaptureDriver()
        {
            EditorApplication.update += Tick;
        }

        [MenuItem("PantographSim/Slice/Capture UI Screenshots")]
        public static void Request()
        {
            if (EditorApplication.isPlaying) return;
            if (EditorSceneManager.GetActiveScene().path != SliceSceneBuilder.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(SliceSceneBuilder.ScenePath);
            }
            SessionState.SetBool(SessionKey, true);
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            // Game 視窗放大倍率 → 介面安全區域
            if (EditorApplication.isPlaying) ViewportInfo.Zoom = zoomOverride > 0 ? zoomOverride : ReadGameViewZoom();

            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.timeSinceStartup > nextPoll)
            {
                nextPoll = EditorApplication.timeSinceStartup + 2.0;
                if (File.Exists(RequestPath) && !EditorApplication.isCompiling)
                {
                    File.Delete(RequestPath);
                    if (EditorSceneManager.GetActiveScene().isDirty && !string.IsNullOrEmpty(EditorSceneManager.GetActiveScene().path))
                        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                    if (EditorSceneManager.GetActiveScene().path != SliceSceneBuilder.ScenePath) EditorSceneManager.OpenScene(SliceSceneBuilder.ScenePath);
                    SessionState.SetBool(SessionKey, true);
                    EditorApplication.EnterPlaymode();
                }
                return;
            }

            if (!EditorApplication.isPlaying) return;
            if (routine == null && SessionState.GetBool(SessionKey, false))
            {
                SessionState.SetBool(SessionKey, false);
                log.Clear(); errors.Clear();
                routine = Run();
                waitUntilFrame = Time.frameCount + 5;
            }
            if (routine == null || Time.frameCount < waitUntilFrame) return;
            try
            {
                if (routine.MoveNext()) waitUntilFrame = Time.frameCount + Math.Max(1, routine.Current is int n ? n : 1);
                else Finish();
            }
            catch (Exception e)
            {
                log.AppendLine("例外：" + e);
                Finish();
            }
        }

        static void Finish()
        {
            routine = null;
            zoomOverride = -1f;
            PointerState.ScriptedRay = null;
            if (Sim.State != null) LiftControl.Release(Sim.State);
            log.AppendLine(errors.Count == 0 ? "過程中沒有任何錯誤事件。" : "錯誤事件：\n" + string.Join("\n", errors));
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/ui-capture.txt", log.ToString());
            Debug.Log("[UiCaptureDriver] 完成\n" + log);
            EditorApplication.ExitPlaymode();
        }

        static float ReadGameViewZoom()
        {
            try
            {
                var t = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                var win = Resources.FindObjectsOfTypeAll(t).FirstOrDefault();
                if (win == null) return 1f;
                var za = t.GetField("m_ZoomArea", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(win);
                var scaleProp = za?.GetType().GetProperty("scale", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (scaleProp == null) return 1f;
                var v = (Vector2)scaleProp.GetValue(za);
                return Mathf.Max(1f, v.y);
            }
            catch { return 1f; }
        }

        // ---------- 玩家動作（與介面／場景點擊相同的事件） ----------
        static ISimState S => Sim.State;

        static void Send(string target, string text, string pointId = "") =>
            Sim.Bus.Publish(new ActionRequested { targetId = target, text = text, pointId = pointId, toolId = "", time = SimClock.Now });

        static void Click(string target, string tool = "", string verb = "click") =>
            Sim.Bus.Publish(new ActionRequested { targetId = target, toolId = tool, text = verb, time = SimClock.Now });

        static int SlotOf(string toolId)
        {
            foreach (var entry in S.Get("tools.list").Split(';'))
            {
                var f = entry.Split('|');
                if (f.Length >= 2 && f[1] == toolId && int.TryParse(f[0], out int k)) return k;
            }
            return 0;
        }

        /// <summary>到工具架點掛位拿起工具（第一次拿某工具會跳教學卡，預設直接關掉）。</summary>
        static void Pick(string toolId, bool closeTutorial = true)
        {
            int k = SlotOf(toolId);
            Sim.Bus.Publish(new ToolPickRequested { targetId = "env.tool_rack.slot_" + k, value = k, time = SimClock.Now });
            if (closeTutorial) Send("ui.tutorial", "close");
        }

        /// <summary>把手上的工具掛回工具架。</summary>
        static void Return()
        {
            string active = S.Get(SimIds.ActiveTool);
            if (string.IsNullOrEmpty(active)) return;
            int k = SlotOf(active);
            Sim.Bus.Publish(new ToolReturnRequested { targetId = "env.tool_rack.slot_" + k, value = k, time = SimClock.Now });
        }

        static void Focus(string ids) => Sim.Bus.Publish(new FocusRequested { targetId = ids, time = SimClock.Now });

        static void Note(string what)
        {
            log.AppendLine($"[{what}] 步驟={S.Get("procedure.current")}　小動作={S.GetFloat("procedure.sub_index"):0}/{S.GetFloat("procedure.sub_count"):0}「{S.Get("hint.sub_text")}」" +
                           $"　手上={S.Get(SimIds.ActiveTool)}　錯誤對話框={(S.GetBool("ui.error.active") ? S.Get("ui.error.id") : "無")}" +
                           $"　錯誤數 M{S.GetFloat("score.minor"):0}/J{S.GetFloat("score.major"):0}/C{S.GetFloat("score.critical"):0}");
        }

        static string R(string key) => S.GetFloat(key).ToString("0.00", CultureInfo.InvariantCulture);

        static IEnumerable<int> WaitStep(string runIdPrefix, int maxPolls = 120)
        {
            for (int i = 0; i < maxPolls && !S.Get("procedure.current").StartsWith(runIdPrefix); i++) yield return 5;
            if (!S.Get("procedure.current").StartsWith(runIdPrefix)) log.AppendLine($"⚠ 等不到步驟 {runIdPrefix}（目前 {S.Get("procedure.current")}，小動作 {S.GetFloat("procedure.sub_index"):0}「{S.Get("hint.sub_text")}」，閘門：{S.Get("procedure.gate_reason")}）");
        }

        static IEnumerator Run()
        {
            while (!Sim.Ready || SliceUI.Instance == null) yield return 5;
            Sim.Bus.Subscribe<ErrorDetected>(e => errors.Add($"{S.Get("procedure.current")}：{e.errorId}（{e.severity}）{e.what}"));
            yield return 20;
            foreach (var x in Capture("ui_01_taskcard")) yield return x;

            Send("app.start", "tutorial:SCN_Default");
            yield return 90;
            Note("開始教學模式（升弓中）");
            foreach (var x in Capture("ui_02_si01_sub_a_raised")) yield return x;
            Focus("part.pan_head,env.tool_rack,env.spare_shelf.slot_2,ip.cab.panel,ip.depot.ground_terminal");
            yield return 60;
            foreach (var x in Capture("ui_12_overview_raised")) yield return x;
            foreach (var x in Capture("ui_10_scale150_si01", 1.5f)) yield return x;

            // ---- SI_01：按住「▼ 下降」→ 一半截圖 → 到位截圖 → 點弓頭 → 查看指示燈 ----
            Focus("ip.pan_head.lower_check");
            // 按住「▼ 下降」：每次輪詢都重送 Down（Unity 視窗失焦時 LiftDriver 會自動寫 Stop，這是放開按鈕的安全行為）
            int guard = 0;
            while (S.GetFloat(SimIds.LiftValue, 1f) > 0.5f && guard++ < 3000) { LiftControl.Send(S, "Down"); yield return 2; }
            Note($"下降到一半（lift={S.GetFloat(SimIds.LiftValue):0.00}，距離 {S.GetFloat(SimIds.PanHeadLowerDistance):0.0} cm，差 {S.GetFloat(SimIds.PanHeadLowerDelta):0.0} cm）");
            foreach (var x in Capture("ui_02b_si01_lowering")) yield return x;
            guard = 0;
            while (!SimIds.PanHeadLowered(S) && guard++ < 6000) { LiftControl.Send(S, "Down"); yield return 2; }
            LiftControl.Release(S);
            yield return 40;
            Note($"到降弓位置（距離 {S.GetFloat(SimIds.PanHeadLowerDistance):0.0} cm，容許 {S.GetFloat(SimIds.PanHeadLowerTolerance):0.0} cm）");
            foreach (var x in Capture("ui_02c_si01_in_position")) yield return x;
            Click("part.pan_head", "", "inspect");
            yield return 20;
            Note("SI_01 小動作 b 完成（點弓頭）");
            foreach (var x in Capture("ui_03_si01_sub_c")) yield return x;
            Click("ip.cab.panel");
            yield return 10;
            Send("ip.cab.pan_indicator", "check");
            yield return 15;
            foreach (var x in Capture("ui_03a_si01_panel")) yield return x;
            Send("ip.cab.panel", "close");
            foreach (var x in WaitStep("STP_SI_04")) yield return x;
            yield return 40;
            Note("SI_01 通過");
            foreach (var x in Capture("ui_03b_si01_passed")) yield return x;
            Focus("part.pan_head,env.tool_rack,env.spare_shelf.slot_2,ip.cab.panel,ip.depot.ground_terminal");
            yield return 60;
            foreach (var x in Capture("ui_12b_overview_lowered")) yield return x;

            // ---- SI_04：到工具架拿驗電器（第一次跳教學卡）→ 自檢 → 驗電 → 放回 ----
            yield return 30;
            Note("SI_04 開始：到工具架拿驗電器");
            foreach (var x in Capture("ui_04_si04_go_to_rack")) yield return x;
            Focus("env.tool_rack");
            yield return 60;
            foreach (var x in Capture("ui_04a_tool_rack")) yield return x;
            Pick(SimIds.ToolVoltageDetector, false);
            yield return 40;
            foreach (var x in Capture("ui_04b_si04_tutorial")) yield return x;
            Send("ui.tutorial", "close");
            yield return 20;
            foreach (var x in Capture("ui_05_si04_tool")) yield return x;
            Click(SimIds.VoltageTester, SimIds.ToolVoltageDetector);
            yield return 10;
            Click(SimIds.GroundClamp, SimIds.ToolVoltageDetector);
            yield return 20;
            Return();
            foreach (var x in WaitStep("STP_SI_05")) yield return x;
            Note("SI_04 驗電完成，驗電器已放回");

            // ---- SI_05：故意先掛弓頭端（Critical）→ 從這一步重做 → 正確接地 ----
            Pick(SimIds.ToolGroundingRod);
            yield return 10;
            Click(SimIds.GroundClamp, SimIds.ToolGroundingRod);
            yield return 20;
            Note("故意先掛弓頭（E3）");
            foreach (var x in Capture("ui_06_error_critical")) yield return x;
            Send("ui.error", "restart");
            yield return 15;
            Pick(SimIds.ToolGroundingRod);   // 重做＝回到本步開始：接地棒回到架上，要重新拿
            yield return 10;
            Click(SimIds.GroundTerminal, SimIds.ToolGroundingRod);
            yield return 5;
            Send(SimIds.ToolGroundingRod, "pulltest");
            yield return 5;
            Click(SimIds.GroundClamp, SimIds.ToolGroundingRod);
            foreach (var x in WaitStep("STP_CSR_01")) yield return x;
            Note("SI_05 接地完成（接地棒留在場上）");

            // ---- CSR_01–05：看作業單、卡尺歸零、量兩條、判定 ----
            if (!S.GetBool("ui.workorder_open")) Send(SimIds.WorkOrder, "toggle");
            foreach (var x in WaitStep("STP_CSR_02")) yield return x;
            Pick(SimIds.ToolCaliper);
            yield return 10;
            Send(SimIds.ToolCaliper, "close");
            Send(SimIds.ToolCaliper, "zero");
            foreach (var x in WaitStep("STP_CSR_03")) yield return x;
            Note("CSR_02 卡尺歸零");

            // 重設工具：卡尺回到未歸零（錯誤數保留）→ 截圖 → 重新歸零
            Send("tool.belt", "reset");
            yield return 20;
            Note("重設卡尺（應顯示未歸零，錯誤數保留）");
            foreach (var x in Capture("ui_11_reset_caliper")) yield return x;
            Send(SimIds.ToolCaliper, "close");
            Send(SimIds.ToolCaliper, "zero");
            yield return 20;

            foreach (var p in new[] { "left", "mid", "right" }) { Click($"ip.carbon_strip_L.measure_{p}", SimIds.ToolCaliper); yield return 10; }
            yield return 20;
            foreach (var x in Capture("ui_07_csr03_caliper")) yield return x;
            foreach (var p in new[] { "left", "mid", "right" }) Send(SimIds.WorkOrder, R($"ip.carbon_strip_L.measure_{p}.reading"), $"reading.L.{p}");
            yield return 10;
            // 作業單是置中對話框：工具面板收起，標題列顯示卡尺讀數
            foreach (var x in Capture("ui_13_workorder_dialog")) yield return x;
            foreach (var x in Capture("ui_09b_scale150_workorder", 1.5f)) yield return x;
            Send(SimIds.WorkOrder, "true", "submit.L");
            foreach (var x in WaitStep("STP_CSR_04")) yield return x;
            foreach (var p in new[] { "left", "mid", "right" }) { Click($"ip.carbon_strip_R.measure_{p}", SimIds.ToolCaliper); yield return 10; }
            foreach (var p in new[] { "left", "mid", "right" }) Send(SimIds.WorkOrder, R($"ip.carbon_strip_R.measure_{p}.reading"), $"reading.R.{p}");
            Send(SimIds.WorkOrder, "true", "submit.R");
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_CSR_05")) yield return x;
            Send(SimIds.WorkOrder, "both", "decision");
            foreach (var x in WaitStep("STP_CSR_06")) yield return x;
            Note("CSR_05 判定兩條都換");
            if (S.GetBool("ui.workorder_open")) Send(SimIds.WorkOrder, "toggle");   // 收起作業單，工具面板回來
            yield return 10;

            // ---- 肘側：鬆、拆、取、清、檢、裝、預鎖 ----
            Pick(SimIds.ToolSocket);
            yield return 10;
            Send(SimIds.ToolSocket, "size:13");
            Send(SimIds.ToolSocket, "dir:loosen");
            yield return 10;
            for (int n = 1; n <= 4; n++) { Sim.Perform(SimIds.ToolSocket, $"ip.carbon_strip_L.bolt_{n}", "rotate:13:loosen", 180); yield return 3; }
            foreach (var x in WaitStep("STP_CSR_07")) yield return x;
            for (int n = 1; n <= 4; n++)
            {
                Sim.Perform(SimIds.ToolSocket, $"ip.carbon_strip_L.bolt_{n}", "rotate:13:loosen", 600); yield return 3;
                Click($"ip.carbon_strip_L.bolt_{n}", SimIds.ToolSocket); yield return 3;
            }
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_CSR_08")) yield return x;
            Send("part.carbon_strip_L", "remove:vertical");
            yield return 15;
            Click(SimIds.PartsTray);
            foreach (var x in WaitStep("STP_CSR_09")) yield return x;
            Pick(SimIds.ToolCloth);
            yield return 10;
            for (float u = 0; u <= 1.001f; u += 0.05f)
            {
                Sim.Perform(SimIds.ToolCloth, "ip.carbon_strip_L.bracket", $"clean:{u.ToString(CultureInfo.InvariantCulture)},0.2");
                Sim.Perform(SimIds.ToolCloth, "ip.carbon_strip_L.bracket", $"clean:{u.ToString(CultureInfo.InvariantCulture)},0.8");
            }
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_CSR_10")) yield return x;
            foreach (var slot in new[] { 1, 2, 3 }) { Click($"env.spare_shelf.slot_{slot}"); yield return 10; }
            Send("env.spare_shelf.slot_2", "flip");
            yield return 10;
            Pick(SimIds.ToolCaliper);
            yield return 10;
            Click("env.spare_shelf.slot_2", SimIds.ToolCaliper);
            yield return 10;
            Return();
            yield return 5;
            Send("env.spare_shelf.slot_2", "pick");
            foreach (var x in WaitStep("STP_CSR_11")) yield return x;
            // 把「滑鼠」移到托架正上方（腳本射線），等手上的新滑板跟過去
            if (Sim.Registry.TryGetPosition("ip.carbon_strip_L.bracket", out var bracket))
                PointerState.ScriptedRay = new Ray(bracket + Vector3.up * 2f, Vector3.down);
            yield return 30;
            Send("carried", "rotate180");
            yield return 5;
            Click("ip.carbon_strip_L.bracket");
            yield return 5;
            PointerState.ScriptedRay = null;
            foreach (var x in WaitStep("STP_CSR_12")) yield return x;
            Note("CSR_11 安裝肘側新滑板");
            Pick(SimIds.ToolSocket);
            yield return 10;
            for (int n = 1; n <= 4; n++) { Click($"ip.carbon_strip_L.bolt_{n}", SimIds.ToolSocket); yield return 3; }
            Send(SimIds.ToolSocket, "dir:lock");
            yield return 5;
            for (int n = 1; n <= 4; n++) { Sim.Perform(SimIds.ToolSocket, $"ip.carbon_strip_L.bolt_{n}", "rotate:13:lock", 360); yield return 3; }
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_CSR_13")) yield return x;
            Note("CSR_12 預鎖完成");

            // ---- CSR_13：扭力扳手（第一次跳教學 → 關掉 → 設定 25 → 螺栓 1 鎖到值）----
            Pick(SimIds.ToolTorque);
            yield return 10;
            Send(SimIds.ToolTorque, "set:25");
            Sim.Perform(SimIds.ToolTorque, "ip.carbon_strip_L.bolt_1", "torque:25", 25.2f);
            S.SetFloat("tool.torque_wrench.current", 25.2f);
            S.SetBool("tool.torque_wrench.reached", true);
            S.SetFloat("tool.torque_wrench.flash_time", Time.time);
            yield return 15;
            Note("CSR_13 扭力（螺栓 1 已鎖到 25.2）");
            foreach (var x in Capture("ui_08_csr13_torque")) yield return x;
            foreach (var x in Capture("ui_09_scale150_csr13", 1.5f)) yield return x;
        }

        /// <summary>把攝影機與介面輸出到 1920×1080 截圖；zoom &gt; 1 時模擬 Game 視窗放大（只看得到中央），驗證面板不會被切掉。</summary>
        static IEnumerable<int> Capture(string name, float zoom = 1f)
        {
            var cam = Camera.main;
            var ui = SliceUI.Instance;
            if (cam == null || ui == null) { log.AppendLine("無法截圖：" + name); yield break; }
            zoomOverride = zoom;
            var rt = new RenderTexture(1920, 1080, 24);
            ui.BeginCapture(cam, rt);
            yield return 4;
            ui.RefreshAll();
            Canvas.ForceUpdateCanvases();
            string overlap = OverlapReport(ui.Safe);
            ui.RenderCapture();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            ui.EndCapture();
            zoomOverride = -1f;
            if (zoom > 1f) tex = CropCenter(tex, zoom);
            File.WriteAllBytes($"Logs/{name}.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            log.AppendLine("截圖：Logs/" + name + ".png" + (zoom > 1f ? $"（模擬 Game 視窗 Scale {zoom}x：只保留中央可見區域）" : "") + "　" + overlap);
            yield return 2;
        }

        /// <summary>步驟面板、工具面板、升降控制器三者（有顯示的）是否互相重疊。</summary>
        static string OverlapReport(RectTransform safe)
        {
            var names = new[] { "StepPanel", "ToolPanel", "LiftPanel" };
            var rects = new List<(string name, Rect rect)>();
            foreach (var n in names)
            {
                var rt = safe.Find(n) as RectTransform;
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                var c = new Vector3[4];
                rt.GetWorldCorners(c);
                Vector2 a = safe.InverseTransformPoint(c[0]), b = safe.InverseTransformPoint(c[2]);
                rects.Add((n, Rect.MinMaxRect(a.x, a.y, b.x, b.y)));
            }
            var hits = new List<string>();
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    if (rects[i].rect.Overlaps(rects[j].rect)) hits.Add(rects[i].name + "×" + rects[j].name);
            string shown = string.Join("、", rects.Select(r => r.name));
            return hits.Count == 0 ? $"面板不重疊（顯示中：{(shown.Length > 0 ? shown : "無")}）" : "⚠ 重疊：" + string.Join("、", hits);
        }

        static Texture2D CropCenter(Texture2D src, float zoom)
        {
            int w = Mathf.RoundToInt(src.width / zoom), h = Mathf.RoundToInt(src.height / zoom);
            int x0 = (src.width - w) / 2, y0 = (src.height - h) / 2;
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGB24, false);
            for (int y = 0; y < dst.height; y++)
                for (int x = 0; x < dst.width; x++)
                    dst.SetPixel(x, y, src.GetPixelBilinear((x0 + (x + 0.5f) * w / dst.width) / src.width, (y0 + (y + 0.5f) * h / dst.height) / src.height));
            dst.Apply();
            UnityEngine.Object.DestroyImmediate(src);
            return dst;
        }
    }
}
