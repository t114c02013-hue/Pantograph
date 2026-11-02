using System;
using System.Collections.Generic;
using System.Reflection;

namespace PantographSim.Core
{
    public interface IEventBus
    {
        void Publish<T>(T evt) where T : struct;
        void Subscribe<T>(Action<T> handler) where T : struct;
        void Unsubscribe<T>(Action<T> handler) where T : struct;
    }

    /// <summary>同步事件匯流排。發佈時複製訂閱清單，處理中可安全訂閱/退訂/再發佈。重播模式下發出的事件自動標上 isReplay=true。</summary>
    public class EventBus : IEventBus
    {
        readonly Dictionary<Type, List<Delegate>> handlers = new Dictionary<Type, List<Delegate>>();

        public void Subscribe<T>(Action<T> handler) where T : struct
        {
            if (!handlers.TryGetValue(typeof(T), out var list)) handlers[typeof(T)] = list = new List<Delegate>();
            if (!list.Contains(handler)) list.Add(handler);
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (handlers.TryGetValue(typeof(T), out var list)) list.Remove(handler);
        }

        public void Publish<T>(T evt) where T : struct
        {
            if (!handlers.TryGetValue(typeof(T), out var list) || list.Count == 0) return;
            if (SimSession.IsReplay) evt = ReplayMark<T>.Apply(evt);
            foreach (var h in list.ToArray()) ((Action<T>)h)(evt);
        }

        /// <summary>把事件的 isReplay 欄位設成 true（只在重播模式用到，平常不裝箱）。</summary>
        static class ReplayMark<T> where T : struct
        {
            static readonly FieldInfo field = typeof(T).GetField("isReplay", BindingFlags.Public | BindingFlags.Instance);

            public static T Apply(T evt)
            {
                if (field == null || field.FieldType != typeof(bool)) return evt;
                object boxed = evt;
                field.SetValue(boxed, true);
                return (T)boxed;
            }
        }
    }
}
