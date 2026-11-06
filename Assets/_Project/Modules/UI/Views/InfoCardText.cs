using System.Collections.Generic;
using System.Text;
using PantographSim.Core;

namespace PantographSim.UI
{
    /// <summary>
    /// 指到物件時的資訊卡文字（桌面滑鼠與 VR 右手射線共用）。只顯示學員自己看得到、量得到的資訊：
    /// 不顯示裂紋、對錯、新品標準值、實際扭力值或判定。文字取自黑板（零件／工具寫入的狀態）與場景物件名稱。
    /// </summary>
    public static class InfoCardText
    {
        static readonly Dictionary<string, string> Purpose = new Dictionary<string, string>
        {
            { SimIds.PanHead, "扳機點一下＝親眼確認弓頭已降下" },
            { SimIds.GroundTerminal, "接地棒先夾在這裡（平台側）" },
            { SimIds.GroundClamp, "驗電、掛接地棒的弓頭端" },
            { SimIds.VoltageTester, "驗電器先在這裡自檢" },
            { SimIds.WorkOrder, "記錄讀數、判定與清點" },
            { SimIds.PartsTray, "放拆下的螺栓與舊滑板" },
            { "env.tool_rack", "九個掛位：握把拿起，在掛位前放開＝歸還" },
            { "ip.depot.platform_anchor", "安全帶掛點（平台）" },
            { "ip.roof.anchor", "安全帶掛點（車頂）" },
        };

        public static string Build(ISimState s, SceneRegistry reg, string id)
        {
            if (string.IsNullOrEmpty(id) || s == null) return "";
            string name = ShortName(reg != null ? reg.NameOf(id) : id);
            if (id.StartsWith("env.spare_shelf.slot_")) return Spare(s, id);
            if (id.StartsWith("part.carbon_strip_")) return Strip(s, id);
            if (SimIds.IsBoltId(id)) return Bolt(s, id);
            if (id.StartsWith("env.tool_rack.slot_")) return RackSlot(s, id);
            if (id == "ip.cab.panel") return ControlPanel(s);
            if (id == "ip.cab.pan_indicator")
                return Title("弓位指示燈") + "\n扳機點一下＝查看指示燈" + (s.GetBool(id + ".checked") ? "\n已查看：" + s.Get(id + ".shows") : "");
            if (id.EndsWith(".bracket")) return Title(name) + "\n裝滑板的托架；換新滑板前要擦乾淨";
            if (id.Contains(".measure_")) return Title(name) + "\n卡尺量測點";
            return Purpose.TryGetValue(id, out var p) ? Title(name) + "\n" + p : Title(name);
        }

        static string Title(string t) => "<b>" + t + "</b>";

        static string Spare(ISimState s, string id)
        {
            var sb = new StringBuilder(Title("碳滑板（備品）"));
            if (s.GetBool(id + ".taken")) return sb.Append("\n已取用").ToString();
            sb.Append("\n型號 ").Append(s.Get(id + ".model"));
            sb.Append(s.GetBool(id + ".measured") ? $"\n中點厚度 {s.GetFloat(id + ".reading"):0.00} mm" : "\n中點厚度：還沒量");
            sb.Append(s.GetBool(id + ".flipped") ? "\n已翻面檢查" : "\n還沒翻面檢查");
            if (s.Get("training.mode") != "practice" && s.Has("spare.required_model"))
                sb.Append("\n<color=#B8C4D4>作業單要求型號 ").Append(s.Get("spare.required_model")).Append("</color>");
            return sb.ToString();
        }

        static string Strip(ISimState s, string id)
        {
            string side = SimIds.SideOf(id);
            string st = s.Get(id + ".state", "OldMounted");
            var sb = new StringBuilder(Title((side == "L" ? "靠關節" : "外側") + "碳滑板"));
            sb.Append(st == "OldMounted" || st == "NewMounted" ? "\n已安裝" : "\n已拆下");
            var reads = new List<string>();
            foreach (var (p, label) in new[] { ("left", "左"), ("mid", "中"), ("right", "右") })
            {
                string key = SimIds.Point(side, "measure_" + p);
                bool isNew = st == "NewMounted";
                if (s.GetBool(key + (isNew ? ".new_measured" : ".measured"))) reads.Add($"{label} {s.GetFloat(key + (isNew ? ".new_reading" : ".reading")):0.00}");
            }
            sb.Append(reads.Count > 0 ? "\n已量：" + string.Join("　", reads) + " mm" : "\n還沒量");
            return sb.ToString();
        }

        static string Bolt(ISimState s, string id)
        {
            string side = SimIds.SideOf(id);
            string n = id.Substring(id.Length - 1);
            var sb = new StringBuilder(Title($"螺栓 {n}（{(side == "L" ? "靠關節" : "外側")}）"));
            sb.Append("\nM8×25（13 mm）");
            sb.Append("\n狀態：").Append(BoltState(s.Get(id + ".state", "Tight")));
            sb.Append(s.GetBool(id + ".marked") ? "\n有防鬆標記" : "\n沒有防鬆標記");
            return sb.ToString();
        }

        public static string BoltState(string st)
        {
            switch (st)
            {
                case "Tight": return "鎖緊"; case "Loosened": return "已鬆開"; case "Out": return "已脫出"; case "InTray": return "在零件盒";
                case "Placed": return "已放回"; case "Snug": return "已預鎖"; case "Torqued": return "已鎖到扭力"; default: return st;
            }
        }

        static string RackSlot(ISimState s, string id)
        {
            string k = id.Substring("env.tool_rack.slot_".Length);
            string toolId = "", toolName = "";
            foreach (var entry in s.Get("tools.list").Split(';'))
            {
                var f = entry.Split('|');
                if (f.Length >= 3 && f[0] == k) { toolId = f[1]; toolName = f[2]; }
            }
            if (InputMode.XrActive) return Title($"{k} {toolName}") + "\n" + s.Get("tools.purpose." + toolId).Split('。')[0];   // VR 指到工具：只看一行做什麼
            string st = s.Get("rack.slot_" + k, "on_rack");
            var sb = new StringBuilder(Title($"{k} {toolName}"));
            sb.Append(st == "on_rack" ? "\n在架" : st == "in_hand" ? "\n空（在你手上）" : "\n使用中");
            string purpose = s.Get("tools.purpose." + toolId);
            if (purpose.Length > 0) sb.Append("\n").Append(purpose);
            return sb.ToString();
        }

        static string ControlPanel(ISimState s)
        {
            bool breaker = s.Get("lockout.breaker") == "locked", raise = s.Get("lockout.raise_switch") == "locked";
            return Title("駕駛室控制盤")
                   + $"\n主斷路器：{(breaker ? "OFF　已上鎖" : "ON　未上鎖")}"
                   + $"\n升弓開關：{(raise ? "已上鎖" : "未上鎖")}"
                   + $"\n升弓氣缸電源：{(s.Get("cab.cylinder_power") == "off" ? "OFF" : "ON")}";
        }

        public static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int i = name.IndexOf('（');
            return i > 0 && name.EndsWith("）") && (name.Contains("點開") || name.Contains("點一下")) ? name.Substring(0, i) : name;
        }
    }
}
