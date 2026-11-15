using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// terry 2026-11-15：實體按鈕（Btn_&lt;名&gt;，樞軸在底面）跟著黑板陷下去，只讀不寫。
    /// hold：watchKey 等於 watchValue 的期間一直陷著（▲▼ 按住）；否則 watchKey 每變一次就陷一下再彈回（自檢）。
    /// </summary>
    public class PressFollow : MonoBehaviour
    {
        [SerializeField] string watchKey, watchValue;
        [SerializeField] bool hold;
        [SerializeField] float depth = 0.005f, seconds = 0.15f;
        Vector3 rest;
        bool hasRest, held, bound;
        float timer;

        public bool Pressed => hold ? held : timer > 0f;

        public void Setup(string key, string value, bool holdWhileEqual, float depthM, float pressSeconds)
        {
            watchKey = key; watchValue = value; hold = holdWhileEqual; depth = depthM; seconds = pressSeconds;
        }

        void Start()
        {
            Rest();
            if (!Sim.Ready) return;
            Sim.Bus.Subscribe<StateChanged>(OnState);
            bound = true;
            held = hold && Sim.State.Get(watchKey) == watchValue;
        }

        void OnDestroy() { if (bound && Sim.Bus != null) Sim.Bus.Unsubscribe<StateChanged>(OnState); }

        void Rest() { if (!hasRest) { rest = transform.localPosition; hasRest = true; } }

        void OnState(StateChanged e)
        {
            if (!e.Has(watchKey)) return;
            if (hold) held = Sim.State.Get(watchKey) == watchValue;
            else timer = seconds;
        }

        void Update() => Tick(Time.deltaTime);

        public void Tick(float dt)
        {
            Rest();
            transform.localPosition = rest + transform.localRotation * Vector3.down * (Pressed ? depth : 0f);
            if (timer > 0f) timer -= dt;
        }
    }
}
