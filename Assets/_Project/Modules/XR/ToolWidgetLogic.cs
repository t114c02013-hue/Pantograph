using System.Collections.Generic;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>
    /// T2-a 工具數值用手調的純邏輯（不碰場景，可在 EditMode 測試）。數值都是 EX，改這裡的預設或呼叫端傳入。
    /// </summary>
    public static class ToolWidgetLogic
    {
        public const float SocketDwellSeconds = 0.15f;   // 扳手頭碰套筒座停留多久才換
        public const float SocketTouchRadius = 0.035f;   // 套筒座膠囊半徑 1.5 cm＋扳手頭 2 cm
        public const float CaliperSnapRadius = 0.03f;    // 量測區靠近量測點 3 cm 內自動吸附
        public const float CaliperStableSeconds = 0.5f;  // 讀數穩定多久自動填入作業單
        public const float TorqueHoldDelay = 0.4f, TorqueRepeatPerSecond = 4f;

        /// <summary>撥桿：一次切換一次。</summary>
        public static string ToggleDir(string current) => current == "lock" ? "loosen" : "lock";

        /// <summary>離 tip 最近、且在 radius 內的點（沒有回傳空字串）。</summary>
        public static string Nearest(Vector3 tip, IEnumerable<KeyValuePair<string, Vector3>> points, float radius)
        {
            string best = ""; float bd = radius;
            foreach (var kv in points)
            {
                float d = Vector3.Distance(tip, kv.Value);
                if (d <= bd) { bd = d; best = kv.Key; }
            }
            return best;
        }
    }

    /// <summary>停留計時：同一個目標連續 seconds 秒才回傳一次（離開或換目標就重算）。套筒座 0.15 s、卡尺穩定 0.5 s 共用。</summary>
    public class DwellTimer
    {
        readonly float seconds;
        string current = "";
        float t;
        bool fired;

        public DwellTimer(float seconds) { this.seconds = seconds; }
        public string Current => current;

        /// <summary>每幀呼叫；回傳達到停留時間的目標（只回傳一次），其他時候回傳 null。</summary>
        public string Update(string touching, float dt)
        {
            touching = touching ?? "";
            if (touching != current) { current = touching; t = 0f; fired = false; return null; }
            if (current.Length == 0 || fired) return null;
            t += dt;
            if (t + 1e-5f < seconds) return null;
            fired = true;
            return current;
        }
    }

    /// <summary>± 鈕：按一下 1 格；按住超過 delay 後每秒 rate 格。</summary>
    public class HoldRepeater
    {
        readonly float delay, rate;
        bool was;
        float held, acc;

        public HoldRepeater(float delay = ToolWidgetLogic.TorqueHoldDelay, float rate = ToolWidgetLogic.TorqueRepeatPerSecond) { this.delay = delay; this.rate = rate; }

        /// <summary>每幀呼叫；回傳這一幀要送幾格。</summary>
        public int Update(bool pressed, float dt)
        {
            if (!pressed) { was = false; return 0; }
            if (!was) { was = true; held = 0f; acc = 0f; return 1; }
            held += dt;
            if (held <= delay) return 0;
            acc += Mathf.Min(dt, held - delay) * rate;
            int n = (int)acc;
            acc -= n;
            return n;
        }
    }
}
