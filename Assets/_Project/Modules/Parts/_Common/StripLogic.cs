using System.Linq;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// 可拆裝滑板的狀態機（純邏輯）：OldMounted →（垂直取出）Removed（拿在手上）→（點零件盒）InTray
    /// →（備品選定）Carried（拿在手上、跟著滑鼠）→（對準托架放下）NewMounted。
    /// 黑板鍵（S＝part.carbon_strip_L）：S.state、S.removal（vertical/pry）、S.remove_blocked、
    /// S.new_orientation（in/out）、S.install_result（OK / WrongOrientation / NotAligned）、S.install_attempts、S.over_bracket；
    /// 手上拿的零件記在 hand.item。
    /// </summary>
    public static class StripLogic
    {
        public const string OldMounted = "OldMounted", Removed = "Removed", InTray = "InTray", Carried = "Carried", NewMounted = "NewMounted";
        /// <summary>放下時孔位與托架的偏差在這個範圍內就吸附到正確位置（之後 install_check 量得 ≤ 0.5 cm）。</summary>
        public static float SnapTolerance => (ToolRulesDef.Load()?.stripSnapCm ?? 5f) / 100f;

        public static string Side(string stripId) => stripId.Substring(stripId.Length - 1);
        public static string BoltId(string stripId, int n) => "ip.carbon_strip_" + Side(stripId) + ".bolt_" + n;

        public static void Initialize(ISimState s, string stripId)
        {
            if (!s.Has(stripId + ".state")) s.Set(stripId + ".state", OldMounted);
        }

        public static bool BoltsRemoved(ISimState s, string stripId) =>
            Enumerable.Range(1, 4).All(n => { var st = s.Get(BoltId(stripId, n) + ".state", FastenerLogic.Tight); return st == FastenerLogic.Out || st == FastenerLogic.InTray; });

        /// <summary>取出舊滑板。method＝vertical（正確，拿到手上）或 pry（撬起，會讓托架變形，不執行）。</summary>
        public static bool Remove(ISimState s, IEventBus bus, string stripId, string method) => s.BatchResult(() => RemoveInBatch(s, stripId, method));

        static bool RemoveInBatch(ISimState s, string stripId, string method)
        {
            if (s.Get(stripId + ".state") != OldMounted) return false;
            if (!BoltsRemoved(s, stripId)) { s.Set(stripId + ".remove_blocked", "bolts"); return false; }
            if (!string.IsNullOrEmpty(s.Get(SimIds.ActiveTool))) { s.Set("ui.toast", "先把手上的工具收好，空手才能拿滑板"); return false; }
            s.Set(stripId + ".removal", method);
            if (method != "vertical") return false;
            s.Set(stripId + ".state", Removed);
            s.Set(stripId + ".removal_prompt", "false");
            s.Set("hand.item", stripId);
            return true;
        }

        /// <summary>手上的舊滑板放進零件盒。</summary>
        public static bool ToTray(ISimState s, IEventBus bus, string stripId) => s.BatchResult(() => ToTrayInBatch(s, bus, stripId));

        static bool ToTrayInBatch(ISimState s, IEventBus bus, string stripId)
        {
            if (s.Get(stripId + ".state") != Removed) return false;
            s.Set(stripId + ".state", InTray);
            if (s.Get("hand.item") == stripId) s.Set("hand.item", "");
            bus?.Publish(new ActionPerformed { targetId = SimIds.PartsTray, text = "add:strip", pointId = stripId, time = SimClock.Now });
            return true;
        }

        /// <summary>備品架選定新滑板後拿在手上；預設方向是反的（斜面朝內），要按 R 轉正。</summary>
        public static bool Carry(ISimState s, string stripId) => s.BatchResult(() => CarryInBatch(s, stripId));

        static bool CarryInBatch(ISimState s, string stripId)
        {
            if (s.Get(stripId + ".state") != InTray) return false;
            s.Set(stripId + ".state", Carried);
            s.Set(stripId + ".new_orientation", "in");
            s.Set("hand.item", stripId);
            return true;
        }

        public static bool Rotate(ISimState s, string stripId)
        {
            if (s.Get(stripId + ".state") != Carried) return false;
            s.Set(stripId + ".new_orientation", s.Get(stripId + ".new_orientation") == "out" ? "in" : "out");
            return true;
        }

        /// <summary>
        /// 放上托架：方向正確且孔位偏差在吸附範圍內才吸附；方向反了一律不吸附並記 WrongOrientation。
        /// 一批寫入：state、方向、結果、真實厚度，以及吸附成功時 onSnapped 寫的位置檢測（install_distance_cm）同時生效。
        /// </summary>
        public static bool Install(ISimState s, string stripId, float holeOffset, System.Action onSnapped = null) =>
            s.BatchResult(() =>
            {
                bool ok = InstallInBatch(s, stripId, holeOffset);
                if (ok) onSnapped?.Invoke();
                return ok;
            });

        static bool InstallInBatch(ISimState s, string stripId, float holeOffset)
        {
            if (s.Get(stripId + ".state") != Carried) return false;
            s.SetFloat(stripId + ".install_attempts", s.GetFloat(stripId + ".install_attempts") + 1);
            if (s.Get(stripId + ".new_orientation") != "out") { s.Set(stripId + ".install_result", "WrongOrientation"); return false; }
            if (holeOffset > SnapTolerance) { s.Set(stripId + ".install_result", "NotAligned"); return false; }
            s.Set(stripId + ".install_result", "OK");
            s.Set(stripId + ".state", NewMounted);
            if (s.Get("hand.item") == stripId) s.Set("hand.item", "");
            float t = s.GetFloat("spare." + Side(stripId) + ".true_mid", 12.0f);
            foreach (var p in new[] { "left", "mid", "right" }) s.SetFloat(stripId + ".true." + p, t);
            return true;
        }
    }
}
