using System.Globalization;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// 作業單（env.work_order）。介面以 ActionRequested(env.work_order, pointId=欄位, text=值) 填寫；
    /// 寫入 workorder.&lt;欄位&gt; 並發 RecordWritten。欄位：reading.L.left/mid/right、reading.R.*、submit.L/R、
    /// decision（both / L / R / pass）、newmid.L/R、signed。讀數會與卡尺實際讀數比對（差 &gt;0.05 記 mismatch）。
    /// </summary>
    public class WorkOrder : SiteObject
    {
        public const float Tolerance = 0.05f;

        /// <summary>拿著工具也能點桌上的作業單打開／收起（邊量邊抄）。</summary>
        protected override bool AcceptsAnyTool => true;

        protected override void OnBareHand(ActionRequested e)
        {
            // 場景點擊（Picker）的 pointId 是擊中點座標、text 是 click／inspect：一律當成打開／收起，不是填欄位
            if (IsToggle(e)) { State.SetBool("ui.workorder_open", !State.GetBool("ui.workorder_open")); return; }
            Write(State, Bus, e.pointId, e.text);
        }

        /// <summary>介面填欄位時 pointId＝欄位名（reading.L.mid、decision…）；其餘（toggle、場景點擊）＝打開／收起。</summary>
        public static bool IsToggle(ActionRequested e) =>
            string.IsNullOrEmpty(e.pointId) || e.text == "toggle" || e.text == "click" || e.text == "inspect" || !IsField(e.pointId);

        static bool IsField(string field) =>
            field.StartsWith("reading.") || field.StartsWith("newmid.") || field.StartsWith("submit.") || field == "decision"
            || field == SignField || field == SignStart || field == SignEnd || field == "toolcount_confirmed" || field.EndsWith("_signed");

        /// <summary>
        /// 「作業人員簽名」按鈕送 sign：依目前步驟寫進對應的格子——SI_06（開工）＝sign_start、WC_04（完工）＝sign_end；
        /// 其他時候提示「現在還不能簽」，不寫入任何格子、不算錯誤。同時寫步驟資料原本用的 workorder.si_signed／unlock_signed。
        /// </summary>
        public const string SignField = "sign", SignStart = "sign_start", SignEnd = "sign_end", NotYet = "現在還不能簽";

        public static string SignTarget(ISimState s)
        {
            string cur = s.Get("procedure.current");
            return cur.StartsWith("STP_SI_06") ? SignStart : cur.StartsWith("STP_WC_04") ? SignEnd : "";
        }

        /// <summary>
        /// 寫一個欄位；回傳是否接受。欄位值、比對結果（mismatch）、送出、高低差一批寫入；
        /// RecordWritten 在 Commit 之後才發（收到的模組讀到的是這次讀數，不是前一次）。
        /// </summary>
        public static bool Write(ISimState s, IEventBus bus, string field, string value)
        {
            string written = null;
            if (field == SignField)
            {
                field = SignTarget(s);
                if (field == "") { s.Set("ui.toast", NotYet); return false; }
                value = "true";
            }
            bool ok = s.BatchResult(() =>
            {
                written = (value ?? "").Trim();
                if (field == SignStart) s.SetBool("workorder.si_signed", true);
                if (field == SignEnd) s.SetBool("workorder.unlock_signed", true);
                if (field.StartsWith("reading.") || field.StartsWith("newmid."))
                {
                    if (!float.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { s.Set("workorder.last_error", "請輸入數字（mm）"); return false; }
                    written = v.ToString("0.00", CultureInfo.InvariantCulture);
                    string readingKey = ReadingKeyFor(field);
                    if (readingKey != null && s.Has(readingKey))
                        s.SetBool("workorder." + field + ".mismatch", Mathf.Abs(s.GetFloat(readingKey) - v) > Tolerance + 1e-4f);
                }
                s.Set("workorder." + field, written);
                if (field.StartsWith("submit.")) s.SetBool("workorder.reading." + field.Substring(7) + ".submitted", true);
                if (field.StartsWith("newmid.") && s.Has("workorder.newmid.L") && s.Has("workorder.newmid.R"))
                    s.SetFloat("workorder.height_diff", Mathf.Abs(s.GetFloat("workorder.newmid.L") - s.GetFloat("workorder.newmid.R")));
                return true;
            });
            if (ok) s.AfterCommit(() => bus?.Publish(new RecordWritten { targetId = SimIds.WorkOrder, pointId = field, text = written, time = SimClock.Now }));
            return ok;
        }

        /// <summary>reading.L.left → ip.carbon_strip_L.measure_left.reading；newmid.R → ip.carbon_strip_R.measure_mid.reading。</summary>
        public static string ReadingKeyFor(string field)
        {
            var p = field.Split('.');
            if (p[0] == "reading" && p.Length == 3) return $"ip.carbon_strip_{p[1]}.measure_{p[2]}.reading";
            if (p[0] == "newmid" && p.Length == 2) return $"ip.carbon_strip_{p[1]}.measure_mid.reading";
            return null;
        }
    }
}
