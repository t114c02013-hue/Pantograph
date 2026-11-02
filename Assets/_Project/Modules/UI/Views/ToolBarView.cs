using System.Collections.Generic;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PantographSim.UI
{
    /// <summary>底部置中工具列：顯示工具架第幾格與名稱（按數字鍵或點這裡＝高亮掛位；點手上那件＝重設）；手上的工具加亮框，本步需要的閃爍。工具要到工具架拿取。</summary>
    public class ToolBarView : View
    {
        RectTransform root;
        readonly List<(string id, Image bg, Text num, Text name)> buttons = new List<(string, Image, Text, Text)>();
        string builtFor = "";
        string requiredTool = "";

        protected override void OnBuild(RectTransform safe)
        {
            var panel = Ui.Panel(safe, "ToolBar");
            root = Ui.Place(panel.rectTransform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(960, 104));
            var h = Ui.HStack(panel.gameObject, 6, 8);
            h.childAlignment = TextAnchor.MiddleCenter;
            if (Sim.Bus != null) Sim.Bus.Subscribe<StepStarted>(e => requiredTool = e.toolId ?? "");
        }

        void Build(string list)
        {
            foreach (Transform c in root) Object.Destroy(c.gameObject);
            buttons.Clear();
            Add("", "手", "放回");
            foreach (var entry in list.Split(';'))
            {
                var f = entry.Split('|');
                if (f.Length >= 3) Add(f[1], f[0], f[2]);
            }
            builtFor = list;
        }

        void Add(string toolId, string key, string name)
        {
            var b = Ui.Button(root, "", 16, () => { if (toolId == "") Ui.Send("tool.belt", "put_away"); else Ui.Send(toolId, "select"); });
            Ui.Size(b, 88, 88);
            Object.Destroy(Ui.LabelOf(b).gameObject);
            var num = Ui.Text(b.transform, key, 34, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
            Ui.Stretch(num.rectTransform, 2, 4, 2, 34);
            var nm = Ui.Text(b.transform, name, 16, Ui.Muted, TextAnchor.LowerCenter);
            Ui.Stretch(nm.rectTransform, 2, 50, 2, 6);
            nm.horizontalOverflow = HorizontalWrapMode.Overflow;
            buttons.Add((toolId, b.GetComponent<Image>(), num, nm));
        }

        public override void Refresh(ISimState s)
        {
            bool show = s.GetBool("training.running");
            Ui.SetActive(root, show);
            if (!show) return;
            var list = s.Get("tools.list");
            if (list != builtFor && list.Length > 0) Build(list);
            string active = s.Get(SimIds.ActiveTool);
            float blink = Ui.Blink(6f);
            foreach (var (id, bg, num, name) in buttons)
            {
                bool isActive = id == active;
                string need = s.Get("hint.sub_tool"); if (string.IsNullOrEmpty(need)) need = requiredTool;
                bool needed = !string.IsNullOrEmpty(need) && id == need && !isActive;
                bg.color = isActive ? Ui.ButtonOn : needed ? Color.Lerp(Ui.ButtonBg, new Color(1f, 0.85f, 0.1f), blink) : Ui.ButtonBg;
                name.color = isActive || needed ? Color.white : Ui.Muted;
            }
        }
    }
}
