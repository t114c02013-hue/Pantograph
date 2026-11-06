using System;
using System.Collections;
using System.Collections.Generic;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;
using UnityEngine.XR;

namespace PantographSim.XR
{
    /// <summary>
    /// VR 開場時序（2026-11-04 試玩修正 第 0 點）：頭盔還沒追蹤就擺人和介面，人會偏離站點、任務卡留在原地看不到。依序做，每一步等前一步完成：
    /// (1) 等頭的追蹤有效（裝置回報 isTracked，只有 OpenXR session 跑起來才會有，而且頭高 &gt; trackedMinHeadY），最多等 startupTimeoutSec，逾時用 fallbackHeadHeight 擺並寫警告；
    /// (2) 才 InputMode.SetXr(true)；(3) 把頭傳到開場站點（資料：course.start_station，R1 用 StationSet.startStationId）；
    /// (4) 以這時的頭擺任務卡、提示板；(5) 寫 xr.ready，開放輸入。逾時之後追蹤才回來時，再做一次 (3)(4)。
    /// </summary>
    public class XrStartupSequencer : MonoBehaviour
    {
        public const string ReadyKey = "xr.ready";
        public enum Gate { Wait, Go, Timeout }

        /// <summary>測試用：取代真的頭盔追蹤（tracked, 頭離地高度）。</summary>
        public static Func<(bool tracked, float headY)> ProbeOverride;

        XrRig rig; XrTeleport teleport; PanelLayout layout;

        /// <summary>這一刻要不要往下走（純函式）：追蹤有效＝Go；超過等待時間＝Timeout；其他繼續等。</summary>
        public static Gate Decide(bool tracked, float headY, float elapsed, PanelLayout l)
        {
            if (tracked && headY > l.trackedMinHeadY) return Gate.Go;
            return elapsed >= l.startupTimeoutSec ? Gate.Timeout : Gate.Wait;
        }

        (bool tracked, float headY) Probe()
        {
            if (ProbeOverride != null) return ProbeOverride();
            var inputs = new List<XRInputSubsystem>();
            SubsystemManager.GetInstances(inputs);
            bool running = inputs.Exists(x => x.running);
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool tracked = running && head.isValid && head.TryGetFeatureValue(CommonUsages.isTracked, out bool t) && t;
            return (tracked, teleport != null ? teleport.HeadHeight : 0f);
        }

        /// <summary>simulated＝Editor 截圖、沒有裝置：不等追蹤（測試用 ProbeOverride 時照等）。</summary>
        public IEnumerator Run(XrRig r, XrTeleport tp, bool simulated, Action enterXr, Action ready)
        {
            rig = r; teleport = tp;
            layout = Resources.Load<PanelLayout>(PanelLayout.ResourceName);
            float t0 = Time.realtimeSinceStartup;
            var gate = simulated && ProbeOverride == null ? Gate.Go : Gate.Wait;
            while (gate == Gate.Wait)
            {
                yield return null;
                var p = Probe();
                gate = Decide(p.tracked, p.headY, Time.realtimeSinceStartup - t0, layout);
            }
            if (gate == Gate.Timeout) Debug.LogWarning($"[XR] 開場：{layout.startupTimeoutSec:0.#} s 內頭盔追蹤沒有生效，先用預設高度 {PostureDef.Load().standEye} m 擺放");
            else Debug.Log($"[XR] 開場：追蹤有效，啟動後 {Time.realtimeSinceStartup - t0:0.00} s");

            Sim.State?.SetFloat("xr.stand_eye", PostureDef.Load().standEye);
            Sim.State?.SetFloat("xr.dialog.distance", layout.taskCardDistance);
            Sim.State?.SetFloat("xr.dialog.below", layout.taskCardBelowEye);
            Sim.State?.SetFloat(XrToolCardKeys.Right, layout.toolCardOffset.x);
            Sim.State?.SetFloat(XrToolCardKeys.Up, layout.toolCardOffset.y);
            Sim.State?.SetFloat(XrToolCardKeys.Toward, layout.toolCardOffset.z);
            Sim.State?.SetFloat(XrToolCardKeys.Scale, layout.toolCardScale);
            Sim.State?.SetFloat(XrToolCardKeys.SummaryScale, layout.toolSummaryScale);
            enterXr?.Invoke();   // (2)
            for (int i = 0; i < 60 && !(Sim.State?.GetBool(XrPanels.ManagedKey) ?? true); i++) yield return null;   // 介面搬到世界空間、面板登記好
            Place(gate == Gate.Timeout);   // (3)(4)
            Sim.State?.SetBool(ReadyKey, true);   // (5)
            ready?.Invoke();
            if (gate == Gate.Timeout) { yield return null; yield return null; Place(true); }   // 開場教學的對話框是照還沒追蹤的頭開的，擺回預設眼高

            while (gate == Gate.Timeout)   // 追蹤比等待時間晚才回來：人和介面再擺一次
            {
                yield return null;
                var p = Probe();
                if (Decide(p.tracked, p.headY, 0f, layout) == Gate.Go) { gate = Gate.Go; Place(false); }
            }
        }

        void Place(bool fallback)
        {
            if (teleport == null) return;
            teleport.PlaceAtStart(layout.startFacesOpposite);
            var head = rig != null ? rig.Head : null;
            var st = teleport.StartPoint;
            if (head == null || st == null) return;
            var eye = fallback ? st.transform.position + Vector3.up * PostureDef.Load().standEye : head.position;
            // 任務卡的方向跟站點走，不跟頭：固定在升降盒對面
            PlacePanels(eye, st.transform.forward * (layout.startFacesOpposite ? -1f : 1f));
        }

        void PlacePanels(Vector3 eye, Vector3 forward)
        {
            var panels = FindObjectOfType<XrPanels>();
            if (panels != null) panels.PlaceInFront(eye, forward);
        }

        /// <summary>≡ 選單「重設高度」：回到最近的站點中心，再把面板擺到頭前（同開場的 (3)(4)）。</summary>
        public void Reseat()
        {
            if (teleport == null) return;
            teleport.Recenter();
            if (rig != null && rig.Head != null && (Sim.State?.GetBool("training.running") ?? false)) PlacePanels(rig.Head.position, rig.Head.forward);   // 開始之前面板固定
        }
    }
}
