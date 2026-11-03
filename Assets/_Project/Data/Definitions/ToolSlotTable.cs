using System;
using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// 一個掛位（crane-software v0.5 §3.4，D17、D18、D20、D25、D26、D30）：掛位編號＝工具的 hotkey，homeStorageId＝它所屬的存放處
    /// （env.tool_bag 隨身工具袋、env.tool_rack 平台工具架、env.pole_rack 桿架）。InRack 的意思是「在它所屬的存放處」；清點件數從這張表算。
    /// </summary>
    [Serializable]
    public class ToolSlotDef
    {
        public int slot;
        public string toolId, homeStorageId;
        [Tooltip("這個掛位在哪門課存在：both／periodic／daily（例：大扭力扳手 slot_13 只有定期檢修）")] public string courses = "both";
        [Tooltip("D30：作業期間放在個人腰包 env.waist_pouch（上鎖掛牌組 slot_1：開始與結束在工具架，作業期間「在腰包」算收好）")] public bool workInPouch;
    }

    /// <summary>R1.5b b3：掛位表（唯一來源）。兩門課規則相同（D26），只有 courses 欄區分課程。</summary>
    [CreateAssetMenu(menuName = "PantographSim/Tool Slot Table")]
    public class ToolSlotTable : ScriptableObject
    {
        public const string ToolBag = "env.tool_bag", ToolRack = "env.tool_rack", PoleRack = "env.pole_rack", WaistPouch = "env.waist_pouch";

        public ToolSlotDef[] slots = Array.Empty<ToolSlotDef>();

        public ToolSlotDef Find(int slot) => slots.FirstOrDefault(s => s != null && s.slot == slot);
        public ToolSlotDef FindTool(string toolId) => slots.FirstOrDefault(s => s != null && s.toolId == toolId);

        public static bool InCourse(ToolSlotDef s, string course) => s.courses == "both" || string.IsNullOrEmpty(course) || s.courses == course;

        /// <summary>某門課、某個存放處的掛位（清點用）。includePouch＝false 時不含作業期間在腰包的掛位（WC_01b 只清點 4、11、12 和桿架）。</summary>
        public IEnumerable<ToolSlotDef> At(string storageId, string course, bool includePouch = true) =>
            slots.Where(s => s != null && s.homeStorageId == storageId && InCourse(s, course) && (includePouch || !s.workInPouch));
    }

    /// <summary>掛位狀態的判讀（純函式，清點、錯誤規則、測試共用）。</summary>
    public static class ToolStorageLogic
    {
        /// <summary>在它所屬的存放處（InRack）。</summary>
        public static bool InHome(ISimState s, int slot) => s.Get(RackLogic.SlotKey(slot), RackLogic.OnRack) == RackLogic.OnRack;

        /// <summary>收好：在所屬存放處，或（作業期間）在腰包。ERR_Tool_NotStowedOnRoof 不會因為「在腰包」觸發（D30）。</summary>
        public static bool Stowed(ISimState s, int slot)
        {
            var st = s.Get(RackLogic.SlotKey(slot), RackLogic.OnRack);
            return st == RackLogic.OnRack || st == RackLogic.InPouch;
        }

        /// <summary>清點：storageId 的掛位裡不在位的（空＝清點通過）。件數與掛位編號都從表算，不寫死。</summary>
        public static List<ToolSlotDef> Missing(ISimState s, ToolSlotTable t, string storageId, string course, bool includePouch = true) =>
            t.At(storageId, course, includePouch).Where(x => !InHome(s, x.slot)).ToList();
    }
}
