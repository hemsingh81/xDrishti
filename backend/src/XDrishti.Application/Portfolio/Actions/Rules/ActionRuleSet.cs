using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Portfolio.Actions.Rules;

/// <summary>The seven rules together (ADR 0022): every item that fires, most important first, and the holdings the lagging rule could not judge. Pure; the one place that lists the rules.</summary>
internal static class ActionRuleSet
{
    public static (IReadOnlyList<RuleHit> Hits, IReadOnlyList<HoldingData> LaggingUnavailable) Evaluate(
        ActionContext context, IReadOnlyList<DailyClose>? indexCloses, TradingCalendar? calendar)
    {
        ArgumentNullException.ThrowIfNull(context);
        var lagging = LaggingNiftyRule.Evaluate(context, indexCloses, calendar);
        List<RuleHit> hits =
        [
            .. BelowCostRule.Evaluate(context),
            .. LargeGainRule.Evaluate(context),
            .. OverweightRule.Evaluate(context),
            .. SectorConcentrationRule.Evaluate(context),
            .. LongTermSoonRule.Evaluate(context),
            .. lagging.Hits,
            .. NotInBasketRule.Evaluate(context),
        ];
        hits.Sort(Compare);
        return (hits, lagging.Unavailable);
    }

    /// <summary>
    /// High before medium before info; within a severity by the rule's place in <see cref="ActionRuleNames.ByPriority"/> (sizes of different rules have different units and are never compared); within a rule the larger trigger first; ties by symbol
    /// (account items by name) and id, so the order is the same on every read.
    /// </summary>
    internal static int Compare(RuleHit a, RuleHit b)
    {
        var bySeverity = ActionSeverities.Rank(a.Item.Severity).CompareTo(ActionSeverities.Rank(b.Item.Severity));
        if (bySeverity != 0)
        {
            return bySeverity;
        }

        var byRule = ActionRuleNames.Rank(a.Item.Rule).CompareTo(ActionRuleNames.Rank(b.Item.Rule));
        if (byRule != 0)
        {
            return byRule;
        }

        var bySize = b.Size.CompareTo(a.Size);
        if (bySize != 0)
        {
            return bySize;
        }

        var bySymbol = string.Compare(a.Item.Symbol ?? a.Item.AccountName, b.Item.Symbol ?? b.Item.AccountName, StringComparison.OrdinalIgnoreCase);
        return bySymbol != 0 ? bySymbol : string.CompareOrdinal(a.Item.Id, b.Item.Id);
    }
}
