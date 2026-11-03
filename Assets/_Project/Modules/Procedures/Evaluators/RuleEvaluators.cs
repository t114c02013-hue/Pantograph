using System;
using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Procedures
{
    /// <summary>判定時的上下文：黑板、目前側別、前面步驟是否全部完成。</summary>
    public class EvalContext
    {
        public ISimState state;
        public string side;
        public Func<bool> allPreviousDone = () => true;

        public string K(string pattern, int n = 0) => StateKeys.Expand(pattern, side, n);
    }

    public interface ICompletionEvaluator
    {
        Type RuleType { get; }
        bool Evaluate(CompletionRule rule, EvalContext ctx);
    }

    public abstract class Evaluator<T> : ICompletionEvaluator where T : CompletionRule
    {
        public Type RuleType => typeof(T);
        public bool Evaluate(CompletionRule rule, EvalContext ctx) => Eval((T)rule, ctx);
        protected abstract bool Eval(T rule, EvalContext ctx);
    }

    public class StateEqualsEvaluator : Evaluator<StateEquals>
    {
        protected override bool Eval(StateEquals r, EvalContext c) => c.state.Get(c.K(r.key)) == r.value;
    }

    public class ChecklistAllEvaluator : Evaluator<ChecklistAll>
    {
        protected override bool Eval(ChecklistAll r, EvalContext c) => r.keys.All(k => c.state.GetBool(c.K(k)));
    }

    public class OrderedSequenceEvaluator : Evaluator<OrderedSequence>
    {
        protected override bool Eval(OrderedSequence r, EvalContext c) => c.state.Get(c.K(r.key)) == r.expected;
    }

    public class ValueInRangeEvaluator : Evaluator<ValueInRange>
    {
        protected override bool Eval(ValueInRange r, EvalContext c)
        {
            foreach (var key in Expand(r.key, r.count, c))
            {
                if (!c.state.Has(key)) return false;
                if (!InRange(c.state.GetFloat(key), r.min, r.max)) return false;
            }
            return true;
        }

        /// <summary>
        /// 比較容差 1e-4：黑板存的是浮點數，恰好在門檻上的值（例 1.0 cm）可能算成 1.0000001 而被誤判超出。
        /// 門檻值本身不變；和 ChecklistEval、SafetyGuard 的 ≤ 比較用同一個容差，同一個值只會有一個結論。
        /// </summary>
        public const float Epsilon = 1e-4f;

        public static bool InRange(float v, float min, float max) => v >= min - Epsilon && v <= max + Epsilon;

        public static IEnumerable<string> Expand(string pattern, int count, EvalContext c)
        {
            if (count <= 0 && pattern.Contains("bolt_{n}")) count = 4;
            if (count <= 0) { yield return c.K(pattern); yield break; }
            for (int n = 1; n <= count; n++) yield return c.K(pattern, n);
        }
    }

    public class DecisionEvaluator : Evaluator<Decision>
    {
        protected override bool Eval(Decision r, EvalContext c)
        {
            var choice = c.state.Get(c.K(r.choiceKey));
            return !string.IsNullOrEmpty(choice) && choice == Answer(r, c.state);
        }

        /// <summary>任一條三點最小值 ≤ 基準 → 兩條都換；否則合格。</summary>
        public static string Answer(Decision r, ISimState state)
        {
            foreach (var side in r.sides)
            {
                float min = float.MaxValue;
                foreach (var p in r.points)
                {
                    var key = StateKeys.Expand(r.trueKeyPattern, side, 0, p);
                    if (state.Has(key)) min = Math.Min(min, state.GetFloat(key));
                }
                if (min <= r.threshold) return r.replaceChoice;
            }
            return r.passChoice;
        }
    }

    public class ReadingMatchesEvaluator : Evaluator<ReadingMatches>
    {
        protected override bool Eval(ReadingMatches r, EvalContext c)
        {
            if (r.recordKeys.Length != r.readingKeys.Length) return false;
            for (int i = 0; i < r.recordKeys.Length; i++)
            {
                var rec = c.K(r.recordKeys[i]); var read = c.K(r.readingKeys[i]);
                if (!c.state.Has(rec) || !c.state.Has(read)) return false;
                if (Math.Abs(c.state.GetFloat(rec) - c.state.GetFloat(read)) > r.tolerance + 1e-4f) return false;
            }
            return true;
        }
    }

    public class ProcedureDoneEvaluator : Evaluator<ProcedureDone>
    {
        protected override bool Eval(ProcedureDone r, EvalContext c) => c.allPreviousDone();
    }

    public class ConditionsAllEvaluator : Evaluator<ConditionsAll>
    {
        protected override bool Eval(ConditionsAll r, EvalContext c) => r.conditions.All(x => ChecklistEval.Done(c.state, c.K(x)));
    }

    public class AllOfEvaluator : Evaluator<AllOf>
    {
        protected override bool Eval(AllOf r, EvalContext c) => r.rules.All(x => RuleEvaluators.Evaluate(x, c));
    }

    /// <summary>依完成條件型別分派到對應的 Evaluator。</summary>
    public static class RuleEvaluators
    {
        static readonly Dictionary<Type, ICompletionEvaluator> map = new ICompletionEvaluator[]
        {
            new StateEqualsEvaluator(), new ChecklistAllEvaluator(), new OrderedSequenceEvaluator(), new ValueInRangeEvaluator(),
            new DecisionEvaluator(), new ReadingMatchesEvaluator(), new ProcedureDoneEvaluator(), new AllOfEvaluator(), new ConditionsAllEvaluator()
        }.ToDictionary(e => e.RuleType);

        public static bool Evaluate(CompletionRule rule, EvalContext ctx)
        {
            if (rule == null) return false;
            if (!map.TryGetValue(rule.GetType(), out var ev)) throw new InvalidOperationException("沒有對應的判定器：" + rule.GetType().Name);
            return ev.Evaluate(rule, ctx);
        }

        /// <summary>找出完成條件中的 Decision（給 ChoiceEquals 錯誤用）。</summary>
        public static Decision FindDecision(CompletionRule rule)
        {
            if (rule is Decision d) return d;
            if (rule is AllOf all) return all.rules.Select(FindDecision).FirstOrDefault(x => x != null);
            return null;
        }
    }
}
