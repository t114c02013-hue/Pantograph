using System.Linq;
using PantographSim.Core;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4：清點（WC_01a、CSR_15 f）。哪些掛位要清點讀 ToolSlotTable（homeStorageId＝env.tool_bag 的都算），加工具只改資料。
    /// ・逐格注視：工具袋某格被指著（ui.hover_id＝那一格，桌面滑鼠、VR 射線都寫這個鍵）就記 env.tool_bag.slot_k.viewed；
    ///   只要有工具不在工具袋裡就全部清掉——清點要在最後一件收回之後做。
    /// ・台架工具盤（env.panto_stand.tool_tray）：放在上面的工具掛位是 on_tray，count＝幾件。
    /// ・車頂遺留 roof.leftover_count＝轉到脫出還沒收的螺栓數；待報廢箱 env.parts_tray.strip_count_ok＝箱裡舊滑板數和 CSR_05 判定要換的條數相同。
    /// 純函式，工具袋元件（ToolBag）與測試共用；只在值改變時寫。
    /// </summary>
    public static class ToolCountLogic
    {
        public const string ToolBag = ToolSlotTable.ToolBag, ToolTray = "env.panto_stand.tool_tray", TrayCount = ToolTray + ".count";
        public const string DishesStowed = "roof.bolt_dishes_stowed";
        public const string Leftover = "roof.leftover_count", StripCountOk = "env.parts_tray.strip_count_ok", HoverKey = "ui.hover_id";
        public static string ViewedKey(int slot) => $"{ToolBag}.slot_{slot}.viewed";

        public static int[] BagSlots(ToolSlotTable t) => t == null ? new int[0] : t.slots.Where(x => x != null && x.homeStorageId == ToolBag).Select(x => x.slot).ToArray();

        /// <summary>依目前黑板更新清點用的鍵（工具袋元件在相關鍵改變時呼叫）。</summary>
        public static void Update(ISimState s, ToolSlotTable table) => s.Batch(() =>
        {
            var bag = BagSlots(table);
            bool allIn = bag.All(k => s.Get(RackLogic.SlotKey(k), RackLogic.OnRack) == RackLogic.OnRack);
            string h = s.Get(HoverKey);
            foreach (var k in bag)
            {
                if (!allIn) { if (s.GetBool(ViewedKey(k))) s.SetBool(ViewedKey(k), false); }   // 有工具不在袋裡：清點重做
                else if (h == "env.tool_rack.slot_" + k) s.SetBool(ViewedKey(k), true);
            }
            if (table != null) Write(s, TrayCount, table.slots.Count(x => x != null && s.Get(RackLogic.SlotKey(x.slot)) == RackLogic.OnTray));
            Write(s, Leftover, s.Keys.Count(k => k.Contains(".bolt_") && k.EndsWith(".state") && s.Get(k) == "Out"));
            string d = s.Get("workorder.decision");   // CSR_05：兩條都換＝2、只換一條＝1、合格＝0
            bool ok = s.GetFloat("tray.old_strips") == (d == "both" ? 2 : d == "L" || d == "R" ? 1 : 0);
            if (!s.Has(StripCountOk) || s.GetBool(StripCountOk) != ok) s.SetBool(StripCountOk, ok);
            // terry 2026-11-14（54A）：車頂螺栓盤（日常課程）兩個都收進工具袋了；別的課程沒有車頂螺栓盤＝true
            var rules = ToolRulesDef.Load();
            bool dishes = rules == null || !rules.BoltDishIn(s.Get(SimIds.CourseId, SimIds.CourseR1)) || (s.GetBool(SimIds.BoltTray("L") + ".stowed") && s.GetBool(SimIds.BoltTray("R") + ".stowed"));
            if (!s.Has(DishesStowed) || s.GetBool(DishesStowed) != dishes) s.SetBool(DishesStowed, dishes);
        });

        static void Write(ISimState s, string key, int n) { if (!s.Has(key) || (int)s.GetFloat(key) != n) s.SetFloat(key, n); }

        /// <summary>把手上的工具放到台架工具盤（掛位 on_tray，手空出來）。回傳 null＝成功。</summary>
        public static string PutOnTray(ISimState s, ToolSlotTable table)
        {
            string active = s.Get(SimIds.ActiveTool);
            var def = table?.FindTool(active);
            if (def == null) return "手上沒有拿著工具";
            s.Batch(() => { s.Set(RackLogic.SlotKey(def.slot), RackLogic.OnTray); s.Set(SimIds.ActiveTool, ""); s.SetBool("tools.all_returned", false); });
            return null;
        }
    }
}
