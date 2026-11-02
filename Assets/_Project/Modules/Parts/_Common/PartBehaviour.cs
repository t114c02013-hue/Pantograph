using PantographSim.Core;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// 零件基底：訂閱針對自己 ID 的徒手點擊（ActionRequested）、工具作用（ActionPerformed）與黑板變化，
    /// 狀態一律寫在黑板 &lt;partId&gt;.*，畫面依黑板更新（所以重做還原時外觀會跟著回來）。
    /// </summary>
    public class PartBehaviour : InteractionPointMarker
    {
        protected IEventBus Bus { get; private set; }
        protected ISimState State { get; private set; }
        bool subscribed;

        protected virtual void Start()
        {
            Subscribe();
            if (State != null) RefreshVisual();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Subscribe();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (!subscribed || Bus == null) return;
            Bus.Unsubscribe<ActionRequested>(HandleRequest);
            Bus.Unsubscribe<ActionPerformed>(HandlePerformed);
            Bus.Unsubscribe<StateChanged>(HandleState);
            subscribed = false;
        }

        void Subscribe()
        {
            if (subscribed || !Sim.Ready) return;
            Bus = Sim.Bus; State = Sim.State;
            Bus.Subscribe<ActionRequested>(HandleRequest);
            Bus.Subscribe<ActionPerformed>(HandlePerformed);
            Bus.Subscribe<StateChanged>(HandleState);
            subscribed = true;
            OnBound();
        }

        void HandleRequest(ActionRequested e)
        {
            if (!Owns(e.targetId) || !string.IsNullOrEmpty(e.toolId)) return;   // 只處理徒手點擊；工具由工具自己處理
            State.Batch(() => HandleBareHand(e));   // 一個徒手動作＝一批寫入
        }

        void HandleBareHand(ActionRequested e)
        {
            if (e.text == "inspect" && e.targetId == Id) { State.SetBool(Id + ".inspected", true); return; }
            // 確認降弓那一步：點弓頭上的任何零件（滑板、螺栓）都算「親眼看過弓頭」，不做其他動作
            if (SimIds.IsPanBody(e.targetId) && !State.GetBool("step.STP_SI_01_ConfirmLowered.done"))
            {
                State.SetBool(SimIds.PanHead + ".inspected", true);
                return;
            }
            OnBareHand(e);
        }

        void HandlePerformed(ActionPerformed e)
        {
            if (Owns(e.targetId)) OnToolAction(e);
        }

        /// <summary>這個零件負責處理哪些目標 ID 的動作（預設只有自己）。</summary>
        protected virtual bool Owns(string targetId) => targetId == Id;

        void HandleState(StateChanged e)
        {
            if (e.Any(WatchesKey)) RefreshVisual();
        }

        /// <summary>服務就緒、第一次訂閱完成時呼叫（可寫入初始狀態）。</summary>
        protected virtual void OnBound() { }
        protected virtual void OnBareHand(ActionRequested e) { }
        protected virtual void OnToolAction(ActionPerformed e) { }
        protected virtual bool WatchesKey(string key) => key.StartsWith(Id + ".");
        protected virtual void RefreshVisual() { }

        /// <summary>徒手動作也要先問守門者（例：未接地不能碰滑板）。</summary>
        protected bool AskBare(string verb) => Sim.Ask("", Id, verb);
    }
}
