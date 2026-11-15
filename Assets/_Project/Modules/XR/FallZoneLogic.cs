using System.Linq;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>terry 134B／185（2026-11-15）：頭的地面投影在車頂的哪一區。</summary>
    public enum FallZone { Normal, Warn, OverPlatform, FallZone, Beyond }

    /// <summary>
    /// 車頂墜落區（純邏輯，比照 BoundaryLogic；EditMode 測）。只在目前站點是車頂時判斷。
    /// 墜落區連續停留 ≥ fallDwellSec 記錯；越過推回線立刻記錯並要求淡出推回；同一步只記一次（兩種共用）；
    /// 傳送落地後、淡出推回後 teleportGraceSec 內、代做、重播、桌機都不判也不計時。
    /// </summary>
    public class FallZoneLogic
    {
        public struct Result { public bool write, pushBack, haptic; public string toast; public FallZone zone; }

        readonly BoundaryDef def;
        FallZone last = FallZone.Normal;
        float dwell, graceUntil = float.NegativeInfinity;
        string writtenStep;

        public FallZoneLogic(BoundaryDef d) { def = d; }

        public static FallZone Classify(float x, float z, BoundaryDef d)
        {
            float ax = Mathf.Abs(x), e = d.roofEdge;
            if (ax <= e - d.fallWarnBand) return FallZone.Normal;
            if (ax <= e) return FallZone.Warn;
            if ((d.platformRects ?? new Rect[0]).Any(r => x >= r.xMin && x <= r.xMax && z >= r.yMin && z <= r.yMax)) return FallZone.OverPlatform;
            return ax <= e + d.fallPushOutsideEdge ? FallZone.FallZone : FallZone.Beyond;
        }

        /// <summary>傳送落地（XrTeleport.Teleported）：之後 teleportGraceSec 內不判。</summary>
        public void Teleported(float now) { graceUntil = now + def.teleportGraceSec; dwell = 0f; }

        /// <summary>每個 tick：頭的 X、Z，目前是不是在車頂站點，現在步驟，是否不判（代做、重播、桌機）。</summary>
        public Result Tick(float now, float dt, float x, float z, bool onRoof, string step, bool suppressed)
        {
            var r = new Result { zone = FallZone.Normal };
            if (!onRoof || suppressed || now < graceUntil) { dwell = 0f; last = FallZone.Normal; return r; }
            var zone = r.zone = Classify(x, z, def);
            bool entered = zone != last;
            last = zone;
            bool canWrite = writtenStep != step;
            if (zone != FallZone.FallZone) dwell = 0f;
            switch (zone)
            {
                case FallZone.Warn:
                    if (entered) { r.toast = def.fallWarnText; r.haptic = true; }
                    break;
                case FallZone.OverPlatform:
                    if (entered) r.toast = def.offRoofText;
                    break;
                case FallZone.FallZone:
                    if (entered) r.toast = def.fallZoneText;
                    dwell += dt;
                    if (dwell >= def.fallDwellSec - 1e-5f && canWrite) { r.write = true; writtenStep = step; }
                    break;
                case FallZone.Beyond:
                    r.toast = def.fallZoneText;
                    if (canWrite) { r.write = true; writtenStep = step; }
                    r.pushBack = true;
                    graceUntil = now + def.teleportGraceSec;   // 推回後一小段時間不判
                    last = FallZone.Normal;
                    break;
            }
            return r;
        }

        /// <summary>淡出推回：XR Origin 要水平移動多少，讓頭回到 |X| ＝ RoofWalkLimitAt(z)，Z 不變。</summary>
        public static Vector3 PushBack(Vector3 head, BoundaryDef d) => new Vector3(Mathf.Sign(head.x) * d.RoofWalkLimitAt(head.z) - head.x, 0f, 0f);
    }
}
