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
        /// <summary>true＝倒過之後又拿回螺栓袋（選做小動作「拿回」的完成條件）。</summary>
        public static string TakenBackKey(string side) => SimIds.BoltTray(side) + ".taken_back";
        /// <summary>terry 2026-11-14（47B、53A、55A）：日常課程的車頂螺栓盤已收進工具袋（收走後不能再倒；工具袋側邊顯示小圓盤）。</summary>
        public static string StowedKey(string side) => SimIds.BoltTray(side) + ".stowed";

        [SerializeField] GameObject bagDish;
        public void SetupBagDish(GameObject g) => bagDish = g;
        [SerializeField] Transform dishBody;   // 車頂小零件盤的盤身（繫繩、扣環不在裡面：抓起來＝解開，留在墊子上）
        public void SetupDishBody(Transform t) => dishBody = t;
        Vector3 dishHome;

        /// <summary>這門課的螺栓盤是不是車頂小零件盤（ToolRulesDef.boltDishCourses）。</summary>
        public static bool DishCourse(ISimState s, ToolRulesDef r) => r != null && r.BoltDishIn(s.Get(SimIds.CourseId, SimIds.CourseR1));
        static bool HasBolts(ISimState s, string side) => s.GetBool(PouredKey(side)) && s.GetFloat(CountKey(side)) > 0;

        /// <summary>52B、63A：握把抓起車頂螺栓盤（手要空著、盤子要空）。回傳提示（null＝這門課不能抓）。</summary>
        public static string Grab(ISimState s, string side, ToolRulesDef r)
        {
            if (!DishCourse(s, r) || s.GetBool(StowedKey(side))) return null;
            if (!string.IsNullOrEmpty(s.Get(SimIds.ActiveTool)) || !string.IsNullOrEmpty(s.Get("hand.item"))) return "手上已經拿著東西";
            if (HasBolts(s, side)) return r.boltDishNotEmptyText;
            s.Batch(() => { s.SetBool(PouredKey(side), false); s.Set("hand.item", SimIds.BoltTray(side)); });
            return r.boltDishGrabText;
        }

        /// <summary>61A、62A：放開——靠近工具袋＝收進去；別處＝回到暫放墊原位並提示。</summary>
        public static string Release(ISimState s, string side, ToolRulesDef r, bool nearBag)
        {
            if (s.Get("hand.item") != SimIds.BoltTray(side)) return null;
            s.Batch(() => { s.Set("hand.item", ""); if (nearBag) s.SetBool(StowedKey(side), true); });
            return nearBag ? r.boltDishStowText : r.boltDishBackText;
        }

        /// <summary>66A、67A：桌機點一下／後台代做直接收進工具袋（盤子要空）。</summary>
        public static string Stow(ISimState s, string side, ToolRulesDef r)
        {
            if (!DishCourse(s, r) || s.GetBool(StowedKey(side))) return null;
            if (HasBolts(s, side)) return r.boltDishNotEmptyText;
            s.Batch(() => { s.SetBool(PouredKey(side), false); s.SetBool(StowedKey(side), true); });
            return r.boltDishStowText;
        }

        /// <summary>這門課有沒有「倒進盤子／拿回袋子」（ToolRulesDef.boltPourCourses）。</summary>
        public static bool PourEnabled(ISimState s, ToolRulesDef r) =>
            r != null && r.BoltPourIn(s.Get(SimIds.CourseId, SimIds.CourseR1));

        /// <summary>點一下：沒倒且有螺栓＝倒進盤子；倒了＝拿回袋子；回傳提示文字（課程沒有這個動作時回傳 null）。</summary>
        public static string Pour(ISimState s, string side, ToolRulesDef r)
        {
            if (!PourEnabled(s, r) || s.GetBool(StowedKey(side))) return null;
            float n = s.GetFloat(CountKey(side));
            bool poured = s.GetBool(PouredKey(side));
            if (!poured && n <= 0) return null;
            s.Batch(() => { s.SetBool(PouredKey(side), !poured); s.SetBool(TakenBackKey(side), poured); });
            return string.Format(poured ? r.boltTakeBackText : r.boltPourText, n);
        }

        protected override void OnBound()
        {
            if (dishBody != null) dishHome = dishBody.localPosition;
            if (!State.Has(CountKey(side))) State.SetFloat(CountKey(side), 0);
        }

        protected override bool Watches(string key) => key == CountKey(side) || key == PouredKey(side) || key == StowedKey(side) || key == "hand.item";

        protected override void OnBareHand(ActionRequested e)
        {
            var r = ToolRulesDef.Load();
            switch (e.text)
            {
                case "grab": ToastIf(Grab(State, side, r)); return;
                case "release:bag": ToastIf(Release(State, side, r, true)); return;
                case "release:other": ToastIf(Release(State, side, r, false)); return;
                case "stow": ToastIf(Stow(State, side, r)); return;
            }
            if (State.GetBool(StowedKey(side))) return;   // 55A：收走之後點了沒反應
            var msg = Pour(State, side, r);
            if (msg == null && DishCourse(State, r) && !InputMode.XrActive && State.GetFloat(CountKey(side)) <= 0) msg = Stow(State, side, r);   // 66A：桌機點空盤子＝收進工具袋
            Toast(msg ?? $"{(side == "L" ? "靠關節" : "外側")}螺栓：{(State.GetBool(PouredKey(side)) ? "螺栓盤上" : "腰間螺栓袋裡")} {State.GetFloat(CountKey(side)):0} 支");
        }

        static void ToastIf(string msg) { if (!string.IsNullOrEmpty(msg)) Toast(msg); }

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
            bool stowed = State.GetBool(StowedKey(side)), held = State.Get("hand.item") == Id;
            if (bagDish != null) bagDish.SetActive(stowed);
            if (dishBody != null)
            {
                dishBody.gameObject.SetActive(!stowed);
                if (!held) dishBody.localPosition = dishHome;   // 62A：沒收進工具袋就回到原位
            }
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = !stowed && !held;   // 收走、拿在手上時射線不會打到它
            for (int i = 0; i < boltIcons.Length; i++) if (boltIcons[i] != null) boltIcons[i].SetActive(!poured && i < n);
            for (int i = 0; i < trayIcons.Length; i++) if (trayIcons[i] != null) trayIcons[i].SetActive(poured && i % 4 < n);   // 每組 4 支（方盤、車頂小零件盤各一組）
        }

        /// <summary>拿在手上：盤身跟著射線（XR＝右手控制器；桌機＝滑鼠）前方一小段。</summary>
        void LateUpdate()
        {
            if (!bound || dishBody == null || State.Get("hand.item") != Id || !PointerState.HasRay) return;
            var target = PointerState.Ray.origin + PointerState.Ray.direction * ToolRulesDef.Load().boltDishHoldDistance;
            dishBody.position = Vector3.Lerp(dishBody.position, target, 1f - Mathf.Exp(-18f * Time.deltaTime));
        }
    }
}
