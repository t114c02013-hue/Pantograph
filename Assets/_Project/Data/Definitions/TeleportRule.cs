using System;
using System.Linq;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>傳送限制的一列：condition 成立時，傳送到 stationPrefix 開頭的站點（空白＝所有站點）會被擋下並顯示 message；只擋不記錯。</summary>
    [Serializable]
    public class TeleportBlock
    {
        public string condition, stationPrefix, message;
        [Tooltip("R1.5b b4（D23）：成立時這一站也不出現在傳送點清單（例：日常更換的 tp.platform.crane）")] public bool hidden;
    }

    /// <summary>
    /// R1.5b b2 傳送限制（crane-software v0.5 §2.5，D18）：和 RoofAccessRule 放在一起，傳送拋物線、B 鍵、帶我到目標都讀這一份。
    /// 2.5 表：吊物懸空一律不能傳送；tp.stand.* 和吊回時的 tp.platform.crane 都是「吊物沒懸空就開放」，已由第一列涵蓋（資深軟體 A 審查 1）；
    /// 平台 ↔ 地面維修區只能傳送（相當於走樓梯），不是限制。之後的段落（吊運中車頂封閉、扣著車頂掛點）在這裡加列。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/R1/Teleport Rule")]
    public class TeleportRule : ScriptableObject
    {
        public TeleportBlock[] blocks =
        {
            new TeleportBlock { condition = "part.pan_assy.state=Lifted", stationPrefix = "", message = "吊物懸空時不能傳送，請用平滑移動走過去" },
        };

        /// <summary>D23：這一站現在不出現在傳送點清單（被 hidden 的列擋下）。</summary>
        public bool Hidden(PantographSim.Core.ISimState s, string stationId) =>
            s != null && blocks.Any(b => b != null && b.hidden && (stationId ?? "").StartsWith(b.stationPrefix ?? "") && !string.IsNullOrEmpty(b.stationPrefix) && PantographSim.Core.ChecklistEval.Done(s, b.condition));

        /// <summary>傳送到 stationId 被擋的原因；null＝可以去。</summary>
        public string BlockReason(PantographSim.Core.ISimState s, string stationId)
        {
            if (s == null) return null;
            foreach (var b in blocks)
                if (b != null && (string.IsNullOrEmpty(b.stationPrefix) || (stationId ?? "").StartsWith(b.stationPrefix)) && PantographSim.Core.ChecklistEval.Done(s, b.condition))
                    return b.message;
            return null;
        }
    }
}
