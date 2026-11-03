using System;
using System.Linq;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>一顆套筒（torque-wrench-sockets.md v1.3 §6.4、§7.2）。數值只寫在這裡；扳手的 ToolDefinition.socketIds 只放 socketId 參照。</summary>
    [Serializable]
    public class SocketDef
    {
        public string socketId, prefab;
        [Tooltip("方頭（吋）：0.375＝3/8\"、0.5＝1/2\"")] public float driveIn;
        public int sizeMm;
        [Tooltip("套筒高（套筒口到驅動端面）＝第 21 項把手高度的加數")] public float heightM;
        public float outerDiaM;
        public string source;
    }

    /// <summary>
    /// 螺栓要用幾 mm 的套筒（R1.5b b3：FastenerDef.socketId 改成 sizeMm，只寫尺寸、不寫哪一顆套筒；只用來判斷對錯，不會自動幫學員套上）。
    /// 例：碳滑板 M8＝13，套筒扳手的 socket.13（1/2"）和小扭力扳手的 socket.13_38（3/8"）都對。pattern 支援 *。
    /// </summary>
    [Serializable]
    public class FastenerSocket
    {
        public string fastenerPattern;
        public int sizeMm;
        [Tooltip("尺寸是否推估（EX）")] public bool estimated;
    }

    /// <summary>a6 增補 7（D39）：套筒目錄。兩支扳手共用的 socket.19／24 數值只寫一次，不會兩邊不一致。</summary>
    [CreateAssetMenu(menuName = "PantographSim/Socket Catalog")]
    public class SocketCatalog : ScriptableObject
    {
        public SocketDef[] sockets = Array.Empty<SocketDef>();
        public FastenerSocket[] fasteners = Array.Empty<FastenerSocket>();

        public SocketDef Find(string socketId) => Array.Find(sockets, s => s != null && s.socketId == socketId);

        /// <summary>螺栓要用的套筒尺寸（第一筆 pattern 相符的；0＝沒有對照）。</summary>
        public int SizeFor(string fastenerId) => fasteners?.FirstOrDefault(f => f != null && Match(f.fastenerPattern, fastenerId))?.sizeMm ?? 0;

        /// <summary>這支扳手能裝的套筒裡，尺寸＝sizeMm 的那一顆（第 21 項查套筒高）。</summary>
        public SocketDef ForTool(ToolDefinition tool, int sizeMm) => tool == null ? null : tool.socketIds.Select(Find).FirstOrDefault(s => s != null && s.sizeMm == sizeMm);

        public static bool Match(string pattern, string id)
        {
            if (string.IsNullOrEmpty(pattern) || id == null) return false;
            var parts = pattern.Split('*');
            int at = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                int k = id.IndexOf(parts[i], at, StringComparison.Ordinal);
                if (k < 0 || (i == 0 && k != 0)) return false;
                at = k + parts[i].Length;
            }
            return parts[parts.Length - 1].Length == 0 || id.EndsWith(parts[parts.Length - 1], StringComparison.Ordinal);
        }
    }
}
