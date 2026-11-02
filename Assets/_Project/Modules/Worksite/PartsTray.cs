using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// 零件盒：收拆下的螺栓與舊滑板，顯示數量。黑板 tray.bolts、tray.old_strips、tray.items（逗號分隔 ID）。
    /// 零件以 ActionPerformed(env.parts_tray, add:bolt / remove:bolt / add:strip) 告知。
    /// </summary>
    public class PartsTray : SiteObject
    {
        [SerializeField] GameObject[] boltIcons = new GameObject[0];
        [SerializeField] GameObject[] stripIcons = new GameObject[0];

        public void Setup(GameObject[] bolts, GameObject[] strips) { boltIcons = bolts; stripIcons = strips; }

        protected override void OnBound()
        {
            if (!State.Has("tray.bolts")) State.SetFloat("tray.bolts", 0);
            if (!State.Has("tray.old_strips")) State.SetFloat("tray.old_strips", 0);
            if (!State.Has("tray.no_loose_bolts")) State.SetBool("tray.no_loose_bolts", true);
        }

        protected override bool Watches(string key) => key.StartsWith("tray.");

        protected override void OnBareHand(ActionRequested e)
        {
            // 手上拿著拆下的舊滑板 → 放進零件盒（由零件自己更新狀態）
            string item = State.Get("hand.item");
            if (!string.IsNullOrEmpty(item) && e.text != "toggle")
            {
                Bus.Publish(new ActionPerformed { targetId = item, text = "to_tray", time = SimClock.Now });
                return;
            }
            State.SetBool("ui.tray_viewed", true);
            State.SetBool("ui.tray_open", !State.GetBool("ui.tray_open"));
        }

        protected override void OnToolAction(ActionPerformed e) => Apply(State, e.text, e.pointId);

        public static void Apply(ISimState s, string text, string item) => s.Batch(() => ApplyInBatch(s, text, item));   // 數量、清單、是否有散落螺栓一批生效

        static void ApplyInBatch(ISimState s, string text, string item)
        {
            var items = new List<string>((s.Get("tray.items") ?? "").Split(',').Where(x => x.Length > 0));
            switch (text)
            {
                case "add:bolt": s.SetFloat("tray.bolts", s.GetFloat("tray.bolts") + 1); items.Add(item); break;
                case "remove:bolt": s.SetFloat("tray.bolts", Mathf.Max(0, s.GetFloat("tray.bolts") - 1)); items.Remove(item); break;
                case "add:strip": s.SetFloat("tray.old_strips", s.GetFloat("tray.old_strips") + 1); items.Add(item); break;
                default: return;
            }
            s.Set("tray.items", string.Join(",", items));
            s.SetBool("tray.no_loose_bolts", s.GetFloat("tray.bolts") < 0.5f);
        }

        protected override void Refresh()
        {
            int bolts = (int)State.GetFloat("tray.bolts"), strips = (int)State.GetFloat("tray.old_strips");
            for (int i = 0; i < boltIcons.Length; i++) if (boltIcons[i] != null) boltIcons[i].SetActive(i < bolts);
            for (int i = 0; i < stripIcons.Length; i++) if (stripIcons[i] != null) stripIcons[i].SetActive(i < strips);
        }
    }
}
