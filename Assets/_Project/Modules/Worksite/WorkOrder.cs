using System.Globalization;
using System.Linq;
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
            if (IsToggle(e))
            {
                bool open = !State.GetBool("ui.workorder_open");
                State.SetBool("ui.workorder_open", open);
                if (open) CheckIsolation(State, ServiceLocator.Get<PantographSim.Data.ProcedureDefinition>());
                return;
            }
            Write(State, Bus, e.pointId, e.text);
        }

        /// <summary>介面填欄位時 pointId＝欄位名（reading.L.mid、decision…）；其餘（toggle、場景點擊）＝打開／收起。</summary>
        public static bool IsToggle(ActionRequested e) =>
            string.IsNullOrEmpty(e.pointId) || e.text == "toggle" || e.text == "click" || e.text == "inspect" || !IsField(e.pointId);

        static bool IsField(string field) =>
            field.StartsWith("reading.") || field.StartsWith("newmid.") || field.StartsWith("submit.") || field == "decision"
            || field == SignField || field == SignStart || field == SignEnd || field == "toolcount_confirmed" || field.EndsWith("_signed")
            || field == AnomalyField || field == IrReadingField || field == PlatformCountField || field == IrJudgementField || field.StartsWith(PartTracePrefix);   // R1.5b b4

        /// <summary>R1.5b b4：CSR_01 b 異常欄（none／found，勾了就寫 env.work_order.anomaly.checked）、RT_02 c 絕緣電阻讀值（MΩ，和高阻計讀值比對）、WC_01b c 平台清點完成。</summary>
        public const string AnomalyField = "anomaly", IrReadingField = "ir_reading", PlatformCountField = "toolcount_platform_confirmed";
        /// <summary>D43：RT_02 c3 絕緣電阻判定（pass／fail）。勾錯：教學擋下並提示，練習／考核照寫並記 workorder.ir_judgement_wrong（ERR_RT02_WrongJudgement）。</summary>
        public const string IrJudgementField = "ir_judgement";
        /// <summary>
        /// CSR_10 g：part_trace.L／R＝抄了哪一條備品的標籤（值＝env.spare_shelf.slot_n）；和選用的那一條（spare.&lt;side&gt;.picked_slot）一致才寫 matches_spare_&lt;side&gt;＝true。
        /// shortcut: 料號、序號沒有分開存（一條備品的標籤＝一組料號＋序號）, 要練「抄錯其中一欄」時再拆成兩欄輸入
        /// </summary>
        public const string PartTracePrefix = "part_trace.", SpareSlotPrefix = "env.spare_shelf.slot_";
        public static string PartTraceMatchKey(string side) => "env.work_order.part_trace.matches_spare_" + side;

        static bool Tutorial(ISimState s) => s.Get("training.mode") == "tutorial";

        /// <summary>R1.5b b4（CSR_01 a，K7）：打開作業單＝逐項核對 SI：這門課所有 SI 步驟都完成才寫 si_all_done＝true（ERR_CSR01_IsolationIncomplete 讀這兩個鍵）。</summary>
        public static void CheckIsolation(ISimState s, PantographSim.Data.ProcedureDefinition prc)
        {
            if (prc == null || prc.steps == null) return;
            bool all = prc.steps.Where(x => x != null && x.stepId.StartsWith("STP_SI_")).All(x => s.GetBool("step." + x.stepId + ".done"));
            s.Batch(() => { s.SetBool("workorder.si_checked", true); s.SetBool("workorder.si_all_done", all); });
        }

        /// <summary>D43：勾選和高阻計讀值相符（≥ 下限＝pass）。</summary>
        public static bool JudgementCorrect(ISimState s, string value) => (value == "pass") == PantographSim.Data.MeggerLogic.Pass(s, PantographSim.Data.ToolRulesDef.Load());

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
                if (field.StartsWith(PartTracePrefix))
                {
                    if (!written.StartsWith(SpareSlotPrefix)) return false;
                    string side = field.Substring(PartTracePrefix.Length);
                    s.SetBool(PartTraceMatchKey(side), written == s.Get("spare." + side + ".picked_slot"));
                }
                if (field == AnomalyField) { if (written != "none" && written != "found") return false; s.SetBool("env.work_order.anomaly.checked", true); }
                if (field == IrJudgementField)
                {
                    if (written != "pass" && written != "fail") return false;
                    bool right = JudgementCorrect(s, written);
                    if (!right && Tutorial(s)) { s.Set("ui.toast", "低於 100 MΩ，不能申請復電，請回報作業負責人"); return false; }   // 教學擋下
                    s.SetBool("workorder.ir_judged", true);
                    if (!right) s.SetBool("workorder.ir_judgement_wrong", true);
                }
                if (field.StartsWith("reading.") || field.StartsWith("newmid.") || field == IrReadingField)
                {
                    if (!float.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { s.Set("workorder.last_error", field == IrReadingField ? "請輸入數字（MΩ）" : "請輸入數字（mm）"); return false; }
                    written = v.ToString("0.00", CultureInfo.InvariantCulture);
                    string readingKey = ReadingKeyFor(field);
                    if (readingKey != null && s.Has(readingKey))
                    {
                        bool mismatch = Mathf.Abs(s.GetFloat(readingKey) - v) > Tolerance + 1e-4f;
                        if (mismatch && field == IrReadingField && Tutorial(s)) { s.Set("ui.toast", "作業單讀值和高阻計顯示的不一樣，照高阻計讀值填"); return false; }   // D43：教學只提示
                        s.SetBool("workorder." + field + ".mismatch", mismatch);
                    }
                }
                s.Set("workorder." + field, written);
                if (field.StartsWith("submit.")) s.SetBool("workorder.reading." + field.Substring(7) + ".submitted", true);
                if (field.StartsWith("newmid.") && s.Has("workorder.newmid.L") && s.Has("workorder.newmid.R"))
                    s.SetFloat("workorder.height_diff", Mathf.Abs(s.GetFloat("workorder.newmid.L") - s.GetFloat("workorder.newmid.R")));
                return true;
            });
            if (ok) s.AfterCommit(() => bus?.Publish(new RecordWritten { targetId = SimIds.WorkOrder, pointId = field, text = written, time = SimClock.Now }));
            if (ok && Tutorial(s) && field.StartsWith("toolcount") && s.Get("hint.sub_target") != SimIds.WorkOrder)   // 小動作照順序：按了會記著，但前面的還沒做不會往下
            { s.Set("ui.toast", ""); s.Set("ui.toast", "已記下。這一步還要先：" + s.Get("hint.sub_text")); }
            return ok;
        }

        /// <summary>reading.L.left → ip.carbon_strip_L.measure_left.reading；newmid.R → ip.carbon_strip_R.measure_mid.new_reading。</summary>
        public static string ReadingKeyFor(string field)
        {
            if (field == IrReadingField) return "tool.megger.reading";
            var p = field.Split('.');
            if (p[0] == "reading" && p.Length == 3) return $"ip.carbon_strip_{p[1]}.measure_{p[2]}.reading";
            if (p[0] == "newmid" && p.Length == 2) return $"ip.carbon_strip_{p[1]}.measure_mid.new_reading";
            return null;
        }
    }
}
