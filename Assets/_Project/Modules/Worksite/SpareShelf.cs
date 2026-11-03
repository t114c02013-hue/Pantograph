using System.Globalization;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// 備品架上的一條備品滑板（env.spare_shelf.slot_1..3）。點它 → 檢視面板（看標籤、翻面、選用）。
    /// 選用前要先用卡尺量中點；型號不符或有裂紋都不能用。選用成功寫 spare.&lt;側&gt;.picked=true，由零件端接手。
    /// 換到外側（procedure.side=R）時備品架補貨。
    /// </summary>
    public class SpareShelf : SiteObject
    {
        public const string CorrectModel = "CS-1100-74A";
        public const float Nominal = 12.0f, Tolerance = 0.2f;   // 示例值

        [SerializeField] string modelCode = CorrectModel;
        [SerializeField] bool cracked;
        [SerializeField] float thicknessMm = Nominal;
        [SerializeField] Transform body;          // 翻面時旋轉
        [SerializeField] GameObject crackDecal;   // 背面裂紋

        public void Setup(string model, bool isCracked, float thickness, Transform b, GameObject crack)
        {
            modelCode = model; cracked = isCracked; thicknessMm = thickness; body = b; crackDecal = crack;
        }

        protected override void OnBound()
        {
            Restock();
            State.Set("spare.required_model", CorrectModel);   // 作業單要求的型號（教學模式資訊卡並排顯示）
        }

        /// <summary>補貨：換成這一側的排列（SpareArrangementDef，負責人裁定 2026-11-03：L slot_2 正確、R slot_3 正確；沒有資料就照場景設定）。</summary>
        void Restock(string side = "L")
        {
            // 只在載入課程時（CourseApplier 寫 course.id）換排列；R1 的 25 步照舊（第 5 部分換成日常課後一律用排列）
            var e = State.Has(CourseApplier.CourseKey) ? SpareArrangementDef.Load()?.Find(side, SlotNumber(id)) : null;
            if (e != null) { modelCode = e.model; cracked = e.cracked; }
            Stock(State, id, modelCode, thicknessMm);
            State.SetBool(id + ".cracked", cracked);
        }

        /// <summary>env.spare_shelf.slot_3 → 3。</summary>
        public static int SlotNumber(string slotId) => int.TryParse(slotId.Substring(slotId.LastIndexOf('_') + 1), out var n) ? n : 0;

        protected override bool Watches(string key) => key.StartsWith(id + ".") || key == "procedure.side";

        protected override void Refresh()
        {
            if (body != null)
            {
                body.gameObject.SetActive(!State.GetBool(id + ".taken"));
                body.localRotation = State.GetBool(id + ".flipped") ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity;
            }
            if (crackDecal != null) crackDecal.SetActive(cracked);
        }

        string lastSide;
        void LateUpdate()
        {
            if (State == null) return;
            var side = State.Get("procedure.side");
            if (side != lastSide) { if (!string.IsNullOrEmpty(lastSide) && side == "R") Restock("R"); lastSide = side; }
        }

        protected override void OnBareHand(ActionRequested e)
        {
            if (State.GetBool(id + ".taken")) return;
            switch (e.text)
            {
                case "flip":
                    State.Batch(() =>   // R1.5b b4（步驟計畫 v3.0）：CSR_10 b 判「本側選定的那一條」，不寫死 slot_2
                    {
                        State.SetBool(id + ".flipped", !State.GetBool(id + ".flipped"));
                        if (State.GetBool(id + ".flipped")) State.SetBool($"spare.{CurrentSide()}.selected.flipped", true);
                    });
                    break;
                case "pick":
                    // 同一批：選用成功就請零件端把新滑板拿到手上（RemovableBehaviour 處理 carry），不會出現「已選用、手上卻沒有」的中間狀態
                    if (Pick(State, id, CurrentSide(), modelCode, cracked))
                        Bus.Publish(new ActionPerformed { targetId = "part.carbon_strip_" + CurrentSide(), text = "carry", pointId = id, time = SimClock.Now });
                    break;
                case "close": State.Set("ui.spare_inspect", ""); break;
                default:
                    // 第一次看這一條（本側）：記入「看過幾條標籤」
                    string side = CurrentSide();
                    if (!State.GetBool($"{id}.viewed_{side}"))
                    {
                        State.SetBool($"{id}.viewed_{side}", true);
                        State.SetFloat($"spare.{side}.viewed_count", State.GetFloat($"spare.{side}.viewed_count") + 1);
                    }
                    State.Set("ui.spare_inspect", id);
                    State.Set("ui.spare_inspect.model", modelCode);
                    State.SetBool("ui.spare_inspect.crack_visible", cracked && State.GetBool(id + ".flipped"));
                    break;
            }
            if (e.text == "flip") State.SetBool("ui.spare_inspect.crack_visible", cracked && State.GetBool(id + ".flipped"));
        }

        string CurrentSide() { var s = State.Get("procedure.side"); return string.IsNullOrEmpty(s) ? "L" : s; }

        /// <summary>選用備品。結果寫 spare.&lt;side&gt;.pick_result：ok / not_measured / wrong_type / cracked。</summary>
        public static bool Pick(ISimState s, string slotId, string side, string model, bool isCracked) =>
            s.BatchResult(() => PickInBatch(s, slotId, side, model, isCracked));   // 結果、次數、選定一批生效

        static bool PickInBatch(ISimState s, string slotId, string side, string model, bool isCracked)
        {
            string key = "spare." + side;
            if (!string.IsNullOrEmpty(s.Get(SimIds.ActiveTool)) || !string.IsNullOrEmpty(s.Get("hand.item")))
            {
                s.Set("ui.toast", "手上拿著東西，先把工具放回工具架再拿新滑板");
                return false;
            }
            string result = model != CorrectModel ? "wrong_type" : isCracked ? "cracked" : !s.GetBool(slotId + ".measured") ? "not_measured" : "ok";
            s.Set(key + ".pick_result", result);
            s.Set(key + ".pick_attempt", slotId + "#" + s.GetFloat(key + ".pick_count"));
            s.SetFloat(key + ".pick_count", s.GetFloat(key + ".pick_count") + 1);
            if (result != "ok") return false;
            float reading = s.GetFloat(slotId + ".reading");
            s.SetBool(key + ".measured_ok", Mathf.Abs(reading - Nominal) <= Tolerance);
            s.SetFloat(key + ".true_mid", s.GetFloat(slotId + ".true", Nominal));
            s.SetBool(slotId + ".taken", true);
            s.SetBool(key + ".picked", true);
            s.Set("ui.spare_inspect", "");
            return true;
        }

        protected override void OnMeasured(MeasurementTaken e) => State.Batch(() =>
        {
            RecordMeasurement(State, id, e.value);
            State.SetBool($"spare.{CurrentSide()}.selected.measured", true);   // CSR_10 d 判本側選定的那一條
        });

        public static void RecordMeasurement(ISimState s, string slotId, float value) => s.Batch(() =>
        {
            s.SetFloat(slotId + ".reading", value);
            s.SetBool(slotId + ".measured", true);
        });

        /// <summary>補貨／初始化：寫入這條備品的真實厚度與型號（測試也用）。</summary>
        public static void Stock(ISimState s, string slotId, string model, float thickness) => s.Batch(() =>
        {
            s.SetFloat(slotId + ".true", thickness);
            s.Set(slotId + ".model", model);
            s.SetBool(slotId + ".flipped", false);
            s.SetBool(slotId + ".taken", false);
            s.SetBool(slotId + ".measured", false);
        });

        public override string ToString() => $"{id} {modelCode} {thicknessMm.ToString(CultureInfo.InvariantCulture)} {(cracked ? "裂紋" : "")}";
    }
}
