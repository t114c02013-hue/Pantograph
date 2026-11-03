using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;

namespace PantographSim.Safety
{
    /// <summary>
    /// 全域安全規則（所有模式都生效）。工具或零件改變狀態前會問這裡；違反就擋下，
    /// 發 ActionBlocked 與 Critical 的 ErrorDetected（含做錯什麼／為什麼／怎麼改）。Safety 不評分。
    /// 也是 IStepGate：未降弓或未接地時，量測與更換步驟不會判定完成。
    /// </summary>
    public class SafetyGuard : IActionGuard, IStepGate
    {
        public const string ConfirmLoweredStep = "STP_SI_01_ConfirmLowered";

        readonly IEventBus bus;
        readonly ISimState state;
        readonly Dictionary<SafetyRuleKind, SafetyRuleDefinition> rules;

        public SafetyGuard(IEventBus bus, ISimState state, IEnumerable<SafetyRuleDefinition> ruleDefs)
        {
            this.bus = bus; this.state = state;
            rules = ruleDefs.Where(r => r != null).GroupBy(r => r.kind).ToDictionary(g => g.Key, g => g.First());
        }

        /// <summary>R1.5b b4：載入課程時（日常／定期）不判已退役的規則（由 ErrorRules.json 的新規則取代，例 ERR_WC03_RemoveOrder），R1 的 25 步照舊。</summary>
        public bool SkipDeprecated;
        bool Active(SafetyRuleKind kind) => !(SkipDeprecated && rules.TryGetValue(kind, out var r) && r.deprecated);

        public const string VoltageTestStep = "STP_SI_04_VoltageTest";
        public const string GroundingStep = "STP_SI_05_Grounding";

        /// <summary>弓頭的檢測點在降弓位置：PositionCheck 寫入的距離 ≤ SR_Global_NotLowered 資產的容許值（不再用「步驟完成」當條件）。</summary>
        bool Lowered => state.Has(SimIds.PanHeadLowerDistance) && state.GetFloat(SimIds.PanHeadLowerDistance) <= LowerToleranceCm + 1e-4f;

        public float LowerToleranceCm => rules.TryGetValue(SafetyRuleKind.NotLowered, out var r) ? r.toleranceCm : 1f;

        /// <summary>已驗電並接地：接地旗標，且驗電（SI_04）與接地（SI_05）兩步都完成。</summary>
        bool Grounded => state.GetBool(SimIds.Grounded) && state.GetBool("step." + VoltageTestStep + ".done") && state.GetBool("step." + GroundingStep + ".done");

        /// <summary>只是看／點選，不會改變零件狀態的動作。</summary>
        public static bool IsLookOnly(string verb) => verb == "inspect" || verb == "select" || verb == "click";

        public GuardResult Check(ActionRequest req)
        {
            // 查看（Inspect）一律放行（SI_01 本身就是要點弓頭確認，不能被自己擋住）
            if (req.kind == ActionKind.Inspect) return GuardResult.Allow;

            if (SimIds.IsPanBody(req.targetId))
            {
                // 會改變零件狀態的動作（工具作用、拆裝、鬆鎖、量測、清潔）
                if (!Lowered) return Block(SafetyRuleKind.NotLowered, req);
                if (!Grounded) return Block(SafetyRuleKind.NotGrounded, req);
            }

            if (req.toolId == SimIds.ToolGroundingRod)
            {
                bool head = state.GetBool(SimIds.HeadClamped), terminal = state.GetBool(SimIds.TerminalClamped);
                if (req.targetId == SimIds.GroundTerminal)
                {
                    if (!terminal && !state.GetBool(SimIds.VoltageTested)) return Block(SafetyRuleKind.NoVoltageTest, req);
                    if (terminal && head && Active(SafetyRuleKind.RemoveOrder)) return Block(SafetyRuleKind.RemoveOrder, req);
                }
                if (req.targetId == SimIds.GroundClamp && !head && !terminal) return Block(SafetyRuleKind.GroundOrder, req);
            }
            return GuardResult.Allow;
        }

        GuardResult Block(SafetyRuleKind kind, ActionRequest req)
        {
            rules.TryGetValue(kind, out var r);
            string errorId = r != null ? r.errorId : "ERR_Safety_" + kind;
            bus.Publish(new ActionBlocked { targetId = req.targetId, pointId = req.pointId, toolId = req.toolId, text = errorId, time = SimClock.Now });
            bus.Publish(new ErrorDetected
            {
                errorId = errorId, severity = Severity.Critical, blocks = true,
                what = r?.what ?? "違反安全規定。", why = r?.why ?? "", fix = r?.fix ?? "",
                stepId = state.Get("procedure.current"), targetId = req.targetId, toolId = req.toolId, time = SimClock.Now
            });
            return GuardResult.Block(errorId);
        }

        public bool CanComplete(string stepId, out string reason)
        {
            reason = "";
            if (!stepId.StartsWith("STP_CSR_")) return true;
            if (!Lowered) { reason = "弓頭不在降弓位置（用右側升降控制器降到綠框）"; return false; }
            if (!Grounded) { reason = "尚未驗電並接地"; return false; }
            return true;
        }
    }
}
