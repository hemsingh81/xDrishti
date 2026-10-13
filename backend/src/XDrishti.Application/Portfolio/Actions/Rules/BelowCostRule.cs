using XDrishti.Application.Portfolio.Returns;

namespace XDrishti.Application.Portfolio.Actions.Rules;

/// <summary>
/// <b>Below cost</b>: an open, priced holding whose unrealised result is at or below −<c>BelowCostPct</c> % of its cost is <c>medium</c>, at or below −<c>BelowCostHighPct</c> % <c>high</c>. Unrealised % is the one the Portfolio screen shows
/// (<see cref="ReturnsCalculator.ForHolding"/>). A holding without a usable price never fires. Pure.
/// </summary>
internal static class BelowCostRule
{
    public static IReadOnlyList<RuleHit> Evaluate(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var t = context.Thresholds;
        var hits = new List<RuleHit>();
        foreach (var holding in context.Holdings.Where(h => h.IsOpen && h.IsPriced))
        {
            var figures = context.Returns(holding);
            if (figures.PnlPct is not { } pct || pct > -t.BelowCostPct)
            {
                continue;
            }

            var high = pct <= -t.BelowCostHighPct;
            var numbers = new ActionNumbers(PnlPct: pct, Pnl: figures.Pnl, Invested: figures.Invested, Value: figures.Value, ThresholdPct: high ? t.BelowCostHighPct : t.BelowCostPct);
            hits.Add(ItemFactory.ForHolding(
                ActionRuleNames.BelowCost,
                high ? ActionSeverities.High : ActionSeverities.Medium,
                holding,
                "Below cost",
                $"Down {ActionText.Percent(-pct)} from cost ({ActionText.Rupees(figures.Invested)} invested)",
                numbers,
                -pct));
        }

        return hits;
    }
}
