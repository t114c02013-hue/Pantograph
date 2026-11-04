using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Procedures
{
    /// <summary>
    /// v3.10 第 8 項：故障處理時間。CourseDef.FaultOption 的 timeFrom 小動作啟用起、timeTo 小動作完成止（吊帶 s1→s4、絕緣子 r1→r6d），這段時間不算進時間判定，報表另列。
    /// 每段結束寫 fault.time＝「步驟|故障鍵|秒數」，錄影記成 FaultTime，SummaryBuilder 從步驟時間扣掉並列在「故障處理時間」。故障序號展開的小動作（s1i2）算 s1。
    /// </summary>
    public sealed class FaultClock : IDisposable
    {
        public const string Key = "fault.time";
        static readonly Regex IndexSuffix = new Regex(@"i\d+$");

        readonly ProcedureRunner runner; readonly IEventBus bus; readonly ISimState state; readonly CourseDef.FaultOption[] faults;
        readonly Dictionary<string, float> open = new Dictionary<string, float>();
        string last = "";

        public FaultClock(ProcedureRunner runner, IEventBus bus, ISimState state, CourseDef.FaultOption[] faults)
        {
            this.runner = runner; this.bus = bus; this.state = state; this.faults = faults ?? new CourseDef.FaultOption[0];
            bus.Subscribe<StateChanged>(OnState);
        }

        public void Dispose() => bus.Unsubscribe<StateChanged>(OnState);

        void OnState(StateChanged e)
        {
            foreach (var c in e.Changes) if (c.key == "procedure.sub_index" || c.key == "procedure.current") { Check(); return; }
        }

        void Check()
        {
            var st = runner.Current;
            if (st == null) return;
            var subs = ProcedureRunner.SubActions(st);
            int idx = (int)state.GetFloat("procedure.sub_index");
            string now = idx < subs.Length ? IndexSuffix.Replace(subs[idx].subId ?? "", "") : "";
            if (now == last) return;
            foreach (var f in faults)
            {
                if (string.IsNullOrEmpty(f.timeFrom)) continue;
                if (open.TryGetValue(f.key, out float t0))
                {
                    if (last == f.timeTo)
                    {
                        open.Remove(f.key);
                        state.Set(Key, st.RunId + "|" + f.key + "|" + (SimClock.Now - t0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
                else if (now == f.timeFrom && state.GetBool(f.key)) open[f.key] = SimClock.Now;
            }
            last = now;
        }
    }
}
