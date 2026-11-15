using System;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// terry 186（RA-21、RA-26）：頭有沒有穿進實心物體（純邏輯；物理查詢由呼叫端傳入，EditMode 測）。
    /// 球碰到 HeadSolid 表面＝在裡面；沒碰到時再看「從上一次確定在外面的位置走到現在」進了幾個面、出了幾個面（整顆頭進到空心車體裡，球碰不到表面）：
    /// 進的比出的多＝在裡面（從側面伸進去、再從車頂上方出來，進出各一，算出來了）。
    /// 人被移動（傳送、淡出推回、掉落救回）後，最後在外面的位置直接設成新的頭位置。
    /// </summary>
    public class HeadSolidLogic
    {
        readonly HeadSolidDef def;
        Vector3? lastClear;
        float graceUntil = float.NegativeInfinity, alpha;
        bool inside;

        public HeadSolidLogic(HeadSolidDef d) { def = d; }

        public float Alpha => alpha;
        public bool Inside => inside;
        public Vector3? LastClear => lastClear;

        /// <summary>人被移過去（不是走過去）：最後在外面的位置設成新的頭，graceSec 內不判。</summary>
        public void Moved(Vector3 head, float now) { lastClear = head; graceUntil = now + def.graceSec; inside = false; alpha = 0f; }

        /// <summary>
        /// 每個 tick。paused：有轉場淡黑或淡出推回正在進行（本功能讓出暗幕，不判不計時）；suppressed：代做、重播、桌機、開場還沒完成。
        /// touches(head)＝球碰到表面；hits(from, to)＝線段從外面打到幾個物體的正面（射線只打正面：反方向數的就是出來的面）。回傳這一 tick 要顯示的暗幕透明度。
        /// </summary>
        public float Tick(float now, float dt, Vector3 head, bool paused, bool suppressed, Func<Vector3, bool> touches, Func<Vector3, Vector3, int> hits)
        {
            if (suppressed || paused)
            {
                inside = false; alpha = 0f;
                if (paused) graceUntil = now + def.graceSec;   // 讓出之後從頭重算
                lastClear = head;
                return alpha;
            }
            if (now < graceUntil || def.Excluded(head)) { inside = false; lastClear = head; }
            else
            {
                inside = touches(head) || (lastClear.HasValue && hits(lastClear.Value, head) > hits(head, lastClear.Value));
                if (!inside) lastClear = head;
            }
            float rate = inside ? def.maxAlpha / Mathf.Max(def.fadeInSec, 1e-3f) : def.maxAlpha / Mathf.Max(def.fadeOutSec, 1e-3f);
            alpha = Mathf.MoveTowards(alpha, inside ? def.maxAlpha : 0f, rate * dt);
            return alpha;
        }

        /// <summary>提示要不要顯示：墜落區提示優先（目前在墜落區監看的任一提示區內就不顯示「請退後」）。</summary>
        public static bool ShowHint(bool inside, FallZone fallZone) => inside && fallZone == FallZone.Normal;
    }
}
