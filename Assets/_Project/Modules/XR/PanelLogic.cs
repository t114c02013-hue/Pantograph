using System.Collections.Generic;
using UnityEngine;

namespace PantographSim.XR
{
    /// <summary>T3 面板擺放的純邏輯（EditMode 測試用）：放開後轉正面向頭、各站點記住位置、叫回眼前。</summary>
    public static class PanelLogic
    {
        /// <summary>只保留水平轉向、面向頭（Canvas 的 +Z 朝離開頭的方向，和 XrPanelLayout 一致）。</summary>
        public static Quaternion FaceHead(Vector3 panelPos, Vector3 headPos)
        {
            var d = panelPos - headPos; d.y = 0f;
            return Quaternion.LookRotation(d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward, Vector3.up);
        }

        /// <summary>叫回眼前：頭前 distance、在視線 ±maxAngle 內（index 依序往左右錯開），比眼睛低 0.15 m，面向頭。</summary>
        public static Pose Recall(Vector3 head, Vector3 headForward, int index, float distance, float maxAngle)
        {
            var f = headForward; f.y = 0f;
            f = f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized;
            float yaw = Mathf.Clamp((index % 2 == 0 ? -1f : 1f) * 15f * ((index + 1) / 2), -maxAngle + 5f, maxAngle - 5f);
            if (index == 0) yaw = -12f;
            var pos = head + Quaternion.Euler(0f, yaw, 0f) * f * distance + Vector3.down * 0.15f;
            return new Pose(pos, FaceHead(pos, head));
        }

        /// <summary>世界位置 → 相對站點（x,y,z 與水平轉向）。</summary>
        public static (Vector3 local, float yaw) ToLocal(Transform station, Vector3 pos, Quaternion rot) =>
            (Quaternion.Inverse(Quaternion.Euler(0f, station.eulerAngles.y, 0f)) * (pos - station.position), Mathf.DeltaAngle(station.eulerAngles.y, rot.eulerAngles.y));

        public static Pose FromLocal(Transform station, Vector3 local, float yaw)
        {
            var r = Quaternion.Euler(0f, station.eulerAngles.y, 0f);
            return new Pose(station.position + r * local, Quaternion.Euler(0f, station.eulerAngles.y + yaw, 0f));
        }
    }

    /// <summary>每塊面板各自記住相對每個站點的位置（整場保留，重做本步也不重設）。</summary>
    public class PanelMemory
    {
        readonly Dictionary<string, (Vector3 local, float yaw)> map = new Dictionary<string, (Vector3, float)>();
        static string Key(string panel, string station) => panel + "|" + station;
        public void Save(string panel, string station, Vector3 local, float yaw) => map[Key(panel, station)] = (local, yaw);
        public bool TryGet(string panel, string station, out Vector3 local, out float yaw)
        {
            bool ok = map.TryGetValue(Key(panel, station), out var v);
            local = v.local; yaw = v.yaw;
            return ok;
        }
    }
}
