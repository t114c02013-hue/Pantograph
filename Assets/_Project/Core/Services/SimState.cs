using System;
using System.Collections.Generic;
using System.Globalization;

namespace PantographSim.Core
{
    public interface ISimState
    {
        string Get(string key, string fallback = "");
        float GetFloat(string key, float fallback = 0f);
        bool GetBool(string key);
        bool Has(string key);
        void Set(string key, string value);
        /// <summary>force＝值沒變也記成一筆變更並通知（例：放開升降時強制寫入最新距離）。</summary>
        void Set(string key, string value, bool force);
        void SetFloat(string key, float value);
        void SetBool(string key, bool value);
        Dictionary<string, string> Snapshot();
        void Restore(Dictionary<string, string> snapshot);
        IEnumerable<string> Keys { get; }

        /// <summary>
        /// 開始一批寫入：<c>using (state.BeginBatch()) { state.Set(..); state.Set(..); }</c>。
        /// 批次中 Set 只寫進暫存區，批次內 Get 讀得到剛寫的值，其他模組要等最外層結束（Commit）才看得到；
        /// Commit 一次寫入並只發一次 StateChanged（帶全部變更，沒有變更就不發）。可巢狀，內層併入外層。
        /// 會拋例外的動作請用 <see cref="Batch(Action)"/>：例外時整批放棄。
        /// </summary>
        IDisposable BeginBatch();
        /// <summary>把 body 包成一批；body 拋出例外時整批放棄（含外層批次），記 Error log 後再把例外往外拋。</summary>
        void Batch(Action body);
        /// <summary>目前是否在批次中（尚未 Commit）。</summary>
        bool InBatch { get; }
        /// <summary>在這一批 Commit（並發出 StateChanged）之後執行 action，例如發「已寫入」類事件；不在批次中就立刻執行。整批放棄時不執行。</summary>
        void AfterCommit(Action action);
    }

    /// <summary>
    /// 黑板：所有模擬狀態都以字串鍵值存在這裡，模組之間只透過它與事件溝通。
    /// 一筆 Set（或一整批）生效後才發 StateChanged；Restore 先全部寫入再對有差異的鍵逐一通知。
    /// </summary>
    public class SimState : ISimState
    {
        readonly Dictionary<string, string> values = new Dictionary<string, string>();
        readonly IEventBus bus;

        // 批次暫存：鍵 → 新值（null＝刪除）；順序＝第一次寫入的順序
        readonly Dictionary<string, string> pending = new Dictionary<string, string>();
        readonly List<string> pendingOrder = new List<string>();
        readonly HashSet<string> forced = new HashSet<string>();
        readonly List<Action> afterCommit = new List<Action>();
        int depth;
        bool aborted;

        public SimState(IEventBus bus) { this.bus = bus; }

        /// <summary>批次例外的記錄方式（預設 Unity Console 的 Error）；測試可以換掉，故意丟的例外就不會出現在 Console。</summary>
        public static Action<string> ErrorLog = UnityEngine.Debug.LogError;

        public static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        public bool InBatch => depth > 0;

        public IEnumerable<string> Keys
        {
            get
            {
                if (depth == 0) return values.Keys;
                var keys = new List<string>();
                foreach (var k in values.Keys) if (!pending.TryGetValue(k, out var v) || v != null) keys.Add(k);
                foreach (var k in pendingOrder) if (pending[k] != null && !values.ContainsKey(k)) keys.Add(k);
                return keys;
            }
        }

        bool TryRead(string key, out string value)
        {
            if (depth > 0 && pending.TryGetValue(key, out value)) return value != null;   // read-your-writes
            return values.TryGetValue(key, out value);
        }

        public string Get(string key, string fallback = "") => TryRead(key, out var v) ? v : fallback;
        public bool Has(string key) => TryRead(key, out _);

        public float GetFloat(string key, float fallback = 0f) =>
            TryRead(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;

        public bool GetBool(string key) => TryRead(key, out var v) && v == "true";

        public void Set(string key, string value) => Set(key, value, false);

        public void Set(string key, string value, bool force)
        {
            if (depth > 0)
            {
                if (!pending.ContainsKey(key)) pendingOrder.Add(key);
                pending[key] = value;
                if (force) forced.Add(key);
                return;
            }
            // 不在批次：等於只有一筆的批次
            var change = Apply(key, value, force);
            if (change.HasValue) Publish(new[] { change.Value }, false);
        }

        public void SetFloat(string key, float value) => Set(key, Format(value));
        public void SetBool(string key, bool value) => Set(key, value ? "true" : "false");

        /// <summary>快照＝已生效的狀態（批次中尚未 Commit 的寫入不在內）。</summary>
        public Dictionary<string, string> Snapshot() => new Dictionary<string, string>(values);

        public void Restore(Dictionary<string, string> snapshot)
        {
            if (depth > 0)
            {
                // 批次中：還原也併入這一批（Commit 時一起生效）
                foreach (var key in new List<string>(Keys)) if (!snapshot.ContainsKey(key)) Set(key, null);
                foreach (var kv in snapshot) Set(kv.Key, kv.Value);
                return;
            }
            var changed = new List<StateChange>();
            foreach (var key in new List<string>(values.Keys))
                if (!snapshot.ContainsKey(key)) { changed.Add(new StateChange(key, values[key], null)); values.Remove(key); }
            foreach (var kv in snapshot)
                if (!values.TryGetValue(kv.Key, out var v) || v != kv.Value)
                {
                    changed.Add(new StateChange(kv.Key, values.TryGetValue(kv.Key, out var old) ? old : null, kv.Value));
                    values[kv.Key] = kv.Value;
                }
            // 全部寫入後才逐鍵通知：每個訂閱者讀到的都是完整的還原狀態
            foreach (var c in changed) Publish(new[] { c }, true);
        }

        // ---------- 批次 ----------
        public IDisposable BeginBatch()
        {
            depth++;
            return new Scope(this);
        }

        public void Batch(Action body)
        {
            using (BeginBatch())
            {
                try { body(); }
                catch (Exception ex)
                {
                    aborted = true;
                    ErrorLog("[SimState] 批次寫入中發生例外，整批放棄（不寫入任何鍵）：" + ex);
                    throw;
                }
            }
        }

        public void AfterCommit(Action action)
        {
            if (action == null) return;
            if (depth > 0) afterCommit.Add(action); else action();
        }

        void End()
        {
            if (depth == 0) return;
            if (--depth > 0) return;
            var after = afterCommit.Count > 0 ? afterCommit.ToArray() : null;
            afterCommit.Clear();
            bool discard = aborted;
            aborted = false;
            if (pendingOrder.Count > 0)
            {
                var order = pendingOrder.ToArray();
                var batch = new Dictionary<string, string>(pending);
                var force = new HashSet<string>(forced);
                pending.Clear(); pendingOrder.Clear(); forced.Clear();
                if (discard) return;

                var changes = new List<StateChange>();
                foreach (var key in order)
                {
                    var c = Apply(key, batch[key], force.Contains(key));
                    if (c.HasValue) changes.Add(c.Value);
                }
                if (changes.Count > 0) Publish(changes, false);
            }
            else forced.Clear();
            if (discard || after == null) return;
            foreach (var a in after) a();
        }

        /// <summary>寫入已生效的狀態；回傳變更（沒變且沒有 force 時為 null）。</summary>
        StateChange? Apply(string key, string value, bool force)
        {
            bool had = values.TryGetValue(key, out var old);
            if (had && old == value || !had && value == null) return force && had ? new StateChange(key, old, old, true) : (StateChange?)null;
            if (value == null) values.Remove(key); else values[key] = value;
            return new StateChange(key, had ? old : null, value, force);
        }

        void Publish(IReadOnlyList<StateChange> changes, bool restored)
        {
            var first = changes[0];
            bus?.Publish(new StateChanged
            {
                targetId = first.key, text = first.newValue, value = first.Value, time = SimClock.Now, isReplay = SimSession.IsReplay,
                restored = restored, Changes = changes
            });
        }

        sealed class Scope : IDisposable
        {
            SimState owner;
            public Scope(SimState s) { owner = s; }
            public void Dispose() { owner?.End(); owner = null; }
        }
    }

    public static class SimStateBatchExtensions
    {
        /// <summary>把 body 包成一批並回傳它的結果（例外時整批放棄）。</summary>
        public static T BatchResult<T>(this ISimState s, Func<T> body)
        {
            T result = default;
            s.Batch(() => { result = body(); });
            return result;
        }

        /// <summary>寫入數值；force＝值沒變也記成一筆變更。</summary>
        public static void SetFloat(this ISimState s, string key, float value, bool force) => s.Set(key, SimState.Format(value), force);
    }

    /// <summary>模擬時間來源；執行期由 App 設成 Time.time，測試時可自訂。</summary>
    public static class SimClock
    {
        public static System.Func<float> Source = () => 0f;
        public static float Now => Source();
    }
}
