using System;
using PantographSim.Core;

namespace PantographSim.Data
{
    /// <summary>
    /// steps-v6 v3.10（D44、吊帶割傷故障 fault.sling_cut）：吊梁上的四條吊帶、割傷吊帶的停用（拆下、掛停用牌、放進停用箱）、備用吊帶（查看、拿、掛回同一號）、碰吊具的條件。
    /// ・開場（教官開了故障）：fault.sling_cut.where＝RM／IN（其他值＝隨機）、fault.sling_cut.index＝1–4（沒預設就隨機）。割傷從那一段才有：RM＝開場（SI_06 平台查看就看得到）、
    ///   IN＝到 IN_01 才有（SI_06 查看時四條都正常，吊運後在台架旁查看才發現），env.sling_&lt;index&gt;.cut＝true。
    /// ・碰吊具（查看吊鉤舌片、吊梁、吊帶，拆、掛吊帶）：緊急停止要按著——教學擋下並提示，練習／考核照做並記 ERR_Sling_TouchWithoutEStop（同一次查看 1 次）；
    ///   頭離吊鉤組外緣 ≥ inspectClearanceM、不在吊鉤組正下方（CraneMechanism 寫 part.crane.hook_block_clear、under_hook_clear），不符合一律不讓碰、不記錯。
    /// ・記錯的鍵寫成「先 true、這一批生效後再 false」的脈衝（每發生一次＝一次由不成立變成立，同一步重複發生由 ErrorDetector 累計次數）。
    /// 純函式，場景元件（Worksite.SlingRig）與測試共用；回傳 null＝成功，否則是提示文字（也寫進 ui.toast）。
    /// </summary>
    public static class SlingLogic
    {
        public const string Fault = "fault.sling_cut", Where = Fault + ".where", Index = Fault + ".index";
        public const string Prefix = "env.sling_", Rejected = "env.sling_rejected", Spare = "env.sling_spare", Tag = "env.tag_out_of_service";
        public const string Rack = "env.rigging_rack", Bin = "env.sling_reject_bin", Hook = "part.crane.hook", Beam = "env.lift_beam";
        public const string InHand = "in_hand", InBin = "in_bin", OnRack = "on_rack", OnBeam = "on_beam";
        public const string CutInUse = "rigging.cut_in_use", Reported = "rigging.cut_reported";
        /// <summary>記錯的脈衝鍵（ErrorRules.json 的 WhenCondition 讀）。</summary>
        public const string JudgedUsableKey = "rigging.cut_judged_usable", FalseRejectKey = "rigging.false_reject", UnreportedKey = "rigging.spare_taken_unreported",
            ReturnedToRackKey = "rigging.cut_returned_to_rack", TouchNoEStopKey = "rigging.touch_without_estop";
        public const string HookClearKey = "part.crane.hook_block_clear", UnderHookClearKey = "part.crane.under_hook_clear";
        public const int Count = 4;
        /// <summary>where＝IN 時割傷出現的步驟。</summary>
        public const string InStep = "STP_IN_01_RigOnStand";

        public static string Sling(int n) => Prefix + n;
        public static string CutKey(int n) => Sling(n) + ".cut";
        public static string DetachedKey(int n) => Sling(n) + ".detached";
        public static string ReplacedKey(int n) => Sling(n) + ".replaced";
        public static string TopEye(int n) => "ip.sling_" + n + ".top_eye";
        public static string TagPoint(int n) => "ip.sling_" + n + ".tag";
        public static string BeamEye(int n) => "ip.lifting_beam.eye_" + n;

        static bool Tutorial(ISimState s) => s.Get("training.mode") == "tutorial";
        static string Hint(ISimState s, string text) { s.Set("ui.toast", text); return text; }
        /// <summary>記錯的脈衝：寫 true，這一批生效後再寫回 false（在 SiteObject 的一批裡也不會被合併掉）。</summary>
        static void Pulse(ISimState s, string key) { s.SetBool(key, true); s.AfterCommit(() => s.SetBool(key, false)); }

        /// <summary>吊帶編號：env.sling_3 → 3；不是 1–Count 的吊帶（含 env.sling_spare）＝0。</summary>
        public static int Number(string id) =>
            id != null && id.StartsWith(Prefix) && int.TryParse(id.Substring(Prefix.Length), out int n) && n >= 1 && n <= Count ? n : 0;

        /// <summary>開場：故障開著才抽在哪一段、第幾條（已預設的照用，測試與重播固定），割傷那一條。</summary>
        public static void Setup(ISimState s, Random rng)
        {
            if (!s.GetBool(Fault)) return;
            s.Batch(() =>
            {
                string w = s.Get(Where);
                if (w != "RM" && w != "IN") s.Set(Where, rng.Next(2) == 0 ? "RM" : "IN");
                int idx = (int)s.GetFloat(Index);
                if (idx < 1 || idx > Count) { idx = rng.Next(1, Count + 1); s.SetFloat(Index, idx); }
                if (s.Get(Where) == "RM") s.SetBool(CutKey(idx), true);
                Recompute(s);
            });
        }

        /// <summary>每一步開始時呼叫：where＝IN 的割傷到 IN_01 才出現。</summary>
        public static void OnStepStarted(ISimState s, string stepId)
        {
            if (stepId != InStep || !s.GetBool(Fault) || s.Get(Where) != "IN") return;
            int idx = (int)s.GetFloat(Index);
            if (s.GetBool(ReplacedKey(idx))) return;
            s.Batch(() => { s.SetBool(CutKey(idx), true); Recompute(s); });
        }

        /// <summary>割傷吊帶還掛在吊梁上（停留點 2／3 吊掛指揮讀這個鍵）。</summary>
        public static void Recompute(ISimState s)
        {
            bool any = false;
            for (int n = 1; n <= Count; n++) any |= s.GetBool(CutKey(n)) && !s.GetBool(DetachedKey(n));
            if (s.GetBool(CutInUse) != any || !s.Has(CutInUse)) s.SetBool(CutInUse, any);
        }

        /// <summary>碰吊鉤組旁的吊具前：位置不對一律擋；緊急停止沒按——教學擋、練習／考核記錯放行。回傳 null＝可以碰。</summary>
        public static string Touch(ISimState s)
        {
            if ((s.Has(HookClearKey) && !s.GetBool(HookClearKey)) || (s.Has(UnderHookClearKey) && !s.GetBool(UnderHookClearKey)))
                return Hint(s, "離吊鉤組外緣 0.3 m 以上，頭和身體不要進到吊鉤組正下方");
            if (s.GetBool(SimIds.CraneEStop)) return null;
            if (Tutorial(s)) return Hint(s, "先按遙控器的緊急停止，吊鉤才不會突然動");
            Pulse(s, TouchNoEStopKey);
            return null;
        }

        /// <summary>只有吊鉤組旁的東西要先 Touch：吊鉤、吊梁、吊梁上的吊帶與它們的互動點（吊具架上的備用吊帶不用）。</summary>
        public static bool NearHook(string id) =>
            id == Hook || id == Beam || Number(id) > 0 || (id != null && (id.StartsWith("ip.sling_") || id.StartsWith("ip.lifting_beam.eye_")));

        /// <summary>吊帶判定（查看後的「可用／不可用」）。割傷判可用：教學擋下，練習／考核照記並記錯；沒破損判不可用：照記並記 Minor。</summary>
        public static string Verdict(ISimState s, string slingId, bool ok, RiggingDef def)
        {
            int n = Number(slingId);
            if (n == 0 && slingId != Spare) return null;
            bool defective = n > 0 && (s.GetBool(CutKey(n)) || (def != null && Array.IndexOf(def.defectiveSlings, n) >= 0));
            if (defective && ok && Tutorial(s)) return Hint(s, "這條吊帶有割傷，不能用：判「不可用」");
            s.Batch(() =>
            {
                s.Set(slingId + ".verdict", ok ? "ok" : "ng");
                s.SetBool(slingId + ".verdict_correct", ok != defective);
                s.Set(InspectLogic.SlingVerdictUi, "");
            });
            if (defective && ok) Pulse(s, JudgedUsableKey);
            if (!defective && !ok) Pulse(s, FalseRejectKey);
            return null;
        }

        /// <summary>s1：從吊梁取下割傷吊帶（點吊帶上端吊耳）。</summary>
        public static string Detach(ISimState s, int n)
        {
            if (!s.GetBool(CutKey(n)) || s.GetBool(DetachedKey(n))) return Hint(s, "這條吊帶沒有破損，不用拆");
            var block = Touch(s); if (block != null) return block;
            s.Batch(() => { s.SetBool(DetachedKey(n), true); s.Set(Rejected + ".state", InHand); s.SetFloat(Rejected + ".from", n); Recompute(s); });
            return null;
        }

        /// <summary>到吊具架拿停用牌（點停用牌）。</summary>
        public static string TakeTag(ISimState s)
        {
            if (s.GetBool(Rejected + ".tagged")) return Hint(s, "停用牌已經掛上了");
            s.SetBool(Tag + ".in_hand", true);
            return null;
        }

        /// <summary>s1：在拆下的吊帶標籤處掛停用牌。</summary>
        public static string HangTag(ISimState s, int n)
        {
            if (s.Get(Rejected + ".state") != InHand || (int)s.GetFloat(Rejected + ".from") != n) return Hint(s, "先把割傷的吊帶從吊梁取下");
            if (!s.GetBool(Tag + ".in_hand")) return Hint(s, "先到吊具架拿停用牌");
            s.Batch(() => { s.SetBool(Rejected + ".tagged", true); s.SetBool(Tag + ".in_hand", false); });
            return null;
        }

        /// <summary>s1：拿著的割傷吊帶放進停用箱（箱口 trigger 或點停用箱）。</summary>
        public static string PutInBin(ISimState s)
        {
            if (s.Get(Rejected + ".state") != InHand) return Hint(s, "手上沒有要停用的吊帶");
            if (!s.GetBool(Rejected + ".tagged")) return Hint(s, "先掛停用牌，再放進停用箱");
            s.Set(Rejected + ".state", InBin);
            return null;
        }

        /// <summary>點吊具架：拿著割傷吊帶＝放回吊具架（錯：教學擋，練習／考核照放並記錯，可以再從架上拿回來）；割傷吊帶在架上＝拿回來。</summary>
        public static string RackClick(ISimState s)
        {
            string st = s.Get(Rejected + ".state");
            if (st == OnRack) { s.Set(Rejected + ".state", InHand); return null; }
            if (st != InHand) return null;
            if (Tutorial(s)) return Hint(s, "割傷的吊帶不能放回吊具架：掛停用牌，放進停用箱");
            s.Set(Rejected + ".state", OnRack);
            Pulse(s, ReturnedToRackKey);
            return null;
        }

        /// <summary>s3：從吊具架拿備用吊帶。還沒回報作業負責人（s2）：教學擋，練習／考核照拿並記錯。</summary>
        public static string TakeSpare(ISimState s)
        {
            if (s.Get(Spare + ".state", OnRack) != OnRack) return null;
            if (!s.GetBool(Reported))
            {
                if (Tutorial(s)) return Hint(s, "先回報作業負責人：吊帶割傷停用");
                Pulse(s, UnreportedKey);
            }
            s.Set(Spare + ".state", InHand);
            return null;
        }

        /// <summary>s4：備用吊帶掛到吊梁原本那一號的吊耳；掛上後接手 env.sling_&lt;n&gt;（割傷已拆下）。</summary>
        public static string HangSpare(ISimState s, int n)
        {
            if (s.Get(Spare + ".state") != InHand) return Hint(s, "先到吊具架拿備用吊帶");
            if (!s.GetBool(DetachedKey(n)) || s.GetBool(ReplacedKey(n))) return Hint(s, "掛到拆下割傷吊帶的那一號吊耳");
            if (s.Get(Spare + ".verdict") != "ok") return Hint(s, "先查看備用吊帶的標籤和破損，判「可用」再掛");
            var block = Touch(s); if (block != null) return block;
            s.Batch(() =>
            {
                s.Set(Spare + ".state", OnBeam);
                s.SetBool(CutKey(n), false); s.SetBool(DetachedKey(n), false); s.SetBool(ReplacedKey(n), true);
                s.Set(Sling(n) + ".verdict", "ok"); s.SetBool(Sling(n) + ".verdict_correct", true);
                Recompute(s);
            });
            return null;
        }
    }
}
