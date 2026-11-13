using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// 螺栓盤（env.bolt_tray_L／_R）：拆下的碳滑板螺栓（CSR_07 b）計數在這裡，裝回時從這裡扣（CSR_12 b）。
    /// terry 2026-11-13（收進隨身袋）：盤子在平台置物架，拆下的螺栓顯示在腰間工具袋的螺栓袋（boltIcons 放在 BoltBag），計數和判定不變。
    /// 每側各自計數 env.bolt_tray_&lt;側&gt;.bolts；同時沿用 PartsTray 的合計（tray.bolts、tray.no_loose_bolts、tray.items），
    /// 所以預鎖「零件盒有螺栓才能放回」與 WC_01「沒有散落螺栓」的判定不變。零件盒（env.parts_tray）只收舊滑板。
    /// </summary>
    /// terry 2026-11-13（16A～25B）：R1、日常課程可以點螺栓盤「倒進盤子清點」，再點一下「拿回螺栓袋」（一側 4 支一起）；
    /// 只是提示、不判定：沒倒也能繼續、不記錯，計數和 WC_01 判定不變。倒了顯示在盤子（trayIcons），沒倒顯示在袋口（boltIcons）。
    public class BoltTray : SiteObject
    {
        [SerializeField] string side = "L";
        [SerializeField] GameObject[] boltIcons = new GameObject[0];
        [SerializeField] GameObject[] trayIcons = new GameObject[0];

        public void Setup(string s, GameObject[] icons) { side = s; boltIcons = icons; }
        public void SetupTray(GameObject[] icons) => trayIcons = icons;

        public static string CountKey(string side) => SimIds.BoltTray(side) + ".bolts";
        /// <summary>true＝這側的螺栓倒在平台螺栓盤上（顯示用，不參與判定）。</summary>
        public static string PouredKey(string side) => SimIds.BoltTray(side) + ".poured";

        /// <summary>這門課有沒有「倒進盤子／拿回袋子」（ToolRulesDef.boltPourCourses）。</summary>
        public static bool PourEnabled(ISimState s, ToolRulesDef r) =>
            r != null && r.BoltPourIn(s.Get(SimIds.CourseId, SimIds.CourseR1));

        /// <summary>點一下：沒倒且有螺栓＝倒進盤子；倒了＝拿回袋子；回傳提示文字（課程沒有這個動作時回傳 null）。</summary>
        public static string Pour(ISimState s, string side, ToolRulesDef r)
        {
            if (!PourEnabled(s, r)) return null;
            float n = s.GetFloat(CountKey(side));
            bool poured = s.GetBool(PouredKey(side));
            if (!poured && n <= 0) return null;
            s.SetBool(PouredKey(side), !poured);
            return string.Format(poured ? r.boltTakeBackText : r.boltPourText, n);
        }

        protected override void OnBound()
        {
            if (!State.Has(CountKey(side))) State.SetFloat(CountKey(side), 0);
        }

        protected override bool Watches(string key) => key == CountKey(side) || key == PouredKey(side);

        protected override void OnBareHand(ActionRequested e)
        {
            var msg = Pour(State, side, ToolRulesDef.Load());
            Toast(msg ?? $"{(side == "L" ? "靠關節" : "外側")}螺栓：{(State.GetBool(PouredKey(side)) ? "螺栓盤上" : "腰間螺栓袋裡")} {State.GetFloat(CountKey(side)):0} 支");
        }

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
            bool poured = State.GetBool(PouredKey(side));
            for (int i = 0; i < boltIcons.Length; i++) if (boltIcons[i] != null) boltIcons[i].SetActive(!poured && i < n);
            for (int i = 0; i < trayIcons.Length; i++) if (trayIcons[i] != null) trayIcons[i].SetActive(poured && i < n);
        }
    }
}
