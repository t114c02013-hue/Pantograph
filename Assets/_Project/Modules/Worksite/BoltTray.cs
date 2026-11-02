using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// 車頂螺栓盤（env.bolt_tray_L／_R，磁性盤，放在共用車頂站點兩側）：收拆下的螺栓（CSR_07 b），裝回時從這裡拿（CSR_12 b）。
    /// 每側各自計數 env.bolt_tray_&lt;側&gt;.bolts；同時沿用 PartsTray 的合計（tray.bolts、tray.no_loose_bolts、tray.items），
    /// 所以預鎖「零件盒有螺栓才能放回」與 WC_01「沒有散落螺栓」的判定不變。零件盒（env.parts_tray）只收舊滑板。
    /// </summary>
    public class BoltTray : SiteObject
    {
        [SerializeField] string side = "L";
        [SerializeField] GameObject[] boltIcons = new GameObject[0];

        public void Setup(string s, GameObject[] icons) { side = s; boltIcons = icons; }

        public static string CountKey(string side) => SimIds.BoltTray(side) + ".bolts";

        protected override void OnBound()
        {
            if (!State.Has(CountKey(side))) State.SetFloat(CountKey(side), 0);
        }

        protected override bool Watches(string key) => key == CountKey(side);

        protected override void OnBareHand(ActionRequested e) =>
            Toast($"{(side == "L" ? "肘側" : "外側")}螺栓盤：{State.GetFloat(CountKey(side)):0} 支螺栓");

        protected override void OnToolAction(ActionPerformed e) => Apply(State, side, e.text, e.pointId);

        /// <summary>add:bolt／remove:bolt：該側計數 ±1，並更新合計（一批寫入）。</summary>
        public static void Apply(ISimState s, string side, string text, string item) => s.Batch(() =>
        {
            float n = s.GetFloat(CountKey(side));
            if (text == "add:bolt") s.SetFloat(CountKey(side), n + 1);
            else if (text == "remove:bolt") s.SetFloat(CountKey(side), Mathf.Max(0, n - 1));
            else return;
            PartsTray.Apply(s, text, item);
        });

        protected override void Refresh()
        {
            int n = (int)State.GetFloat(CountKey(side));
            for (int i = 0; i < boltIcons.Length; i++) if (boltIcons[i] != null) boltIcons[i].SetActive(i < n);
        }
    }
}
