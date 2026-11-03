using PantographSim.Core;

namespace PantographSim.Data
{
    /// <summary>
    /// 工具架與手上狀態（純邏輯；工具列、工具架、測試共用）。
    /// 掛位 env.tool_rack.slot_1..9 的狀態寫在黑板 rack.slot_&lt;k&gt;：on_rack（在架）／in_hand（拿在手上）／in_use（使用中：接地棒掛在場上、安全帶穿在身上）。
    /// 手上一次只能拿一件：tool.active；拿著工具又去拿別的 → 提示先放回，不算錯。
    /// </summary>
    public static class RackLogic
    {
        public const string OnRack = "on_rack", InHand = "in_hand", InUse = "in_use";
        /// <summary>R1.5b b3：在腰包（D30，上鎖掛牌組作業期間）、掛在防墜繫繩上垂著（車頂放開沒收回工具袋）、掉在地上（地面工作區放開）。</summary>
        public const string InPouch = "in_pouch", Hanging = "hanging", Dropped = "dropped";
        /// <summary>R1.5b b4：放在台架工具盤上（CSR_15 f 要全部收回工具袋）。</summary>
        public const string OnTray = "on_tray";

        public static string SlotKey(int slot) => "rack.slot_" + slot;
        public static string SlotId(int slot) => "env.tool_rack.slot_" + slot;

        public static void Initialize(ISimState s, int slot)
        {
            if (!s.Has(SlotKey(slot))) s.Set(SlotKey(slot), OnRack);
        }

        /// <summary>從掛位拿起工具。回傳 null 表示成功，否則是給玩家的提示。</summary>
        public static string TryPick(ISimState s, int slot, string toolId, string toolName, string activeName)
        {
            string active = s.Get(SimIds.ActiveTool);
            if (active == toolId) return null;
            if (!string.IsNullOrEmpty(active)) return $"手上已經拿著「{activeName}」，先把它放回工具架";
            if (!string.IsNullOrEmpty(s.Get("hand.item"))) return "手上拿著零件，先把它放好再拿工具";
            string st = s.Get(SlotKey(slot), OnRack);
            if (st != OnRack && st != Hanging && st != Dropped && st != OnTray) return $"「{toolName}」正在使用中，不在架上";   // 垂在繫繩上、掉在地上的工具可以撿回來
            // 一批寫入：手上、掛位、清點三個鍵同時生效，步驟判定與下一步的開始快照不會拍到只寫一半的狀態
            // （上一輪 WC_01 卡關就是快照拍到「手上空了、清點還是沒收回」）
            s.Batch(() =>
            {
                s.SetBool("tools.all_returned", false);
                s.Set(SlotKey(slot), InHand);
                s.Set(SimIds.ActiveTool, toolId);
            });
            return null;
        }

        /// <summary>把手上的工具（或使用中、已收回的工具）掛回掛位。canReturnInUse：使用中的工具現在可以收回（例：接地已拆除）。</summary>
        public static string TryReturn(ISimState s, int slot, string toolId, bool canReturnInUse)
        {
            string st = s.Get(SlotKey(slot), OnRack);
            if (s.Get(SimIds.ActiveTool) == toolId)
            {
                s.Batch(() =>   // 一批（見 TryPick）
                {
                    s.SetBool("tools.all_returned", true);
                    s.Set(SlotKey(slot), OnRack);
                    s.Set(SimIds.ActiveTool, "");
                });
                return null;
            }
            if (st == InUse && canReturnInUse && string.IsNullOrEmpty(s.Get(SimIds.ActiveTool)))
            {
                s.Set(SlotKey(slot), OnRack);
                return null;
            }
            return st == InUse ? "這件工具還在使用中（例如接地還掛著），不能收回" : null;
        }

        /// <summary>手上的工具收進腰包（D30）；回傳 null＝成功。</summary>
        public static string StowToPouch(ISimState s, int slot, string toolId)
        {
            if (s.Get(SimIds.ActiveTool) != toolId) return "手上沒有拿著這件工具";
            s.Batch(() => { s.SetBool("tools.all_returned", true); s.Set(SlotKey(slot), InPouch); s.Set(SimIds.ActiveTool, ""); });
            return null;
        }

        /// <summary>從腰包拿出（手要空著）。</summary>
        public static string TakeFromPouch(ISimState s, int slot, string toolId)
        {
            if (s.Get(SlotKey(slot)) != InPouch) return "腰包裡沒有這件工具";
            if (!string.IsNullOrEmpty(s.Get(SimIds.ActiveTool)) || !string.IsNullOrEmpty(s.Get("hand.item"))) return "手上已經拿著東西";
            s.Batch(() => { s.SetBool("tools.all_returned", false); s.Set(SlotKey(slot), InHand); s.Set(SimIds.ActiveTool, toolId); });
            return null;
        }

        /// <summary>在車頂放開沒收回工具袋（Hanging）或在地面放開（Dropped）：手空出來，工具不在存放處。</summary>
        public static void Release(ISimState s, int slot, bool onRoof)
        {
            s.Batch(() => { s.SetBool("tools.all_returned", false); s.Set(SlotKey(slot), onRoof ? Hanging : Dropped); s.Set(SimIds.ActiveTool, ""); });
        }

        /// <summary>工具留在場上使用（接地棒掛上後），手空出來。</summary>
        public static void Deploy(ISimState s, int slot)
        {
            s.Batch(() =>   // 一批（見 TryPick）
            {
                s.SetBool("tools.all_returned", true);
                s.Set(SlotKey(slot), InUse);
                s.Set(SimIds.ActiveTool, "");
            });
        }
    }
}
