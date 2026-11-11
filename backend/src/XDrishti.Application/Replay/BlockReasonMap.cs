using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SizeScaling;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Replay;

/// <summary>
/// Maps what the live plan builder writes on a plan item that was not taken to the one reason of the replay's closed list (SR-01). The engine code is kept beside it, so nothing is lost. Pure.
/// </summary>
internal static class BlockReasonMap
{
    /// <summary>The reason of an item that is in the plan's reserve or rejected group; the first reason the engine gave decides.</summary>
    public static ReplayBlocked Of(NewPlanItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var first = item.Reasons.Count == 0 ? string.Empty : item.Reasons[0];
        var code = first.Split(':')[0];
        var reason = code switch
        {
            SmartRuleCodes.ReserveGateNoTrade or SmartRuleCodes.ReserveGateReduced => BlockReason.Gate,
            SmartRuleCodes.SameBetBetterTicket or SmartRuleCodes.SameBetOpenTrade or SmartRuleCodes.RuleRemovedHalf => BlockReason.SmartRule,
            "merged_duplicate" or "conflict_lost" or "conflict_close" or "conflict_dropped" => BlockReason.SmartRule, // the merge of one symbol's tickets
            BudgetFit.ReserveCode => BlockReason.Capital,
            FillCodes.ClusterCap => BlockReason.ClusterCap,
            FillCodes.TotalRisk or FillCodes.PerTradeRisk or FillCodes.NetExposure => BlockReason.TotalRisk,
            FillCodes.OpenPositions or FillCodes.PlanItems or FillCodes.EntriesPerDay or FillCodes.ReserveFull or "not_taken" => BlockReason.MaxPositions,
            FillCodes.Book or FillCodes.OnePerSymbol or FillCodes.HeldLongTerm or FillCodes.Sector or FillCodes.Mix or FillCodes.Basket or FillCodes.Correlation => BlockReason.SmartRule,
            _ => item.Group == TicketGroup.Reserve ? BlockReason.SmartRule : BlockReason.Gate, // a selection filter (eligibility) or an unknown code of a rejected item is a gate: never a pass
        };
        return new ReplayBlocked(item.Symbol, item.Evidence.CellKey, reason, first);
    }
}
