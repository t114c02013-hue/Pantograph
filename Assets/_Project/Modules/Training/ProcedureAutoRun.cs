using System;
using System.Collections.Generic;
using System.Globalization;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Training
{
    /// <summary>
    /// 代替人把 R1 的步驟照標準做完（送的是與介面／場景點擊相同的事件，不改判定）。yield 的數字＝要等幾幀。
    /// 編輯器的 RecordingCheckDriver 和後台控制面板共用；走到指定步驟後用 HandOff 交給人。
    /// </summary>
    public class ProcedureAutoRun
    {
        /// <summary>每步之間額外等待（秒）。</summary>
        public float Pace;
        /// <summary>整場目標長度（秒）；大於 0 時扭力步驟等到最後一分鐘。</summary>
        public float Target;
        /// <summary>開頭先故意犯一次 E1（紀錄功能的實機檢查用）。</summary>
        public bool DemoError;
        public Action<string> Log = _ => { };
        /// <summary>從開始錄影起的秒數；沒在錄影是 -1。</summary>
        public Func<float> VideoSeconds = () => -1f;
        readonly float started = Time.realtimeSinceStartup;
        public float Elapsed => Time.realtimeSinceStartup - started;

        public static string DriverTag => ToolRulesDef.Load().testDriverTag;

        /// <summary>走到 until 指定的步驟了沒。</summary>
        public static bool ReachedStep(string current, string until) => !string.IsNullOrEmpty(until) && !string.IsNullOrEmpty(current) && current.StartsWith(until);

        const float VrWaitSeconds = 6f;   // 比 XrModeSwitch 等頭盔的 4 秒長

        /// <summary>後台面板：開始一場，照標準做到 stepId 再交給人。走不到就停在原地，把原因寫在面板上。</summary>
        public IEnumerable<int> GoTo(string mode, string stepId)
        {
            yield return 20;
            // 等 VR 開場走完再開始：不然紀錄的輸入方式會寫成桌機（Link 關著時等滿就照桌機走）
            for (float until = Time.realtimeSinceStartup + VrWaitSeconds; Time.realtimeSinceStartup < until && !InputMode.ForceDesktop && !S.GetBool("xr.ready");) yield return 5;
            Send("rec", "trainee:" + DriverTag);
            Send("app.start", mode + ":SCN_Default");
            yield return 30;
            if (!ReachedStep(S.Get("procedure.current"), stepId))
                foreach (var x in Procedure())
                {
                    if (ReachedStep(S.Get("procedure.current"), stepId)) break;
                    yield return x;
                }
            if (!ReachedStep(S.Get("procedure.current"), stepId))
            {
                S.Set(SimIds.AutoRunStatus, $"✘ 走不到 {stepId}，停在 {S.Get("procedure.current")}");
                yield break;
            }
            foreach (var x in HandOff()) yield return x;
            S.Set(SimIds.AutoRunStatus, "");
        }

        /// <summary>交給人接手：手上的工具收回原位、面板關掉，之後的操作才算數。</summary>
        public IEnumerable<int> HandOff()
        {
            PointerState.ScriptedRay = null;   // 腳本可能停在動作中間
            PointerState.XrTriggerHeld = false;
            LiftControl.Release(S);
            Return();
            yield return 10;
            if (S.GetBool("ui.workorder_open")) Send(SimIds.WorkOrder, "toggle");
            Send("ui.tutorial", "close");
            Send("xr.tutorial", "skip");   // 開場控制器教學不留在眼前
            Sim.Bus.Publish(new FocusRequested { time = SimClock.Now });   // 人轉向這一步的目標（桌面是鏡頭）
            yield return 40;
            S.Set("ui.toast", "");
            Sim.Bus.Publish(new TestDriverHandoff { tag = DriverTag, time = SimClock.Now });
        }

        public static ISimState S => Sim.State;
        public static void Send(string target, string text, string pointId = "") =>
            Sim.Bus.Publish(new ActionRequested { targetId = target, text = text, pointId = pointId, toolId = "", time = SimClock.Now });
        public static void Click(string target, string tool = "", string verb = "click") =>
            Sim.Bus.Publish(new ActionRequested { targetId = target, toolId = tool, text = verb, time = SimClock.Now });

        public static int SlotOf(string toolId)
        {
            foreach (var entry in S.Get("tools.list").Split(';'))
            {
                var f = entry.Split('|');
                if (f.Length >= 2 && f[1] == toolId && int.TryParse(f[0], out int k)) return k;
            }
            return 0;
        }

        public static void Pick(string toolId)
        {
            int k = SlotOf(toolId);
            Sim.Bus.Publish(new ToolPickRequested { targetId = "env.tool_rack.slot_" + k, value = k, time = SimClock.Now });
            Send("ui.tutorial", "close");
        }

        public static void Return()
        {
            string active = S.Get(SimIds.ActiveTool);
            if (string.IsNullOrEmpty(active)) return;
            int k = SlotOf(active);
            Sim.Bus.Publish(new ToolReturnRequested { targetId = "env.tool_rack.slot_" + k, value = k, time = SimClock.Now });
        }

        static string R(string key) => S.GetFloat(key).ToString("0.00", CultureInfo.InvariantCulture);

        public void Note(string what) =>
            Log($"[{Elapsed,6:0.0}s] {what}　步驟={S.Get("procedure.current")}　小動作={S.GetFloat("procedure.sub_index"):0}/{S.GetFloat("procedure.sub_count"):0}" +
                           $"　手上={S.Get(SimIds.ActiveTool)}　錯誤數 M{S.GetFloat("score.minor"):0}/J{S.GetFloat("score.major"):0}/C{S.GetFloat("score.critical"):0}");

        IEnumerable<int> WaitStep(string runIdPrefix, float maxSeconds = 15f)
        {
            float until = Time.realtimeSinceStartup + maxSeconds;
            while (Time.realtimeSinceStartup < until && !S.Get("procedure.current").StartsWith(runIdPrefix) && !S.GetBool("procedure.finished")) yield return 5;
            if (!S.Get("procedure.current").StartsWith(runIdPrefix) && !S.GetBool("procedure.finished"))
                Log($"⚠ 等不到步驟 {runIdPrefix}（目前 {S.Get("procedure.current")}，小動作 {S.GetFloat("procedure.sub_index"):0}「{S.Get("hint.sub_text")}」，閘門：{S.Get("procedure.gate_reason")}）");
            yield return 1;   // 換步之後先讓外面檢查到了沒，不然同一幀會接著做下一步
        }

        public static IEnumerable<int> Sec(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return 1;
        }

        IEnumerable<int> Paced() => Sec(Pace);

        /// <summary>練習模式照標準做完 25 步（DemoError：SI_01 先故意在升弓時碰螺栓＝E1，選從這一步重做）。</summary>
        public IEnumerable<int> Procedure()
        {
            // ---- E1：還在升弓就拿套筒去碰螺栓 ----
            if (DemoError)
            {
                Pick(SimIds.ToolSocket);
                yield return 10;
                Click("ip.carbon_strip_L.bolt_1", SimIds.ToolSocket);
                yield return 20;
                Note($"故意 E1（錯誤視窗 {S.Get("ui.error.id")}）");
                Send("ui.error", "restart");
                yield return 15;
                Return();
                yield return 10;
                foreach (var x in Paced()) yield return x;
            }

            // ---- SI_01 ----
            float until = Time.realtimeSinceStartup + 90f;   // 依時間等（Editor 幀率不固定）
            while (!SimIds.PanHeadLowered(S) && Time.realtimeSinceStartup < until) { LiftControl.Send(S, "Down"); yield return 2; }
            LiftControl.Release(S);
            yield return 30;
            Click("part.pan_head", "", "inspect");
            yield return 10;
            Click("ip.cab.panel");
            yield return 10;
            Send("ip.cab.pan_indicator", "check");
            yield return 10;
            Send("ip.cab.panel", "close");
            foreach (var x in WaitStep("STP_SI_04")) yield return x;
            Note("SI_01 通過");
            foreach (var x in Paced()) yield return x;

            // ---- SI_04 驗電 ----
            Pick(SimIds.ToolVoltageDetector);
            yield return 10;
            Click(SimIds.VoltageTester, SimIds.ToolVoltageDetector);
            yield return 10;
            Click(SimIds.GroundClamp, SimIds.ToolVoltageDetector);
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_SI_05")) yield return x;
            foreach (var x in Paced()) yield return x;

            // ---- SI_05 接地 ----
            Pick(SimIds.ToolGroundingRod);
            yield return 10;
            Click(SimIds.GroundTerminal, SimIds.ToolGroundingRod);
            yield return 5;
            Send(SimIds.ToolGroundingRod, "pulltest");
            yield return 5;
            Click(SimIds.GroundClamp, SimIds.ToolGroundingRod);
            foreach (var x in WaitStep("STP_CSR_01")) yield return x;
            Note("SI_05 接地完成");
            foreach (var x in Paced()) yield return x;

            // ---- CSR_01–05 ----
            if (!S.GetBool("ui.workorder_open")) Send(SimIds.WorkOrder, "toggle");
            foreach (var x in WaitStep("STP_CSR_02")) yield return x;
            Pick(SimIds.ToolCaliper);
            yield return 10;
            Send(SimIds.ToolCaliper, "close");
            Send(SimIds.ToolCaliper, "zero");
            foreach (var x in WaitStep("STP_CSR_03")) yield return x;
            foreach (var x in Paced()) yield return x;
            foreach (var side in new[] { "L", "R" })
            {
                foreach (var pt in new[] { "left", "mid", "right" }) { Click($"ip.carbon_strip_{side}.measure_{pt}", SimIds.ToolCaliper); yield return 10; }
                foreach (var pt in new[] { "left", "mid", "right" }) Send(SimIds.WorkOrder, R($"ip.carbon_strip_{side}.measure_{pt}.reading"), $"reading.{side}.{pt}");
                yield return 5;
                Send(SimIds.WorkOrder, "true", "submit." + side);
                yield return 10;
                foreach (var x in Paced()) yield return x;
            }
            Return();
            foreach (var x in WaitStep("STP_CSR_05")) yield return x;
            Send(SimIds.WorkOrder, "both", "decision");
            foreach (var x in WaitStep("STP_CSR_06")) yield return x;
            if (S.GetBool("ui.workorder_open")) Send(SimIds.WorkOrder, "toggle");
            yield return 10;
            Note("CSR_05 判定兩條都換");

            // ---- 兩側：CSR_06–12，再兩側 CSR_13–14（扭力放到最後一分鐘） ----
            foreach (var x in ReplaceSide("L")) yield return x;
            foreach (var x in TorqueAndMark("L", Target)) yield return x;
            foreach (var x in ReplaceSide("R")) yield return x;
            foreach (var x in TorqueAndMark("R", 0)) yield return x;

            // ---- CSR_15 高低差 ----
            Pick(SimIds.ToolCaliper);
            yield return 10;
            foreach (var side in new[] { "L", "R" }) { Click($"ip.carbon_strip_{side}.measure_mid", SimIds.ToolCaliper); yield return 10; }
            if (!S.GetBool("ui.workorder_open")) Send(SimIds.WorkOrder, "toggle");
            foreach (var side in new[] { "L", "R" }) Send(SimIds.WorkOrder, R($"ip.carbon_strip_{side}.measure_mid.new_reading"), "newmid." + side);
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_WC_01")) yield return x;
            Note("CSR_15 高低差");

            // ---- WC_01 清點 ----
            Click("env.tool_rack");
            yield return 5;
            Click(SimIds.PartsTray);
            yield return 5;
            Send(SimIds.WorkOrder, "true", "toolcount_confirmed");
            yield return 10;
            if (S.GetBool("ui.workorder_open")) Send(SimIds.WorkOrder, "toggle");
            foreach (var x in WaitStep("STP_WC_03")) yield return x;

            // ---- WC_03 拆接地（徒手：先弓頭、再端子，接地棒掛回第 3 格） ----
            Click(SimIds.GroundClamp);
            yield return 10;
            Click(SimIds.GroundTerminal);
            yield return 10;
            int slot = SlotOf(SimIds.ToolGroundingRod);
            Sim.Bus.Publish(new ToolReturnRequested { targetId = "env.tool_rack.slot_" + slot, value = slot, time = SimClock.Now });
            for (int i = 0; i < 200 && !S.GetBool("score.finished"); i++) yield return 5;
            Note("WC_03 拆接地");
        }

        IEnumerable<int> ReplaceSide(string side)
        {
            string b(int n) => $"ip.carbon_strip_{side}.bolt_{n}";
            foreach (var x in WaitStep("STP_CSR_06")) yield return x;
            Pick(SimIds.ToolSocket);
            yield return 10;
            Send(SimIds.ToolSocket, "size:13");
            Send(SimIds.ToolSocket, "dir:loosen");
            yield return 10;
            for (int n = 1; n <= 4; n++) { Sim.Perform(SimIds.ToolSocket, b(n), "rotate:13:loosen", 180); yield return 3; }
            foreach (var x in WaitStep("STP_CSR_07")) yield return x;
            for (int n = 1; n <= 4; n++)
            {
                Sim.Perform(SimIds.ToolSocket, b(n), "rotate:13:loosen", 600); yield return 3;
                Click(b(n), SimIds.ToolSocket); yield return 3;
            }
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_CSR_08")) yield return x;
            foreach (var x in Paced()) yield return x;
            Send("part.carbon_strip_" + side, "remove:vertical");
            yield return 15;
            Click(SimIds.PartsTray);
            foreach (var x in WaitStep("STP_CSR_09")) yield return x;
            Pick(SimIds.ToolCloth);
            yield return 10;
            for (float u = 0; u <= 1.001f; u += 0.05f)
            {
                Sim.Perform(SimIds.ToolCloth, $"ip.carbon_strip_{side}.bracket", $"clean:{u.ToString(CultureInfo.InvariantCulture)},0.2");
                Sim.Perform(SimIds.ToolCloth, $"ip.carbon_strip_{side}.bracket", $"clean:{u.ToString(CultureInfo.InvariantCulture)},0.8");
            }
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_CSR_10")) yield return x;
            foreach (var x in Paced()) yield return x;
            foreach (var s in new[] { 1, 2, 3 }) { Click($"env.spare_shelf.slot_{s}"); yield return 10; }
            Send(S.Get("spare.good_slot"), "flip");
            yield return 10;
            Pick(SimIds.ToolCaliper);
            yield return 10;
            Click(S.Get("spare.good_slot"), SimIds.ToolCaliper);
            yield return 10;
            Return();
            yield return 5;
            Send(S.Get("spare.good_slot"), "pick");
            foreach (var x in WaitStep("STP_CSR_11")) yield return x;
            if (Sim.Registry.TryGetPosition($"ip.carbon_strip_{side}.bracket", out var bracket))
                PointerState.ScriptedRay = new Ray(bracket + Vector3.up * 2f, Vector3.down);
            yield return 30;
            Send("carried", "rotate180");
            yield return 5;
            Click($"ip.carbon_strip_{side}.bracket");
            yield return 5;
            PointerState.ScriptedRay = null;
            foreach (var x in WaitStep("STP_CSR_12")) yield return x;
            foreach (var x in Paced()) yield return x;
            Pick(SimIds.ToolSocket);
            yield return 10;
            for (int n = 1; n <= 4; n++) { Click(b(n), SimIds.ToolSocket); yield return 3; }
            Send(SimIds.ToolSocket, "dir:lock");
            yield return 5;
            for (int n = 1; n <= 4; n++) { Sim.Perform(SimIds.ToolSocket, b(n), "rotate:13:lock", 360); yield return 3; }
            yield return 10;
            Return();
            foreach (var x in WaitStep("STP_CSR_13")) yield return x;
            Note($"CSR_06–12 {side} 完成");
        }

        /// <summary>扭力：真的按住（XR 扳機）讓扭力每秒 +8 N·m 上升，到 25 N·m 喀一聲、扳手閃色就放開。target&gt;0 時先等到整場只剩約 60 s。</summary>
        IEnumerable<int> TorqueAndMark(string side, float target)
        {
            if (target > 0)
            {
                float wait = target - 60f - Elapsed;
                if (wait > 0) { Log($"等 {wait:0} s，讓扭力步驟落在最後一分鐘"); foreach (var x in Sec(wait)) yield return x; }
            }
            Pick(SimIds.ToolTorque);
            yield return 10;
            Send(SimIds.ToolTorque, "set:25");
            yield return 5;
            for (int n = 1; n <= 4; n++)
            {
                string bolt = $"ip.carbon_strip_{side}.bolt_{n}";
                PointerState.XrTriggerHeld = true;
                Click(bolt, SimIds.ToolTorque);
                yield return 1;
                float limit = Time.realtimeSinceStartup + 10f;   // 每秒 +8 N·m：依時間等，不依幀數
                while (!S.GetBool("tool.torque_wrench.reached") && Time.realtimeSinceStartup < limit) yield return 1;
                Log($"[{Elapsed,6:0.0}s] 喀（{side} 螺栓 {n}）flash_time={S.GetFloat("tool.torque_wrench.flash_time"):0.000}　影片時間≈{VideoSeconds():0.000}");
                PointerState.XrTriggerHeld = false;
                yield return 6;
            }
            Return();
            foreach (var x in WaitStep("STP_CSR_14")) yield return x;
            Pick(SimIds.ToolMarker);
            yield return 10;
            for (int n = 1; n <= 4; n++) { Click($"ip.carbon_strip_{side}.bolt_{n}", SimIds.ToolMarker); yield return 3; }
            yield return 5;
            Return();
            yield return 10;
            Note($"CSR_13–14 {side} 完成");
        }
    }
}
