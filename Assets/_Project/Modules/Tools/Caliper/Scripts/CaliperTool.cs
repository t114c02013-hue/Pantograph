using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Tools
{
    /// <summary>
    /// 數位卡尺：滾輪開合量爪（每格 1 mm）；量爪合攏時按 ZERO 歸零；
    /// 點量測點（黑板有 &lt;id&gt;.true 的目標）自動吸附並讀數：0.01 mm、±0.02 雜訊，未歸零 +0.30。
    /// 讀數只顯示在卡尺上（tool.caliper.display），由玩家自己抄到作業單。
    /// </summary>
    public class CaliperTool : ToolBase
    {
        int measureCount;

        public override bool CapturesScroll => true;

        public override void OnReset() => ToolResetLogic.Reset(State, ToolId);

        public override void OnSelected()
        {
            if (!State.Has(Key("jaw"))) State.SetFloat(Key("jaw"), 25f);
            UpdateDisplay();
        }

        public override void Tick()
        {
            float notches = ScrollNotches();
            if (notches == 0f) return;
            State.SetFloat(Key("jaw"), Mathf.Clamp(State.GetFloat(Key("jaw")) + notches, 0f, 150f));
            State.Set(Key("snapped"), "");
            UpdateDisplay();
        }

        public override void OnCommand(string command)
        {
            switch (command)
            {
                case "close": State.SetFloat(Key("jaw"), 0f); UpdateDisplay(); break;
                case "open": State.SetFloat(Key("jaw"), 25f); UpdateDisplay(); break;
                case "zero":
                    if (State.GetFloat(Key("jaw")) > 0.05f) { State.Set(Key("message"), "量爪要先合攏（按「合攏」或滾輪轉到 0）才能歸零"); return; }
                    State.Batch(() =>   // 歸零、訊息、顯示一批生效
                    {
                        State.SetBool(Key("zeroed"), true);
                        State.Set(Key("message"), "已歸零");
                        UpdateDisplay();
                    });
                    break;
            }
        }

        public override void OnClick(ActionRequested e)
        {
            var s = State;
            if (!s.Has(e.targetId + ".true")) return;
            if (!Ask(e.targetId, "measure")) return;
            measureCount++;
            float truth = s.GetFloat(e.targetId + ".true");
            bool zeroed = s.GetBool(Key("zeroed"));
            float reading = CaliperLogic.Reading(truth, zeroed, CaliperLogic.PieceRng((int)s.GetFloat("scenario.seed", 1), CaliperLogic.Piece(s, e.targetId)));
            // 一批：卡尺讀數／量爪／顯示，與量測點的讀數（MeasurementTaken 的接收者在同一批寫入）同時生效
            s.Batch(() =>
            {
                s.SetFloat(Key("jaw"), truth);
                s.SetFloat(Key("reading"), reading);
                s.Set(Key("snapped"), e.targetId);
                s.SetBool(Key("measured_any"), true);
                UpdateDisplay();
                Bus.Publish(new MeasurementTaken { targetId = e.targetId, toolId = ToolId, value = reading, text = CaliperLogic.Display(reading), time = SimClock.Now });
            });
        }

        void UpdateDisplay()
        {
            var s = State;
            float shown = !string.IsNullOrEmpty(s.Get(Key("snapped"))) ? s.GetFloat(Key("reading"))
                : s.GetFloat(Key("jaw")) + (s.GetBool(Key("zeroed")) ? 0f : CaliperLogic.UnzeroedOffsetMm);
            s.Set(Key("display"), CaliperLogic.Display(shown) + " mm");
        }
    }
}
